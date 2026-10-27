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
///     has a primary reviewer and an optional alternate, and either may sign. Any signed-in user may add,
///     change and remove slots, but only while the slot has no signature at all (current or out of date),
///     because a signature must always point to a slot that still describes who signed. A user occupies at
///     most one slot of a review.
///     Signing needs the signer's password again: it is checked by <see cref="UserService.ReauthenticateAsync"/>,
///     the same path the login uses, so the hasher, the lockout counting and the single generic failure are the
///     same as at login; a failure is recorded in the audit trail as <c>signature.reauth-failed</c>. The signer is the <see cref="Actor"/> taken from the signed-in principal, never a form
///     field, and the password is neither stored, logged nor put in a message. A signature is allowed only when
///     every applicable check is answered, none is answered Fail and no thread is open; otherwise the blockers
///     are returned as plain sentences. A signature is current only while its digest (see
///     <see cref="RecordDigest"/>) equals the digest of the record now. The status (Approved or In review) is
///     computed on demand and never stored.
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
    ///     The clock that supplies signing times.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Serializes every operation that changes slots or signatures.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    ///     Initializes a new instance of the <see cref="SigningService"/> class.
    /// </summary>
    /// <param name="store">The store that persists slots and signatures. Must not be null.</param>
    /// <param name="users">The user service used to look up users and to re-authenticate signers. Must not be null.</param>
    /// <param name="timeProvider">The clock used for signing times; injected so tests control time. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public SigningService(IReviewStore store, UserService users, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _users = users;
        _timeProvider = timeProvider;
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
        return ReadApprovalAsync(projectId, reviewId, null, cancellationToken);
    }

    /// <summary>
    ///     Reads the approval status of several reviews of a project, loading the users once for all of them.
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
        var members = (await _users.ListAsync(cancellationToken)).ToDictionary(user => user.Id);
        var approvals = new Dictionary<string, ReviewApproval>();
        foreach (var reviewId in reviewIds)
        {
            if (await ReadApprovalAsync(projectId, reviewId, members, cancellationToken) is { } approval)
            {
                approvals[reviewId] = approval;
            }
        }

        return approvals;
    }

    /// <summary>
    ///     Reads the approval status of a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="preloaded">The users by identity when the caller already loaded them; <c>null</c> loads them.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status, or <c>null</c> when the review does not exist.</returns>
    private async Task<ReviewApproval?> ReadApprovalAsync(
        int projectId,
        string reviewId,
        IReadOnlyDictionary<int, User>? preloaded,
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
                Member(members, slot.PrimaryUserId),
                slot.AlternateUserId is { } alternate ? Member(members, alternate) : null,
                state,
                current?.DisplayName,
                current?.SignedAt));
        }

        var isApproved = statuses.Count > 0 && statuses.All(s => s.State == SlotSignatureState.Signed);
        var (checklist, blockers, failed) = Assess(record);
        return new ReviewApproval(
            isApproved,
            digest,
            statuses,
            blockers,
            NextStepFor(isApproved, blockers, statuses.Count, record, checklist, failed));
    }

    /// <summary>
    ///     Lists the users that can be chosen for a slot.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The enabled users ordered by display name and then user name; nothing secret is included.</returns>
    public async Task<IReadOnlyList<SlotMember>> ListCandidatesAsync(CancellationToken cancellationToken = default)
    {
        return (await _users.ListAsync(cancellationToken))
            .Where(user => user.Enabled)
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
    ///     <see cref="DomainRule.SlotUserInvalid"/> when a rule is violated; nothing is stored then.
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
            await EnsureActorAsync(actor, cancellationToken);
            await EnsureReviewAsync(projectId, reviewId, cancellationToken);
            var slots = await _store.ListSlotsAsync(projectId, reviewId, cancellationToken);
            await ValidateMembersAsync(slots, null, primaryUserId, alternateUserId, cancellationToken);
            var members = await DescribeMembersAsync(primaryUserId, alternateUserId, cancellationToken);

            return await _store.AddSlotAsync(
                new ReviewerSlot
                {
                    ProjectId = projectId,
                    ReviewId = reviewId,
                    PrimaryUserId = primaryUserId,
                    AlternateUserId = alternateUserId
                },
                stored => AuditDraft.By(actor, "slot.added", $"slot {stored.Id}: {members}", projectId, reviewId),
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
    ///     <see cref="DomainRule.SlotUserInvalid"/> when a rule is violated; nothing is stored then.
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
            await EnsureActorAsync(actor, cancellationToken);
            var slots = await EnsureUnsignedSlotAsync(projectId, reviewId, slotId, cancellationToken);
            await ValidateMembersAsync(slots, slotId, primaryUserId, alternateUserId, cancellationToken);
            var members = await DescribeMembersAsync(primaryUserId, alternateUserId, cancellationToken);

            var audit = AuditDraft.By(actor, "slot.updated", $"slot {slotId}: {members}", projectId, reviewId);
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
    ///     <see cref="DomainRule.SlotSigned"/> when a rule is violated; nothing is removed then.
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
            await EnsureActorAsync(actor, cancellationToken);
            var slots = await EnsureUnsignedSlotAsync(projectId, reviewId, slotId, cancellationToken);

            // The entry describes the slot as it was before it goes
            var slot = slots.First(s => s.Id == slotId);
            var members = await DescribeMembersAsync(slot.PrimaryUserId, slot.AlternateUserId, cancellationToken);
            var audit = AuditDraft.By(actor, "slot.removed", $"slot {slotId}: {members}", projectId, reviewId);
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
    ///     should discard it as soon as this call returns.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored signature.</returns>
    /// <remarks>
    ///     The slot, the seat and the signing rules are all checked before the password is asked for, so a
    ///     refused signing never counts as a failed password attempt. Every password failure (wrong, locked,
    ///     disabled or unknown) gives the same generic message and signs nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/>, <see cref="DomainRule.ReviewNotFound"/>,
    ///     <see cref="DomainRule.SlotNotFound"/>, <see cref="DomainRule.SigningNotAllowed"/> (already signed, or
    ///     blocked by the checklist or threads), <see cref="DomainRule.ReauthenticationFailed"/> or
    ///     <see cref="DomainRule.RecordChanged"/> when a rule is violated; nothing is stored then.
    /// </exception>
    public async Task<ReviewSignature> SignAsync(
        Actor actor,
        int projectId,
        string reviewId,
        int slotId,
        string expectedDigest,
        string? password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(expectedDigest);
        RejectApiToken(actor);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // The signer must be a live account, and must be one of the two people of the slot
            var user = await EnsureActorAsync(actor, cancellationToken);
            var record = await _store.GetRecordAsync(projectId, reviewId, cancellationToken)
                         ?? throw ReviewNotFound(reviewId);
            var slot = (await _store.ListSlotsAsync(projectId, reviewId, cancellationToken))
                .FirstOrDefault(s => s.Id == slotId) ?? throw SlotNotFound(slotId);
            if (user.Id != slot.PrimaryUserId && user.Id != slot.AlternateUserId)
            {
                throw new DomainRuleException(
                    DomainRule.ActorNotAllowed,
                    "You are not a reviewer of this slot.");
            }

            var role = user.Id == slot.PrimaryUserId ? SlotRole.Primary : SlotRole.Alternate;

            // The record must be the one the signer saw, and must be signable; both are refused before the
            // password is asked for
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

            // Re-authenticate through the login path (same hasher, lockout and generic failure); a failure is
            // recorded as a failed re-authentication, not as a failed sign-in
            var verified = await _users.ReauthenticateAsync(actor.Username, password, cancellationToken);
            if (verified is null || verified.Id != user.Id)
            {
                throw new DomainRuleException(DomainRule.ReauthenticationFailed, "The password is incorrect.");
            }

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
                    $"slot {slotId}, role {role}, record digest {digest}",
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
    ///     Checks that the signed-in user still has an enabled account.
    /// </summary>
    /// <param name="actor">The signed-in user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored user.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ActorNotAllowed"/> when the account is gone or disabled.</exception>
    private async Task<User> EnsureActorAsync(Actor actor, CancellationToken cancellationToken)
    {
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
    /// <param name="slots">The slots of the review now.</param>
    /// <param name="ownSlotId">The slot being changed, which does not count as "another slot"; <c>null</c> for a new slot.</param>
    /// <param name="primaryUserId">The chosen primary reviewer.</param>
    /// <param name="alternateUserId">The chosen alternate reviewer, or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the choice is accepted.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.SlotUserInvalid"/> when the choice is not valid.</exception>
    private async Task ValidateMembersAsync(
        IReadOnlyList<ReviewerSlot> slots,
        int? ownSlotId,
        int primaryUserId,
        int? alternateUserId,
        CancellationToken cancellationToken)
    {
        var enabled = (await _users.ListAsync(cancellationToken)).Where(user => user.Enabled).ToDictionary(user => user.Id);
        if (!enabled.ContainsKey(primaryUserId))
        {
            throw SlotUserInvalid("The primary reviewer must be an existing, enabled user.");
        }

        if (alternateUserId is { } alternate && !enabled.ContainsKey(alternate))
        {
            throw SlotUserInvalid("The alternate reviewer must be an existing, enabled user.");
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
    }

    /// <summary>
    ///     Resolves a user identity to a slot member.
    /// </summary>
    /// <param name="members">All users by identity.</param>
    /// <param name="userId">The identity to resolve.</param>
    /// <returns>The member; a placeholder name when the user is unknown, so a slot never fails to show.</returns>
    private static SlotMember Member(IReadOnlyDictionary<int, User> members, int userId)
    {
        return members.TryGetValue(userId, out var user)
            ? new SlotMember(user.Id, user.Username, user.DisplayName)
            : new SlotMember(userId, string.Empty, $"User {userId}");
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
