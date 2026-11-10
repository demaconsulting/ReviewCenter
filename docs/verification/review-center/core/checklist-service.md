### ChecklistService

#### Verification Approach

`ChecklistService`, the `DefaultChecklist` definition, and the progress rules of `ChecklistState` are verified with
unit tests defined in `ChecklistServiceTests.cs`, the ballot rules (slot, hand-in, own view, impact of a hand-in) in
`ChecklistServiceBallotTests.cs`, and the strict validation of a definition (`ChecklistValidator`) with
the unit tests in `ChecklistValidatorTests.cs`, which need no store. The service is exercised over an in-memory fake
store and the project-access double `AllowAllProjectAccess` (the access rules themselves are verified by the `ProjectAccess`
unit), so each
test creates its own review, seats the reviewers in slots and reads the stored result back. The tests of the refusal of
a person outside the project, of an auditor, and of the alternate reviewer of a slot use the real project access rules
instead, and the tests of the signature impact and of the conclusion text use the signed-review world of the tests.
The freeze at review creation is verified by the review service, which is a different unit. The real database is
covered by the `Storage` subsystem.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The default checklist has the 14 sections and 58 items of the review template, in order, with unique ids that
  start with the id of their section, it survives a JSON round trip, and it passes the strict validation; a definition
  that is read from JSON but is incomplete is rejected with a `JsonException`.
- The strict validation accepts a definition only when it is valid JSON of at most 256 KiB, with a version of 1 to 64
  characters, 1 to 50 sections, 1 to 500 checks, no empty section, identifiers of 1 to 32 allowed characters that are
  unique across the definition, titles of at most 200, wording of at most 1000, and a hint of at most 500 characters;
  it reports every violation with its path and never throws for bad content.
- A review's checklist is the copy stored with the review; a later change to the default does not change it, a review
  without a copy has no checklist, and an unknown review has no checklist.
- A section marked as not applicable needs a reason of at most 2000 characters, which is stored trimmed; marking it
  applicable again clears the reason; an unknown section is refused.
- A Pass is stored without a justification; a Fail or N/A needs one (an N/A with a justification is stored); a
  justification is trimmed and limited to 2000 characters; an unknown review or item is refused; an outcome that is not
  defined is refused and nothing is stored; answering again replaces the earlier answer of the slot.
- The answer and the change of a section each carry the user name and the display name of the actor, with the UTC
  time; a missing actor is refused for an answer and for a section change.
- An answer or a section change by a person outside the project is refused with `ProjectNotFound`, before any other
  problem of the call is reported, and nothing is stored; the service refuses to be created without its collaborators.
- The audit entries of an answer and of a section change name the actor, and a refused change records no entry.
- Progress is counted per section and overall; a failed check counts as answered; a not-applicable section counts as
  done and leaves the totals; the
  checklist is complete when every applicable check is answered; the next unanswered check and the first incomplete
  section are found; the draft answers of the only slot of a review count in the consolidated progress, and those of
  one of several slots do not.
- Answering, changing a section and handing in a ballot need a reviewer slot of the review: anybody else is refused
  with `NoReviewerSlot` and nothing is stored (also for the change of a section).
- With several slots a ballot stays a draft until `SubmitBallotAsync` hands it in; an incomplete ballot is refused with
  `BallotIncomplete`; handing in again changes nothing. With one slot the answer that completes the ballot hands it in,
  and an earlier answer leaves it a draft; a section change that completes the ballot hands it in as well.
- A viewer who holds a slot gets the own ballot with its draft and nothing of another slot's draft; a viewer without a
  slot gets the consolidated standing of the submitted ballots, marked as without a slot and not masked, in a review
  with a single slot or a concluded review (a review with several slots that is not concluded gives such a viewer a
  masked state, see the
  verification of *BallotVisibility*); the ballot view names, for every viewer of the project, the other reviewers who
  have not handed in, and gives the justifications of the others, the differing checks and the failed count only to a
  viewer who may see the others and may view the project (never to a viewer who has not handed in, an API token, even
  one whose owner has handed in, or a stranger); the API reads the consolidated standing with `GetConsolidatedAsync`,
  which requires a viewer and masks for an API token unless the owner of the token has handed in.
- On an approved review, answering a check and changing the applicability of a section are refused with
  `ReviewLocked`, and nothing is stored.
- While the project does not let authors answer the checklist (the default, also for projects that existed before the
  rule was enforced), the author of the review is refused when answering a check or changing a section with
  `AuthorCannotAnswer`, also on an approved review (the rule is reported before the lock), and nothing is stored;
  another member is not affected, threads are not affected, the author may answer once the rule is off, and
  `GetAsync` reports `AuthorBlocked` for the author only while the rule is on.
- A hand-in that would change the record digest and make a current signature of another slot out of date is refused
  with `SignaturesWouldBecomeOutOfDate` naming each affected slot number and signer, unless the slot is acknowledged,
  and nothing is stored; the signature of the own slot needs no acknowledgment; a draft edit, a hand-in that agrees
  with the others, another slot giving the same value, a review without a current signature, and a locked review
  (`ReviewLocked` first) behave as described.
- Answers and not-applicable sections that a follow-up review carried count like the others (a review whose checks are
  all carried is complete); answering a carried check again, or setting a carried section again, stores a row without
  the carried mark and with the new person.
- The review conclusion text (the free text, not the approved or rejected conclusion) can be written by the reviewer
  of any slot, as the primary or as the alternate reviewer, and the lead only, never by an auditor, an API token,
  another member or a person outside the project; it
  is normalized (LF, trimmed, blank means none) and limited to 4000 characters without control characters; a concluded
  review refuses it with `ReviewLocked`; a change while a signature is current needs the acknowledgment; the same text
  again changes nothing; a change by someone else in between is refused with `RecordChanged`; and every change is
  audited as `review.conclusion-changed` with the two lengths and never the text.

#### Test Scenarios

**ChecklistService_DefaultChecklist_HasTemplateSectionsAndItemIdsInOrder**: The default checklist is read; it has
14 sections and 58 items, with section and item ids in the order of the template. This scenario is tested by
`ChecklistService_DefaultChecklist_HasTemplateSectionsAndItemIdsInOrder`.

**ChecklistService_DefaultChecklist_HasAVersionLabel**: The default checklist is read; its version label is not blank,
is at most 64 characters long, and is the label that `DefaultChecklist` publishes. This scenario is tested by
`ChecklistService_DefaultChecklist_HasAVersionLabel`.

**ChecklistService_DefaultChecklist_IdsUniqueAndPrefixedBySection**: Every item id is unique and starts with its
section id, a hyphen, and two digits, and every item has wording. This scenario is tested by
`ChecklistService_DefaultChecklist_IdsUniqueAndPrefixedBySection`.

**ChecklistService_DefaultChecklist_RoundTripsThroughJson**: The default checklist is written to JSON and read back
unchanged. This scenario is tested by `ChecklistService_DefaultChecklist_RoundTripsThroughJson`.

**ChecklistService_FromJson_IncompleteDefinition_ThrowsJsonException**: A theory over JSON that parses but is not a
complete definition (`null`, `{}`, missing or null sections, a null section, a section without id, title or items, a
null item, an item without id or text, no version): `ChecklistDefinition.FromJson` and `ReviewChecklist.Parse` both
raise a `JsonException`. This scenario is tested by `ChecklistService_FromJson_IncompleteDefinition_ThrowsJsonException`.

**ChecklistService_Get_AfterDefaultChanges_ReturnsFrozenSnapshot**: A review holds a snapshot that differs from the
current default; the service returns the snapshot. This scenario is tested by
`ChecklistService_Get_AfterDefaultChanges_ReturnsFrozenSnapshot`.

**ChecklistService_Get_ReviewWithoutChecklistCopy_ReturnsNull**: A review that exists but holds no checklist copy is
read; no checklist is returned. This scenario is tested by `ChecklistService_Get_ReviewWithoutChecklistCopy_ReturnsNull`.

**ChecklistService_Get_UnknownReview_ReturnsNull**: The checklist of an unknown review is requested; nothing is
returned. This scenario is tested by `ChecklistService_Get_UnknownReview_ReturnsNull`.

**ChecklistService_SetSectionApplicability_NotApplicableWithoutReason_Throws**: A section is marked as not
applicable with a null, empty or blank reason; the call is refused. This scenario is tested by
`ChecklistService_SetSectionApplicability_NotApplicableWithoutReason_Throws`.

**ChecklistService_SetSectionApplicability_NotApplicableWithReason_StoresTrimmedReason**: A section is marked as not
applicable with a padded reason; the trimmed reason is stored. This scenario is tested by
`ChecklistService_SetSectionApplicability_NotApplicableWithReason_StoresTrimmedReason`.

**ChecklistService_SetSectionApplicability_Applicable_ClearsReason**: A not-applicable section is marked applicable
again; the reason is cleared. This scenario is tested by
`ChecklistService_SetSectionApplicability_Applicable_ClearsReason`.

**ChecklistService_SetSectionApplicability_UnknownSection_ThrowsChecklistItemNotFound**: An unknown section is
marked; the call is refused with the not-found rule. This scenario is tested by
`ChecklistService_SetSectionApplicability_UnknownSection_ThrowsChecklistItemNotFound`.

**ChecklistService_SetSectionApplicability_ReasonTooLong_Throws**: A section is marked as not applicable with a reason
of 2001 characters; the call is refused with the justification rule and the earlier reason is kept, while a reason of
exactly 2000 characters is accepted. This scenario is tested by
`ChecklistService_SetSectionApplicability_ReasonTooLong_Throws`.

**ChecklistService_SetSectionApplicability_StoresActorAndUtcTime**: A section is marked as not applicable by an actor;
the stored state holds the user name, the display name, and the UTC time. This scenario is tested by
`ChecklistService_SetSectionApplicability_StoresActorAndUtcTime`.

**ChecklistService_SetAnswer_PassWithoutJustification_Stores**: A theory over an absent and a blank justification: a
Pass is stored with no justification in both cases. This scenario is tested by
`ChecklistService_SetAnswer_PassWithoutJustification_Stores`.

**ChecklistService_SetAnswer_NotApplicableWithJustification_Stores**: An answer of Not applicable with a justification
is stored as given. This scenario is tested by `ChecklistService_SetAnswer_NotApplicableWithJustification_Stores`.

**ChecklistService_SetAnswer_FailOrNotApplicableWithoutJustification_Throws**: A Fail or N/A is stored without a
justification; the call is refused. This scenario is tested by
`ChecklistService_SetAnswer_FailOrNotApplicableWithoutJustification_Throws`.

**ChecklistService_SetAnswer_JustificationTooLong_Throws**: A justification over 2000 characters is refused, while
one of exactly 2000 is accepted. This scenario is tested by
`ChecklistService_SetAnswer_JustificationTooLong_Throws`.

**ChecklistService_SetAnswer_UnknownItem_ThrowsChecklistItemNotFound**: An answer for an unknown item is refused.
This scenario is tested by `ChecklistService_SetAnswer_UnknownItem_ThrowsChecklistItemNotFound`.

**ChecklistService_SetAnswer_UnknownReview_ThrowsChecklistNotFound**: An answer for an unknown review is refused.
This scenario is tested by `ChecklistService_SetAnswer_UnknownReview_ThrowsChecklistNotFound`.

**ChecklistService_SetAnswer_UndefinedOutcome_Throws**: An answer with an outcome value that is not defined is
refused with an `ArgumentOutOfRangeException`, and nothing is stored. This scenario is tested by
`ChecklistService_SetAnswer_UndefinedOutcome_Throws`.

**ChecklistService_SetAnswer_ReanswerWithPaddedJustification_OverwritesAndTrims**: A check is answered twice, the
second time with a padded justification; one answer remains, with the trimmed text. This scenario is tested by
`ChecklistService_SetAnswer_ReanswerWithPaddedJustification_OverwritesAndTrims`.

**ChecklistService_SetAnswer_NullActor_Throws**: An answer and a section change are made without an actor; both are
refused with an `ArgumentNullException` and nothing is stored. This scenario is tested by
`ChecklistService_SetAnswer_NullActor_Throws`.

**ChecklistService_SetAnswer_StoresActorAndUtcTime**: An answer is stored as an actor; it holds the display name and
the user name of the actor and the UTC time of the clock. This scenario is tested by
`ChecklistService_SetAnswer_StoresActorAndUtcTime`.

**ChecklistService_Mutations_ApprovedReview_ThrowReviewLocked**: A theory over the changes of a checklist (an answer,
the applicability of a section in both directions, and the hand-in of a ballot) on a review whose every slot holds a
current signature; each is refused with `ReviewLocked` and the answers, sections and ballots are as they were. The fake
store does not judge the lock itself, so the test shows that the service does. This scenario is tested by
`ChecklistService_Mutations_ApprovedReview_ThrowReviewLocked`.

**ChecklistService_Changes_NoSignatureOrNoOp_DoNotThrow**: In a review with three slots and no signature, a reviewer
changes an answer and a section, answers back to the earlier value, and hands the ballot in again; none is refused.
After slot 1 signs, the reviewer changes the draft of the own ballot again; this is not refused either and the
signature stays current, because a draft does not count and the other ballots still agree, so the digest of the record
does not change. This scenario is tested by `ChecklistService_Changes_NoSignatureOrNoOp_DoNotThrow`.

**ChecklistService_SetAnswerAsync_SameValueByOtherSlot_KeepsTheSignature**: With slot 1 signed, another reviewer saves
the same value that the others gave, which takes the ballot of that reviewer back to a draft; nothing is refused, the
others still agree, the answerer is not part of the digest, and the signature of slot 1 stays current. This scenario
is tested by `ChecklistService_SetAnswerAsync_SameValueByOtherSlot_KeepsTheSignature`.

**ChecklistService_SetAnswerAsync_PrimaryAndAlternateSigners_NamesSlotNumbersAndSigners**: Slots 1 (signed by its
alternate) and 3 are signed and slot 2 is not; a reviewer who disagrees changes an answer and hands the ballot in
without an acknowledgment; the refusal at the hand-in names slot 1 and slot 3 with the people who signed, and their
identities. This
scenario is tested by `ChecklistService_SetAnswerAsync_PrimaryAndAlternateSigners_NamesSlotNumbersAndSigners`.

**ChecklistService_SetAnswerAsync_PartialAcknowledgment_ThrowsWithRemainingSlots**: Slots 1 and 2 are signed and a
reviewer disagrees in the draft; handing in with only some of the signed slots acknowledged, and with an unrelated
slot acknowledged, is refused naming the others, and the ballot is not handed in. This scenario is tested by
`ChecklistService_SetAnswerAsync_PartialAcknowledgment_ThrowsWithRemainingSlots`.

**ChecklistService_SetAnswerAsync_FullAcknowledgment_StoresAndSignatureOutOfDate**: Every signed slot is acknowledged
at the hand-in; the ballot is handed in and each signature is out of date. This scenario is tested by
`ChecklistService_SetAnswerAsync_FullAcknowledgment_StoresAndSignatureOutOfDate`.

**ChecklistService_SetSectionApplicabilityAsync_Signed_NeedsAcknowledgment**: A section is switched to not applicable
in the draft of a ballot on a signed review, which needs no acknowledgment; handing the ballot in is refused until the
slot is acknowledged, and then it is handed in. Marking a section applicable that has no stored state changes nothing.
This scenario is tested by `ChecklistService_SetSectionApplicabilityAsync_Signed_NeedsAcknowledgment`.

**ChecklistService_SetAnswerAsync_ActorWithoutSlot_ThrowsNoReviewerSlot**: In a review whose one slot is held by
another user, a member who holds no slot answers a check; the call is refused with `NoReviewerSlot` and the review has
no ballot. This scenario is tested by `ChecklistService_SetAnswerAsync_ActorWithoutSlot_ThrowsNoReviewerSlot`, which is
defined in `ChecklistServiceBallotTests.cs`, like the scenarios below up to the hand-in impact.

**ChecklistService_SetSectionApplicabilityAsync_ActorWithoutSlot_ThrowsNoReviewerSlot**: A member who holds no slot marks
a section as not applicable; the call is refused with `NoReviewerSlot` and nothing is stored. This scenario is tested by
`ChecklistService_SetSectionApplicabilityAsync_ActorWithoutSlot_ThrowsNoReviewerSlot`.

**ChecklistService_SubmitBallotAsync_ActorWithoutSlot_ThrowsNoReviewerSlot**: A member who holds no slot hands a ballot
in; the call is refused with `NoReviewerSlot`. This scenario is tested by
`ChecklistService_SubmitBallotAsync_ActorWithoutSlot_ThrowsNoReviewerSlot`.

**ChecklistService_SubmitBallotAsync_TwoSlots_StaysDraftUntilSubmit**: In a review with two slots the reviewer of slot 1
answers every check; the ballot is still a draft. After `SubmitBallotAsync` it is submitted, and a second call returns
the submitted ballot and changes nothing. This scenario is tested by
`ChecklistService_SubmitBallotAsync_TwoSlots_StaysDraftUntilSubmit`.

**ChecklistService_SubmitBallotAsync_Incomplete_ThrowsBallotIncomplete**: In a review with two slots the reviewer
answers one check only and hands in; the call is refused with `BallotIncomplete` and the ballot stays a draft. This
scenario is tested by `ChecklistService_SubmitBallotAsync_Incomplete_ThrowsBallotIncomplete`.

**ChecklistService_SetAnswerAsync_SingleSlotComplete_AutoSubmits**: The reviewer of the only slot answers every check
but the last; the ballot is a draft. The last answer hands it in, with the user name of the reviewer. This scenario is
tested by `ChecklistService_SetAnswerAsync_SingleSlotComplete_AutoSubmits`.

**ChecklistService_SetSectionApplicabilityAsync_SingleSlotCompleting_AutoSubmits**: The reviewer of the only slot
answers every check of the checklist but those of one section; the ballot is a draft. Marking that section as not
applicable completes the ballot and hands it in. This scenario is tested by
`ChecklistService_SetSectionApplicabilityAsync_SingleSlotCompleting_AutoSubmits`.

**ChecklistService_GetAsync_SingleSlotDraft_ConsolidatedProgressCountsIt**: The reviewer of the only slot answers one
check and does not hand in; a read as a member without a slot counts that answer in the overall progress. This scenario
is tested by `ChecklistService_GetAsync_SingleSlotDraft_ConsolidatedProgressCountsIt`.

**ChecklistService_GetAsync_NoSlotViewerOfSingleSlotReview_GetsConsolidatedStanding**: The reviewer of the only slot
answers one check Pass and does not hand in; a read as a member without a slot is marked `NoSlot`, is not the own
ballot, is not masked, and shows the Pass. This scenario is tested by
`ChecklistService_GetAsync_NoSlotViewerOfSingleSlotReview_GetsConsolidatedStanding`.

**ChecklistService_GetAsync_Viewer_SeesOwnDraftOnly**: In a review with three agreeing, submitted ballots, a reviewer
changes an answer to Fail and does not hand it in. A read as that reviewer shows the own draft (`IsOwnBallot`, draft,
Fail); a read as another reviewer shows the own Pass, never the draft; a read without a viewer shows the consolidated
Pass. This scenario is tested by `ChecklistService_GetAsync_Viewer_SeesOwnDraftOnly`.

**ChecklistService_GetAsync_NoSlot_Consolidated**: In a review with one slot, held by a reviewer who has a draft
answer, a read as a member without a slot gives the consolidated standing (the review has a single slot, so nothing is
hidden), marked `NoSlot`, with no own ballot and no answer to that check, because the draft does not count. This
scenario is tested by `ChecklistService_GetAsync_NoSlot_Consolidated`.

**ChecklistService_GetBallotView_OtherBallotMissing_NamesThemForViewersOfTheProject**: Ann has handed in, bob has a
draft, and cyd is a member without a slot. The ballot view of ann and that of cyd name bob as not handed in; the view of
bob, who is masked because his ballot is a draft, names nobody (ann is in, and his own slot is never named); a user
outside the project gets an empty list. This scenario is tested by
`ChecklistService_GetBallotView_OtherBallotMissing_NamesThemForViewersOfTheProject`.

**ChecklistService_GetBallotView_SubmittedViewer_GetsReasonsOwnSlotAndFailedCount**: Ann passes and bob fails a check
with a reason, both handed in. The ballot view of ann holds the check as differing with the reason of bob and none of
ann, her own slot number 1, and no failed check, because a differing check is not a failed one. In a second review
where both fail the check, the view has no differing check and counts one failed check. This scenario is tested by
`ChecklistService_GetBallotView_SubmittedViewer_GetsReasonsOwnSlotAndFailedCount`.

**ChecklistService_GetBallotView_HiddenViewers_LearnNoReasonDisputeOrFailedCheck**: Ann handed in a Fail with a reason
and bob has a draft Fail. The ballot view for bob, for a member without a slot, for that member through an API token and
for a user outside the project has no differing check and no failed check, and its serialized form does not contain the
reason. This scenario is tested by `ChecklistService_GetBallotView_HiddenViewers_LearnNoReasonDisputeOrFailedCheck`.

**ChecklistService_SubmitBallotAsync_DigestChanging_RequiresAcknowledgmentOfOtherSlots**: Three submitted ballots
agree and slot 1 has signed. A reviewer of another slot changes an answer to Fail; the edit is stored and the signature
stays current, because the other ballots still agree. Handing the differing ballot in without an acknowledgment is
refused with `SignaturesWouldBecomeOutOfDate` and the signature stays; with slot 1 acknowledged the hand-in is stored
and the signature is out of date. This scenario is tested by
`ChecklistService_SubmitBallotAsync_DigestChanging_RequiresAcknowledgmentOfOtherSlots`.

**ChecklistService_SubmitBallotAsync_AgreeingSubmit_NoWarning**: Slot 1 has signed; a reviewer of another slot saves an
answer to the value it has and hands the ballot in without an acknowledgment; nothing is refused and the signature of
slot 1 is still current. This scenario is tested by `ChecklistService_SubmitBallotAsync_AgreeingSubmit_NoWarning`.

**ChecklistService_SubmitBallotAsync_SignedOwnSlot_NoSelfAcknowledgment**: The reviewer of slot 1 has signed, changes
the own answer to Fail and hands the ballot in without any acknowledgment; nothing is refused and the own signature is
out of date. This scenario is tested by `ChecklistService_SubmitBallotAsync_SignedOwnSlot_NoSelfAcknowledgment`.

**ChecklistService_Ballot_ReopenerWhileRuleIsOn_EditsAndSubmitsButCannotSign**: Cyd, the primary of the only slot,
reopens an approved review while the project requires that the reopener does not sign again. Cyd changes an answer and
puts it back and hands the ballot in; the ballot is submitted with the answer Pass. Cyd's request to sign is then
refused with `ReopenerCannotSign`. The test is in `SigningServiceTests.cs`, which holds the reopening fixture. This
scenario is tested by `ChecklistService_Ballot_ReopenerWhileRuleIsOn_EditsAndSubmitsButCannotSign`.

**ChecklistService_BallotWrites_AuditorHoldingSlot_ThrowsActorNotAllowed**: With the real access rules, an auditor who
still holds the only slot of a review tries to answer a check, to mark a section not applicable and to hand in the
ballot. Each is refused with `ActorNotAllowed`, not `NoReviewerSlot`, and no ballot is stored. This scenario is tested
by `ChecklistService_BallotWrites_AuditorHoldingSlot_ThrowsActorNotAllowed`.

**ChecklistService_SetAnswerAsync_ApprovedReview_ReportsLockedBeforeImpact**: On an approved review the refusal is
`ReviewLocked`, not the signature impact. This scenario is tested by
`ChecklistService_SetAnswerAsync_ApprovedReview_ReportsLockedBeforeImpact`.

**ChecklistService_Answer_AuthorWhileRuleIsOn_ThrowsAuthorCannotAnswerAndStoresNothing**: The author of the review
answers a check and changes a section while the rule is on (the default); each is refused with `AuthorCannotAnswer`
and nothing is stored, while another member answers. This scenario is tested by
`ChecklistService_Answer_AuthorWhileRuleIsOn_ThrowsAuthorCannotAnswerAndStoresNothing`.

**ChecklistService_Answer_AuthorWhileRuleIsOff_Stores**: With the rule switched off the author's answer is stored. This
scenario is tested by `ChecklistService_Answer_AuthorWhileRuleIsOff_Stores`.

**ChecklistService_Answer_AuthorOfApprovedReview_ReportsAuthorRuleBeforeLock**: The author answers on an approved
review; the refusal is `AuthorCannotAnswer`, not `ReviewLocked`. This scenario is tested by
`ChecklistService_Answer_AuthorOfApprovedReview_ReportsAuthorRuleBeforeLock`.

**ChecklistService_Get_ViewerUsername_SetsAuthorBlockedOnlyForTheAuthorWhileRuleIsOn**: `GetAsync` with the user name of
the author reports `AuthorBlocked` while the rule is on, and does not for another user, without a user name, or when
the rule is off. This scenario is tested by
`ChecklistService_Get_ViewerUsername_SetsAuthorBlockedOnlyForTheAuthorWhileRuleIsOn`.

**ChecklistService_DomainRuleException_AffectedSlots_AreCopiedFromTheSource**: The exception copies the list of
affected slots it is given, so a later change to the caller's list does not change it, and an exception without slots
has an empty list. This scenario is tested by `ChecklistService_DomainRuleException_AffectedSlots_AreCopiedFromTheSource`.

**ChecklistService_SubmitBallotAsync_Refusal_AffectedSlotsDoNotChangeAfterwards**: With the signature of slot 1 current,
a reviewer whose draft differs hands the ballot in without the acknowledgment; the refusal lists the affected slot and
its list is recorded. The ballot is then handed in with the acknowledgment, which puts the signature out of date; the
list of the refusal that was caught is still the recorded one. This scenario is tested by
`ChecklistService_SubmitBallotAsync_Refusal_AffectedSlotsDoNotChangeAfterwards`.

**ChecklistService_Constructor_NullArguments_Throws**: The service is created with a missing store, project access, or
clock; the constructor throws, so the service cannot exist without the check that the actor may see the project. This
scenario is tested by `ChecklistService_Constructor_NullArguments_Throws`.

**ChecklistService_SetAnswer_NonMember_ThrowsProjectNotFoundAndStoresNothing**: With the real access rules, a person
outside the project answers an unknown check; the call is refused with `ProjectNotFound`, not the not-found rule of the
check, and nothing is stored. This scenario is tested by
`ChecklistService_SetAnswer_NonMember_ThrowsProjectNotFoundAndStoresNothing`.

**ChecklistService_SetSectionApplicability_NonMember_ThrowsProjectNotFoundAndStoresNothing**: With the real access rules,
a person outside the project changes a section; the call is refused with `ProjectNotFound` and nothing is stored. This
scenario is tested by `ChecklistService_SetSectionApplicability_NonMember_ThrowsProjectNotFoundAndStoresNothing`.

**ChecklistService_SetAnswer_CarriedAnswer_StoredWithoutTheCarriedMark**: In a follow-up review a carried answer is
answered again; the stored row has no carried mark, while the other carried answer keeps it. This scenario is tested by
`ChecklistService_SetAnswer_CarriedAnswer_StoredWithoutTheCarriedMark`.

**ChecklistService_SetSectionApplicability_CarriedSection_StoredWithoutTheCarriedMark**: In a follow-up review a carried
not-applicable section is set again with a new reason; the stored state has no carried mark. This scenario is tested by
`ChecklistService_SetSectionApplicability_CarriedSection_StoredWithoutTheCarriedMark`.

**ChecklistService_Get_CarriedAnswers_CountAsAnswered**: In a follow-up review with two carried answers and a carried
not-applicable section, the progress counts the two answers as answered and the checks of the section leave the total
(2 of the checks of the default checklist without those of the section `DES`). This scenario is tested by
`ChecklistService_Get_CarriedAnswers_CountAsAnswered`.

**ChecklistState_Progress_FailedAnswer_CountsAsAnswered**: A check is answered Fail; it counts as answered. This
scenario is tested by `ChecklistState_Progress_FailedAnswer_CountsAsAnswered`.

**ChecklistState_Progress_CountsAnsweredPerSectionAndOverall**: Some checks are answered; each section and the
overall progress show the counts. This scenario is tested by
`ChecklistState_Progress_CountsAnsweredPerSectionAndOverall`.

**ChecklistState_Progress_NotApplicableSection_CountsAsDoneAndLeavesTotal**: A section is not applicable; it counts
as done and its checks leave the total. This scenario is tested by
`ChecklistState_Progress_NotApplicableSection_CountsAsDoneAndLeavesTotal`.

**ChecklistState_Progress_AnswersInNotApplicableSection_Ignored**: A not-applicable section holds answers; they are
not counted. This scenario is tested by `ChecklistState_Progress_AnswersInNotApplicableSection_Ignored`.

**ChecklistState_Progress_AllApplicableAnswered_IsComplete**: Every applicable check is answered; the checklist is
complete. This scenario is tested by `ChecklistState_Progress_AllApplicableAnswered_IsComplete`.

**ChecklistState_Progress_AllSectionsNotApplicable_IsComplete**: Every section is not applicable; the checklist is
complete with no checks to answer. This scenario is tested by
`ChecklistState_Progress_AllSectionsNotApplicable_IsComplete`.

**ChecklistState_NextUnansweredItemId_SkipsAnsweredAndNotApplicableSections**: The next unanswered check is the first
check that is neither answered nor in a not-applicable section. This scenario is tested by
`ChecklistState_NextUnansweredItemId_SkipsAnsweredAndNotApplicableSections`.

**ChecklistState_FirstIncompleteSectionId_ReturnsFirstSectionWithUnanswered**: The first incomplete section is the
first applicable section with an unanswered check. This scenario is tested by
`ChecklistState_FirstIncompleteSectionId_ReturnsFirstSectionWithUnanswered`.

**ChecklistService_Changes_Refused_RecordNoAuditEntry**: A Fail without a justification, an answer to an unknown check,
a change of an unknown section, and a not-applicable section without a reason are tried; each is refused and none of
them adds an audit entry. This scenario is tested by `ChecklistService_Changes_Refused_RecordNoAuditEntry`.

**ChecklistService_ChecklistEvents_RecordOutcomeAndDigestButNotJustification**: A check is answered and the
applicability of a section is changed; the entries `checklist.answered` and `checklist.applicability-changed` carry
the check or section and the outcome, and a justification appears only as an eight-character digest prefix. This
scenario is tested by `ChecklistService_ChecklistEvents_RecordOutcomeAndDigestButNotJustification`, which is defined
in `AuditEventTests.cs`.

**ChecklistService_ChecklistEvents_NameTheActor**: A check is answered and a section is changed by a reviewer; both
audit entries name that reviewer as the actor. This scenario is tested by
`ChecklistService_ChecklistEvents_NameTheActor`.

**ChecklistValidator_Validate_DefaultChecklist_IsValid**: The built-in default checklist is checked; it has no error.
This scenario is tested by `ChecklistValidator_Validate_DefaultChecklist_IsValid`.

**ChecklistValidator_Validate_SmallDefinition_IsValid**: A one-section, one-check definition is checked; it is valid.
This scenario is tested by `ChecklistValidator_Validate_SmallDefinition_IsValid`.

**ChecklistValidator_Validate_EmptyText_GivesOneError**: A theory over a missing, an empty, and a white-space text; each
gives one error for the text as a whole. This scenario is tested by `ChecklistValidator_Validate_EmptyText_GivesOneError`.

**ChecklistValidator_Validate_BadJson_GivesOneError**: A theory over text that is not JSON or not a definition; each
gives one error and no exception. This scenario is tested by `ChecklistValidator_Validate_BadJson_GivesOneError`.

**ChecklistValidator_Validate_ParserError_UsesTheCanonicalPath**: A theory over texts with a value of the wrong type at
different positions and with member names in different cases; each gives one error whose path is the canonical one
(`version`, `sections[0]`, `sections[0].items[0].id`, and so on), without `$`, and `json` for a list at the root. This
scenario is tested by `ChecklistValidator_Validate_ParserError_UsesTheCanonicalPath`.

**ChecklistValidator_Validate_TextSize_LimitsAtMaxBytes**: A text of exactly the size limit is accepted by the size
rule; one byte more gives the size error alone, without being parsed. This scenario is tested by
`ChecklistValidator_Validate_TextSize_LimitsAtMaxBytes`.

**ChecklistValidator_Validate_Version_BoundariesAndPresence**: A 64-character version is accepted; a 65-character, an
empty, and a missing version are each refused at the path `version`. This scenario is tested by
`ChecklistValidator_Validate_Version_BoundariesAndPresence`.

**ChecklistValidator_Validate_NoSections_IsRefused**: A definition with no sections is refused at the path `sections`.
This scenario is tested by `ChecklistValidator_Validate_NoSections_IsRefused`.

**ChecklistValidator_Validate_SectionCount_LimitsAtMaxSections**: Fifty sections are accepted and fifty-one are
refused. This scenario is tested by `ChecklistValidator_Validate_SectionCount_LimitsAtMaxSections`.

**ChecklistValidator_Validate_ItemCount_LimitsAtMaxItems**: Five hundred checks in all are accepted and five hundred
and one are refused. This scenario is tested by `ChecklistValidator_Validate_ItemCount_LimitsAtMaxItems`.

**ChecklistValidator_Validate_EmptySection_IsRefused**: A section without checks is refused at the path of its items.
This scenario is tested by `ChecklistValidator_Validate_EmptySection_IsRefused`.

**ChecklistValidator_Validate_IdForm_AcceptsOnlyTheAllowedCharacters**: A theory over identifiers, each used as a section
identifier and as a check identifier: letters, digits, dots, underscores, and hyphens that start with a letter or digit
are accepted; a leading hyphen, dot, or underscore, a space, a slash, a colon, a non-ASCII letter, and a line break are
refused. This scenario is tested by
`ChecklistValidator_Validate_IdForm_AcceptsOnlyTheAllowedCharacters`.

**ChecklistValidator_Validate_IdLength_LimitsAtMaxIdLength**: An identifier of 32 characters is accepted; one of 33
characters and a missing identifier are refused. This scenario is tested by `ChecklistValidator_Validate_IdLength_LimitsAtMaxIdLength`.

**ChecklistValidator_Validate_DuplicateIds_AreReportedAtTheLaterUse**: A check identifier repeated in another section or
in the same section, a check that repeats a section identifier, a repeated section identifier, and an identifier that
differs only in case are each reported at the later use, with the path of that value. This scenario is tested by
`ChecklistValidator_Validate_DuplicateIds_AreReportedAtTheLaterUse`.

**ChecklistValidator_Validate_TitlesTextAndHint_BoundariesAndPresence**: Titles of 200, check wording of 1000, and a hint
of 500 characters are accepted; one more character, an empty value, or a missing value is refused, each with its path.
This scenario is tested by `ChecklistValidator_Validate_TitlesTextAndHint_BoundariesAndPresence`.

**ChecklistValidator_Validate_NullEntries_AreReported**: A missing section entry, a missing list of checks, and a missing
check entry are each reported with their path instead of failing. This scenario is tested by
`ChecklistValidator_Validate_NullEntries_AreReported`.

**ChecklistValidator_Validate_ManyProblems_AreAllReportedTogether**: A definition with several different problems is
checked once; every problem is in the result, each with its path. This scenario is tested by
`ChecklistValidator_Validate_ManyProblems_AreAllReportedTogether`.

**ChecklistValidator_Validate_MemberNamesInAnyCase_AreRead**: A definition written with upper-case member names (as
`ToJson` writes them) is read and valid. This scenario is tested by
`ChecklistValidator_Validate_MemberNamesInAnyCase_AreRead`.

**ChecklistValidator_ParseOrThrow_Invalid_ThrowsWithPathsAndAtMostTenErrors**: An invalid definition with more than ten
errors raises `ChecklistDefinitionInvalid`; the message lists ten errors with their paths and says how many more there
are. This scenario is tested by `ChecklistValidator_ParseOrThrow_Invalid_ThrowsWithPathsAndAtMostTenErrors`.

**ChecklistValidationError_ToString_IsPathAndMessage**: An error is written as its path, a colon, and its message. This
scenario is tested by `ChecklistValidationError_ToString_IsPathAndMessage`.

**Carried answers and sections** are verified in `ChecklistServiceTests.cs`, over a follow-up review that the test
builds in the fake store with rows carried over from the first review.

**Review conclusion text** is verified in `ChecklistServiceReviewConclusionTextTests.cs`. The tests of who may write
use the real `ProjectAccess` and `FakeUserStore` with the users root (the lead and creator), alt (the reviewer of a
slot), usr and mem (plain members, usr also the primary and mem the alternate of a slot in the test of the alternate
reviewer), out (not a member) and aud (an auditor). The tests of the lock and of the signature
impact use the signed-review world, in which everybody is a lead. The text under test is the free text, never the
approved or rejected conclusion of a ballot.

**ChecklistService_SetReviewConclusionTextAsync_SlotOwnerOrLead_Stores**: A theory over the reviewer of a slot and the
lead; each stores a text, the review reads back with it, and exactly one `review.conclusion-changed` entry names the
writer. This scenario is tested by `ChecklistService_SetReviewConclusionTextAsync_SlotOwnerOrLead_Stores`.

**ChecklistService_SetReviewConclusionTextAsync_AlternateOfSlot_Stores**: A member who is the alternate of a slot, and
holds no other role, writes the text; it is stored and the single audit entry names that member. This scenario is
tested by `ChecklistService_SetReviewConclusionTextAsync_AlternateOfSlot_Stores`.

**ChecklistService_SetReviewConclusionTextAsync_OtherMember_ThrowsActorNotAllowed**: A plain member who holds no slot
and did not create the review is refused with `ActorNotAllowed`; nothing is stored or audited. This scenario is tested
by `ChecklistService_SetReviewConclusionTextAsync_OtherMember_ThrowsActorNotAllowed`.

**ChecklistService_SetReviewConclusionTextAsync_NonMember_ThrowsProjectNotFound**: A person outside the project is
refused with `ProjectNotFound`, like a project that does not exist. This scenario is tested by
`ChecklistService_SetReviewConclusionTextAsync_NonMember_ThrowsProjectNotFound`.

**ChecklistService_SetReviewConclusionTextAsync_Auditor_Refused**: An auditor is refused with `ActorNotAllowed`; nothing
is stored. This scenario is tested by `ChecklistService_SetReviewConclusionTextAsync_Auditor_Refused`.

**ChecklistService_SetReviewConclusionTextAsync_ApiToken_Refused**: The lead acting through an API token is refused with
`ActorNotAllowed`; nothing is stored. This scenario is tested by
`ChecklistService_SetReviewConclusionTextAsync_ApiToken_Refused`.

**ChecklistService_SetReviewConclusionTextAsync_Concluded_ThrowsReviewLocked**: On a review whose slots are all signed
with no open thread, the change is refused with `ReviewLocked`; the text and the audit trail are unchanged. This
scenario is tested by `ChecklistService_SetReviewConclusionTextAsync_Concluded_ThrowsReviewLocked`.

**ChecklistService_SetReviewConclusionTextAsync_CrlfBlankLongControl_NormalizesOrThrows**: A text with CRLF and
surrounding white space is stored with LF and trimmed; a text of exactly 4000 characters is stored; 4001 characters, a
control character and a lone carriage return are each refused with `ConclusionTextInvalid` and leave the text as it was;
a blank text clears it. This scenario is tested by
`ChecklistService_SetReviewConclusionTextAsync_CrlfBlankLongControl_NormalizesOrThrows`.

**ChecklistService_SetReviewConclusionTextAsync_StrayCarriageReturn_ThrowsAndStoresNothing**: A carriage return alone,
at the start, at the end, or among white space, or before a CRLF, is refused with `ConclusionTextInvalid`, because the
control character check runs before the trim; the text and the audit trail are unchanged. This scenario is tested by
`ChecklistService_SetReviewConclusionTextAsync_StrayCarriageReturn_ThrowsAndStoresNothing`.

**ChecklistService_SetReviewConclusionTextAsync_AfterSignature_NeedsAcknowledgment**: With slot 1 signed, a change
without acknowledgment is refused with `SignaturesWouldBecomeOutOfDate` naming slot 1 and nothing is stored; with the
acknowledgment the text is stored and the signature is out of date. This scenario is tested by
`ChecklistService_SetReviewConclusionTextAsync_AfterSignature_NeedsAcknowledgment`.

**ChecklistService_SetReviewConclusionTextAsync_SameValue_NoOpNoAudit**: Saving the text a review already has, written
as a browser sends it, returns `false`, needs no acknowledgment, records no entry and keeps the signature. This
scenario is tested by `ChecklistService_SetReviewConclusionTextAsync_SameValue_NoOpNoAudit`.

**ChecklistService_SetReviewConclusionTextAsync_Audit_RecordsLengthsOnly**: Setting, replacing and clearing the text
record entries with the details `length 0 -> 15`, `length 15 -> 5` and `length 5 -> 0`, the action
`review.conclusion-changed`, the project and the review, and no entry contains the text. This scenario is tested by
`ChecklistService_SetReviewConclusionTextAsync_Audit_RecordsLengthsOnly`.

**ChecklistService_SetReviewConclusionTextAsync_ConcurrentChange_ThrowsRecordChanged**: Another writer stores a text
after the service read the review and before it stores; the service throws `RecordChanged`, the first writer's text
and its single audit entry remain. This scenario is tested by
`ChecklistService_SetReviewConclusionTextAsync_ConcurrentChange_ThrowsRecordChanged`.

**ChecklistService_CanSetReviewConclusionTextAsync_Roles**: The lead and the reviewer of a slot may write the text; a
plain member, an auditor, a person outside the project, no actor, an API token and a missing review give `false`. This
scenario is tested by `ChecklistService_CanSetReviewConclusionTextAsync_Roles`.
