### ChecklistService

#### Verification Approach

`ChecklistService`, the `DefaultChecklist` definition, and the progress rules of `ChecklistState` are verified with
unit tests defined in `ChecklistServiceTests.cs`. The service is exercised over an in-memory fake store, so each
test creates its own review and reads the stored result back. The freeze at review creation is verified in
`ReviewServiceTests.cs`. The real database is covered by the `Storage` subsystem.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The default checklist has the 14 sections and 58 items of the review template, in order, with unique ids that
  start with the id of their section, and it survives a JSON round trip.
- A review's checklist is the snapshot stored when the review was created; a later change to the default does not
  change it, and an unknown review has no checklist.
- A section marked as not applicable needs a reason, which is stored trimmed; marking it applicable again clears the
  reason; an unknown section is refused.
- A Pass is stored without a justification; a Fail or N/A needs one; a justification is trimmed and limited to 2000
  characters; an unknown review or item is refused; answering again replaces the earlier answer.
- The answer carries the user name and the display name of the actor, with the UTC time; a missing actor is refused
  for an answer and for a section change.
- Progress is counted per section and overall; a not-applicable section counts as done and leaves the totals; the
  checklist is complete when every applicable check is answered; the next unanswered check and the first incomplete
  section are found.

#### Test Scenarios

**ChecklistService_DefaultChecklist_HasTemplateSectionsAndItemIdsInOrder**: The default checklist is read; it has
14 sections and 58 items, with section and item ids in the order of the template. This scenario is tested by
`ChecklistService_DefaultChecklist_HasTemplateSectionsAndItemIdsInOrder`.

**ChecklistService_DefaultChecklist_IdsUniqueAndPrefixedBySection**: Every item id is unique and starts with its
section id. This scenario is tested by `ChecklistService_DefaultChecklist_IdsUniqueAndPrefixedBySection`.

**ChecklistService_DefaultChecklist_RoundTripsThroughJson**: The default checklist is written to JSON and read back
unchanged. This scenario is tested by `ChecklistService_DefaultChecklist_RoundTripsThroughJson`.

**ReviewService_CreateDraftAsync_ValidInput_AttachesDefaultChecklistSnapshot**: A review is created; it carries the
version and the sections of the default checklist. This scenario is tested by
`ReviewService_CreateDraftAsync_ValidInput_AttachesDefaultChecklistSnapshot`.

**ChecklistService_Get_AfterDefaultChanges_ReturnsFrozenSnapshot**: A review holds a snapshot that differs from the
current default; the service returns the snapshot. This scenario is tested by
`ChecklistService_Get_AfterDefaultChanges_ReturnsFrozenSnapshot`.

**ChecklistService_Get_UnknownReview_ReturnsNull**: The checklist of an unknown review is requested; nothing is
returned. This scenario is tested by `ChecklistService_Get_UnknownReview_ReturnsNull`.

**ChecklistService_SetSectionApplicability_NotApplicableWithoutReason_Throws**: A section is marked as not
applicable with a null, empty or blank reason; the call is refused. This scenario is tested by
`ChecklistService_SetSectionApplicability_NotApplicableWithoutReason_Throws`.

**ChecklistService_SetSectionApplicability_NotApplicableWithReason_StoresTrimmedReason**: A section is marked as not
applicable with a padded reason; the trimmed reason is stored. This scenario is tested by
`ChecklistService_SetSectionApplicability_NotApplicableWithReason_StoresTrimmedReason`.

**ChecklistService_SetSectionApplicability_Applicable_ClearsReason**: A not-applicable section is marked applicable
again; the reason is cleared. This scenario is tested by
`ChecklistService_SetSectionApplicability_Applicable_ClearsReason`.

**ChecklistService_SetSectionApplicability_UnknownSection_ThrowsChecklistItemNotFound**: An unknown section is
marked; the call is refused with the not-found rule. This scenario is tested by
`ChecklistService_SetSectionApplicability_UnknownSection_ThrowsChecklistItemNotFound`.

**ChecklistService_SetAnswer_PassWithoutJustification_Stores**: A Pass is stored without a justification. This
scenario is tested by `ChecklistService_SetAnswer_PassWithoutJustification_Stores`.

**ChecklistService_SetAnswer_FailOrNotApplicableWithoutJustification_Throws**: A Fail or N/A is stored without a
justification; the call is refused. This scenario is tested by
`ChecklistService_SetAnswer_FailOrNotApplicableWithoutJustification_Throws`.

**ChecklistService_SetAnswer_JustificationTooLong_Throws**: A justification over 2000 characters is refused, while
one of exactly 2000 is accepted. This scenario is tested by
`ChecklistService_SetAnswer_JustificationTooLong_Throws`.

**ChecklistService_SetAnswer_UnknownItem_ThrowsChecklistItemNotFound**: An answer for an unknown item is refused.
This scenario is tested by `ChecklistService_SetAnswer_UnknownItem_ThrowsChecklistItemNotFound`.

**ChecklistService_SetAnswer_UnknownReview_ThrowsChecklistNotFound**: An answer for an unknown review is refused.
This scenario is tested by `ChecklistService_SetAnswer_UnknownReview_ThrowsChecklistNotFound`.

**ChecklistService_SetAnswer_UndefinedOutcome_Throws**: An answer with an outcome value that is not defined is
refused with an `ArgumentOutOfRangeException`, and nothing is stored. This scenario is tested by
`ChecklistService_SetAnswer_UndefinedOutcome_Throws`.

**ChecklistService_SetAnswer_ReanswerWithPaddedJustification_OverwritesAndTrims**: A check is answered twice, the
second time with a padded justification; one answer remains, with the trimmed text. This scenario is tested by
`ChecklistService_SetAnswer_ReanswerWithPaddedJustification_OverwritesAndTrims`.

**ChecklistService_SetAnswer_NullActor_Throws**: An answer and a section change are made without an actor; both are
refused with an `ArgumentNullException` and nothing is stored. This scenario is tested by
`ChecklistService_SetAnswer_NullActor_Throws`.

**ChecklistService_SetAnswer_StoresActorAndUtcTime**: An answer is stored as an actor; it holds the display name and
the user name of the actor and the UTC time of the clock. This scenario is tested by
`ChecklistService_SetAnswer_StoresActorAndUtcTime`.

**ChecklistService_Constructor_NullArguments_Throws**: The service is created with a missing argument; the
constructor throws. This scenario is tested by `ChecklistService_Constructor_NullArguments_Throws`.

**ChecklistState_Progress_CountsAnsweredPerSectionAndOverall**: Some checks are answered; each section and the
overall progress show the counts. This scenario is tested by
`ChecklistState_Progress_CountsAnsweredPerSectionAndOverall`.

**ChecklistState_Progress_NotApplicableSection_CountsAsDoneAndLeavesTotal**: A section is not applicable; it counts
as done and its checks leave the total. This scenario is tested by
`ChecklistState_Progress_NotApplicableSection_CountsAsDoneAndLeavesTotal`.

**ChecklistState_Progress_AnswersInNotApplicableSection_Ignored**: A not-applicable section holds answers; they are
not counted. This scenario is tested by `ChecklistState_Progress_AnswersInNotApplicableSection_Ignored`.

**ChecklistState_Progress_AllApplicableAnswered_IsComplete**: Every applicable check is answered; the checklist is
complete. This scenario is tested by `ChecklistState_Progress_AllApplicableAnswered_IsComplete`.

**ChecklistState_Progress_AllSectionsNotApplicable_IsComplete**: Every section is not applicable; the checklist is
complete with no checks to answer. This scenario is tested by
`ChecklistState_Progress_AllSectionsNotApplicable_IsComplete`.

**ChecklistState_NextUnansweredItemId_SkipsAnsweredAndNotApplicableSections**: The next unanswered check is the first
check that is neither answered nor in a not-applicable section. This scenario is tested by
`ChecklistState_NextUnansweredItemId_SkipsAnsweredAndNotApplicableSections`.

**ChecklistState_FirstIncompleteSectionId_ReturnsFirstSectionWithUnanswered**: The first incomplete section is the
first applicable section with an unanswered check. This scenario is tested by
`ChecklistState_FirstIncompleteSectionId_ReturnsFirstSectionWithUnanswered`.
