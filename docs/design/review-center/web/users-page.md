### UsersPage

![Web Structure](WebView.svg)

#### Purpose

`UsersPage` is the page at `/users`. Its responsibilities are to list the users, to offer a form that creates a user,
to let an administrator disable or enable a user, set a role, and set a password, and to report when the page is
interactive. It requires the `Admin` role: a signed-in user with another role gets HTTP status 403, and an anonymous
visitor is sent to the login page. It is a Razor component split into presentation (`UsersPage.razor`) and behavior
(`UsersPage.razor.cs`).

#### Data Model

The page instance is created per browser connection, so its state is per user.

**Users**: the users shown, loaded when the page is created and reloaded after every change.

**RoleChoices**: the role chosen in a row but not yet saved, by user identity. **Passwords**: the new password typed in
a row, by user identity.

**NewUsername**, **NewDisplayName**, **NewRole**, **NewPassword**: the values typed into the new-user form. The role
defaults to `Reviewer`.

**ErrorMessage**, **StatusMessage**: the message of the last rejected action, or of the last stored change; `null`
when there is none. **MessageUserId** says where it is shown: inside the user row the action was started from, or
in the new-user card when `null`, so that the message appears next to the control the operator just used.

**InteractiveMarker**: as for the other pages.

Every interactive element and key value has a stable `data-testid` (see the test identifier rule of the Web
subsystem): `users-page` (root), `home-link`, `users-error`, `users-status`, `users-list`, `new-user-form`, `new-user-username-input`,
`new-user-display-name-input`, `new-user-role-select`, `new-user-password-input`, and `create-user-button`, and per
user (by user identity) `user-row-{id}`, `user-username-{id}`, `user-display-name-{id}`, `user-role-{id}`,
`user-enabled-{id}`, `user-toggle-enabled-{id}`, `user-role-select-{id}`, `user-set-role-{id}`,
`user-password-input-{id}`, and `user-set-password-{id}`.

#### Key Methods

**OnInitializedAsync**: Loads the user list.

**CreateUserAsync**: Handles submission of the new-user form through `UserService.CreateAsync`.

- *Postconditions*: On success the user is created, the user name, display name, and role are cleared, and the list is
  reloaded. On a rule violation the entered values are kept, except the password, and the message is shown. The
  password field is emptied in every case.

**ToggleEnabledAsync**, **SetRoleAsync**, **SetPasswordAsync**: Disable or enable the user of a row, set the role
chosen in the row, and set the password typed in the row (`UserService.SetEnabledAsync`, `SetRoleAsync`, and
`SetPasswordAsync`). The password field of the row is emptied after use. A change that would remove the last enabled
administrator is refused by Core and shown as a message.

Every change reloads the list, whether or not it succeeded, so the rows show what is stored, and clears the unsaved
role choices. Every change first goes through one shared step (`RunAsync`) that reads the signed-in user from the
authentication state and looks the user up in the user store; unless that user is enabled and has the `Admin` role,
the action is not run, nothing is reloaded, and the message "Administrator rights are required." is shown. This is
needed because an open page keeps the principal it started with.

**OnAfterRender**: Marks the page interactive after the first render on a live connection.

An administrator who changes their own role or display name must sign in again, because the authentication cookie is
checked against the stored user (see the `Identity` design).

#### Error Handling

A `DomainRuleException` from `UserService` is caught and its message shown in the page; no other exception is handled
by the page. An action by a signed-in user who is no longer an enabled administrator is refused with the message
"Administrator rights are required." The inputs carry the maximum lengths as a convenience; the service remains the
authority. Passwords are never shown or logged.

#### Dependencies

- **UserService** — lists, creates, and changes users, and looks up the signed-in user.
- **AuthenticationStateProvider** (ASP.NET Core) — gives the signed-in user; read through the `Identity` helper that
  turns it into an `Actor`.
- **ReviewLimits** and **UserRole** (Core) — the input limits and the roles offered.
- **ASP.NET Core Blazor components** — the component base class, events, authorization attribute, and the interactive
  server render mode.

#### Callers

- **App** — the router renders the page for `/users`.
- **App** (`MainLayout`) — the header has a link to this page while an administrator is signed in.
