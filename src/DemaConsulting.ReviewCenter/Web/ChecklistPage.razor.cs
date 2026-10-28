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

using System.Globalization;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the checklist page (<c>ChecklistPage.razor</c>): the checklist of one review, with its
///     progress, sections and checks.
/// </summary>
/// <remarks>
///     The page reads reviews through <see cref="ReviewService"/> and reads and changes the checklist through
///     <see cref="ChecklistService"/> only; the rules live there and a violation is shown next to the element
///     that caused it. One section is open at a time. A Pass is saved as soon as it is chosen; Fail and N/A
///     need a justification, so the choice is kept here, unsaved, until the reviewer saves it. A section is
///     only stored as not applicable together with its reason, so unchecking the switch likewise stays
///     unsaved until the reason is saved. The person recorded with each answer is the signed-in user (an
///     <see cref="Actor"/> read from the authentication state), never something typed on the page. Each browser
///     connection (circuit) gets its own page instance,
///     so the state here is per-user and needs no synchronization.
/// </remarks>
public partial class ChecklistPage : ComponentBase
{
    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     The test id of the element to scroll into view after the next render, or <c>null</c>.
    /// </summary>
    private string? _scrollTarget;

    /// <summary>
    ///     Gets or sets the identity of the owning project, from the page address.
    /// </summary>
    [Parameter]
    public int ProjectId { get; set; }

    /// <summary>
    ///     Gets or sets the review identifier, from the page address.
    /// </summary>
    [Parameter]
    public string ReviewId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the service that reads reviews, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ReviewService ReviewService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that reads and changes the checklist, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ChecklistService ChecklistService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that reads projects, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectService ProjectService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the browser script access used to scroll a check into view, supplied by dependency injection.
    /// </summary>
    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the project access rules that every action re-checks, supplied by dependency injection.
    /// </summary>
    [Inject]
    private IProjectAccess Access { get; set; } = null!;

    /// <summary>
    ///     Gets the name of the owning project shown in the breadcrumb, or <c>null</c> when it is unknown.
    /// </summary>
    private string? ProjectName { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the lookup has finished.
    /// </summary>
    private bool Loaded { get; set; }

    /// <summary>
    ///     Gets the review shown, or <c>null</c> when it does not exist.
    /// </summary>
    private Review? Review { get; set; }

    /// <summary>
    ///     Gets the checklist state, or <c>null</c> when the review does not exist or has no checklist.
    /// </summary>
    private ChecklistState? State { get; set; }

    /// <summary>
    ///     Gets the identifier of the open section, or <c>null</c> when none is open.
    /// </summary>
    private string? OpenSectionId { get; set; }

    /// <summary>
    ///     Gets the outcomes chosen but not yet saved, by item identifier.
    /// </summary>
    private Dictionary<string, ChecklistOutcome> PendingOutcomes { get; } = [];

    /// <summary>
    ///     Gets the justification texts typed, by item identifier; an entry means the text differs from what is saved.
    /// </summary>
    private Dictionary<string, string> JustificationTexts { get; } = [];

    /// <summary>
    ///     Gets the message of the last failed save for each check, by item identifier.
    /// </summary>
    private Dictionary<string, string> ItemErrors { get; } = [];

    /// <summary>
    ///     Gets the sections the switch was unchecked for, but whose reason is not saved yet.
    /// </summary>
    private HashSet<string> PendingNotApplicable { get; } = [];

    /// <summary>
    ///     Gets the reasons typed, by section identifier.
    /// </summary>
    private Dictionary<string, string> ReasonTexts { get; } = [];

    /// <summary>
    ///     Gets the message of the last failed change for each section, by section identifier.
    /// </summary>
    private Dictionary<string, string> SectionErrors { get; } = [];

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Loads the review and its checklist whenever the address parameters change, and clears everything
    ///     typed for the previous review.
    /// </summary>
    /// <returns>A task that completes when the checklist is loaded.</returns>
    protected override async Task OnParametersSetAsync()
    {
        // Clear the previous review before the first await so the loading state is shown, and a stale
        // checklist is never displayed or acted on under the new address
        ResetState();
        Loaded = false;

        // A project the user may not see is treated like a missing one: nothing of its review is loaded
        var project = await ProjectService.GetAsync(await AuthenticationState.GetActorOrNullAsync(), ProjectId);
        ProjectName = project?.Name;
        Review = project is null ? null : await ReviewService.GetAsync(ProjectId, ReviewId);
        State = project is null ? null : await ChecklistService.GetAsync(ProjectId, ReviewId);

        // Start where the work is: the first section with unanswered checks
        OpenSectionId = State?.FirstIncompleteSectionId();
        Loaded = true;
    }

    /// <summary>
    ///     Re-checks that the signed-in user may still view the project, at the start of an action on the open page.
    /// </summary>
    /// <returns>
    ///     <c>true</c> when the action may go on; <c>false</c> when the access is gone, in which case the page has
    ///     already dropped everything it loaded and shows the not-found state, and the action must stop.
    /// </returns>
    private Task<bool> GuardAsync()
    {
        return Access.GuardViewAsync(AuthenticationState, ProjectId, ShowNoAccess);
    }

    /// <summary>
    ///     Runs an action that only changes what the page shows of the loaded checklist, after the access re-check.
    /// </summary>
    /// <param name="action">The action to run while the user may still view the project.</param>
    /// <returns>A task that completes when the action has run or the page has dropped its data.</returns>
    private async Task GuardedAsync(Action action)
    {
        if (await GuardAsync())
        {
            action();
        }
    }

    /// <summary>
    ///     Drops everything loaded and shows the same "does not exist" state as for a non-member.
    /// </summary>
    private void ShowNoAccess()
    {
        ResetState();
        Loaded = true;
    }

    /// <summary>
    ///     Forgets the review, its checklist and everything typed for it.
    /// </summary>
    private void ResetState()
    {
        Review = null;
        State = null;
        ProjectName = null;
        OpenSectionId = null;
        PendingOutcomes.Clear();
        JustificationTexts.Clear();
        ItemErrors.Clear();
        PendingNotApplicable.Clear();
        ReasonTexts.Clear();
        SectionErrors.Clear();
        _scrollTarget = null;
    }

    /// <summary>
    ///     Marks the page interactive after it has rendered on a live circuit, and scrolls a chosen check into view.
    /// </summary>
    /// <param name="firstRender"><c>true</c> for the first render of this component instance.</param>
    /// <returns>A task that completes when the optional scroll has been requested.</returns>
    /// <remarks>
    ///     <c>OnAfterRender</c> is never invoked during static prerendering, so reaching it proves that
    ///     the circuit is connected. A second render makes the new marker value reach the browser. A connection
    ///     that is already gone is ignored.
    /// </remarks>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Only the first render transitions the marker; later renders must not loop
        if (firstRender)
        {
            _interactive = true;
            StateHasChanged();
        }

        if (_scrollTarget is not null)
        {
            var target = _scrollTarget;
            _scrollTarget = null;
            try
            {
                await JS.InvokeVoidAsync("reviewCenter.scrollToTestId", target, "center");
            }
            catch (JSDisconnectedException)
            {
                // The browser went away; there is nothing left to scroll
            }
        }
    }

    /// <summary>
    ///     Opens a section and closes the open one; choosing the open section closes it.
    /// </summary>
    /// <param name="sectionId">The section identifier.</param>
    private void ToggleSection(string sectionId)
    {
        OpenSectionId = OpenSectionId == sectionId ? null : sectionId;
    }

    /// <summary>
    ///     Opens the section of the first unanswered check and scrolls to that check; when only sections being
    ///     switched off are left, opens the first of them and scrolls to its reason field.
    /// </summary>
    private void GoToNextUnanswered()
    {
        var itemId = State?.NextUnansweredItemId(PendingNotApplicable);
        if (itemId is null)
        {
            // Only sections being switched off are left: show the first one's reason field so it can be saved
            var pending = State?.Definition.Sections.FirstOrDefault(section => PendingNotApplicable.Contains(section.Id));
            if (pending is not null)
            {
                OpenSectionId = pending.Id;
                _scrollTarget = $"section-{TestId(pending.Id)}-reason";
            }

            return;
        }

        OpenSectionId = State!.Definition.Sections.First(section => section.Items.Any(item => item.Id == itemId)).Id;
        _scrollTarget = $"check-{TestId(itemId)}";
    }

    /// <summary>
    ///     Handles the "applies to the review" switch of a section.
    /// </summary>
    /// <param name="sectionId">The section identifier.</param>
    /// <param name="applicable">The new position of the switch.</param>
    /// <returns>A task that completes when the change is stored or the problem is shown.</returns>
    /// <remarks>
    ///     Switching on is stored at once (it needs no reason). Switching off only shows the reason field: the
    ///     section is stored as not applicable when the reason is saved.
    /// </remarks>
    private async Task SetApplicableAsync(string sectionId, bool applicable)
    {
        if (!await GuardAsync())
        {
            return;
        }

        SectionErrors.Remove(sectionId);
        if (!applicable)
        {
            PendingNotApplicable.Add(sectionId);
            return;
        }

        PendingNotApplicable.Remove(sectionId);
        ReasonTexts.Remove(sectionId);
        if (State is not null && State.IsApplicable(sectionId))
        {
            return;
        }

        await SaveSectionAsync(sectionId, applicable: true, reason: null);
    }

    /// <summary>
    ///     Stores a section as not applicable with the typed reason.
    /// </summary>
    /// <param name="sectionId">The section identifier.</param>
    /// <returns>A task that completes when the section is stored or the problem is shown.</returns>
    private async Task SaveNotApplicableAsync(string sectionId)
    {
        if (!await GuardAsync())
        {
            return;
        }

        if (await SaveSectionAsync(sectionId, applicable: false, ReasonText(sectionId)))
        {
            PendingNotApplicable.Remove(sectionId);
            ReasonTexts.Remove(sectionId);
        }
    }

    /// <summary>
    ///     Chooses an outcome for a check: a Pass is stored at once, Fail and N/A wait for their justification.
    /// </summary>
    /// <param name="itemId">The check identifier.</param>
    /// <param name="outcome">The outcome chosen.</param>
    /// <returns>A task that completes when the answer is stored or the choice is noted.</returns>
    private async Task ChooseAsync(string itemId, ChecklistOutcome outcome)
    {
        if (!await GuardAsync())
        {
            return;
        }

        ItemErrors.Remove(itemId);
        if (outcome == ChecklistOutcome.Pass)
        {
            await SaveAnswerAsync(itemId, outcome, justification: null);
            return;
        }

        // Keep the saved justification as the starting text when the reviewer only changes the outcome;
        // choosing the stored outcome again leaves nothing unsaved
        if (State?.AnswerFor(itemId)?.Outcome == outcome)
        {
            PendingOutcomes.Remove(itemId);
        }
        else
        {
            PendingOutcomes[itemId] = outcome;
        }
    }

    /// <summary>
    ///     Stores the outcome and justification currently shown for a check.
    /// </summary>
    /// <param name="itemId">The check identifier.</param>
    /// <returns>A task that completes when the answer is stored or the problem is shown.</returns>
    private async Task SaveAsync(string itemId)
    {
        if (!await GuardAsync())
        {
            return;
        }

        if (ShownOutcome(itemId) is { } outcome)
        {
            await SaveAnswerAsync(itemId, outcome, JustificationText(itemId));
        }
    }

    /// <summary>
    ///     Stores an answer for the signed-in user and shows a rule violation next to the check.
    /// </summary>
    /// <param name="itemId">The check identifier.</param>
    /// <param name="outcome">The outcome to store.</param>
    /// <param name="justification">The justification, or <c>null</c> for none.</param>
    /// <returns>A task that completes when the answer is stored or the problem is shown.</returns>
    private async Task SaveAnswerAsync(string itemId, ChecklistOutcome outcome, string? justification)
    {
        try
        {
            await ChecklistService.SetAnswerAsync(
                ProjectId, ReviewId, itemId, outcome, justification, await AuthenticationState.GetActorAsync());
        }
        catch (DomainRuleException ex)
        {
            ItemErrors[itemId] = ex.Message;
            return;
        }

        ItemErrors.Remove(itemId);
        PendingOutcomes.Remove(itemId);
        JustificationTexts.Remove(itemId);
        State = await ChecklistService.GetAsync(ProjectId, ReviewId);
    }

    /// <summary>
    ///     Stores the applicability of a section for the signed-in user and shows a rule violation in the section.
    /// </summary>
    /// <param name="sectionId">The section identifier.</param>
    /// <param name="applicable">Whether the section applies.</param>
    /// <param name="reason">The reason it does not apply, or <c>null</c>.</param>
    /// <returns><c>true</c> when the change was stored.</returns>
    private async Task<bool> SaveSectionAsync(string sectionId, bool applicable, string? reason)
    {
        try
        {
            await ChecklistService.SetSectionApplicabilityAsync(
                ProjectId, ReviewId, sectionId, applicable, reason, await AuthenticationState.GetActorAsync());
        }
        catch (DomainRuleException ex)
        {
            SectionErrors[sectionId] = ex.Message;
            return false;
        }

        SectionErrors.Remove(sectionId);
        State = await ChecklistService.GetAsync(ProjectId, ReviewId);
        return true;
    }

    /// <summary>
    ///     Reports whether the section is shown as applicable: stored as applicable and not just switched off.
    /// </summary>
    /// <param name="sectionId">The section identifier.</param>
    /// <returns><c>true</c> when the checks of the section are shown.</returns>
    private bool IsApplicableInPage(string sectionId)
    {
        return State!.IsApplicable(sectionId) && !PendingNotApplicable.Contains(sectionId);
    }

    /// <summary>
    ///     Gets the reason text shown for a section: what was typed, otherwise the stored reason.
    /// </summary>
    /// <param name="sectionId">The section identifier.</param>
    /// <returns>The text; empty when there is none.</returns>
    private string ReasonText(string sectionId)
    {
        return ReasonTexts.TryGetValue(sectionId, out var typed) ? typed : State?.ReasonFor(sectionId) ?? string.Empty;
    }

    /// <summary>
    ///     Gets the outcome shown for a check: the unsaved choice, otherwise the stored outcome.
    /// </summary>
    /// <param name="itemId">The check identifier.</param>
    /// <returns>The outcome, or <c>null</c> when the check is unanswered.</returns>
    private ChecklistOutcome? ShownOutcome(string itemId)
    {
        if (PendingOutcomes.TryGetValue(itemId, out var pending))
        {
            return pending;
        }

        return State?.AnswerFor(itemId)?.Outcome;
    }

    /// <summary>
    ///     Gets the justification text shown for a check: what was typed, otherwise the stored text.
    /// </summary>
    /// <param name="itemId">The check identifier.</param>
    /// <returns>The text; empty when there is none.</returns>
    private string JustificationText(string itemId)
    {
        return JustificationTexts.TryGetValue(itemId, out var typed)
            ? typed
            : State?.AnswerFor(itemId)?.Justification ?? string.Empty;
    }

    /// <summary>
    ///     Reports whether what is shown for a check differs from what is stored.
    /// </summary>
    /// <param name="itemId">The check identifier.</param>
    /// <returns><c>true</c> when an outcome was chosen or a justification typed and not saved.</returns>
    private bool IsUnsaved(string itemId)
    {
        return PendingOutcomes.ContainsKey(itemId) || JustificationTexts.ContainsKey(itemId);
    }

    /// <summary>
    ///     Words the "what is left" status line.
    /// </summary>
    /// <returns>For example "15 checks unanswered." or "Checklist complete.".</returns>
    private string StatusLine()
    {
        var unanswered = State!.Overall.Unanswered;
        return unanswered switch
        {
            0 => "Checklist complete.",
            1 => "1 check unanswered.",
            _ => $"{unanswered} checks unanswered."
        };
    }

    /// <summary>
    ///     Words the overall progress, mentioning sections that do not apply.
    /// </summary>
    /// <returns>For example "37 of 52 checks answered - 6 sections not applicable".</returns>
    private string ProgressLine()
    {
        var overall = State!.Overall;
        var line = $"{overall.Answered} of {overall.Total} checks answered";
        var notApplicable = State.NotApplicableSectionCount;
        return notApplicable switch
        {
            0 => line,
            1 => $"{line} - 1 section not applicable",
            _ => $"{line} - {notApplicable} sections not applicable"
        };
    }

    /// <summary>
    ///     Words the progress of one section for its heading.
    /// </summary>
    /// <param name="sectionId">The section identifier.</param>
    /// <returns>"Not applicable", "Done" or, for example, "3 of 8".</returns>
    private string SectionProgressLabel(string sectionId)
    {
        if (!State!.IsApplicable(sectionId))
        {
            return "Not applicable";
        }

        var progress = State.SectionProgress(sectionId);
        return progress.IsDone ? "Done" : $"{progress.Answered} of {progress.Total}";
    }

    /// <summary>
    ///     Gets the value of the <c>aria-pressed</c> attribute of an outcome button.
    /// </summary>
    /// <param name="shown">The outcome shown for the check.</param>
    /// <param name="button">The outcome of the button.</param>
    /// <returns>"true" for the button of the shown outcome, otherwise "false".</returns>
    private static string PressedValue(ChecklistOutcome? shown, ChecklistOutcome button)
    {
        return shown == button ? "true" : "false";
    }

    /// <summary>
    ///     Turns a section or check identifier into the form used in test ids.
    /// </summary>
    /// <param name="id">The identifier (for example <c>REQ-01</c>).</param>
    /// <returns>The lower-case form (for example <c>req-01</c>).</returns>
    private static string TestId(string id)
    {
        return id.ToLowerInvariant();
    }

    /// <summary>
    ///     Formats an answer time for display.
    /// </summary>
    /// <param name="time">The time to format.</param>
    /// <returns>The UTC time as "yyyy-MM-dd HH:mm UTC".</returns>
    private static string FormatTime(DateTimeOffset time)
    {
        return time.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }
}
