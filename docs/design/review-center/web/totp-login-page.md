### TotpLoginPage

![Web Structure](WebView.svg)

#### Purpose

`TotpLoginPage` is the second page of a sign-in with a second factor, at `/login/totp`. After the password was right,
the person types the current code of the authenticator app, or a recovery code, and the form posts it to the sign-in
endpoint (see _TotpSignIn_). The page decides nothing about the sign-in: it only shows the form and the outcome that the
endpoint reports.

#### Data Model

The page is statically rendered (no interactive state). The form is a plain HTML form that posts to `/auth/login-totp`
and contains the antiforgery token. The `error` query value selects the message: `throttled` shows that there were too
many attempts and the person should wait; any other value shows the generic message that the code is not valid or was
already used. There is a link back to the sign-in page. The elements have stable test identifiers:
`totp-login-page`, `totp-login-form`, `totp-code-input`, `totp-login-button`, `totp-login-error`,
`totp-login-throttled`, and `totp-login-cancel`.

#### Key Methods

**OnInitialized**: Reads the challenge cookie and the client address and asks `TotpService.PeekChallenge` whether a
challenge is alive. When there is none (no cookie, an ended challenge, another address) the page navigates to the login
page with `error=totp-expired`, so the person is not left with a form that cannot work. The challenge is only peeked,
never used.

#### Error Handling

The page shows no detail of why a code failed and never shows a code or a token. An expired challenge is shown on the
login page (see _LoginPage_).

#### Dependencies

- **TotpService** — peeks the challenge.
- **TotpSignIn** — the cookie name and the endpoint of the form.
- **ASP.NET Core Razor components** — rendering, navigation, and the antiforgery token.

#### Callers

- **SignIn** — redirects to the page after a right password for an account with a second factor.
- **TotpSignIn** — redirects back to the page after a wrong code.
