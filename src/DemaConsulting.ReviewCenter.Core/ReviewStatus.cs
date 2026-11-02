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
///     One reviewer slot in the "Where are we?" panel: who holds it, how far the ballot is and whether it is signed.
/// </summary>
/// <remarks>
///     It carries the state of the ballot and never what it says. The record is an immutable value holder.
/// </remarks>
/// <param name="SlotId">The identity of the slot.</param>
/// <param name="Number">The position of the slot, starting at 1.</param>
/// <param name="Name">The display name of the primary reviewer.</param>
/// <param name="Ballot">Whether the reviewer has not started, has a draft, or has handed in the ballot.</param>
/// <param name="Signature">Whether the slot is signed for the review as it is now, or was signed before it changed.</param>
/// <param name="IsYou">Whether the viewer holds this slot.</param>
public sealed record ReviewerStatus(
    int SlotId,
    int Number,
    string Name,
    BallotState Ballot,
    SlotSignatureState Signature,
    bool IsYou);

/// <summary>
///     A check the reviewers answered differently, as the panel shows it to a viewer who may see that.
/// </summary>
/// <param name="ItemId">The check identifier.</param>
/// <param name="Text">The wording of the check.</param>
/// <param name="CanDiscuss">
///     Whether the viewer can start a discussion about the check now: they may start one, and none is open yet.
/// </param>
public sealed record DisputedCheck(string ItemId, string Text, bool CanDiscuss);

/// <summary>
///     One change to the review after a reviewer signed, in plain words.
/// </summary>
/// <param name="At">When it happened.</param>
/// <param name="By">Who did it.</param>
/// <param name="Reason">What changed, in a sentence that does not reveal anything the viewer may not see.</param>
public sealed record ReviewChange(DateTimeOffset At, string By, string Reason);

/// <summary>
///     What changed since a reviewer signed, for a slot whose signature no longer matches the review.
/// </summary>
/// <param name="SlotId">The identity of the slot.</param>
/// <param name="Number">The position of the slot, starting at 1.</param>
/// <param name="SignerName">The display name of the person who signed.</param>
/// <param name="SignedAt">When that person signed.</param>
/// <param name="Changes">The changes since then that alter the report, oldest first.</param>
public sealed record SignatureChanges(
    int SlotId,
    int Number,
    string SignerName,
    DateTimeOffset SignedAt,
    IReadOnlyList<ReviewChange> Changes);

/// <summary>
///     The "Where are we?" status of a review for one viewer: who has done what, what is disputed, what is open and
///     what to do next.
/// </summary>
/// <remarks>
///     <para>
///         It is computed on demand by <see cref="ReviewStatusService"/> and never stored. It never holds the content of
///         a ballot. For a viewer who may not see the answers of the other reviewers yet (see
///         <see cref="BallotVisibility"/>), <see cref="DisputedChecks"/> is empty, the open threads about checks are not
///         counted and <see cref="IsMasked"/> is <c>true</c>.
///     </para>
///     <para>
///         <see cref="ChangesSinceSigning"/> is derived from the audit trail: it lists the entries after the signature
///         that alter the report (a ballot handed in or taken back, the conclusion text, and the questions and findings
///         that go in the report). A reply or a comment is listed only when its entry says it is in the report. The list
///         explains the usual reasons a signature no longer matches; it is not a proof. The record is an immutable value
///         holder and is safe to share between threads.
///     </para>
/// </remarks>
/// <param name="IsMasked">
///     <c>true</c> when the viewer may not see the answers of the other reviewers yet, so disputes and discussions about
///     checks are left out.
/// </param>
/// <param name="IsConcluded">Whether the review is concluded.</param>
/// <param name="HasChecklist">Whether the review has a checklist, so the ballot states mean something.</param>
/// <param name="Reviewers">The slots in order.</param>
/// <param name="DisputedChecks">The checks the submitted ballots answer differently; empty when masked.</param>
/// <param name="OpenQuestions">How many questions are open, among those the viewer may see.</param>
/// <param name="OpenFindings">How many findings are open, among those the viewer may see.</param>
/// <param name="NextAction">The one thing the viewer should do next.</param>
/// <param name="ChangesSinceSigning">The changes since signing, for each slot that was signed before the review changed.</param>
public sealed record ReviewStatus(
    bool IsMasked,
    bool IsConcluded,
    bool HasChecklist,
    IReadOnlyList<ReviewerStatus> Reviewers,
    IReadOnlyList<DisputedCheck> DisputedChecks,
    int OpenQuestions,
    int OpenFindings,
    ReviewNextAction NextAction,
    IReadOnlyList<SignatureChanges> ChangesSinceSigning);
