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
///     every applicable check is answered, none is answered Fail and no thread is open; otherwise the blockers
///     are returned as plain sentences. A signature is current only while its digest (see
///     <see cref="RecordDigest"/>) equals the digest of the record now. The status (Approved or In review) is
///     computed on demand and never stored.
///     An approved review is locked: while every slot holds a current signature, the threads, checklist answers,
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
    ///     The statement every signature makes.
    /// </summary>
    public const string MeaningText = "I have reviewed this record and approve it.";

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
        if (!user.IsExternal || !await _access.IsMemberAsync(user.Id, projectId, cancellationToken))
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "Only a member of the project who signs in with an identity provider can confirm this way.");
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
    ///     <see cref="SlotSignatureState.NotSigned"/> otherwise. The review is approved when it has at least one slot
    ///     and every slot is signed.
    /// </remarks>
    public Task<ReviewApproval?> GetApprovalAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        return ReadApprovalAsync(projectId, reviewId, null, null, cancellationToken);
    }

    /// <summary>
    ///     Reads the approval status of several reviews of a project, loading the users once for all of them, and not
    ///     at all when there are no reviews.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewIds">The review identifiers.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The status of each review that exists, by review identifier; a review that does not exist is absent.
    ///     Each status equals what <see cref="GetApprovalAsync"/> returns for the same review.
    /// </returns>
    public async Task<IReadOnlyDictionary<string, ReviewApproval>> GetApprovalsAsync(
        int projectId,
        IEnumerable<string> reviewIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewIds);
        var ids = reviewIds.ToList();
        var approvals = new Dictionary<string, ReviewApproval>();

        // Nothing to read, so do not load the whole user list
        if (ids.Count == 0)
        {
            return approvals;
        }

        var memberIds = (await _store.ListMembershipsAsync(projectId, cancellationToken))
            .Select(membership => membership.UserId)
            .ToHashSet();
        var members = (await _users.ListAsync(cancellationToken)).ToDictionary(user => user.Id);

        // A review that does not exist is absent from the result
        var read = new List<(string ReviewId, ReviewApproval? Approval)>();
        foreach (var reviewId in ids)
        {
            read.Add((reviewId, await ReadApprovalAsync(projectId, reviewId, members, memberIds, cancellationToken)));
        }

        foreach (var (reviewId, approval) in read.Where(r => r.Approval is not null))
        {
            approvals[reviewId] = approval!;
        }

        return approvals;
    }

    /// <summary>
    ///     Reads the approval status of a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="preloaded">The users by identity when the caller already loaded them; <c>null</c> loads them.</param>
    /// <param name="preloadedMemberIds">
    ///     The identities of the current project members when the caller already loaded them; <c>null</c> loads them.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status, or <c>null</c> when the review does not exist.</returns>
    private async Task<ReviewApproval?> ReadApprovalAsync(
        int projectId,
        string reviewId,
        IReadOnlyDictionary<int, User>? preloaded,
        IReadOnlySet<int>? preloadedMemberIds,
        CancellationToken cancellationToken)
    {
        // One snapshot, so the digest, the slots and the signatures compared with it describe the same state
        var snapshot = await _store.GetSnapshotAsync(projectId, reviewId, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        var (record, slots, signatures) = snapshot;
        var members = preloaded ?? (await _users.ListAsync(cancellationToken)).ToDictionary(user => user.Id);

        // Who is still a member: a slot keeps its assignee after a removal, so the status says who can no longer sign
        var memberIds = preloadedMemberIds
                        ?? (await _store.ListMembershipsAsync(projectId, cancellationToken))
                        .Select(membership => membership.UserId)
                        .ToHashSet();
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
                current?.SignedAt));
        }

        var isApproved = ReviewLock.IsApproved(record, slots, signatures);
        var (checklist, blockers, failed) = Assess(record);

        // The history is read only for a review that was reopened, so the common case costs no extra query
        var reopens = record.Reopens > 0
            ? await _store.ListReopensAsync(projectId, reviewId, cancellationToken)
            : null;
        return new ReviewApproval(
            isApproved,
            digest,
            statuses,
            blockers,
            NextStepFor(isApproved, blockers, statuses.Count, record, checklist, failed),
            reopens);
    }

    /// <summary>
    ///     Lists the users that can be chosen for a slot of a project: its enabled members.
    /// </summary>
    /// <param name="actor">The signed-in user who asks; must be able to see the project. Must not be null.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The enabled members ordered by display name and then user name; nothing secret is included.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ProjectNotFound"/> when the actor may not see the project.</exception>
    public async Task<IReadOnlyList<SlotMember>> ListCandidatesAsync(
        Actor actor,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireViewAsync(actor, projectId, cancellationToken);
        var memberIds = (await _store.ListMembershipsAsync(projectId, cancellationToken))
            .Select(membership => membership.UserId)
            .ToHashSet();
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
    ///     <see cref="DomainRule.ReviewLocked"/> when the review is approved and with
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

            // An approved review is locked: who may sign it can not change until it is reopened
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
    /// <returns>A task that completes when the slot is stored.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/>, <see cref="DomainRule.SlotNotFound"/>,
    ///     <see cref="DomainRule.SlotSigned"/> (the slot has any signature, current or out of date) or
    ///     <see cref="DomainRule.SlotUserInvalid"/> when a rule is violated; nothing is stored then. Also thrown with
    ///     <see cref="DomainRule.ReviewLocked"/> when the review is approved and with
    ///     <see cref="DomainRule.ReviewerNotIndependent"/> as <see cref="AddSlotAsync"/> does.
    /// </exception>
    public async Task UpdateSlotAsync(
        Actor actor,
        int projectId,
        string reviewId,
        int slotId,
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
            await ReviewLock.EnsureUnlockedAsync(_store, projectId, reviewId, cancellationToken);
            var slots = await EnsureUnsignedSlotAsync(projectId, reviewId, slotId, cancellationToken);
            await ValidateMembersAsync(
                projectId, reviewId, slots, slotId, primaryUserId, alternateUserId, cancellationToken);
            var members = await DescribeMembersAsync(primaryUserId, alternateUserId, cancellationToken);

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
    /// <returns>A task that completes when the slot is removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/>, <see cref="DomainRule.SlotNotFound"/> or
    ///     <see cref="DomainRule.SlotSigned"/> when a rule is violated; nothing is removed then. Also thrown with
    ///     <see cref="DomainRule.ReviewLocked"/> when the review is approved.
    /// </exception>
    public async Task RemoveSlotAsync(
        Actor actor,
        int projectId,
        string reviewId,
        int slotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RejectApiToken(actor);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureActorAsync(actor, projectId, cancellationToken);
            await ReviewLock.EnsureUnlockedAsync(_store, projectId, reviewId, cancellationToken);
            var slots = await EnsureUnsignedSlotAsync(projectId, reviewId, slotId, cancellationToken);

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
    ///     <see cref="DomainRule.ReopenReasonInvalid"/>, <see cref="DomainRule.ReviewNotFound"/> or
    ///     <see cref="DomainRule.ReviewNotLocked"/> (the review is not approved) when a rule is violated; nothing is
    ///     stored then.
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

            // What: only an approved review is locked; the digest of that approval goes into the history
            var snapshot = await _store.GetSnapshotAsync(projectId, reviewId, cancellationToken)
                           ?? throw ReviewNotFound(reviewId);
            if (!ReviewLock.IsApproved(snapshot.Record, snapshot.Slots, snapshot.Signatures))
            {
                throw new DomainRuleException(
                    DomainRule.ReviewNotLocked,
                    "The review is not locked, so there is nothing to reopen.");
            }

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
    ///     requires independent reviewers.
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
    ///     requires independent reviewers.
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
            // The record must be the one the signer saw, and must be signable; both are refused before the
            // password is asked for
            var digest = await EnsureSignableAsync(
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
                    Meaning = MeaningText
                },
                AuditDraft.By(
                    actor,
                    "signature.added",
                    $"slot {slotNumber}, role {role}, record digest {digest}",
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
        if (!await _access.IsMemberAsync(user.Id, projectId, cancellationToken))
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "Only a member of the project can sign its reviews.");
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
    /// <returns>The digest of the record, which the signature will point to.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown when the record changed, the slot already has a current signature or a blocker remains.
    /// </exception>
    private async Task<string> EnsureSignableAsync(
        ReviewRecord record,
        int projectId,
        string reviewId,
        int slotId,
        string expectedDigest,
        CancellationToken cancellationToken)
    {
        // An approved review is locked, so nobody signs it again; the message says why instead of "already signed"
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

        var (_, blockers, _) = Assess(record);
        if (blockers.Count > 0)
        {
            throw new DomainRuleException(
                DomainRule.SigningNotAllowed,
                "The review cannot be signed yet: " + string.Join("; ", blockers) + ".");
        }

        return digest;
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
    /// <returns>
    ///     The checklist state (or <c>null</c> without a checklist), the blockers as plain sentences (empty when the
    ///     review can be signed) and the number of checks answered Fail in sections that apply.
    /// </returns>
    private static (ChecklistState? Checklist, IReadOnlyList<string> Blockers, int Failed) Assess(ReviewRecord record)
    {
        var blockers = new List<string>();

        // A review needs a checklist to be signed
        if (record.Checklist is null)
        {
            blockers.Add("This review has no checklist.");
        }

        ChecklistState? checklist = null;
        var failed = 0;
        if (record.Checklist is not null)
        {
            checklist = new ChecklistState(record.Checklist.Parse(), record.SectionStates, record.Answers);

            // Every check of an applicable section must be answered, and none may be a Fail
            var unanswered = checklist.Overall.Unanswered;
            if (unanswered > 0)
            {
                blockers.Add(unanswered == 1
                    ? "1 checklist check is unanswered"
                    : $"{unanswered} checklist checks are unanswered");
            }

            failed = checklist.Definition.Sections
                .Where(section => checklist.IsApplicable(section.Id))
                .SelectMany(section => section.Items)
                .Count(item => checklist.AnswerFor(item.Id)?.Outcome == ChecklistOutcome.Fail);
            if (failed > 0)
            {
                blockers.Add(failed == 1 ? "1 check is answered Fail" : $"{failed} checks are answered Fail");
            }
        }

        // Every thread must be resolved
        var open = record.Threads.Count(thread => thread.State == ReviewThreadState.Open);
        if (open > 0)
        {
            blockers.Add(open == 1 ? "1 thread is open" : $"{open} threads are open");
        }

        return (checklist, blockers, failed);
    }

    /// <summary>
    ///     Words the next step for the reader of a review.
    /// </summary>
    /// <param name="isApproved">Whether the review is approved.</param>
    /// <param name="blockers">The blockers of signing.</param>
    /// <param name="slotCount">The number of slots.</param>
    /// <param name="record">The record.</param>
    /// <param name="checklist">The checklist state, or <c>null</c> without a checklist.</param>
    /// <param name="failed">The number of failed checks.</param>
    /// <returns>One short sentence.</returns>
    private static string NextStepFor(
        bool isApproved,
        IReadOnlyList<string> blockers,
        int slotCount,
        ReviewRecord record,
        ChecklistState? checklist,
        int failed)
    {
        if (isApproved)
        {
            return "Approved. Every reviewer slot has a current signature.";
        }

        if (blockers.Count > 0)
        {
            return ThreadService.NextStep(record.Threads, checklist?.Overall, failed);
        }

        return slotCount == 0 ? "Add a reviewer slot." : "Ready for signatures.";
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
    ///     Checks that the signed-in user still has an enabled account and may see the project.
    /// </summary>
    /// <param name="actor">The signed-in user.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored user.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> when the actor may not see the project, and with
    ///     <see cref="DomainRule.ActorNotAllowed"/> when the account is gone or disabled.
    /// </exception>
    private async Task<User> EnsureActorAsync(Actor actor, int projectId, CancellationToken cancellationToken)
    {
        await _access.RequireViewAsync(actor, projectId, cancellationToken);
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
        var memberIds = (await _store.ListMembershipsAsync(projectId, cancellationToken))
            .Select(membership => membership.UserId)
            .ToHashSet();
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
            ? new SlotMember(user.Id, user.Username, user.DisplayName, isMember)
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
