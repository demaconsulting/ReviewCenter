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
///     Unit tests for <see cref="NotificationDispatcher"/>: the baseline that a user who has just switched a mode on
///     gets, so the backlog is recorded and not mailed.
/// </summary>
public class NotificationDispatcherBaselineTests
{
    /// <summary>
    ///     Test that the backlog is recorded as notified without a mail.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_BaselinePending_RecordsBacklogNoMail()
    {
        // Arrange: ann has two reviews to answer when she switches the mode on
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        await world.SeatAsync("r2", "ann");
        world.Enroll("ann", NotificationMode.Every, baselinePending: true);

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(0, 0, 0), result);
        Assert.Empty(world.Sender.Sent);
        Assert.Equal(["r1/ToAnswer=to-answer", "r2/ToAnswer=to-answer"], world.NotifiedOf("ann"));
    }

    /// <summary>
    ///     Test that the pending flag is cleared after the baseline.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_BaselinePending_FlagCleared()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every, baselinePending: true);

        // Act
        await world.PassAsync();

        // Assert
        Assert.False(world.Store.StateOf(world.IdOf("ann")).BaselinePending);
    }

    /// <summary>
    ///     Test that the baseline writes no send-log row, because no mail went out.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Baseline_NoLogRow()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Daily, baselinePending: true);

        // Act
        await world.PassAsync();

        // Assert
        Assert.Empty(world.Store.Log);
    }

    /// <summary>
    ///     Test that the baseline starts the digest day, so a daily summary waits for the next local date.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Baseline_DigestHandledNow()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Daily, baselinePending: true);

        // Act
        await world.PassAsync();

        // Assert
        Assert.Equal(world.Clock.GetUtcNow(), world.Store.StateOf(world.IdOf("ann")).DigestHandledUtc);
    }

    /// <summary>
    ///     Test that an item that arrives after the baseline is mailed, and the backlog is not.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_AfterBaseline_NewItemMailed()
    {
        // Arrange: the baseline records r1
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every, baselinePending: true);
        await world.PassAsync();
        await world.SeatAsync("r2", "ann");

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        var message = Assert.Single(sent);
        Assert.Contains("r2", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("r1", message.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a pending baseline is read although the audit sequence did not move.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_BaselinePendingSequenceUnchanged_ReadsInbox()
    {
        // Arrange: a first pass has seen the sequence; then the user switches the mode on without any change of a review
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        await world.PassAsync();
        world.Store.Seed(world.IdOf("ann"), "ann", NotificationDispatcherWorld.AddressOf("ann"), NotificationMode.Daily, baselinePending: true);
        var reads = world.InboxStore.InboxReads;
        world.Advance(TimeSpan.FromMinutes(1));

        // Act
        await world.PassAsync();

        // Assert
        Assert.Equal(reads + 1, world.InboxStore.InboxReads);
        Assert.False(world.Store.StateOf(world.IdOf("ann")).BaselinePending);
    }

    /// <summary>
    ///     Test that switching between the active modes before the first pass keeps the baseline pending, so the
    ///     backlog is recorded and not mailed.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_ModeSwitchedBeforeBaseline_BacklogNotMailed()
    {
        // Arrange: ann has a review to answer, switches the daily summary on and then the mode every change on, before
        // any pass recorded the baseline
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Daily, baselinePending: true);
        world.Store.SwitchMode(world.IdOf("ann"), NotificationMode.Every);

        // Act
        await world.PassAsync();
        world.Advance(NotificationDispatcherWorld.DefaultWindow);
        await world.PassAsync();

        // Assert
        Assert.Empty(world.Sender.Sent);
        Assert.Equal(["r1/ToAnswer=to-answer"], world.NotifiedOf("ann"));
        Assert.False(world.Store.StateOf(world.IdOf("ann")).BaselinePending);
    }
}
