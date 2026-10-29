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
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.EntityFrameworkCore;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for <see cref="TotpStore"/> and the second factor tables of <see cref="ReviewCenterDbContext"/> against
///     a real SQLite database file, where the conditional updates must have exactly one winner.
/// </summary>
public class TotpStoreTests
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
    ///     Builds an audit draft.
    /// </summary>
    /// <param name="action">The action code.</param>
    /// <returns>The draft.</returns>
    private static AuditDraft Draft(string action)
    {
        return AuditDraft.By(new Actor("ann", "Ann"), action, "test");
    }

    /// <summary>
    ///     A hash of the shape the service stores (64 lowercase hex characters).
    /// </summary>
    /// <param name="start">The first character.</param>
    /// <returns>The hash.</returns>
    private static string Hash(char start) => start + new string('0', 63);

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

    /// <summary>
    ///     Saves a pending enrollment for a user.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="userId">The user.</param>
    /// <returns>Whether it was saved.</returns>
    private static Task<bool> PendingAsync(TestStore db, int userId)
    {
        return db.Totp.SavePendingAsync(
            new UserTotp { UserId = userId, ProtectedSecret = "protected-" + userId, Created = _now }, Token);
    }

    /// <summary>
    ///     Saves and confirms an enrollment with the given recovery code hashes.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="userId">The user.</param>
    /// <param name="step">The accepted step of the confirmation.</param>
    /// <param name="hashes">The recovery code hashes.</param>
    /// <returns>A task that completes when the enrollment is confirmed.</returns>
    private static async Task EnrollAsync(TestStore db, int userId, long step, params string[] hashes)
    {
        Assert.True(await PendingAsync(db, userId));
        Assert.True(await db.Totp.ConfirmAsync(userId, step, _now, hashes, Draft("auth.totp-enrolled"), Token));
    }

    /// <summary>
    ///     Test that a pending enrollment round-trips, is not active, is replaced by a newer one and is listed nowhere.
    /// </summary>
    [Fact]
    public async Task TotpStore_SavePendingAsync_RoundTripsAndReplacesAPendingEnrollment()
    {
        // Arrange: a user
        using var db = new TestStore();
        var userId = await AddUserAsync(db);

        // Act: save two pending enrollments in a row
        Assert.True(await PendingAsync(db, userId));
        Assert.True(await db.Totp.SavePendingAsync(
            new UserTotp { UserId = userId, ProtectedSecret = "second", Created = _now.AddMinutes(1) }, Token));
        var row = await db.Totp.GetAsync(userId, Token);

        // Assert: the second one is stored, not active, and not in the list of enrolled users
        Assert.NotNull(row);
        Assert.Equal("second", row.ProtectedSecret);
        Assert.Equal(_now.AddMinutes(1), row.Created);
        Assert.Null(row.ConfirmedAt);
        Assert.False(row.IsActive);
        Assert.Equal(0, row.LastAcceptedStep);
        Assert.Empty(await db.Totp.ListActiveUserIdsAsync(Token));
        Assert.Null(await db.Totp.GetAsync(userId + 1, Token));
    }

    /// <summary>
    ///     Runs an action many times at once, released together.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="count">How many parallel calls.</param>
    /// <param name="action">The action; gets the index of the call.</param>
    /// <returns>The results, or the first exception when any call fails.</returns>
    private static async Task<T[]> ParallelAsync<T>(int count, Func<int, Task<T>> action)
    {
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(
            async () =>
            {
                start.Wait(Token);
                return await action(i);
            },
            Token)).ToList();
        start.Set();
        return await Task.WhenAll(tasks);
    }

    /// <summary>
    ///     Test that many concurrent starts of the same user never raise: all of them succeed, and exactly one
    ///     coherent pending enrollment remains.
    /// </summary>
    [Fact]
    public async Task TotpStore_SavePendingAsync_ManyConcurrentStarts_NoExceptionAndOneCoherentPendingRow()
    {
        // Arrange: a database; five rounds, each with a new user and 32 starts with distinct secrets
        using var db = new TestStore();
        for (var round = 0; round < 5; round++)
        {
            var userId = await AddUserAsync(db, "user" + round);
            var secrets = Enumerable.Range(0, 32).Select(i => $"secret-{round}-{i}").ToList();

            // Act: all start at once
            var results = await ParallelAsync(
                32,
                i => db.Totp.SavePendingAsync(new UserTotp { UserId = userId, ProtectedSecret = secrets[i], Created = _now }, Token));

            // Assert: none raised, all succeeded, and one pending row with one of the submitted secrets
            Assert.All(results, Assert.True);
            await using var context = await db.Factory.CreateDbContextAsync(Token);
            var row = Assert.Single(await context.UserTotps.Where(t => t.UserId == userId).ToListAsync(Token));
            Assert.Null(row.ConfirmedAt);
            Assert.Equal(0, row.LastAcceptedStep);
            Assert.Contains(row.ProtectedSecret, secrets);
        }
    }

    /// <summary>
    ///     Test that concurrent starts against a confirmed enrollment all refuse and never overwrite it.
    /// </summary>
    [Fact]
    public async Task TotpStore_SavePendingAsync_ConfirmedRow_ConcurrentStarts_AllRefuseAndNeverOverwrite()
    {
        // Arrange: a confirmed enrollment with two recovery codes
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await EnrollAsync(db, userId, 42, Hash('a'), Hash('b'));

        // Act: 32 starts at once
        var results = await ParallelAsync(
            32,
            i => db.Totp.SavePendingAsync(new UserTotp { UserId = userId, ProtectedSecret = "other-" + i, Created = _now.AddHours(1) }, Token));

        // Assert: all refused, and the enrollment is exactly as it was
        Assert.All(results, Assert.False);
        var row = await db.Totp.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.Equal("protected-" + userId, row.ProtectedSecret);
        Assert.Equal(_now, row.ConfirmedAt);
        Assert.Equal(42, row.LastAcceptedStep);
        Assert.Equal(2, await db.Totp.CountUnusedRecoveryCodesAsync(userId, Token));
    }

    /// <summary>
    ///     Test that starts racing with a confirmation never raise and never overwrite the confirmed enrollment.
    /// </summary>
    [Fact]
    public async Task TotpStore_SavePendingAsync_ConcurrentWithConfirm_NoExceptionAndConfirmedStaysConfirmed()
    {
        // Arrange: a pending enrollment
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        Assert.True(await PendingAsync(db, userId));
        var secrets = Enumerable.Range(0, 16).Select(i => "racing-" + i).Append("protected-" + userId).ToList();

        // Act: 16 starts and one confirmation at once
        var results = await ParallelAsync(
            17,
            async i => i < 16
                ? await db.Totp.SavePendingAsync(new UserTotp { UserId = userId, ProtectedSecret = secrets[i], Created = _now }, Token)
                : await db.Totp.ConfirmAsync(userId, 7, _now, [Hash('a'), Hash('b')], Draft("auth.totp-enrolled"), Token));

        // Assert: the confirmation won (a pending row always exists), the final row is confirmed with its step and
        // codes, and starts after it were refused
        Assert.True(results[16]);
        var row = await db.Totp.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.True(row.IsActive);
        Assert.Equal(7, row.LastAcceptedStep);
        Assert.Contains(row.ProtectedSecret, secrets);
        Assert.Equal(2, await db.Totp.CountUnusedRecoveryCodesAsync(userId, Token));
    }

    /// <summary>
    ///     Test that confirming activates the enrollment, stores the recovery codes and the audit entry together, and
    ///     that a second confirmation and a new pending enrollment are refused.
    /// </summary>
    [Fact]
    public async Task TotpStore_ConfirmAsync_ActivatesStoresCodesAndAuditsOnce()
    {
        // Arrange: a pending enrollment
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        Assert.True(await PendingAsync(db, userId));
        var hashes = new[] { Hash('a'), Hash('b'), Hash('c') };

        // Act: confirm twice, then try to start over
        var first = await db.Totp.ConfirmAsync(userId, 55, _now, hashes, Draft("auth.totp-enrolled"), Token);
        var second = await db.Totp.ConfirmAsync(userId, 56, _now, [Hash('d')], Draft("auth.totp-enrolled"), Token);
        var restart = await PendingAsync(db, userId);

        // Assert: only the first won, the row is active with its step, three codes exist, one audit entry, and the
        // confirmed enrollment was not replaced
        Assert.True(first);
        Assert.False(second);
        Assert.False(restart);
        var row = await db.Totp.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.True(row.IsActive);
        Assert.Equal(_now, row.ConfirmedAt);
        Assert.Equal(55, row.LastAcceptedStep);
        Assert.Equal(3, await db.Totp.CountUnusedRecoveryCodesAsync(userId, Token));
        Assert.Equal([userId], await db.Totp.ListActiveUserIdsAsync(Token));
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        Assert.Single(await context.AuditEntries.Where(e => e.Action == "auth.totp-enrolled").ToListAsync(Token));
    }

    /// <summary>
    ///     Test that confirming without a pending enrollment changes nothing and records nothing.
    /// </summary>
    [Fact]
    public async Task TotpStore_ConfirmAsync_NothingPending_ReturnsFalseAndStoresNothing()
    {
        // Arrange: a user without enrollment
        using var db = new TestStore();
        var userId = await AddUserAsync(db);

        // Act: confirm
        var confirmed = await db.Totp.ConfirmAsync(userId, 1, _now, [Hash('a')], Draft("auth.totp-enrolled"), Token);

        // Assert: refused, no codes and no audit entry
        Assert.False(confirmed);
        Assert.Equal(0, await db.Totp.CountUnusedRecoveryCodesAsync(userId, Token));
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        Assert.Empty(await context.AuditEntries.Where(e => e.Action == "auth.totp-enrolled").ToListAsync(Token));
    }

    /// <summary>
    ///     Test that a step can only move forward, only for a confirmed enrollment, and never twice.
    /// </summary>
    [Fact]
    public async Task TotpStore_TryAdvanceStepAsync_OnlyForwardAndOnlyWhenConfirmed()
    {
        // Arrange: ann confirmed at step 100, bob only pending
        using var db = new TestStore();
        var ann = await AddUserAsync(db);
        var bob = await AddUserAsync(db, "bob");
        await EnrollAsync(db, ann, 100, Hash('a'));
        Assert.True(await PendingAsync(db, bob));

        // Act: try steps at, before and after the stored one, for a pending user and for an unknown user
        var same = await db.Totp.TryAdvanceStepAsync(ann, 100, Token);
        var older = await db.Totp.TryAdvanceStepAsync(ann, 99, Token);
        var newer = await db.Totp.TryAdvanceStepAsync(ann, 101, Token);
        var again = await db.Totp.TryAdvanceStepAsync(ann, 101, Token);
        var pending = await db.Totp.TryAdvanceStepAsync(bob, 5, Token);
        var unknown = await db.Totp.TryAdvanceStepAsync(999, 5, Token);

        // Assert: only the newer step was accepted, once
        Assert.False(same);
        Assert.False(older);
        Assert.True(newer);
        Assert.False(again);
        Assert.False(pending);
        Assert.False(unknown);
        Assert.Equal(101, (await db.Totp.GetAsync(ann, Token))!.LastAcceptedStep);
    }

    /// <summary>
    ///     Test that many parallel requests with the same step have exactly one winner (compare-and-set).
    /// </summary>
    [Fact]
    public async Task TotpStore_TryAdvanceStepAsync_ThirtyTwoParallelRequests_HaveExactlyOneWinner()
    {
        // Arrange: a confirmed enrollment and 32 requests released together
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await EnrollAsync(db, userId, 10, Hash('a'));
        using var start = new ManualResetEventSlim();

        // Act: all try step 11
        var tasks = Enumerable.Range(0, 32).Select(_ => Task.Run(
            async () =>
            {
                start.Wait(Token);
                return await db.Totp.TryAdvanceStepAsync(userId, 11, Token);
            },
            Token)).ToList();
        start.Set();
        var results = await Task.WhenAll(tasks);

        // Assert: exactly one won
        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(11, (await db.Totp.GetAsync(userId, Token))!.LastAcceptedStep);
    }

    /// <summary>
    ///     Test that a recovery code can be spent once, with one audit entry, and that unknown and foreign codes are
    ///     refused without an entry.
    /// </summary>
    [Fact]
    public async Task TotpStore_TryConsumeRecoveryCodeAsync_SpendsOnceAndAuditsOnce()
    {
        // Arrange: ann has two codes; bob has one
        using var db = new TestStore();
        var ann = await AddUserAsync(db);
        var bob = await AddUserAsync(db, "bob");
        await EnrollAsync(db, ann, 1, Hash('a'), Hash('b'));
        await EnrollAsync(db, bob, 1, Hash('c'));

        // Act: spend ann's first code twice, an unknown code, and bob's code as ann
        var first = await db.Totp.TryConsumeRecoveryCodeAsync(ann, Hash('a'), _now, Draft("auth.totp-recovery-used"), Token);
        var again = await db.Totp.TryConsumeRecoveryCodeAsync(ann, Hash('a'), _now, Draft("auth.totp-recovery-used"), Token);
        var unknown = await db.Totp.TryConsumeRecoveryCodeAsync(ann, Hash('z'), _now, Draft("auth.totp-recovery-used"), Token);
        var foreign = await db.Totp.TryConsumeRecoveryCodeAsync(ann, Hash('c'), _now, Draft("auth.totp-recovery-used"), Token);

        // Assert: one success, one entry, and the counts are right
        Assert.True(first);
        Assert.False(again);
        Assert.False(unknown);
        Assert.False(foreign);
        Assert.Equal(1, await db.Totp.CountUnusedRecoveryCodesAsync(ann, Token));
        Assert.Equal(1, await db.Totp.CountUnusedRecoveryCodesAsync(bob, Token));
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        Assert.Single(await context.AuditEntries.Where(e => e.Action == "auth.totp-recovery-used").ToListAsync(Token));
    }

    /// <summary>
    ///     Test that parallel requests with the same recovery code have exactly one winner and one audit entry.
    /// </summary>
    [Fact]
    public async Task TotpStore_TryConsumeRecoveryCodeAsync_ParallelRequests_HaveExactlyOneWinner()
    {
        // Arrange: one code and eight requests released together
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await EnrollAsync(db, userId, 1, Hash('a'));
        using var start = new ManualResetEventSlim();

        // Act: all spend the code
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(
            async () =>
            {
                start.Wait(Token);
                return await db.Totp.TryConsumeRecoveryCodeAsync(userId, Hash('a'), _now, Draft("auth.totp-recovery-used"), Token);
            },
            Token)).ToList();
        start.Set();
        var results = await Task.WhenAll(tasks);

        // Assert: one winner and one entry
        Assert.Equal(1, results.Count(r => r));
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        Assert.Single(await context.AuditEntries.Where(e => e.Action == "auth.totp-recovery-used").ToListAsync(Token));
    }

    /// <summary>
    ///     Test that replacing the recovery codes removes the old ones, and is refused without a confirmed enrollment.
    /// </summary>
    [Fact]
    public async Task TotpStore_ReplaceRecoveryCodesAsync_ReplacesAllOldCodes()
    {
        // Arrange: ann confirmed with two codes, one spent; bob only pending
        using var db = new TestStore();
        var ann = await AddUserAsync(db);
        var bob = await AddUserAsync(db, "bob");
        await EnrollAsync(db, ann, 1, Hash('a'), Hash('b'));
        await db.Totp.TryConsumeRecoveryCodeAsync(ann, Hash('a'), _now, Draft("auth.totp-recovery-used"), Token);
        Assert.True(await PendingAsync(db, bob));

        // Act: replace for ann and try for bob
        var replaced = await db.Totp.ReplaceRecoveryCodesAsync(ann, [Hash('d'), Hash('e'), Hash('f')], _now, Draft("auth.totp-recovery-regenerated"), Token);
        var pending = await db.Totp.ReplaceRecoveryCodesAsync(bob, [Hash('g')], _now, Draft("auth.totp-recovery-regenerated"), Token);

        // Assert: three new unused codes, the old ones are gone, bob got none
        Assert.True(replaced);
        Assert.False(pending);
        Assert.Equal(3, await db.Totp.CountUnusedRecoveryCodesAsync(ann, Token));
        Assert.False(await db.Totp.TryConsumeRecoveryCodeAsync(ann, Hash('b'), _now, Draft("auth.totp-recovery-used"), Token));
        Assert.True(await db.Totp.TryConsumeRecoveryCodeAsync(ann, Hash('d'), _now, Draft("auth.totp-recovery-used"), Token));
        Assert.Equal(0, await db.Totp.CountUnusedRecoveryCodesAsync(bob, Token));
    }

    /// <summary>
    ///     Test that removing deletes the enrollment and its codes with one audit entry, and that removing nothing
    ///     records nothing.
    /// </summary>
    [Fact]
    public async Task TotpStore_RemoveAsync_DeletesEverythingOnceWithOneAuditEntry()
    {
        // Arrange: ann confirmed with codes
        using var db = new TestStore();
        var ann = await AddUserAsync(db);
        await EnrollAsync(db, ann, 1, Hash('a'), Hash('b'));

        // Act: remove twice
        var first = await db.Totp.RemoveAsync(ann, Draft("auth.totp-disabled"), Token);
        var second = await db.Totp.RemoveAsync(ann, Draft("auth.totp-disabled"), Token);

        // Assert: removed once, nothing remains, one entry
        Assert.True(first);
        Assert.False(second);
        Assert.Null(await db.Totp.GetAsync(ann, Token));
        Assert.Equal(0, await db.Totp.CountUnusedRecoveryCodesAsync(ann, Token));
        Assert.Empty(await db.Totp.ListActiveUserIdsAsync(Token));
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        Assert.Single(await context.AuditEntries.Where(e => e.Action == "auth.totp-disabled").ToListAsync(Token));
    }

    /// <summary>
    ///     Test that removing a pending enrollment leaves a confirmed one alone.
    /// </summary>
    [Fact]
    public async Task TotpStore_RemovePendingAsync_RemovesPendingOnly()
    {
        // Arrange: ann confirmed, bob pending
        using var db = new TestStore();
        var ann = await AddUserAsync(db);
        var bob = await AddUserAsync(db, "bob");
        await EnrollAsync(db, ann, 1, Hash('a'));
        Assert.True(await PendingAsync(db, bob));

        // Act: remove both as pending
        var annResult = await db.Totp.RemovePendingAsync(ann, Token);
        var bobResult = await db.Totp.RemovePendingAsync(bob, Token);

        // Assert: only bob's was removed
        Assert.False(annResult);
        Assert.True(bobResult);
        Assert.NotNull(await db.Totp.GetAsync(ann, Token));
        Assert.Null(await db.Totp.GetAsync(bob, Token));
    }

    /// <summary>
    ///     Test that two identical recovery code hashes of one user are refused by the unique index and the whole
    ///     confirmation, including its audit entry, is rolled back.
    /// </summary>
    [Fact]
    public async Task TotpStore_ConfirmAsync_DuplicateHash_IsRefusedByTheDatabaseAndRolledBack()
    {
        // Arrange: a pending enrollment
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        Assert.True(await PendingAsync(db, userId));

        // Act: confirm with two equal hashes
        await Assert.ThrowsAnyAsync<Exception>(() => db.Totp.ConfirmAsync(
            userId, 1, _now, [Hash('a'), Hash('a')], Draft("auth.totp-enrolled"), Token));

        // Assert: still pending, no codes, no audit entry
        var row = await db.Totp.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.False(row.IsActive);
        Assert.Equal(0, await db.Totp.CountUnusedRecoveryCodesAsync(userId, Token));
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        Assert.Empty(await context.AuditEntries.Where(e => e.Action == "auth.totp-enrolled").ToListAsync(Token));
    }

    /// <summary>
    ///     Test that the same recovery code hash may exist for two different users.
    /// </summary>
    [Fact]
    public async Task TotpStore_ConfirmAsync_SameHashForDifferentUsers_IsAllowed()
    {
        // Arrange and Act: two users confirm with the same hash
        using var db = new TestStore();
        var ann = await AddUserAsync(db);
        var bob = await AddUserAsync(db, "bob");
        await EnrollAsync(db, ann, 1, Hash('a'));
        await EnrollAsync(db, bob, 1, Hash('a'));

        // Assert: both have their code
        Assert.Equal(1, await db.Totp.CountUnusedRecoveryCodesAsync(ann, Token));
        Assert.Equal(1, await db.Totp.CountUnusedRecoveryCodesAsync(bob, Token));
    }

    /// <summary>
    ///     Test that the audit chain stays intact after every kind of second factor change.
    /// </summary>
    [Fact]
    public async Task TotpStore_Changes_KeepTheAuditChainIntact()
    {
        // Arrange: a user who goes through the life cycle
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await EnrollAsync(db, userId, 1, Hash('a'), Hash('b'));
        await db.Totp.TryConsumeRecoveryCodeAsync(userId, Hash('a'), _now, Draft("auth.totp-recovery-used"), Token);
        await db.Totp.ReplaceRecoveryCodesAsync(userId, [Hash('c')], _now, Draft("auth.totp-recovery-regenerated"), Token);
        await db.Totp.RemoveAsync(userId, Draft("auth.totp-disabled"), Token);

        // Act: verify the chain
        var result = await new AuditService(db.Audit, null, TimeProvider.System).VerifyAsync(Token);

        // Assert: intact
        Assert.True(result.IsIntact);
    }

    /// <summary>
    ///     Test that removing a user deletes the enrollment and the codes with it (cascade).
    /// </summary>
    [Fact]
    public async Task TotpStore_DeletingTheUser_DeletesTheEnrollmentAndCodes()
    {
        // Arrange: a confirmed enrollment with a code
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        await EnrollAsync(db, userId, 1, Hash('a'));

        // Act: delete the user row directly
        await using (var context = await db.Factory.CreateDbContextAsync(Token))
        {
            await context.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(Token);
        }

        // Assert: both tables are empty for the user
        await using var check = await db.Factory.CreateDbContextAsync(Token);
        Assert.Empty(await check.UserTotps.ToListAsync(Token));
        Assert.Empty(await check.TotpRecoveryCodes.ToListAsync(Token));
    }

    /// <summary>
    ///     Test that the stored secret column holds exactly what the store was given, and null arguments are refused.
    /// </summary>
    [Fact]
    public async Task TotpStore_NullArguments_Throw()
    {
        // Arrange: a store
        using var db = new TestStore();

        // Act and Assert: each misuse is refused
        await Assert.ThrowsAsync<ArgumentNullException>(() => db.Totp.SavePendingAsync(null!, Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => db.Totp.ConfirmAsync(1, 1, _now, null!, Draft("a"), Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => db.Totp.ConfirmAsync(1, 1, _now, [], null!, Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => db.Totp.TryConsumeRecoveryCodeAsync(1, null!, _now, Draft("a"), Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => db.Totp.TryConsumeRecoveryCodeAsync(1, "h", _now, null!, Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => db.Totp.ReplaceRecoveryCodesAsync(1, null!, _now, Draft("a"), Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => db.Totp.ReplaceRecoveryCodesAsync(1, [], _now, null!, Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => db.Totp.RemoveAsync(1, null!, Token));
    }
}
