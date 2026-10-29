### TotpService

#### Verification Approach

`TotpService` is verified with unit tests defined in `TotpServiceTests.cs`. The tests build the real `TotpService`,
`UserService`, `TotpAlgorithm`, and `TotpChallengeStore` in `TotpTestWorld` over in-memory stores (`FakeTotpStore`,
`FakeUserStore`, and an in-memory audit store), a movable clock, and a reversible test protector, so the logic of the
service is verified without a database. The expected codes are never taken from the code under test: `TotpOracle`
is an independent implementation of HOTP and TOTP built on the HMAC-SHA1 of `System.Security.Cryptography`, checked
against the published RFC 4226 and RFC 6238 vectors, and it computes every code the tests type in. Replay and
compare-and-set protection is verified with parallel requests for the same code. The real database behavior of the
same operations is verified by the `TotpStore` tests, and the pages that call the service are verified by the
`AccountPage`, `UsersPage`, `ReviewPage`, and `TotpLoginPage` tests.

#### Test Environment

N/A - standard test environment. The tests need no database, no files, and no network.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Starting an enrollment needs the current password and returns the secret and an `otpauth` address, but stores only
  a pending enrollment that is not active; starting again replaces the pending secret.
- The accounts that can not use the feature (API token actors, external accounts, accounts that must change the
  password, and every account when the feature is switched off) are refused with `TotpNotAvailable`, and an active
  enrollment is refused with `TotpAlreadyEnrolled`.
- Confirming with the right code turns the second factor on and returns ten distinct recovery codes stored only as
  hashes; a wrong code leaves the enrollment pending and counts as a failure; an old pending enrollment is refused
  with `TotpEnrollmentExpired`; nothing started is refused with `TotpNotEnrolled`.
- A code is accepted within one step before or after the current step, never at or before the last accepted step,
  and only once even when many requests present it at the same time.
- A wrong, reused, malformed, or missing code gives the same failed result, which carries no user.
- A recovery code works once, in any usual spelling, only for its own user, and its use is audited without the code.
- Wrong codes and wrong passwords share one failure counter, five failures lock the account, and only a complete
  sign-in clears the counter.
- A pending sign-in works once, only from the client address it was issued to, and while it lives; an attempt from
  another address does not spend it.
- A switched-off feature makes an existing enrollment inert, and an unreadable stored secret refuses codes but not
  recovery codes.
- Removing the second factor, and regenerating recovery codes, need the password and a code; a recovery code is
  accepted for removal but not for regeneration; both are audited, and removal ends pending sign-ins.
- Only an enabled system administrator who is not an API token actor can reset the second factor of another user;
  the reset is audited with the administrator and the target and ends pending sign-ins.
- Neither a secret, a code, nor a recovery code reaches an audit entry or an error message.
- Changing the own password with a second factor on needs the current password and a fresh code or an unused
  recovery code, enforced in Core: a missing, wrong, or reused code is refused, counts on the shared counter, and
  leaves the hash and the counter as they were; the counter is cleared only after the whole proof succeeded; an API
  token actor is refused; users without a second factor, with a temporary password, or with the feature off need no
  code.
- A challenge whose user is invalidated while its code is checked is not given back.

#### Test Scenarios

**TotpService_BeginEnrollmentAsync_ValidPassword_ReturnsSecretAndUriButIsNotActive**: A user starts an enrollment with
the right password. The result holds a Base32 secret and an `otpauth` address with the secret and the issuer, the
status is available but not enrolled, and the password alone still signs in. This scenario is tested by
`TotpService_BeginEnrollmentAsync_ValidPassword_ReturnsSecretAndUriButIsNotActive`.

**TotpService_BeginEnrollmentAsync_Twice_ReplacesThePendingSecret**: Two enrollments are started in a row. The secrets
differ and a code of the first secret no longer confirms. This scenario is tested by
`TotpService_BeginEnrollmentAsync_Twice_ReplacesThePendingSecret`.

**TotpService_BeginEnrollmentAsync_WrongPassword_ThrowsAndStoresNothing**: A wrong password raises
`CurrentPasswordIncorrect` and no row exists. This scenario is tested by
`TotpService_BeginEnrollmentAsync_WrongPassword_ThrowsAndStoresNothing`.

**TotpService_BeginEnrollmentAsync_UnavailableAccount_ThrowsTotpNotAvailable**: A theory over an API token actor, an
external account, and an account that must change its password; each raises `TotpNotAvailable`. This scenario is
tested by `TotpService_BeginEnrollmentAsync_UnavailableAccount_ThrowsTotpNotAvailable`.

**TotpService_BeginEnrollmentAsync_FeatureDisabled_ThrowsTotpNotAvailable**: With the feature switched off, starting
raises `TotpNotAvailable` and the status reports the feature as off and not available. This scenario is tested by
`TotpService_BeginEnrollmentAsync_FeatureDisabled_ThrowsTotpNotAvailable`.

**TotpService_BeginEnrollmentAsync_AlreadyEnrolled_ThrowsTotpAlreadyEnrolled**: An enrolled user starts again and
`TotpAlreadyEnrolled` is raised. This scenario is tested by
`TotpService_BeginEnrollmentAsync_AlreadyEnrolled_ThrowsTotpAlreadyEnrolled`.

**TotpService_ConfirmEnrollmentAsync_ValidCode_ActivatesAndIssuesTenRecoveryCodes**: A complete enrollment returns ten
distinct recovery codes of the shown form, stores only their hashes, writes one audit entry, reports ten codes left,
and makes the password step ask for the second factor. This scenario is tested by
`TotpService_ConfirmEnrollmentAsync_ValidCode_ActivatesAndIssuesTenRecoveryCodes`.

**TotpService_ConfirmEnrollmentAsync_WrongCode_ThrowsAndStaysPending**: A wrong and a malformed code raise
`TotpCodeIncorrect` with the same message, the enrollment stays pending, two failures are counted, and the right code
still confirms. This scenario is tested by `TotpService_ConfirmEnrollmentAsync_WrongCode_ThrowsAndStaysPending`.

**TotpService_ConfirmEnrollmentAsync_AfterFifteenMinutes_ThrowsTotpEnrollmentExpired**: A started enrollment waits
sixteen minutes; even a code that is right now raises `TotpEnrollmentExpired`. This scenario is tested by
`TotpService_ConfirmEnrollmentAsync_AfterFifteenMinutes_ThrowsTotpEnrollmentExpired`.

**TotpService_ConfirmEnrollmentAsync_NothingStarted_ThrowsTotpNotEnrolled**: Confirming without a started enrollment
raises `TotpNotEnrolled`. This scenario is tested by
`TotpService_ConfirmEnrollmentAsync_NothingStarted_ThrowsTotpNotEnrolled`.

**TotpService_CancelEnrollmentAsync_PendingOnly_RemovesPendingKeepsConfirmed**: Cancelling removes a pending
enrollment and leaves a confirmed one active. This scenario is tested by
`TotpService_CancelEnrollmentAsync_PendingOnly_RemovesPendingKeepsConfirmed`.

**TotpService_CompleteSignInAsync_CodeOfTheEnrollment_IsAReplay**: The code that confirmed the enrollment fails at
sign-in, and the code of the next step signs in. This scenario is tested by
`TotpService_CompleteSignInAsync_CodeOfTheEnrollment_IsAReplay`.

**TotpService_CompleteSignInAsync_WindowAndOrder_AcceptsPlusMinusOneStepOnlyOnceForward**: Codes two steps away are
refused, a code one step behind is accepted once, the current step is accepted, and an older step is refused
afterwards. This scenario is tested by
`TotpService_CompleteSignInAsync_WindowAndOrder_AcceptsPlusMinusOneStepOnlyOnceForward`.

**TotpService_CompleteSignInAsync_WrongReusedMalformed_AreIndistinguishable**: A reused code, a wrong code, a short
code, text, a long code, and no code each give exactly the same failed result without a user. This scenario is tested
by `TotpService_CompleteSignInAsync_WrongReusedMalformed_AreIndistinguishable`.

**TotpService_CompleteSignInAsync_ConcurrentSameCode_ExactlyOneSucceeds**: Several pending sign-ins of one user present
the same code in parallel; exactly one succeeds and the step moves forward once. This scenario is tested by
`TotpService_CompleteSignInAsync_ConcurrentSameCode_ExactlyOneSucceeds`.

**TotpService_CompleteSignInAsync_RecoveryCode_WorksOnceAndIsAudited**: A recovery code in lower case with spaces signs
in with nine codes left, a second use fails, and the audit entry does not hold the code. This scenario is tested by
`TotpService_CompleteSignInAsync_RecoveryCode_WorksOnceAndIsAudited`.

**TotpService_CompleteSignInAsync_ConcurrentSameRecoveryCode_ExactlyOneSucceeds**: Several pending sign-ins present the
same recovery code in parallel; exactly one succeeds and nine codes are left. This scenario is tested by
`TotpService_CompleteSignInAsync_ConcurrentSameRecoveryCode_ExactlyOneSucceeds`.

**TotpService_CompleteSignInAsync_RecoveryCodeOfAnotherUser_IsRefused**: One user presents the recovery code of
another user and is refused. This scenario is tested by
`TotpService_CompleteSignInAsync_RecoveryCodeOfAnotherUser_IsRefused`.

**TotpService_CompleteSignInAsync_FiveWrongCodes_LocksTheAccount**: Four wrong codes fail, the fifth locks the account,
the right code is refused while locked and works after the lockout; five failures and the lockout are audited. This
scenario is tested by `TotpService_CompleteSignInAsync_FiveWrongCodes_LocksTheAccount`.

**TotpService_CompleteSignInAsync_PasswordAndCodeFailures_ShareOneCounter**: Three wrong passwords and two wrong codes
lock the account on the second code. This scenario is tested by
`TotpService_CompleteSignInAsync_PasswordAndCodeFailures_ShareOneCounter`.

**TotpService_CompleteSignInAsync_RightPasswordBetweenWrongCodes_DoesNotResetTheCounter**: After four wrong codes a right
password only begins the second step and the next wrong code still locks the account. This scenario is tested by
`TotpService_CompleteSignInAsync_RightPasswordBetweenWrongCodes_DoesNotResetTheCounter`.

**TotpService_CompleteSignInAsync_Success_ClearsTheCounter**: A complete sign-in after four failures clears the counter,
so four more failures do not lock. This scenario is tested by
`TotpService_CompleteSignInAsync_Success_ClearsTheCounter`.

**TotpService_CompleteSignInAsync_BadChallenge_IsChallengeInvalid**: A theory over a missing, garbage, foreign-client,
used, expired, and user-disabled challenge; none signs in and none carries a user. This scenario is tested by
`TotpService_CompleteSignInAsync_BadChallenge_IsChallengeInvalid`.

**TotpService_CompleteSignInAsync_ForeignClient_DoesNotSpendTheChallenge**: Another address tries first and gets
`ChallengeInvalid`; the owner address then still signs in. This scenario is tested by
`TotpService_CompleteSignInAsync_ForeignClient_DoesNotSpendTheChallenge`.

**TotpService_CompleteSignInAsync_FeatureDisabled_IsChallengeInvalidAndPasswordAloneSignsIn**: With the feature off and
an enrollment row present, a challenge can not be completed, the password alone signs in, and the account is not
reported as enrolled. This scenario is tested by
`TotpService_CompleteSignInAsync_FeatureDisabled_IsChallengeInvalidAndPasswordAloneSignsIn`.

**TotpService_CompleteSignInAsync_UnreadableSecret_RefusesCodesButRecoveryStillWorks**: With the protector unable to
read the secret, the right code fails and a recovery code signs in. This scenario is tested by
`TotpService_CompleteSignInAsync_UnreadableSecret_RefusesCodesButRecoveryStillWorks`.

**TotpService_DisableAsync_PasswordAndCode_RemovesAndInvalidatesPendingSignIns**: A wrong password and a wrong code are
refused; the right ones remove the enrollment with an audit entry, end the pending sign-in, and let the password alone
sign in. This scenario is tested by `TotpService_DisableAsync_PasswordAndCode_RemovesAndInvalidatesPendingSignIns`.

**TotpService_DisableAsync_RecoveryCode_Removes**: A recovery code removes the enrollment and its use is audited. This
scenario is tested by `TotpService_DisableAsync_RecoveryCode_Removes`.

**TotpService_DisableAsync_NotEnrolled_ThrowsTotpNotEnrolled**: Removing without a second factor raises
`TotpNotEnrolled`. This scenario is tested by `TotpService_DisableAsync_NotEnrolled_ThrowsTotpNotEnrolled`.

**TotpService_RegenerateRecoveryCodesAsync_PasswordAndCode_ReplacesTheCodes**: A recovery code is refused for
regeneration; a code issues ten new codes, the old codes stop working, a new code works, and the regeneration is
audited. This scenario is tested by `TotpService_RegenerateRecoveryCodesAsync_PasswordAndCode_ReplacesTheCodes`.

**TotpService_ResetAsync_SystemAdministrator_RemovesAndInvalidatesPendingSignIns**: A system administrator resets a
user; the enrollment is removed, the audit entry names the administrator and the target, and the pending sign-in ends.
This scenario is tested by `TotpService_ResetAsync_SystemAdministrator_RemovesAndInvalidatesPendingSignIns`.

**TotpService_ResetAsync_NotASystemAdministrator_ThrowsActorNotAllowed**: A theory over an ordinary user, a project
administrator, the target itself, an API token administrator, a disabled administrator, and an unknown caller; each
raises `ActorNotAllowed`. This scenario is tested by
`TotpService_ResetAsync_NotASystemAdministrator_ThrowsActorNotAllowed`.

**TotpService_ResetAsync_UnknownOrNotEnrolled_Throws**: Resetting an unknown user or a user without a second factor is
refused. This scenario is tested by `TotpService_ResetAsync_UnknownOrNotEnrolled_Throws`.

**TotpService_ListEnrolledUserIdsAsync_HoldsConfirmedEnrollmentsOnly**: Only the user with a confirmed enrollment is
listed; a pending one and a plain one are not. This scenario is tested by
`TotpService_ListEnrolledUserIdsAsync_HoldsConfirmedEnrollmentsOnly`.

**TotpService_WholeLifeCycle_NeverPutsSecretsOrCodesInAuditOrErrors**: A user enrolls, fails, signs in with a code and a
recovery code, regenerates, and is removed; no audit entry and no error message holds a secret, a code, or a recovery
code. This scenario is tested by `TotpService_WholeLifeCycle_NeverPutsSecretsOrCodesInAuditOrErrors`.

**TotpService_GetStatusAsync_ApiTokenActor_IsNotAvailable**: The status for an API token actor reports the feature as
not available. This scenario is tested by `TotpService_GetStatusAsync_ApiTokenActor_IsNotAvailable`.

**TotpService_NullArguments_Throw**: Null arguments raise `ArgumentNullException`. This scenario is tested by
`TotpService_NullArguments_Throw`.

**TotpService_CompleteSignInAsync_UserInvalidatedWhileCodeIsChecked_DoesNotResurrectTheChallenge**: The user is
invalidated while a wrong code is being checked (a hook of the in-memory store runs at that moment, so nothing waits);
the challenge is not given back and a later try with it finds nothing. This scenario is tested by
`TotpService_CompleteSignInAsync_UserInvalidatedWhileCodeIsChecked_DoesNotResurrectTheChallenge`.

**TotpService_ChangeOwnPasswordAsync_NoCode_IsRefusedCountsAndKeepsTheHash**: A theory over a null, an empty, and a
blank code; for an enrolled user the right password alone is refused with `TotpCodeIncorrect`, the stored hash is
unchanged, and the failure counter is not cleared. This scenario is tested by
`TotpService_ChangeOwnPasswordAsync_NoCode_IsRefusedCountsAndKeepsTheHash`.

**TotpService_ChangeOwnPasswordAsync_WrongCodeOrWrongPassword_IsRefusedAndKeepsTheHash**: A wrong code raises
`TotpCodeIncorrect` and counts as a failure; a wrong password raises `CurrentPasswordIncorrect`; neither changes the
hash. This scenario is tested by
`TotpService_ChangeOwnPasswordAsync_WrongCodeOrWrongPassword_IsRefusedAndKeepsTheHash`.

**TotpService_ChangeOwnPasswordAsync_ReusedCode_IsRefused**: The code of a step that was already accepted is refused
and the hash is unchanged. This scenario is tested by `TotpService_ChangeOwnPasswordAsync_ReusedCode_IsRefused`.

**TotpService_ChangeOwnPasswordAsync_PasswordAndValidCode_ChangesAndClearsTheCounter**: The right password and a fresh
code change the password, clear the failure counter, and the new password works. This scenario is tested by
`TotpService_ChangeOwnPasswordAsync_PasswordAndValidCode_ChangesAndClearsTheCounter`.

**TotpService_ChangeOwnPasswordAsync_RecoveryCode_WorksOnceAndIsAudited**: A recovery code changes the password once;
its use is audited without the code, and a second use is refused. This scenario is tested by
`TotpService_ChangeOwnPasswordAsync_RecoveryCode_WorksOnceAndIsAudited`.

**TotpService_ChangeOwnPasswordAsync_FiveWrongCodes_LockTheAccount**: Five wrong codes lock the account, and a sixth
try with the right code is refused while it is locked. This scenario is tested by
`TotpService_ChangeOwnPasswordAsync_FiveWrongCodes_LockTheAccount`.

**TotpService_ChangeOwnPasswordAsync_PasswordOnlyBetweenWrongCodes_DoesNotResetTheCounter**: Password-only attempts
between wrong codes do not reset the shared counter, so the account is locked after five failures in total. This
scenario is tested by `TotpService_ChangeOwnPasswordAsync_PasswordOnlyBetweenWrongCodes_DoesNotResetTheCounter`.

**TotpService_ChangeOwnPasswordAsync_UnreadableSecret_RefusesCodesButRecoveryStillWorks**: With the protector unable
to read the secret, a code is refused and a recovery code changes the password. This scenario is tested by
`TotpService_ChangeOwnPasswordAsync_UnreadableSecret_RefusesCodesButRecoveryStillWorks`.

**TotpService_ChangeOwnPasswordAsync_ApiTokenActor_ThrowsActorNotAllowed**: An API token actor is refused with
`ActorNotAllowed` and nothing changes. This scenario is tested by
`TotpService_ChangeOwnPasswordAsync_ApiTokenActor_ThrowsActorNotAllowed`.

**TotpService_ChangeOwnPasswordAsync_NotEnrolledOrFeatureOff_PasswordAloneWorks**: A theory over a user without a
second factor and a user with one while the feature is switched off; the password alone changes the password. This
scenario is tested by `TotpService_ChangeOwnPasswordAsync_NotEnrolledOrFeatureOff_PasswordAloneWorks`.

**TotpService_ChangeOwnPasswordAsync_TemporaryPassword_NeedsNoCode**: A user with a temporary password changes it with
the password alone. This scenario is tested by `TotpService_ChangeOwnPasswordAsync_TemporaryPassword_NeedsNoCode`.

**TotpService_ChangeOwnPasswordAsync_ConcurrentSameCode_ExactlyOneSucceeds**: Several changes with the same code are
released together; exactly one succeeds. This scenario is tested by
`TotpService_ChangeOwnPasswordAsync_ConcurrentSameCode_ExactlyOneSucceeds`.
