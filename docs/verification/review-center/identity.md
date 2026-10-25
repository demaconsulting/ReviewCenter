## Identity

### Verification Approach

The `Identity` subsystem is verified by an integration test defined in `IdentitySubsystemTests.cs`. The test starts
the real server pipeline, built by `ServerHost.CreateApplication`, on an in-memory test server with a temporary
database, and uses the real `SignIn`, `RevalidatingAuthenticationStateProvider`, and `PasswordHasher` units together
with the real `UserService` and the real
database, so nothing is mocked. A user is created through the service, and the browser helper `TestBrowser` signs in
over HTTP the way a person does, with the antiforgery token of the login page. The units are verified in detail by
their own unit tests.

### Test Environment

N/A - standard test environment (temporary SQLite databases are created by the test helper).

### Acceptance Criteria

- All integration tests pass with zero failures.
- A protected page is refused to an anonymous client and sends it to the login page.
- A user created through the service signs in with the password, is sent to the page asked for, and sees their own
  display name there.
- After signing out, the protected page is refused again.
- The stored password hash does not contain the password.
- The state provider that a new scope (a circuit) gets from the real pipeline is the revalidating provider, and it
  reports a user as valid while enabled and as no longer valid after the user is disabled.

### Test Scenarios

**IdentitySubsystem_SignInJourney_LoginUseAndLogout_WorksEndToEnd**: A user is created through `UserService` on the
real pipeline. A request for `/projects` is redirected; the user signs in through the login page with `/projects` as
the return URL and is redirected to it; the page shows the display name of the user in the header; the user signs
out, and `/projects` is redirected to the login page again. The stored password hash does not contain the password.
This scenario is tested by `IdentitySubsystem_SignInJourney_LoginUseAndLogout_WorksEndToEnd`.

**IdentitySubsystem_Revalidation_UserDisabledAfterSignIn_OpenSessionIsNoLongerValid**: On the real pipeline a user is
created through `UserService`, and the authentication state provider is resolved from a new service scope, as a circuit
gets it; it must be the revalidating provider. The provider is given a signed-in state of the user and checks it: the
user is valid. The user is disabled through the real service, and the check is repeated: the user is no longer valid.
This scenario is tested by `IdentitySubsystem_Revalidation_UserDisabledAfterSignIn_OpenSessionIsNoLongerValid`.
