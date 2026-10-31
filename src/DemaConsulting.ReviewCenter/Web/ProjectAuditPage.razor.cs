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
///     Code-behind for the project audit page (<c>ProjectAuditPage.razor</c>): the audit entries of one project for
///     whoever may read its audit trail.
/// </summary>
/// <remarks>
///     An attribute on the page requires a signed-in user; the page shows the entries only to whoever holds the
///     audit-read right of the project (a system administrator, a project admin of it, or an Auditor of it) and shows the not-found state of a
///     missing project to anybody else. The page changes nothing. It reads only the entries that carry the project
///     identity (see <see cref="AuditService.ListAsync"/>), never entries of other projects or global ones, with the
///     filter of the address (<see cref="AuditFilterState"/>, which never fails on a hand-edited address). The
///     position in the trail is carried in the address (<c>?before=</c> for an older page, <c>?after=</c> for a
///     newer one). Each browser connection (circuit) gets its own page instance, so the state here is per-user and
///     needs no synchronization.
/// </remarks>
public partial class ProjectAuditPage : ComponentBase
{
    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     Gets or sets the identity of the project whose trail is shown, from the page address.
    /// </summary>
    [Parameter]
    public int ProjectId { get; set; }

    /// <summary>
    ///     Gets or sets the sequence to start before (the "Older" position), from the <c>before</c> query value.
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "before")]
    public long? Before { get; set; }

    /// <summary>
    ///     Gets or sets the sequence to start after (the "Newer" position), from the <c>after</c> query value.
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "after")]
    public long? After { get; set; }

    /// <summary>
    ///     Gets or sets the actor text to look for, from the <c>actor</c> query value.
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "actor")]
    public string? ActorQuery { get; set; }

    /// <summary>
    ///     Gets or sets the action or action group to look for, from the <c>action</c> query value.
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "action")]
    public string? ActionQuery { get; set; }

    /// <summary>
    ///     Gets or sets the review identifier to look for, from the <c>review</c> query value.
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "review")]
    public string? ReviewQuery { get; set; }

    /// <summary>
    ///     Gets or sets the first UTC day to show, from the <c>from</c> query value (<c>yyyy-MM-dd</c>).
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "from")]
    public string? FromQuery { get; set; }

    /// <summary>
    ///     Gets or sets the last UTC day to show, from the <c>to</c> query value (<c>yyyy-MM-dd</c>).
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "to")]
    public string? ToQuery { get; set; }

    /// <summary>
    ///     Gets or sets the page size, from the <c>size</c> query value.
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "size")]
    public string? SizeQuery { get; set; }

    /// <summary>
    ///     Gets or sets the service that reads the audit trail, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuditService AuditService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that reads projects, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectService ProjectService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the access service, supplied by dependency injection; decides who may read the audit trail of the project.
    /// </summary>
    [Inject]
    private IProjectAccess Access { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets a value indicating whether the lookup has finished.
    /// </summary>
    private bool Loaded { get; set; }

    /// <summary>
    ///     Gets the project shown, or <c>null</c> when it does not exist or the user may not administer it.
    /// </summary>
    private Project? Project { get; set; }

    /// <summary>
    ///     Gets the page of entries shown, or <c>null</c> when it is not loaded (or the user is refused).
    /// </summary>
    private AuditEntryPage? Page { get; set; }

    /// <summary>
    ///     Gets the cleaned filter of the address (the project of the page is always applied on top of it).
    /// </summary>
    private AuditFilterState Filter { get; set; } = AuditFilterState.FromQuery(null, null, null, null, null, null, null);

    /// <summary>
    ///     Gets the address the paging buttons extend, without a query.
    /// </summary>
    private string BasePath => $"/projects/{ProjectId}/audit";

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Loads the page of entries whenever the address changes, for an administrator of the project only.
    /// </summary>
    /// <returns>A task that completes when the page is loaded or refused.</returns>
    protected override async Task OnParametersSetAsync()
    {
        // Clear the previous page before the first await so a stale page is never shown under a new address
        Loaded = false;
        Project = null;
        Page = null;
        Filter = AuditFilterState.FromQuery(ActorQuery, ActionQuery, ReviewQuery, FromQuery, ToQuery, SizeQuery, null);

        // The shared guard is the one access check of every handler; here it also decides whether to load at all.
        // Reading the trail is the audit-read right (project administrators and system administrators today)
        if (await Access.GuardAuditAsync(AuthenticationState, ProjectId, ShowNoAccess))
        {
            var actor = await AuthenticationState.GetActorOrNullAsync();
            Project = await ProjectService.GetAsync(actor, ProjectId);
            if (Project is not null)
            {
                Page = await AuditService.ListAsync(Filter.ToFilter(ProjectId), Before, After, Filter.PageSize);
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
    ///     Drops the project and its entries and shows the same "does not exist" state as for a non-member.
    /// </summary>
    private void ShowNoAccess()
    {
        Project = null;
        Page = null;
        Loaded = true;
    }
}
