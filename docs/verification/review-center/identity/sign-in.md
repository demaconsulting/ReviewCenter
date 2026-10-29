### SignIn

#### Verification Approach

`SignIn` is verified with unit tests defined in `SignInTests.cs`. Most tests start the real server pipeline on an
in-memory test server with a temporary database, so the real cookie authentication, authorization, and antiforgery
protection of ASP.NET Core run with the real `UserService` and `PasswordHasher`; users are created through the
service, and the browser helper `TestBrowser` signs in over HTTP with the antiforgery token of the login page. The
pure functions (the return URL check, the actor, and the argument checks) are called directly, and the login page
is rendered through the pipeline. No mocking is applied.

#### Test Environment

N/A - standard test environment (temporary SQLite databases are created by the test helper).

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A valid sign-in redirects to the return URL and sets a session cookie that is HTTP-only and `SameSite=Lax`, and
  secure when the request used HTTPS.
- An unknown user name, a wrong password, a disabled user, and a locked user all get the same answer, a redirect to
  the login page with an error, and no cookie.
- Only a local path is accepted as a return URL; anything else becomes the home page.
- An anonymous request for a deep link (escaped characters and query included) is redirected to the login page with
  the address as requested, and signing in leads back to the same page.
- The login and logout posts need an antiforgery token.
- Every page except the home, login, and health routes needs a signed-in user, and the users page needs a system
  administrator.
- Signing out ends the session.
- A session ends when its user is disabled or its system administrator state changes.
- A user with a temporary password signs in, is redirected to the account page whatever return URL was given, gets no
  system administrator role (even a system administrator), and has a session that ends once the flag is cleared; the
  shared rule is current only when the claim is present exactly when the stored user has the flag.
- The actor is built from the signed-in user, and asking for it without a signed-in user is refused with a message
  that says nothing was saved.
- The cookie check and the re-check of open pages share one rule, and the interval of the re-check has a default of
  30 seconds and a minimum of 5.
- The login page shows the error only after a failed sign-in, and carries only a safe return URL.
- When the throttle refuses an attempt (even with the right password), the answer is the same for known and unknown
  user names, sets no cookie, and the login page shows a "too many attempts" message; a success gives the attempts
  back.
- Failed sign-ins of unknown users and locked accounts, and the refused attempts, are audited with the cleaned typed
  name, never the password, and at most one entry per key per minute.
- When the account has a second factor, the right password does not sign in: the answer is a redirect to the code page
  with a short-lived challenge cookie and no session, and the right password does not use up the address quota of the
  throttle; an account without a second factor signs in as before. The code step itself is verified by the
  `TotpSignIn` unit.

#### Test Scenarios

**SignIn_Login_ValidCredentials_SetsSafeSessionCookieAndRedirects**: A user signs in with the right password; the
response redirects to the return URL and sets the session cookie, which is HTTP-only, `SameSite=Lax`, has no expiry
(it is a session cookie), and is not marked secure over plain HTTP. This scenario is tested by
`SignIn_Login_ValidCredentials_SetsSafeSessionCookieAndRedirects`.

**SignIn_Login_OverHttps_CookieIsSecure**: A user signs in over an HTTPS request; the session cookie is marked
secure. This scenario is tested by `SignIn_Login_OverHttps_CookieIsSecure`.

**SignIn_Login_EveryKindOfFailure_GivesTheSameAnswerAndNoCookie**: Sign-in is tried with an unknown user, a wrong
password, a disabled user, and a locked user; each is redirected to the login page with the error flag, and none
gets a cookie, so the answer does not reveal which case applied. This scenario is tested by
`SignIn_Login_EveryKindOfFailure_GivesTheSameAnswerAndNoCookie`.

**SignIn_Login_WrongPasswordWithReturnUrl_KeepsReturnUrlOnLoginPage**: Sign-in with a wrong password and the return
URL `/projects/1` is redirected to the login page with the error flag and the same return URL. This scenario is
tested by `SignIn_Login_WrongPasswordWithReturnUrl_KeepsReturnUrlOnLoginPage`.

**SignIn_SafeReturnUrl_AnyInput_KeepsOnlyLocalPaths**: A theory over return URLs; local paths are kept, and an
absolute URL, a protocol-relative URL, a backslash path, a `javascript:` URL, a relative path, a text with a line
break, an empty text, and null all become `/`. This scenario is tested by
`SignIn_SafeReturnUrl_AnyInput_KeepsOnlyLocalPaths`.

**SignIn_Login_HostileReturnUrl_RedirectsToHomePage**: A valid sign-in with an absolute URL as the return URL
redirects to the home page and not to the other site. This scenario is tested by
`SignIn_Login_HostileReturnUrl_RedirectsToHomePage`.

**SignIn_PostWithoutAntiforgeryToken_IsRefusedAndSignsNobodyIn**: The login and logout endpoints are posted to
without an antiforgery token; both answer status 400 and no cookie is set. This scenario is tested by
`SignIn_PostWithoutAntiforgeryToken_IsRefusedAndSignsNobodyIn`.

**SignIn_ProtectedPage_Anonymous_RedirectsToLogin**: A theory over the projects, project, review, checklist,
account, and users pages; an anonymous request is redirected to the login page with the requested address as the
return URL. This scenario is tested by `SignIn_ProtectedPage_Anonymous_RedirectsToLogin`.

**SignIn_PublicPage_Anonymous_IsServed**: A theory over the home, login, and health routes; an anonymous request is
answered with status 200. This scenario is tested by `SignIn_PublicPage_Anonymous_IsServed`.

**SignIn_UsersPage_ReviewerAndAdmin_OnlyAdminIsAllowed**: The users page is requested by a signed-in ordinary user and
by a signed-in system administrator; the ordinary user gets status 403 with an access-denied explanation and the system
administrator status 200. The role claim therefore opens the page only for a system administrator. This scenario is
tested by `SignIn_UsersPage_ReviewerAndAdmin_OnlyAdminIsAllowed`.

**SignIn_RepositoriesPage_NonMemberAndSystemAdmin_OnlyAdminIsServed**: The repositories page of a project is
requested by a signed-in user who is not a member of the project and by a system administrator; the non-member gets
the answer for a project that does not exist, and the system administrator is served with status 200. The page
therefore takes no decision from the cookie alone: the project rule of `Core` decides. This scenario is tested by
`SignIn_RepositoriesPage_NonMemberAndSystemAdmin_OnlyAdminIsServed`.

**SignIn_Logout_SignedInUser_EndsTheSession**: A signed-in user signs out; the response redirects to the home page,
and a protected page is refused again. This scenario is tested by `SignIn_Logout_SignedInUser_EndsTheSession`.

**SignIn_Session_UserDisabledAfterLogin_IsRejected**: A signed-in user is disabled through the service; the old
cookie is no longer accepted and the protected page redirects to the login page. This scenario is tested by
`SignIn_Session_UserDisabledAfterLogin_IsRejected`.

**SignIn_Session_RoleChangedAfterLogin_IsRejected**: A signed-in system administrator is demoted through the service;
the old cookie is no longer accepted, so the users page redirects to the login page. This scenario is tested by
`SignIn_Session_RoleChangedAfterLogin_IsRejected`.

**SignIn_Login_TemporaryPassword_GoesToAccountPageWithoutAdminRoleAndStaleWhenFlagChanges**: A system administrator
whose must-change-password flag is set signs in with the return URL `/projects`; the redirect goes to `/account`, one
session cookie is set, the users page is not served, and, after the flag is cleared in the store, the old cookie is
no longer accepted and the protected page redirects. This scenario is tested by
`SignIn_Login_TemporaryPassword_GoesToAccountPageWithoutAdminRoleAndStaleWhenFlagChanges`.

**SignIn_IsCurrent_TemporaryPasswordFlag_MustMatchTheClaim**: The shared rule is called for every combination of the
stored flag and the claim, with and without a stored system administrator state; the principal is current only when
the claim is present exactly when the stored flag is set (and a flagged system administrator has no role claim), and
`IsPasswordChangeRequired` reports the claim. This scenario is tested by
`SignIn_IsCurrent_TemporaryPasswordFlag_MustMatchTheClaim`.

**SignIn_GetActorAsync_SignedInAndAnonymous_ReturnsActorOrThrows**: The actor is requested from an authentication
state with a signed-in user and from one with nobody; the first gives the user name and display name of the user and
the second throws a `DomainRuleException` of the rule `ActorNotAllowed` whose message says that nothing was saved and
that the page must be reloaded. This scenario is tested by
`SignIn_GetActorAsync_SignedInAndAnonymous_ReturnsActorOrThrows`.

**SignIn_IsCurrent_StoredUserVersusPrincipal_FollowsTheRule**: The shared rule is called with a stored user and a
principal; an enabled user whose system administrator state (the presence of the `SystemAdmin` role claim) and display
name equal the stored ones is current, and a disabled user, another system administrator state, or
another display name is not. This scenario is tested by `SignIn_IsCurrent_StoredUserVersusPrincipal_FollowsTheRule`.

**SignIn_IsCurrent_MissingPrincipalUserOrClaims_ReturnsFalse**: The shared rule is called with a missing principal, a
missing stored user, and a principal without the user name claim or without the display name claim; each is not
current. This
scenario is tested by `SignIn_IsCurrent_MissingPrincipalUserOrClaims_ReturnsFalse`.

**AuthOptions_RevalidationSeconds_Default_IsThirtyWithMinimumFive**: The default of the re-check interval of open pages
is 30 seconds, and the minimum that the options accept is 5 seconds. The refusal of a smaller value at start-up is
verified by the `Hosting` unit test `ServerHost_CreateApplication_RevalidationSecondsBelowMinimum_ThrowsClearError`.
This scenario is tested by `AuthOptions_RevalidationSeconds_Default_IsThirtyWithMinimumFive`.

**SignIn_AddSignIn_NullArguments_Throw**: The registration method is called with null arguments; an
`ArgumentNullException` is thrown. This scenario is tested by `SignIn_AddSignIn_NullArguments_Throw`.

**SignIn_LoginPage_ErrorAndReturnUrl_AreShownSafely**: The login page is requested plainly, after a failed sign-in
with a local return URL, and with a hostile return URL. It shows the user name, password, and sign-in controls; the
error "Invalid user name or password." only after a failed sign-in; and a hidden return URL field that holds the
local path, or `/` for the hostile URL. This scenario is tested by
`SignIn_LoginPage_ErrorAndReturnUrl_AreShownSafely`.

**SignIn audit scenarios**: The tests in `SignInAuditTests.cs` run the sign-in and sign-out endpoints over the real
pipeline and read the audit trail. A valid sign-in records `auth.sign-in`, which is tested by
`SignIn_Login_ValidCredentials_RecordsSignInEntry`; a sign-out records `auth.sign-out`, which is tested by
`SignIn_Logout_SignedInUser_RecordsSignOutEntry`. A failed sign-in of an unknown user records the actor
`unknown user` and the typed name, cleaned and cut, without the password, which is tested by
`SignIn_Login_UnknownUser_RecordsCleanedTypedNameWithoutPassword`, and a wrong password records the failure for the
user without the password, which is tested by `SignIn_Login_WrongPassword_RecordsFailureForTheUserWithoutPassword`.
Repeated failures record the lockout once, which is tested by `SignIn_Login_RepeatedFailures_RecordsLockoutOnce`;
an attempt on a locked account records `auth.sign-in-locked` once per minute, which is tested by
`SignIn_Login_LockedAccount_RecordsOneSignInLockedEntry`; refused attempts record `auth.sign-in-throttled` once per
minute, which is tested by `SignIn_Login_Throttled_RecordsOneThrottledEntryPerWindow`; and a complete flow forms an
intact chain, which is tested by `SignIn_Flow_LoginFailureAndLogout_FormAnIntactChain`.

**SignIn_Login_TooManyAttempts_RedirectsToThrottledAnswerIdenticalForKnownAndUnknownNames**: With the per-name limit
lowered to 2, a known and an unknown user name are each tried three times with a wrong password; for both names the
first two answers are the generic failure redirect and the third is the same redirect to `/login?error=throttled`,
with the return URL kept. This scenario is tested by
`SignIn_Login_TooManyAttempts_RedirectsToThrottledAnswerIdenticalForKnownAndUnknownNames`.

**SignIn_Login_Throttled_RefusesEvenTheRightPasswordAndSetsNoCookie**: After the limit is reached, the right password
is refused with the throttled answer and no cookie is set. This scenario is tested by
`SignIn_Login_Throttled_RefusesEvenTheRightPasswordAndSetsNoCookie`.

**SignIn_Login_SuccessAfterFailures_ResetsTheNameCounter**: With the limit lowered to 3, two wrong attempts and then the
right password are answered normally; then three wrong attempts get the generic answer and the fourth the throttled
answer, so the count started again after the success. This scenario is tested by
`SignIn_Login_SuccessAfterFailures_ResetsTheNameCounter`.

**SignIn_LoginPage_ThrottledError_ShowsTooManyAttemptsMessage**: The login page requested with `error=throttled`
shows the "Too many sign-in attempts" message and not the generic error. This scenario is tested by
`SignIn_LoginPage_ThrottledError_ShowsTooManyAttemptsMessage`.

**SignIn_DeepLink_SignedOut_ReturnsToTheSameLinkAfterLogin**: A theory over file paths with nested directories, a
space, `#`, `?`, a literal `%`, a literal `%2F` and non-ASCII letters. An anonymous request for the line link of the
file is redirected to `/login?ReturnUrl=`; the return address of that redirect, the hidden field of the login page and
the redirect after the login all equal the link as requested; and the page then shows that file with the line
highlighted. **SignIn_DeepLink_PlusAndAmpersand_ReturnsToTheSameFileAfterLogin**: the same for a name with `+` and `&`.
**SignIn_DeepLink_HostileReturnUrl_GoesToTheHomePage**: absolute, protocol-relative and backslash addresses that look
like deep links are not followed after a login. These scenarios are tested by the tests of those names.

**TotpSignIn_PasswordStep_EnrolledAccount_IssuesChallengeCookieAndNoSession**: An account with a second factor posts
the right password; the answer is a redirect to the code page with a challenge cookie and no session cookie. This
scenario is tested by `TotpSignIn_PasswordStep_EnrolledAccount_IssuesChallengeCookieAndNoSession`, defined in
`TotpSignInTests.cs` and described with the `TotpSignIn` unit.

**TotpSignIn_PasswordStep_AccountWithoutSecondFactor_SignsInAsBefore**: An account without a second factor signs in
with the password alone, with a session cookie and no challenge cookie. This scenario is tested by
`TotpSignIn_PasswordStep_AccountWithoutSecondFactor_SignsInAsBefore`, defined in `TotpSignInTests.cs` and described with
the `TotpSignIn` unit.

**TotpSignIn_PasswordStep_CorrectPassword_DoesNotUseUpTheAddressQuota**: With an address limit of two, three enrolled
accounts pass the password step from one address and none is throttled. This scenario is tested by
`TotpSignIn_PasswordStep_CorrectPassword_DoesNotUseUpTheAddressQuota`, defined in `TotpSignInTests.cs` and described
with the `TotpSignIn` unit.
