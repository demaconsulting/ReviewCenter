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
- A viewer who may not see the others yet gets no disputed check, no answer, no failed check, no outcome note, no early
  signing caution, no count of discussions about checks, and no answer in the changes since a signature.
- A viewer who may see the answers gets, for each disputed check, the answer and the justification of every submitted
  ballot in slot order; the outcome note that names the failed check and says Rejected; a caution that names the
  reviewers who have not handed in when the viewer is about to sign (none for a single reviewer); a sentence instead
  of a
  button for the person who asked every open question, while the other reviewer is still asked to answer; and, for a
  reviewer who signed, the name of the reviewer the review waits for.
- The changes since a signature list only the entries after the signature that can alter the report, a reply or a
  comment only when it is in the report, and nothing when the signature still matches.
- The audit trail of a long review is read to its end, so the newest entries are listed however many entries precede
  them.
- A reviewer who reopened a review is offered the signing form only when the project lets a reopener sign again, with
  the words for a signature that lapsed or for a first signature as it applies.
- A status built from the approval, the threads and the capabilities that a caller passes in equals the status the
  service reads itself, uses the values passed in, and still refuses a viewer who may not see the project.

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

**ReviewNextAction_OwnQuestion_WaitsForTheOtherParty**: The person who asked every open question gets a sentence that
names whom they wait for (or none when there are many), the other party is still asked to answer, unknown facts keep the
older rule, and findings still come first. This scenario is tested by
`ReviewNextAction_OwnQuestion_WaitsForTheOtherParty`.

**ReviewNextAction_AfterSigning_NamesWhoIsMissing**: A reviewer who signed is told whom the review waits for, and a
reviewer whose signature no longer counts, and who cannot sign yet, is told so without a button. This scenario is tested
by `ReviewNextAction_AfterSigning_NamesWhoIsMissing`.

**ReviewStatus_Reviewers_ShowBallotAndSignatureState**: The reviewers are listed in slot order with the state of the
ballot and of the signature. This scenario is tested by `ReviewStatus_Reviewers_ShowBallotAndSignatureState`.

**ReviewStatus_DisputedChecks_ListedWithDiscussUntilThreadIsOpen**: A disputed check is listed with Discuss until a
discussion about it is open. This scenario is tested by
`ReviewStatus_DisputedChecks_ListedWithDiscussUntilThreadIsOpen`.

**ReviewStatus_MaskedViewer_NoDisputedChecks**: A reviewer with a draft, and a member without a slot, see no disputed
check and no discussion about a check in the counts. This scenario is tested by
`ReviewStatus_MaskedViewer_NoDisputedChecks`.

**ReviewStatus_MaskedViewer_NoAnswersNoFailedCheckNoRejectedWording**: For a reviewer with a draft and a member
without a
slot, whose view of a review with failed checks and an open discussion is serialized, nothing of the answers, the
justification, the check text, the check id, the outcome note, the caution or the word Rejected is present, and a
stranger is refused. This scenario is tested by `ReviewStatus_MaskedViewer_NoAnswersNoFailedCheckNoRejectedWording`.

**ReviewStatus_DisputedChecks_ListEveryAnswerWithJustification**: A viewer who handed in, and the lead, get the answer
of every submitted ballot to a disputed check in slot order, with the justification written. This scenario is tested by
`ReviewStatus_DisputedChecks_ListEveryAnswerWithJustification`.

**ReviewStatus_BothFailed_OutcomeNoteOnlyForViewerWhoMaySeeIt**: A review that both reviewers failed on a check gives a
viewer who handed in an outcome note with the check and the word Rejected, and a viewer without a ballot nothing. This
scenario is tested by `ReviewStatus_BothFailed_OutcomeNoteOnlyForViewerWhoMaySeeIt`.

**ReviewStatus_EarlySigning_CautionNamesReviewersWithoutBallot**: A reviewer who could sign while another reviewer has
not handed in is cautioned by name; the reviewer without a ballot and the reviewer after the ballot is in get no caution.
This scenario is tested by `ReviewStatus_EarlySigning_CautionNamesReviewersWithoutBallot`.

**ReviewStatus_SingleSlot_NoCaution**: A review with one reviewer gives no caution. This scenario is tested by
`ReviewStatus_SingleSlot_NoCaution`.

**ReviewStatus_OwnQuestion_AskerWaitsAndOtherPartyAnswers**: The person who started the discussion about a check waits
for the other reviewer without a button, and the other reviewer is asked to answer. This scenario is tested by
`ReviewStatus_OwnQuestion_AskerWaitsAndOtherPartyAnswers`.

**ReviewStatus_AfterSigning_NamesWhoSignsNext**: A reviewer who signed is told whom the review waits for, in the words
of the panel and in the next step of the approval. This scenario is tested by
`ReviewStatus_AfterSigning_NamesWhoSignsNext`.

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

**ReviewStatus_ChangesSinceSigning_MaskedForDraftViewer**: A viewer who may not see the others still reads the time, the
person and the general reason of each change, and reads the neutral sentence for the entries whose details the audit
masking hides. This scenario is tested by `ReviewStatus_ChangesSinceSigning_MaskedForDraftViewer`.

**ReviewStatus_ChangesSinceSigning_ReadsPastTwentyThousandEntries**: More than 20,000 entries that alter nothing are
followed by a change of the conclusion; the status still lists that newest change, because the trail is read to its end.
This scenario is tested by `ReviewStatus_ChangesSinceSigning_ReadsPastTwentyThousandEntries`.

**ReviewStatus_ChangeRules_FollowThreadKindFromEarlierEntries**: A resolution note counts only for a Finding, which the
rule learns from the entries that start a thread or change its kind, also those before the signature. This scenario is
tested by `ReviewStatus_ChangeRules_FollowThreadKindFromEarlierEntries`.

**ReviewStatus_ChangeRules_CarriedFinding_ResolvedWithNote_IsListed**: A Finding that was carried over from the followed
review has no starting entry in this review; with the carried thread passed in, a resolution note after the signature is
listed. This scenario is tested by `ReviewStatus_ChangeRules_CarriedFinding_ResolvedWithNote_IsListed`.

**ReviewStatus_ChangeRules_CarriedFindingDemotedLater_UsesTheKindBeforeTheChange**: A carried thread that is a Question
now because an entry of this review changed its kind from a Finding is seeded as the Finding it was: a resolution note
before that change is listed together with the change, and one after it is not. This scenario is tested by
`ReviewStatus_ChangeRules_CarriedFindingDemotedLater_UsesTheKindBeforeTheChange`.

**ReviewStatus_ChangeRules_CarriedQuestion_ResolvedWithNote_IsNotListed**: A carried Question resolved with a note is not
listed, because a resolution note counts only for a Finding. This scenario is tested by
`ReviewStatus_ChangeRules_CarriedQuestion_ResolvedWithNote_IsNotListed`.

**ReviewStatus_ChangesSinceSigning_CarriedFindingResolvedWithNote_IsListed**: In a follow-up review, a reviewer signs,
the lead reopens the review and resolves the carried Finding with a note; the status of the reviewer lists "Resolved a
finding with a note that goes in the report". This scenario is tested by
`ReviewStatus_ChangesSinceSigning_CarriedFindingResolvedWithNote_IsListed`.

**ReviewStatus_Reopener_SignAfterSignatureOutOfDate_DependsOnProjectRule**: A reviewer who reopened an approved review
and whose signature no longer matches is told to wait while the project forbids a reopener to sign again, and to look at
the changes and sign again when it does not; a reviewer who did not reopen is always asked to sign again. This scenario
is tested by `ReviewStatus_Reopener_SignAfterSignatureOutOfDate_DependsOnProjectRule`.

**ReviewStatus_Reopener_WithoutEarlierSignature_SignDependsOnProjectRule**: A reviewer who reopened a review and then
took a new slot, which has no signature, is told to wait while the project forbids a reopener to sign again, and to sign
the review when it does not. This scenario is tested by
`ReviewStatus_Reopener_WithoutEarlierSignature_SignDependsOnProjectRule`.

**ReviewStatus_PassedInValues_AreUsedInsteadOfReread**: For every kind of viewer, the status built from the approval,
the threads and the capabilities passed in equals the status the service reads itself, and capabilities that allow
nothing are used as given. This scenario is tested by `ReviewStatus_PassedInValues_AreUsedInsteadOfReread`.

**ReviewStatus_PassedInValues_OutsiderIsStillRefused**: The overload with values passed in refuses a viewer outside the
project and gives nothing for a missing review. This scenario is tested by
`ReviewStatus_PassedInValues_OutsiderIsStillRefused`.

#### Requirements Coverage

- **ReviewCenter-ReviewStatusService-NextAction**: `ReviewNextAction_NoSlot_AssignMe`,
  `ReviewNextAction_DraftBallot_Submit`, `ReviewNextAction_SubmittedWithDispute_Discuss`,
  `ReviewNextAction_OpenFindings_Resolve`, `ReviewNextAction_ReadyToSign_Sign`,
  `ReviewNextAction_SignedWaiting_WaitForOthers`, `ReviewNextAction_Concluded_Done`,
  `ReviewNextAction_OneActionPerViewer`, `ReviewNextAction_OwnQuestion_WaitsForTheOtherParty`,
  `ReviewNextAction_AfterSigning_NamesWhoIsMissing`
- **ReviewCenter-ReviewStatusService-Status**: `ReviewStatus_Reviewers_ShowBallotAndSignatureState`,
  `ReviewStatus_DisputedChecks_ListedWithDiscussUntilThreadIsOpen`, `ReviewStatus_Auditor_SeesDisputesButCannotDiscuss`,
  `ReviewStatus_Outsider_IsRefusedAndMissingReviewIsNull`, `ReviewStatus_NoSlots_LeadIsAskedToAddReviewers`,
  `ReviewStatus_SingleSlot_IsNotMaskedAndAsksTheReviewer`, `ReviewStatus_PassedInValues_AreUsedInsteadOfReread`,
  `ReviewStatus_PassedInValues_OutsiderIsStillRefused`,
  `ReviewStatus_Reopener_SignAfterSignatureOutOfDate_DependsOnProjectRule`,
  `ReviewStatus_Reopener_WithoutEarlierSignature_SignDependsOnProjectRule`
- **ReviewCenter-ReviewStatusService-MaskedViewer**: `ReviewStatus_MaskedViewer_NoDisputedChecks`,
  `ReviewStatus_MaskedViewer_NoAnswersNoFailedCheckNoRejectedWording`,
  `ReviewStatus_ChangesSinceSigning_MaskedForDraftViewer`
- **ReviewCenter-ReviewStatusService-Guidance**: `ReviewStatus_DisputedChecks_ListEveryAnswerWithJustification`,
  `ReviewStatus_BothFailed_OutcomeNoteOnlyForViewerWhoMaySeeIt`,
  `ReviewStatus_EarlySigning_CautionNamesReviewersWithoutBallot`, `ReviewStatus_SingleSlot_NoCaution`,
  `ReviewStatus_OwnQuestion_AskerWaitsAndOtherPartyAnswers`, `ReviewStatus_AfterSigning_NamesWhoSignsNext`
- **ReviewCenter-ReviewStatusService-ChangesSinceSigning**:
  `ReviewStatus_ChangesSinceSigning_ListsDigestAffectingEntriesOnly`,
  `ReviewStatus_ChangesSinceSigning_EmptyWhenSignatureStillMatches`,
  `ReviewStatus_ChangeRules_FollowThreadKindFromEarlierEntries`,
  `ReviewStatus_ChangesSinceSigning_ReadsPastTwentyThousandEntries`,
  `ReviewStatus_ChangeRules_CarriedFinding_ResolvedWithNote_IsListed`,
  `ReviewStatus_ChangeRules_CarriedFindingDemotedLater_UsesTheKindBeforeTheChange`,
  `ReviewStatus_ChangeRules_CarriedQuestion_ResolvedWithNote_IsNotListed`,
  `ReviewStatus_ChangesSinceSigning_CarriedFindingResolvedWithNote_IsListed`
