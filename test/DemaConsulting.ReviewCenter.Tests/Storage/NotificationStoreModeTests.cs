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

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for the notification mode, the notified items and the dispatch state of
///     <see cref="DemaConsulting.ReviewCenter.Storage.NotificationStore"/> and the matching tables of the database
///     context, against a real SQLite database file.
/// </summary>
public class NotificationStoreModeTests
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
    ///     Test that a row created without a mode reads as the mode None.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Get_NewRow_ModeNone()
    {
        // Arrange: a confirmed address, no mode chosen
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");

        // Act
        var row = await db.Notifications.GetAsync(userId, Token);

        // Assert
        Assert.NotNull(row);
        Assert.Equal(NotificationMode.None, row.Mode);
    }

    /// <summary>
    ///     Test that a row created without a mode reads as no pending baseline.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Get_NewRow_NoPendingBaseline()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");

        // Act
        var row = await db.Notifications.GetAsync(userId, Token);

        // Assert
        Assert.NotNull(row);
        Assert.False(row.BaselinePending);
    }

    /// <summary>
    ///     Test that a row created without a mode reads as an empty dispatch state.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Get_NewRow_EmptyDispatchState()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");

        // Act
        var row = await db.Notifications.GetAsync(userId, Token);

        // Assert
        Assert.NotNull(row);
        Assert.Null(row.BatchStartedUtc);
        Assert.Null(row.DigestHandledUtc);
        Assert.Equal(0, row.FailureCount);
        Assert.Null(row.NextAttemptUtc);
    }

    /// <summary>
    ///     Test that a chosen mode is returned to a store instance that was created after the choice.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_Persisted_ReadBack()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");

        // Act
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        var otherStore = new NotificationStore(db.Factory, new AuditAppender(TimeProvider.System));
        var row = await otherStore.GetAsync(userId, Token);

        // Assert
        Assert.NotNull(row);
        Assert.Equal(NotificationMode.Every, row.Mode);
    }

    /// <summary>
    ///     Test that the mode and its audit entry are saved in one transaction.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_AuditFailure_ModeUnchanged()
    {
        // Arrange: a trigger refuses every audit insert, which stands in for a failing disk
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        Execute(db, "CREATE TRIGGER refuse_audit BEFORE INSERT ON AuditEntries BEGIN SELECT RAISE(ABORT, 'audit refused'); END;");

        // Act
        await Assert.ThrowsAnyAsync<Exception>(
            () => db.Notifications.SetModeAsync(userId, NotificationMode.Daily, _now, Draft(), Token));

        // Assert
        Execute(db, "DROP TRIGGER refuse_audit");
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.Equal(NotificationMode.None, row.Mode);
        Assert.False(row.BaselinePending);
    }

    /// <summary>
    ///     Test that switching a mode on sets the baseline flag.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_NoneToDaily_FlagSet()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserWithLeftoverStateAsync(db);

        // Act
        var changed = await db.Notifications.SetModeAsync(userId, NotificationMode.Daily, _now, Draft(), Token);

        // Assert
        Assert.True(changed);
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.Equal(NotificationMode.Daily, row.Mode);
        Assert.True(row.BaselinePending);
    }

    /// <summary>
    ///     Test that switching a mode on clears a batch window that was left from an earlier use of the mode.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_NoneToDaily_BatchWindowCleared()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserWithLeftoverStateAsync(db);

        // Act
        await db.Notifications.SetModeAsync(userId, NotificationMode.Daily, _now, Draft(), Token);

        // Assert
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.Null(row.BatchStartedUtc);
    }

    /// <summary>
    ///     Test that switching a mode on clears a failure state that was left from an earlier use of the mode.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_NoneToDaily_FailureStateCleared()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserWithLeftoverStateAsync(db);

        // Act
        await db.Notifications.SetModeAsync(userId, NotificationMode.Daily, _now, Draft(), Token);

        // Assert
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.Equal(0, row.FailureCount);
        Assert.Null(row.NextAttemptUtc);
    }

    /// <summary>
    ///     Test that switching between two active modes keeps the items.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_DailyToEvery_KeepsItems()
    {
        // Arrange: Daily with one notified item
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Daily, _now, Draft(), Token);
        await ApplyCurrentAsync(
            db, userId, Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "to-answer")]));

        // Act
        var changed = await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);

        // Assert
        Assert.True(changed);
        Assert.Single(await db.Notifications.GetNotifiedAsync(userId, Token));
    }

    /// <summary>
    ///     Test that switching between two active modes sets no baseline flag.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_DailyToEvery_NoBaselineFlag()
    {
        // Arrange: Daily, with the baseline taken
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Daily, _now, Draft(), Token);
        await ApplyCurrentAsync(db, userId, Commit());

        // Act
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);

        // Assert
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.False(row.BaselinePending);
    }

    /// <summary>
    ///     Test that switching between two active modes before the baseline was recorded keeps the baseline pending.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_DailyToEveryBeforeBaseline_KeepsBaselinePending()
    {
        // Arrange: switched on, and no pass has recorded the baseline yet
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Daily, _now, Draft(), Token);

        // Act
        var changed = await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);

        // Assert
        Assert.True(changed);
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.Equal(NotificationMode.Every, row.Mode);
        Assert.True(row.BaselinePending);
    }

    /// <summary>
    ///     Test that switching between two active modes after the baseline was recorded keeps the baseline cleared.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_EveryToDailyAfterBaseline_KeepsBaselineCleared()
    {
        // Arrange: switched on, and a pass recorded the baseline
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        await ApplyCurrentAsync(db, userId, Commit());

        // Act
        await db.Notifications.SetModeAsync(userId, NotificationMode.Daily, _now, Draft(), Token);

        // Assert
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.False(row.BaselinePending);
    }

    /// <summary>
    ///     Test that switching to the mode None clears a baseline that is still pending.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_ToNoneBeforeBaseline_ClearsBaselinePending()
    {
        // Arrange: switched on, and no pass has recorded the baseline yet
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Daily, _now, Draft(), Token);

        // Act
        await db.Notifications.SetModeAsync(userId, NotificationMode.None, _now, Draft(), Token);

        // Assert
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.False(row.BaselinePending);
    }

    /// <summary>
    ///     Test that switching an active mode off and on again marks the baseline as pending again.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_ActiveToNoneToActive_SetsBaselinePendingAgain()
    {
        // Arrange: a baseline that was recorded, then the mode is switched off
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Daily, _now, Draft(), Token);
        await ApplyCurrentAsync(db, userId, Commit());
        await db.Notifications.SetModeAsync(userId, NotificationMode.None, _now, Draft(), Token);

        // Act
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);

        // Assert
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.True(row.BaselinePending);
    }

    /// <summary>
    ///     Test that the mode None deletes the notified items.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_None_DeletesNotified()
    {
        // Arrange: Every with a notified item and a handled daily slot
        using var db = new TestStore();
        var userId = await ArrangeEveryWithItemAndHandledSlotAsync(db);

        // Act
        var changed = await db.Notifications.SetModeAsync(userId, NotificationMode.None, _now, Draft(), Token);

        // Assert
        Assert.True(changed);
        Assert.Empty(await db.Notifications.GetNotifiedAsync(userId, Token));
    }

    /// <summary>
    ///     Test that the mode None deletes the memory of the handled daily slot.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_None_ClearsDailySlotMemory()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ArrangeEveryWithItemAndHandledSlotAsync(db);

        // Act
        await db.Notifications.SetModeAsync(userId, NotificationMode.None, _now, Draft(), Token);

        // Assert
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.Equal(NotificationMode.None, row.Mode);
        Assert.Null(row.DigestHandledUtc);
    }

    /// <summary>
    ///     Test that setting a mode for a user without a preference row changes nothing.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_NoRow_ReturnsFalse()
    {
        // Arrange
        using var db = new TestStore();
        var noRow = await AddUserAsync(db, "bob");

        // Act
        var changed = await db.Notifications.SetModeAsync(noRow, NotificationMode.Daily, _now, Draft(), Token);

        // Assert
        Assert.False(changed);
        Assert.Null(await db.Notifications.GetAsync(noRow, Token));
    }

    /// <summary>
    ///     Test that setting a mode for a user who has only a waiting address changes nothing.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_NoConfirmedAddress_ReturnsFalse()
    {
        // Arrange
        using var db = new TestStore();
        var pending = await AddUserAsync(db, "cy");
        await ReserveAsync(db, pending, "cy@example.com", 'c');

        // Act
        var changed = await db.Notifications.SetModeAsync(pending, NotificationMode.Daily, _now, Draft(), Token);

        // Assert
        Assert.False(changed);
        var row = await db.Notifications.GetAsync(pending, Token);
        Assert.NotNull(row);
        Assert.Equal(NotificationMode.None, row.Mode);
    }

    /// <summary>
    ///     Test that setting the mode that a user has already changes nothing.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_SameMode_ReturnsFalse()
    {
        // Arrange
        using var db = new TestStore();
        var confirmed = await ConfirmedUserAsync(db, "ann", "ann@example.com");

        // Act
        var changed = await db.Notifications.SetModeAsync(confirmed, NotificationMode.None, _now, Draft(), Token);

        // Assert
        Assert.False(changed);
    }

    /// <summary>
    ///     Test that an unchanged mode, a missing row and a missing address write no audit entry.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_NoChangePossible_NoAuditEntry()
    {
        // Arrange: a user without a row, a user with only a waiting address, and a user who has the mode already
        using var db = new TestStore();
        var noRow = await AddUserAsync(db, "bob");
        var pending = await AddUserAsync(db, "cy");
        await ReserveAsync(db, pending, "cy@example.com", 'c');
        var confirmed = await ConfirmedUserAsync(db, "ann", "ann@example.com");

        // Act
        await db.Notifications.SetModeAsync(noRow, NotificationMode.Daily, _now, Draft(), Token);
        await db.Notifications.SetModeAsync(pending, NotificationMode.Daily, _now, Draft(), Token);
        await db.Notifications.SetModeAsync(confirmed, NotificationMode.None, _now, Draft(), Token);

        // Assert
        Assert.Equal(0, await CountAuditAsync(db));
    }

    /// <summary>
    ///     Test that only users with a mode other than None and a confirmed address are listed.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ListRecipients_Mixed_OnlyEligible()
    {
        // Arrange: Ann Daily, Bob None, Cy waiting address only, Di Every
        using var db = new TestStore();
        var ann = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await ConfirmedUserAsync(db, "bob", "bob@example.com");
        var cy = await AddUserAsync(db, "cy");
        await ReserveAsync(db, cy, "cy@example.com", 'c');
        var di = await ConfirmedUserAsync(db, "di", "di@example.com");
        await db.Notifications.SetModeAsync(ann, NotificationMode.Daily, _now, Draft(), Token);
        await db.Notifications.SetModeAsync(di, NotificationMode.Every, _now, Draft(), Token);

        // Act
        var recipients = await db.Notifications.ListRecipientsAsync(Token);

        // Assert
        Assert.Equal([ann, di], recipients.Select(r => r.UserId).ToArray());
        Assert.Equal(["ann", "di"], recipients.Select(r => r.Username).ToArray());
        Assert.Equal(["ann@example.com", "di@example.com"], recipients.Select(r => r.Address).ToArray());
        Assert.Equal([NotificationMode.Daily, NotificationMode.Every], recipients.Select(r => r.Mode).ToArray());
    }

    /// <summary>
    ///     Test that the disabled flag of the user is joined.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ListRecipients_Flags_Joined()
    {
        // Arrange: Ann is normal, Bob is disabled
        using var db = new TestStore();
        var ann = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        var bob = await ConfirmedUserAsync(db, "bob", "bob@example.com");
        foreach (var id in new[] { ann, bob })
        {
            await db.Notifications.SetModeAsync(id, NotificationMode.Daily, _now, Draft(), Token);
        }

        Execute(db, $"UPDATE Users SET Enabled = 0 WHERE Id = {bob}");

        // Act
        var recipients = await db.Notifications.ListRecipientsAsync(Token);

        // Assert
        var byName = recipients.ToDictionary(r => r.Username);
        Assert.False(byName["ann"].IsDisabled);
        Assert.True(byName["bob"].IsDisabled);
    }

    /// <summary>
    ///     Test that the flag of the user who must change a temporary password is joined.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ListRecipients_MustChangePassword_Joined()
    {
        // Arrange: Ann is normal, Cy must change the password
        using var db = new TestStore();
        var ann = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        var cy = await ConfirmedUserAsync(db, "cy", "cy@example.com");
        foreach (var id in new[] { ann, cy })
        {
            await db.Notifications.SetModeAsync(id, NotificationMode.Daily, _now, Draft(), Token);
        }

        Execute(db, $"UPDATE Users SET MustChangePassword = 1 WHERE Id = {cy}");

        // Act
        var recipients = await db.Notifications.ListRecipientsAsync(Token);

        // Assert
        var byName = recipients.ToDictionary(r => r.Username);
        Assert.False(byName["ann"].MustChangePassword);
        Assert.True(byName["cy"].MustChangePassword);
    }

    /// <summary>
    ///     Test that the stored dispatch state is returned with the recipient.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ListRecipients_State_RoundTrips()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        var state = new NotificationDispatchState(false, _now.AddMinutes(-3), _now.AddHours(-9), 2, _now.AddMinutes(15));
        await ApplyCurrentAsync(db, userId, Commit(state: state));

        // Act
        var recipient = Assert.Single(await db.Notifications.ListRecipientsAsync(Token));

        // Assert
        Assert.Equal(state, recipient.State);
    }

    /// <summary>
    ///     Test that the dispatch timestamps are stored in the sortable UTC text form, also when they were given with
    ///     an offset and with a fraction of a second.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_State_TimestampsStoredAsUtcText()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        var started = new DateTimeOffset(2025, 3, 4, 7, 6, 7, 1234567 / 10_000, TimeSpan.FromHours(2));
        var handled = new DateTimeOffset(2025, 3, 3, 23, 59, 59, TimeSpan.FromHours(-5));
        var next = _now.AddTicks(1234567);

        // Act
        await ApplyCurrentAsync(
            db, userId, Commit(state: new NotificationDispatchState(false, started, handled, 1, next)));

        // Assert
        Assert.Equal("2025-03-04T05:06:07.1234567Z", ReadText(db, $"SELECT NextAttemptUtc FROM NotificationPreferences WHERE UserId = {userId}"));
        Assert.Equal("2025-03-04T05:06:07.1230000Z", ReadText(db, $"SELECT BatchStartedUtc FROM NotificationPreferences WHERE UserId = {userId}"));
    }

    /// <summary>
    ///     Test that the dispatch timestamps are read back as the instants that were stored.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_State_TimestampsRoundTrip()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        var started = new DateTimeOffset(2025, 3, 4, 7, 6, 7, 1234567 / 10_000, TimeSpan.FromHours(2));
        var handled = new DateTimeOffset(2025, 3, 3, 23, 59, 59, TimeSpan.FromHours(-5));
        var next = _now.AddTicks(1234567);

        // Act
        await ApplyCurrentAsync(
            db, userId, Commit(state: new NotificationDispatchState(false, started, handled, 1, next)));

        // Assert
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.Equal(started, row.BatchStartedUtc);
        Assert.Equal(handled, row.DigestHandledUtc);
        Assert.Equal(next, row.NextAttemptUtc);
    }

    /// <summary>
    ///     Test that a commit inserts a new item.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_Upserts_InsertsNewItem()
    {
        // Arrange: one item exists
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        await ApplyCurrentAsync(
            db, userId, Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "to-answer")]));

        // Act: insert another item
        var applied = await ApplyCurrentAsync(
            db, userId, Commit(upserts: [Entry(1, "r2", InboxSection.ToSign, "to-sign")]));

        // Assert
        Assert.True(applied);
        var items = await db.Notifications.GetNotifiedAsync(userId, Token);
        Assert.Equal(2, items.Count);
        Assert.Equal(InboxSection.ToSign, items.Single(i => i.ReviewId == "r2").Section);
    }

    /// <summary>
    ///     Test that a commit updates the state of an existing item.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_Upserts_UpdatesExistingItem()
    {
        // Arrange: one item exists
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        await ApplyCurrentAsync(
            db, userId, Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "to-answer")]));

        // Act
        var later = _now.AddHours(1);
        await ApplyCurrentAsync(
            db, userId, Commit(later, upserts: [Entry(1, "r1", InboxSection.ToAnswer, "to-answer:2")]));

        // Assert
        var first = Assert.Single(await db.Notifications.GetNotifiedAsync(userId, Token));
        Assert.Equal("to-answer:2", first.State);
        Assert.Equal(later, first.NotifiedUtc);
    }

    /// <summary>
    ///     Test that the flag of a state the server gave up mailing is stored and read back, and that a new item is
    ///     not flagged unless the commit says so.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_GaveUpFlag_RoundTrips()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);

        // Act
        await ApplyCurrentAsync(
            db,
            userId,
            Commit(
                upserts:
                [
                    Entry(1, "r1", InboxSection.ToAnswer, "to-answer", gaveUp: true),
                    Entry(1, "r2", InboxSection.ToAnswer, "to-answer")
                ]));

        // Assert
        var items = await db.Notifications.GetNotifiedAsync(userId, Token);
        Assert.True(Assert.Single(items, i => i.ReviewId == "r1").GaveUp);
        Assert.False(Assert.Single(items, i => i.ReviewId == "r2").GaveUp);
    }

    /// <summary>
    ///     Test that an upsert of an existing row that was given up clears the flag when the commit does not carry it,
    ///     and sets it when the commit does.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_GaveUpRowUpserted_FlagFollowsCommit()
    {
        // Arrange: one item that was given up
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        await ApplyCurrentAsync(
            db, userId, Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "to-answer", gaveUp: true)]));

        // Act and Assert: a mail of a new state clears the flag
        await ApplyCurrentAsync(
            db, userId, Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "to-answer:2")]));
        Assert.False(Assert.Single(await db.Notifications.GetNotifiedAsync(userId, Token)).GaveUp);

        // Act and Assert: a later give-up sets it again
        await ApplyCurrentAsync(
            db, userId, Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "to-answer:3", gaveUp: true)]));
        Assert.True(Assert.Single(await db.Notifications.GetNotifiedAsync(userId, Token)).GaveUp);
    }

    /// <summary>
    ///     Test that a database without the flag column is reported as not available.
    /// </summary>
    [Fact]
    public async Task NotificationStore_IsAvailable_NoGaveUpColumn_False()
    {
        // Arrange: a database from before the flag existed
        using var db = new TestStore();
        Execute(db, "ALTER TABLE NotifiedItems DROP COLUMN GaveUp");

        // Act
        var available = await db.Notifications.IsAvailableAsync(Token);

        // Assert
        Assert.False(available);
    }

    /// <summary>
    ///     Test that the named keys are deleted and the others stay.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_RemoveKeys_Deleted()
    {
        // Arrange: two items
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        await ApplyCurrentAsync(
            db,
            userId,
            Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "a"), Entry(1, "r2", InboxSection.ToSign, "b")]));

        // Act
        await ApplyCurrentAsync(
            db,
            userId,
            Commit(removeKeys: [Key(1, "r1", InboxSection.ToAnswer), Key(9, "none", InboxSection.Updates)]));

        // Assert
        var remaining = Assert.Single(await db.Notifications.GetNotifiedAsync(userId, Token));
        Assert.Equal("r2", remaining.ReviewId);
    }

    /// <summary>
    ///     Test that a replace-all commit leaves exactly the given set.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_ReplaceAll_ReplacesSet()
    {
        // Arrange: two items
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        await ApplyCurrentAsync(
            db,
            userId,
            Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "a"), Entry(1, "r2", InboxSection.ToSign, "b")]));

        // Act: the baseline holds one old and one new item
        await ApplyCurrentAsync(
            db,
            userId,
            Commit(replaceAll: [Entry(1, "r2", InboxSection.ToSign, "b2"), Entry(2, "r3", InboxSection.Disputed, "d")]));

        // Assert
        var items = await db.Notifications.GetNotifiedAsync(userId, Token);
        Assert.Equal(["r2", "r3"], items.Select(i => i.ReviewId).Order().ToArray());
        Assert.Equal("b2", items.Single(i => i.ReviewId == "r2").State);
    }

    /// <summary>
    ///     Test that a commit writes no send-log row: the mail was counted when it was reserved.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_Commit_WritesNoLogRow()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        var before = await CountLogAsync(db);

        // Act
        var applied = await ApplyCurrentAsync(
            db, userId, Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "a")]));

        // Assert
        Assert.True(applied);
        Assert.Equal(before, await CountLogAsync(db));
    }

    /// <summary>
    ///     Test that a failure while writing the items rolls back the state as well.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_Failure_NothingPersisted()
    {
        // Arrange: a trigger refuses every insert of a notified item
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        Execute(db, "CREATE TRIGGER refuse_item BEFORE INSERT ON NotifiedItems BEGIN SELECT RAISE(ABORT, 'item refused'); END;");
        var commit = Commit(
            upserts: [Entry(1, "r1", InboxSection.ToAnswer, "a")],
            state: new NotificationDispatchState(false, null, _now, 4, _now.AddHours(1)));

        // Act
        await Assert.ThrowsAnyAsync<Exception>(() => ApplyCurrentAsync(db, userId, commit));

        // Assert: neither the item, nor the state
        Execute(db, "DROP TRIGGER refuse_item");
        Assert.Empty(await db.Notifications.GetNotifiedAsync(userId, Token));
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.Equal(0, row.FailureCount);
        Assert.Null(row.DigestHandledUtc);
    }

    /// <summary>
    ///     Test that a commit for a user whose mode is None is refused and reported as not applied.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_ModeNone_ReturnsFalse()
    {
        // Arrange: a confirmed user with the mode None
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");

        // Act
        var applied = await ApplyCurrentAsync(
            db, userId, Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "a")]));

        // Assert
        Assert.False(applied);
    }

    /// <summary>
    ///     Test that a commit for a user whose mode is None writes no item.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_ModeNone_WritesNothing()
    {
        // Arrange: a confirmed user with the mode None
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        var before = await CountLogAsync(db);

        // Act
        await ApplyCurrentAsync(
            db,
            userId,
            Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "a")]));

        // Assert
        Assert.Empty(await db.Notifications.GetNotifiedAsync(userId, Token));
        Assert.Equal(before, await CountLogAsync(db));
    }

    /// <summary>
    ///     Test that a commit for a user without a preference row returns false.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_NoRow_ReturnsFalse()
    {
        // Arrange: a user without a row
        using var db = new TestStore();
        var userId = await AddUserAsync(db, "ann");

        // Act
        var applied = await ApplyCurrentAsync(
            db, userId, Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "a")]));

        // Assert
        Assert.False(applied);
        Assert.Empty(await db.Notifications.GetNotifiedAsync(userId, Token));
    }

    /// <summary>
    ///     Test that a commit made for an earlier generation of the preferences changes nothing, though the mode is
    ///     the same again (the mode was switched off and on while the pass was sending).
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_StaleGeneration_WritesNothing()
    {
        // Arrange: a commit prepared in the mode Every, then the mode flips Every -> None -> Every
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        var prepared = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(prepared);
        await db.Notifications.SetModeAsync(userId, NotificationMode.None, _now, Draft(), Token);
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        var before = await CountLogAsync(db);

        // Act
        var applied = await db.Notifications.ApplyAsync(
            userId,
            Commit(
                upserts: [Entry(1, "r1", InboxSection.ToAnswer, "a")],
                state: new NotificationDispatchState(false, null, _now, 4, _now.AddHours(1)))
            with
            {
                Address = prepared.Email!,
                Generation = prepared.Generation
            },
            Token);

        // Assert: nothing was written
        Assert.False(applied);
        Assert.Empty(await db.Notifications.GetNotifiedAsync(userId, Token));
        Assert.Equal(before, await CountLogAsync(db));
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(row);
        Assert.Equal(0, row.FailureCount);
        Assert.Null(row.DigestHandledUtc);
    }

    /// <summary>
    ///     Test that a commit made for another confirmed address changes nothing.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_ChangedAddress_WritesNothing()
    {
        // Arrange: the current generation, but the commit was prepared for the address that was replaced
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        var current = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(current);
        var before = await CountLogAsync(db);

        // Act
        var applied = await db.Notifications.ApplyAsync(
            userId,
            Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "a")])
            with
            {
                Address = "old@example.com",
                Generation = current.Generation
            },
            Token);

        // Assert: nothing was written
        Assert.False(applied);
        Assert.Empty(await db.Notifications.GetNotifiedAsync(userId, Token));
        Assert.Equal(before, await CountLogAsync(db));
    }

    /// <summary>
    ///     Test that a commit made for the current address and generation is applied.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_CurrentAddressAndGeneration_Applied()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        var current = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(current);

        // Act
        var applied = await db.Notifications.ApplyAsync(
            userId,
            Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "a")]) with
            {
                Address = "ann@example.com",
                Generation = current.Generation
            },
            Token);

        // Assert
        Assert.True(applied);
        Assert.Single(await db.Notifications.GetNotifiedAsync(userId, Token));
    }

    /// <summary>
    ///     Test that a change of the mode raises the generation of the preferences.
    /// </summary>
    [Fact]
    public async Task NotificationStore_SetMode_Changed_RaisesGeneration()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        var before = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(before);

        // Act
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);

        // Assert
        var after = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(after);
        Assert.Equal(before.Generation + 1, after.Generation);
    }

    /// <summary>
    ///     Test that confirming an address raises the generation of the preferences.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Confirm_Address_RaisesGeneration()
    {
        // Arrange: a confirmed address and a replacement that waits for its code
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        var before = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(before);
        await ReserveAsync(db, userId, "new@example.com", 'b');

        // Act
        Assert.True(await db.Notifications.ConfirmAsync(userId, Hash('b'), _now, Draft("setup"), Token));

        // Assert
        var after = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(after);
        Assert.Equal("new@example.com", after.Email);
        Assert.Equal(before.Generation + 1, after.Generation);
    }

    /// <summary>
    ///     Test that the listed recipients carry the generation of their preferences.
    /// </summary>
    [Fact]
    public async Task NotificationStore_ListRecipients_Generation_Joined()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        var row = await db.Notifications.GetAsync(userId, Token);
        Assert.NotNull(row);

        // Act
        var recipients = await db.Notifications.ListRecipientsAsync(Token);

        // Assert
        Assert.Equal(row.Generation, Assert.Single(recipients).Generation);
    }

    /// <summary>
    ///     Test that the items of one section with different kinds are kept side by side.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_SameSectionOtherKind_TwoRows()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);

        // Act: the standing item and the question of the same review
        await ApplyCurrentAsync(
            db,
            userId,
            Commit(
                upserts:
                [
                    Entry(1, "r1", InboxSection.ToAnswer, "to-answer"),
                    Entry(1, "r1", InboxSection.ToAnswer, "question", "question")
                ]));

        // Assert
        var items = await db.Notifications.GetNotifiedAsync(userId, Token);
        Assert.Equal(["question", "to-answer"], items.Select(i => i.Kind).Order().ToArray());
    }

    /// <summary>
    ///     Test that removing one kind keeps the other kind of the same section.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_RemoveOneKind_KeepsOtherKind()
    {
        // Arrange: both items of the section
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        await ApplyCurrentAsync(
            db,
            userId,
            Commit(
                upserts:
                [
                    Entry(1, "r1", InboxSection.ToAnswer, "to-answer"),
                    Entry(1, "r1", InboxSection.ToAnswer, "question", "question")
                ]));

        // Act
        await ApplyCurrentAsync(db, userId, Commit(removeKeys: [Key(1, "r1", InboxSection.ToAnswer, "question")]));

        // Assert
        var item = Assert.Single(await db.Notifications.GetNotifiedAsync(userId, Token));
        Assert.Equal("to-answer", item.Kind);
    }

    /// <summary>
    ///     Test that deleting a review deletes the notified items of that review (cascade).
    /// </summary>
    [Fact]
    public async Task NotificationStore_Delete_Review_CascadesNotifiedItems()
    {
        // Arrange: items for two reviews
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        await ApplyCurrentAsync(
            db,
            userId,
            Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "a"), Entry(1, "r2", InboxSection.ToSign, "b")]));

        // Act: delete one review row directly
        await using (var context = await db.Factory.CreateDbContextAsync(Token))
        {
            await context.Reviews.Where(review => review.Id == "r1").ExecuteDeleteAsync(Token);
        }

        // Assert: only the item of the other review is left
        var left = Assert.Single(await db.Notifications.GetNotifiedAsync(userId, Token));
        Assert.Equal("r2", left.ReviewId);
    }

    /// <summary>
    ///     Test that a review that is deleted and created again under the same id has no notified items, so the
    ///     first mail about it is not suppressed.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Delete_ReviewRecreated_HasNoNotifiedItems()
    {
        // Arrange: an item for r1
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        await ApplyCurrentAsync(db, userId, Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "a")]));

        // Act: delete the review and create one with the same id
        await using (var context = await db.Factory.CreateDbContextAsync(Token))
        {
            await context.Reviews.Where(review => review.Id == "r1").ExecuteDeleteAsync(Token);
            context.Reviews.Add(new Review
            {
                ProjectId = 1,
                Id = "r1",
                Title = "Again",
                Created = _now,
                ContentFingerprint = Fingerprint.Compute(Array.Empty<byte[]>())
            });
            await context.SaveChangesAsync(Token);
        }

        // Assert: nothing is remembered about the new review
        Assert.Empty(await db.Notifications.GetNotifiedAsync(userId, Token));
    }

    /// <summary>
    ///     Test that a database without the generation column is reported as not available.
    /// </summary>
    [Fact]
    public async Task NotificationStore_IsAvailable_NoGenerationColumn_False()
    {
        // Arrange: a database from before the generation existed
        using var db = new TestStore();
        Execute(db, "ALTER TABLE NotificationPreferences DROP COLUMN Generation");

        // Act
        var available = await db.Notifications.IsAvailableAsync(Token);

        // Assert
        Assert.False(available);
    }

    /// <summary>
    ///     Test that the same user, project, review and section keep one row, whichever way it is written.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_SameKeyTwice_OneRow()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);

        // Act
        await ApplyCurrentAsync(db, userId, Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "a")]));
        await ApplyCurrentAsync(db, userId, Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "b")]));

        // Assert
        var item = Assert.Single(await db.Notifications.GetNotifiedAsync(userId, Token));
        Assert.Equal("b", item.State);
    }

    /// <summary>
    ///     Test that the database itself refuses a second row for the same key, and accepts a row for another section
    ///     and a row of another kind in the same section.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Create_Database_NotifiedItemsKeyUnique()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        const string insert =
            "INSERT INTO NotifiedItems (UserId, ProjectId, ReviewId, Section, Kind, State, NotifiedUtc) VALUES ({0}, 1, 'r1', 'ToAnswer', 'to-answer', 'a', '2025-03-04T05:06:07.0000000Z')";
        Execute(db, string.Format(System.Globalization.CultureInfo.InvariantCulture, insert, userId));

        // Act and Assert: a duplicate is refused; another section and another kind are accepted
        Assert.ThrowsAny<SqliteException>(
            () => Execute(db, string.Format(System.Globalization.CultureInfo.InvariantCulture, insert, userId)));
        Execute(
            db,
            string.Format(System.Globalization.CultureInfo.InvariantCulture, insert, userId).Replace("'ToAnswer'", "'ToSign'", StringComparison.Ordinal));
        Execute(
            db,
            string.Format(System.Globalization.CultureInfo.InvariantCulture, insert, userId).Replace("'to-answer'", "'question'", StringComparison.Ordinal));
        var items = await db.Notifications.GetNotifiedAsync(userId, Token);
        Assert.Equal(3, items.Count);

        // A row inserted without the flag is not given up
        Assert.All(items, item => Assert.False(item.GaveUp));
    }

    /// <summary>
    ///     Test that a new database has the table of notified items.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Create_Database_HasNotifiedItems()
    {
        // Arrange and Act: a new database
        using var db = new TestStore();
        await using var context = await db.Factory.CreateDbContextAsync(Token);

        // Assert: the table and its columns can be queried
        Assert.Empty(await context.NotifiedItems.ToListAsync(Token));
        Assert.Equal("1", ReadText(db, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'NotifiedItems'"));
    }

    /// <summary>
    ///     Test that only the items of the asked user are returned.
    /// </summary>
    [Fact]
    public async Task NotificationStore_GetNotified_ReturnsOnlyOwnItems()
    {
        // Arrange: two users with the same review
        using var db = new TestStore();
        var ann = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        var bob = await ConfirmedUserAsync(db, "bob", "bob@example.com");
        foreach (var id in new[] { ann, bob })
        {
            await db.Notifications.SetModeAsync(id, NotificationMode.Every, _now, Draft(), Token);
        }

        await ApplyCurrentAsync(db, ann, Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "a")]));
        await ApplyCurrentAsync(db, bob, Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "b")]));

        // Act
        var items = await db.Notifications.GetNotifiedAsync(ann, Token);

        // Assert
        var item = Assert.Single(items);
        Assert.Equal(ann, item.UserId);
        Assert.Equal("a", item.State);
    }

    /// <summary>
    ///     Test that the notified items go away with the user.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Delete_User_CascadesNotifiedItems()
    {
        // Arrange: Ann and Bob each have an item
        using var db = new TestStore();
        var ann = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        var bob = await ConfirmedUserAsync(db, "bob", "bob@example.com");
        foreach (var id in new[] { ann, bob })
        {
            await db.Notifications.SetModeAsync(id, NotificationMode.Every, _now, Draft(), Token);
            await ApplyCurrentAsync(db, id, Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "a")]));
        }

        // Act
        await using (var context = await db.Factory.CreateDbContextAsync(Token))
        {
            await context.Users.Where(u => u.Id == ann).ExecuteDeleteAsync(Token);
        }

        // Assert
        Assert.Empty(await db.Notifications.GetNotifiedAsync(ann, Token));
        Assert.Single(await db.Notifications.GetNotifiedAsync(bob, Token));
    }

    /// <summary>
    ///     Test that removing the address deletes the notified items.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Remove_DeletesNotified()
    {
        // Arrange
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        await ApplyCurrentAsync(db, userId, Commit(upserts: [Entry(1, "r1", InboxSection.ToAnswer, "a")]));

        // Act
        var removed = await db.Notifications.RemoveAsync(userId, Draft(), Token);

        // Assert
        Assert.True(removed);
        Assert.Empty(await db.Notifications.GetNotifiedAsync(userId, Token));
        Assert.Null(await db.Notifications.GetAsync(userId, Token));
    }

    /// <summary>
    ///     Test that the mails of one user since an instant are counted.
    /// </summary>
    [Fact]
    public async Task NotificationStore_CountSent_User_CountsOwn()
    {
        // Arrange: Ann has two mails, one before the instant; Bob has one
        using var db = new TestStore();
        var ann = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        var bob = await ConfirmedUserAsync(db, "bob", "bob@example.com");
        foreach (var id in new[] { ann, bob })
        {
            await db.Notifications.SetModeAsync(id, NotificationMode.Every, _now, Draft(), Token);
        }

        var before = await db.Notifications.CountSentAsync(ann, _now.AddHours(-1), Token);
        await AddLogAsync(db, ann, _now.AddHours(-2));
        await AddLogAsync(db, ann, _now.AddMinutes(-10));
        await AddLogAsync(db, bob, _now.AddMinutes(-5));

        // Act
        var count = await db.Notifications.CountSentAsync(ann, _now.AddHours(-1), Token);

        // Assert: one more than before, because the old mail and the mail of Bob do not count
        Assert.Equal(before + 1, count);
    }

    /// <summary>
    ///     Test that the mails of all users since an instant are counted.
    /// </summary>
    [Fact]
    public async Task NotificationStore_CountSent_All_CountsEverything()
    {
        // Arrange: as above, counted for the whole server
        using var db = new TestStore();
        var ann = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        var bob = await ConfirmedUserAsync(db, "bob", "bob@example.com");
        foreach (var id in new[] { ann, bob })
        {
            await db.Notifications.SetModeAsync(id, NotificationMode.Every, _now, Draft(), Token);
        }

        var before = await db.Notifications.CountSentAsync(null, _now.AddHours(-1), Token);
        await AddLogAsync(db, ann, _now.AddHours(-2));
        await AddLogAsync(db, ann, _now.AddMinutes(-10));
        await AddLogAsync(db, bob, _now.AddMinutes(-5));

        // Act
        var count = await db.Notifications.CountSentAsync(null, _now.AddHours(-1), Token);

        // Assert: the two recent mails, not the old one
        Assert.Equal(before + 2, count);
    }

    /// <summary>
    ///     Test that recording a mail deletes send-log rows older than one day.
    /// </summary>
    [Fact]
    public async Task NotificationStore_Apply_OldLogRows_Pruned()
    {
        // Arrange: an old row (25 hours before) and a recent one (23 hours before)
        using var db = new TestStore();
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        await AddLogAsync(db, userId, _now.AddHours(-25));
        await AddLogAsync(db, userId, _now.AddHours(-23));
        var before = await CountLogAsync(db);

        // Act
        await ApplyCurrentAsync(db, userId, Commit());

        // Assert: one row went, none came
        Assert.Equal(before - 1, await CountLogAsync(db));
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        Assert.DoesNotContain(await context.NotificationSendLog.ToListAsync(Token), e => e.SentUtc == _now.AddHours(-25));
    }

    /// <summary>
    ///     Test that a database without the table of notified items is reported as not available.
    /// </summary>
    [Fact]
    public async Task NotificationStore_IsAvailable_NoNotifiedItems_False()
    {
        // Arrange: a database from before the dispatcher existed
        using var db = new TestStore();
        Execute(db, "DROP TABLE NotifiedItems");

        // Act
        var available = await db.Notifications.IsAvailableAsync(Token);

        // Assert
        Assert.False(available);
    }

    /// <summary>
    ///     Test that a database without the mode column is reported as not available.
    /// </summary>
    [Fact]
    public async Task NotificationStore_IsAvailable_NoModeColumn_False()
    {
        // Arrange: a database from before the dispatcher existed
        using var db = new TestStore();
        Execute(db, "ALTER TABLE NotificationPreferences DROP COLUMN Mode");

        // Act
        var available = await db.Notifications.IsAvailableAsync(Token);

        // Assert
        Assert.False(available);
    }

    /// <summary>
    ///     Builds a commit that changes nothing unless the arguments say so.
    /// </summary>
    /// <param name="nowUtc">The time of the pass; the test time when omitted.</param>
    /// <param name="upserts">The items to insert or update.</param>
    /// <param name="removeKeys">The items to forget.</param>
    /// <param name="replaceAll">The baseline set.</param>
    /// <param name="state">The new dispatch state; the empty state when omitted.</param>
    /// <returns>The commit.</returns>
    private static NotificationCommit Commit(
        DateTimeOffset? nowUtc = null,
        NotifiedEntry[]? upserts = null,
        NotifiedKey[]? removeKeys = null,
        NotifiedEntry[]? replaceAll = null,
        NotificationDispatchState? state = null)
    {
        return new NotificationCommit(
            nowUtc ?? _now,
            upserts ?? [],
            removeKeys ?? [],
            replaceAll,
            state ?? NotificationDispatchState.Empty,
            string.Empty,
            0);
    }

    /// <summary>
    ///     Adds a user with a confirmed address and the dispatch state of an earlier use of a mode, as a row written
    ///     by a previous version would hold it.
    /// </summary>
    /// <param name="db">The test store.</param>
    /// <returns>The identity of the user.</returns>
    private static async Task<int> ConfirmedUserWithLeftoverStateAsync(TestStore db)
    {
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        Execute(
            db,
            $"UPDATE NotificationPreferences SET BatchStartedUtc = '2025-03-04T05:00:00.0000000Z', FailureCount = 3, NextAttemptUtc = '2025-03-04T09:00:00.0000000Z' WHERE UserId = {userId}");
        return userId;
    }

    /// <summary>
    ///     Adds a user in the mode Every with one notified item and a handled daily slot.
    /// </summary>
    /// <param name="db">The test store.</param>
    /// <returns>The identity of the user.</returns>
    private static async Task<int> ArrangeEveryWithItemAndHandledSlotAsync(TestStore db)
    {
        var userId = await ConfirmedUserAsync(db, "ann", "ann@example.com");
        await db.Notifications.SetModeAsync(userId, NotificationMode.Every, _now, Draft(), Token);
        await ApplyCurrentAsync(
            db,
            userId,
            Commit(
                upserts: [Entry(1, "r1", InboxSection.ToAnswer, "to-answer")],
                state: new NotificationDispatchState(false, null, _now, 0, null)));
        return userId;
    }

    /// <summary>
    ///     Builds a notified entry.
    /// </summary>
    /// <param name="projectId">The project.</param>
    /// <param name="reviewId">The review.</param>
    /// <param name="section">The section.</param>
    /// <param name="state">The state token.</param>
    /// <param name="kind">The kind of the item.</param>
    /// <param name="gaveUp">Whether the entry records a state the server gave up mailing.</param>
    /// <returns>The entry.</returns>
    private static NotifiedEntry Entry(
        int projectId,
        string reviewId,
        InboxSection section,
        string state,
        string kind = "to-answer",
        bool gaveUp = false)
    {
        return new NotifiedEntry(Key(projectId, reviewId, section, kind), state, gaveUp);
    }

    /// <summary>
    ///     Builds the key of a notified item.
    /// </summary>
    /// <param name="projectId">The project.</param>
    /// <param name="reviewId">The review.</param>
    /// <param name="section">The section.</param>
    /// <param name="kind">The kind of the item.</param>
    /// <returns>The key.</returns>
    private static NotifiedKey Key(int projectId, string reviewId, InboxSection section, string kind = "to-answer")
    {
        return new NotifiedKey(projectId, reviewId, section, kind);
    }

    /// <summary>
    ///     Applies a commit as a dispatcher would that has just read the current address and generation of the user.
    /// </summary>
    /// <param name="db">The test store.</param>
    /// <param name="userId">The user.</param>
    /// <param name="commit">The commit; its address and generation are replaced by the current ones.</param>
    /// <returns><c>true</c> when the commit was applied.</returns>
    private static async Task<bool> ApplyCurrentAsync(TestStore db, int userId, NotificationCommit commit)
    {
        var row = await db.Notifications.GetAsync(userId, Token);
        return await db.Notifications.ApplyAsync(
            userId,
            commit with { Address = row?.Email ?? string.Empty, Generation = row?.Generation ?? 0 },
            Token);
    }

    /// <summary>
    ///     Creates the projects and reviews the notified items of the tests refer to, when they do not exist yet.
    /// </summary>
    /// <param name="db">The test store.</param>
    /// <returns>A task that completes when the reviews exist.</returns>
    private static async Task EnsureReviewsAsync(TestStore db)
    {
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        if (await context.Projects.AnyAsync(Token))
        {
            return;
        }

        context.Projects.Add(new Project { Id = 1, Name = "One", Created = _now });
        context.Projects.Add(new Project { Id = 2, Name = "Two", Created = _now });
        await context.SaveChangesAsync(Token);
        foreach (var (projectId, reviewId) in new[] { (1, "r1"), (1, "r2"), (2, "r3") })
        {
            context.Reviews.Add(new Review
            {
                ProjectId = projectId,
                Id = reviewId,
                Title = "Title " + reviewId,
                Created = _now,
                ContentFingerprint = Fingerprint.Compute(Array.Empty<byte[]>())
            });
        }

        await context.SaveChangesAsync(Token);
    }

    /// <summary>
    ///     Creates a user with a confirmed address (and the mode None).
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="username">The user name.</param>
    /// <param name="address">The address.</param>
    /// <returns>The identity of the user.</returns>
    private static async Task<int> ConfirmedUserAsync(TestStore db, string username, string address)
    {
        var userId = await AddUserAsync(db, username);
        await EnsureReviewsAsync(db);
        await ReserveAsync(db, userId, address, 'a');
        Assert.True(await db.Notifications.ConfirmAsync(userId, Hash('a'), _now, Draft("setup"), Token));
        return userId;
    }

    /// <summary>
    ///     Reserves a confirmation for an address.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="userId">The user.</param>
    /// <param name="address">The address.</param>
    /// <param name="hashStart">The first character of the hash.</param>
    /// <returns>A task that completes when the address waits.</returns>
    private static async Task ReserveAsync(TestStore db, int userId, string address, char hashStart)
    {
        var request = new ConfirmationRequest(
            userId,
            address,
            Hash(hashStart),
            "salt",
            _now,
            _now.AddMinutes(30),
            TimeSpan.FromSeconds(60),
            3,
            3,
            200);
        var (outcome, _) = await db.Notifications.ReserveConfirmationAsync(request, Draft("setup"), Token);
        Assert.Equal(ConfirmationReservation.Reserved, outcome);
    }

    /// <summary>
    ///     Adds a row to the send log.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="userId">The user.</param>
    /// <param name="sentUtc">When the mail was sent.</param>
    /// <returns>A task that completes when the row is stored.</returns>
    private static async Task AddLogAsync(TestStore db, int userId, DateTimeOffset sentUtc)
    {
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        context.NotificationSendLog.Add(new NotificationSendLogEntry
        {
            UserId = userId,
            ToAddress = "x@example.com",
            Kind = NotificationSendLogEntry.KindUpdate,
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
    ///     Counts the audit entries of the settings change.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <returns>The number of entries.</returns>
    private static async Task<int> CountAuditAsync(TestStore db)
    {
        await using var context = await db.Factory.CreateDbContextAsync(Token);
        return await context.AuditEntries.CountAsync(
            a => a.Action == "notification.settings-changed" && a.Detail == "mode",
            Token);
    }

    /// <summary>
    ///     A hash of the shape the service stores (64 lowercase hex characters).
    /// </summary>
    /// <param name="start">The first character.</param>
    /// <returns>The hash.</returns>
    private static string Hash(char start) => start + new string('0', 63);

    /// <summary>
    ///     Builds an audit draft for a settings change by the test user.
    /// </summary>
    /// <param name="detail">The detail text of the entry.</param>
    /// <returns>The draft.</returns>
    private static AuditDraft Draft(string detail = "mode")
    {
        return AuditDraft.By(new Actor("ann", "Ann"), "notification.settings-changed", detail);
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

    /// <summary>
    ///     Runs a statement that changes the database, outside the store.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="sql">The statement.</param>
    private static void Execute(TestStore db, string sql)
    {
        using var connection = new SqliteConnection(db.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    ///     Reads the first column of the first row of a query as text.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="sql">The query.</param>
    /// <returns>The text.</returns>
    private static string ReadText(TestStore db, string sql)
    {
        using var connection = new SqliteConnection(db.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
