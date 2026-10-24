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
///     not-applicable check needs a justification. There is no sign-in yet, so the name of the person
///     answering is unverified text; that is TEMPORARY until the signed-in user replaces it in slice 4.
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
    ///     The clock that supplies answer times.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ChecklistService"/> class.
    /// </summary>
    /// <param name="store">The store that persists checklists. Must not be null.</param>
    /// <param name="timeProvider">The clock used for answer times; injected so tests control time. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public ChecklistService(IReviewStore store, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
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
    /// <param name="setBy">The name of the person; same rules as the answer name (see <see cref="SetAnswerAsync"/>).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the applicability is stored.</returns>
    /// <exception cref="DomainRuleException">Thrown when a rule is violated (see <see cref="DomainRule"/>); nothing is stored then.</exception>
    public async Task SetSectionApplicabilityAsync(
        int projectId,
        string reviewId,
        string sectionId,
        bool applicable,
        string? reason,
        string? setBy,
        CancellationToken cancellationToken = default)
    {
        var name = ValidateName(setBy);
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
                SetBy = name,
                SetAt = _timeProvider.GetUtcNow()
            },
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
    /// <param name="answeredBy">
    ///     The name of the person answering; leading and trailing white space is removed. Must not be empty and
    ///     must not exceed <see cref="ReviewLimits.MaxAuthorLength"/> characters. TEMPORARY free text until sign-in exists.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the answer is stored.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome"/> is not a defined value.</exception>
    /// <exception cref="DomainRuleException">Thrown when a rule is violated (see <see cref="DomainRule"/>); nothing is stored then.</exception>
    public async Task SetAnswerAsync(
        int projectId,
        string reviewId,
        string itemId,
        ChecklistOutcome outcome,
        string? justification,
        string? answeredBy,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The outcome is not a known value.");
        }

        var name = ValidateName(answeredBy);
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
                AnsweredBy = name,
                AnsweredAt = _timeProvider.GetUtcNow()
            },
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
    ///     Checks and normalizes the name of the person answering.
    /// </summary>
    /// <param name="name">The candidate name.</param>
    /// <returns>The name without leading and trailing white space.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ChecklistAnsweredByInvalid"/> when the name is empty or too long.</exception>
    private static string ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainRuleException(DomainRule.ChecklistAnsweredByInvalid, "Your name is required.");
        }

        if (trimmed.Length > ReviewLimits.MaxAuthorLength)
        {
            throw new DomainRuleException(
                DomainRule.ChecklistAnsweredByInvalid,
                $"Your name must not be longer than {ReviewLimits.MaxAuthorLength} characters.");
        }

        return trimmed;
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
