### ReviewStore

#### Verification Approach

`ReviewStore` is verified with unit tests defined in `ReviewStoreTests.cs`. The tests use the real store over a real
SQLite database file that each test class instance creates in a private temporary folder and deletes afterward.
Real SQLite is used because ordering, keys, constraints, and binary content are the behavior under test. No
mocking is applied.

#### Test Environment

Tests create temporary SQLite database files, which are removed after the test run. The native SQLite library must
be loadable on the test platform.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A stored project is read back with all values; projects are listed by name; an unknown project gives none; an
  identity supplied with a new project is ignored.
- A stored review is read back with its files; file content is returned unchanged; reviews are listed only for
  their own project, ordered by identifier.
- Review metadata is returned without file content, and an unknown review or file gives nothing.
- A duplicate review key is reported as a duplicate review identifier.

#### Test Scenarios

**ReviewStore_AddProjectAsync_ValidProject_RoundTrips**: A project is stored; it is read back with the same
values and a generated identity. This scenario is tested by `ReviewStore_AddProjectAsync_ValidProject_RoundTrips`.

**ReviewStore_AddProjectAsync_SuppliedId_IsIgnoredAndDatabaseAssignsOwn**: Projects that carry the identity of an
existing project and an unused identity are added; both receive new database-assigned identities, the existing
project is unchanged, and the unused identity is not stored. This scenario is tested by
`ReviewStore_AddProjectAsync_SuppliedId_IsIgnoredAndDatabaseAssignsOwn`.

**ReviewStore_ListProjectsAsync_SeveralProjects_OrdersByName**: Several projects are stored; they are listed in
name order. This scenario is tested by `ReviewStore_ListProjectsAsync_SeveralProjects_OrdersByName`.

**ReviewStore_GetProjectAsync_UnknownId_ReturnsNull**: An unknown project identity returns null. This scenario is
tested by `ReviewStore_GetProjectAsync_UnknownId_ReturnsNull`.

**ReviewStore_AddReviewAsync_BinaryCrLfAndEmptyFiles_StoresBytesVerbatim**: A review with binary, carriage
return, and empty files is stored; the file content is read back unchanged. This scenario is tested by
`ReviewStore_AddReviewAsync_BinaryCrLfAndEmptyFiles_StoresBytesVerbatim`.

**ReviewStore_AddReviewAsync_SameIdInTwoProjects_KeepsThemApart**: Two projects each hold a review with the same
identifier; each is read back from its own project. This scenario is tested by
`ReviewStore_AddReviewAsync_SameIdInTwoProjects_KeepsThemApart`.

**ReviewStore_ListReviewsAsync_TwoProjects_ReturnsOnlyOwnReviewsOrderedById**: Reviews are listed for one project;
only its reviews are returned, in identifier order. This scenario is tested by
`ReviewStore_ListReviewsAsync_TwoProjects_ReturnsOnlyOwnReviewsOrderedById`.

**ReviewStore_GetReviewAsync_StoredReview_ReturnsMetadataWithoutContent**: A review is read; its files have path,
size, and hash but no content. This scenario is tested by
`ReviewStore_GetReviewAsync_StoredReview_ReturnsMetadataWithoutContent`.

**ReviewStore_GetReviewAsync_UnknownReviewOrFile_ReturnsNull**: An unknown review, and an unknown file of a known
review, return null. This scenario is tested by `ReviewStore_GetReviewAsync_UnknownReviewOrFile_ReturnsNull`.

**ReviewStore_AddReviewAsync_DuplicateKey_ThrowsReviewIdDuplicate**: A review with an existing key is added; a
`DomainRuleException` for `ReviewIdDuplicate` is thrown. This scenario is tested by
`ReviewStore_AddReviewAsync_DuplicateKey_ThrowsReviewIdDuplicate`.
