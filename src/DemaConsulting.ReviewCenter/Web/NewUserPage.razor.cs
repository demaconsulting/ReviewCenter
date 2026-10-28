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
///     Code-behind for the new user page (<c>NewUserPage.razor</c>): creates a user.
/// </summary>
/// <remarks>
///     An attribute on the page limits entry to system administrators; the submission also re-checks that the signed-in
///     user is still an enabled system administrator (defense in depth; <see cref="UserService"/> checks the rules). A rule
///     violation is shown next to the form instead of being thrown. The password is never shown or logged and is
///     emptied as soon as it has been used. Each browser connection (circuit) gets its own page instance, so the
///     state here is per-user and needs no synchronization.
/// </remarks>
public partial class NewUserPage : ComponentBase
{
    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     Gets or sets the service that manages users, supplied by dependency injection.
    /// </summary>
    [Inject]
    private UserService UserService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the navigation manager, supplied by dependency injection; used to return to the user list.
    /// </summary>
    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the user name typed into the form.
    /// </summary>
    private string NewUsername { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the display name typed into the form.
    /// </summary>
    private string NewDisplayName { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets whether the new user is a system administrator.
    /// </summary>
    private bool NewSystemAdmin { get; set; }

    /// <summary>
    ///     Gets or sets the initial password typed into the form.
    /// </summary>
    private string NewPassword { get; set; } = string.Empty;

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
    ///     Creates a user from the form values and returns to the user list.
    /// </summary>
    /// <returns>A task that completes when the submission has been handled.</returns>
    /// <remarks>
    ///     On a rule violation the form keeps what was typed, except the password, so that the operator can
    ///     correct it.
    /// </remarks>
    private async Task CreateUserAsync()
    {
        ErrorMessage = null;
        var password = NewPassword;
        NewPassword = string.Empty;

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
        if (current is not { Enabled: true, IsSystemAdmin: true })
        {
            ErrorMessage = "System administrator rights are required.";
            return;
        }

        try
        {
            await UserService.CreateAsync(NewUsername, NewDisplayName, NewSystemAdmin, password, actor);
        }
        catch (DomainRuleException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        Navigation.NavigateTo("/users");
    }
}
