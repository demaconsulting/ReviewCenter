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
///     Code-behind for the user administration page (<c>UsersPage.razor</c>): lists users, creates them,
///     disables or enables them, sets their role and sets their password.
/// </summary>
/// <remarks>
///     An attribute on the page limits entry to administrators. An open page re-checks its user only every
///     <see cref="AuthOptions.RevalidationSeconds"/>, so for the time before the next check every
///     action also re-checks that the signed-in user is still an enabled administrator in the user store,
///     and is refused with an error otherwise (defense in depth). The page talks to the domain only through
///     <see cref="UserService"/>, which owns the rules (including the rule that the last enabled administrator
///     cannot be disabled or demoted). A rule violation, like a stored change, is shown next to the control that was
///     used instead of being thrown. Passwords are never shown or logged, and a password field is emptied once it has been used. Each
///     browser connection (circuit) gets its own page instance, so the state here is per-user and needs no
///     synchronization.
/// </remarks>
public partial class UsersPage : ComponentBase
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
    ///     Gets the users shown in the list.
    /// </summary>
    private IReadOnlyList<User> Users { get; set; } = [];

    /// <summary>
    ///     Gets the role chosen in each row but not yet saved, by user identity.
    /// </summary>
    private Dictionary<int, UserRole> RoleChoices { get; } = [];

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
    ///     Gets the role shown in the role selector of a row: the one chosen, otherwise the stored role.
    /// </summary>
    /// <param name="user">The user of the row.</param>
    /// <returns>The role to show.</returns>
    private UserRole RoleChoice(User user)
    {
        return RoleChoices.GetValueOrDefault(user.Id, user.Role);
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
    ///     Sets the role chosen in the row on the user.
    /// </summary>
    /// <param name="user">The user as shown.</param>
    /// <returns>A task that completes when the change is stored or the problem is shown.</returns>
    private Task SetRoleAsync(User user)
    {
        var role = RoleChoice(user);
        return RunAsync(user.Id, $"Role of '{user.Username}' is now {role}.",
            actor => UserService.SetRoleAsync(user.Username, role, actor));
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
    ///     The change is refused, without reloading, unless the signed-in user is still an enabled
    ///     administrator in the user store, and also when the sign-in state has already ended (the message then
    ///     says so).
    /// </remarks>
    private async Task RunAsync(int? userId, string success, Func<Actor, Task> action)
    {
        // The outcome is shown next to the control that was used, where the operator is looking
        MessageUserId = userId;
        ErrorMessage = null;
        StatusMessage = null;

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
            await action(actor);
            StatusMessage = success;
        }
        catch (DomainRuleException ex)
        {
            ErrorMessage = ex.Message;
        }

        // Reload so the rows show what is stored, whether or not the change succeeded
        Users = await UserService.ListAsync();
        RoleChoices.Clear();
    }
}
