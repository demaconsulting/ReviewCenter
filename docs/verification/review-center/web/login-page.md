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
