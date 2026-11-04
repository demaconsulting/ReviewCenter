### LoginPage

#### Verification Approach

`LoginPage` is an ordinary HTML form that is rendered on the server only and posts to the login endpoint. It is verified
with component tests that render the real component with bUnit (`AppTests.cs`, `OidcPageTests.cs`, and
`TotpLoginPageTests.cs`), also with the router (`Routes`) to show that a visitor who is not signed in is served the
page. The antiforgery token is added by the server when the page is served, so the component tests cannot see it; the
test of the `Identity` subsystem that requests the page over HTTP, using the browser helper `TestBrowser` on the real
server pipeline, is the evidence of the requirement `ReviewCenter-Web-SignIn-LoginPage-Antiforgery` of the `Web`
subsystem. The sign-in itself, including the redirect back to this page after a failure and the end-to-end behavior of
the provider button, is verified by the `SignIn` and `OidcSignIn` units and the `Identity` subsystem. No mocking is
applied.

#### Test Environment

N/A - standard test environment (the component tests use in-memory fakes; the test that requests the page over HTTP
creates a temporary SQLite database through the test helper).

#### Acceptance Criteria

- All tests pass with zero failures.
- The router shows the form to a visitor who is not signed in, with no access denied message and no prompt to sign in.
- The form has a user name field, a password field that hides what is typed, and a Sign in button.
- The form posts to the sign-in endpoint with the plain form method (verified by the component test); the page served
  over HTTP carries an antiforgery token in the form (the subsystem requirement).
- The error "Invalid user name or password." is shown for any failure flag in the address, whatever its value, and not
  on a plain visit.
- The message "Too many sign-in attempts. Try again in a few minutes." is shown when the error flag is `throttled`,
  and the generic message is not.
- A local return address in the address is kept in the hidden field of the form after a failure; an address that leads
  to another site is replaced by `/`.
- The notice to sign in with the new password is shown when the address carries `changed=1`, and not without the flag
  or with another value.
- Only when sign-in with a provider is on, the page shows a "Sign in with" button with the display name of the
  provider, in a form of its own beside the local form, that carries the local return address; when the address says
  the provider failed (`error=oidc`) it shows one general message next to the button and none in the local form; when
  the provider is off it shows no button.
- When the address says the second step expired (`error=totp-expired`), the page shows a notice to sign in again and
  neither the invalid password message nor the throttle message; an ordinary failure shows the password message and
  no such notice.

#### Test Scenarios

**LoginPage_Render_AnonymousThroughRouter_ShowsFormAndNoSignInPrompt**: Nobody is signed in and the router is rendered
at `/login` (in `AppTests.cs`); the login form is shown, and neither the access denied message nor the link to sign in
is. This scenario is tested by `LoginPage_Render_AnonymousThroughRouter_ShowsFormAndNoSignInPrompt`.

**LoginPage_Render_Default_ShowsUserNamePasswordAndSignInButton**: With nobody signed in the page is rendered; the form
has a text field named `username` and one of the type password named `password`, each with its label, and a submit
button "Sign in". This scenario is tested by `LoginPage_Render_Default_ShowsUserNamePasswordAndSignInButton`.

**LoginPage_Render_ErrorFlag_ShowsOneMessageWhateverTheReason**: A theory over the addresses `/login?error=1`,
`/login?error=unknown-user`, `/login?error=locked`, and `/login`; the message "Invalid user name or password." is shown
once for each of the first three, and none for the plain address. This scenario is tested by
`LoginPage_Render_ErrorFlag_ShowsOneMessageWhateverTheReason`.

**LoginPage_Render_ThrottledFlag_ShowsTooManyAttemptsMessageOnly**: The address is `/login?error=throttled`; the message
says there were too many attempts and to try again in a few minutes, and the generic error is not shown. This scenario
is tested by `LoginPage_Render_ThrottledFlag_ShowsTooManyAttemptsMessageOnly`.

**LoginPage_Render_FailureWithLocalReturnAddress_KeepsItInTheForm**: The address is `/login?error=1` with the return
address `/projects/1`; the error is shown and the hidden field `returnUrl` of the form holds `/projects/1`. This
scenario is tested by `LoginPage_Render_FailureWithLocalReturnAddress_KeepsItInTheForm`.

**LoginPage_Render_ForeignReturnAddress_IsReplacedByHomePage**: A theory over the return addresses
`https://evil.example/`, `//evil.example/`, and `javascript:alert(1)`; the hidden field `returnUrl` holds `/` and the
markup does not contain the foreign address. This scenario is tested by
`LoginPage_Render_ForeignReturnAddress_IsReplacedByHomePage`.

**LoginPage_Render_Default_FormPostsToSignInEndpoint**: With nobody signed in the page is rendered as a component (in
`AppTests.cs`); the login form uses the method post, its action is the sign-in endpoint `/auth/login`, and it has no
event handler of a live connection. This scenario is tested by `LoginPage_Render_Default_FormPostsToSignInEndpoint`.

**SignIn_LoginPage_Anonymous_FormPostsToSignInEndpointWithAntiforgeryToken**: The page is requested without being
signed in; the login form uses the method post, its action is the sign-in endpoint `/auth/login`, and it carries a
non-empty antiforgery token field. The token is added by the server when the page is served, so the component test
cannot see it. This scenario is tested by
`SignIn_LoginPage_Anonymous_FormPostsToSignInEndpointWithAntiforgeryToken`, the evidence of the requirement
`ReviewCenter-Web-SignIn-LoginPage-Antiforgery`.

**LoginPage_Render_ChangedQuery_ShowsNoticeOnlyAfterPasswordChange**: A theory over the addresses `/login?changed=1`,
`/login`, and `/login?changed=0`, rendered as a component with nobody signed in (the one test of this unit that does not
start the server, in `AppTests.cs`); the notice is shown only for `changed=1`. This scenario is tested by
`LoginPage_Render_ChangedQuery_ShowsNoticeOnlyAfterPasswordChange`.

**LoginPage_Render_ProviderEnabled_ShowsButtonBesideLocalForm**: With the provider on and nobody signed in, the page is
rendered as a component with the return address `/projects` in the query (in `OidcPageTests.cs`). The button reads
"Sign in with"
and the provider name, its form posts to `/auth/oidc/login` and carries the return address `/projects`, the local form
still posts to `/auth/login`, and no provider error is shown. This scenario is tested by
`LoginPage_Render_ProviderEnabled_ShowsButtonBesideLocalForm`.

**LoginPage_Render_ProviderFailed_ShowsGenericMessageNextToButton**: With the provider on, the address
`/login?error=oidc` shows the general message next to the button, which names the provider and not a cause, and no
error in the local form. This scenario is tested by `LoginPage_Render_ProviderFailed_ShowsGenericMessageNextToButton`.

**LoginPage_Render_ProviderOff_ShowsNoButton**: With the provider off, the address `/login?error=oidc` shows no
provider card, and the local form is the only form. This scenario is tested by
`LoginPage_Render_ProviderOff_ShowsNoButton`.

**LoginPage_Render_TotpExpired_ShowsNoticeAndNoPasswordError**: The page is rendered as a component with nobody signed
in and the address `/login?error=totp-expired` (in `TotpLoginPageTests.cs`); the notice asks to sign in again, and
neither the password error nor the throttle message is shown. This scenario is tested by
`LoginPage_Render_TotpExpired_ShowsNoticeAndNoPasswordError`.

**LoginPage_Render_PasswordFailure_ShowsNoExpiredNotice**: With the address `/login?error=1` the password error is
shown and the expired notice is not. This scenario is tested by `LoginPage_Render_PasswordFailure_ShowsNoExpiredNotice`.
