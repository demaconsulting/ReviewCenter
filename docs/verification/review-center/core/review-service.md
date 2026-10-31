### ReviewService

#### Verification Approach

`ReviewService` is verified with unit tests defined in `ReviewServiceTests.cs`, the freezing of the checklist of the
project in `ReviewServiceChecklistTests.cs` (which sets the checklist through the real `ProjectChecklistService`), and
the due date in `ReviewServiceDueDateTests.cs`, and the follow-up review in `FollowUpCarryTests.cs` and
`ReviewServiceFollowUpTests.cs`. The
tests use the real service and the real `Fingerprint` with a fake in-memory store (`FakeReviewStore`), the
project-access double
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
- A new review, from files or from a repository or a plan, freezes the checklist of its project (the built-in default
  when the project has none); a stored definition that cannot be used refuses the review with
  `ChecklistDefinitionInvalid` and never falls back to the default; a later change of the checklist of the project
  changes neither the wording nor the record digest of an existing review, approved or not.
- A review may carry an optional due date, a calendar day in UTC, that is not part of the record digest: it is
  accepted at creation from today to two years ahead and refused outside that range with `DueDateInvalid`; it is
  audited at creation, and changed or cleared with one entry `review.due-date-changed` by a project administrator, a
  system administrator, or the creator, never through a token, never for an approved review (`ReviewLocked`); a review
  is overdue only when the date has passed and it is not approved.
- A review can be created as a follow-up of an earlier review of the same project: the Pass and N/A answers of checks
  with unchanged wording, the not-applicable sections with the same title, and the open threads on files of the new
  review are carried, marked with the earlier review, and stored with it in one step, each thread with its comments
  keeping their kinds and report flags (a Finding stays a Finding, chat stays chat, and the note about a moved anchor
  is a comment that is not included); Fail answers, resolved threads,
  slots, signatures and reopenings are not; a line anchor is kept only for identical content; an unknown or foreign
  review, a review that does not fit, and dropped open threads without an acknowledgement are refused with
  `FollowUpInvalid`; the audit entry names the carry; a review that follows nothing is unchanged. The preview of a
  follow-up (`PreviewFollowUpAsync`) reports, for a set of files, what the creation would carry and drop, by the same
  rule, writes and audits nothing, refuses an unfit review with `FollowUpInvalid`, and is refused as a missing project
  for a user who may not see the project.

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

**ReviewService_CreateFromRepositoryAsync_NullSelection_ReviewsEveryChangedFile**: A review is created without a
selection (`null`); every changed file is reviewed and the stored selection is `**/*`, while empty text is still refused
with `SelectionInvalid`. This scenario is tested by
`ReviewService_CreateFromRepositoryAsync_NullSelection_ReviewsEveryChangedFile`.

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

**ReviewService_Create_ProjectChecklist_IsFrozenAtBothCreatePaths**: The project is given a checklist of its own; a
review is created from uploaded files and another from a repository. Both carry that definition in its normal form with
its version label, and the checklist service shows its wording. This scenario is tested by
`ReviewService_Create_ProjectChecklist_IsFrozenAtBothCreatePaths`.

**ReviewService_Create_NoProjectChecklist_FreezesTheDefaultAtBothCreatePaths**: A project that never had a checklist,
and one that was reset to the default, give the built-in default to reviews created from files and from a repository.
This scenario is tested by `ReviewService_Create_NoProjectChecklist_FreezesTheDefaultAtBothCreatePaths`.

**ReviewService_Create_UnusableProjectChecklist_FailsClosed**: A theory over a stored definition that is not JSON and one
that has no sections, at both create paths; each raises `ChecklistDefinitionInvalid` with a message that names the
project, stores no review and no entry, and never falls back to the default. This scenario is tested by
`ReviewService_Create_UnusableProjectChecklist_FailsClosed`.

**ReviewService_ProjectChecklistChangedLater_ExistingReviewsAreUnaffected**: A review from files and a review from a
repository are created; the project then changes its checklist, goes back to the default, and changes it again. The two
reviews keep their version, their wording, and their record digest; a review created afterwards has the newest definition.
This scenario is tested by `ReviewService_ProjectChecklistChangedLater_ExistingReviewsAreUnaffected`.

**ReviewService_ProjectChecklistChangedLater_ApprovedReviewIsUnaffected**: An approved review is made under the default;
the project then replaces its checklist. The record digest, the checklist version, and the answers are unchanged, and
the review is still locked. This scenario is tested by
`ReviewService_ProjectChecklistChangedLater_ApprovedReviewIsUnaffected`.

**ReviewPlanner_CreateAsync_ProjectChecklist_IsFrozenIntoTheReview**: A review that the planner creates for a review-set
carries the checklist of the project. This scenario is tested by
`ReviewPlanner_CreateAsync_ProjectChecklist_IsFrozenIntoTheReview`, which is defined in `ReviewPlannerTests.cs`.

**The due date** is verified in `ReviewServiceDueDateTests.cs` with the real service, a fake store, and a fixed clock.

**ReviewService_CreateDraftAsync_DueDateInRange_StoresDateAndAuditsIt**: A theory creates a review with a due date of
today, one day ahead, and exactly two years ahead (UTC). Each is stored as the calendar day, is not part of the record
digest, and the `review.created` entry ends with `, due yyyy-MM-dd`. This scenario is tested by
`ReviewService_CreateDraftAsync_DueDateInRange_StoresDateAndAuditsIt`.

**ReviewService_CreateDraftAsync_NoDueDate_HasNoDateAndNoAuditText**: A review without a due date has none, and its
audit entry has no due text. This scenario is tested by `ReviewService_CreateDraftAsync_NoDueDate_HasNoDateAndNoAuditText`.

**ReviewService_CreateDraftAsync_DueDateOutOfRange_IsRefusedAndStoresNothing**: A due date of yesterday, and one a day
beyond two years ahead, are refused with `DueDateInvalid`; no review and no entry is stored. This scenario is tested by
`ReviewService_CreateDraftAsync_DueDateOutOfRange_IsRefusedAndStoresNothing`.

**ReviewService_SetDueDateAsync_ProjectAdmin_ChangesAndAuditsOldAndNew**: A project administrator sets, then changes,
the date; the review shows it and each change is one entry `review.due-date-changed` with the old and the new date,
written with the change. A date in the past is accepted. This scenario is tested by
`ReviewService_SetDueDateAsync_ProjectAdmin_ChangesAndAuditsOldAndNew`.

**ReviewService_SetDueDateAsync_CreatorAndSystemAdmin_AreAllowed**: The creator of the review and a system
administrator may set the date. This scenario is tested by `ReviewService_SetDueDateAsync_CreatorAndSystemAdmin_AreAllowed`.

**ReviewService_SetDueDateAsync_OtherMemberOrOutsider_IsRefusedAndChangesNothing**: A member who did not create the
review, and a person outside the project, are refused (`ActorNotAllowed`, `ProjectNotFound`) and nothing changes. This
scenario is tested by `ReviewService_SetDueDateAsync_OtherMemberOrOutsider_IsRefusedAndChangesNothing`.

**ReviewService_SetDueDateAsync_CreatorNowReadOnly_IsRefused**: A creator who has since become a read-only member
(Auditor) is refused (`ActorNotAllowed`), the date is unchanged and the edit control is not offered. This scenario is
tested by `ReviewService_SetDueDateAsync_CreatorNowReadOnly_IsRefused`.

**ReviewService_SetDueDateAsync_ApiToken_IsRefused**: An actor that came through a personal access token is refused,
even when its owner is the creator or an administrator. This scenario is tested by
`ReviewService_SetDueDateAsync_ApiToken_IsRefused`.

**ReviewService_SetDueDateAsync_ClearAndUnchanged_AuditsOnlyTheChange**: Clearing removes the date and is audited as
`<old> -> none`; setting the same value again returns false and writes no entry. This scenario is tested by
`ReviewService_SetDueDateAsync_ClearAndUnchanged_AuditsOnlyTheChange`.

**ReviewService_SetDueDateAsync_TooFarAhead_IsRefused**: A date beyond two years ahead is refused with
`DueDateInvalid` on edit too. This scenario is tested by `ReviewService_SetDueDateAsync_TooFarAhead_IsRefused`.

**ReviewService_SetDueDateAsync_MissingReview_IsNotFound**: A review that does not exist is refused with
`ReviewNotFound`. This scenario is tested by `ReviewService_SetDueDateAsync_MissingReview_IsNotFound`.

**ReviewService_SetDueDateAsync_ApprovedReview_IsLockedAndNeverOverdue**: An approved review refuses the change with
`ReviewLocked` (reopen the review first), keeps its date, and is never overdue even when the date has passed. This
scenario is tested by `ReviewService_SetDueDateAsync_ApprovedReview_IsLockedAndNeverOverdue`.

**ReviewService_SetDueDateAsync_ChangedByOtherMeanwhile_FailsAsRecordChanged**: The date is changed through the store
after the service read it; the compare-and-set fails with `RecordChanged` and the other value stays. This scenario is
tested by `ReviewService_SetDueDateAsync_ChangedByOtherMeanwhile_FailsAsRecordChanged`.

**ReviewService_SetDueDateAsync_AfterSignature_KeepsDigestAndSignature**: A review is signed before the date is
changed (this applies while the review is not yet approved); the record digest is the same and the signature still
verifies. This scenario is tested by `ReviewService_SetDueDateAsync_AfterSignature_KeepsDigestAndSignature`.

**ReviewService_IsOverdue_PastDateAndNotApproved_IsTheOnlyOverdueCase**: Only a date before today (UTC) on a review
that is not approved is overdue; no date, today, a future date, and an approved review are not. This scenario is tested
by `ReviewService_IsOverdue_PastDateAndNotApproved_IsTheOnlyOverdueCase`.

**ReviewService_ParseDueDate_IsoTextOnly_ReadsDateOrRefuses**: Missing, null, and blank text is no date; `yyyy-MM-dd`
is read; any other text raises `DueDateInvalid`. This scenario is tested by
`ReviewService_ParseDueDate_IsoTextOnly_ReadsDateOrRefuses`.

**The follow-up review** is verified in `FollowUpCarryTests.cs` (the pure helper by itself, with records built in
memory and no store) and `ReviewServiceFollowUpTests.cs` (the real service, `FakeReviewStore`, and a fixed clock; the
repository paths use a fake git provider and the real `RepositoryService`).

**FollowUpCarry_Compute_PassAndNotApplicableAnswers_AreCarriedWithOriginalAnswerer**: Pass and N/A answers of checks
that are in the new checklist with the same wording are carried with their outcome, justification, answerer and time,
marked with the earlier review. This scenario is tested by
`FollowUpCarry_Compute_PassAndNotApplicableAnswers_AreCarriedWithOriginalAnswerer`.

**FollowUpCarry_Compute_FailChangedAndMissingChecks_AreNotCarried**: A Fail answer, a check whose wording changed, and a
check that is not in the new checklist are not carried. This scenario is tested by
`FollowUpCarry_Compute_FailChangedAndMissingChecks_AreNotCarried`.

**FollowUpCarry_Compute_SourceWithoutChecklist_CarriesNoChecklistRows**: An earlier review without a checklist carries
no answer and no section state. This scenario is tested by
`FollowUpCarry_Compute_SourceWithoutChecklist_CarriesNoChecklistRows`.

**FollowUpCarry_Compute_NotApplicableSections_AreCarriedOnlyWhenTheSectionIsTheSame**: A not-applicable section with the
same title is carried with its reason; one with another title, one that is gone, and a section that applies are not.
This scenario is tested by `FollowUpCarry_Compute_NotApplicableSections_AreCarriedOnlyWhenTheSectionIsTheSame`.

**FollowUpCarry_Compute_OpenThread_IsCarriedWithAllCommentsVerbatim**: An open thread on a file of the new review is
carried open with every comment (author, user name, text, time) unchanged, its original time, and the marks of the
review and the thread it came from. This scenario is tested by
`FollowUpCarry_Compute_OpenThread_IsCarriedWithAllCommentsVerbatim`.

**FollowUpCarry_CarryThreads_CarriedFinding_KeepsKindFlagsAndResolutionComments**: A carried Finding keeps the flags
and the kinds of its comments, including its Resolution comments. This scenario is tested by
`FollowUpCarry_CarryThreads_CarriedFinding_KeepsKindFlagsAndResolutionComments`.

**FollowUpCarry_CarryThreads_CarriedChat_StaysChat**: A carried chat thread keeps its comments as chat. This scenario
is tested by `FollowUpCarry_CarryThreads_CarriedChat_StaysChat`.

**FollowUpCarry_CarryThreads_MovedAnchorNote_IsExcludedComment**: The note about a moved anchor is added as a comment
that is not included in the report and has the kind Comment. This scenario is tested by
`FollowUpCarry_CarryThreads_MovedAnchorNote_IsExcludedComment`.

**FollowUpCarry_Compute_ResolvedAndDroppedThreads_AreCountedAndListed**: A resolved thread is counted and not carried;
an open thread on a file that the new review does not have is listed (sorted, once per file) and not carried. This
scenario is tested by `FollowUpCarry_Compute_ResolvedAndDroppedThreads_AreCountedAndListed`.

**FollowUpCarry_Compute_LineAnchor_IsKeptOnlyForIdenticalContent**: A theory over the head and the baseline side: a
line anchor is kept when the hash of its own side is the same in both reviews, and otherwise the thread becomes a
whole-file thread with a last comment by `ReviewCenter` that names the old lines and side. This scenario is tested by
`FollowUpCarry_Compute_LineAnchor_IsKeptOnlyForIdenticalContent`.

**FollowUpCarry_Compute_SingleLineWholeFileAndUnknownHash_AreHandled**: A single-line anchor is named as "line N" in the
note, a whole-file thread keeps its anchor and gets no note, and an empty or unknown hash never counts as the same
content. This scenario is tested by `FollowUpCarry_Compute_SingleLineWholeFileAndUnknownHash_AreHandled`.

**FollowUpCarry_CheckFit_Matrix**: A theory over the review-set, the repository, and the file paths: equal sets fit,
different sets do not; without sets the same repository fits, another repository or an upload against a repository does
not; two uploads fit; reviews without a common file do not. This scenario is tested by `FollowUpCarry_CheckFit_Matrix`.

**FollowUpCarry_DroppedMessage_ListsFilesAndTheRest**: The message names up to ten files and then says how many more,
and uses the singular for one file. This scenario is tested by `FollowUpCarry_DroppedMessage_ListsFilesAndTheRest`.

**FollowUpCarry_NullArguments_Throw**: A null argument of `Compute`, `CheckFit` or `DroppedMessage` raises
`ArgumentNullException`. This scenario is tested by `FollowUpCarry_NullArguments_Throw`.

**ReviewService_CreateDraftAsync_FollowUp_CarriesAnswersAndSections**: A review created with `followsReviewId` stores
the carried answers and not-applicable sections, marked with the earlier review, with the original answerer, and
`FollowsReviewId` and the counts. This scenario is tested by
`ReviewService_CreateDraftAsync_FollowUp_CarriesAnswersAndSections`.

**ReviewService_CreateDraftAsync_FollowUp_CarriedAnswersCountAsAnswered**: A checklist whose checks are all carried is
complete and has no unanswered check. This scenario is tested by
`ReviewService_CreateDraftAsync_FollowUp_CarriedAnswersCountAsAnswered`.

**ReviewService_CreateDraftAsync_FollowUp_ChangedChecklistWording_IsNotCarried**: After the checklist of the project
changed the wording of a check, the answer to that check is not carried and the check is unanswered. This scenario is
tested by `ReviewService_CreateDraftAsync_FollowUp_ChangedChecklistWording_IsNotCarried`.

**ReviewService_CreateDraftAsync_FollowUp_CarriesOpenThreadsAndAuditsCounts**: Open threads are carried; the entry
`review.created` ends the review text with `, follows 'X'; carried N answers, M threads` (`X` is the whole identifier) and
names the resolved threads
that were not carried. This scenario is tested by
`ReviewService_CreateDraftAsync_FollowUp_CarriesOpenThreadsAndAuditsCounts`.

**ReviewService_CreateReview_FollowUp_AuditNamesTheWholeSourceId**: Four earlier reviews have 100-character identifiers
that share their first 57 characters; a follow-up of each, two per create path, names the whole identifier in the
`review.created` detail, so the entries of two follow-ups differ, and no detail exceeds 500 characters. This scenario is
tested by `ReviewService_CreateReview_FollowUp_AuditNamesTheWholeSourceId`.

**ReviewService_CreateDraftAsync_NoFollowUp_HasNoCarriedData**: A review without `followsReviewId` follows nothing,
carries nothing, and its audit detail has no follow-up text. This scenario is tested by
`ReviewService_CreateDraftAsync_NoFollowUp_HasNoCarriedData`.

**ReviewService_CreateDraftAsync_UnknownOrForeignSource_IsRefusedWithoutRevealing**: A review that does not exist, and a
review of another project, are refused with `FollowUpInvalid` and the same message, which does not name the other
project, and nothing is stored. This scenario is tested by
`ReviewService_CreateDraftAsync_UnknownOrForeignSource_IsRefusedWithoutRevealing`.

**ReviewService_CreateDraftAsync_UploadFollowUp_NeedsACommonFile**: Two uploads with a file in common fit; without a
common file the follow-up is refused with `FollowUpInvalid`. This scenario is tested by
`ReviewService_CreateDraftAsync_UploadFollowUp_NeedsACommonFile`.

**ReviewService_CreateFollowUp_UploadRepositoryAndOtherRepository_AreRefused**: An upload cannot follow a repository
review and the reverse, and a review from one repository cannot follow one from another; each is `FollowUpInvalid`.
This scenario is tested by `ReviewService_CreateFollowUp_UploadRepositoryAndOtherRepository_AreRefused`.

**ReviewService_CreateFromRepositoryAsync_FollowUp_KeepsOrMovesAnchors**: A repository follow-up keeps the line anchor of
a thread whose file is unchanged and moves the thread of a changed file to the whole file with the note. This scenario
is tested by `ReviewService_CreateFromRepositoryAsync_FollowUp_KeepsOrMovesAnchors`.

**ReviewService_CreateFromRepositoryAsync_FollowUp_SameContent_KeepsTheLineAnchor**: When the file has the same content
in both reviews, the line anchor and the comments are kept without a note. This scenario is tested by
`ReviewService_CreateFromRepositoryAsync_FollowUp_SameContent_KeepsTheLineAnchor`.

**ReviewService_CreateDraftAsync_DroppedThreads_NeedAcknowledgement**: Open threads on a file that is not in the new
review refuse the creation with `FollowUpInvalid` and a message that lists the file; with
`acknowledgeDroppedThreads` the review is created without them and the audit detail says how many files were dropped.
This scenario is tested by `ReviewService_CreateDraftAsync_DroppedThreads_NeedAcknowledgement`.

**ReviewService_CreateDraftAsync_AcknowledgementWithoutDroppedThreads_IsHarmless**: The acknowledgement changes nothing
when no thread is dropped. This scenario is tested by
`ReviewService_CreateDraftAsync_AcknowledgementWithoutDroppedThreads_IsHarmless`.

**ReviewService_CreateDraftAsync_ApprovedSourceAndChain_PointToTheImmediateSource**: An approved earlier review can be
followed, and a follow-up of a follow-up points to the one before it, and what it carries is marked with that review.
This scenario is tested by `ReviewService_CreateDraftAsync_ApprovedSourceAndChain_PointToTheImmediateSource`.

**ReviewService_CreateFromSessionAsync_ReviewSet_MustMatch**: Reviews planned from the same review-set fit, and from
different sets are refused with `FollowUpInvalid`. This scenario is tested by
`ReviewService_CreateFromSessionAsync_ReviewSet_MustMatch`.

**ReviewService_FollowUp_ReAnsweringOrResettingSection_ClearsTheCarriedMarker**: Answering a carried check again, and
setting a carried section again, stores a row with no carried mark and with the new person. This scenario is tested by
`ReviewService_FollowUp_ReAnsweringOrResettingSection_ClearsTheCarriedMarker`.

**ReviewService_FollowUp_DigestBindsToFollowAndIsImmuneToLaterChecklistChange**: The record digest of a follow-up
differs from that of the same record without the follow-up, and a later change of the checklist of the project does
not change it. This scenario is tested by
`ReviewService_FollowUp_DigestBindsToFollowAndIsImmuneToLaterChecklistChange`.

**ReviewService_FollowUp_CreatorStaysTheAuthorForIndependence**: The creator of a follow-up that carried another
person's answers still cannot be seated as a reviewer while independent reviewers are required (`ReviewerNotIndependent`),
while the person who gave the carried answers can be. This
scenario is tested by `ReviewService_FollowUp_CreatorStaysTheAuthorForIndependence`.

**ReviewService_FollowUp_CarriedOpenThreadBlocksSigning_CarriedAnswersDoNot**: With only carried answers the review has
no blocker for the checklist; a carried open thread blocks with "1 thread is open", until it is resolved. This scenario
is tested by `ReviewService_FollowUp_CarriedOpenThreadBlocksSigning_CarriedAnswersDoNot`.

**ReviewService_FollowUp_ApprovedIsLockedAndCanBeReopened**: A follow-up that is approved is locked like any review and
can be reopened. This scenario is tested by `ReviewService_FollowUp_ApprovedIsLockedAndCanBeReopened`.

**ReviewService_FollowUp_DuplicateId_IsRefusedAndNothingChanges**: A follow-up with an identifier that exists is refused
with `ReviewIdDuplicate` and stores nothing. This scenario is tested by
`ReviewService_FollowUp_DuplicateId_IsRefusedAndNothingChanges`.

**ReviewService_PreviewFollowUpAsync_CountsWhatCreationCarries**: For an earlier review with a Pass, a Fail, a
not-applicable section, an open and a resolved thread on a file the new review has, and an open thread on a file it
lacks, the preview of the new files reports one carried answer, one carried section, one carried thread, one resolved
thread that is not carried, and the file of the dropped thread, and the creation of the same follow-up carries exactly
those numbers. This scenario is tested by `ReviewService_PreviewFollowUpAsync_CountsWhatCreationCarries`.

**ReviewService_PreviewFollowUpAsync_ChangesNothing**: After a preview, the number of reviews, audit entries and threads
is the same, and the earlier review keeps its digest. This scenario is tested by
`ReviewService_PreviewFollowUpAsync_ChangesNothing`.

**ReviewService_PreviewFollowUpAsync_UnfitSource_IsRefusedWithFollowUpInvalid**: A missing or blank review identifier, a
review that does not exist, a review of another project and an earlier review without a file in common are each refused
with `FollowUpInvalid`; the last message says that there is no file in common. This scenario is tested by
`ReviewService_PreviewFollowUpAsync_UnfitSource_IsRefusedWithFollowUpInvalid`.

**ReviewService_PreviewFollowUpAsync_InvalidFiles_AreRefusedLikeCreation**: A set of files with a duplicate path, a path
that is too long, or no file at all is refused with the rule of the creation (`DuplicatePath`, `PathInvalid`,
`NoFiles`) before any path is compared, and the message of the duplicate names the path. This scenario is tested by
`ReviewService_PreviewFollowUpAsync_InvalidFiles_AreRefusedLikeCreation`.

**The access to the preview** is verified with the other operations that take an actor, in
`ReviewOperations_NotAMember_AreRefusedAsAMissingProjectAndChangeNothing` (a user who is no member gets the not-found
refusal of a project that does not exist), `ReviewReads_Auditor_AreAllowed` (an auditor may preview), and
`AccessCoverage_PublicActorMethods_AreAllClassified` (the new method is classified as a read).
