### ReviewService

#### Verification Approach

`ReviewService` is verified with unit tests defined in `ReviewServiceTests.cs`. The tests use the real service and
the real `Fingerprint` with a fake in-memory store (`FakeReviewStore`), the project-access double
`AllowAllProjectAccess` (the access rules are verified by the `ProjectAccess` unit), and a fixed clock, so every rule
and every
limit can be exercised without a database. Failures are checked through the rule carried by the
`DomainRuleException`. Limits are tested at the boundary: the limit itself is accepted and one more is rejected.
The tests that use large content build it in memory. The blank-path, invalid-id, and blank-title tests are
theories over several invalid values.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A valid request stores a Draft review with the known fingerprint, the hash and size of each file, and the bytes
  unchanged, including binary content and carriage returns.
- The fingerprint does not depend on file paths or order.
- Each rule violation is rejected with the matching rule; a project that does not exist is refused with the rule that
  also hides a project the actor may not see, and a repository that is not the project's is refused as not found.
- Every limit is accepted at its value and rejected above it.
- A review can be read back with its files, listed, and its file content read; the content of a missing file or
  review is null.
- A review from a repository freezes the repository, both commits, the selection, and for every changed file the
  path, the change kind, and the baseline and head bytes; the fingerprint covers the head files only.
- A selection that matches no change is refused with `NothingToReview` and stores nothing; the selection, the
  limits, and the skipped entries are honored as designed.
- Later changes of the repository do not change the stored review.
- The audit entry of a repository review names the repository, the commits, the counts, and the fingerprint.
- A review created from a plan carries its review-set identifier and names it last in the audit entry; a manual
  review has none.
- A review records the user who created it; a review created by an actor that came through a personal access token is
  flagged as created through the API, and the audit entry of the creation names the id of the token, never its value,
  both for a draft from files and for a review from a repository; a review created from the web pages is not flagged.
- The diff of a file is computed from the stored content only, never from git; it reports no baseline, a binary
  side, or a file that is too large instead of rows, and flags content that differs only in line endings.

#### Test Scenarios

**ReviewService_CreateDraftAsync_ValidInput_StoresDraftWithFingerprint**: A review of three files is created; it
is stored in the Draft state with the known fingerprint, and every file has the size and the known SHA-256 hash
of its own content. This scenario is tested by `ReviewService_CreateDraftAsync_ValidInput_StoresDraftWithFingerprint`.

**ReviewService_CreateDraftAsync_BinaryAndCrLfContent_KeepsBytesVerbatim**: A file with binary content and
carriage returns and an empty file are created; the stored bytes equal the uploaded bytes, and the sizes and
hashes (including the hash of empty content) are those of the input. This scenario is tested by
`ReviewService_CreateDraftAsync_BinaryAndCrLfContent_KeepsBytesVerbatim`.

**ReviewService_CreateDraftAsync_SameBytesDifferentPathsAndOrder_GivesSameFingerprint**: The same contents under
other paths and in another order give the same fingerprint. This scenario is tested by
`ReviewService_CreateDraftAsync_SameBytesDifferentPathsAndOrder_GivesSameFingerprint`.

**ReviewService_CreateDraftAsync_InvalidId_ThrowsReviewIdInvalid**: Null, empty, and ill-formed identifiers are
rejected with `ReviewIdInvalid`. This scenario is tested by
`ReviewService_CreateDraftAsync_InvalidId_ThrowsReviewIdInvalid`.

**ReviewService_CreateDraftAsync_IdLength_LimitIsInclusive**: An identifier of the maximum length is accepted and
one character longer is rejected. This scenario is tested by
`ReviewService_CreateDraftAsync_IdLength_LimitIsInclusive`.

**ReviewService_CreateDraftAsync_DuplicateIdInSameProject_ThrowsReviewIdDuplicate**: A second review with an
existing identifier in the same project is rejected with `ReviewIdDuplicate`. This scenario is tested by
`ReviewService_CreateDraftAsync_DuplicateIdInSameProject_ThrowsReviewIdDuplicate`.

**ReviewService_CreateDraftAsync_SameIdInOtherProject_IsAllowed**: The same identifier in another project is
accepted. This scenario is tested by `ReviewService_CreateDraftAsync_SameIdInOtherProject_IsAllowed`.

**ReviewService_CreateDraftAsync_BlankTitle_ThrowsReviewTitleInvalid**: Null, empty, and white-space titles are
rejected with `ReviewTitleInvalid`. This scenario is tested by
`ReviewService_CreateDraftAsync_BlankTitle_ThrowsReviewTitleInvalid`.

**ReviewService_CreateDraftAsync_TitleTooLong_ThrowsReviewTitleInvalid**: A title longer than the maximum is
rejected with `ReviewTitleInvalid`. This scenario is tested by
`ReviewService_CreateDraftAsync_TitleTooLong_ThrowsReviewTitleInvalid`.

**ReviewService_CreateDraftAsync_UnknownProject_ThrowsProjectNotFound**: A review in a project that does not exist
is rejected with `ProjectNotFound`, which is also the refusal for a project the actor may not see. This scenario is
tested by `ReviewService_CreateDraftAsync_UnknownProject_ThrowsProjectNotFound`.

**ReviewService_CreateDraftAsync_NoFiles_ThrowsNoFiles**: A review without files is rejected with `NoFiles`. This
scenario is tested by `ReviewService_CreateDraftAsync_NoFiles_ThrowsNoFiles`.

**ReviewService_CreateDraftAsync_DuplicatePaths_ThrowsDuplicatePath**: Two files with the same path are rejected
with `DuplicatePath`. This scenario is tested by `ReviewService_CreateDraftAsync_DuplicatePaths_ThrowsDuplicatePath`.

**ReviewService_CreateDraftAsync_PathsDifferingInCase_AreDistinct**: Two paths that differ only in letter case are
both accepted, because paths are compared ordinally. This scenario is tested by
`ReviewService_CreateDraftAsync_PathsDifferingInCase_AreDistinct`.

**ReviewService_CreateDraftAsync_BlankPath_ThrowsPathInvalid**: Empty and white-space paths are rejected with
`PathInvalid`. This scenario is tested by `ReviewService_CreateDraftAsync_BlankPath_ThrowsPathInvalid`.

**ReviewService_CreateDraftAsync_PathTooLong_ThrowsPathInvalid**: A path longer than the maximum is rejected with
`PathInvalid`. This scenario is tested by `ReviewService_CreateDraftAsync_PathTooLong_ThrowsPathInvalid`.

**ReviewService_CreateDraftAsync_FileSize_LimitIsInclusive**: A file of exactly the maximum size is accepted and a
file one byte larger is rejected with `FileTooLarge`. This scenario is tested by
`ReviewService_CreateDraftAsync_FileSize_LimitIsInclusive`.

**ReviewService_CreateDraftAsync_TooManyFiles_ThrowsTooManyFiles**: More files than the maximum are rejected with
`TooManyFiles`. This scenario is tested by `ReviewService_CreateDraftAsync_TooManyFiles_ThrowsTooManyFiles`.

**ReviewService_CreateDraftAsync_TotalTooLarge_ThrowsTotalSizeExceeded**: Files that are each within the limit but
together larger than the total limit are rejected with `TotalSizeExceeded`. This scenario is tested by
`ReviewService_CreateDraftAsync_TotalTooLarge_ThrowsTotalSizeExceeded`.

**ReviewService_GetAsync_CreatedReview_ReturnsReviewListAndContent**: A created review is read back, appears in
the list of its project, and the content of a file is read. This scenario is tested by
`ReviewService_GetAsync_CreatedReview_ReturnsReviewListAndContent`.

**ReviewService_GetFileContentAsync_MissingFileOrReview_ReturnsNull**: The content of a file that does not exist
in an existing review, and of a file of a review that does not exist, is read; both return null. This scenario is
tested by `ReviewService_GetFileContentAsync_MissingFileOrReview_ReturnsNull`.

**ReviewService_CreateDraftAsync_Review_RecordsReviewCreatedWithFingerprint**: A review is created by a named actor;
the audit entry `review.created` is recorded with the review title, the file count, and the fingerprint in the
detail. This scenario is tested by `ReviewService_CreateDraftAsync_Review_RecordsReviewCreatedWithFingerprint`, and
the plural wording for several files by `ReviewService_CreateDraftAsync_SeveralFiles_RecordsPluralCount`; both are
defined in `AuditEventTests.cs`.

**ReviewService_CreateDraftAsync_ApiActor_FlagsReviewAndAuditText**: A draft is created by an actor that carries a token
id. The review records the user name as creator, is flagged as created through the API, and the `review.created` detail
ends with `, created via API token 'K7M2QX9P'`. This scenario is tested by
`ReviewService_CreateDraftAsync_ApiActor_FlagsReviewAndAuditText`.

**ReviewService_CreateDraftAsync_UiActor_RecordsCreatorWithoutApiFlag**: A draft is created by a person who signed in
with a password. The creator is recorded, the review is not flagged, and the audit detail has no token text. This
scenario is tested by `ReviewService_CreateDraftAsync_UiActor_RecordsCreatorWithoutApiFlag`.

**ReviewService_CreateFromRepositoryAsync_ApiActor_FlagsReviewAndAuditText**: A review is created from a repository by
an actor that carries a token id. The review is flagged, records the creator, and the one `review.created` detail starts
with `review 'Title', created via API token 'K7M2QX9P' from repository`. This scenario is tested by
`ReviewService_CreateFromRepositoryAsync_ApiActor_FlagsReviewAndAuditText`, defined in
`ReviewServiceRepositoryTests.cs`.

**Reviews from a repository** are verified in `ReviewServiceRepositoryTests.cs` with the real service, a fake store,
and a fake git provider that serves scripted changes and blobs (`FakeGitRepositoryProvider`); one test uses a real
temporary git repository (`ReviewFromGitTests.cs`, skipped when git is not installed).

**ReviewService_CreateFromRepositoryAsync_Changes_FreezesFilesAndSource**: A review is created from an added, a
modified, and a deleted file; the review holds both commits, the selection, the repository, and for every file the
kind and the baseline and head bytes, with the deleted file having empty head content. This scenario is tested by
`ReviewService_CreateFromRepositoryAsync_Changes_FreezesFilesAndSource`.

**ReviewService_CreateFromRepositoryAsync_Changes_FingerprintCoversHeadFilesOnly**: The fingerprint equals that of
the added and modified files; the deleted file does not take part. This scenario is tested by
`ReviewService_CreateFromRepositoryAsync_Changes_FingerprintCoversHeadFilesOnly`.

**ReviewService_CreateFromRepositoryAsync_Selection_ChoosesFiles**: Include and exclude patterns choose which changed
files are reviewed. This scenario is tested by `ReviewService_CreateFromRepositoryAsync_Selection_ChoosesFiles`.

**ReviewService_CreateFromRepositoryAsync_SelectionOfDeletedFile_ReviewsIt**: A deleted file is matched by its path
and reviewed. This scenario is tested by `ReviewService_CreateFromRepositoryAsync_SelectionOfDeletedFile_ReviewsIt`.

**ReviewService_CreateFromRepositoryAsync_NoMatchingChange_RefusesWithNothingToReview**: A selection that matches no
changed file is refused with `NothingToReview` and the message names the baseline and the head; no review is stored.
This scenario is tested by `ReviewService_CreateFromRepositoryAsync_NoMatchingChange_RefusesWithNothingToReview`, and
the wording without a baseline by `ReviewService_CreateFromRepositoryAsync_NoBaselineNoMatch_NamesBeginningOfHistory`.

**ReviewService_CreateFromRepositoryAsync_BlankBaseline_ReviewsAllHeadFilesAsAdded**: A null or blank baseline
reviews every file at the head as added. This scenario is tested by
`ReviewService_CreateFromRepositoryAsync_BlankBaseline_ReviewsAllHeadFilesAsAdded`.

**ReviewService_CreateFromRepositoryAsync_SkippedEntries_ReturnedAndCounted**: Skipped symbolic links and submodules
that match the selection are returned in the result and counted in the audit. This scenario is tested by
`ReviewService_CreateFromRepositoryAsync_SkippedEntries_ReturnedAndCounted`.

**ReviewService_CreateFromRepositoryAsync_Changes_AuditDetailDescribesSource**: The `review.created` entry names the
repository, both commits, the counts, and the fingerprint. This scenario is tested by
`ReviewService_CreateFromRepositoryAsync_Changes_AuditDetailDescribesSource`.

**ReviewService_CreateFromRepositoryAsync_RepositoryChangesLater_ReviewIsUnaffected**: After the review is created
the fake repository changes; the stored files, the fingerprint, and the record digest are the same, and nothing
reads the repository again. This scenario is tested by
`ReviewService_CreateFromRepositoryAsync_RepositoryChangesLater_ReviewIsUnaffected`, and with a real git repository
(a further commit after the creation) by
`ReviewService_CreateFromRepositoryAsync_RealGit_FreezesChangesAgainstLaterCommits`.

**ReviewService_CreateFromRepositoryAsync_FileTooLarge_RefusedBeforeReading**: A baseline or head blob over the
per-file limit is refused before any blob is read. This scenario is tested by
`ReviewService_CreateFromRepositoryAsync_FileTooLarge_RefusedBeforeReading`, the total limit by
`ReviewService_CreateFromRepositoryAsync_TotalTooLarge_RefusedBeforeReading`, and the file count by
`ReviewService_CreateFromRepositoryAsync_TooManyFiles_Refused`.

**ReviewService_CreateFromRepositoryAsync_BadRequests_AreRefusedWithTheirRule**: A disabled or unknown repository, an
unknown or invalid revision, an unusable selection, and a bad identifier are each refused with their rule
(`RepositoryDisabled`, `RepositoryNotFound`, `RevisionNotFound`, `RevisionInvalid`, `SelectionInvalid`,
`ReviewIdInvalid`). This scenario is tested by
`ReviewService_CreateFromRepositoryAsync_BadRequests_AreRefusedWithTheirRule`; a git failure is passed on
(`ReviewService_CreateFromRepositoryAsync_GitFailure_IsPassedOn`), a duplicate identifier or unknown project is
refused before the repository is opened
(`ReviewService_CreateFromRepositoryAsync_DuplicateIdOrUnknownProject_RefusedBeforeOpening`), and a service built
without repositories throws (`ReviewService_CreateFromRepositoryAsync_WithoutRepositories_Throws`).

**ReviewService_CreateFromRepositoryAsync_SameFilesOtherSource_ChangesDigest**: Two reviews with the same head files
but another selection text, head commit, or repository have different record digests. This scenario is tested by
`ReviewService_CreateFromRepositoryAsync_SameFilesOtherSource_ChangesDigest`.

**ReviewService_CreateFromRepositoryAsync_SelectionTooLong_ThrowsAndManualReviewHasNoSet**: A selection longer than
the limit is refused with `SelectionInvalid` before git is opened, and a manual review from a repository has no
review-set. This scenario is tested by
`ReviewService_CreateFromRepositoryAsync_SelectionTooLong_ThrowsAndManualReviewHasNoSet`.

**ReviewService_GetDiffAsync_ModifiedAddedDeleted_ComparesFrozenContentWithoutGit**: A modified, an added, and a
deleted file of a repository review are compared from the stored content; the rows, the counts, and the line numbers
of both sides are as expected, the missing side of an added or deleted file is empty, and git is never opened. This
scenario is tested by `ReviewService_GetDiffAsync_ModifiedAddedDeleted_ComparesFrozenContentWithoutGit`.

**ReviewService_GetDiffAsync_UnknownFileOrReview_ReturnsNull**: A file that is not in the review and a review that
does not exist both give null. This scenario is tested by `ReviewService_GetDiffAsync_UnknownFileOrReview_ReturnsNull`.

**ReviewService_GetDiffAsync_NoBaselineOrUpload_ReportsNoBaseline**: A file of an uploaded review and a file of a
repository review without a baseline commit give the status `NoBaseline` and no rows. This scenario is tested by
`ReviewService_GetDiffAsync_NoBaselineOrUpload_ReportsNoBaseline`.

**ReviewService_GetDiffAsync_BinarySide_ReportsBinary**: A theory over a binary baseline and a binary head gives
the status `Binary` and no rows. This scenario is tested by `ReviewService_GetDiffAsync_BinarySide_ReportsBinary`.

**ReviewService_GetDiffAsync_BeyondLineLimit_ReportsTooLarge**: A file with more lines than the diff limit gives the
status `TooLarge` and no rows. This scenario is tested by `ReviewService_GetDiffAsync_BeyondLineLimit_ReportsTooLarge`.

**ReviewService_GetDiffAsync_OnlyLineEndingsDiffer_IsFlagged**: A theory over two contents that differ only in their
line endings gives no changed rows and sets the flag that says the line endings differ. This scenario is tested by
`ReviewService_GetDiffAsync_OnlyLineEndingsDiffer_IsFlagged`.

**Reviews created from a plan** use `CreateFromSessionAsync` through the planner and are verified in
`ReviewPlannerTests.cs` (see the `ReviewPlanner` verification): the review carries the set identifier and the audit
entry ends with `, set '{id}'` (`ReviewPlanner_CreateAsync_PlanRows_CreatesReviewsWithSetIdAndOneSession`), an
overlong set identifier is refused (`ReviewPlanner_CreateAsync_InvalidRequests_FailPerRequest`), and the longest title,
repository name, and set identifier still fit in the audit detail
(`ReviewPlanner_CreateAsync_LongestInputs_AuditDetailFits`).
