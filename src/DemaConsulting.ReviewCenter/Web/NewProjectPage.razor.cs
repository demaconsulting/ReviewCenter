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
///     Code-behind for the new-project page (<c>NewProjectPage.razor</c>): creates a project.
/// </summary>
/// <remarks>
///     The page talks to the domain only through <see cref="ProjectService"/>, which owns the rules; a rule
///     violation is shown to the operator next to the form instead of being thrown. Each browser connection
///     (circuit) gets its own page instance, so the state here is per-user and needs no synchronization.
///     The page requires a signed-in user.
/// </remarks>
public partial class NewProjectPage : ComponentBase
{
    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     Gets or sets the service that creates projects, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectService ProjectService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the navigation manager used to open the new project, supplied by dependency injection.
    /// </summary>
    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the name typed into the form.
    /// </summary>
    private string NewName { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the description typed into the form.
    /// </summary>
    private string NewDescription { get; set; } = string.Empty;

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
    ///     Creates a project from the form values and opens it.
    /// </summary>
    /// <returns>A task that completes when the submission has been handled.</returns>
    /// <remarks>
    ///     On a rule violation the form keeps what was typed so that the operator can correct it, and the
    ///     message is shown. The submission is refused, with a message, when the sign-in state of the page has
    ///     already ended.
    /// </remarks>
    private async Task CreateProjectAsync()
    {
        Project project;
        try
        {
            // The actor is audited; asking for it also refuses an ended sign-in state
            var actor = await AuthenticationState.GetActorAsync();
            project = await ProjectService.CreateAsync(NewName, NewDescription, actor);
        }
        catch (DomainRuleException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        // Success: open the new project
        ErrorMessage = null;
        Navigation.NavigateTo($"/projects/{project.Id}");
    }
}
