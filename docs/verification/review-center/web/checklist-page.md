### ChecklistPage

#### Verification Approach

`ChecklistPage` is verified with component tests defined in `ChecklistPageTests.cs` and `ChecklistPageFollowUpTests.cs`
(the marks of a follow-up review), which use the bUnit test context
to render the real component over real Core services and an in-memory fake store holding a review with the default
checklist. The signed-in user is supplied by a test authentication state in `PageTestBase`: the user `ann` (display
name "Ann") unless a test signs in as someone else. Tests locate elements by their `data-testid` attributes (the page
units share the helper `PageTestBase`), click the buttons, and compare the shown values with the progress counts of
the default checklist. The browser
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
- A Pass is stored at once with the signed-in user and the time, and the progress follows.
- A change on a review with a current signature is stored by the page, which acknowledges the affected signatures for
  now, and the signature is then out of date.
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
- A review that is approved is shown as locked: a notice (`checklist-locked`) says that the checklist is read-only and
  that an administrator can reopen the review, and the Pass, Fail, and N/A buttons, the justification and reason boxes,
  the save buttons, and the applicability switches are disabled and marked `data-locked="true"`, while the sections
  can still be opened and read; a review that is not approved shows no notice and enabled controls.

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
user name and display name of that user. This scenario is tested by `ChecklistPage_Answer_SignedInUser_IsRecordedAsAnsweredBy`.

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

**ChecklistPage_Answer_SignedReview_ShowsWarningAndConfirmStoresTheAnswerAndMarksTheSignatureOutOfDate**: A review is
fully answered, has two slots, and only the first is signed. A check is changed to Fail with a justification; the page
shows the warning naming "Slot 1 (Rita)", the old answer and the signature stand. Cancel removes the warning and keeps
the unsaved Fail and its text. Saving again and Confirm store the answer and the first slot's signature is out of date.
This scenario is tested by
`ChecklistPage_Answer_SignedReview_ShowsWarningAndConfirmStoresTheAnswerAndMarksTheSignatureOutOfDate`.

**ChecklistPage_Answer_PassOnSignedReview_ShowsWarningNextToTheCheckAndConfirmStores**: A check is changed to Pass on a
signed review; the warning is shown inside that check and the stored answer is unchanged until Confirm, which stores it
and makes the signature out of date. This scenario is tested by
`ChecklistPage_Answer_PassOnSignedReview_ShowsWarningNextToTheCheckAndConfirmStores`.

**ChecklistPage_Answer_SignatureAddedWhileWarningOpen_ShowsMergedWarningBeforeStoring**: With a warning open, another
reviewer signs a second slot and Confirm is clicked; nothing is stored and the warning names both slots. A second
Confirm stores the answer. This scenario is tested by
`ChecklistPage_Answer_SignatureAddedWhileWarningOpen_ShowsMergedWarningBeforeStoring`.

**ChecklistPage_Applicability_NotApplicableOnSignedReview_ShowsWarningAndConfirmStores**: A section is switched off and
its reason saved on a signed review; the warning is shown in the section and nothing is stored. Cancel removes the
warning and keeps the unsaved switch and reason. Saving again and Confirm store the section as not applicable and the
signature is out of date. This scenario is tested by
`ChecklistPage_Applicability_NotApplicableOnSignedReview_ShowsWarningAndConfirmStores`.

**ChecklistPage_Applicability_SwitchOnOnSignedReview_CancelRevertsTheSwitchAndConfirmStores**: A section that is not
applicable is switched on, on a signed review; the warning is shown. Cancel returns the switch to its stored state;
switching on again and Confirm store the section as applicable. This scenario is tested by
`ChecklistPage_Applicability_SwitchOnOnSignedReview_CancelRevertsTheSwitchAndConfirmStores`.

**ChecklistPage_Applicability_SwitchOnWarningOpen_SwitchingOffDropsTheWarning**: While the switch-on warning is open,
the section is switched off again; the warning is gone, the switch is off, and nothing is stored. This scenario is
tested by `ChecklistPage_Applicability_SwitchOnWarningOpen_SwitchingOffDropsTheWarning`.

**ChecklistPage_Render_SignatureWarningOpen_EveryInteractiveElementHasKebabCaseTestId**: With a warning open, every
link, button, input, and text area of the page, including the two buttons of the warning, has a kebab-case
`data-testid`. This scenario is tested by
`ChecklistPage_Render_SignatureWarningOpen_EveryInteractiveElementHasKebabCaseTestId`.

**ChecklistPage_Render_NonMember_LooksLikeAMissingReview**: A review is opened by a user who is no member of the
project. The not-found text equals the text for a project that does not exist (apart from the project number), and no
status, progress, check text, project name or review title is shown. This scenario is tested by
`ChecklistPage_Render_NonMember_LooksLikeAMissingReview`.

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
