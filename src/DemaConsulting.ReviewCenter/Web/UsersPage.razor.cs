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

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the user administration page (<c>UsersPage.razor</c>): lists users, creates them,
///     disables or enables them, makes them system administrators and sets their password.
/// </summary>
/// <remarks>
///     An attribute on the page limits entry to system administrators. An open page re-checks its user only every
///     <see cref="AuthOptions.RevalidationSeconds"/>, so for the time before the next check every
///     action also re-checks that the signed-in user is still an enabled system administrator in the user store,
///     and drops the list and the typed passwords otherwise (defense in depth). The page talks to the domain only
///     through <see cref="UserService"/>, which owns the rules (including the rule that the last enabled system
///     administrator
///     cannot be disabled or demoted). A rule violation, like a stored change, is shown next to the control that was
///     used instead of being thrown. Passwords are never shown or logged, and a password field is emptied once it has been used. Each
///     browser connection (circuit) gets its own page instance, so the state here is per-user and needs no
///     synchronization.
/// </remarks>
public partial class UsersPage : ComponentBase
{
    /// <summary>
    ///     The reason shown when the signed-in user is no system administrator any more.
    /// </summary>
    private const string SystemAdminRequired = "System administrator rights are required.";

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
    ///     Gets or sets the access service, supplied by dependency injection; decides who is a system administrator.
    /// </summary>
    [Inject]
    private IProjectAccess Access { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the settings of sign-in with an identity provider, supplied by dependency injection; the
    ///     display name of the provider marks the accounts that belong to it.
    /// </summary>
    [Inject]
    private IOptions<OidcOptions> Oidc { get; set; } = null!;

    /// <summary>
    ///     Gets the users shown in the list.
    /// </summary>
    private IReadOnlyList<User> Users { get; set; } = [];

    /// <summary>
    ///     Gets a value indicating whether the signed-in user turned out to be no system administrator any more, so
    ///     that the list and its controls are not rendered.
    /// </summary>
    private bool AccessLost { get; set; }

    /// <summary>
    ///     Gets the new passwords typed in the rows, by user identity.
    /// </summary>
    private Dictionary<int, string> Passwords { get; } = [];

    /// <summary>
    ///     Gets the message of the last rejected action, or <c>null</c> when there is none.
    /// </summary>
    private string? ErrorMessage { get; set; }

    /// <summary>
    ///     Gets the message about the last stored change, or <c>null</c> when there is none.
    /// </summary>
    private string? StatusMessage { get; set; }

    /// <summary>
    ///     Gets the user row the last message belongs to, or <c>null</c> when it belongs to the new-user form.
    /// </summary>
    private int? MessageUserId { get; set; }

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Loads the user list when the page is created.
    /// </summary>
    /// <returns>A task that completes when the list is loaded.</returns>
    protected override async Task OnInitializedAsync()
    {
        Users = await UserService.ListAsync();
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
    ///     Gets the password text shown in the password field of a row.
    /// </summary>
    /// <param name="userId">The user identity of the row.</param>
    /// <returns>The typed text; empty when nothing was typed.</returns>
    private string PasswordChoice(int userId)
    {
        return Passwords.GetValueOrDefault(userId, string.Empty);
    }

    /// <summary>
    ///     Remembers the password text typed in the password field of a row.
    /// </summary>
    /// <param name="userId">The user identity of the row.</param>
    /// <param name="value">The typed text; <c>null</c> counts as empty.</param>
    private void SetPasswordChoice(int userId, string? value)
    {
        Passwords[userId] = value ?? string.Empty;
    }

    /// <summary>
    ///     Disables an enabled user or enables a disabled one.
    /// </summary>
    /// <param name="user">The user as shown.</param>
    /// <returns>A task that completes when the change is stored or the problem is shown.</returns>
    private Task ToggleEnabledAsync(User user)
    {
        return RunAsync(user.Id, $"{(user.Enabled ? "Disabled" : "Enabled")} '{user.Username}'.",
            actor => UserService.SetEnabledAsync(user.Username, !user.Enabled, actor));
    }

    /// <summary>
    ///     Makes the user a system administrator, or takes that away.
    /// </summary>
    /// <param name="user">The user as shown.</param>
    /// <returns>A task that completes when the change is stored or the problem is shown.</returns>
    private Task ToggleSystemAdminAsync(User user)
    {
        var makeAdmin = !user.IsSystemAdmin;
        return RunAsync(
            user.Id,
            makeAdmin ? $"'{user.Username}' is now a system administrator." : $"'{user.Username}' is no longer a system administrator.",
            actor => UserService.SetSystemAdminAsync(user.Username, makeAdmin, actor));
    }

    /// <summary>
    ///     Sets the password typed in the row on the user.
    /// </summary>
    /// <param name="user">The user as shown.</param>
    /// <returns>A task that completes when the change is stored or the problem is shown.</returns>
    private async Task SetPasswordAsync(User user)
    {
        await RunAsync(user.Id, $"Password of '{user.Username}' changed.",
            actor => UserService.SetPasswordAsync(user.Username, PasswordChoice(user.Id), actor));
        Passwords.Remove(user.Id);
    }

    /// <summary>
    ///     Runs a change, shows its outcome next to the control that was used, and reloads the list.
    /// </summary>
    /// <param name="userId">The user row the change was started from, or <c>null</c> for the new-user form.</param>
    /// <param name="success">The message to show when the change is stored.</param>
    /// <param name="action">The change to run; it receives the verified administrator as the actor to audit.</param>
    /// <returns>A task that completes when the change is stored or the problem is shown.</returns>
    /// <remarks>
    ///     The change is refused, without reloading, unless the signed-in user is still an enabled system
    ///     administrator, and also when the sign-in state has already ended (the message then says so). A refusal
    ///     drops the user list and the typed passwords, because the list is for system administrators only. The
    ///     same happens after the change when the user is no longer an administrator (for instance after taking
    ///     the right away from themselves); the status of the stored change stays so that it is acknowledged.
    /// </remarks>
    private async Task RunAsync(int? userId, string success, Func<Actor, Task> action)
    {
        // The outcome is shown next to the control that was used, where the operator is looking
        MessageUserId = userId;
        ErrorMessage = null;
        StatusMessage = null;

        // Defense in depth: an open page re-checks its user only every revalidation interval, so ask who the user
        // is now. An ended sign-in state is shown as a message, not raised as a fault
        Actor actor;
        try
        {
            actor = await AuthenticationState.GetActorAsync();
        }
        catch (DomainRuleException ex)
        {
            ShowAccessLost(ex.Message);
            return;
        }

        if (!await GuardAsync())
        {
            return;
        }

        try
        {
            await action(actor);
            StatusMessage = success;
        }
        catch (DomainRuleException ex)
        {
            ErrorMessage = ex.Message;
        }

        // The change may have taken the right from this very user; then the list is not reloaded and not shown
        if (!await GuardAsync())
        {
            return;
        }

        // Reload so the rows show what is stored, whether or not the change succeeded
        Users = await UserService.ListAsync();
    }

    /// <summary>
    ///     Re-checks that the signed-in user is still an enabled system administrator.
    /// </summary>
    /// <returns>
    ///     <c>true</c> when the action may go on; <c>false</c> when the right is gone, in which case the page has
    ///     already dropped the list and the typed passwords, and the action must stop.
    /// </returns>
    private Task<bool> GuardAsync()
    {
        return Access.GuardSystemAdminAsync(AuthenticationState, () => ShowAccessLost(SystemAdminRequired));
    }

    /// <summary>
    ///     Drops the user list and the typed passwords and shows why.
    /// </summary>
    /// <param name="message">The reason shown instead of the list.</param>
    /// <remarks>
    ///     The usernames, display names and flags of all users are for system administrators only. The status
    ///     message is left as it is, so that a change that was stored just before the right was lost stays
    ///     acknowledged.
    /// </remarks>
    private void ShowAccessLost(string message)
    {
        Users = [];
        Passwords.Clear();
        MessageUserId = null;
        AccessLost = true;
        ErrorMessage = message;
    }
}
