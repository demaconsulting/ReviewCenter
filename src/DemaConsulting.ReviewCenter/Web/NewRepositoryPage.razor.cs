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
///     Code-behind for the add repository page (<c>NewRepositoryPage.razor</c>): registers a git repository.
/// </summary>
/// <remarks>
///     An attribute on the page requires a signed-in user and the page shows the form only to whoever manages the
///     project; the submission also re-checks that (defense in depth; <see cref="RepositoryService"/> checks again). The
///     token is write-only and is emptied as soon as it has been used. A rule violation is shown next to the form
///     instead of being thrown. Each browser connection (circuit) gets its own page instance, so the state here is
///     per-user and needs no synchronization.
/// </remarks>
public partial class NewRepositoryPage : ComponentBase
{
    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     Gets or sets the identity of the project the repository is added to, from the page address.
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
    ///     Gets or sets the navigation manager, supplied by dependency injection; used to return to the repositories.
    /// </summary>
    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    /// <summary>
    ///     Gets the project the repository is added to, or <c>null</c> when it does not exist or the user may not
    ///     manage it.
    /// </summary>
    private Project? Project { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the project lookup has finished.
    /// </summary>
    private bool Loaded { get; set; }

    /// <summary>
    ///     Gets or sets the name typed into the form.
    /// </summary>
    private string NewName { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the location typed into the form.
    /// </summary>
    private string NewLocation { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the user name typed into the form.
    /// </summary>
    private string NewUsername { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the token typed into the form; emptied after every submission.
    /// </summary>
    private string NewToken { get; set; } = string.Empty;

    /// <summary>
    ///     Gets the message of the last rejected submission, or <c>null</c> when there is none.
    /// </summary>
    private string? ErrorMessage { get; set; }

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Looks up the project whenever the project identity changes; only whoever manages it gets the form.
    /// </summary>
    /// <returns>A task that completes when the lookup is done.</returns>
    protected override async Task OnParametersSetAsync()
    {
        Loaded = false;
        var actor = await AuthenticationState.GetActorOrNullAsync();
        Project = actor is not null && await Access.CanAdministerAsync(actor, ProjectId)
            ? await ProjectService.GetAsync(actor, ProjectId)
            : null;
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
    ///     Registers the repository from the form values and returns to the registry.
    /// </summary>
    /// <returns>A task that completes when the submission has been handled.</returns>
    /// <remarks>
    ///     On a rule violation the form keeps what was typed, except the token, so that the operator can
    ///     correct it.
    /// </remarks>
    private async Task CreateRepositoryAsync()
    {
        ErrorMessage = null;
        var token = NewToken;
        NewToken = string.Empty;

        // Defense in depth: an open page re-checks its user only every revalidation interval, so ask who the user
        // is now; when the right is gone the page drops the form and nothing is registered
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
            await RepositoryService.CreateAsync(ProjectId, NewName, NewLocation, NewUsername, token, actor);
        }
        catch (DomainRuleException ex)
        {
            // The service also refuses when the right was lost during the call; ask again so that the form does
            // not stay next to an error in that case
            if (await GuardAsync())
            {
                ErrorMessage = ex.Message;
            }

            return;
        }

        Navigation.NavigateTo($"/projects/{ProjectId}/repositories");
    }

    /// <summary>
    ///     Re-checks that the signed-in user may still administer the project, at the start of the submission.
    /// </summary>
    /// <returns>
    ///     <c>true</c> when the submission may go on; <c>false</c> when the right is gone, in which case the page
    ///     has already dropped the form and shows the not-found state, and the submission must stop.
    /// </returns>
    private Task<bool> GuardAsync()
    {
        return Access.GuardAdministerAsync(AuthenticationState, ProjectId, ShowNoAccess);
    }

    /// <summary>
    ///     Drops the project and everything typed and shows the same "does not exist" state as for a non-member.
    /// </summary>
    /// <remarks>
    ///     The form registers a repository for a project the user may no longer administer, so it must not stay
    ///     rendered or submittable.
    /// </remarks>
    private void ShowNoAccess()
    {
        Project = null;
        NewName = string.Empty;
        NewLocation = string.Empty;
        NewUsername = string.Empty;
        NewToken = string.Empty;
        ErrorMessage = null;
        Loaded = true;
    }
}
