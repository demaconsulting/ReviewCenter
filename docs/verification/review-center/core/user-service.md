### UserService

#### Verification Approach

`UserService` is verified with unit tests defined in `UserServiceTests.cs`. The service depends on a store and a
password hasher, which are the interfaces of the design; the tests use the in-memory fake store (`FakeUserStore`),
a trivial password hasher (`FakePasswordHasher`) so that the strength of the real hasher is not part of the test, and
a clock the test can move (`MovableTimeProvider`) so that the lockout can be tested without waiting. The real store
and hasher are verified by their own units and by the `Storage` and `Identity` subsystems. The rules are checked
through the `DomainRuleException` that names the broken rule.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A user name has 3 to 50 characters from letters, digits, dot, underscore, and hyphen, and is unique regardless of
  case.
- A display name is trimmed and has 1 to 100 characters.
- A password has 12 to 128 characters, is not trimmed, and is stored only as a hash.
- A role that is not defined is refused.
- Signing in succeeds only for an existing, enabled, unlocked user with the right password, in any case of the user
  name; every other case gives no user.
- Five failures in a row lock the account for five minutes, and a success resets the count.
- The last enabled administrator cannot be disabled or demoted.
- A user changes their own password only with the current password; an administrator sets a new password for any
  user, which also ends a lockout.
- A change of a user that does not exist is reported as such.
- Null arguments are rejected with an `ArgumentNullException`.

#### Test Scenarios

**UserService_CreateAsync_ValidUsername_Creates**: A theory over user names of 3 characters, with every allowed
kind of character, and of 50 characters; each user is created. This scenario is tested by
`UserService_CreateAsync_ValidUsername_Creates`.

**UserService_CreateAsync_InvalidUsername_ThrowsUserNameInvalid**: A theory over a null, an empty, a too short, a
too long (51 characters) user name, and names with a space, an at sign, or a non-ASCII letter; each throws the rule
`UserNameInvalid`. This scenario is tested by `UserService_CreateAsync_InvalidUsername_ThrowsUserNameInvalid`.

**UserService_CreateAsync_DuplicateUsernameOtherCase_ThrowsUserNameTaken**: A user is created and a second user with
the same name in another case is refused with the rule `UserNameTaken`. This scenario is tested by
`UserService_CreateAsync_DuplicateUsernameOtherCase_ThrowsUserNameTaken`.

**UserService_CreateAsync_DisplayName_IsTrimmedAndLimited**: A padded display name is stored trimmed; a blank or
null one throws the rule `DisplayNameInvalid`; 100 characters are accepted and 101 refused. This scenario is tested
by `UserService_CreateAsync_DisplayName_IsTrimmedAndLimited`.

**UserService_CreateAsync_PasswordLength_FollowsPolicy**: A theory over passwords of 11, 12, 128, and 129
characters; the first and last throw the rule `PasswordInvalid` and the others create the user. This scenario is
tested by `UserService_CreateAsync_PasswordLength_FollowsPolicy`.

**UserService_CreateAsync_MissingPasswordOrWhiteSpacePadding_FollowsLengthRule**: A null password is refused; a
password of 12 spaces is accepted and is not trimmed. This scenario is tested by
`UserService_CreateAsync_MissingPasswordOrWhiteSpacePadding_FollowsLengthRule`.

**UserService_CreateAsync_StoresHashNotPassword**: A user is created; the stored record holds the hash and the
password appears nowhere in it. This scenario is tested by `UserService_CreateAsync_StoresHashNotPassword`.

**UserService_CreateAsync_UndefinedRole_Throws**: A user is created with a role value that is not defined; an
`ArgumentOutOfRangeException` is thrown. This scenario is tested by `UserService_CreateAsync_UndefinedRole_Throws`.

**UserService_AuthenticateAsync_RightWrongAndUnknown_OnlyRightSucceeds**: The right password succeeds, also with
the user name in another case; a wrong password, an unknown user, and a null password give no user. This scenario is
tested by `UserService_AuthenticateAsync_RightWrongAndUnknown_OnlyRightSucceeds`.

**UserService_AuthenticateAsync_DisabledUser_ReturnsNullUntilEnabled**: A disabled user is refused with the right
password until the user is enabled again. This scenario is tested by
`UserService_AuthenticateAsync_DisabledUser_ReturnsNullUntilEnabled`.

**UserService_AuthenticateAsync_FiveFailures_LockForFiveMinutes**: Four wrong passwords do not lock the account (the
right password still signs in and resets the count); five wrong passwords in a row do, so that the right password
is refused; at 4 minutes 59 seconds it is still refused, and at five minutes it succeeds. This scenario is tested by
`UserService_AuthenticateAsync_FiveFailures_LockForFiveMinutes`.

**UserService_AuthenticateAsync_SuccessBetweenFailures_ResetsCounter**: Three failures, a success, and three more
failures do not lock the account, and the failure count is 0 after the success. This scenario is tested by
`UserService_AuthenticateAsync_SuccessBetweenFailures_ResetsCounter`.

**UserService_SetEnabledAsync_SetRoleAsync_LastAdmin_IsProtected**: The only administrator cannot be disabled or
demoted; both throw the rule `LastAdmin`. With a second administrator, the first can be demoted, and the second is
then protected in turn. This scenario is tested by `UserService_SetEnabledAsync_SetRoleAsync_LastAdmin_IsProtected`.

**UserService_SetEnabledAsync_OtherAdminDisabled_StillProtectsLastEnabledAdmin**: With a second administrator who
is already disabled, the only enabled administrator still cannot be disabled or demoted. This scenario is tested by
`UserService_SetEnabledAsync_OtherAdminDisabled_StillProtectsLastEnabledAdmin`.

**UserService_ChangeOwnPasswordAsync_NeedsCurrentPasswordAndPolicy**: A change with a wrong current password throws
the rule `CurrentPasswordIncorrect`, one with a too short new password throws `PasswordInvalid`, and a valid change
replaces the password. This scenario is tested by `UserService_ChangeOwnPasswordAsync_NeedsCurrentPasswordAndPolicy`.

**UserService_ChangeOwnPasswordAsync_RepeatedWrongPassword_LocksOut**: Five changes with a wrong current password
count as failures; a sixth with the right current password is then refused, and the old password does not sign in
while the account is locked. This scenario is tested by
`UserService_ChangeOwnPasswordAsync_RepeatedWrongPassword_LocksOut`.

**UserService_SetPasswordAsync_NewPassword_WorksAndEndsLockout**: A new password is set for a locked user; the user
signs in with it at once, and the old password no longer works. This scenario is tested by
`UserService_SetPasswordAsync_NewPassword_WorksAndEndsLockout`.

**UserService_Changes_UnknownUser_ThrowUserNotFound**: Enabling, setting a role, setting a password, and changing a
password for a user that does not exist each throw the rule `UserNotFound`. This scenario is tested by
`UserService_Changes_UnknownUser_ThrowUserNotFound`.

**UserService_SetRoleAsync_ValidAndUndefinedRole_ChangesOrThrows**: A defined role is stored; an undefined role
throws an `ArgumentOutOfRangeException`. This scenario is tested by
`UserService_SetRoleAsync_ValidAndUndefinedRole_ChangesOrThrows`.

**UserService_Constructor_NullArguments_Throws**: The constructor is called with a null store, hasher, or time
provider; an `ArgumentNullException` is thrown for each. This scenario is tested by
`UserService_Constructor_NullArguments_Throws`.

**UserService_AdministrationEvents_AreRecordedWithoutPasswords**: A user is created, disabled, enabled, given
another role, given a password by an administrator, and changes the own password; the entries `user.created`,
`user.disabled`, `user.enabled`, `user.role-changed`, `user.password-set-by-admin`, and `user.password-changed` are
recorded and none contains a password. This scenario is tested by
`UserService_AdministrationEvents_AreRecordedWithoutPasswords`. A user created from the command line is recorded
with the actor `(command line)`, which is tested by `UserService_CreateAsync_CommandLineActor_IsRecordedAsCommandLine`.

**UserService_AuthenticateAsync audit scenarios**: The sign-in events are best effort and are verified as follows. A
wrong password leaves the counter update without an audit draft, which is tested by
`UserService_AuthenticateAsync_WrongPassword_CounterUpdateCarriesNoDraft`, and records `auth.sign-in-failed` for the
existing user without the password, which is tested by
`UserService_AuthenticateAsync_WrongPassword_RecordsExistingUserWithoutPassword`. An unknown user is recorded with
the actor `unknown user` and the typed name cleaned and cut to 50 characters, never the password, which is tested by
`UserService_AuthenticateAsync_UnknownUser_RecordsCleanedCappedTypedNameWithoutPassword`, and repeated failures of the
same name in one minute produce one entry, with the suppressed count on the next entry of the next minute, which is
tested by `UserService_AuthenticateAsync_UnknownUserRepeated_RecordsOneEntryPerWindow`. A disabled account is
recorded, which is tested by `UserService_AuthenticateAsync_DisabledAccount_RecordsAccountDisabled`, and repeated
attempts on it in one minute produce one entry, with the suppressed count on the next entry of the next minute, which
is tested by `UserService_AuthenticateAsync_DisabledAccountRepeated_RecordsOneEntryPerWindow`. The fifth failure
records `auth.locked-out` once, which is tested by `UserService_AuthenticateAsync_FifthFailure_RecordsLockoutOnce`,
and attempts on the locked account record `auth.sign-in-locked` once per minute, which is tested by
`UserService_AuthenticateAsync_LockedAccount_RecordsSignInLockedOncePerWindow`. A failing audit store
does not change the refusal and throws nothing, which is tested by
`UserService_AuthenticateAsync_AuditStoreFails_StillRefusesAndDoesNotThrow`, and a service without an audit service
still works, which is tested by `UserService_AuthenticateAsync_NoAuditService_Works`.

**UserService_ReauthenticateAsync audit scenario**: Wrong passwords, an attempt on the locked account, and an unknown
user through `ReauthenticateAsync` record `signature.reauth-failed` (details `wrong password`, `account locked`, and
`unknown user, name 'zed'`) and never `auth.sign-in-failed` or `auth.sign-in-locked`; the password is in no entry, and
the fifth failure still records `auth.locked-out` once. This scenario is tested by
`UserService_ReauthenticateAsync_WrongPasswordAndLockedAccount_RecordSignatureReauthFailed`. Repeated attempts on a
disabled account record one `signature.reauth-failed` per minute, which is tested by
`UserService_ReauthenticateAsync_DisabledAccountRepeated_RecordsOneSignatureReauthFailedPerWindow`, and a disabled
and a locked attempt on the same account within one minute are both recorded, because the reason is part of the
coalescing key, which is tested by `UserService_ReauthenticateAsync_DisabledThenLockedInOneMinute_RecordsBothReasons`.
These tests are defined in `AuditEventTests.cs`.
