### UserStore

#### Verification Approach

`UserStore` is verified with unit tests defined in `UserStoreTests.cs`. Each test opens a new SQLite database file
in a private temporary folder, supplied by the test helper `TestDatabase`, and creates the schema with the real
`DatabaseSetup`, so the keys, the unique index, and the ordering of the real database are part of what is verified. A
database substitute is not used. Tests add, read, change, and list users and compare the values read back.

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

**Audit drafts**: The store takes an audit draft with the creation of a user and with an update; an update without a
draft is allowed and is not audited. The tests of this unit build the store with the real `AuditAppender`. The
transaction that stores a change with its audit entry is verified in the tests of `AuditStore`.
