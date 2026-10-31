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
///     There is at most one answer per check; answering again replaces it. The person who answered comes
///     from the signed-in user (see <see cref="Actor"/>). Instances are created by
///     <see cref="ChecklistService"/>, which enforces the rules. The type is not thread-safe.
/// </remarks>
public sealed class ChecklistAnswer
{
    /// <summary>
    ///     Gets the identity of the owning project.
    /// </summary>
    public int ProjectId { get; init; }

    /// <summary>
    ///     Gets the identifier of the owning review.
    /// </summary>
    public string ReviewId { get; init; } = string.Empty;

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
