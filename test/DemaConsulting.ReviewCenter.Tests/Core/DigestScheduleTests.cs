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
///     Unit tests for <see cref="DigestSchedule"/>: the digest hour, the local date, catching up after a downtime and
///     the days a clock changes. Custom time zones keep the tests independent of the zone database of the machine.
/// </summary>
public class DigestScheduleTests
{
    /// <summary>
    ///     A time zone with a fixed offset of three hours ahead of UTC.
    /// </summary>
    private static readonly TimeZoneInfo _plusThree =
        TimeZoneInfo.CreateCustomTimeZone("plus-three", TimeSpan.FromHours(3), "plus-three", "plus-three");

    /// <summary>
    ///     A time zone one hour ahead of UTC that moves its clock forward at 02:00 on 30 March and back at 03:00 on
    ///     26 October.
    /// </summary>
    private static readonly TimeZoneInfo _withDaylightSaving = CreateDaylightSavingZone();

    /// <summary>
    ///     Test that the slot is not due before the digest hour of the local day.
    /// </summary>
    [Fact]
    public void DigestSchedule_IsDue_BeforeHour_False()
    {
        // Arrange: the digest hour is 8 in UTC
        var schedule = new DigestSchedule(8, TimeZoneInfo.Utc);

        // Act
        var due = schedule.IsDue(Utc(2025, 3, 4, 7, 59), null);

        // Assert
        Assert.False(due);
    }

    /// <summary>
    ///     Test that the slot is due at the digest hour.
    /// </summary>
    [Fact]
    public void DigestSchedule_IsDue_AtHour_True()
    {
        // Arrange
        var schedule = new DigestSchedule(8, TimeZoneInfo.Utc);

        // Act
        var due = schedule.IsDue(Utc(2025, 3, 4, 8, 0), Utc(2025, 3, 3, 8, 0));

        // Assert
        Assert.True(due);
    }

    /// <summary>
    ///     Test that the slot is due after the digest hour.
    /// </summary>
    [Fact]
    public void DigestSchedule_IsDue_AfterHour_True()
    {
        // Arrange
        var schedule = new DigestSchedule(8, TimeZoneInfo.Utc);

        // Act
        var due = schedule.IsDue(Utc(2025, 3, 4, 17, 30), null);

        // Assert
        Assert.True(due);
    }

    /// <summary>
    ///     Test that the slot is not due when the slot of the local date was handled.
    /// </summary>
    [Fact]
    public void DigestSchedule_IsDue_HandledToday_False()
    {
        // Arrange
        var schedule = new DigestSchedule(8, TimeZoneInfo.Utc);

        // Act
        var due = schedule.IsDue(Utc(2025, 3, 4, 17, 30), Utc(2025, 3, 4, 8, 1));

        // Assert
        Assert.False(due);
    }

    /// <summary>
    ///     Test that the slot is due when the last slot was handled on an earlier local date.
    /// </summary>
    [Fact]
    public void DigestSchedule_IsDue_HandledYesterday_True()
    {
        // Arrange
        var schedule = new DigestSchedule(8, TimeZoneInfo.Utc);

        // Act
        var due = schedule.IsDue(Utc(2025, 3, 4, 8, 0), Utc(2025, 3, 3, 23, 59));

        // Assert
        Assert.True(due);
    }

    /// <summary>
    ///     Test that the slot is due after the hour when no slot was ever handled.
    /// </summary>
    [Fact]
    public void DigestSchedule_IsDue_NeverHandled_True()
    {
        // Arrange
        var schedule = new DigestSchedule(8, TimeZoneInfo.Utc);

        // Act
        var due = schedule.IsDue(Utc(2025, 3, 4, 9, 0), null);

        // Assert
        Assert.True(due);
    }

    /// <summary>
    ///     Test that the slot is due as soon as the server runs again after a downtime of several days.
    /// </summary>
    [Fact]
    public void DigestSchedule_IsDue_HandledDaysAgo_True()
    {
        // Arrange: the last slot was handled five days ago
        var schedule = new DigestSchedule(8, TimeZoneInfo.Utc);

        // Act
        var due = schedule.IsDue(Utc(2025, 3, 9, 12, 0), Utc(2025, 3, 4, 8, 0));

        // Assert
        Assert.True(due);
    }

    /// <summary>
    ///     Test that the local date of the zone counts, not the date in UTC.
    /// </summary>
    [Fact]
    public void DigestSchedule_IsDue_ZoneAheadOfUtc_UsesLocalDate()
    {
        // Arrange: three hours ahead; 22:00 UTC on 3 March is 01:00 on 4 March, the same local date as 05:30 UTC
        var schedule = new DigestSchedule(8, _plusThree);

        // Act
        var sameLocalDate = schedule.IsDue(Utc(2025, 3, 4, 5, 30), Utc(2025, 3, 3, 22, 0));
        var beforeLocalHour = schedule.IsDue(Utc(2025, 3, 4, 4, 59), null);

        // Assert: handled on the local date although the UTC dates differ; 04:59 UTC is 07:59 local
        Assert.False(sameLocalDate);
        Assert.False(beforeLocalHour);
        Assert.True(schedule.IsDue(Utc(2025, 3, 4, 5, 0), null));
    }

    /// <summary>
    ///     Test that the digest hour 0 is accepted.
    /// </summary>
    [Fact]
    public void DigestSchedule_Constructor_Hour0_Accepted()
    {
        // Arrange, Act
        var schedule = new DigestSchedule(0, TimeZoneInfo.Utc);

        // Assert
        Assert.NotNull(schedule);
    }

    /// <summary>
    ///     Test that the hour 0 opens the slot right after midnight.
    /// </summary>
    [Fact]
    public void DigestSchedule_IsDue_HourZero_DueAfterMidnight()
    {
        // Arrange
        var schedule = new DigestSchedule(0, TimeZoneInfo.Utc);

        // Act
        var due = schedule.IsDue(Utc(2025, 3, 4, 0, 0), Utc(2025, 3, 3, 0, 5));

        // Assert
        Assert.True(due);
    }

    /// <summary>
    ///     Test that a digest hour that a spring-forward change skips is due at the first local time past the gap.
    /// </summary>
    [Fact]
    public void DigestSchedule_IsDue_SpringForwardGap_DueAfterGap()
    {
        // Arrange: on 30 March the local clock jumps from 01:59 to 03:00 (01:00 UTC); the digest hour 2 does not exist
        var schedule = new DigestSchedule(2, _withDaylightSaving);
        var handled = Utc(2025, 3, 29, 1, 0);

        // Act
        var beforeGap = schedule.IsDue(Utc(2025, 3, 30, 0, 59), handled);
        var afterGap = schedule.IsDue(Utc(2025, 3, 30, 1, 0), handled);

        // Assert
        Assert.False(beforeGap);
        Assert.True(afterGap);
    }

    /// <summary>
    ///     Test that a day with a repeated hour has the slot due once: after it was handled, the repeated hour is not due.
    /// </summary>
    [Fact]
    public void DigestSchedule_IsDue_FallBackRepeat_OnceAfterHandled()
    {
        // Arrange: on 26 October the hour 02:00 to 03:00 happens twice (00:00 to 01:00 UTC, then 01:00 to 02:00 UTC)
        var schedule = new DigestSchedule(2, _withDaylightSaving);
        var handled = Utc(2025, 10, 25, 1, 0);

        // Act
        var firstPass = schedule.IsDue(Utc(2025, 10, 26, 0, 0), handled);
        var repeated = schedule.IsDue(Utc(2025, 10, 26, 1, 0), Utc(2025, 10, 26, 0, 0));

        // Assert
        Assert.True(firstPass);
        Assert.False(repeated);
    }

    /// <summary>
    ///     Test that the local date of an instant is converted to the zone.
    /// </summary>
    [Fact]
    public void DigestSchedule_LocalDate_Instant_ConvertsToZone()
    {
        // Arrange: 22:30 UTC is already the next day three hours ahead
        var schedule = new DigestSchedule(8, _plusThree);

        // Act
        var date = schedule.LocalDate(Utc(2025, 3, 3, 22, 30));

        // Assert
        Assert.Equal(new DateOnly(2025, 3, 4), date);
    }

    /// <summary>
    ///     Test that an hour outside 0 to 23 is refused.
    /// </summary>
    [Fact]
    public void DigestSchedule_Constructor_Hour24_Throws()
    {
        // Arrange, Act and Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new DigestSchedule(24, TimeZoneInfo.Utc));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DigestSchedule(-1, TimeZoneInfo.Utc));
    }

    /// <summary>
    ///     Test that a missing time zone is refused.
    /// </summary>
    [Fact]
    public void DigestSchedule_Constructor_NullZone_Throws()
    {
        // Arrange, Act and Assert
        Assert.Throws<ArgumentNullException>(() => new DigestSchedule(8, null!));
    }

    /// <summary>
    ///     Makes an instant in UTC.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <param name="month">The month.</param>
    /// <param name="day">The day.</param>
    /// <param name="hour">The hour.</param>
    /// <param name="minute">The minute.</param>
    /// <returns>The instant.</returns>
    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
    }

    /// <summary>
    ///     Creates the time zone with a clock change.
    /// </summary>
    /// <returns>The time zone.</returns>
    private static TimeZoneInfo CreateDaylightSavingZone()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified), 3, 30);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0, DateTimeKind.Unspecified), 10, 26);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone(
            "plus-one-dst", TimeSpan.FromHours(1), "plus-one-dst", "plus-one-dst", "plus-one-dst-summer", [rule]);
    }
}
