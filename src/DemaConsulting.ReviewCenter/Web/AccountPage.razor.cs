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
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the account page (<c>AccountPage.razor</c>): the signed-in user changes their own
///     password, manages their optional second factor (authenticator app) and their personal access tokens for the
///     API.
/// </summary>
/// <remarks>
///     The page talks to the domain only through <see cref="UserService"/>, <see cref="TotpService"/> and
///     <see cref="PersonalAccessTokenService"/>,
///     which own the rules; a rule violation is shown next to the form instead of being thrown. Whose password or
///     tokens change is decided by the authentication state, never by a field on the page. The repeated new password
///     is only a typing check, so it is compared here. Passwords and codes are never shown or logged, and the fields are
///     emptied after every attempt. A new token value, the secret of a second factor set-up and new recovery codes
///     are held only in the state of this page instance until they are dismissed or the set-up ends; they are never
///     written to a log or stored. Each browser connection (circuit) gets its own page instance, so the state here
///     is per-user and needs no synchronization.
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
    ///     Gets or sets the settings of sign-in with an identity provider, supplied by dependency injection.
    /// </summary>
    [Inject]
    private IOptions<OidcOptions> Oidc { get; set; } = null!;

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
    ///     Gets or sets the service of the optional second factor, supplied by dependency injection.
    /// </summary>
    [Inject]
    private TotpService Totp { get; set; } = null!;

    /// <summary>
    ///     Gets what the page shows of the second factor of the signed-in user, or <c>null</c> when it is not loaded
    ///     (a temporary password, an account of an identity provider, or no account).
    /// </summary>
    private TotpStatus? TotpInfo { get; set; }

    /// <summary>
    ///     Gets the secret of the enrollment in progress, or <c>null</c>; held only in the memory of this page
    ///     instance and gone once the enrollment is confirmed, cancelled or the page is left.
    /// </summary>
    private TotpEnrollment? PendingEnrollment { get; set; }

    /// <summary>
    ///     Gets the recovery codes that were just issued, or <c>null</c>; held only in the memory of this page
    ///     instance until dismissed.
    /// </summary>
    private IReadOnlyList<string>? NewRecoveryCodes { get; set; }

    /// <summary>
    ///     Gets the message of the last rejected second factor action, or <c>null</c>.
    /// </summary>
    private string? TotpError { get; set; }

    /// <summary>
    ///     Gets the confirmation of the last second factor action, or <c>null</c>.
    /// </summary>
    private string? TotpMessage { get; set; }

    /// <summary>
    ///     Gets the result of the last attempt to copy the recovery codes, or <c>null</c>.
    /// </summary>
    private string? TotpCopyStatus { get; set; }

    /// <summary>
    ///     Gets or sets the password typed into the set-up form.
    /// </summary>
    private string TotpStartPassword { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the code typed into the confirm form.
    /// </summary>
    private string TotpConfirmCode { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the password typed into the turn-off form.
    /// </summary>
    private string TotpDisablePassword { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the code typed into the turn-off form.
    /// </summary>
    private string TotpDisableCode { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the password typed into the new-recovery-codes form.
    /// </summary>
    private string TotpRegenPassword { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the code typed into the new-recovery-codes form.
    /// </summary>
    private string TotpRegenCode { get; set; } = string.Empty;

    /// <summary>
    ///     Gets the secret of the enrollment in progress in groups of four characters, which are easier to type.
    /// </summary>
    private string GroupedSecret => PendingEnrollment is null
        ? string.Empty
        : string.Join(' ', PendingEnrollment.SecretBase32.Chunk(4).Select(group => new string(group)));

    /// <summary>
    ///     Gets the words that tell how many recovery codes are left.
    /// </summary>
    private string RecoveryLeftText => TotpInfo?.RecoveryCodesLeft switch
    {
        1 => "1 recovery code left",
        var count => $"{count} recovery codes left"
    };

    /// <summary>
    ///     Loads the tokens of the signed-in user.
    /// </summary>
    /// <returns>A task that completes when the list is loaded.</returns>
    protected override async Task OnInitializedAsync()
    {
        // A temporary password limits the page to the password change; the service refuses tokens for it anyway
        var state = await AuthenticationState.GetAuthenticationStateAsync();
        MustChangePassword = SignIn.IsPasswordChangeRequired(state.User);
        var name = state.User.Identity?.IsAuthenticated == true ? state.User.Identity.Name : null;
        IsExternal = name is not null && (await UserService.GetAsync(name))?.IsExternal == true;
        if (!MustChangePassword)
        {
            await LoadTokensAsync();
            await LoadTotpAsync();
        }
    }

    /// <summary>
    ///     Loads the second factor status of the signed-in user; nothing is shown for an account of an identity
    ///     provider or when the account cannot be found.
    /// </summary>
    /// <returns>A task that completes when the status is loaded.</returns>
    private async Task LoadTotpAsync()
    {
        TotpInfo = null;
        if (IsExternal || !Totp.FeatureEnabled)
        {
            return;
        }

        try
        {
            TotpInfo = await Totp.GetStatusAsync(await AuthenticationState.GetActorAsync());
        }
        catch (DomainRuleException)
        {
            // The account is gone; SessionGate shows the "session ended" message
        }
    }

    /// <summary>
    ///     Starts the set-up with the typed password and shows the new secret.
    /// </summary>
    /// <returns>A task that completes when the attempt has been handled.</returns>
    private async Task StartTotpAsync()
    {
        ClearTotpMessages();
        try
        {
            PendingEnrollment = await Totp.BeginEnrollmentAsync(await AuthenticationState.GetActorAsync(), TotpStartPassword);
        }
        catch (DomainRuleException ex)
        {
            TotpError = ex.Message;
        }
        finally
        {
            TotpStartPassword = string.Empty;
        }
    }

    /// <summary>
    ///     Confirms the set-up with the typed code, turns the second factor on and shows the recovery codes once.
    /// </summary>
    /// <returns>A task that completes when the attempt has been handled.</returns>
    private async Task ConfirmTotpAsync()
    {
        ClearTotpMessages();
        try
        {
            NewRecoveryCodes = await Totp.ConfirmEnrollmentAsync(await AuthenticationState.GetActorAsync(), TotpConfirmCode);

            // The secret is not needed any more and is never shown again
            PendingEnrollment = null;
            TotpMessage = "Two-factor sign-in is on.";
            await LoadTotpAsync();
        }
        catch (DomainRuleException ex)
        {
            TotpError = ex.Message;

            // A set-up that is gone or too old cannot be confirmed any more; start again
            if (ex.Rule is DomainRule.TotpNotEnrolled or DomainRule.TotpEnrollmentExpired)
            {
                PendingEnrollment = null;
            }
        }
        finally
        {
            TotpConfirmCode = string.Empty;
        }
    }

    /// <summary>
    ///     Gives up the set-up in progress and forgets its secret.
    /// </summary>
    /// <returns>A task that completes when the set-up is gone.</returns>
    private async Task CancelTotpAsync()
    {
        ClearTotpMessages();
        PendingEnrollment = null;
        TotpConfirmCode = string.Empty;
        try
        {
            await Totp.CancelEnrollmentAsync(await AuthenticationState.GetActorAsync());
        }
        catch (DomainRuleException ex)
        {
            TotpError = ex.Message;
        }
    }

    /// <summary>
    ///     Turns the second factor off with the typed password and code.
    /// </summary>
    /// <returns>A task that completes when the attempt has been handled.</returns>
    private async Task DisableTotpAsync()
    {
        ClearTotpMessages();
        try
        {
            await Totp.DisableAsync(await AuthenticationState.GetActorAsync(), TotpDisablePassword, TotpDisableCode);
            NewRecoveryCodes = null;
            TotpMessage = "Two-factor sign-in is off.";
            await LoadTotpAsync();
        }
        catch (DomainRuleException ex)
        {
            TotpError = ex.Message;
        }
        finally
        {
            TotpDisablePassword = string.Empty;
            TotpDisableCode = string.Empty;
        }
    }

    /// <summary>
    ///     Replaces the recovery codes with new ones, shown once.
    /// </summary>
    /// <returns>A task that completes when the attempt has been handled.</returns>
    private async Task RegenerateRecoveryCodesAsync()
    {
        ClearTotpMessages();
        try
        {
            NewRecoveryCodes = await Totp.RegenerateRecoveryCodesAsync(
                await AuthenticationState.GetActorAsync(),
                TotpRegenPassword,
                TotpRegenCode);
            TotpMessage = "You have new recovery codes. The old ones no longer work.";
            await LoadTotpAsync();
        }
        catch (DomainRuleException ex)
        {
            TotpError = ex.Message;
        }
        finally
        {
            TotpRegenPassword = string.Empty;
            TotpRegenCode = string.Empty;
        }
    }

    /// <summary>
    ///     Copies the new recovery codes to the clipboard of the browser.
    /// </summary>
    /// <returns>A task that completes when the browser has answered.</returns>
    private async Task CopyRecoveryCodesAsync()
    {
        try
        {
            var copied = await Js.InvokeAsync<bool>("reviewCenter.copyText", string.Join('\n', NewRecoveryCodes ?? []));
            TotpCopyStatus = copied ? "Copied." : "Could not copy; select the codes and copy them by hand.";
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException)
        {
            TotpCopyStatus = "Could not copy; select the codes and copy them by hand.";
        }
    }

    /// <summary>
    ///     Hides the new recovery codes for good.
    /// </summary>
    private void DismissRecoveryCodes()
    {
        NewRecoveryCodes = null;
        TotpCopyStatus = null;
    }

    /// <summary>
    ///     Clears the messages of the second factor card before an action.
    /// </summary>
    private void ClearTotpMessages()
    {
        TotpError = null;
        TotpMessage = null;
    }

    /// <summary>
    ///     Gets a value indicating whether the signed-in user signs in with an identity provider and so has no
    ///     password to change here.
    /// </summary>
    private bool IsExternal { get; set; }

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
