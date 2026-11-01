### ReviewStatusService

#### Verification Approach

`ReviewStatusService` is verified with tests defined in `ReviewNextActionTests.cs` and `ReviewStatusServiceTests.cs`. The
next-action rule is pure, so its tests call `ReviewNextAction.Decide` directly with in-memory facts. The service tests
build a review with several reviewer slots over in-memory stores and the real access rules (`BallotMaskingWorld.cs`),
and read the status as viewers with different roles: a reviewer who has handed in, a reviewer with a draft, the lead, an
Auditor, and an outsider. The change list is checked with audit entries that the tests write with a time before and after
a signature. The tests check what the viewer may read, never the stored data. The page that shows the status is verified
by the `ReviewPage` unit, and the two system scenarios that drive it in a browser are listed there and in the system
requirements.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All tests pass with zero failures.
- Exactly one next action is decided for every combination of facts, in the order of the rule: the ballot first, then
  the disagreements, the findings, the questions, the conclusion, and the signature.
- The status lists the reviewers in slot order with the ballot state and the signature state and never the content of a
  ballot, lists the disputed checks with whether a discussion can be started, and counts the open questions and findings.
- A review with one slot is not masked and asks the reviewer to answer; a review with no slot asks the lead to add the
  reviewers; a viewer who may not see the project is refused; a missing review gives nothing.
- A viewer who may not see the others yet gets no disputed check, no count of discussions about checks, and no answer
  in the changes since a signature.
- The changes since a signature list only the entries after the signature that can alter the report, a reply or a
  comment only when it is in the report, and nothing when the signature still matches.

#### Test Scenarios

**ReviewNextAction_NoSlot_AssignMe**: A review without a reviewer asks the lead to add the reviewers and another member
to add themselves. This scenario is tested by `ReviewNextAction_NoSlot_AssignMe`.

**ReviewNextAction_DraftBallot_Submit**: A reviewer with unanswered checks is asked to answer, one with a complete draft
to hand in, and to hand in and sign when that is offered. This scenario is tested by
`ReviewNextAction_DraftBallot_Submit`.

**ReviewNextAction_SubmittedWithDispute_Discuss**: A reviewer who has handed in and sees a check answered differently is
asked to discuss it. This scenario is tested by `ReviewNextAction_SubmittedWithDispute_Discuss`.

**ReviewNextAction_OpenFindings_Resolve**: Open findings come before open questions. This scenario is tested by
`ReviewNextAction_OpenFindings_Resolve`.

**ReviewNextAction_ReadyToSign_Sign**: A reviewer who can sign is asked to sign, or to look at the changes and sign
again. This scenario is tested by `ReviewNextAction_ReadyToSign_Sign`.

**ReviewNextAction_SignedWaiting_WaitForOthers**: A reviewer who has signed waits for the others. This scenario is
tested by `ReviewNextAction_SignedWaiting_WaitForOthers`.

**ReviewNextAction_Concluded_Done**: A concluded review is done for everybody. This scenario is tested by
`ReviewNextAction_Concluded_Done`.

**ReviewNextAction_OneActionPerViewer**: Every combination of the facts gives one action, and a sentence has no place
to go to. This scenario is tested by `ReviewNextAction_OneActionPerViewer`.

**ReviewStatus_Reviewers_ShowBallotAndSignatureState**: The reviewers are listed in slot order with the state of the
ballot and of the signature. This scenario is tested by `ReviewStatus_Reviewers_ShowBallotAndSignatureState`.

**ReviewStatus_DisputedChecks_ListedWithDiscussUntilThreadIsOpen**: A disputed check is listed with Discuss until a
discussion about it is open. This scenario is tested by
`ReviewStatus_DisputedChecks_ListedWithDiscussUntilThreadIsOpen`.

**ReviewStatus_MaskedViewer_NoDisputedChecks**: A reviewer with a draft, and a member without a slot, see no disputed
check and no discussion about a check in the counts. This scenario is tested by
`ReviewStatus_MaskedViewer_NoDisputedChecks`.

**ReviewStatus_Auditor_SeesDisputesButCannotDiscuss**: An Auditor sees the disputed checks and may not start a
discussion. This scenario is tested by `ReviewStatus_Auditor_SeesDisputesButCannotDiscuss`.

**ReviewStatus_Outsider_IsRefusedAndMissingReviewIsNull**: A viewer outside the project is refused and a missing review
gives nothing. This scenario is tested by `ReviewStatus_Outsider_IsRefusedAndMissingReviewIsNull`.

**ReviewStatus_NoSlots_LeadIsAskedToAddReviewers**: A review with no slot gives the lead the action to add reviewers.
This scenario is tested by `ReviewStatus_NoSlots_LeadIsAskedToAddReviewers`.

**ReviewStatus_SingleSlot_IsNotMaskedAndAsksTheReviewer**: A review with one slot is never masked. This scenario is
tested by `ReviewStatus_SingleSlot_IsNotMaskedAndAsksTheReviewer`.

**ReviewStatus_ChangesSinceSigning_ListsDigestAffectingEntriesOnly**: After a signature, the entries that can alter the
report are listed with plain reasons, and an answer, a reply that is not in the report, and an entry before the
signature are not. This scenario is tested by `ReviewStatus_ChangesSinceSigning_ListsDigestAffectingEntriesOnly`.

**ReviewStatus_ChangesSinceSigning_EmptyWhenSignatureStillMatches**: A signature that still matches has no list of
changes. This scenario is tested by `ReviewStatus_ChangesSinceSigning_EmptyWhenSignatureStillMatches`.

**ReviewStatus_ChangesSinceSigning_MaskedForDraftViewer**: A viewer who may not see the others reads the neutral
sentence for the entries that the audit masking hides. This scenario is tested by
`ReviewStatus_ChangesSinceSigning_MaskedForDraftViewer`.

**ReviewStatus_ChangeRules_FollowThreadKindFromEarlierEntries**: A resolution note counts only for a Finding, which the
rule learns from the entries that start a thread or change its kind, also those before the signature. This scenario is
tested by `ReviewStatus_ChangeRules_FollowThreadKindFromEarlierEntries`.

#### Requirements Coverage

- **ReviewCenter-ReviewStatusService-NextAction**: `ReviewNextAction_NoSlot_AssignMe`,
  `ReviewNextAction_DraftBallot_Submit`, `ReviewNextAction_SubmittedWithDispute_Discuss`,
  `ReviewNextAction_OpenFindings_Resolve`, `ReviewNextAction_ReadyToSign_Sign`,
  `ReviewNextAction_SignedWaiting_WaitForOthers`, `ReviewNextAction_Concluded_Done`,
  `ReviewNextAction_OneActionPerViewer`
- **ReviewCenter-ReviewStatusService-Status**: `ReviewStatus_Reviewers_ShowBallotAndSignatureState`,
  `ReviewStatus_DisputedChecks_ListedWithDiscussUntilThreadIsOpen`, `ReviewStatus_Auditor_SeesDisputesButCannotDiscuss`,
  `ReviewStatus_Outsider_IsRefusedAndMissingReviewIsNull`, `ReviewStatus_NoSlots_LeadIsAskedToAddReviewers`,
  `ReviewStatus_SingleSlot_IsNotMaskedAndAsksTheReviewer`
- **ReviewCenter-ReviewStatusService-MaskedViewer**: `ReviewStatus_MaskedViewer_NoDisputedChecks`,
  `ReviewStatus_ChangesSinceSigning_MaskedForDraftViewer`
- **ReviewCenter-ReviewStatusService-ChangesSinceSigning**:
  `ReviewStatus_ChangesSinceSigning_ListsDigestAffectingEntriesOnly`,
  `ReviewStatus_ChangesSinceSigning_EmptyWhenSignatureStillMatches`,
  `ReviewStatus_ChangeRules_FollowThreadKindFromEarlierEntries`
