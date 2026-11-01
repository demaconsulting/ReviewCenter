### ThreadService

#### Verification Approach

`ThreadService` is verified with unit tests defined in `ThreadServiceTests.cs`, `ThreadServiceReportTests.cs` (the
report rules, the acknowledgements, and the audit entries), `ThreadKindRulesTests.cs` (the kind and the role rules),
and `ReviewActorRolesTests.cs` (how a person becomes the lead or a slot owner, directly and through the thread
actions, with the real access rules), `CheckThreadTests.cs` (the threads about a check, through the service with the
real access rules for the roles), and `CheckThreadRulesTests.cs` (the pure rule of who may start a check thread). The
tests use the real service and
the real `FileText` with a fake in-memory store (`FakeReviewStore`), the project-access double
`AllowAllProjectAccess` (the access rules themselves are verified by the `ProjectAccess` unit), and a fixed clock, so
every rule can be
exercised without a database. Failures are checked through the rule carried by the `DomainRuleException`, and every
rejection test also checks that nothing was stored. The line-count tests are theories over texts with Unix and
Windows line endings, with and without a final line ending. The text tests check the limit itself and
one character more. The author is not checked by the service: it is an `Actor` built from the signed-in user, so
the tests check that both of its names are stored and that a missing actor is refused. The behavior of the real
database is verified by the Storage tests, not here.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A thread on the whole file and a thread on a line range are stored in the Open state with the creation time and
  the first comment.
- A range ending exactly at the last line is accepted, and a range beyond it, a range with one endpoint missing,
  a start below 1, an end before the start, a range on a binary or empty file, and a thread on an unknown file or
  review are rejected with `ThreadAnchorInvalid` (on a concluded review the lock is reported first).
- A head line range on a deleted file is rejected; a whole-file thread on a deleted file is accepted.
- A line is accepted on a side exactly when it exists on that side, counted in the lines of that side; the baseline
  side needs a repository review with a baseline commit and a modified or deleted file; a whole-file anchor on the
  baseline side is rejected; a thread started without a side is a head thread.
- The audit text of a thread on the baseline side says so, and the text of a head thread is unchanged.
- Blank or too long texts are rejected, and a padded text is stored trimmed.
- A comment records the user name and the display name of the actor, and a missing actor is refused with an
  `ArgumentNullException`.
- A reply is appended after the earlier comments without changing them or the state.
- Resolving and reopening change only the state and are idempotent.
- An unknown thread, or one of another review, is rejected with `ThreadNotFound`.
- Threads are listed in creation order with their comments, and the list is empty when there are none.
- The last activity of a thread is the time of its latest comment; the index keeps the threads of the requested
  state (or all), newest activity first, the higher identity first on equal times, and is empty for no threads.
- The next step names starting a discussion, the number of open threads, or that all threads are resolved.
- With a checklist, the next step names the unanswered checks first, then the open threads, and says when the
  checklist is complete and all threads are resolved.
- The service cannot be constructed without a project access, which every change by an actor uses to refuse a
  project the actor may not see (the rule itself is verified with the `ProjectAccess` unit).
- On a concluded review (every slot holds a current signature and no thread is open), every thread action (start,
  reply, resolve, reopen, promote, demote, and toggle) is refused with `ReviewLocked`, and nothing is stored; a review
  with an open thread is not concluded.
- A comment is chat unless it is included in the report; a thread whose starting comment is included is a Finding and
  every other thread is a Question. A Finding is resolved only with a note, which is stored as a Resolution comment in
  the same change, included for a Finding; a Question needs no note, and a blank note is no note.
- The starter, a slot owner, and the lead promote a thread; the starter and the lead demote it; the author of a
  reply and the lead toggle a reply; the starting comment and a Resolution comment cannot be toggled; the Auditor
  cannot do any of these. A change that is already in effect stores nothing and records no entry.
- The lead is the creator of the review while the creator may still contribute, a project administrator, or a system
  administrator who is not a member (the creator is matched without regard to case); a creator made read-only, a
  plain member, an outsider, the command line, and an actor authenticated with an API token (whatever the user behind
  the token is) are not a lead. A slot owner is the primary or the alternate reviewer of any slot, found by user
  name, also for an actor with an API token; the command line, an unknown user, and a review without slots have none.
- A change that does not change the digest (chat, the state of a thread, the resolution of a Question, the reopening
  of a Finding) needs no acknowledgement and leaves the signatures current; a change that does (an included start or
  reply, a promotion, a demotion, a toggle, the resolution of a Finding) is refused with
  `SignaturesWouldBecomeOutOfDate` unless the slots are acknowledged. A reply to a thread that does not exist is
  `ThreadNotFound`, and an invalid anchor is reported before the signature impact.
- A thread about a check is stored with the check, an empty file, no lines, and the head side; an anchor that names a
  file and a check, a check with lines or the baseline side, a missing checklist, and a check that is not in the
  checklist are refused and nothing is stored. It takes replies, resolution, and reopening like any thread.
- A check thread is started by an administrator, the lead, a reviewer whose ballot is submitted, and the owner of the
  only slot (with a draft); an auditor, a plain contributor, and a reviewer of one of several slots with a draft or no
  ballot are refused with `ActorNotAllowed`. A concluded review refuses it with `ReviewLocked`, a start that would
  outdate a signature needs the acknowledgement, and the audit entry names the check and never the text.
- The capabilities of a viewer equal the rules that the actions enforce (promote: starter, slot owner, lead; demote:
  starter, lead; toggle: author of the reply, lead), an auditor and a concluded review allow nothing, a token gets what
  its user gets except the lead's rights, and the threads are counted by kind and state with the open ones worded by
  kind.

#### Test Scenarios

**ThreadService_StartThreadAsync_WholeFile_StoresOpenThreadWithFirstComment**: A thread without lines is started;
it is stored Open with no anchor lines, the clock time, and one comment with the given actor and text. This
scenario is tested by `ThreadService_StartThreadAsync_WholeFile_StoresOpenThreadWithFirstComment`.

**ThreadService_StartThreadAsync_LineRange_StoresRange**: A single line and a range of several lines are started;
the start and end lines are stored as given. This scenario is tested by
`ThreadService_StartThreadAsync_LineRange_StoresRange`.

**ThreadService_StartThreadAsync_RangeLimits_AreInclusiveAndCountLinesBySeparator**: For texts with Unix and Windows
line endings, with and without a final line ending, a range ending at the last line is accepted and one line more is
rejected. This scenario is tested by `ThreadService_StartThreadAsync_RangeLimits_AreInclusiveAndCountLinesBySeparator`.

**ThreadService_StartThreadAsync_InvalidRange_ThrowsThreadAnchorInvalid**: A range with only one endpoint given, a
start of 0 or below, and an end before the start are rejected with `ThreadAnchorInvalid`. This scenario is tested by
`ThreadService_StartThreadAsync_InvalidRange_ThrowsThreadAnchorInvalid`.

**ThreadService_StartThreadAsync_RangeOnBinaryOrEmptyFile_ThrowsThreadAnchorInvalid**: A line range on a binary file
and on an empty file is rejected with `ThreadAnchorInvalid`. This scenario is tested by
`ThreadService_StartThreadAsync_RangeOnBinaryOrEmptyFile_ThrowsThreadAnchorInvalid`.

**ThreadService_StartThreadAsync_WholeFileOnBinaryFile_IsAllowed**: A thread on the whole of a binary file is
accepted. This scenario is tested by `ThreadService_StartThreadAsync_WholeFileOnBinaryFile_IsAllowed`.

**ThreadService_StartThreadAsync_UnknownFileOrReview_ThrowsThreadAnchorInvalid**: A thread on a path that is not in
the review, and on a review that does not exist, is rejected with `ThreadAnchorInvalid`. This scenario is tested by
`ThreadService_StartThreadAsync_UnknownFileOrReview_ThrowsThreadAnchorInvalid`.

**ThreadService_StartThreadAsync_LineOnDeletedFile_ThrowsThreadAnchorInvalid**: A head line range on a file that was
deleted in a review from a repository is rejected with `ThreadAnchorInvalid` and the message "A deleted file has no
head lines; comment on its baseline lines or on the whole file instead." This scenario is tested by
`ThreadService_StartThreadAsync_LineOnDeletedFile_ThrowsThreadAnchorInvalid`.

**ThreadService_StartThreadAsync_WholeFileOnDeletedAndLineOnModified_AreAllowed**: A whole-file thread on a deleted
file and a line thread on a modified file are accepted. This scenario is tested by
`ThreadService_StartThreadAsync_WholeFileOnDeletedAndLineOnModified_AreAllowed`.

**ThreadService_StartThreadAsync_SideMatrix_AcceptsOnlyLinesThatExistOnThatSide**: In a repository review with a
baseline, a theory over a modified file (four baseline lines, two head lines), a deleted file (two baseline lines),
and an added file (three head lines): a line is accepted on a side exactly when it exists on that side, so a head
line 3 of the modified file, a baseline line 5, any head line of the deleted file, a baseline line 3 of the deleted
file, a head line 4 of the added file, and any baseline line of the added file are rejected with
`ThreadAnchorInvalid` and nothing is stored, while the side of an accepted thread is the side that was requested.
This scenario is tested by `ThreadService_StartThreadAsync_SideMatrix_AcceptsOnlyLinesThatExistOnThatSide`.

**ThreadService_StartThreadAsync_BaselineWithoutBaselineCommit_ThrowsThreadAnchorInvalid**: A baseline line on a
file of a repository review that has no baseline commit, and on a file of an uploaded review, is rejected with
`ThreadAnchorInvalid` and nothing is stored. This scenario is tested by
`ThreadService_StartThreadAsync_BaselineWithoutBaselineCommit_ThrowsThreadAnchorInvalid`.

**ThreadService_StartThreadAsync_WholeFileSide_BaselineRefusedHeadStored**: A whole-file anchor that names the
baseline side is rejected with `ThreadAnchorInvalid`; the same anchor on the head side is stored as a head thread.
This scenario is tested by `ThreadService_StartThreadAsync_WholeFileSide_BaselineRefusedHeadStored`.

**ThreadService_StartThreadAsync_NoSideGiven_IsHeadThread**: An anchor that names no side is a head anchor, so the
thread is a head thread. This scenario is tested by `ThreadService_StartThreadAsync_NoSideGiven_IsHeadThread`.

**ThreadService_StartThreadAsync_BaselineRange_UsesBaselineLineCount**: A range over four lines is accepted on the
baseline side of the modified file, which has four baseline lines, and rejected on its head side, which has two. This
scenario is tested by `ThreadService_StartThreadAsync_BaselineRange_UsesBaselineLineCount`.

**ThreadService_StartThreadAsync_UndefinedSide_ThrowsArgumentOutOfRange**: A side value that no member has is refused
with an `ArgumentOutOfRangeException` naming the anchor, before the review is looked up (the same refusal on a review
that does not exist). This scenario is tested by `ThreadService_StartThreadAsync_UndefinedSide_ThrowsArgumentOutOfRange`.

**ThreadService_StartThreadAsync_Audit_NamesBaselineSideOnly**: The audit detail of a thread on one baseline line, on
a baseline range, and on a head line reads `line 3 (baseline)`, `lines 2-4 (baseline)`, and `line 1` with the thread
and comment identities and the text digest, within the length limit of the detail. This scenario is tested by
`ThreadService_StartThreadAsync_Audit_NamesBaselineSideOnly`.

**ThreadService_StartThreadAsync_Actor_RecordsUsernameAndDisplayName**: A thread is started as an actor; its first
comment holds the user name and the display name of the actor. A thread started without an actor is refused with an
`ArgumentNullException` and nothing more is stored. This scenario is tested by
`ThreadService_StartThreadAsync_Actor_RecordsUsernameAndDisplayName`.

**ThreadService_StartThreadAsync_BlankOrLongText_ThrowsCommentTextInvalid**: A missing text, a white-space text, and
a text over the limit are rejected with `CommentTextInvalid`, while a text of exactly the limit is accepted. This
scenario is tested by `ThreadService_StartThreadAsync_BlankOrLongText_ThrowsCommentTextInvalid`.

**ThreadService_StartThreadAsync_PaddedText_IsTrimmed**: A text with surrounding white space is stored without it,
keeping the inner line break. This scenario is tested by `ThreadService_StartThreadAsync_PaddedText_IsTrimmed`.

**ThreadService_ReplyAsync_ExistingThread_AppendsInOrderAndKeepsEarlierComments**: Two replies are added to a
thread, which is also resolved; the comments are in posting order, the earlier ones are unchanged, and the state is
unchanged. This scenario is tested by
`ThreadService_ReplyAsync_ExistingThread_AppendsInOrderAndKeepsEarlierComments`.

**ThreadService_ReplyAsync_InvalidText_ThrowsAndStoresNothing**: A reply with a blank text is rejected with
`CommentTextInvalid`, a reply without an actor is refused with an `ArgumentNullException`, and the thread keeps only
its first comment. This scenario is tested by
`ThreadService_ReplyAsync_InvalidText_ThrowsAndStoresNothing`.

**ThreadService_ReplyAsync_UnknownThreadOrOtherReview_ThrowsThreadNotFound**: A reply to a thread that does not
exist, and to a thread of another review, is rejected with `ThreadNotFound`. This scenario is tested by
`ThreadService_ReplyAsync_UnknownThreadOrOtherReview_ThrowsThreadNotFound`.

**ThreadService_ResolveAsync_ReopenAsync_ChangeStateKeepCommentsAndAreIdempotent**: A thread is resolved twice and
reopened twice; the state follows each request, and the comments stay as they were. This scenario is tested by
`ThreadService_ResolveAsync_ReopenAsync_ChangeStateKeepCommentsAndAreIdempotent`.

**ThreadService_ResolveAsync_UnknownThread_ThrowsThreadNotFound**: Resolving and reopening a thread that does not
exist is rejected with `ThreadNotFound`. This scenario is tested by
`ThreadService_ResolveAsync_UnknownThread_ThrowsThreadNotFound`.

**ThreadService_ListAsync_SeveralThreads_ReturnsAllInOrderWithComments**: Threads on two files with replies are
listed; all are returned in creation order with their comments in posting order. This scenario is tested by
`ThreadService_ListAsync_SeveralThreads_ReturnsAllInOrderWithComments`.

**ThreadService_ListAsync_NoThreadsOrUnknownReview_ReturnsEmpty**: A review without threads and a review that does
not exist both give an empty list. This scenario is tested by
`ThreadService_ListAsync_NoThreadsOrUnknownReview_ReturnsEmpty`.

**ThreadService_LastActivity_IsTimeOfLatestComment**: A thread with a later reply and a thread with one comment are
created as objects; the last activity is the latest comment time in each. This scenario is tested by
`ThreadService_LastActivity_IsTimeOfLatestComment`.

**ThreadService_BuildIndex_StateFilter_ReturnsMatchingNewestActivityFirst**: Open and resolved threads, one with a
late reply and two with equal times, are indexed for all, Open and Resolved; the result is filtered, ordered newest
activity first with the higher identity first on a tie, and empty input gives an empty list. This scenario is
tested by `ThreadService_BuildIndex_StateFilter_ReturnsMatchingNewestActivityFirst`.

**ThreadService_NextStep_NoThreadsOpenAndAllResolved_NamesNextAction**: No threads, one open thread, three open and
one resolved thread, and only resolved threads each give the matching sentence. This scenario is tested by
`ThreadService_NextStep_NoThreadsOpenAndAllResolved_NamesNextAction`.

**ThreadService_NextStep_WithChecklist_CombinesChecksAndThreads**: A theory over the number of unanswered checks,
open threads, and resolved threads: only checks, one check, checks with open threads, checks with one open thread,
a complete checklist with open threads, with only resolved threads, and with no threads each give the matching
sentence. This scenario is tested by `ThreadService_NextStep_WithChecklist_CombinesChecksAndThreads`.

**ThreadService_NextStep_WithFailedChecks_NamesThemBeforeThreads**: A theory over the number of unanswered checks,
checks answered Fail, and open threads: a complete checklist with one failed check, with three failed checks and two
open threads, and four unanswered checks with one failed check each give the matching sentence, with the failed
checks named before the open threads. This scenario is tested by
`ThreadService_NextStep_WithFailedChecks_NamesThemBeforeThreads`.

**ThreadService_NextStep_DisputedAndConclusion_AreNamed**: A theory over the number of unanswered checks, checks
answered Fail, disputed checks, open threads, and whether a conclusion is needed: one disputed check, two disputed
checks with an open thread, unanswered checks with a disputed check, and a failed check with a needed conclusion, with
and without open threads, each give the matching sentence. The disputed checks are named and the checklist is not called
complete while a check is disputed; a needed conclusion ends the sentence with "a conclusion stating the reasons is
needed". This scenario is tested by `ThreadService_NextStep_DisputedAndConclusion_AreNamed`.

**ThreadService_Mutations_ApprovedReview_ThrowReviewLocked**: A theory over the four changes of a thread (start, reply,
resolve, and reopen) on a concluded review, that is a review whose every slot holds a current signature and that has no
open thread; each is refused with `ReviewLocked`, the
threads and comments are as they were, and the review is still approved with the same digest. The fake store does not
judge the lock itself, so the test shows that the service does. This scenario is tested by
`ThreadService_Mutations_ApprovedReview_ThrowReviewLocked`.

**ThreadService_Threads_AuthorWhileRuleIsOn_AreNotAffected**: With the rule that keeps the author from answering the
checklist on, the author of the review starts a thread, replies, and resolves it; none is refused, because the rule
concerns the checklist only. This scenario is tested by `ThreadService_Threads_AuthorWhileRuleIsOn_AreNotAffected`.

**ThreadService_StartThreadAsync_IncludedAndSigned_NeedsAcknowledgement**: A thread that is included in the report is
started on a review with a current signature; without an acknowledgement it is refused naming the slot and the signer
and nothing is stored, and with the acknowledgement it is stored and the signature is out of date. This scenario is
tested by `ThreadService_StartThreadAsync_IncludedAndSigned_NeedsAcknowledgement`.

**ThreadService_StartThreadAsync_SignedAndInvalidAnchor_ReportsTheAnchor**: A thread on a file that is not in the
review is started on a signed review; the refusal is `ThreadAnchorInvalid`, not the signature impact. This scenario is
tested by `ThreadService_StartThreadAsync_SignedAndInvalidAnchor_ReportsTheAnchor`.

**ThreadService_ReplyAsync_IncludedAndSigned_NeedsAcknowledgement**: A reply that is included in the report, to a
resolved thread on a signed review, is refused without the acknowledgement and stored with it; a reply to a thread that
does not exist is `ThreadNotFound`. This scenario is tested by
`ThreadService_ReplyAsync_IncludedAndSigned_NeedsAcknowledgement`.

**ThreadService_ReopenAndResolveAsync_Signed_StateChangesNeedNoAcknowledgement**: Resolving a resolved thread on a
signed review changes nothing; reopening and resolving a chat thread need no acknowledgement and leave the signature
current, because the state of a thread is not part of the record. This scenario is tested by
`ThreadService_ReopenAndResolveAsync_Signed_StateChangesNeedNoAcknowledgement`.

**ThreadService_StartThreadAsync_ChatAfterSignature_NeedsNoAcknowledgementAndKeepsSignature**: A chat thread is started
on a review with a current signature without an acknowledgement; it is stored and the signature stays current. This
scenario is tested by `ThreadService_StartThreadAsync_ChatAfterSignature_NeedsNoAcknowledgementAndKeepsSignature`.

**ThreadService_ReplyAsync_ChatAfterSignature_KeepsSignature**: A chat reply on a signed review is stored without an
acknowledgement and the signature stays current. This scenario is tested by
`ThreadService_ReplyAsync_ChatAfterSignature_KeepsSignature`.

**ThreadService_StartThreadAsync_IncludedAfterSignature_NeedsAcknowledgement**: An included thread after a signature
is refused with `SignaturesWouldBecomeOutOfDate` without the acknowledgement, and stored with it. This scenario is
tested by `ThreadService_StartThreadAsync_IncludedAfterSignature_NeedsAcknowledgement`.

**ThreadService_ReplyAsync_IncludedAfterSignature_NeedsAcknowledgement**: An included reply after a signature is
refused without the acknowledgement and stored with it. This scenario is tested by
`ThreadService_ReplyAsync_IncludedAfterSignature_NeedsAcknowledgement`.

**ThreadService_PromoteAsync_AfterSignature_NeedsAcknowledgement**: Promoting a thread after a signature is refused
without the acknowledgement and changes the digest with it. This scenario is tested by
`ThreadService_PromoteAsync_AfterSignature_NeedsAcknowledgement`.

**ThreadService_DemoteAsync_AfterSignature_NeedsAcknowledgement**: Demoting a Finding after a signature is refused
without the acknowledgement and changes the digest with it. This scenario is tested by
`ThreadService_DemoteAsync_AfterSignature_NeedsAcknowledgement`.

**ThreadService_SetCommentIncludedAsync_AfterSignature_NeedsAcknowledgement**: Toggling the flag of a reply after a
signature is refused without the acknowledgement and changes the digest with it. This scenario is tested by
`ThreadService_SetCommentIncludedAsync_AfterSignature_NeedsAcknowledgement`.

**ThreadService_ResolveAsync_FindingAfterSignature_NeedsAcknowledgement**: Resolving a Finding with a note after a
signature is refused without the acknowledgement, because the included resolution note changes the record. This
scenario is tested by `ThreadService_ResolveAsync_FindingAfterSignature_NeedsAcknowledgement`.

**ThreadService_ResolveAsync_QuestionAfterSignature_NeedsNoAcknowledgement**: Resolving a Question, with or without a
note, needs no acknowledgement and leaves the signature current. This scenario is tested by
`ThreadService_ResolveAsync_QuestionAfterSignature_NeedsNoAcknowledgement`.

**ThreadService_ReopenAsync_FindingAfterSignature_NeedsNoAcknowledgement**: Reopening a resolved Finding after a
signature needs no acknowledgement, because the earlier resolution note stays in the record. This scenario is tested by
`ThreadService_ReopenAsync_FindingAfterSignature_NeedsNoAcknowledgement`.

**ThreadService_PromoteThenDemoteWithAcknowledgement_RestoresOriginalDigest**: A thread is promoted and demoted with
acknowledgements; the digest after the demotion equals the digest before the promotion. This scenario is tested by
`ThreadService_PromoteThenDemoteWithAcknowledgement_RestoresOriginalDigest`.

**ThreadService_StartThreadAsync_IncludeInReport_StoresFindingWithFlaggedStartingComment**: A thread started with the
flag is stored with its starting comment included, so it is a Finding. This scenario is tested by
`ThreadService_StartThreadAsync_IncludeInReport_StoresFindingWithFlaggedStartingComment`.

**ThreadService_StartThreadAsync_Default_StoresQuestion**: A thread started without the flag has an excluded starting
comment, so it is a Question. This scenario is tested by `ThreadService_StartThreadAsync_Default_StoresQuestion`.

**ThreadService_ReplyAsync_IncludeInReport_FlagsOnlyTheReply**: A reply with the flag is included while the other
comments keep their flags. This scenario is tested by `ThreadService_ReplyAsync_IncludeInReport_FlagsOnlyTheReply`.

**ThreadService_ListAsync_Comments_CarryFlagAndKind**: The listed comments carry their kind (a comment or a
resolution) and their flag. This scenario is tested by `ThreadService_ListAsync_Comments_CarryFlagAndKind`.

**ThreadService_ResolveAsync_FindingWithoutNote_ThrowsResolutionNoteRequiredAndStoresNothing**: Resolving a Finding
without a note is refused with `ResolutionNoteRequired` and nothing is stored. This scenario is tested by
`ThreadService_ResolveAsync_FindingWithoutNote_ThrowsResolutionNoteRequiredAndStoresNothing`.

**ThreadService_ResolveAsync_FindingWithNote_StoresIncludedResolutionComment**: Resolving a Finding with a note stores
the state and a Resolution comment that is included. This scenario is tested by
`ThreadService_ResolveAsync_FindingWithNote_StoresIncludedResolutionComment`.

**ThreadService_ResolveAsync_QuestionWithNote_StoresExcludedResolutionComment**: Resolving a Question with a note
stores a Resolution comment that is not included. This scenario is tested by
`ThreadService_ResolveAsync_QuestionWithNote_StoresExcludedResolutionComment`.

**ThreadService_ResolveAsync_QuestionWithoutNote_Resolves**: A Question is resolved without a note and no comment is
added. This scenario is tested by `ThreadService_ResolveAsync_QuestionWithoutNote_Resolves`.

**ThreadService_ResolveAsync_BlankNoteOnQuestion_IsNoNote**: A white-space note counts as no note. This scenario is
tested by `ThreadService_ResolveAsync_BlankNoteOnQuestion_IsNoNote`.

**ThreadService_ResolveAsync_NoteTooLong_ThrowsCommentTextInvalid**: A note over the text limit is refused with
`CommentTextInvalid`. This scenario is tested by `ThreadService_ResolveAsync_NoteTooLong_ThrowsCommentTextInvalid`.

**ThreadService_ResolveAsync_AlreadyResolved_IsNoOpWithoutNoteOrAudit**: Resolving a resolved thread, even with a
note, stores nothing and records no audit entry. This scenario is tested by
`ThreadService_ResolveAsync_AlreadyResolved_IsNoOpWithoutNoteOrAudit`.

**ThreadService_ResolveAsync_KindChangedBeforeStoreWrite_ThrowsRecordChangedAndStoresNothing**: A Question is promoted,
or a Finding is demoted, after the service read the thread and before the store writes the resolution; the resolve is
refused with `RecordChanged`, the thread stays open with its comments, and only the kind change is audited. This
scenario is tested by `ThreadService_ResolveAsync_KindChangedBeforeStoreWrite_ThrowsRecordChangedAndStoresNothing`.

**ThreadService_ReopenThenResolveFinding_AddsSecondResolutionComment**: A Finding that is resolved, reopened, and
resolved again has two Resolution comments. This scenario is tested by
`ThreadService_ReopenThenResolveFinding_AddsSecondResolutionComment`.

**ThreadService_ReopenAsync_Finding_KeepsEarlierResolutionComment**: Reopening a Finding keeps its earlier Resolution
comment. This scenario is tested by `ThreadService_ReopenAsync_Finding_KeepsEarlierResolutionComment`.

**ThreadService_PromoteAsync_StarterSlotOwnerAndLead_Allowed**: The starter, a slot owner, and the project lead may
promote a thread. This scenario is tested by `ThreadService_PromoteAsync_StarterSlotOwnerAndLead_Allowed`.

**ThreadService_PromoteAsync_OtherMember_ThrowsActorNotAllowed**: Any other member is refused with
`ActorNotAllowed`. This scenario is tested by `ThreadService_PromoteAsync_OtherMember_ThrowsActorNotAllowed`.

**ThreadService_PromoteAsync_AlsoIncludesExistingResolutionComments**: Promoting a resolved thread includes its
Resolution comments. This scenario is tested by `ThreadService_PromoteAsync_AlsoIncludesExistingResolutionComments`.

**ThreadService_PromoteAsync_ResolvedQuestionWithoutNote_ThrowsResolutionNoteRequired**: Promoting a Question that was
resolved without a note is refused with `ResolutionNoteRequired` and changes nothing; after the thread is reopened the
promotion works. This scenario is tested by
`ThreadService_PromoteAsync_ResolvedQuestionWithoutNote_ThrowsResolutionNoteRequired`.

**ThreadService_PromoteAsync_AlreadyFinding_IsNoOp**: Promoting a Finding stores nothing and records no entry. This
scenario is tested by `ThreadService_PromoteAsync_AlreadyFinding_IsNoOp`.

**ThreadService_DemoteAsync_StarterAndLead_Allowed**: The starter and the project lead may demote a thread. This
scenario is tested by `ThreadService_DemoteAsync_StarterAndLead_Allowed`.

**ThreadService_DemoteAsync_SlotOwnerWhoIsNotStarter_ThrowsActorNotAllowed**: A slot owner who did not start the
thread is refused with `ActorNotAllowed`. This scenario is tested by
`ThreadService_DemoteAsync_SlotOwnerWhoIsNotStarter_ThrowsActorNotAllowed`.

**ThreadService_DemoteAsync_ExcludesResolutionButKeepsReplyFlags**: Demoting excludes the starting comment and the
Resolution comments and keeps the flags of replies. This scenario is tested by
`ThreadService_DemoteAsync_ExcludesResolutionButKeepsReplyFlags`.

**ThreadService_DemoteAsync_AlreadyQuestion_IsNoOp**: Demoting a Question stores nothing and records no entry. This
scenario is tested by `ThreadService_DemoteAsync_AlreadyQuestion_IsNoOp`.

**ThreadService_SetCommentIncludedAsync_ReplyByAuthorOrLead_Toggles**: The author of a reply and the project lead
toggle its flag. This scenario is tested by `ThreadService_SetCommentIncludedAsync_ReplyByAuthorOrLead_Toggles`.

**ThreadService_SetCommentIncludedAsync_OtherMember_ThrowsActorNotAllowed**: Any other member is refused with
`ActorNotAllowed`. This scenario is tested by
`ThreadService_SetCommentIncludedAsync_OtherMember_ThrowsActorNotAllowed`.

**ThreadService_SetCommentIncludedAsync_StartingOrResolutionComment_ThrowsCommentNotTogglable**: The starting comment
and a Resolution comment are refused with `CommentNotTogglable`. This scenario is tested by
`ThreadService_SetCommentIncludedAsync_StartingOrResolutionComment_ThrowsCommentNotTogglable`.

**ThreadService_SetCommentIncludedAsync_UnknownComment_ThrowsThreadNotFound**: A comment that is not in the thread is
refused with `ThreadNotFound`. This scenario is tested by
`ThreadService_SetCommentIncludedAsync_UnknownComment_ThrowsThreadNotFound`.

**ThreadService_SetCommentIncludedAsync_SameValue_IsNoOp**: Setting the flag to its current value stores nothing and
records no entry. This scenario is tested by `ThreadService_SetCommentIncludedAsync_SameValue_IsNoOp`.

**ThreadService_ReportMutations_Auditor_ThrowActorNotAllowedOrProjectNotFoundAsBefore**: The Auditor cannot promote,
demote, toggle, or resolve with a note; the refusals are the same as for the other thread actions. This scenario is
tested by `ThreadService_ReportMutations_Auditor_ThrowActorNotAllowedOrProjectNotFoundAsBefore`.

**ThreadService_ReportMutations_OutsideUser_ThrowsProjectNotFound**: A user outside the project is refused with
`ProjectNotFound` for each of these actions. This scenario is tested by
`ThreadService_ReportMutations_OutsideUser_ThrowsProjectNotFound`.

**ThreadService_ReportActions_CommandLine_ThrowsProjectNotFound**: The command line has no user record, so a promote
is refused by the access check with `ProjectNotFound` and nothing changes. This scenario is tested by
`ThreadService_ReportActions_CommandLine_ThrowsProjectNotFound`, defined in `ReviewActorRolesTests.cs`, as are all
scenarios of the roles below.

**ThreadService_PromoteAsync_AlternateOfSlot_Allowed**: The alternate reviewer of a slot, who did not start the
thread, may promote it, signed in with a password and with an API token, because the slot belongs to the person. This
scenario is tested by `ThreadService_PromoteAsync_AlternateOfSlot_Allowed`.

**ThreadService_ReportActions_LeadWhoIsNotStarterOrReviewer_Allowed**: A project administrator who did not create the
review, a system administrator who is not a member of the project, and the creator (a plain project user) each
promote, demote, and toggle a reply of a thread that somebody else started. This scenario is tested by
`ThreadService_ReportActions_LeadWhoIsNotStarterOrReviewer_Allowed`.

**ThreadService_ReportActions_LeadViaApiToken_ThrowsActorNotAllowed**: The same three users authenticated with an API
token are refused with `ActorNotAllowed` for promote, demote, and toggle; nothing changes and no entry is recorded.
This scenario is tested by `ThreadService_ReportActions_LeadViaApiToken_ThrowsActorNotAllowed`.

**ThreadService_ReportActions_CreatorDowngradedToAuditor_ThrowsActorNotAllowed**: A creator who was made an auditor
after starting a thread is refused with `ActorNotAllowed`, so the creator no longer leads the review. This scenario is
tested by `ThreadService_ReportActions_CreatorDowngradedToAuditor_ThrowsActorNotAllowed`.

**ReviewActorRoles_IsLeadAsync_Creator_IsLeadWithoutRegardToCase**: The creator leads the review, also when the user
name is given in another case. This scenario is tested by
`ReviewActorRoles_IsLeadAsync_Creator_IsLeadWithoutRegardToCase`.

**ReviewActorRoles_IsLeadAsync_ProjectAdminWhoIsNotCreator_IsLead**: A project administrator who did not create the
review leads it. This scenario is tested by `ReviewActorRoles_IsLeadAsync_ProjectAdminWhoIsNotCreator_IsLead`.

**ReviewActorRoles_IsLeadAsync_SystemAdminWhoIsNotMember_IsLead**: A system administrator who is not a member of the
project leads its reviews. This scenario is tested by `ReviewActorRoles_IsLeadAsync_SystemAdminWhoIsNotMember_IsLead`.

**ReviewActorRoles_IsLeadAsync_CreatorDowngradedToAuditor_IsNotLead**: A creator who is now read-only does not lead the
review. This scenario is tested by `ReviewActorRoles_IsLeadAsync_CreatorDowngradedToAuditor_IsNotLead`.

**ReviewActorRoles_IsLeadAsync_PlainMemberAndOutsider_AreNotLead**: A plain member and a user who is not a member do
not lead the review. This scenario is tested by `ReviewActorRoles_IsLeadAsync_PlainMemberAndOutsider_AreNotLead`.

**ReviewActorRoles_IsLeadAsync_ApiTokenCommandLineOrNoActor_IsNotLead**: An actor with an API token (the creator, an
administrator, or a system administrator), the command line, and no actor are never a lead. This scenario is tested by
`ReviewActorRoles_IsLeadAsync_ApiTokenCommandLineOrNoActor_IsNotLead`.

**ReviewActorRoles_IsLeadAsync_ReviewWithoutCreator_OnlyAdministratorsLead**: A review without a recorded creator is
led by administrators only. This scenario is tested by
`ReviewActorRoles_IsLeadAsync_ReviewWithoutCreator_OnlyAdministratorsLead`.

**ReviewActorRoles_IsSlotOwnerAsync_PrimaryAndAlternate_AreSlotOwners**: The primary and the alternate reviewer of a
slot are slot owners. This scenario is tested by
`ReviewActorRoles_IsSlotOwnerAsync_PrimaryAndAlternate_AreSlotOwners`.

**ReviewActorRoles_IsSlotOwnerAsync_NoSlotCommandLineOrNoActor_IsNotSlotOwner**: A member without a slot, an unknown
user, the command line, no actor, and a review without slots have no slot owner. This scenario is tested by
`ReviewActorRoles_IsSlotOwnerAsync_NoSlotCommandLineOrNoActor_IsNotSlotOwner`.

**ThreadService_AllThreadActions_ConcludedReview_ThrowReviewLocked**: A theory over every thread action (start, reply,
resolve, reopen, promote, demote, and toggle) on a concluded review; each is refused with `ReviewLocked` and nothing
is stored. This scenario is tested by `ThreadService_AllThreadActions_ConcludedReview_ThrowReviewLocked`.

**ThreadService_StartThreadAsync_ConcludedReviewAndInvalidAnchor_ReportsReviewLocked**: A thread on a file that is not
in a concluded review, and on lines the file does not have, is refused with `ReviewLocked`, not `ThreadAnchorInvalid`,
because the lock is checked before the file is looked up; nothing is stored or recorded. This scenario is tested by
`ThreadService_StartThreadAsync_ConcludedReviewAndInvalidAnchor_ReportsReviewLocked`.

**ThreadService_Mutations_OpenThreadReview_NotLockedEvenWhenAllSigned**: A review whose slots are all signed but with
an open thread is not concluded, so the thread actions still work. This scenario is tested by
`ThreadService_Mutations_OpenThreadReview_NotLockedEvenWhenAllSigned`.

**ThreadService_PromoteAndDemoteAsync_Audit_NamesKindChange**: The entries `thread.kind-changed` read
`thread N: question -> finding` and the reverse. This scenario is tested by
`ThreadService_PromoteAndDemoteAsync_Audit_NamesKindChange`.

**ThreadService_SetCommentIncludedAsync_Audit_NamesCommentAndChange**: The entry `thread.comment-report-changed` names
the thread, the comment, and whether it was included or taken out. This scenario is tested by
`ThreadService_SetCommentIncludedAsync_Audit_NamesCommentAndChange`.

**ThreadService_ResolveAsync_WithNote_Audit_HasNoteHashButNotText**: The entry `thread.resolved` carries the hash of
the note and never its text. This scenario is tested by
`ThreadService_ResolveAsync_WithNote_Audit_HasNoteHashButNotText`.

**ThreadService_StartThreadAsync_IncludeInReport_Audit_MarksInReport**: The entries `thread.started` and
`thread.replied` say `in report` when the comment is included. This scenario is tested by
`ThreadService_StartThreadAsync_IncludeInReport_Audit_MarksInReport`.

**ThreadService_StartThreadAsync_CheckAnchor_StoresCheckThreadWithEmptyFileAndNoLines**: A thread about a check is
stored with the check, an empty file, no lines, and the head side. This scenario is tested by
`ThreadService_StartThreadAsync_CheckAnchor_StoresCheckThreadWithEmptyFileAndNoLines`.

**ThreadService_StartThreadAsync_CheckAnchorUnknownItem_ThrowsChecklistItemNotFound**: A check that the frozen
checklist does not have is refused and nothing is stored. This scenario is tested by
`ThreadService_StartThreadAsync_CheckAnchorUnknownItem_ThrowsChecklistItemNotFound`.

**ThreadService_StartThreadAsync_CheckAnchorWithoutChecklist_ThrowsChecklistNotFound**: A review without a checklist has
no check to discuss and is refused. This scenario is tested by
`ThreadService_StartThreadAsync_CheckAnchorWithoutChecklist_ThrowsChecklistNotFound`.

**ThreadService_StartThreadAsync_MixedFileAndCheckAnchor_ThrowsThreadAnchorInvalid**: An anchor that names both a file
and a check is refused. This scenario is tested by
`ThreadService_StartThreadAsync_MixedFileAndCheckAnchor_ThrowsThreadAnchorInvalid`.

**ThreadService_StartThreadAsync_CheckAnchorWithLinesOrBaselineSide_ThrowsThreadAnchorInvalid**: A check anchor with
lines or the baseline side, and one that names no check, are refused. This scenario is tested by
`ThreadService_StartThreadAsync_CheckAnchorWithLinesOrBaselineSide_ThrowsThreadAnchorInvalid`.

**ThreadService_ReplyAsync_ResolveAsync_ReopenAsync_OnCheckThread_Work**: A check thread takes a reply, is resolved with
a note when it is a Finding, and is reopened like any thread. This scenario is tested by
`ThreadService_ReplyAsync_ResolveAsync_ReopenAsync_OnCheckThread_Work`.

**ThreadService_StartThreadAsync_CheckAnchorByAuditorOrPlainContributor_ThrowsActorNotAllowed**: An auditor and a plain
contributor who holds no slot and does not lead the review are refused with `ActorNotAllowed`. This scenario is tested
by `ThreadService_StartThreadAsync_CheckAnchorByAuditorOrPlainContributor_ThrowsActorNotAllowed`.

**ThreadService_StartThreadAsync_CheckAnchor_SlotOwnerNeedsSubmittedBallotWhenSeveralSlots**: The lead, an
administrator, and the only reviewer with a draft may start a check thread; a reviewer of one of several slots needs
the own ballot submitted. This scenario is tested by
`ThreadService_StartThreadAsync_CheckAnchor_SlotOwnerNeedsSubmittedBallotWhenSeveralSlots`.

**ThreadService_StartThreadAsync_CheckAnchorWhenConcluded_ThrowsReviewLocked**: A concluded review refuses a new check
thread. This scenario is tested by `ThreadService_StartThreadAsync_CheckAnchorWhenConcluded_ThrowsReviewLocked`.

**ThreadService_StartThreadAsync_CheckAnchorWouldOutdateSignature_RequiresAcknowledgement**: A check thread whose
starting comment is included makes a current signature out of date and needs the acknowledgement; a chat check thread
does not. This scenario is tested by
`ThreadService_StartThreadAsync_CheckAnchorWouldOutdateSignature_RequiresAcknowledgement`.

**ThreadService_StartThreadAsync_CheckAnchor_AuditsThreadStartedOnCheck**: The entry `thread.started` names the thread,
the check, and the hash of the text but not the text. This scenario is tested by
`ThreadService_StartThreadAsync_CheckAnchor_AuditsThreadStartedOnCheck`.

**CheckThreadRules_CanStart_SubmittedSlotOwner_IsAllowed**: A slot owner whose ballot is submitted may start a check
thread whatever the number of slots. This scenario is tested by
`CheckThreadRules_CanStart_SubmittedSlotOwner_IsAllowed`.

**CheckThreadRules_CanStart_DraftOrNoBallotSlotOwner_IsRefusedWhenSeveralSlots**: A slot owner with a draft or no
ballot is refused when the review has several slots. This scenario is tested by
`CheckThreadRules_CanStart_DraftOrNoBallotSlotOwner_IsRefusedWhenSeveralSlots`.

**CheckThreadRules_CanStart_SoleSlotOwnerWithDraft_IsAllowed**: The only reviewer of a review may start a check thread
with a draft or no ballot. This scenario is tested by `CheckThreadRules_CanStart_SoleSlotOwnerWithDraft_IsAllowed`.

**CheckThreadRules_CanStart_LeadCreatorProjectAdminAndSystemAdmin_AreAllowed**: The lead and a system administrator
may start a check thread, and a person with no role may not. This scenario is tested by
`CheckThreadRules_CanStart_LeadCreatorProjectAdminAndSystemAdmin_AreAllowed`.

**ThreadService_NoOpReportMutations_RecordNothing**: A promote, a demote, a toggle, and a resolve that change nothing
record no audit entry. This scenario is tested by `ThreadService_NoOpReportMutations_RecordNothing`.

**ThreadKindRules_KindOf_StartingCommentIncluded_IsFinding**: A thread whose starting comment is included is a
Finding. This scenario is tested by `ThreadKindRules_KindOf_StartingCommentIncluded_IsFinding`.

**ThreadKindRules_KindOf_OnlyReplyIncluded_IsQuestion**: A thread whose only included comment is a reply is a
Question. This scenario is tested by `ThreadKindRules_KindOf_OnlyReplyIncluded_IsQuestion`.

**ThreadKindRules_CanPromote_Roles_StarterSlotOwnerLeadOnly**: Only the starter, a slot owner, and the lead may
promote. This scenario is tested by `ThreadKindRules_CanPromote_Roles_StarterSlotOwnerLeadOnly`.

**ThreadKindRules_CanDemote_Roles_StarterAndLeadOnly**: Only the starter and the lead may demote. This scenario is
tested by `ThreadKindRules_CanDemote_Roles_StarterAndLeadOnly`.

**ThreadKindRules_CanToggleReply_Roles_AuthorAndLeadOnly**: Only the author of a reply and the lead may toggle it.
This scenario is tested by `ThreadKindRules_CanToggleReply_Roles_AuthorAndLeadOnly`.

**ThreadKindRules_IsToggleable_StartingAndResolutionComments_AreNot**: The starting comment and Resolution comments
cannot be toggled. This scenario is tested by `ThreadKindRules_IsToggleable_StartingAndResolutionComments_AreNot`.

**ThreadService_Constructor_NullArguments_Throws**: A null store, a null project access, and a null clock are rejected
with `ArgumentNullException`, so the service cannot be built without the check that the actor may see the project.
This scenario is tested by `ThreadService_Constructor_NullArguments_Throws`.

**ThreadService_ThreadEvents_RecordIdsAnchorsAndDigestButNotText**: A thread is started, replied to, resolved, and
reopened; the entries `thread.started`, `thread.replied`, `thread.resolved`, and `thread.reopened` carry the thread
and the anchor, and the comment text appears only as an eight-character digest prefix. This scenario is tested by
`ThreadService_ThreadEvents_RecordIdsAnchorsAndDigestButNotText`. A resolve of a thread that does not exist records
nothing, which is tested by `ThreadService_ResolveAsync_MissingThread_RecordsNothing`. Both tests are defined in
`AuditEventTests.cs`. The new entries `thread.kind-changed` and `thread.comment-report-changed` and the note hash of
`thread.resolved` are verified by the Audit scenarios of this unit above.

**ThreadCapabilities_Promote_StarterSlotOwnerAndLead**: The starter, the owner of a slot, and the lead may promote a
Question; a plain contributor may not. This scenario is tested by `ThreadCapabilities_Promote_StarterSlotOwnerAndLead`.

**ThreadCapabilities_Demote_StarterAndLeadOnly**: Only the starter and the lead may demote a Finding, not a slot
owner. This scenario is tested by `ThreadCapabilities_Demote_StarterAndLeadOnly`.

**ThreadCapabilities_ResolvedQuestion_PromoteNeedsResolutionNote**: A resolved Question without a resolution note
cannot be promoted, and one with a note can. This scenario is tested by
`ThreadCapabilities_ResolvedQuestion_PromoteNeedsResolutionNote`.

**ThreadCapabilities_Auditor_NothingAllowed** and **ThreadCapabilities_Concluded_NothingAllowed**: An auditor and every
viewer of a concluded review are allowed nothing. These scenarios are tested by the tests of the same names.

**ThreadCapabilities_ApiToken_ActsForItsUserButIsNeverLead**: A token of a slot owner is offered the promote that the
slot owner is offered, as the service enforces it by user name; a token of the lead is offered neither promote nor
demote, because a token is never a lead. This scenario is tested by
`ThreadCapabilities_ApiToken_ActsForItsUserButIsNeverLead`.

**ThreadCapabilities_ToggleReply_AuthorOrLead**: The author of a reply and the lead may toggle it, other people may
not, and the starting comment and a resolution note cannot be toggled. This scenario is tested by
`ThreadCapabilities_ToggleReply_AuthorOrLead`.

**ThreadCapabilities_Resolve_AnyContributor_UnknownThreadNothing**: A contributor may resolve a thread, and a thread
that does not exist gets no action. This scenario is tested by
`ThreadCapabilities_Resolve_AnyContributor_UnknownThreadNothing`.

**ThreadCapabilities_Equality_ComparesPerThreadActionsByValue**: Two capabilities with equal per-thread actions are
equal. This scenario is tested by `ThreadCapabilities_Equality_ComparesPerThreadActionsByValue`.

**ThreadCounts_SplitByKindAndState**, **ThreadCounts_OpenPhrase_NamesTheKinds**,
**ThreadCounts_KindPhrases_NameZeroOneAndMany**, and
**ThreadCounts_From_NullThreads_Throws**: The threads are counted by kind and state, the open ones are worded
("2 open findings, 1 open question"), one kind is worded also for a zero count ("0 open questions"), and a null list
is refused. These scenarios are tested by the tests of the same
names, all defined in `ThreadCapabilitiesTests.cs`.
