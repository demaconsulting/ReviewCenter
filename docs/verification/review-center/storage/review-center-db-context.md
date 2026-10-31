### ReviewCenterDbContext

#### Verification Approach

`ReviewCenterDbContext` is verified with the unit tests of `ReviewStoreTests.cs`, `ReviewStoreSigningTests.cs`,
`UserStoreTests.cs`, `RepositoryStoreTests.cs`, `PersonalAccessTokenStoreTests.cs`, `ReviewStoreRepositoryTests.cs`,
`ReviewStoreMembershipTests.cs`, `EvidenceSettingsStoreTests.cs`, `ProjectChecklistStoreTests.cs`,
`TotpStoreTests.cs`, and `DatabaseSetupTests.cs`, which
exercise the context through the units that use it. The context has no behavior that can be reached without a
database, and its job is to produce the right schema, so the tests run against real SQLite database files in a
private temporary folder. No mocking or in-memory substitute is applied. Each test uses its own database file.

#### Test Environment

Tests create temporary SQLite database files, which are removed after the test run. The native SQLite library must
be loadable on the test platform.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The tables for projects, reviews, and review files are created, and reviews are keyed by project and review
  identifier, so the same review identifier can exist in two projects.
- File content, including binary content, carriage returns, and empty files, is stored byte for byte.
- A duplicate review key and a review of an unknown project are refused by the database.
- The tables for comment threads and comments are created, and a thread for a file that is not in the review is
  refused by the database.
- The tables for the checklist snapshot, section states, and answers are keyed so that a review has one state per
  section and one answer per check.
- The comments, answers, and section states keep the user name of the person who made them next to the display name.
- The table for users stores every field of a user, including the system administrator flag, and its unique index on
  the user name ignores case.
- The users table stores the external issuer and subject, which are read back exactly, and its unique index on the
  pair refuses a second user with the same pair while any number of users without one are allowed.
- The tables for reviewer slots and signatures are created; the database refuses a slot for a review that does not
  exist, refuses to remove a slot that has a signature, and refuses a second signature of the same slot with the same
  digest; the role, digest, and meaning of a signature are read back as stored.
- The table for repositories stores every field of a registration including the project and the encrypted token, and
  its unique index on the project and the name ignores case, so the same name can be used in two projects.
- The table for project memberships holds one row per user and project, refuses a second row for the same pair, and
  refuses a row for an unknown project.
- The seven evidence settings columns of a project are created with the default settings as their defaults, so a new
  project reads back with the defaults, and a changed setting is stored and read back from its own column.
- The `ChecklistDefinition` column of a project is created as nullable text with no length limit: a new project reads
  back with none, and a definition of the full size limit is stored and read back unchanged.
- The `DueDate` column of a review is created as nullable text holding the calendar day: a review without a date reads
  back with none and a date reads back as the same day.
- The source columns of a review and the change kind and baseline columns of a file are created, stored, and read back
  as written, with the baseline content as binary data and no value for an uploaded review.
- The table for personal access tokens has a unique index on the hash and no column for the value of a token, and a
  review keeps its creator and whether it was created through the API.
- The tables for the second factor hold one enrollment per user and the recovery codes of a user; the database refuses
  two identical recovery code hashes of one user and allows the same hash for two users, and deleting a user deletes the
  enrollment and its codes.
- The table for review reopens holds the reopen history of a review (who, when, why, and the previous digest), with the
  identity assigned by the database; it is removed together with its review; and a new project reads back with the
  `RequireIndependentReviewers` column set (a column of the `Projects` table without a database default, whose value
  comes from the entity). The `RequireNonAuthorChecklistAnswers` and `RequireNonReopenerSigning` columns of the project
  table are created the same way and read back with the project.

#### Test Scenarios

**ReviewStore_GetReviewAsync_RepositoryReview_ReturnsSourceAndKindsWithoutBytes**: A review from a repository is
stored and read back with its source columns and the change kind and baseline metadata of every file. This scenario
is tested by `ReviewStore_GetReviewAsync_RepositoryReview_ReturnsSourceAndKindsWithoutBytes`, the baseline bytes by
`ReviewStore_GetBaselineContentAsync_StoredFiles_ReturnsBaselineBytesOrNull`, an uploaded review without these
values by `ReviewStore_GetRecordAsync_UploadReview_HasUploadSource`, and the longest review-set identifier by
`ReviewStore_AddReviewAsync_LongestReviewSetId_RoundTrips`; all are defined in
`ReviewStoreRepositoryTests.cs`.
**DatabaseSetup_EnsureCreated_MissingFolder_CreatesFolderFileAndTables**: A new database is created and the
project, review, review file, thread, and comment tables can be queried. This scenario is tested by
`DatabaseSetup_EnsureCreated_MissingFolder_CreatesFolderFileAndTables`.

**ReviewStore_AddReviewAsync_SameIdInTwoProjects_KeepsThemApart**: Reviews with the same identifier are stored in
two projects; both exist separately. This scenario is tested by
`ReviewStore_AddReviewAsync_SameIdInTwoProjects_KeepsThemApart`.

**ReviewStore_AddReviewAsync_BinaryCrLfAndEmptyFiles_StoresBytesVerbatim**: Files with binary content, carriage
returns, and no content are stored and read back with identical bytes. This scenario is tested by
`ReviewStore_AddReviewAsync_BinaryCrLfAndEmptyFiles_StoresBytesVerbatim`.

**ReviewStore_AddReviewAsync_DuplicateKey_ThrowsReviewIdDuplicate**: A review with an existing key is refused by
the database and reported as a duplicate. This scenario is tested by
`ReviewStore_AddReviewAsync_DuplicateKey_ThrowsReviewIdDuplicate`.

**ReviewStore_AddReviewAsync_UnknownProject_IsNotStored**: A review of a project that does not exist is refused
by the database and nothing is stored. This scenario is tested by
`ReviewStore_AddReviewAsync_UnknownProject_IsNotStored`.

**ReviewStore_AddThreadAsync_UnknownFile_IsRefusedByDatabase**: A thread for a file that is not in the review is
refused by the database (the foreign key is enforced) and nothing is stored. This scenario is tested by
`ReviewStore_AddThreadAsync_UnknownFile_IsRefusedByDatabase`.

**ReviewStore_AddThreadAsync_WholeFileAndRange_RoundTripsWithComments**: Threads with and without a line range are
stored with their comments and read back unchanged, including the display name and user name of the comment author,
which verifies the thread and comment tables. This scenario is
tested by `ReviewStore_AddThreadAsync_WholeFileAndRange_RoundTripsWithComments`.

**ReviewStore_AddThreadAsync_Side_IsStoredAsText**: A thread on the baseline side is stored, and a raw query of the
`Side` column of the `Threads` table returns the text "Baseline", which verifies the mapping of the side to text.
This scenario is tested by `ReviewStore_AddThreadAsync_Side_IsStoredAsText`.

**ReviewStore_SetChecklistAnswer_UpsertsSingleRowPerItem**: A check is answered twice and another check once; the
database holds one row per check, the first with the latest answer. This scenario is tested by
`ReviewStore_SetChecklistAnswer_UpsertsSingleRowPerItem`.

**ReviewStore_SetChecklistSectionState_UpsertsSingleRowPerSection**: A section state is set twice; the database
holds one row for the section with the latest state. This scenario is tested by
`ReviewStore_SetChecklistSectionState_UpsertsSingleRowPerSection`.

**UserStore_AddAsync_User_RoundTripsEveryField**: A user is stored with every field set and a supplied identity;
the database assigns its own identity and every value is read back, which verifies the users table. This scenario is
tested by `UserStore_AddAsync_User_RoundTripsEveryField`.

**UserStore_AddAsync_UsernameOtherCase_ThrowsUserNameTakenAndLookupIgnoresCase**: A user name that differs from an
existing one only in case is refused by the unique index and reported as taken, and a lookup in another case finds
the original user. This scenario is tested by
`UserStore_AddAsync_UsernameOtherCase_ThrowsUserNameTakenAndLookupIgnoresCase`.

**External identity columns**: The `ExternalIssuer` and `ExternalSubject` columns and their unique index are exercised
by the tests of `UserStore`: a user of an identity provider round-trips its issuer and subject, a lookup of the exact
pair finds it, and a lookup with another case or another issuer does not, which is tested by
`UserStore_GetByExternalAsync_FindsOnlyTheExactPair`; and the unique index refuses a second user with the same pair
while local users without a pair are stored freely, which is tested by
`UserStore_AddAsync_SameExternalIdentityTwice_IsRefused`.

**ReviewStore_AddSlotAsync_UnknownReview_IsRefusedByDatabase**: A slot for a review that does not exist is refused
by the database (the foreign key to the review is enforced) and nothing is stored. This scenario is tested by
`ReviewStore_AddSlotAsync_UnknownReview_IsRefusedByDatabase`.

**ReviewStore_RemoveSlotAsync_SignedSlot_IsRefusedByDatabase**: A slot that has a signature is deleted; the database
refuses it through the foreign key from the signature to the slot (which uses the no-action delete behavior), and the
slot and the signature remain. This scenario is tested by `ReviewStore_RemoveSlotAsync_SignedSlot_IsRefusedByDatabase`.

**ReviewStore_AddSignatureAsync_SameSlotAndDigestTwice_ThrowsSigningNotAllowed**: A slot is signed twice with the same
digest; the unique index on the slot and the digest refuses the second signature, which the store reports as a
signing that is not allowed, and one signature remains. This scenario is tested by
`ReviewStore_AddSignatureAsync_SameSlotAndDigestTwice_ThrowsSigningNotAllowed`.

**ReviewStore_AddSignatureAsync_CurrentDigest_StoresAndRoundTrips**: A signature is stored and read back with its
role, its 64-character digest, and its meaning text unchanged, which verifies the signatures table.
This scenario is tested by `ReviewStore_AddSignatureAsync_CurrentDigest_StoresAndRoundTrips`.

**Repositories table**: The `Repositories` table is exercised by the tests of `RepositoryStore`: a repository round-trips
with every field, which is tested by `RepositoryStore_AddAsync_Repository_RoundTripsAndAudits`, the encrypted token is
stored and read back, which is tested by `RepositoryStore_GetEncryptedTokenAsync_ReturnsStoredText`, and the unique index
on the project and the name ignores case, which is tested by `RepositoryStore_NameOtherCase_ThrowsRepositoryNameTaken`
and `RepositoryStore_ListAsync_ScopedToProject_AndNamesAreUniquePerProject`.

**ProjectMemberships table**: The `ProjectMemberships` table is exercised by the tests of `ReviewStore`: a membership
round-trips, which is tested by `ReviewStore_AddMembershipAsync_Membership_RoundTripsAndIsListed`, the composite key
refuses a second membership of a user in a project, which is tested by
`ReviewStore_AddMembershipAsync_SecondMembership_IsRejected`, and the foreign key refuses a membership for an unknown
project, which is tested by `ReviewStore_AddMembershipAsync_UnknownProject_IsRefusedByTheDatabase`.

**Evidence settings columns**: The seven boolean columns of the `Projects` table are exercised by the tests of
`ReviewStore` in `EvidenceSettingsStoreTests.cs`: a new project reads back with the default settings, which is tested by
`ReviewStore_AddProjectAsync_NewProject_HasDefaultEvidenceSettings`, and a changed setting is stored in its own columns
and read back with the project, while the other projects keep the defaults, which is tested by
`ReviewStore_UpdateEvidenceSettingsAsync_Change_IsPersistedAndAudited`.

**Review reopens table and independent reviewers column**: The `ReviewReopens` table and the `RequireIndependentReviewers`
column of the `Projects` table are exercised by the tests of `ReviewStore` in `ReviewStoreSigningTests.cs`: a reopen is
stored with the administrator, the time, the reason, and the previous digest and is listed in the order of the reopens,
which is tested by `ReviewStore_ReopenReviewAsync_Persists_AuditAndHistory`, and a new project reads back with the
requirement on, a change is stored in the column by a compare-and-set and read back with the project, which is tested
by `ReviewStore_UpdateRequireIndependentReviewersAsync_CompareAndSet`. The new table and column mean that a database
that was created before them must be deleted and recreated (see the design of this unit).

**Review rule columns**: The `RequireNonAuthorChecklistAnswers` and `RequireNonReopenerSigning` columns of the `Projects`
table are exercised by the tests of `ReviewStore`: a new project reads back with both rules on, and a project added with
both rules off reads back that way from the read and from the listing, which is tested by
`ReviewStore_AddProjectAsync_NewReviewRules_AreStoredAndReturned` in `ReviewStoreTests.cs`; a change of each rule is
stored in its own column by a compare-and-set and read back with the project, which is tested by
`ReviewStore_UpdateReviewRuleAsync_CompareAndSet` in `ReviewStoreSigningTests.cs`. The new columns mean that a database
that was created before them must be deleted and recreated (see the design of this unit).

**Audit entries table**: The `AuditEntries` table is mapped by `ReviewCenterDbContext` and is exercised by the tests
of `AuditStore`, which write, read, and verify entries through the real schema; all scenarios of this unit run over a
schema that includes it.

**ReviewCenterDbContext_Model_HasTokenTableAndReviewCreatorColumns**: A review with a creator and the API flag is stored
and read back, and the model of the token table is read. The review keeps its creator and the flag; the index on the
token hash is unique; and the table has exactly the columns for the identity, the owner, the name, the display id, the
hash, the creation, the expiry, the last use, and the revocation, and none for the value of a token. This scenario is
tested by `ReviewCenterDbContext_Model_HasTokenTableAndReviewCreatorColumns`, defined in
`PersonalAccessTokenStoreTests.cs`. The display id is not unique at the database (it is only a label that is shown); the
hash alone identifies a token.

**Checklist definition column**: A project added without a definition reads back with none, and one added with a
definition reads it back unchanged, which is tested by
`ReviewStore_AddProjectAsync_ChecklistDefinition_IsNullByDefaultAndKeptWhenGiven`; a text of the full size limit with
non-ASCII characters is stored in the unbounded column and read back unchanged by the read and by the listing, which is
tested by `ReviewStore_UpdateChecklistDefinitionAsync_LargeText_RoundTripsThroughTheColumn`. Both tests are defined in
`ProjectChecklistStoreTests.cs` and described with the `ReviewStore` unit.

**Due date column**: A review added with a due date reads it back as the same calendar day, and one added without reads
back with none, which is tested by `ReviewStore_AddReviewAsync_DueDate_RoundTripsAsCalendarDay`, defined in
`ReviewDueDateStoreTests.cs` and described with the `ReviewStore` unit.

**Second factor tables**: The `UserTotps` and `TotpRecoveryCodes` tables are exercised by the tests of `TotpStore` in
`TotpStoreTests.cs` over a real database file. The unique index on the user and the recovery code hash refuses a
duplicate hash of one user, and the confirmation that held it is rolled back including its audit entry, which is tested
by `TotpStore_ConfirmAsync_DuplicateHash_IsRefusedByTheDatabaseAndRolledBack`; the same hash for two users is allowed,
which is tested by `TotpStore_ConfirmAsync_SameHashForDifferentUsers_IsAllowed`. Deleting a user removes the enrollment
and the codes by cascade, which is tested by `TotpStore_DeletingTheUser_DeletesTheEnrollmentAndCodes`. Both tests are
described with the `TotpStore` unit.
