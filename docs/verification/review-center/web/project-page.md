### ProjectPage

#### Verification Approach

`ProjectPage` is verified with component tests defined in `ProjectPageTests.cs` and
`ProjectPageRepositoryTests.cs`, which use the bUnit test context
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
- The form offers "Upload files" by default and "From a repository", which shows the repository fields instead of
  the file input with the default selection `**/*`; the repository choice lists only enabled repositories by name
  (never a location, user name, or token) and is disabled with a hint when none is enabled.
- A valid repository form creates the review with its source and opens it; without a baseline every file at the head
  is reviewed; a selection with no changed file shows the nothing-to-review message, a revision that does not exist
  shows the git error, no chosen repository asks for one, and a repository disabled in the meantime is refused; each
  creates nothing and keeps the entered values.
- Skipped links and submodules are shown to the creator before the review is opened.
- The page links to the plan page of the project when a repository is enabled, and otherwise says whom to ask.

#### Test Scenarios

**Reviews from a repository** are verified in `ProjectPageRepositoryTests.cs`, with the fake git provider of the
`Web` tests (`FakeGitRepositoryProvider`) behind the real `RepositoryService` and `ReviewService`.

**ProjectPage_SourceChoice_RepositoryChosen_ShowsRepositoryFieldsInsteadOfUpload**: The upload source is shown first;
choosing the repository source shows the repository, head, baseline, and selection fields (the selection starts as
`**/*`) and hides the file input. This scenario is tested by
`ProjectPage_SourceChoice_RepositoryChosen_ShowsRepositoryFieldsInsteadOfUpload`.

**ProjectPage_RepositorySelect_OffersOnlyEnabledRepositoriesWithoutLocationOrToken**: An enabled and a disabled
repository exist; only the enabled one is offered, by name, and the page contains neither location, user name, nor
token. This scenario is tested by
`ProjectPage_RepositorySelect_OffersOnlyEnabledRepositoriesWithoutLocationOrToken`.

**ProjectPage_SourceChoice_NoEnabledRepository_DisablesRepositorySourceWithHint**: With no enabled repository the
repository choice is disabled and a hint says why. This scenario is tested by
`ProjectPage_SourceChoice_NoEnabledRepository_DisablesRepositorySourceWithHint`.

**ProjectPage_CreateForm_FromRepository_CreatesReviewWithSourceAndNavigates**: A valid repository form creates the
review with its repository, commits, selection, and changes, and navigates to it. This scenario is tested by
`ProjectPage_CreateForm_FromRepository_CreatesReviewWithSourceAndNavigates`, and without a baseline by
`ProjectPage_CreateForm_FromRepositoryWithoutBaseline_ReviewsAllFilesAtHead`.

**ProjectPage_CreateForm_EmptySelection_ShowsNothingToReviewAndCreatesNothing**: A selection with no changed file
shows "Nothing to review: no files matching the selection changed between X and Y" and creates nothing. This
scenario is tested by `ProjectPage_CreateForm_EmptySelection_ShowsNothingToReviewAndCreatesNothing`.

**ProjectPage_CreateForm_BadRevision_ShowsErrorAndCreatesNothing**: A revision that does not exist shows the error
and creates nothing. This scenario is tested by `ProjectPage_CreateForm_BadRevision_ShowsErrorAndCreatesNothing`.

**ProjectPage_CreateForm_NoRepositoryChosen_AsksForOne**: Submitting without a repository shows "Choose a
repository." and creates nothing. This scenario is tested by `ProjectPage_CreateForm_NoRepositoryChosen_AsksForOne`.

**ProjectPage_CreateForm_RepositoryDisabledMeanwhile_ShowsErrorAndCreatesNothing**: A repository disabled after the
page opened is refused on submit and nothing is created. This scenario is tested by
`ProjectPage_CreateForm_RepositoryDisabledMeanwhile_ShowsErrorAndCreatesNothing`.

**ProjectPage_CreateForm_SkippedEntries_AreShownBeforeOpeningTheReview**: Skipped entries are listed with a link to
open the created review, and the page does not navigate until the link is followed. This scenario is tested by
`ProjectPage_CreateForm_SkippedEntries_AreShownBeforeOpeningTheReview`.
**ProjectPage_Render_PlanLink_ShownOnlyWithEnabledRepository**: A project without any repository shows the note and no
link; after a repository is registered, the link goes to `/projects/{id}/plan` and the note is gone. This scenario is
tested by `ProjectPage_Render_PlanLink_ShownOnlyWithEnabledRepository`.

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
