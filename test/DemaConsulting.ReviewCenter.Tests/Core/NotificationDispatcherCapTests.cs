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
///     Unit tests for <see cref="NotificationDispatcher"/>: the server-wide hourly limit, the daily limit of a
///     user, and the kinds of the send-log rows the mails write.
/// </summary>
public class NotificationDispatcherCapTests
{
    /// <summary>
    ///     Makes a world with the hourly limit of the server set, and a mail of ann due after the window.
    /// </summary>
    /// <param name="maxPerHour">The hourly limit.</param>
    /// <returns>The world, with the window of ann running.</returns>
    private static async Task<NotificationDispatcherWorld> ArrangeAsync(int maxPerHour = 200)
    {
        var world = await NotificationDispatcherWorld.CreateAsync(new NotificationSettings(true, "Test Center", maxPerHour));
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        await world.PassAsync();
        world.Advance(NotificationDispatcherWorld.DefaultWindow);
        return world;
    }

    /// <summary>
    ///     Test that a mail is deferred when the hourly limit of the server is reached.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_GlobalCapReached_Deferred()
    {
        // Arrange: two mails of other users in the last hour, a limit of two
        var world = await ArrangeAsync(2);
        var now = world.Clock.GetUtcNow();
        world.Store.AddLog(world.IdOf("dee"), "dee@example.com", now.AddMinutes(-10), NotificationSendLogEntry.KindUpdate);
        world.Store.AddLog(world.IdOf("eve"), "eve@example.com", now.AddMinutes(-20), NotificationSendLogEntry.KindUpdate);

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(0, 0, 1), result);
        Assert.Empty(world.Sender.Sent);
    }

    /// <summary>
    ///     Test that mails of every kind, a confirmation mail too, count against the hourly limit.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_ConfirmationMailsCounted()
    {
        // Arrange: one confirmation mail in the last hour, a limit of one
        var world = await ArrangeAsync(1);
        world.Store.AddLog(world.IdOf("dee"), "dee@example.com", world.Clock.GetUtcNow().AddMinutes(-5));

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(1, result.Deferred);
        Assert.Empty(world.Sender.Sent);
    }

    /// <summary>
    ///     Test that a mail is deferred when the user has had 24 mails in the last day.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_UserCapReached_Deferred()
    {
        // Arrange: 24 mails to ann between 2 and 24 hours ago
        var world = await ArrangeAsync();
        FillUserLog(world, "ann", NotificationDispatcher.MaxMailsPerUserPerDay);

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(0, 0, 1), result);
        Assert.Empty(world.Sender.Sent);
    }

    /// <summary>
    ///     Test that a mail whose reservation is refused changes nothing: no log row is added, no item is recorded and
    ///     the window of the mail stays.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_ReservationRefused_NothingChanges()
    {
        // Arrange: one mail of another user in the last hour, a limit of one
        var world = await ArrangeAsync(1);
        world.Store.AddLog(world.IdOf("dee"), "dee@example.com", world.Clock.GetUtcNow().AddMinutes(-5), NotificationSendLogEntry.KindUpdate);
        var before = world.Store.StateOf(world.IdOf("ann"));

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(0, 0, 1), result);
        Assert.Empty(world.Sender.Sent);
        Assert.Single(world.Store.Log);
        Assert.Empty(world.NotifiedOf("ann"));
        Assert.Equal(before, world.Store.StateOf(world.IdOf("ann")));
    }

    /// <summary>
    ///     Test that a deferred item stays new, and is mailed when the limit allows it again.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Deferred_SentAfterWindowOfCap()
    {
        // Arrange: the hourly limit is reached by two mails of 10 and 20 minutes ago
        var world = await ArrangeAsync(2);
        var now = world.Clock.GetUtcNow();
        world.Store.AddLog(world.IdOf("dee"), "dee@example.com", now.AddMinutes(-10), NotificationSendLogEntry.KindUpdate);
        world.Store.AddLog(world.IdOf("eve"), "eve@example.com", now.AddMinutes(-20), NotificationSendLogEntry.KindUpdate);
        await world.PassAsync();
        Assert.Empty(world.NotifiedOf("ann"));

        // Act: an hour later the two mails no longer count
        world.Advance(TimeSpan.FromMinutes(61));
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(1, 0, 0), result);
        Assert.Contains("r1", Assert.Single(world.Sender.Sent).TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a deferral is not a failure: the failure count and the next attempt stay as they were.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Deferred_FailureCountUnchanged()
    {
        // Arrange
        var world = await ArrangeAsync();
        FillUserLog(world, "ann", NotificationDispatcher.MaxMailsPerUserPerDay);

        // Act
        await world.PassAsync();

        // Assert
        var state = world.Store.StateOf(world.IdOf("ann"));
        Assert.Equal(0, state.FailureCount);
        Assert.Null(state.NextAttemptUtc);
        Assert.Empty(world.Errors);
    }

    /// <summary>
    ///     Test that the mail of the mode every change writes a send-log row of the kind "update".
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_EverySent_LogKindUpdate()
    {
        // Arrange
        var world = await ArrangeAsync();

        // Act
        await world.PassAsync();

        // Assert
        var row = Assert.Single(world.Store.Log);
        Assert.Equal(NotificationSendLogEntry.KindUpdate, row.Kind);
        Assert.Equal("ann@example.com", row.ToAddress);
        Assert.Equal(world.IdOf("ann"), row.UserId);
    }

    /// <summary>
    ///     Test that the mail of the mode daily summary writes a send-log row of the kind "digest".
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_DailySent_LogKindDigest()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Daily);
        world.AdvanceTo(new DateTimeOffset(2025, 3, 4, 8, 0, 0, TimeSpan.Zero));

        // Act
        await world.PassAsync();

        // Assert
        Assert.Equal(NotificationSendLogEntry.KindDigest, Assert.Single(world.Store.Log).Kind);
    }

    /// <summary>
    ///     Adds mails to a user to the send log, from 2 hours ago back to 24 hours ago.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="username">The user name.</param>
    /// <param name="count">How many mails.</param>
    private static void FillUserLog(NotificationDispatcherWorld world, string username, int count)
    {
        var now = world.Clock.GetUtcNow();
        for (var i = 0; i < count; i++)
        {
            world.Store.AddLog(
                world.IdOf(username),
                NotificationDispatcherWorld.AddressOf(username),
                now.AddHours(-2).AddMinutes(-i * 50),
                NotificationSendLogEntry.KindUpdate);
        }
    }
}
