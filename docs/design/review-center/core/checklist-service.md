### ChecklistService

![Core Structure](CoreView.svg)

#### Purpose

`ChecklistService` owns the rules for the review checklist: it reads the checklist of a review with its answers,
records whether a section applies to the review, records the answer to a check, hands in the ballot of a reviewer
slot, and holds the free-text review conclusion text of the record. The answers are held per reviewer slot, in a
ballot (see Ballots below). It is also the home of the
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

**ChecklistSectionState**: whether a section applies to a review: `ProjectId`, `ReviewId`, `SlotId`, `SectionId`,
`Applicable`, `Reason`, `SetBy` (the display name of the person at that time), `SetByUsername` (the user name), and
`SetAt` (UTC), and `CarriedFromReviewId` (the identifier of the earlier review the state was carried over from when the
review was created as a follow-up, otherwise null; see `ReviewService`). A section without a row applies. `SlotId` is 0
(`ChecklistAnswer.BaselineSlotId`) for the review-level baseline rows that a follow-up carries (they only seed the
ballots and are not part of the record), and the identity of a reviewer slot for the rows of that slot's ballot, where
the service writes every change.

**ChecklistAnswer**: the answer to one check: `ProjectId`, `ReviewId`, `SlotId`, `ItemId`, `Outcome`, `Justification`,
`AnsweredBy` (the display name of the person at that time), `AnsweredByUsername` (the user name), `AnsweredAt`
(UTC), and `CarriedFromReviewId` (the earlier review the answer was carried over from, otherwise null; a carried
answer keeps the original answerer and time). A check has at most one answer per slot; answering again replaces it.
`SlotId` is 0 (`BaselineSlotId`) for the review-level baseline answers of a follow-up, and the identity of the
reviewer slot for an answer given in a ballot, which is the only kind the service writes. The answerer is kept in the
ballot row but is not covered by the record digest.

**ChecklistBallot**: the status of the ballot of one reviewer slot: `ProjectId`, `ReviewId`, `SlotId` (always above 0),
`Status` (`BallotStatus`: `Draft` or `Submitted`), and, for a submitted ballot, `SubmittedAt` (UTC) and
`SubmittedByUsername`. A slot without a row has not started a ballot. The type is stored by `ReviewStore`; the
`Ballot` value of the record joins the status with the answers and section states of the slot.

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
applicable section is answered, so a checklist whose sections all do not apply is complete. A state also says whose
view it is: `SlotId`, `BallotState` (`NotStarted`, `Draft`, `Submitted`) and `IsOwnBallot` for the ballot of a
reviewer, `NoSlot` for a viewer who holds no slot, `Consolidation` (the agreed standing) for the consolidated view, and
`IsMasked` for a viewer who may not see the standing yet.

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

**Ballots**: the answers and section states are held per reviewer slot, in the ballot of the slot (see
`BallotConsolidation` for how the ballots are consolidated into the agreed standing that the record digest covers). A
ballot is a `Draft` until its reviewer hands it in, and `Submitted` after; only submitted ballots count in the
record. Rules:

- *Slot required*: `SetAnswerAsync`, `SetSectionApplicabilityAsync` and `SubmitBallotAsync` need the actor to be the
  primary or the alternate reviewer of a slot of the review (a user sits in one slot only). Anybody else, an
  administrator included, is refused with `NoReviewerSlot` and nothing is stored. The edit goes into the ballot of that
  slot and into no other.
- *Edit*: every edit of a ballot, also of a submitted one, leaves it a draft. In a review with more than one slot the
  reviewer then hands it in again with `SubmitBallotAsync`.
- *Submit*: `SubmitBallotAsync` refuses an incomplete ballot (a check of an applicable section without an answer) with
  `BallotIncomplete`, and an actor without a slot with `NoReviewerSlot`. It is idempotent: a submitted ballot is
  returned as it is. It records `SubmittedAt` and `SubmittedByUsername`, and the audit entry `ballot.submitted`.
- *Single slot*: in a review with exactly one slot nobody else has to agree and there is no separate hand-in step, so
  the edit that makes the ballot complete hands it in (the store does this in the same transaction, after it tested
  completeness again). Answering is then implicit, as it was before ballots. A ballot that no edit completes (a
  checklist whose sections all do not apply, or one that is completely carried) is handed in with `SubmitBallotAsync`.
- *Own view*: `GetAsync` with a viewer who holds a slot returns the own ballot, drafts included, and never the draft of
  another slot; a viewer without a slot gets the consolidated standing of the submitted ballots in a review with a
  single slot or a concluded review, and a masked state (see `GetAsync`) in a review with several slots that is not
  concluded, unless the viewer is the lead, an administrator or an Auditor.
- *Carried rows*: a follow-up review seeds each new ballot as a draft from the carried baseline rows, which keep their
  mark until the reviewer answers the check again (see Carried answers and sections).

**Person**: the person who answers a check or decides on a section is the signed-in user, passed as an `Actor` by
the page, never typed text. The display name at that time and the user name are stored with the answer or the
decision, as for a comment author (see `ThreadService`). A null actor is a programming error and raises
`ArgumentNullException`; there is no rule about the name, because the display name of a user was validated when the
user was created.

**Access**: every change (`SetAnswerAsync`, `SetSectionApplicabilityAsync`, `SubmitBallotAsync`) first requires that the
actor may work in the project (`IProjectAccess.RequireContributeAsync`), so a user outside the project, like an
anonymous or disabled one, gets `ProjectNotFound`, identical to a project that does not exist, and a read-only member
(an auditor) gets `ActorNotAllowed`, before the review or the input is looked at. A member who may work in the project
and holds a slot of the review can answer (see Ballots). Reads (`GetAsync`) take no actor; the boundary guards them
(every page and API handler first reads the project as the actor).

**Approval lock**: the checklist is part of what the signatures cover, so while the review is approved (locked; see the
`SigningService` unit) `SetAnswerAsync`, `SetSectionApplicabilityAsync` and `SubmitBallotAsync` are refused with
`ReviewLocked`. Each calls `ReviewLock.EnsureUnlockedAsync` after its input checks and before the store write, and the
store repeats the check inside its write transaction. `GetAsync` is not affected. After an administrator reopens the
review the checklist can change again.

**Signature impact**: a change after somebody has signed can make that signature out of date (see "Signature impact" in
the `SigningService` unit). `SetAnswerAsync`, `SetSectionApplicabilityAsync` and `SubmitBallotAsync` therefore take an
optional last argument `IReadOnlyCollection<int>? acknowledgedSlotIds`, after the cancellation token. Each builds the
record as it would be after the change in memory (`BallotEdits`) and passes it with the snapshot that
`ReviewLock.EnsureUnlockedAsync` returned to `SignatureImpact.RequireAcknowledged`, together with the slot of the actor.
The digest decides, and it covers the agreed standing of the submitted ballots and no answerer, so:

- A draft does not count. An edit of a ballot in a review with more than one slot normally changes nothing the digest
  covers; it needs an acknowledgement only when taking a submitted ballot back to a draft changes the agreed standing.
  The impact is therefore checked where the standing changes: at the hand-in. `SubmitBallotAsync` refuses with
  `SignaturesWouldBecomeOutOfDate` when the record with the ballot submitted has another digest and a current signature
  of another slot is not in `acknowledgedSlotIds`, and nothing is stored. A ballot that agrees with the others leaves
  the digest alone and needs no acknowledgement.
- In a review with one slot the edit that completes the ballot hands it in, so the edit is checked as the hand-in, with
  the ballot as the store will leave it.
- The signature of the own slot of the reviewer never needs an acknowledgement: the change is the one that withdraws
  it. The signatures of the other slots are listed, each by slot number and signer.
- Another reviewer giving the same value does not change the digest (the answerer is not in it), so a signature stays.
  Marking a section applicable that has no row, or saving a ballot to the value it already has, does not either.

The check is the last one, after `ReviewLocked` and after the slot test, and nothing is stored when it fails.

**Author rule**: while the project's `RequireNonAuthorChecklistAnswers` setting is on (the default, also for projects
that existed before the rule was enforced; see `ProjectService`), the author of the review (`Review.CreatedBy`, the
owner of the token for an API-token review, compared ignoring case; a review without an author is not restricted)
cannot answer a check or change the applicability of a section: `SetAnswerAsync`, `SetSectionApplicabilityAsync` and
`SubmitBallotAsync`
throw `AuthorCannotAnswer` ("You created this review, so you cannot answer its checklist..."). The check runs right
after the access check, before the checklist is read and before the lock check, so an author of an approved review is
told about the rule and not about the lock, and nothing is stored. `GetAsync` takes an optional `viewerUsername`; when
it is the author and the rule is on, the returned `ChecklistState.AuthorBlocked` is true, so the page can disable the
controls without a second read. Threads
and comments are not affected by the rule.

**GetAsync**: Reads the checklist of a review.

- *Parameters*: `int projectId`, `string reviewId`, a cancellation token, and the optional `string? viewerUsername`.
- *Returns*: `Task<ChecklistState?>` — the frozen checklist with the section states and answers, or null when the
  review does not exist or has no checklist.
- *Viewer dependent*: with a `viewerUsername` that is the primary or the alternate reviewer of a slot, the state is the
  own ballot of that slot, drafts included (`IsOwnBallot`, `BallotState`, `SlotId`), and never the ballot of another
  slot. For a viewer who holds no slot it is the consolidated standing of the submitted ballots
  (`BallotProjection.Consolidate`) when the viewer may see the others (a review with a single slot, a concluded
  review, or a viewer who is the lead, an administrator or an Auditor): an agreed check is answered with the row of the
  lowest slot that answered it, and a disputed or pending check has no answer. In a review with several slots that is
  not concluded, a viewer without a slot who is none of those gets a state with no answers and no consolidation, and
  `IsMasked` is true, because the standing would give away the answers of the others. `NoSlot` is true when a viewer is
  given and holds no slot. A review with
  exactly one slot is read with its sole ballot as submitted, for the progress counts only, so a partly answered
  single-slot draft counts in the progress. `AuthorBlocked` is set as described above.
- *System callers*: without a `viewerUsername` no viewer is asked about, so nothing is masked and `AuthorBlocked` is not
  set: the consolidated standing is returned. This is an explicit, named path, `GetForSystemAsync`, for callers that
  act for the system and not for a person (a report, a carry-over); a page that shows the state to a person always
  passes the viewer. The API and the evidence read through the system path today; masking them is deferred to a later
  slice (PR 5). A test pins that the system path is not masked and a viewer's read is.

**SetSectionApplicabilityAsync**: Records whether a section applies in the ballot of the slot of the actor.

- *Parameters*: `int projectId`, `string reviewId`, `string sectionId`, `bool applicable`, `string? reason`,
  `Actor actor`, a cancellation token, and the optional `acknowledgedSlotIds`.
- *Returns*: `Task`.
- *Postconditions*: The section has one state in the ballot of the slot; the reason is stored (trimmed) when the section
  does not apply and cleared when it applies. The ballot is a draft, or submitted when the review has one slot and the
  change completes it. Nothing is stored when a rule fails (including `ReviewLocked`).

**SetAnswerAsync**: Records the answer to one check in the ballot of the slot of the actor.

- *Parameters*: `int projectId`, `string reviewId`, `string itemId`, `ChecklistOutcome outcome`,
  `string? justification`, `Actor actor`, a cancellation token, and the optional `acknowledgedSlotIds`.
- *Returns*: `Task`.
- *Postconditions*: The check has exactly one answer in the ballot of the slot, with the display name and user name of
  the actor and the UTC time from the clock; an earlier answer of that slot is replaced. The ballot is a draft, or
  submitted when the review has one slot and the answer completes it. Nothing is stored when a rule fails (including
  `ReviewLocked`).

**SubmitBallotAsync**: Hands in the ballot of the slot of the actor, so it counts in the record.

- *Parameters*: `int projectId`, `string reviewId`, `Actor actor`, a cancellation token, and the optional
  `acknowledgedSlotIds`.
- *Returns*: `Task<Ballot>` — the submitted ballot; one that was submitted already is returned as it is.
- *Postconditions*: The ballot is `Submitted` with the time and the user name of the actor, and `ballot.submitted` is
  audited. Nothing is stored when a rule fails.

The rules are checked in this order, and the first violation is reported: that the author is not refused by the
project rule (`AuthorCannotAnswer`); that the review has a checklist
(`ChecklistNotFound`); for the two edits, that the section or check is in the frozen checklist
(`ChecklistItemNotFound`) and the justification or reason (`ChecklistJustificationInvalid`); that the review is not
locked (`ReviewLocked`); that the actor holds a slot (`NoReviewerSlot`); for `SubmitBallotAsync`, that the ballot is
complete (`BallotIncomplete`); and that no current signature of another slot would silently become out of date
(`SignaturesWouldBecomeOutOfDate`). An outcome that is not
a defined value is a programming error and throws `ArgumentOutOfRangeException` before any rule is checked.

**Carried answers and sections**: a review that was created as a follow-up starts with answers and not-applicable
section states that were carried over (written by the store with the review as baseline rows, not through these
methods). The store seeds the draft ballot of each new slot from them, and a carried answer never hands the ballot in.
They count as answers and decisions for `ChecklistState` (progress, `IsComplete`) exactly like answers given in the
review, so the checklist needs no other change. The write methods store a row with `CarriedFromReviewId` null:
whoever answers a carried check again, or sets a carried section again, makes the row an answer of this review (new
answerer, new time, no marker), even when the outcome and the justification are the same, and the store's write must
not keep the old marker. The audit entries are those of any answer.

**Audit**: Changing the applicability of a section records `checklist.applicability-changed`, and answering a check
records `checklist.answered`. The draft is passed to the store and saved in the same transaction. The detail of an
answer names the item and the outcome and, when there is a justification, only a short hash of it, not the text. A
hand-in records `ballot.submitted` (also when the answer of a single-slot review hands the ballot in, written by the
store), and an edit that takes a submitted ballot back to a draft records `ballot.reopened`. See the `AuditService`
unit for the detail formats.

**Review conclusion text**: the free-text conclusion of the record, `Review.ReviewConclusionText` (plain text, at most
`ReviewLimits.MaxConclusionTextLength` = 4000 characters). It is never the enumerated review conclusion (approved or
rejected) of a ballot, which is a decision of its own; the word "conclusion" alone is not used for the text. The text
has no web page or API yet: it is written through the two methods below and shown read-only later.

- `CanSetReviewConclusionTextAsync(Actor? actor, int projectId, string reviewId, ...)` answers, without throwing,
  whether a person may write the text: a signed-in person (not an API token) who may contribute to the project and is
  the primary or alternate reviewer of any slot of the review, or its lead (the creator who can still contribute, or an
  administrator of the project; see `ReviewActorRoles`). It is `false` for no actor, an API token, an auditor, a
  member who is neither, a person outside the project and a review that does not exist. It says nothing about the lock.
- `SetReviewConclusionTextAsync(int projectId, string reviewId, string? conclusionText, Actor actor, ct,
  acknowledgedSlotIds)` returns `Task<bool>`: `true` when the text changed, `false` when the review already had this
  text (nothing is stored, recorded or acknowledged). The checks are made in this order and the first violation is
  reported: the actor may contribute (`ProjectNotFound` for a person outside the project, `ActorNotAllowed` for an
  auditor); the actor is not an API token (`ActorNotAllowed`); the review exists (`ReviewNotFound`); the actor is a
  reviewer of a slot or the lead (`ActorNotAllowed`); the text is valid (`ConclusionTextInvalid`); the review is not
  concluded (`ReviewLocked`; `ReviewLock.IsConcluded` on the snapshot that was read); no current signature would become
  out of date without acknowledgement (`SignaturesWouldBecomeOutOfDate`, naming each slot and signer, computed with
  `SignatureImpact.RequireAcknowledged` on the record with the new text). Then the store applies the change as a
  compare-and-set on the text that was read; when someone changed it meanwhile the service throws `RecordChanged` and
  nothing is stored.
- *Normalization*: `\r\n` becomes `\n`. Then a control character other than a line feed or a tab (a lone carriage
  return included) is refused with `ConclusionTextInvalid`; this check runs before the trim, so a carriage return at
  the start or the end of the text cannot be trimmed away. Then leading and trailing white space is removed, a blank
  text means no text, and the text has at most 4000 characters. The stored text, its length and the digest are
  therefore the same whatever the browser sent.
- *Digest*: the text is part of the record digest (the line `conclusion-text`; see `RecordDigest`), so a change while a
  signature is current needs the acknowledgement, and a concluded review is locked against it. It is not carried over
  to a follow-up (see `ReviewService`).
- *Planned*: the rule that a rejected review needs a conclusion text before it can be signed, and the web pages and the
  attestation that show the text, are separate, later changes and are not part of this unit yet.

**Audit of the review conclusion text**: a change records `review.conclusion-changed` (the constant
`ReviewConclusionTextChangedAction`), saved with the change in the same transaction. The detail is
`length {old} -> {new}` (no text counts as 0) and never contains the text.

#### Error Handling

Rule violations raise `DomainRuleException` with the matching `DomainRule`; the ballot rules are `NoReviewerSlot` (the
actor holds no slot) and `BallotIncomplete` (a hand-in with an unanswered check). A null store, project access, user
store, or
clock at construction, or a null actor, raises `ArgumentNullException`. Store failures propagate unchanged.

#### Dependencies

- **IReviewStore** — reads the snapshot (frozen checklist, slots, ballots), and saves the ballot edits and the hand-in
  (injected).
- **BallotVisibility** — decides whether the viewer may see the answers of the other reviewers; the state and the ballot
  view that `GetAsync` and `GetBallotViewAsync` give to a viewer who may not hold nothing of them.
- **IProjectAccess** — requires that the actor may work in the project (injected).
- **IUserStore** — resolves the user name of a person to the identity that holds a reviewer slot, for the ballot of the
  actor and the view of a viewer, and for the review conclusion text (injected).
- **BallotConsolidation**, **BallotProjection** and **BallotEdits** — test that a ballot is complete, build the
  consolidated or the own view, and compute the record an edit or a hand-in leads to (static, no state).
- **ReviewLock** and **SignatureImpact** (part of the `SigningService` unit) — refuse a change while the review is
  approved, and refuse one that would make a current signature out of date without acknowledgement.
- **TimeProvider** — supplies the answer and hand-in times (injected).

#### Callers

- **ChecklistPage** — reads, answers, sets applicability, and hands in the ballot.
- **ReviewPage** — reads the progress for the summary and the next-step line.
- **ReviewService** — takes the checklist of the project (validated with `ChecklistValidator`), or
  `DefaultChecklist.Current`, as the copy for a new review.
- **ProjectChecklistService** — validates every definition that it stores with `ChecklistValidator`.
- **ServerHost** — registers the service for the pages.
