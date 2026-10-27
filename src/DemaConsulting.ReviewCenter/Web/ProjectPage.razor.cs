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
using Microsoft.AspNetCore.Components;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the project page (<c>ProjectPage.razor</c>): shows one project and lists its reviews with
///     their approval state. Reviews are created on the separate new review page.
/// </summary>
/// <remarks>
///     The page talks to the domain only through <see cref="ProjectService"/>, <see cref="ReviewService"/>,
///     <see cref="SigningService"/> and <see cref="RepositoryService"/>. Each browser connection (circuit) gets its
///     own page instance, so the state here is per-user and needs no synchronization. The page requires a signed-in
///     user.
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
    ///     Gets a value indicating whether the project lookup has finished.
    /// </summary>
    private bool Loaded { get; set; }

    /// <summary>
    ///     Gets the project shown, or <c>null</c> when it does not exist.
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
        Project = await ProjectService.GetAsync(ProjectId);
        Reviews = Project is null ? [] : await ReviewService.ListAsync(ProjectId);
        Repositories = await RepositoryService.ListEnabledAsync();

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
