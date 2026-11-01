### ReviewProgressService

#### Verification Approach

`ReviewProgressService` is verified with unit tests defined in `ReviewProgressServiceTests.cs`. The tests use the real
`ReviewProgressService`, `ReviewService`, `SigningService`, `ChecklistService`, `ThreadService`, `UserService`, and
`ProjectAccess` over the in-memory fakes `FakeReviewStore`, `FakeUserStore`, and `FakeAuditStore`, with a movable clock
(`MovableTimeProvider`, set to 4 March 2025) so that no database is needed. One arranged world has a project with the
users `root` (a system administrator), `padm` (a project administrator), `creator`, `member`, `reviewer1`, `reviewer2`,
and `auditor` (a read-only member), and a user `stranger` outside the project. The blocker counts are compared with the
numbers in the blocker sentences of the approval status read from `SigningService`. The audit store behind the last
activity is the fake one; the grouped query itself is verified in the `AuditStore` unit against a real database
(`AuditStoreTests.cs`), and the counts and the sentences in the `SigningService` unit (`SigningServiceTests.cs`). The
project page and the API list that show the answer are verified in their own units.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A review without a slot is `Setup`, one with an unsigned or out-of-date slot is `InReview`, and one whose every slot has
  a valid signature and nothing blocks it is `Approved`; a project without reviews gives an empty list; a reopened review
  is `InReview` and flagged as reopened.
- A review is `Approved` only when every slot has a valid signature, nothing blocks it, and no thread is open; a
  review whose slots are all signed but that has an open thread is `InReview` and blocked.
- A review whose ballots agree on a Fail, with a conclusion text and every slot signed for the rejection, has the stage
  `Rejected`, a conclusion of Rejected, is not approved, is not blocked, and waits on nobody. A Fail that is agreed is no
  blocker: the failed count is kept, no blocker names it, and the review is blocked only while the conclusion text is
  missing (`ConclusionRequired`).
- The signature counts equal the slots of the approval status; a comment that is included in the report, added after a
  signature, turns that signature from signed into out of date, while chat does not.
- The open-thread and unanswered-check counts equal the numbers in the blocker sentences, and the Fail count is the
  informational count of the status; a review with open threads, unanswered checks, disputed checks, a missing
  conclusion text, or without a checklist, that is not concluded, is blocked, a concluded review is not.
- `WaitingOn` names the primary and a different alternate of every slot that is not signed, each person once, leaves out
  signed slots, shows a disabled person with the suffix, and is empty for a concluded review.
- A review is overdue from the day after its due date and not on it, never without a date, and never when concluded.
- The last activity is the time of the newest audit entry of the review, the creation time when there is none, and
  entries of another review, of another project, and without a review do not count.
- Members, read-only members, project administrators, and system administrators may read the progress; a user outside the
  project and no actor at all get `ProjectNotFound`.
- The progress of a project with 300 reviews reads the audit trail once, is complete, and is produced within a loose time
  bound of ten seconds (a guard against one query per review, not a benchmark).
- The overload that is given the review list returns the same progress as the one that lists the reviews, makes no
  further read of the review list, uses exactly the given list (a subset, in the given order; an empty list gives an
  empty answer without reading the last activity), still checks the access, and refuses a null list.
- A null collaborator is refused with an argument exception.

#### Test Scenarios

**ReviewProgressService_GetProgressAsync_SlotsAndSignatures_DeriveTheStage**: Three reviews: one without a slot, one with
an unsigned slot, and one signed by its only reviewer after the checklist was answered. The list is ordered by identifier
and the stages are Setup, In review, and Approved, with 0, 1, and 1 slots required and 0, 0, and 1 signed; the approved
review waits on nobody. This scenario is tested by `ReviewProgressService_GetProgressAsync_SlotsAndSignatures_DeriveTheStage`.

**ReviewProgressService_GetProgressAsync_ProjectWithoutReviews_IsEmpty**: A project without reviews gives an empty list.
This scenario is tested by `ReviewProgressService_GetProgressAsync_ProjectWithoutReviews_IsEmpty`.

**ReviewProgressService_GetProgressAsync_BlockerCounts_EqualTheApprovalBlockers**: A review has a Fail answer, unanswered
checks, and two open threads; the counts are compared with the numbers in the blocker sentences of the approval status
and with the counts of the status, and the review is blocked. An approved review has no blockers and is not blocked. This
scenario is tested by `ReviewProgressService_GetProgressAsync_BlockerCounts_EqualTheApprovalBlockers`.

**ReviewProgressService_GetProgressAsync_RejectedConcluded_StageRejectedNotBlocked**: One slot whose ballot is a Fail,
with the conclusion text, signs the rejection. The stage is Rejected, the conclusion is Rejected, the review is not
approved and not blocked, nobody is waited on, and one slot is signed. This scenario is tested by
`ReviewProgressService_GetProgressAsync_RejectedConcluded_StageRejectedNotBlocked`.

**ReviewProgressService_GetProgressAsync_RejectedBlankText_BlockedConclusionRequired**: One slot whose ballot is a Fail,
without the conclusion text. The stage is In review, `ConclusionRequired` is set, the review is blocked, the failed count
is one, the approval blockers hold `SigningService.ConclusionRequiredText`, and no blocker names Fail. This scenario is
tested by `ReviewProgressService_GetProgressAsync_RejectedBlankText_BlockedConclusionRequired`.

**ReviewProgressService_GetProgressAsync_FailOnlyAgreed_NotBlocked**: One slot whose ballot is a Fail, with the
conclusion text, not yet signed. The stage is In review, no conclusion is needed, the review is not blocked, the failed
count is one, and the approval has no blocker. This scenario is tested by
`ReviewProgressService_GetProgressAsync_FailOnlyAgreed_NotBlocked`.

**ReviewProgressService_GetProgressAsync_ReviewWithoutChecklist_IsBlocked**: A review is stored without a checklist,
and another review has a checklist, every check answered, and a slot. The first has `NoChecklist` set, is blocked, is
not approved, and has no counted blockers; its approval status has the sentence "This review has no checklist." and the
same flag. The second has no flag and is not blocked. This scenario is tested by
`ReviewProgressService_GetProgressAsync_ReviewWithoutChecklist_IsBlocked`.

**ReviewProgressService_GetProgressAsync_CommentAfterSignature_CountsOutOfDate**: A review with two slots has the first
signed (2 required, 1 signed, 0 out of date); a thread that is included in the report is started with the signature
acknowledged, which changes the record, and the progress shows 2
required, 0 signed, 1 out of date, one open thread, and the stage In review. This scenario is tested by
`ReviewProgressService_GetProgressAsync_CommentAfterSignature_CountsOutOfDate`.

**ReviewProgressService_GetProgressAsync_AllSignedOpenThread_IsInReviewAndBlocked**: Every slot is signed and a thread
is open; the stage is In review, the review is blocked with one open thread, and it is not approved. This scenario is
tested by `ReviewProgressService_GetProgressAsync_AllSignedOpenThread_IsInReviewAndBlocked`.

**ReviewProgressService_GetProgressAsync_ReopenedReview_IsFlaggedAndNotApproved**: Two approved reviews; a project
administrator reopens the first. It is flagged as reopened, not approved, In review, with one out-of-date signature; the
other is still approved and not flagged. This scenario is tested by
`ReviewProgressService_GetProgressAsync_ReopenedReview_IsFlaggedAndNotApproved`.

**ReviewProgressService_GetProgressAsync_WaitingOn_NamesPrimaryAndAlternateOfOpenSlots**: A review has one slot with a
primary and an alternate and one slot with another primary. The names are the primary and the alternate of the first slot
and then the primary of the second; after the first reviewer signs, only the second is named; after the account of the
second is disabled, the name carries the suffix. This scenario is tested by
`ReviewProgressService_GetProgressAsync_WaitingOn_NamesPrimaryAndAlternateOfOpenSlots`.

**ReviewProgressService_GetProgressAsync_DueDate_OverdueFromTheDayAfter**: Reviews due today, due tomorrow and then moved
to yesterday, without a date, and approved with a date of yesterday. Only the one that was moved to yesterday is overdue;
the dates are carried. This scenario is tested by
`ReviewProgressService_GetProgressAsync_DueDate_OverdueFromTheDayAfter`.

**ReviewProgressService_GetProgressAsync_LastActivity_ComesFromTheAuditTrail**: The audit trail gets two entries of the
first review an hour apart, an entry without a review, and an entry of the same review identifier in another project; the
last activity of the first review is the time of its second entry, and that of the second review, which has no entry, is
its creation time. This scenario is tested by
`ReviewProgressService_GetProgressAsync_LastActivity_ComesFromTheAuditTrail`.

**ReviewProgressService_GetProgressAsync_ActorKinds_AreAllowedOrNotFound**: A theory over a member, a read-only
member, a
project administrator, a system administrator, a user outside the project, and no actor. The first four get the same
single row, the last two get `ProjectNotFound`. This scenario is tested by
`ReviewProgressService_GetProgressAsync_ActorKinds_AreAllowedOrNotFound`.

**ReviewProgressService_GetProgressAsync_ManyReviews_ReadsActivityOnceAndIsFast**: 300 reviews, a third with a slot, over
an audit store that counts the reads of the last activity. All 300 are returned, 100 are In review, the audit trail was
read once, and the call finished within ten seconds. This scenario is tested by
`ReviewProgressService_GetProgressAsync_ManyReviews_ReadsActivityOnceAndIsFast`.

**ReviewProgressService_GetProgressAsync_GivenList_EqualsOriginalAndReadsNoList**: Three reviews (one with a due date, one
with a primary and an alternate, one signed) are read with the overload that lists them and then with the one that is
given the list, over a `FakeReviewStore` that counts the list reads. The two answers are equal field by field, the first
made one list read and the second none. This scenario is tested by
`ReviewProgressService_GetProgressAsync_GivenList_EqualsOriginalAndReadsNoList`.

**ReviewProgressService_GetProgressAsync_GivenSubset_ReturnsOnlyThoseInGivenOrder**: A list of two of three reviews, the
later one first, gives exactly those two in that order. This scenario is tested by
`ReviewProgressService_GetProgressAsync_GivenSubset_ReturnsOnlyThoseInGivenOrder`.

**ReviewProgressService_GetProgressAsync_GivenEmptyList_IsEmptyAndReadsNothingElse**: An empty list gives an empty answer
and the audit store counts no read of the last activity. This scenario is tested by
`ReviewProgressService_GetProgressAsync_GivenEmptyList_IsEmptyAndReadsNothingElse`.

**ReviewProgressService_GetProgressAsync_GivenList_ChecksAccessAndRefusesNullList**: With a valid list a user outside the
project and no actor get `ProjectNotFound`, a member gets the row, and a null list raises `ArgumentNullException`. This
scenario is tested by `ReviewProgressService_GetProgressAsync_GivenList_ChecksAccessAndRefusesNullList`.

**ReviewProgressService_Constructor_NullArgument_Throws**: A null review service, signing service, audit store, or access
rule each raise `ArgumentNullException`. This scenario is tested by `ReviewProgressService_Constructor_NullArgument_Throws`.
