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
- A thread is stored with its anchor (whole file or line range), first comment, and state, and identities are
  assigned by the database; comments are appended and read in posting order; each comment keeps the display name and
  the user name of its author; the state can be changed back and
  forth; and an unknown thread, or a thread of another review, is reported as not found.
- Threads are listed only for their own review, in creation order, and the database refuses a thread for a file
  that is not in the review.
- A checklist snapshot is stored with its review and read back with its version; a review without one, and an
  unknown review, have none.
- Answering a check again, or setting a section's applicability again, replaces the earlier row, so there is one
  row per check and per section; outcome, display name, user name, and time are read back as saved, and only for
  their own review.

#### Test Scenarios

**ReviewStore_AddReview_WithChecklist_StoresSnapshotAtomically**: A review carrying a checklist snapshot is stored
and the snapshot is read back with the default version and a definition that parses to the default checklist. This
scenario is tested by `ReviewStore_AddReview_WithChecklist_StoresSnapshotAtomically`.

**ReviewStore_GetChecklist_ReviewWithoutSnapshot_ReturnsNull**: A review stored without a snapshot, and an unknown
review, both give no checklist. This scenario is tested by
`ReviewStore_GetChecklist_ReviewWithoutSnapshot_ReturnsNull`.

**ReviewStore_SetChecklistAnswer_UpsertsSingleRowPerItem**: A check is answered Pass and then Fail with a
justification, and another check is answered N/A; two answers are listed and the first holds the latest outcome and
justification. This scenario is tested by `ReviewStore_SetChecklistAnswer_UpsertsSingleRowPerItem`.

**ReviewStore_SetChecklistSectionState_UpsertsSingleRowPerSection**: A section is stored as not applicable with a
reason and then as applicable; one row is listed each time and holds the latest state, and the display name and
user name of who set it. This scenario is tested by
`ReviewStore_SetChecklistSectionState_UpsertsSingleRowPerSection`.

**ReviewStore_ChecklistRows_RoundTripOutcomeAndTimestamps**: A check of one review is answered N/A; the outcome,
display name, user name, and time are read back as saved, and the other review has no answers. This scenario is
tested by
`ReviewStore_ChecklistRows_RoundTripOutcomeAndTimestamps`.

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

**ReviewStore_AddThreadAsync_WholeFileAndRange_RoundTripsWithComments**: A whole-file thread and a range thread
(each carrying an identity that must be ignored) are stored and listed; identities are assigned and growing, and
the anchors, file path, state "Open", creation time, and first comment (with the display name and user name of its
author) come back unchanged. This scenario is
tested by `ReviewStore_AddThreadAsync_WholeFileAndRange_RoundTripsWithComments`.

**ReviewStore_AddCommentAsync_ExistingThread_AppendsInOrder**: Two comments are added to a thread; the stored
comment has an identity and the three comments are listed in posting order. This scenario is tested by
`ReviewStore_AddCommentAsync_ExistingThread_AppendsInOrder`.

**ReviewStore_AddCommentAsync_UnknownOrOtherReviewThread_ReturnsNull**: A comment for an unknown thread, and for a
thread through another review, returns null and adds nothing. This scenario is tested by
`ReviewStore_AddCommentAsync_UnknownOrOtherReviewThread_ReturnsNull`.

**ReviewStore_SetThreadStateAsync_ExistingAndUnknown_ChangesStateOrReturnsFalse**: A thread is resolved and
reopened; the listed state follows each change; an unknown thread and a thread of another review report false. This
scenario is tested by `ReviewStore_SetThreadStateAsync_ExistingAndUnknown_ChangesStateOrReturnsFalse`.

**ReviewStore_ListThreadsAsync_TwoReviews_ReturnsOnlyOwnThreadsInOrder**: Threads are added to two reviews in an
interleaved order; each review lists only its own threads in creation order, and an unknown review lists none. This
scenario is tested by `ReviewStore_ListThreadsAsync_TwoReviews_ReturnsOnlyOwnThreadsInOrder`.

**ReviewStore_AddThreadAsync_UnknownFile_IsRefusedByDatabase**: A thread for a file that is not in the review
raises a `DbUpdateException` and no thread is stored. This scenario is tested by
`ReviewStore_AddThreadAsync_UnknownFile_IsRefusedByDatabase`.
