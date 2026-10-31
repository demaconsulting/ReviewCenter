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
///     their approval state. Reviews are created on the separate new review page. An administrator of the project (a
///     project admin or a system administrator) also manages its members here and opens its audit trail.
/// </summary>
/// <remarks>
///     The page talks to the domain only through <see cref="ProjectService"/>, <see cref="ReviewService"/>,
///     <see cref="SigningService"/> and <see cref="RepositoryService"/>. A project the user may not see is shown
///     exactly like a project that does not exist. Adding a member needs typing an exact user name and happens on
///     its own page, so no list of users is ever loaded here. Every handler asks the shared guard again before it
///     reads or changes anything, so a user who lost the right sees nothing stale. Each browser connection
///     (circuit) gets its own page instance, so the state here is per-user and needs no synchronization. The page
///     requires a signed-in user.
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
    ///     Gets or sets the service that reads the checklist of a project, supplied by dependency injection; used
    ///     only for the one-line summary of an administrator.
    /// </summary>
    [Inject]
    private ProjectChecklistService Checklists { get; set; } = null!;

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
    ///     Gets a value indicating whether the user may manage this project (system administrator or project admin).
    /// </summary>
    private bool CanAdminister { get; set; }

    /// <summary>
    ///     Gets the members of the project; only loaded for someone who may administer the project.
    /// </summary>
    private IReadOnlyList<ProjectMember> Members { get; set; } = [];

    /// <summary>
    ///     Gets the message of the last rejected member change, or <c>null</c> when there is none.
    /// </summary>
    private string? MemberError { get; set; }

    /// <summary>
    ///     Gets the message about the last stored member change, or <c>null</c> when there is none.
    /// </summary>
    private string? MemberStatus { get; set; }

    /// <summary>
    ///     Gets the value of the independent reviewers switch as the user set it (not yet saved).
    /// </summary>
    private bool RequireIndependentChoice { get; set; } = true;

    /// <summary>
    ///     Gets the message of the last rejected review rule change, or <c>null</c> when there is none.
    /// </summary>
    private string? RulesError { get; set; }

    /// <summary>
    ///     Gets the message about the last stored review rule change, or <c>null</c> when there is none.
    /// </summary>
    private string? RulesStatus { get; set; }

    /// <summary>
    ///     Gets the one-line summary of the project's checklist for the review rules card; <c>null</c> for someone
    ///     who may not administer the project.
    /// </summary>
    private string? ChecklistSummary { get; set; }

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
        MemberError = null;
        MemberStatus = null;
        RulesError = null;
        RulesStatus = null;
        var actor = await AuthenticationState.GetActorOrNullAsync();
        Project = await ProjectService.GetAsync(actor, ProjectId);
        if (actor is null || Project is null)
        {
            ShowNoAccess();
            return;
        }

        RequireIndependentChoice = Project.RequireIndependentReviewers;
        Reviews = await ReviewService.ListAsync(ProjectId);
        Repositories = await RepositoryService.ListEnabledAsync(actor, ProjectId);
        CanAdminister = await Access.CanAdministerAsync(actor, ProjectId);
        ChecklistSummary = CanAdminister ? await DescribeChecklistAsync(actor) : null;
        await LoadMembersAsync(actor);

        // One approval lookup per review; the list of reviews of a project is short
        ApprovedReviews.Clear();
        foreach (var id in Reviews.Select(review => review.Id))
        {
            var approval = await SigningService.GetApprovalAsync(ProjectId, id);
            if (approval?.IsApproved == true)
            {
                ApprovedReviews.Add(id);
            }
        }

        Loaded = true;
    }

    /// <summary>
    ///     Describes the checklist of the project in one line, for the review rules card.
    /// </summary>
    /// <param name="actor">The signed-in user, who may administer the project.</param>
    /// <returns>
    ///     The version label and size, and whether it is the built-in default; a sentence when the stored definition
    ///     cannot be read.
    /// </returns>
    private async Task<string> DescribeChecklistAsync(Actor actor)
    {
        try
        {
            var checklist = await Checklists.GetAsync(actor, ProjectId);
            return ProjectChecklistPage.Describe(checklist) + (checklist.IsDefault ? ", built-in default" : ", custom");
        }
        catch (DomainRuleException)
        {
            // An unreadable stored definition (or a right lost just now) must not break the page
            return "cannot be read";
        }
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
    ///     Re-checks, at the start of a member action on the open page, that the signed-in user may still view and
    ///     administer the project.
    /// </summary>
    /// <returns>
    ///     <c>true</c> when the action may go on. <c>false</c> when the view is gone (the page has dropped
    ///     everything and shows the not-found state) or only the administer right is gone (the page has dropped the
    ///     members section, and the project stays visible); the action must stop in both cases.
    /// </returns>
    /// <remarks>
    ///     The members are data only an administrator of the project may read, so they must not stay rendered once
    ///     the right is gone. The check is the shared guard, so it gives the answer a user without the right gets.
    /// </remarks>
    private async Task<bool> GuardMembersAsync()
    {
        // The view comes first: without it the whole page goes, not only the members section
        if (!await Access.GuardViewAsync(AuthenticationState, ProjectId, ShowNoAccess))
        {
            return false;
        }

        return await Access.GuardAdministerAsync(AuthenticationState, ProjectId, ShowMembersLost);
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
        ShowMembersLost();
        Loaded = true;
    }

    /// <summary>
    ///     Drops what only an administrator of the project may see: the members and the administration buttons.
    /// </summary>
    private void ShowMembersLost()
    {
        CanAdminister = false;
        ChecklistSummary = null;
        Members = [];
        MemberError = null;
        MemberStatus = null;
        RulesError = null;
        RulesStatus = null;
    }

    /// <summary>
    ///     Saves the review rules (today: whether independent reviewers are required).
    /// </summary>
    /// <returns>A task that completes when the rule is stored or the problem is shown.</returns>
    /// <remarks>
    ///     Only while the user may still view and administer the project. The change is a compare-and-set against the
    ///     value the page loaded, so two administrators cannot silently overwrite each other; a stale value is shown
    ///     as an error and the page reloads the stored value.
    /// </remarks>
    private async Task SaveReviewRulesAsync()
    {
        RulesError = null;
        RulesStatus = null;
        if (!await GuardMembersAsync())
        {
            return;
        }

        try
        {
            var actor = await AuthenticationState.GetActorAsync();
            var expected = Project?.RequireIndependentReviewers ?? true;
            var changed = await ProjectService.SetRequireIndependentReviewersAsync(
                actor, ProjectId, expected, RequireIndependentChoice);
            Project = await ProjectService.GetAsync(actor, ProjectId);
            var message = "The review rules were already set this way.";
            if (changed)
            {
                message = RequireIndependentChoice
                    ? "Independent reviewers are now required."
                    : "Independent reviewers are no longer required.";
            }

            RulesStatus = message;
        }
        catch (DomainRuleException ex)
        {
            // A refusal can also mean the right was lost during the call; ask again so that the error is shown only
            // when the user may still see the section
            if (await GuardMembersAsync())
            {
                RulesError = ex.Message;
                var actor = await AuthenticationState.GetActorAsync();
                Project = await ProjectService.GetAsync(actor, ProjectId);
                RequireIndependentChoice = Project?.RequireIndependentReviewers ?? true;
            }
        }
    }

    /// <summary>
    ///     Removes a member from the project.
    /// </summary>
    /// <param name="member">The member as shown.</param>
    /// <returns>A task that completes when the member is removed or the problem is shown.</returns>
    private Task<bool> RemoveMemberAsync(ProjectMember member)
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
    private Task<bool> ToggleRoleAsync(ProjectMember member)
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
    ///     The change runs only while the user may still view and administer the project; when either is gone the
    ///     page drops what the user may no longer see instead of showing an error next to it. The status is shown
    ///     only after the list has been reloaded, so it never stands next to a stale list. A refusal such as the
    ///     last-project-admin rule is shown as the error of the section.
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
    ///     Loads the member list; only an administrator of the project manages members.
    /// </summary>
    /// <param name="actor">The signed-in user.</param>
    /// <returns>A task that completes when the list is loaded.</returns>
    private async Task LoadMembersAsync(Actor actor)
    {
        Members = CanAdminister ? await ProjectService.ListMembersAsync(actor, ProjectId) : [];
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
