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

    /// <summary>The review is approved: every slot holds a current signature of the outcome approved, no thread is open, and the review is locked.</summary>
    Approved,

    /// <summary>The review is rejected: every slot holds a current signature of the outcome rejected, the conclusion states the reasons, no thread is open, and the review is locked.</summary>
    Rejected
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
///     disabled carries the suffix <see cref="ReviewProgressService.DisabledSuffix"/>. Empty for a concluded (approved or rejected) review.
/// </param>
/// <param name="OpenThreads">The number of comment threads that are open: <paramref name="OpenQuestions"/> plus <paramref name="OpenFindings"/>.</param>
/// <param name="UnansweredChecks">The number of checks of applicable sections that have no answer.</param>
/// <param name="FailedChecks">
///     The number of checks of applicable sections the ballots agree on as Fail; informational, not a blocker.
/// </param>
/// <param name="NoChecklist"><c>true</c> when the review has no checklist, which blocks signing.</param>
/// <param name="Created">When the review was created.</param>
/// <param name="LastActivity">
///     When the review last had an entry in the audit trail; the creation time for a review without one.
/// </param>
/// <param name="DueDate">The due date (a UTC calendar day), or <c>null</c> when there is none.</param>
/// <param name="IsOverdue"><c>true</c> when the due date is before today (UTC) and the review is not concluded.</param>
/// <param name="Conclusion">
///     How the review is concluded: approved or rejected when every slot holds a current signature of that outcome and
///     no thread is open, otherwise none.
/// </param>
/// <param name="Reopened"><c>true</c> when the review was reopened after it was concluded and is not concluded now.</param>
/// <param name="IsBlocked">
///     <c>true</c> when the review is not concluded and has no checklist, an open thread, an unanswered or disputed
///     check, or a rejected outcome that still needs its conclusion text.
/// </param>
/// <param name="FollowsReviewId">The review this one follows up, or <c>null</c> when it is not a follow-up.</param>
/// <param name="ConclusionRequired">
///     <c>true</c> when the outcome is rejected and the review has no conclusion text yet.
/// </param>
/// <param name="DisputedChecks">
///     The number of checks of applicable sections on which the submitted ballots disagree; each blocks signing.
/// </param>
/// <param name="OpenQuestions">The number of open threads that are Questions (chat).</param>
/// <param name="OpenFindings">The number of open threads that are Findings (part of the report).</param>
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
    ReviewConclusion Conclusion,
    bool Reopened,
    bool IsBlocked,
    string? FollowsReviewId,
    bool ConclusionRequired = false,
    int DisputedChecks = 0,
    int OpenQuestions = 0,
    int OpenFindings = 0)
{
    /// <summary>
    ///     Gets a value indicating whether the review is concluded as approved.
    /// </summary>
    public bool IsApproved => Conclusion == ReviewConclusion.Approved;

    /// <summary>
    ///     Gets a value indicating whether the review is concluded, as approved or as rejected.
    /// </summary>
    public bool IsConcluded => Conclusion != ReviewConclusion.None;

    /// <summary>
    ///     Gets the open threads in words, for example "2 open findings, 1 open question"; "no open threads" when
    ///     none is open.
    /// </summary>
    public string OpenThreadsText => OpenQuestions + OpenFindings == 0 && OpenThreads > 0
        ? Plural(OpenThreads)
        : ThreadCounts.OpenPhrase(OpenQuestions, OpenFindings);

    /// <summary>
    ///     Words a number of threads of unknown kind, for a progress that carries no counts by kind.
    /// </summary>
    /// <param name="count">The number of open threads.</param>
    /// <returns>The count and the word.</returns>
    private static string Plural(int count)
    {
        return count == 1 ? "1 open thread" : $"{count} open threads";
    }
}
