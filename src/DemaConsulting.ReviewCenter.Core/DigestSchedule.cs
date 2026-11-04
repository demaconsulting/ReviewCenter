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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     Decides when the daily summary of a local date is due: at the digest hour of a configured time zone.
/// </summary>
/// <remarks>
///     A slot is due when the local hour has reached the digest hour and the slot of the local date was not handled
///     yet (the last handled slot lies on an earlier local date, or there is none). Comparing local dates, not
///     elapsed time, keeps the rule right on the days a clock changes: the hour that a spring-forward change skips
///     is covered by "the hour has reached", the hour that a fall-back change repeats is covered by "not handled
///     yet today". After a downtime the slot is due as soon as the server runs again. The type is immutable and
///     thread-safe.
/// </remarks>
public sealed class DigestSchedule
{
    /// <summary>
    ///     The local hour (0 to 23) at which the slot of a day opens.
    /// </summary>
    private readonly int _hour;

    /// <summary>
    ///     The time zone whose local time counts.
    /// </summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DigestSchedule"/> class.
    /// </summary>
    /// <param name="hour">The local hour at which the slot opens, 0 to 23.</param>
    /// <param name="zone">The time zone. Must not be null.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="hour"/> is outside 0 to 23.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zone"/> is null.</exception>
    public DigestSchedule(int hour, TimeZoneInfo zone)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hour, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hour, 23);
        ArgumentNullException.ThrowIfNull(zone);
        _hour = hour;
        _zone = zone;
    }

    /// <summary>
    ///     Gets the local date of an instant in the time zone of the schedule.
    /// </summary>
    /// <param name="utc">The instant.</param>
    /// <returns>The local date.</returns>
    public DateOnly LocalDate(DateTimeOffset utc)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, _zone).DateTime);
    }

    /// <summary>
    ///     Tells whether the slot of the current local date is due and not handled yet.
    /// </summary>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="handledUtc">When the last slot was handled; <c>null</c> when none was.</param>
    /// <returns><c>true</c> when the local hour has reached the digest hour and no slot was handled on this local date.</returns>
    public bool IsDue(DateTimeOffset nowUtc, DateTimeOffset? handledUtc)
    {
        var local = TimeZoneInfo.ConvertTime(nowUtc, _zone);
        if (local.Hour < _hour)
        {
            return false;
        }

        return handledUtc is not { } handled || LocalDate(handled) < DateOnly.FromDateTime(local.DateTime);
    }
}
