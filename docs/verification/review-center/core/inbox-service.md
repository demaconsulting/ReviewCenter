### InboxService

#### Verification Approach

`InboxService` is verified with unit tests defined in `InboxServiceTests.cs` and `InboxRulesTests.cs`. The service tests
use the real `InboxService`, `ProjectAccess`, `RecordDigest`, and `SigningService` with the in-memory stand-ins for the
stores (`FakeReviewStore`, `FakeUserStore`, and `FakeInboxStore`, which share the review data of the test), and a fixed
clock that the test can move. The helper `InboxTestWorld` builds a project with members, reviews, slots, threads,
answers, signatures, and reopenings, so each test states only the facts that matter. The signing service is used to give
real signatures, so a signature goes out of date the way it does in the product. The rules class is verified directly,
with plain values, so each rule can be checked without a store. The database side is verified by the tests of the
`InboxStore` unit, and the cooperation of the real services by the `Core` subsystem test.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All tests pass with zero failures.
- A primary or an alternate reviewer of a slot without a current signature finds the review under To sign, with the
  chip "Not signed" or "Out of date, sign again", and a signed slot gives no row.
- A thread in which the user wrote, or of the user's own review, whose latest comment is by someone else (the system
  counts) is a reply waiting; a thread whose latest comment is the user's own, and a resolved thread, are not.
- The author finds the failed checks of the review with the number, and a Fail in a section that does not apply is
  ignored.
- An approval by someone else, or a reopening by someone else, after the user last saw the review is an update; it is
  not counted as action, and an own signature or an own reopening is no update.
- An approved review has no action row, and a past due date marks an open review overdue and an approved one not.
- An auditor, a non-member, a removed member, a disabled user, a user with a temporary password, no actor, the command
  line, and an unknown name get no sign or reply rows or an empty inbox without an error; a system administrator who is
  no member gets only what is their own.
- The action count is the number of distinct reviews in the three action groups, and a review with several reasons is
  listed once in each group.
- A comment is unread only when it is by someone else (without regard to case) and newer than the last seen one; a user
  without a state row finds all comments of others unread.
- Marking never moves the state back, stores nothing for a user who may not see the review, and marking all stores a
  state for every listed review, including the ones that were never opened.
- Missing arguments are refused.

#### Test Scenarios

**InboxService_GetInboxAsync_PrimarySlotNotSigned_ShowsToSignItem**: The primary reviewer of a slot without a signature
finds the review under To sign with the chip "Not signed", and the row is unread. This scenario is tested by
`InboxService_GetInboxAsync_PrimarySlotNotSigned_ShowsToSignItem`.

**InboxService_GetInboxAsync_AlternateSlotNotSigned_ShowsToSignItem**: The alternate reviewer of the slot also finds the
review under To sign. This scenario is tested by `InboxService_GetInboxAsync_AlternateSlotNotSigned_ShowsToSignItem`.

**InboxService_GetInboxAsync_SignatureOutOfDate_ShowsSignAgainChip**: After a signature the record changes (a new
answer); the signature no longer covers the record and the reviewer finds the chip "Out of date, sign again". This
scenario is tested by `InboxService_GetInboxAsync_SignatureOutOfDate_ShowsSignAgainChip`.

**InboxService_GetInboxAsync_SlotSigned_HasNoToSignItem**: A slot with a current signature gives its reviewer no To
sign row. This scenario is tested by `InboxService_GetInboxAsync_SlotSigned_HasNoToSignItem`.

**InboxService_GetInboxAsync_ApprovedReview_HasNoActionItems**: When every slot is signed and the review is approved,
no reviewer has an action row; the other people get only an information row. This scenario is tested by
`InboxService_GetInboxAsync_ApprovedReview_HasNoActionItems`.

**InboxService_GetInboxAsync_Auditor_GetsNoSignOrReplyItems**: An auditor named in a slot and the author of a review
gets no To sign and no Replies waiting row. This scenario is tested by
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

**InboxService_GetInboxAsync_FailAnswersInApplicableSections_ShowsFailedChecksWithCount**: The author sees the failed
checks of the review with the number, and the failures are new to the author. This scenario is tested by
`InboxService_GetInboxAsync_FailAnswersInApplicableSections_ShowsFailedChecksWithCount`.

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

**InboxRules_WaitingThreads_OpenLatestByOther_AreWaiting**: The threads that wait for the user are the open ones with
the latest comment by someone else in which the user wrote or that belong to a review the user authored. This scenario
is tested by `InboxRules_WaitingThreads_OpenLatestByOther_AreWaiting`.

**InboxRules_CompletingSignature_LatestOfCurrentDigest_IsFound**: The completing signature is the latest one of the
current digest, with the higher identity winning a tie, and there is none without one. This scenario is tested by
`InboxRules_CompletingSignature_LatestOfCurrentDigest_IsFound`.

**InboxRules_FailedAnswers_OnlyFailInApplicableSections_AreFound**: Only the checks answered Fail in sections that apply
are found. This scenario is tested by `InboxRules_FailedAnswers_OnlyFailInApplicableSections_AreFound`.
