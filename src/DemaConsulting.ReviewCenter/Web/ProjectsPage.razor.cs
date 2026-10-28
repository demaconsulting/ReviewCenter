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
///     Code-behind for the projects page (<c>ProjectsPage.razor</c>): lists the projects the user may see.
/// </summary>
/// <remarks>
///     The page talks to the domain only through <see cref="ProjectService"/>, which returns every project to a
///     system administrator and only the member projects to anybody else. Each browser connection
///     (circuit) gets its own page instance, so the state here is per-user and needs no synchronization.
///     The page requires a signed-in user.
/// </remarks>
public partial class ProjectsPage : ComponentBase
{
    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     Gets or sets the service that creates and lists projects, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectService ProjectService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets the projects shown in the list.
    /// </summary>
    private IReadOnlyList<Project> Projects { get; set; } = [];

    /// <summary>
    ///     Gets a value indicating whether the user is a system administrator (who may create projects).
    /// </summary>
    private bool IsSystemAdmin { get; set; }

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Loads the visible projects when the page is created.
    /// </summary>
    /// <returns>A task that completes when the list is loaded.</returns>
    protected override async Task OnInitializedAsync()
    {
        var state = await AuthenticationState.GetAuthenticationStateAsync();
        IsSystemAdmin = state.User.IsInRole(SignIn.AdminRole);
        Projects = await ProjectService.ListAsync(await AuthenticationState.GetActorOrNullAsync());
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
