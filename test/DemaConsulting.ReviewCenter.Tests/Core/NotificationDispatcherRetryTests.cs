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
///     Unit tests for <see cref="NotificationDispatcher"/>: a failed send is retried after 5 minutes, 15 minutes,
///     1 hour, 4 hours and 12 hours, and given up after the sixth failure.
/// </summary>
public class NotificationDispatcherRetryTests
{
    /// <summary>
    ///     Makes a world in which ann has a mail due after the window and the sender fails.
    /// </summary>
    /// <returns>The world, with the window running.</returns>
    private static async Task<NotificationDispatcherWorld> ArrangeAsync()
    {
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        world.Sender.Fail = true;
        await world.PassAsync();
        world.Advance(NotificationDispatcherWorld.DefaultWindow);
        return world;
    }

    /// <summary>
    ///     Fails the send a number of times, each time after the wait the previous failure asked for.
    /// </summary>
    /// <param name="world">The world, as <see cref="ArrangeAsync"/> made it.</param>
    /// <param name="failures">How many failures.</param>
    /// <returns>The time of the last failure.</returns>
    private static async Task<DateTimeOffset> FailAsync(NotificationDispatcherWorld world, int failures)
    {
        var at = world.Clock.GetUtcNow();
        for (var i = 1; i <= failures; i++)
        {
            at = world.Clock.GetUtcNow();
            var result = await world.PassAsync();
            Assert.Equal(1, result.Failed);
            if (i < failures)
            {
                world.Advance(NotificationDispatcher.RetryDelays[i - 1]);
            }
        }

        return at;
    }

    /// <summary>
    ///     Test that the extra full read after a failed pass does not send a failed mail earlier than its wait.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_FullReadAfterFailedPass_RetryWaitHonored()
    {
        // Arrange: ann's mail fails and bob's inbox cannot be read, in the same pass
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        world.Enroll("bob", NotificationMode.Every);
        await world.PassAsync();
        world.Advance(NotificationDispatcherWorld.DefaultWindow);
        await world.ChangeAsync();
        world.Sender.Fail = true;
        world.InboxStore.FailForUserId = world.IdOf("bob");
        var failed = await world.PassAsync();
        Assert.Equal(1, failed.Failed);
        world.Sender.Fail = false;
        world.InboxStore.FailForUserId = null;

        // Act: a pass that reads everything again, before the wait of 5 minutes is over
        world.Advance(NotificationDispatcher.RetryDelays[0] - TimeSpan.FromSeconds(1));
        var early = await world.PassAsync();

        // Assert: nothing was sent; one second later the retry goes out
        Assert.Equal(new NotificationPassResult(0, 0, 0), early);
        Assert.Equal(0, world.SentTo("ann"));
        world.Advance(TimeSpan.FromSeconds(1));
        var due = await world.PassAsync();
        Assert.Equal(new NotificationPassResult(1, 0, 0), due);
        Assert.Equal(1, world.SentTo("ann"));
    }

    /// <summary>
    ///     Test that a failed send stores the number of failed sends in a row.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_SendFails_FailureCountStored()
    {
        // Arrange
        var world = await ArrangeAsync();

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(0, 1, 0), result);
        var state = world.Store.StateOf(world.IdOf("ann"));
        Assert.Equal(1, state.FailureCount);
        Assert.NotNull(state.BatchStartedUtc);
    }

    /// <summary>
    ///     Test that a failed send stores the time of the next attempt.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_SendFails_NextAttemptStored()
    {
        // Arrange
        var world = await ArrangeAsync();
        var now = world.Clock.GetUtcNow();

        // Act
        await world.PassAsync();

        // Assert
        var state = world.Store.StateOf(world.IdOf("ann"));
        Assert.Equal(now + TimeSpan.FromMinutes(5), state.NextAttemptUtc);
    }

    /// <summary>
    ///     Test that the first failure waits 5 minutes.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Failure1_Waits5Min()
    {
        // Arrange, Act
        var world = await ArrangeAsync();
        var at = await FailAsync(world, 1);

        // Assert
        var state = world.Store.StateOf(world.IdOf("ann"));
        Assert.Equal(1, state.FailureCount);
        Assert.Equal(at + TimeSpan.FromMinutes(5), state.NextAttemptUtc);
    }

    /// <summary>
    ///     Test that the second failure waits 15 minutes.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Failure2_Waits15Min()
    {
        // Arrange, Act
        var world = await ArrangeAsync();
        var at = await FailAsync(world, 2);

        // Assert
        var state = world.Store.StateOf(world.IdOf("ann"));
        Assert.Equal(2, state.FailureCount);
        Assert.Equal(at + TimeSpan.FromMinutes(15), state.NextAttemptUtc);
    }

    /// <summary>
    ///     Test that the third failure waits 1 hour.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Failure3_Waits1Hour()
    {
        // Arrange, Act
        var world = await ArrangeAsync();
        var at = await FailAsync(world, 3);

        // Assert
        var state = world.Store.StateOf(world.IdOf("ann"));
        Assert.Equal(3, state.FailureCount);
        Assert.Equal(at + TimeSpan.FromHours(1), state.NextAttemptUtc);
    }

    /// <summary>
    ///     Test that the fourth failure waits 4 hours.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Failure4_Waits4Hours()
    {
        // Arrange, Act
        var world = await ArrangeAsync();
        var at = await FailAsync(world, 4);

        // Assert
        var state = world.Store.StateOf(world.IdOf("ann"));
        Assert.Equal(4, state.FailureCount);
        Assert.Equal(at + TimeSpan.FromHours(4), state.NextAttemptUtc);
    }

    /// <summary>
    ///     Test that the fifth failure waits 12 hours.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Failure5_Waits12Hours()
    {
        // Arrange, Act
        var world = await ArrangeAsync();
        var at = await FailAsync(world, 5);

        // Assert
        var state = world.Store.StateOf(world.IdOf("ann"));
        Assert.Equal(5, state.FailureCount);
        Assert.Equal(at + TimeSpan.FromHours(12), state.NextAttemptUtc);
    }

    /// <summary>
    ///     Test that no attempt is made before the next attempt time.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_BeforeNextAttempt_NoSend()
    {
        // Arrange: the first attempt failed; the sender works again, but the wait is not over
        var world = await ArrangeAsync();
        await world.PassAsync();
        world.Sender.Fail = false;
        world.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));

        // Act
        var early = await world.PassAsync();
        world.Advance(TimeSpan.FromSeconds(1));
        var due = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(0, 0, 0), early);
        Assert.Equal(new NotificationPassResult(1, 0, 0), due);
    }

    /// <summary>
    ///     Test that a retry uses the inbox as it is then: an item that arrived meanwhile is part of the mail.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Retry_UsesCurrentInbox()
    {
        // Arrange: the first attempt failed, then r2 arrives
        var world = await ArrangeAsync();
        await world.PassAsync();
        await world.SeatAsync("r2", "ann");
        world.Sender.Fail = false;
        world.Advance(TimeSpan.FromMinutes(5));

        // Act
        await world.PassAsync();

        // Assert
        var message = Assert.Single(world.Sender.Sent);
        Assert.Contains("r1", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("r2", message.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the sixth failure gives up, and that nothing is tried again.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Failure6_GivesUp()
    {
        // Arrange: five failures
        var world = await ArrangeAsync();
        await FailAsync(world, 5);
        world.Advance(TimeSpan.FromHours(12));

        // Act
        var result = await world.PassAsync();

        // Assert: the state is cleared and the error callback got the failure once
        Assert.Equal(new NotificationPassResult(0, 1, 0), result);
        var state = world.Store.StateOf(world.IdOf("ann"));
        Assert.Equal(0, state.FailureCount);
        Assert.Null(state.NextAttemptUtc);
        Assert.Null(state.BatchStartedUtc);
        Assert.Single(world.Errors);

        // Act: the sender works again
        world.Sender.Fail = false;
        world.Advance(TimeSpan.FromHours(1));
        var later = await world.PassAsync();

        // Assert: the dead mail is not tried again
        Assert.Equal(new NotificationPassResult(0, 0, 0), later);
        Assert.Empty(world.Sender.Sent);
    }

    /// <summary>
    ///     Test that the items are recorded as notified when the dispatcher gives up.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_GiveUp_ItemsRecorded()
    {
        // Arrange
        var world = await ArrangeAsync();
        await FailAsync(world, 5);
        world.Advance(TimeSpan.FromHours(12));

        // Act
        await world.PassAsync();

        // Assert
        Assert.Equal(["r1/ToAnswer=to-answer"], world.NotifiedOf("ann"));
        Assert.Empty(world.Store.Log);
    }

    /// <summary>
    ///     Test that a successful retry clears the failure state.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_RetrySucceeds_StateReset()
    {
        // Arrange
        var world = await ArrangeAsync();
        await FailAsync(world, 2);
        world.Advance(TimeSpan.FromMinutes(15));
        world.Sender.Fail = false;

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(1, 0, 0), result);
        var state = world.Store.StateOf(world.IdOf("ann"));
        Assert.Equal(0, state.FailureCount);
        Assert.Null(state.NextAttemptUtc);
        Assert.Null(state.BatchStartedUtc);
    }

    /// <summary>
    ///     Test that a failed send to one user does not keep the next user from being mailed.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_OneSendFails_OtherUserMailed()
    {
        // Arrange: ann's address fails, bob's works
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        await world.SeatAsync("r2", "bob");
        world.Enroll("ann", NotificationMode.Every);
        world.Enroll("bob", NotificationMode.Every);
        world.Sender.FailForAddress = "ann@example.com";
        await world.PassAsync();
        world.Advance(NotificationDispatcherWorld.DefaultWindow);

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(1, 1, 0), result);
        Assert.Equal(1, world.SentTo("bob"));
        Assert.Equal(0, world.SentTo("ann"));
    }
}
