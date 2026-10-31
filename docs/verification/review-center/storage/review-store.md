### ReviewStore

#### Verification Approach

`ReviewStore` is verified with unit tests defined in `ReviewStoreTests.cs` (projects, reviews, threads, and
checklists), `ReviewStoreSigningTests.cs` (reviewer slots, the record, and signatures), and
`ReviewStoreRepositoryTests.cs` (reviews from a repository), `ReviewStoreMembershipTests.cs` (project
memberships), `ReviewStoreUserImpactTests.cs` (the slots of a user and the projects the user alone administers),
`ProjectMembersStoreTests.cs` (the last project administrator rule, including two changes made at
the same time), `EvidenceSettingsStoreTests.cs` (the evidence settings of a project and their compare-and-set
change), and `ProjectChecklistStoreTests.cs` (the checklist definition of a project, its compare-and-set change by the
hash of the stored text, and the race of two writers), `ReviewDueDateStoreTests.cs` (the due date of a review and
its compare-and-set change), `ReviewStoreReviewConclusionTextTests.cs` (the review conclusion text of a review, its
compare-and-set change, and the approval lock), and `ReviewStoreRecordContentTests.cs` (the included comments, the
resolution notes, the
kind of a thread, the report flag of a comment, and the open-thread rule of the signature). The tests use the real
store over a real
SQLite database file that each test class instance creates in a private temporary folder and deletes afterward.
Real SQLite is used because ordering, keys, constraints, transactions, and binary content are the behavior under test.
No mocking is applied.

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
  the user name of its author, its kind (a comment or a resolution), and whether it is included in the report; a
  resolved thread can be reopened; the store has no method that resolves a thread except the one that applies the
  resolution-note rule; and an unknown thread, or a thread of another review, is reported as not found.
  A comment written without a kind or a flag is a comment that is not included, and the kind of a thread follows from
  its starting comment (a Finding when it is included, otherwise a Question).
- A Finding is resolved in one transaction together with a Resolution comment that carries the note, and without a note
  the store refuses with `ResolutionNoteRequired` and stores nothing; the kind is read again inside the transaction and
  compared with the kind the caller expected, and a difference is refused with `RecordChanged` with nothing stored;
  resolving a resolved thread stores nothing; promoting and demoting set the flag of the starting comment and of the
  Resolution comments together; the flag of a reply can be changed, while the starting comment and a Resolution comment
  are refused; each change is stored with its audit entry.
- A signature is refused inside its transaction with `SigningNotAllowed` while any thread of the review is open, after
  the comparison of the digest, and nothing is stored.
- Threads are listed only for their own review, in creation order, and the database refuses a thread for a file
  that is not in the review.
- A checklist snapshot is stored with its review and read back with its version; a review without one, and an
  unknown review, have none.
- Answering a check again, or setting a section's applicability again, replaces the earlier row, so there is one
  row per check and per section; outcome, display name, user name, and time are read back as saved, and only for
  their own review.
- Reviewer slots are stored with database-assigned identities (a supplied identity is ignored), listed only for their
  own review in the order they were added, and refused for an unknown review; a slot can be updated and removed, and
  an unknown slot, or a slot reached through another review, is reported as not found.
- The record of a review carries its fingerprint, section states, answers, and threads with their comments in posting
  order, changes when a comment is added, and is none for an unknown review.
- A review from a repository is stored and read back with its repository, commits, selection, and for every file the
  change kind and baseline size and hash, without loading any file bytes; the baseline bytes are read on request and
  are none for a file without a baseline; its record carries the source and one change per file; an uploaded review
  has the upload source. The review-set identifier is stored with the review, is null for a review made by hand, and
  an identifier of the longest allowed length is stored and read back.
- A signature is stored with every value and read back; a digest that is not the digest of the stored record, whether
  made up or stale because the record changed, is refused as a changed record and nothing is stored; the same slot
  and digest a second time is refused as a signing that is not allowed; an unknown slot or review is refused; the
  database refuses to remove a slot that has a signature.
- A transaction begun at the `Serializable` level takes the write lock of SQLite at the begin, so a second writer is
  refused while it is open.
- A project membership is stored and read back by project and by user in a fixed order; a user can be a member of a
  project only once; changing a role and removing report whether a membership was there; every change that affected a
  row is audited with the project, and one that found nothing is not; the database refuses a membership for an
  unknown project.
- When told to require another administrator, the store refuses to remove or demote the last enabled project
  administrator with the rule `LastProjectAdmin`, stores nothing and no audit entry for the refusal, does not count a
  disabled administrator as the other one, lets exactly one of two administrators who remove each other at the same
  time succeed, and applies the same change without the check when it is not told to require one.
- The slots of a user are listed as primary and as alternate with the project, the review, the title, the slot, and the
  role, ordered by project, review, and slot, without the slots of other users and with none for a user without a slot;
  the projects a user alone administers are listed by name, without a project that has another enabled administrator,
  counting neither a disabled administrator nor an ordinary member as another administrator, and with none for a
  disabled or unknown user.
- A new project has the default evidence settings; a change of the settings is stored for its own project only, is read
  back with the project, and is stored with one audit entry of the project whose chain verifies; a change that names
  settings which are no longer stored, and a change of an unknown project, change nothing and write no entry.
- A change of reviewed content (a thread, a comment, the state of a thread, a resolution, the kind of a thread, the
  report flag of a comment, a checklist answer, the applicability of a
  section) and a change of the reviewer slots (add, update, remove) on a concluded review (every slot signed for the
  current record and no thread open) is refused inside the write
  transaction with `ReviewLocked`, whatever the caller did before, and stores no row and no audit entry; the same
  change on a review that is not approved is stored; a change of a review that does not exist is not refused as
  locked; and when a change races with the final signature of a review, exactly one of them takes effect (the change
  is refused as locked, or the signature as a changed record) and the stored state agrees with it.
- A reopen of an approved review stores one row with the administrator, the display name, the time, the reason, and the
  previous digest, together with one audit entry of the review whose chain verifies; the history is listed in the
  order of the reopens, only for its own review; the record of the review carries the number of reopens, so its digest
  changes; a reopen whose audit entry cannot be stored stores no row and leaves the review approved.
- The reopen of a review that is not approved is refused with `ReviewNotLocked` and a missing one with
  `ReviewNotFound`, both without a row or an entry; of two reopens made at the same time exactly one succeeds.
- A new project requires independent reviewers, or does not when the caller says so; the stored value is returned by
  the read and by the listing; the setting is changed by a compare-and-set that is stored with one
  audit entry, is read back with the project, and for an expected value that is not stored (or an unknown project)
  reports `false` and changes nothing.
- A new project has no checklist definition; a definition of the full size limit is stored in its own project only,
  read back unchanged, and reset to none; the change is a compare-and-set on the hash of the stored text that is stored
  with one audit entry of the project (the chain verifies), a hash that is not the stored one or an unknown project
  succeeds.
- A review stores an optional due date as a calendar day that reads back unchanged; the change is a compare-and-set on
  the stored date that is stored with one audit entry of the review (the chain verifies), a date that is not the
  stored one or an unknown review reports `false` and changes nothing and writes no entry, and of two writers that start
  from the same date exactly one succeeds. An approved review refuses the change with `ReviewLocked`.

#### Test Scenarios

**ReviewStore_AddSlotAsync_TwoSlots_AssignsIdsAndListsInOrderPerReview**: Two slots, one carrying an identity that
must be ignored, are added to one review and a third to another review; the first review lists its two slots in the
order added with database-assigned identities and the stored primary and alternate users (none for the second slot's
alternate). This scenario is tested by `ReviewStore_AddSlotAsync_TwoSlots_AssignsIdsAndListsInOrderPerReview`.

**ReviewStore_AddSlotAsync_UnknownReview_IsRefusedByDatabase**: A slot for a review that does not exist is added; a
`DbUpdateException` is thrown. This scenario is tested by `ReviewStore_AddSlotAsync_UnknownReview_IsRefusedByDatabase`.

**ReviewStore_UpdateAndRemoveSlotAsync_ExistingAndUnknown_ChangesOrReturnsFalse**: A slot is updated and its users are
read back; an unknown slot and a slot through another review report false for update and for removal; the slot is
then removed and no slot is listed. This scenario is tested by
`ReviewStore_UpdateAndRemoveSlotAsync_ExistingAndUnknown_ChangesOrReturnsFalse`.

**ReviewStore_GetRecordAsync_StoredReview_CarriesFingerprintAnswersThreadsAndComments**: A review with an answer, a
section marked not applicable, and a thread with a comment is read as a record, which carries the fingerprint, the
answer, the section state, and the comment; after another comment is added the digest of the record differs. This
scenario is tested by `ReviewStore_GetRecordAsync_StoredReview_CarriesFingerprintAnswersThreadsAndComments`.

**ReviewStore_GetRecordAsync_UnknownReview_ReturnsNull**: The record of an unknown review is requested; it is null.
This scenario is tested by `ReviewStore_GetRecordAsync_UnknownReview_ReturnsNull`.

**ReviewStore_GetReviewAsync_RepositoryReview_ReturnsSourceAndKindsWithoutBytes**: A review from a repository with an
added, a modified, and a deleted file is stored and read back; the repository, name, commits, and selection, and for
every file the change kind and the baseline size and hash are returned, and no file has bytes. This scenario is
tested by `ReviewStore_GetReviewAsync_RepositoryReview_ReturnsSourceAndKindsWithoutBytes`.

**ReviewStore_GetBaselineContentAsync_StoredFiles_ReturnsBaselineBytesOrNull**: The baseline bytes of a modified and a
deleted file are returned exactly; an added file, an unknown file, and an unknown review give null. This scenario is
tested by `ReviewStore_GetBaselineContentAsync_StoredFiles_ReturnsBaselineBytesOrNull`.

**ReviewStore_GetRecordAsync_RepositoryReview_CarriesSourceAndChanges**: The record of a repository review carries the
repository identity, both commits, the selection, and one change per file with its kind and baseline and head hashes
(none where a side is absent). This scenario is tested by
`ReviewStore_GetRecordAsync_RepositoryReview_CarriesSourceAndChanges`.

**ReviewStore_GetRecordAsync_UploadReview_HasUploadSource**: The record of an uploaded review has the upload source
and no changes. This scenario is tested by `ReviewStore_GetRecordAsync_UploadReview_HasUploadSource`.

**ReviewStore_AddReviewAsync_LongestReviewSetId_RoundTrips**: A review whose review-set identifier has the longest
allowed length is stored and read back with the identifier unchanged. This scenario is tested by
`ReviewStore_AddReviewAsync_LongestReviewSetId_RoundTrips`.

**ReviewStore_GetSnapshotAsync_SignedReview_CarriesRecordSlotsAndSignatures**: A review with a thread, a slot, and a
signature is read as a snapshot; the record, the slot, and the signature come back together, and the digest of the
record equals the digest stored in the signature. This scenario is tested by
`ReviewStore_GetSnapshotAsync_SignedReview_CarriesRecordSlotsAndSignatures`.

**ReviewStore_GetSnapshotAsync_UnknownReview_ReturnsNull**: The snapshot of an unknown review is requested; it is null.
This scenario is tested by `ReviewStore_GetSnapshotAsync_UnknownReview_ReturnsNull`.

**ReviewStore_ListSnapshotsAsync_SeveralReviews_EqualsSingleReadsAndReopenHistory**: One project holds an approved review,
a review reopened twice, a review whose signature a reopening made out of date and that then got a thread, a review with
two threads, three comments, two answers, a section state, a checklist and two slots, a review without slots, a review
from a repository with an added, a modified, and a deleted file, and a same-named review of another project. The batch
read returns every review, each part of each snapshot equals the single read, the interesting parts are present, and the
batch of reopens equals the single reads and leaves out the reviews that were never reopened. This scenario is tested by
`ReviewStore_ListSnapshotsAsync_SeveralReviews_EqualsSingleReadsAndReopenHistory`.

**ReviewStore_ListSnapshotsAsync_EmptyUnknownDuplicateAndForeignIds_AreAbsentOrReadOnce**: An empty input gives an empty
result (also for the reopens), an unknown identifier is absent, a duplicated identifier is read once, a review is not
found through another project, and a null list raises `ArgumentNullException`. This scenario is tested by
`ReviewStore_ListSnapshotsAsync_EmptyUnknownDuplicateAndForeignIds_AreAbsentOrReadOnce`.

**ReviewStore_ListSnapshotsAsync_MoreIdsThanOneChunk_ReadsAll**: Seven hundred identifiers, of which the first and the
last exist, are read; both reviews are returned. This scenario is tested by
`ReviewStore_ListSnapshotsAsync_MoreIdsThanOneChunk_ReadsAll`.

**ReviewStore_ListSnapshotsAsync_MoreReviews_SendsTheSameNumberOfQueries**: A store over the same database counts the
commands it sends (a command interceptor). Reading 2 reviews and reading 20 reviews send the same number of commands,
and reading the 20 reviews one by one sends more than five times as many. This scenario is tested by
`ReviewStore_ListSnapshotsAsync_MoreReviews_SendsTheSameNumberOfQueries`.

**ReviewStore_AddSignatureAsync_CurrentDigest_StoresAndRoundTrips**: A slot is signed with the current digest; the
signature is stored with a database-assigned identity and listed with the slot, user, display name, role, time, digest,
and meaning unchanged. This scenario is tested by `ReviewStore_AddSignatureAsync_CurrentDigest_StoresAndRoundTrips`.

**ReviewStore_AddSignatureAsync_StaleDigest_ThrowsRecordChangedAndStoresNothing**: A signature is added with a digest
that was made up, and with a digest that was current until a thread was added; each throws a `DomainRuleException` for
`RecordChanged` and no signature is listed. This scenario is tested by
`ReviewStore_AddSignatureAsync_StaleDigest_ThrowsRecordChangedAndStoresNothing`.

**ReviewStore_AddSignatureAsync_SameSlotAndDigestTwice_ThrowsSigningNotAllowed**: A slot is signed and then signed
again with the same digest; the second attempt throws a `DomainRuleException` for `SigningNotAllowed` and one
signature remains. This scenario is tested by
`ReviewStore_AddSignatureAsync_SameSlotAndDigestTwice_ThrowsSigningNotAllowed`.

**ReviewStore_AddSignatureAsync_UnknownSlotOrReview_IsRefused**: A signature for a slot that does not exist, and one
for a review that does not exist, are added with the current digest; they throw a `DomainRuleException` for
`SlotNotFound` and for `ReviewNotFound`. This scenario is tested by
`ReviewStore_AddSignatureAsync_UnknownSlotOrReview_IsRefused`.

**ReviewStore_RemoveSlotAsync_SignedSlot_IsRefusedByDatabase**: A signed slot is removed; the database refuses it with
a `SqliteException`, and the slot and its signature remain. This scenario is tested by
`ReviewStore_RemoveSlotAsync_SignedSlot_IsRefusedByDatabase`.

**ReviewStore_SerializableTransaction_TakesWriteLockAtBegin**: One connection begins a `Serializable` transaction and
does nothing else; a second connection with a short timeout tries to write and fails with SQLite error 5 (busy),
which shows that the write lock is taken at the begin, as `AddSignatureAsync` relies on. This scenario is tested by
`ReviewStore_SerializableTransaction_TakesWriteLockAtBegin`.

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

**ReviewStore_ReopenThreadAsync_ExistingAndUnknown_ReopensOrReturnsFalse**: A thread is resolved through the resolve
method and reopened; the listed state follows each change; reopening an open thread reports true; an unknown thread and
a thread of another review report false. This scenario is tested by
`ReviewStore_ReopenThreadAsync_ExistingAndUnknown_ReopensOrReturnsFalse`.

**ReviewStore_ListThreadsAsync_TwoReviews_ReturnsOnlyOwnThreadsInOrder**: Threads are added to two reviews in an
interleaved order; each review lists only its own threads in creation order, and an unknown review lists none. This
scenario is tested by `ReviewStore_ListThreadsAsync_TwoReviews_ReturnsOnlyOwnThreadsInOrder`.

**ReviewStore_AddThreadAsync_UnknownFile_IsRefusedByDatabase**: A thread for a file that is not in the review
raises a `DbUpdateException` and no thread is stored. This scenario is tested by
`ReviewStore_AddThreadAsync_UnknownFile_IsRefusedByDatabase`.

**ReviewStore_AddThreadAsync_Side_IsPersisted**: A thread on the baseline side and a thread on the head side are
stored and read back with the side they were given. This scenario is tested by
`ReviewStore_AddThreadAsync_Side_IsPersisted`.

**ReviewStore_AddThreadAsync_Side_IsStoredAsText**: A thread on the baseline side is stored, and a raw query of the
`Side` column of the `Threads` table returns the text "Baseline". This scenario is tested by
`ReviewStore_AddThreadAsync_Side_IsStoredAsText`.

**ReviewStore_AddThreadAsync_IncludedComment_PersistsFlagAndKind**: A thread whose starting comment is included is
stored; read back, the comment has its flag and the kind Comment, and the thread is a Finding. This scenario is tested
by `ReviewStore_AddThreadAsync_IncludedComment_PersistsFlagAndKind`.

**ReviewStore_AddCommentAsync_IncludedReply_PersistsFlag**: A reply that is included is stored with its flag, and the
thread stays a Question. This scenario is tested by `ReviewStore_AddCommentAsync_IncludedReply_PersistsFlag`.

**ReviewStore_GetRecordAsync_ThreadKind_DerivedFromStartingComment**: The record of a review carries the thread with
its comments, their kinds, and their flags, and the kind of the thread is derived from its starting comment. This
scenario is tested by `ReviewStore_GetRecordAsync_ThreadKind_DerivedFromStartingComment`.

**ReviewStore_ResolveThreadAsync_FindingWithoutResolution_ThrowsResolutionNoteRequired**: Resolving a Finding without
a Resolution comment is refused with `ResolutionNoteRequired`, the thread stays open, and no entry is stored. This
scenario is tested by `ReviewStore_ResolveThreadAsync_FindingWithoutResolution_ThrowsResolutionNoteRequired`.

**ReviewStore_ResolveThreadAsync_KindChangedSinceRead_ThrowsRecordChangedAndStoresNothing**: A Finding is demoted and a
Question is promoted after the caller read them; resolving them with the kind the caller expected is refused with
`RecordChanged`, the threads stay open with their comments, and no entry is stored; a caller that expects the kind they
have now resolves them, and the note of the Question is not included. This scenario is tested by
`ReviewStore_ResolveThreadAsync_KindChangedSinceRead_ThrowsRecordChangedAndStoresNothing`.

**ReviewStore_ThreadStateMethods_CannotResolveWithoutNote_ReopenStillWorks**: The reopen method leaves an open Finding
open, resolving a Finding without a note is refused, the store interface has no method that takes a thread state, and a
Finding resolved with a note can be reopened with its history kept. This scenario is tested by
`ReviewStore_ThreadStateMethods_CannotResolveWithoutNote_ReopenStillWorks`.

**ReviewStore_ResolveThreadAsync_FindingWithNote_StoresIncludedResolutionAndAudits**: Resolving a Finding with a note
stores the state, an included Resolution comment, and the entry `thread.resolved` in one transaction. This scenario is
tested by `ReviewStore_ResolveThreadAsync_FindingWithNote_StoresIncludedResolutionAndAudits`.

**ReviewStore_ResolveThreadAsync_AlreadyResolved_ReturnsFalseAndAddsNothing**: Resolving a resolved thread reports
nothing done, adds no comment, and stores no entry. This scenario is tested by
`ReviewStore_ResolveThreadAsync_AlreadyResolved_ReturnsFalseAndAddsNothing`.

**ReviewStore_SetThreadKindAsync_PromoteDemote_UpdatesStartingAndResolutionComments**: Promoting sets the flag of the
starting comment and of the Resolution comments, demoting clears them, replies keep their flags, and each change is
stored with its entry. This scenario is tested by
`ReviewStore_SetThreadKindAsync_PromoteDemote_UpdatesStartingAndResolutionComments`.

**ReviewStore_SetCommentReportAsync_ReplyToggles_StartingAndResolutionRefused**: The flag of a reply is changed with
an entry, and the starting comment and a Resolution comment are refused. This scenario is tested by
`ReviewStore_SetCommentReportAsync_ReplyToggles_StartingAndResolutionRefused`.

**ReviewStore_ThreadWrites_ConcludedReview_ThrowReviewLocked**: A theory over every thread write (start, reply, state,
resolve, promote, demote, and toggle) on a concluded review; each is refused with `ReviewLocked` and stores nothing.
This scenario is tested by `ReviewStore_ThreadWrites_ConcludedReview_ThrowReviewLocked`.

**ReviewStore_AddSignatureAsync_OpenThread_ThrowsSigningNotAllowedAndStoresNothing**: A signature with the current
digest on a review with an open thread is refused with `SigningNotAllowed` and no signature is stored. This scenario is
tested by `ReviewStore_AddSignatureAsync_OpenThread_ThrowsSigningNotAllowedAndStoresNothing`.

**ReviewStore_AddSignatureAsync_ThreadOpenedBeforeFinalSignature_IsRefused**: A thread is opened after the service
looked at the review and before the final signature is stored; the store refuses the signature in the transaction.
This scenario is tested by `ReviewStore_AddSignatureAsync_ThreadOpenedBeforeFinalSignature_IsRefused`.

**ReviewStore_IsConcluded_Records_FollowReviewLock**: For records of every combination of signatures and threads, the
store decides whether a review is concluded as `ReviewLock` does. This scenario is tested by
`ReviewStore_IsConcluded_Records_FollowReviewLock`.

**ReviewCenterDbContext_Model_CommentColumns_DefaultToChatAndComment**: A comment inserted without the new columns
reads back as chat with the kind Comment, the columns hold the kind as text and the flag as a boolean, and the
`Threads` table has no column for the kind. This scenario is tested by
`ReviewCenterDbContext_Model_CommentColumns_DefaultToChatAndComment`.

**ReviewStore_AddMembershipAsync_Membership_RoundTripsAndIsListed**: Memberships are added for two users in two
projects, one as project administrator and the others with the default role; each is read back by user and project,
the project lists its memberships ordered by user, the user lists its memberships ordered by project, and a missing
membership reads as none. This scenario is tested by `ReviewStore_AddMembershipAsync_Membership_RoundTripsAndIsListed`.

**ReviewStore_AddMembershipAsync_SecondMembership_IsRejected**: A user who is already a member is added again with
another role; the store throws the rule `MembershipExists` and the role is unchanged. This scenario is tested by
`ReviewStore_AddMembershipAsync_SecondMembership_IsRejected`.

**ReviewStore_SetAndRemoveMembershipAsync_ChangeRoleAndRemove_ReportWhetherFound**: The role of a member is changed
and the member is removed; each call answers `true`, the call for a user who is no member answers `false`, a second
removal answers `false`, and the membership is gone afterward. This scenario is tested by
`ReviewStore_SetAndRemoveMembershipAsync_ChangeRoleAndRemove_ReportWhetherFound`.

**ReviewStore_Membership_Changes_AreAuditedWithTheProject**: A membership is added, changed, removed, and a
non-existing one is removed; the audit trail holds exactly three `project.member-` events, each carrying the project
identity, so the removal that found nothing writes no event. This scenario is tested by
`ReviewStore_Membership_Changes_AreAuditedWithTheProject`.

**ReviewStore_AddMembershipAsync_UnknownProject_IsRefusedByTheDatabase**: A membership for a project that does not
exist is added; the database refuses it through the foreign key. This scenario is tested by
`ReviewStore_AddMembershipAsync_UnknownProject_IsRefusedByTheDatabase`.

**ReviewStore_RemoveMembershipAsync_TwoAdminsRemoveEachOther_ExactlyOneSucceeds**: A project has two administrators,
and both are removed at the same time, each call requiring another administrator; exactly one call succeeds, the
other throws the rule `LastProjectAdmin`, one administrator remains, and only one `project.member-removed` event is
stored. This scenario is tested by `ReviewStore_RemoveMembershipAsync_TwoAdminsRemoveEachOther_ExactlyOneSucceeds`.

**ReviewStore_MembershipChanges_RemoveRacesDemote_LeavesAnAdministrator**: One administrator is removed while the
other is demoted to user, at the same time and with the check required; exactly one call is refused and one project
administrator remains. This scenario is tested by
`ReviewStore_MembershipChanges_RemoveRacesDemote_LeavesAnAdministrator`.

**ReviewStore_RemoveMembershipAsync_LastAdmin_RefusedAndRolledBackUnlessExempt**: The only administrator is removed
and demoted with the check required; both calls throw `LastProjectAdmin`, the role is unchanged, and no further event
is stored. The same removal without the check then succeeds and the project has no memberships. This scenario is
tested by `ReviewStore_RemoveMembershipAsync_LastAdmin_RefusedAndRolledBackUnlessExempt`.

**ReviewStore_RemoveMembershipAsync_OtherAdminDisabled_IsRefused**: The other administrator is disabled, so the
removal of the remaining one with the check required throws `LastProjectAdmin` and both memberships remain. This
scenario is tested by `ReviewStore_RemoveMembershipAsync_OtherAdminDisabled_IsRefused`.

**ReviewStore_ListSlotsOfUserAsync_PrimaryAndAlternate_AreFoundInOrder**: `ann` is primary of a slot of Apollo/r2 and of
Gemini/g1 and alternate of Apollo/r1; a slot of Apollo/r3 is `bob`'s alone. The call lists the three slots of `ann`
ordered by project, review, and slot with the project name, the title, the slot identity, and the role, and not the
slot of `bob`.
This scenario is tested by `ReviewStore_ListSlotsOfUserAsync_PrimaryAndAlternate_AreFoundInOrder`.

**ReviewStore_ListSlotsOfUserAsync_NoSlot_IsEmpty**: A user without a slot and an unknown identity give an empty list.
This scenario is tested by `ReviewStore_ListSlotsOfUserAsync_NoSlot_IsEmpty`.

**ReviewStore_ListSoleAdminProjectsAsync_OnlyEnabledAdministrator_IsFound**: `ann` alone administers Gemini and Apollo,
is a plain member of Borealis, and shares Cygnus with another enabled administrator. Only Apollo and Gemini are listed,
in that order. This scenario is tested by `ReviewStore_ListSoleAdminProjectsAsync_OnlyEnabledAdministrator_IsFound`.

**ReviewStore_ListSoleAdminProjectsAsync_DisabledOrOrdinaryOthers_DoNotCount**: A project whose other administrator is
disabled and a project whose other member is an ordinary member are both listed for `ann`; the disabled administrator
of a third project is not listed for that administrator, and an ordinary member and an unknown identity get an empty
list. This scenario is tested by `ReviewStore_ListSoleAdminProjectsAsync_DisabledOrOrdinaryOthers_DoNotCount`.

**DatabaseSetup_EnsureCreated_CreatesReviewUserStateTableAndInboxIndexes**: A new database has the indexes on the
primary and the alternate user of the reviewer slots that the read of the slots of a user relies on. This scenario is
defined in the `DatabaseSetup` unit and is tested by `DatabaseSetup_EnsureCreated_CreatesReviewUserStateTableAndInboxIndexes`.

**ReviewStore_AddProjectAsync_NewProject_HasDefaultEvidenceSettings**: A project is stored without other settings; it
is read back with the default evidence settings (the two anonymous switches off and the five content switches on). This
scenario is defined in `EvidenceSettingsStoreTests.cs` and is tested by
`ReviewStore_AddProjectAsync_NewProject_HasDefaultEvidenceSettings`.

**ReviewStore_UpdateEvidenceSettingsAsync_Change_IsPersistedAndAudited**: The settings of one of two projects are
changed from the defaults, with an audit draft. The call reports `true`; the new settings are read back by
`GetProjectAsync` and by `ListProjectsAsync`; the other project keeps the defaults; one entry
`project.evidence-settings-changed` of the project is stored; and the hash chain of the audit trail still verifies. This
scenario is tested by `ReviewStore_UpdateEvidenceSettingsAsync_Change_IsPersistedAndAudited`.

**ReviewStore_UpdateEvidenceSettingsAsync_ExpectedDiffers_ChangesNothing**: After a first change, a second change that
still names the defaults as the expected settings, and a change of a project that does not exist, both report `false`;
the stored settings are those of the first change, and only the entry of the first change exists. This scenario is
tested by `ReviewStore_UpdateEvidenceSettingsAsync_ExpectedDiffers_ChangesNothing`.

**ReviewStore_WriteMethods_ApprovedReview_RefuseWithReviewLockedInTransaction**: A review is signed in every slot, so
that it is approved, with the store called directly (the services and their lock check are not involved). Adding a
thread, adding a comment, changing the state of a thread, answering a check, changing the applicability of a section,
changing the due date, and adding, updating, and removing a slot are each refused with `ReviewLocked`; the digest of
the record, the slot, and the thread with its comment and state are as they were, no answer or section state exists,
and the number of audit
entries is unchanged. After the review is reopened, the same answer is stored. This scenario is defined in
`ReviewStoreSigningTests.cs` and is tested by `ReviewStore_WriteMethods_ApprovedReview_RefuseWithReviewLockedInTransaction`.

**ReviewStore_WriteMethods_UnknownReview_AreNotRefusedAsLocked**: Each of the same writes is made for a review that
does not exist. None is refused with `ReviewLocked` (a not-found result or a refusal by the relationships of the
database is accepted), and no thread, slot, answer, section state, or audit entry exists afterward. This scenario is
tested by `ReviewStore_WriteMethods_UnknownReview_AreNotRefusedAsLocked`.

**ReviewStore_AddSignatureAsync_RacingWithWrite_SignatureStartedFirst_EndsConsistent** and
**ReviewStore_AddSignatureAsync_RacingWithWrite_WriteStartedFirst_EndsConsistent**: A review has two slots and the
first is signed. The final signature and the addition of a thread are started together through the store, once with
the signature started first and once with the write started first, and both are awaited. Exactly one takes effect:
either the signature is stored, the thread is refused with `ReviewLocked`, and the review is approved; or the thread is
stored, the signature is refused with `RecordChanged`, and the review is not approved. The audit chain verifies. The
checks hold for either order, so the tests do not depend on timing. These scenarios are tested by the two tests named.

**ReviewStore_ReopenReviewAsync_Persists_AuditAndHistory**: An approved review is reopened with an audit draft. The
history read back holds one reopen with the administrator, the display name, the time, the reason, and the previous
digest; the record of the review has a reopen count of 1, so its digest differs from the approved one; the old
signature is kept but the review is no longer approved; exactly one entry `review.reopened` of the review is stored
with the detail of the draft and the chain verifies. This scenario is defined in `ReviewStoreSigningTests.cs` and is
tested by `ReviewStore_ReopenReviewAsync_Persists_AuditAndHistory`.

**ReviewStore_ReopenReviewAsync_NotApprovedOrUnknown_IsRefusedAndStoresNothing**: A review whose slot has not signed,
a review that was already reopened, and a review that does not exist are reopened; the first two throw `ReviewNotLocked`
and the last `ReviewNotFound`, and no reopen row and no audit entry is added. This scenario is tested by
`ReviewStore_ReopenReviewAsync_NotApprovedOrUnknown_IsRefusedAndStoresNothing`.

**ReviewStore_ReopenReviewAsync_AuditInsertFails_RollsBackTheReopen**: The database is made to refuse every audit
entry and an approved review is reopened. The call fails, no reopen row exists, the reopen count of the record is still
0, the digest is as approved, and the review is still approved. With the refusal removed, the same reopen succeeds.
This scenario is tested by `ReviewStore_ReopenReviewAsync_AuditInsertFails_RollsBackTheReopen`.

**ReviewStore_ListReopensAsync_SeveralReviewsAndReopens_AreScopedAndInCreationOrder**: Three approved reviews are made:
`REV-1` and `REV-2` in one project and `REV-1` in another. They are reopened in an interleaved order, and `REV-1` of
the first project is approved again and reopened two more times. The listing of each review holds only the reopens of
that review (the other review of the same project and the same identifier of the other project do not appear), in the
order in which they were made, with growing identities, and the reopen count of the record is 3 for the review reopened
three times. This scenario is tested by `ReviewStore_ListReopensAsync_SeveralReviewsAndReopens_AreScopedAndInCreationOrder`.

**ReviewStore_ReopenReviewAsync_TwoAtOnce_ExactlyOneSucceeds**: Two reopens of one approved review are made at the same
time; one succeeds and the other throws `ReviewNotLocked`; one row and one audit entry exist, the reopen count is 1, and
the chain verifies. This scenario is tested by `ReviewStore_ReopenReviewAsync_TwoAtOnce_ExactlyOneSucceeds`.

**ReviewStore_AddProjectAsync_RequireIndependentReviewers_IsStoredAndReturned**: A project is added with the setting
switched off and another without saying; the read and the listing return false for the first and true for the second.
This scenario is tested by `ReviewStore_AddProjectAsync_RequireIndependentReviewers_IsStoredAndReturned`.

**ReviewStore_UpdateRequireIndependentReviewersAsync_CompareAndSet**: A new project requires independent reviewers.
The setting is switched off with the expected value `true`; the call reports `true`, the new value is read back, and one
audit entry is stored. A second call that still names `true` as the expected value, and a call for a project that does
not exist, report `false` and change nothing. This scenario is defined in `ReviewStoreSigningTests.cs` and is tested
by `ReviewStore_UpdateRequireIndependentReviewersAsync_CompareAndSet`.

**ReviewStore_AddProjectAsync_NewReviewRules_AreStoredAndReturned**: A project is added with both further review rules
(authors answering the checklist of their own review, and the person who reopened a review signing it again) switched
off and another without saying; the read and the listing return false for both rules of the first and true for both
rules of the second, and the independence rule of the first is still true. This scenario is tested by
`ReviewStore_AddProjectAsync_NewReviewRules_AreStoredAndReturned`.

**ReviewStore_UpdateReviewRuleAsync_CompareAndSet**: For each of the three rules, a new project has the rule on. The rule
is switched off with the expected value `true`; the call reports `true`, the new value is read back, and one audit entry
is stored. A second call that still names `true` as the expected value, and a call for a project that does not exist,
report `false` and change nothing, and the audit chain verifies. This scenario is defined in
`ReviewStoreSigningTests.cs` and is tested by `ReviewStore_UpdateReviewRuleAsync_CompareAndSet`.

**ReviewStore_UpdateReviewRuleAsync_ChangesOnlyTheNamedRule**: The three rules of a new project are switched off one
after the other; after each call exactly the rules switched off so far read back false and the others true. This scenario
is defined in `ReviewStoreSigningTests.cs` and is tested by `ReviewStore_UpdateReviewRuleAsync_ChangesOnlyTheNamedRule`.

**ReviewStore_UpdateReviewRuleAsync_UndefinedRule_ThrowsArgumentOutOfRange**: A rule that is not a defined value is
rejected with `ArgumentOutOfRangeException` and no audit entry is stored. This scenario is defined in
`ReviewStoreSigningTests.cs` and is tested by `ReviewStore_UpdateReviewRuleAsync_UndefinedRule_ThrowsArgumentOutOfRange`.

**ReviewStore_AddProjectAsync_ChecklistDefinition_IsNullByDefaultAndKeptWhenGiven**: A project added without a
checklist definition reads back with none; a project added with one reads back with it unchanged. This scenario is
defined in `ProjectChecklistStoreTests.cs` and is tested by
`ReviewStore_AddProjectAsync_ChecklistDefinition_IsNullByDefaultAndKeptWhenGiven`.

**ReviewStore_UpdateChecklistDefinitionAsync_LargeText_RoundTripsThroughTheColumn**: A definition text of the full size
limit, with non-ASCII characters, is stored and read back unchanged by the read and by the listing. This scenario is
defined in `ProjectChecklistStoreTests.cs` and is tested by
`ReviewStore_UpdateChecklistDefinitionAsync_LargeText_RoundTripsThroughTheColumn`.

**ReviewStore_UpdateChecklistDefinitionAsync_ChangeAndReset_ArePersistedAndAudited**: The definition of one project is
replaced with the expected hash of the default and then reset with the hash of the replacement; both calls report
`true`, the other project is untouched, two entries `project.checklist-changed` of the project exist, and the chain
verifies. This scenario is defined in `ProjectChecklistStoreTests.cs` and is tested by
`ReviewStore_UpdateChecklistDefinitionAsync_ChangeAndReset_ArePersistedAndAudited`.

**ReviewStore_UpdateChecklistDefinitionAsync_ExpectedDiffers_ChangesNothing**: A change that names a hash that is no longer
the stored one, one that names a made-up hash, and one for an unknown project report `false`, leave the stored
definition as it was, write no entry, and leave the chain intact. This scenario is defined in
`ProjectChecklistStoreTests.cs` and is tested by
`ReviewStore_UpdateChecklistDefinitionAsync_ExpectedDiffers_ChangesNothing`.

**ReviewStore_UpdateChecklistDefinitionAsync_ConcurrentWriters_ExactlyOneWins**: Two writers that start from the same
hash with different definitions are released together, once calling the store and once calling the service; exactly one
wins, the stored definition is the winner's, one entry exists, and the chain verifies. This scenario is defined in
`ProjectChecklistStoreTests.cs` and is tested by
`ReviewStore_UpdateChecklistDefinitionAsync_ConcurrentWriters_ExactlyOneWins`.

**The due date** is verified in `ReviewDueDateStoreTests.cs`.

**ReviewStore_AddReviewAsync_DueDate_RoundTripsAsCalendarDay**: A review is stored with a due date and one without.
The first reads back with the same calendar day, the second with none. This scenario is tested by
`ReviewStore_AddReviewAsync_DueDate_RoundTripsAsCalendarDay`.

**ReviewStore_UpdateDueDateAsync_ChangeAndClear_ArePersistedAndAudited**: A due date is set and then cleared with a
compare-and-set on the stored value. Each change is persisted, and each writes one entry `review.due-date-changed` of
the review in the same transaction; the chain of the audit trail verifies. This scenario is tested by
`ReviewStore_UpdateDueDateAsync_ChangeAndClear_ArePersistedAndAudited`.

**ReviewStore_UpdateDueDateAsync_ExpectedDiffers_ChangesNothing**: A change that expects no date, one that expects
another date than the stored one, and one for a review that does not exist report `false`; the stored date is as it
was and no entry is written. This scenario is tested by `ReviewStore_UpdateDueDateAsync_ExpectedDiffers_ChangesNothing`.

**ReviewStore_UpdateDueDateAsync_ConcurrentWriters_ExactlyOneWins**: Two writers that start from the same stored date
with different new dates are released together; exactly one wins, the stored date is the winner's, one entry exists,
and the chain verifies. This scenario is tested by `ReviewStore_UpdateDueDateAsync_ConcurrentWriters_ExactlyOneWins`.

**The review conclusion text** is verified in `ReviewStoreReviewConclusionTextTests.cs`.

**ReviewStore_UpdateReviewConclusionTextAsync_StoresTextAndAuditAndRecord**: A text is set with a matching
expectation; the call reports `true`, writes one `review.conclusion-changed` entry, the text reads back in the review
and in the record that `GetRecordAsync` returns, and the digest changes. This scenario is tested by
`ReviewStore_UpdateReviewConclusionTextAsync_StoresTextAndAuditAndRecord`.

**ReviewStore_UpdateReviewConclusionTextAsync_CompareAndSet_RefusesStaleExpectation**: A change that expects another
text than the stored one reports `false`; the stored text is as it was and no entry is written. This scenario is
tested by `ReviewStore_UpdateReviewConclusionTextAsync_CompareAndSet_RefusesStaleExpectation`.

**ReviewStore_UpdateReviewConclusionTextAsync_MissingReview_ReturnsFalse**: A change for a review that does not exist
reports `false` and writes no entry. This scenario is tested by
`ReviewStore_UpdateReviewConclusionTextAsync_MissingReview_ReturnsFalse`.

**ReviewStore_UpdateReviewConclusionTextAsync_ConcludedReview_ThrowsReviewLocked**: A change of a review that is
concluded at that moment is refused with `ReviewLocked`; nothing is stored and no entry is written. This scenario is
tested by `ReviewStore_UpdateReviewConclusionTextAsync_ConcludedReview_ThrowsReviewLocked`.

**Audit drafts**: Every mutation of the store takes an audit draft, and the tests of this unit pass one through
the real `AuditAppender`; the helper `TestStore` builds the store and an `AuditStore` over the same database. The
transaction that stores a change with its audit entry, and the linear chain of changes made through different
stores, are verified in the tests of `AuditStore`.

**The follow-up review** is verified in `ReviewFollowUpStoreTests.cs` against a real SQLite database file, with the real
`AuditAppender` and a helper store that has a project with the review `REV-1`.

**ReviewStore_AddReviewAsync_FollowUp_RoundTripsEveryNewColumn**: A follow-up review is added with a carried answer, a
carried not-applicable section, and a carried thread with a comment. Read back, the review has `FollowsReviewId` and the
two counts, the answer, the section, and the thread have the carried marks (and the thread its original number), the
comment is unchanged, the record that a signature covers has them, and the audit chain verifies. This scenario is tested
by `ReviewStore_AddReviewAsync_FollowUp_RoundTripsEveryNewColumn`.

**ReviewStore_AddReviewAsync_FollowUpCarryFails_CreatesNothing**: A carried thread names a file that the new review does
not have, so the database refuses. Afterwards the review, every carried row, and the audit entry do not exist, and the
followed review is unchanged. This scenario is tested by `ReviewStore_AddReviewAsync_FollowUpCarryFails_CreatesNothing`.

**ReviewStore_SetChecklistAnswerAndSectionState_CarriedRows_AreReplacedWithoutMarker**: An answer and a section state
that were carried are set again by another person; the stored rows are the new ones, have no marker, and there is still
one row each, while the carried thread keeps its marker. This scenario is tested by
`ReviewStore_SetChecklistAnswerAndSectionState_CarriedRows_AreReplacedWithoutMarker`.

**ReviewStore_AddReviewAsync_CarriedFinding_PersistsFlagsAndKind**: A carried Finding with its included starting
comment and its included Resolution comment is stored with the follow-up; read back, the comments keep their kinds and
flags, the thread is a Finding, and carried chat stays chat. This scenario is tested by
`ReviewStore_AddReviewAsync_CarriedFinding_PersistsFlagsAndKind`.
