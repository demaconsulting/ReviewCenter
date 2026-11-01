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
///     Answers "where does every review of this project stand, and who is holding it up?" for the project overview and
///     the API list.
/// </summary>
/// <remarks>
///     <para>
///         The progress of a review is computed on demand from what is already stored: the approval status of
///         <see cref="SigningService"/> (slots, signatures, blockers and their counts), the due date rule of
///         <see cref="ReviewService"/> and the newest audit entry of the review. Nothing new is stored, so the
///         overview cannot disagree with the review page. The stage is derived (<see cref="ReviewStage"/>), never the
///         stored review state.
///     </para>
///     <para>
///         A project overview needs a constant number of reads however many reviews the project has: one read of the
///         review list (none when a caller that already has the list passes it to the overload that takes it), one
///         batch read of all approvals (<see cref="SigningService.GetApprovalsAsync"/>, itself one
///         set-based read of the snapshots of all reviews, <see cref="IReviewStore.ListSnapshotsAsync"/>, plus one of
///         the reopen history of the reviews that were reopened) and one read
///         of the last activity of all reviews (<see cref="IAuditStore.LastActivityAsync"/>). Reading is open to
///         everyone who may see the project (members, auditors and system administrators); a stranger gets the same
///         "no such project" refusal as everywhere else. The service holds no state and is safe for concurrent use
///         when the services it uses are.
///     </para>
/// </remarks>
public sealed class ReviewProgressService
{
    /// <summary>
    ///     The suffix after the name of a person whose account is disabled in the list of people a review waits on.
    /// </summary>
    public const string DisabledSuffix = " (disabled)";

    /// <summary>
    ///     Lists and dates reviews.
    /// </summary>
    private readonly ReviewService _reviews;

    /// <summary>
    ///     Computes the approval status of reviews.
    /// </summary>
    private readonly SigningService _signing;

    /// <summary>
    ///     The audit trail, for the last activity of reviews.
    /// </summary>
    private readonly IAuditStore _audit;

    /// <summary>
    ///     Decides who may see a project.
    /// </summary>
    private readonly IProjectAccess _access;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ReviewProgressService"/> class.
    /// </summary>
    /// <param name="reviews">The review service that lists reviews and decides what is overdue. Must not be null.</param>
    /// <param name="signing">The signing service that computes approval statuses. Must not be null.</param>
    /// <param name="audit">The audit trail that supplies the last activity of reviews. Must not be null.</param>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public ReviewProgressService(
        ReviewService reviews,
        SigningService signing,
        IAuditStore audit,
        IProjectAccess access)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(signing);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(access);
        _reviews = reviews;
        _signing = signing;
        _audit = audit;
        _access = access;
    }

    /// <summary>
    ///     Gets the progress of every review of a project.
    /// </summary>
    /// <param name="actor">The caller; must be allowed to see the project.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The progress of each review ordered by review identifier; empty when the project has no reviews.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ProjectNotFound"/> when the actor may not see the project (or it does not exist).</exception>
    public async Task<IReadOnlyList<ReviewProgress>> GetProgressAsync(
        Actor? actor,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        await _access.RequireViewAsync(actor, projectId, cancellationToken);
        var reviews = await _reviews.ListAsync(projectId, cancellationToken);
        return await ComputeAsync(projectId, reviews, cancellationToken);
    }

    /// <summary>
    ///     Gets the progress of reviews of a project that the caller has already listed, so a caller that needs the
    ///     list itself reads it once.
    /// </summary>
    /// <param name="actor">The caller; must be allowed to see the project.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviews">The reviews of the project, as <see cref="ReviewService.ListAsync"/> returned them. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The progress of each of the given reviews that still exists, in the order given.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reviews"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ProjectNotFound"/> when the actor may not see the project (or it does not exist).</exception>
    public async Task<IReadOnlyList<ReviewProgress>> GetProgressAsync(
        Actor? actor,
        int projectId,
        IReadOnlyList<Review> reviews,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        await _access.RequireViewAsync(actor, projectId, cancellationToken);
        return await ComputeAsync(projectId, reviews, cancellationToken);
    }

    /// <summary>
    ///     Computes the progress of the given reviews; the caller has checked the access.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviews">The reviews of the project.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The progress of each review that still exists, in the order given.</returns>
    private async Task<IReadOnlyList<ReviewProgress>> ComputeAsync(
        int projectId,
        IReadOnlyList<Review> reviews,
        CancellationToken cancellationToken)
    {
        if (reviews.Count == 0)
        {
            return [];
        }

        // A constant number of reads, however many reviews the project has
        var approvals = await _signing.GetApprovalsAsync(projectId, reviews.Select(r => r.Id), cancellationToken);
        var activity = await _audit.LastActivityAsync(projectId, cancellationToken);

        var result = new List<ReviewProgress>(reviews.Count);
        foreach (var review in reviews)
        {
            if (!approvals.TryGetValue(review.Id, out var approval))
            {
                // Removed between the two reads; leave it out rather than invent a status
                continue;
            }

            result.Add(Build(review, approval, activity));
        }

        return result;
    }

    /// <summary>
    ///     Derives the progress of one review.
    /// </summary>
    /// <param name="review">The review.</param>
    /// <param name="approval">The approval status of the review.</param>
    /// <param name="activity">The newest audit time of each review of the project.</param>
    /// <returns>The progress.</returns>
    private ReviewProgress Build(
        Review review,
        ReviewApproval approval,
        IReadOnlyDictionary<string, DateTimeOffset> activity)
    {
        var stage = ReviewStage.InReview;
        if (approval.Slots.Count == 0)
        {
            stage = ReviewStage.Setup;
        }
        else if (approval.IsApproved)
        {
            stage = ReviewStage.Approved;
        }
        else if (approval.IsConcluded)
        {
            stage = ReviewStage.Rejected;
        }

        // An agreed Fail answer is not a blocker (it leads to a rejected outcome); a missing conclusion text is
        var blocked = !approval.IsConcluded
            && (approval.OpenThreads > 0
                || approval.UnansweredChecks > 0
                || approval.DisputedChecks > 0
                || approval.ConclusionRequired
                || approval.MissingChecklist);

        return new ReviewProgress(
            review.Id,
            review.Title,
            stage,
            approval.Slots.Count,
            approval.Slots.Count(s => s.State == SlotSignatureState.Signed),
            approval.Slots.Count(s => s.State == SlotSignatureState.OutOfDate),
            approval.IsConcluded ? [] : WaitingOn(approval.Slots),
            approval.OpenThreads,
            approval.UnansweredChecks,
            approval.FailedChecks,
            approval.MissingChecklist,
            review.Created,
            activity.TryGetValue(review.Id, out var last) ? last : review.Created,
            review.DueDate,
            _reviews.IsOverdue(review, approval.IsConcluded),
            approval.Conclusion,
            !approval.IsConcluded && approval.Reopens is { Count: > 0 },
            blocked,
            review.FollowsReviewId,
            approval.ConclusionRequired,
            approval.DisputedChecks);
    }

    /// <summary>
    ///     Names the people whose signature is missing.
    /// </summary>
    /// <param name="slots">The slots of the review.</param>
    /// <returns>
    ///     For each slot that is not signed or is out of date, the primary reviewer and the alternate when that is
    ///     another person; each person once, in slot order.
    /// </returns>
    private static List<string> WaitingOn(IReadOnlyList<SlotStatus> slots)
    {
        var names = new List<string>();
        var seen = new HashSet<int>();
        foreach (var slot in slots.Where(s => s.State != SlotSignatureState.Signed))
        {
            Add(slot.Primary);
            if (slot.Alternate is not null && slot.Alternate.UserId != slot.Primary.UserId)
            {
                Add(slot.Alternate);
            }
        }

        return names;

        void Add(SlotMember member)
        {
            if (seen.Add(member.UserId))
            {
                names.Add(member.Enabled ? member.DisplayName : member.DisplayName + DisabledSuffix);
            }
        }
    }
}
