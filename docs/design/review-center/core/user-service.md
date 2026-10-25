### UserService

![Core Structure](CoreView.svg)

#### Purpose

`UserService` owns the rules for local user accounts: it creates users, checks a user name and password for sign-in,
enables and disables users, sets roles, changes passwords, and protects accounts against password guessing and against
losing the last administrator. It is also the home of the supporting types of identity: `User`, `UserRole`, `Actor`,
and the interfaces `IPasswordHasher` and `IUserStore`, together with the user rules of `DomainRule` and the user
limits of `ReviewLimits`.

#### Data Model

**User**: a data class of `Id` (assigned by the store), `Username`, `DisplayName`, `Role`, `Enabled`, `PasswordHash`,
`FailedLoginCount`, `LockoutUntil` (`null` when the user is not locked out), and `Created` (UTC). `Username`,
`DisplayName`, and `Created` are fixed once the user exists; the role, the enabled flag, the hash, and the lockout
state have setters, so the service can change a copy it has read and give it back to the store. The password hash is
never shown, logged, or put into a message, and the password itself is never stored.

**UserRole**: a closed enumeration with the values `Reviewer` and `Admin`. It is stored by name, so adding a role
never renumbers stored rows. Any signed-in user can work on projects and reviews; only `Admin` can manage users.

**Actor**: an immutable record of `Username` and `DisplayName`: the signed-in person on whose behalf a service records
a comment or a checklist answer. `ThreadService` and `ChecklistService` accept an `Actor`, never free text, so the
author of a record always comes from a signed-in user and cannot be supplied by a form field.

**IPasswordHasher**: the interface with `Hash(password)` and `Verify(hash, password)`. It keeps `Core` free of any
web framework. It is implemented by `PasswordHasher` in the `Identity` subsystem, and tests of `Core` use a trivial
fake.

**IUserStore**: the persistence interface of users, with the operations `AddAsync`, `GetByUsernameAsync`,
`ListAsync`, and `UpdateAsync`. Users have their own interface so that the review store stays about reviews. It is
implemented by `UserStore` in the `Storage` subsystem. Every read returns a new `User` object, so a caller can change
it and pass it to `UpdateAsync` without touching shared state.

**Rules for the input**:

- The user name has 3 to 50 characters, each an ASCII letter or digit, a dot, an underscore, or a hyphen. It is not
  trimmed, and it is unique without regard to case (`Ann` and `ann` are the same name). It is shown and stored as it was
  entered.
- The display name is trimmed and has 1 to 100 characters.
- The password has 12 to 128 characters and is taken exactly as given: it is not trimmed and has no composition
  rules (no required digits or symbols). The upper limit keeps the hashing time bounded.
- The role must be a defined value; anything else is a programming error and throws `ArgumentOutOfRangeException`.

**Lockout**: five consecutive wrong passwords lock the account for 5 minutes. When the fifth failure is counted, the
lock time is set and the counter restarts at 0. While the lock lasts, every attempt is refused without counting, even
with the right password. A right password resets the counter. The lockout is per user name, not per source address.

**Last administrator**: a change that would leave no enabled administrator is refused: disabling the last enabled
administrator and demoting the last enabled administrator. Only an enabled administrator counts, so a disabled
administrator does not satisfy the rule, and a change to anyone else is not checked.

**Serialization**: the operations that change an existing user, and the sign-in check (which counts failures), run one
at a time under a semaphore, which makes the last-administrator check and the failure counter free of races. This only
holds for a single instance, so the service is registered as a singleton. Creating a user is not under the semaphore;
the unique index of the store covers two requests racing for the same name.

**Dummy hash**: when the user name is unknown, the service still verifies the password against a hash of no account
(created on first use), so the response time does not show whether a user name exists.

**DomainRule values** added for users: `UserNameInvalid`, `UserNameTaken`, `DisplayNameInvalid`, `PasswordInvalid`,
`CurrentPasswordIncorrect`, `UserNotFound`, and `LastAdmin`. The values `CommentAuthorInvalid` and
`ChecklistAnsweredByInvalid` no longer exist, because the author is no longer typed text (see the `Core` subsystem).

**ReviewLimits constants** added for users: `MinUsernameLength` (3), `MaxUsernameLength` (50),
`MaxDisplayNameLength` (100), `MinPasswordLength` (12), and `MaxPasswordLength` (128). `MaxAuthorLength` (100) now
limits the display name stored with a comment, an answer, or a section decision. The lockout settings (5 failures,
5 minutes) are private constants of the service.

#### Key Methods

**CreateAsync**: Creates an enabled user.

- *Parameters*: `string? username`, `string? displayName`, `UserRole role`, `string? password`, and a cancellation
  token.
- *Returns*: `Task<User>` — the stored user with its assigned identity and the creation time from the clock; only the
  hash of the password is kept.
- *Postconditions*: Nothing is stored when a rule fails.

The rules are checked in this order, and the first violation is reported: the user name (`UserNameInvalid`); the
display name (`DisplayNameInvalid`); the password (`PasswordInvalid`); and then the store, which refuses a name that
exists (`UserNameTaken`). The checks that need no I/O come first so that a bad request fails cheaply, and the password
is hashed only after the input is valid.

**AuthenticateAsync**: Checks a user name and password for sign-in.

- *Parameters*: `string? username`, `string? password`, and a cancellation token.
- *Returns*: `Task<User?>` — the user when the account exists, is not locked out, is enabled, and the password is
  right; otherwise `null`.
- *Postconditions*: A right password resets the failure counter. A wrong password counts as a failure and may start the
  lockout.

Every failure gives the same `null`: unknown user, wrong password, disabled account, and locked account, so a caller
cannot tell the cases apart. The order of the checks is: user exists (otherwise the dummy hash is verified and the
result is `null`), not locked out (otherwise the dummy hash is verified and the result is `null`), password right
(otherwise the failure is counted), and enabled. A disabled user with a wrong password is therefore counted like any
other wrong password, and a disabled user with the right password is refused without counting.

**ListAsync**: Lists all users ordered by user name. **GetAsync**: Reads a user by user name, without regard to case;
`null` when there is none.

**SetEnabledAsync**: Enables or disables a user.

- *Parameters*: `string username`, `bool enabled`, and a cancellation token.
- *Returns*: `Task`.
- *Postconditions*: The enabled flag is stored. Disabling stops sign-in; the work already recorded by the user is kept.

Reports `UserNotFound` for an unknown user and `LastAdmin` when the change would disable the last enabled
administrator.

**SetRoleAsync**: Changes the role of a user. Reports `UserNotFound` for an unknown user and `LastAdmin` when the
change would demote the last enabled administrator.

**ChangeOwnPasswordAsync**: Changes the password of a user who knows the current one.

- *Parameters*: `string username`, `string? currentPassword`, `string? newPassword`, and a cancellation token.
- *Returns*: `Task`.
- *Postconditions*: The new hash is stored and the failure counter and lockout are cleared. Nothing is stored when a
  rule fails.

The new password is checked first (`PasswordInvalid`); then the user must exist (`UserNotFound`). A locked-out account
is refused like a wrong password. A wrong current password counts as a failed sign-in, so this operation is not a free
way to guess passwords, and is reported as `CurrentPasswordIncorrect`. Other sessions of the user are not ended.

**SetPasswordAsync**: Sets the password of a user without the current one (for an administrator). The password is
checked (`PasswordInvalid`), an unknown user is reported as `UserNotFound`, and the failure counter and lockout of the
user are cleared, so it also ends a lockout. Other sessions of the user are not ended.

**Audit**: The operations that change a user take the `Actor` and record `user.created`, `user.enabled`,
`user.disabled`, `user.role-changed`, and `user.password-set-by-admin`; changing one's own password records
`user.password-changed` with that user as the actor. These drafts are saved by the store in the same transaction as the
change. The command line passes `Actor.CommandLine`. The constructor takes an optional `AuditService` (the web server
and the command line supply it): with it, a failed sign-in records `auth.sign-in-failed` (detail `wrong password`,
`account disabled`, or `unknown user`, the last as the actor text `unknown user`) and the fifth failure also records
`auth.locked-out`. These events change no data of their own and are recorded best-effort through `TryRecordAsync`,
awaited and logged on failure, so a broken trail can never block a sign-in. The update of the failure counter and the
lockout state is itself not audited (no draft). Two limits are known and deliberate: an attempt on an account that is
already locked is refused before the password is checked and is not recorded, and a wrong current password at signing
or on the account page is reported by the same code path and appears as `auth.sign-in-failed`.

#### Error Handling

Rule violations raise `DomainRuleException` with the matching `DomainRule`. A null store, hasher, or clock at
construction raises `ArgumentNullException`, and so does a null user name where one is required. A role that is not a
defined value raises `ArgumentOutOfRangeException`. A failed sign-in is not an error: `AuthenticateAsync` returns
`null`. Store failures propagate unchanged. The messages never contain a password or a hash.

#### Dependencies

- **IUserStore** — reads and stores users (injected).
- **IPasswordHasher** — hashes and verifies passwords (injected).
- **TimeProvider** — supplies the creation and lockout times (injected).

#### Callers

- **SignIn** — checks the password at sign-in and reads the user to validate the cookie of each request.
- **AccountPage** — changes the password of the signed-in user.
- **UsersPage** — lists, creates, enables, disables, and changes users.
- **UserCommand** — creates the first administrator and other users from the command line.
- **ServerHost** — registers the service as a singleton.
