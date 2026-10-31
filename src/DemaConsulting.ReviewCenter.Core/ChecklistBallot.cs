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
///     The stored status row of the ballot of one reviewer slot: whether the slot's answers are still a draft or
///     have been handed in.
/// </summary>
/// <remarks>
///     The answers and section states of the ballot are separate rows that carry the same <see cref="SlotId"/>; this
///     row only holds the status. A slot that was never started has no row (the display state
///     <see cref="BallotState.NotStarted"/>). A <see cref="BallotStatus.Submitted"/> row always has
///     <see cref="SubmittedAt"/> and <see cref="SubmittedByUsername"/>; a <see cref="BallotStatus.Draft"/> row has
///     neither. The rows are written only by the store (see <see cref="IReviewStore"/>), which keeps that invariant
///     and removes the row with its slot. The type is not thread-safe.
/// </remarks>
public sealed class ChecklistBallot
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
    ///     Gets the identity of the reviewer slot the ballot belongs to; always greater than 0.
    /// </summary>
    public int SlotId { get; init; }

    /// <summary>
    ///     Gets the stored status of the ballot.
    /// </summary>
    public BallotStatus Status { get; init; }

    /// <summary>
    ///     Gets the moment the ballot was handed in (UTC); <c>null</c> for a draft.
    /// </summary>
    public DateTimeOffset? SubmittedAt { get; init; }

    /// <summary>
    ///     Gets the user name of the person who handed the ballot in; <c>null</c> for a draft.
    /// </summary>
    public string? SubmittedByUsername { get; init; }
}
