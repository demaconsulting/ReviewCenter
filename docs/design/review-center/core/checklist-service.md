### ChecklistService

![Core Structure](CoreView.svg)

#### Purpose

`ChecklistService` owns the rules for the review checklist: it reads the checklist of a review with its answers,
records whether a section applies to the review, and records the answer to a check. It is also the home of the
checklist types (`ChecklistDefinition`, `ChecklistSection`, `ChecklistItem`, `DefaultChecklist`, `ReviewChecklist`,
`ChecklistSectionState`, `ChecklistAnswer`, `ChecklistOutcome`) and of `ChecklistState` and `ChecklistProgress`,
which derive what is left to do.

#### Data Model

**ChecklistDefinition**: a record of a `Version` label and a list of `ChecklistSection` (`Id`, `Title`, an
applicability hint, and a list of `ChecklistItem` with `Id` and `Text`). It serializes to and from JSON
(`ToJson`, `FromJson`).

**DefaultChecklist**: the checklist given to every new review, `DefaultChecklist.Current`, with version
`default-1`. It is seeded from the Continuous Compliance review template: 14 sections and 58 checks, with the
section and check identifiers and the wording of the template. A check identifier starts with the identifier of its
section (for example `REQ-01` in section `REQ`). Changing the wording means a new version label; reviews are not
affected (see Frozen copy below).

**ReviewChecklist**: the frozen copy stored for a review: `ProjectId`, `ReviewId`, `Version`, and `Definition`
(the JSON text of the definition). `Parse` returns the definition.

**ChecklistSectionState**: whether a section applies to a review: `ProjectId`, `ReviewId`, `SectionId`,
`Applicable`, `Reason`, `SetBy`, and `SetAt` (UTC). A section without a row applies.

**ChecklistAnswer**: the answer to one check: `ProjectId`, `ReviewId`, `ItemId`, `Outcome`, `Justification`,
`AnsweredBy`, and `AnsweredAt` (UTC). A check has at most one answer; answering again replaces it.

**ChecklistOutcome**: a closed enumeration with the values `Pass`, `Fail`, and `NotApplicable` (shown as N/A). It
is stored as text.

**ChecklistProgress**: a value of `Answered` and `Total` with the derived `Unanswered` and `IsDone`.

**ChecklistState**: the frozen definition together with the section states and the answers of one review. It
derives, without any storage access: `SectionProgress(sectionId)` and `Overall`, the `IsComplete` flag, the
number of sections that do not apply, the applicability and reason of a section, the answer to a check,
`FirstIncompleteSectionId`, and `NextUnansweredItemId` (which can skip further sections, for example those a page has
just switched off but not yet saved). The progress rules are: a failed check is answered (the
review found a problem, which is an answer); a section that does not apply counts as done, contributes no checks to
the totals, and any answers kept for it are ignored; the checklist is complete when every check of every
applicable section is answered, so a checklist whose sections all do not apply is complete.

Rules for the input: a justification (for Fail and N/A) or a reason (for a section that does not apply) is not empty
after trimming and has at most 2000 characters (`ReviewLimits.MaxJustificationLength`); it is stored trimmed. A
Pass needs no justification and any text given is stored trimmed, or not stored when empty. The name of the person
is not empty after trimming and has at most 100 characters (the same limit as a comment author); it is stored
trimmed.

**Frozen copy**: `ReviewService` gives every review a `ReviewChecklist` made from `DefaultChecklist.Current` when
the review is created, and the store saves it in the same transaction as the review. Every later read of the
checklist uses that copy, so changing the default wording never changes an existing review, and the version
recorded with the copy tells which wording a review was done against. Reviews created before checklists existed
have no copy; the service reports that they have no checklist rather than inventing one. The database of such
a review does not have the checklist tables either (see the `Storage` design), so development databases must be
recreated.

**Thread linking**: an earlier plan let a check be linked to a comment thread. It was dropped to keep the checklist
simple; the checklist and the threads are independent, and the review page shows both counts side by side.

**Temporary identity**: there is no sign-in yet, so the name saved with an answer or a section decision is free text
that is not verified. It is in the same position as a comment author, and is to be replaced by the signed-in user
when identity is added (slice 4).

#### Key Methods

**GetAsync**: Reads the checklist of a review.

- *Parameters*: `int projectId`, `string reviewId`, and a cancellation token.
- *Returns*: `Task<ChecklistState?>` — the frozen checklist with the section states and answers, or null when the
  review does not exist or has no checklist.

**SetSectionApplicabilityAsync**: Records whether a section applies.

- *Parameters*: `int projectId`, `string reviewId`, `string sectionId`, `bool applicable`, `string? reason`,
  `string? setBy`, and a cancellation token.
- *Returns*: `Task`.
- *Postconditions*: The section has one state; the reason is stored (trimmed) when the section does not apply and
  cleared when it applies. Nothing is stored when a rule fails.

**SetAnswerAsync**: Records the answer to one check.

- *Parameters*: `int projectId`, `string reviewId`, `string itemId`, `ChecklistOutcome outcome`,
  `string? justification`, `string? answeredBy`, and a cancellation token.
- *Returns*: `Task`.
- *Postconditions*: The check has exactly one answer, with the trimmed name and the UTC time from the clock; an
  earlier answer is replaced. Nothing is stored when a rule fails.

The rules are checked in this order, and the first violation is reported: the name (`ChecklistAnsweredByInvalid`);
that the review has a checklist (`ChecklistNotFound`); that the section or check is in the frozen checklist
(`ChecklistItemNotFound`); and the justification or reason (`ChecklistJustificationInvalid`). An outcome that is not
a defined value is a programming error and throws `ArgumentOutOfRangeException` before any rule is checked.

#### Error Handling

Rule violations raise `DomainRuleException` with the matching `DomainRule`. A null store or clock at construction
raises `ArgumentNullException`. Store failures propagate unchanged.

#### Dependencies

- **IReviewStore** — reads the frozen checklist, section states and answers, and saves the changes (injected).
- **TimeProvider** — supplies the answer times (injected).

#### Callers

- **ChecklistPage** — reads, answers, and sets applicability.
- **ReviewPage** — reads the progress for the summary and the next-step line.
- **ReviewService** — takes `DefaultChecklist.Current` as the copy for a new review.
- **ServerHost** — registers the service for the pages.
