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
///     Every change is refused with <see cref="DomainRule.ReviewLocked"/> while the review is approved (see
///     <see cref="ReviewLock"/>).
///     When the project requires it (on by default, also for projects that existed before the rule was enforced), the
///     person who created the review cannot answer its checks or set the applicability of its sections: the change is
///     refused with <see cref="DomainRule.AuthorCannotAnswer"/>. Comment threads are not part of that rule.
///     A change that would make a current signature out of date is refused with
///     <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/> unless the caller acknowledges the affected slots.
///     The service holds no state of its own and is safe for concurrent use as long as the injected
///     <see cref="IReviewStore"/> is.
/// </remarks>
public sealed class ChecklistService
{
    /// <summary>
    ///     The store that persists checklists.
    /// </summary>
    private readonly IReviewStore _store;

    /// <summary>
    ///     Decides who may see which project; every change needs a user who can see it.
    /// </summary>
    private readonly IProjectAccess _access;

    /// <summary>
    ///     The clock that supplies answer times.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ChecklistService"/> class.
    /// </summary>
    /// <param name="store">The store that persists checklists. Must not be null.</param>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <param name="timeProvider">The clock used for answer times; injected so tests control time. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public ChecklistService(IReviewStore store, IProjectAccess access, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _access = access;
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
        var checklist = await _store.GetChecklistAsync(projectId, reviewId, cancellationToken);
        if (checklist is null)
        {
            return null;
        }

        var sections = await _store.ListChecklistSectionStatesAsync(projectId, reviewId, cancellationToken);
        var answers = await _store.ListChecklistAnswersAsync(projectId, reviewId, cancellationToken);

        // Only a viewer is asked about; the project and review are read for them alone
        var authorBlocked = viewerUsername is not null
                            && IsAuthorBlocked(
                                await _store.GetProjectAsync(projectId, cancellationToken),
                                await _store.GetReviewAsync(projectId, reviewId, cancellationToken),
                                viewerUsername);
        return new ChecklistState(checklist.Parse(), sections, answers) { AuthorBlocked = authorBlocked };
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

        var state = new ChecklistSectionState
        {
            ProjectId = projectId,
            ReviewId = reviewId,
            SectionId = sectionId,
            Applicable = applicable,
            Reason = trimmedReason,
            SetBy = actor.DisplayName,
            SetByUsername = actor.Username,
            SetAt = _timeProvider.GetUtcNow()
        };

        // A change to what the signatures cover needs the acknowledgement of every signature it makes out of date
        SignatureImpact.RequireAcknowledged(
            snapshot,
            snapshot is null
                ? null
                : snapshot.Record with
                {
                    SectionStates = snapshot.Record.SectionStates
                        .Where(s => s.SectionId != sectionId)
                        .Append(state)
                        .ToList()
                },
            acknowledgedSlotIds);

        await _store.SetChecklistSectionStateAsync(
            state,
            AuditDraft.By(
                actor,
                "checklist.applicability-changed",
                applicable
                    ? $"section {sectionId}: applicable"
                    : $"section {sectionId}: not applicable, reason sha256 {AuditText.ShortHash(trimmedReason)}",
                projectId,
                reviewId),
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

        var answer = new ChecklistAnswer
        {
            ProjectId = projectId,
            ReviewId = reviewId,
            ItemId = itemId,
            Outcome = outcome,
            Justification = trimmed,
            AnsweredBy = actor.DisplayName,
            AnsweredByUsername = actor.Username,
            AnsweredAt = _timeProvider.GetUtcNow()
        };

        // A change to what the signatures cover needs the acknowledgement of every signature it makes out of date
        SignatureImpact.RequireAcknowledged(
            snapshot,
            snapshot is null
                ? null
                : snapshot.Record with
                {
                    Answers = snapshot.Record.Answers
                        .Where(a => a.ItemId != itemId)
                        .Append(answer)
                        .ToList()
                },
            acknowledgedSlotIds);

        await _store.SetChecklistAnswerAsync(
            answer,
            AuditDraft.By(
                actor,
                "checklist.answered",
                trimmed is null
                    ? $"item {itemId}: {outcome}"
                    : $"item {itemId}: {outcome}; justification sha256 {AuditText.ShortHash(trimmed)}",
                projectId,
                reviewId),
            cancellationToken);
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
