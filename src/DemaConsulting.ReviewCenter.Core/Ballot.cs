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
///     The answers and section applicability that one reviewer slot gives to the checklist of a review.
/// </summary>
/// <remarks>
///     A ballot is the unit that <see cref="BallotConsolidation"/> compares: each reviewer slot has at most one,
///     and the consolidation counts it only once <see cref="Status"/> is <see cref="BallotStatus.Submitted"/>.
///     The record is immutable and holds the answers it is given; it does not validate them, because the
///     services that create ballots enforce the rules. It is thread-safe as long as the lists are not
///     changed.
/// </remarks>
/// <param name="SlotId">The identity of the reviewer slot the ballot belongs to.</param>
/// <param name="Status">Whether the ballot is still a draft or has been handed in.</param>
/// <param name="SubmittedAt">The moment the ballot was handed in (UTC); <c>null</c> for a draft.</param>
/// <param name="SubmittedByUsername">The user name of the person who handed the ballot in; <c>null</c> for a draft.</param>
/// <param name="Answers">The answers to the checks, at most one for each check.</param>
/// <param name="SectionStates">
///     The sections that this ballot marks as applicable or not applicable; a section without a state applies.
/// </param>
public sealed record Ballot(
    int SlotId,
    BallotStatus Status,
    DateTimeOffset? SubmittedAt,
    string? SubmittedByUsername,
    IReadOnlyList<ChecklistAnswer> Answers,
    IReadOnlyList<ChecklistSectionState> SectionStates)
{
    /// <summary>
    ///     Gets the display state of the ballot.
    /// </summary>
    /// <remarks>
    ///     This is <see cref="BallotState.Submitted"/> or <see cref="BallotState.Draft"/>; an existing ballot is
    ///     never <see cref="BallotState.NotStarted"/>. That value is for read models that show a slot with no
    ///     ballot at all, which callers represent by the absence of a <see cref="Ballot"/>.
    /// </remarks>
    public BallotState State => Status == BallotStatus.Submitted ? BallotState.Submitted : BallotState.Draft;
}
