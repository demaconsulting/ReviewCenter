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
///     An attribute on the page limits entry to administrators; the submission also re-checks that the signed-in
///     user is still an enabled administrator (defense in depth; <see cref="RepositoryService"/> checks again). The
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
    ///     Gets or sets the service that manages the registry, supplied by dependency injection.
    /// </summary>
    [Inject]
    private RepositoryService RepositoryService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that knows the users, supplied by dependency injection.
    /// </summary>
    [Inject]
    private UserService UserService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the navigation manager, supplied by dependency injection; used to return to the registry.
    /// </summary>
    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

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

        // Defense in depth: an open page re-checks its user only every revalidation interval, so ask the store
        // who the user is now. An ended sign-in state is shown as a message, not raised as a fault
        Actor actor;
        try
        {
            actor = await AuthenticationState.GetActorAsync();
        }
        catch (DomainRuleException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        var current = await UserService.GetAsync(actor.Username);
        if (current is not { Enabled: true, Role: UserRole.Admin })
        {
            ErrorMessage = "Administrator rights are required.";
            return;
        }

        try
        {
            await RepositoryService.CreateAsync(NewName, NewLocation, NewUsername, token, actor);
        }
        catch (DomainRuleException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        Navigation.NavigateTo("/repositories");
    }
}
