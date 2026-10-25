### ReviewCenterDbContext

#### Verification Approach

`ReviewCenterDbContext` is verified with the unit tests of `ReviewStoreTests.cs`, `ReviewStoreSigningTests.cs`,
`UserStoreTests.cs`, and
`DatabaseSetupTests.cs`, which
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
- The table for users stores every field of a user, and its unique index on the user name ignores case.
- The tables for reviewer slots and signatures are created; the database refuses a slot for a review that does not
  exist, refuses to remove a slot that has a signature, and refuses a second signature of the same slot with the same
  digest; the role, digest, and meaning of a signature are read back as stored.

#### Test Scenarios

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
