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

using System.Text;
using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the checklist template page (<c>ProjectChecklistPage.razor</c>): shows the checklist a
///     project gives to its new reviews and lets an administrator export it, import another one, validate and
///     preview it, replace the current one, or go back to the built-in default.
/// </summary>
/// <remarks>
///     <para>
///         An attribute on the page requires a signed-in user and the page shows the checklist only to whoever may
///         administer the project (a system administrator or a project admin of it). Every handler re-checks that
///         through the shared guard before it reads or changes anything, and drops everything the page holds when the
///         right is gone; <see cref="ProjectChecklistService"/> checks again and records the change in the audit trail.
///     </para>
///     <para>
///         The definition is imported as text, either pasted (a limited number of characters, because the text travels
///         over the live connection of the page) or uploaded as a file of up to
///         <see cref="ReviewLimits.MaxChecklistBytes"/> bytes. Validation shows every error with its path and never
///         changes anything. A replace carries the hash of the checklist the page loaded, so a change made by someone
///         else in the meantime is refused and the current checklist is shown instead of being overwritten. A change
///         applies to new reviews only; each review keeps the copy it was created with. Each browser connection
///         (circuit) gets its own page instance, so the state here is per-user and needs no synchronization.
///     </para>
/// </remarks>
public partial class ProjectChecklistPage : ComponentBase
{
    /// <summary>
    ///     The number of characters the paste box accepts. The text of the box travels to the server in one message
    ///     of the live connection, which the framework limits to 32 KiB, so larger definitions are uploaded as a file.
    /// </summary>
    internal const int PasteLimit = 20000;

    /// <summary>
    ///     The media type of an exported definition.
    /// </summary>
    private const string JsonContentType = "application/json";

    /// <summary>
    ///     The characters of a project's version label that are not allowed in the name of a downloaded file.
    /// </summary>
    /// <returns>The regular expression.</returns>
    [GeneratedRegex(@"[^A-Za-z0-9._-]+")]
    private static partial Regex UnsafeFileNameCharacters();

    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     The hash of the stored definition when it cannot be read, so that it can still be replaced or reset.
    /// </summary>
    private string? _unreadableHash;

    /// <summary>
    ///     Gets or sets the identity of the project whose checklist is shown, from the page address.
    /// </summary>
    [Parameter]
    public int ProjectId { get; set; }

    /// <summary>
    ///     Gets or sets the service that reads and changes the checklist of a project, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectChecklistService Checklists { get; set; } = null!;

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
    ///     Gets or sets the JavaScript runtime that starts the download of an exported file, supplied by dependency
    ///     injection.
    /// </summary>
    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    /// <summary>
    ///     Gets the project the checklist belongs to, or <c>null</c> when it does not exist or the user may not
    ///     administer it.
    /// </summary>
    private Project? Project { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the lookup has finished.
    /// </summary>
    private bool Loaded { get; set; }

    /// <summary>
    ///     Gets the checklist the project uses, as loaded or last changed; a change is based on its hash. It is
    ///     <c>null</c> when the stored definition cannot be read.
    /// </summary>
    private ProjectChecklist? Current { get; set; }

    /// <summary>
    ///     Gets the message about a stored definition that cannot be read, or <c>null</c> when it can be read.
    /// </summary>
    private string? UnreadableMessage { get; set; }

    /// <summary>
    ///     Gets the text shown in the paste box.
    /// </summary>
    private string PastedText { get; set; } = string.Empty;

    /// <summary>
    ///     Gets the text that was imported (pasted or uploaded) and is checked and replaced on request; <c>null</c>
    ///     when nothing was imported.
    /// </summary>
    private string? ImportText { get; set; }

    /// <summary>
    ///     Gets the name of the source of <see cref="ImportText"/>: the name of the uploaded file or "pasted text";
    ///     <c>null</c> when nothing was imported.
    /// </summary>
    private string? ImportSource { get; set; }

    /// <summary>
    ///     Gets the result of the last validation, or <c>null</c> when the imported text was not validated yet.
    /// </summary>
    private ChecklistValidationResult? Validation { get; set; }

    /// <summary>
    ///     Gets the differences between the validated definition and the current one, or <c>null</c> when there is
    ///     no valid definition (or the current one cannot be read).
    /// </summary>
    private ChecklistDiff? Diff { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the replace is waiting for the confirmation of the user.
    /// </summary>
    private bool ConfirmingReplace { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the reset is waiting for the confirmation of the user.
    /// </summary>
    private bool ConfirmingReset { get; set; }

    /// <summary>
    ///     Gets the message of the last rejected change, or <c>null</c> when there is none.
    /// </summary>
    private string? ErrorMessage { get; set; }

    /// <summary>
    ///     Gets the confirmation of the last change, or <c>null</c> when there is none.
    /// </summary>
    private string? StatusMessage { get; set; }

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Gets the hash a change is based on: the hash of the loaded checklist, or of the stored text that cannot be read.
    /// </summary>
    private string? CurrentHash => Current?.Hash ?? _unreadableHash;

    /// <summary>
    ///     Gets a value indicating whether the validated definition is valid and differs from the current one, so
    ///     that replacing makes sense.
    /// </summary>
    private bool CanReplace =>
        Validation is { IsValid: true, Definition: { } candidate }
        && CurrentHash is not null
        && (Current is null || !string.Equals(candidate.ToJson(), Current.Json, StringComparison.Ordinal));

    /// <summary>
    ///     Gets a value indicating whether the project has a checklist of its own that a reset would remove.
    /// </summary>
    private bool CanReset => CurrentHash is not null && Current is not { IsDefault: true };

    /// <summary>
    ///     Gets a value indicating whether the validated definition keeps the version label of the current one while
    ///     its content differs, which the service refuses.
    /// </summary>
    private bool KeepsVersionLabel => Diff is { VersionChanged: false, HasContentChanges: true };

    /// <summary>
    ///     Loads the project and its checklist whenever the project identity changes; only whoever administers the
    ///     project gets them.
    /// </summary>
    /// <returns>A task that completes when the lookup is done.</returns>
    protected override async Task OnParametersSetAsync()
    {
        // The router reuses this instance when only the project id changes; drop what belonged to the previous one
        Loaded = false;
        ClearData();

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
    ///     Takes the text of the paste box as the imported text once the box is left.
    /// </summary>
    /// <param name="e">The change event. Must not be null.</param>
    /// <returns>A task that completes when the change has been handled.</returns>
    private async Task PasteChangedAsync(ChangeEventArgs e)
    {
        if (!await GuardAsync())
        {
            return;
        }

        PastedText = e.Value as string ?? string.Empty;
        SetImport(PastedText.Length == 0 ? null : PastedText, "pasted text");
    }

    /// <summary>
    ///     Reads an uploaded file as the imported text.
    /// </summary>
    /// <param name="e">The change event with the file. Must not be null.</param>
    /// <returns>A task that completes when the file has been read.</returns>
    /// <remarks>
    ///     The file is limited to <see cref="ReviewLimits.MaxChecklistBytes"/> bytes while it is read, so a larger
    ///     file is refused without being kept. The text is not put into the paste box: it would travel back to the
    ///     server with the next change of the box.
    /// </remarks>
    private async Task FileSelectedAsync(InputFileChangeEventArgs e)
    {
        ErrorMessage = null;
        StatusMessage = null;
        if (!await GuardAsync())
        {
            return;
        }

        // A file above the limit is refused before it is opened; an earlier import is dropped so the page matches the message
        if (e.File.Size > ReviewLimits.MaxChecklistBytes)
        {
            ClearImport();
            ErrorMessage = $"The file is larger than the limit of {ReviewLimits.MaxChecklistBytes / 1024} KiB.";
            return;
        }

        try
        {
            // The explicit limit is the domain maximum (256 KiB); the analyzer cannot see through the constant
#pragma warning disable S5693 // Content length is limited to ReviewLimits.MaxChecklistBytes
            await using var stream = e.File.OpenReadStream(ReviewLimits.MaxChecklistBytes);
#pragma warning restore S5693
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            PastedText = string.Empty;

            // A file saved with a byte order mark would start with an invisible character that JSON does not allow
            SetImport(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).TrimStart('\uFEFF'), e.File.Name);
        }
        catch (IOException)
        {
            // The framework also refuses a file that grows above the limit while it is read, and a file that cannot be read
            ClearImport();
            ErrorMessage = $"The file could not be read, or it is larger than the limit of {ReviewLimits.MaxChecklistBytes / 1024} KiB.";
        }
    }

    /// <summary>
    ///     Checks the imported text and, when it is valid, compares it with the current checklist.
    /// </summary>
    /// <returns>A task that completes when the check has been handled.</returns>
    private async Task ValidateAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;
        if (!await GuardAsync())
        {
            return;
        }

        ConfirmingReplace = false;
        ValidateImport();
    }

    /// <summary>
    ///     Asks the user to confirm replacing the checklist.
    /// </summary>
    /// <returns>A task that completes when the request has been handled.</returns>
    private async Task AskReplaceAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;
        if (await GuardAsync() && CanReplace)
        {
            ConfirmingReplace = true;
            ConfirmingReset = false;
        }
    }

    /// <summary>
    ///     Asks the user to confirm resetting the checklist to the built-in default.
    /// </summary>
    /// <returns>A task that completes when the request has been handled.</returns>
    private async Task AskResetAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;
        if (await GuardAsync() && CanReset)
        {
            ConfirmingReset = true;
            ConfirmingReplace = false;
        }
    }

    /// <summary>
    ///     Withdraws a pending confirmation without changing anything.
    /// </summary>
    private void CancelConfirmation()
    {
        ConfirmingReplace = false;
        ConfirmingReset = false;
    }

    /// <summary>
    ///     Replaces the checklist of the project by the validated definition, based on the hash the page loaded.
    /// </summary>
    /// <returns>A task that completes when the replace has been handled.</returns>
    /// <remarks>
    ///     Nothing is changed when the right was lost while the page was open. When someone else changed the
    ///     checklist first, the page shows theirs and says so, and nothing is overwritten.
    /// </remarks>
    private async Task ReplaceAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;
        ConfirmingReplace = false;
        if (!await GuardAsync() || !CanReplace || ImportText is null || CurrentHash is null)
        {
            return;
        }

        var actor = await TryGetActorAsync();
        if (actor is null)
        {
            return;
        }

        try
        {
            var changed = await Checklists.SetAsync(actor, ProjectId, CurrentHash, ImportText);
            await LoadAsync();
            ClearImport();
            StatusMessage = changed
                ? $"Checklist replaced: new reviews now use {Describe(Current)}. Existing reviews keep the checklist they were created with."
                : "Nothing to replace: the checklist is unchanged.";
        }
        catch (DomainRuleException ex)
        {
            await HandleRefusalAsync(ex);
        }
    }

    /// <summary>
    ///     Resets the checklist of the project to the built-in default, based on the hash the page loaded.
    /// </summary>
    /// <returns>A task that completes when the reset has been handled.</returns>
    private async Task ResetAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;
        ConfirmingReset = false;
        if (!await GuardAsync() || CurrentHash is null)
        {
            return;
        }

        var actor = await TryGetActorAsync();
        if (actor is null)
        {
            return;
        }

        try
        {
            var changed = await Checklists.ResetAsync(actor, ProjectId, CurrentHash);
            await LoadAsync();
            if (Validation is not null)
            {
                ValidateImport();
            }

            StatusMessage = changed
                ? $"Checklist reset: new reviews now use the built-in default, {Describe(Current)}. Existing reviews keep the checklist they were created with."
                : "Nothing to reset: the project already uses the built-in default.";
        }
        catch (DomainRuleException ex)
        {
            await HandleRefusalAsync(ex);
        }
    }

    /// <summary>
    ///     Downloads the checklist the project uses now as a JSON file.
    /// </summary>
    /// <returns>A task that completes when the download has been started.</returns>
    private async Task ExportCurrentAsync()
    {
        if (await GuardAsync() && Current is not null)
        {
            await DownloadAsync(Current.Definition);
        }
    }

    /// <summary>
    ///     Downloads the built-in default checklist as a JSON file.
    /// </summary>
    /// <returns>A task that completes when the download has been started.</returns>
    private async Task ExportDefaultAsync()
    {
        if (await GuardAsync())
        {
            await DownloadAsync(DefaultChecklist.Current);
        }
    }

    /// <summary>
    ///     Hands a definition to the browser as a downloaded file named after its version label.
    /// </summary>
    /// <param name="definition">The definition to export. Must not be null.</param>
    /// <returns>A task that completes when the browser has been told.</returns>
    private async Task DownloadAsync(ChecklistDefinition definition)
    {
        var name = UnsafeFileNameCharacters().Replace(definition.Version, "-").Trim('-', '.');
        await JS.InvokeVoidAsync(
            "reviewCenter.downloadText",
            $"checklist-{(name.Length == 0 ? "definition" : name)}.json",
            ChecklistDiff.ToIndentedJson(definition),
            JsonContentType);
    }

    /// <summary>
    ///     Shows the reason of a refused change, re-checking first that the right is still there.
    /// </summary>
    /// <param name="ex">The refusal. Must not be null.</param>
    /// <returns>A task that completes when the refusal has been shown.</returns>
    private async Task HandleRefusalAsync(DomainRuleException ex)
    {
        // The service also refuses when the right was lost during the call; ask again so that the page does not
        // stay next to an error in that case
        if (!await GuardAsync())
        {
            return;
        }

        ErrorMessage = ex.Message;
        if (ex.Rule == DomainRule.RecordChanged)
        {
            // Someone else changed it first: show what is stored now instead of the stale one, and compare the
            // imported text with it again
            await LoadAsync();
            if (Validation is not null)
            {
                ValidateImport();
            }
        }
    }

    /// <summary>
    ///     Reads the signed-in user for a change.
    /// </summary>
    /// <returns>The user, or <c>null</c> when the sign-in ended (the page then shows the not-found state).</returns>
    private async Task<Actor?> TryGetActorAsync()
    {
        // The sign-in state can only end between the guard and this read; it counts as the right being gone
        try
        {
            return await AuthenticationState.GetActorAsync();
        }
        catch (DomainRuleException)
        {
            ShowNoAccess();
            return null;
        }
    }

    /// <summary>
    ///     Remembers a new imported text; whatever was validated before no longer applies to it.
    /// </summary>
    /// <param name="text">The text, or <c>null</c> for none.</param>
    /// <param name="source">The name of the source of the text.</param>
    private void SetImport(string? text, string source)
    {
        ImportText = text;
        ImportSource = text is null ? null : source;
        Validation = null;
        Diff = null;
        ConfirmingReplace = false;
    }

    /// <summary>
    ///     Checks the imported text and compares it with the current checklist.
    /// </summary>
    private void ValidateImport()
    {
        Validation = ChecklistValidator.Validate(ImportText);
        Diff = Validation.Definition is { } candidate && Current is not null
            ? ChecklistDiff.Compare(Current.Definition, candidate)
            : null;
    }

    /// <summary>
    ///     Reads the project and its checklist.
    /// </summary>
    /// <returns>A task that completes when the checklist is read or the access is found to be gone.</returns>
    private async Task LoadAsync()
    {
        try
        {
            var actor = await AuthenticationState.GetActorOrNullAsync();
            Project = await ProjectService.GetAsync(actor, ProjectId);
            Current = null;
            UnreadableMessage = null;
            _unreadableHash = null;
            if (Project is not null)
            {
                try
                {
                    Current = await Checklists.GetAsync(actor, ProjectId);
                }
                catch (DomainRuleException ex) when (ex.Rule == DomainRule.ChecklistDefinitionInvalid)
                {
                    // The stored text cannot be read; it can still be replaced or reset, based on its hash
                    UnreadableMessage = ex.Message;
                    _unreadableHash = ProjectChecklistService.HashOf(Project.ChecklistDefinition);
                }
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
    ///     already dropped the checklist and shows the not-found state, and the handler must stop.
    /// </returns>
    private Task<bool> GuardAsync()
    {
        return Access.GuardAdministerAsync(AuthenticationState, ProjectId, ShowNoAccess);
    }

    /// <summary>
    ///     Drops the project, the checklist, the imported text and every message and shows the same "does not
    ///     exist" state as for a non-member.
    /// </summary>
    /// <remarks>
    ///     The page changes the checklist of a project the user may no longer administer, so it must not stay
    ///     rendered or submittable.
    /// </remarks>
    private void ShowNoAccess()
    {
        ClearData();
        Loaded = true;
    }

    /// <summary>
    ///     Drops everything the page holds about a project.
    /// </summary>
    private void ClearData()
    {
        Project = null;
        Current = null;
        UnreadableMessage = null;
        _unreadableHash = null;
        ClearImport();
        ErrorMessage = null;
        StatusMessage = null;
    }

    /// <summary>
    ///     Drops the imported text, its validation and the pending confirmations.
    /// </summary>
    private void ClearImport()
    {
        PastedText = string.Empty;
        ImportText = null;
        ImportSource = null;
        Validation = null;
        Diff = null;
        ConfirmingReplace = false;
        ConfirmingReset = false;
    }

    /// <summary>
    ///     Describes a checklist in one phrase: its version label and its size.
    /// </summary>
    /// <param name="checklist">The checklist, or <c>null</c> when it cannot be read.</param>
    /// <returns>The phrase, such as <c>default-1 (14 sections, 58 checks)</c>.</returns>
    internal static string Describe(ProjectChecklist? checklist)
    {
        return checklist is null
            ? "a checklist that cannot be read"
            : $"{checklist.Version} ({Count(checklist.SectionCount, "section")}, {Count(checklist.ItemCount, "check")})";
    }

    /// <summary>
    ///     Writes a number with its noun in singular or plural.
    /// </summary>
    /// <param name="count">The number.</param>
    /// <param name="noun">The singular noun.</param>
    /// <returns>The number and the noun, such as <c>1 check</c> or <c>58 checks</c>.</returns>
    private static string Count(int count, string noun)
    {
        return count == 1 ? $"1 {noun}" : $"{count} {noun}s";
    }
}
