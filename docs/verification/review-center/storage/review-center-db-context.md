### ReviewCenterDbContext

#### Verification Approach

`ReviewCenterDbContext` is verified with the unit tests of `ReviewStoreTests.cs` and `DatabaseSetupTests.cs`, which
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
stored with their comments and read back unchanged, which verifies the thread and comment tables. This scenario is
tested by `ReviewStore_AddThreadAsync_WholeFileAndRange_RoundTripsWithComments`.
