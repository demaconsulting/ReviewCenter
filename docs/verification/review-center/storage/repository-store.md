### RepositoryStore

#### Verification Approach

`RepositoryStore` is verified with tests defined in `RepositoryStoreTests.cs`, which run against a real SQLite database
file created by the test (`TestDatabase`), because the properties to verify are those of the database: the unique name
index with its case-insensitive collation, the transaction shared with the audit entry, and the stored token column.
The tests store and read through the real store, and read the audit entries through the real `AuditStore`. The absence of
a token member in the read model is checked on the public surface by reflection. The `Core` types are used directly, so
no fake is needed.

#### Test Environment

N/A - standard test environment (temporary SQLite databases are created by the test helper).

#### Acceptance Criteria

- All tests pass with zero failures.
- A repository round-trips with every field and a database-assigned identity, and its audit entry is stored in the same
  transaction.
- The encrypted token is read only through the dedicated method, and is null when there is none.
- A name that differs only in case is `RepositoryNameTaken` for adding and for renaming, and the failed change leaves no
  audit entry.
- An update changes the fields and keeps, replaces, or clears the token as asked.
- Disabling keeps the registration; updating an unknown repository is `RepositoryNotFound`.
- The list is ordered by name without regard to case, and an unknown identity reads as null.
- A project lists only its own repositories, and the same name may be used in two projects but only once within one.
- The read model has no token member other than the flag.
- Every change stores one audit entry, and a refused name or an audit entry that cannot be stored leaves the
  repositories as they were.

#### Test Scenarios

**RepositoryStore_AddAsync_Repository_RoundTripsAndAudits**: A repository is added with its audit draft; it reads back with
every field, an assigned identity, and equal creation and update times, and the audit entry exists. This scenario is
tested by `RepositoryStore_AddAsync_Repository_RoundTripsAndAudits`.

**RepositoryStore_GetEncryptedTokenAsync_ReturnsStoredText**: The stored encrypted text is returned by the dedicated method,
and null for a repository without a token. This scenario is tested by `RepositoryStore_GetEncryptedTokenAsync_ReturnsStoredText`.

**RepositoryStore_NameOtherCase_ThrowsRepositoryNameTaken**: A name that differs only in case is refused with
`RepositoryNameTaken` when adding and when renaming, and no audit entry is stored for the failed change. This scenario is
tested by `RepositoryStore_NameOtherCase_ThrowsRepositoryNameTaken`.

**RepositoryStore_UpdateAsync_TokenActions_KeepReplaceClear**: An update changes the fields and keeps, replaces, or clears
the token as asked. This scenario is tested by `RepositoryStore_UpdateAsync_TokenActions_KeepReplaceClear`.

**RepositoryStore_UpdateAsync_Disable_KeepsTheRepository**: Disabling a repository keeps the registration: the
repository is still listed and is disabled. This scenario is tested by
`RepositoryStore_UpdateAsync_Disable_KeepsTheRepository`.

**RepositoryStore_UpdateAsync_UnknownRepository_ThrowsRepositoryNotFound**: Updating an unknown repository names
`RepositoryNotFound`. This scenario is tested by
`RepositoryStore_UpdateAsync_UnknownRepository_ThrowsRepositoryNotFound`.

**RepositoryStore_ListAsync_OrdersByNameIgnoringCase**: The list is ordered by name without regard to case, and an unknown
identity reads as null. This scenario is tested by `RepositoryStore_ListAsync_OrdersByNameIgnoringCase`.

**RepositoryStore_ListAsync_ScopedToProject_AndNamesAreUniquePerProject**: The same name is added to two projects and
accepted in both; each project lists only its own repository (by the project identity of the read model); a name that
differs only in case from one in the same project is refused with `RepositoryNameTaken`. This scenario is tested by
`RepositoryStore_ListAsync_ScopedToProject_AndNamesAreUniquePerProject`.

**RepositoryStore_ReadModel_HasNoTokenMember**: The read model has no member that can hold a token other than the flag.
This scenario is tested by `RepositoryStore_ReadModel_HasNoTokenMember`.

**RepositoryStore_UpdateAsync_Change_StoresOneAuditEntry**: A repository is added and then renamed with an audit draft.
The trail holds the creation entry and one update entry. This scenario is tested by
`RepositoryStore_UpdateAsync_Change_StoresOneAuditEntry`.

**RepositoryStore_NameTaken_AddAndRename_LeaveTheDataUnchanged**: A duplicate name is added and another repository is
renamed to a duplicate name; both are refused and the list of the project is equal to the list before. This scenario is
tested by `RepositoryStore_NameTaken_AddAndRename_LeaveTheDataUnchanged`.

**RepositoryStore_AddAsync_AuditInsertFails_StoresNoRepository**: A trigger makes the database refuse every audit entry
and a repository is added. The add fails and, after the trigger is dropped, no repository and no entry exist. This
scenario is tested by `RepositoryStore_AddAsync_AuditInsertFails_StoresNoRepository`.

**RepositoryStore_UpdateAsync_AuditInsertFails_LeavesTheRepositoryUnchanged**: A repository with a token is added,
then a
trigger makes the database refuse every audit entry and the repository is renamed, disabled, and cleared of its token.
The update fails, the repository and the token are as before, and only the creation entry exists. This scenario is
tested by `RepositoryStore_UpdateAsync_AuditInsertFails_LeavesTheRepositoryUnchanged`.
