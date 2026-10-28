### UserStore

#### Verification Approach

`UserStore` is verified with unit tests defined in `UserStoreTests.cs`. Each test opens a new SQLite database file
in a private temporary folder, supplied by the test helper `TestDatabase`, and creates the schema with the real
`DatabaseSetup`, so the keys, the unique index, and the ordering of the real database are part of what is verified. A
database substitute is not used. Tests add, read, change, and list users and compare the values read back. The atomic
creation of an account with its membership and its audit entry is verified in `ProjectMembersStoreTests.cs`, which
uses the same real database through the helper `TestStore`.

#### Test Environment

N/A - standard test environment (temporary SQLite databases are created by the test helper).

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A user is stored with every field, and its identity is assigned by the database; a supplied identity is ignored.
- A user name that differs from an existing one only in case is refused with the rule `UserNameTaken`, and a lookup
  by user name ignores case; an unknown user name gives no user.
- A change of the system administrator flag, the enabled flag, the password hash, the failure count, and the lockout time
  is stored.
- Users are listed ordered by user name.
- A user added to a project is stored with its must-change-password flag, its membership with the given role, and
  exactly one audit entry that names the project; a user name that is taken, or a project that does not exist, stores
  no account, no membership, and no entry, and leaves the gate free for the next change.

#### Test Scenarios

**UserStore_AddAsync_User_RoundTripsEveryField**: A user with every field set and a supplied identity is added; the
identity is the database's, and the user name, display name, system administrator flag, enabled flag, password hash,
failure count,
lockout time, and creation time are read back unchanged. This scenario is tested by
`UserStore_AddAsync_User_RoundTripsEveryField`.

**UserStore_AddAsync_UsernameOtherCase_ThrowsUserNameTakenAndLookupIgnoresCase**: A user "Ann" exists; adding "ANN"
throws the rule `UserNameTaken`, a lookup of "aNN" finds the original, a lookup of an unknown name gives no user, and
only one user is stored. This scenario is tested by
`UserStore_AddAsync_UsernameOtherCase_ThrowsUserNameTakenAndLookupIgnoresCase`.

**UserStore_UpdateAsync_ChangedFields_AreStored**: The system administrator flag, enabled flag, password hash, failure
count, and
lockout time of a stored user are changed and updated; they are read back as changed. This scenario is tested by
`UserStore_UpdateAsync_ChangedFields_AreStored`.

**UserStore_ListAsync_Users_AreOrderedByUsername**: Three users are added out of order; they are listed ordered by
user name. This scenario is tested by `UserStore_ListAsync_Users_AreOrderedByUsername`.

**UserStore_AddToProjectAsync_StoresAccountMembershipAndOneEventTogether**: A user whose must-change-password flag is
set is added to a project as project administrator; the user is read back with the flag, the membership has the
role, and the audit trail holds exactly one `project.account-created` event of the project, built from the stored
user. This scenario is tested by `UserStore_AddToProjectAsync_StoresAccountMembershipAndOneEventTogether`.

**UserStore_AddToProjectAsync_Failure_StoresNothing**: A user name that is already taken is added to a project, and a
user is added to a project that does not exist; the first throws `UserNameTaken`, and neither stores an account, a
membership, or an event, and a later add still succeeds because the gate was released. This scenario is tested by
`UserStore_AddToProjectAsync_Failure_StoresNothing`.

**Audit drafts**: The store takes an audit draft with the creation of a user and with an update; an update without a
draft is allowed and is not audited. The tests of this unit build the store with the real `AuditAppender`. The
transaction that stores a change with its audit entry is verified in the tests of `AuditStore`.
