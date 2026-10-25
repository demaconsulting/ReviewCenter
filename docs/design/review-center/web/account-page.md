### AccountPage

![Web Structure](WebView.svg)

#### Purpose

`AccountPage` is the page at `/account`. Its responsibilities are to let the signed-in user change their own password
and to report when the page is interactive. It requires a signed-in user. It is a Razor component split into
presentation (`AccountPage.razor`) and behavior (`AccountPage.razor.cs`).

#### Data Model

The page instance is created per browser connection, so its state is per user.

**CurrentPassword**, **NewPassword**, **ConfirmPassword**: the values typed into the form. The second entry of the new
password only guards against a typing error, so it is compared on the page; Core does not know it.

**ErrorMessage**: the message of the last rejected attempt; `null` when there is none.

**Message**: the confirmation of the last successful change; `null` when there is none.

**InteractiveMarker**: `"true"` or `"false"`, rendered as the `data-interactive` attribute of the root element; it
becomes `true` after the first render on a live connection.

Whose password changes is decided by the authentication state, never by a field on the page.

Every interactive element and key value has a stable `data-testid` (see the test identifier rule of the Web
subsystem): `account-page` (root), `home-link`, `change-password-form`, `current-password-input`,
`new-password-input`, `confirm-password-input`, `account-error`, `account-message`, and `change-password-button`.

#### Key Methods

**ChangePasswordAsync**: Handles submission of the form.

- *Postconditions*: On success the password of the signed-in user is changed, the message "Your password was changed."
  is shown, and the error is removed. When the two entries of the new password differ, "The new passwords do not
  match." is shown and nothing is sent to Core. On a rule violation (a new password outside the policy, a wrong current
  password, or a locked account) the message of the violation is shown. The three password fields are emptied in every
  case, so a password never stays in the page.

The user name comes from `SignIn.GetActorAsync`, and the change is made by `UserService.ChangeOwnPasswordAsync`.

**OnAfterRender**: Marks the page interactive after the first render on a live connection, as `HomePage` does.

Other sessions of the user stay signed in after the change. An open page of the user that was disabled, removed, or
changed ends at the next revalidation of the open page (see the `Identity` design).

#### Error Handling

A `DomainRuleException` from `UserService`, or from `GetActorAsync` when the sign-in state of the open page has already
ended, is caught and its message shown in the page; no other exception is handled
by the page. The new password inputs carry the maximum length as a convenience; the service remains the authority.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content, so the page is replaced by a "Session ended" message when the
  sign-in state of the open page ends.
- **UserService** — changes the password.
- **SignIn** — `GetActorAsync` supplies the user name from the authentication state.
- **ASP.NET Core Blazor components** — the component base class, events, authorization attribute, and the interactive
  server render mode.

#### Callers

- **App** — the router renders the page for `/account`.
- **App** (`MainLayout`) — the header has a link to this page while a user is signed in.
