### ChecklistService

#### Verification Approach

`ChecklistService`, the `DefaultChecklist` definition, and the progress rules of `ChecklistState` are verified with
unit tests defined in `ChecklistServiceTests.cs`, and the strict validation of a definition (`ChecklistValidator`) with
the unit tests in `ChecklistValidatorTests.cs`, which need no store. The service is exercised over an in-memory fake
store and the project-access double `AllowAllProjectAccess` (the access rules themselves are verified by the `ProjectAccess`
unit), so each
test creates its own review and reads the stored result back. The freeze at review creation is verified in
`ReviewServiceTests.cs`. The real database is covered by the `Storage` subsystem.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The default checklist has the 14 sections and 58 items of the review template, in order, with unique ids that
  start with the id of their section, it survives a JSON round trip, and it passes the strict validation; a definition
  that is read from JSON but is incomplete is rejected with a `JsonException`.
- The strict validation accepts a definition only when it is valid JSON of at most 256 KiB, with a version of 1 to 64
  characters, 1 to 50 sections, 1 to 500 checks, no empty section, identifiers of 1 to 32 allowed characters that are
  unique across the definition, titles of at most 200, wording of at most 1000, and a hint of at most 500 characters;
  it reports every violation with its path and never throws for bad content.
- A review's checklist is the snapshot stored when the review was created; a later change to the default or to the
  checklist of the project does not change it, and an unknown review has no checklist.
- A section marked as not applicable needs a reason, which is stored trimmed; marking it applicable again clears the
  reason; an unknown section is refused.
- A Pass is stored without a justification; a Fail or N/A needs one; a justification is trimmed and limited to 2000
  characters; an unknown review or item is refused; answering again replaces the earlier answer.
- The answer carries the user name and the display name of the actor, with the UTC time; a missing actor is refused
  for an answer and for a section change.
- Progress is counted per section and overall; a not-applicable section counts as done and leaves the totals; the
  checklist is complete when every applicable check is answered; the next unanswered check and the first incomplete
  section are found.
- On an approved review, answering a check and changing the applicability of a section are refused with
  `ReviewLocked`, and nothing is stored.
- Answers and not-applicable sections that a follow-up review carried count like the others (a review whose checks are
  all carried is complete); answering a carried check again, or setting a carried section again, stores a row without
  the carried mark and with the new person.

#### Test Scenarios

**ChecklistService_DefaultChecklist_HasTemplateSectionsAndItemIdsInOrder**: The default checklist is read; it has
14 sections and 58 items, with section and item ids in the order of the template. This scenario is tested by
`ChecklistService_DefaultChecklist_HasTemplateSectionsAndItemIdsInOrder`.

**ChecklistService_DefaultChecklist_IdsUniqueAndPrefixedBySection**: Every item id is unique and starts with its
section id. This scenario is tested by `ChecklistService_DefaultChecklist_IdsUniqueAndPrefixedBySection`.

**ChecklistService_DefaultChecklist_RoundTripsThroughJson**: The default checklist is written to JSON and read back
unchanged. This scenario is tested by `ChecklistService_DefaultChecklist_RoundTripsThroughJson`.

**ChecklistService_FromJson_IncompleteDefinition_ThrowsJsonException**: A theory over JSON that parses but is not a
complete definition (`null`, `{}`, missing or null sections, a null section, a section without id, title or items, a
null item, an item without id or text, no version): `ChecklistDefinition.FromJson` and `ReviewChecklist.Parse` both
raise a `JsonException`. This scenario is tested by `ChecklistService_FromJson_IncompleteDefinition_ThrowsJsonException`.

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

**ChecklistService_Mutations_ApprovedReview_ThrowReviewLocked**: A theory over the two changes of a checklist (an
answer and the applicability of a section) on a review whose every slot holds a current signature; each is refused
with `ReviewLocked` and the answers and sections are as they were. The fake store does not judge the lock itself, so
the test shows that the service does. This scenario is tested by
`ChecklistService_Mutations_ApprovedReview_ThrowReviewLocked`.

**ChecklistService_Constructor_NullArguments_Throws**: The service is created with a missing store, project access, or
clock; the constructor throws, so the service cannot exist without the check that the actor may see the project. This
scenario is tested by `ChecklistService_Constructor_NullArguments_Throws`.

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

**ChecklistService_ChecklistEvents_RecordOutcomeAndDigestButNotJustification**: A check is answered and the
applicability of a section is changed; the entries `checklist.answered` and `checklist.applicability-changed` carry
the check or section and the outcome, and a justification appears only as an eight-character digest prefix. This
scenario is tested by `ChecklistService_ChecklistEvents_RecordOutcomeAndDigestButNotJustification`, which is defined
in `AuditEventTests.cs`.

**ChecklistValidator_Validate_DefaultChecklist_IsValid**: The built-in default checklist is checked; it has no error.
This scenario is tested by `ChecklistValidator_Validate_DefaultChecklist_IsValid`.

**ChecklistValidator_Validate_SmallDefinition_IsValid**: A one-section, one-check definition is checked; it is valid.
This scenario is tested by `ChecklistValidator_Validate_SmallDefinition_IsValid`.

**ChecklistValidator_Validate_EmptyText_GivesOneError**: A theory over a missing, an empty, and a white-space text; each
gives one error for the text as a whole. This scenario is tested by `ChecklistValidator_Validate_EmptyText_GivesOneError`.

**ChecklistValidator_Validate_BadJson_GivesOneError**: A theory over text that is not JSON or not a definition; each
gives one error and no exception. This scenario is tested by `ChecklistValidator_Validate_BadJson_GivesOneError`.

**ChecklistValidator_Validate_ParserError_UsesTheCanonicalPath**: A theory over texts with a value of the wrong type at
different positions and with member names in different cases; each gives one error whose path is the canonical one
(`version`, `sections[0]`, `sections[0].items[0].id`, and so on), without `$`, and `json` for a list at the root. This
scenario is tested by `ChecklistValidator_Validate_ParserError_UsesTheCanonicalPath`.

**ChecklistValidator_Validate_TextSize_LimitsAtMaxBytes**: A text of exactly the size limit is accepted by the size
rule; one byte more gives the size error alone, without being parsed. This scenario is tested by
`ChecklistValidator_Validate_TextSize_LimitsAtMaxBytes`.

**ChecklistValidator_Validate_Version_BoundariesAndPresence**: A 64-character version is accepted; a 65-character, an
empty, and a missing version are each refused at the path `version`. This scenario is tested by
`ChecklistValidator_Validate_Version_BoundariesAndPresence`.

**ChecklistValidator_Validate_NoSections_IsRefused**: A definition with no sections is refused at the path `sections`.
This scenario is tested by `ChecklistValidator_Validate_NoSections_IsRefused`.

**ChecklistValidator_Validate_SectionCount_LimitsAtMaxSections**: Fifty sections are accepted and fifty-one are
refused. This scenario is tested by `ChecklistValidator_Validate_SectionCount_LimitsAtMaxSections`.

**ChecklistValidator_Validate_ItemCount_LimitsAtMaxItems**: Five hundred checks in all are accepted and five hundred
and one are refused. This scenario is tested by `ChecklistValidator_Validate_ItemCount_LimitsAtMaxItems`.

**ChecklistValidator_Validate_EmptySection_IsRefused**: A section without checks is refused at the path of its items.
This scenario is tested by `ChecklistValidator_Validate_EmptySection_IsRefused`.

**ChecklistValidator_Validate_IdForm_AcceptsOnlyTheAllowedCharacters**: A theory over identifiers, each used as a section
identifier and as a check identifier: letters, digits, dots, underscores, and hyphens that start with a letter or digit
are accepted; a leading hyphen, dot, or underscore, a space, a slash, a colon, a non-ASCII letter, and a line break are
refused. This scenario is tested by
`ChecklistValidator_Validate_IdForm_AcceptsOnlyTheAllowedCharacters`.

**ChecklistValidator_Validate_IdLength_LimitsAtMaxIdLength**: An identifier of 32 characters is accepted; one of 33
characters and a missing identifier are refused. This scenario is tested by `ChecklistValidator_Validate_IdLength_LimitsAtMaxIdLength`.

**ChecklistValidator_Validate_DuplicateIds_AreReportedAtTheLaterUse**: A check identifier repeated in another section or
in the same section, a check that repeats a section identifier, a repeated section identifier, and an identifier that
differs only in case are each reported at the later use, with the path of that value. This scenario is tested by
`ChecklistValidator_Validate_DuplicateIds_AreReportedAtTheLaterUse`.

**ChecklistValidator_Validate_TitlesTextAndHint_BoundariesAndPresence**: Titles of 200, check wording of 1000, and a hint
of 500 characters are accepted; one more character, an empty value, or a missing value is refused, each with its path.
This scenario is tested by `ChecklistValidator_Validate_TitlesTextAndHint_BoundariesAndPresence`.

**ChecklistValidator_Validate_NullEntries_AreReported**: A missing section entry, a missing list of checks, and a missing
check entry are each reported with their path instead of failing. This scenario is tested by
`ChecklistValidator_Validate_NullEntries_AreReported`.

**ChecklistValidator_Validate_ManyProblems_AreAllReportedTogether**: A definition with several different problems is
checked once; every problem is in the result, each with its path. This scenario is tested by
`ChecklistValidator_Validate_ManyProblems_AreAllReportedTogether`.

**ChecklistValidator_Validate_MemberNamesInAnyCase_AreRead**: A definition written with upper-case member names (as
`ToJson` writes them) is read and valid. This scenario is tested by
`ChecklistValidator_Validate_MemberNamesInAnyCase_AreRead`.

**ChecklistValidator_ParseOrThrow_Invalid_ThrowsWithPathsAndAtMostTenErrors**: An invalid definition with more than ten
errors raises `ChecklistDefinitionInvalid`; the message lists ten errors with their paths and says how many more there
are. This scenario is tested by `ChecklistValidator_ParseOrThrow_Invalid_ThrowsWithPathsAndAtMostTenErrors`.

**ChecklistValidationError_ToString_IsPathAndMessage**: An error is written as its path, a colon, and its message. This
scenario is tested by `ChecklistValidationError_ToString_IsPathAndMessage`.

**Carried answers and sections** are verified in `ReviewServiceFollowUpTests.cs`, which runs the real `ChecklistService`
over the fake store after a follow-up was created.

**ReviewService_FollowUp_ReAnsweringOrResettingSection_ClearsTheCarriedMarker**: A carried check is answered again by
another person and a carried section is set again; the stored rows have the new person and no carried mark, while a
carried answer that was not touched keeps its mark. This scenario is tested by
`ReviewService_FollowUp_ReAnsweringOrResettingSection_ClearsTheCarriedMarker`.

**ReviewService_CreateDraftAsync_FollowUp_CarriedAnswersCountAsAnswered**: A review whose checks are all carried has a
complete checklist with no unanswered check. This scenario is tested by
`ReviewService_CreateDraftAsync_FollowUp_CarriedAnswersCountAsAnswered`.
