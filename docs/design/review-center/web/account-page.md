### AccountPage

![Web Structure](WebView.svg)

#### Purpose

`AccountPage` is the page at `/account`. Its responsibilities are to let the signed-in user choose the appearance of the
interface, let the user change their own password, and report when the page is interactive. It requires a signed-in
user. It is a Razor component split into presentation (`AccountPage.razor`) and behavior (`AccountPage.razor.cs`). The
page uses the narrow page container, because it holds forms, and its two cards are headed Appearance and Change password.

#### Data Model

The page instance is created per browser connection, so its state is per user.

**Theme**: the selected appearance, `system` (the default), `light`, or `dark`. It is not stored on the server and not in
the database: the choice is kept per browser in the browser storage (`localStorage`, key `reviewcenter-theme`; the
system choice is represented by no entry), and the page reads it from the browser after its first render and writes it
through the script functions `reviewCenter.getTheme` and `reviewCenter.setTheme`. These functions are defined by the
script in the HTML head of the `App` component, which also applies the stored choice before the first paint (see the
`App` design); there `system` follows the device setting `prefers-color-scheme`, and the result is published as the
`data-theme` attribute and the `color-scheme` style of the root element. All access to the browser storage is guarded,
so a browser that blocks it simply shows the default.

**CurrentPassword**, **NewPassword**, **ConfirmPassword**: the values typed into the form. The second entry of the new
password only guards against a typing error, so it is compared on the page; Core does not know it.

**ErrorMessage**: the message of the last rejected attempt; `null` when there is none.

**Message**: the confirmation of the last successful change; `null` when there is none.

**InteractiveMarker**: `"true"` or `"false"`, rendered as the `data-interactive` attribute of the root element; it
becomes `true` after the first render on a live connection.

Whose password changes is decided by the authentication state, never by a field on the page.

Every interactive element and key value has a stable `data-testid` (see the test identifier rule of the Web
subsystem): `account-page` (root), `appearance-card`, `theme-system`, `theme-light`, `theme-dark`,
`change-password-form`, `current-password-input`, `new-password-input`, `confirm-password-input`, `account-error`,
`account-message`, and `change-password-button`.

#### Key Methods

**SelectThemeAsync**: Handles the choice of one of the three option cards (radio inputs). It sets `Theme` and calls
`reviewCenter.setTheme`, which applies the appearance at once and stores it in the browser.

- *Postconditions*: The chosen option is selected, and the page and every other page of the browser change at once.
  When the browser script is not available, or the circuit is gone, the choice stays selected for this view and nothing
  else happens.

**ChangePasswordAsync**: Handles submission of the form.

- *Postconditions*: On success the password of the signed-in user is changed, the message "Your password was changed."
  is shown, and the error is removed. When the two entries of the new password differ, "The new passwords do not
  match." is shown and nothing is sent to Core. On a rule violation (a new password outside the policy, a wrong current
  password, or a locked account) the message of the violation is shown. The three password fields are emptied in every
  case, so a password never stays in the page.

The user name comes from `SignIn.GetActorAsync`, and the change is made by `UserService.ChangeOwnPasswordAsync`.

**OnAfterRenderAsync**: Marks the page interactive after the first render on a live connection, as `HomePage` does, and
then reads the stored appearance with `reviewCenter.getTheme` to show it as selected.

Other sessions of the user stay signed in after the change. An open page of the user that was disabled, removed, or
changed ends at the next revalidation of the open page (see the `Identity` design).

#### Error Handling

A `DomainRuleException` from `UserService`, or from `GetActorAsync` when the sign-in state of the open page has already
ended, is caught and its message shown in the page. A failure of the browser script that reads or stores the appearance
(no script, or a lost connection) is caught and ignored, so the default or the last selected choice stays. No other
exception is handled by the page. The new password inputs carry the maximum length as a convenience; the service remains
the authority.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content, so the page is replaced by a "Session ended" message when the
  sign-in state of the open page ends.
- **UserService** — changes the password.
- **SignIn** — `GetActorAsync` supplies the user name from the authentication state.
- **App** — its head script defines `reviewCenter.getTheme` and `reviewCenter.setTheme` and applies the stored
  appearance before the first paint.
- **IJSRuntime** (ASP.NET Core) — calls the browser script functions.
- **ASP.NET Core Blazor components** — the component base class, events, authorization attribute, and the interactive
  server render mode.

#### Callers

- **App** — the router renders the page for `/account`.
- **App** (`MainLayout`) — the header has a link to this page while a user is signed in.
