### OidcSignIn

#### Verification Approach

`OidcSignIn` is verified with tests in `OidcSignInTests.cs`, `OidcReauthTests.cs` and `OidcOptionsTests.cs`. The
settings and the validator are checked directly. The flows are checked end to end: the real application starts in
memory with its real OpenID Connect handler (`OidcTestApp`) and talks over loopback http to a fake provider
(`FakeIdentityProvider`) built on OpenIddict, which serves discovery, keys and the authorization and token endpoints and
signs real identity tokens. A scenario object lets a test make the provider send an error, refuse the code, or send
another subject, an old or missing `auth_time`, or an unverified e-mail address. The pure check `VerifyReauth` is also
called directly. A real database and the real `UserService`, `SigningService` and `SignInThrottle` are used; nothing in
the unit is mocked. See the OTS verification of OpenIdConnect and OpenIddict for the packages.

#### Test Environment

N/A - standard test environment. The tests start the application and the fake provider in the test process; they need
no external service and no network beyond the loopback address.

#### Acceptance Criteria

- All tests pass with zero failures.
- A sign-in uses the code flow with PKCE, creates a plain account (no project, no administrator right, no password) on
  the first sign-in, finds it again by issuer and subject, never links to a local account, and sanitizes names.
- A failed state, a refused code, a provider error, an unverified e-mail address when required, a disabled account and
  too many callbacks end at the login page with one message and no session; the audit trail has the reason class only.
- Foreign return addresses are replaced by a local one; sign-out is local only; no secret reaches the log or the trail
  (the framework handler's own log may name the issuer and audience of a refused token, nothing else).
- Both forms that start a flow are refused without a valid antiforgery token and the provider is not called.
- A password sign-in of an account of the provider is refused.
- Settings are validated at start-up with messages that name the setting and never a value (also through the
  `ServerHost` tests); nothing is mapped when sign-in with a provider is off.
- The client secret may be given in a file (`ClientSecretFile`): the trimmed contents are used, both forms at once are
  refused, and a missing, empty, or oversized file stops the start with a message that names the setting and never the
  secret.
- A confirmation of identity asks for `prompt=login` and `max_age=0`, issues exactly one single-use proof for the
  right user only when `auth_time` (checked at the exact limits of its window), subject, issuer and the user of the
  session are right, and otherwise fails closed with a recorded reason.

#### Test Scenarios

**OidcSignIn_FirstSignIn_CreatesPlainAccountAndSignsIn**: A first sign-in creates an enabled account with the issuer and
subject, no password, no administrator right and no project, sets an HttpOnly session cookie and returns to the
requested local address. This scenario is tested by `OidcSignIn_FirstSignIn_CreatesPlainAccountAndSignsIn`.

**OidcSignIn_FirstSignIn_RecordsCreationAndSignIn**: The sign-in and the creation of the account are recorded with the
host of the issuer only. This scenario is tested by `OidcSignIn_FirstSignIn_RecordsCreationAndSignIn`.

**OidcSignIn_SecondSignIn_UsesTheSameAccount**: A second sign-in with a changed name and e-mail address uses the same
account and creates none. This scenario is tested by `OidcSignIn_SecondSignIn_UsesTheSameAccount`.

**OidcSignIn_OtherSubjectSameName_CreatesSeparateAccountWithUniqueName**: Another subject that proposes the same user
name gets an account of its own with a numbered name. This scenario is tested by
`OidcSignIn_OtherSubjectSameName_CreatesSeparateAccountWithUniqueName`.

**OidcSignIn_NameOfLocalAccount_NeverLinksToIt**: A person who proposes the name of a local administrator gets a new
plain account and the local account is untouched. This scenario is tested by
`OidcSignIn_NameOfLocalAccount_NeverLinksToIt`.

**OidcSignIn_HostileNames_AreSanitized**: A user name derived from a hostile e-mail address and a display name with
control and bidirectional characters are reduced to safe text. This scenario is tested by
`OidcSignIn_HostileNames_AreSanitized`.

**OidcSignIn_DisabledAccount_IsRefusedLikeAnyFailure**: A disabled account is sent to the login page with the generic
message, gets no cookie, and the trail names the reason. This scenario is tested by
`OidcSignIn_DisabledAccount_IsRefusedLikeAnyFailure`.

**OidcSignIn_PasswordSignInOfExternalAccount_IsRefused**: No password, not even an empty one, signs in an account of the
provider. This scenario is tested by `OidcSignIn_PasswordSignInOfExternalAccount_IsRefused`.

**OidcSignIn_ProviderRefuses_ShowsFailureAndRecordsReasonClass**: An error answer of the provider ends at the login page
without an account, with the reason class `provider-error` recorded. This scenario is tested by
`OidcSignIn_ProviderRefuses_ShowsFailureAndRecordsReasonClass`.

**OidcSignIn_ProviderRefusesTheCode_EndsWithoutSession**: A refused code exchange ends at the login page without a
session and is recorded. This scenario is tested by `OidcSignIn_ProviderRefusesTheCode_EndsWithoutSession`.

**OidcSignIn_TamperedState_IsRefused**: An answer with a forged state is refused with the reason class
`state-or-nonce`. This scenario is tested by `OidcSignIn_TamperedState_IsRefused`.

**OidcSignIn_WrongIssuerAudienceOrNonce_FailsClosed**: A validly signed identity token with a wrong issuer, audience or
nonce is refused with the reason class `token-invalid` or `state-or-nonce`, sets no cookie, creates no account, and no
token value reaches the log or the trail. This scenario is tested by
`OidcSignIn_WrongIssuerAudienceOrNonce_FailsClosed`.

**OidcSignIn_AccountDisabledWhileSessionIsOpen_EndsTheSession**: An account that is disabled after the sign-in loses
its open session at the next request. This scenario is tested by
`OidcSignIn_AccountDisabledWhileSessionIsOpen_EndsTheSession`.

**OidcSignIn_UnverifiedEmail_IsRefusedOnlyWhenRequired**: An unverified e-mail address is refused when the setting
asks for a verified one and accepted otherwise. This scenario is tested by
`OidcSignIn_UnverifiedEmail_IsRefusedOnlyWhenRequired`.

**OidcSignIn_TooManyCallbacks_AreThrottled**: After the callbacks allowed for one client address by the throttle, a
correct sign-in is refused too, creates no account and is recorded as `throttled`. This scenario is tested by
`OidcSignIn_TooManyCallbacks_AreThrottled`.

**OidcSignIn_BadCallbacksBelowAddressLimit_DoNotBlockAnotherPerson**: With a low limit per name and a higher limit per
address, more bad callbacks than the limit per name come from one address and are refused as before; another browser
from the same address then signs in correctly, and no `throttled` reason is recorded, because the callback counts the
address only and has no counter per name. This scenario is tested by
`OidcSignIn_BadCallbacksBelowAddressLimit_DoNotBlockAnotherPerson`.

**OidcSignIn_SuccessfulCallback_HandsTheAddressSlotBack**: With an address limit of 2 and one refused callback already
counted, two successful sign-ins in a row from the same address are both accepted, because each accepted callback
hands its attempt back. This scenario is tested by `OidcSignIn_SuccessfulCallback_HandsTheAddressSlotBack`.

**OidcSignIn_ForeignReturnUrl_IsReplacedByHome**: Absolute, protocol-relative, backslash and `javascript:` return
addresses are replaced by the home page. This scenario is tested by `OidcSignIn_ForeignReturnUrl_IsReplacedByHome`.

**OidcSignIn_Disabled_EndpointsAreNotMappedAndNoButtonIsShown**: With the provider off, the endpoints and the callback
answer 404 and the login page shows no button. This scenario is tested by
`OidcSignIn_Disabled_EndpointsAreNotMappedAndNoButtonIsShown`.

**OidcSignIn_Enabled_SignInPageOffersButtonBesideLocalForm**: With the provider on, the login page shows the button with
the display name beside the local form, the generic message, and the local return address. This scenario is tested by
`OidcSignIn_Enabled_SignInPageOffersButtonBesideLocalForm`.

**OidcSignIn_Logout_IsLocalOnly**: Signing out ends the session and the provider sees no request. This scenario is
tested by `OidcSignIn_Logout_IsLocalOnly`.

**OidcSignIn_Flow_NeverLogsSecretsAndUsesCodeFlowWithPkce**: The authorization request asks for a code with an S256
challenge, a non-empty state and a non-empty nonce, and no prompt, and neither the client secret nor the code appears in
the log or in the trail. This scenario is tested by `OidcSignIn_Flow_NeverLogsSecretsAndUsesCodeFlowWithPkce`.

**OidcSignIn_PostWithoutValidAntiforgeryToken_IsRefusedAndProviderIsNotCalled**: A post to the sign-in form without an
antiforgery token, and one with a token that is not valid, is answered with 400, the provider sees no request, no cookie
is set and nobody is signed in. This scenario is tested by
`OidcSignIn_PostWithoutValidAntiforgeryToken_IsRefusedAndProviderIsNotCalled`.

**OidcOptionsValidator_Validate_ValidSettings_Succeeds**: Valid settings and settings at every limit are accepted. This
scenario is tested by `OidcOptionsValidator_Validate_ValidSettings_Succeeds`.

**OidcOptionsValidator_Validate_Disabled_SucceedsWhateverTheValues**: Nothing is checked while sign-in with a provider
is off. This scenario is tested by `OidcOptionsValidator_Validate_Disabled_SucceedsWhateverTheValues`.

**OidcOptionsValidator_Validate_InvalidSetting_FailsNamingTheSettingWithoutValues**: Each invalid setting fails with a
message that names it and holds neither its value nor the secret. This scenario is tested by
`OidcOptionsValidator_Validate_InvalidSetting_FailsNamingTheSettingWithoutValues`.

**OidcOptionsValidator_Validate_MissingClientSecret_FailsNamingTheSetting**: With sign-in on, a client secret that is
null, empty, white space or a tab, with no secret file either, fails with a message that names
`Identity:Oidc:ClientSecret` and `Identity:Oidc:ClientSecretFile` and says one is required; a public client without a
secret is not accepted. This scenario is tested by
`OidcOptionsValidator_Validate_MissingClientSecret_FailsNamingTheSetting`.

**OidcOptionsValidator_Validate_ClientSecretFile_SucceedsAndResolvesTheTrimmedSecret**: With `ClientSecretFile` naming a
file in a temporary directory whose contents end in a line break, the settings are valid and the resolved secret is the
trimmed contents; the inline setting stays empty. This scenario is tested by
`OidcOptionsValidator_Validate_ClientSecretFile_SucceedsAndResolvesTheTrimmedSecret`.

**OidcOptionsValidator_Validate_ClientSecretAndFileBothSet_FailsNamingBothSettings**: With both `ClientSecret` and
`ClientSecretFile` set, the settings are refused with a message that names both and holds neither secret. This
scenario is tested by `OidcOptionsValidator_Validate_ClientSecretAndFileBothSet_FailsNamingBothSettings`.

**OidcOptionsValidator_Validate_ClientSecretFileProblem_FailsNamingTheFileSettingWithoutTheSecret**: A secret file that
is missing, empty, larger than 8 KB, too long as a secret, or holds a control character fails with a message that names
`Identity:Oidc:ClientSecretFile` and does not contain the contents of the file. This scenario is tested by
`OidcOptionsValidator_Validate_ClientSecretFileProblem_FailsNamingTheFileSettingWithoutTheSecret`.

**ServerHost_CreateApplication_ClientSecretFileProblem_ThrowsClearErrorWithoutSecret**: With sign-in on, a missing
secret file, or a secret set both inline and in a file, stops the start of the application with a message that names
the settings and does not contain the secret. The test is in the `ServerHost` tests and is linked from the client
secret file requirement of this unit. This scenario is tested by
`ServerHost_CreateApplication_ClientSecretFileProblem_ThrowsClearErrorWithoutSecret`.

**OidcSignIn_ClientSecretFile_SignsInAndHandlerUsesTheTrimmedSecret**: With the secret in a file, a sign-in through the
fake provider succeeds and the configured handler of the framework holds the trimmed contents of the file as its client
secret. This scenario is tested by `OidcSignIn_ClientSecretFile_SignsInAndHandlerUsesTheTrimmedSecret`.

**OidcOptionsValidator_Validate_InvalidClientSecret_FailsWithoutEchoingTheSecret**: A secret that is too long or holds
a control character fails with a message that names the setting and does not contain the secret. This scenario is
tested by `OidcOptionsValidator_Validate_InvalidClientSecret_FailsWithoutEchoingTheSecret`.

**OidcOptionsValidator_Validate_ValuePastTheLimit_Fails**: A value one past each length limit is refused. This scenario
is tested by `OidcOptionsValidator_Validate_ValuePastTheLimit_Fails`.

**OidcOptionsValidator_Validate_TooManyOrTooLongScopes_Fails**: Too many scopes, or a scope that is too long, are
refused. This scenario is tested by `OidcOptionsValidator_Validate_TooManyOrTooLongScopes_Fails`.

**OidcOptionsValidator_Validate_ReauthMaxAgeSeconds_MustBeWithinBounds**: The age is accepted from 10 to 900 seconds and
refused outside. This scenario is tested by `OidcOptionsValidator_Validate_ReauthMaxAgeSeconds_MustBeWithinBounds`.

**OidcOptionsValidator_Validate_Scheme_HttpOnlyForLoopback**: https is accepted, and http only for `localhost`,
`127.0.0.1` and `::1`. This scenario is tested by `OidcOptionsValidator_Validate_Scheme_HttpOnlyForLoopback`.

**OidcOptionsValidator_Validate_Null_Throws**: A null settings object raises `ArgumentNullException`. This scenario is
tested by `OidcOptionsValidator_Validate_Null_Throws`.

**ServerHost_CreateApplication_InvalidOidcSettings_ThrowsClearErrorWithoutSecret**: With sign-in on, a wrong setting
stops the application from being created with a message that names the setting and holds neither its value nor the
secret. The test is in the `ServerHost` tests and is linked from the settings validation requirement of this unit. This
scenario is tested by `ServerHost_CreateApplication_InvalidOidcSettings_ThrowsClearErrorWithoutSecret`.

**ServerHost_CreateApplication_OidcOffOrValid_BuildsTheApplication**: With sign-in off, or with valid settings, the
application is created. This scenario is tested by `ServerHost_CreateApplication_OidcOffOrValid_BuildsTheApplication`.

**OidcReauth_MissingOrStaleAuthTime_ReviewPageNamesTheCause**: After a refused confirmation the review page names the
cause when the provider sent no usable `auth_time` (absent, or present but negative): the text says the provider did not
report a usable sign-in time, names `auth_time`, a whole number of seconds since 1970 and the administrator; and when the
sign-in was too old it says it was not recent enough and does not name `auth_time`. In neither case is the general text
"We could not confirm your identity" shown. This scenario is tested by
`OidcReauth_MissingOrStaleAuthTime_ReviewPageNamesTheCause`.

**OidcReauth_VerifyReauth_AuthTimeAtTheLimitsOfTheWindow_IsDecidedExactly**: With a fixed clock, an `auth_time` exactly
the allowed age ago and one exactly 60 seconds in the future are accepted, and one second more in either direction is
refused as `stale-auth-time`, for the default age and for the smallest (10) and the largest (900) age of the settings.
This scenario is tested by `OidcReauth_VerifyReauth_AuthTimeAtTheLimitsOfTheWindow_IsDecidedExactly`.

**OidcReauth_SessionUserDiffersFromConfirmedUser_IsRefusedAndGivesNoProof**: A confirmation that olive started is
answered by the provider with olive's identity after another user of the provider has signed in in the same browser; it
is refused with the reason class `other` recorded for the user of the session, no proof is issued, and the session is
not replaced. This scenario is tested by `OidcReauth_SessionUserDiffersFromConfirmedUser_IsRefusedAndGivesNoProof`.

**OidcReauth_PostWithoutValidAntiforgeryToken_IsRefusedAndProviderIsNotCalled**: A post to the confirmation form without
an antiforgery token, and one with a token that is not valid, from a signed-in user who may confirm the slot, is answered
with 400, the provider sees no request, no cookie is set, no proof is issued and nothing is recorded. This scenario is
tested by `OidcReauth_PostWithoutValidAntiforgeryToken_IsRefusedAndProviderIsNotCalled`.

**OidcReauth_FreshSignInOfSamePerson_AllowsExactlyOneSignature**: The provider is asked with `prompt=login` and
`max_age=0`, a proof is issued, one signature without a password uses it up, and the slot is signed. This scenario is
tested by `OidcReauth_FreshSignInOfSamePerson_AllowsExactlyOneSignature`.

**OidcReauth_Success_KeepsTheSession**: After a confirmation the person is still signed in as the same user. This
scenario is tested by `OidcReauth_Success_KeepsTheSession`.

**OidcReauth_AuthTimeNotInWindow_IsRefusedFailClosed**: An `auth_time` that is too old, in the future or missing is
refused with `stale-auth-time` or `missing-auth-time`, and no proof is issued; the person returns to the review with
`reauth=stale-auth-time` or `reauth=missing-auth-time` (not `reauth=failed`), while the recorded reason class is
unchanged. Each refusal is asserted against a fixed, literal expected value of the `reauth` query parameter that the
test supplies, and not against a value computed from the reason class, so a swapped or wrong mapping fails the test.
This scenario is tested by `OidcReauth_AuthTimeNotInWindow_IsRefusedFailClosed`.

**OidcReauth_AuthTimeOutsideTheRangeOfDates_IsRefusedWithoutError**: An `auth_time` of 9223372036854775807, of
-9223372036854775808, of -1, and of 253402300800 (one second past the last date that can be represented) is refused
with `missing-auth-time`, and 253402300799 (representable, but far in the future) with `stale-auth-time`. In each case
the person returns to the review with `reauth=missing-auth-time` or `reauth=stale-auth-time` respectively (fixed
literal values in the test), exactly one `signature.reauth-failed` entry holds the reason
class, no proof is issued, signing is refused, and the server raises no error (the account page still answers 200).
This scenario is tested by `OidcReauth_AuthTimeOutsideTheRangeOfDates_IsRefusedWithoutError`.

**OidcReauth_OtherSubject_IsRefusedAndCreatesNoAccount**: Another person who signs in at the provider gives no proof
(`subject-mismatch`), creates no account and leaves the session unchanged. This scenario is tested by
`OidcReauth_OtherSubject_IsRefusedAndCreatesNoAccount`.

**OidcReauth_ProviderRefuses_ReturnsToReviewWithoutProof**: An error of the provider returns to the review with
`reauth=failed` (a fixed literal in the test) and no proof. This scenario is tested by
`OidcReauth_ProviderRefuses_ReturnsToReviewWithoutProof`.

**OidcReauth_WrongIssuerAudienceOrNonce_IsRefusedFailClosed**: A validly signed identity token with a wrong issuer,
audience or nonce returns to the review with `reauth=failed`, gives no proof, creates no account, keeps the session,
records `signature.reauth-failed` with the reason class only, and no token value reaches the log or the trail. This
scenario is tested by `OidcReauth_WrongIssuerAudienceOrNonce_IsRefusedFailClosed`.

**OidcReauth_SessionEndedAtTheProvider_IsRefusedWithoutProof**: A confirmation whose session ended (sign-out, or the
account disabled) while the person was at the provider gives no proof, goes to the login page and records the reason
class `session-ended`. This scenario is tested by `OidcReauth_SessionEndedAtTheProvider_IsRefusedWithoutProof`.

**OidcReauth_SignWithoutProof_IsRefusedAndRecorded**: Signing without a confirmation is refused and recorded as
`signature.reauth-failed`. This scenario is tested by `OidcReauth_SignWithoutProof_IsRefusedAndRecorded`.

**OidcReauth_NotAllowed_IsNeverSentToTheProvider**: A visitor without a session, a local account, and a user who may not
sign the slot are not sent to the provider. This scenario is tested by `OidcReauth_NotAllowed_IsNeverSentToTheProvider`.

**OidcReauth_ForeignReturnUrl_ReturnsToLocalAddress**: A foreign return address is replaced by the home page after a
confirmation. This scenario is tested by `OidcReauth_ForeignReturnUrl_ReturnsToLocalAddress`.

**OidcReauth_ReviewPage_OffersConfirmationThenSigning**: The review page offers the confirmation button without a
password field, shows the failure after a refused one, and offers the sign button after a confirmed one. This scenario
is tested by `OidcReauth_ReviewPage_OffersConfirmationThenSigning`.

**OidcReauth_VerifyReauth_ComparesIssuerAndSubjectOfTheAccount**: The check returns `issuer-mismatch`,
`subject-mismatch` or `not-external`, and accepts the stored identity. This scenario is tested by
`OidcReauth_VerifyReauth_ComparesIssuerAndSubjectOfTheAccount`.

**OidcReauth_VerifyReauth_WrongBinding_IsRefusedAsOther**: A confirmation started for another user, without a binding,
or without claims is refused as `other`. This scenario is tested by `OidcReauth_VerifyReauth_WrongBinding_IsRefusedAsOther`.
