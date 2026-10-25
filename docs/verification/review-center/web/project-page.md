### ProjectPage

#### Verification Approach

`ProjectPage` is verified with component tests defined in `ProjectPageTests.cs`, which use the bUnit test context
to render the real component over real Core services and an in-memory fake store. File selection is simulated the
way a browser does it, by setting the files of the file input in the test context. Tests locate elements by their
`data-testid` attributes (the page units share the helper `PageTestBase`) and wait for the asynchronous file
reading with bUnit's wait helpers. The real database is covered by the `Storage` subsystem and the integration
tests of the `Web` subsystem.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- An unknown project shows a not-found message and no form; a known project shows its name and description and
  its reviews with link, title, state, and an Approved or In review badge, or an empty message. A review whose every
  slot has a current signature is Approved; a review without slots or with an unsigned slot is In review.
- Selected files are listed with names and sizes, and a new selection replaces the previous one; reusing the page
  for another project shows that project and discards the previous form values and selection.
- A valid form creates the review with the exact bytes and the correct fingerprint and opens the review page.
- Rule violations (no files, duplicate review identifier, duplicate file names) and a selection over a limit (too
  many files, a file over the size limit, or a total over the size limit) are shown as errors that keep the
  entered values and create nothing.
- The interactive marker is `true` after the page has rendered on a live connection.

#### Test Scenarios

**ProjectPage_Render_UnknownProject_ShowsNotFound**: The page is rendered for an unknown project; the not-found
message names the identity and no form is shown. This scenario is tested by
`ProjectPage_Render_UnknownProject_ShowsNotFound`.

**ProjectPage_Render_ProjectWithoutReviews_ShowsNameAndEmptyMessage**: The page is rendered for a project without
reviews; the name, the description, and the empty message are shown. This scenario is tested by
`ProjectPage_Render_ProjectWithoutReviews_ShowsNameAndEmptyMessage`.

**ProjectPage_Render_ProjectWithReview_ListsReviewWithLinkTitleAndState**: The page is rendered for a project with
a review; the review is listed with a link to its page, its title, the state "Draft", and the badge "In review" (it has
no reviewer slot). This scenario is tested
by `ProjectPage_Render_ProjectWithReview_ListsReviewWithLinkTitleAndState`.

**ProjectPage_Render_SignedAndUnsignedReviews_ShowApprovedOrInReview**: A project has one review whose only slot was
signed with the right password and one review without slots; the first is listed as "Approved" and the second as "In
review". This scenario is tested by `ProjectPage_Render_SignedAndUnsignedReviews_ShowApprovedOrInReview`.

**ProjectPage_FileInput_FilesSelected_ListsNamesAndSizes**: Two files are chosen; each is listed with its name and
size in bytes. This scenario is tested by `ProjectPage_FileInput_FilesSelected_ListsNamesAndSizes`.

**ProjectPage_FileInput_SecondSelection_ReplacesFirst**: A second selection replaces the first. This scenario is
tested by `ProjectPage_FileInput_SecondSelection_ReplacesFirst`.

**ProjectPage_ParametersChange_OtherProject_ClearsFormAndSelection**: A form is filled and a file selected, then the
page is reused for another project; the other project is shown with an empty form and no selected files. This
scenario is tested by `ProjectPage_ParametersChange_OtherProject_ClearsFormAndSelection`.

**ProjectPage_CreateForm_ValidInput_CreatesReviewAndNavigatesToIt**: Files are chosen, the form is completed and
submitted; the review is stored with the title, the fingerprint of the files, and the exact file bytes, and the
browser is sent to the review page. This scenario is tested by
`ProjectPage_CreateForm_ValidInput_CreatesReviewAndNavigatesToIt`.

**ProjectPage_CreateForm_SessionEndedBeforePageKnows_ShowsClearErrorAndCreatesNothing**: Files are chosen and a title
is entered, and the sign-in state ends without the page being told; the form is submitted. An error says that the user
is no longer signed in, and no review is stored. This scenario is tested by
`ProjectPage_CreateForm_SessionEndedBeforePageKnows_ShowsClearErrorAndCreatesNothing`.

**ProjectPage_CreateForm_NoFiles_ShowsErrorAndCreatesNothing**: The form is submitted without files; an error that
mentions files is shown and no review is stored. This scenario is tested by
`ProjectPage_CreateForm_NoFiles_ShowsErrorAndCreatesNothing`.

**ProjectPage_CreateForm_DuplicateReviewId_ShowsErrorNamingTheId**: A review with an existing identifier is
submitted; the error names the identifier, the entered values stay, and only the original review exists. This
scenario is tested by `ProjectPage_CreateForm_DuplicateReviewId_ShowsErrorNamingTheId`.

**ProjectPage_CreateForm_DuplicateFileNames_ShowsDuplicatePathError**: Two files with the same name are chosen and
the form is submitted; the error names the file and no review is stored. This scenario is tested by
`ProjectPage_CreateForm_DuplicateFileNames_ShowsDuplicatePathError`.

**ProjectPage_FileInput_FileTooLarge_ShowsErrorAndKeepsNothing**: A file over the size limit is chosen; the error
names the file and no selection is kept. The form is then submitted; the error still names the file (it is not
replaced by the "no files" error) and nothing is stored. This scenario is tested by
`ProjectPage_FileInput_FileTooLarge_ShowsErrorAndKeepsNothing`.

**ProjectPage_FileInput_TooManyFiles_ShowsErrorAndKeepsNothing**: One more small file than the file-count limit is
chosen; the error names the limit and no selection is kept. The form is then submitted; the error is still shown
and nothing is stored. This scenario is tested by `ProjectPage_FileInput_TooManyFiles_ShowsErrorAndKeepsNothing`.

**ProjectPage_FileInput_TotalSizeTooLarge_ShowsErrorAndKeepsNothing**: Enough files, each exactly at the per-file
size limit, are chosen so that together they exceed the total size limit (the files share one buffer to keep the
test's memory use low); the error says the files together are too large and no selection is kept. The form is then
submitted; the error is still shown and nothing is stored. This scenario is tested by
`ProjectPage_FileInput_TotalSizeTooLarge_ShowsErrorAndKeepsNothing`.

**ProjectPage_FileInput_DeclaredSizesUnderstated_RejectsOnReceivedBytes**: A fake browser file reports one byte
but streams the per-file maximum, and enough of them are delivered to the file input to pass the total limit; the
total-size error is shown and no selection is kept. This scenario is tested by
`ProjectPage_FileInput_DeclaredSizesUnderstated_RejectsOnReceivedBytes`.

**ProjectPage_AfterRender_Interactive_SetsMarker**: The page is rendered; its root element has `data-interactive`
equal to `true`. This scenario is tested by `ProjectPage_AfterRender_Interactive_SetsMarker`.
