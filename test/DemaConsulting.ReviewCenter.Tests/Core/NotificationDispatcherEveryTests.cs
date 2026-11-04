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
///     Unit tests for <see cref="NotificationDispatcher"/> in the mode every change: the batch window that starts at
///     the first new item and ends with one mail.
/// </summary>
public class NotificationDispatcherEveryTests
{
    /// <summary>
    ///     Test that the window starts when the first new item is seen.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Every_FirstNewItem_StartsWindow()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(0, 0, 0), result);
        Assert.Equal(world.Clock.GetUtcNow(), world.Store.StateOf(world.IdOf("ann")).BatchStartedUtc);
        Assert.Empty(world.Sender.Sent);
        Assert.Empty(world.NotifiedOf("ann"));
    }

    /// <summary>
    ///     Test that nothing is sent while the window runs.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Every_InsideWindow_NothingSent()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        await world.PassAsync();
        world.Advance(NotificationDispatcherWorld.DefaultWindow - TimeSpan.FromSeconds(1));

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(0, 0, 0), result);
        Assert.Empty(world.Sender.Sent);
    }

    /// <summary>
    ///     Test that a mail is sent when the window has elapsed.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Every_AfterWindow_Sends()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        await world.PassAsync();
        world.Advance(NotificationDispatcherWorld.DefaultWindow);

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(1, 0, 0), result);
        Assert.Single(world.Sender.Sent);
    }

    /// <summary>
    ///     Test that all new items of the window are in one message.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Every_ThreeItems_OneMessage()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        await world.SeatAsync("r2", "ann");
        await world.SeatAsync("r3", "ann");
        world.Enroll("ann", NotificationMode.Every);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        var message = Assert.Single(sent);
        Assert.Contains("r1", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("r2", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("r3", message.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an item that arrives inside the window is part of the mail at its end.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Every_ItemInWindow_Included()
    {
        // Arrange: the window starts with r1, then r2 arrives
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        await world.PassAsync();
        world.Advance(TimeSpan.FromMinutes(2));
        await world.SeatAsync("r2", "ann");
        await world.PassAsync();
        world.Advance(TimeSpan.FromMinutes(3));

        // Act
        await world.PassAsync();

        // Assert
        var message = Assert.Single(world.Sender.Sent);
        Assert.Contains("r1", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("r2", message.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an item the user handled before the window ended is not mailed.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Every_ItemHandled_Dropped()
    {
        // Arrange: r1 and r2 start the window, then ann's seat in r1 is removed
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        await world.SeatAsync("r2", "ann");
        world.Enroll("ann", NotificationMode.Every);
        await world.PassAsync();
        await RemoveSeatAsync(world, "r1");
        world.Advance(NotificationDispatcherWorld.DefaultWindow);

        // Act
        await world.PassAsync();

        // Assert
        var message = Assert.Single(world.Sender.Sent);
        Assert.Contains("r2", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("r1", message.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that no mail is sent and the window is cleared when everything was handled by its end.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Every_AllHandled_NoMailWindowCleared()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        await world.PassAsync();
        await RemoveSeatAsync(world, "r1");
        world.Advance(NotificationDispatcherWorld.DefaultWindow);

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(0, 0, 0), result);
        Assert.Empty(world.Sender.Sent);
        Assert.Null(world.Store.StateOf(world.IdOf("ann")).BatchStartedUtc);
    }

    /// <summary>
    ///     Test that the window is kept when the server restarts, because it is stored.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Every_NewInstance_KeepsWindow()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        await world.PassAsync();
        world.Restart();
        world.Advance(NotificationDispatcherWorld.DefaultWindow);

        // Act
        await world.PassAsync();

        // Assert
        Assert.Single(world.Sender.Sent);
    }

    /// <summary>
    ///     Test that the configured length of the window is honored.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Every_CustomWindow_Honored()
    {
        // Arrange: a window of 20 minutes
        var world = await NotificationDispatcherWorld.CreateAsync(batchWindow: TimeSpan.FromMinutes(20));
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        await world.PassAsync();
        world.Advance(TimeSpan.FromMinutes(19));
        await world.PassAsync();
        Assert.Empty(world.Sender.Sent);
        world.Advance(TimeSpan.FromMinutes(1));

        // Act
        await world.PassAsync();

        // Assert
        Assert.Single(world.Sender.Sent);
    }

    /// <summary>
    ///     Test that the window is cleared after a successful send.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Every_AfterSend_WindowCleared()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);

        // Act
        await world.DeliverEveryAsync();

        // Assert
        var state = world.Store.StateOf(world.IdOf("ann"));
        Assert.Null(state.BatchStartedUtc);
        Assert.Equal(0, state.FailureCount);
        Assert.Null(state.NextAttemptUtc);
    }

    /// <summary>
    ///     Removes the seat of ann in a review, so the review leaves her inbox.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <returns>A task that completes when the seat is removed and the audit sequence has moved.</returns>
    internal static async Task RemoveSeatAsync(NotificationDispatcherWorld world, string reviewId)
    {
        var slots = await world.Inbox.Store.ListSlotsAsync(world.Inbox.ProjectId, reviewId, TestContext.Current.CancellationToken);
        var slot = slots.Single(s => s.PrimaryUserId == world.IdOf("ann"));
        await world.Inbox.Signing.RemoveSlotAsync(
            InboxTestWorld.ActorOf("author"), world.Inbox.ProjectId, reviewId, slot.Id, TestContext.Current.CancellationToken);
        await world.ChangeAsync();
    }
}
