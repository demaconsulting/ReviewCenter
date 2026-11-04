### UserService

#### Verification Approach

`UserService` is verified with unit tests defined in `UserServiceTests.cs`, `UserServiceTotpTests.cs`,
`UserServiceExternalTests.cs`, `MustChangePasswordTests.cs`, and `AuditEventTests.cs`. The service depends on a store
and a password hasher, which are the interfaces of the design; the tests use the in-memory fake store
(`FakeUserStore`), a trivial password hasher (`FakePasswordHasher`) so that the strength of the real hasher is not
part of the test, and a clock the test can move (`MovableTimeProvider`) so that the lockout can be tested without
waiting. The real store and hasher are verified by their own units and by the `Storage` and `Identity` subsystems.
The rules are checked through the `DomainRuleException` that names the broken rule.

Each requirement of the unit is verified by the tests of its own scenarios below; a scenario that checks one aspect
is named after that aspect, so that a failing test points to the one clause that broke.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A user name has 3 to 50 characters from letters, digits, dot, underscore, and hyphen, and is unique regardless of
  case.
- A display name is trimmed and has 1 to 100 characters.
- A password has 12 to 128 characters, is not trimmed, and is stored only as a hash.
- A user is created as a system administrator only when asked to; no role has to be validated.
- Signing in succeeds only for an existing, enabled, unlocked user with the right password, in any case of the user
  name; every other case gives no user.
- Five failures in a row lock the account for five minutes, and a success resets the count.
- The last enabled system administrator cannot be disabled or demoted.
- A user changes their own password only with the current password; an administrator sets a new password for any
  user, which also ends a lockout. For a user with a second factor the password-only overload fails closed and keeps
  the hash and the failure counter.
- A user with a temporary password signs in but is refused (and audited) at a re-authentication for a signature; the
  change of the own password must differ from the temporary one and ends the temporary state; a password set by an
  administrator for another user makes the state temporary, and one set for the own account does not.
- The last sign-in is stored by a right password without a second factor, by the completed second step, and by an
  accepted sign-in with a provider, and by no failed sign-in, re-authentication, or password step that waits for the
  code; storing it writes no audit entry.
- A user is locked until the end of the lockout and no longer; only an enabled system administrator without a
  temporary password, not through an API token, unlocks a user, with the entry `user.unlocked`.
- A change of a user that does not exist is reported as such.
- A sign-in with an external identity finds the account by the exact issuer and subject only, creates an enabled
  account with no right, no project, and no password on the first sign-in, and never uses an account by its name;
  a missing or too long identity and a disabled account are refused.
- The display name and user name of a new external account are cleaned and bounded, the user name is made unique by a
  number or a generated name, and every password path refuses an external account.
- Null arguments are rejected with an `ArgumentNullException`.
- When a second factor store is present and the feature is on, the right password of an enrolled, enabled, unlocked
  local user does not sign in: the password step reports that a second factor is required and does not reset the
  failure counter; wrong passwords, unknown users, and disabled users get the same answers as for a plain account; the
  password-only checks (`AuthenticateAsync` and `ReauthenticateAsync`) refuse an enrolled account (fail closed) and
  record why; a plain account, a switched-off feature, and a service without the store behave as before.
- A password confirmation checks only the password, for an enrolled account too, does not reset the failure counter,
  and refuses wrong passwords, external accounts, and locked accounts.

#### Test Scenarios

##### User Name

**UserService_CreateAsync_ValidUsername_Creates**: A theory over user names made of every allowed kind of
character; each user is created enabled. This scenario is tested by
`UserService_CreateAsync_ValidUsername_Creates`.

**UserService_CreateAsync_UsernameOfThreeCharacters_Creates**: A user name of exactly 3 characters is accepted and
stored. This scenario is tested by `UserService_CreateAsync_UsernameOfThreeCharacters_Creates`.

**UserService_CreateAsync_UsernameOfFiftyCharacters_Creates**: A user name of exactly 50 characters is accepted and
stored. This scenario is tested by `UserService_CreateAsync_UsernameOfFiftyCharacters_Creates`.

**UserService_CreateAsync_InvalidUsername_ThrowsUserNameInvalid**: A theory over user names with a space, an at sign,
or a non-ASCII letter; each throws the rule `UserNameInvalid`. This scenario is tested by
`UserService_CreateAsync_InvalidUsername_ThrowsUserNameInvalid`.

**UserService_CreateAsync_UsernameShorterThanThree_ThrowsUserNameInvalid**: A null, an empty, and a too short user
name each throw the rule `UserNameInvalid`. This scenario is tested by
`UserService_CreateAsync_UsernameShorterThanThree_ThrowsUserNameInvalid`.

**UserService_CreateAsync_UsernameLongerThanFifty_ThrowsUserNameInvalid**: A user name of 51 characters throws the
rule `UserNameInvalid`. This scenario is tested by
`UserService_CreateAsync_UsernameLongerThanFifty_ThrowsUserNameInvalid`.

**UserService_CreateAsync_DuplicateUsernameOtherCase_ThrowsUserNameTaken**: A user is created and a second user with
the same name in another case is refused with the rule `UserNameTaken`. This scenario is tested by
`UserService_CreateAsync_DuplicateUsernameOtherCase_ThrowsUserNameTaken`.

**UserService_CreateAsync_RejectedUsername_StoresNothing**: An invalid user name and a taken one are refused; nothing
new is stored and the first user is as it was. This scenario is tested by
`UserService_CreateAsync_RejectedUsername_StoresNothing`.

##### Display Name

**UserService_CreateAsync_DisplayNameWithPadding_StoresItTrimmed**: A display name with leading and trailing white
space is stored trimmed. This scenario is tested by
`UserService_CreateAsync_DisplayNameWithPadding_StoresItTrimmed`.

**UserService_CreateAsync_DisplayNameOfOneCharacter_Creates**: A display name of one character after trimming is
accepted and stored trimmed. This scenario is tested by
`UserService_CreateAsync_DisplayNameOfOneCharacter_Creates`.

**UserService_CreateAsync_DisplayNameOfHundredCharacters_Creates**: A display name of 100 characters after trimming
is accepted and stored trimmed. This scenario is tested by
`UserService_CreateAsync_DisplayNameOfHundredCharacters_Creates`.

**UserService_CreateAsync_DisplayNameBlank_ThrowsDisplayNameInvalid**: A null, an empty, and a blank display name
each throw the rule `DisplayNameInvalid`. This scenario is tested by
`UserService_CreateAsync_DisplayNameBlank_ThrowsDisplayNameInvalid`.

**UserService_CreateAsync_DisplayNameLongerThanHundred_ThrowsDisplayNameInvalid**: A display name of 101 characters
after trimming throws the rule `DisplayNameInvalid`. This scenario is tested by
`UserService_CreateAsync_DisplayNameLongerThanHundred_ThrowsDisplayNameInvalid`.

**UserService_CreateAsync_RejectedDisplayName_StoresNothing**: A blank display name and a too long one are refused;
nothing is stored and nothing is audited. This scenario is tested by
`UserService_CreateAsync_RejectedDisplayName_StoresNothing`.

##### Password

**UserService_CreateAsync_PasswordOfTwelveCharacters_Creates**: A password of exactly 12 characters is accepted. This
scenario is tested by `UserService_CreateAsync_PasswordOfTwelveCharacters_Creates`.

**UserService_CreateAsync_PasswordInPolicyRange_CreatesAndAuthenticates**: A password inside the policy range is
accepted and signs the new user in. This scenario is tested by
`UserService_CreateAsync_PasswordInPolicyRange_CreatesAndAuthenticates`.

**UserService_CreateAsync_PasswordOfHundredTwentyEightCharacters_Creates**: A password of exactly 128 characters is
accepted and only its hash is stored. This scenario is tested by
`UserService_CreateAsync_PasswordOfHundredTwentyEightCharacters_Creates`.

**UserService_CreateAsync_PasswordOfElevenCharacters_ThrowsPasswordInvalid**: A password of 11 characters throws the
rule `PasswordInvalid`. This scenario is tested by
`UserService_CreateAsync_PasswordOfElevenCharacters_ThrowsPasswordInvalid`.

**UserService_CreateAsync_PasswordOfHundredTwentyNineCharacters_ThrowsPasswordInvalid**: A password of 129
characters throws the rule `PasswordInvalid`. This scenario is tested by
`UserService_CreateAsync_PasswordOfHundredTwentyNineCharacters_ThrowsPasswordInvalid`.

**UserService_CreateAsync_PasswordOfOneRepeatedLetter_Creates**: A password of one repeated letter is accepted,
because only its length is checked. This scenario is tested by
`UserService_CreateAsync_PasswordOfOneRepeatedLetter_Creates`.

**UserService_CreateAsync_PasswordMissing_ThrowsPasswordInvalid**: A missing password throws the rule
`PasswordInvalid`. This scenario is tested by `UserService_CreateAsync_PasswordMissing_ThrowsPasswordInvalid`.

**UserService_CreateAsync_PasswordOfTwelveSpaces_IsKeptUntrimmed**: A password of 12 spaces is accepted and kept as
it is, not trimmed. This scenario is tested by `UserService_CreateAsync_PasswordOfTwelveSpaces_IsKeptUntrimmed`.

**UserService_CreateAsync_RejectedPassword_StoresNothing**: A refused password stores and audits nothing. This
scenario is tested by `UserService_CreateAsync_RejectedPassword_StoresNothing`.

**UserService_CreateAsync_StoresHashNotPassword**: A user is created; the stored record holds the hash and the
password appears nowhere in it. This scenario is tested by `UserService_CreateAsync_StoresHashNotPassword`.

##### Create

**UserService_CreateAsync_NewUser_IsEnabled**: A created user without the administrator request is stored enabled.
This scenario is tested by `UserService_CreateAsync_NewUser_IsEnabled`.

**UserService_CreateAsync_AdministratorRequested_CreatesSystemAdministrator**: A user created with the administrator
request is stored as a system administrator. This scenario is tested by
`UserService_CreateAsync_AdministratorRequested_CreatesSystemAdministrator`.

**UserService_CreateAsync_AdministratorNotRequested_CreatesPlainUser**: A user created without the administrator
request is stored as a plain user. This scenario is tested by
`UserService_CreateAsync_AdministratorNotRequested_CreatesPlainUser`.

**UserService_Constructor_NullArguments_Throws**: The constructor is called with a null store, hasher, or time
provider; an `ArgumentNullException` is thrown for each. This scenario is tested by
`UserService_Constructor_NullArguments_Throws`.

##### Sign-In and Lockout

**UserService_AuthenticateAsync_RightPassword_ReturnsTheUser**: The right password signs the user in and returns that
user. This scenario is tested by `UserService_AuthenticateAsync_RightPassword_ReturnsTheUser`.

**UserService_AuthenticateAsync_UserNameInOtherCase_ReturnsTheUser**: The user is found by a user name written in
another letter case. This scenario is tested by
`UserService_AuthenticateAsync_UserNameInOtherCase_ReturnsTheUser`.

**UserService_AuthenticateAsync_WrongPassword_ReturnsNull**: A wrong, an empty, and a missing password give no user.
This scenario is tested by `UserService_AuthenticateAsync_WrongPassword_ReturnsNull`.

**UserService_AuthenticateAsync_UnknownUser_ReturnsNull**: An unknown user name gives no user. This scenario is
tested by `UserService_AuthenticateAsync_UnknownUser_ReturnsNull`.

**UserService_AuthenticateAsync_UnknownUser_VerifiesAHashLikeAKnownUser**: A wrong password of an unknown user costs
one hash verification, as it does for a known user. This scenario is tested by
`UserService_AuthenticateAsync_UnknownUser_VerifiesAHashLikeAKnownUser`.

**UserService_AuthenticateAsync_DisabledUser_ReturnsNullUntilEnabled**: A disabled user is refused with the right
password until the user is enabled again. This scenario is tested by
`UserService_AuthenticateAsync_DisabledUser_ReturnsNullUntilEnabled`.

**UserService_AuthenticateAsync_FifthFailureInARow_LocksTheUser**: Four wrong passwords do not lock the account and
the fifth does, so that the right password is then refused. This scenario is tested by
`UserService_AuthenticateAsync_FifthFailureInARow_LocksTheUser`.

**UserService_AuthenticateAsync_Lockout_LastsExactlyFiveMinutes**: A lockout ends exactly five minutes after it
began: the right password is still refused one second earlier and succeeds at five minutes. This scenario is tested
by `UserService_AuthenticateAsync_Lockout_LastsExactlyFiveMinutes`.

**UserService_AuthenticateAsync_LockedUser_RightPasswordIsRefused**: The right password is refused while the lockout
lasts. This scenario is tested by `UserService_AuthenticateAsync_LockedUser_RightPasswordIsRefused`.

**UserService_AuthenticateAsync_LockedUser_AttemptsAreNotCounted**: Attempts while the lockout lasts leave the
failure count and the end of the lockout as they were. This scenario is tested by
`UserService_AuthenticateAsync_LockedUser_AttemptsAreNotCounted`.

**UserService_AuthenticateAsync_SuccessBetweenFailures_ResetsCounter**: Three failures, a success, and three more
failures do not lock the account, and the failure count is 0 after the success. This scenario is tested by
`UserService_AuthenticateAsync_SuccessBetweenFailures_ResetsCounter`.

##### Enabled and Last Administrator

**UserService_SetEnabledAsync_False_DisablesTheUser**: Disabling a user stores the user as disabled. This scenario is
tested by `UserService_SetEnabledAsync_False_DisablesTheUser`.

**UserService_SetEnabledAsync_LastEnabledAdministrator_IsRefused**: Disabling the only enabled system administrator
throws the rule `LastAdmin`. This scenario is tested by
`UserService_SetEnabledAsync_LastEnabledAdministrator_IsRefused`.

**UserService_SetSystemAdminAsync_LastEnabledAdministrator_IsRefused**: Demoting the only enabled system
administrator throws the rule `LastAdmin`. This scenario is tested by
`UserService_SetSystemAdminAsync_LastEnabledAdministrator_IsRefused`.

**UserService_SetEnabledAndSetSystemAdmin_AnotherEnabledAdministrator_AllowTheChange**: With a second enabled system
administrator, the first can be disabled or demoted. This scenario is tested by
`UserService_SetEnabledAndSetSystemAdmin_AnotherEnabledAdministrator_AllowTheChange`.

**UserService_SetEnabledAsync_OtherAdminDisabled_StillProtectsLastEnabledAdmin**: With a second system administrator
who is already disabled, the only enabled system administrator still cannot be disabled or demoted. This scenario is
tested by `UserService_SetEnabledAsync_OtherAdminDisabled_StillProtectsLastEnabledAdmin`.

**UserService_SetSystemAdminAsync_Demote_TakesTheRightAway**: The system administrator right can be taken away from
one of two administrators. This scenario is tested by `UserService_SetSystemAdminAsync_Demote_TakesTheRightAway`.

**UserService_SetSystemAdminAsync_Promote_GivesTheRight**: A promotion by user name stores the system administrator
right. This scenario is tested by `UserService_SetSystemAdminAsync_Promote_GivesTheRight`.

##### Own Password and Set Password

**UserService_ChangeOwnPasswordAsync_CorrectCurrentPassword_ChangesThePassword**: A change with the right current
password stores the new password, which then signs in while the old one does not. This scenario is tested by
`UserService_ChangeOwnPasswordAsync_CorrectCurrentPassword_ChangesThePassword`.

**UserService_ChangeOwnPasswordAsync_WrongCurrentPassword_IsRefusedAndKeepsThePassword**: A wrong current password
throws the rule `CurrentPasswordIncorrect` and the stored password hash stays as it was. This scenario is tested by
`UserService_ChangeOwnPasswordAsync_WrongCurrentPassword_IsRefusedAndKeepsThePassword`.

**UserService_ChangeOwnPasswordAsync_NewPasswordBreakingTheRules_IsRefused**: A new password that breaks the
password rules throws the rule `PasswordInvalid` and the stored password hash stays as it was. This scenario is
tested by `UserService_ChangeOwnPasswordAsync_NewPasswordBreakingTheRules_IsRefused`.

**UserService_ChangeOwnPasswordAsync_WrongCurrentPassword_CountsOneFailure**: One wrong current password counts as
one failed sign-in. This scenario is tested by
`UserService_ChangeOwnPasswordAsync_WrongCurrentPassword_CountsOneFailure`.

**UserService_ChangeOwnPasswordAsync_RepeatedWrongPassword_LocksOut**: Five changes with a wrong current password
count as failures; a sixth with the right current password is then refused, and the old password does not sign in
while the account is locked. This scenario is tested by
`UserService_ChangeOwnPasswordAsync_RepeatedWrongPassword_LocksOut`.

**UserService_SetPasswordAsync_NewPassword_ReplacesThePassword**: A new password replaces the password: the new one
signs in and the old one does not. This scenario is tested by
`UserService_SetPasswordAsync_NewPassword_ReplacesThePassword`.

**UserService_SetPasswordAsync_PasswordBreakingTheRules_IsRefused**: A new password that breaks the password rules
throws the rule `PasswordInvalid` and the stored password hash stays as it was. This scenario is tested by
`UserService_SetPasswordAsync_PasswordBreakingTheRules_IsRefused`.

**UserService_SetPasswordAsync_LockedUser_EndsTheLockout**: Setting a password ends the lockout of the user and
clears the failure count. This scenario is tested by `UserService_SetPasswordAsync_LockedUser_EndsTheLockout`.

**UserService_Changes_UnknownUser_ThrowUserNotFound**: A theory over enabling, disabling, changing the role, changing the
system administrator right, changing the own password, and setting a password for a user that does not exist; each
throws the rule `UserNotFound`. This scenario is tested by `UserService_Changes_UnknownUser_ThrowUserNotFound`.

##### Administration Audit

**UserService_AdministrationEvents_NameTheActor**: Every administration event names the acting administrator. This
scenario is tested by `UserService_AdministrationEvents_NameTheActor`.

**UserService_AdministrationEvents_NeverRecordAPassword**: A user is created, disabled, enabled, given the system
administrator right, given a password by an administrator, and changes the own password; no entry holds a password or
a password hash. This scenario is tested by `UserService_AdministrationEvents_NeverRecordAPassword`.

**UserService_CreateAsync_NewUser_RecordsUserCreated**: Creating a user records `user.created` with the new user and
the acting administrator. This scenario is tested by `UserService_CreateAsync_NewUser_RecordsUserCreated`.

**UserService_CreateAsync_CommandLineActor_IsRecordedAsCommandLine**: A user created from the command line is
recorded with the actor `(command line)`. This scenario is tested by
`UserService_CreateAsync_CommandLineActor_IsRecordedAsCommandLine`.

**UserService_SetEnabledAsync_True_RecordsUserEnabled**: Enabling a user records `user.enabled` and the user. This
scenario is tested by `UserService_SetEnabledAsync_True_RecordsUserEnabled`.

**UserService_SetEnabledAsync_False_RecordsUserDisabled**: Disabling a user records `user.disabled` and the user.
This scenario is tested by `UserService_SetEnabledAsync_False_RecordsUserDisabled`.

**UserService_SetSystemAdminAsync_Promote_RecordsTheOldAndTheNewValue**: Promoting a user records
`user.admin-changed` with the old and the new value (`system administrator False -> True`). This scenario is tested
by `UserService_SetSystemAdminAsync_Promote_RecordsTheOldAndTheNewValue`.

**UserService_SetPasswordAsync_ByAdministrator_RecordsPasswordSetByAdmin**: Setting the password of a user as an
administrator records `user.password-set-by-admin` and the user. This scenario is tested by
`UserService_SetPasswordAsync_ByAdministrator_RecordsPasswordSetByAdmin`.

**UserService_ChangeOwnPasswordAsync_NewPassword_RecordsPasswordChangedByTheUser**: A user who changes the own
password is recorded with `user.password-changed` and the user as the actor. This scenario is tested by
`UserService_ChangeOwnPasswordAsync_NewPassword_RecordsPasswordChangedByTheUser`.

The test `UserService_AdministrationEvents_AreRecordedWithoutPasswords` checks all of these events together; it is
verified for the audit trail of the `Core` subsystem and is not a scenario of this unit.

##### Sign-In Audit

The sign-in events are best effort. These tests are defined in `AuditEventTests.cs`.

**UserService_AuthenticateAsync_WrongPassword_CounterUpdateCarriesNoDraft**: A wrong password leaves the counter
update without an audit draft. This scenario is tested by
`UserService_AuthenticateAsync_WrongPassword_CounterUpdateCarriesNoDraft`.

**UserService_AuthenticateAsync_WrongPassword_RecordsExistingUserWithoutPassword**: A wrong password for an existing
user records `auth.sign-in-failed` for that user and never the password. This scenario is tested by
`UserService_AuthenticateAsync_WrongPassword_RecordsExistingUserWithoutPassword`.

**UserService_AuthenticateAsync_DisabledAccount_RecordsAccountDisabled**: A sign-in on a disabled account with the
right password is recorded as such. This scenario is tested by
`UserService_AuthenticateAsync_DisabledAccount_RecordsAccountDisabled`.

**UserService_AuthenticateAsync_UnknownUser_RecordsUnknownUserEntry**: A sign-in of an unknown user is recorded with
the actor `unknown user` and the typed name, never the password. This scenario is tested by
`UserService_AuthenticateAsync_UnknownUser_RecordsUnknownUserEntry`.

**UserService_AuthenticateAsync_UnknownUserWithLineBreaks_RecordsTheTypedNameOnOneLine**: The typed name
`mallory`, a line break, and `fake line` is recorded as `unknown user, name 'mallory  fake line'`, on one line and
without a control character. This scenario is tested by
`UserService_AuthenticateAsync_UnknownUserWithLineBreaks_RecordsTheTypedNameOnOneLine`.

**UserService_AuthenticateAsync_UnknownUserWithLongName_RecordsTheTypedNameCapped**: A typed name of more characters
than the user name limit is recorded cut to 47 characters plus an ellipsis, which counts toward the limit of 50. This
scenario is tested by `UserService_AuthenticateAsync_UnknownUserWithLongName_RecordsTheTypedNameCapped`.

**UserService_AuthenticateAsync_UnknownUserRepeated_RecordsOneEntryPerWindow**: Repeated failures of the same name in
one minute produce one entry, with the suppressed count on the next entry of the next minute. This scenario is
tested by `UserService_AuthenticateAsync_UnknownUserRepeated_RecordsOneEntryPerWindow`.

**UserService_AuthenticateAsync_DisabledAccountRepeated_RecordsOneEntryPerWindow**: Repeated attempts on a disabled
account in one minute produce one entry, with the suppressed count on the next entry of the next minute. This
scenario is tested by `UserService_AuthenticateAsync_DisabledAccountRepeated_RecordsOneEntryPerWindow`.

**UserService_AuthenticateAsync_FifthFailure_RecordsLockoutOnce**: The fifth failure records `auth.locked-out` once.
This scenario is tested by `UserService_AuthenticateAsync_FifthFailure_RecordsLockoutOnce`.

**UserService_AuthenticateAsync_LockedAccount_RecordsSignInLockedOncePerWindow**: Attempts on the locked account
record `auth.sign-in-locked` once per minute and account, even with the right password. This scenario is tested by
`UserService_AuthenticateAsync_LockedAccount_RecordsSignInLockedOncePerWindow`.

**UserService_AuthenticateAsync_LockedAccountRepeated_RecordsOneEntryPerWindow**: Repeated attempts on a locked
account are recorded once per window, then once more with the count. This scenario is tested by
`UserService_AuthenticateAsync_LockedAccountRepeated_RecordsOneEntryPerWindow`.

**UserService_AuthenticateAsync_AuditStoreFails_StillRefusesAndDoesNotThrow**: A failing audit store does not change
the refusal and throws nothing. This scenario is tested by
`UserService_AuthenticateAsync_AuditStoreFails_StillRefusesAndDoesNotThrow`.

**UserService_AuthenticateAsync_NoAuditService_Works**: A service without an audit service still works. This
scenario is tested by `UserService_AuthenticateAsync_NoAuditService_Works`.

##### Re-Authentication Audit

These tests are defined in `AuditEventTests.cs`.

**UserService_ReauthenticateAsync_WrongPassword_RecordsSignatureReauthFailed**: A wrong password during
re-authentication records `signature.reauth-failed` with the detail `wrong password`, never the password. This
scenario is tested by `UserService_ReauthenticateAsync_WrongPassword_RecordsSignatureReauthFailed`.

**UserService_ReauthenticateAsync_RightPassword_ReturnsTheUser**: The right password is accepted by a
re-authentication, which records nothing. This scenario is tested by
`UserService_ReauthenticateAsync_RightPassword_ReturnsTheUser`.

**UserService_ReauthenticateAsync_LockedAccount_RecordsSignatureReauthFailed**: A re-authentication on a locked
account records `signature.reauth-failed` with the detail `account locked`, even with the right password. This
scenario is tested by `UserService_ReauthenticateAsync_LockedAccount_RecordsSignatureReauthFailed`.

**UserService_ReauthenticateAsync_DisabledAccount_RecordsSignatureReauthFailed**: A re-authentication on a disabled
account records `signature.reauth-failed` with the reason, even with the right password. This scenario is tested by
`UserService_ReauthenticateAsync_DisabledAccount_RecordsSignatureReauthFailed`.

**UserService_ReauthenticateAsync_UnknownUser_RecordsSignatureReauthFailed**: A re-authentication of an unknown user
records `signature.reauth-failed` with the detail `unknown user, name 'zed'`. This scenario is tested by
`UserService_ReauthenticateAsync_UnknownUser_RecordsSignatureReauthFailed`.

**UserService_ReauthenticateAsync_FifthFailure_LocksTheAccountOut**: The fifth wrong password during
re-authentication locks the account and records `auth.locked-out` once. This scenario is tested by
`UserService_ReauthenticateAsync_FifthFailure_LocksTheAccountOut`.

**UserService_ReauthenticateAsync_Failures_AreNotRecordedAsSignInFailures**: Failed re-authentications are recorded as
`signature.reauth-failed` and never as `auth.sign-in-failed` or `auth.sign-in-locked`. This scenario is tested by
`UserService_ReauthenticateAsync_Failures_AreNotRecordedAsSignInFailures`.

**UserService_ReauthenticateAsync_Failures_NeverRecordThePassword**: No field of the entries of failed
re-authentications holds the typed password. This scenario is tested by
`UserService_ReauthenticateAsync_Failures_NeverRecordThePassword`.

**UserService_ReauthenticateAsync_LockedAccountRepeated_RecordsOneEntryPerWindow**: Repeated re-authentications on a
locked account are recorded once per window, then once more with the count. This scenario is tested by
`UserService_ReauthenticateAsync_LockedAccountRepeated_RecordsOneEntryPerWindow`.

**UserService_ReauthenticateAsync_DisabledAccountRepeated_RecordsOneSignatureReauthFailedPerWindow**: Repeated
attempts on a disabled account record one `signature.reauth-failed` per minute. This scenario is tested by
`UserService_ReauthenticateAsync_DisabledAccountRepeated_RecordsOneSignatureReauthFailedPerWindow`.

**UserService_ReauthenticateAsync_DisabledThenLockedInOneMinute_RecordsBothReasons**: A disabled and a locked attempt
on the same account within one minute are both recorded, because the reason is part of the coalescing key. This
scenario is tested by `UserService_ReauthenticateAsync_DisabledThenLockedInOneMinute_RecordsBothReasons`.

##### Temporary Password

These tests are defined in `MustChangePasswordTests.cs`, where a user whose must-change flag is set is modeled over
`FakeUserStore`, `FakeReviewStore`, `FakeAuditStore`, and `FakePasswordHasher`.

**UserService_AuthenticateAsync_TemporaryPassword_SignsIn**: Signing in with the temporary password returns the user
with the flag set. This scenario is tested by `UserService_AuthenticateAsync_TemporaryPassword_SignsIn`.

**UserService_ReauthenticateAsync_TemporaryPassword_IsRefusedAndAudited**: A re-authentication with the temporary
password returns no user and records one `signature.reauth-failed` entry whose detail says `password change pending`
and does not contain the password. This scenario is tested by
`UserService_ReauthenticateAsync_TemporaryPassword_IsRefusedAndAudited`.

**UserService_ChangeOwnPasswordAsync_SameAsTemporary_IsRefusedAndKeepsTheFlag**: Choosing the temporary password
again is refused with `PasswordInvalid` and keeps the flag. This scenario is tested by
`UserService_ChangeOwnPasswordAsync_SameAsTemporary_IsRefusedAndKeepsTheFlag`.

**UserService_ChangeOwnPasswordAsync_NewPassword_ClearsTheTemporaryFlag**: Choosing a new password clears the
temporary flag of the stored account. This scenario is tested by
`UserService_ChangeOwnPasswordAsync_NewPassword_ClearsTheTemporaryFlag`. What the cleared flag lets the account do
is verified with `ProjectAccess` in `project-access.md`.

**UserService_SetPasswordAsync_ByAdministrator_MakesThePasswordTemporary**: An administrator who sets the password of
another account makes it temporary, also for an account that was not flagged. This scenario is tested by
`UserService_SetPasswordAsync_ByAdministrator_MakesThePasswordTemporary`.

**UserService_SetPasswordAsync_OtherUserByAdministratorOrCommandLine_ForcesAChange**: A password set by another user,
an administrator, or the command line is temporary. This scenario is defined in `UserServiceTests.cs` and is tested by
`UserService_SetPasswordAsync_OtherUserByAdministratorOrCommandLine_ForcesAChange`.

**UserService_SetPasswordAsync_OwnRow_DoesNotForceAChange**: An administrator who sets the own password gets no
forced change. This scenario is tested by `UserService_SetPasswordAsync_OwnRow_DoesNotForceAChange`.

**UserService_SetPasswordAsync_OwnRow_ClearsAPendingChange**: An administrator who sets the own password ends a
pending forced change. This scenario is tested by `UserService_SetPasswordAsync_OwnRow_ClearsAPendingChange`.

**UserService_SetPasswordAsync_ApiTokenActorWithTheUserName_ForcesAChange**: An actor that acts through an API token
with the name of the user is not the own row, so the password it sets is temporary. This scenario is tested by
`UserService_SetPasswordAsync_ApiTokenActorWithTheUserName_ForcesAChange`.

##### Last Sign-In

**UserService_AuthenticateAsync_RightPassword_StoresTheTimeOfTheSignIn**: A right password without a second factor
stores the time of the clock in `LastSignInUtc` and writes no audit entry. This scenario is tested by
`UserService_AuthenticateAsync_RightPassword_StoresTheTimeOfTheSignIn`.

**UserService_AuthenticateAsync_WrongPassword_StoresNoSignInTime**: A failed sign-in stores no time of a sign-in.
This scenario is tested by `UserService_AuthenticateAsync_WrongPassword_StoresNoSignInTime`.

**UserService_ReauthenticatePasswordAsync_RightPassword_StoresNoSignInTime**: A re-authentication with the right
password stores no time of a sign-in. This scenario is tested by
`UserService_ReauthenticatePasswordAsync_RightPassword_StoresNoSignInTime`.

**UserService_SignInExternalAsync_AcceptedSignIn_StoresTheTimeOfTheSignIn**: The first and a later accepted sign-in
of an account at a provider store the time of the sign-in. This scenario is tested by
`UserService_SignInExternalAsync_AcceptedSignIn_StoresTheTimeOfTheSignIn`.

**UserService_SignInExternalAsync_DisabledAccount_StoresNoSignInTime**: The refused sign-in of a disabled account of
an identity provider leaves the stored time of the last sign-in as it was. This scenario is tested by
`UserService_SignInExternalAsync_DisabledAccount_StoresNoSignInTime`.

**UserService_RecordSignInAsync_KnownUser_StoresTheTimeOfTheSignIn**: Recording a complete sign-in, as the second
step of a sign-in does, stores its time and clears the count of failures. This scenario is tested by
`UserService_RecordSignInAsync_KnownUser_StoresTheTimeOfTheSignIn`. The completed second step itself is verified with
`TotpService` in `totp-service.md`.

**UserService_AuthenticatePasswordAsync_EnrolledUser_StoresNoSignInTime**: The right password of an enrolled account,
which only asks for the code, stores no sign-in time. This scenario is tested by
`UserService_AuthenticatePasswordAsync_EnrolledUser_StoresNoSignInTime`.

##### Locked Status and Unlock

**UserService_IsLocked_RunningLockout_IsLocked**: A lockout that is still running counts as locked, up to one second
before its end. This scenario is tested by `UserService_IsLocked_RunningLockout_IsLocked`.

**UserService_IsLocked_LockoutAtItsEnd_IsNotLocked**: A lockout is no longer a lockout at its end instant. This
scenario is tested by `UserService_IsLocked_LockoutAtItsEnd_IsNotLocked`.

**UserService_IsLocked_NoLockout_IsNotLocked**: A user without a lockout is not locked. This scenario is tested by
`UserService_IsLocked_NoLockout_IsNotLocked`.

**UserService_UnlockAsync_SystemAdministrator_EndsTheLockout**: A system administrator ends the lockout of a user by
identifier, and the user signs in with the password at once. This scenario is tested by
`UserService_UnlockAsync_SystemAdministrator_EndsTheLockout`.

**UserService_UnlockAsync_SystemAdministrator_ClearsTheCountOfFailedSignIns**: An unlock by a system administrator
clears the count of failed sign-ins. This scenario is tested by
`UserService_UnlockAsync_SystemAdministrator_ClearsTheCountOfFailedSignIns`.

**UserService_UnlockAsync_SystemAdministrator_RecordsUserUnlocked**: An unlock by a system administrator records
`user.unlocked` with the actor and the user name. This scenario is tested by
`UserService_UnlockAsync_SystemAdministrator_RecordsUserUnlocked`.

**UserService_UnlockAsync_ActorThatIsNotAnEnabledAdministrator_IsRefused**: A theory over an ordinary user, a
disabled administrator, an administrator with a temporary password, and an actor through an API token; each is
refused with `ActorNotAllowed`. This scenario is tested by
`UserService_UnlockAsync_ActorThatIsNotAnEnabledAdministrator_IsRefused`.

**UserService_UnlockAsync_RefusedActor_ChangesAndAuditsNothing**: A refused actor leaves the lockout and the failure
count as they were and adds no audit entry. This scenario is tested by
`UserService_UnlockAsync_RefusedActor_ChangesAndAuditsNothing`.

**UserService_UnlockAsync_UnknownUserIdentifier_ThrowsUserNotFound**: An identifier that no user has is refused with
the rule `UserNotFound`. This scenario is tested by `UserService_UnlockAsync_UnknownUserIdentifier_ThrowsUserNotFound`.

##### External Identity Sign-In

These tests are defined in `UserServiceExternalTests.cs` over `FakeUserStore`.

**UserService_SignInExternalAsync_FirstThenSecond_CreatesOnceAndMatches**: The first sign-in creates an enabled
account that is not a system administrator and has an empty password hash, with one `account.oidc-created` entry
that names only the host of the issuer, and the second sign-in finds the same account and keeps its names. This
scenario is tested by `UserService_SignInExternalAsync_FirstThenSecond_CreatesOnceAndMatches`.

**UserService_SignInExternalAsync_FirstSignIn_CreatesAnEnabledUser**: The first sign-in of an identity creates an
enabled account. This scenario is tested by `UserService_SignInExternalAsync_FirstSignIn_CreatesAnEnabledUser`.

**UserService_SignInExternalAsync_FirstSignIn_CreatesNoSystemAdministrator**: The first sign-in of an identity
creates an account that is not a system administrator. This scenario is tested by
`UserService_SignInExternalAsync_FirstSignIn_CreatesNoSystemAdministrator`.

**UserService_SignInExternalAsync_FirstSignIn_RecordsAccountOidcCreated**: The first sign-in of an identity records
the creation of the account with the host of the issuer. This scenario is tested by
`UserService_SignInExternalAsync_FirstSignIn_RecordsAccountOidcCreated`.

**UserService_SignInExternalAsync_MatchesOnlyByIssuerAndSubject**: With a local account of the same name, a sign-in,
one with the subject in another case, and one with another issuer each create their own account (`olive-2`,
`olive-3`, `olive-4`); none is the local one, and none is an administrator. This scenario is tested by
`UserService_SignInExternalAsync_MatchesOnlyByIssuerAndSubject`.

**UserService_SignInExternalAsync_MissingIdentity_IsInvalid**: A missing or empty issuer or subject gives `Invalid`
and no account. This scenario is tested by `UserService_SignInExternalAsync_MissingIdentity_IsInvalid`.

**UserService_SignInExternalAsync_IssuerOfMaximumLength_SignsIn**: An issuer of exactly the maximum length is
accepted. This scenario is tested by `UserService_SignInExternalAsync_IssuerOfMaximumLength_SignsIn`.

**UserService_SignInExternalAsync_IssuerOverTheLimit_IsInvalid**: An issuer one character over the limit gives
`Invalid` and no account. This scenario is tested by
`UserService_SignInExternalAsync_IssuerOverTheLimit_IsInvalid`.

**UserService_SignInExternalAsync_SubjectOfMaximumLength_SignsIn**: A subject of exactly the maximum length is
accepted. This scenario is tested by `UserService_SignInExternalAsync_SubjectOfMaximumLength_SignsIn`.

**UserService_SignInExternalAsync_SubjectOverTheLimit_IsInvalid**: A subject one character over the limit gives
`Invalid` and no account. This scenario is tested by
`UserService_SignInExternalAsync_SubjectOverTheLimit_IsInvalid`.

**UserService_SignInExternalAsync_DisabledAccount_IsRefused**: A disabled account is refused as `Disabled`, has no
user in the answer, and stays unchanged. This scenario is tested by
`UserService_SignInExternalAsync_DisabledAccount_IsRefused`.

##### External Account Names

**UserService_SignInExternalAsync_Candidate_IsReducedToAValidUsername**: A theory over suggested names: valid names
are kept and markup characters are dropped. This scenario is tested by
`UserService_SignInExternalAsync_Candidate_IsReducedToAValidUsername`.

**UserService_SignInExternalAsync_CandidateWithNothingUsable_UsesTheNameUser**: A name without usable characters or
a null gives `user`. This scenario is tested by
`UserService_SignInExternalAsync_CandidateWithNothingUsable_UsesTheNameUser`.

**UserService_SignInExternalAsync_ShortCandidate_IsPrefixedWithUser**: A single character gives `user-a`. This
scenario is tested by `UserService_SignInExternalAsync_ShortCandidate_IsPrefixedWithUser`.

**UserService_SignInExternalAsync_LongCandidate_IsCutToFortyFourCharacters**: A long suggested name is cut to 44
characters. This scenario is tested by `UserService_SignInExternalAsync_LongCandidate_IsCutToFortyFourCharacters`.

**UserService_SignInExternalAsync_DisplayName_IsSanitized**: A theory over display names: control characters, a
bidirectional override, and zero-width characters are removed, and white space is collapsed and trimmed. This
scenario is tested by `UserService_SignInExternalAsync_DisplayName_IsSanitized`.

**UserService_SignInExternalAsync_BlankDisplayName_UsesTheUserName**: A blank or missing display name is replaced by
the user name. This scenario is tested by `UserService_SignInExternalAsync_BlankDisplayName_UsesTheUserName`.

**UserService_SignInExternalAsync_LongDisplayName_IsBounded**: A display name of 1000 characters is cut to 100. This
scenario is tested by `UserService_SignInExternalAsync_LongDisplayName_IsBounded`.

**UserService_SignInExternalAsync_SuggestedNameTaken_AddsANumber**: A number is added to the made user name when a
local account holds that name. This scenario is tested by
`UserService_SignInExternalAsync_SuggestedNameTaken_AddsANumber`.

**UserService_SignInExternalAsync_AllNamesTaken_UsesNameFromIdentity**: With the plain name and the numbered names up
to `-50` taken by other accounts, the new account gets a generated name that starts with `oidc-` and has 17
characters, and no other account is used. This scenario is tested by
`UserService_SignInExternalAsync_AllNamesTaken_UsesNameFromIdentity`.

**UserService_SignInExternalAsync_OnlyIdentityNameTaken_UsesFirstVariant**: With the same names taken, and a local
account that holds the generated name itself, the new account gets that name with the suffix `-2`. This scenario is
tested by `UserService_SignInExternalAsync_OnlyIdentityNameTaken_UsesFirstVariant`.

**UserService_SignInExternalAsync_IdentityNameTakenByLocalAccount_UsesNumberedVariant**: With the same names taken,
and local accounts that hold the generated name and its `-2` variant, the new account is created with the suffix
`-3`, within the length limit and with only letters, digits, and hyphens; the local accounts are not changed, and the
next sign-in of the same identity finds the new account. This scenario is tested by
`UserService_SignInExternalAsync_IdentityNameTakenByLocalAccount_UsesNumberedVariant`.

##### External Account Passwords

**UserService_ExternalAccount_SetPassword_IsRefused**: Setting a password for an account of an identity provider
throws the rule `PasswordInvalid` and leaves it without a password hash. This scenario is tested by
`UserService_ExternalAccount_SetPassword_IsRefused`.

**UserService_ExternalAccount_Authenticate_IsRefused**: No password signs in an account of an identity provider,
whatever is typed, and the attempts do not count as failures. This scenario is tested by
`UserService_ExternalAccount_Authenticate_IsRefused`.

**UserService_ExternalAccount_ConfirmForASignature_IsRefused**: No password confirms a signature for an account of an
identity provider, through `ReauthenticateAsync` or `ConfirmPasswordAsync`. This scenario is tested by
`UserService_ExternalAccount_ConfirmForASignature_IsRefused`.

**UserService_ExternalAccount_ChangeOwnPassword_IsRefused**: Changing the own password of an account of an identity
provider is refused with the rule `CurrentPasswordIncorrect`, leaves it without a password hash, and does not count a
failure. This scenario is tested by `UserService_ExternalAccount_ChangeOwnPassword_IsRefused`.

##### Second Factor

These tests are defined in `UserServiceTotpTests.cs`, over the same fakes with `FakeTotpStore` and a switch for the
feature (`TotpTestWorld`).

**UserService_AuthenticatePasswordAsync_EnrolledUser_AsksForTheSecondFactor**: The right password of an enrolled
account reports that a second factor is required and returns no user. This scenario is tested by
`UserService_AuthenticatePasswordAsync_EnrolledUser_AsksForTheSecondFactor`.

**UserService_AuthenticatePasswordAsync_EnrolledUser_KeepsTheCountOfFailures**: The right password of an enrolled
account leaves the count of failures as it is. This scenario is tested by
`UserService_AuthenticatePasswordAsync_EnrolledUser_KeepsTheCountOfFailures`.

**UserService_AuthenticateAsync_EnrolledUser_FailsClosed**: The legacy password-only sign-in refuses an enrolled
account even with the right password and records why. This scenario is tested by
`UserService_AuthenticateAsync_EnrolledUser_FailsClosed`.

**UserService_ReauthenticateAsync_EnrolledUser_FailsClosed**: The legacy password-only re-authentication refuses an
enrolled account even with the right password and records why. This scenario is tested by
`UserService_ReauthenticateAsync_EnrolledUser_FailsClosed`.

**UserService_AuthenticateAsync_NoSecondFactorOrFeatureOff_SignsInWithThePassword**: The same checks accept an
account without a second factor, and an enrolled account when the feature is switched off. This scenario is tested
by `UserService_AuthenticateAsync_NoSecondFactorOrFeatureOff_SignsInWithThePassword`.

**UserService_AuthenticatePasswordAsync_WithoutTotpStore_NeverAsksForTheSecondFactor**: A user service built without
a second factor store never asks for a second factor. This scenario is tested by
`UserService_AuthenticatePasswordAsync_WithoutTotpStore_NeverAsksForTheSecondFactor`.

**UserService_AuthenticatePasswordAsync_WrongPassword_LooksTheSameWithAndWithoutSecondFactor**: Wrong passwords and
unknown users look the same for an enrolled account and a plain one. This scenario is tested by
`UserService_AuthenticatePasswordAsync_WrongPassword_LooksTheSameWithAndWithoutSecondFactor`.

**UserService_AuthenticatePasswordAsync_DisabledEnrolledUser_IsRefused**: The right password of a disabled enrolled
account is refused like that of any disabled account. This scenario is tested by
`UserService_AuthenticatePasswordAsync_DisabledEnrolledUser_IsRefused`.

**UserService_ChangeOwnPasswordAsync_EnrolledUser_FailsClosedAndKeepsHashAndCounter**: For an enrolled user the
password-only overload with the right current password throws `TotpCodeIncorrect`, the stored hash and the failure
counter are unchanged, and the refusal is recorded. This scenario is tested by
`UserService_ChangeOwnPasswordAsync_EnrolledUser_FailsClosedAndKeepsHashAndCounter`.

**UserService_ChangeOwnPasswordAsync_EnrolledUser_WrongPasswordCounts**: For an enrolled user a wrong current
password still counts as a failure and is reported as `CurrentPasswordIncorrect`. This scenario is tested by
`UserService_ChangeOwnPasswordAsync_EnrolledUser_WrongPasswordCounts`.

**UserService_ChangeOwnPasswordAsync_EnrolledUserWithTheFeatureOff_ChangesThePassword**: With the feature switched
off the password-only change works for an enrolled user. This scenario is tested by
`UserService_ChangeOwnPasswordAsync_EnrolledUserWithTheFeatureOff_ChangesThePassword`.

**UserService_ConfirmPasswordAsync_ChecksThePasswordOnly**: The password confirmation works for an enrolled account
without a code and without resetting the counter, and refuses wrong passwords and external accounts. This scenario
is tested by `UserService_ConfirmPasswordAsync_ChecksThePasswordOnly`.

**UserService_ConfirmPasswordAsync_LockedAccount_IsRefused**: A locked account is refused until the lockout ends.
This scenario is tested by `UserService_ConfirmPasswordAsync_LockedAccount_IsRefused`.

**UserService_RegisterSecondFactorFailureAsync_WrongCodesAndPasswords_ShareOneCounter**: Wrong codes count with wrong
passwords in one counter that locks the account on the fifth failure. This scenario is tested by
`UserService_RegisterSecondFactorFailureAsync_WrongCodesAndPasswords_ShareOneCounter`.

**UserService_IsLockedOutAsync_LockedAndFreeAccounts_TellsWhetherLocked**: The check whether an account is locked out
is true for a locked account and false for a free or unknown one. This scenario is tested by
`UserService_IsLockedOutAsync_LockedAndFreeAccounts_TellsWhetherLocked`.
