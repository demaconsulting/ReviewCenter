### ReviewPage

#### Verification Approach

`ReviewPage` is verified with component tests defined in `ReviewPageTests.cs` and
`ReviewPageRepositoryTests.cs` and `ReviewPageDiffTests.cs`, which use the bUnit test context to
render the real component over real Core services and an in-memory fake store holding a review of known files.
The signed-in user is supplied by a test authentication state in `PageTestBase`: the user `ann` (display name "Ann")
unless a test signs in as someone else. The signing tests also create real users (with a password) through
`UserService`, and the page uses the real `SigningService` over the same fake store, so signatures and passwords are
checked by the same code as in production.
Tests locate elements by their `data-testid` attributes (the page units share the helper `PageTestBase`) and
compare the shown values with the known hashes and fingerprint. The real database is covered by the `Storage`
subsystem and the integration tests of the `Web` subsystem.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- An unknown review shows a not-found message and no details.
- A known review shows its title with an Approved or In review badge, its next-step line, lists every file with path
  and size, and keeps its identifier,
  state, the known fingerprint, the record digest (the first 16 characters shown and all 64 in the title), and the
  SHA-256 hash of each file in a Details section that is collapsed by
  default and comes after the files and the thread index; each file's View button has an accessible name that
  includes the file path.
- Reusing the page for another review shows the new review and clears the previously selected file.
- No content is shown until a file is selected; selecting a text file shows its lines numbered, with the exact
  text of each line as plain text; selecting another file replaces the content.
- Binary content, including text that is not valid UTF-8, shows a notice instead of content; an empty file shows
  an empty-file notice and no lines.
- With no threads the summary and every file count are zero; with threads the summary, the per-file open counts,
  the anchored-line markers, and the comments of the selected file are shown, and an empty-state message appears
  for a file without threads.
- The line Comment button fills the line fields; starting a thread anchors the whole file, a line range, or one
  line, shows the thread under the signed-in user and clears the text; every rule violation (missing text, line
  outside the file, line that is not a number, line range on a binary file) shows a message next to the form and
  keeps what was typed. The page has no field for an author.
- A reply is added to a thread under the signed-in user; a blank reply shows a message in that thread.
- Resolve and reopen change the thread state and the summary; the next-step line follows the threads and the
  unanswered checks of the checklist, asks for a reviewer slot when only that is missing, and says that the review is
  approved once every slot is signed.
- A review with a checklist shows a card with its progress and a link to the checklist page; a review without one
  shows no card.
- The Reviewers card is shown for every review. Without slots it says so and offers the add form, and the review is
  In review. A slot is added with its primary and alternate and listed as "Slot 1" with "Not signed"; a missing
  primary or the same user twice shows a message and adds nothing; an unsigned slot can be changed and removed. A
  slot that has a signature, current or out of date, offers neither change nor removal. A user-controlled display
  name is shown as plain text.
- A user who occupies a slot of a review that cannot be signed sees the blockers instead of the sign form; a user who
  does not occupy the slot sees neither. For a signable review the occupant sees the meaning text and a password
  field of type password.
- A wrong password shows only the generic message, stores no signature, and the typed password appears nowhere in
  the page; the right password records the signature, shows who signed and when, shows the review as Approved with
  the Approved next-step sentence, and removes the sign form and the slot controls. A change to the record after
  signing shows the signature as out of date and the review as In review, with the slot still locked and the sign
  form offered again. Signing a record that changed since the page was drawn is refused with a message and stores
  nothing.
- The thread index shows the open threads by default and can show all or only resolved threads, each row with file,
  anchor, state, number of comments, first author, and last activity, and an accessible name that states the same
  facts; choosing a row selects its file, highlights
   its lines and thread, expands a resolved thread, and asks the browser to scroll to the first line (the center of
  the window).
- Choosing a file asks the browser to bring the top of its code into view, and the line Comment button asks it to
  bring the new-thread form into view.
- A resolved thread is a one-line header that expands and collapses on request, an open thread is expanded, and
  resolving a thread collapses it.
- Thread and comment text is shown as plain text, never as markup, and every interactive control, including every
  control of the Reviewers card, has a `data-testid`.
- The interactive marker is `true` after the page has rendered on a live connection.
- When the user of the open page is disabled, the page is replaced by the "Session ended" message: its forms, the
  typed text, and the sign form are gone, and a stale submission of the thread form or the sign form stores nothing. A
  thread started after the session ended, before the page learned of it, shows an error that says the user is no
  longer signed in and stores nothing.
- A review from a repository shows the change kind of every file (an uploaded review shows none) and the repository,
  both commits, and the selection in the details; a deleted file is listed with its baseline size.
- A review planned from a review-set shows the identifier of the set in the details; other reviews show no such row.
- A modified file of a review with a baseline offers a Diff, a Head, and a Baseline view and opens on Diff; Head is
  the numbered view with comments, Baseline is the frozen text, read-only, without comment controls; an added file
  and a file of an uploaded review have no choice and show the head only; selecting another file starts in its
  default view; switching views keeps the typed text and clears the line selection.
- A deleted file opens on the Diff view of removed lines, offers Diff and Baseline but no Head, and accepts a
  whole-file comment; its Baseline view has a note and no line comment controls.
- The Diff view is laid out side by side, the baseline on the left and the head on the right: it shows the summary
  "x added, y removed", the hint "Click + to comment on a line.", each side's line numbers, an unchanged line on both
  sides, a removed run paired row by row with the added run that follows it with muted empty placeholders for the
  leftover cells, rows with their sign, and unchanged runs of more than three lines collapsed behind a "Show N
  unchanged lines" button that expands them; opening a file brings its first change into view.
- A file that is too large to compare shows "Too large to compare line by line." with the Head and the Baseline
  view offered; a binary file shows the binary notice; two versions that differ only in line endings are said to.
- The + control of the left cell starts a thread on the Baseline with the baseline line number (a removed row, or
  the baseline copy of an unchanged row); the control of the right cell starts it on the Head with the head line
  number; Shift-click and the "Through line" field extend the range within one side only; Cancel closes the form, and
  "Comment on the whole file instead" keeps the typed text.
- Threads are drawn under the row of the last line of their anchor across the full width, also where the lines would
  be collapsed, the cell of their side marked, a resolved thread as a collapsed header; reply, resolve, and reopen
  work there; the list below the diff holds only whole-file threads; the per-file thread counts include both sides.
- The thread index words the anchor as "Before: line N" or "After: lines N-M" for a file with a baseline, and
  choosing a row shows the diff at the thread (or the Baseline or Head view for a file that is too large).
- The stylesheet lays each row out as two columns, wraps long lines inside their cell, stacks the cells and hides the
  empty placeholders on a narrow screen, and keeps the gap buttons and inline threads in view, with a narrower gutter
  on a phone.

#### Test Scenarios

**Reviews from a repository** are verified in `ReviewPageRepositoryTests.cs` over a review stored with a source,
changed files, and baseline bytes.

**ReviewPage_Render_RepositoryReview_ShowsChangeKindPerFile**: Each file of a repository review shows Added,
Modified, or Deleted. This scenario is tested by `ReviewPage_Render_RepositoryReview_ShowsChangeKindPerFile`.

**ReviewPage_Render_RepositoryReview_DetailsShowRepositoryShasAndSelection**: The details show the repository name,
the baseline and head commits, and the selection. This scenario is tested by
`ReviewPage_Render_RepositoryReview_DetailsShowRepositoryShasAndSelection`, which also checks that a review that was not
planned shows no review-set row.

**ReviewPage_Render_PlannedReview_DetailsShowReviewSet**: A review created by the planner from the review-set `core`
shows the set identifier in the details, after the repository. This scenario is tested by
`ReviewPage_Render_PlannedReview_DetailsShowReviewSet`.

**ReviewPage_FileSelect_ModifiedFile_TogglesBetweenDiffHeadAndReadOnlyBaseline**: A modified file opens on the Diff
view; the Head view has comment controls, the Baseline view shows the baseline lines without comment controls, and
the Diff view returns. This scenario is tested by
`ReviewPage_FileSelect_ModifiedFile_TogglesBetweenDiffHeadAndReadOnlyBaseline`.

**ReviewPage_FileSelect_UploadAndAddedFile_KeepHeadOnlyView**: An added file and a file of an uploaded review show
the head with no view choice. This scenario is tested by `ReviewPage_FileSelect_UploadAndAddedFile_KeepHeadOnlyView`.

**ReviewPage_ViewSwitch_KeepsTextAndClearsTheSelection**: Switching between the views keeps the text typed in the
comment form and clears the selected line. This scenario is tested by
`ReviewPage_ViewSwitch_KeepsTextAndClearsTheSelection`.

**ReviewPage_FileSelect_AfterBaselineView_ShowsHeadOfTheNextFile**: After the baseline of one file was shown,
selecting another file shows its head view. This scenario is tested by
`ReviewPage_FileSelect_AfterBaselineView_ShowsHeadOfTheNextFile`.

**ReviewPage_FileSelect_DeletedFile_ShowsRemovedLinesAndAcceptsWholeFileComment**: A deleted file opens on the Diff
view with only removed lines, and a whole-file comment is accepted. This scenario is tested by
`ReviewPage_FileSelect_DeletedFile_ShowsRemovedLinesAndAcceptsWholeFileComment`.

**ReviewPage_FileSelect_DeletedFileBaselineView_ShowsNoteWithoutLineControls**: The Baseline view of a deleted file
shows a note and no line comment buttons. This scenario is tested by
`ReviewPage_FileSelect_DeletedFileBaselineView_ShowsNoteWithoutLineControls`.

**Diff view** scenarios are verified in `ReviewPageDiffTests.cs` over a modified file of twenty lines with one line
replaced and one line added.

**ReviewPage_FileSelect_ModifiedFile_ShowsDiffWithSummaryHintAndNumbers**: The Diff view shows "2 added, 1 removed",
the hint, the line numbers of each side (a removed row only a baseline number, an added row only a head number), and
the added and removed rows. This scenario is tested by
`ReviewPage_FileSelect_ModifiedFile_ShowsDiffWithSummaryHintAndNumbers`.

**ReviewPage_DiffView_Rows_AreSideBySideWithPlaceholders**: The removed line and the added line that replaces it
share one row, left and right; an unchanged line shows on both sides with its own ids; a line that is only added has
a muted, hidden-from-assistive-technology empty placeholder on the left. This scenario is tested by
`ReviewPage_DiffView_Rows_AreSideBySideWithPlaceholders`.

**ReviewPage_FileSelect_ModifiedFile_ScrollsToFirstChange**: Opening a modified file whose first change follows
collapsed lines asks the browser to scroll that row to the middle of the window (the bUnit stand-in records the
call). This scenario is tested by `ReviewPage_FileSelect_ModifiedFile_ScrollsToFirstChange`.

**ReviewPage_DiffView_UnchangedRuns_AreCollapsedAndExpandOnRequest**: Unchanged runs far from a change show
"Show N unchanged lines", and the button expands the run. This scenario is tested by
`ReviewPage_DiffView_UnchangedRuns_AreCollapsedAndExpandOnRequest`.

**ReviewPage_DiffView_OnlyLineEndingsDiffer_SaysSo**: Two versions that differ only in line endings show a sentence
saying so. This scenario is tested by `ReviewPage_DiffView_OnlyLineEndingsDiffer_SaysSo`.

**ReviewPage_FileSelect_TooLargeFile_OffersHeadAndBaseline**: A file over the comparison limit shows "Too large to
compare line by line." and the Head and Baseline buttons. This scenario is tested by
`ReviewPage_FileSelect_TooLargeFile_OffersHeadAndBaseline`.

**ReviewPage_FileSelect_BinaryFile_ShowsNotice**: A binary file shows the binary notice instead of rows. This
scenario is tested by `ReviewPage_FileSelect_BinaryFile_ShowsNotice`.

**ReviewPage_DiffView_CommentOnRemovedLine_StartsBaselineThread**: The + control of a removed row (left cell) opens
the form worded "Comment on line 10 (before)" and starts a thread on the Baseline with the baseline line number. This
scenario is tested by `ReviewPage_DiffView_CommentOnRemovedLine_StartsBaselineThread`.

**ReviewPage_DiffView_CommentOnAddedAndContextLines_StartsHeadThreads**: The + control of an added row or of the right
cell of an unchanged row starts a thread on the Head with the head line number. This scenario is tested by
`ReviewPage_DiffView_CommentOnAddedAndContextLines_StartsHeadThreads`.

**ReviewPage_DiffView_LeftControlIsBaseline_RightControlIsHead**: The + control in the left cell of an unchanged row
starts a Baseline thread with the baseline number, shown under the row and marking only the left cell; the control in
the right cell of the same row starts a Head thread. This scenario is tested by
`ReviewPage_DiffView_LeftControlIsBaseline_RightControlIsHead`.

**ReviewPage_DiffView_ShiftClick_ExtendsOnlyWithinTheSameSide**: Shift-click extends the selection on the same side
and starts a new selection on the other side. This scenario is tested by
`ReviewPage_DiffView_ShiftClick_ExtendsOnlyWithinTheSameSide`.

**ReviewPage_DiffView_ThroughLineField_ExtendsTheRange**: The "Through line" field turns the comment into a range.
This scenario is tested by `ReviewPage_DiffView_ThroughLineField_ExtendsTheRange`.

**ReviewPage_DiffView_CancelAndWholeFileOption_BehaveAsLabelled**: Cancel closes the form, and "Comment on the whole
file instead" keeps the text and anchors the whole file. This scenario is tested by
`ReviewPage_DiffView_CancelAndWholeFileOption_BehaveAsLabelled`.

**ReviewPage_DiffView_ExistingThreads_AreShownUnderTheirLines**: Threads of both sides are drawn under their lines,
a resolved one collapsed, even where the lines would be collapsed. This scenario is tested by
`ReviewPage_DiffView_ExistingThreads_AreShownUnderTheirLines`.

**ReviewPage_DiffView_InlineThread_ReplyResolveAndReopen**: A thread in the diff can be answered, resolved, and
reopened. This scenario is tested by `ReviewPage_DiffView_InlineThread_ReplyResolveAndReopen`.

**ReviewPage_ThreadIndex_ShowsSideAndLinesAndJumpsToTheDiff**: Index rows say "Before" or "After" with the lines,
and choosing one shows the diff at the thread. This scenario is tested by
`ReviewPage_ThreadIndex_ShowsSideAndLinesAndJumpsToTheDiff`.

**ReviewPage_ThreadIndex_TooLargeFile_OpensTheVersionOfTheThread**: For a file that is too large to compare,
choosing a row opens the Baseline or the Head view by the side of the thread. This scenario is tested by
`ReviewPage_ThreadIndex_TooLargeFile_OpensTheVersionOfTheThread`.

**ReviewPage_FileList_ThreadCounts_IncludeBothSides**: The open thread count of a file in the list counts threads
on both sides. This scenario is tested by `ReviewPage_FileList_ThreadCounts_IncludeBothSides`.

**ReviewPage_Render_DeletedFile_ListsBaselineSize**: A deleted file is listed with the size of its baseline. This
scenario is tested by `ReviewPage_Render_DeletedFile_ListsBaselineSize`.

**ReviewPage_Render_UnknownReview_ShowsNotFound**: The page is rendered for an unknown review; the not-found
message names the identifier and no fingerprint is shown. This scenario is tested by
`ReviewPage_Render_UnknownReview_ShowsNotFound`.

**ReviewPage_Render_ExistingReview_ShowsIdTitleStateAndFingerprint**: The page is rendered for a review; the
identifier, title, state "Draft", and the known fingerprint are shown. This scenario is tested by
`ReviewPage_Render_ExistingReview_ShowsIdTitleStateAndFingerprint`.

**ReviewPage_Render_ExistingReview_ListsFilesWithPathSizeAndHash**: Every file is listed with its path and size, and
its known SHA-256 hash is shown in the Details section. This scenario is tested by
`ReviewPage_Render_ExistingReview_ListsFilesWithPathSizeAndHash`.

**ReviewPage_Render_ExistingReview_FilesFirstAndDetailsCollapsedHoldMetadata**: The page is rendered; the next-step
line, the files, and the thread index come before the Details section, which has no `open` attribute and holds the
identifier, state, fingerprint, and file hashes, while the file list holds no hash. This scenario is tested by
`ReviewPage_Render_ExistingReview_FilesFirstAndDetailsCollapsedHoldMetadata`.

**ReviewPage_Render_NextStep_FollowsThreadState**: With no threads the line names the 58 unanswered checks; after a
thread is started it names the checks and one open thread to resolve in one sentence; after the thread is resolved
only the checks are named again. This scenario is tested by `ReviewPage_Render_NextStep_FollowsThreadState`.

**ReviewPage_Render_NextStep_IncludesChecklistProgress**: A check is answered and a section is marked not
applicable; the line counts the remaining unanswered checks (52). When everything that applies is answered and the
thread is resolved, no reviewer slot exists and the line is "Add a reviewer slot.". This scenario is tested by
`ReviewPage_Render_NextStep_IncludesChecklistProgress`.

**ReviewPage_Render_NewReview_ShowsChecklistProgressAndLink**: A review with two answered checks is rendered; the
checklist card shows "2 of 58 checks answered" and a link to the checklist page of the review. This scenario is
tested by `ReviewPage_Render_NewReview_ShowsChecklistProgressAndLink`.

**ReviewPage_Render_ReviewWithoutChecklist_HidesChecklistCard**: A review stored without a checklist is rendered;
there is no checklist card, and the next-step line does not mention the checklist. This scenario is tested by
`ReviewPage_Render_ReviewWithoutChecklist_HidesChecklistCard`.

**ReviewPage_ThreadIndex_Filters_ShowRowsWithFileAnchorStateCountAuthorAndActivity**: With no threads the index
says there are no open threads; with an open thread (with a reply) and a resolved thread, the default shows only
the open row with file, anchor, state, comment count, first author, and activity time; All shows both with the
counts on the filter buttons; Resolved shows only the resolved row. This scenario is tested by
`ReviewPage_ThreadIndex_Filters_ShowRowsWithFileAnchorStateCountAuthorAndActivity`.

**ReviewPage_ThreadIndex_RowClick_SelectsFileHighlightsAnchorAndScrolls**: With another file shown, a resolved
thread is chosen in the index; its file is shown, its lines and thread card are highlighted, the thread is
expanded, and the browser scroll function is last called for its first line (a bUnit stand-in records the calls);
choosing the first file had asked it to scroll to the top of the code.
This scenario is tested by `ReviewPage_ThreadIndex_RowClick_SelectsFileHighlightsAnchorAndScrolls`.

**ReviewPage_ResolvedThread_CollapsedHeaderExpandsOnRequest**: A resolved thread shows only anchor, state, and
comment count; Expand shows its comments and Reopen, Collapse hides them again; an open thread is always expanded
and collapses when resolved. This scenario is tested by `ReviewPage_ResolvedThread_CollapsedHeaderExpandsOnRequest`.

**ReviewPage_Render_FileSelected_ShowsProjectNameAndHighlightsRow**: A file is selected; the breadcrumb link to
the owning project is labeled with the project name and only the selected file's row is highlighted. This scenario
is tested by `ReviewPage_Render_FileSelected_ShowsProjectNameAndHighlightsRow`.

**ReviewPage_Render_NoFileSelected_ShowsNoContent**: Before a file is selected neither content nor the binary
notice is shown. This scenario is tested by `ReviewPage_Render_NoFileSelected_ShowsNoContent`.

**ReviewPage_FileSelect_TextFile_ShowsNumberedLinesWithExactText**: A text file is selected; its lines are shown
with numbers and their exact text, with the selected path, and no binary notice. This scenario is tested by
`ReviewPage_FileSelect_TextFile_ShowsNumberedLinesWithExactText`.

**ReviewPage_FileSelect_BinaryFile_ShowsBinaryNoticeAndNoContent**: A binary file is selected; the binary notice
is shown and no content. This scenario is tested by `ReviewPage_FileSelect_BinaryFile_ShowsBinaryNoticeAndNoContent`.

**ReviewPage_FileSelect_InvalidUtf8_ShowsBinaryNotice**: A file that is not valid UTF-8 and has no zero byte is
selected; the binary notice is shown. This scenario is tested by `ReviewPage_FileSelect_InvalidUtf8_ShowsBinaryNotice`.

**ReviewPage_FileSelect_EmptyFile_ShowsNoLines**: An empty file is selected; the empty-file notice is shown, and no
lines and no binary notice. This scenario is tested by `ReviewPage_FileSelect_EmptyFile_ShowsNoLines`.

**ReviewPage_FileSelect_SecondFile_ReplacesContent**: A second file is selected; its content and path replace the
first. This scenario is tested by `ReviewPage_FileSelect_SecondFile_ReplacesContent`.

**ReviewPage_Render_ExistingReview_FileButtonsNameTheirFile**: Every file's View button has an `aria-label` of
`View` followed by its path. This scenario is tested by `ReviewPage_Render_ExistingReview_FileButtonsNameTheirFile`.

**ReviewPage_ParametersChange_OtherReview_ShowsNewReviewAndClearsSelection**: A file is selected, then the page is
reused for another review of the project; the other review is shown and no file content remains. This scenario is
tested by `ReviewPage_ParametersChange_OtherReview_ShowsNewReviewAndClearsSelection`.

**ReviewPage_Render_NoThreads_ShowsZeroSummaryAndZeroFileCounts**: A review without threads shows zero open and
resolved threads and "0 open threads" for every file. This scenario is tested by
`ReviewPage_Render_NoThreads_ShowsZeroSummaryAndZeroFileCounts`.

**ReviewPage_FileSelect_ThreadsExist_ShowsSummaryCountsMarkersAndComments**: A review with open and resolved
threads on two files; the summary, the per-file counts, the anchor labels, the anchored-line markers, and the
comments with author, time, and text are shown for the selected file. This scenario is tested by
`ReviewPage_FileSelect_ThreadsExist_ShowsSummaryCountsMarkersAndComments`.

**ReviewPage_FileSelect_NoThreadsOnFile_ShowsEmptyMessage**: A file without threads shows an empty-state message.
This scenario is tested by `ReviewPage_FileSelect_NoThreadsOnFile_ShowsEmptyMessage`.

**ReviewPage_LineCommentButton_Click_FillsStartAndEndLine**: A line's Comment button fills both line fields with
that line and the target text names it. This scenario is tested by
`ReviewPage_LineCommentButton_Click_FillsStartAndEndLine`.

**ReviewPage_StartThread_WholeFile_CreatesThreadAndClearsText**: A thread with blank line fields is anchored to the
whole file and created by the signed-in user with the text only, with no author field on the page; it is shown, and
the text is cleared. This scenario is tested by
`ReviewPage_StartThread_WholeFile_CreatesThreadAndClearsText`.

**ReviewPage_StartThread_LineRangeAndSingleLine_AnchorLines**: A range and a single line are stored with the
entered anchors and shown with the matching labels. This scenario is tested by
`ReviewPage_StartThread_LineRangeAndSingleLine_AnchorLines`.

**ReviewPage_StartThread_InvalidInput_ShowsErrorAndKeepsValues**: Missing text, a line outside the
file, and a line that is not a number each show a message next to the form, create no thread, and keep what was
typed. This scenario is tested by `ReviewPage_StartThread_InvalidInput_ShowsErrorAndKeepsValues`.

**ReviewPage_StartThread_BinaryFile_WholeFileAllowedAndRangeRefused**: On a binary file a whole-file thread is
accepted and a line range is refused. This scenario is tested by
`ReviewPage_StartThread_BinaryFile_WholeFileAllowedAndRangeRefused`.

**ReviewPage_Reply_ValidAndBlank_AppendsCommentOrShowsErrorInThread**: A valid reply is appended to the thread under
the signed-in user, whose user name is stored with it; a
blank reply shows a message in the thread. This scenario is tested by
`ReviewPage_Reply_ValidAndBlank_AppendsCommentOrShowsErrorInThread`.

**ReviewPage_Reply_NoAuthorField_AuthorCannotBeChosen**: With another user ("bob") signed in, a reply is posted to a
thread of someone else; the page has no field for a name and no "Your name" text, and the new comment is shown with
the display name of the signed-in user. This scenario is tested by
`ReviewPage_Reply_NoAuthorField_AuthorCannotBeChosen`.

**ReviewPage_ResolveAndReopen_Toggle_ChangesStateAndSummary**: Resolving an open thread (which collapses it) and,
after expanding it, reopening it changes the state label, the button label, and the summary counts. This scenario is
tested by `ReviewPage_ResolveAndReopen_Toggle_ChangesStateAndSummary`.

**ReviewPage_Render_ThreadText_IsShownAsPlainText**: Markup in a comment is shown as text and creates no element.
This scenario is tested by `ReviewPage_Render_ThreadText_IsShownAsPlainText`.

**ReviewPage_Render_ThreadControls_EveryInteractiveElementHasTestId**: Every button, input, and text area of the
thread controls and the thread index (with an open and a resolved thread) has a `data-testid`. This scenario is
tested by `ReviewPage_Render_ThreadControls_EveryInteractiveElementHasTestId`.

**ReviewPage_Render_NoSlots_ShowsEmptyReviewersCardAndInReviewBadge**: A review without slots is rendered; the
Reviewers card, its empty message, and the add form are shown, and the badge reads "In review". This scenario is
tested by `ReviewPage_Render_NoSlots_ShowsEmptyReviewersCardAndInReviewBadge`.

**ReviewPage_AddSlot_PrimaryAndAlternate_ShowsSlotNotSigned**: A primary and an alternate are chosen and the add
form is submitted; the slot is listed as "Slot 1" with both names and "Not signed", it is stored, and the empty
message is gone. This scenario is tested by `ReviewPage_AddSlot_PrimaryAndAlternate_ShowsSlotNotSigned`.

**ReviewPage_AddSlot_InvalidChoices_ShowsErrorAndAddsNothing**: The add form is submitted without a primary, and
then with the same user as primary and alternate; each time a message is shown and no slot is stored. This scenario
is tested by `ReviewPage_AddSlot_InvalidChoices_ShowsErrorAndAddsNothing`.

**ReviewPage_EditAndRemoveSlot_Unsigned_ChangesAndRemoves**: The primary of an unsigned slot is changed and its
alternate dropped, and Save shows the change ("None" for the alternate); Remove then removes the slot and the empty
message returns. This scenario is tested by `ReviewPage_EditAndRemoveSlot_Unsigned_ChangesAndRemoves`.

**ReviewPage_Render_BlockedReview_ShowsBlockersInsteadOfSignForm**: A slot is rendered for a review with unanswered
checks and an open thread; its primary sees the blockers (naming both) and no sign form, and a user who is not in
the slot sees neither. This scenario is tested by `ReviewPage_Render_BlockedReview_ShowsBlockersInsteadOfSignForm`.

**ReviewPage_Render_SignableReview_NonOccupantSeesNoSignForm**: A signable review is rendered for a user who is not
in the slot; there is no sign form and no Sign button. This scenario is tested by
`ReviewPage_Render_SignableReview_NonOccupantSeesNoSignForm`.

**ReviewPage_Sign_WrongPassword_ShowsGenericErrorAndKeepsNoPassword**: The occupant sees the meaning text and a
password field of type password, types a wrong password, and signs; the generic message "The password is
incorrect." is shown, the slot is still "Not signed", no signature is stored, and neither the typed value nor a
field value is in the markup. This scenario is tested by
`ReviewPage_Sign_WrongPassword_ShowsGenericErrorAndKeepsNoPassword`.

**ReviewPage_Sign_CorrectPassword_ApprovesReviewAndLocksSlot**: The occupant signs with the right password; the slot
shows "Signed by Ann on" the time, the badge reads "Approved", the next step is "Approved. Every reviewer slot has a
current signature.", the password is nowhere in the markup, the Save, Remove, and sign controls are gone, and the
stored signature belongs to the user in the primary role. This scenario is tested by
`ReviewPage_Sign_CorrectPassword_ApprovesReviewAndLocksSlot`.

**ReviewPage_Render_RecordChangedAfterSigning_ShowsOutOfDateAndInReview**: A slot is signed, a thread is then
started and resolved through the service, and the page is rendered again; the slot shows "Signature out of date", the
badge reads "In review", the slot has no Save or Remove, and the sign form is offered again. This scenario is tested
by `ReviewPage_Render_RecordChangedAfterSigning_ShowsOutOfDateAndInReview`.

**ReviewPage_Sign_RecordChangedSincePageDrawn_IsRefused**: A signable page is drawn, the record is changed through
the service, and the occupant signs with the right password; the message says the record changed and no signature is
stored. This scenario is tested by `ReviewPage_Sign_RecordChangedSincePageDrawn_IsRefused`.

**ReviewPage_Render_Details_ShowShortDigestWithFullValueInTitle**: Inside the Details element, the record digest
shows its first 16 characters, and its title holds the full 64-character digest computed by the service. This
scenario is tested by `ReviewPage_Render_Details_ShowShortDigestWithFullValueInTitle`.

**ReviewPage_Render_SlotDisplayName_IsShownAsPlainText**: A user whose display name is `<b>Eve</b>` is the primary
of a slot; the card shows the literal text and creates no element. This scenario is tested by
`ReviewPage_Render_SlotDisplayName_IsShownAsPlainText`.

**ReviewPage_Render_ReviewersCard_EveryInteractiveElementHasTestId**: With a slot ready to sign (its sign error
shown), a second slot, and an add-form error, every link, button, input, select, and text area inside the Reviewers
card has a `data-testid`. This scenario is tested by
`ReviewPage_Render_ReviewersCard_EveryInteractiveElementHasTestId`.

**ReviewPage_AfterRender_Interactive_SetsMarker**: The page is rendered; its root element has `data-interactive`
equal to `true`. This scenario is tested by `ReviewPage_AfterRender_Interactive_SetsMarker`.

**ReviewPage_Revalidation_UserDisabled_PageStopsShowingAndAcceptingActions**: A signable review is open for a user
whose sign-in state is re-checked every 100 milliseconds, with a file selected and text typed. The user is disabled;
within the limit of 10 seconds the "Session ended" message appears, and the page, the thread form, the sign form and
button, and the typed text are gone. Submitting the stale thread form and sign form stores no thread and no signature.
This scenario is tested by `ReviewPage_Revalidation_UserDisabled_PageStopsShowingAndAcceptingActions`.

**ReviewPage_StartThread_SessionEndedBeforePageKnows_ShowsClearErrorAndStoresNothing**: A file is selected and text is
typed, and the sign-in state ends without the page being told; the thread form is submitted. The thread error says
that the user is no longer signed in, and no thread exists. This scenario is tested by
`ReviewPage_StartThread_SessionEndedBeforePageKnows_ShowsClearErrorAndStoresNothing`.

**ReviewPage_ResolveAndReopen_SessionEndedBeforePageKnows_ShowsClearErrorAndChangesNothing**: An open thread and a
resolved thread are shown, and the sign-in state ends without the page being told; resolving the first and reopening
the second are both attempted. Each thread shows the message that the user is no longer signed in, and both threads
keep their stored state, so the approval state cannot be changed. This scenario is tested by
`ReviewPage_ResolveAndReopen_SessionEndedBeforePageKnows_ShowsClearErrorAndChangesNothing`.

**ReviewPage_Render_ExistingReview_ShowsHistoryLink**: A review is rendered; the page contains the history link
that points to the history page of the review. This scenario is tested by
`ReviewPage_Render_ExistingReview_ShowsHistoryLink`.
