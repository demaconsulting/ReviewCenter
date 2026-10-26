### ReviewPlanner

#### Verification Approach

`ReviewPlanner` is verified with two groups of tests. The unit tests in `ReviewPlannerTests.cs` use the real
`ReviewPlanner`, `ReviewService`, `RepositoryService`, and `SigningService` over in-memory fakes of the review store, the
repository store, the git provider, and the review-set parser, so every branch of the baseline choice and of the
classification is reached with exact commits and files, and the number of sessions that git opened can be counted. The
integration tests in `ReviewPlannerGitTests.cs` use the real `GitSource` and `ReviewMarkConfigParser` over real git
repositories that the tests create with the installed git (through `GitTestSupport`), and a real SQLite database, so
that the plan is computed from real history: the baseline since the last signed review, the fallback when that commit is
not an ancestor, the detection of a review that was already filed, and the skipping of a set without changes. The tests
that need git skip themselves when it is not installed.

#### Test Environment

N/A - standard test environment (the integration tests need git, and skip themselves when it is not installed).

#### Acceptance Criteria

- All tests pass with zero failures (or skip because git is not installed).
- A plan has one row per review-set in file order, is made with one session, and counts the added, modified, and deleted
  files of each selection.
- A missing, oversized, or refused configuration file, an unknown revision, and an unknown project or repository stop the
  plan with their rule; an unusable set is only a `CannotPlan` row.
- With no baseline every selected file is reviewed; with an explicit baseline every set uses it; with the last reviewed
  mode a set uses the head of the review that was signed last when it is an ancestor of the new head and the selection is
  the same, and otherwise gets a full review with a note that says why.
- Change lists are loaded once per distinct baseline and one at a time: the earlier list is no longer held when the
  next one is requested, and the rows keep the order of the file.
- A set with a review of the same head and baseline is `AlreadyFiled`; a set with no changed file, or at the head of its
  last signed review, is `NothingToReview`.
- Creation uses one session, creates each review through the review service with its set identifier and the range title,
  reports each request on its own, and lets the others succeed when one fails, also when the failure is an unexpected
  fault, which is reported with a generic reason; a git access failure is reported with a fixed sentence that carries no
  git text; a cancellation is not turned into a result.
- Review identifiers are safe, within the limit, and unique, also when taken during the creation.

#### Test Scenarios

**ReviewPlanner_PlanAsync_NoBaseline_ClassifiesSetsWithOneSession**: A plan with no baseline gives a `WillCreate` row
with the counts of its files, a row for a set without matching files, and the order of the file; git was opened once.
This scenario is tested by `ReviewPlanner_PlanAsync_NoBaseline_ClassifiesSetsWithOneSession`.

**ReviewPlanner_PlanAsync_ExplicitBaseline_UsesItForEverySet**: With an explicit baseline every set lists the changes
since it and the note names the revision. This scenario is tested by
`ReviewPlanner_PlanAsync_ExplicitBaseline_UsesItForEverySet`.

**ReviewPlanner_PlanAsync_UnknownExplicitBaseline_ThrowsRevisionNotFound**: An explicit baseline that git does not know
stops the plan with `RevisionNotFound`. This scenario is tested by
`ReviewPlanner_PlanAsync_UnknownExplicitBaseline_ThrowsRevisionNotFound`.

**ReviewPlanner_PlanAsync_LastReviewedWithOnlyDraft_FullReview**: A set whose only review is not signed gets a full
review with the note that there is no signed review yet. This scenario is tested by
`ReviewPlanner_PlanAsync_LastReviewedWithOnlyDraft_FullReview`.

**ReviewPlanner_PlanAsync_LastReviewedSignedAncestor_UsesItsHeadAsBaseline**: A signed review whose head is an ancestor
gives the changes since that head. This scenario is tested by
`ReviewPlanner_PlanAsync_LastReviewedSignedAncestor_UsesItsHeadAsBaseline`.

**ReviewPlanner_PlanAsync_LastReviewedTwoSigned_PicksTheOneSignedLast**: Of two signed reviews of a set the one signed
last is the baseline. This scenario is tested by `ReviewPlanner_PlanAsync_LastReviewedTwoSigned_PicksTheOneSignedLast`.

**ReviewPlanner_PlanAsync_SeveralBaselines_HoldsOneChangeListAtATimeAndKeepsRowOrder**: Four sets alternate between two
last-signed baselines and the full head; the head and each baseline are listed once, no earlier change list is still
alive when the next is requested, and the rows are in the order of the file with the changes of their own baseline. This
scenario is tested by `ReviewPlanner_PlanAsync_SeveralBaselines_HoldsOneChangeListAtATimeAndKeepsRowOrder`.

**ReviewPlanner_PlanAsync_LastReviewedNotAncestor_FullReviewWithNote**: A signed review whose head is not an ancestor
gives a full review and a note about the rewritten history. This scenario is tested by
`ReviewPlanner_PlanAsync_LastReviewedNotAncestor_FullReviewWithNote`.

**ReviewPlanner_PlanAsync_AncestorCheckFails_FullReviewWithNote**: When git cannot answer the ancestor question the plan
is still made, with a full review and a note. This scenario is tested by
`ReviewPlanner_PlanAsync_AncestorCheckFails_FullReviewWithNote`.

**ReviewPlanner_PlanAsync_SelectionChanged_FullReviewWithNote**: A set whose selection differs from that of its last
signed review gets a full review and a note. This scenario is tested by
`ReviewPlanner_PlanAsync_SelectionChanged_FullReviewWithNote`.

**ReviewPlanner_PlanAsync_LastReviewedSameHead_NothingToReview**: A set whose last signed review has the new head is
`NothingToReview`. This scenario is tested by `ReviewPlanner_PlanAsync_LastReviewedSameHead_NothingToReview`.

**ReviewPlanner_PlanAsync_ReviewExists_AlreadyFiled**: A set with a review of the same head and baseline is
`AlreadyFiled` with the identifier of that review. This scenario is tested by
`ReviewPlanner_PlanAsync_ReviewExists_AlreadyFiled`.

**ReviewPlanner_PlanAsync_UnusableSets_CannotPlanRows**: A set with an invalid path, one with an identifier over the
limit, and one that selects too many files are `CannotPlan` rows with a problem, and the good set is still planned. This
scenario is tested by `ReviewPlanner_PlanAsync_UnusableSets_CannotPlanRows`.

**ReviewPlanner_PlanAsync_NoConfiguration_ThrowsConfigMissing**: A head without `.reviewmark.yaml` in its root stops the
plan with `ReviewSetConfigMissing`. This scenario is tested by
`ReviewPlanner_PlanAsync_NoConfiguration_ThrowsConfigMissing`.

**ReviewPlanner_PlanAsync_ConfigurationTooLarge_ThrowsConfigInvalidWithoutReading**: A configuration file over the limit
stops the plan with `ReviewSetConfigInvalid`, and its bytes were never read. This scenario is tested by
`ReviewPlanner_PlanAsync_ConfigurationTooLarge_ThrowsConfigInvalidWithoutReading`.

**ReviewPlanner_PlanAsync_ParserRejectsFile_ThrowsConfigInvalid**: The rule of the parser reaches the caller. This
scenario is tested by `ReviewPlanner_PlanAsync_ParserRejectsFile_ThrowsConfigInvalid`.

**ReviewPlanner_PlanAsync_BadRequests_ThrowTheirRules**: An unknown head, an invalid head, an unknown project, an unknown
repository, and a disabled repository stop the plan with their own rules. This scenario is tested by
`ReviewPlanner_PlanAsync_BadRequests_ThrowTheirRules`.

**ReviewPlanner_CreateAsync_PlanRows_CreatesReviewsWithSetIdAndOneSession**: The rows of a plan are created; each review
has its set identifier, the head commit, and a frozen file list, and git was opened once for the batch. This scenario is
tested by `ReviewPlanner_CreateAsync_PlanRows_CreatesReviewsWithSetIdAndOneSession`.

**ReviewPlanner_CreateAsync_DeltaRow_UsesBaselineAndRangeTitle**: A row with a baseline is created from that baseline,
with a title that shows the range. This scenario is tested by
`ReviewPlanner_CreateAsync_DeltaRow_UsesBaselineAndRangeTitle`.

**ReviewPlanner_CreateAsync_OneRequestFails_OthersAreCreated**: One request fails and the others are created; the failed
one is reported with its reason and nothing was stored for it. This scenario is tested by
`ReviewPlanner_CreateAsync_OneRequestFails_OthersAreCreated`.

**ReviewPlanner_CreateAsync_UnexpectedFault_FailsOnlyThatRowWithGenericMessage**: One request is created; then the git
listing fails with an exception that is not a domain rule, and its text contains a marker. The next request is reported
as `Failed` with a generic message that does not contain the marker, nothing is stored for it, the earlier result is
intact, and creating it again afterwards succeeds. This scenario is tested by
`ReviewPlanner_CreateAsync_UnexpectedFault_FailsOnlyThatRowWithGenericMessage`.

**ReviewPlanner_CreateAsync_GitAccessFailed_FailsOnlyThatRowWithFixedSentence**: One request is created; then the git
listing fails with a git access failure whose text carries git output, a location, and a token. The next request is
reported as `Failed` with exactly the fixed sentence "The repository could not be read." without any of that text,
nothing is stored for it, the earlier result is intact, and creating it again afterwards succeeds. This scenario is
tested by `ReviewPlanner_CreateAsync_GitAccessFailed_FailsOnlyThatRowWithFixedSentence`.

**ReviewPlanner_CreateAsync_Cancellation_Propagates**: A cancellation raised while a request is created is not turned
into a failed result; it reaches the caller. This scenario is tested by `ReviewPlanner_CreateAsync_Cancellation_Propagates`.

**ReviewPlanner_CreateAsync_SameRangeTwice_SecondIsAlreadyFiled**: Creating the same range again gives `AlreadyFiled`
with the existing identifier. This scenario is tested by `ReviewPlanner_CreateAsync_SameRangeTwice_SecondIsAlreadyFiled`.

**ReviewPlanner_CreateAsync_IdTaken_UsesSuffix**: A set identifier that is already a review identifier gets the next
free number. This scenario is tested by `ReviewPlanner_CreateAsync_IdTaken_UsesSuffix`.

**ReviewPlanner_CreateAsync_IdTakenDuringCreate_RetriesWithNextSuffix**: An identifier that another user took after the
list was read makes the store report a duplicate, and the planner retries with the next number. This scenario is tested
by `ReviewPlanner_CreateAsync_IdTakenDuringCreate_RetriesWithNextSuffix`.

**ReviewPlanner_CreateAsync_InvalidRequests_FailPerRequest**: A baseline that is not a commit, an invalid selection,
and a set identifier over the limit fail one by one with a plain message, and nothing is stored. This scenario is
tested by `ReviewPlanner_CreateAsync_InvalidRequests_FailPerRequest`.

**ReviewPlanner_CreateAsync_BadBatch_Throws**: A head that is not a commit, too many requests, a null list, a null actor,
and an unknown project are refused for the whole batch, and git is never opened. This scenario is tested by
`ReviewPlanner_CreateAsync_BadBatch_Throws`.

**ReviewPlanner_CreateAsync_LongestInputs_AuditDetailFits**: A review made from the longest repository name, set
identifier, and title is created, and its audit detail and title fit their limits. This scenario is tested by
`ReviewPlanner_CreateAsync_LongestInputs_AuditDetailFits`.

**ReviewPlanner_GenerateId_SetId_MakesSafeUniqueId**: Unsafe characters, runs, leading and trailing punctuation, empty
results, and taken identifiers give the expected safe and unique identifiers. This scenario is tested by
`ReviewPlanner_GenerateId_SetId_MakesSafeUniqueId`.

**ReviewPlanner_GenerateId_LongSetId_CutsAndStaysWithinLimit**: A set identifier of the maximum length gives an
identifier within the limit, also with a suffix. This scenario is tested by
`ReviewPlanner_GenerateId_LongSetId_CutsAndStaysWithinLimit`.

**ReviewPlanner_GenerateTitle_LongSetTitle_CutsTitleKeepsRange**: A long set title is cut so that the range and the whole
title fit. This scenario is tested by `ReviewPlanner_GenerateTitle_LongSetTitle_CutsTitleKeepsRange`.

**ReviewPlanner_Constructor_NullArgument_Throws**: Every dependency of the constructor is checked for null. This
scenario is tested by `ReviewPlanner_Constructor_NullArgument_Throws`.

**ReviewPlanner_PlanAsync_RealGit_NoBaselineAndExplicitBaseline**: On a real repository with a configuration file, a plan
with no baseline lists the files of each set as added, a set without matching files is `NothingToReview` (with the note
that no file matches), and a plan with an explicit baseline counts the changes since it. This scenario is tested by
`ReviewPlanner_PlanAsync_RealGit_NoBaselineAndExplicitBaseline`.

**ReviewPlanner_CreateAsync_RealGit_LastReviewedBaselineThenAlreadyFiled**: On a real repository, reviews of two sets are
created at a tag and one is signed. The plan of the branch head in last reviewed mode gives the signed set a delta whose
baseline is the head of that review, and the unsigned set a full review; the rows are created under new identifiers,
frozen with the baseline and the set, and a second plan then shows both as already filed. This scenario is tested by
`ReviewPlanner_CreateAsync_RealGit_LastReviewedBaselineThenAlreadyFiled`.

**ReviewPlanner_PlanAsync_RealGit_NotAncestor_FallsBackToFullReview**: On a real repository, a review of a set is signed
at the head of a side branch that the main branch does not contain; the real ancestor check says no, and the set gets a
full review with the note. This scenario is tested by `ReviewPlanner_PlanAsync_RealGit_NotAncestor_FallsBackToFullReview`.

**ReviewPlanner_PlanAsync_RealGit_SelectionChanged_FallsBackToFullReview**: On a real repository, the paths of a set
changed after its last signed review, so the set gets a full review with the note, although the old head is an
ancestor. This scenario is tested by `ReviewPlanner_PlanAsync_RealGit_SelectionChanged_FallsBackToFullReview`.

**ReviewPlanner_PlanAsync_RealGit_NoConfigurationOrUnknownHead_ThrowTheirRules**: On a real repository, a head without a
configuration file is `ReviewSetConfigMissing`, and a head that does not exist is `RevisionNotFound`. This scenario is
tested by `ReviewPlanner_PlanAsync_RealGit_NoConfigurationOrUnknownHead_ThrowTheirRules`.
