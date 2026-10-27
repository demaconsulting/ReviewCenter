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
using Microsoft.JSInterop;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the account page (<c>AccountPage.razor</c>): the signed-in user changes their own
///     password.
/// </summary>
/// <remarks>
///     The page talks to the domain only through <see cref="UserService"/>, which owns the rules; a rule
///     violation is shown next to the form instead of being thrown. Whose password changes is decided by the
///     authentication state, never by a field on the page. The repeated new password is only a typing check,
///     so it is compared here. Passwords are never shown or logged, and the fields are emptied after every
///     attempt. Each browser connection (circuit) gets its own page instance, so the state here is per-user and
///     needs no synchronization.
/// </remarks>
public partial class AccountPage : ComponentBase
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
    ///     Gets or sets the browser script runtime used to apply and read the appearance choice.
    /// </summary>
    [Inject]
    private IJSRuntime Js { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the selected appearance: <c>system</c> (the default), <c>light</c> or <c>dark</c>.
    /// </summary>
    private string Theme { get; set; } = "system";

    /// <summary>
    ///     Gets or sets the current password typed into the form.
    /// </summary>
    private string CurrentPassword { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the new password typed into the form.
    /// </summary>
    private string NewPassword { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the repeated new password typed into the form.
    /// </summary>
    private string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>
    ///     Gets the message of the last rejected attempt, or <c>null</c> when there is none.
    /// </summary>
    private string? ErrorMessage { get; set; }

    /// <summary>
    ///     Gets the confirmation of the last successful change, or <c>null</c> when there is none.
    /// </summary>
    private string? Message { get; set; }

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
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Only the first render transitions the marker; later renders must not loop
        if (!firstRender)
        {
            return;
        }

        _interactive = true;
        StateHasChanged();

        // Show the browser's stored appearance choice; the choice lives in the browser, not on the server
        try
        {
            var stored = await Js.InvokeAsync<string?>("reviewCenter.getTheme");
            if (stored is "system" or "light" or "dark" && stored != Theme)
            {
                Theme = stored;
                StateHasChanged();
            }
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException)
        {
            // No browser script available (or the circuit is gone): keep the default choice
        }
    }

    /// <summary>
    ///     Applies and remembers the appearance chosen by the user.
    /// </summary>
    /// <param name="theme">The choice: <c>system</c>, <c>light</c> or <c>dark</c>.</param>
    /// <returns>A task that completes when the browser has been told.</returns>
    private async Task SelectThemeAsync(string theme)
    {
        Theme = theme;
        try
        {
            await Js.InvokeVoidAsync("reviewCenter.setTheme", theme);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException)
        {
            // The choice stays selected for this view even if the browser could not store it
        }
    }

    /// <summary>
    ///     Changes the password of the signed-in user from the form values.
    /// </summary>
    /// <returns>A task that completes when the attempt has been handled.</returns>
    /// <remarks>
    ///     The password fields are emptied whatever the outcome, so a password never stays in the page.
    /// </remarks>
    private async Task ChangePasswordAsync()
    {
        ErrorMessage = null;
        Message = null;

        try
        {
            // The new password must be typed twice the same way before anything is sent
            if (NewPassword != ConfirmPassword)
            {
                ErrorMessage = "The new passwords do not match.";
                return;
            }

            var actor = await AuthenticationState.GetActorAsync();
            await UserService.ChangeOwnPasswordAsync(actor.Username, CurrentPassword, NewPassword);
            Message = "Your password was changed.";
        }
        catch (DomainRuleException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
        }
    }
}
