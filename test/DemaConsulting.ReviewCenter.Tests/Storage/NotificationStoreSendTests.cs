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
using Microsoft.EntityFrameworkCore;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for the reservation of notification mails in
///     <see cref="DemaConsulting.ReviewCenter.Storage.NotificationStore"/>: the sending limits are applied and the mail
///     is counted in one serialized transaction, against a real SQLite database file.
/// </summary>
public class NotificationStoreSendTests
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
    ///     Test that a reservation under both limits is granted and counts the mail with its kind, address and time.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveSend_UnderLimits_ReservedAndCounted()
    {
        // Arrange
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");

        // Act
        var (outcome, logId) = await db.Notifications.ReserveSendAsync(
            ann, NotificationSendLogEntry.KindDigest, "ann@example.com", _now, 200, 24, Token);

        // Assert
        Assert.Equal(SendReservation.Reserved, outcome);
        Assert.NotEqual(0, logId);
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        var row = await context.NotificationSendLog.SingleAsync(Token);
        Assert.Equal(logId, row.Id);
        Assert.Equal(ann, row.UserId);
        Assert.Equal("ann@example.com", row.ToAddress);
        Assert.Equal(NotificationSendLogEntry.KindDigest, row.Kind);
        Assert.Equal(_now, row.SentUtc);
    }

    /// <summary>
    ///     Test that the hourly limit of the server counts the mails of every kind and of every user.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveSend_HourlyLimit_CountsOtherKindsAndUsers()
    {
        // Arrange: a confirmation mail of Bob and an update mail of Cyd in the last hour, a limit of two
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var cyd = await AddUserAsync(db, "cyd");
        await AddLogAsync(db, bob, NotificationSendLogEntry.KindConfirmation, _now.AddMinutes(-30));
        await AddLogAsync(db, cyd, NotificationSendLogEntry.KindUpdate, _now.AddMinutes(-5));

        // Act
        var (outcome, logId) = await db.Notifications.ReserveSendAsync(
            ann, NotificationSendLogEntry.KindUpdate, "ann@example.com", _now, 2, 24, Token);

        // Assert: refused, and nothing was counted
        Assert.Equal(SendReservation.ServerHourLimitReached, outcome);
        Assert.Equal(0, logId);
        Assert.Equal(2, await CountLogAsync(db));
    }

    /// <summary>
    ///     Test that a mail that is older than one hour does not count towards the hourly limit.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveSend_HourlyLimit_IgnoresOlderMails()
    {
        // Arrange: one mail 61 minutes ago, a limit of one
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        await AddLogAsync(db, bob, NotificationSendLogEntry.KindUpdate, _now.AddMinutes(-61));

        // Act
        var (outcome, _) = await db.Notifications.ReserveSendAsync(
            ann, NotificationSendLogEntry.KindUpdate, "ann@example.com", _now, 1, 24, Token);

        // Assert
        Assert.Equal(SendReservation.Reserved, outcome);
    }

    /// <summary>
    ///     Test that the daily limit of a user counts the mails of every kind of that user, and no mail of another
    ///     user.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveSend_UserDailyLimit_CountsOwnMailsOfEveryKind()
    {
        // Arrange: three mails of Ann in the last day, older than one hour, one of them a confirmation mail; two of Bob
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        await AddLogAsync(db, ann, NotificationSendLogEntry.KindConfirmation, _now.AddHours(-3));
        await AddLogAsync(db, ann, NotificationSendLogEntry.KindUpdate, _now.AddHours(-5));
        await AddLogAsync(db, ann, NotificationSendLogEntry.KindDigest, _now.AddHours(-23));
        await AddLogAsync(db, bob, NotificationSendLogEntry.KindUpdate, _now.AddHours(-3));
        await AddLogAsync(db, bob, NotificationSendLogEntry.KindUpdate, _now.AddHours(-4));

        // Act: a limit of three per user
        var (annOutcome, annLogId) = await db.Notifications.ReserveSendAsync(
            ann, NotificationSendLogEntry.KindUpdate, "ann@example.com", _now, 200, 3, Token);
        var (bobOutcome, _) = await db.Notifications.ReserveSendAsync(
            bob, NotificationSendLogEntry.KindUpdate, "bob@example.com", _now, 200, 3, Token);

        // Assert: Ann is refused, Bob is not
        Assert.Equal(SendReservation.UserDayLimitReached, annOutcome);
        Assert.Equal(0, annLogId);
        Assert.Equal(SendReservation.Reserved, bobOutcome);
    }

    /// <summary>
    ///     Test that two reservations that race for the last slot of the hourly limit yield exactly one reservation.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveSend_ParallelAtHourlyLimit_OneReserved()
    {
        // Arrange: one mail below the limit of three
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var cyd = await AddUserAsync(db, "cyd");
        await AddLogAsync(db, cyd, NotificationSendLogEntry.KindUpdate, _now.AddMinutes(-1));
        await AddLogAsync(db, cyd, NotificationSendLogEntry.KindUpdate, _now.AddMinutes(-2));

        // Act
        var results = await Task.WhenAll(
            db.Notifications.ReserveSendAsync(ann, NotificationSendLogEntry.KindUpdate, "ann@example.com", _now, 3, 24, Token),
            db.Notifications.ReserveSendAsync(bob, NotificationSendLogEntry.KindUpdate, "bob@example.com", _now, 3, 24, Token));

        // Assert
        Assert.Equal(1, results.Count(r => r.Outcome == SendReservation.Reserved));
        Assert.Equal(1, results.Count(r => r.Outcome == SendReservation.ServerHourLimitReached));
        Assert.Equal(3, await CountLogAsync(db));
    }

    /// <summary>
    ///     Test that two reservations of one user that race for the last slot of the daily limit yield exactly one
    ///     reservation.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveSend_ParallelAtUserLimit_OneReserved()
    {
        // Arrange: one mail below the limit of two
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        await AddLogAsync(db, ann, NotificationSendLogEntry.KindUpdate, _now.AddHours(-3));

        // Act
        var results = await Task.WhenAll(
            db.Notifications.ReserveSendAsync(ann, NotificationSendLogEntry.KindUpdate, "ann@example.com", _now, 200, 2, Token),
            db.Notifications.ReserveSendAsync(ann, NotificationSendLogEntry.KindDigest, "ann@example.com", _now, 200, 2, Token));

        // Assert
        Assert.Equal(1, results.Count(r => r.Outcome == SendReservation.Reserved));
        Assert.Equal(1, results.Count(r => r.Outcome == SendReservation.UserDayLimitReached));
        Assert.Equal(2, await CountLogAsync(db));
    }

    /// <summary>
    ///     Test that releasing a reservation frees its slot.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReleaseSend_Reserved_FreesSlot()
    {
        // Arrange: the only slot of the hourly limit is reserved
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var (_, logId) = await db.Notifications.ReserveSendAsync(
            ann, NotificationSendLogEntry.KindUpdate, "ann@example.com", _now, 1, 24, Token);
        var (refused, _) = await db.Notifications.ReserveSendAsync(
            ann, NotificationSendLogEntry.KindUpdate, "ann@example.com", _now, 1, 24, Token);
        Assert.Equal(SendReservation.ServerHourLimitReached, refused);

        // Act
        await db.Notifications.ReleaseSendAsync(ann, logId, Token);

        // Assert: the row is gone and the slot can be reserved again
        Assert.Equal(0, await CountLogAsync(db));
        var (outcome, _) = await db.Notifications.ReserveSendAsync(
            ann, NotificationSendLogEntry.KindUpdate, "ann@example.com", _now, 1, 24, Token);
        Assert.Equal(SendReservation.Reserved, outcome);
    }

    /// <summary>
    ///     Test that releasing a reservation that is gone does nothing, and that the row of another user is not
    ///     deleted.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReleaseSend_GoneOrForeign_NoOp()
    {
        // Arrange: a reservation of Ann
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var (_, logId) = await db.Notifications.ReserveSendAsync(
            ann, NotificationSendLogEntry.KindUpdate, "ann@example.com", _now, 200, 24, Token);

        // Act: Bob names the row of Ann, then Ann releases twice
        await db.Notifications.ReleaseSendAsync(bob, logId, Token);
        var afterForeign = await CountLogAsync(db);
        await db.Notifications.ReleaseSendAsync(ann, logId, Token);
        await db.Notifications.ReleaseSendAsync(ann, logId, Token);

        // Assert
        Assert.Equal(1, afterForeign);
        Assert.Equal(0, await CountLogAsync(db));
    }

    /// <summary>
    ///     Test that a reservation deletes the rows that are older than one day.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveSend_OldRows_AreDeleted()
    {
        // Arrange: a row of 25 hours ago and a row of 23 hours ago
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        await AddLogAsync(db, ann, NotificationSendLogEntry.KindUpdate, _now.AddHours(-25));
        await AddLogAsync(db, ann, NotificationSendLogEntry.KindUpdate, _now.AddHours(-23));

        // Act
        await db.Notifications.ReserveSendAsync(
            ann, NotificationSendLogEntry.KindUpdate, "ann@example.com", _now, 200, 24, Token);

        // Assert: the old row went, the other two stay
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        var rows = await context.NotificationSendLog.ToListAsync(Token);
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, e => e.SentUtc == _now.AddHours(-25));
    }

    /// <summary>
    ///     Test that a confirmation mail that was reserved earlier counts against the hourly limit of a notification
    ///     mail.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveSend_AfterConfirmationReserved_SharesHourlyCount()
    {
        // Arrange: Bob has a reserved confirmation mail, the limit of the server is one mail per hour
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var (confirmation, _) = await db.Notifications.ReserveConfirmationAsync(Request(bob, _now), Draft(), Token);
        Assert.Equal(ConfirmationReservation.Reserved, confirmation);

        // Act
        var (outcome, _) = await db.Notifications.ReserveSendAsync(
            ann, NotificationSendLogEntry.KindUpdate, "ann@example.com", _now, 1, 24, Token);

        // Assert
        Assert.Equal(SendReservation.ServerHourLimitReached, outcome);
    }

    /// <summary>
    ///     Test that a notification mail that was reserved earlier counts against the hourly limit of a confirmation
    ///     mail.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ReserveSend_BeforeConfirmation_SharesHourlyCount()
    {
        // Arrange: Ann has a reserved notification mail, the limit of the server is one mail per hour
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var (reserved, _) = await db.Notifications.ReserveSendAsync(
            ann, NotificationSendLogEntry.KindUpdate, "ann@example.com", _now, 1, 24, Token);
        Assert.Equal(SendReservation.Reserved, reserved);

        // Act
        var (outcome, _) = await db.Notifications.ReserveConfirmationAsync(Request(bob, _now, maxPerHour: 1), Draft(), Token);

        // Assert
        Assert.Equal(ConfirmationReservation.GlobalLimitReached, outcome);
    }

    /// <summary>
    ///     Test that the decision of the limits looks at the server limit first, then at the user limit.
    /// </summary>
    [Theory]
    [InlineData(0, 0, SendReservation.Reserved)]
    [InlineData(199, 23, SendReservation.Reserved)]
    [InlineData(200, 0, SendReservation.ServerHourLimitReached)]
    [InlineData(0, 24, SendReservation.UserDayLimitReached)]
    [InlineData(200, 24, SendReservation.ServerHourLimitReached)]
    public void SendLimits_Decide_Counts_ReturnsOutcome(int serverCount, int userCount, SendReservation expected)
    {
        // Act
        var outcome = SendLimits.Decide(serverCount, userCount, 200, 24);

        // Assert
        Assert.Equal(expected, outcome);
    }

    /// <summary>
    ///     Builds a request for a confirmation mail.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="now">The current time.</param>
    /// <param name="maxPerHour">The hourly limit of the server.</param>
    /// <returns>The request.</returns>
    private static ConfirmationRequest Request(int userId, DateTimeOffset now, int maxPerHour = 200)
    {
        return new ConfirmationRequest(
            userId,
            "pending@example.com",
            "a" + new string('0', 63),
            "salt",
            now,
            now.AddMinutes(30),
            TimeSpan.FromSeconds(60),
            3,
            3,
            maxPerHour);
    }

    /// <summary>
    ///     Builds an audit draft.
    /// </summary>
    /// <returns>The draft.</returns>
    private static AuditDraft Draft()
    {
        return AuditDraft.By(new Actor("ann", "Ann"), "notification.settings-changed", "test");
    }

    /// <summary>
    ///     Adds a row to the send log.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="userId">The user.</param>
    /// <param name="kind">The kind of the mail.</param>
    /// <param name="sentUtc">When the mail was sent.</param>
    /// <returns>A task that completes when the row is stored.</returns>
    private static async Task AddLogAsync(TestStore db, int userId, string kind, DateTimeOffset sentUtc)
    {
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        context.NotificationSendLog.Add(new NotificationSendLogEntry
        {
            UserId = userId,
            ToAddress = "x@example.com",
            Kind = kind,
            SentUtc = sentUtc
        });
        await context.SaveChangesAsync(Token);
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
    ///     Creates a user in the database.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="username">The user name.</param>
    /// <returns>The identity of the user.</returns>
    private static async Task<int> AddUserAsync(TestStore db, string username)
    {
        var user = await db.Users.AddAsync(
            new User { Username = username, DisplayName = username, PasswordHash = "hash", Created = _now },
            Token);
        return user.Id;
    }
}
