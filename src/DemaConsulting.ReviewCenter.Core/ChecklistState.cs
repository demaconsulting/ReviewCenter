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
///     A review's frozen checklist together with its answers and section applicability, with the progress calculations.
/// </summary>
/// <remarks>
///     The progress rules live here as pure functions so every page shows the same numbers. Only sections
///     that apply count: a section that does not apply has no checks that need an answer and counts as
///     done. A failed check counts as answered. Answers kept for a section that was later marked as not
///     applicable are retained but ignored. The state is a snapshot: it does not change when the stored
///     answers change, and it is safe to read from several threads.
/// </remarks>
public sealed class ChecklistState
{
    /// <summary>
    ///     The section states by section identifier.
    /// </summary>
    private readonly Dictionary<string, ChecklistSectionState> _sections;

    /// <summary>
    ///     The answers by item identifier.
    /// </summary>
    private readonly Dictionary<string, ChecklistAnswer> _answers;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ChecklistState"/> class.
    /// </summary>
    /// <param name="definition">The frozen checklist. Must not be null.</param>
    /// <param name="sectionStates">The stored section states. Must not be null.</param>
    /// <param name="answers">The stored answers. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public ChecklistState(
        ChecklistDefinition definition,
        IEnumerable<ChecklistSectionState> sectionStates,
        IEnumerable<ChecklistAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(sectionStates);
        ArgumentNullException.ThrowIfNull(answers);
        Definition = definition;
        _sections = sectionStates.ToDictionary(state => state.SectionId, StringComparer.Ordinal);
        _answers = answers.ToDictionary(answer => answer.ItemId, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Gets the frozen checklist.
    /// </summary>
    public ChecklistDefinition Definition { get; }

    /// <summary>
    ///     Gets a value indicating whether the viewer created the review and the project does not let the author answer
    ///     the checklist, so the answers and the applicability of sections are read-only for the viewer.
    /// </summary>
    /// <remarks>
    ///     It is <c>false</c> unless <see cref="ChecklistService.GetAsync"/> was given the viewer; it tells the page what
    ///     to show, the service refuses the change either way.
    /// </remarks>
    public bool AuthorBlocked { get; init; }

    /// <summary>
    ///     Gets the identity of the slot whose ballot this state shows, or <c>null</c> for the consolidated view.
    /// </summary>
    public int? SlotId { get; init; }

    /// <summary>
    ///     Gets the state of the ballot this state shows; <see cref="BallotState.NotStarted"/> when the viewer's slot has
    ///     no answer yet, and for the consolidated view (which is no ballot).
    /// </summary>
    public BallotState BallotState { get; init; } = BallotState.NotStarted;

    /// <summary>
    ///     Gets a value indicating whether the state shows the viewer's own ballot (drafts included) rather than the
    ///     consolidated standing of the submitted ballots.
    /// </summary>
    public bool IsOwnBallot { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the viewer holds no reviewer slot, so the viewer cannot answer; set by
    ///     <see cref="ChecklistService.GetAsync"/> for a viewer without a slot. Such a viewer sees the consolidated
    ///     standing, or, when <see cref="IsMasked"/> is set, no standing.
    /// </summary>
    public bool NoSlot { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the viewer may not see the answers of the other reviewers yet, so this state
    ///     holds no answer and no consolidation; set by <see cref="ChecklistService.GetAsync"/> for a viewer without a
    ///     slot of a review with several slots that is not concluded, unless the viewer is the lead, an administrator
    ///     or an Auditor (see <see cref="BallotVisibility"/>).
    /// </summary>
    public bool IsMasked { get; init; }

    /// <summary>
    ///     Gets the consolidation this state was built from, or <c>null</c> for the state of one ballot.
    /// </summary>
    public BallotConsolidationResult? Consolidation { get; init; }

    /// <summary>
    ///     Gets the standing of a check in the consolidation.
    /// </summary>
    /// <param name="itemId">The item identifier.</param>
    /// <returns>The standing, or <c>null</c> when the state is no consolidation or the check is not listed.</returns>
    public CheckConsolidation? ConsolidationFor(string itemId)
    {
        return Consolidation?.Checks.FirstOrDefault(check => check.ItemId == itemId);
    }

    /// <summary>
    ///     Reports whether the submitted ballots disagree on whether a section applies.
    /// </summary>
    /// <param name="sectionId">The section identifier.</param>
    /// <returns><c>true</c> only for a consolidation in which the section is disputed.</returns>
    public bool IsSectionDisputed(string sectionId)
    {
        return Consolidation?.Sections.Any(s => s.SectionId == sectionId && s.Consensus == SectionConsensus.Disputed) == true;
    }

    /// <summary>
    ///     Gets the stored state of a section.
    /// </summary>
    /// <param name="sectionId">The section identifier.</param>
    /// <returns>The state (the representative row in a consolidation), or <c>null</c> when none was stored.</returns>
    public ChecklistSectionState? SectionStateFor(string sectionId)
    {
        return _sections.GetValueOrDefault(sectionId);
    }

    /// <summary>
    ///     Gets the progress over all sections that apply.
    /// </summary>
    public ChecklistProgress Overall
    {
        get
        {
            var sections = Definition.Sections.Select(section => SectionProgress(section.Id)).ToList();
            return new ChecklistProgress(sections.Sum(p => p.Answered), sections.Sum(p => p.Total));
        }
    }

    /// <summary>
    ///     Gets a value indicating whether every check of every applicable section has been answered
    ///     (also when no section applies).
    /// </summary>
    public bool IsComplete => Overall.IsDone;

    /// <summary>
    ///     Gets the number of sections that have been marked as not applicable.
    /// </summary>
    public int NotApplicableSectionCount => Definition.Sections.Count(section => !IsApplicable(section.Id));

    /// <summary>
    ///     Reports whether a section applies to the review.
    /// </summary>
    /// <param name="sectionId">The section identifier.</param>
    /// <returns><c>false</c> only when the section was marked as not applicable.</returns>
    public bool IsApplicable(string sectionId)
    {
        return !_sections.TryGetValue(sectionId, out var state) || state.Applicable;
    }

    /// <summary>
    ///     Gets the reason a section does not apply.
    /// </summary>
    /// <param name="sectionId">The section identifier.</param>
    /// <returns>The reason, or <c>null</c> when the section applies.</returns>
    public string? ReasonFor(string sectionId)
    {
        return _sections.TryGetValue(sectionId, out var state) && !state.Applicable ? state.Reason : null;
    }

    /// <summary>
    ///     Gets the review a not-applicable section was carried over from.
    /// </summary>
    /// <param name="sectionId">The section identifier.</param>
    /// <returns>The identifier of the earlier review, or <c>null</c> when the section applies or was marked in this review.</returns>
    public string? CarriedFromFor(string sectionId)
    {
        return _sections.TryGetValue(sectionId, out var state) && !state.Applicable ? state.CarriedFromReviewId : null;
    }

    /// <summary>
    ///     Gets the answer to a check.
    /// </summary>
    /// <param name="itemId">The item identifier.</param>
    /// <returns>The answer, or <c>null</c> when the check has not been answered.</returns>
    public ChecklistAnswer? AnswerFor(string itemId)
    {
        return _answers.GetValueOrDefault(itemId);
    }

    /// <summary>
    ///     Gets the progress of one section.
    /// </summary>
    /// <param name="sectionId">The section identifier.</param>
    /// <returns>
    ///     The answered and total checks of the section; 0 of 0 when the section does not apply or does not exist.
    /// </returns>
    public ChecklistProgress SectionProgress(string sectionId)
    {
        var section = Definition.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section is null || !IsApplicable(sectionId))
        {
            return default;
        }

        return new ChecklistProgress(section.Items.Count(item => _answers.ContainsKey(item.Id)), section.Items.Count);
    }

    /// <summary>
    ///     Gets the first section, in checklist order, that applies and still has unanswered checks.
    /// </summary>
    /// <returns>The section identifier, or <c>null</c> when the checklist is complete.</returns>
    public string? FirstIncompleteSectionId()
    {
        return Definition.Sections.FirstOrDefault(section => !SectionProgress(section.Id).IsDone)?.Id;
    }

    /// <summary>
    ///     Gets the first unanswered check of the sections that apply, in checklist order.
    /// </summary>
    /// <param name="skipSectionIds">
    ///     Sections to skip as well, for example those just switched off but not yet saved; <c>null</c> skips none.
    /// </param>
    /// <returns>The item identifier, or <c>null</c> when the checklist is complete.</returns>
    public string? NextUnansweredItemId(IReadOnlySet<string>? skipSectionIds = null)
    {
        return Definition.Sections
            .Where(section => IsApplicable(section.Id) && skipSectionIds?.Contains(section.Id) != true)
            .SelectMany(section => section.Items)
            .FirstOrDefault(item => !_answers.ContainsKey(item.Id))?.Id;
    }
}
