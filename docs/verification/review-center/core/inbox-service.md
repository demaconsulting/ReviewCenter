### InboxService

#### Verification Approach

`InboxService` is verified with unit tests defined in `InboxServiceTests.cs` and `InboxRulesTests.cs`. The service tests
use the real `InboxService`, `ProjectAccess`, `RecordDigest`, and `SigningService` with the in-memory stand-ins for the
stores (`FakeReviewStore`, `FakeUserStore`, and `FakeInboxStore`, which share the review data of the test), and a fixed
clock that the test can move. The helper `InboxTestWorld` builds a project with members, reviews, slots, threads,
answers (a person answers into the ballot of the own slot, and the helper hands it in), signatures, and reopenings,
so each test states only the facts that matter. The signing service is used to give
real signatures, so a signature goes out of date the way it does in the product. The rules class is verified directly,
with plain values, so each rule can be checked without a store. The database side is verified by the tests of the
`InboxStore` unit, and the cooperation of the real services by the `Core` subsystem test.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All tests pass with zero failures.
- A reviewer who holds a slot of a review with a checklist and has not handed in the ballot finds the review under To
  answer, with the chip "Not started" or "Draft", the number of answered checks in the detail, the link to the checklist,
  and no other standing row. A reviewer who is blocked as author, a review without a checklist, and a concluded review
  give no To answer row; a ballot that goes back to a draft returns the row.
- A reviewer who handed in the ballot and may see the other ballots finds the review under Disputed, with the chip
  "{n} disputed", while checks or sections are disputed; the row goes away when the dispute is settled, and is unread
  after another reviewer hands in a ballot. The creator, who may see the ballots, finds it too.
- A reviewer who has not handed in the ballot never finds a Disputed row, and no dispute count, however the others
  answered.
- A reviewer who handed in the ballot, with nothing blocking the signature, and a slot without a current signature
  finds the review under Ready to sign, with the chip "Not signed" or "The review changed, sign again", and a signed slot
  gives no such row. An open thread, a dispute or a pending check puts the reviewer under Waiting for others instead.
- A reviewer who has done their part finds the review under Waiting for others, with a chip and the names of the people
  who are waited for; this row is never unread and is not counted as action.
- A thread in which the user wrote, or of the user's own review, whose latest comment is by someone else (the system
  counts) is a reply waiting; a thread whose latest comment is the user's own, and a resolved thread, are not.
- The author finds the failed checks of the review (the checks that the submitted ballots agree are Fail) with the
  number and the detail "Reviewers marked n checks as Fail so far. If that stands, the review will be rejected and its
  reasons must be written down.", and a Fail in a section that does not apply is ignored.
- An approval or a rejection by someone else, or a reopening by someone else, after the user last saw the review is an
  update (with the chip "Approved", "Rejected" or "Reopened"); it is not counted as action, and an own signature or an
  own reopening is no update. A review concluded as rejected has no failed-checks and no standing rows.
- A concluded review has no action row (a review whose slots are all signed but that has an open thread is not
  concluded, so it still has the Replies waiting rows), and a past due date marks an open review overdue and a
  concluded one not.
- An auditor, a non-member, a removed member, a disabled user, a user with a temporary password, no actor, the command
  line, and an unknown name get no standing or reply rows or an empty inbox without an error; a system administrator who
  is no member gets only what is their own.
- The action count is the number of distinct reviews in the five action groups (To answer, Disputed, Ready to sign,
  Replies waiting, Failed checks; not Waiting for others and not Updates), and a review with several reasons is
  listed once in each group.
- A comment is unread only when it is by someone else (without regard to case) and newer than the last seen one; a user
  without a state row finds all comments of others unread.
- Marking never moves the state back, stores nothing for a user who may not see the review, and marking all stores a
  state for every listed review, including the ones that were never opened.
- Missing arguments are refused.
- The Replies waiting row names the kinds of the threads that wait ("Waiting for your reply: 1 open finding, 2 open
  questions.").

#### Test Scenarios

**InboxService_GetInboxAsync_RepliesWaiting_TextNamesFindingsAndQuestions**: A finding and two questions wait for the
reply of the user; the chip is "3 waiting" and the detail names one open finding and two open questions. This scenario
is tested by `InboxService_GetInboxAsync_RepliesWaiting_TextNamesFindingsAndQuestions`.

**InboxService_GetInboxAsync_PrimarySlotNotSigned_ShowsToSignItem**: The primary reviewer of a slot without a signature
finds the review under Ready to sign with the chip "Not signed", and the row is unread. This scenario is tested by
`InboxService_GetInboxAsync_PrimarySlotNotSigned_ShowsToSignItem`.

**InboxService_GetInboxAsync_AlternateSlotNotSigned_ShowsToSignItem**: The alternate reviewer of the slot also finds the
review under Ready to sign. This scenario is tested by `InboxService_GetInboxAsync_AlternateSlotNotSigned_ShowsToSignItem`.

**InboxService_GetInboxAsync_SignatureOutOfDate_ShowsSignAgainChip**: After a signature the record changes (a new
answer); the signature no longer covers the record and the reviewer finds the chip "The review changed, sign again". This
scenario is tested by `InboxService_GetInboxAsync_SignatureOutOfDate_ShowsSignAgainChip`.

**InboxService_GetInboxAsync_SlotSigned_HasNoToSignItem**: A slot with a current signature gives its reviewer no Ready
to sign row. This scenario is tested by `InboxService_GetInboxAsync_SlotSigned_HasNoToSignItem`.

**InboxService_GetInboxAsync_ApprovedReview_HasNoActionItems**: When every slot is signed and the review is approved,
no reviewer has an action row; the other people get only an information row. This scenario is tested by
`InboxService_GetInboxAsync_ApprovedReview_HasNoActionItems`.

**InboxService_GetInboxAsync_AllSignedOpenThread_ShowsRepliesWaitingNotConcluded**: Every slot is signed and a thread
is open; the review is not concluded, so the person whose reply is waited for still finds it under Replies waiting.
This scenario is tested by `InboxService_GetInboxAsync_AllSignedOpenThread_ShowsRepliesWaitingNotConcluded`.

**InboxService_GetInboxAsync_Auditor_GetsNoSignOrReplyItems**: An auditor named in a slot and the author of a review
gets no Ready to sign and no Replies waiting row. This scenario is tested by
`InboxService_GetInboxAsync_Auditor_GetsNoSignOrReplyItems`.

**InboxService_GetInboxAsync_NonMemberOrRemovedMember_GetsEmptyView**: A user who is no member, and a member whose
membership was removed, get the empty view. This scenario is tested by
`InboxService_GetInboxAsync_NonMemberOrRemovedMember_GetsEmptyView`.

**InboxService_GetInboxAsync_DisabledUser_GetsEmptyView**: A disabled user gets the empty view and no error. This
scenario is tested by `InboxService_GetInboxAsync_DisabledUser_GetsEmptyView`.

**InboxService_GetInboxAsync_TemporaryPasswordUser_GetsEmptyView**: A user who still has a temporary password gets the
empty view. This scenario is tested by `InboxService_GetInboxAsync_TemporaryPasswordUser_GetsEmptyView`.

**InboxService_GetInboxAsync_AnonymousOrTokenOnlyActor_GetsEmptyView**: No actor, the command line, and a name that is
no user give the empty view and no error. This scenario is tested by
`InboxService_GetInboxAsync_AnonymousOrTokenOnlyActor_GetsEmptyView`.

**InboxService_GetInboxAsync_LatestCommentByOther_IAnswered_ShowsRepliesWaiting**: A thread in which the user wrote and
that someone else answered last is a reply waiting, with the chip "1 waiting". This scenario is tested by
`InboxService_GetInboxAsync_LatestCommentByOther_IAnswered_ShowsRepliesWaiting`.

**InboxService_GetInboxAsync_LatestCommentByMe_HasNoRepliesWaiting**: A thread whose latest comment is the user's own is
not waiting. This scenario is tested by `InboxService_GetInboxAsync_LatestCommentByMe_HasNoRepliesWaiting`.

**InboxService_GetInboxAsync_ResolvedThread_HasNoRepliesWaiting**: A resolved thread is not waiting. This scenario is
tested by `InboxService_GetInboxAsync_ResolvedThread_HasNoRepliesWaiting`.

**InboxService_GetInboxAsync_AuthorOfReview_ThreadByOther_ShowsRepliesWaiting**: The author of a review sees a thread
of someone else that nobody answered. This scenario is tested by
`InboxService_GetInboxAsync_AuthorOfReview_ThreadByOther_ShowsRepliesWaiting`.

**InboxService_GetInboxAsync_SystemCommentLast_CountsAsOther**: A comment of the system as the latest comment counts as
a comment of someone else. This scenario is tested by `InboxService_GetInboxAsync_SystemCommentLast_CountsAsOther`.

**InboxService_GetInboxAsync_FailAnswersInApplicableSections_ShowsFailedChecksWithCount**: Bob, seated in a slot of the
review, answers two checks Fail and hands the ballot in (the helper does both); the author sees the failed checks of the
review with the number, the failures are new to the author, and bob sees no such row. The number is the one of the
consolidated standing. This scenario is tested by
`InboxService_GetInboxAsync_FailAnswersInApplicableSections_ShowsFailedChecksWithCount`.

**InboxService_GetInboxAsync_NoBallot_ShowsToAnswerNotStarted**: A reviewer who has not started the ballot finds the
review under To answer with the chip "Not started" and an unread row. This scenario is tested by
`InboxService_GetInboxAsync_NoBallot_ShowsToAnswerNotStarted`.

**InboxService_GetInboxAsync_DraftBallot_ShowsToAnswerDraftWithProgressText**: A reviewer with a draft ballot finds the
chip "Draft" and a detail that says how many checks are answered. This scenario is tested by
`InboxService_GetInboxAsync_DraftBallot_ShowsToAnswerDraftWithProgressText`.

**InboxService_GetInboxAsync_AlternateHolder_ShowsToAnswer**: The alternate of a slot also finds the review under To
answer. This scenario is tested by `InboxService_GetInboxAsync_AlternateHolder_ShowsToAnswer`.

**InboxService_GetInboxAsync_AuthorBlockedViewer_ShowsNoToAnswer**: A slot holder who is the author of the review in a
project that forbids the author to answer gets no To answer row. This scenario is tested by
`InboxService_GetInboxAsync_AuthorBlockedViewer_ShowsNoToAnswer`.

**InboxService_GetInboxAsync_NoChecklist_ShowsNoToAnswer**: A review without a checklist has no ballots, so it gives no
To answer row. This scenario is tested by `InboxService_GetInboxAsync_NoChecklist_ShowsNoToAnswer`.

**InboxService_GetInboxAsync_RevertedToDraft_ReturnsToToAnswer**: A handed-in ballot that is edited goes back to a
draft and the review returns to To answer. This scenario is tested by
`InboxService_GetInboxAsync_RevertedToDraft_ReturnsToToAnswer`.

**InboxService_GetInboxAsync_HiddenViewerWithDispute_ShowsNoDisputedRowOrCount**: Two other reviewers dispute a check
while the viewer has not handed in; the viewer gets the To answer row only, with no Disputed row and no dispute count in
any chip or detail. This scenario is tested by
`InboxService_GetInboxAsync_HiddenViewerWithDispute_ShowsNoDisputedRowOrCount`.

**InboxService_GetInboxAsync_SubmittedViewerWithDispute_ShowsDisputedWithCount**: A reviewer who handed in finds the
Disputed row with the count, and no Ready to sign row while the dispute stands. This scenario is tested by
`InboxService_GetInboxAsync_SubmittedViewerWithDispute_ShowsDisputedWithCount`.

**InboxService_GetInboxAsync_AuthorWithDispute_ShowsDisputed**: The creator of the review, who may see the ballots, finds
the Disputed row with the text for someone who does not answer. This scenario is tested by
`InboxService_GetInboxAsync_AuthorWithDispute_ShowsDisputed`.

**InboxService_GetInboxAsync_DisputeResolved_RowDisappears**: When the reviewers agree the Disputed row goes away. This
scenario is tested by `InboxService_GetInboxAsync_DisputeResolved_RowDisappears`.

**InboxService_GetInboxAsync_DisputedUnread_AfterOtherSubmission**: A Disputed row the user has seen is unread again when
another reviewer hands in a ballot after the last seen time. This scenario is tested by
`InboxService_GetInboxAsync_DisputedUnread_AfterOtherSubmission`.

**InboxService_GetInboxAsync_SubmittedOwnBallotNothingBlocking_ShowsReadyToSign**: A reviewer who handed in, with nothing
blocking, finds the review under Ready to sign with the chip "Not signed". This scenario is tested by
`InboxService_GetInboxAsync_SubmittedOwnBallotNothingBlocking_ShowsReadyToSign`.

**InboxService_GetInboxAsync_OpenThread_ShowsWaitingNotReadyToSign**: A reviewer who handed in and has an open thread
waits for others and does not find the review under Ready to sign. This scenario is tested by
`InboxService_GetInboxAsync_OpenThread_ShowsWaitingNotReadyToSign`.

**InboxService_GetInboxAsync_OwnSlotSignedOthersPending_ShowsWaitingWithNames**: A reviewer who signed finds the review
under Waiting for others with the chip and the names of the people who have not signed; the row is never unread and is
not counted. This scenario is tested by `InboxService_GetInboxAsync_OwnSlotSignedOthersPending_ShowsWaitingWithNames`.

**InboxService_GetInboxAsync_WaitingForOthersOverdueReview_ShowsNoDueDateOrOverdue**: A Waiting for others row of a
review whose due date has passed shows no due date and no overdue mark, because the row is no work. This scenario is
tested by `InboxService_GetInboxAsync_WaitingForOthersOverdueReview_ShowsNoDueDateOrOverdue`.

**InboxService_GetInboxAsync_ActionCount_IncludesToAnswerAndDisputedNotWaiting**: The action count includes the To answer
and the Disputed reviews and leaves out the Waiting for others and the Updates rows. This scenario is tested by
`InboxService_GetInboxAsync_ActionCount_IncludesToAnswerAndDisputedNotWaiting`.

**InboxService_GetInboxAsync_FailedChecks_TextSaysFailSoFarAndRejectionNeedsReasons**: Bob answers a check of
the review of the author as Fail; the failed-checks row of the author has the detail "Reviewers marked 1 check as Fail so
far. If that stands, the review will be rejected and its reasons must be written down." This scenario is tested by
`InboxService_GetInboxAsync_FailedChecks_TextSaysFailSoFarAndRejectionNeedsReasons`.

**InboxService_GetInboxAsync_RejectedByOther_ShowsRejectedUpdateNotAction**: Both slots reject and sign, ann first and
bob five minutes later. The inbox of ann has one row, in Updates, with `InboxService.RejectedChip`, no action is
counted, and the review has no failed-checks row of the author and no to-sign row of bob. This scenario is tested by
`InboxService_GetInboxAsync_RejectedByOther_ShowsRejectedUpdateNotAction`.

**InboxService_GetInboxAsync_FailInNotApplicableSection_IsIgnored**: A failed check in a section that does not apply is
ignored. This scenario is tested by `InboxService_GetInboxAsync_FailInNotApplicableSection_IsIgnored`.

**InboxService_GetInboxAsync_ApprovedSinceLastSeen_ShowsUpdate_NotCountedAsAction**: A review approved by someone else
after the user last looked is shown as an update with the chip "Approved", is not counted as work, and goes away once
the user has seen it. This scenario is tested by
`InboxService_GetInboxAsync_ApprovedSinceLastSeen_ShowsUpdate_NotCountedAsAction`.

**InboxService_GetInboxAsync_ReopenedByOther_ShowsUpdate**: A reopening by someone else is shown as an update with the
chip "Reopened". This scenario is tested by `InboxService_GetInboxAsync_ReopenedByOther_ShowsUpdate`.

**InboxService_GetInboxAsync_ReopenedByMe_ShowsNoUpdate**: A reopening by the user is no news to the user. This
scenario is tested by `InboxService_GetInboxAsync_ReopenedByMe_ShowsNoUpdate`.

**InboxService_GetInboxAsync_DueDateInPast_ShowsOverdueChip_ApprovedShowsNone**: A past due date of an open review gives
the overdue mark, and an approved review shows no due date. This scenario is tested by
`InboxService_GetInboxAsync_DueDateInPast_ShowsOverdueChip_ApprovedShowsNone`.

**InboxService_GetInboxAsync_ReviewInSeveralSections_AppearsInEach**: A review with several reasons is listed once in
each group. This scenario is tested by `InboxService_GetInboxAsync_ReviewInSeveralSections_AppearsInEach`.

**InboxService_GetInboxAsync_NoStateRow_AllCommentsByOthersAreUnread**: A user without a state row finds every comment of
others unread, and the baseline is zero. This scenario is tested by
`InboxService_GetInboxAsync_NoStateRow_AllCommentsByOthersAreUnread`.

**InboxService_GetInboxAsync_OwnComments_AreNeverUnread**: A user's own comments are never unread, whatever the case of
the user name. This scenario is tested by `InboxService_GetInboxAsync_OwnComments_AreNeverUnread`.

**InboxService_GetInboxAsync_CommentsAfterLastSeen_AreUnread**: Only the comments after the last seen one are unread,
and the row of the inbox follows. This scenario is tested by
`InboxService_GetInboxAsync_CommentsAfterLastSeen_AreUnread`.

**InboxService_GetInboxAsync_ToSign_OtherSignsAfterLastSeen_IsUnread**: A review to sign that the user has seen is
unread again when another reviewer signs it after the last seen time. This scenario is tested by
`InboxService_GetInboxAsync_ToSign_OtherSignsAfterLastSeen_IsUnread`.

**InboxService_GetInboxAsync_ToSign_CommentByOtherAfterLastSeen_IsUnread**: A review to sign that the user has seen is
unread again when someone else writes a comment after the last seen one. This scenario is tested by
`InboxService_GetInboxAsync_ToSign_CommentByOtherAfterLastSeen_IsUnread`.

**InboxService_Unread_DraftBallotOfOtherSlot_Ignored**: A review to sign that the user has seen stays read when another
reviewer answers a check in a draft ballot, because a draft is not part of the record. This scenario is tested by
`InboxService_Unread_DraftBallotOfOtherSlot_Ignored`.

**InboxService_Unread_SubmittedBallotOfOtherSlot_IsUnread**: A review to sign that the user has seen is unread again
when another reviewer hands in a ballot after the last seen time. This scenario is tested by
`InboxService_Unread_SubmittedBallotOfOtherSlot_IsUnread`.

**InboxService_CountAsync_EqualsDistinctActionReviews**: The count is the number of distinct reviews that need action,
not the number of rows and not the information rows. This scenario is tested by
`InboxService_CountAsync_EqualsDistinctActionReviews`.

**InboxService_GetInboxAsync_Sysadmin_SeesOnlyOwnSlotsAndAuthoredReviews**: A system administrator who is no member gets
rows only for what is their own: the review they authored, and no sign row for a slot without membership. This scenario
is tested by `InboxService_GetInboxAsync_Sysadmin_SeesOnlyOwnSlotsAndAuthoredReviews`.

**InboxService_MarkSeenAsync_NeverMovesBackwards**: Marking a review as seen never moves the state backwards, keeps the
later time, and stores nothing for a user who may not see the review. This scenario is tested by
`InboxService_MarkSeenAsync_NeverMovesBackwards`.

**InboxService_MarkAllReadAsync_UpsertsEveryListedReview**: Marking everything as read stores a state for every listed
review, including the ones that were never opened, with the highest comment; a new comment is unread again. This
scenario is tested by `InboxService_MarkAllReadAsync_UpsertsEveryListedReview`.

**InboxService_MarkAllReadAsync_CommentAfterTheStart_StaysUnread**: A comment written after the time the marking began is
not stored as seen, so the review stays unread. This scenario is tested by
`InboxService_MarkAllReadAsync_CommentAfterTheStart_StaysUnread`.

**InboxService_NullArguments_Throw**: Null arguments are refused where a value is required. This scenario is tested by
`InboxService_NullArguments_Throw`.

**InboxRules_SlotState_MatchesSigningServiceApproval**: The slot state is the one the signing service reports for a slot
that was never signed, a slot with a current signature, and a slot whose signature is out of date. This scenario is
tested by `InboxRules_SlotState_MatchesSigningServiceApproval`.

**InboxRules_SameUser_IgnoresCase**: User names are compared without regard to case, and nothing equals a missing name.
This scenario is tested by `InboxRules_SameUser_IgnoresCase`.

**InboxRules_IsAssigned_PrimaryAndAlternate_AreAssigned**: A user is assigned to a slot as primary and as alternate, and
not otherwise. This scenario is tested by `InboxRules_IsAssigned_PrimaryAndAlternate_AreAssigned`.

**InboxRules_IsUnread_OtherAndNewer_IsTheOnlyUnread**: A comment is unread only when it is by someone else and newer
than the last seen one. This scenario is tested by `InboxRules_IsUnread_OtherAndNewer_IsTheOnlyUnread`.

**InboxRules_CountUnreadAndMaxCommentId_AllThreads_AreCounted**: The unread count and the highest comment look at the
comments of all threads, and no comment gives zero. This scenario is tested by
`InboxRules_CountUnreadAndMaxCommentId_AllThreads_AreCounted`.

**InboxRules_MaxCommentId_WithCutoff_IgnoresLaterComments**: With a cutoff time, the highest comment ignores the comments
created after it. This scenario is tested by `InboxRules_MaxCommentId_WithCutoff_IgnoresLaterComments`.

**InboxRules_WaitingThreads_OpenLatestByOther_AreWaiting**: The threads that wait for the user are the open ones with
the latest comment by someone else in which the user wrote or that belong to a review the user authored. This scenario
is tested by `InboxRules_WaitingThreads_OpenLatestByOther_AreWaiting`.

**InboxRules_CompletingSignature_LatestOfCurrentDigest_IsFound**: The completing signature is the latest one of the
current digest, with the higher identity winning a tie, and there is none without one. This scenario is tested by
`InboxRules_CompletingSignature_LatestOfCurrentDigest_IsFound`.

**InboxRules_FailedAnswers_OnlyFailInApplicableSections_AreFound**: Only the checks answered Fail in sections that apply
are found, read from the consolidated standing with the slot count of the review. This scenario is tested by
`InboxRules_FailedAnswers_OnlyFailInApplicableSections_AreFound`.

**InboxRules_ReviewerStanding_Facts_DecideTheSection**: A table of thirteen sets of facts (a concluded review, the
author, no checklist, a blocked author, an unsubmitted ballot, a hidden viewer, a dispute, a block on the signature,
nothing to sign) gives the standing To answer, Disputed, Ready to sign, Waiting for others, or none. This scenario is
tested by `InboxRules_ReviewerStanding_Facts_DecideTheSection`.

**InboxRules_AuthorBlocked_ParityWithChecklistService**: The rule that forbids the author to answer gives the same
answer as `ChecklistService` for a project with and without the rule, for the creator, for another user and for a
missing creator. This scenario is tested by `InboxRules_AuthorBlocked_ParityWithChecklistService`.

**InboxRules_Texts_ToAnswerWaitingAndChip_AreWorded**: The detail of a To answer row, the names of a Waiting row and
the chip for one other, several others and none are worded as in the help. This scenario is tested by
`InboxRules_Texts_ToAnswerWaitingAndChip_AreWorded`.

**InboxRules_InboxSections_IsAction_ExcludesWaitingAndUpdates**: Only Waiting for others and Updates are not action. This
scenario is tested by `InboxRules_InboxSections_IsAction_ExcludesWaitingAndUpdates`.
