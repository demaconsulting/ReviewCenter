### LoginPage

#### Verification Approach

`LoginPage` is verified with tests defined in `SignInTests.cs`. The page is rendered on the server only and is an
ordinary HTML form that posts to the login endpoint, so it has no live behavior that a component test could drive;
instead the tests start the real server pipeline on an in-memory test server with a temporary database and request
the page over HTTP, using the browser helper `TestBrowser`, which parses the response. The sign-in itself is
verified by the `SignIn` unit and the `Identity` subsystem. No mocking is applied.

#### Test Environment

N/A - standard test environment (temporary SQLite databases are created by the test helper).

#### Acceptance Criteria

- All tests pass with zero failures.
- The page is served to a visitor who is not signed in, and has the user name, password, and sign-in controls.
- The error "Invalid user name or password." is shown only after a failed sign-in.
- The message "Too many sign-in attempts. Try again in a few minutes." is shown when the error flag is `throttled`,
  and the generic message is not.
- The hidden return URL field carries only a local path, and `/` for any other value.
- A failed sign-in comes back to this page with the error flag and the return URL kept.
- The notice to sign in with the new password is shown when the address carries `changed=1`, and not without the flag
  or with another value.
- Only when sign-in with a provider is on, the page shows a "Sign in with" button with the display name of the
  provider, in a form of its own beside the local form, that carries the local return address; when the address says
  the provider failed (`error=oidc`) it shows one general message next to the button and none in the local form; when
  the provider is off it shows no button.

#### Test Scenarios

**SignIn_LoginPage_ErrorAndReturnUrl_AreShownSafely**: The page is requested plainly, after a failed sign-in with a
local return URL, and with a hostile return URL. The controls are present; the error is shown only after the failed
sign-in; and the hidden return URL field holds the local path, or `/` for the hostile URL. This scenario is tested
by `SignIn_LoginPage_ErrorAndReturnUrl_AreShownSafely`.

**SignIn_PublicPage_Anonymous_IsServed**: A theory over the home, login, and health routes; the anonymous request
for the login page is answered with status 200. This scenario is tested by `SignIn_PublicPage_Anonymous_IsServed`.

**SignIn_Login_WrongPasswordWithReturnUrl_KeepsReturnUrlOnLoginPage**: Sign-in with a wrong password and a return URL
is redirected to this page with the error flag and the same return URL. This scenario is tested by
`SignIn_Login_WrongPasswordWithReturnUrl_KeepsReturnUrlOnLoginPage`.

**SignIn_LoginPage_ThrottledError_ShowsTooManyAttemptsMessage**: The page is requested with the error flag
`throttled`; it shows the "too many attempts" message and not the generic error. This scenario is tested by
`SignIn_LoginPage_ThrottledError_ShowsTooManyAttemptsMessage`.

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

**OidcSignIn_Enabled_SignInPageOffersButtonBesideLocalForm**: Through the real pipeline with a fake provider and the
provider on, the page requested over HTTP shows the button with the display name beside the local form, the general
message after a failure, and the local return address. This scenario is tested by
`OidcSignIn_Enabled_SignInPageOffersButtonBesideLocalForm`, defined in `OidcSignInTests.cs` and described with the
`OidcSignIn` unit.
