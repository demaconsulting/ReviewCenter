## Identity

### Verification Approach

The `Identity` subsystem is verified by an integration test defined in `IdentitySubsystemTests.cs`. The test starts
the real server pipeline, built by `ServerHost.CreateApplication`, on an in-memory test server with a temporary
database, and uses the real `SignIn`, `RevalidatingAuthenticationStateProvider`, and `PasswordHasher` units together
with the real `UserService` and the real
database, so nothing is mocked. A user is created through the service, and the browser helper `TestBrowser` signs in
over HTTP the way a person does, with the antiforgery token of the login page. The units are verified in detail by
their own unit tests. The optional sign-in with an OpenID Connect provider is verified end to end by the tests of the
`OidcSignIn` unit (`OidcSignInTests.cs`, `OidcReauthTests.cs`), which start the real application with its real OpenID
Connect handler and talk to a fake provider built on OpenIddict over loopback http (see the `OidcSignIn` verification).

### Test Environment

N/A - standard test environment (temporary SQLite databases are created by the test helper).

### Acceptance Criteria

- All integration tests pass with zero failures.
- A protected page is refused to an anonymous client and sends it to the login page.
- A user created through the service signs in with the password, is sent to the page asked for, and sees their own
  display name there.
- After signing out, the protected page is refused again.
- The stored password hash does not contain the password.
- Repeated failed sign-ins of one name are refused by the throttle with the same message, and audited once per window.
- The state provider that a new scope (a circuit) gets from the real pipeline is the revalidating provider, and it
  reports a user as valid while enabled and as no longer valid after the user is disabled.
- The role claim `SystemAdmin` belongs only to a system administrator, and a session ends when the system
  administrator state of its user changes.
- With a fake OpenID Connect provider, a first sign-in creates a plain account (no project, no administrator right, no
  password) and a session, sign-out is local only, and a fresh sign-in of the same person at the provider allows
  exactly one signature without a password.

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

**System administrator claim**: The users page is served only to a signed-in system administrator and is forbidden to
an ordinary user, which is tested by `SignIn_UsersPage_ReviewerAndAdmin_OnlyAdminIsAllowed`; the shared rule compares the
presence of the claim with the stored flag, which is tested by `SignIn_IsCurrent_StoredUserVersusPrincipal_FollowsTheRule`
and, for an open page, by `RevalidatingAuthenticationStateProvider_IsCurrentAsync_RoleChanged_ReturnsFalse`; and an old
cookie of a demoted system administrator is rejected, which is tested by
`SignIn_Session_RoleChangedAfterLogin_IsRejected`. All are defined in the unit tests of `SignIn` and
`RevalidatingAuthenticationStateProvider`.

**Sign-in audit**: On the real pipeline a user signs in with a wrong password, then with the right one, and signs
out; the audit trail holds the failed sign-in, the sign-in, and the sign-out in this order, and its chain is intact.
This scenario is tested by `SignIn_Flow_LoginFailureAndLogout_FormAnIntactChain`.

**IdentitySubsystem_Throttle_RepeatedFailures_AreRefusedAndAuditedOncePerWindow**: On the real pipeline with a limit of
three attempts per name, a user is tried three times with a wrong password and then twice with the right one. The
first three answers are the generic failure; the next attempts are refused with the throttled answer even with the
right password; the login page then shows the throttle message; and the audit trail holds three `auth.sign-in-failed`
entries and one `auth.sign-in-throttled` entry, none with a password. This scenario is tested by
`IdentitySubsystem_Throttle_RepeatedFailures_AreRefusedAndAuditedOncePerWindow`.

**OidcSignIn_FirstSignIn_CreatesPlainAccountAndSignsIn**: With the fake provider, a person signs in for the first
time; the account is created with the issuer and subject, no password, no administrator right, and no project, and the
person has a session at the local return address. This scenario is tested by
`OidcSignIn_FirstSignIn_CreatesPlainAccountAndSignsIn`.

**OidcSignIn_Logout_IsLocalOnly**: After such a sign-in the person signs out; the session ends and the provider sees
no request. This scenario is tested by `OidcSignIn_Logout_IsLocalOnly`.

**OidcReauth_FreshSignInOfSamePerson_AllowsExactlyOneSignature**: A reviewer of the provider confirms the identity for
a slot; the provider is asked with `prompt=login` and `max_age=0`, a proof is issued, one signature without a password
uses it up, and the slot is signed. This scenario is tested by
`OidcReauth_FreshSignInOfSamePerson_AllowsExactlyOneSignature`.
