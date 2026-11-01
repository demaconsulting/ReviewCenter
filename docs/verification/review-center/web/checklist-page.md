### ChecklistPage

#### Verification Approach

`ChecklistPage` is verified with component tests defined in `ChecklistPageTests.cs`, `ChecklistPageFollowUpTests.cs`
(the marks of a follow-up review), and `ChecklistPageBallotTests.cs` (the ballot controls). The hidden ballots and the
side-by-side answers are verified in `ChecklistPageSideBySideTests.cs`, Submit and sign in
`ChecklistPageSubmitAndSignTests.cs`, Assign me in `ChecklistPageAssignMeTests.cs` (all over the shared helper
`BallotPageTestBase.cs`), and the `SignConfirm` component in `SignConfirmTests.cs`, and use the bUnit test
context
to render the real component over real Core services and an in-memory fake store holding a review with the default
checklist. The signed-in user is supplied by a test authentication state in `PageTestBase`: the user `ann` (display
name "Ann") unless a test signs in as someone else; the tests that answer give that user a reviewer slot first, because
answering needs a slot (see `NoReviewerSlot`), so the ballot of a review with one slot counts as soon as it is complete.
Tests locate elements by their `data-testid` attributes (the page units share the helper `PageTestBase`), click the
buttons, and compare the shown values with the progress counts of the default checklist. The browser
function that scrolls is replaced by bUnit's JavaScript interop mock, which records the call. The real database is
covered by the `Storage` subsystem and the integration tests of the `Web` subsystem.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- An unknown review shows a not-found message, and a review without a checklist says so.
- A new review shows the status line "58 checks unanswered.", the overall progress, every section with its
  progress, and only the first section open; a partly answered review opens the first section with unanswered
  checks; opening a section closes the one that was open.
- A Pass is stored at once with the signed-in user and the time, in the draft ballot of the user's slot, and the
  progress follows.
- A reviewer who holds a slot sees the state of the own ballot; a complete draft ballot of a review with several slots
  offers **Submit ballot**, which hands it in and changes the status line; an incomplete ballot and the ballot of a
  single-slot review that the completing answer handed in offer no button (a complete single-slot draft that no answer
  produced still does); a user without a slot sees the notice
  `no-slot-notice`, no ballot status and no button.
- Saving answers into a draft shows no signature warning; handing the ballot in, or taking back a dissent that the
  others did not share, while a signature of another slot is current, stores nothing and shows a warning that names the
  slots; the signature is out of date only after Confirm.
- A Fail or N/A shows a justification box marked as required; saving it empty shows an error next to the box and
  stores nothing; saving it with text stores the answer and shows who answered it and when.
- The page has no field for a name; an answer is recorded under the user name and display name of the signed-in
  user.
- A section marked as not applicable without a reason shows an error and changes nothing; with a reason it counts as
  done, shows no checks, and leaves the totals; while the switch is off and no reason is saved yet, the section says
  "Reason needed" and that nothing is saved until the reason is saved.
- The next-unanswered button opens the section of the first unanswered check and asks the browser to scroll to
  it; when everything is answered the page says the checklist is complete and offers a link back to the review
  instead of that button.
- A finished section that is open ends with the next action: the next-unanswered button, or a link back to the
  review once the whole checklist is complete.
- Every interactive element has a kebab-case `data-testid`, and the interactive marker is `true` after the page has
  rendered on a live connection.
- A review that is concluded (approved or rejected) is shown as locked: a notice (`checklist-locked`) says that the
  checklist is read-only and
  that an administrator can reopen the review, and the Pass, Fail, and N/A buttons, the justification and reason boxes,
  the save buttons, and the applicability switches are disabled and marked `data-locked="true"`, while the sections
  can still be opened and read; a review that is not concluded shows no notice and enabled controls.
- While the project does not let authors answer the checklist, the author of the review sees the notice
  `author-answer-blocked` and the same controls disabled (without `data-locked`), no other member sees the notice, the
  notice is not shown when the rule is off, an approved review shows only the lock notice, and a page that was loaded
  before the rule was switched on shows the refusal of the rule when the author clicks, and stores nothing.
- In a review with several slots, a reviewer who has not handed in sees the count of handed-in ballots and the notice
  that the others stay hidden, and no standing, no disputed count, and no different-answers badge; a member without a
  slot sees the same count and no standing; an administrator sees the differing answers without a ballot; a review
  with one slot shows none of it.
- A reviewer who has handed in sees the differing answers by name in slot order under the check, with a badge, and
  Discuss starts a discussion thread about the check, refuses an empty text, and closes on cancel.
- A complete draft offers Submit and sign as the main action and Submit ballot as the secondary one; an incomplete
  draft and a review with one slot offer neither combination; Submit and sign hands in and signs, a wrong password
  leaves the ballot handed in and explains, a change by another reviewer between the hand-in and the signature leaves
  the ballot handed in, signs nothing and says that something changed, Submit ballot alone does not sign, and an
  account of an identity provider is handed in and then asked to confirm at the provider.
- Assign me as reviewer adds a new slot for a member without one and keeps the existing slots; it is offered to a project
  administrator without a slot, and to nobody who holds a slot or is no member.
- `SignConfirm` hands the password (and the code) over once and clears the password field, and draws no field for an
  account of an identity provider.

**ChecklistPage_Render_NewReview_ShowsProgressAndOpensFirstSection**: The page is rendered for a new review; the
status line, the overall progress, all 14 sections with their progress, and exactly one open section are shown, and
each check's outcome group is named by its check text.
This scenario is tested by `ChecklistPage_Render_NewReview_ShowsProgressAndOpensFirstSection`.

**ChecklistPage_Render_UnknownReview_ShowsNotFound**: The page is rendered for an unknown review; the not-found
message is shown and no checklist. This scenario is tested by `ChecklistPage_Render_UnknownReview_ShowsNotFound`.

**ChecklistPage_Render_ReviewWithoutChecklist_ShowsMissing**: The page is rendered for a review stored without a
checklist; a message says the review has none. This scenario is tested by
`ChecklistPage_Render_ReviewWithoutChecklist_ShowsMissing`.

**ChecklistPage_Render_PartlyAnswered_OpensFirstIncompleteSection**: Every check of the first section is answered;
the second section is the one that is open. This scenario is tested by
`ChecklistPage_Render_PartlyAnswered_OpensFirstIncompleteSection`.

**ChecklistPage_Render_OpeningSection_ClosesOthers**: A section heading is clicked; that section is open and the
previously open one is closed. This scenario is tested by `ChecklistPage_Render_OpeningSection_ClosesOthers`.

**ChecklistPage_Answer_Pass_SavesImmediately**: Pass is clicked; the answer is stored with the signed-in user and the
clock time, the button is pressed, and the progress counts it. This scenario is tested by
`ChecklistPage_Answer_Pass_SavesImmediately`.

**ChecklistPage_Answer_Fail_ShowsJustificationBoxAndRequiresText**: Fail is clicked; the justification box appears
with its required marker, saving it empty shows an error next to it, and nothing is stored. This scenario is tested
by `ChecklistPage_Answer_Fail_ShowsJustificationBoxAndRequiresText`.

**ChecklistPage_Answer_FailWithJustification_SavesAndShowsAnsweredBy**: Fail is clicked and a justification is
saved; the answer is stored, the text stays in the box, and "Answered by" shows the display name and the UTC time.
This scenario is tested by `ChecklistPage_Answer_FailWithJustification_SavesAndShowsAnsweredBy`.

**ChecklistPage_Answer_SignedInUser_IsRecordedAsAnsweredBy**: Another user ("bea") is signed in and Pass is clicked;
the page has no name field, "Answered by" shows the display name of that user, and the stored answer carries the
user name and display name of that user. This scenario is tested by
`ChecklistPage_Answer_SignedInUser_IsRecordedAsAnsweredBy`.

**ChecklistPage_Applicability_NotApplicableWithoutReason_ShowsError**: A section is switched to not applicable and
saved without a reason; an error is shown and the section is still applicable in the store. This scenario is tested
by `ChecklistPage_Applicability_NotApplicableWithoutReason_ShowsError`.

**ChecklistPage_Applicability_NotApplicableWithReason_CountsAsDoneAndHidesChecks**: A section is switched to not
applicable and saved with a reason; the section shows "Not applicable", its checks are hidden, and the totals
exclude its checks. This scenario is tested by
`ChecklistPage_Applicability_NotApplicableWithReason_CountsAsDoneAndHidesChecks`.

**ChecklistPage_Applicability_SwitchOffBeforeReasonSaved_ShowsReasonNeeded**: A section is switched off without a
reason; its progress label reads "Reason needed" with the `pending` style, the hint "Not saved until you press Save
reason." is shown, the totals are unchanged, and the stored section still applies. After a reason is typed and saved the
section shows "Not applicable" and the hint is gone. This scenario is tested by
`ChecklistPage_Applicability_SwitchOffBeforeReasonSaved_ShowsReasonNeeded`.

**ChecklistPage_NextUnanswered_Click_OpensSectionOfFirstUnansweredCheck**: With all but the last check of the first
section answered and another section open, the next-unanswered button is clicked; the first section is open, its
last check is shown, and the browser is asked to scroll to that check. This scenario is tested by
`ChecklistPage_NextUnanswered_Click_OpensSectionOfFirstUnansweredCheck`.

**ChecklistPage_NextUnanswered_PendingNotApplicableSection_SkipsIt**: With the first section answered, the second
section is switched off without saving and the next-unanswered button is clicked; the section after it is open and
the browser is asked to scroll to its first check. This scenario is tested by
`ChecklistPage_NextUnanswered_PendingNotApplicableSection_SkipsIt`.

**ChecklistPage_NextUnanswered_OnlyPendingNotApplicableSectionLeft_OpensItsReasonField**: With every section but one
answered, that section is switched off without saving and closed, and the next-unanswered button is clicked; the
section is open with its reason field and the browser is asked to scroll to the reason field. This scenario is tested
by `ChecklistPage_NextUnanswered_OnlyPendingNotApplicableSectionLeft_OpensItsReasonField`.

**ChecklistPage_Render_AllAnswered_ShowsCompleteAndNoNextAction**: Every check is answered; the status line says
"Checklist complete.", the next-unanswered button is absent, and a link back to the review is shown. This scenario
is tested by `ChecklistPage_Render_AllAnswered_ShowsCompleteAndNoNextAction`.

**ChecklistPage_FinishedSection_OffersNextActionAtItsEnd**: A finished section is opened; it ends with a
next-unanswered button, which opens the section of the next unanswered check. With every check answered, the end of
the section holds a link back to the review instead. This scenario is tested by
`ChecklistPage_FinishedSection_OffersNextActionAtItsEnd`.

**ChecklistPage_Render_EveryInteractiveElementHasKebabCaseTestId**: A Fail is saved without a justification and a
section is switched off and saved without a reason, so that every kind of control and error is shown; every link,
button, input, and text area of the page has a kebab-case `data-testid`. This scenario is tested by
`ChecklistPage_Render_EveryInteractiveElementHasKebabCaseTestId`.

**ChecklistPage_AfterRender_Interactive_SetsMarker**: The page is rendered; the root element reports interactivity.
This scenario is tested by `ChecklistPage_AfterRender_Interactive_SetsMarker`.

**ChecklistPage_Render_ApprovedReview_ShowsLockedAndDisablesControls**: The page is rendered for a review whose every
slot holds a current signature; the notice `checklist-locked` is shown, the check buttons, the justification boxes, the
save buttons, and the applicability switches are disabled and marked `data-locked="true"`, and a section can still be
opened. The same page for a review that is not approved shows no notice and no disabled control. This scenario is
tested by `ChecklistPage_Render_ApprovedReview_ShowsLockedAndDisablesControls`.

**ChecklistPage_Render_AuthorWhileRuleIsOn_ShowsNoticeAndDisablesControls**: The page is rendered for the author of the
review while the rule is on: the notice `author-answer-blocked` is shown and the check buttons, boxes, and switches are
disabled. Another member sees no notice and enabled controls, and with the rule switched off the author sees none. This
scenario is tested by `ChecklistPage_Render_AuthorWhileRuleIsOn_ShowsNoticeAndDisablesControls`.

**ChecklistPage_Render_AuthorOfApprovedReview_ShowsOnlyTheLockNotice**: The author of an approved review sees
`checklist-locked` and not `author-answer-blocked`. This scenario is tested by
`ChecklistPage_Render_AuthorOfApprovedReview_ShowsOnlyTheLockNotice`.

**ChecklistPage_Render_RejectedConcludedReview_IsReadOnly**: The page is rendered for a review concluded as rejected;
the notice `checklist-locked` is shown with the wording for a review that is concluded (approved or rejected), and the
controls are disabled. This scenario is tested by `ChecklistPage_Render_RejectedConcludedReview_IsReadOnly`.

**ChecklistPage_Answer_RuleSwitchedOnAfterLoad_ShowsTheRefusalAndStoresNothing**: The page is loaded while the rule is
off, the rule is switched on, and the author clicks Pass; the error line of the check contains "Authors cannot answer
the checklist" and nothing is stored. This scenario is tested by
`ChecklistPage_Answer_RuleSwitchedOnAfterLoad_ShowsTheRefusalAndStoresNothing`.

**ChecklistPage_Answer_SignedReview_ShowsWarningAndConfirmStoresTheAnswerAndMarksTheSignatureOutOfDate**: A review has
two slots; the first (Rita) is signed and the owner of the second (Sam) has handed in a ballot that agrees. Sam changes
a check to Fail with a justification and saves; the answer is stored as a draft of Sam's ballot, no warning is shown
(the draft is outside the record) and the signature stays current. **Submit ballot** shows the warning naming
"Slot 1 (Rita)" and the ballot stays a draft. Cancel removes the warning, the ballot stays a draft and the signature
stays current. Submit again and Confirm make the ballot submitted and the first slot's signature out of date. The name
of the test is older than the ballots; what it verifies now is the draft, the hand-in warning and the confirmation.
This scenario is tested by
`ChecklistPage_Answer_SignedReview_ShowsWarningAndConfirmStoresTheAnswerAndMarksTheSignatureOutOfDate`.

**ChecklistPage_Answer_PassOnSignedReview_ShowsWarningNextToTheCheckAndConfirmStores**: Sam had submitted a Fail with a
justification on the first check (a dissent) before the first slot was signed. Sam passes the check: taking the
dissent back returns the ballot to a draft and changes the consolidated standing, so the warning is shown at once inside
that check, and the stored answer keeps its justification until Confirm, which stores the Pass and makes the signature
out of date. This scenario is tested by
`ChecklistPage_Answer_PassOnSignedReview_ShowsWarningNextToTheCheckAndConfirmStores`.

**ChecklistPage_Answer_SignatureAddedWhileWarningOpen_ShowsMergedWarningBeforeStoring**: A review has three slots with
the first signed. The owner of the third (Tom) saves a Fail as a draft and presses Submit ballot; the warning names
"Slot 1 (Rita)". Another reviewer signs the second slot meanwhile and Confirm is clicked; the ballot is still a draft
and the warning now names "Slot 1 (Rita), Slot 2 (Sam)". A second Confirm hands the ballot in. This scenario is tested
by `ChecklistPage_Answer_SignatureAddedWhileWarningOpen_ShowsMergedWarningBeforeStoring`.

**ChecklistPage_Applicability_NotApplicableOnSignedReview_ShowsWarningAndConfirmStores**: On a signed review a section
is switched off and its reason saved; the section is stored as not applicable in the draft ballot with no warning and
the signature stays current. **Submit ballot** shows the warning under the ballot status line and the ballot stays a
draft; Cancel keeps the switch off with the typed reason. Submit and Confirm make the ballot submitted, with the section
not applicable and the reason, and the signature out of date. This scenario is tested by
`ChecklistPage_Applicability_NotApplicableOnSignedReview_ShowsWarningAndConfirmStores`.

**ChecklistPage_Applicability_SwitchOnOnSignedReview_CancelRevertsTheSwitchAndConfirmStores**: Sam had submitted a
ballot that marks a section not applicable, which Rita's signed ballot does not. Switching the section on again takes
the dissent back and changes the standing, so the warning is shown at once. Cancel returns the switch to its stored
state; switching on again and Confirm store the section as applicable and make the signature out of date. This scenario
is tested by `ChecklistPage_Applicability_SwitchOnOnSignedReview_CancelRevertsTheSwitchAndConfirmStores`.

**ChecklistPage_Applicability_SwitchOnWarningOpen_SwitchingOffDropsTheWarning**: While the switch-on warning is open,
the section is switched off again; the warning is gone, the switch is off, and nothing is stored. This scenario is
tested by `ChecklistPage_Applicability_SwitchOnWarningOpen_SwitchingOffDropsTheWarning`.

**ChecklistPage_Render_SignatureWarningOpen_EveryInteractiveElementHasKebabCaseTestId**: With the warning of the
taken-back dissent open inside a check, every link, button, input, and text area of the page, including the two buttons
of the warning, has a kebab-case `data-testid`. This scenario is tested by
`ChecklistPage_Render_SignatureWarningOpen_EveryInteractiveElementHasKebabCaseTestId`.

**ChecklistPage_SubmitButton_ShownWhenDraftComplete**: A review has two slots (ann and bob) and ann has answered every
check, so her own ballot is a complete draft. The page for ann shows a ballot status line that says draft and the
`submit-ballot` button; pressing it hands the ballot in, the status line says submitted, the button is gone, and the
service reports the ballot as submitted. This scenario is tested by `ChecklistPage_SubmitButton_ShownWhenDraftComplete`.

**ChecklistPage_SubmitButton_HiddenWhenIncompleteOrSingleSlot**: In a review with two slots where ann has answered one
check only, the page shows no submit button. In a review with one slot (cyd) that cyd answered completely, which hands
the ballot in, the page shows no submit button and the status line says submitted. This scenario is tested by
`ChecklistPage_SubmitButton_HiddenWhenIncompleteOrSingleSlot`.

**ChecklistPage_NoSlot_ShowsHint**: ann opens the checklist of a review whose only slot belongs to bob; the notice
`no-slot-notice` is shown, and there is no submit button and no ballot status line. This scenario is tested by
`ChecklistPage_NoSlot_ShowsHint`.

**ChecklistPage_Render_NonMember_LooksLikeAMissingReview**: A review is opened by a user who is no member of the
project. The not-found text equals the text for a project that does not exist (apart from the project number), and no
status, progress, check text, project name or review title is shown. This scenario is tested by
`ChecklistPage_Render_NonMember_LooksLikeAMissingReview`.

**ChecklistPage_Render_Auditor_ShowsReadOnlyNoticeAndDisablesEveryControl**: An Auditor opens the checklist; the answers
are visible, the `readonly-notice` is shown, and every answer, applicability, and justification control is disabled.
This scenario is tested by `ChecklistPage_Render_Auditor_ShowsReadOnlyNoticeAndDisablesEveryControl`.

**ChecklistPage_Choose_MembershipRevoked_ShowsNotFoundAndNoData**: A member has the checklist open and the membership is
removed; passing a check shows the not-found message, nothing of the checklist remains in the markup, and no answer is
stored. This scenario is tested by `ChecklistPage_Choose_MembershipRevoked_ShowsNotFoundAndNoData`.

**ChecklistPage_ToggleSection_MembershipRevoked_ShowsNotFoundAndNoData**: The same arrangement, and a section is opened,
an action that reads no service; the access check still runs and the result is the not-found page without data. This
scenario is tested by `ChecklistPage_ToggleSection_MembershipRevoked_ShowsNotFoundAndNoData`.

**ChecklistPage_Render_FollowUp_ShowsCarriedMarksAndEarlierFail**: The checklist of a follow-up is rendered; a carried
Pass and a carried not-applicable section carry a "Carried from" mark with a link to the earlier review, and a check
that failed in the earlier review shows the earlier Fail with its justification and a link, and no carried mark. This
scenario is tested by `ChecklistPage_Render_FollowUp_ShowsCarriedMarksAndEarlierFail`.

**ChecklistPage_Render_FollowUp_DecidedAgainOrEarlierReview_ShowsNoEarlierFail**: Once the failed check is answered
again the earlier Fail is gone, and the checklist of the earlier review itself shows no carried mark and no earlier
Fail. This scenario is tested by `ChecklistPage_Render_FollowUp_DecidedAgainOrEarlierReview_ShowsNoEarlierFail`.

**ChecklistPage_Render_FollowUp_ReworkedCheckThatFailed_ShowsNoEarlierFail**: Two checks failed in the earlier review
and the project then reworded one of them before the follow-up was created. The follow-up shows no earlier Fail for the
reworded check, because the earlier justification belongs to other wording, and still shows it for the unchanged check.
This scenario is tested by `ChecklistPage_Render_FollowUp_ReworkedCheckThatFailed_ShowsNoEarlierFail`.

**ChecklistPage_DraftBallot_HidesOtherBallotsAndDisputeCounts**: In a review with three slots two ballots are handed in
with different answers, and the third reviewer has a draft. The page shows how many ballots are handed in and the notice
that the others stay hidden, and neither a standing, nor a count of disputed checks, nor a name of another reviewer next
to an answer. This scenario is tested by `ChecklistPage_DraftBallot_HidesOtherBallotsAndDisputeCounts`.

**ChecklistPage_MemberWithoutSlot_SeesNoStandingOnlyBallotCount**: A member without a slot opens the checklist of such a
review and sees the count and the Assign me button, and no standing. This scenario is tested by
`ChecklistPage_MemberWithoutSlot_SeesNoStandingOnlyBallotCount`.

**ChecklistPage_SubmittedBallot_ShowsSideBySideForDifferingChecks**: A reviewer who has handed in sees "Ann: Pass /
Bob: N/A" under the check on which the ballots differ and not under a check on which they agree. This scenario is tested
by `ChecklistPage_SubmittedBallot_ShowsSideBySideForDifferingChecks`.

**ChecklistPage_AdministratorWithoutBallot_SeesDifferingAnswers**: A system administrator who holds no slot sees the
differing answers. This scenario is tested by `ChecklistPage_AdministratorWithoutBallot_SeesDifferingAnswers`.

**ChecklistPage_SingleSlot_ShowsNoBallotCountsOrSideBySide**: A review with one slot shows no count of ballots and no
side-by-side line. This scenario is tested by `ChecklistPage_SingleSlot_ShowsNoBallotCountsOrSideBySide`.

**ChecklistPage_DisputedCheck_DiscussStartsCheckThread**: Discuss is pressed, a text is typed, and the discussion is
started; a thread about the check exists with that text, and a link to the review page is shown. This scenario is tested
by `ChecklistPage_DisputedCheck_DiscussStartsCheckThread`.

**ChecklistPage_DiscussWithoutText_ShowsError**: Discuss is started without text; a message is shown next to the form
and no thread is created. This scenario is tested by `ChecklistPage_DiscussWithoutText_ShowsError`.

**ChecklistPage_DiscussCancel_ClosesTheForm**: Cancel closes the Discuss form. This scenario is tested by
`ChecklistPage_DiscussCancel_ClosesTheForm`.

**ChecklistPage_CompleteDraft_OffersSubmitAndSignAsPrimaryAction**: A reviewer with a complete draft in a review with
two slots sees Submit and sign as the main action with the password field, and Submit ballot as the secondary one. This
scenario is tested by `ChecklistPage_CompleteDraft_OffersSubmitAndSignAsPrimaryAction`.

**ChecklistPage_IncompleteDraft_OffersNoSubmitButtons**: An incomplete draft offers neither button. This scenario is
tested by `ChecklistPage_IncompleteDraft_OffersNoSubmitButtons`.

**ChecklistPage_SubmitAndSign_SubmitsThenSigns**: The password is typed and Submit and sign is pressed; the ballot is
submitted and the slot has signed. This scenario is tested by `ChecklistPage_SubmitAndSign_SubmitsThenSigns`.

**ChecklistPage_SubmitAndSign_WrongPassword_KeepsBallotAndExplains**: A wrong password is typed; the ballot is handed
in, no signature is stored, and the page says so. This scenario is tested by
`ChecklistPage_SubmitAndSign_WrongPassword_KeepsBallotAndExplains`.

**ChecklistPage_SubmitBallotAlone_DoesNotSign**: Submit ballot is pressed; the ballot is handed in and the slot has not
signed. This scenario is tested by `ChecklistPage_SubmitBallotAlone_DoesNotSign`.

**ChecklistPage_SingleSlot_OffersNoSubmitAndSign**: A review with one slot offers no Submit and sign button. This
scenario is tested by `ChecklistPage_SingleSlot_OffersNoSubmitAndSign`.

**ChecklistPage_SubmitAndSign_ProviderUser_SubmitsThenAsksToConfirmIdentity**: A reviewer whose account is of an
identity provider presses the button; the ballot is handed in and the page asks for the confirmation at the provider.
This scenario is tested by `ChecklistPage_SubmitAndSign_ProviderUser_SubmitsThenAsksToConfirmIdentity`.

**ChecklistPage_SubmitAndSign_ProviderUser_DifferingBallotRevealed_ExplainsAndSkipsConfirmation**: A reviewer of an
identity provider presses the button while the other reviewer changes an answer so that the ballots differ right after
the hand-in. The ballot is handed in, no signature is stored, no confirmation at the provider is offered, and the page
says that something changed and that the ballot is not signed. This scenario is tested by
`ChecklistPage_SubmitAndSign_ProviderUser_DifferingBallotRevealed_ExplainsAndSkipsConfirmation`.

**ChecklistPage_SubmitAndSign_OtherReviewerChangesAnswerAfterHandIn_SignsNothingAndExplains**: A reviewer presses
Submit and sign while the other reviewer changes an answer so that the ballots differ right after the hand-in. The
ballot is handed in, no signature is stored, the page says that something changed and that the ballot is not signed,
and shows the differing answers that the reviewer now sees, so the reviewer reads them and signs from the review page.
This scenario is tested by
`ChecklistPage_SubmitAndSign_OtherReviewerChangesAnswerAfterHandIn_SignsNothingAndExplains`.

**ChecklistPage_AssignMe_AddsNewSlotAndKeepsExistingOnes**: A member without a slot presses Assign me; a new slot with
the member as primary reviewer and no alternate exists, and the other slots are as before. This scenario is tested by
`ChecklistPage_AssignMe_AddsNewSlotAndKeepsExistingOnes`.

**ChecklistPage_ProjectAdministratorWithoutSlot_OffersAssignMe**: A project administrator without a slot is offered the
button. This scenario is tested by `ChecklistPage_ProjectAdministratorWithoutSlot_OffersAssignMe`.

**ChecklistPage_ViewerWithSlot_OffersNoAssignMe**: A reviewer who holds a slot is not offered the button. This scenario
is tested by `ChecklistPage_ViewerWithSlot_OffersNoAssignMe`.

**ChecklistPage_NonMember_OffersNoAssignMe**: A user who is no member sees no button. This scenario is tested by
`ChecklistPage_NonMember_OffersNoAssignMe`.

**SignConfirm_PasswordUser_HandsPasswordOverOnceAndClearsField**: The password is typed and the button pressed; the
password is handed over once and the field is empty afterwards. This scenario is tested by
`SignConfirm_PasswordUser_HandsPasswordOverOnceAndClearsField`.

**SignConfirm_UserWithSecondFactor_HandsPasswordAndCodeOver**: With a second factor the password and the code are
handed over. This scenario is tested by `SignConfirm_UserWithSecondFactor_HandsPasswordAndCodeOver`.

**SignConfirm_ProviderUser_ShowsNoFieldsAndHandsOverNothing**: For an account of an identity provider no field is shown
and nothing is handed over. This scenario is tested by `SignConfirm_ProviderUser_ShowsNoFieldsAndHandsOverNothing`.
