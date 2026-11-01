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
///     Reads and answers the checklist of a review, enforcing the checklist rules.
/// </summary>
/// <remarks>
///     The service is the single place where checklist rules live. The checklist itself is the frozen copy
///     stored with the review (see <see cref="ReviewChecklist"/>); the service only records which sections
///     apply and the answer to each check. A section that does not apply needs a reason; a failed or
///     not-applicable check needs a justification. The person answering is the signed-in <see cref="Actor"/>,
///     never free text.
///     The answers are held per reviewer slot: a person answers into the ballot of the slot they hold as primary or
///     alternate reviewer (see <see cref="Ballot"/>), a person without a slot is refused with
///     <see cref="DomainRule.NoReviewerSlot"/>, and the ballot counts in the record once it is handed in (see
///     <see cref="SubmitBallotAsync"/>; a review with one slot hands it in with the last answer). <see cref="GetAsync"/>
///     shows a reviewer the own ballot and everybody else the consolidated standing, never the ballot of another slot.
///     Every change is refused with <see cref="DomainRule.ReviewLocked"/> while the review is approved (see
///     <see cref="ReviewLock"/>).
///     When the project requires it (on by default, also for projects that existed before the rule was enforced), the
///     person who created the review cannot answer its checks or set the applicability of its sections: the change is
///     refused with <see cref="DomainRule.AuthorCannotAnswer"/>. Comment threads are not part of that rule.
///     A change that would make a current signature out of date is refused with
///     <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/> unless the caller acknowledges the affected slots.
///     The service also owns the review conclusion text: the free text, in plain text, in which the reviewers write the
///     conclusion of the record (see <see cref="Review.ReviewConclusionText"/>; not the approved or rejected conclusion
///     of the ballot). It lives here because it is written to the same record as the answers and follows the same
///     rules: it is part of the digest, so a change needs the same acknowledgement, and it is refused while the review
///     is concluded.
///     The service holds no state of its own and is safe for concurrent use as long as the injected
///     <see cref="IReviewStore"/> is.
/// </remarks>
public sealed class ChecklistService
{
    /// <summary>
    ///     The audit action recorded when the review conclusion text changes; its detail names lengths, never the text.
    /// </summary>
    public const string ReviewConclusionTextChangedAction = "review.conclusion-changed";

    /// <summary>
    ///     The store that persists checklists.
    /// </summary>
    private readonly IReviewStore _store;

    /// <summary>
    ///     Decides who may see which project; every change needs a user who can see it.
    /// </summary>
    private readonly IProjectAccess _access;

    /// <summary>
    ///     Resolves a user name to a user, so the reviewers of the slots of a review can be recognized.
    /// </summary>
    private readonly IUserStore _users;

    /// <summary>
    ///     The clock that supplies answer times.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ChecklistService"/> class.
    /// </summary>
    /// <param name="store">The store that persists checklists. Must not be null.</param>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <param name="users">The user store used to recognize slot owners for the review conclusion text. Must not be null.</param>
    /// <param name="timeProvider">The clock used for answer times; injected so tests control time. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public ChecklistService(IReviewStore store, IProjectAccess access, IUserStore users, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _access = access;
        _users = users;
        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     Reads the checklist of a review with its answers and section applicability.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="viewerUsername">
    ///     The user name of the person who looks at the checklist, or <c>null</c> (the default) when there is none;
    ///     it decides <see cref="ChecklistState.AuthorBlocked"/>.
    /// </param>
    /// <returns>The state, or <c>null</c> when the review does not exist or has no checklist.</returns>
    public async Task<ChecklistState?> GetAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default,
        string? viewerUsername = null)
    {
        // One consistent read of the record (with the ballots) and the slots
        var snapshot = await _store.GetSnapshotAsync(projectId, reviewId, cancellationToken);
        if (snapshot?.Record.Checklist is null)
        {
            return null;
        }

        var definition = snapshot.Record.Checklist.Parse();

        // Only a viewer is asked about; the project and review are read for them alone
        var authorBlocked = viewerUsername is not null
                            && IsAuthorBlocked(
                                await _store.GetProjectAsync(projectId, cancellationToken),
                                await _store.GetReviewAsync(projectId, reviewId, cancellationToken),
                                viewerUsername);

        // A reviewer sees the own ballot, drafts included, and never the ballot of another slot; everybody else sees the
        // standing of the submitted ballots
        var own = viewerUsername is null
            ? null
            : await FindOwnSlotAsync(viewerUsername, snapshot.Slots, cancellationToken);
        if (own is not null)
        {
            var ballot = snapshot.Record.Ballots.FirstOrDefault(b => b.SlotId == own.Id);
            return BallotProjection.ForBallot(definition, own.Id, ballot, authorBlocked);
        }

        return BallotProjection.Consolidate(snapshot.Record, snapshot.Slots.Count, authorBlocked, viewerUsername is not null)!.State;
    }

    /// <summary>
    ///     Records whether a section of the checklist applies to the review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="sectionId">The section identifier within the frozen checklist.</param>
    /// <param name="applicable"><c>true</c> when the section applies; <c>false</c> when it does not.</param>
    /// <param name="reason">
    ///     The reason the section does not apply; leading and trailing white space is removed. Required when
    ///     <paramref name="applicable"/> is <c>false</c>, at most <see cref="ReviewLimits.MaxJustificationLength"/>
    ///     characters; ignored (and cleared) when the section applies.
    /// </param>
    /// <param name="actor">The signed-in user who sets the applicability. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="acknowledgedSlotIds">
    ///     The identities of the reviewer slots whose signatures the person agreed to make out of date, or
    ///     <c>null</c> for none. Only needed when the change alters the record while a signature is current.
    /// </param>
    /// <returns>A task that completes when the applicability is stored.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown when a rule is violated (see <see cref="DomainRule"/>), among them
    ///     <see cref="DomainRule.NoReviewerSlot"/> when the actor holds no reviewer slot of the review,
    ///     <see cref="DomainRule.AuthorCannotAnswer"/> when the actor created the review and the project does not let
    ///     the author answer, and
    ///     <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/> when a current signature is not acknowledged;
    ///     nothing is stored then.
    /// </exception>
    public async Task SetSectionApplicabilityAsync(
        int projectId,
        string reviewId,
        string sectionId,
        bool applicable,
        string? reason,
        Actor actor,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? acknowledgedSlotIds = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireContributeAsync(actor, projectId, cancellationToken);
        await RequireNotAuthorAsync(projectId, reviewId, actor, cancellationToken);
        var definition = await GetDefinitionAsync(projectId, reviewId, cancellationToken);
        if (!definition.Sections.Any(section => section.Id == sectionId))
        {
            throw new DomainRuleException(
                DomainRule.ChecklistItemNotFound,
                $"The checklist has no section '{sectionId}'.");
        }

        // A section that applies has no reason; one that does not apply must say why
        var trimmedReason = applicable ? null : ValidateJustification(reason, required: true, "reason");

        // An approved review is locked: the checklist is part of what the signatures cover
        var snapshot = await ReviewLock.EnsureUnlockedAsync(_store, projectId, reviewId, cancellationToken);

        // The answer goes into the ballot of the slot of the actor; nobody else has a ballot to answer on
        var slot = await RequireOwnSlotAsync(actor, snapshot, cancellationToken);

        var state = new ChecklistSectionState
        {
            ProjectId = projectId,
            ReviewId = reviewId,
            SlotId = slot.Id,
            SectionId = sectionId,
            Applicable = applicable,
            Reason = trimmedReason,
            SetBy = actor.DisplayName,
            SetByUsername = actor.Username,
            SetAt = _timeProvider.GetUtcNow()
        };

        // A change to what the signatures cover needs the acknowledgement of every signature it makes out of date; the
        // own signature of the reviewer is the one the change withdraws and needs none
        var edited = BallotEdits.WithSectionState(CurrentBallot(snapshot, slot), slot.Id, state);
        SignatureImpact.RequireAcknowledged(
            snapshot,
            AfterEdit(snapshot, definition, edited, state.SetAt, actor.Username),
            acknowledgedSlotIds,
            slot.Id);

        await _store.SetBallotSectionStateAsync(
            state,
            AuditDraft.By(
                actor,
                "checklist.applicability-changed",
                applicable
                    ? $"section {sectionId}: applicable"
                    : $"section {sectionId}: not applicable, reason sha256 {AuditText.ShortHash(trimmedReason)}",
                projectId,
                reviewId),
            ballot => BallotConsolidation.IsComplete(definition, ballot),
            cancellationToken);
    }

    /// <summary>
    ///     Records the answer to one check of the checklist, replacing an earlier answer.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="itemId">The check identifier within the frozen checklist (for example <c>REQ-01</c>).</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="justification">
    ///     The justification; leading and trailing white space is removed. Required for
    ///     <see cref="ChecklistOutcome.Fail"/> and <see cref="ChecklistOutcome.NotApplicable"/>, optional for a
    ///     pass (an empty one is stored as none), at most <see cref="ReviewLimits.MaxJustificationLength"/> characters.
    /// </param>
    /// <param name="actor">The signed-in user who answers. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="acknowledgedSlotIds">
    ///     The identities of the reviewer slots whose signatures the person agreed to make out of date, or
    ///     <c>null</c> for none. Only needed when the change alters the record while a signature is current.
    /// </param>
    /// <returns>A task that completes when the answer is stored.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome"/> is not a defined value.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown when a rule is violated (see <see cref="DomainRule"/>), among them
    ///     <see cref="DomainRule.NoReviewerSlot"/> when the actor holds no reviewer slot of the review,
    ///     <see cref="DomainRule.AuthorCannotAnswer"/> when the actor created the review and the project does not let
    ///     the author answer, and
    ///     <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/> when a current signature is not acknowledged;
    ///     nothing is stored then.
    /// </exception>
    public async Task SetAnswerAsync(
        int projectId,
        string reviewId,
        string itemId,
        ChecklistOutcome outcome,
        string? justification,
        Actor actor,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? acknowledgedSlotIds = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireContributeAsync(actor, projectId, cancellationToken);
        await RequireNotAuthorAsync(projectId, reviewId, actor, cancellationToken);
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The outcome is not a known value.");
        }

        var definition = await GetDefinitionAsync(projectId, reviewId, cancellationToken);
        if (!definition.Sections.SelectMany(section => section.Items).Any(item => item.Id == itemId))
        {
            throw new DomainRuleException(
                DomainRule.ChecklistItemNotFound,
                $"The checklist has no check '{itemId}'.");
        }

        // Only a pass can go without a justification; an empty optional one is stored as none
        var trimmed = ValidateJustification(justification, outcome != ChecklistOutcome.Pass, "justification");

        // An approved review is locked: the checklist is part of what the signatures cover
        var snapshot = await ReviewLock.EnsureUnlockedAsync(_store, projectId, reviewId, cancellationToken);

        // The answer goes into the ballot of the slot of the actor; nobody else has a ballot to answer on
        var slot = await RequireOwnSlotAsync(actor, snapshot, cancellationToken);

        var answer = new ChecklistAnswer
        {
            ProjectId = projectId,
            ReviewId = reviewId,
            SlotId = slot.Id,
            ItemId = itemId,
            Outcome = outcome,
            Justification = trimmed,
            AnsweredBy = actor.DisplayName,
            AnsweredByUsername = actor.Username,
            AnsweredAt = _timeProvider.GetUtcNow()
        };

        // A change to what the signatures cover needs the acknowledgement of every signature it makes out of date; the
        // own signature of the reviewer is the one the change withdraws and needs none
        var edited = BallotEdits.WithAnswer(CurrentBallot(snapshot, slot), slot.Id, answer);
        SignatureImpact.RequireAcknowledged(
            snapshot,
            AfterEdit(snapshot, definition, edited, answer.AnsweredAt, actor.Username),
            acknowledgedSlotIds,
            slot.Id);

        await _store.SetBallotAnswerAsync(
            answer,
            AuditDraft.By(
                actor,
                "checklist.answered",
                trimmed is null
                    ? $"item {itemId}: {outcome}"
                    : $"item {itemId}: {outcome}; justification sha256 {AuditText.ShortHash(trimmed)}",
                projectId,
                reviewId),
            ballot => BallotConsolidation.IsComplete(definition, ballot),
            cancellationToken);
    }

    /// <summary>
    ///     Hands in the ballot of the slot of the actor, so it counts in the consolidated record.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="actor">The signed-in user who hands the ballot in; must be the primary or alternate reviewer of a slot. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="acknowledgedSlotIds">
    ///     The identities of the reviewer slots whose signatures the person agreed to make out of date, or
    ///     <c>null</c> for none. Only needed when handing in the ballot changes the record while a signature of another
    ///     slot is current.
    /// </param>
    /// <returns>The submitted ballot; a ballot that was submitted already is returned as it is.</returns>
    /// <remarks>
    ///     A review with exactly one slot hands the ballot in with the last answer, so this call is only needed when no
    ///     answer fired the hand-in (a checklist whose sections are all not applicable, for example). The completeness
    ///     test runs again inside the store transaction.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.NoReviewerSlot"/> when the actor holds no slot,
    ///     <see cref="DomainRule.BallotIncomplete"/> when a check that applies has no answer,
    ///     <see cref="DomainRule.ReviewLocked"/> when the review is concluded, and
    ///     <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/> when a current signature of another slot is not
    ///     acknowledged; nothing is stored then.
    /// </exception>
    public async Task<Ballot> SubmitBallotAsync(
        int projectId,
        string reviewId,
        Actor actor,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? acknowledgedSlotIds = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireContributeAsync(actor, projectId, cancellationToken);
        await RequireNotAuthorAsync(projectId, reviewId, actor, cancellationToken);
        var definition = await GetDefinitionAsync(projectId, reviewId, cancellationToken);
        var snapshot = await ReviewLock.EnsureUnlockedAsync(_store, projectId, reviewId, cancellationToken);
        var slot = await RequireOwnSlotAsync(actor, snapshot, cancellationToken);

        // The ballot as it would be once handed in; an incomplete one is refused here and again in the store
        var current = CurrentBallot(snapshot, slot) ?? new Ballot(slot.Id, BallotStatus.Draft, null, null, [], []);
        if (!BallotConsolidation.IsComplete(definition, current))
        {
            throw new DomainRuleException(
                DomainRule.BallotIncomplete,
                "The ballot cannot be submitted while a check that applies has no answer.");
        }

        var now = _timeProvider.GetUtcNow();
        var submitted = current with { Status = BallotStatus.Submitted, SubmittedAt = now, SubmittedByUsername = actor.Username };
        SignatureImpact.RequireAcknowledged(
            snapshot,
            BallotEdits.WithBallot(snapshot!.Record, submitted),
            acknowledgedSlotIds,
            slot.Id);

        var number = snapshot.Slots.OrderBy(s => s.Id).ToList().FindIndex(s => s.Id == slot.Id) + 1;
        return await _store.SubmitBallotAsync(
            projectId,
            reviewId,
            slot.Id,
            now,
            actor.Username,
            AuditDraft.By(
                actor,
                "ballot.submitted",
                $"slot {number}: ballot submitted ({current.Answers.Count} answers, {current.SectionStates.Count} section states)",
                projectId,
                reviewId),
            ballot => BallotConsolidation.IsComplete(definition, ballot),
            cancellationToken);
    }

    /// <summary>
    ///     Finds the slot a user holds in a review.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="slots">The slots of the review.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The slot of which the user is the primary or the alternate reviewer; <c>null</c> for an unknown user or no slot.</returns>
    private async Task<ReviewerSlot?> FindOwnSlotAsync(
        string username,
        IReadOnlyList<ReviewerSlot> slots,
        CancellationToken cancellationToken)
    {
        if (slots.Count == 0)
        {
            return null;
        }

        var user = await _users.GetByUsernameAsync(username, cancellationToken);
        return user is null
            ? null
            : slots.OrderBy(s => s.Id).FirstOrDefault(s => s.PrimaryUserId == user.Id || s.AlternateUserId == user.Id);
    }

    /// <summary>
    ///     Finds the slot of the actor or refuses the change.
    /// </summary>
    /// <param name="actor">The signed-in user.</param>
    /// <param name="snapshot">The snapshot of the review, or <c>null</c> when the review does not exist.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The slot of the actor.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.NoReviewerSlot"/> when the actor holds no slot.</exception>
    private async Task<ReviewerSlot> RequireOwnSlotAsync(
        Actor actor,
        ReviewSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        var slot = snapshot is null ? null : await FindOwnSlotAsync(actor.Username, snapshot.Slots, cancellationToken);
        return slot ?? throw new DomainRuleException(
            DomainRule.NoReviewerSlot,
            "You are not a reviewer of this review, so you cannot answer its checklist. Add yourself as a reviewer " +
            "slot first.");
    }

    /// <summary>
    ///     Gets the ballot of a slot from a snapshot.
    /// </summary>
    /// <param name="snapshot">The snapshot of the review, or <c>null</c>.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The ballot, or <c>null</c> when the slot has none yet.</returns>
    private static Ballot? CurrentBallot(ReviewSnapshot? snapshot, ReviewerSlot slot)
    {
        return snapshot?.Record.Ballots.FirstOrDefault(b => b.SlotId == slot.Id);
    }

    /// <summary>
    ///     Computes the record an edit of a ballot leads to.
    /// </summary>
    /// <param name="snapshot">The snapshot read before the edit, or <c>null</c> when the review does not exist.</param>
    /// <param name="definition">The frozen checklist.</param>
    /// <param name="edited">The ballot as a draft after the edit.</param>
    /// <param name="at">The moment of the edit.</param>
    /// <param name="username">The user name of the editor.</param>
    /// <returns>
    ///     The record with the edited ballot; the ballot is submitted when the review has exactly one slot and the edit
    ///     completes it, as the store does, so the digest compared is the digest that results.
    /// </returns>
    private static ReviewRecord? AfterEdit(
        ReviewSnapshot? snapshot,
        ChecklistDefinition definition,
        Ballot edited,
        DateTimeOffset at,
        string username)
    {
        if (snapshot is null)
        {
            return null;
        }

        var ballot = snapshot.Slots.Count == 1 && BallotConsolidation.IsComplete(definition, edited)
            ? edited with { Status = BallotStatus.Submitted, SubmittedAt = at, SubmittedByUsername = username }
            : edited;
        return BallotEdits.WithBallot(snapshot.Record, ballot);
    }

    /// <summary>
    ///     Tells whether an actor may write the review conclusion text of a review.
    /// </summary>
    /// <remarks>
    ///     The text is written by the people who stand behind the record: the primary or the alternate reviewer of any
    ///     slot of the review, or the lead of the review (the person who created it and can still contribute, or an
    ///     administrator of the project). A read-only member (an auditor), a person outside the project and an API token
    ///     may not. The answer is about the person only: whether the review is concluded (and therefore locked) is a
    ///     separate matter, so a page shows its edit control only when both allow it.
    ///     <see cref="SetReviewConclusionTextAsync"/> checks the same rule again, so the pages are never the only guard.
    /// </remarks>
    /// <param name="actor">The signed-in user; may be null, which is not allowed.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the actor is a person who may write the text; <c>false</c> otherwise, also when the review does not exist.</returns>
    public async Task<bool> CanSetReviewConclusionTextAsync(
        Actor? actor,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        if (actor is null || actor.ViaApiToken || !await _access.CanContributeAsync(actor, projectId, cancellationToken))
        {
            return false;
        }

        var review = await _store.GetReviewAsync(projectId, reviewId, cancellationToken);
        var snapshot = await _store.GetSnapshotAsync(projectId, reviewId, cancellationToken);
        return review is not null
               && snapshot is not null
               && await IsConclusionTextWriterAsync(actor, review, snapshot, cancellationToken);
    }

    /// <summary>
    ///     Sets, changes or clears the review conclusion text of a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="conclusionText">
    ///     The new review conclusion text, in plain text. Line breaks are normalized to <c>\n</c>, leading and trailing
    ///     white space is removed, and a blank text clears it. At most <see cref="ReviewLimits.MaxConclusionTextLength"/>
    ///     characters after that; control characters other than line breaks and tabs are refused.
    /// </param>
    /// <param name="actor">
    ///     The signed-in user; must be the primary or the alternate reviewer of a slot of the review, or its lead (see
    ///     <see cref="CanSetReviewConclusionTextAsync"/>). An API token is refused. Must not be null.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="acknowledgedSlotIds">
    ///     The identities of the reviewer slots whose signatures the person agreed to make out of date, or
    ///     <c>null</c> for none. Needed when the change alters the digest while a signature is current.
    /// </param>
    /// <returns><c>true</c> when the text was changed; <c>false</c> when the review already had this text and nothing was recorded.</returns>
    /// <remarks>
    ///     The text is the free-text conclusion of the record; it is not the review conclusion of the ballot (approved or
    ///     rejected), which is a decision. It is part of the digest (the <c>conclusion-text</c> line), so changing it
    ///     while a signature is current needs the acknowledgement of the affected reviewers. It is audited as
    ///     <c>review.conclusion-changed</c> with the old and the new length only, never the text. The change is a
    ///     compare-and-set on the text this method read, stored with its audit entry in one transaction, so two people who
    ///     write at the same moment cannot silently overwrite each other. A concluded review is locked and refuses the
    ///     change; after a reopen the text can be changed again. The text is not carried over to a follow-up.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> when the actor may not see the project,
    ///     <see cref="DomainRule.ReviewNotFound"/> when the review does not exist,
    ///     <see cref="DomainRule.ActorNotAllowed"/> for a read-only member, an API token or a member who is neither a
    ///     reviewer of the review nor its lead, <see cref="DomainRule.ConclusionTextInvalid"/> for a text that is too long
    ///     or has control characters, <see cref="DomainRule.ReviewLocked"/> when the review is concluded,
    ///     <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/> when a current signature is not acknowledged, and
    ///     <see cref="DomainRule.RecordChanged"/> when someone else changed the text at the same moment; nothing is
    ///     stored then.
    /// </exception>
    public async Task<bool> SetReviewConclusionTextAsync(
        int projectId,
        string reviewId,
        string? conclusionText,
        Actor actor,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? acknowledgedSlotIds = null)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // Whoever may not see the project learns nothing about it; a read-only member (an auditor) never writes
        await _access.RequireContributeAsync(actor, projectId, cancellationToken);
        if (actor.ViaApiToken)
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "The review conclusion text cannot be written with an API token; sign in to the web pages.");
        }

        var review = await _store.GetReviewAsync(projectId, reviewId, cancellationToken);
        var snapshot = await _store.GetSnapshotAsync(projectId, reviewId, cancellationToken);
        if (review is null || snapshot is null)
        {
            throw new DomainRuleException(DomainRule.ReviewNotFound, "The review does not exist.");
        }

        // Who may write it: a reviewer of any slot, or the lead of the review
        if (!await IsConclusionTextWriterAsync(actor, review, snapshot, cancellationToken))
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "Only a reviewer of the review or its lead can write the review conclusion text.");
        }

        var normalized = NormalizeConclusionText(conclusionText);

        // A concluded review is locked: the text is part of what the signatures cover
        if (ReviewLock.IsConcluded(snapshot.Record, snapshot.Slots, snapshot.Signatures))
        {
            throw new DomainRuleException(DomainRule.ReviewLocked, ReviewLock.LockedMessage);
        }

        // The same text again changes nothing: no acknowledgement, no audit entry
        var previous = snapshot.Record.ReviewConclusionText;
        if (string.Equals(previous, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        // A change to what the signatures cover needs the acknowledgement of every signature it makes out of date
        SignatureImpact.RequireAcknowledged(
            snapshot,
            snapshot.Record with { ReviewConclusionText = normalized },
            acknowledgedSlotIds);

        // The audit entry names lengths only: the text itself stays out of the audit trail
        var stored = await _store.UpdateReviewConclusionTextAsync(
            projectId,
            reviewId,
            previous,
            normalized,
            AuditDraft.By(
                actor,
                ReviewConclusionTextChangedAction,
                $"length {LengthOf(previous)} -> {LengthOf(normalized)}",
                projectId,
                reviewId),
            cancellationToken);
        if (!stored)
        {
            throw new DomainRuleException(
                DomainRule.RecordChanged,
                "The review conclusion text was changed by someone else. The current text is shown; check it and save again.");
        }

        return true;
    }

    /// <summary>
    ///     Tells whether a person who may contribute is a reviewer of the review or its lead.
    /// </summary>
    /// <param name="actor">The signed-in person.</param>
    /// <param name="review">The review.</param>
    /// <param name="snapshot">The snapshot whose slots decide who is a reviewer.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the person holds a slot (primary or alternate) of the review or leads it.</returns>
    private async Task<bool> IsConclusionTextWriterAsync(
        Actor actor,
        Review review,
        ReviewSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        return await ReviewActorRoles.IsSlotOwnerAsync(_users, actor, snapshot.Slots, cancellationToken)
               || await ReviewActorRoles.IsLeadAsync(_access, actor, review, cancellationToken);
    }

    /// <summary>
    ///     Checks and normalizes a review conclusion text.
    /// </summary>
    /// <param name="text">The candidate text, or <c>null</c>.</param>
    /// <returns>The text with <c>\n</c> line breaks and without leading and trailing white space, or <c>null</c> when it is blank.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ConclusionTextInvalid"/> when the normalized text is longer than
    ///     <see cref="ReviewLimits.MaxConclusionTextLength"/> characters or contains a control character other than a
    ///     line feed or a tab.
    /// </exception>
    private static string? NormalizeConclusionText(string? text)
    {
        // Browsers send CRLF; one form keeps the stored text, its length and the digest independent of the sender
        var unified = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);

        // Plain text only: line breaks and tabs are fine, other control characters (a lone carriage return included)
        // are not. This runs before the trim, which would otherwise drop a leading or trailing carriage return.
        if (unified.Any(c => char.IsControl(c) && c != '\n' && c != '\t'))
        {
            throw new DomainRuleException(
                DomainRule.ConclusionTextInvalid,
                "The review conclusion text must be plain text without control characters.");
        }

        var normalized = unified.Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        if (normalized.Length > ReviewLimits.MaxConclusionTextLength)
        {
            throw new DomainRuleException(
                DomainRule.ConclusionTextInvalid,
                $"The review conclusion text must not be longer than {ReviewLimits.MaxConclusionTextLength} characters.");
        }

        return normalized;
    }

    /// <summary>
    ///     Gets the length of a review conclusion text for the audit detail.
    /// </summary>
    /// <param name="text">The text, or <c>null</c> for none.</param>
    /// <returns>The length in characters; zero for none.</returns>
    private static int LengthOf(string? text)
    {
        return text?.Length ?? 0;
    }

    /// <summary>
    ///     Tells whether a user created the review while the project does not let the author answer its checklist.
    /// </summary>
    /// <param name="project">The project, or <c>null</c> when it does not exist (nothing is then enforced).</param>
    /// <param name="review">The review, or <c>null</c> when it does not exist (nothing is then enforced).</param>
    /// <param name="username">The user name to test, or <c>null</c> for nobody.</param>
    /// <returns>
    ///     <c>true</c> when the project requires that the author does not answer and the review was created by the
    ///     user, compared without regard to case. A review without a recorded author (for example one created from
    ///     the command line) never matches.
    /// </returns>
    /// <remarks>
    ///     The author is the Review Center user who created the review (for a review created with an API token, the
    ///     owner of the token). The authors of git commits are not known to Review Center and are not matched.
    /// </remarks>
    internal static bool IsAuthorBlocked(Project? project, Review? review, string? username)
    {
        return project is { RequireNonAuthorChecklistAnswers: true }
               && !string.IsNullOrEmpty(username)
               && !string.IsNullOrEmpty(review?.CreatedBy)
               && string.Equals(review.CreatedBy, username, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Refuses the change when the actor created the review and the project does not let the author answer.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="actor">The signed-in user who changes the checklist.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the actor is allowed.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.AuthorCannotAnswer"/> when the actor is blocked.</exception>
    private async Task RequireNotAuthorAsync(
        int projectId,
        string reviewId,
        Actor actor,
        CancellationToken cancellationToken)
    {
        var project = await _store.GetProjectAsync(projectId, cancellationToken);
        if (project is not { RequireNonAuthorChecklistAnswers: true })
        {
            return;
        }

        var review = await _store.GetReviewAsync(projectId, reviewId, cancellationToken);
        if (IsAuthorBlocked(project, review, actor.Username))
        {
            throw new DomainRuleException(
                DomainRule.AuthorCannotAnswer,
                "You created this review, so you cannot answer its checklist in this project. Another project member " +
                "must answer it, or a project administrator can switch off 'Authors cannot answer the checklist of " +
                "their own review'.");
        }
    }

    /// <summary>
    ///     Reads the frozen checklist definition of a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ChecklistNotFound"/> when the review has no checklist.</exception>
    private async Task<ChecklistDefinition> GetDefinitionAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken)
    {
        var checklist = await _store.GetChecklistAsync(projectId, reviewId, cancellationToken);
        return checklist?.Parse()
               ?? throw new DomainRuleException(
                   DomainRule.ChecklistNotFound,
                   $"Review '{reviewId}' does not exist or has no checklist.");
    }

    /// <summary>
    ///     Checks and normalizes a justification or reason.
    /// </summary>
    /// <param name="text">The candidate text.</param>
    /// <param name="required">Whether the text must not be empty.</param>
    /// <param name="label">The word used for the text in messages (for example <c>justification</c>).</param>
    /// <returns>The text without leading and trailing white space, or <c>null</c> when it is empty and optional.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ChecklistJustificationInvalid"/> when the text is missing or too long.</exception>
    private static string? ValidateJustification(string? text, bool required, string label)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return required
                ? throw new DomainRuleException(DomainRule.ChecklistJustificationInvalid, $"A {label} is required.")
                : null;
        }

        if (trimmed.Length > ReviewLimits.MaxJustificationLength)
        {
            throw new DomainRuleException(
                DomainRule.ChecklistJustificationInvalid,
                $"The {label} must not be longer than {ReviewLimits.MaxJustificationLength} characters.");
        }

        return trimmed;
    }
}
