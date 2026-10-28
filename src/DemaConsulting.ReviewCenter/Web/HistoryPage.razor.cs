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

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the review history page (<c>HistoryPage.razor</c>): the audit entries recorded for one
///     review, newest first, 50 per page.
/// </summary>
/// <remarks>
///     The page reads the review and the project name through <see cref="ReviewService"/> and
///     <see cref="ProjectService"/>, and the entries through <see cref="AuditService"/>; it changes nothing. The
///     position in the trail is carried in the address (<c>?before=</c> for an older page, <c>?after=</c> for a
///     newer one), so the page can be bookmarked and the links are plain anchors. The page requires a signed-in
///     user; every signed-in user may read the history of a review, as every signed-in user may read the review
///     itself. Each browser connection (circuit) gets its own page instance, so the state here is per-user and
///     needs no synchronization.
/// </remarks>
public partial class HistoryPage : ComponentBase
{
    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

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
    ///     Gets or sets the service that reads the audit trail, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuditService AuditService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that reads reviews, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ReviewService ReviewService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that reads projects, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectService ProjectService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

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
    ///     Gets the page of entries shown, or <c>null</c> when the review does not exist.
    /// </summary>
    private AuditEntryPage? Page { get; set; }

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Loads the review and the page of entries whenever the address changes.
    /// </summary>
    /// <returns>A task that completes when the page is loaded.</returns>
    protected override async Task OnParametersSetAsync()
    {
        // Clear the previous page before the first await so a stale page is never shown under a new address
        Loaded = false;
        Review = null;
        Page = null;
        ProjectName = null;

        // A project the user may not see is treated like a missing one: nothing of its review is loaded
        var project = await ProjectService.GetAsync(await AuthenticationState.GetActorOrNullAsync(), ProjectId);
        ProjectName = project?.Name;
        Review = project is null ? null : await ReviewService.GetAsync(ProjectId, ReviewId);
        if (Review is not null)
        {
            Page = await AuditService.ListAsync(ProjectId, ReviewId, Before, After);
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
    ///     Formats the time of an entry in UTC.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The time as <c>yyyy-MM-dd HH:mm:ss</c> in UTC.</returns>
    private static string FormatTime(AuditEntry entry)
    {
        return entry.TimestampUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Builds the address of another page of this history.
    /// </summary>
    /// <param name="key">The query key: <c>before</c> or <c>after</c>.</param>
    /// <param name="sequence">The sequence to carry; <c>null</c> gives the first page.</param>
    /// <returns>The address, relative to the site root.</returns>
    private string PageLink(string key, long? sequence)
    {
        var path = $"/projects/{ProjectId}/reviews/{Uri.EscapeDataString(ReviewId)}/history";
        return sequence is { } value ? $"{path}?{key}={value.ToString(CultureInfo.InvariantCulture)}" : path;
    }
}
