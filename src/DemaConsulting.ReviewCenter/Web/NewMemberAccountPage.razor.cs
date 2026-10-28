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
using Microsoft.JSInterop;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the create account page (<c>NewMemberAccountPage.razor</c>): creates a local account and adds
///     it to the project in one step, then shows the generated temporary password once.
/// </summary>
/// <remarks>
///     An attribute on the page requires a signed-in user and the page shows the form only to whoever may administer
///     the project; every handler re-checks that through the shared guard before it reads or changes anything, and
///     <see cref="ProjectService"/> checks again. The temporary password is held only in the state of this page
///     instance until "Done" is used or the page is left, and is dropped at once when the right is lost. It is never
///     written to a log or stored. A rule violation is shown next to the form instead of being thrown. Each browser
///     connection (circuit) gets its own page instance, so the state here is per-user and needs no synchronization.
/// </remarks>
public partial class NewMemberAccountPage : ComponentBase
{
    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     Gets or sets the identity of the project the account is added to, from the page address.
    /// </summary>
    [Parameter]
    public int ProjectId { get; set; }

    /// <summary>
    ///     Gets or sets the service that reads projects and changes their members, supplied by dependency injection.
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
    ///     Gets or sets the navigation manager, supplied by dependency injection; used to return to the project.
    /// </summary>
    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the browser script runtime used to copy the password, supplied by dependency injection.
    /// </summary>
    [Inject]
    private IJSRuntime Js { get; set; } = null!;

    /// <summary>
    ///     Gets the project the account is added to, or <c>null</c> when it does not exist or the user may not
    ///     administer it.
    /// </summary>
    private Project? Project { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the project lookup has finished.
    /// </summary>
    private bool Loaded { get; set; }

    /// <summary>
    ///     Gets or sets the user name typed into the form.
    /// </summary>
    private string Username { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the display name typed into the form.
    /// </summary>
    private string DisplayName { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets a value indicating whether the new member becomes a project admin.
    /// </summary>
    private bool AsProjectAdmin { get; set; }

    /// <summary>
    ///     Gets the message of the last rejected submission, or <c>null</c> when there is none.
    /// </summary>
    private string? ErrorMessage { get; set; }

    /// <summary>
    ///     Gets the account that was just created with its temporary password, or <c>null</c>; held only in the
    ///     memory of this page instance and gone once dismissed, when the right is lost, or when the page is left.
    /// </summary>
    private NewMemberAccount? Created { get; set; }

    /// <summary>
    ///     Gets the result of the last copy attempt, or <c>null</c>.
    /// </summary>
    private string? CopyStatus { get; set; }

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Looks up the project whenever the project identity changes; only whoever administers it gets the form.
    /// </summary>
    /// <returns>A task that completes when the lookup is done.</returns>
    protected override async Task OnParametersSetAsync()
    {
        // The router reuses this instance when only the project id changes; drop what belonged to the previous one
        Loaded = false;
        ShowNoAccess();
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
    ///     Creates the account from the form values and shows the temporary password.
    /// </summary>
    /// <returns>A task that completes when the submission has been handled.</returns>
    /// <remarks>
    ///     On a refusal the form keeps what was typed so that it can be corrected. Nothing is created when the right
    ///     was lost while the page was open.
    /// </remarks>
    private async Task CreateAccountAsync()
    {
        ErrorMessage = null;

        // An open page re-checks its user only every revalidation interval, so ask who the user is now; when the
        // right is gone the page drops the form and nothing is created
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
            var role = AsProjectAdmin ? ProjectRole.ProjectAdmin : ProjectRole.User;
            Created = await ProjectService.CreateMemberAccountAsync(actor, ProjectId, Username, DisplayName, role);
            CopyStatus = null;
        }
        catch (DomainRuleException ex)
        {
            // The service also refuses when the right was lost during the call; ask again so that the form does
            // not stay next to an error in that case
            if (await GuardAsync())
            {
                ErrorMessage = ex.Message;
            }
        }
    }

    /// <summary>
    ///     Copies the temporary password to the clipboard of the browser.
    /// </summary>
    /// <returns>A task that completes when the browser has answered.</returns>
    /// <remarks>
    ///     The password is data of the project, so the access is asked again first; when it is gone the password
    ///     is dropped and nothing is copied.
    /// </remarks>
    private async Task CopyPasswordAsync()
    {
        if (!await GuardAsync() || Created is null)
        {
            return;
        }

        try
        {
            var copied = await Js.InvokeAsync<bool>("reviewCenter.copyText", Created.TemporaryPassword);
            CopyStatus = copied ? "Copied." : "Could not copy; select the password and copy it by hand.";
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException)
        {
            CopyStatus = "Could not copy; select the password and copy it by hand.";
        }
    }

    /// <summary>
    ///     Hides the password for good and returns to the project.
    /// </summary>
    private void Done()
    {
        // Dropping data needs no access check; leaving the page ends the life of the password
        ShowNoAccess();
        Navigation.NavigateTo($"/projects/{ProjectId}");
    }

    /// <summary>
    ///     Re-checks that the signed-in user may still administer the project, at the start of a handler.
    /// </summary>
    /// <returns>
    ///     <c>true</c> when the handler may go on; <c>false</c> when the right is gone, in which case the page
    ///     has already dropped the form and the password and shows the not-found state, and the handler must stop.
    /// </returns>
    private Task<bool> GuardAsync()
    {
        return Access.GuardAdministerAsync(AuthenticationState, ProjectId, ShowNoAccess);
    }

    /// <summary>
    ///     Drops the project, everything typed and the temporary password, and shows the same "does not exist"
    ///     state as for a non-member.
    /// </summary>
    /// <remarks>
    ///     The form creates an account for a project the user may no longer administer, and the password is data of
    ///     that project, so neither may stay rendered or submittable.
    /// </remarks>
    private void ShowNoAccess()
    {
        Project = null;
        Username = string.Empty;
        DisplayName = string.Empty;
        AsProjectAdmin = false;
        ErrorMessage = null;
        Created = null;
        CopyStatus = null;
        Loaded = true;
    }
}
