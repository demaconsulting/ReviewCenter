### BallotVisibility

#### Verification Approach

`BallotVisibility` is verified with tests defined in `BallotVisibilityTests.cs`, `AuditMaskingTests.cs`, and
`BallotMaskingServiceTests.cs`. The rule and the audit masking are pure, so their tests call `CanSeeOthers` and
`ForViewer` directly with in-memory values. The service tests build a review with several reviewer slots over in-memory
stores and the real access rules (`BallotMaskingWorld.cs`), and read the ballot view, the checklist state, the approval,
the progress, the inbox, and the thread list as viewers with different roles: a reviewer who has handed in, a reviewer
with a draft, a member without a slot, the lead, an administrator, and an Auditor. The tests check what the viewer may
read, never the stored data.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All tests pass with zero failures.
- A viewer sees the answers of the other reviewers of a review with a single slot, of a concluded review, and when the
  viewer leads the review, administers the project or the system, or is an Auditor.
- A project member without a slot, and a slot owner whose ballot is not submitted, do not see them; a slot owner whose
  ballot is submitted does.
- The ballot view lists the differing checks with the answers in slot order only for a viewer who may see the others,
  always counts the handed-in ballots, and offers Assign me and Submit and sign only when the service would accept
  them.
- A viewer who may not see the others reads no disputed or failed count, no outcome, and no conclusion text in the
  approval, the progress, or the inbox, and a list read equals a single read.
- A viewer who is not known is treated as one who has not handed in, in a review with several slots only.
- The discussions about checks are left out of the list for a viewer who may not see the others, and the capabilities
  follow the rules for starting them.
- The audit masking hides only the detail of entries that name an answer, a change of applicability, or a discussion
  about a check (the start, and every later entry of a thread that is not known to be about a file), keeps the action,
  the actor, the time, and the hashes, and never hides the entries of the viewer.
- Null input is refused.

#### Test Scenarios

**BallotVisibility_CanSeeOthers_SingleSlot_AlwaysSeesAll**: With one slot every viewer sees everything. This scenario
is tested by `BallotVisibility_CanSeeOthers_SingleSlot_AlwaysSeesAll`.

**BallotVisibility_CanSeeOthers_PlainContributorWithoutSlot_Masked**: A member who holds no slot does not see the
others in a review with several slots. This scenario is tested by
`BallotVisibility_CanSeeOthers_PlainContributorWithoutSlot_Masked`.

**BallotVisibility_CanSeeOthers_SlotOwnerWithoutSubmittedBallot_Masked**: A slot owner with no ballot or a draft does
not see the others. This scenario is tested by `BallotVisibility_CanSeeOthers_SlotOwnerWithoutSubmittedBallot_Masked`.

**BallotVisibility_CanSeeOthers_SlotOwnerSubmitted_SeesOthers**: A slot owner whose ballot is submitted sees the
others. This scenario is tested by `BallotVisibility_CanSeeOthers_SlotOwnerSubmitted_SeesOthers`.

**BallotVisibility_CanSeeOthers_LeadAdminAuditor_SeeAll**: A viewer who sees everything does so without a ballot. This
scenario is tested by `BallotVisibility_CanSeeOthers_LeadAdminAuditor_SeeAll`.

**BallotVisibility_CanSeeOthers_Concluded_SeesAll**: Once the review is concluded everybody sees everything. This
scenario is tested by `BallotVisibility_CanSeeOthers_Concluded_SeesAll`.

**AuditMasking_ForViewer_OtherAnswer_HidesDetailKeepsActorAndHash**: The answer entry of another user shows the fixed
sentence, with the actor and the hashes as stored. This scenario is tested by
`AuditMasking_ForViewer_OtherAnswer_HidesDetailKeepsActorAndHash`.

**AuditMasking_ForViewer_OwnAnswer_StaysVisible**: The viewer sees the own answer entry. This scenario is tested by
`AuditMasking_ForViewer_OwnAnswer_StaysVisible`.

**AuditMasking_ForViewer_ApplicabilityAndCheckDiscussion_HiddenOthersKept**: The applicability change and the
discussion about a check are hidden, and an unrelated entry is kept. This scenario is tested by
`AuditMasking_ForViewer_ApplicabilityAndCheckDiscussion_HiddenOthersKept`.

**AuditMasking_ForViewer_LaterEntryOfCheckThread_IsHiddenButFileThreadStays**: Each later entry kind of a discussion
(reply, resolution, reopening, change of kind, change of the report flag) of a check thread shows the fixed sentence,
while the same entry of a file thread stays. This scenario is tested by
`AuditMasking_ForViewer_LaterEntryOfCheckThread_IsHiddenButFileThreadStays`.

**AuditMasking_ForViewer_ThreadOfUnknownStart_IsHidden**: A later entry whose thread start is not among the entries,
whose thread number belongs to another review, or whose detail names no thread is hidden. This scenario is tested by
`AuditMasking_ForViewer_ThreadOfUnknownStart_IsHidden`.

**AuditMasking_ForViewer_OwnEntryOfCheckThread_StaysVisible**: The viewer sees their own reply in a discussion about a
check. This scenario is tested by `AuditMasking_ForViewer_OwnEntryOfCheckThread_StaysVisible`.

**AuditMasking_ForViewer_CanSeeOthers_ReturnsEntriesUnchanged**: A viewer who may see the others gets the entries
unchanged. This scenario is tested by `AuditMasking_ForViewer_CanSeeOthers_ReturnsEntriesUnchanged`.

**AuditMasking_ForViewer_UnknownViewer_HidesEveryAnswer**: With no viewer identity every answer entry is hidden. This
scenario is tested by `AuditMasking_ForViewer_UnknownViewer_HidesEveryAnswer`.

**AuditMasking_ForViewer_NullEntries_Throws**: A null list is refused. This scenario is tested by
`AuditMasking_ForViewer_NullEntries_Throws`.

**ChecklistService_GetBallotView_SubmittedViewer_ListsDifferingChecksInSlotOrder**: A reviewer who has handed in sees
the differing checks with the answers in slot order. This scenario is tested by
`ChecklistService_GetBallotView_SubmittedViewer_ListsDifferingChecksInSlotOrder`.

**ChecklistService_GetBallotView_ViewerWithoutSubmittedBallot_HidesDifferences**: A reviewer with a draft sees no
difference but still the counts. This scenario is tested by
`ChecklistService_GetBallotView_ViewerWithoutSubmittedBallot_HidesDifferences`.

**ChecklistService_GetBallotView_LeadAdminAuditor_SeeDifferencesWithoutBallot**: The lead, an administrator, and an
Auditor see the differences without a ballot. This scenario is tested by
`ChecklistService_GetBallotView_LeadAdminAuditor_SeeDifferencesWithoutBallot`.

**ChecklistService_GetBallotView_CanAssignMe_OnlyForMemberWithoutSlot**: Assign me is offered to a member without a
slot and to nobody else. This scenario is tested by
`ChecklistService_GetBallotView_CanAssignMe_OnlyForMemberWithoutSlot`.

**ChecklistService_GetBallotView_CompleteDraft_OffersSubmitAndSignOnlyWhenNothingBlocks**: Submit and sign is offered
for a complete draft only while nothing but the hand-in blocks the signature. This scenario is tested by
`ChecklistService_GetBallotView_CompleteDraft_OffersSubmitAndSignOnlyWhenNothingBlocks`.

**ChecklistService_GetBallotView_UnknownReview_ReturnsNull**: An unknown review gives no view. This scenario is tested
by `ChecklistService_GetBallotView_UnknownReview_ReturnsNull`.

**ChecklistService_GetAsync_ViewerWithoutSlotAndBallot_ReturnsMaskedState**: A member without a slot gets an empty,
masked state in place of the standing. This scenario is tested by
`ChecklistService_GetAsync_ViewerWithoutSlotAndBallot_ReturnsMaskedState`.

**ChecklistService_GetForSystemAsync_NoViewer_ReturnsUnmaskedStanding**: In a review with several slots, the system
caller path (`GetForSystemAsync`) and a read without a viewer give the unmasked consolidated standing, while a read as a
member without a slot is masked. This pins that the unmasked read is a named choice for callers that act for the system
(the API and the evidence, masked in a later slice) and never reached by a page, which passes the viewer. This scenario
is tested by `ChecklistService_GetForSystemAsync_NoViewer_ReturnsUnmaskedStanding`.

**SigningService_GetApproval_MaskedViewer_ShowsNoDisputeCountOrOutcome**: The blockers of a masked viewer hold no
count of disputed or failed checks, no outcome, and no conclusion text, only the fixed sentence. This scenario is tested
by `SigningService_GetApproval_MaskedViewer_ShowsNoDisputeCountOrOutcome`.

**SigningService_GetApproval_NullViewerAndSingleSlot_FailsClosedOnlyForSeveralSlots**: An unknown viewer is masked in a
review with several slots and sees everything in a review with one slot. This scenario is tested by
`SigningService_GetApproval_NullViewerAndSingleSlot_FailsClosedOnlyForSeveralSlots`.

**SigningService_GetApprovals_MaskedViewer_MatchesSingleRead**: The list read gives the same masked texts as a single
read. This scenario is tested by `SigningService_GetApprovals_MaskedViewer_MatchesSingleRead`.

**SigningService_GetApprovals_ManyReviews_LooksRoleUpOncePerBatch**: A list read of three reviews of the lead makes
the same number of membership reads as a list read of one, the creator sees all of each review, and an API token of the
creator sees none of it. This scenario is tested by `SigningService_GetApprovals_ManyReviews_LooksRoleUpOncePerBatch`.

**ReviewProgressService_GetProgress_MaskedViewer_HidesDisputedAndFailedCounts**: The progress of a masked viewer shows
no disputed or failed count. This scenario is tested by
`ReviewProgressService_GetProgress_MaskedViewer_HidesDisputedAndFailedCounts`.

**InboxService_GetInbox_MaskedViewer_ShowsNoDisputeOrFailureText**: The inbox of a masked viewer shows no dispute or
failure text. This scenario is tested by `InboxService_GetInbox_MaskedViewer_ShowsNoDisputeOrFailureText`.

**InboxService_GetInbox_CreatorViaApiToken_IsMaskedLikeAnyViewer**: Two reviewers handed in the same Fail. The creator
signed in with a password gets the failed-checks row; an API token of the same user does not, because a token is never
the lead. This scenario is tested by `InboxService_GetInbox_CreatorViaApiToken_IsMaskedLikeAnyViewer`.

**InboxService_GetInbox_HiddenCheckThread_DoesNotMakeRowUnreadForMaskedViewer**: A reviewer with a draft and a reviewer
who handed in have both seen the review; the lead then starts a discussion about a check. The To sign row of the
reviewer with the draft stays read, because the hidden discussion is not counted as news, while the row of the reviewer
who handed in is unread. This scenario is tested by
`InboxService_GetInbox_HiddenCheckThread_DoesNotMakeRowUnreadForMaskedViewer`.

**ThreadService_List_MaskedViewer_LeavesOutCheckThreads**: A masked viewer gets no discussion about a check in the
list. This scenario is tested by `ThreadService_List_MaskedViewer_LeavesOutCheckThreads`.

**ThreadService_List_ReviewVanishesBetweenReads_LeavesOutCheckThreads**: The review vanishes between the thread read
and the visibility check. The list keeps the discussion about a file and leaves out the one about a check (fail
closed). This scenario is tested by `ThreadService_List_ReviewVanishesBetweenReads_LeavesOutCheckThreads`.

**ThreadService_GetCapabilities_CanStartCheckThread_MatchesCheckThreadRules**: The capability to start a discussion
about a check follows the rules of the lead, the administrator, the slot owner, and the single slot. This scenario is
tested by `ThreadService_GetCapabilities_CanStartCheckThread_MatchesCheckThreadRules`.
