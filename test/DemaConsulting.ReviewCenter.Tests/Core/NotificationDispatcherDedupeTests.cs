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
        world.Store.AddNotified(world.IdOf("ann"), new NotifiedKey(world.Inbox.ProjectId, "r1", InboxSection.ToAnswer), NotificationComposer.StateQuestion);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        Assert.Single(sent);
        Assert.Equal(["r1/ToAnswer=to-answer"], world.NotifiedOf("ann"));
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
        world.Store.AddNotified(world.IdOf("ann"), new NotifiedKey(world.Inbox.ProjectId, "r1", InboxSection.RepliesWaiting), "replies:1");
        world.Store.AddNotified(world.IdOf("ann"), new NotifiedKey(world.Inbox.ProjectId, "r1", InboxSection.ToAnswer), NotificationComposer.StateToAnswer);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        var message = Assert.Single(sent);
        Assert.Contains("2 replies are waiting", message.TextBody, StringComparison.Ordinal);
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
        world.Store.AddNotified(world.IdOf("ann"), new NotifiedKey(world.Inbox.ProjectId, "r1", InboxSection.RepliesWaiting), "replies:3");
        world.Store.AddNotified(world.IdOf("ann"), new NotifiedKey(world.Inbox.ProjectId, "r1", InboxSection.ToAnswer), NotificationComposer.StateToAnswer);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        Assert.Empty(sent);
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
    ///     Test that the notified items are recorded together with the send-log row after the mail was sent.
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
        var commit = world.Store.Commits[^1];
        Assert.NotNull(commit.Log);
        Assert.Equal("ann@example.com", commit.Log.Address);
    }

    /// <summary>
    ///     Test that nothing is recorded as notified while the mail is being sent: the record follows the send.
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
        Assert.Equal(0, logRowsDuringSend);
        Assert.Equal(["r1/ToAnswer=to-answer"], world.NotifiedOf("ann"));
    }

    /// <summary>
    ///     Test that nothing is recorded as notified when the send fails.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_SendFails_ItemsNotRecorded()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        world.Sender.Fail = true;

        // Act
        await world.DeliverEveryAsync();

        // Assert
        Assert.Empty(world.NotifiedOf("ann"));
        Assert.Empty(world.Store.Log);
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
