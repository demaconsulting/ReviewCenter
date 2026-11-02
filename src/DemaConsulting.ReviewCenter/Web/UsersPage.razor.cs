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
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the user administration page (<c>UsersPage.razor</c>): lists users with their status (locked,
///     temporary password, last sign-in), searches and filters them, creates them, disables or enables them, makes
///     them system administrators, unlocks them and sets their password.
/// </summary>
/// <remarks>
///     An attribute on the page limits entry to system administrators. An open page re-checks its user only every
///     <see cref="AuthOptions.RevalidationSeconds"/>, so for the time before the next check every
///     action also re-checks that the signed-in user is still an enabled system administrator in the user store,
///     and drops the list and the typed passwords otherwise (defense in depth). The page talks to the domain only
///     through <see cref="UserService"/>, which owns the rules (including the rule that the last enabled system
///     administrator
///     cannot be disabled or demoted), <see cref="UserImpactService"/> (what a disabled user leaves behind) and
///     <see cref="TotpService"/>. A rule violation, like a stored change, is shown next to the control that was
///     used instead of being thrown. Passwords are never shown or logged, and a password field is emptied once it has been used. The search
///     and the filter work in memory on the loaded list. Disabling a user, resetting two-factor sign-in and setting
///     a password open an inline confirmation panel that needs a second click; nothing is changed before it. Each
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
    ///     The filter value that shows every user.
    /// </summary>
    private const string FilterAll = "all";

    /// <summary>
    ///     The filter value that shows enabled users.
    /// </summary>
    private const string FilterEnabled = "enabled";

    /// <summary>
    ///     The filter value that shows disabled users.
    /// </summary>
    private const string FilterDisabled = "disabled";

    /// <summary>
    ///     The filter value that shows locked users.
    /// </summary>
    private const string FilterLocked = "locked";

    /// <summary>
    ///     The filter value that shows system administrators.
    /// </summary>
    private const string FilterAdmins = "admins";

    /// <summary>
    ///     What an open confirmation panel is about to do.
    /// </summary>
    private enum ConfirmKind
    {
        /// <summary>Disable the user.</summary>
        Disable,

        /// <summary>Remove the two-factor sign-in of the user.</summary>
        ResetTotp,

        /// <summary>Set the typed password on the user.</summary>
        SetPassword
    }

    /// <summary>
    ///     A confirmation panel that waits for the second click.
    /// </summary>
    /// <param name="UserId">The user row the panel belongs to.</param>
    /// <param name="Kind">What the panel is about to do.</param>
    /// <param name="Impact">What disabling leaves behind; only for <see cref="ConfirmKind.Disable"/>.</param>
    /// <param name="OwnPassword">
    ///     <c>true</c> when the password is the one of the signed-in administrator, which is not made temporary; only
    ///     for <see cref="ConfirmKind.SetPassword"/>.
    /// </param>
    private sealed record Confirmation(int UserId, ConfirmKind Kind, UserImpact? Impact, bool OwnPassword = false);

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
    ///     Gets or sets the service that finds what a user leaves behind, supplied by dependency injection.
    /// </summary>
    [Inject]
    private UserImpactService ImpactService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the clock, supplied by dependency injection; decides which lockouts are still running.
    /// </summary>
    [Inject]
    private TimeProvider Time { get; set; } = null!;

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
    ///     Gets or sets the service of the optional second factor, supplied by dependency injection.
    /// </summary>
    [Inject]
    private TotpService Totp { get; set; } = null!;

    /// <summary>
    ///     Gets the identities of the users that have two-factor sign-in turned on.
    /// </summary>
    private IReadOnlySet<int> TotpUserIds { get; set; } = new HashSet<int>();

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
    ///     Gets the text typed in the search box; matches the user name and the display name without regard to case.
    /// </summary>
    private string SearchText { get; set; } = string.Empty;

    /// <summary>
    ///     Gets the selected status filter (one of the <c>Filter</c> values).
    /// </summary>
    private string StatusFilter { get; set; } = FilterAll;

    /// <summary>
    ///     Gets the open confirmation panel, or <c>null</c> when none is open.
    /// </summary>
    private Confirmation? Pending { get; set; }

    /// <summary>
    ///     Gets the users the search and the filter let through, in the order of the list.
    /// </summary>
    private IEnumerable<User> VisibleUsers
    {
        get
        {
            var now = Time.GetUtcNow();
            var text = SearchText.Trim();
            return Users.Where(user =>
                (text.Length == 0
                 || user.Username.Contains(text, StringComparison.OrdinalIgnoreCase)
                 || user.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase))
                && StatusFilter switch
                {
                    FilterEnabled => user.Enabled,
                    FilterDisabled => !user.Enabled,
                    FilterLocked => UserService.IsLocked(user, now),
                    FilterAdmins => user.IsSystemAdmin,
                    _ => true
                });
        }
    }

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
        TotpUserIds = await Totp.ListEnrolledUserIdsAsync();
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
    ///     Enables a disabled user at once, or starts disabling an enabled one.
    /// </summary>
    /// <param name="user">The user as shown.</param>
    /// <returns>A task that completes when the change is stored, the confirmation is open or the problem is shown.</returns>
    /// <remarks>
    ///     Disabling only opens the confirmation panel (see <see cref="BeginDisableAsync"/>); enabling is harmless and
    ///     stays one click.
    /// </remarks>
    private Task ToggleEnabledAsync(User user)
    {
        return user.Enabled
            ? BeginDisableAsync(user)
            : RunAsync(user.Id, $"Enabled '{user.Username}'.", actor => UserService.SetEnabledAsync(user.Username, true, actor));
    }

    /// <summary>
    ///     Opens the confirmation panel for disabling a user, after loading what the user would leave behind.
    /// </summary>
    /// <param name="user">The user as shown.</param>
    /// <returns>A task that completes when the panel is open or the problem is shown.</returns>
    /// <remarks>
    ///     The warning never blocks: the panel lists the open reviews and the projects left without a project
    ///     administrator, and the second click disables the user whatever the list says. Nothing is changed here. A
    ///     rule violation while loading is shown next to the button, without a panel.
    /// </remarks>
    private async Task BeginDisableAsync(User user)
    {
        Pending = null;
        var actor = await StartActionAsync(user.Id);
        if (actor is null)
        {
            return;
        }

        try
        {
            var impact = await ImpactService.GetImpactAsync(actor, user.Id);
            Pending = new Confirmation(user.Id, ConfirmKind.Disable, impact);
        }
        catch (DomainRuleException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    ///     Opens the confirmation panel for removing the two-factor sign-in of a user.
    /// </summary>
    /// <param name="user">The user as shown.</param>
    private void BeginResetTotp(User user)
    {
        MessageUserId = user.Id;
        ErrorMessage = null;
        StatusMessage = null;
        Pending = new Confirmation(user.Id, ConfirmKind.ResetTotp, null);
    }

    /// <summary>
    ///     Opens the confirmation panel for setting the typed password on a user.
    /// </summary>
    /// <param name="user">The user as shown.</param>
    /// <returns>A task that completes when the confirmation is open or the problem is shown.</returns>
    /// <remarks>
    ///     An empty field opens no panel; the row says that a password must be typed first. The password of the
    ///     signed-in administrator is not made temporary by the service, so its panel says that instead.
    /// </remarks>
    private async Task BeginSetPasswordAsync(User user)
    {
        Pending = null;
        var actor = await StartActionAsync(user.Id);
        if (actor is null)
        {
            return;
        }

        if (PasswordChoice(user.Id).Length == 0)
        {
            ErrorMessage = "Type a new password first.";
            return;
        }

        var own = !actor.ViaApiToken && string.Equals(actor.Username, user.Username, StringComparison.OrdinalIgnoreCase);
        Pending = new Confirmation(user.Id, ConfirmKind.SetPassword, null, own);
    }

    /// <summary>
    ///     Closes the confirmation panel without changing anything.
    /// </summary>
    private void CancelConfirmation()
    {
        Pending = null;
        MessageUserId = null;
        ErrorMessage = null;
        StatusMessage = null;
    }

    /// <summary>
    ///     Carries out the change the open confirmation panel asks for; the second click.
    /// </summary>
    /// <param name="user">The user as shown.</param>
    /// <returns>A task that completes when the change is stored or the problem is shown.</returns>
    private Task ConfirmAsync(User user)
    {
        var pending = Pending;
        if (pending is null || pending.UserId != user.Id)
        {
            return Task.CompletedTask;
        }

        return pending.Kind switch
        {
            ConfirmKind.Disable => RunAsync(
                user.Id,
                $"Disabled '{user.Username}'.",
                actor => UserService.SetEnabledAsync(user.Username, false, actor)),
            ConfirmKind.ResetTotp => ResetTotpAsync(user),
            _ => SetPasswordAsync(user, pending.OwnPassword)
        };
    }

    /// <summary>
    ///     Ends the lockout of a user so the user can try to sign in again at once.
    /// </summary>
    /// <param name="user">The user as shown.</param>
    /// <returns>A task that completes when the change is stored or the problem is shown.</returns>
    private Task UnlockAsync(User user)
    {
        return RunAsync(user.Id, $"Unlocked '{user.Username}'.", actor => UserService.UnlockAsync(actor, user.Id));
    }

    /// <summary>
    ///     Tells whether a user is locked out right now.
    /// </summary>
    /// <param name="user">The user as shown.</param>
    /// <returns><c>true</c> while the lockout lasts.</returns>
    private bool IsLocked(User user)
    {
        return UserService.IsLocked(user, Time.GetUtcNow());
    }

    /// <summary>
    ///     Formats the end of a lockout for the badge.
    /// </summary>
    /// <param name="user">A locked user.</param>
    /// <returns>The time of day in UTC, for example <c>Locked until 05:11 UTC</c>.</returns>
    private static string LockedText(User user)
    {
        return $"Locked until {user.LockoutUntil?.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture)} UTC";
    }

    /// <summary>
    ///     Words the reason why a review slot still waits, for the impact list.
    /// </summary>
    /// <param name="state">The signature state of the slot.</param>
    /// <returns><c>Not signed</c> or <c>Signed, then the review changed</c>.</returns>
    private static string SlotStateText(SlotSignatureState state)
    {
        return state == SlotSignatureState.OutOfDate ? "Signed, then the review changed" : "Not signed";
    }

    /// <summary>
    ///     Formats the last sign-in of a user.
    /// </summary>
    /// <param name="user">The user as shown.</param>
    /// <returns>The UTC date and time, or <c>Never</c> when the user has not signed in.</returns>
    private static string LastSignInText(User user)
    {
        return user.LastSignInUtc is { } moment
            ? moment.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
            : "Never";
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
    ///     Removes the two-factor sign-in of a user who lost the device and the recovery codes.
    /// </summary>
    /// <param name="user">The user as shown.</param>
    /// <returns>A task that completes when the change is stored or the problem is shown.</returns>
    /// <remarks>
    ///     Runs after the confirmation. The service checks again that the signed-in user is a system administrator;
    ///     the sessions of the user stay valid and the user can set up two-factor sign-in again.
    /// </remarks>
    private Task ResetTotpAsync(User user)
    {
        return RunAsync(user.Id, $"Two-factor sign-in of '{user.Username}' was reset.",
            actor => Totp.ResetAsync(actor, user.Username));
    }

    /// <summary>
    ///     Sets the password typed in the row on the user.
    /// </summary>
    /// <param name="user">The user as shown.</param>
    /// <returns>A task that completes when the change is stored or the problem is shown.</returns>
    /// <param name="ownPassword"><c>true</c> when it is the password of the signed-in administrator.</param>
    /// <remarks>
    ///     Runs after the confirmation. The password is temporary, so the user must change it at the next sign-in,
    ///     except the own password of the administrator.
    /// </remarks>
    private async Task SetPasswordAsync(User user, bool ownPassword)
    {
        var success = ownPassword
            ? $"Password of '{user.Username}' set; it is not temporary."
            : $"Password of '{user.Username}' set; the user must change it at the next sign-in.";
        await RunAsync(user.Id, success,
            actor => UserService.SetPasswordAsync(user.Username, PasswordChoice(user.Id), actor));
        Passwords.Remove(user.Id);
    }

    /// <summary>
    ///     Starts an action: shows the outcome next to the control that was used and checks who is acting.
    /// </summary>
    /// <param name="userId">The user row the action was started from, or <c>null</c> for the new-user form.</param>
    /// <returns>The verified administrator; <c>null</c> when the action must stop and the page says why.</returns>
    private async Task<Actor?> StartActionAsync(int? userId)
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
            return null;
        }

        return await GuardAsync() ? actor : null;
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
    ///     the right away from themselves); the status of the stored change stays so that it is acknowledged. An
    ///     open confirmation panel is closed.
    /// </remarks>
    private async Task RunAsync(int? userId, string success, Func<Actor, Task> action)
    {
        Pending = null;
        var actor = await StartActionAsync(userId);
        if (actor is null)
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
        TotpUserIds = await Totp.ListEnrolledUserIdsAsync();
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
        TotpUserIds = new HashSet<int>();
        Passwords.Clear();
        Pending = null;
        MessageUserId = null;
        AccessLost = true;
        ErrorMessage = message;
    }
}
