### TotpSignIn

#### Verification Approach

`TotpSignIn` is verified with tests in `TotpSignInTests.cs` and `TotpOptionsTests.cs`. The settings and their validator
are checked directly. The two-step sign-in is checked end to end: the real server pipeline starts in memory with a
real database, the real `UserService`, `TotpService`, `TotpChallengeStore`, `SignInThrottle`, and `SignIn`, and the
browser helper `TestBrowser` posts the login form and then the code form the way a person does, with the antiforgery
token and its own cookies. A clock that the test can move stands for the passing of time, so the lifetime of a challenge
and the time steps of the codes pass without waiting, and the test builds the right code for the moment with the
independent reference `TotpOracle`. A header sets the client address that the server sees, so a challenge can be
used from another address. A recording logger and the audit trail are read to show that nothing secret is recorded.
Nothing in the unit is mocked. The rules of the second factor itself are verified by the `TotpService` tests, and the
page that asks for the code by the `TotpLoginPage` tests.

#### Test Environment

N/A - standard test environment. The tests start the application on an in-memory test server with temporary SQLite
databases; they need no external service and no network.

#### Acceptance Criteria

- All tests pass with zero failures.
- For an account with a second factor, the right password issues no session; it sets only the challenge cookie
  (HttpOnly, SameSite Strict, five minutes, marked secure on an https request) and sends the browser to the page that
  asks for the code. An account without a second factor signs in as before and gets no challenge.
- A right code of the current step or of the step before or after it, and an unused recovery code, issue the session,
  remove the challenge cookie, and go to the local return address; the audit entry names the kind of code and holds no
  code.
- A wrong code, a code used before, a code from far away, input that is not a code, a missing code, and an empty code
  all get the same redirect and no session; a recovery code works once.
- A missing, forged, ended, or used challenge, and a challenge used from another address, is refused by sending the
  person to the login page with no session, and the refusal from another address does not spend the challenge of the
  owner. A post without the antiforgery token is refused.
- Five wrong codes lock the account for the password step as well; wrong passwords and wrong codes count in one
  counter, a right password alone does not clear it, and a complete sign-in does.
- Every attempt of the code step is counted by the sign-in throttle before the code is checked, so past the limit
  even a right code is refused with the throttled answer; a right password does not use up the quota of the address.
- A reset by an administrator ends a pending challenge and lets the account sign in with the password alone.
- With the feature switched off the code endpoint does not exist and an enrolled account signs in with the password
  alone; a personal access token of an enrolled account works without a code.
- The settings default to on and the issuer `ReviewCenter`; an empty issuer, one with a colon or a control character,
  and one that is too long are refused with a message that names the setting and does not echo the value.
- No secret, no code, no recovery code, and no challenge token is recorded in the audit trail or in a log entry.

#### Test Scenarios

**TotpSignIn_PasswordStep_EnrolledAccount_IssuesChallengeCookieAndNoSession**: An enrolled account types the right
password; the answer is a redirect to `/login/totp`, no session cookie is set, the one challenge cookie is HttpOnly,
SameSite Strict, and lives 300 seconds, and the page of the account is not served. This scenario is tested by
`TotpSignIn_PasswordStep_EnrolledAccount_IssuesChallengeCookieAndNoSession`.

**TotpSignIn_PasswordStep_OverHttps_ChallengeCookieIsSecure**: The same over https; the challenge cookie is marked
secure. This scenario is tested by `TotpSignIn_PasswordStep_OverHttps_ChallengeCookieIsSecure`.

**TotpSignIn_PasswordStep_AccountWithoutSecondFactor_SignsInAsBefore**: A plain account signs in with the password; the
session cookie is set, the redirect goes to the return address, and there is no challenge cookie. This scenario is
tested by `TotpSignIn_PasswordStep_AccountWithoutSecondFactor_SignsInAsBefore`.

**TotpSignIn_CodeStep_RightCode_SignsInAndRedirects**: After the password step the code of the current step is posted;
the redirect goes to the requested page, an HttpOnly session cookie is set, the challenge cookie is removed, the page of
the account opens, and the audit trail names the authenticator code as the second factor. This scenario is tested by
`TotpSignIn_CodeStep_RightCode_SignsInAndRedirects`.

**TotpSignIn_CodeStep_NeighboringStep_SignsIn**: A theory over the offsets -1 and 1; the code of the step before or
after the current one signs in. This scenario is tested by `TotpSignIn_CodeStep_NeighboringStep_SignsIn`.

**TotpSignIn_CodeStep_EveryKindOfFailure_GivesTheSameAnswerAndNoSession**: One sign-in used the code of this step; a
second browser, past the password step, posts that used code, a wrong code, the code of a step five steps away, and
`abcdef` (four failures, one short of the lockout). Every answer is the same redirect to `/login/totp?error=1` and no
session is issued. This scenario is tested by
`TotpSignIn_CodeStep_EveryKindOfFailure_GivesTheSameAnswerAndNoSession`.

**TotpSignIn_CodeStep_MissingOrEmptyCode_IsAnsweredLikeAWrongCode**: A post without the code field and a post with an
empty code get the redirect of a wrong code and no session. This scenario is tested by
`TotpSignIn_CodeStep_MissingOrEmptyCode_IsAnsweredLikeAWrongCode`.

**TotpSignIn_CodeStep_RecoveryCode_WorksOnce**: A recovery code signs in once; used in a second sign-in it gets the
redirect of a wrong code and no session. The audit trail holds `auth.totp-recovery-used` and the text for a sign-in with
a recovery code. This scenario is tested by `TotpSignIn_CodeStep_RecoveryCode_WorksOnce`.

**TotpSignIn_CodeStep_CodeUsedBefore_IsRefusedAndTheNextStepWorks**: A sign-in spends the code of the current step; a
new sign-in with the same code is refused and keeps its challenge; after the clock moves one step the code of the next
step signs in. This scenario is tested by `TotpSignIn_CodeStep_CodeUsedBefore_IsRefusedAndTheNextStepWorks`.

**TotpSignIn_CodeStep_NoChallenge_RedirectsToLoginAndIssuesNothing**: A browser that never did the password step posts
a right code; it is sent to `/login?error=totp-expired` and gets no session. This scenario is tested by
`TotpSignIn_CodeStep_NoChallenge_RedirectsToLoginAndIssuesNothing`.

**TotpSignIn_CodeStep_ForgedChallengeCookie_IsRefused**: A request with a made-up challenge cookie and a right code gets
the expired redirect and no session. This scenario is tested by `TotpSignIn_CodeStep_ForgedChallengeCookie_IsRefused`.

**TotpSignIn_CodeStep_ChallengeFromAnotherAddress_IsRefusedAndNotConsumed**: The challenge cookie is presented from
another client address with a right code; the answer is the expired redirect with no session, and the challenge is
still pending in the store. This scenario is tested by
`TotpSignIn_CodeStep_ChallengeFromAnotherAddress_IsRefusedAndNotConsumed`.

**TotpSignIn_CodeStep_ExpiredChallenge_IsRefused**: The clock moves past the lifetime of the challenge and a right code
is posted; the answer is the expired redirect with no session. This scenario is tested by
`TotpSignIn_CodeStep_ExpiredChallenge_IsRefused`.

**TotpSignIn_CodeStep_ChallengeAfterSuccess_CannotBeUsedAgain**: After a complete sign-in the old challenge cookie is
sent by another browser with the code of the next step; the answer is the expired redirect with no session. This
scenario is tested by `TotpSignIn_CodeStep_ChallengeAfterSuccess_CannotBeUsedAgain`.

**TotpSignIn_CodeStep_WithoutAntiforgeryToken_IsRefused**: A right code is posted without the antiforgery token; the
answer is 400 and no session is issued. This scenario is tested by
`TotpSignIn_CodeStep_WithoutAntiforgeryToken_IsRefused`.

**TotpSignIn_CodeStep_FiveWrongCodes_LockTheAccount**: Five wrong codes are posted; a later sign-in with the right
password is answered like a wrong password and sets no challenge, and a right code posted in that browser gets the
expired redirect. No session exists in either browser. This scenario is tested by
`TotpSignIn_CodeStep_FiveWrongCodes_LockTheAccount`.

**TotpSignIn_CodeStep_PasswordAndCodeFailures_ShareOneCounter**: Three wrong passwords, a right password (which goes
to the code page and does not clear the counter), and two wrong codes lock the account; the next password step is
answered like a wrong password. This scenario is tested by
`TotpSignIn_CodeStep_PasswordAndCodeFailures_ShareOneCounter`.

**TotpSignIn_CodeStep_Success_ResetsTheFailureCounter**: Four wrong codes and then a right one sign in; after another
step four more wrong codes and a right one sign in again, so the counter started from zero after the success. This
scenario is tested by `TotpSignIn_CodeStep_Success_ResetsTheFailureCounter`.

**TotpSignIn_CodeStep_TooManyAttempts_AreThrottledEvenForARightCode**: With three attempts allowed per name, wrong codes
get the wrong-code redirect and then the `error=throttled` redirect, and a right code after that gets the throttled
answer too, with no session. This scenario is tested by `TotpSignIn_CodeStep_TooManyAttempts_AreThrottledEvenForARightCode`.

**TotpSignIn_PasswordStep_CorrectPassword_DoesNotUseUpTheAddressQuota**: With an address limit of two, three enrolled
accounts pass the password step from one address; none is throttled. This scenario is tested by
`TotpSignIn_PasswordStep_CorrectPassword_DoesNotUseUpTheAddressQuota`.

**TotpSignIn_CodeStep_AfterAdministratorReset_ChallengeIsDead**: After the password step an administrator resets the
second factor; the code posted afterwards gets the expired redirect and no session. This scenario is tested by
`TotpSignIn_CodeStep_AfterAdministratorReset_ChallengeIsDead`.

**TotpSignIn_PasswordStep_AfterAdministratorReset_SignsInWithPasswordOnly**: After the reset the account signs in with
the password alone: a session and no challenge. This scenario is tested by
`TotpSignIn_PasswordStep_AfterAdministratorReset_SignsInWithPasswordOnly`.

**TotpSignIn_FeatureDisabled_NoEndpointAndPasswordOnlySignIn**: An account is enrolled while the feature is on; the same
database is then served with the feature off. The password signs in with a session and no challenge, and a post to the
code endpoint answers 404. This scenario is tested by `TotpSignIn_FeatureDisabled_NoEndpointAndPasswordOnlySignIn`.

**TotpSignIn_PersonalAccessToken_EnrolledAccount_IsUnaffected**: A personal access token of an enrolled account calls the
API without a code and is accepted. This scenario is tested by
`TotpSignIn_PersonalAccessToken_EnrolledAccount_IsUnaffected`.

**TotpSignIn_FullLifeCycle_NeverRecordsSecretsCodesOrTheChallenge**: An enrolled account sees a wrong code, a right
code, a sign-in with a recovery code, and a reset by an administrator; the audit trail and every log entry hold none of
the secret (in Base32 or hexadecimal), the setup address, the recovery codes (with or without hyphens), the typed
codes, and the challenge token, but do hold `auth.totp-failed` and `auth.totp-reset`. This scenario is tested by
`TotpSignIn_FullLifeCycle_NeverRecordsSecretsCodesOrTheChallenge`.

**TotpOptions_Defaults_AreEnabledWithTheProductIssuer**: A new settings object is on and has the issuer
`ReviewCenter`, which equals the default of the service settings. This scenario is tested by
`TotpOptions_Defaults_AreEnabledWithTheProductIssuer`.

**TotpOptionsValidator_Validate_ValidIssuer_Succeeds**: A theory over `ReviewCenter`, `Acme Review Center`, and a one
character issuer; each is accepted. This scenario is tested by `TotpOptionsValidator_Validate_ValidIssuer_Succeeds`.

**TotpOptionsValidator_Validate_InvalidIssuer_FailsAndNamesTheSetting**: A theory over an empty issuer, white space, a
colon, and a control character; each fails with a message that names `Identity:Totp:Issuer` and does not echo the value.
This scenario is tested by `TotpOptionsValidator_Validate_InvalidIssuer_FailsAndNamesTheSetting`.

**TotpOptionsValidator_Validate_TooLongIssuer_Fails**: An issuer one character over the maximum length fails. This
scenario is tested by `TotpOptionsValidator_Validate_TooLongIssuer_Fails`. The refusal of invalid settings at start-up
is verified by the `Hosting` unit test `ServerHost_CreateApplication_InvalidTotpSettings_ThrowsClearError`.
