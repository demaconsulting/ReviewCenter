### ChecklistPage

#### Verification Approach

`ChecklistPage` is verified with component tests defined in `ChecklistPageTests.cs`, which use the bUnit test context
to render the real component over real Core services and an in-memory fake store holding a review with the default
checklist. Tests locate elements by their `data-testid` attributes (the page units share the helper `PageTestBase`),
click the buttons, and compare the shown values with the progress counts of the default checklist. The browser
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
- A Pass is stored at once with the name and time, and the progress follows.
- A Fail or N/A shows a justification box marked as required; saving it empty shows an error next to the box and
  stores nothing; saving it with text stores the answer and shows who answered it and when.
- Answering without a name shows an error that names the "Your name" field and stores nothing.
- A section marked as not applicable without a reason shows an error and changes nothing; with a reason it counts as
  done, shows no checks, and leaves the totals.
- The next-unanswered button opens the section of the first unanswered check and asks the browser to scroll to
  it; when everything is answered the page says the checklist is complete and offers a link back to the review
  instead of that button.
- A finished section that is open ends with the next action: the next-unanswered button, or a link back to the
  review once the whole checklist is complete.
- Every interactive element has a kebab-case `data-testid`, and the interactive marker is `true` after the page has
  rendered on a live connection.

#### Test Scenarios

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

**ChecklistPage_Answer_Pass_SavesImmediately**: A name is entered and Pass is clicked; the answer is stored with the
name and the clock time, the button is pressed, and the progress counts it. This scenario is tested by
`ChecklistPage_Answer_Pass_SavesImmediately`.

**ChecklistPage_Answer_Fail_ShowsJustificationBoxAndRequiresText**: Fail is clicked; the justification box appears
with its required marker, saving it empty shows an error next to it, and nothing is stored. This scenario is tested
by `ChecklistPage_Answer_Fail_ShowsJustificationBoxAndRequiresText`.

**ChecklistPage_Answer_FailWithJustification_SavesAndShowsAnsweredBy**: Fail is clicked and a justification is
saved; the answer is stored, the text stays in the box, and "Answered by" shows the name and the UTC time. This
scenario is tested by `ChecklistPage_Answer_FailWithJustification_SavesAndShowsAnsweredBy`.

**ChecklistPage_Answer_NoName_ShowsNameError**: Pass is clicked without a name; the error next to the check says to
use the "Your name" field and nothing is stored. This scenario is tested by
`ChecklistPage_Answer_NoName_ShowsNameError`.

**ChecklistPage_Applicability_NotApplicableWithoutReason_ShowsError**: A section is switched to not applicable and
saved without a reason; an error is shown and the section is still applicable in the store. This scenario is tested
by `ChecklistPage_Applicability_NotApplicableWithoutReason_ShowsError`.

**ChecklistPage_Applicability_NotApplicableWithReason_CountsAsDoneAndHidesChecks**: A section is switched to not
applicable and saved with a reason; the section shows "Not applicable", its checks are hidden, and the totals
exclude its checks. This scenario is tested by
`ChecklistPage_Applicability_NotApplicableWithReason_CountsAsDoneAndHidesChecks`.

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
