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
///     Unit tests for <see cref="NotificationDispatcher"/>: an idle server stays cheap (the inbox is read only when the
///     audit trail moved, every 15 minutes, or when stored work is due), and one failing user or a cancellation
///     does not harm the others.
/// </summary>
public class NotificationDispatcherIdleTests
{
    /// <summary>
    ///     The cancellation token of the running test.
    /// </summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Test that the audit sequence is not read when no user has a mode.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_NoRecipients_NoSequenceRead()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(0, 0, 0), result);
        Assert.Equal(0, world.Audit.LatestSequenceReads);
        Assert.Equal(0, world.InboxStore.InboxReads);
    }

    /// <summary>
    ///     Test that the inbox is read on the first pass.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_First_ReadsInbox()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        world.Enroll("ann", NotificationMode.Every);

        // Act
        await world.PassAsync();

        // Assert
        Assert.Equal(1, world.InboxStore.InboxReads);
    }

    /// <summary>
    ///     Test that the inbox is not read again when the audit sequence did not move.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_SequenceUnchanged_NoInboxRead()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        world.Enroll("ann", NotificationMode.Every);
        await world.PassAsync();
        world.Advance(TimeSpan.FromMinutes(1));

        // Act
        await world.PassAsync();

        // Assert
        Assert.Equal(1, world.InboxStore.InboxReads);
        Assert.Equal(2, world.Audit.LatestSequenceReads);
    }

    /// <summary>
    ///     Test that the inbox is read when the audit sequence moved.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_SequenceChanged_ReadsInbox()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        world.Enroll("ann", NotificationMode.Every);
        await world.PassAsync();
        world.Advance(TimeSpan.FromMinutes(1));
        await world.ChangeAsync();

        // Act
        await world.PassAsync();

        // Assert
        Assert.Equal(2, world.InboxStore.InboxReads);
    }

    /// <summary>
    ///     Test that the inbox is read after 15 minutes without a change, so time-driven news is not missed.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_15MinutesNoChange_ReadsInbox()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        world.Enroll("ann", NotificationMode.Every);
        await world.PassAsync();
        world.Advance(NotificationDispatcher.SweepInterval - TimeSpan.FromSeconds(1));
        await world.PassAsync();
        Assert.Equal(1, world.InboxStore.InboxReads);
        world.Advance(TimeSpan.FromSeconds(1));

        // Act
        await world.PassAsync();

        // Assert
        Assert.Equal(2, world.InboxStore.InboxReads);
    }

    /// <summary>
    ///     Test that a window that has elapsed is served although the audit sequence did not move.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_WindowElapsedSequenceUnchanged_Sends()
    {
        // Arrange: the window starts at the first pass; nothing is audited afterwards
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        await world.PassAsync();
        world.Advance(NotificationDispatcherWorld.DefaultWindow);

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(1, 0, 0), result);
    }

    /// <summary>
    ///     Test that a pass in which a user failed does not remember the sequence, so the next pass reads again.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_PassFails_NextPassReads()
    {
        // Arrange: ann's inbox cannot be read on the first pass
        var world = await NotificationDispatcherWorld.CreateAsync();
        world.Enroll("ann", NotificationMode.Every);
        world.InboxStore.FailForUserId = world.IdOf("ann");
        await world.PassAsync();
        world.InboxStore.FailForUserId = null;
        world.Advance(TimeSpan.FromMinutes(1));

        // Act: no change, within 15 minutes
        await world.PassAsync();

        // Assert
        Assert.Equal(2, world.InboxStore.InboxReads);
    }

    /// <summary>
    ///     Test that a failure of the inbox of one user does not keep another user from being mailed.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_InboxFails_OtherUserMailed()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        await world.SeatAsync("r2", "bob");
        world.Enroll("ann", NotificationMode.Every);
        world.Enroll("bob", NotificationMode.Every);
        world.InboxStore.FailForUserId = world.IdOf("ann");
        await world.PassAsync();
        world.Advance(NotificationDispatcherWorld.DefaultWindow);

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(1, 0, 0), result);
        Assert.Equal(1, world.SentTo("bob"));
        Assert.Equal(0, world.SentTo("ann"));
    }

    /// <summary>
    ///     Test that an unexpected error of a user is passed to the error callback.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_InboxFails_CallbackInvoked()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        world.Enroll("ann", NotificationMode.Every);
        world.InboxStore.FailForUserId = world.IdOf("ann");

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(0, 0, 0), result);
        Assert.IsType<InvalidOperationException>(Assert.Single(world.Errors));
    }

    /// <summary>
    ///     Test that a canceled pass stops with a cancellation exception.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Cancelled_Throws()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        world.Enroll("ann", NotificationMode.Every);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await source.CancelAsync();

        // Act and Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Dispatcher.RunPassAsync(source.Token));
        Assert.Empty(world.Errors);
    }

    /// <summary>
    ///     Test that the result counts the mails of a pass, sent, failed and deferred, and holds no address.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Mixed_ResultCounts()
    {
        // Arrange: ann is mailed, bob's address fails, cyd has had 24 mails in a day
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        await world.SeatAsync("r2", "bob");
        await world.SeatAsync("r3", "cyd");
        world.Enroll("ann", NotificationMode.Every);
        world.Enroll("bob", NotificationMode.Every);
        world.Enroll("cyd", NotificationMode.Every);
        world.Sender.FailForAddress = "bob@example.com";
        var now = world.Clock.GetUtcNow();
        for (var i = 0; i < NotificationDispatcher.MaxMailsPerUserPerDay; i++)
        {
            world.Store.AddLog(world.IdOf("cyd"), "cyd@example.com", now.AddHours(-2).AddMinutes(-i * 50), NotificationSendLogEntry.KindUpdate);
        }

        await world.PassAsync();
        world.Advance(NotificationDispatcherWorld.DefaultWindow);

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(1, 1, 1), result);
        Assert.Equal(typeof(int), typeof(NotificationPassResult).GetProperty(nameof(NotificationPassResult.Sent))!.PropertyType);
    }

    /// <summary>
    ///     Test that the result of a pass that mailed a user holds only numbers and shows no address.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Sent_ResultHoldsNoAddress()
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
        Assert.Equal(1, result.Sent);
        Assert.All(typeof(NotificationPassResult).GetProperties(), property => Assert.Equal(typeof(int), property.PropertyType));
        Assert.DoesNotContain("example.com", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("ann", result.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
