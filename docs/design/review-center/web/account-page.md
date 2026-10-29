### AccountPage

![Web Structure](WebView.svg)

#### Purpose

`AccountPage` is the page at `/account`. Its responsibilities are to let the signed-in user choose the appearance of the
interface, let the user change their own password (a user who signs in with an identity provider has no password, so
the page says so instead), let the user create, list, and revoke their personal access tokens
for the API, let a local user set up and turn off an optional second factor (a one-time code from an authenticator app,
with recovery codes), and report when the page is interactive. It requires a signed-in user. When the account still has
a
temporary password (the claim `must_change_password`), it is also the only page the user may use: it then shows a
banner and only the change-password form (no appearance, no tokens, and no second factor), and after the change it
sends the user to the
login page. It is a Razor component split into
presentation (`AccountPage.razor`) and behavior (`AccountPage.razor.cs`). The
page uses the narrow page container, because it holds forms, and its cards are headed Appearance, Change password,
API tokens, and Two-factor authentication.

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

**MustChangePassword**: whether the signed-in user still has to replace a temporary password, read once from the
authentication state with `SignIn.IsPasswordChangeRequired` when the page is initialized. When it is true the tokens are
not loaded, the appearance and tokens cards are not shown, a banner says that the password is temporary, and the label
of the first password field reads "Temporary password" instead of "Current password".

**CurrentPassword**, **NewPassword**, **ConfirmPassword**: the values typed into the form. The second entry of the new
password only guards against a typing error, so it is compared on the page; Core does not know it.

**IsExternal**: whether the signed-in user signs in with an identity provider, read once with `UserService.GetAsync`
when the page is initialized. When it is true the change-password card holds only the sentence "Your password is
managed by {DisplayName of the provider}. There is no password to change here." (the display name comes from the
injected `IOptions<OidcOptions>`), and no form. The appearance and tokens cards are the same as for any user.

**ErrorMessage**: the message of the last rejected attempt; `null` when there is none.

**Totp** (injected `TotpService`) and the second factor state of the Two-factor authentication card, which exists
only when the user has no temporary password and the feature is available (`TotpService.FeatureEnabled`; for an account
of an identity provider the card holds only the sentence "Two-factor sign-in is handled by {provider}."). **TotpInfo**:
the status read with `TotpService.GetStatusAsync` (whether the factor is on and how many recovery codes are left, shown
as "N recovery codes left"), read when the page is initialized and after every change. **PendingEnrollment**: the
enrollment in progress after the password was accepted; it is held only in the memory of this page instance. The page
shows its secret grouped by four characters and its `otpauth://` address once (there is no QR code, so the user types
or pastes them into the authenticator app). **NewRecoveryCodes**: the ten recovery codes of a confirmation or a
renewal, shown once in a box with a copy button (`reviewCenter.copyText`) and an "I have saved them" button that
removes them. **TotpStartPassword**, **TotpConfirmCode**, **TotpDisablePassword**, **TotpDisableCode**,
**TotpRegenPassword**, **TotpRegenCode**: the values typed into the forms; **TotpError**, **TotpMessage**, and
**TotpCopyStatus** hold the last rejected action, the last confirmation, and the answer of the copy button.

**Message**: the confirmation of the last successful change; `null` when there is none.

**InteractiveMarker**: `"true"` or `"false"`, rendered as the `data-interactive` attribute of the root element; it
becomes `true` after the first render on a live connection.

**Tokens**: the personal access tokens of the signed-in user, newest first, read from `PersonalAccessTokenService` when
the page is initialized and again after every change. The page never sees a token value, only the name, the display id,
the creation, expiry, and last-used times, and the state. **TokenName** and **TokenExpiryDays** (30, 90, or 365; 90 is
selected at first) are the values of the create form. **TokenError** and **TokenMessage** hold the last rejected action
and the last confirmation. **NewTokenValue** is the value of the token that was just created: it is held only in the
memory of this page instance (the state of this connection), shown once in a box that says it cannot be shown again, and
removed by the "Done" button or when the page is left. **CopyStatus** is the answer of the copy button. The state of a
token is worded by the page: Revoked when it has a revocation time, Expired when the clock of the page is at or past the
expiry, and otherwise Active.

Whose password changes is decided by the authentication state, never by a field on the page.

Every interactive element and key value has a stable `data-testid` (see the test identifier rule of the Web
subsystem): `account-page` (root), `password-change-required-banner` (only with a temporary password), `appearance-card`,
`theme-system`, `theme-light`, `theme-dark`,
`change-password-form`, `current-password-input`, `new-password-input`, `confirm-password-input`, `account-error`,
`account-message`, `change-password-button`, `account-external-password` (only for a user of an identity provider,
in place of the form), and for the tokens card `tokens-card`, `tokens-help-link`,
`create-token-form`, `token-name-input`, `token-expiry-{days}`, `create-token-button`, `token-error`, `token-message`,
`new-token-box`, `new-token-value`, `copy-token-button`, `dismiss-token-button`, `token-copy-status`,
`token-list-empty`, `token-list`, and for each token (by its identity) `token-row-{id}`, `token-name-{id}`,
`token-display-id-{id}`, `token-status-{id}`, `token-created-{id}`, `token-expires-{id}`, `token-last-used-{id}`, and
`revoke-token-{id}`; and for the two-factor card `totp-card`, `totp-external`, `totp-help-link`, `totp-status`,
`totp-recovery-left`, `totp-error`, `totp-message`, the forms and buttons `totp-start-form`, `totp-start-password`,
`totp-start-button`, `totp-pending`, `totp-secret`, `totp-uri`, `totp-confirm-form`, `totp-confirm-code`,
`totp-confirm-button`, `totp-cancel-button`, `totp-disable-form`, `totp-disable-password`, `totp-disable-code`,
`totp-disable-button`, `totp-regen-form`, `totp-regen-password`, `totp-regen-code`, `totp-regen-button`, and for the
recovery codes `totp-recovery-box`, `totp-recovery-codes`, `totp-recovery-code-{index}`, `totp-recovery-copy`,
`totp-recovery-dismiss`, and `totp-copy-status`.

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
  case, so a password never stays in the page. For a user with a temporary password the service also refuses a new
  password equal to the temporary one and clears the flag; the page then navigates to `/login?changed=1` with a full
  page load, because the session of the change still carries the claim and is stale (see `SignIn.IsCurrent`), and the
  login page shows a notice that the password was changed.

The user name comes from `SignIn.GetActorAsync`, and the change is made by `UserService.ChangeOwnPasswordAsync`.

**OnAfterRenderAsync**: Marks the page interactive after the first render on a live connection, as `HomePage` does, and
then reads the stored appearance with `reviewCenter.getTheme` to show it as selected.

**CreateTokenAsync**: Handles submission of the token form.

- *Postconditions*: On success a token of the chosen name and lifetime exists, its value is shown once in the box, the
  name field is emptied, the message "The token was created." is shown, and the list is read again. On a rule violation
  (an empty or too long name, a limit of 20 active tokens, or an account that is not available) the message of the
  violation is shown and nothing is created.

**CopyTokenAsync**: Copies the new value with the browser script `reviewCenter.copyText` (defined by the head script of
`App`) and shows "Copied.". When the browser refuses or there is no script, the page shows a hint to select the text and
copy it by hand, and the value stays in the box.

**DismissToken**: Removes the value of the new token from the page for good.

**StartTotpAsync**: Handles the password form of the set-up: `TotpService.BeginEnrollmentAsync` with the user and the
typed password.

- *Postconditions*: On success `PendingEnrollment` holds the new secret and the page shows it with the confirmation
  form. A rule violation (a wrong password, a locked account, a second factor that is already on, or one that is not
  available) is shown as `TotpError`. The password field is emptied in every case.

**ConfirmTotpAsync**: Handles the confirmation form: `TotpService.ConfirmEnrollmentAsync` with the typed code.

- *Postconditions*: On success the factor is on, the pending secret is removed, and `NewRecoveryCodes` holds the ten
  recovery codes. A rule violation is shown as `TotpError`; when the enrollment is gone or has expired, the pending
  secret is removed as well, so the user starts again. The code field is emptied in every case.

**CancelTotpAsync**: Cancels the set-up in progress (`TotpService.CancelEnrollmentAsync`) and removes the secret from
the page.

**DisableTotpAsync**: Handles the turn-off form: `TotpService.DisableAsync` with the typed password and a code or a
recovery code. On success the factor is off and the page says so; a rule violation is shown as `TotpError`. Both fields
are emptied in every case.

**RegenerateRecoveryCodesAsync**: Handles the renewal form: `TotpService.RegenerateRecoveryCodesAsync` with the typed
password and code. On success the old recovery codes no longer work and the new ones are shown once; a rule violation
is shown as `TotpError`. Both fields are emptied in every case.

**CopyRecoveryCodesAsync** and **DismissRecoveryCodes**: Copy the recovery codes with `reviewCenter.copyText` and show
"Copied." (or a hint to copy by hand), and remove the codes from the page for good.

**RevokeTokenAsync**: Revokes one token of the user by its identity and shows "The token was revoked.". The revoke
button is offered only for a token that is active; a rule violation is shown as the message of the violation.

The tokens are managed on behalf of the signed-in user: the actor comes from `SignIn.GetActorAsync`, so a token cannot be
created or revoked for anyone else, and a page can only be reached with the cookie, never with a token.

Other sessions of the user stay signed in after the change. An open page of the user that was disabled, removed, or
changed ends at the next revalidation of the open page (see the `Identity` design).

#### Error Handling

A `DomainRuleException` from `UserService` or `PersonalAccessTokenService`, or from `GetActorAsync` when the sign-in
state of the open page has already
ended, is caught and its message shown in the page. A failure of the browser script that reads or stores the appearance
(no script, or a lost connection) is caught and ignored, so the default or the last selected choice stays. No other
exception is handled by the page. The new password inputs carry the maximum length as a convenience; the service remains
the authority.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content, so the page is replaced by a "Session ended" message when the
  sign-in state of the open page ends.
- **UserService** — changes the password, and tells whether the signed-in user is an account of an identity provider.
- **TotpService** — sets up, confirms, cancels, and turns off the second factor, renews the recovery codes, and reports
  the status and whether the feature is on.
- **OidcOptions** (Identity) — the display name of the provider named in the sentence (injected options).
- **PersonalAccessTokenService** — creates, lists, and revokes the tokens.
- **TimeProvider** — decides whether a token has expired.
- **SignIn** — `GetActorAsync` supplies the user name from the authentication state, and `IsPasswordChangeRequired`
  tells whether the password is temporary.
- **NavigationManager** (ASP.NET Core) — sends the user to the login page after the change of a temporary password.
- **App** — its head script defines `reviewCenter.getTheme`, `reviewCenter.setTheme`, and `reviewCenter.copyText`, and
  applies the stored
  appearance before the first paint.
- **IJSRuntime** (ASP.NET Core) — calls the browser script functions.
- **ASP.NET Core Blazor components** — the component base class, events, authorization attribute, and the interactive
  server render mode.

#### Callers

- **App** — the router renders the page for `/account`.
- **App** (`MainLayout`) — the header has a link to this page while a user is signed in.
