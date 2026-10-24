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
- No content is shown until a file is selected; selecting a text file shows its raw text in a preformatted block;
  selecting another file replaces the content.
- Binary content, including text that is not valid UTF-8, shows a notice instead of content; an empty file shows
  an empty content block.
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

**ReviewPage_FileSelect_TextFile_ShowsRawTextInPre**: A text file is selected; its raw text is shown in a `pre`
element, with the selected path, and no binary notice. This scenario is tested by
`ReviewPage_FileSelect_TextFile_ShowsRawTextInPre`.

**ReviewPage_FileSelect_BinaryFile_ShowsBinaryNoticeAndNoContent**: A binary file is selected; the binary notice
is shown and no content. This scenario is tested by `ReviewPage_FileSelect_BinaryFile_ShowsBinaryNoticeAndNoContent`.

**ReviewPage_FileSelect_InvalidUtf8_ShowsBinaryNotice**: A file that is not valid UTF-8 and has no zero byte is
selected; the binary notice is shown. This scenario is tested by `ReviewPage_FileSelect_InvalidUtf8_ShowsBinaryNotice`.

**ReviewPage_FileSelect_EmptyFile_ShowsEmptyContent**: An empty file is selected; an empty content block is shown
and no binary notice. This scenario is tested by `ReviewPage_FileSelect_EmptyFile_ShowsEmptyContent`.

**ReviewPage_FileSelect_SecondFile_ReplacesContent**: A second file is selected; its content and path replace the
first. This scenario is tested by `ReviewPage_FileSelect_SecondFile_ReplacesContent`.

**ReviewPage_Render_ExistingReview_FileButtonsNameTheirFile**: Every file's View button has an `aria-label` of
`View` followed by its path. This scenario is tested by `ReviewPage_Render_ExistingReview_FileButtonsNameTheirFile`.

**ReviewPage_ParametersChange_OtherReview_ShowsNewReviewAndClearsSelection**: A file is selected, then the page is
reused for another review of the project; the other review is shown and no file content remains. This scenario is
tested by `ReviewPage_ParametersChange_OtherReview_ShowsNewReviewAndClearsSelection`.

**ReviewPage_AfterRender_Interactive_SetsMarker**: The page is rendered; its root element has `data-interactive`
equal to `true`. This scenario is tested by `ReviewPage_AfterRender_Interactive_SetsMarker`.
