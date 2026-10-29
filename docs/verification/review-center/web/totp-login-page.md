### TotpLoginPage

#### Verification Approach

`TotpLoginPage` is verified with unit tests defined in `TotpLoginPageTests.cs`, which render the component in memory
with bUnit on the shared page test base (`PageTestBase`). The tests use the real `TotpChallengeStore` and a real user
service over in-memory stores: a pending sign-in is started for a user and the request that carries its cookie is
passed to the page as the cascading HTTP context. The form itself is an ordinary form that posts to the sign-in
endpoint, and the posting is verified by the `TotpSignIn` unit. No mocking is applied.

#### Test Environment

N/A - standard test environment. The tests render in memory and need no server, no files, and no network.

#### Acceptance Criteria

- All tests pass with zero failures.
- With a valid pending sign-in the page shows a form that posts to `/auth/login-totp` with the antiforgery field, a
  code field that asks for a one-time code, and a link back to the sign-in page, and it does not show the user name.
- A failed attempt shows one generic message whatever the value of the error flag and never says why the code failed;
  the flag `throttled` shows the throttle message instead.
- Without a cookie, with a cookie that names no pending sign-in, or with the cookie from another client address the
  page sends the browser to the sign-in page with the expired flag.
- The sign-in page shows a notice to sign in again when the second step expired, and then no invalid password
  message; it shows no notice for an ordinary failure.

#### Test Scenarios

**TotpLoginPage_Render_PendingSignIn_ShowsCodeFormAndNoUserName**: The page is rendered with a pending sign-in. The form
posts to `/auth/login-totp`, the code field is named `code` with the one-time code hint, the link goes to `/login`, no
error is shown, the main text does not contain the user name, and the address stays on the page. This scenario is
tested by `TotpLoginPage_Render_PendingSignIn_ShowsCodeFormAndNoUserName`.

**TotpLoginPage_Render_ErrorFlag_ShowsMatchingMessage**: A theory over the flags `error=1`, `error=anything`, and
`error=throttled`; the first two show the generic message and the third shows the throttle message, and the other
message is absent. This scenario is tested by `TotpLoginPage_Render_ErrorFlag_ShowsMatchingMessage`.

**TotpLoginPage_Render_NoValidChallenge_RedirectsToLoginWithExpiredFlag**: A theory over no cookie and a cookie that
names no challenge; the browser is sent to `/login?error=totp-expired`. This scenario is tested by
`TotpLoginPage_Render_NoValidChallenge_RedirectsToLoginWithExpiredFlag`.

**TotpLoginPage_Render_ChallengeFromOtherAddress_RedirectsToLogin**: The pending sign-in was started from one address
and the page is requested from another; the browser is sent to the sign-in page with the expired flag. This scenario
is tested by `TotpLoginPage_Render_ChallengeFromOtherAddress_RedirectsToLogin`.
