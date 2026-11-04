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
///     Unit tests for <see cref="NotificationDispatcher"/> in the mode daily summary: one summary per local date at
///     the digest hour, catching up after a downtime, and the time zone and the days a clock changes.
/// </summary>
public class NotificationDispatcherDailyTests
{
    /// <summary>
    ///     Makes an instant in UTC.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <param name="month">The month.</param>
    /// <param name="day">The day.</param>
    /// <param name="hour">The hour.</param>
    /// <param name="minute">The minute.</param>
    /// <returns>The instant.</returns>
    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0)
    {
        return new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
    }

    /// <summary>
    ///     Test that no summary is sent before the digest hour.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Daily_BeforeHour_NothingSent()
    {
        // Arrange: the clock starts at 05:06 UTC; the digest hour is 8
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Daily);

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(0, 0, 0), result);
        Assert.Empty(world.Sender.Sent);
        Assert.Null(world.Store.StateOf(world.IdOf("ann")).DigestHandledUtc);
    }

    /// <summary>
    ///     Test that the summary is sent at the digest hour when something is new.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Daily_AtHour_Sends()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Daily);
        world.AdvanceTo(Utc(2025, 3, 4, 8));

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(1, 0, 0), result);
        var message = Assert.Single(world.Sender.Sent);
        Assert.Equal("Test Center: your daily summary", message.Subject);
        Assert.Contains("r1", message.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that nothing is sent when nothing is new, and that the slot of the day is used up.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Daily_NothingNew_NoMailSlotConsumed()
    {
        // Arrange: nothing to tell at 08:00
        var world = await NotificationDispatcherWorld.CreateAsync();
        world.Enroll("ann", NotificationMode.Daily);
        world.AdvanceTo(Utc(2025, 3, 4, 8));
        await world.PassAsync();
        var handled = world.Store.StateOf(world.IdOf("ann")).DigestHandledUtc;

        // Act: news arrives later the same day
        world.AdvanceTo(Utc(2025, 3, 4, 12));
        await world.SeatAsync("r1", "ann");
        await world.PassAsync();

        // Assert
        Assert.Equal(Utc(2025, 3, 4, 8), handled);
        Assert.Empty(world.Sender.Sent);
    }

    /// <summary>
    ///     Test that at most one summary is sent per local date.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Daily_TwoPassesSameDate_OneMail()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Daily);
        world.AdvanceTo(Utc(2025, 3, 4, 8));
        await world.PassAsync();

        // Act
        world.AdvanceTo(Utc(2025, 3, 4, 20));
        await world.SeatAsync("r2", "ann");
        await world.PassAsync();

        // Assert
        Assert.Single(world.Sender.Sent);
    }

    /// <summary>
    ///     Test that an item that arrives after the summary waits for the next local date.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Daily_ItemAfterSlot_SentNextDay()
    {
        // Arrange: the summary of the first day holds r1; r2 arrives in the afternoon
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Daily);
        world.AdvanceTo(Utc(2025, 3, 4, 8));
        await world.PassAsync();
        world.AdvanceTo(Utc(2025, 3, 4, 15));
        await world.SeatAsync("r2", "ann");
        await world.PassAsync();
        Assert.Single(world.Sender.Sent);

        // Act
        world.AdvanceTo(Utc(2025, 3, 5, 8));
        await world.PassAsync();

        // Assert: the second summary names r2 only; r1 is only counted
        Assert.Equal(2, world.Sender.Sent.Count);
        var second = world.Sender.Sent[1];
        Assert.Contains("r2", second.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("r1", second.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the summary is sent after a downtime that spanned the digest hour, as soon as the server runs.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Daily_DowntimeOverHour_SendsOnRestart()
    {
        // Arrange: the last summary was handled on 4 March; the server is down until the afternoon of 6 March
        var world = await NotificationDispatcherWorld.CreateAsync();
        world.Enroll("ann", NotificationMode.Daily);
        world.AdvanceTo(Utc(2025, 3, 4, 8));
        await world.PassAsync();
        await world.SeatAsync("r1", "ann");
        world.AdvanceTo(Utc(2025, 3, 6, 15, 30));
        world.Restart();

        // Act
        var result = await world.PassAsync();

        // Assert
        Assert.Equal(new NotificationPassResult(1, 0, 0), result);
    }

    /// <summary>
    ///     Test that the digest hour is the local hour of the configured time zone.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Daily_ZoneOffset_UsesLocalHour()
    {
        // Arrange: three hours ahead of UTC, the clock starts at 05:06 UTC, which is 08:06 local
        var plusThree = TimeZoneInfo.CreateCustomTimeZone("plus-three", TimeSpan.FromHours(3), "plus-three", "plus-three");
        var local = await NotificationDispatcherWorld.CreateAsync(zone: plusThree);
        await local.SeatAsync("r1", "ann");
        local.Enroll("ann", NotificationMode.Daily);
        var utc = await NotificationDispatcherWorld.CreateAsync();
        await utc.SeatAsync("r1", "ann");
        utc.Enroll("ann", NotificationMode.Daily);

        // Act
        await local.PassAsync();
        await utc.PassAsync();

        // Assert
        Assert.Single(local.Sender.Sent);
        Assert.Empty(utc.Sender.Sent);
    }

    /// <summary>
    ///     Test that on the day a clock changes the summary is sent once, whether the hour is skipped or repeated.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Daily_DstChangeDay_OneMail()
    {
        // Arrange: one hour ahead of UTC; on 30 March the local clock jumps from 02:00 to 03:00, and the digest hour is 2
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified), 3, 30);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0, DateTimeKind.Unspecified), 10, 26);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
        var zone = TimeZoneInfo.CreateCustomTimeZone("plus-one-dst", TimeSpan.FromHours(1), "plus-one-dst", "plus-one-dst", "plus-one-dst-summer", [rule]);
        var world = await NotificationDispatcherWorld.CreateAsync(digestHour: 2, zone: zone);
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Daily);
        world.AdvanceTo(Utc(2025, 3, 30, 0, 30));
        await world.PassAsync();
        Assert.Empty(world.Sender.Sent);

        // Act: the clock jumps (01:00 UTC is 03:00 local) and the day goes on
        foreach (var hour in new[] { 1, 2, 5, 12, 22 })
        {
            world.AdvanceTo(Utc(2025, 3, 30, hour));
            await world.PassAsync();
        }

        // Assert
        Assert.Single(world.Sender.Sent);
    }

    /// <summary>
    ///     Test that the summary lists the new items and counts those that were mailed before and still wait.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Daily_OutstandingCounted()
    {
        // Arrange: r1 and r2 were mailed on the first day; r3 is new on the second
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        await world.SeatAsync("r2", "ann");
        world.Enroll("ann", NotificationMode.Daily);
        world.AdvanceTo(Utc(2025, 3, 4, 8));
        await world.PassAsync();
        world.AdvanceTo(Utc(2025, 3, 5, 8));
        await world.SeatAsync("r3", "ann");

        // Act
        await world.PassAsync();

        // Assert
        var second = world.Sender.Sent[1];
        Assert.Contains("r3", second.TextBody, StringComparison.Ordinal);
        Assert.Contains("2 more items you were told about earlier are still waiting for you.", second.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a user who switches from every change to the daily summary gets the next summary when the slot opens.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_SwitchEveryToDaily_DueWhenSlotOpen()
    {
        // Arrange: a mail of the mode every change was sent for r1; then the user switches to daily and r2 arrives
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        await world.DeliverEveryAsync();
        world.Store.Seed(world.IdOf("ann"), "ann", NotificationDispatcherWorld.AddressOf("ann"), NotificationMode.Daily);
        world.Store.AddNotified(world.IdOf("ann"), new NotifiedKey(world.Inbox.ProjectId, "r1", InboxSection.ToAnswer, "to-answer"), NotificationComposer.StateToAnswer);
        await world.SeatAsync("r2", "ann");
        await world.PassAsync();
        Assert.Single(world.Sender.Sent);

        // Act: the digest hour comes
        world.AdvanceTo(Utc(2025, 3, 4, 8));
        await world.PassAsync();

        // Assert
        Assert.Equal(2, world.Sender.Sent.Count);
        Assert.Equal("Test Center: your daily summary", world.Sender.Sent[1].Subject);
        Assert.Contains("r2", world.Sender.Sent[1].TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a user who has just switched the daily summary on gets nothing for the backlog on the same date,
    ///     and the first summary on the next local date.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_DailyJustEnabled_NoMailSameDate()
    {
        // Arrange: ann switches the mode on at 09:00, after the digest hour
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.AdvanceTo(Utc(2025, 3, 4, 9));
        world.Enroll("ann", NotificationMode.Daily, baselinePending: true);
        await world.PassAsync();
        await world.SeatAsync("r2", "ann");
        world.AdvanceTo(Utc(2025, 3, 4, 10));

        // Act
        await world.PassAsync();
        var sameDate = world.Sender.Sent.Count;
        world.AdvanceTo(Utc(2025, 3, 5, 8));
        await world.PassAsync();

        // Assert
        Assert.Equal(0, sameDate);
        var message = Assert.Single(world.Sender.Sent);
        Assert.Contains("r2", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("r1", message.TextBody, StringComparison.Ordinal);
    }
}
