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
///     Where a review stands in its life, derived from its reviewer slots and signatures.
/// </summary>
/// <remarks>
///     The stored <see cref="ReviewState"/> only ever says <see cref="ReviewState.Draft"/>, so it cannot tell a lead
///     anything; the stage is computed on demand and never stored. Overdue, blocked and reopened are flags of
///     <see cref="ReviewProgress"/>, not stages.
/// </remarks>
public enum ReviewStage
{
    /// <summary>The review has no reviewer slot yet, so nobody is expected to sign.</summary>
    Setup,

    /// <summary>The review has reviewer slots and at least one has no current signature.</summary>
    InReview,

    /// <summary>The review is approved: every slot holds a current signature, no thread is open, and the review is locked.</summary>
    Approved
}

/// <summary>
///     The progress of one review of a project, as the overview of the project shows it.
/// </summary>
/// <remarks>
///     Computed on demand by <see cref="ReviewProgressService"/> from the stored record, the slots, the signatures and
///     the audit trail; nothing here is stored, so it cannot disagree with them. An immutable value holder that is
///     safe to share between threads.
/// </remarks>
/// <param name="ReviewId">The review identifier.</param>
/// <param name="Title">The review title.</param>
/// <param name="Stage">Where the review stands; see <see cref="ReviewStage"/>.</param>
/// <param name="SlotsRequired">The number of reviewer slots, each of which must hold a current signature.</param>
/// <param name="SlotsSigned">The number of slots that hold a signature of the current record.</param>
/// <param name="SlotsOutOfDate">The number of slots whose signatures are all of an earlier record.</param>
/// <param name="WaitingOn">
///     The display names of the people whose signature is missing: the primary reviewer and, when it is another
///     person, the alternate of every slot that is not signed or out of date, each once. A person whose account is
///     disabled carries the suffix <see cref="ReviewProgressService.DisabledSuffix"/>. Empty for an approved review.
/// </param>
/// <param name="OpenThreads">The number of comment threads that are open.</param>
/// <param name="UnansweredChecks">The number of checks of applicable sections that have no answer.</param>
/// <param name="FailedChecks">The number of checks of applicable sections answered Fail.</param>
/// <param name="NoChecklist"><c>true</c> when the review has no checklist, which blocks signing.</param>
/// <param name="Created">When the review was created.</param>
/// <param name="LastActivity">
///     When the review last had an entry in the audit trail; the creation time for a review without one.
/// </param>
/// <param name="DueDate">The due date (a UTC calendar day), or <c>null</c> when there is none.</param>
/// <param name="IsOverdue"><c>true</c> when the due date is before today (UTC) and the review is not approved.</param>
/// <param name="IsApproved"><c>true</c> when every slot holds a current signature and no thread is open.</param>
/// <param name="Reopened"><c>true</c> when the review was reopened after an approval and is not approved now.</param>
/// <param name="IsBlocked">
///     <c>true</c> when the review is not approved and has no checklist, an open thread, an unanswered check or a
///     check answered Fail.
/// </param>
/// <param name="FollowsReviewId">The review this one follows up, or <c>null</c> when it is not a follow-up.</param>
public sealed record ReviewProgress(
    string ReviewId,
    string Title,
    ReviewStage Stage,
    int SlotsRequired,
    int SlotsSigned,
    int SlotsOutOfDate,
    IReadOnlyList<string> WaitingOn,
    int OpenThreads,
    int UnansweredChecks,
    int FailedChecks,
    bool NoChecklist,
    DateTimeOffset Created,
    DateTimeOffset LastActivity,
    DateOnly? DueDate,
    bool IsOverdue,
    bool IsApproved,
    bool Reopened,
    bool IsBlocked,
    string? FollowsReviewId);
