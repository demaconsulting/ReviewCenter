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
- A user is created as a system administrator only when asked to; no role has to be validated.
- Signing in succeeds only for an existing, enabled, unlocked user with the right password, in any case of the user
  name; every other case gives no user.
- Five failures in a row lock the account for five minutes, and a success resets the count.
- The last enabled system administrator cannot be disabled or demoted.
- A user changes their own password only with the current password; an administrator sets a new password for any
  user, which also ends a lockout.
- A user with a temporary password signs in but is refused (and audited) at a re-authentication for a signature; the
  change of the own password must differ from the temporary one and ends the temporary state; a password set by an
  administrator leaves the state unchanged.
- A change of a user that does not exist is reported as such.
- A sign-in with an external identity finds the account by the exact issuer and subject only, creates an enabled
  account with no right, no project, and no password on the first sign-in, and never uses an account by its name;
  a missing or too long identity and a disabled account are refused.
- The display name and user name of a new external account are cleaned and bounded, the user name is made unique by a
  number or a generated name, and every password path refuses an external account.
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

**UserService_SetEnabledAsync_SetRoleAsync_LastAdmin_IsProtected**: The only system administrator cannot be disabled
or demoted; both throw the rule `LastAdmin`. With a second system administrator, the first can be demoted, and the
second is then protected in turn. This scenario is tested by
`UserService_SetEnabledAsync_SetRoleAsync_LastAdmin_IsProtected`.

**UserService_SetEnabledAsync_OtherAdminDisabled_StillProtectsLastEnabledAdmin**: With a second system administrator
who is already disabled, the only enabled system administrator still cannot be disabled or demoted. This scenario is
tested by `UserService_SetEnabledAsync_OtherAdminDisabled_StillProtectsLastEnabledAdmin`.

**UserService_SetSystemAdminAsync_PromoteAndDemote_LastAdminIsKept**: With one system administrator and a plain user,
demoting and disabling the administrator both throw the rule `LastAdmin`; after the plain user is promoted with
`SetSystemAdminAsync`, the first administrator can be demoted, and the flags read back as stored. This scenario is
tested by `UserService_SetSystemAdminAsync_PromoteAndDemote_LastAdminIsKept`.

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

**UserService_Changes_UnknownUser_ThrowUserNotFound**: Enabling, changing the system administrator right, setting a
password, and changing a password for a user that does not exist each throw the rule `UserNotFound`. This scenario is
tested by `UserService_Changes_UnknownUser_ThrowUserNotFound`.

**UserService_Constructor_NullArguments_Throws**: The constructor is called with a null store, hasher, or time
provider; an `ArgumentNullException` is thrown for each. This scenario is tested by
`UserService_Constructor_NullArguments_Throws`.

**UserService_AdministrationEvents_AreRecordedWithoutPasswords**: A user is created, disabled, enabled, given
the system administrator right, given a password by an administrator, and changes the own password; the entries
`user.created`, `user.disabled`, `user.enabled`, `user.admin-changed` (detail
`user 'carl': system administrator False -> True`), `user.password-set-by-admin`, and `user.password-changed` are
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

**UserService_SignInExternalAsync scenarios**: The accounts of an external identity provider are verified in
`UserServiceExternalTests.cs` over `FakeUserStore`. The first sign-in creates an enabled account that is not a system
administrator and has an empty password hash, with one `account.oidc-created` entry that names only the host of the
issuer, and the second sign-in finds the same account and keeps its names, which is tested by
`UserService_SignInExternalAsync_FirstThenSecond_CreatesOnceAndMatches`. With a local account of the same name, a
sign-in, one with the subject in another case, and one with another issuer each create their own account (`olive-2`,
`olive-3`, `olive-4`), none is the local one, and none is an administrator, which is tested by
`UserService_SignInExternalAsync_MatchesOnlyByIssuerAndSubject`. A missing or empty issuer or subject gives
`Invalid` and no account, which is tested by `UserService_SignInExternalAsync_MissingIdentity_IsInvalid`; an issuer and
a subject at their limit are accepted and one character more is refused, which is tested by
`UserService_SignInExternalAsync_IdentityLengthLimits_AreEnforced`. A disabled account is refused as `Disabled`, has no
user in the answer, and stays unchanged, which is tested by `UserService_SignInExternalAsync_DisabledAccount_IsRefused`.

**UserService_SignInExternalAsync_Candidate_IsReducedToAValidUsername**: A theory over suggested names: valid names are
kept, markup characters are dropped, a name without usable characters or a null gives `user`, a single character gives
`user-a`, and a long name is cut to 44 characters. This scenario is tested by
`UserService_SignInExternalAsync_Candidate_IsReducedToAValidUsername`.

**UserService_SignInExternalAsync_DisplayName_IsSanitized**: A theory over display names: control characters, a
bidirectional override, and zero-width characters are removed, white space is collapsed and trimmed, and a blank or
null name is replaced by the user name. This scenario is tested by
`UserService_SignInExternalAsync_DisplayName_IsSanitized`.

**UserService_SignInExternalAsync_LongDisplayName_IsBounded**: A display name of 1000 characters is cut to 100. This
scenario is tested by `UserService_SignInExternalAsync_LongDisplayName_IsBounded`.

**UserService_SignInExternalAsync_AllNamesTaken_UsesNameFromIdentity**: With the plain name and the numbered names up to
`-50` taken by other accounts, the new account gets a generated name that starts with `oidc-` and has 17 characters,
and no other account is used. This scenario is tested by
`UserService_SignInExternalAsync_AllNamesTaken_UsesNameFromIdentity`.

**UserService_ExternalAccount_EveryPasswordPathIsRefused**: For an external account, `AuthenticateAsync` with an empty
and with a hash-like password, and `ReauthenticateAsync` give no user, `ChangeOwnPasswordAsync` is refused, and
`SetPasswordAsync` throws the rule `PasswordInvalid`; the failure count stays 0. This scenario is tested by
`UserService_ExternalAccount_EveryPasswordPathIsRefused`.

**UserService temporary password scenarios**: The temporary password of an account created for a project is modeled
in `MustChangePasswordTests.cs` by a user whose must-change flag is set, over `FakeUserStore`, `FakeReviewStore`,
`FakeAuditStore`, and `FakePasswordHasher`. Signing in with the temporary password returns the user with the flag set,
which is tested by `UserService_AuthenticateAsync_TemporaryPassword_SignsIn`. A re-authentication with the temporary
password returns no user and records one `signature.reauth-failed` entry whose detail says `password change pending`
and does not contain the password, which is tested by
`UserService_ReauthenticateAsync_TemporaryPassword_IsRefusedAndAudited`. Changing the own password to a new one clears
the flag, after which the new password re-authenticates and the account has access and may list its tokens, which is
tested by `UserService_ChangeOwnPasswordAsync_NewPassword_ClearsTheFlagAndGivesAccess`. Choosing the temporary password
again is refused with `PasswordInvalid` and keeps the flag, which is tested by
`UserService_ChangeOwnPasswordAsync_SameAsTemporary_IsRefusedAndKeepsTheFlag`. A password set by an administrator
through `SetPasswordAsync` leaves the flag of a flagged account set and of a normal account clear, which is tested by
`UserService_SetPasswordAsync_ByAdministrator_LeavesTheFlagAlone`.
