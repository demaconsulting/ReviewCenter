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
///     What the notification dispatcher remembers about one user between two passes.
/// </summary>
/// <remarks>
///     The values are stored with the preference row, so a restart of the server keeps a batch window, a retry
///     schedule and the day of the last summary. The record is immutable and thread-safe.
/// </remarks>
/// <param name="BaselinePending">
///     <c>true</c> while the items that exist now must be recorded as already known, without a mail, because the
///     user has just switched a mode on.
/// </param>
/// <param name="BatchStartedUtc">When the batch window of the mode <see cref="NotificationMode.Every"/> started (UTC); <c>null</c> when none runs.</param>
/// <param name="DigestHandledUtc">When the last daily slot was handled (UTC); <c>null</c> when none was.</param>
/// <param name="FailureCount">How many sends in a row failed.</param>
/// <param name="NextAttemptUtc">The earliest moment of the next attempt after a failure (UTC); <c>null</c> when none waits.</param>
public sealed record NotificationDispatchState(
    bool BaselinePending,
    DateTimeOffset? BatchStartedUtc,
    DateTimeOffset? DigestHandledUtc,
    int FailureCount,
    DateTimeOffset? NextAttemptUtc)
{
    /// <summary>
    ///     Gets the state of a user the dispatcher knows nothing about.
    /// </summary>
    public static NotificationDispatchState Empty { get; } = new(false, null, null, 0, null);
}
