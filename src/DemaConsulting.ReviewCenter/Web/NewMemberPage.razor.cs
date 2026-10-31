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
///     Code-behind for the add member page (<c>NewMemberPage.razor</c>): adds an existing user to a project by the
///     exact user name.
/// </summary>
/// <remarks>
///     An attribute on the page requires a signed-in user and the page shows the form only to whoever may administer
///     the project; the submission re-checks that through the shared guard before it changes anything, and
///     <see cref="ProjectService"/> checks again. The page never lists users: the name must be typed, and a name that
///     cannot be added gets one generic message from the service, so the page cannot be used to find out which
///     accounts exist. A rule violation is shown next to the form instead of being thrown. Each browser connection
///     (circuit) gets its own page instance, so the state here is per-user and needs no synchronization.
/// </remarks>
public partial class NewMemberPage : ComponentBase
{
    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     Gets or sets the identity of the project the member is added to, from the page address.
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
    ///     Gets the project the member is added to, or <c>null</c> when it does not exist or the user may not
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
    ///     Gets or sets a value indicating whether the member to add becomes a project admin.
    /// </summary>
    private bool AsProjectAdmin { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the member to add becomes a read-only auditor.
    /// </summary>
    private bool AsAuditor { get; set; }

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
    ///     Looks up the project whenever the project identity changes; only whoever administers it gets the form.
    /// </summary>
    /// <returns>A task that completes when the lookup is done.</returns>
    protected override async Task OnParametersSetAsync()
    {
        // The router reuses this instance when only the project id changes; drop what belonged to the previous one
        Loaded = false;
        ErrorMessage = null;
        Username = string.Empty;
        AsProjectAdmin = false;
        AsAuditor = false;
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
    ///     Adds the user named in the form and returns to the project.
    /// </summary>
    /// <returns>A task that completes when the submission has been handled.</returns>
    /// <remarks>
    ///     On a refusal the form keeps what was typed so that it can be corrected. Nothing is added when the right
    ///     was lost while the page was open.
    /// </remarks>
    private async Task AddMemberAsync()
    {
        ErrorMessage = null;

        // An open page re-checks its user only every revalidation interval, so ask who the user is now; when the
        // right is gone the page drops the form and nothing is added
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
            if (AsProjectAdmin && AsAuditor)
            {
                ErrorMessage = "Choose either project admin or auditor, not both.";
                return;
            }

            var role = AsAuditor ? ProjectRole.Auditor : ProjectRole.User;
            if (AsProjectAdmin)
            {
                role = ProjectRole.ProjectAdmin;
            }
            await ProjectService.AddMemberAsync(actor, ProjectId, Username, role);
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

        Navigation.NavigateTo($"/projects/{ProjectId}");
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
    ///     The form adds a member to a project the user may no longer administer, so it must not stay rendered or
    ///     submittable.
    /// </remarks>
    private void ShowNoAccess()
    {
        Project = null;
        Username = string.Empty;
        AsProjectAdmin = false;
        AsAuditor = false;
        ErrorMessage = null;
        Loaded = true;
    }
}
