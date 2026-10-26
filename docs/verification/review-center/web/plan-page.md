### PlanPage

#### Verification Approach

`PlanPage` is verified with component tests defined in `PlanPageTests.cs`, which use the bUnit test context to render the
real component over the real `ReviewPlanner`, `ReviewService`, `RepositoryService`, and `SigningService`, with an
in-memory fake store, a scripted git provider (`FakeGitRepositoryProvider`), and a scripted review-set parser
(`FakeReviewSetParser`), so that exact commits, files, and failures can be arranged. Tests locate elements by their
`data-testid` attributes (the page units share the helper `PageTestBase`). The real database and real git are covered by
the `Storage` subsystem, the `ReviewPlanner` integration tests, and the integration tests of the `Web` subsystem, which
also request the plan route over HTTP.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- An unknown project shows a not-found message and no form; a project without a registered repository shows a plain
  message and no form; otherwise the form offers the enabled repositories by name only (a single one is preselected),
  never a location, user name, or token, and the baseline choice defaults to Last reviewed, with the baseline text box
  only for Explicit revision.
- A form that is not filled in says what is missing and reads nothing from git.
- A plan shows one summary sentence, a facts line, and a next-step sentence; the sets that need a review come first,
  ticked, with title, baseline note, and counts; the sets with nothing to review are collapsed with a count; the groups
  for sets that cannot be planned and sets already filed appear only when they are not empty, the latter with a link to
  the review.
- The create button states the number of ticked rows and is disabled at zero; select all and select none appear only
  for more than three rows; ticking does not read git again.
- Creating shows one result per review, with a link for a created review and the reason for a failed one (which shows no
  raw git text, location, or credential), creates only the ticked rows, keeps the others when one fails, and offers to
  plan again.
- Every failure of a plan (unknown revision, no `.reviewmark.yaml`, invalid configuration, disabled repository, git
  access failure, unexpected fault) is explained in plain English with what to do next, shows no raw git text, location,
  credential, or exception text, and creates nothing; a refused creation keeps the plan.
- Changing the form, or reusing the page for another project, discards the plan; the button shows and disables itself
  while the repository is read, and every form control (and, while reviews are created, every tick and select all and
  none) is disabled while the page is busy and enabled again afterwards.
- Set titles are shown as text, never as markup.
- The interactive marker is `true` after the page has rendered on a live connection.

#### Test Scenarios

**PlanPage_Render_OneRepository_ShowsFormWithDefaults**: One repository is registered; the form has the repository
preselected, an empty head, the Last reviewed baseline chosen, and no baseline text box. This scenario is tested by
`PlanPage_Render_OneRepository_ShowsFormWithDefaults`.

**PlanPage_Render_UnknownProject_ShowsNotFound**: The page is rendered for an unknown project; the message names the
identity and there is no form. This scenario is tested by `PlanPage_Render_UnknownProject_ShowsNotFound`.

**PlanPage_Render_NoRepository_ShowsPlainMessageInsteadOfForm**: Without a repository the page says "No repository is
registered; ask an administrator." and shows no form. This scenario is tested by
`PlanPage_Render_NoRepository_ShowsPlainMessageInsteadOfForm`.

**PlanPage_Render_SeveralRepositories_OffersEnabledOnesWithoutSecrets**: Two enabled repositories and a disabled one
exist; only the enabled ones are offered, by name, with nothing preselected, and the page contains no location and no
token. This scenario is tested by `PlanPage_Render_SeveralRepositories_OffersEnabledOnesWithoutSecrets`.

**PlanPage_BaselineMode_ExplicitOnly_ShowsBaselineInput**: Choosing Explicit revision shows the baseline text box and
choosing another mode hides it. This scenario is tested by `PlanPage_BaselineMode_ExplicitOnly_ShowsBaselineInput`.

**PlanPage_ShowPlan_IncompleteForm_SaysWhatIsMissing**: Submitting without a repository, without a head, and with the
explicit mode without a baseline each shows its own message, and git is never opened. This scenario is tested by
`PlanPage_ShowPlan_IncompleteForm_SaysWhatIsMissing`.

**PlanPage_ShowPlan_SetsNeedingReview_ShowsSummaryTickedRowsAndCollapsedEmpties**: A plan with two sets that need a review
and one empty set shows "2 sets need review, 1 has nothing to review.", the facts line, the ticked rows with their
notes and counts, the create button "Create 2 reviews", and the empty set in a collapsed list "Nothing to review (1)";
the groups without rows are absent. This scenario is tested by
`PlanPage_ShowPlan_SetsNeedingReview_ShowsSummaryTickedRowsAndCollapsedEmpties`.

**PlanPage_ShowPlan_ExplicitBaseline_CountsChangesSinceTheBaseline**: With an explicit baseline the counts show added
and modified files, the note names the baseline, and the facts line says so. This scenario is tested by
`PlanPage_ShowPlan_ExplicitBaseline_CountsChangesSinceTheBaseline`.

**PlanPage_ShowPlan_NoBaseline_SaysAllFiles**: With no baseline the facts line says "all files (no baseline)". This
scenario is tested by `PlanPage_ShowPlan_NoBaseline_SaysAllFiles`.

**PlanPage_Ticks_Clear_UpdatesCreateButtonAndDisablesAtZero**: Clearing the ticks of rows lowers the number on the
button; at zero it is disabled and a hint says why; ticking again enables it; git was opened once. This scenario is
tested by `PlanPage_Ticks_Clear_UpdatesCreateButtonAndDisablesAtZero`.

**PlanPage_Ticks_ManyRows_OffersSelectAllAndNone**: With four rows the select all and select none buttons are offered and
work. This scenario is tested by `PlanPage_Ticks_ManyRows_OffersSelectAllAndNone`.

**PlanPage_Create_TickedRows_ShowsResultWithLinksAndCreatesReviews**: Creating shows "2 reviews created.", a link to each
review, replaces the plan and the form, and stores the reviews with their set identifier and the commit of the plan. This
scenario is tested by `PlanPage_Create_TickedRows_ShowsResultWithLinksAndCreatesReviews`.

**PlanPage_Create_OneRowNotTicked_CreatesOnlyTheTickedRow**: A row that is not ticked is not created. This scenario is
tested by `PlanPage_Create_OneRowNotTicked_CreatesOnlyTheTickedRow`.

**PlanPage_Create_OneRowFails_ShowsReasonAndCreatesTheOthers**: One review fails with an unexpected fault whose text
contains a marker; the result says "1 review created, 1 failed.", shows a generic reason without the marker, and the other
review exists. Planning again shows the created set as filed and offers the failed one once more. This scenario is tested
by `PlanPage_Create_OneRowFails_ShowsReasonAndCreatesTheOthers`.

**PlanPage_Create_OneRowGitAccessFailed_ShowsFixedSentenceWithoutGitText**: One review fails with a git access failure
whose text carries git output, the location, and a token; the failed row shows only the fixed sentence "The repository
could not be read.", the page contains none of that text, and the other review is created. This scenario is tested by
`PlanPage_Create_OneRowGitAccessFailed_ShowsFixedSentenceWithoutGitText`.

**PlanPage_ShowPlan_EverythingFiled_OpensFiledListWithLinksAndNoCreateButton**: When every set is filed, the summary
says no
set needs a review, the filed list is open with a link to each review, and there is no create button. This scenario is
tested by `PlanPage_ShowPlan_EverythingFiled_OpensFiledListWithLinksAndNoCreateButton`.

**PlanPage_ShowPlan_SetCannotBePlanned_ShowsReasonInOwnGroup**: A set with an invalid selection is counted in the summary,
shown in its own group with a reason and no checkbox. This scenario is tested by
`PlanPage_ShowPlan_SetCannotBePlanned_ShowsReasonInOwnGroup`.

**PlanPage_FormChange_DiscardsPlan**: Changing the head or the baseline mode removes the plan. This scenario is tested by
`PlanPage_FormChange_DiscardsPlan`.

**PlanPage_ParametersChange_OtherProject_ClearsFormAndPlan**: Reusing the page for another project shows that project with
an empty head and no plan. This scenario is tested by `PlanPage_ParametersChange_OtherProject_ClearsFormAndPlan`.

**PlanPage_ShowPlan_WhileReading_ShowsLoadingAndDisablesButton**: While the listing waits, the button reads "Reading
repository..." and is disabled; afterwards the plan is shown. This scenario is tested by
`PlanPage_ShowPlan_WhileReading_ShowsLoadingAndDisablesButton`.

**PlanPage_ShowPlan_WhileReading_DisablesEveryFormControl**: While the listing waits, the repository, head, the three
baseline choices, and the baseline text box are disabled; after the plan is shown they are enabled again. This scenario
is tested by `PlanPage_ShowPlan_WhileReading_DisablesEveryFormControl`.

**PlanPage_Create_WhileCreating_DisablesFormAndRowControls**: While the first review of a batch waits, the form
controls, select all, select none, and the row ticks are disabled; afterwards the results are shown. This scenario is
tested by `PlanPage_Create_WhileCreating_DisablesFormAndRowControls`.

**PlanPage_Error_UnknownRevision_ExplainsAndSuggestsChecking**: An unknown head names the revision and says to check the
spelling. This scenario is tested by `PlanPage_Error_UnknownRevision_ExplainsAndSuggestsChecking`.

**PlanPage_Error_NoConfiguration_ExplainsAndLinksToProject**: A head without `.reviewmark.yaml` says so, tells what to do,
and links to the project page. This scenario is tested by `PlanPage_Error_NoConfiguration_ExplainsAndLinksToProject`.

**PlanPage_Error_InvalidConfiguration_ShowsMessageAndHint**: An invalid configuration shows the message with its position
and says to fix the file. This scenario is tested by `PlanPage_Error_InvalidConfiguration_ShowsMessageAndHint`.

**PlanPage_Error_RepositoryDisabledMeanwhile_ExplainsWithoutSecrets**: A repository disabled after the page opened is
explained, names the administrator, and shows no secret. This scenario is tested by
`PlanPage_Error_RepositoryDisabledMeanwhile_ExplainsWithoutSecrets`.

**PlanPage_Error_GitAccessFailed_ShowsFixedSentenceWithoutGitText**: A git failure whose text carries git output and the
location is shown as the fixed sentence "The repository could not be read." only. This scenario is tested by
`PlanPage_Error_GitAccessFailed_ShowsFixedSentenceWithoutGitText`.

**PlanPage_Error_UnexpectedFault_ShowsGenericMessage**: An exception that is not a rule of the domain is shown as a generic
message with none of its text. This scenario is tested by `PlanPage_Error_UnexpectedFault_ShowsGenericMessage`.

**PlanPage_Create_WholeBatchRefused_ShowsErrorNextToButtonAndKeepsPlan**: A repository disabled after the plan was shown
makes the creation fail as a whole; one error box is shown next to the create button, the plan and the ticks stay, and
nothing is created. This scenario is tested by `PlanPage_Create_WholeBatchRefused_ShowsErrorNextToButtonAndKeepsPlan`.

**PlanPage_ShowPlan_MarkupInSetTitle_IsEscaped**: A set title that is markup is shown as text. This scenario is tested by
`PlanPage_ShowPlan_MarkupInSetTitle_IsEscaped`.

**PlanPage_AfterRender_Interactive_SetsMarker**: After the first render the root element reports `true`. This scenario is
tested by `PlanPage_AfterRender_Interactive_SetsMarker`.
