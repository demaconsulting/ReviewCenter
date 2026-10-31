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
///     Whether one section of a review's checklist applies to the review, and why not when it does not.
/// </summary>
/// <remarks>
///     A row exists only once a reviewer has set the applicability; until then the section applies.
///     The person who set it comes from the signed-in user (see <see cref="Actor"/>). Instances are created
///     by <see cref="ChecklistService"/>, which enforces the rules. The type is not thread-safe.
/// </remarks>
public sealed class ChecklistSectionState
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
    ///     Gets the identifier of the section within the frozen checklist.
    /// </summary>
    public string SectionId { get; init; } = string.Empty;

    /// <summary>
    ///     Gets a value indicating whether the section applies to the review.
    /// </summary>
    public bool Applicable { get; init; } = true;

    /// <summary>
    ///     Gets the reason the section does not apply (trimmed, at most <see cref="ReviewLimits.MaxJustificationLength"/>
    ///     characters); <c>null</c> exactly when the section applies.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    ///     Gets the display name of the person who set the applicability, at the time it was set.
    /// </summary>
    public string SetBy { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the user name of the signed-in person who set the applicability.
    /// </summary>
    public string SetByUsername { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the moment the applicability was set (UTC).
    /// </summary>
    public DateTimeOffset SetAt { get; init; }

    /// <summary>
    ///     Gets the identifier of the earlier review this applicability was carried over from when the review was
    ///     created as a follow-up (a section that did not apply, with its reason), or <c>null</c> when it was set in
    ///     this review. Setting the applicability again stores a state without the marker. The marker is part of the
    ///     record digest.
    /// </summary>
    public string? CarriedFromReviewId { get; init; }
}
