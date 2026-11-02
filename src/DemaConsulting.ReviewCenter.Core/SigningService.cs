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
///     Manages the reviewer slots of a review and records re-authenticated signatures on it.
/// </summary>
/// <remarks>
///     The service is the single place where the signing rules live. A review has any number of slots; a slot
///     has a primary reviewer and an optional alternate, and either may sign. Any actor who may see the project (a
///     member, or a system administrator who is not a member) may add,
///     change and remove slots, but only while the slot has no signature at all (current or out of date),
///     because a signature must always point to a slot that still describes who signed. A user occupies at
///     most one slot of a review.
///     Signing needs the signer's password again: it is checked by <see cref="UserService.ReauthenticatePasswordAsync"/>,
///     the same path the login uses, so the hasher, the lockout counting and the single generic failure are the
///     same as at login; a failure is recorded in the audit trail as <c>signature.reauth-failed</c>. A user of an
///     external identity provider has no password: that user confirms by signing in again at the provider, and
///     the signing step spends the single-use proof (<see cref="ReauthProofStore"/>) that the sign-in callback
///     issued for exactly this user, review and slot; without one the signature is refused and
///     <c>signature.reauth-failed</c> is recorded. The signer is the <see cref="Actor"/> taken from the signed-in principal, never a form
///     field, and the password is neither stored, logged nor put in a message. A signature is allowed only when
///     every applicable check is answered, no ballots are disputed and no thread is open; otherwise the blockers
///     are returned as plain sentences. An agreed Fail answer is not a blocker: it leads to the outcome Rejected,
///     which is signed like an approval and needs a conclusion text stating the reasons. Every signature records the
///     outcome it attests. A signature is current only while its digest (see
///     <see cref="RecordDigest"/>) equals the digest of the record now. The status (Approved, Rejected or In review)
///     is computed on demand and never stored.
///     A concluded (approved or rejected) review is locked: while every slot holds a current signature of the same
///     outcome and no thread is open, the threads,
///     checklist answers,
///     applicability and the reviewer slots are refused with <see cref="DomainRule.ReviewLocked"/> (see
///     <see cref="ReviewLock"/>), and signing is refused too. Only <see cref="ReopenReviewAsync"/>, which needs an
///     administrator and a reason, unlocks it; the earlier signatures then stay as history and fresh ones are needed.
///     When the project requires independent reviewers (the default), the user who created the review can neither
///     be put in one of its slots nor sign it.
///     Every change is re-authorized here, whatever the caller showed, and all changes run one at a time under
///     one gate, which is sufficient because there is one service instance per process; the store additionally
///     rechecks the digest inside its transaction for record changes that bypass this gate. The service is safe
///     for concurrent use.
/// </remarks>
public sealed class SigningService
{
    /// <summary>
    ///     The statement a signature of an approved outcome makes.
    /// </summary>
    public const string MeaningText = "I have reviewed this record and approve it.";

    /// <summary>
    ///     The statement a signature of a rejected outcome makes.
    /// </summary>
    public const string MeaningRejectedText = "I have reviewed this record and reject it.";

    /// <summary>
    ///     The reason a rejected review cannot be signed without a conclusion; it is also the blocker sentence.
    /// </summary>
    public const string ConclusionRequiredText = "A rejected review needs a conclusion stating the reasons";

    /// <summary>
    ///     Gets the statement a signature of an outcome makes.
    /// </summary>
    /// <param name="outcome">The outcome the signature attests.</param>
    /// <returns><see cref="MeaningRejectedText"/> for a rejected outcome, otherwise <see cref="MeaningText"/>.</returns>
    public static string MeaningFor(ReviewOutcome outcome)
    {
        return outcome == ReviewOutcome.Rejected ? MeaningRejectedText : MeaningText;
    }

    /// <summary>
    ///     The store that persists slots and signatures and reads the record.
    /// </summary>
    private readonly IReviewStore _store;

    /// <summary>
    ///     The user service that looks users up and re-authenticates signers.
    /// </summary>
    private readonly UserService _users;

    /// <summary>
    ///     Decides who may see which project and who belongs to it.
    /// </summary>
    private readonly IProjectAccess _access;

    /// <summary>
    ///     The clock that supplies signing times.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Serializes every operation that changes slots or signatures.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    ///     The proofs of external-provider users, or <c>null</c> when sign-in with a provider is not available.
    /// </summary>
    private readonly ReauthProofStore? _proofs;

    /// <summary>
    ///     The second factor service, or <c>null</c> when the deployment has none (a local signer then needs the password only).
    /// </summary>
    private readonly TotpService? _totp;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SigningService"/> class.
    /// </summary>
    /// <param name="store">The store that persists slots and signatures. Must not be null.</param>
    /// <param name="users">The user service used to look up users and to re-authenticate signers. Must not be null.</param>
    /// <param name="access">The access rules; slots belong to project members. Must not be null.</param>
    /// <param name="timeProvider">The clock used for signing times; injected so tests control time. Must not be null.</param>
    /// <param name="proofs">
    ///     The proofs of identity that users of an external identity provider obtain by signing in again; <c>null</c>
    ///     means there are none, so such a user can not sign.
    /// </param>
    /// <param name="totp">
    ///     Checks the code of a signer with a second factor; <c>null</c> means the deployment has none, and a signer
    ///     whose account requires a code then can not sign (fail closed).
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public SigningService(
        IReviewStore store,
        UserService users,
        IProjectAccess access,
        TimeProvider timeProvider,
        ReauthProofStore? proofs = null,
        TotpService? totp = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _users = users;
        _access = access;
        _timeProvider = timeProvider;
        _proofs = proofs;
        _totp = totp;
    }

    /// <summary>
    ///     Tells whether a user of an external identity provider has confirmed their identity for a signature, without
    ///     spending the confirmation.
    /// </summary>
    /// <param name="actor">The signed-in user. Must not be null.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier. Must not be null.</param>
    /// <param name="slotId">The slot.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the user has an external account and a live confirmation for exactly this review and
    ///     slot; otherwise <c>false</c>, also for a local account, an API token and an unknown user.
    /// </returns>
    /// <remarks>A page uses it to show "Identity confirmed"; <see cref="SignAsync"/> decides on its own.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public async Task<bool> HasReauthProofAsync(
        Actor actor,
        int projectId,
        string reviewId,
        int slotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(reviewId);
        if (_proofs is null || actor.ViaApiToken)
        {
            return false;
        }

        var user = await _users.GetAsync(actor.Username, cancellationToken);
        return user is { IsExternal: true, Enabled: true } && _proofs.Has(user.Id, projectId, reviewId, slotId);
    }

    /// <summary>
    ///     Checks that a signed-in user of an external identity provider may be asked to confirm their identity for
    ///     a slot, so no confirmation is requested for a signature the user could not make.
    /// </summary>
    /// <param name="actor">The signed-in user. Must not be null.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier. Must not be null.</param>
    /// <param name="slotId">The slot the user wants to sign.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored user, whose external identity the confirmation must match.</returns>
    /// <remarks>Read-only; nothing is stored and nothing is spent.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/> when the actor is an API token, has no enabled
    ///     external account, is not a member of the project or is not a reviewer of the slot,
    ///     <see cref="DomainRule.ProjectNotFound"/> when the actor may not see the project, and
    ///     <see cref="DomainRule.SlotNotFound"/> when the slot does not exist.
    /// </exception>
    public async Task<User> RequireConfirmableAsync(
        Actor actor,
        int projectId,
        string reviewId,
        int slotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(reviewId);
        RejectApiToken(actor);

        // The same rules as signing, in the same order, minus everything that needs the record
        var user = await EnsureActorAsync(actor, projectId, cancellationToken);
        if (!user.IsExternal || !await IsReviewerMemberAsync(user.Id, projectId, cancellationToken))
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "Only a member of the project who can work in it and signs in with an identity provider can confirm this way.");
        }

        var slot = (await _store.ListSlotsAsync(projectId, reviewId, cancellationToken))
            .FirstOrDefault(s => s.Id == slotId) ?? throw SlotNotFound(slotId);
        if (user.Id != slot.PrimaryUserId && user.Id != slot.AlternateUserId)
        {
            throw new DomainRuleException(DomainRule.ActorNotAllowed, "You are not a reviewer of this slot.");
        }

        return user;
    }

    /// <summary>
    ///     Reads the approval status of a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status, or <c>null</c> when the review does not exist.</returns>
    /// <remarks>
    ///     A slot is <see cref="SlotSignatureState.Signed"/> when a signature of the current digest exists,
    ///     <see cref="SlotSignatureState.OutOfDate"/> when it has signatures but none of them is current, and
    ///     <see cref="SlotSignatureState.NotSigned"/> otherwise. The review is concluded when it has at least one slot
    ///     and every slot is signed.
    /// </remarks>
    public Task<ReviewApproval?> GetApprovalAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        return ReadApprovalAsync(projectId, reviewId, cancellationToken);
    }

    /// <summary>
    ///     Reads the approval status of a review as a given viewer may see it: the same status as
    ///     <see cref="GetApprovalAsync(int, string, CancellationToken)"/>, reduced by <see cref="ReviewApproval.Masked"/>
    ///     while the viewer may not see the answers of the other reviewers yet (see <see cref="BallotVisibility"/>).
    /// </summary>
    /// <param name="viewer">
    ///     The person who looks at the status; <c>null</c> is treated as a viewer who has not handed in a ballot, so the
    ///     status is masked (fail closed) when it has to be.
    /// </param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status, or <c>null</c> when the review does not exist.</returns>
    /// <remarks>
    ///     The masked status is for showing only. Signing is checked against the unmasked blockers inside the signing
    ///     methods, so the masking can never let a review be signed that must not be.
    /// </remarks>
    public async Task<ReviewApproval?> GetApprovalAsync(
        Actor? viewer,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        var read = await ReadSnapshotAndApprovalAsync(projectId, reviewId, cancellationToken);
        if (read is null)
        {
            return null;
        }

        // A single slot or a concluded review hides nothing, so no role needs to be looked up
        var (snapshot, approval) = read.Value;
        if (approval.IsConcluded || snapshot.Slots.Count <= 1)
        {
            return approval;
        }

        var review = await _store.GetReviewAsync(projectId, reviewId, cancellationToken);
        var seesAll = review is not null
                      && await BallotVisibility.SeesAllInReviewAsync(_access, viewer, review, cancellationToken);
        var user = viewer is null ? null : await _users.GetAsync(viewer.Username, cancellationToken);
        return BallotVisibility.CanSeeOthers(seesAll, snapshot, user?.Id) ? approval : approval.Masked();
    }

    /// <summary>
    ///     Reads the approval status of several reviews of a project with one set-based read of their snapshots,
    ///     loading the users once for all of them, and not at all when there are no reviews.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewIds">The review identifiers.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The status of each review that exists, by review identifier; a review that does not exist is absent.
    ///     Each status equals what <see cref="GetApprovalAsync(int, string, CancellationToken)"/> returns for the same
    ///     review.
    /// </returns>
    public async Task<IReadOnlyDictionary<string, ReviewApproval>> GetApprovalsAsync(
        int projectId,
        IEnumerable<string> reviewIds,
        CancellationToken cancellationToken = default)
    {
        var (reads, _) = await ReadManyAsync(projectId, reviewIds, cancellationToken);
        return reads.ToDictionary(entry => entry.Key, entry => entry.Value.Approval);
    }

    /// <summary>
    ///     Reads the approval status of several reviews of a project as a given viewer may see it: the batch form of
    ///     <see cref="GetApprovalAsync(Actor?, int, string, CancellationToken)"/>, with the same constant number of
    ///     reads as <see cref="GetApprovalsAsync(int, IEnumerable{string}, CancellationToken)"/>.
    /// </summary>
    /// <param name="viewer">The person who looks; <c>null</c> is masked where a review has to be masked.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewIds">The review identifiers.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The status of each review that exists, by review identifier, each masked exactly when
    ///     <see cref="GetApprovalAsync(Actor?, int, string, CancellationToken)"/> would mask it.
    /// </returns>
    /// <remarks>
    ///     The roles of the viewer are resolved once for the project; the reviews of the project are read (for the
    ///     creator of each) only when a review actually has to be masked and the viewer is not an administrator or an
    ///     Auditor.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, ReviewApproval>> GetApprovalsAsync(
        Actor? viewer,
        int projectId,
        IEnumerable<string> reviewIds,
        CancellationToken cancellationToken = default)
    {
        var (reads, members) = await ReadManyAsync(projectId, reviewIds, cancellationToken);
        var approvals = new Dictionary<string, ReviewApproval>();

        // Resolved lazily: a project in which nothing has to be masked costs no role lookup at all
        bool? seesAllInProject = null;
        bool? canContribute = null;
        IReadOnlyList<Review>? reviews = null;
        var viewerId = viewer is null
            ? null
            : members.Values
                .FirstOrDefault(user => string.Equals(user.Username, viewer.Username, StringComparison.OrdinalIgnoreCase))?.Id;
        foreach (var (reviewId, read) in reads)
        {
            var (snapshot, approval) = read;
            if (approval.IsConcluded || snapshot.Slots.Count <= 1)
            {
                approvals[reviewId] = approval;
                continue;
            }

            seesAllInProject ??= await BallotVisibility.SeesAllInProjectAsync(_access, viewer, projectId, cancellationToken);
            var seesAll = seesAllInProject.Value;
            if (!seesAll && viewer is { ViaApiToken: false })
            {
                // Only the lead is left to find out. An administrator is already covered above, so the lead right is
                // the creator of the review who can still contribute: the project role is looked up once for the whole
                // batch and the creator comes from the reviews read once, not one membership lookup per review
                reviews ??= await _store.ListReviewsAsync(projectId, cancellationToken);
                var review = reviews.FirstOrDefault(r => r.Id == reviewId);
                if (review?.CreatedBy is not null
                    && string.Equals(review.CreatedBy, viewer.Username, StringComparison.OrdinalIgnoreCase))
                {
                    canContribute ??= await _access.CanContributeAsync(viewer, projectId, cancellationToken);
                    seesAll = canContribute.Value;
                }
            }

            approvals[reviewId] = BallotVisibility.CanSeeOthers(seesAll, snapshot, viewerId) ? approval : approval.Masked();
        }

        return approvals;
    }

    /// <summary>
    ///     Reads the snapshots and statuses of several reviews with one set-based read, and the users once.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewIds">The review identifiers.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The snapshot and status of each review that exists, by review identifier (a review that does not exist is
    ///     absent), and the users by identity; empty without any read of the users when there are no reviews.
    /// </returns>
    private async Task<(Dictionary<string, (ReviewSnapshot Snapshot, ReviewApproval Approval)> Reads,
        IReadOnlyDictionary<int, User> Members)> ReadManyAsync(
        int projectId,
        IEnumerable<string> reviewIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reviewIds);
        var ids = reviewIds.Distinct().ToList();
        var reads = new Dictionary<string, (ReviewSnapshot Snapshot, ReviewApproval Approval)>();

        // Nothing to read, so do not load the whole user list
        if (ids.Count == 0)
        {
            return (reads, new Dictionary<int, User>());
        }

        var memberIds = (await _store.ListMembershipsAsync(projectId, cancellationToken))
            .Select(membership => membership.UserId)
            .ToHashSet();
        var members = (await _users.ListAsync(cancellationToken)).ToDictionary(user => user.Id);

        // One set-based read of all snapshots, and one of the history of the reviews that were reopened; a review
        // that does not exist is absent from the result
        var snapshots = await _store.ListSnapshotsAsync(projectId, ids, cancellationToken);
        var reopened = snapshots
            .Where(entry => entry.Value.Record.Reopens > 0)
            .Select(entry => entry.Key)
            .ToList();
        var history = reopened.Count > 0
            ? await _store.ListReopensAsync(projectId, reopened, cancellationToken)
            : new Dictionary<string, IReadOnlyList<ReviewReopen>>();
        foreach (var (reviewId, snapshot) in snapshots)
        {
            reads[reviewId] = (snapshot, BuildApproval(snapshot, members, memberIds, history.GetValueOrDefault(reviewId)));
        }

        return (reads, members);
    }

    /// <summary>
    ///     Reads the approval status of a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status, or <c>null</c> when the review does not exist.</returns>
    private async Task<ReviewApproval?> ReadApprovalAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken)
    {
        return (await ReadSnapshotAndApprovalAsync(projectId, reviewId, cancellationToken))?.Approval;
    }

    /// <summary>
    ///     Reads the snapshot of a review and the approval status built from it.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The snapshot and the status, or <c>null</c> when the review does not exist.</returns>
    private async Task<(ReviewSnapshot Snapshot, ReviewApproval Approval)?> ReadSnapshotAndApprovalAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken)
    {
        // One snapshot, so the digest, the slots and the signatures compared with it describe the same state
        var snapshot = await _store.GetSnapshotAsync(projectId, reviewId, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        var members = (await _users.ListAsync(cancellationToken)).ToDictionary(user => user.Id);

        // Who is still a member: a slot keeps its assignee after a removal, so the status says who can no longer sign
        var memberIds = (await _store.ListMembershipsAsync(projectId, cancellationToken))
            .Select(membership => membership.UserId)
            .ToHashSet();

        // The history is read only for a review that was reopened, so the common case costs no extra query
        var reopens = snapshot.Record.Reopens > 0
            ? await _store.ListReopensAsync(projectId, reviewId, cancellationToken)
            : null;
        return (snapshot, BuildApproval(snapshot, members, memberIds, reopens));
    }

    /// <summary>
    ///     Builds the approval status of a review from what was already read; the one place that does, for both the
    ///     single and the batch read, so they cannot differ.
    /// </summary>
    /// <param name="snapshot">The snapshot of the review.</param>
    /// <param name="members">The users by identity.</param>
    /// <param name="memberIds">The identities of the current project members.</param>
    /// <param name="reopens">The reopens of the review, or <c>null</c> when it was never reopened.</param>
    /// <returns>The status.</returns>
    private static ReviewApproval BuildApproval(
        ReviewSnapshot snapshot,
        IReadOnlyDictionary<int, User> members,
        IReadOnlySet<int> memberIds,
        IReadOnlyList<ReviewReopen>? reopens)
    {
        var (record, slots, signatures) = snapshot;
        var digest = RecordDigest.Compute(record);

        // One status per slot, numbered in creation order
        var statuses = new List<SlotStatus>();
        foreach (var slot in slots.OrderBy(s => s.Id))
        {
            var own = signatures.Where(s => s.SlotId == slot.Id).ToList();
            var current = own.Where(s => s.RecordDigest == digest).MaxBy(s => s.Id);
            var state = SlotSignatureState.NotSigned;
            if (current is not null)
            {
                state = SlotSignatureState.Signed;
            }
            else if (own.Count > 0)
            {
                state = SlotSignatureState.OutOfDate;
            }

            statuses.Add(new SlotStatus(
                slot.Id,
                statuses.Count + 1,
                Member(members, memberIds, slot.PrimaryUserId),
                slot.AlternateUserId is { } alternate ? Member(members, memberIds, alternate) : null,
                state,
                current?.DisplayName,
                current?.SignedAt,
                // A review without a checklist has no ballots; with one, a slot without a ballot has not started
                record.Checklist is null
                    ? null
                    : record.Ballots.FirstOrDefault(b => b.SlotId == slot.Id)?.State ?? BallotState.NotStarted));
        }

        var conclusion = ReviewLock.ConcludedOutcome(record, slots, signatures);
        var assessment = Assess(record, slots.Count);
        var (checklist, blockers, failed) = (assessment.Checklist, assessment.Blockers, assessment.Failed);
        var unsubmitted = record.Checklist is null
            ? 0
            : slots.Count(slot => record.Ballots.FirstOrDefault(b => b.SlotId == slot.Id)?.State != BallotState.Submitted);
        var disputed = checklist?.Consolidation?.Disputed ?? 0;
        var openCounts = ThreadCounts.From(record.Threads);
        var openCheckCounts = ThreadCounts.From(record.Threads, thread => thread.IsCheckThread);
        return new ReviewApproval(
            conclusion,
            digest,
            statuses,
            blockers,
            NextStepFor(conclusion, blockers, statuses.Count, record, checklist, failed, assessment.ConclusionRequired, assessment.Outcome),
            reopens,
            // The structured counts behind the blocker sentences of Assess, so a list can show them without parsing
            record.Threads.Count(thread => thread.State == ReviewThreadState.Open),
            checklist?.Consolidation?.Pending ?? 0,
            failed,
            // Assess returns no checklist state exactly when it adds the "no checklist" blocker
            checklist is null,
            disputed,
            unsubmitted,
            assessment.Outcome,
            assessment.ConclusionRequired,
            record.Threads.Count(thread => thread.State == ReviewThreadState.Open && thread.IsCheckThread),
            IsMasked: false,
            OpenQuestions: openCounts.OpenQuestions,
            OpenFindings: openCounts.OpenFindings,
            OpenCheckQuestions: openCheckCounts.OpenQuestions,
            OpenCheckFindings: openCheckCounts.OpenFindings);
    }

    /// <summary>
    ///     Lists the users that can be chosen for a slot of a project: its enabled members who can work in it (a
    ///     read-only auditor is never offered).
    /// </summary>
    /// <param name="actor">The signed-in user who asks; must be able to work in the project. Must not be null.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The enabled members ordered by display name and then user name; nothing secret is included.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> when the actor may not see the project, and with
    ///     <see cref="DomainRule.ActorNotAllowed"/> when the actor may only read it.
    /// </exception>
    public async Task<IReadOnlyList<SlotMember>> ListCandidatesAsync(
        Actor actor,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireContributeAsync(actor, projectId, cancellationToken);
        var memberIds = await ReviewerMemberIdsAsync(projectId, cancellationToken);
        return (await _users.ListAsync(cancellationToken))
            .Where(user => user.Enabled && memberIds.Contains(user.Id))
            .OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
            .Select(user => new SlotMember(user.Id, user.Username, user.DisplayName))
            .ToList();
    }

    /// <summary>
    ///     Adds a reviewer slot to a review.
    /// </summary>
    /// <param name="actor">The signed-in user who makes the change. Must not be null; the account must exist and be enabled.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="primaryUserId">The primary reviewer.</param>
    /// <param name="alternateUserId">The alternate reviewer, or <c>null</c> for none.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored slot.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/>, <see cref="DomainRule.ReviewNotFound"/> or
    ///     <see cref="DomainRule.SlotUserInvalid"/> when a rule is violated; nothing is stored then. Also thrown with
    ///     <see cref="DomainRule.ReviewLocked"/> when the review is concluded (approved or rejected) and with
    ///     <see cref="DomainRule.ReviewerNotIndependent"/> when a chosen user created the review and the project
    ///     requires independent reviewers.
    /// </exception>
    public async Task<ReviewerSlot> AddSlotAsync(
        Actor actor,
        int projectId,
        string reviewId,
        int primaryUserId,
        int? alternateUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RejectApiToken(actor);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureActorAsync(actor, projectId, cancellationToken);
            await EnsureReviewAsync(projectId, reviewId, cancellationToken);

            // A concluded (approved or rejected) review is locked: who may sign it can not change until it is reopened
            await ReviewLock.EnsureUnlockedAsync(_store, projectId, reviewId, cancellationToken);
            var slots = await _store.ListSlotsAsync(projectId, reviewId, cancellationToken);
            await ValidateMembersAsync(
                projectId, reviewId, slots, null, primaryUserId, alternateUserId, cancellationToken);
            var members = await DescribeMembersAsync(primaryUserId, alternateUserId, cancellationToken);

            return await _store.AddSlotAsync(
                new ReviewerSlot
                {
                    ProjectId = projectId,
                    ReviewId = reviewId,
                    PrimaryUserId = primaryUserId,
                    AlternateUserId = alternateUserId
                },
                _ => AuditDraft.By(
                    actor,
                    "slot.added",
                    // The new slot is the last one, and the page numbers the slots in creation order
                    $"slot {slots.Count + 1}: {members}",
                    projectId,
                    reviewId),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Changes the primary and alternate reviewer of an unsigned slot.
    /// </summary>
    /// <param name="actor">The signed-in user who makes the change. Must not be null; the account must exist and be enabled.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <param name="primaryUserId">The new primary reviewer.</param>
    /// <param name="alternateUserId">The new alternate reviewer, or <c>null</c> for none.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="acknowledgedSlotIds">
    ///     The identities of the reviewer slots whose signatures the person agreed to make out of date, or <c>null</c> for
    ///     none. Needed when a reviewer leaves the slot and the slot has a ballot: the ballot reverts to a draft without
    ///     the rows of the departed reviewer, which changes what the signatures of the other slots cover.
    /// </param>
    /// <returns>A task that completes when the slot is stored.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/>, <see cref="DomainRule.SlotNotFound"/>,
    ///     <see cref="DomainRule.SlotSigned"/> (the slot has any signature, current or out of date) or
    ///     <see cref="DomainRule.SlotUserInvalid"/> when a rule is violated; nothing is stored then. Also thrown with
    ///     <see cref="DomainRule.ReviewLocked"/> when the review is concluded (approved or rejected),
    ///     <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/> when a current signature is not acknowledged and with
    ///     <see cref="DomainRule.ReviewerNotIndependent"/> as <see cref="AddSlotAsync"/> does.
    /// </exception>
    public async Task UpdateSlotAsync(
        Actor actor,
        int projectId,
        string reviewId,
        int slotId,
        int primaryUserId,
        int? alternateUserId,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? acknowledgedSlotIds = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RejectApiToken(actor);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureActorAsync(actor, projectId, cancellationToken);
            var snapshot = await ReviewLock.EnsureUnlockedAsync(_store, projectId, reviewId, cancellationToken);
            var slots = await EnsureUnsignedSlotAsync(projectId, reviewId, slotId, cancellationToken);
            await ValidateMembersAsync(
                projectId, reviewId, slots, slotId, primaryUserId, alternateUserId, cancellationToken);
            var members = await DescribeMembersAsync(primaryUserId, alternateUserId, cancellationToken);

            // A reviewer who leaves takes the own rows out of the ballot, which reverts to a draft: that may change what
            // the signatures of the other slots cover
            var old = slots.First(s => s.Id == slotId);
            var ballot = snapshot?.Record.Ballots.FirstOrDefault(b => b.SlotId == slotId);
            var departing = new[] { old.PrimaryUserId, old.AlternateUserId ?? 0 }
                .Where(id => id != 0 && id != primaryUserId && id != alternateUserId)
                .Distinct()
                .ToList();
            if (snapshot is not null && ballot is not null && departing.Count > 0)
            {
                var names = (await _users.ListAsync(cancellationToken))
                    .Where(user => departing.Contains(user.Id))
                    .Select(user => user.Username)
                    .ToList();
                SignatureImpact.RequireAcknowledged(
                    snapshot,
                    BallotEdits.WithBallot(snapshot.Record, BallotEdits.Reassign(ballot, names)),
                    acknowledgedSlotIds);
            }

            var audit = AuditDraft.By(
                actor, "slot.updated", $"slot {SlotNumberOf(slots, slotId)}: {members}", projectId, reviewId);
            if (!await _store.UpdateSlotAsync(projectId, reviewId, slotId, primaryUserId, alternateUserId, audit, cancellationToken))
            {
                throw SlotNotFound(slotId);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Removes an unsigned slot.
    /// </summary>
    /// <param name="actor">The signed-in user who makes the change. Must not be null; the account must exist and be enabled.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="acknowledgedSlotIds">
    ///     The identities of the reviewer slots whose signatures the person agreed to make out of date, or <c>null</c> for
    ///     none. Needed when the slot has a submitted ballot: removing the slot removes its ballot from the record.
    /// </param>
    /// <returns>A task that completes when the slot is removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/>, <see cref="DomainRule.SlotNotFound"/> or
    ///     <see cref="DomainRule.SlotSigned"/> when a rule is violated; nothing is removed then. Also thrown with
    ///     <see cref="DomainRule.ReviewLocked"/> when the review is concluded (approved or rejected) and with
    ///     <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/> when a current signature is not acknowledged.
    /// </exception>
    public async Task RemoveSlotAsync(
        Actor actor,
        int projectId,
        string reviewId,
        int slotId,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? acknowledgedSlotIds = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RejectApiToken(actor);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureActorAsync(actor, projectId, cancellationToken);
            var snapshot = await ReviewLock.EnsureUnlockedAsync(_store, projectId, reviewId, cancellationToken);
            var slots = await EnsureUnsignedSlotAsync(projectId, reviewId, slotId, cancellationToken);

            // The ballot of the slot leaves the record with it: that may change what the other signatures cover
            if (snapshot is not null)
            {
                SignatureImpact.RequireAcknowledged(
                    snapshot,
                    BallotEdits.WithoutBallot(snapshot.Record, slotId),
                    acknowledgedSlotIds);
            }

            // The entry describes the slot as it was before it goes, including the number it had on the page
            var slot = slots.First(s => s.Id == slotId);
            var members = await DescribeMembersAsync(slot.PrimaryUserId, slot.AlternateUserId, cancellationToken);
            var audit = AuditDraft.By(
                actor, "slot.removed", $"slot {SlotNumberOf(slots, slotId)}: {members}", projectId, reviewId);
            if (!await _store.RemoveSlotAsync(projectId, reviewId, slotId, audit, cancellationToken))
            {
                throw SlotNotFound(slotId);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Reopens an approved (locked) review so it can change again.
    /// </summary>
    /// <param name="actor">
    ///     The signed-in user who reopens. Must not be null; must administer the project (or be a system
    ///     administrator); an API token is refused.
    /// </param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="reason">
    ///     Why the review is reopened; leading and trailing white space is removed. Required, at most
    ///     <see cref="ReviewLimits.MaxReopenReasonLength"/> characters.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the reopen is stored.</returns>
    /// <remarks>
    ///     Reopening is the only way out of the lock. It is a deliberate, accountable act: it needs an administrator and
    ///     a reason, and the audit entry <c>review.reopened</c> names who did it, why, and the digest of the approved
    ///     record that was reopened. The reopen raises the reopen count of the record, so the digest changes: every
    ///     earlier signature stays as history (out of date, never current again, not even when the content is
    ///     changed back) and the review needs fresh signatures. Runs under the same gate as the other slot and
    ///     signing changes.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/> (an API token, a disabled account or a user who does
    ///     not administer the project), <see cref="DomainRule.ProjectNotFound"/>,
    ///     <see cref="DomainRule.ReopenReasonInvalid"/>, <see cref="DomainRule.ReviewNotFound"/>,
    ///     <see cref="DomainRule.ReviewNotLocked"/> (the review is not approved) or
    ///     <see cref="DomainRule.ReopenWouldStrandSlot"/> (the project does not let a reopener sign again, and a slot
    ///     would have nobody left who may sign) when a rule is violated; nothing is stored then.
    /// </exception>
    public async Task ReopenReviewAsync(
        Actor actor,
        int projectId,
        string reviewId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(reviewId);
        RejectApiToken(actor);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Who: a live account that administers the project; asked again on every call
            var user = await EnsureActorAsync(actor, projectId, cancellationToken);
            await _access.RequireAdministerAsync(actor, projectId, cancellationToken);

            // Why: a reason is mandatory and must fit in the audit entry together with the digest
            var trimmed = reason?.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.Length > ReviewLimits.MaxReopenReasonLength)
            {
                throw new DomainRuleException(
                    DomainRule.ReopenReasonInvalid,
                    $"A reason of at most {ReviewLimits.MaxReopenReasonLength} characters is required to reopen a review.");
            }

            // What: only a concluded (approved or rejected) review is locked; the digest of that conclusion goes into the history
            var snapshot = await _store.GetSnapshotAsync(projectId, reviewId, cancellationToken)
                           ?? throw ReviewNotFound(reviewId);
            if (!ReviewLock.IsConcluded(snapshot.Record, snapshot.Slots, snapshot.Signatures))
            {
                throw new DomainRuleException(
                    DomainRule.ReviewNotLocked,
                    "The review is not locked, so there is nothing to reopen.");
            }

            // A reopen blocks the person reopening (and anyone who reopened before) from signing again when the project
            // says so, so it must not leave a slot in which nobody may sign: the review would never be approved again
            await EnsureNoStrandedSlotAsync(snapshot, user, projectId, reviewId, cancellationToken);

            var previousDigest = RecordDigest.Compute(snapshot.Record);
            await _store.ReopenReviewAsync(
                projectId,
                reviewId,
                new ReviewReopen(
                    0,
                    projectId,
                    reviewId,
                    _timeProvider.GetUtcNow(),
                    user.Username,
                    user.DisplayName,
                    trimmed,
                    previousDigest),
                AuditDraft.By(
                    actor,
                    "review.reopened",
                    $"previous digest {previousDigest}; reason: {trimmed}",
                    projectId,
                    reviewId),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Signs the review in a slot, after the signer has confirmed their password.
    /// </summary>
    /// <param name="actor">The signed-in user who signs. Must not be null; the account must exist and be enabled.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot to sign in; the actor must be its primary or alternate reviewer.</param>
    /// <param name="expectedDigest">
    ///     The record digest the signer was looking at (see <see cref="ReviewApproval.Digest"/>); the signature is
    ///     refused when the record has changed since, so nobody signs something they have not seen.
    /// </param>
    /// <param name="password">
    ///     The signer's password, checked like a login. It is never stored, logged or put in a message; the caller
    ///     should discard it as soon as this call returns. Ignored for a user of an external identity provider.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored signature.</returns>
    /// <remarks>
    ///     The slot, the seat and the signing rules are all checked before the password is asked for, so a
    ///     refused signing never counts as a failed password attempt. Every password failure (wrong, locked,
    ///     disabled or unknown) gives the same generic message and signs nothing. A signer with a second factor
    ///     cannot use this overload; <see cref="SignWithCredentialsAsync"/> takes the code too.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/>, <see cref="DomainRule.ReviewNotFound"/>,
    ///     <see cref="DomainRule.SlotNotFound"/>, <see cref="DomainRule.SigningNotAllowed"/> (already signed, or
    ///     blocked by the checklist or threads), <see cref="DomainRule.ReauthenticationFailed"/> or
    ///     <see cref="DomainRule.RecordChanged"/> when a rule is violated; nothing is stored then. Also thrown with
    ///     <see cref="DomainRule.ReviewLocked"/> when the review is already approved and with
    ///     <see cref="DomainRule.ReviewerNotIndependent"/> when the signer created the review and the project
    ///     requires independent reviewers, and with <see cref="DomainRule.ReopenerCannotSign"/> when the signer
    ///     reopened the review and the project does not let a reopener sign it again.
    /// </exception>
    public Task<ReviewSignature> SignAsync(
        Actor actor,
        int projectId,
        string reviewId,
        int slotId,
        string expectedDigest,
        string? password,
        CancellationToken cancellationToken = default)
    {
        return SignWithCredentialsAsync(
            actor, projectId, reviewId, slotId, expectedDigest, new SignerCredentials(password), cancellationToken);
    }

    /// <summary>
    ///     Signs the review in a slot, after the signer has confirmed their password and, when they have a second
    ///     factor, a fresh authenticator code.
    /// </summary>
    /// <param name="actor">The signed-in user who signs. Must not be null; the account must exist and be enabled.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot to sign in; the actor must be its primary or alternate reviewer.</param>
    /// <param name="expectedDigest">
    ///     The record digest the signer was looking at (see <see cref="ReviewApproval.Digest"/>); the signature is
    ///     refused when the record has changed since, so nobody signs something they have not seen.
    /// </param>
    /// <param name="credentials">
    ///     What the signer typed to confirm their identity: the password and, optionally, the authenticator code.
    ///     Must not be null. Nothing in it is stored, logged or put in a message.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored signature.</returns>
    /// <remarks>
    ///     The slot, the seat and the signing rules are all checked before the password is asked for, so a
    ///     refused signing never counts as a failed password attempt. Every password failure (wrong, locked,
    ///     disabled or unknown) gives the same generic message and signs nothing. A signer with a second factor
    ///     needs the password and a fresh code. A wrong or already used code fails the signing like a wrong password.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="actor"/>, <paramref name="expectedDigest"/> or <paramref name="credentials"/>
    ///     is null.
    /// </exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/>, <see cref="DomainRule.ReviewNotFound"/>,
    ///     <see cref="DomainRule.SlotNotFound"/>, <see cref="DomainRule.SigningNotAllowed"/> (already signed, or
    ///     blocked by the checklist or threads), <see cref="DomainRule.ReauthenticationFailed"/> or
    ///     <see cref="DomainRule.RecordChanged"/> when a rule is violated; nothing is stored then. Also thrown with
    ///     <see cref="DomainRule.ReviewLocked"/> when the review is already approved and with
    ///     <see cref="DomainRule.ReviewerNotIndependent"/> when the signer created the review and the project
    ///     requires independent reviewers, and with <see cref="DomainRule.ReopenerCannotSign"/> when the signer
    ///     reopened the review and the project does not let a reopener sign it again.
    /// </exception>
    public async Task<ReviewSignature> SignWithCredentialsAsync(
        Actor actor,
        int projectId,
        string reviewId,
        int slotId,
        string expectedDigest,
        SignerCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(expectedDigest);
        ArgumentNullException.ThrowIfNull(credentials);
        RejectApiToken(actor);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // The signer must be a live account, a member of the project (an administrator who is not a member
            // cannot sign), and one of the two people of the slot
            var user = await EnsureActorAsync(actor, projectId, cancellationToken);
            var (record, role, slotNumber) = await EnsureSignerOfSlotAsync(user, projectId, reviewId, slotId, cancellationToken);

            // The author of the review may not sign it when the project requires independent reviewers; this is
            // checked here, not only when the slot is filled, so a slot assigned before the rule was switched on
            // is covered too, and it is refused before the password is asked for
            await EnsureIndependentAsync(projectId, reviewId, user, cancellationToken);

            // Whoever reopened the review may not sign it again when the project says so; refused before the
            // password is asked for, like the independence rule
            await EnsureNotReopenerAsync(record, projectId, reviewId, user, cancellationToken);

            // The record must be the one the signer saw, and must be signable; both are refused before the
            // password is asked for
            var (digest, outcome) = await EnsureSignableAsync(
                record, projectId, reviewId, slotId, expectedDigest, cancellationToken);

            // Re-authenticate, after every other check passed
            var verified = await ReauthenticateAsync(
                actor, user, projectId, reviewId, slotId, credentials, cancellationToken);

            // The store recomputes the digest inside its transaction and refuses the signature if it changed
            return await _store.AddSignatureAsync(
                new ReviewSignature
                {
                    ProjectId = projectId,
                    ReviewId = reviewId,
                    SlotId = slotId,
                    UserId = verified.Id,
                    DisplayName = verified.DisplayName,
                    Role = role,
                    SignedAt = _timeProvider.GetUtcNow(),
                    RecordDigest = digest,
                    Outcome = outcome,
                    Meaning = MeaningFor(outcome)
                },
                AuditDraft.By(
                    actor,
                    "signature.added",
                    $"slot {slotNumber}, role {role}, outcome {outcome}, record digest {digest}",
                    projectId,
                    reviewId),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Checks that the signer is a project member and one of the two people of the slot.
    /// </summary>
    /// <param name="user">The signer's account.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot to sign in.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The record of the review, the role the signer has in the slot and the number of the slot as the review page
    ///     shows it.
    /// </returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown when the signer is not a member, the review or slot does not exist, or the signer is not a reviewer
    ///     of the slot.
    /// </exception>
    private async Task<(ReviewRecord Record, SlotRole Role, int SlotNumber)> EnsureSignerOfSlotAsync(
        User user,
        int projectId,
        string reviewId,
        int slotId,
        CancellationToken cancellationToken)
    {
        if (!await IsReviewerMemberAsync(user.Id, projectId, cancellationToken))
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "Only a member of the project who can work in it (not a read-only auditor) can sign its reviews.");
        }

        var record = await _store.GetRecordAsync(projectId, reviewId, cancellationToken)
                     ?? throw ReviewNotFound(reviewId);
        var slots = await _store.ListSlotsAsync(projectId, reviewId, cancellationToken);
        var slot = slots.FirstOrDefault(s => s.Id == slotId) ?? throw SlotNotFound(slotId);
        if (user.Id != slot.PrimaryUserId && user.Id != slot.AlternateUserId)
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "You are not a reviewer of this slot.");
        }

        return (record, user.Id == slot.PrimaryUserId ? SlotRole.Primary : SlotRole.Alternate, SlotNumberOf(slots, slotId));
    }

    /// <summary>
    ///     Gets the number of a slot as the review page shows it: its position among the slots in creation order.
    /// </summary>
    /// <param name="slots">The slots of the review; must contain the slot.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <returns>The 1-based position of the slot when the slots are ordered by identity.</returns>
    /// <remarks>
    ///     The audit trail says "slot N" with this number and never the database identity, so an entry reads the same
    ///     as the page. The number is the position at the time of the entry.
    /// </remarks>
    private static int SlotNumberOf(IEnumerable<ReviewerSlot> slots, int slotId)
    {
        return slots.Count(s => s.Id < slotId) + 1;
    }

    /// <summary>
    ///     Checks that the record is the one the signer saw, is not already signed in the slot and has no blockers.
    /// </summary>
    /// <param name="record">The record as it is now.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot to sign in.</param>
    /// <param name="expectedDigest">The digest the signer saw.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The digest of the record, which the signature will point to, and the outcome the signature attests.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown when the record changed, the slot already has a current signature or a blocker remains, with
    ///     <see cref="DomainRule.ConclusionRequired"/> when the outcome is rejected and the conclusion text is blank.
    /// </exception>
    private async Task<(string Digest, ReviewOutcome Outcome)> EnsureSignableAsync(
        ReviewRecord record,
        int projectId,
        string reviewId,
        int slotId,
        string expectedDigest,
        CancellationToken cancellationToken)
    {
        // A concluded (approved or rejected) review is locked, so nobody signs it again; the message says why instead of "already signed"
        await ReviewLock.EnsureUnlockedAsync(_store, projectId, reviewId, cancellationToken);

        var digest = RecordDigest.Compute(record);
        if (digest != expectedDigest)
        {
            throw RecordChangedException();
        }

        var signatures = await _store.ListSignaturesAsync(projectId, reviewId, cancellationToken);
        if (signatures.Any(s => s.SlotId == slotId && s.RecordDigest == digest))
        {
            throw new DomainRuleException(
                DomainRule.SigningNotAllowed,
                "This slot already has a signature of the current record.");
        }

        var slots = await _store.ListSlotsAsync(projectId, reviewId, cancellationToken);
        var assessment = Assess(record, slots.Count);
        var blockers = assessment.Blockers;

        // The signer's own ballot must be handed in: the digest covers only the submitted ballots, so it cannot say so.
        // A review with one slot hands the ballot in with the last answer, so there the open checks are the better
        // message and come first.
        var ownNotSubmitted = record.Checklist is not null
                              && record.Ballots.FirstOrDefault(b => b.SlotId == slotId)?.State != BallotState.Submitted;
        if (ownNotSubmitted && !(slots.Count == 1 && blockers.Count > 0))
        {
            throw new DomainRuleException(
                DomainRule.BallotNotSubmitted,
                "Hand in your ballot before you sign: the signature covers the consolidated ballots, and yours is " +
                "not among them yet.");
        }

        // The conclusion is the only blocker: the dedicated rule names it exactly. With other blockers the generic
        // message lists them all, the conclusion among them.
        if (assessment.ConclusionRequired && blockers.Count == 1)
        {
            throw new DomainRuleException(DomainRule.ConclusionRequired, ConclusionRequiredText + ".");
        }

        if (blockers.Count > 0)
        {
            throw new DomainRuleException(
                DomainRule.SigningNotAllowed,
                "The review cannot be signed yet: " + string.Join("; ", blockers) + ".");
        }

        // No blocker remains, so the submitted ballots agree and the outcome is known: the signature attests it. The
        // digest of the same record covers it, so the signature stays tied to what the signer was shown.
        var outcome = BallotConsolidation.Consolidate(record.Checklist!.Parse(), record.Ballots).Outcome
                      ?? throw new DomainRuleException(DomainRule.SigningNotAllowed, "The review has no outcome yet.");
        return (digest, outcome);
    }

    /// <summary>
    ///     Confirms the signer's identity again, the way their kind of account requires.
    /// </summary>
    /// <remarks>
    ///     A local account goes through the login path (same hasher, lockout and generic failure; a failure is
    ///     recorded as a failed re-authentication, not as a failed sign-in). An external account has no password: its
    ///     proof is the fresh sign-in at the provider that the callback issued for exactly this user, review and
    ///     slot, spent here and only here, after every other check passed.
    /// </remarks>
    /// <param name="actor">The signed-in principal.</param>
    /// <param name="user">The signer's account.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot to sign in.</param>
    /// <param name="credentials">What the signer typed.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The account that was confirmed.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ReauthenticationFailed"/> when the identity is not confirmed.
    /// </exception>
    private async Task<User> ReauthenticateAsync(
        Actor actor,
        User user,
        int projectId,
        string reviewId,
        int slotId,
        SignerCredentials credentials,
        CancellationToken cancellationToken)
    {
        if (user.IsExternal)
        {
            if (_proofs is null || !_proofs.TryConsume(user.Id, projectId, reviewId, slotId))
            {
                await _users.RecordExternalReauthFailureAsync(user, "no-proof", cancellationToken);
                throw new DomainRuleException(
                    DomainRule.ReauthenticationFailed,
                    "Your identity is not confirmed. Confirm it again, then sign.");
            }

            return user;
        }

        // A user with a confirmed second factor must also present a fresh code; the message then names both,
        // and never tells which of the two was wrong
        var failure = _totp is not null && await _totp.IsEnrolledAsync(actor.Username, cancellationToken)
            ? "The password or the code is incorrect."
            : "The password is incorrect.";
        var step = await _users.ReauthenticatePasswordAsync(actor.Username, credentials.Password, cancellationToken);
        if (step.User is null || step.User.Id != user.Id)
        {
            throw new DomainRuleException(DomainRule.ReauthenticationFailed, failure);
        }

        // Without the code service a required second factor can not be satisfied: fail closed
        if (step.SecondFactorRequired
            && (_totp is null || !await _totp.VerifyForSignatureAsync(step.User, credentials.TotpCode, cancellationToken)))
        {
            throw new DomainRuleException(DomainRule.ReauthenticationFailed, failure);
        }

        return step.User;
    }

    /// <summary>
    ///     Writes the members of a slot for the audit trail.
    /// </summary>
    /// <param name="primaryUserId">The primary reviewer.</param>
    /// <param name="alternateUserId">The alternate reviewer, or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>For example <c>primary ada, alternate bob</c>; <c>alternate none</c> without an alternate.</returns>
    private async Task<string> DescribeMembersAsync(
        int primaryUserId,
        int? alternateUserId,
        CancellationToken cancellationToken)
    {
        var names = (await _users.ListAsync(cancellationToken)).ToDictionary(user => user.Id, user => user.Username);
        string NameOf(int id) => names.TryGetValue(id, out var name) ? name : $"user {id}";
        var alternate = alternateUserId is { } id ? NameOf(id) : "none";
        return $"primary {NameOf(primaryUserId)}, alternate {alternate}";
    }

    /// <summary>
    ///     Computes what blocks signing from a record.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="slotCount">The number of reviewer slots of the review.</param>
    /// <returns>
    ///     The assessment: the consolidated checklist state (or <c>null</c> without a checklist), the blockers as plain
    ///     sentences (empty when the review can be signed), the number of checks the submitted ballots agree on as Fail
    ///     in sections that apply (informational, not a blocker), the outcome the consolidation leads to and whether
    ///     the conclusion blocker is among the blockers.
    /// </returns>
    /// <remarks>
    ///     A review with one slot counts the partial draft of that slot in the progress and the blockers (never in the
    ///     digest), so the single reviewer sees what is left to answer. An agreed Fail answer does not block signing:
    ///     the outcome is then rejected, and a rejected outcome needs a conclusion text that is not blank.
    /// </remarks>
    private static Assessment Assess(
        ReviewRecord record,
        int slotCount)
    {
        var blockers = new List<string>();

        // A review needs a checklist to be signed
        var view = BallotProjection.Consolidate(record, slotCount);
        if (view is null)
        {
            blockers.Add("This review has no checklist.");
        }

        var checklist = view?.State;
        var failed = view?.Result.Failed ?? 0;
        var outcome = view?.Result.Outcome;
        var conclusionRequired = false;
        if (view is not null)
        {
            // Every check of an applicable section must be answered
            var unanswered = view.Result.Pending;
            if (unanswered > 0)
            {
                blockers.Add(unanswered == 1
                    ? "1 checklist check is unanswered"
                    : $"{unanswered} checklist checks are unanswered");
            }

            // Ballots that disagree must be settled before anybody signs
            var disputed = view.Result.Disputed;
            if (disputed > 0)
            {
                blockers.Add(disputed == 1
                    ? "1 checklist check is disputed between the ballots"
                    : $"{disputed} checklist checks are disputed between the ballots");
            }

            // A rejection is signed with its reasons; the outcome is only known when nothing is pending or disputed
            conclusionRequired = outcome == ReviewOutcome.Rejected && string.IsNullOrWhiteSpace(record.ReviewConclusionText);
        }

        // Every thread must be resolved
        var open = ThreadCounts.From(record.Threads);
        if (open.Open > 0)
        {
            blockers.Add($"{open.OpenText} to resolve");
        }

        if (conclusionRequired)
        {
            blockers.Add(ConclusionRequiredText);
        }

        return new Assessment(checklist, blockers, failed, outcome, conclusionRequired);
    }

    /// <summary>
    ///     Tells whether a record, as it would stand once a ballot is handed in, could be signed, as far as the viewer
    ///     may know.
    /// </summary>
    /// <param name="record">The record as it would stand.</param>
    /// <param name="slotCount">The number of reviewer slots of the review.</param>
    /// <param name="hidden">
    ///     <c>true</c> when the viewer may not see the standing of the other reviewers (see
    ///     <see cref="BallotVisibility"/>): disputes, check threads and the outcome then take no part, so the answer
    ///     never gives them away.
    /// </param>
    /// <returns><c>true</c> when no blocker the viewer may know of is left.</returns>
    internal static bool CanSignAfter(ReviewRecord record, int slotCount, bool hidden)
    {
        var view = BallotProjection.Consolidate(record, slotCount);
        if (view is null || view.Result.Pending > 0)
        {
            return false;
        }

        // The threads the viewer may see; a hidden viewer does not know of the check threads
        var open = record.Threads.Count(thread =>
            thread.State == ReviewThreadState.Open && !(hidden && thread.IsCheckThread));
        if (open > 0)
        {
            return false;
        }

        // Disputes and the conclusion a rejection needs are only known to a viewer who may see them
        if (hidden)
        {
            return true;
        }

        var rejectedWithoutText = view.Result.Outcome == ReviewOutcome.Rejected
                                  && string.IsNullOrWhiteSpace(record.ReviewConclusionText);
        return view.Result.Disputed == 0 && !rejectedWithoutText;
    }

    /// <summary>
    ///     What blocks signing a record and what the signature would attest; see <see cref="Assess"/>.
    /// </summary>
    /// <param name="Checklist">The consolidated checklist state, or <c>null</c> without a checklist.</param>
    /// <param name="Blockers">The blockers as plain sentences; empty when the review can be signed.</param>
    /// <param name="Failed">The number of agreed Fail answers in sections that apply.</param>
    /// <param name="Outcome">The outcome the consolidation leads to, or <c>null</c> while it is not known.</param>
    /// <param name="ConclusionRequired">Whether the conclusion blocker is among the blockers.</param>
    private sealed record Assessment(
        ChecklistState? Checklist,
        IReadOnlyList<string> Blockers,
        int Failed,
        ReviewOutcome? Outcome,
        bool ConclusionRequired);

    /// <summary>
    ///     Words the next step for the reader of a review.
    /// </summary>
    /// <param name="conclusion">How the review is concluded.</param>
    /// <param name="blockers">The blockers of signing.</param>
    /// <param name="slotCount">The number of slots.</param>
    /// <param name="record">The record.</param>
    /// <param name="checklist">The checklist state, or <c>null</c> without a checklist.</param>
    /// <param name="failed">The number of failed checks.</param>
    /// <param name="conclusionRequired">Whether a rejected review still needs its conclusion text.</param>
    /// <param name="outcome">The outcome the consolidation leads to, or <c>null</c> while it is not known.</param>
    /// <returns>One short sentence.</returns>
    private static string NextStepFor(
        ReviewConclusion conclusion,
        IReadOnlyList<string> blockers,
        int slotCount,
        ReviewRecord record,
        ChecklistState? checklist,
        int failed,
        bool conclusionRequired,
        ReviewOutcome? outcome)
    {
        if (conclusion == ReviewConclusion.Approved)
        {
            return "Approved. Every reviewer slot has a current signature.";
        }

        if (conclusion == ReviewConclusion.Rejected)
        {
            return "Rejected. Every reviewer slot has a current signature.";
        }

        if (blockers.Count > 0)
        {
            return ThreadService.NextStep(
                record.Threads,
                checklist?.Overall,
                failed,
                checklist?.Consolidation?.Disputed ?? 0,
                conclusionRequired);
        }

        if (slotCount == 0)
        {
            return "Add a reviewer slot.";
        }

        return outcome == ReviewOutcome.Rejected ? "Ready for signatures (rejected outcome)." : "Ready for signatures.";
    }

    /// <summary>
    ///     Refuses an actor that authenticated with a personal access token.
    /// </summary>
    /// <param name="actor">The actor of the signing action.</param>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/> when the actor is an API token.
    /// </exception>
    /// <remarks>
    ///     A signature is a person's fresh, password-confirmed act, and the slots decide who may sign, so neither can
    ///     be done by a stored credential. The check is the first statement of every slot and signing method, before
    ///     the gate, any password check or any store access, so a future API route that forgets to leave signing out
    ///     still cannot sign.
    /// </remarks>
    private static void RejectApiToken(Actor actor)
    {
        if (actor.ViaApiToken)
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "Reviewer slots and signatures cannot be changed with an API token; sign in to the web pages.");
        }
    }

    /// <summary>
    ///     Checks that the signed-in user still has an enabled account and may work in the project.
    /// </summary>
    /// <param name="actor">The signed-in user.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored user.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> when the actor may not see the project, and with
    ///     <see cref="DomainRule.ActorNotAllowed"/> when the actor may only read it or the account is gone or
    ///     disabled.
    /// </exception>
    private async Task<User> EnsureActorAsync(Actor actor, int projectId, CancellationToken cancellationToken)
    {
        await _access.RequireContributeAsync(actor, projectId, cancellationToken);
        var user = await _users.GetAsync(actor.Username, cancellationToken);
        if (user is null || !user.Enabled)
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "Your account is not allowed to make this change.");
        }

        return user;
    }

    /// <summary>
    ///     Lists the users who may be a reviewer or sign: the members of the project whose role is user or project
    ///     administrator. A read-only auditor is a member but never a reviewer.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The identities of those users.</returns>
    private async Task<HashSet<int>> ReviewerMemberIdsAsync(int projectId, CancellationToken cancellationToken)
    {
        return (await _store.ListMembershipsAsync(projectId, cancellationToken))
            .Where(membership => membership.Role is ProjectRole.User or ProjectRole.ProjectAdmin)
            .Select(membership => membership.UserId)
            .ToHashSet();
    }

    /// <summary>
    ///     Reports whether a user is a member of the project who may be a reviewer or sign (not an auditor).
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the user is a member whose role is user or project administrator.</returns>
    private async Task<bool> IsReviewerMemberAsync(int userId, int projectId, CancellationToken cancellationToken)
    {
        return await _store.GetMembershipAsync(userId, projectId, cancellationToken)
            is { Role: ProjectRole.User or ProjectRole.ProjectAdmin };
    }

    /// <summary>
    ///     Checks that a review exists.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the review is known to exist.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ReviewNotFound"/> when it does not.</exception>
    private async Task EnsureReviewAsync(int projectId, string reviewId, CancellationToken cancellationToken)
    {
        if (!await _store.ReviewExistsAsync(projectId, reviewId, cancellationToken))
        {
            throw ReviewNotFound(reviewId);
        }
    }

    /// <summary>
    ///     Checks that a slot exists and has no signature at all.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The slots of the review, for further checks.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.SlotNotFound"/> or <see cref="DomainRule.SlotSigned"/>.
    /// </exception>
    private async Task<IReadOnlyList<ReviewerSlot>> EnsureUnsignedSlotAsync(
        int projectId,
        string reviewId,
        int slotId,
        CancellationToken cancellationToken)
    {
        var slots = await _store.ListSlotsAsync(projectId, reviewId, cancellationToken);
        if (slots.All(slot => slot.Id != slotId))
        {
            throw SlotNotFound(slotId);
        }

        // Any signature counts, including an out-of-date one: signatures are history and must keep their slot
        var signatures = await _store.ListSignaturesAsync(projectId, reviewId, cancellationToken);
        if (signatures.Any(signature => signature.SlotId == slotId))
        {
            throw new DomainRuleException(
                DomainRule.SlotSigned,
                "This slot has been signed and can no longer be changed.");
        }

        return slots;
    }

    /// <summary>
    ///     Checks the users chosen for a slot.
    /// </summary>
    /// <param name="projectId">The project the slot belongs to; the chosen users must be members of it.</param>
    /// <param name="reviewId">The review the slot belongs to; its author may not be chosen while independence is required.</param>
    /// <param name="slots">The slots of the review now.</param>
    /// <param name="ownSlotId">The slot being changed, which does not count as "another slot"; <c>null</c> for a new slot.</param>
    /// <param name="primaryUserId">The chosen primary reviewer.</param>
    /// <param name="alternateUserId">The chosen alternate reviewer, or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the choice is accepted.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.SlotUserInvalid"/> when the choice is not valid.</exception>
    private async Task ValidateMembersAsync(
        int projectId,
        string reviewId,
        IReadOnlyList<ReviewerSlot> slots,
        int? ownSlotId,
        int primaryUserId,
        int? alternateUserId,
        CancellationToken cancellationToken)
    {
        var memberIds = await ReviewerMemberIdsAsync(projectId, cancellationToken);
        var enabled = (await _users.ListAsync(cancellationToken))
            .Where(user => user.Enabled && memberIds.Contains(user.Id))
            .ToDictionary(user => user.Id);
        if (!enabled.ContainsKey(primaryUserId))
        {
            throw SlotUserInvalid("The primary reviewer must be an enabled member of the project.");
        }

        if (alternateUserId is { } alternate && !enabled.ContainsKey(alternate))
        {
            throw SlotUserInvalid("The alternate reviewer must be an enabled member of the project.");
        }

        if (alternateUserId == primaryUserId)
        {
            throw SlotUserInvalid("The primary and the alternate reviewer must be different users.");
        }

        // A user may sit in one slot only, so one person cannot fill the review with their own approvals
        var others = slots.Where(slot => slot.Id != ownSlotId).ToList();
        var taken = new[] { primaryUserId, alternateUserId ?? 0 }
            .Where(id => id != 0 && others.Any(slot => slot.PrimaryUserId == id || slot.AlternateUserId == id))
            .ToList();
        if (taken.Count > 0)
        {
            throw SlotUserInvalid($"{enabled[taken[0]].DisplayName} is already in another slot of this review.");
        }

        // The author of the review may not be one of its reviewers when the project requires independent ones
        var (review, project) = await LoadForIndependenceAsync(projectId, reviewId, cancellationToken);
        var authorChosen = new[] { primaryUserId, alternateUserId ?? 0 }
            .Where(id => id != 0 && IsAuthorUnderIndependence(review, project, enabled[id]))
            .ToList();
        if (authorChosen.Count > 0)
        {
            throw new DomainRuleException(
                DomainRule.ReviewerNotIndependent,
                $"{enabled[authorChosen[0]].DisplayName} created this review and cannot be one of its reviewers " +
                "while independent reviewers are required.");
        }
    }

    /// <summary>
    ///     Checks that a signer is not the author of the review while the project requires independent reviewers.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="user">The signer's account.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the signer is allowed.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ReviewerNotIndependent"/> when the signer created the review and the
    ///     project requires independent reviewers.
    /// </exception>
    private async Task EnsureIndependentAsync(
        int projectId,
        string reviewId,
        User user,
        CancellationToken cancellationToken)
    {
        var (review, project) = await LoadForIndependenceAsync(projectId, reviewId, cancellationToken);
        if (IsAuthorUnderIndependence(review, project, user))
        {
            throw new DomainRuleException(
                DomainRule.ReviewerNotIndependent,
                "You created this review, so you cannot be a reviewer of it. A different project member must " +
                "review and sign it, or a project administrator can switch off 'Require independent reviewers'.");
        }
    }

    /// <summary>
    ///     Checks that a signer did not reopen the review while the project does not let a reopener sign it again.
    /// </summary>
    /// <param name="record">The record of the review; a never reopened review needs no further read.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="user">The signer's account.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the signer is allowed.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ReopenerCannotSign"/> when the signer reopened the review (once or
    ///     earlier) and the project does not let a reopener sign it again.
    /// </exception>
    private async Task EnsureNotReopenerAsync(
        ReviewRecord record,
        int projectId,
        string reviewId,
        User user,
        CancellationToken cancellationToken)
    {
        if (record.Reopens == 0
            || await _store.GetProjectAsync(projectId, cancellationToken) is not { RequireNonReopenerSigning: true })
        {
            return;
        }

        var reopens = await _store.ListReopensAsync(projectId, reviewId, cancellationToken);
        if (reopens.Any(reopen => InboxRules.SameUser(reopen.ReopenedBy, user.Username)))
        {
            throw new DomainRuleException(
                DomainRule.ReopenerCannotSign,
                "You reopened this review, so you cannot sign it again. Another reviewer must sign it, or a project " +
                "administrator can switch off 'The person who reopened a review cannot sign it again'.");
        }
    }

    /// <summary>
    ///     Checks that a reopen would not leave a slot in which nobody may sign again.
    /// </summary>
    /// <param name="snapshot">    the snapshot of the concluded review that is reopened.</param>
    /// <param name="user">The administrator who reopens.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the reopen is allowed.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ReopenWouldStrandSlot"/> when the project does not let a reopener sign
    ///     again and a slot has only blocked people (the administrator and earlier reopening administrators) in it.
    /// </exception>
    /// <remarks>
    ///     The people are matched by user name (the history keeps names, the slots keep identities). Occupants that
    ///     are disabled or no longer project members are not considered.
    /// </remarks>
    private async Task EnsureNoStrandedSlotAsync(
        ReviewSnapshot snapshot,
        User user,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken)
    {
        if (await _store.GetProjectAsync(projectId, cancellationToken) is not { RequireNonReopenerSigning: true })
        {
            return;
        }

        var blockedNames = new HashSet<string>([user.Username], StringComparer.OrdinalIgnoreCase);
        if (snapshot.Record.Reopens > 0)
        {
            foreach (var reopen in await _store.ListReopensAsync(projectId, reviewId, cancellationToken))
            {
                blockedNames.Add(reopen.ReopenedBy);
            }
        }

        var blockedIds = (await _users.ListAsync(cancellationToken))
            .Where(u => blockedNames.Contains(u.Username))
            .Select(u => u.Id)
            .ToHashSet();
        var stranded = ReopenRules.StrandedSlots(snapshot.Slots, blockedIds);
        if (stranded.Count > 0)
        {
            throw new DomainRuleException(DomainRule.ReopenWouldStrandSlot, ReopenRules.StrandedMessage(stranded));
        }
    }

    /// <summary>
    ///     Loads the review and its project, which together decide whether a user is the author of a review that
    ///     needs independent reviewers.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The review and the project; each is <c>null</c> when it does not exist (nothing is then enforced).</returns>
    private async Task<(Review? Review, Project? Project)> LoadForIndependenceAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken)
    {
        var review = await _store.GetReviewAsync(projectId, reviewId, cancellationToken);
        var project = await _store.GetProjectAsync(projectId, cancellationToken);
        return (review, project);
    }

    /// <summary>
    ///     Tells whether a user is the author of a review whose project requires independent reviewers.
    /// </summary>
    /// <param name="review">The review, or <c>null</c>.</param>
    /// <param name="project">The project, or <c>null</c>.</param>
    /// <param name="user">The user to test.</param>
    /// <returns>
    ///     <c>true</c> when the project requires independent reviewers and the review was created by the user. A review
    ///     without a recorded author (for example one created from the command line) never matches.
    /// </returns>
    /// <remarks>
    ///     The author is the Review Center user who created the review (for a review created with an API token, the
    ///     owner of the token). The authors of git commits are not known to Review Center and are not matched.
    /// </remarks>
    private static bool IsAuthorUnderIndependence(Review? review, Project? project, User user)
    {
        return project is { RequireIndependentReviewers: true }
               && !string.IsNullOrEmpty(review?.CreatedBy)
               && string.Equals(review.CreatedBy, user.Username, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Resolves a user identity to a slot member.
    /// </summary>
    /// <param name="members">All users by identity.</param>
    /// <param name="memberIds">The identities of the current members of the project.</param>
    /// <param name="userId">The identity to resolve.</param>
    /// <returns>The member; a placeholder name when the user is unknown, so a slot never fails to show.</returns>
    private static SlotMember Member(
        IReadOnlyDictionary<int, User> members,
        IReadOnlySet<int> memberIds,
        int userId)
    {
        var isMember = memberIds.Contains(userId);
        return members.TryGetValue(userId, out var user)
            ? new SlotMember(user.Id, user.Username, user.DisplayName, isMember, user.Enabled)
            : new SlotMember(userId, string.Empty, $"User {userId}", isMember);
    }

    /// <summary>
    ///     Builds the exception for a review that does not exist.
    /// </summary>
    /// <param name="reviewId">The review identifier.</param>
    /// <returns>The exception to throw.</returns>
    private static DomainRuleException ReviewNotFound(string reviewId)
    {
        return new DomainRuleException(DomainRule.ReviewNotFound, $"Review '{reviewId}' does not exist.");
    }

    /// <summary>
    ///     Builds the exception for a slot that is not part of the review.
    /// </summary>
    /// <param name="slotId">The identity that was not found.</param>
    /// <returns>The exception to throw.</returns>
    private static DomainRuleException SlotNotFound(int slotId)
    {
        return new DomainRuleException(DomainRule.SlotNotFound, $"Slot {slotId} does not exist in this review.");
    }

    /// <summary>
    ///     Builds the exception for an invalid choice of slot users.
    /// </summary>
    /// <param name="message">The explanation.</param>
    /// <returns>The exception to throw.</returns>
    private static DomainRuleException SlotUserInvalid(string message)
    {
        return new DomainRuleException(DomainRule.SlotUserInvalid, message);
    }

    /// <summary>
    ///     Builds the exception for a record that changed after the signer saw it.
    /// </summary>
    /// <returns>The exception to throw.</returns>
    private static DomainRuleException RecordChangedException()
    {
        return new DomainRuleException(
            DomainRule.RecordChanged,
            "The record changed while you were signing. Review it and try again.");
    }
}

/// <summary>
///     What a signer types to confirm their identity when signing.
/// </summary>
/// <remarks>
///     A parameter object, so that <see cref="SigningService.SignWithCredentialsAsync"/> stays within a readable
///     number of parameters. Nothing in it is ever stored, logged or put in a message.
/// </remarks>
/// <param name="Password">
///     The signer's password, checked like a login; ignored for a user of an external identity provider.
/// </param>
/// <param name="TotpCode">
///     The current authenticator code of a signer who has a second factor; ignored for everyone else.
/// </param>
public sealed record SignerCredentials(string? Password, string? TotpCode = null);
