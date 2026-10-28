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

using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the evidence settings page (<c>EvidenceSettingsPage.razor</c>): shows and changes the
///     anonymous-access and PDF-content settings of one project.
/// </summary>
/// <remarks>
///     An attribute on the page requires a signed-in user and the page shows the switches only to whoever may
///     administer the project (a system administrator or a project admin of it). Every handler re-checks that
///     through the shared guard before it reads or changes anything, and drops everything the page holds when the
///     right is gone; <see cref="EvidenceSettingsService"/> checks again and records the change in the audit trail.
///     The switches edit a draft that is saved as a whole; the warnings follow the draft. The save carries the
///     settings the page loaded, so a change made by someone else in the meantime is refused and the current
///     settings are shown instead of being overwritten. Each browser connection (circuit) gets its own page
///     instance, so the state here is per-user and needs no synchronization.
/// </remarks>
public partial class EvidenceSettingsPage : ComponentBase
{
    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     Backing field for <see cref="Switches"/>; built once because the switches only read and write the draft.
    /// </summary>
    private IReadOnlyList<SettingSwitch>? _switches;

    /// <summary>
    ///     Gets or sets the identity of the project whose settings are shown, from the page address.
    /// </summary>
    [Parameter]
    public int ProjectId { get; set; }

    /// <summary>
    ///     Gets or sets the service that reads and changes the evidence settings, supplied by dependency injection.
    /// </summary>
    [Inject]
    private EvidenceSettingsService Settings { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that reads projects, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectService ProjectService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the access service, supplied by dependency injection; decides who may administer the project.
    /// </summary>
    [Inject]
    private IProjectAccess Access { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets the project the settings belong to, or <c>null</c> when it does not exist or the user may not
    ///     administer it.
    /// </summary>
    private Project? Project { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the lookup has finished.
    /// </summary>
    private bool Loaded { get; set; }

    /// <summary>
    ///     Gets the settings as stored when the page loaded or last saved; a save is based on them.
    /// </summary>
    private EvidenceSettings Saved { get; set; } = EvidenceSettings.Default;

    /// <summary>
    ///     Gets the settings as the switches show them now (not yet saved).
    /// </summary>
    private EvidenceSettings Draft { get; set; } = EvidenceSettings.Default;

    /// <summary>
    ///     Gets the message of the last rejected save, or <c>null</c> when there is none.
    /// </summary>
    private string? ErrorMessage { get; set; }

    /// <summary>
    ///     Gets the confirmation of the last save, or <c>null</c> when there is none.
    /// </summary>
    private string? StatusMessage { get; set; }

    /// <summary>
    ///     Gets the number of anonymous requests per minute and address the server allows, for the warning text.
    /// </summary>
    private static int AnonymousLimit => Api.AnonymousRateLimit.Limit;

    /// <summary>
    ///     Gets the content sections the draft leaves out of the PDFs.
    /// </summary>
    private IReadOnlyList<string> OmittedSections => Draft.OmittedSections;

    /// <summary>
    ///     Gets a value indicating whether the draft opens anything to anonymous readers.
    /// </summary>
    private bool AnonymousIsOn => Draft.AnonymousIndex || Draft.AnonymousAttestations;

    /// <summary>
    ///     Gets a value indicating whether anonymous readers would get PDFs that still carry comments or names.
    /// </summary>
    private bool AnonymousPdfShowsPeopleOrComments =>
        Draft.AnonymousAttestations && (Draft.IncludeThreads || Draft.IncludeReviewerNames);

    /// <summary>
    ///     Gets the words that name what an anonymous PDF would still contain, for the exposure warning.
    /// </summary>
    private string AnonymousPdfExposure => (Draft.IncludeThreads, Draft.IncludeReviewerNames) switch
    {
        (true, true) => "the review discussion and the reviewer names",
        (true, false) => "the review discussion",
        _ => "the reviewer names",
    };

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Gets the switches in the order they are shown: the anonymous ones first, then the PDF content ones.
    /// </summary>
    private IReadOnlyList<SettingSwitch> Switches => _switches ??=
    [
        new SettingSwitch(
            "anonymous-index",
            "Anonymous evidence index",
            "Anyone may read the evidence index of this project without signing in. The index alone is useless to "
            + "ReviewMark unless anonymous attestation PDFs are on too, or the client has credentials.",
            null,
            true,
            () => Draft.AnonymousIndex,
            value => Draft = Draft with { AnonymousIndex = value }),
        new SettingSwitch(
            "anonymous-attestations",
            "Anonymous attestation PDFs",
            "Anyone may download the attestation PDFs of this project without signing in.",
            null,
            true,
            () => Draft.AnonymousAttestations,
            value => Draft = Draft with { AnonymousAttestations = value }),
        new SettingSwitch(
            "threads",
            "Include review discussion threads",
            "The PDF lists the comment threads of the review with their comments and how each thread ended.",
            "Omitting the review discussion may not satisfy your regulatory regime, which may expect the findings "
            + "and their resolution to be on record. Confirm with your quality or compliance lead.",
            false,
            () => Draft.IncludeThreads,
            value => Draft = Draft with { IncludeThreads = value }),
        new SettingSwitch(
            "reviewer-names",
            "Include reviewer names",
            "The PDF names the people: requester, reviewers, who answered each question and who commented.",
            "Omitting reviewer names may not satisfy your regulatory regime. "
            + "Confirm with your quality or compliance lead.",
            false,
            () => Draft.IncludeReviewerNames,
            value => Draft = Draft with { IncludeReviewerNames = value }),
        new SettingSwitch(
            "checklist-wording",
            "Include checklist wording",
            "The PDF shows the wording of each checklist question and section title next to its outcome.",
            "Without the checklist wording the PDF shows outcomes only, so a reader cannot see what was checked. "
            + "This may not satisfy your regulatory regime. Confirm with your quality or compliance lead.",
            false,
            () => Draft.IncludeChecklistWording,
            value => Draft = Draft with { IncludeChecklistWording = value }),
        new SettingSwitch(
            "files",
            "Include file list and hashes",
            "The PDF lists every reviewed file with its hash.",
            "Without the file list the PDF cannot show which files were reviewed; only the content fingerprint "
            + "identifies them. This may not satisfy your regulatory regime. Confirm with your quality or "
            + "compliance lead.",
            false,
            () => Draft.IncludeFiles,
            value => Draft = Draft with { IncludeFiles = value }),
        new SettingSwitch(
            "git-context",
            "Include git context",
            "The PDF names the repository and the baseline and head revisions the review compared.",
            "Without the git context the PDF does not say which repository and revisions were reviewed. "
            + "This may not satisfy your regulatory regime. Confirm with your quality or compliance lead.",
            false,
            () => Draft.IncludeGitContext,
            value => Draft = Draft with { IncludeGitContext = value }),
    ];

    /// <summary>
    ///     Loads the project and its settings whenever the project identity changes; only whoever administers the
    ///     project gets them.
    /// </summary>
    /// <returns>A task that completes when the lookup is done.</returns>
    protected override async Task OnParametersSetAsync()
    {
        // The router reuses this instance when only the project id changes; drop what belonged to the previous one
        Loaded = false;
        ErrorMessage = null;
        StatusMessage = null;
        Project = null;
        Saved = EvidenceSettings.Default;
        Draft = Saved;

        // The shared guard is the one access check of every handler; here it also decides whether to load at all
        if (await Access.GuardAdministerAsync(AuthenticationState, ProjectId, ShowNoAccess))
        {
            await LoadAsync();
        }

        Loaded = true;
    }

    /// <summary>
    ///     Marks the page interactive after it has rendered on a live circuit.
    /// </summary>
    /// <param name="firstRender"><c>true</c> for the first render of this component instance.</param>
    protected override void OnAfterRender(bool firstRender)
    {
        // Only the first render transitions the marker; later renders must not loop
        if (firstRender)
        {
            _interactive = true;
            StateHasChanged();
        }
    }

    /// <summary>
    ///     Reads the checked state out of a change event of a switch.
    /// </summary>
    /// <param name="e">The change event. Must not be null.</param>
    /// <returns><c>true</c> when the switch is now on.</returns>
    private static bool Checked(ChangeEventArgs e) => e.Value is true;

    /// <summary>
    ///     Applies a change of a switch to the draft, after re-checking that the user still administers the project.
    /// </summary>
    /// <param name="item">The switch that changed. Must not be null.</param>
    /// <param name="e">The change event. Must not be null.</param>
    /// <returns>A task that completes when the change has been handled.</returns>
    /// <remarks>
    ///     When the right was lost while the page was open the page drops the settings and the draft is not touched,
    ///     so no setting or warning about it stays visible.
    /// </remarks>
    private async Task ChangeAsync(SettingSwitch item, ChangeEventArgs e)
    {
        // An open page re-checks its user only every revalidation interval, so ask who the user is now
        if (!await GuardAsync())
        {
            return;
        }

        item.Set(Checked(e));
    }

    /// <summary>
    ///     Saves the draft, based on the settings the page loaded.
    /// </summary>
    /// <returns>A task that completes when the save has been handled.</returns>
    /// <remarks>
    ///     Nothing is changed when the right was lost while the page was open. When someone else changed the
    ///     settings first, the page shows theirs and says so, and nothing is overwritten.
    /// </remarks>
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;

        // An open page re-checks its user only every revalidation interval, so ask who the user is now; when the
        // right is gone the page drops the settings and nothing is saved
        if (!await GuardAsync())
        {
            return;
        }

        // The sign-in state can only end between the two reads; it counts as the right being gone
        Actor actor;
        try
        {
            actor = await AuthenticationState.GetActorAsync();
        }
        catch (DomainRuleException)
        {
            ShowNoAccess();
            return;
        }

        try
        {
            var changed = await Settings.SetAsync(actor, ProjectId, Saved, Draft);
            Saved = Draft;
            StatusMessage = changed
                ? "Evidence settings saved."
                : "Nothing to save: the settings are unchanged.";
        }
        catch (DomainRuleException ex)
        {
            // The service also refuses when the right was lost during the call; ask again so that the page does
            // not stay next to an error in that case
            if (!await GuardAsync())
            {
                return;
            }

            ErrorMessage = ex.Message;
            if (ex.Rule == DomainRule.RecordChanged)
            {
                // Someone else saved first: show what is stored now instead of the stale draft
                await LoadAsync();
            }
        }
    }

    /// <summary>
    ///     Reads the project and its stored settings and shows them in the switches.
    /// </summary>
    /// <returns>A task that completes when the settings are read or the access is found to be gone.</returns>
    private async Task LoadAsync()
    {
        try
        {
            var actor = await AuthenticationState.GetActorOrNullAsync();
            Project = await ProjectService.GetAsync(actor, ProjectId);
            if (Project is not null)
            {
                Saved = await Settings.GetAsync(actor, ProjectId);
                Draft = Saved;
            }
        }
        catch (DomainRuleException)
        {
            // The right went between the guard and the read; show the same state as for a missing project
            ShowNoAccess();
        }
    }

    /// <summary>
    ///     Re-checks that the signed-in user may still administer the project, at the start of a handler.
    /// </summary>
    /// <returns>
    ///     <c>true</c> when the handler may go on; <c>false</c> when the right is gone, in which case the page has
    ///     already dropped the settings and shows the not-found state, and the handler must stop.
    /// </returns>
    private Task<bool> GuardAsync()
    {
        return Access.GuardAdministerAsync(AuthenticationState, ProjectId, ShowNoAccess);
    }

    /// <summary>
    ///     Drops the project, the settings and every message and shows the same "does not exist" state as for a
    ///     non-member.
    /// </summary>
    /// <remarks>
    ///     The page changes the settings of a project the user may no longer administer, so it must not stay
    ///     rendered or submittable.
    /// </remarks>
    private void ShowNoAccess()
    {
        Project = null;
        Saved = EvidenceSettings.Default;
        Draft = Saved;
        ErrorMessage = null;
        StatusMessage = null;
        Loaded = true;
    }

    /// <summary>
    ///     One switch of the page: what it is called, what it says, and how it reads and writes the draft.
    /// </summary>
    /// <param name="TestId">The stable key used in the <c>data-testid</c> values of the switch.</param>
    /// <param name="Title">The label of the switch.</param>
    /// <param name="Description">One or two sentences saying what the switch does.</param>
    /// <param name="Warning">The regulatory warning shown while the switch is off; <c>null</c> for none.</param>
    /// <param name="Anonymous">Whether the switch opens anonymous access (shown in the first group).</param>
    /// <param name="Get">Reads the switch from the draft.</param>
    /// <param name="Set">Writes the switch into the draft.</param>
    private sealed record SettingSwitch(
        string TestId,
        string Title,
        string Description,
        string? Warning,
        bool Anonymous,
        Func<bool> Get,
        Action<bool> Set);
}
