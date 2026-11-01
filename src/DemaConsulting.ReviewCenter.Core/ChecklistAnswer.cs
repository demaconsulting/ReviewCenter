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
///     The recorded answer to one check of a review's checklist.
/// </summary>
/// <remarks>
///     There is at most one answer per check in a ballot (or at the review level); answering again replaces it. What a
///     review shows for a check is the answer its submitted ballots agree on (the row of the lowest slot that gave it); a
///     pending or disputed check shows as "Not answered". The person who answered comes
///     from the signed-in user (see <see cref="Actor"/>). Instances are created by
///     <see cref="ChecklistService"/>, which enforces the rules. The type is not thread-safe.
/// </remarks>
public sealed class ChecklistAnswer
{
    /// <summary>
    ///     The value of <see cref="SlotId"/> for a review-level answer, one that belongs to no reviewer slot.
    /// </summary>
    /// <remarks>
    ///     An answer lives in the ballot of the reviewer slot that gave it (<see cref="SlotId"/> greater than 0). Only
    ///     the baseline that a follow-up review carries from the earlier review before any slot exists uses this value;
    ///     the ballot of a new slot is seeded from it. Such a row is never a ballot and is not part of the record or
    ///     its digest, because no reviewer slot has the identity 0.
    /// </remarks>
    public const int BaselineSlotId = 0;

    /// <summary>
    ///     Gets the identity of the owning project.
    /// </summary>
    public int ProjectId { get; init; }

    /// <summary>
    ///     Gets the identifier of the owning review.
    /// </summary>
    public string ReviewId { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the identity of the reviewer slot whose ballot holds the answer, or <see cref="BaselineSlotId"/> (the
    ///     default) for a review-level answer.
    /// </summary>
    public int SlotId { get; init; } = BaselineSlotId;

    /// <summary>
    ///     Gets the identifier of the check within the frozen checklist (for example <c>REQ-01</c>).
    /// </summary>
    public string ItemId { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the recorded outcome.
    /// </summary>
    public ChecklistOutcome Outcome { get; init; }

    /// <summary>
    ///     Gets the justification (trimmed, at most <see cref="ReviewLimits.MaxJustificationLength"/> characters);
    ///     never empty for <see cref="ChecklistOutcome.Fail"/> and <see cref="ChecklistOutcome.NotApplicable"/>,
    ///     <c>null</c> for a pass without one.
    /// </summary>
    public string? Justification { get; init; }

    /// <summary>
    ///     Gets the display name of the person who answered, at the time of answering.
    /// </summary>
    public string AnsweredBy { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the user name of the signed-in person who answered.
    /// </summary>
    public string AnsweredByUsername { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the moment the check was answered (UTC).
    /// </summary>
    public DateTimeOffset AnsweredAt { get; init; }

    /// <summary>
    ///     Gets the identifier of the earlier review this answer was carried over from when the review was created as a
    ///     follow-up, or <c>null</c> for an answer given in this review.
    /// </summary>
    /// <remarks>
    ///     The answerer and the time are those of the original answer; carrying never makes the creator the answerer.
    ///     Answering the check again (even with the same value) stores an answer without the marker. The marker is part
    ///     of the record digest.
    /// </remarks>
    public string? CarriedFromReviewId { get; init; }
}
