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
    /// <returns>The state, or <c>null</c> when the review does not exist or has no checklist.</returns>
    public async Task<ChecklistState?> GetAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        var checklist = await _store.GetChecklistAsync(projectId, reviewId, cancellationToken);
        if (checklist is null)
        {
            return null;
        }

        var sections = await _store.ListChecklistSectionStatesAsync(projectId, reviewId, cancellationToken);
        var answers = await _store.ListChecklistAnswersAsync(projectId, reviewId, cancellationToken);
        return new ChecklistState(checklist.Parse(), sections, answers);
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
    /// <returns>A task that completes when the applicability is stored.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown when a rule is violated (see <see cref="DomainRule"/>); nothing is stored then.</exception>
    public async Task SetSectionApplicabilityAsync(
        int projectId,
        string reviewId,
        string sectionId,
        bool applicable,
        string? reason,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireViewAsync(actor, projectId, cancellationToken);
        var definition = await GetDefinitionAsync(projectId, reviewId, cancellationToken);
        if (!definition.Sections.Any(section => section.Id == sectionId))
        {
            throw new DomainRuleException(
                DomainRule.ChecklistItemNotFound,
                $"The checklist has no section '{sectionId}'.");
        }

        // A section that applies has no reason; one that does not apply must say why
        var trimmedReason = applicable ? null : ValidateJustification(reason, required: true, "reason");

        await _store.SetChecklistSectionStateAsync(
            new ChecklistSectionState
            {
                ProjectId = projectId,
                ReviewId = reviewId,
                SectionId = sectionId,
                Applicable = applicable,
                Reason = trimmedReason,
                SetBy = actor.DisplayName,
                SetByUsername = actor.Username,
                SetAt = _timeProvider.GetUtcNow()
            },
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
    /// <returns>A task that completes when the answer is stored.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome"/> is not a defined value.</exception>
    /// <exception cref="DomainRuleException">Thrown when a rule is violated (see <see cref="DomainRule"/>); nothing is stored then.</exception>
    public async Task SetAnswerAsync(
        int projectId,
        string reviewId,
        string itemId,
        ChecklistOutcome outcome,
        string? justification,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireViewAsync(actor, projectId, cancellationToken);
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

        await _store.SetChecklistAnswerAsync(
            new ChecklistAnswer
            {
                ProjectId = projectId,
                ReviewId = reviewId,
                ItemId = itemId,
                Outcome = outcome,
                Justification = trimmed,
                AnsweredBy = actor.DisplayName,
                AnsweredByUsername = actor.Username,
                AnsweredAt = _timeProvider.GetUtcNow()
            },
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
