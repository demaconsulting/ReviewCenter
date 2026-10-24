### ReviewPage

#### Verification Approach

`ReviewPage` is verified with component tests defined in `ReviewPageTests.cs`, which use the bUnit test context to
render the real component over real Core services and an in-memory fake store holding a review of known files.
Tests locate elements by their `data-testid` attributes (the page units share the helper `PageTestBase`) and
compare the shown values with the known hashes and fingerprint. The real database is covered by the `Storage`
subsystem and the integration tests of the `Web` subsystem.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- An unknown review shows a not-found message and no details.
- A known review shows its identifier, title, state, and the known fingerprint, and lists every file with path,
  size, and SHA-256 hash; each file's View button has an accessible name that includes the file path.
- Reusing the page for another review shows the new review and clears the previously selected file.
- No content is shown until a file is selected; selecting a text file shows its lines numbered, with the exact
  text of each line as plain text; selecting another file replaces the content.
- Binary content, including text that is not valid UTF-8, shows a notice instead of content; an empty file shows
  an empty-file notice and no lines.
- With no threads the summary and every file count are zero; with threads the summary, the per-file open counts,
  the anchored-line markers, and the comments of the selected file are shown, and an empty-state message appears
  for a file without threads.
- The line Comment button fills the line fields; starting a thread anchors the whole file, a line range, or one
  line, shows the thread and clears the text; every rule violation (missing author, missing text, line outside the
  file, line that is not a number, line range on a binary file) shows a message next to the form and keeps what was
  typed.
- A reply is added to a thread; a blank reply or a missing author shows a message in that thread, and the missing
  author message points to the name field.
- Resolve and reopen change the thread state and the summary.
- Thread and comment text is shown as plain text, never as markup, and every interactive control has a
  `data-testid`.
- The interactive marker is `true` after the page has rendered on a live connection.

#### Test Scenarios

**ReviewPage_Render_UnknownReview_ShowsNotFound**: The page is rendered for an unknown review; the not-found
message names the identifier and no fingerprint is shown. This scenario is tested by
`ReviewPage_Render_UnknownReview_ShowsNotFound`.

**ReviewPage_Render_ExistingReview_ShowsIdTitleStateAndFingerprint**: The page is rendered for a review; the
identifier, title, state "Draft", and the known fingerprint are shown. This scenario is tested by
`ReviewPage_Render_ExistingReview_ShowsIdTitleStateAndFingerprint`.

**ReviewPage_Render_ExistingReview_ListsFilesWithPathSizeAndHash**: Every file is listed with its path, size, and
known SHA-256 hash. This scenario is tested by `ReviewPage_Render_ExistingReview_ListsFilesWithPathSizeAndHash`.

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
whole file, is shown, and the text is cleared. This scenario is tested by
`ReviewPage_StartThread_WholeFile_CreatesThreadAndClearsText`.

**ReviewPage_StartThread_LineRangeAndSingleLine_AnchorLines**: A range and a single line are stored with the
entered anchors and shown with the matching labels. This scenario is tested by
`ReviewPage_StartThread_LineRangeAndSingleLine_AnchorLines`.

**ReviewPage_StartThread_InvalidInput_ShowsErrorAndKeepsValues**: A missing author, missing text, line outside the
file, and a line that is not a number each show a message next to the form, create no thread, and keep what was
typed. This scenario is tested by `ReviewPage_StartThread_InvalidInput_ShowsErrorAndKeepsValues`.

**ReviewPage_StartThread_BinaryFile_WholeFileAllowedAndRangeRefused**: On a binary file a whole-file thread is
accepted and a line range is refused. This scenario is tested by
`ReviewPage_StartThread_BinaryFile_WholeFileAllowedAndRangeRefused`.

**ReviewPage_Reply_ValidAndBlank_AppendsCommentOrShowsErrorInThread**: A valid reply is appended to the thread; a
blank reply shows a message in the thread. This scenario is tested by
`ReviewPage_Reply_ValidAndBlank_AppendsCommentOrShowsErrorInThread`.

**ReviewPage_Reply_NoAuthor_ShowsErrorPointingToNameField**: A reply with no author shows a message that points to
the name field. This scenario is tested by `ReviewPage_Reply_NoAuthor_ShowsErrorPointingToNameField`.

**ReviewPage_ResolveAndReopen_Toggle_ChangesStateAndSummary**: Resolving an open thread and reopening it changes
the state label, the button label, and the summary counts. This scenario is tested by
`ReviewPage_ResolveAndReopen_Toggle_ChangesStateAndSummary`.

**ReviewPage_Render_ThreadText_IsShownAsPlainText**: Markup in a comment is shown as text and creates no element.
This scenario is tested by `ReviewPage_Render_ThreadText_IsShownAsPlainText`.

**ReviewPage_Render_ThreadControls_EveryInteractiveElementHasTestId**: Every button, input, and text area of the
thread controls has a `data-testid`. This scenario is tested by
`ReviewPage_Render_ThreadControls_EveryInteractiveElementHasTestId`.

**ReviewPage_AfterRender_Interactive_SetsMarker**: The page is rendered; its root element has `data-interactive`
equal to `true`. This scenario is tested by `ReviewPage_AfterRender_Interactive_SetsMarker`.
