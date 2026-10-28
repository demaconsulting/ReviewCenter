## Storage

### Verification Approach

The `Storage` subsystem is verified by subsystem tests defined in `StorageSubsystemTests.cs`. The tests build the
real `DatabaseSetup`, `ReviewCenterDbContext`, `ReviewStore`, `UserStore`, `RepositoryStore`, and
`PersonalAccessTokenStore` over a real SQLite
database file, and
run the real
`ProjectService`, `ReviewService`, `ThreadService`, `ChecklistService`, `UserService`, `SigningService`,
`RepositoryService`, and `PersonalAccessTokenService` of the `Core`
subsystem over
them, so that
the subsystem is
verified in the way
the application uses it. Each test uses a new database file in a private temporary folder, supplied by the test
helper `TestDatabase`. The units are verified in detail by their own unit tests, which also use real SQLite
databases; no database substitute is used anywhere, because the behavior of the database (keys, constraints,
ordering, and binary content) is part of what is verified. The only substitutes are the fast test password hasher of
the user scenario, because the strength of the real hasher is not what the subsystem proves, and a trivial token
protector in the repository scenario, because the encryption is verified in the `ContentSources` subsystem.

### Test Environment

Tests create temporary SQLite database files in a private temporary folder, which is removed after the test run.
The native SQLite library must be loadable on the test platform (it is supplied by the provider package).

### Acceptance Criteria

- All subsystem tests pass with zero failures.
- Data written through the services is still present, byte for byte and with the same fingerprint, after the
  database is closed and opened again.
- A duplicate review identifier is refused and the original review is unchanged.
- Comment threads, their comments (in posting order), their anchors, and their state, written through the services,
  are still present after the database is closed and opened again, and each comment keeps the user name of its author.
- Users written through the user service, including the system administrator flag, and a changed password, are still
  present after the database is closed and opened again; the user signs in with the changed password only.
- The checklist snapshot of a review, its answers, and its section decisions, written through the services, are
  still present after the database is closed and opened again.
- A reviewer slot and the signature made for it, written through the services, are still present after the database
  is closed and opened again, and the signature is still current, so the review is still approved.
- A review for a project that does not exist is refused, by the services and by the database itself, and the stored
  data is unchanged.
- A registered repository of a project, with its token flag and its disabled state, written through the service, is
  still present after the database is closed and opened again, and a disabled repository is not offered for selection.
- An unsupported provider is refused with an error that names it, and no database is created.
- Project memberships are stored, kept unique per user and project, audited with the project, and refused for an
  unknown project; the last enabled project administrator cannot be removed or demoted when the caller requires another
  administrator, also when two changes race; and an account is stored with its membership and its audit entry together
  or not at all; these are verified in the unit tests of `ReviewStore` and `UserStore` on the real database.
- An account of an identity provider is stored with its issuer and subject, is found only by the exact pair, and a
  second account with the same pair is refused; this is verified in the unit tests of `UserStore` on the real database.
- The evidence settings of a project are stored with the project (the defaults for a new project), are changed only
  when the stored settings still equal the expected ones, and are stored with their audit entry or not at all; this is
  verified in the unit tests of `ReviewStore` on the real database.
- Personal access tokens written through the service, with one of them revoked, are still present after the database is
  closed and opened again: the value of the kept token still authenticates its owner, the revoked token is refused,
  and no
  row holds a token value.
- A review from a repository, with its source and the change kind and baseline content of each file, is stored and
  read back without file bytes in the review, and its baseline content is read on request; the scenario is verified
  by the unit tests of `ReviewStore` on the real database (`ReviewStore_GetReviewAsync_RepositoryReview_ReturnsSourceAndKindsWithoutBytes`
  and `ReviewStore_GetBaselineContentAsync_StoredFiles_ReturnsBaselineBytesOrNull`); the review-set a review was planned
  for is stored with it (`ReviewStore_AddReviewAsync_LongestReviewSetId_RoundTrips`).
- Every audited change is stored together with its audit entry; changes made at the same moment through different
  stores give one linear, intact chain, and the audit store offers no update or delete.

### Test Scenarios

**StorageSubsystem_ServicesOverDatabase_ReopenedDatabase_KeepsProjectAndReview**: A project and a review of three
files are created through the services over a new database, which is then closed and opened again. The project,
the review with its known fingerprint and three files, and the exact bytes of the binary file are read back. This
scenario is tested by `StorageSubsystem_ServicesOverDatabase_ReopenedDatabase_KeepsProjectAndReview`.

**StorageSubsystem_ServicesOverDatabase_ReopenedDatabase_KeepsThreadsAndComments**: A review is created through the
services over a new database; a thread on line 1 is started, answered, and resolved through `ThreadService`; the
database is closed and opened again. The anchor, the state "Resolved", and both comments in posting order, each with
the user name of its author, are read
back. This scenario is tested by `StorageSubsystem_ServicesOverDatabase_ReopenedDatabase_KeepsThreadsAndComments`.

**StorageSubsystem_Users_PersistAcrossReopen**: A system administrator is created through `UserService` over a new
database and its password is changed; the database is closed and opened again. The user signs in in another case of
the user name with the new password and is still a system administrator, and the old password is refused. This
scenario is tested by `StorageSubsystem_Users_PersistAcrossReopen`.

**StorageSubsystem_Checklist_PersistsSnapshotAnswersAndSectionsAcrossReopen**: A review is created through the
services over a new database, one check is answered Fail with a justification, and one section is marked as not
applicable with a reason; the database is closed and opened again. The checklist is read with the default
version, the answer with its justification, the reason of the section, and the progress 1 of 53. This scenario is
tested by `StorageSubsystem_Checklist_PersistsSnapshotAnswersAndSectionsAcrossReopen`.

**StorageSubsystem_Signatures_PersistAcrossReopenAndStayCurrent**: Two reviewers and a review with every check
answered Pass are created through the services over a new database; a slot is added through `SigningService` and the
alternate reviewer signs it with the correct password; the database is closed and opened again. The approval is read
again: the review is approved, the single slot is signed by the alternate reviewer at the saved time, and the
signature is current for the unchanged record. This scenario is tested by
`StorageSubsystem_Signatures_PersistAcrossReopenAndStayCurrent`.

**StorageSubsystem_Repositories_PersistAcrossReopen**: A system administrator is created over a new database, a
project is created, and a private repository with a user name and a token is registered in it through
`RepositoryService`, then disabled; the database is
closed and opened again. The repository is listed for the project with its name, user name, the token flag set, and
the disabled state, and no repository is offered for selection. This scenario is tested by
`StorageSubsystem_Repositories_PersistAcrossReopen`.

**StorageSubsystem_AccessTokens_PersistAcrossReopen**: A user and two tokens are created through the services over a new
database and one token is revoked; the database is closed and opened again. The kept token still authenticates its owner,
the revoked token is refused, both are listed, the revoked one with its revocation time, and no row holds a token value.
This scenario is tested by `StorageSubsystem_AccessTokens_PersistAcrossReopen`.

**StorageSubsystem_UnsupportedProvider_Opening_FailsWithClearErrorAndCreatesNothing**: A database is opened with
the provider name `Oracle` and a new file path; an `InvalidOperationException` that names the provider is thrown
and the file does not exist. This scenario is tested by
`StorageSubsystem_UnsupportedProvider_Opening_FailsWithClearErrorAndCreatesNothing`.

**StorageSubsystem_ServicesOverDatabase_DuplicateReviewId_IsRefusedAndKeepsOriginal**: A second review with the id
of an existing review in the same project is created through the services; a `DomainRuleException` for
`ReviewIdDuplicate` is thrown, and the original review still has its title and fingerprint and is the only review.
This scenario is tested by `StorageSubsystem_ServicesOverDatabase_DuplicateReviewId_IsRefusedAndKeepsOriginal`.

**StorageSubsystem_ServicesOverDatabase_UnknownProject_IsRefusedAndKeepsData**: A project with one review exists.
A review for a project id that does not exist is created through the services, and a review for that id is added
directly to the store. The service throws a `DomainRuleException` for `ProjectNotFound`, the store throws a
`DbUpdateException` because of the foreign key, no review and no project exist for the unknown id, and the existing
project and its review are unchanged. This scenario is tested by
`StorageSubsystem_ServicesOverDatabase_UnknownProject_IsRefusedAndKeepsData`.

**Project memberships**: The round trip, the uniqueness, the role change and removal, the audit with the project, and
the refusal of an unknown project are tested by `ReviewStore_AddMembershipAsync_Membership_RoundTripsAndIsListed`,
`ReviewStore_AddMembershipAsync_SecondMembership_IsRejected`,
`ReviewStore_SetAndRemoveMembershipAsync_ChangeRoleAndRemove_ReportWhetherFound`,
`ReviewStore_Membership_Changes_AreAuditedWithTheProject`, and
`ReviewStore_AddMembershipAsync_UnknownProject_IsRefusedByTheDatabase`, all defined in `ReviewStoreMembershipTests.cs`.
The last project administrator rule, including two removals made at the same time, is tested by
`ReviewStore_RemoveMembershipAsync_TwoAdminsRemoveEachOther_ExactlyOneSucceeds`,
`ReviewStore_MembershipChanges_RemoveRacesDemote_LeavesAnAdministrator`,
`ReviewStore_RemoveMembershipAsync_LastAdmin_RefusedAndRolledBackUnlessExempt`, and
`ReviewStore_RemoveMembershipAsync_OtherAdminDisabled_IsRefused`, and the atomic storing of an account with its
membership and its audit entry by `UserStore_AddToProjectAsync_StoresAccountMembershipAndOneEventTogether` and
`UserStore_AddToProjectAsync_Failure_StoresNothing`, all defined in `ProjectMembersStoreTests.cs`. The listing of the
audit trail for one project only is tested by `AuditStore_ListAsync_ProjectOnly_IsIsolatedAndTheChainStillVerifies`
in the same file.

**External identity**: An account of an identity provider is found only by the exact pair of issuer and subject, and a
second account with the same pair is refused, while any number of local accounts (no pair) are stored; these are tested
by `UserStore_GetByExternalAsync_FindsOnlyTheExactPair` and `UserStore_AddAsync_SameExternalIdentityTwice_IsRefused`,
defined in `UserStoreTests.cs` and verified on the real database.

**Evidence settings**: A new project has the default evidence settings, a change is stored for its own project only
together with its audit entry, and a change based on settings that are no longer stored changes nothing; these are
tested by `ReviewStore_AddProjectAsync_NewProject_HasDefaultEvidenceSettings`,
`ReviewStore_UpdateEvidenceSettingsAsync_Change_IsPersistedAndAudited`, and
`ReviewStore_UpdateEvidenceSettingsAsync_ExpectedDiffers_ChangesNothing`, all defined in
`EvidenceSettingsStoreTests.cs`.

**Audit trail transaction**: Changes are made at the same moment through different stores that share the
process-wide gate; the sequence numbers are consecutive and the chain is intact. This scenario is tested by
`AuditStore_ConcurrentChangesFromDifferentStores_ProduceLinearChain`. The public surface of the audit store has no
method that updates or deletes an entry, which is tested by `AuditStore_PublicSurface_OffersNoUpdateOrDelete`. The
reading and verification of stored entries is verified in the unit tests of `AuditStore`.
