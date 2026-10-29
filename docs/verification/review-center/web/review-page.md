### ReviewPage

#### Verification Approach

`ReviewPage` is verified with component tests defined in `ReviewPageTests.cs` and
`ReviewPageRepositoryTests.cs` and `ReviewPageDiffTests.cs`, which use the bUnit test context to
render the real component over real Core services and an in-memory fake store holding a review of known files.
The signed-in user is supplied by a test authentication state in `PageTestBase`: the user `ann` (display name "Ann")
unless a test signs in as someone else. The signing tests also create real users (with a password) through
`UserService`, and the page uses the real `SigningService` over the same fake store, so signatures and passwords are
checked by the same code as in production. The one scenario of an identity-provider user, which needs the provider
flow, is not a component test: it starts the real server pipeline with a fake identity provider
(see the `OidcSignIn` verification) and requests the page over HTTP.
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
- A review with a checklist shows its progress, a progress bar and a link to the checklist page in the summary strip; a
  review without one shows no checklist cell.
- The summary strip at the top of the page shows, besides the checklist, the reviewer state ("No reviewer slots yet"
  or how many slots are signed) and the number of open and of resolved threads.
- The Reviewers card is shown for every review. Without slots it says so and offers the add form, and the review is
  In review. A slot is added with its primary and alternate and listed as "Slot 1" with "Not signed"; a missing
  primary or the same user twice shows a message and adds nothing; an unsigned slot can be changed and removed. A
  slot that has a signature, current or out of date, offers neither change nor removal. A user-controlled display
  name is shown as plain text.
- An assignee of a slot who is no longer a member of the project is marked with "No longer a project member", and the
  slot shows the hint that such a reviewer cannot sign until added again; assignees who are members, in the same slot
  or another, get no marker.
- A user who occupies a slot of a review that cannot be signed sees the blockers instead of the sign form; a user who
  does not occupy the slot sees neither. For a signable review the occupant sees the meaning text and a password
  field of type password.
- A wrong password shows only the generic message, stores no signature, and the typed password appears nowhere in
  the page; the right password records the signature, shows who signed and when, shows the review as Approved with
  the Approved next-step sentence, and removes the sign form and the slot controls. A change to the record after
  signing shows the signature as out of date and the review as In review, with the slot still locked and the sign
  nothing.
- A reviewer who signs in with an identity provider sees, instead of the password field, a button to confirm the
  identity at the provider, whose form posts to the confirmation endpoint, and no Sign form and no password field; a
  failed confirmation is shown next to the button; after a confirmation the page shows the Sign button and a note that
  the identity is confirmed, and no confirmation button.
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
- A file of a review with a baseline commit opens on a Diff view and offers: for a modified file Diff, Head and
  Baseline; for an added file Diff and Head but no Baseline (the diff shows every line added with an empty left
  side); for a deleted file Diff and Baseline but no Head. Head is the numbered view with comments, Baseline is the
  frozen text, read-only, without comment controls; a file of an uploaded review, or of a review without a baseline
  commit, has no choice and shows the head only; selecting another file starts in its default view; switching views
  keeps the typed text and clears the line selection.
- A deleted file opens on the Diff view of removed lines, offers Diff and Baseline but no Head, and accepts a
  whole-file comment; its Baseline view has a note and no line comment controls.
- The Diff view is laid out side by side, the baseline on the left and the head on the right: it shows the summary
  "x added, y removed", the hint "Click + to comment on a line.", each side's line numbers, an unchanged line on both
  sides, a removed run paired row by row with the added run that follows it with muted empty placeholders for the
  leftover cells, rows with their sign, and unchanged runs of more than three lines collapsed behind a "Show N
  unchanged lines" button that expands them; opening a file brings its first change into view.
- A file that is too large to compare shows "Too large to compare line by line." with the Head and the Baseline
  view offered as the file has them (an added file offers Head only); a binary file shows the binary notice; two
  versions that differ only in line endings are said to.
- The + control of the left cell starts a thread on the Baseline with the baseline line number (a removed row, or
  the baseline copy of an unchanged row); the control of the right cell starts it on the Head with the head line
  number; Shift-click and the "Through line" field extend the range within one side only; Cancel closes the form, and
  "Comment on the whole file instead" keeps the typed text.
- Threads are drawn under the row of the last line of their anchor across the full width, also where the lines would
  be collapsed, the cell of their side marked, a resolved thread as a collapsed header; reply, resolve, and reopen
  work there; the list below the diff holds only whole-file threads; the per-file thread counts include both sides.
- The thread index words the anchor as "Before: line N" or "After: lines N-M" for every file of a review with a
  baseline commit, an added file included, and
  choosing a row shows the diff at the thread (or the Baseline or Head view for a file that is too large).
- The stylesheet lays each row out as two columns, wraps long lines inside their cell, stacks the cells and hides the
  empty placeholders on a narrow screen, and keeps the gap buttons and inline threads in view, with a narrower gutter
  on a phone.
- The file, line and thread addresses open the review page at that place with the highlight and the scroll request;
  an address that names nothing gives a message with a link back to the file list; choosing a file or a thread asks
  the browser to replace its address by the matching link without navigating; every thread has a plain,
  unobtrusive link (not styled as a button); links
  from `ReviewLinks` return the same file path through the router, whatever its characters.
- An approved review offers "Download attestation (PDF)", whose address is the attestation address of `ReviewLinks`
  and whose file name follows the project and the review, with a note that a self-signed key is not an organization key
  (generated by the server for the `SelfSigned` provider, supplied by the administrator from the certificate store
  otherwise; or the subject and thumbprint start of the certificate of an organization key) and a link to the help
  topic; a key
  that cannot be used shows the reason as an alert instead of the download; a review that is not approved shows
  nothing about the attestation; and the download appears as soon as the last signature is made through the page.
- A signer who has a second factor sees a code field (that asks for a one-time code) next to the password field, and a
  signer who has none sees no such field; the password and a fresh code sign and both fields are empty afterwards; a
  missing, wrong, recovery, or already used code shows the one message that names the password and the code, signs
  nothing, and empties both fields; a code used within its step is refused until the next code.

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

**ReviewPage_FileSelect_UploadAndNoBaselineReview_KeepHeadOnlyView**: A file of an uploaded review and a file of a
repository review created without a baseline revision show the head with no view choice and no diff. This scenario
is tested by `ReviewPage_FileSelect_UploadAndNoBaselineReview_KeepHeadOnlyView`.

**ReviewPage_FileSelect_AddedFileOfReviewWithBaseline_OpensAsAllAddedDiffWithDiffAndHeadChoice**: An added file of a
review with a baseline commit opens on the Diff view with every line added, an empty left placeholder per row and
no old line numbers, and offers Diff and Head but no Baseline. This scenario is tested by
`ReviewPage_FileSelect_AddedFileOfReviewWithBaseline_OpensAsAllAddedDiffWithDiffAndHeadChoice`.

**ReviewPage_FileSelect_AddedFile_HeadViewAndBack_ShowsNumberedLinesAndCommentControls**: The Head view of an added
file shows the numbered lines with comment controls and the form, and the Diff view returns, with no Baseline
choice at any point. This scenario is tested by
`ReviewPage_FileSelect_AddedFile_HeadViewAndBack_ShowsNumberedLinesAndCommentControls`.

**ReviewPage_DiffView_AddedFile_CommentOnLine_StartsHeadThreadUnderTheRow**: The + control of a line of an added
file's diff starts a Head thread on that line, shown under the row as "After: line 2" with its link, and no control
for the Baseline exists. This scenario is tested by
`ReviewPage_DiffView_AddedFile_CommentOnLine_StartsHeadThreadUnderTheRow`.

**ReviewPage_FileSelect_AddedFileTooLarge_OffersHeadOnly**: An added file with more lines than a diff accepts says
so and offers the Head version only; the Head version shows its lines. This scenario is tested by
`ReviewPage_FileSelect_AddedFileTooLarge_OffersHeadOnly`.

**ReviewPage_FileSelect_AddedBinaryFile_ShowsNoticeAndOffersHeadOnly**: An added binary file shows the binary
notice, no diff and no Baseline choice. This scenario is tested by
`ReviewPage_FileSelect_AddedBinaryFile_ShowsNoticeAndOffersHeadOnly`.

**ReviewPage_FileSelect_EmptyAddedFile_ShowsDiffWithoutRows**: An empty added file shows a diff that says "No
changes." without rows, and the whole-file comment form is offered. This scenario is tested by
`ReviewPage_FileSelect_EmptyAddedFile_ShowsDiffWithoutRows`.

**ReviewPage_ThreadIndex_AddedFileHeadThread_JumpsToTheDiffRow**: A head thread of an added file is worded "After:
line 2" in the index, and choosing it opens the Diff view with that row focused and scrolled to. This scenario is
tested by `ReviewPage_ThreadIndex_AddedFileHeadThread_JumpsToTheDiffRow`.

**ReviewPage_ViewSwitch_KeepsTextAndClearsTheSelection**: Switching between the views keeps the text typed in the
comment form and clears the selected line. This scenario is tested by
`ReviewPage_ViewSwitch_KeepsTextAndClearsTheSelection`.

**ReviewPage_FileSelect_AfterBaselineView_ShowsHeadOfTheNextFile**: After the baseline of one file was shown,
selecting another file shows that file in its default view (the Diff view of an added file), not a baseline view.
This scenario is tested by
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

**ReviewPage_DiffView_MultiLineThread_KeepsItsFirstLineVisible**: A thread over several lines keeps its first line
visible even when that line would be collapsed, so a scroll to the first line can succeed. This scenario is tested by
`ReviewPage_DiffView_MultiLineThread_KeepsItsFirstLineVisible`.

**ReviewPage_DiffView_InlineThread_ReplyResolveAndReopen**: A thread in the diff can be answered, resolved, and
reopened. This scenario is tested by `ReviewPage_DiffView_InlineThread_ReplyResolveAndReopen`.

**ReviewPage_ThreadIndex_ShowsSideAndLinesAndJumpsToTheDiff**: Index rows say "Before" or "After" with the lines,
and choosing one shows the diff at the thread. This scenario is tested by
`ReviewPage_ThreadIndex_ShowsSideAndLinesAndJumpsToTheDiff`.

**ReviewPage_ThreadIndex_TooLargeFile_OpensTheVersionOfTheThread**: For a file that is too large to compare,
choosing a row opens the Baseline or the Head view by the side of the thread. This scenario is tested by
`ReviewPage_ThreadIndex_TooLargeFile_OpensTheVersionOfTheThread`.

**ReviewPage_ThreadIndex_BaselineThreadOnTooLargeFile_HighlightsItsBaselineLines**: Choosing a baseline thread over
three lines of a file too large to compare highlights exactly those lines of the Baseline text, and choosing a head
thread highlights its line of the Head text. This scenario is tested by
`ReviewPage_ThreadIndex_BaselineThreadOnTooLargeFile_HighlightsItsBaselineLines`.

**ReviewPage_ThreadIndex_ThreadHighlight_StaysOnTheSideOfTheThread**: With a baseline thread chosen, the Head line of
the same number is not highlighted, and with a head thread chosen the Baseline line is not. This scenario is tested by
`ReviewPage_ThreadIndex_ThreadHighlight_StaysOnTheSideOfTheThread`.

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
checklist cell of the summary strip shows "2 of 58 checks answered" and a link to the checklist page of the review.
This scenario is tested by `ReviewPage_Render_NewReview_ShowsChecklistProgressAndLink`.

**ReviewPage_Render_ReviewWithoutChecklist_HidesChecklistCard**: A review stored without a checklist is rendered;
there is no checklist cell in the summary strip, and the next-step line does not mention the checklist. This scenario
is tested by `ReviewPage_Render_ReviewWithoutChecklist_HidesChecklistCard`.

**ReviewPage_Render_ExistingReview_ShowsSummaryStripWithChecklistReviewersAndThreads**: A review without answers,
slots or threads is rendered; the summary strip contains the checklist progress and a link to the checklist page of
the review, the text "No reviewer slots yet", and the thread counts "0 open" and "0 resolved". This scenario is
tested by `ReviewPage_Render_ExistingReview_ShowsSummaryStripWithChecklistReviewersAndThreads`.

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

**ReviewPage_Render_AssigneesNoLongerMembers_ShowsIndicatorOnlyForThem**: A slot has ann as primary and bob as
alternate, and the page is rendered while both are members: no marker is shown. After bob left the project only the
alternate carries "No longer a project member"; after ann left too, both do, and the hint of the slot is shown. This
scenario is tested by `ReviewPage_Render_AssigneesNoLongerMembers_ShowsIndicatorOnlyForThem`.

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

**OidcReauth_ReviewPage_OffersConfirmationThenSigning**: Through the real pipeline with a fake identity provider (the
test is in `OidcReauthTests.cs`), a reviewer who signs in at the provider and occupies a slot opens the review page.
Before the confirmation the page offers the button "Confirm identity with" and the provider name, whose form posts to
`/auth/oidc/reauth`, and has no password field and no Sign form; after `reauth=failed` the general failure is shown
next to the button, and after `reauth=bogus` nothing; after the confirmation at the provider the page shows the Sign
button and the "Identity confirmed" note and no
confirmation button. This scenario is tested by `OidcReauth_ReviewPage_OffersConfirmationThenSigning`.

**OidcReauth_MissingOrStaleAuthTime_ReviewPageNamesTheCause**: Through the real pipeline with a fake identity
provider, a confirmation whose identity token has no `auth_time` returns to the review page, which says that the
provider did not report when the person signed in, names `auth_time` and an administrator, and does not show the
general text; a confirmation whose `auth_time` is 600 seconds old says that the sign-in was not recent enough, does
not name `auth_time`, and does not show the general text. This scenario is tested by
`OidcReauth_MissingOrStaleAuthTime_ReviewPageNamesTheCause`.

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

**ReviewPage_StartThread_SessionEndedBeforePageKnows_ShowsNotFoundAndStoresNothing**: A file is selected and text is
typed, and the sign-in state ends without the page being told; the thread form is submitted. The access check that
starts the handler finds no signed-in user, so the not-found message replaces the page, the thread form is gone, and
no thread exists. This scenario is tested by
`ReviewPage_StartThread_SessionEndedBeforePageKnows_ShowsNotFoundAndStoresNothing`.

**ReviewPage_ResolveAndReopen_SessionEndedBeforePageKnows_ShowsNotFoundAndChangesNothing**: An open thread and a
resolved thread are shown, and the sign-in state ends without the page being told; resolving the open thread is
attempted. The not-found message replaces the page, no thread control remains, and both threads keep their stored
state, so the approval state cannot be changed. This scenario is tested by
`ReviewPage_ResolveAndReopen_SessionEndedBeforePageKnows_ShowsNotFoundAndChangesNothing`.

**ReviewPage_Render_NonMember_LooksLikeAMissingReview**: A review with distinctive file content, a comment, a
reviewer name and a checklist answer is opened by a user who is no member of the project. The not-found text equals the
text for a project that does not exist (apart from the project number), and none of the project name, review title,
file names, file content, comment, reviewer name or checklist progress is in the markup. This scenario is tested by
`ReviewPage_Render_NonMember_LooksLikeAMissingReview`.

**ReviewPage_SelectFile_MembershipRevoked_ShowsNotFoundAndNoData**: A member has the review open, and the membership is
removed in the store; choosing a file shows the not-found message, and nothing that the page had loaded (names, file
content, comments, reviewer name, checklist progress) remains in the markup. This scenario is tested by
`ReviewPage_SelectFile_MembershipRevoked_ShowsNotFoundAndNoData`.

**ReviewPage_SelectThread_MembershipRevoked_ShowsNotFoundAndNoData**: The same arrangement, and a thread of the index is
chosen; the result is the not-found page without data. This scenario is tested by
`ReviewPage_SelectThread_MembershipRevoked_ShowsNotFoundAndNoData`.

**ReviewPage_FilterIndex_MembershipRevoked_ShowsNotFoundAndNoData**: The same arrangement, and the thread index filter is
changed, an action that reads no service; the access check still runs and the result is the not-found page without
data. This scenario is tested by `ReviewPage_FilterIndex_MembershipRevoked_ShowsNotFoundAndNoData`.

**ReviewPage_Reply_MembershipRevoked_ShowsNotFoundAndStoresNothing**: A member has a file selected and a reply typed,
and the membership is removed; posting the reply shows the not-found page without data, and the thread still has its one
comment. This scenario is tested by `ReviewPage_Reply_MembershipRevoked_ShowsNotFoundAndStoresNothing`.

**ReviewPage_DiffView_RevealRun_MembershipRevoked_RevealsNothingAndShowsNotFound**: A member has the diff of a modified
file open with the first run of unchanged lines collapsed, and the membership is removed; choosing "Show 6 unchanged
lines" shows the not-found page, the diff is gone, and the hidden lines were not revealed. This scenario is tested by
`ReviewPage_DiffView_RevealRun_MembershipRevoked_RevealsNothingAndShowsNotFound`.

**ReviewPage_Render_ExistingReview_ShowsHistoryLink**: A review is rendered; the page contains the history link
that points to the history page of the review. This scenario is tested by
`ReviewPage_Render_ExistingReview_ShowsHistoryLink`.

**Deep links** are verified in `ReviewDeepLinkTests.cs` and `ReviewLinksTests.cs`. The tests open an address through the
real router (`Routes`) over a repository review with a modified, an added and a deleted file, or over an upload review.

**ReviewLinks_ForFile_Path_EscapesEachSegment** and the other `ReviewLinks_*` tests: the builder is called directly for
the review address, file paths with spaces, `#`, `?`, `%`, `+`, `&`, non-ASCII letters and nested directories (each
segment escaped once, the separators kept), a line with a side, a thread, null arguments, and the check that every
address is a local path that `SignIn.SafeReturnUrl` keeps. These scenarios are tested by the tests of those names.

**ReviewPage_FileAddress_ModifiedFile_OpensFileInDiff**, **ReviewPage_LineAddress_HeadLine_HighlightsAndScrollsToTheRow**,
**ReviewPage_LineAddress_BaselineLine_HighlightsTheLeftCell**,
**ReviewPage_LineAddress_LineInCollapsedRun_IsShownAndHighlighted**,
**ReviewPage_LineAddress_AddedFile_HighlightsTheDiffRow**,
**ReviewPage_LineAddress_UploadFile_HighlightsTheCodeLine** and
**ReviewPage_LineAddress_DeletedFile_BaselineLineYesHeadLineNo**: the file address opens the file in the Diff view with
no message and no highlight, and the review address still opens no file; a line address highlights the cell of the
right side and asks the browser to scroll to its row (the baseline copy of an unchanged line for a baseline line, a
line inside a collapsed run is shown); a line of an added file of a review with a baseline highlights its diff row; a
file without a diff (an upload) highlights the line in the numbered text; a deleted file
has baseline lines but no head lines. These scenarios are tested by the tests of those names.

**ReviewPage_ThreadAddress_HeadThread_OpensFileAndHighlightsThread**,
**ReviewPage_ThreadAddress_AddedFileHeadThread_OpensDiffAndHighlightsRow**,
**ReviewPage_ThreadAddress_ResolvedBaselineThread_IsExpandedAndHighlightedOnTheLeft**,
**ReviewPage_ThreadAddress_BaselineThreadOnTooLargeFile_HighlightsItsBaselineLines**,
**ReviewPage_ThreadAddress_BaselineThread_DoesNotHighlightTheSameLinesOfTheHead**,
**ReviewPage_ThreadAddress_WholeFileThread_ScrollsToTheThread**,
**ReviewPage_ThreadAddress_KeepsThreadCountsInTheFileList** and
**ReviewPage_ThreadAddress_OverHttp_RendersFileAndHighlightedThread**: the thread address opens the file of the thread
in the Diff view, highlights the thread and its line and scrolls to it; a resolved thread is expanded; a thread on the
baseline side highlights the removed or baseline line, and on a file shown only as its baseline (too large to compare)
every baseline line of the thread's range, never the head line of the same number; a whole-file thread is scrolled to
as a card; the per-file thread
counts stay in the file list; and the first response over HTTP (before any live connection) already shows the file and
the highlight, which is what a reload gets. These scenarios are tested by the tests of those names.

**ReviewPage_Address_NamesNothing_ShowsMessageWithWayBack**: A theory over an unknown file, an unknown thread, a thread
of another review, a baseline line of an added file, and the lines `999`, `0`, `-1` and `abc`. Each shows a message in
the page, the link back to the review address, the whole file list, and nothing highlighted; no error page is shown.
**ReviewPage_Address_UnknownSideAndLaterChoice_UsesHeadAndClearsMessage**: an unknown `side` value is read as the head,
and the message of an unknown file disappears when a file is chosen. These scenarios are tested by the tests of those
names.

**ReviewPage_Choose_FileAndThread_ReplacesAddressWithoutNavigating** and
**ReviewPage_OpenDeepLink_DoesNotReplaceAddress**: choosing a file and then a thread asks the browser, through
`reviewCenter.replaceUrl`, for the link of the file and then of the thread, while the navigation manager keeps its
address and gains no history entry; opening a deep link does not rewrite the address. These scenarios are tested by the
tests of those names.

**ReviewPage_Thread_ShowsLinkButtonAndCopyLinkControl**: every thread, in the diff and in a list, has an anchor whose
text is "Link", whose `href` is the thread address, and whose classes are `thread-link` and the button-style `action`,
and a `button` of type `button` with the text "Copy link" beside it. This scenario is tested by the test of that name.

**ReviewPage_CopyLink_Click_CopiesAbsoluteThreadAddressAndShowsCopied**: with the browser function
`reviewCenter.copyText` answering true, a click on "Copy link" calls it once with the absolute address
(`http://localhost` plus the thread address), the status region reads "Copied" while the button keeps reading
"Copy link", and after the fake clock is advanced by two seconds the status is empty (awaited, as the clearing runs
through the renderer). **ReviewPage_CopyLink_SecondClick_KeepsFeedbackUntilTwoSecondsAfterTheLatestClick**: with
a manual clock, two clicks 1.5 seconds apart and the clock advanced by another 1.5 seconds, the status still reads
"Copied" (the timer of the first click does not clear the feedback of the second); after 0.6 seconds more it is empty.
**ReviewPage_CopyLink_ClipboardFails_ShowsCopyFailed**:
with the function answering false the status reads "Copy failed", the button still reads "Copy link", and the thread
link is unchanged. These scenarios are tested by the tests of those names.

**WebSubsystem_Stylesheet_ThreadLink_IsButtonStyled**: the stylesheet has no `a.thread-link` rule that strips the
button look, and the thread header gives the link and the buttons the same padding and font size. This scenario is
tested by the test of that name.

**WebSubsystem_Stylesheet_DiffCommentControl_ShowsOnHoverFocusAndTouch**: the stylesheet hides the `+` control of a diff
row with `opacity: 0`, reveals it on row hover, focus within the row, a selected row, and keyboard focus, shows it
always under `@media (hover: none)`, and never uses `display: none` or `visibility: hidden` for it, so it stays in the tab
order. This scenario is tested by the test of that name. A real browser has the hover state, so the reach is also proven
end to end by the Puppet test
`PuppetEndToEnd_ReviewDiff_HoverRevealsLineCommentAndClickOpensComposer_WorksAgainstRealServer` (`hover` on a row reveals
its `+`, a `click` on it opens the composer; see the verification of ReviewCenterPuppet), and by the acceptance
walk-through with Puppet (`puppet hover` on a row, a screenshot, then `puppet click` on its `+`).

**ReviewPage_RoundTrip_LinkFromBuilder_OpensTheSameFileAndLine**: a theory over file paths with nested directories,
spaces, `#`, `?`, `%`, a literal `%2F`, a literal `%20`, `+`, `&` and non-ASCII letters, next to a decoy file; the link
from `ReviewLinks` opens exactly that file and highlights its line through the router, so nothing is decoded twice.
This scenario is tested by the test of that name.

**ReviewPage_Render_ReviewNotApproved_OffersNoAttestation**: A review that nobody has signed is rendered; neither the
attestation line nor the download button is present. This scenario is tested by the test of that name.

**ReviewPage_Render_ReviewApprovedWithSelfSignedKey_OffersAttestationDownloadWithKeyNote**: A complete review is
rendered with the default key, which is self-signed. The button reads "Download attestation (PDF)", its `href` is the
address from `ReviewLinks.ForAttestation`, its `download` file name is `attestation-{project}-REV-1.pdf`, it has the
button style `action`, the note says the key is self-signed, generated by this server (the `SelfSigned` provider) and not
an organization key, the help link points to `/help/signed-attestation`, and no key problem is shown. This scenario is
tested by the test of that name.

**ReviewPage_Render_ReviewApprovedWithSelfSignedCertificateFromStore_NoteSaysAdministratorSuppliedIt**: A complete
review is rendered with a self-signed key reported by the `CertificateStore` provider. The note says that it is a
self-signed certificate supplied by the administrator from the certificate store, not an organization key and not
issued by a certificate authority, and does not say "generated by this server". This scenario is tested by the test of
that name.

**ReviewPage_Render_ReviewApprovedWithOrganizationKey_NoteNamesTheCertificate**: A complete review is rendered with a
key that is not self-signed; the note holds the subject of the certificate and the first 8 characters of its thumbprint
and does not say "self-signed". This scenario is tested by the test of that name.

**ReviewPage_Render_ReviewApprovedWithKeyProblem_ShowsProblemInsteadOfDownload**: A complete review is rendered with a
key provider that reports that the key store is locked; an element with the role `alert` holds the reason and there is
no download button. This scenario is tested by the test of that name.

**ReviewPage_Sign_CorrectPassword_OffersAttestationDownload**: A review with one slot is viewed without a download; the
reviewer signs with the correct password and the download button appears. This scenario is tested by the test of that
name.

**ReviewLinks_ForAttestation_ProjectAndReview_BuildsAddress**: The address for a review is
`/projects/3/reviews/REV-1/attestation.pdf`, and an identifier with a space and a slash is escaped once
(`a%20b%2Fc`). This scenario is tested by the test of that name.

**ReviewPage second factor scenarios**: These tests are in `ReviewPageTests.cs`; the signer is enrolled through the real
`TotpService`, the clock is moved to the next time step, and the codes are computed by the independent `TotpOracle`. A
signer without a second factor sees the password field and no code field, which is tested by
`ReviewPage_Render_SignerNotEnrolled_ShowsNoCodeField`. An enrolled signer sees the code field with the one-time code
hint; the password and a fresh code sign, the slot shows "Signed by Ann", one signature is stored, and neither the
password nor the code remains in the page; this is tested by
`ReviewPage_Sign_EnrolledSignerWithPasswordAndFreshCode_SignsAndClearsFields`. A theory over an empty and a wrong code
shows "The password or the code is incorrect.", stores no signature, and leaves both fields empty; this is tested by
`ReviewPage_Sign_EnrolledSignerWithWrongCode_ShowsGenericErrorAndSignsNothing`. The code of the step in which the user
enrolled is refused, and after the clock moves on the code of the next step signs; this is tested by
`ReviewPage_Sign_CodeAlreadyUsedInStep_IsRefusedUntilTheNextCode`. A recovery code typed in place of the code is
refused and nothing is signed, which is tested by `ReviewPage_Sign_RecoveryCodeInsteadOfCode_IsRefused`.
