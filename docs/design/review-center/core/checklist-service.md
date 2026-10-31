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
(`ToJson`, `FromJson`). `FromJson` also checks the structure it reads: JSON that is invalid, or that lacks the version,
the sections, a section with its id, title or items, or an item with its id or text, raises `JsonException`, so no
reader of a stored definition meets a null part. The applicability hint is display help and is not required.

**DefaultChecklist**: the built-in checklist, `DefaultChecklist.Current`, with version `default-1`. It is the checklist
of every project that has not been given a definition of its own (see `ProjectChecklistService`) and the fallback of
a project without one, and it is the starting point for a project that wants its own. It is seeded from the Continuous
Compliance review template: 14 sections and 58 checks, with the
section and check identifiers and the wording of the template. A check identifier starts with the identifier of its
section (for example `REQ-01` in section `REQ`). Changing the wording means a new version label; reviews are not
affected (see Frozen copy below). The default passes `ChecklistValidator`.

**ChecklistValidator**: the strict check of a definition that a project administrator enters (see
`ProjectChecklistService`) and that `ReviewService` repeats when it takes a project's definition for a new review. It
reads a JSON text and reports every violation, each with the path of the value in the definition (for example
`sections[2].items[1].id: duplicate "code-03"`; indexes start at zero), in the lists of a `ChecklistValidationResult`
(`Errors`, and the parsed `Definition` when there are none), not only the first. The rules: the text is JSON and at most
`ReviewLimits.MaxChecklistBytes` (256 KiB; a larger text is not parsed and only the size is reported); the version is not
empty and at most 64 characters; there are 1 to 50 sections and 1 to 500 checks in all; a section has at least one
check; every identifier of a section or a check has 1 to 32 letters, digits, dots, underscores, and hyphens and starts
with a letter or digit; identifiers are unique across the whole definition, sections and checks together, and compared
without regard to case (the default's own identifiers use upper case, so the rule allows it); a section title and a
check title are not empty and at most 200 characters; the wording of a check is not empty and at most 1000 characters;
the applicability hint has at most 500 characters. The names of the members are read without regard to case, because
`ToJson` writes them in Pascal case; the paths use the lower-case names. A duplicate is reported at its later use.
An error that the reader finds (a value of the wrong type) is reported at the same kind of path: the parser's path
(`$.Sections[0].Items[1].Id`) is converted by removing the `$` prefix and spelling the known member names in their
canonical form, so every consumer sees one path format; a path that names no member is `json`.
`ParseOrThrow` returns the definition or raises `ChecklistDefinitionInvalid`, with the first ten errors in the message.
The limits are in `ReviewLimits`. `FromJson` stays lenient: it is the reader of frozen copies and of stored text, and
the strict rules apply only where a definition enters the system or is taken for a new review.

**ReviewChecklist**: the frozen copy stored for a review: `ProjectId`, `ReviewId`, `Version`, and `Definition`
(the JSON text of the definition). `Parse` returns the definition.

**ChecklistSectionState**: whether a section applies to a review: `ProjectId`, `ReviewId`, `SectionId`,
`Applicable`, `Reason`, `SetBy` (the display name of the person at that time), `SetByUsername` (the user name), and
`SetAt` (UTC), and `CarriedFromReviewId` (the identifier of the earlier review the state was carried over from when the
review was created as a follow-up, otherwise null; see `ReviewService`). A section without a row applies.

**ChecklistAnswer**: the answer to one check: `ProjectId`, `ReviewId`, `ItemId`, `Outcome`, `Justification`,
`AnsweredBy` (the display name of the person at that time), `AnsweredByUsername` (the user name), `AnsweredAt`
(UTC), and `CarriedFromReviewId` (the earlier review the answer was carried over from, otherwise null; a carried
answer keeps the original answerer and time). A check has at most one answer; answering again replaces it.

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
Pass needs no justification and any text given is stored trimmed, or not stored when empty.

**Frozen copy**: `ReviewService` gives every review a `ReviewChecklist` made from the checklist of the project when
the review is created (the definition stored with the project, parsed and validated with `ChecklistValidator`, or
`DefaultChecklist.Current` when the project has none), and the store saves it in the same transaction as the review.
Every later read of the
checklist uses that copy, so changing the default wording, or the checklist of the project, never changes an existing
review, and the version
recorded with the copy tells which wording a review was done against. Reviews created before checklists existed
have no copy; the service reports that they have no checklist rather than inventing one. The database of such
a review does not have the checklist tables either (see the `Storage` design), so development databases must be
recreated.

**Thread linking**: an earlier plan let a check be linked to a comment thread. It was dropped to keep the checklist
simple; the checklist and the threads are independent, and the review page shows both counts side by side.

**Person**: the person who answers a check or decides on a section is the signed-in user, passed as an `Actor` by
the page, never typed text. The display name at that time and the user name are stored with the answer or the
decision, as for a comment author (see `ThreadService`). A null actor is a programming error and raises
`ArgumentNullException`; there is no rule about the name, because the display name of a user was validated when the
user was created.

**Access**: every change (`SetAnswerAsync`, `SetSectionApplicabilityAsync`) first requires that the actor may work in
the project (`IProjectAccess.RequireContributeAsync`), so a user outside the project, like an anonymous or disabled
one, gets `ProjectNotFound`, identical to a project that does not exist, and a read-only member (an auditor) gets
`ActorNotAllowed`, before the review or the input is looked at. Every member who may work in the project can answer.
Reads (`GetAsync`) take no actor; the boundary guards them (every page and API handler first reads the project as the
actor).

**Approval lock**: the checklist is part of what the signatures cover, so while the review is approved (locked; see the
`SigningService` unit) `SetAnswerAsync` and `SetSectionApplicabilityAsync` are refused with `ReviewLocked`. Each calls
`ReviewLock.EnsureUnlockedAsync` after its input checks and before the store write, and the store repeats the check
inside its write transaction. `GetAsync` is not affected. After an administrator reopens the review the checklist can
change again.

**GetAsync**: Reads the checklist of a review.

- *Parameters*: `int projectId`, `string reviewId`, and a cancellation token.
- *Returns*: `Task<ChecklistState?>` — the frozen checklist with the section states and answers, or null when the
  review does not exist or has no checklist.

**SetSectionApplicabilityAsync**: Records whether a section applies.

- *Parameters*: `int projectId`, `string reviewId`, `string sectionId`, `bool applicable`, `string? reason`,
  `Actor actor`, and a cancellation token.
- *Returns*: `Task`.
- *Postconditions*: The section has one state; the reason is stored (trimmed) when the section does not apply and
  cleared when it applies. Nothing is stored when a rule fails (including `ReviewLocked`).

**SetAnswerAsync**: Records the answer to one check.

- *Parameters*: `int projectId`, `string reviewId`, `string itemId`, `ChecklistOutcome outcome`,
  `string? justification`, `Actor actor`, and a cancellation token.
- *Returns*: `Task`.
- *Postconditions*: The check has exactly one answer, with the display name and user name of the actor and the UTC
  time from the clock; an
  earlier answer is replaced. Nothing is stored when a rule fails (including `ReviewLocked`).

The rules are checked in this order, and the first violation is reported: that the review has a checklist
(`ChecklistNotFound`); that the section or check is in the frozen checklist
(`ChecklistItemNotFound`); the justification or reason (`ChecklistJustificationInvalid`); and that the review is not
locked (`ReviewLocked`). An outcome that is not
a defined value is a programming error and throws `ArgumentOutOfRangeException` before any rule is checked.

**Carried answers and sections**: a review that was created as a follow-up starts with answers and not-applicable
section states that were carried over (written by the store with the review, not through these methods). They count as
answers and decisions for `ChecklistState` (progress, `IsComplete`) exactly like answers given in the review, so the
checklist needs no other change. Both write methods store a row with `CarriedFromReviewId` null: whoever answers a
carried check again, or sets a carried section again, makes the row an answer of this review (new answerer, new time,
no marker), even when the outcome and the justification are the same, and the store's write must not keep the old
marker. The audit entries are those of any answer.

**Audit**: Changing the applicability of a section records `checklist.applicability-changed`, and answering a check
records `checklist.answered`. The draft is passed to the store and saved in the same transaction. The detail of an
answer names the item and the outcome and, when there is a justification, only a short hash of it, not the text. See
the `AuditService` unit for the detail formats.

#### Error Handling

Rule violations raise `DomainRuleException` with the matching `DomainRule`. A null store, project access, or clock at
construction, or a null actor, raises `ArgumentNullException`. Store failures propagate unchanged.

#### Dependencies

- **IReviewStore** — reads the frozen checklist, section states and answers, and saves the changes (injected).
- **IProjectAccess** — requires that the actor may work in the project (injected).
- **ReviewLock** (part of the `SigningService` unit) — refuses a change while the review is approved.
- **TimeProvider** — supplies the answer times (injected).

#### Callers

- **ChecklistPage** — reads, answers, and sets applicability.
- **ReviewPage** — reads the progress for the summary and the next-step line.
- **ReviewService** — takes the checklist of the project (validated with `ChecklistValidator`), or
  `DefaultChecklist.Current`, as the copy for a new review.
- **ProjectChecklistService** — validates every definition that it stores with `ChecklistValidator`.
- **ServerHost** — registers the service for the pages.
