### UsersPage

![Web Structure](WebView.svg)

#### Purpose

`UsersPage` is the page at `/users`. Its responsibilities are to list the users with their status (locked, temporary
password, last sign-in), to narrow the list by a search and a filter, to lead to the page that creates a user, to let a
system administrator disable or enable a user, make a user a system administrator or take that away, set a
password, unlock a user, and reset the second factor of a user, to ask for a second click before the actions that are
hard to undo, and to report when the page is interactive. Being a system
administrator is a flag of the user, not a role
to choose; what a user may do in a project is set on the page of the project. The page requires the `SystemAdmin`
role: a signed-in user without it gets HTTP status 403, and an anonymous
visitor is sent to the login page. It is a Razor component split into presentation (`UsersPage.razor`) and behavior
(`UsersPage.razor.cs`).

The unit also contains `NewUserPage`, the page at `/users/new` that holds only the form that creates a user, split into
presentation (`NewUserPage.razor`) and behavior (`NewUserPage.razor.cs`). It has the same access rule as the list page,
a Cancel link back to the list, and returns to the list after a user has been created.

#### Layout

The list page uses the wide page container with a heading row that holds the title and the "+ New user" button, which is
the single primary button of the page. Below it a filter bar holds the search box and a drop-down with the choices All,
Enabled, Disabled, Locked, and System administrators. Each user is a row card: the user name in bold, the display name
muted, and
badges for "System admin" (only for a system administrator), the enabled state, "Locked until HH:mm UTC" (only while the
lockout lasts), "Temporary password" (only while the user has to change the password), "OIDC" (only for an account of an
identity provider; its title names the provider), and "TOTP" (only for a user with a second factor); a muted line shows
the last sign-in as `yyyy-MM-dd HH:mm UTC` or "Never"; the Disable or
Enable button, the
button "Make system admin" or "Remove system admin", the button "Unlock" (only for a locked user), and, for a user
with a
second factor, the button "Reset
two-factor" are small outline buttons at the side of the
row. Below the row the password field with its "Set password" button is
kept on one tidy line (it wraps on a narrow screen); an account of an identity provider has no password, so a muted
note "Signs in with {provider}; no local password." takes the place of that line. A confirmation panel (a warning
notice with the role `alertdialog`) opens inside the row, below the controls, and holds the explanation and the two
buttons. `NewUserPage` uses the narrow page
container for forms and starts
with a breadcrumb (muted, with "/" separators, and the current page as text instead of a link) that leads back to the
list.

#### Data Model

The page instance is created per browser connection, so its state is per user.

**Users**: the users shown, loaded when the page is created and reloaded after every change.

**SearchText**, **StatusFilter**: the text typed in the search box and the chosen filter (`all`, `enabled`, `disabled`,
`locked`, or `admins`). The visible users are those of **Users**, in the same order, whose user name or display name
contains the text in any letter case and who match the filter; the work is done in memory, with no new request. A user
is locked when `UserService.IsLocked` says so for the time of the injected `TimeProvider`.

**Pending**: the one open confirmation, if any: the user row, what it is about to do (disable, reset the second factor,
or set the password), and, for disabling, the `UserImpact` that was loaded when the panel opened. Opening another
panel, cancelling, or any finished action replaces or clears it.

**Passwords**: the new password typed in a row, by user identity.

**TotpUserIds**: the identities of the users with a confirmed second factor (`TotpService.ListEnrolledUserIdsAsync`),
read with the list and after every action, and cleared when access is lost. It decides which rows show the badge and
the reset button.

**NewUsername**, **NewDisplayName**, **NewSystemAdmin**, **NewPassword**, and an **ErrorMessage** of its own: the
values typed
into the form of `NewUserPage` and the message of its last refusal. The System administrator checkbox is unchecked by
default.

**ErrorMessage**, **StatusMessage**: the message of the last rejected action, or of the last stored change; `null`
when there is none. **MessageUserId** says where it is shown: inside the user row the action was started from, or
at the top of the page when `null`, so that the message appears next to the control the operator just used.

**InteractiveMarker**: as for the other pages.

**External account** (`User.IsExternal`): the row of such an account shows the badge `OIDC`, whose title is "Signs in
with {DisplayName of the provider}" (from the injected `IOptions<OidcOptions>`), and the note instead of the password
field and the "Set password" button, because the account has no password and `UserService.SetPasswordAsync` would refuse
it. The other controls (disable or enable, system administrator) stay, so an administrator can still disable such an
account. The page of the creation form creates local accounts only.

Every interactive element and key value has a stable `data-testid` (see the test identifier rule of the Web
subsystem): `users-page` (root), `new-user-button`, `users-error`, `users-status`, `users-list`, and per user (by user
identity) `user-row-{id}`, `user-username-{id}`, `user-display-name-{id}`, `user-admin-{id}` (the badge, only for a
system administrator), `user-enabled-{id}`, `user-locked-{id}` and `user-temporary-{id}` (the badges),
`user-last-signin-{id}`,
`user-unlock-{id}` (only for a locked user), `users-search`, `users-filter`, `users-filter-bar`,
`user-toggle-enabled-{id}`, `user-toggle-admin-{id}`, `user-password-input-{id}`, `user-set-password-{id}`,
`user-oidc-{id}` (the badge, only for an account of an identity provider), `user-no-password-{id}` (its note),
`user-totp-{id}` (the badge, only for a user with a second factor), and `user-reset-totp-{id}` (its reset button).
The confirmation panels have `user-confirm-disable-{id}`, `user-confirm-reset-totp-{id}`, and
`user-confirm-set-password-{id}`, each with the buttons `…-yes-{id}` and `…-cancel-{id}`; the disable panel also has
`user-impact-{id}` (the lists), `user-impact-review-{id}-{reviewId}-{slot}` and `user-impact-project-{id}-{projectId}`
(the links), and `user-impact-empty-{id}` (the note that nothing is waiting).
The page of the form has `new-user-page` (root), `breadcrumb-users-link`, `users-error`,
`new-user-form`, `new-user-username-input`, `new-user-display-name-input`, `new-user-system-admin-checkbox`,
`new-user-password-input`, `create-user-button`, and `cancel-new-user-link`.

#### Key Methods

**OnInitializedAsync**: Loads the user list.

**CreateUserAsync** (`NewUserPage`): Handles submission of the form through `UserService.CreateAsync`, after the same
checks as `RunAsync` (the signed-in state has not ended and the user is still an enabled system administrator).

- *Postconditions*: On success the user is created and the page navigates to `/users`. On a rule violation the page
  stays, the entered values are kept, except the password, and the message is shown above the form. The password field
  is emptied in every case.

**ToggleEnabledAsync**: Enables the user of a row at once; for an enabled user it only starts disabling (see
`BeginDisableAsync`).

**BeginDisableAsync**: Opens the disable confirmation. After the same signed-in check as `RunAsync` it loads
`UserImpactService.GetImpactAsync(actor, userId)` and opens the panel. The panel says that the user can no longer sign
in, that the API tokens of the user stop working, and that open review slots and signatures stay as they are; it lists
the open reviews as links to `/projects/{projectId}/reviews/{reviewId}` and the projects where the user is the only
project administrator as links to `/projects/{projectId}`, or says that nothing is waiting. The warning never blocks:
nothing is changed until the second click on "Disable anyway" (`ConfirmAsync`, which runs
`UserService.SetEnabledAsync` through `RunAsync`), and "Cancel" closes the panel. A rule violation while loading (for
example, the user does not exist any more) is shown as the error of the row, with no panel.

**BeginResetTotp**, **BeginSetPasswordAsync**: Open the confirmation for the reset of the second factor and for setting
the typed password; they change nothing. The first says that the authenticator and the recovery codes are removed, that
the user has to set up two-factor sign-in again, that the user signs in with the password alone until then because the
second factor is not enforced, and that open sessions stay valid. The second reads the signed-in administrator like
`RunAsync` does (and so makes the same signed-in check) and says that the user must change the password at the next
sign-in and can only use the Account, Help, and About pages until then (the pages that the layout leaves open while the
password is temporary), cannot sign reviews, and that the API tokens of the user stop working until the password is
changed. For the row of the signed-in administrator, whose own password `UserService.SetPasswordAsync` does not make
temporary, the panel says instead that the password is not temporary and that nothing is restricted. A password field
that is empty opens no panel and shows "Type a new password first." The rules behind each sentence are those of
`UserService`, `TotpService`, the layout of the page shell, `SigningService`, and `PersonalAccessTokenService`.

**ToggleSystemAdminAsync**, **SetPasswordAsync**, **UnlockAsync**: Make the user of a row a system administrator or take
that away (the button shows the opposite of the current state), set the
password typed in the row, and end the lockout (`UserService.SetSystemAdminAsync`, `SetPasswordAsync`, and
`UnlockAsync`). Setting the password is the action behind the second click. The password field of the row is emptied
after use. The page shows "'{user}' is now a system
administrator." or "'{user}' is no longer a system administrator.", "Password of '{user}' set; the user must change it
at the next sign-in." (for the own row: "Password of '{user}' set; it is not temporary."), and "Unlocked '{user}'." A
change that would remove the last enabled
system administrator is refused by Core and shown as a message. The impact list words the state of a slot as "Not
signed" or "Signed, then the review changed".

**ResetTotpAsync**: Removes the second factor of the user of a row through `TotpService.ResetAsync` (for a user who lost
the phone and the recovery codes), through the same `RunAsync` step as the other actions, after the second click. The
page shows "Two-factor
sign-in of '{user}' was reset." Only a system administrator may do it; Core refuses anybody else. The user is not
signed out: a session that is already open stays valid, and the next sign-in needs only the password until the user
sets the factor up again.

Every change reloads the list, whether or not it succeeded, so the rows show what is stored. Every change first goes
through one shared step (`RunAsync`) that reads the signed-in user from the authentication state and then asks
`ProjectViewGuard.GuardSystemAdminAsync` (through `GuardAsync`, over `IProjectAccess`) whether that user is still an
enabled system administrator; unless so, the action is not run, nothing is reloaded, and `ShowAccessLost` is called
with the message "System administrator rights are required." The guard is asked a second time before the reload,
because the change may have taken the right from the very user who made it; the stored change is then still
acknowledged, but the list is not shown. `ShowAccessLost` clears `Users`, `Passwords`, `TotpUserIds`, `Pending`, and
`MessageUserId`, sets
`AccessLost`, and shows the message, and the page then renders neither the "+ New user" button nor the list, so the
names and roles of the users, and the passwords typed into the rows, are no longer readable. This check is kept on
every action although the page itself is replaced when the sign-in state ends (see `SessionGate`), because an
open page can keep its state for up to one revalidation interval after the user was demoted or disabled. When the state
has already ended, `GetActorAsync` refuses with a rule violation, which `RunAsync` catches and shows through
`ShowAccessLost` as a message, again without the list.

**OnAfterRender**: Marks the page interactive after the first render on a live connection.

A system administrator who changes their own system administrator flag or display name must sign in again, because the
authentication cookie is
checked against the stored user (see the `Identity` design).

#### Error Handling

A `DomainRuleException` from `UserService` is caught and its message shown in the page; no other exception is handled
by the page. An action by a signed-in user who is no longer an enabled system administrator is refused with the message
"System administrator rights are required." and the user list is no longer rendered. A state that has already ended is
shown with the message of the refusal ("You are
no longer signed in, so nothing was saved. Reload the page to sign in again."), also without the list. The inputs carry
the maximum lengths as
a convenience; the service remains the authority. Passwords are never shown or logged.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content, so the page is replaced by a "Session ended" message when the
  sign-in state of the open page ends.
- **UserService** — lists, creates, and changes users, ends a lockout, tells whether a user is locked, and looks up the
  signed-in user.
- **UserImpactService** (Core) — tells what disabling a user leaves behind, for the disable confirmation.
- **TimeProvider** — the time against which a lockout is still running.
- **TotpService** — lists the users with a second factor and resets the second factor of a user.
- **OidcOptions** (Identity) — the display name of the provider that the badge and the note name (injected options).
- **IProjectAccess** (Core) — through `ProjectViewGuard.GuardSystemAdminAsync`, tells whether the signed-in user is
  still an enabled system administrator, before and after every action.
- **AuthenticationStateProvider** (ASP.NET Core) — gives the signed-in user; read through the `Identity` helper that
  turns it into an `Actor`.
- **ReviewLimits** (Core) — the input limits.
- **NavigationManager** (ASP.NET Core) — used by `NewUserPage` to return to the list.
- **ASP.NET Core Blazor components** — the component base class, events, authorization attribute, and the interactive
  server render mode.

#### Callers

- **App** — the router renders the pages for `/users` and `/users/new`.
- **App** (`MainLayout`) — the header has a link to this page while a system administrator is signed in.
