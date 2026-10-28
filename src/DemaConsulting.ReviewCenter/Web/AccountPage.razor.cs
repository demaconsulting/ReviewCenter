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
///     password and manages their personal access tokens for the API.
/// </summary>
/// <remarks>
///     The page talks to the domain only through <see cref="UserService"/> and <see cref="PersonalAccessTokenService"/>,
///     which own the rules; a rule violation is shown next to the form instead of being thrown. Whose password or
///     tokens change is decided by the authentication state, never by a field on the page. The repeated new password
///     is only a typing check, so it is compared here. Passwords are never shown or logged, and the fields are
///     emptied after every attempt. A new token value is held only in the state of this page instance until it is
///     dismissed; it is never written to a log or stored. Each browser connection (circuit) gets its own page
///     instance, so the state here is per-user and needs no synchronization.
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
    ///     Gets or sets the service that manages personal access tokens, supplied by dependency injection.
    /// </summary>
    [Inject]
    private PersonalAccessTokenService TokenService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the navigation manager, supplied by dependency injection.
    /// </summary>
    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the clock that decides whether a token has expired, supplied by dependency injection.
    /// </summary>
    [Inject]
    private TimeProvider Time { get; set; } = null!;

    /// <summary>
    ///     Gets the tokens of the signed-in user, newest first; never a token value.
    /// </summary>
    private IReadOnlyList<PersonalAccessToken> Tokens { get; set; } = [];

    /// <summary>
    ///     Gets or sets the name typed into the create-token form.
    /// </summary>
    private string TokenName { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the chosen lifetime of the next token, in days.
    /// </summary>
    private int TokenExpiryDays { get; set; } = ReviewLimits.DefaultTokenExpiryDays;

    /// <summary>
    ///     Gets the message of the last rejected token action, or <c>null</c> when there is none.
    /// </summary>
    private string? TokenError { get; set; }

    /// <summary>
    ///     Gets the confirmation of the last token action, or <c>null</c> when there is none.
    /// </summary>
    private string? TokenMessage { get; set; }

    /// <summary>
    ///     Gets the value of the token that was just created, or <c>null</c>; held only in the memory of this page
    ///     instance and gone once dismissed or when the page is left.
    /// </summary>
    private string? NewTokenValue { get; set; }

    /// <summary>
    ///     Gets the result of the last copy attempt, or <c>null</c>.
    /// </summary>
    private string? CopyStatus { get; set; }

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
    ///     Loads the tokens of the signed-in user.
    /// </summary>
    /// <returns>A task that completes when the list is loaded.</returns>
    protected override async Task OnInitializedAsync()
    {
        // A temporary password limits the page to the password change; the service refuses tokens for it anyway
        var state = await AuthenticationState.GetAuthenticationStateAsync();
        MustChangePassword = SignIn.IsPasswordChangeRequired(state.User);
        if (!MustChangePassword)
        {
            await LoadTokensAsync();
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the signed-in user still has to replace a temporary password; the page
    ///     then shows only the change-password form.
    /// </summary>
    private bool MustChangePassword { get; set; }

    /// <summary>
    ///     Creates a token from the form and shows its value once.
    /// </summary>
    /// <returns>A task that completes when the attempt has been handled.</returns>
    private async Task CreateTokenAsync()
    {
        TokenError = null;
        TokenMessage = null;
        try
        {
            var actor = await AuthenticationState.GetActorAsync();
            var created = await TokenService.CreateAsync(actor, TokenName, TokenExpiryDays);

            // The value exists only here: it is not stored anywhere that can show it again
            NewTokenValue = created.Value;
            CopyStatus = null;
            TokenName = string.Empty;
            TokenMessage = "The token was created.";
            await LoadTokensAsync();
        }
        catch (DomainRuleException ex)
        {
            TokenError = ex.Message;
        }
    }

    /// <summary>
    ///     Revokes one of the tokens of the signed-in user.
    /// </summary>
    /// <param name="tokenId">The identity of the token.</param>
    /// <returns>A task that completes when the attempt has been handled.</returns>
    private async Task RevokeTokenAsync(int tokenId)
    {
        TokenError = null;
        TokenMessage = null;
        try
        {
            var actor = await AuthenticationState.GetActorAsync();
            await TokenService.RevokeAsync(actor, tokenId);
            TokenMessage = "The token was revoked.";
            await LoadTokensAsync();
        }
        catch (DomainRuleException ex)
        {
            TokenError = ex.Message;
        }
    }

    /// <summary>
    ///     Copies the new token to the clipboard of the browser.
    /// </summary>
    /// <returns>A task that completes when the browser has answered.</returns>
    private async Task CopyTokenAsync()
    {
        try
        {
            var copied = await Js.InvokeAsync<bool>("reviewCenter.copyText", NewTokenValue);
            CopyStatus = copied ? "Copied." : "Could not copy; select the token and copy it by hand.";
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException)
        {
            CopyStatus = "Could not copy; select the token and copy it by hand.";
        }
    }

    /// <summary>
    ///     Hides the value of the new token for good.
    /// </summary>
    private void DismissToken()
    {
        NewTokenValue = null;
        CopyStatus = null;
    }

    /// <summary>
    ///     Loads the token list of the signed-in user; an empty list when the account cannot be found.
    /// </summary>
    /// <returns>A task that completes when the list is loaded.</returns>
    private async Task LoadTokensAsync()
    {
        try
        {
            var actor = await AuthenticationState.GetActorAsync();
            Tokens = await TokenService.ListAsync(actor);
        }
        catch (DomainRuleException)
        {
            // The account is gone; SessionGate shows the "session ended" message, so there is nothing to list
            Tokens = [];
        }
    }

    /// <summary>
    ///     Words the state of a token.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns><c>Revoked</c>, <c>Expired</c> or <c>Active</c>.</returns>
    private string StatusOf(PersonalAccessToken token)
    {
        if (token.Revoked is not null)
        {
            return "Revoked";
        }

        return token.IsActive(Time.GetUtcNow()) ? "Active" : "Expired";
    }

    /// <summary>
    ///     Formats a time as a UTC date.
    /// </summary>
    /// <param name="time">The time.</param>
    /// <returns>The date as <c>yyyy-MM-dd</c>.</returns>
    private static string FormatDate(DateTimeOffset time)
    {
        return time.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

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

            // The old session still describes the temporary password, so the user signs in again with the new one
            if (MustChangePassword)
            {
                Navigation.NavigateTo("/login?changed=1", forceLoad: true);
            }
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
