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
///     Code-behind for the repository page of a project (<c>RepositoriesPage.razor</c>): lists the repositories of
///     the project, and edits them or disables and enables them. New ones are added on the separate new repository
///     page.
/// </summary>
/// <remarks>
///     The page is for whoever may administer the project (a system administrator or a project admin of it); anybody
///     else sees the not-found state, the same as for a project that does not exist. An open page re-checks its user
///     only every <see cref="AuthOptions.RevalidationSeconds"/>, so for the time before the next check every action
///     also re-checks that the signed-in user may still administer the project (defense in depth;
///     <see cref="RepositoryService"/> checks again). The page talks to the domain only through
///     <see cref="RepositoryService"/>, which owns the rules. The token is write-only: the page never receives a
///     stored token (the read model has no member for it), it shows only whether one is set, and the token fields are
///     emptied as soon as they have been used. A rule violation is shown next to the control that was used instead of
///     being thrown. Each browser connection (circuit) gets its own page instance, so the state here is per-user and
///     needs no synchronization.
/// </remarks>
public partial class RepositoriesPage : ComponentBase
{
    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     Gets or sets the identity of the project whose repositories are shown, from the page address.
    /// </summary>
    [Parameter]
    public int ProjectId { get; set; }

    /// <summary>
    ///     Gets or sets the service that manages the registry, supplied by dependency injection.
    /// </summary>
    [Inject]
    private RepositoryService RepositoryService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that reads projects, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectService ProjectService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the access service, supplied by dependency injection; decides who may manage the project.
    /// </summary>
    [Inject]
    private IProjectAccess Access { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets the project shown, or <c>null</c> when it does not exist or the user may not manage it.
    /// </summary>
    private Project? Project { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the page has finished loading.
    /// </summary>
    private bool Loaded { get; set; }

    /// <summary>
    ///     Gets the repositories shown in the list.
    /// </summary>
    private IReadOnlyList<SourceRepository> Repositories { get; set; } = [];

    /// <summary>
    ///     Gets the identity of the repository being edited, or <c>null</c> when no edit form is open.
    /// </summary>
    private int? EditingId { get; set; }

    /// <summary>
    ///     Gets or sets the name typed into the edit form.
    /// </summary>
    private string EditName { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the location typed into the edit form.
    /// </summary>
    private string EditLocation { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the user name typed into the edit form.
    /// </summary>
    private string EditUsername { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets what the edit form does with the stored token; keeping it is the default.
    /// </summary>
    private TokenAction EditTokenAction { get; set; } = TokenAction.Keep;

    /// <summary>
    ///     Gets or sets the new token typed into the edit form; emptied after every submission.
    /// </summary>
    private string EditToken { get; set; } = string.Empty;

    /// <summary>
    ///     Gets the message of the last rejected action, or <c>null</c> when there is none.
    /// </summary>
    private string? ErrorMessage { get; set; }

    /// <summary>
    ///     Gets the message about the last stored change, or <c>null</c> when there is none.
    /// </summary>
    private string? StatusMessage { get; set; }

    /// <summary>
    ///     Gets the repository row the last message belongs to, or <c>null</c> when it belongs to the add form.
    /// </summary>
    private int? MessageRepositoryId { get; set; }

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Loads the project and its repositories whenever the project identity changes.
    /// </summary>
    /// <returns>A task that completes when the list is loaded.</returns>
    protected override async Task OnParametersSetAsync()
    {
        // The page is for whoever may manage the project; anybody else gets the not-found state, the same as for
        // a project that does not exist
        Loaded = false;
        EditingId = null;
        ErrorMessage = null;
        StatusMessage = null;
        Repositories = [];
        var actor = await AuthenticationState.GetActorOrNullAsync();
        Project = actor is not null && await Access.CanAdministerAsync(actor, ProjectId)
            ? await ProjectService.GetAsync(actor, ProjectId)
            : null;
        if (actor is not null && Project is not null)
        {
            try
            {
                Repositories = await RepositoryService.ListAsync(actor, ProjectId);
            }
            catch (DomainRuleException ex)
            {
                ErrorMessage = ex.Message;
            }
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
    ///     Opens the edit form for a repository, with the stored values and the token kept.
    /// </summary>
    /// <param name="repository">The repository as shown.</param>
    private void StartEdit(SourceRepository repository)
    {
        EditingId = repository.Id;
        EditName = repository.Name;
        EditLocation = repository.Location;
        EditUsername = repository.Username ?? string.Empty;
        EditTokenAction = TokenAction.Keep;
        EditToken = string.Empty;
        MessageRepositoryId = repository.Id;
        ErrorMessage = null;
        StatusMessage = null;
    }

    /// <summary>
    ///     Closes the edit form without saving.
    /// </summary>
    private void CancelEdit()
    {
        EditingId = null;
        EditToken = string.Empty;
    }

    /// <summary>
    ///     Saves the edit form and refreshes the list.
    /// </summary>
    /// <returns>A task that completes when the change is stored or the problem is shown.</returns>
    private async Task SaveEditAsync()
    {
        if (EditingId is not { } id)
        {
            return;
        }

        var token = EditTokenAction == TokenAction.Replace ? TokenChange.Replace(EditToken) : new TokenChange(EditTokenAction, null);
        EditToken = string.Empty;
        await RunAsync(id, $"Saved repository '{EditName.Trim()}'.", async actor =>
        {
            await RepositoryService.UpdateAsync(id, EditName, EditLocation, EditUsername, token, actor);
            EditingId = null;
        });
    }

    /// <summary>
    ///     Disables an enabled repository or enables a disabled one.
    /// </summary>
    /// <param name="repository">The repository as shown.</param>
    /// <returns>A task that completes when the change is stored or the problem is shown.</returns>
    private Task ToggleEnabledAsync(SourceRepository repository)
    {
        return RunAsync(repository.Id, $"{(repository.Enabled ? "Disabled" : "Enabled")} '{repository.Name}'.",
            actor => RepositoryService.SetEnabledAsync(repository.Id, !repository.Enabled, actor));
    }

    /// <summary>
    ///     Runs a change, shows its outcome next to the control that was used, and reloads the list.
    /// </summary>
    /// <param name="repositoryId">The repository row the change was started from, or <c>null</c> for the add form.</param>
    /// <param name="success">The message to show when the change is stored.</param>
    /// <param name="action">The change to run; it receives the verified administrator as the actor to audit.</param>
    /// <returns>A task that completes when the change is stored or the problem is shown.</returns>
    /// <remarks>
    ///     The change is refused, without reloading, unless the signed-in user is still an enabled
    ///     administrator in the user store, and also when the sign-in state has already ended (the message then
    ///     says so).
    /// </remarks>
    private async Task RunAsync(int? repositoryId, string success, Func<Actor, Task> action)
    {
        // The outcome is shown next to the control that was used, where the operator is looking
        MessageRepositoryId = repositoryId;
        ErrorMessage = null;
        StatusMessage = null;

        // Defense in depth: an open page re-checks its user only every revalidation interval, so ask the store
        // who the user is now. An ended sign-in state is shown as a message, not raised as a fault
        Actor actor;
        try
        {
            actor = await AuthenticationState.GetActorAsync();
        }
        catch (DomainRuleException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        if (!await Access.CanAdministerAsync(actor, ProjectId))
        {
            ErrorMessage = "Project administrator rights are required.";
            return;
        }

        try
        {
            await action(actor);
            StatusMessage = success;
        }
        catch (DomainRuleException ex)
        {
            ErrorMessage = ex.Message;
        }

        // Reload so the rows show what is stored, whether or not the change succeeded
        try
        {
            Repositories = await RepositoryService.ListAsync(actor, ProjectId);
        }
        catch (DomainRuleException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
