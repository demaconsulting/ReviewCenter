### UsersPage

![Web Structure](WebView.svg)

#### Purpose

`UsersPage` is the page at `/users`. Its responsibilities are to list the users, to lead to the page that creates a user,
to let an administrator disable or enable a user, set a role, and set a password, and to report when the page is
interactive. It requires the `Admin` role: a signed-in user with another role gets HTTP status 403, and an anonymous
visitor is sent to the login page. It is a Razor component split into presentation (`UsersPage.razor`) and behavior
(`UsersPage.razor.cs`).

The unit also contains `NewUserPage`, the page at `/users/new` that holds only the form that creates a user, split into
presentation (`NewUserPage.razor`) and behavior (`NewUserPage.razor.cs`). It has the same access rule as the list page,
a Cancel link back to the list, and returns to the list after a user has been created.

#### Layout

The list page uses the wide page container with a heading row that holds the title and the "+ New user" button, which is
the single primary button of the page. Each user is a row card: the user name in bold, the display name muted, and
badges for the role and the enabled state; the Disable or Enable button is a small outline button at the side of the
row. Below the row the role select with its "Set role" button and the password field with its "Set password" button are
kept on one tidy line (they wrap on a narrow screen). `NewUserPage` uses the narrow page container for forms and starts
with a breadcrumb (muted, with "/" separators, and the current page as text instead of a link) that leads back to the
list.

#### Data Model

The page instance is created per browser connection, so its state is per user.

**Users**: the users shown, loaded when the page is created and reloaded after every change.

**RoleChoices**: the role chosen in a row but not yet saved, by user identity. **Passwords**: the new password typed in
a row, by user identity.

**NewUsername**, **NewDisplayName**, **NewRole**, **NewPassword**, and an **ErrorMessage** of its own: the values typed
into the form of `NewUserPage` and the message of its last refusal. The role defaults to `Reviewer`.

**ErrorMessage**, **StatusMessage**: the message of the last rejected action, or of the last stored change; `null`
when there is none. **MessageUserId** says where it is shown: inside the user row the action was started from, or
at the top of the page when `null`, so that the message appears next to the control the operator just used.

**InteractiveMarker**: as for the other pages.

Every interactive element and key value has a stable `data-testid` (see the test identifier rule of the Web
subsystem): `users-page` (root), `new-user-button`, `users-error`, `users-status`, `users-list`, and per user (by user
identity) `user-row-{id}`, `user-username-{id}`, `user-display-name-{id}`, `user-role-{id}`, `user-enabled-{id}`,
`user-toggle-enabled-{id}`, `user-role-select-{id}`, `user-set-role-{id}`, `user-password-input-{id}`, and
`user-set-password-{id}`. The page of the form has `new-user-page` (root), `breadcrumb-users-link`, `users-error`,
`new-user-form`, `new-user-username-input`, `new-user-display-name-input`, `new-user-role-select`,
`new-user-password-input`, `create-user-button`, and `cancel-new-user-link`.

#### Key Methods

**OnInitializedAsync**: Loads the user list.

**CreateUserAsync** (`NewUserPage`): Handles submission of the form through `UserService.CreateAsync`, after the same
checks as `RunAsync` (the signed-in state has not ended and the user is still an enabled administrator).

- *Postconditions*: On success the user is created and the page navigates to `/users`. On a rule violation the page
  stays, the entered values are kept, except the password, and the message is shown above the form. The password field
  is emptied in every case.

**ToggleEnabledAsync**, **SetRoleAsync**, **SetPasswordAsync**: Disable or enable the user of a row, set the role
chosen in the row, and set the password typed in the row (`UserService.SetEnabledAsync`, `SetRoleAsync`, and
`SetPasswordAsync`). The password field of the row is emptied after use. A change that would remove the last enabled
administrator is refused by Core and shown as a message.

Every change reloads the list, whether or not it succeeded, so the rows show what is stored, and clears the unsaved
role choices. Every change first goes through one shared step (`RunAsync`) that reads the signed-in user from the
authentication state and looks the user up in the user store; unless that user is enabled and has the `Admin` role,
the action is not run, nothing is reloaded, and the message "Administrator rights are required." is shown. This check is
kept on every action although the page itself is replaced when the sign-in state ends (see `SessionGate`), because an
open page can keep its state for up to one revalidation interval after the user was demoted or disabled. When the state
has already ended, `GetActorAsync` refuses with a rule violation, which `RunAsync` catches and shows as a message.

**OnAfterRender**: Marks the page interactive after the first render on a live connection.

An administrator who changes their own role or display name must sign in again, because the authentication cookie is
checked against the stored user (see the `Identity` design).

#### Error Handling

A `DomainRuleException` from `UserService` is caught and its message shown in the page; no other exception is handled
by the page. An action by a signed-in user who is no longer an enabled administrator is refused with the message
"Administrator rights are required." A state that has already ended is shown with the message of the refusal ("You are
no longer signed in, so nothing was saved. Reload the page to sign in again."). The inputs carry the maximum lengths as
a convenience; the service remains the authority. Passwords are never shown or logged.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content, so the page is replaced by a "Session ended" message when the
  sign-in state of the open page ends.
- **UserService** — lists, creates, and changes users, and looks up the signed-in user.
- **AuthenticationStateProvider** (ASP.NET Core) — gives the signed-in user; read through the `Identity` helper that
  turns it into an `Actor`.
- **ReviewLimits** and **UserRole** (Core) — the input limits and the roles offered.
- **NavigationManager** (ASP.NET Core) — used by `NewUserPage` to return to the list.
- **ASP.NET Core Blazor components** — the component base class, events, authorization attribute, and the interactive
  server render mode.

#### Callers

- **App** — the router renders the pages for `/users` and `/users/new`.
- **App** (`MainLayout`) — the header has a link to this page while an administrator is signed in.
