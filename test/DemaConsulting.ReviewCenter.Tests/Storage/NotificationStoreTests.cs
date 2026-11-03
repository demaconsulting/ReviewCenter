// Copyright (c) DEMA Consulting
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for <see cref="DemaConsulting.ReviewCenter.Storage.NotificationStore"/> and the notification tables
///     of the database context, against a real SQLite database file where the conditional updates must have
///     exactly one winner.
/// </summary>
public class NotificationStoreTests
{
    /// <summary>
    ///     The time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Gets the cancellation token of the running test.
    /// </summary>
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Test that a new database has the notification tables.
    /// </summary>
    [Fact]
    public async Task NotificationStore_IsAvailableAsync_NewDatabase_ReturnsTrue()
    {
        // Arrange: a new database
        using var db = new TestStore();

        // Act: ask
        var available = await db.Notifications.IsAvailableAsync(Token);

        // Assert: available
        Assert.True(available);
    }

    /// <summary>
    ///     Test that a database without the notification tables is reported as not available.
    /// </summary>
    [Fact]
    public async Task NotificationStore_IsAvailableAsync_TablesMissing_ReturnsFalse()
    {
        // Arrange: a database from before notifications existed
        using var db = new TestStore();
        await using (var context = await db.Factory.CreateDbContextAsync(Token))
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE NotificationSendLog", Token);
            await context.Database.ExecuteSqlRawAsync("DROP TABLE NotificationPreferences", Token);
        }

        // Act: ask
        var available = await db.Notifications.IsAvailableAsync(Token);

        // Assert: not available
        Assert.False(available);
    }

    /// <summary>
    ///     Test that a database failure that is not a missing table is not reported as "not available".
    /// </summary>
    [Fact]
    public async Task NotificationStore_IsAvailableAsync_OtherDatabaseFailure_Propagates()
    {
        // Arrange: a file that is not a database, so every statement fails with an error other than a missing table
        var connectionString = TestDatabase.NewConnectionString();
        var path = new SqliteConnectionStringBuilder(connectionString).DataSource;
        await File.WriteAllTextAsync(path, new string('x', 4096), Token);
        using var provider = new ServiceCollection()
            .AddDbContextFactory<ReviewCenterDbContext>(builder => builder.UseSqlite(connectionString))
            .BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
        var store = new NotificationStore(factory, new AuditAppender(TimeProvider.System));

        // Act and Assert: the failure surfaces instead of the answer false
        await Assert.ThrowsAsync<SqliteException>(() => store.IsAvailableAsync(Token));
    }

    /// <summary>
    ///     Test that a user without an address has no row.
    /// </summary>
    [Fact]
    public async Task NotificationStore_GetAsync_NoRow_ReturnsNull()
    {
        // Arrange: a user without an address
        using var db = new TestStore();
        var userId = await AddUserAsync(db);

        // Act and Assert: no row
        Assert.Null(await db.Notifications.GetAsync(userId, Token));
    }

    /// <summary>
    ///     Test that a reservation stores the pending state, counts the mail and saves the audit entry together.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveConfirmationAsync_Reserved_StoresPendingLogAndAudit()
    {
        // Arrange: a user
        using var db = new TestStore();
        var userId = await AddUserAsync(db);

        // Act: reserve
        var (outcome, logId) = await db.Notifications.ReserveConfirmationAsync(
            Request(userId, "ann@example.com", "a"), Draft("notification.settings-changed"), Token);

        // Assert: reserved with a log identity, the pending state, one counted mail and one audit entry
        Assert.Equal(ConfirmationReservation.Reserved, outcome);
        Assert.True(logId > 0);
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.Equal("ann@example.com", row!.PendingEmail);
        Assert.Equal(Hash('a'), row.PendingCodeHash);
        Assert.Equal("salt", row.PendingCodeSalt);
        Assert.Equal(_now.AddMinutes(30), row.PendingCodeExpiresUtc);
        Assert.Equal(_now, row.PendingSentUtc);
        Assert.Equal(0, row.PendingAttempts);
        Assert.Null(row.Email);
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        var log = await context.NotificationSendLog.SingleAsync(Token);
        Assert.Equal(logId, log.Id);
        Assert.Equal("ann@example.com", log.ToAddress);
        Assert.Equal(_now, log.SentUtc);
        Assert.Equal(1, await context.AuditEntries.CountAsync(a => a.Action == "notification.settings-changed", Token));
    }

    /// <summary>
    ///     Test that a confirmation request deletes the log entries that are older than one day and keeps the others.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveConfirmationAsync_OldLogEntries_AreDeleted()
    {
        // Arrange: one log entry that is 25 hours old and one that is 1 hour old
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await using (var seed = await db.Factory.CreateDbContextAsync(Token))
        {
            seed.NotificationSendLog.Add(new NotificationSendLogEntry
            {
                UserId = userId,
                ToAddress = "old@example.com",
                Kind = NotificationSendLogEntry.KindConfirmation,
                SentUtc = _now.AddHours(-25)
            });
            seed.NotificationSendLog.Add(new NotificationSendLogEntry
            {
                UserId = userId,
                ToAddress = "recent@example.com",
                Kind = NotificationSendLogEntry.KindConfirmation,
                SentUtc = _now.AddHours(-1)
            });
            await seed.SaveChangesAsync(Token);
        }

        // Act: reserve a confirmation
        var outcome = await ReserveAsync(db, userId, "ann@example.com", 'a', _now);

        // Assert: reserved, the old entry is gone, the recent one and the new one stay
        Assert.Equal(ConfirmationReservation.Reserved, outcome);
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        var addresses = await context.NotificationSendLog.Select(e => e.ToAddress).ToListAsync(Token);
        Assert.Equal(2, addresses.Count);
        Assert.DoesNotContain("old@example.com", addresses);
        Assert.Contains("recent@example.com", addresses);
        Assert.Contains("ann@example.com", addresses);
    }

    /// <summary>
    ///     Test that a new reservation keeps the confirmed address and starts the attempts again.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveConfirmationAsync_AfterConfirmed_KeepsConfirmedAndResetsAttempts()
    {
        // Arrange: a confirmed address and a pending one with attempts used
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await ReserveAsync(db, userId, "old@example.com", 'a', _now);
        Assert.True(await db.Notifications.ConfirmAsync(userId, Hash('a'), _now, Draft("notification.email-confirmed"), Token));
        await ReserveAsync(db, userId, "new@example.com", 'b', _now.AddMinutes(5));
        Assert.True(await db.Notifications.TryCountAttemptAsync(userId, Hash('b'), 5, Token));

        // Act: reserve again after the cooldown
        var outcome = await ReserveAsync(db, userId, "newer@example.com", 'c', _now.AddMinutes(10));

        // Assert: reserved; the old confirmed address stays; the pending state is the new one with no attempts
        Assert.Equal(ConfirmationReservation.Reserved, outcome);
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.Equal("old@example.com", row!.Email);
        Assert.Equal("newer@example.com", row.PendingEmail);
        Assert.Equal(Hash('c'), row.PendingCodeHash);
        Assert.Equal(0, row.PendingAttempts);
    }

    /// <summary>
    ///     Test that a second request within the cooldown is refused and stores nothing.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveConfirmationAsync_WithinCooldown_ReturnsTooSoon()
    {
        // Arrange: a mail was just sent
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await ReserveAsync(db, userId, "ann@example.com", 'a', _now);

        // Act: ask again 30 seconds later
        var outcome = await ReserveAsync(db, userId, "ann@example.com", 'b', _now.AddSeconds(30));

        // Assert: too soon; the first state and one counted mail remain
        Assert.Equal(ConfirmationReservation.TooSoon, outcome);
        Assert.Equal(Hash('a'), (await db.Notifications.GetAsync(userId, Token))!.PendingCodeHash);
        Assert.Equal(1, await CountLogAsync(db));
        Assert.Equal(1, await CountAuditAsync(db));
    }

    /// <summary>
    ///     Test that the per-user daily limit is applied.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveConfirmationAsync_UserLimit_ReturnsUserLimitReached()
    {
        // Arrange: three mails to the user, spaced beyond the cooldown
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        for (var index = 0; index < 3; index++)
        {
            await ReserveAsync(db, userId, $"ann{index}@example.com", 'a', _now.AddMinutes(index * 2));
        }

        // Act: a fourth within the day, and one after the day
        var refused = await ReserveAsync(db, userId, "ann9@example.com", 'b', _now.AddMinutes(10));
        var later = await ReserveAsync(db, userId, "ann9@example.com", 'c', _now.AddHours(25));

        // Assert: refused, then allowed again
        Assert.Equal(ConfirmationReservation.UserLimitReached, refused);
        Assert.Equal(ConfirmationReservation.Reserved, later);
    }

    /// <summary>
    ///     Test that the per-address daily limit counts the mails of all users.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveConfirmationAsync_AddressLimit_ReturnsAddressLimitReached()
    {
        // Arrange: three users already received a mail for the same address
        using var db = new TestStore();
        for (var index = 0; index < 3; index++)
        {
            var other = await AddUserAsync(db, $"user{index}");
            await ReserveAsync(db, other, "shared@example.com", 'a', _now.AddMinutes(index));
        }

        var fourth = await AddUserAsync(db, "user9");

        // Act: a fourth user asks for the same address
        var outcome = await ReserveAsync(db, fourth, "shared@example.com", 'b', _now.AddMinutes(10));

        // Assert: refused, and nothing stored for the fourth user
        Assert.Equal(ConfirmationReservation.AddressLimitReached, outcome);
        Assert.Null(await db.Notifications.GetAsync(fourth, Token));
    }

    /// <summary>
    ///     Test that the server-wide hourly limit is applied.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveConfirmationAsync_ServerLimit_ReturnsGlobalLimitReached()
    {
        // Arrange: the server limit is two mails in an hour and two users used it
        using var db = new TestStore();
        var first = await AddUserAsync(db, "user1");
        var second = await AddUserAsync(db, "user2");
        var third = await AddUserAsync(db, "user3");
        await ReserveAsync(db, first, "a@example.com", 'a', _now, maxPerHour: 2);
        await ReserveAsync(db, second, "b@example.com", 'a', _now.AddMinutes(1), maxPerHour: 2);

        // Act: a third user asks
        var outcome = await ReserveAsync(db, third, "c@example.com", 'a', _now.AddMinutes(2), maxPerHour: 2);

        // Assert: refused
        Assert.Equal(ConfirmationReservation.GlobalLimitReached, outcome);
    }

    /// <summary>
    ///     Test that many concurrent reservations for one user never exceed the per-user cooldown rule.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveConfirmationAsync_Concurrent_HasExactlyOneWinner()
    {
        // Arrange: a user and eight requests at the same moment
        using var db = new TestStore();
        var userId = await AddUserAsync(db);

        // Act: race
        var outcomes = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(index => ReserveAsync(db, userId, "ann@example.com", (char)('a' + index), _now)));

        // Assert: one reservation, the others too soon; one counted mail
        Assert.Equal(1, outcomes.Count(o => o == ConfirmationReservation.Reserved));
        Assert.Equal(7, outcomes.Count(o => o == ConfirmationReservation.TooSoon));
        Assert.Equal(1, await CountLogAsync(db));
    }

    /// <summary>
    ///     Test that aborting takes the mail back out of the count and clears the pending state.
    /// </summary>
    [Fact]
    public async Task NotificationStore_AbortConfirmationAsync_RemovesLogAndPending()
    {
        // Arrange: a reservation
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        var (_, logId) = await db.Notifications.ReserveConfirmationAsync(
            Request(userId, "ann@example.com", "a"), Draft("notification.settings-changed"), Token);

        // Act: abort
        await db.Notifications.AbortConfirmationAsync(userId, logId, Hash('a'), Token);

        // Assert: no counted mail and nothing pending
        Assert.Equal(0, await CountLogAsync(db));
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.Null(row!.PendingEmail);
        Assert.Null(row.PendingCodeHash);
        Assert.Null(row.PendingSentUtc);
    }

    /// <summary>
    ///     Test that aborting leaves a newer pending state alone.
    /// </summary>
    [Fact]
    public async Task NotificationStore_AbortConfirmationAsync_OtherHash_LeavesPendingAlone()
    {
        // Arrange: a reservation
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        var (_, logId) = await db.Notifications.ReserveConfirmationAsync(
            Request(userId, "ann@example.com", "a"), Draft("notification.settings-changed"), Token);

        // Act: abort with another hash
        await db.Notifications.AbortConfirmationAsync(userId, logId, Hash('z'), Token);

        // Assert: the pending state remains
        Assert.Equal(Hash('a'), (await db.Notifications.GetAsync(userId, Token))!.PendingCodeHash);
    }

    /// <summary>
    ///     Test that an attempt is counted only for the current code and only until the limit.
    /// </summary>
    [Fact]
    public async Task NotificationStore_TryCountAttemptAsync_CountsUpToTheLimitForTheCurrentCode()
    {
        // Arrange: a pending code
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await ReserveAsync(db, userId, "ann@example.com", 'a', _now);

        // Act: another code, then three attempts with a limit of two
        var other = await db.Notifications.TryCountAttemptAsync(userId, Hash('z'), 2, Token);
        var first = await db.Notifications.TryCountAttemptAsync(userId, Hash('a'), 2, Token);
        var second = await db.Notifications.TryCountAttemptAsync(userId, Hash('a'), 2, Token);
        var third = await db.Notifications.TryCountAttemptAsync(userId, Hash('a'), 2, Token);

        // Assert: only the first two attempts count
        Assert.False(other);
        Assert.True(first);
        Assert.True(second);
        Assert.False(third);
        Assert.Equal(2, (await db.Notifications.GetAsync(userId, Token))!.PendingAttempts);
    }

    /// <summary>
    ///     Test that concurrent attempts never exceed the limit.
    /// </summary>
    [Fact]
    public async Task NotificationStore_TryCountAttemptAsync_Concurrent_NeverExceedsTheLimit()
    {
        // Arrange: a pending code
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await ReserveAsync(db, userId, "ann@example.com", 'a', _now);

        // Act: ten attempts at once with a limit of five
        var results = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => db.Notifications.TryCountAttemptAsync(userId, Hash('a'), 5, Token)));

        // Assert: exactly five were counted
        Assert.Equal(5, results.Count(r => r));
        Assert.Equal(5, (await db.Notifications.GetAsync(userId, Token))!.PendingAttempts);
    }

    /// <summary>
    ///     Test that confirming moves the pending address to the confirmed one and saves the audit entry.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ConfirmAsync_MovesPendingToConfirmedWithAudit()
    {
        // Arrange: a pending address
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await ReserveAsync(db, userId, "ann@example.com", 'a', _now);

        // Act: confirm
        var confirmed = await db.Notifications.ConfirmAsync(
            userId, Hash('a'), _now.AddMinutes(1), Draft("notification.email-confirmed"), Token);

        // Assert: confirmed, nothing pending, one audit entry
        Assert.True(confirmed);
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.Equal("ann@example.com", row!.Email);
        Assert.Null(row.PendingEmail);
        Assert.Null(row.PendingCodeHash);
        Assert.Null(row.PendingCodeExpiresUtc);
        Assert.Equal(0, row.PendingAttempts);
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        Assert.Equal(1, await context.AuditEntries.CountAsync(a => a.Action == "notification.email-confirmed", Token));
    }

    /// <summary>
    ///     Test that confirming with another code changes nothing and writes no audit entry.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ConfirmAsync_OtherCode_ChangesNothing()
    {
        // Arrange: a pending address
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await ReserveAsync(db, userId, "ann@example.com", 'a', _now);

        // Act: confirm with another hash
        var confirmed = await db.Notifications.ConfirmAsync(
            userId, Hash('z'), _now, Draft("notification.email-confirmed"), Token);

        // Assert: refused, still pending, no confirmed audit entry
        Assert.False(confirmed);
        Assert.Null((await db.Notifications.GetAsync(userId, Token))!.Email);
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        Assert.Equal(0, await context.AuditEntries.CountAsync(a => a.Action == "notification.email-confirmed", Token));
    }

    /// <summary>
    ///     Test that a code that has expired at the time of the update is not confirmed, even though it was valid when it
    ///     was read, and that nothing changes and no audit entry is written.
    /// </summary>
    /// <param name="minutesAfterReserve">The minutes between the reservation and the confirmation.</param>
    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public async Task NotificationStore_ConfirmAsync_ExpiredCode_ChangesNothing(int minutesAfterReserve)
    {
        // Arrange: a pending address whose code lives for 30 minutes
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await ReserveAsync(db, userId, "ann@example.com", 'a', _now);

        // Act: confirm at the moment the code expires, and after it
        var confirmed = await db.Notifications.ConfirmAsync(
            userId, Hash('a'), _now.AddMinutes(minutesAfterReserve), Draft("notification.email-confirmed"), Token);

        // Assert: refused, still pending with the same code, no confirmed address, no confirmed audit entry
        Assert.False(confirmed);
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.Null(row!.Email);
        Assert.Equal("ann@example.com", row.PendingEmail);
        Assert.Equal(Hash('a'), row.PendingCodeHash);
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        Assert.Equal(0, await context.AuditEntries.CountAsync(a => a.Action == "notification.email-confirmed", Token));
    }

    /// <summary>
    ///     Test that a code that is still valid one tick before it expires is confirmed.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ConfirmAsync_LastInstantBeforeExpiry_Confirms()
    {
        // Arrange: a pending address whose code lives for 30 minutes
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await ReserveAsync(db, userId, "ann@example.com", 'a', _now);

        // Act: confirm one tick before the code expires
        var confirmed = await db.Notifications.ConfirmAsync(
            userId, Hash('a'), _now.AddMinutes(30).AddTicks(-1), Draft("notification.email-confirmed"), Token);

        // Assert: confirmed
        Assert.True(confirmed);
        Assert.Equal("ann@example.com", (await db.Notifications.GetAsync(userId, Token))!.Email);
    }

    /// <summary>
    ///     Test that concurrent confirmations have exactly one winner and one audit entry.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ConfirmAsync_Concurrent_HasExactlyOneWinner()
    {
        // Arrange: a pending address
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await ReserveAsync(db, userId, "ann@example.com", 'a', _now);

        // Act: eight confirmations at once
        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => db.Notifications.ConfirmAsync(
                userId, Hash('a'), _now, Draft("notification.email-confirmed"), Token)));

        // Assert: one winner and one audit entry
        Assert.Equal(1, results.Count(r => r));
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        Assert.Equal(1, await context.AuditEntries.CountAsync(a => a.Action == "notification.email-confirmed", Token));
    }

    /// <summary>
    ///     Test that removing deletes the row and writes the audit entry, and that a second removal does neither.
    /// </summary>
    [Fact]
    public async Task NotificationStore_RemoveAsync_DeletesRowWithAuditOnce()
    {
        // Arrange: a confirmed address
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await ReserveAsync(db, userId, "ann@example.com", 'a', _now);
        await db.Notifications.ConfirmAsync(userId, Hash('a'), _now, Draft("notification.email-confirmed"), Token);

        // Act: remove twice
        var first = await db.Notifications.RemoveAsync(userId, Draft("notification.email-removed"), Token);
        var second = await db.Notifications.RemoveAsync(userId, Draft("notification.email-removed"), Token);

        // Assert: removed once, one audit entry, no row
        Assert.True(first);
        Assert.False(second);
        Assert.Null(await db.Notifications.GetAsync(userId, Token));
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        Assert.Equal(1, await context.AuditEntries.CountAsync(a => a.Action == "notification.email-removed", Token));
    }

    /// <summary>
    ///     Test that removing the address keeps the counted mails, so a user can not reset the limits by removing it.
    /// </summary>
    [Fact]
    public async Task NotificationStore_RemoveAsync_KeepsTheSendLog()
    {
        // Arrange: a reservation and a removal
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await ReserveAsync(db, userId, "ann@example.com", 'a', _now);

        // Act: remove
        await db.Notifications.RemoveAsync(userId, Draft("notification.email-removed"), Token);

        // Assert: the mail is still counted
        Assert.Equal(1, await CountLogAsync(db));
    }

    /// <summary>
    ///     Builds a reservation request with the limits of the service.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="address">The address.</param>
    /// <param name="hashStart">The first character of the hash.</param>
    /// <param name="now">The time of the request; the test time when omitted.</param>
    /// <param name="maxPerHour">The server limit per hour.</param>
    /// <returns>The request.</returns>
    private static ConfirmationRequest Request(
        int userId,
        string address,
        string hashStart,
        DateTimeOffset? now = null,
        int maxPerHour = 200)
    {
        var at = now ?? _now;
        return new ConfirmationRequest(
            userId,
            address,
            Hash(hashStart[0]),
            "salt",
            at,
            at.AddMinutes(30),
            TimeSpan.FromSeconds(60),
            3,
            3,
            maxPerHour);
    }

    /// <summary>
    ///     Reserves a confirmation and returns the outcome.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="userId">The user.</param>
    /// <param name="address">The address.</param>
    /// <param name="hashStart">The first character of the hash.</param>
    /// <param name="now">The time of the request.</param>
    /// <param name="maxPerHour">The server limit per hour.</param>
    /// <returns>The outcome.</returns>
    private static async Task<ConfirmationReservation> ReserveAsync(
        TestStore db,
        int userId,
        string address,
        char hashStart,
        DateTimeOffset now,
        int maxPerHour = 200)
    {
        var (outcome, _) = await db.Notifications.ReserveConfirmationAsync(
            Request(userId, address, hashStart.ToString(), now, maxPerHour),
            Draft("notification.settings-changed"),
            Token);
        return outcome;
    }

    /// <summary>
    ///     Counts the rows of the send log.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <returns>The number of rows.</returns>
    private static async Task<int> CountLogAsync(TestStore db)
    {
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        return await context.NotificationSendLog.CountAsync(Token);
    }

    /// <summary>
    ///     Counts the audit entries of the settings change.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <returns>The number of entries.</returns>
    private static async Task<int> CountAuditAsync(TestStore db)
    {
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        return await context.AuditEntries.CountAsync(a => a.Action == "notification.settings-changed", Token);
    }

    /// <summary>
    ///     A hash of the shape the service stores (64 lowercase hex characters).
    /// </summary>
    /// <param name="start">The first character.</param>
    /// <returns>The hash.</returns>
    private static string Hash(char start) => start + new string('0', 63);

    /// <summary>
    ///     Builds an audit draft.
    /// </summary>
    /// <param name="action">The action code.</param>
    /// <returns>The draft.</returns>
    private static AuditDraft Draft(string action)
    {
        return AuditDraft.By(new Actor("ann", "Ann"), action, "test");
    }

    /// <summary>
    ///     Creates a user in the database.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="username">The user name.</param>
    /// <returns>The identity of the user.</returns>
    private static async Task<int> AddUserAsync(TestStore db, string username = "ann")
    {
        var user = await db.Users.AddAsync(
            new User { Username = username, DisplayName = username, PasswordHash = "hash", Created = _now },
            Token);
        return user.Id;
    }
}
