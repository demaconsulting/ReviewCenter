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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="NotificationDispatcher"/>: an item is mailed once per state, and recorded as
///     notified only after the mail was sent.
/// </summary>
public class NotificationDispatcherDedupeTests
{
    /// <summary>
    ///     The cancellation token of the running test.
    /// </summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Test that the same item in the same state is not mailed twice.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_SameItemSameState_NotMailedTwice()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        await world.DeliverEveryAsync();
        await world.ChangeAsync();

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        Assert.Single(sent);
    }

    /// <summary>
    ///     Test that a mail is sent when the state of an item changes.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_StateChanged_Mailed()
    {
        // Arrange: ann was told that r1 asks a question; now it asks for her answers
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        world.Store.AddNotified(world.IdOf("ann"), new NotifiedKey(world.Inbox.ProjectId, "r1", InboxSection.ToAnswer, "question"), NotificationComposer.StateQuestion);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        Assert.Single(sent);
        Assert.Equal(["r1/ToAnswer=to-answer"], world.NotifiedOf("ann"));
    }

    /// <summary>
    ///     Test that a question asked after the item to answer was mailed is mailed as well.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_QuestionAfterToAnswerNotified_Mailed()
    {
        // Arrange: ann holds a seat and was mailed that she has answers to give
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        var first = await world.DeliverEveryAsync();
        Assert.Single(first);
        Assert.DoesNotContain("a question is waiting for you", first[0].TextBody, StringComparison.Ordinal);

        // Act: bob asks a question, so ann's inbox holds the standing row and the "Question for you" row
        await world.Inbox.StartThreadAsync("r1", "bob", "Why is this here?");
        await world.ChangeAsync();
        var all = await world.DeliverEveryAsync();

        // Assert: a second mail tells her about the question, and both states are remembered
        Assert.Equal(2, all.Count);
        Assert.Contains("a question is waiting for you", all[1].TextBody, StringComparison.Ordinal);
        Assert.Equal(["r1/ToAnswer=question", "r1/ToAnswer=to-answer"], world.NotifiedOf("ann"));
    }

    /// <summary>
    ///     Test that the question and the item to answer, once both were mailed, are not mailed again.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_QuestionAndToAnswerNotified_NotMailedAgain()
    {
        // Arrange: both rows exist and were mailed
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        await world.Inbox.StartThreadAsync("r1", "bob", "Why is this here?");
        await world.ChangeAsync();
        world.Enroll("ann", NotificationMode.Every);
        var first = await world.DeliverEveryAsync();
        Assert.Single(first);
        await world.ChangeAsync();

        // Act: a later pass with an unchanged inbox
        var all = await world.DeliverEveryAsync();

        // Assert
        Assert.Single(all);
    }

    /// <summary>
    ///     Test that "Not started" and "Draft" are the same state, so starting an answer is no news.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_NotStartedThenDraft_NotNew()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        await world.DeliverEveryAsync();
        await world.Inbox.StartBallotAsync("r1", "ann");
        await world.ChangeAsync();
        Assert.Equal(InboxService.DraftChip, Assert.Single(await world.Inbox.RowsAsync("ann", InboxSection.ToAnswer)).Chip);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        Assert.Single(sent);
    }

    /// <summary>
    ///     Test that a mail is sent when the number of waiting replies rises.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_RepliesRise_Mailed()
    {
        // Arrange: ann was told about one reply; now bob answered two threads of hers
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.Inbox.CreateReviewAsync("r1");
        await world.Inbox.AddSlotAsync("r1", "ann");
        var first = await world.Inbox.StartThreadAsync("r1", "ann");
        var second = await world.Inbox.StartThreadAsync("r1", "ann");
        await world.Inbox.ReplyAsync("r1", first.Id, "bob");
        await world.Inbox.ReplyAsync("r1", second.Id, "bob");
        await world.ChangeAsync();
        Assert.Equal("2 waiting", Assert.Single(await world.Inbox.RowsAsync("ann", InboxSection.RepliesWaiting)).Chip);
        world.Enroll("ann", NotificationMode.Every);
        world.Store.AddNotified(world.IdOf("ann"), new NotifiedKey(world.Inbox.ProjectId, "r1", InboxSection.RepliesWaiting, "replies"), "replies:1");
        world.Store.AddNotified(world.IdOf("ann"), new NotifiedKey(world.Inbox.ProjectId, "r1", InboxSection.ToAnswer, "to-answer"), NotificationComposer.StateToAnswer);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        var message = Assert.Single(sent);
        Assert.Contains("2 replies are waiting", message.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Makes a world in which ann has a seat in r1 and some replies of bob waiting, and was told about a number of
    ///     replies and about the item to answer.
    /// </summary>
    /// <param name="mode">The mode of ann.</param>
    /// <param name="waiting">How many replies wait now.</param>
    /// <param name="stored">The state token of the waiting replies that ann was told about.</param>
    /// <returns>The world.</returns>
    internal static async Task<NotificationDispatcherWorld> ArrangeRepliesAsync(NotificationMode mode, int waiting, string stored)
    {
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.Inbox.CreateReviewAsync("r1");
        await world.Inbox.AddSlotAsync("r1", "ann");
        for (var i = 0; i < waiting; i++)
        {
            await AddReplyAsync(world);
        }

        await world.ChangeAsync();
        world.Enroll("ann", mode);
        world.Store.AddNotified(world.IdOf("ann"), new NotifiedKey(world.Inbox.ProjectId, "r1", InboxSection.ToAnswer, "to-answer"), NotificationComposer.StateToAnswer);
        world.Store.AddNotified(world.IdOf("ann"), RepliesKey(world), stored);
        return world;
    }

    /// <summary>
    ///     Lets bob answer one more thread of ann, so one more reply waits for her, and moves the audit sequence.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <returns>A task that completes when the reply exists.</returns>
    internal static async Task AddReplyAsync(NotificationDispatcherWorld world)
    {
        var thread = await world.Inbox.StartThreadAsync("r1", "ann");
        await world.Inbox.ReplyAsync("r1", thread.Id, "bob");
        await world.ChangeAsync();
    }

    /// <summary>
    ///     Gets the key of the waiting replies of ann in r1.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <returns>The key.</returns>
    internal static NotifiedKey RepliesKey(NotificationDispatcherWorld world)
    {
        return new NotifiedKey(world.Inbox.ProjectId, "r1", InboxSection.RepliesWaiting, "replies");
    }

    /// <summary>
    ///     Test that no mail is sent when the number of waiting replies falls.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_RepliesFall_NotMailed()
    {
        // Arrange: ann was told about three replies; one is waiting now
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.Inbox.CreateReviewAsync("r1");
        await world.Inbox.AddSlotAsync("r1", "ann");
        var thread = await world.Inbox.StartThreadAsync("r1", "ann");
        await world.Inbox.ReplyAsync("r1", thread.Id, "bob");
        await world.ChangeAsync();
        world.Enroll("ann", NotificationMode.Every);
        world.Store.AddNotified(world.IdOf("ann"), new NotifiedKey(world.Inbox.ProjectId, "r1", InboxSection.RepliesWaiting, "replies"), "replies:3");
        world.Store.AddNotified(world.IdOf("ann"), new NotifiedKey(world.Inbox.ProjectId, "r1", InboxSection.ToAnswer, "to-answer"), NotificationComposer.StateToAnswer);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        Assert.Empty(sent);
    }

    /// <summary>
    ///     Test that the stored count of waiting replies follows a fall, without a mail.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Every_RepliesFall_StoredStateLowered()
    {
        // Arrange: ann was told about 5 replies; 2 wait now
        var world = await ArrangeRepliesAsync(NotificationMode.Every, 2, "replies:5");

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        Assert.Empty(sent);
        Assert.Contains("r1/RepliesWaiting=replies:2", world.NotifiedOf("ann"));
    }

    /// <summary>
    ///     Test that a rise after a fall is mailed, although it does not reach the count ann was told about first.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Every_RepliesFallThenRise_Mailed()
    {
        // Arrange: ann was told about 5 replies; 2 wait, which is remembered without a mail
        var world = await ArrangeRepliesAsync(NotificationMode.Every, 2, "replies:5");
        await world.DeliverEveryAsync();

        // Act: a third reply arrives
        await AddReplyAsync(world);
        var sent = await world.DeliverEveryAsync();

        // Assert
        var message = Assert.Single(sent);
        Assert.Contains("3 replies are waiting", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("r1/RepliesWaiting=replies:3", world.NotifiedOf("ann"));
    }

    /// <summary>
    ///     Test that the row of waiting replies is removed when no reply waits any more.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Every_RepliesFallToZero_RowRemoved()
    {
        // Arrange: ann was told about 5 replies; none waits now
        var world = await ArrangeRepliesAsync(NotificationMode.Every, 0, "replies:5");

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        Assert.Empty(sent);
        Assert.Equal(["r1/ToAnswer=to-answer"], world.NotifiedOf("ann"));
    }

    /// <summary>
    ///     Test that a count that is lowered once is not written again by the next passes.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Every_RepliesLowered_NoRewriteLater()
    {
        // Arrange: the count was lowered at the first time
        var world = await ArrangeRepliesAsync(NotificationMode.Every, 2, "replies:5");
        await world.DeliverEveryAsync();
        var first = Assert.Single(world.Store.NotifiedOf(world.IdOf("ann")), i => i.Section == InboxSection.RepliesWaiting);

        // Act: later passes read the inbox again
        world.Advance(TimeSpan.FromMinutes(30));
        await world.ChangeAsync();
        await world.DeliverEveryAsync();

        // Assert: the time of the record did not move
        var later = Assert.Single(world.Store.NotifiedOf(world.IdOf("ann")), i => i.Section == InboxSection.RepliesWaiting);
        Assert.Equal(first.NotifiedUtc, later.NotifiedUtc);
    }

    /// <summary>
    ///     Test that lowering the count of waiting replies keeps the flag of a state that was given up.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Every_RepliesFall_GaveUpFlagKept()
    {
        // Arrange: the server gave up mailing 5 replies; 2 wait now
        var world = await ArrangeRepliesAsync(NotificationMode.Every, 2, "replies:5");
        world.Store.AddNotified(world.IdOf("ann"), RepliesKey(world), "replies:5", gaveUp: true);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        Assert.Empty(sent);
        var row = Assert.Single(world.Store.NotifiedOf(world.IdOf("ann")), i => i.Section == InboxSection.RepliesWaiting);
        Assert.Equal("replies:2", row.State);
        Assert.True(row.GaveUp);
    }

    /// <summary>
    ///     Test that an item that left the inbox and came back is mailed again.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_ItemLeavesAndReturns_Mailed()
    {
        // Arrange: r1 is mailed, then ann's seat is removed and given back
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        await world.DeliverEveryAsync();
        await NotificationDispatcherEveryTests.RemoveSeatAsync(world, "r1");
        await world.PassAsync();
        await world.Inbox.AddSlotAsync("r1", "ann");
        await world.ChangeAsync();

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        Assert.Equal(2, sent.Count);
    }

    /// <summary>
    ///     Test that the key of an item that left the inbox is forgotten.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_ItemLeaves_KeyRemoved()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        await world.DeliverEveryAsync();
        Assert.Single(world.NotifiedOf("ann"));
        await NotificationDispatcherEveryTests.RemoveSeatAsync(world, "r1");

        // Act
        await world.PassAsync();

        // Assert
        Assert.Empty(world.NotifiedOf("ann"));
    }

    /// <summary>
    ///     Test that a review that was concluded and is reopened is mailed again, because its state changed.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_ConcludedThenReopened_BothMailed()
    {
        // Arrange: ann is told that r1 was concluded
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.ApproveAsync("r1");
        world.Enroll("ann", NotificationMode.Every);
        await world.DeliverEveryAsync();
        await world.ReopenAsync("r1");

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        Assert.Equal(2, sent.Count);
        Assert.Contains("was concluded", sent[0].TextBody, StringComparison.Ordinal);
        Assert.Contains("was reopened", sent[1].TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the notified items are recorded after the mail was sent and that its send-log row stays as the
    ///     count of the mail.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_SendSucceeds_ItemsRecorded()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);

        // Act
        await world.DeliverEveryAsync();

        // Assert
        Assert.Equal(["r1/ToAnswer=to-answer"], world.NotifiedOf("ann"));
        var row = Assert.Single(world.Store.Log);
        Assert.Equal("ann@example.com", row.ToAddress);
        Assert.Equal(NotificationSendLogEntry.KindUpdate, row.Kind);
    }

    /// <summary>
    ///     Test that nothing is recorded as notified while the mail is being sent: the record follows the send. The
    ///     mail is already counted at that moment, because it is reserved before the send.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_SendInProgress_ItemsNotYetRecorded()
    {
        // Arrange: the sender looks at the store at the moment of the send
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        IReadOnlyList<string>? notifiedDuringSend = null;
        var logRowsDuringSend = -1;
        world.Sender.OnSend = _ =>
        {
            notifiedDuringSend = world.NotifiedOf("ann");
            logRowsDuringSend = world.Store.Log.Count;
        };

        // Act
        await world.DeliverEveryAsync();

        // Assert: empty during the send, recorded after it
        Assert.NotNull(notifiedDuringSend);
        Assert.Empty(notifiedDuringSend);
        Assert.Equal(1, logRowsDuringSend);
        Assert.Equal(["r1/ToAnswer=to-answer"], world.NotifiedOf("ann"));
    }

    /// <summary>
    ///     Test that the mail is counted by a reservation that exists while the mail is being sent.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_SendInProgress_ReservationExists()
    {
        // Arrange: the sender looks at the send log at the moment of the send
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        NotificationSendLogEntry? rowDuringSend = null;
        world.Sender.OnSend = _ => rowDuringSend = world.Store.Log.SingleOrDefault();

        // Act
        await world.DeliverEveryAsync();

        // Assert
        Assert.NotNull(rowDuringSend);
        Assert.Equal(world.IdOf("ann"), rowDuringSend.UserId);
        Assert.Equal("ann@example.com", rowDuringSend.ToAddress);
        Assert.Equal(NotificationSendLogEntry.KindUpdate, rowDuringSend.Kind);
    }

    /// <summary>
    ///     Test that nothing is recorded as notified when the send fails, and that the mail is not counted.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_SendFails_ItemsNotRecorded()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        var rowsDuringSend = -1;
        world.Sender.BeforeSend = _ => rowsDuringSend = world.Store.Log.Count;
        world.Sender.Fail = true;

        // Act
        await world.DeliverEveryAsync();

        // Assert: the reservation existed during the failed send and was given back
        Assert.Empty(world.NotifiedOf("ann"));
        Assert.Equal(1, rowsDuringSend);
        Assert.Empty(world.Store.Log);
    }

    /// <summary>
    ///     Test that a send that is cancelled gives its reservation back.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_SendCancelled_ReservationReleased()
    {
        // Arrange: the pass is cancelled while the mail is being sent
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        using var source = new CancellationTokenSource();
        world.Sender.BeforeSend = _ => source.Cancel();
        await world.PassAsync();
        world.Advance(NotificationDispatcherWorld.DefaultWindow);

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Dispatcher.RunPassAsync(source.Token));

        // Assert
        Assert.Empty(world.Store.Log);
        Assert.Empty(world.NotifiedOf("ann"));
    }


    /// <summary>
    ///     Test that an update the user looked at before the mail was due is dropped.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_UpdateSeenBeforeSend_Dropped()
    {
        // Arrange: ann is told by the window that r1 was concluded, then she looks at it
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.ApproveAsync("r1");
        world.Enroll("ann", NotificationMode.Every);
        await world.PassAsync();
        world.Advance(TimeSpan.FromMinutes(1));
        await world.Inbox.Inbox.MarkSeenAsync(InboxTestWorld.ActorOf("ann"), world.Inbox.ProjectId, "r1", 0, null, Ct);
        world.Advance(NotificationDispatcherWorld.DefaultWindow);

        // Act
        await world.PassAsync();

        // Assert
        Assert.Empty(world.Sender.Sent);
    }
}
