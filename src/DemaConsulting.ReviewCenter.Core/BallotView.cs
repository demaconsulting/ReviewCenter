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
///     What one reviewer answered to a check, as shown next to the answers of the other reviewers.
/// </summary>
/// <param name="SlotNumber">The position of the reviewer's slot among the slots of the review, starting at 1.</param>
/// <param name="ReviewerName">The display name of the reviewer who holds the slot (the primary reviewer).</param>
/// <param name="Outcome">The answer.</param>
/// <param name="Justification">The justification of the answer; <c>null</c> when there is none.</param>
public sealed record BallotAnswerView(int SlotNumber, string ReviewerName, ChecklistOutcome Outcome, string? Justification);

/// <summary>
///     What the checklist page needs to know about the ballots, for one viewer: whether the answers of the other
///     reviewers are hidden, the checks the submitted ballots answer differently, and which ballot actions are offered.
/// </summary>
/// <remarks>
///     The view is computed by <see cref="ChecklistService.GetBallotViewAsync"/> and is never stored. While the viewer
///     may not see the other reviewers' answers (see <see cref="BallotVisibility"/>), nothing about them is in it: no
///     check differs, and the counts say only how many ballots are handed in. It is an immutable value holder and is
///     safe to share between threads.
/// </remarks>
/// <param name="IsMasked">
///     <c>true</c> when the viewer may not see the answers of the other reviewers yet; the review has more than one slot
///     and the viewer has not handed in their own ballot (and sees everything for no other reason).
/// </param>
/// <param name="Differing">
///     The checks that the submitted ballots answer differently, by check identifier, each with the answer of every
///     submitted ballot in slot order; empty while <paramref name="IsMasked"/> is <c>true</c>.
/// </param>
/// <param name="BallotsSubmitted">The number of ballots handed in; who has handed in is not hidden.</param>
/// <param name="BallotsTotal">The number of reviewer slots.</param>
/// <param name="CanSubmitAndSign">
///     <c>true</c> when the viewer's ballot is a complete draft of a review with several slots and nothing the viewer is
///     allowed to see blocks signing once it is handed in, so "Submit and sign" is the natural next step.
/// </param>
/// <param name="CanAssignMe">
///     <c>true</c> when the viewer holds no slot but could add themselves as the primary reviewer of a new slot.
/// </param>
public sealed record BallotView(
    bool IsMasked,
    IReadOnlyDictionary<string, IReadOnlyList<BallotAnswerView>> Differing,
    int BallotsSubmitted,
    int BallotsTotal,
    bool CanSubmitAndSign,
    bool CanAssignMe);
