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
///     A user as shown in a reviewer slot, without anything secret.
/// </summary>
/// <remarks>
///     The view models of the signing feature carry this record instead of <see cref="User"/>, so a password
///     hash can never reach a page.
/// </remarks>
/// <param name="UserId">The identity of the user.</param>
/// <param name="Username">The user name.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="IsMember">
///     <c>false</c> when the user is no longer a member of the project of the review (so the user cannot sign until
///     added again); <c>true</c> for a current member and for the candidates of a slot, who are members by definition.
/// </param>
/// <param name="Enabled">
///     <c>false</c> when the account of the user is disabled (so the user cannot sign); <c>true</c> for an enabled
///     account and for the candidates of a slot, who are enabled by definition.
/// </param>
public sealed record SlotMember(
    int UserId,
    string Username,
    string DisplayName,
    bool IsMember = true,
    bool Enabled = true);

/// <summary>
///     Whether a reviewer slot holds a signature for the record as it is now.
/// </summary>
public enum SlotSignatureState
{
    /// <summary>The slot has no signature.</summary>
    NotSigned,

    /// <summary>The slot has a signature of the current record.</summary>
    Signed,

    /// <summary>The slot has signatures, but none of the record as it is now.</summary>
    OutOfDate
}

/// <summary>
///     The status of one reviewer slot.
/// </summary>
/// <param name="SlotId">The identity of the slot.</param>
/// <param name="Number">The position of the slot among the slots of the review, starting at 1 ("Slot 1").</param>
/// <param name="Primary">The primary reviewer.</param>
/// <param name="Alternate">The alternate reviewer, or <c>null</c> when the slot has none.</param>
/// <param name="State">Whether the slot has a current signature.</param>
/// <param name="SignerName">The display name of the signer of the current signature; <c>null</c> unless <see cref="State"/> is <see cref="SlotSignatureState.Signed"/>.</param>
/// <param name="SignedAt">The time of the current signature; <c>null</c> unless <see cref="State"/> is <see cref="SlotSignatureState.Signed"/>.</param>
public sealed record SlotStatus(
    int SlotId,
    int Number,
    SlotMember Primary,
    SlotMember? Alternate,
    SlotSignatureState State,
    string? SignerName,
    DateTimeOffset? SignedAt);

/// <summary>
///     The approval status of a review: its slots, the current record digest and what blocks signing.
/// </summary>
/// <remarks>
///     The status is computed on demand from the stored record, slots and signatures and is never stored, so it
///     cannot disagree with them. It is an immutable value holder and is safe to share between threads.
/// </remarks>
/// <param name="IsApproved">
///     <c>true</c> when the review is concluded: it has at least one slot, every slot has a current signature and no
///     thread is open (see <see cref="ReviewLock.IsConcluded"/>). A concluded review is
///     also locked (see <see cref="ReviewLock"/>); there is no separate locked flag, so the two can never differ.
/// </param>
/// <param name="Digest">The digest of the review record as it is now.</param>
/// <param name="Slots">The slots in creation order.</param>
/// <param name="Blockers">The plain-language reasons the review cannot be signed yet; empty when it can be.</param>
/// <param name="NextStep">One short sentence that words what to do next.</param>
/// <param name="Reopens">
///     The history of reopens of the review, oldest first; <c>null</c> or empty for a review never reopened. It is
///     filled by <see cref="SigningService.GetApprovalAsync"/> and <see cref="SigningService.GetApprovalsAsync"/>.
/// </param>
/// <param name="OpenThreads">The number of comment threads that are open; the count behind the "threads are open" blocker.</param>
/// <param name="UnansweredChecks">
///     The number of checks of applicable sections that have no answer; the count behind the "checks are unanswered"
///     blocker. Zero for a review without a checklist.
/// </param>
/// <param name="FailedChecks">
///     The number of checks of applicable sections answered Fail; the count behind the "checks are answered Fail"
///     blocker. Zero for a review without a checklist.
/// </param>
/// <param name="MissingChecklist">
///     <c>true</c> when the review has no checklist; the structured form of the "This review has no checklist."
///     blocker, so it is <c>true</c> exactly when that sentence is among <paramref name="Blockers"/>.
/// </param>
/// <param name="DisputedChecks">
///     The number of checks and sections on which the submitted ballots disagree; the count behind the "reviewers
///     disagree" blocker. Zero for a review without a checklist.
/// </param>
/// <param name="UnsubmittedBallots">
///     The number of slots whose ballot has not been handed in (a draft, or none yet); zero for a review without a
///     checklist. Every such slot counts, whatever the state of the answers. A single-slot review hands the ballot in
///     with the last answer, but a complete single-slot draft that needs no answer (for example because all checks
///     were carried over) is not handed in automatically, so it still counts one until it is submitted.
/// </param>
public sealed record ReviewApproval(
    bool IsApproved,
    string Digest,
    IReadOnlyList<SlotStatus> Slots,
    IReadOnlyList<string> Blockers,
    string NextStep,
    IReadOnlyList<ReviewReopen>? Reopens = null,
    int OpenThreads = 0,
    int UnansweredChecks = 0,
    int FailedChecks = 0,
    bool MissingChecklist = false,
    int DisputedChecks = 0,
    int UnsubmittedBallots = 0);
