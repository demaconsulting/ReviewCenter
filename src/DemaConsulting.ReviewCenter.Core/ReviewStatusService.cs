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
///     Answers "where are we in this review, and what should I do next?" for the review page.
/// </summary>
/// <remarks>
///     <para>
///         The status is computed on demand from what the other services already offer, each for the same viewer: the
///         approval status of <see cref="SigningService"/> (slots, signatures, blockers), the ballot view of
///         <see cref="ChecklistService"/> (disputes, and whether they may be seen), the threads and what the viewer may
///         start of <see cref="ThreadService"/>, and the audit trail of the review for what changed since a reviewer
///         signed. Nothing is stored, so the panel cannot disagree with the rest of the page.
///     </para>
///     <para>
///         The status never holds the content of a ballot. While the viewer may not see the answers of the other
///         reviewers (see <see cref="BallotVisibility"/>), it holds no dispute, counts no discussion about a check, and
///         words the audit entries that the audit masking hides with one neutral sentence (see
///         <see cref="AuditMasking"/>). Reading is open to everyone who may see the project. The service holds no
///         state and is safe for concurrent use when the services it uses are.
///     </para>
/// </remarks>
public sealed class ReviewStatusService
{
    /// <summary>
    ///     The number of audit entries read in one batch.
    /// </summary>
    private const int BatchSize = 200;

    /// <summary>
    ///     The largest number of audit entries of one review that are read; a review has far fewer.
    /// </summary>
    private const int MaxEntries = 20_000;

    /// <summary>
    ///     The store of reviews, slots, ballots and signatures.
    /// </summary>
    private readonly IReviewStore _store;

    /// <summary>
    ///     The store of users, to find the viewer.
    /// </summary>
    private readonly IUserStore _users;

    /// <summary>
    ///     Decides who may see and take part in a project.
    /// </summary>
    private readonly IProjectAccess _access;

    /// <summary>
    ///     Computes the approval status of reviews.
    /// </summary>
    private readonly SigningService _signing;

    /// <summary>
    ///     Computes what the viewer may do about the checklist and the ballots.
    /// </summary>
    private readonly ChecklistService _checklists;

    /// <summary>
    ///     Lists the threads the viewer may see.
    /// </summary>
    private readonly ThreadService _threads;

    /// <summary>
    ///     The audit trail, for what changed since a signature.
    /// </summary>
    private readonly IAuditStore _audit;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ReviewStatusService"/> class.
    /// </summary>
    /// <param name="store">The store of reviews. Must not be null.</param>
    /// <param name="users">The store of users. Must not be null.</param>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <param name="signing">The signing service that computes approval statuses. Must not be null.</param>
    /// <param name="checklists">The checklist service that computes the ballot view. Must not be null.</param>
    /// <param name="threads">The thread service. Must not be null.</param>
    /// <param name="audit">The audit trail. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public ReviewStatusService(
        IReviewStore store,
        IUserStore users,
        IProjectAccess access,
        SigningService signing,
        ChecklistService checklists,
        ThreadService threads,
        IAuditStore audit)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(signing);
        ArgumentNullException.ThrowIfNull(checklists);
        ArgumentNullException.ThrowIfNull(threads);
        ArgumentNullException.ThrowIfNull(audit);
        _store = store;
        _users = users;
        _access = access;
        _signing = signing;
        _checklists = checklists;
        _threads = threads;
        _audit = audit;
    }

    /// <summary>
    ///     Gets the "Where are we?" status of a review for a viewer.
    /// </summary>
    /// <param name="actor">The signed-in user who looks; must be allowed to see the project.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status, or <c>null</c> when the review does not exist.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> when the actor may not see the project (or it does not
    ///     exist).
    /// </exception>
    public async Task<ReviewStatus?> GetStatusAsync(
        Actor? actor,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        await _access.RequireViewAsync(actor, projectId, cancellationToken);
        var snapshot = await _store.GetSnapshotAsync(projectId, reviewId, cancellationToken);
        var review = snapshot is null ? null : await _store.GetReviewAsync(projectId, reviewId, cancellationToken);
        var approval = snapshot is null ? null : await _signing.GetApprovalAsync(actor, projectId, reviewId, cancellationToken);
        if (snapshot is null || review is null || approval is null)
        {
            return null;
        }

        // What the viewer may see of the ballots and the threads decides what the status may say
        var user = actor is null ? null : await _users.GetByUsernameAsync(actor.Username, cancellationToken);
        var ballotView = await _checklists.GetBallotViewAsync(actor, projectId, reviewId, cancellationToken);
        var masked = approval.IsMasked || ballotView?.IsMasked == true;
        var threads = await _threads.ListAsync(actor, projectId, reviewId, cancellationToken);
        var capabilities = await _threads.GetCapabilitiesAsync(actor, projectId, reviewId, cancellationToken);
        var counts = ThreadCounts.From(threads.Where(thread => thread.State == ReviewThreadState.Open));

        var ownSlot = ReviewActorRoles.FindOwnSlot(user?.Id, snapshot.Slots);
        var reviewers = BuildReviewers(snapshot, approval, ownSlot);
        var disputes = BuildDisputes(snapshot, ballotView, threads, capabilities);
        var nextAction = ReviewNextAction.Decide(
            await BuildFactsAsync(actor, review, snapshot, approval, ballotView, ownSlot, disputes, counts, cancellationToken));
        var changes = await BuildChangesAsync(actor, user, masked, review, snapshot, approval, cancellationToken);
        return new ReviewStatus(
            masked,
            approval.IsConcluded,
            snapshot.Record.Checklist is not null,
            reviewers,
            disputes,
            counts.OpenQuestions,
            counts.OpenFindings,
            nextAction,
            changes);
    }

    /// <summary>
    ///     Builds the line of every reviewer slot.
    /// </summary>
    /// <param name="snapshot">The snapshot of the review.</param>
    /// <param name="approval">The approval status as the viewer may see it.</param>
    /// <param name="ownSlot">The slot of the viewer, or <c>null</c>.</param>
    /// <returns>The reviewers in slot order.</returns>
    private static List<ReviewerStatus> BuildReviewers(ReviewSnapshot snapshot, ReviewApproval approval, ReviewerSlot? ownSlot)
    {
        var hasChecklist = snapshot.Record.Checklist is not null;
        return approval.Slots
            .Select(slot => new ReviewerStatus(
                slot.SlotId,
                slot.Number,
                slot.Primary.DisplayName,
                hasChecklist ? BallotStateOf(snapshot, slot.SlotId) : BallotState.Submitted,
                slot.State,
                ownSlot?.Id == slot.SlotId))
            .ToList();
    }

    /// <summary>
    ///     Gets the state of the ballot of a slot.
    /// </summary>
    /// <param name="snapshot">The snapshot of the review.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <returns>Not started when the slot has no ballot, otherwise the state of its ballot.</returns>
    private static BallotState BallotStateOf(ReviewSnapshot snapshot, int slotId)
    {
        var ballot = snapshot.Record.Ballots.FirstOrDefault(b => b.SlotId == slotId);
        return ballot?.State ?? BallotState.NotStarted;
    }

    /// <summary>
    ///     Builds the list of disputed checks the viewer may see.
    /// </summary>
    /// <param name="snapshot">The snapshot of the review.</param>
    /// <param name="ballotView">The ballot view for the viewer; <c>null</c> for a review without a checklist.</param>
    /// <param name="threads">The threads the viewer may see.</param>
    /// <param name="capabilities">What the viewer may start.</param>
    /// <returns>The disputed checks in checklist order; empty when the viewer may not see the disputes.</returns>
    private static List<DisputedCheck> BuildDisputes(
        ReviewSnapshot snapshot,
        BallotView? ballotView,
        IReadOnlyList<ReviewThread> threads,
        ThreadCapabilities capabilities)
    {
        if (ballotView is null || ballotView.IsMasked || ballotView.Differing.Count == 0 || snapshot.Record.Checklist is null)
        {
            return [];
        }

        var items = snapshot.Record.Checklist.Parse().Sections.SelectMany(section => section.Items);
        return items
            .Where(item => ballotView.Differing.ContainsKey(item.Id))
            .Select(item => new DisputedCheck(
                item.Id,
                item.Text,
                capabilities.CanStartCheckThread
                && !threads.Any(thread => thread.CheckItemId == item.Id && thread.State == ReviewThreadState.Open)))
            .ToList();
    }

    /// <summary>
    ///     Gathers the facts the next action is decided from.
    /// </summary>
    /// <param name="actor">The viewer.</param>
    /// <param name="review">The review.</param>
    /// <param name="snapshot">The snapshot of the review.</param>
    /// <param name="approval">The approval status as the viewer may see it.</param>
    /// <param name="ballotView">The ballot view for the viewer; <c>null</c> for a review without a checklist.</param>
    /// <param name="ownSlot">The slot of the viewer, or <c>null</c>.</param>
    /// <param name="disputes">The disputed checks the viewer may see.</param>
    /// <param name="counts">The open threads the viewer may see.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The facts.</returns>
    private async Task<NextActionFacts> BuildFactsAsync(
        Actor? actor,
        Review review,
        ReviewSnapshot snapshot,
        ReviewApproval approval,
        BallotView? ballotView,
        ReviewerSlot? ownSlot,
        List<DisputedCheck> disputes,
        ThreadCounts counts,
        CancellationToken cancellationToken)
    {
        var projectId = review.ProjectId;
        var canContribute = actor is not null && await _access.CanContributeAsync(actor, projectId, cancellationToken);
        var isLead = canContribute && await ReviewActorRoles.IsLeadAsync(_access, actor, review, cancellationToken);
        var ownBallot = ownSlot is null ? null : snapshot.Record.Ballots.FirstOrDefault(b => b.SlotId == ownSlot.Id);
        var hasChecklist = snapshot.Record.Checklist is not null;

        // Without a checklist there is no ballot to hand in; the slot goes straight to signing
        var ballotState = hasChecklist ? ownBallot?.State ?? BallotState.NotStarted : BallotState.Submitted;
        var ballotComplete = !hasChecklist
                             || ownBallot is not null
                             && BallotConsolidation.IsComplete(snapshot.Record.Checklist!.Parse(), ownBallot);
        var signature = ownSlot is null
            ? SlotSignatureState.NotSigned
            : approval.Slots.FirstOrDefault(slot => slot.SlotId == ownSlot.Id)?.State ?? SlotSignatureState.NotSigned;
        var conclusionNeeded = approval.ConclusionRequired
                               && await _checklists.CanSetReviewConclusionTextAsync(actor, projectId, review.Id, cancellationToken);

        return new NextActionFacts(
            approval.IsConcluded,
            canContribute,
            isLead,
            snapshot.Slots.Count,
            ownSlot is not null,
            ballotState,
            ballotComplete,
            signature,
            ballotView?.CanAssignMe == true,
            ballotView?.CanSubmitAndSign == true,
            disputes.Count(dispute => dispute.CanDiscuss),
            counts.OpenFindings,
            counts.OpenQuestions,
            conclusionNeeded,
            await CanSignAsync(actor, projectId, approval, ownSlot, signature, cancellationToken));
    }

    /// <summary>
    ///     Tells whether the viewer can sign right now: they hold a slot that is not signed, nothing blocks signing, and
    ///     they did not reopen the review while the project forbids that person to sign again.
    /// </summary>
    /// <param name="actor">The viewer.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="approval">The approval status as the viewer may see it.</param>
    /// <param name="ownSlot">The slot of the viewer, or <c>null</c>.</param>
    /// <param name="signature">The signature state of the slot of the viewer.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the viewer is offered the signing form.</returns>
    private async Task<bool> CanSignAsync(
        Actor? actor,
        int projectId,
        ReviewApproval approval,
        ReviewerSlot? ownSlot,
        SlotSignatureState signature,
        CancellationToken cancellationToken)
    {
        if (actor is null || ownSlot is null || signature == SlotSignatureState.Signed || approval.Blockers.Count > 0)
        {
            return false;
        }

        // The reopener rule is a fact about the person, not a blocker of the review
        var project = await _store.GetProjectAsync(projectId, cancellationToken);
        return project is not { RequireNonReopenerSigning: true }
               || approval.Reopens is not { } reopens
               || !reopens.Any(reopen => string.Equals(reopen.ReopenedBy, actor.Username, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Builds what changed since signing for every slot whose signature no longer matches the review.
    /// </summary>
    /// <param name="actor">The viewer.</param>
    /// <param name="user">The user of the viewer, or <c>null</c>.</param>
    /// <param name="masked">Whether the viewer may not see the answers of the other reviewers yet.</param>
    /// <param name="review">The review.</param>
    /// <param name="snapshot">The snapshot of the review.</param>
    /// <param name="approval">The approval status as the viewer may see it.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One entry for each such slot; empty when every signature matches or none was given.</returns>
    private async Task<List<SignatureChanges>> BuildChangesAsync(
        Actor? actor,
        User? user,
        bool masked,
        Review review,
        ReviewSnapshot snapshot,
        ReviewApproval approval,
        CancellationToken cancellationToken)
    {
        var stale = approval.Slots.Where(slot => slot.State == SlotSignatureState.OutOfDate).ToList();
        if (stale.Count == 0 || actor is null)
        {
            return [];
        }

        var entries = await ReadEntriesAsync(review.ProjectId, review.Id, cancellationToken);
        var shown = AuditMasking.ForViewer(entries, !masked, user?.Id);
        var result = new List<SignatureChanges>();
        foreach (var slot in stale)
        {
            // The newest signature of the slot is the one that no longer matches the review
            var signature = snapshot.Signatures.Where(s => s.SlotId == slot.SlotId).MaxBy(s => s.SignedAt);
            if (signature is null)
            {
                continue;
            }

            var changes = ReviewChangeRules.Derive(entries, shown, signature.SignedAt);
            result.Add(new SignatureChanges(slot.SlotId, slot.Number, signature.DisplayName, signature.SignedAt, changes));
        }

        return result;
    }

    /// <summary>
    ///     Reads the audit entries of a review, oldest first.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The entries of the review, at most <see cref="MaxEntries"/>.</returns>
    private async Task<List<AuditEntry>> ReadEntriesAsync(int projectId, string reviewId, CancellationToken cancellationToken)
    {
        var filter = new AuditFilter(ProjectId: projectId, ReviewId: reviewId);
        var upTo = await _audit.LatestSequenceAsync(cancellationToken);
        var entries = new List<AuditEntry>();
        var after = 0L;
        while (entries.Count < MaxEntries)
        {
            var batch = await _audit.ReadMatchingAsync(filter, after, upTo, BatchSize, cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            entries.AddRange(batch);
            after = batch[^1].Sequence;
        }

        return entries;
    }
}
