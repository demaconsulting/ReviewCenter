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
///     Code-behind for the project page (<c>ProjectPage.razor</c>): shows one project and lists its reviews with
///     their approval state. Reviews are created on the separate new review page. A system administrator also
///     manages the members of the project here.
/// </summary>
/// <remarks>
///     The page talks to the domain only through <see cref="ProjectService"/>, <see cref="ReviewService"/>,
///     <see cref="SigningService"/>, <see cref="RepositoryService"/> and <see cref="UserService"/> (to offer the
///     users that can be added). A project the user may not see is shown exactly like a project that does not exist.
///     Each browser connection (circuit) gets its own page instance, so the state here is per-user and needs no
///     synchronization. The page requires a signed-in user.
/// </remarks>
public partial class ProjectPage : ComponentBase
{
    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     Gets or sets the identity of the project to show, from the page address.
    /// </summary>
    [Parameter]
    public int ProjectId { get; set; }

    /// <summary>
    ///     Gets or sets the service that reads projects, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectService ProjectService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that lists reviews, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ReviewService ReviewService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that computes the approval status of a review, supplied by dependency injection.
    /// </summary>
    [Inject]
    private SigningService SigningService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the repository registry, supplied by dependency injection; used only to know whether any
    ///     enabled repository exists, which decides whether reviews can be planned.
    /// </summary>
    [Inject]
    private RepositoryService RepositoryService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the user service, supplied by dependency injection; lists the users a system administrator
    ///     can add as members.
    /// </summary>
    [Inject]
    private UserService UserService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the access service, supplied by dependency injection; decides what the user may do here.
    /// </summary>
    [Inject]
    private IProjectAccess Access { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets a value indicating whether the project lookup has finished.
    /// </summary>
    private bool Loaded { get; set; }

    /// <summary>
    ///     Gets the project shown, or <c>null</c> when it does not exist or is not visible to the user.
    /// </summary>
    private Project? Project { get; set; }

    /// <summary>
    ///     Gets the reviews of the project.
    /// </summary>
    private IReadOnlyList<Review> Reviews { get; set; } = [];

    /// <summary>
    ///     Gets the identifiers of the reviews that are approved (every slot has a current signature).
    /// </summary>
    private HashSet<string> ApprovedReviews { get; } = [];

    /// <summary>
    ///     Gets the enabled repositories (name only; never location or credentials).
    /// </summary>
    private IReadOnlyList<RepositorySummary> Repositories { get; set; } = [];

    /// <summary>
    ///     Gets a value indicating whether the user is a system administrator.
    /// </summary>
    private bool IsSystemAdmin { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the user may manage this project (system administrator or project admin).
    /// </summary>
    private bool CanAdminister { get; set; }

    /// <summary>
    ///     Gets the members of the project; only loaded for a system administrator.
    /// </summary>
    private IReadOnlyList<ProjectMember> Members { get; set; } = [];

    /// <summary>
    ///     Gets the enabled users who are not members yet, for the add-member form.
    /// </summary>
    private IReadOnlyList<User> Candidates { get; set; } = [];

    /// <summary>
    ///     Gets a value indicating whether the add-member form is shown.
    /// </summary>
    private bool ShowAddForm { get; set; }

    /// <summary>
    ///     Gets or sets the user name chosen in the add-member form.
    /// </summary>
    private string AddUsername { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets a value indicating whether the member to add becomes a project admin.
    /// </summary>
    private bool AddAsProjectAdmin { get; set; }

    /// <summary>
    ///     Gets the message of the last rejected member change, or <c>null</c> when there is none.
    /// </summary>
    private string? MemberError { get; set; }

    /// <summary>
    ///     Gets the message about the last stored member change, or <c>null</c> when there is none.
    /// </summary>
    private string? MemberStatus { get; set; }

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Loads the project and its reviews whenever the project identity changes.
    /// </summary>
    /// <returns>A task that completes when the data is loaded.</returns>
    protected override async Task OnParametersSetAsync()
    {
        // The router reuses this instance when only the project id changes; drop the previous project's data
        Loaded = false;
        ShowAddForm = false;
        MemberError = null;
        MemberStatus = null;
        var actor = await AuthenticationState.GetActorOrNullAsync();
        Project = await ProjectService.GetAsync(actor, ProjectId);
        if (actor is null || Project is null)
        {
            ShowNoAccess();
            return;
        }

        Reviews = await ReviewService.ListAsync(ProjectId);
        Repositories = await RepositoryService.ListEnabledAsync(actor, ProjectId);
        IsSystemAdmin = await Access.IsSystemAdminAsync(actor);
        CanAdminister = await Access.CanAdministerAsync(actor, ProjectId);
        await LoadMembersAsync(actor);

        // One approval lookup per review; the list of reviews of a project is short
        ApprovedReviews.Clear();
        foreach (var id in Reviews.Select(review => review.Id))
        {
            if ((await SigningService.GetApprovalAsync(ProjectId, id))?.IsApproved == true)
            {
                ApprovedReviews.Add(id);
            }
        }

        Loaded = true;
    }

    /// <summary>
    ///     Tells whether a review is approved.
    /// </summary>
    /// <param name="reviewId">The review identifier.</param>
    /// <returns><c>true</c> when the review has at least one slot and every slot has a current signature.</returns>
    private bool IsApproved(string reviewId)
    {
        return ApprovedReviews.Contains(reviewId);
    }

    /// <summary>
    ///     Re-checks, at the start of a member action on the open page, that the signed-in user may still view the
    ///     project and is still a system administrator.
    /// </summary>
    /// <returns>
    ///     <c>true</c> when the action may go on. <c>false</c> when the view is gone (the page has dropped
    ///     everything and shows the not-found state) or only the system administrator right is gone (the page has
    ///     dropped the members section, and the project stays visible); the action must stop in both cases.
    /// </returns>
    /// <remarks>
    ///     The members and the users that can be added are data only a system administrator may read, so they must
    ///     not stay rendered once the right is gone. A user who lost the right may still be a project admin, so the
    ///     link to the repositories is refreshed to what the user may do now.
    /// </remarks>
    private async Task<bool> GuardMembersAsync()
    {
        // The view comes first: without it the whole page goes, not only the members section
        if (!await Access.GuardViewAsync(AuthenticationState, ProjectId, ShowNoAccess))
        {
            return false;
        }

        if (await Access.GuardSystemAdminAsync(AuthenticationState, ShowMembersLost))
        {
            return true;
        }

        // Only the system administrator right is gone; show what the user may still do on this project
        CanAdminister = await Access.CanAdministerAsync(await AuthenticationState.GetActorOrNullAsync(), ProjectId);
        return false;
    }

    /// <summary>
    ///     Drops everything loaded and shows the same "does not exist" state as for a non-member.
    /// </summary>
    private void ShowNoAccess()
    {
        Project = null;
        Reviews = [];
        ApprovedReviews.Clear();
        Repositories = [];
        CanAdminister = false;
        ShowMembersLost();
        Loaded = true;
    }

    /// <summary>
    ///     Drops what only a system administrator may see: the members, the users that can be added and the forms.
    /// </summary>
    private void ShowMembersLost()
    {
        IsSystemAdmin = false;
        Members = [];
        Candidates = [];
        ShowAddForm = false;
        AddUsername = string.Empty;
        AddAsProjectAdmin = false;
        MemberError = null;
        MemberStatus = null;
    }

    /// <summary>
    ///     Shows the add-member form.
    /// </summary>
    /// <returns>A task that completes when the form is shown or the page has dropped what it may no longer show.</returns>
    private async Task OpenAddFormAsync()
    {
        if (!await GuardMembersAsync())
        {
            return;
        }

        MemberError = null;
        MemberStatus = null;
        AddUsername = Candidates.Count > 0 ? Candidates[0].Username : string.Empty;
        AddAsProjectAdmin = false;
        ShowAddForm = true;
    }

    /// <summary>
    ///     Hides the add-member form without adding anybody.
    /// </summary>
    private void CloseAddForm()
    {
        ShowAddForm = false;
    }

    /// <summary>
    ///     Adds the user chosen in the form as a member.
    /// </summary>
    /// <returns>A task that completes when the member is added or the problem is shown.</returns>
    private async Task AddMemberAsync()
    {
        var username = AddUsername;
        var role = AddAsProjectAdmin ? ProjectRole.ProjectAdmin : ProjectRole.User;
        if (await RunMemberChangeAsync(
                actor => ProjectService.AddMemberAsync(actor, ProjectId, username, role),
                $"Added '{username}' as {RoleText(role)}."))
        {
            ShowAddForm = false;
        }
    }

    /// <summary>
    ///     Removes a member from the project.
    /// </summary>
    /// <param name="member">The member as shown.</param>
    /// <returns>A task that completes when the member is removed or the problem is shown.</returns>
    private Task RemoveMemberAsync(ProjectMember member)
    {
        return RunMemberChangeAsync(
            actor => ProjectService.RemoveMemberAsync(actor, ProjectId, member.Username),
            $"Removed '{member.Username}' from the project.");
    }

    /// <summary>
    ///     Switches a member between project admin and plain member.
    /// </summary>
    /// <param name="member">The member as shown.</param>
    /// <returns>A task that completes when the role is changed or the problem is shown.</returns>
    private Task ToggleRoleAsync(ProjectMember member)
    {
        var role = member.Role == ProjectRole.ProjectAdmin ? ProjectRole.User : ProjectRole.ProjectAdmin;
        return RunMemberChangeAsync(
            actor => ProjectService.SetMemberRoleAsync(actor, ProjectId, member.Username, role),
            $"'{member.Username}' is now {RoleText(role)}.");
    }

    /// <summary>
    ///     Gets the words used for a project role.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns>The text shown to the user.</returns>
    private static string RoleText(ProjectRole role)
    {
        return role == ProjectRole.ProjectAdmin ? "project admin" : "member";
    }

    /// <summary>
    ///     Runs a member change, shows its outcome, and reloads the member list.
    /// </summary>
    /// <param name="change">The change; it receives the signed-in user as the actor to audit.</param>
    /// <param name="success">The message to show when the change is stored.</param>
    /// <returns><c>true</c> when the change was stored.</returns>
    /// <remarks>
    ///     The change runs only while the user may still view the project and is still a system administrator; when
    ///     either is gone the page drops what the user may no longer see instead of showing an error next to it.
    ///     The status is shown only after the lists have been reloaded, so it never stands next to a stale list.
    /// </remarks>
    private async Task<bool> RunMemberChangeAsync(Func<Actor, Task> change, string success)
    {
        MemberError = null;
        MemberStatus = null;
        if (!await GuardMembersAsync())
        {
            return false;
        }

        try
        {
            var actor = await AuthenticationState.GetActorAsync();
            await change(actor);
            await LoadMembersAsync(actor);
            MemberStatus = success;
            return true;
        }
        catch (DomainRuleException ex)
        {
            // A refusal can also mean the right was lost during the call (or the sign-in ended); ask again so that
            // the error is shown only when the user may still see the section
            if (await GuardMembersAsync())
            {
                MemberError = ex.Message;
            }

            return false;
        }
    }

    /// <summary>
    ///     Loads the member list and the users that can still be added; only a system administrator manages members.
    /// </summary>
    /// <param name="actor">The signed-in user.</param>
    /// <returns>A task that completes when the lists are loaded.</returns>
    private async Task LoadMembersAsync(Actor actor)
    {
        if (!IsSystemAdmin)
        {
            Members = [];
            Candidates = [];
            return;
        }

        Members = await ProjectService.ListMembersAsync(actor, ProjectId);
        var memberNames = Members.Select(member => member.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Candidates = (await UserService.ListAsync())
            .Where(user => user.Enabled && !memberNames.Contains(user.Username))
            .ToList();
    }

    /// <summary>
    ///     Marks the page interactive after it has rendered on a live circuit.
    /// </summary>
    /// <param name="firstRender"><c>true</c> for the first render of this component instance.</param>
    /// <remarks>
    ///     <c>OnAfterRender</c> is never invoked during static prerendering, so reaching it proves that
    ///     the circuit is connected. A second render makes the new marker value reach the browser.
    /// </remarks>
    protected override void OnAfterRender(bool firstRender)
    {
        // Only the first render transitions the marker; later renders must not loop
        if (firstRender)
        {
            _interactive = true;
            StateHasChanged();
        }
    }
}
