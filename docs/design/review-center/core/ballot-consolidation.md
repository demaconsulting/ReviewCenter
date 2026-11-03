### BallotConsolidation

![Core Structure](CoreView.svg)

#### Purpose

`BallotConsolidation` turns the ballots that the reviewer slots of a review hand in into one agreed standing of every
check and section of the checklist, derives the outcome that standing leads to, and decides whether one ballot answers
everything it has to. It is a pure, static unit with no state, no file system, database, or clock access, and it is
thread-safe. The record of a review, and so what a signature binds to, must depend only on what the reviewers have
handed in and agree on, which is why only submitted ballots take part.

The unit also holds `BallotProjection`, a second static class that projects the ballots of a `ReviewRecord` into the
views that the rest of the application reads: the consolidated standing of the review (`ConsolidatedView`) and the
checklist state of one ballot. Progress, blockers, the inbox, the attestation, and the carry-over of a follow-up read
the checklist through it, and `RecordDigest` consolidates the same submitted ballots, so each of them sees the same
agreed standing.

The unit also holds `BallotEdits`, a third static class of pure functions that compute the record or the ballot that a
ballot edit or a slot change would lead to, so that the services can ask the signature impact rules which signatures
the change would make out of date before anything is stored. It mirrors in memory the rules that the store applies to
the stored rows, and never changes its input.

#### Data Model

**Ballot** (record): the answers of one reviewer slot.

- **SlotId**: `int` — the identity of the slot.
- **Status**: `BallotStatus` — `Draft` or `Submitted`, the two stored statuses.
- **SubmittedAt**: `DateTimeOffset?` and **SubmittedByUsername**: `string?` — set when the ballot is submitted.
- **Answers**: `IReadOnlyList<ChecklistAnswer>` — one answer per checked item.
- **SectionStates**: `IReadOnlyList<ChecklistSectionState>` — whether each section applies to the ballot.
- **State**: `BallotState` — a computed display state: `Submitted` for a submitted ballot, otherwise `Draft`. The third
  member, `NotStarted`, is for a slot that has no ballot at all and is set by callers, never by a `Ballot`.

**ReviewOutcome** (enum): `Approved` or `Rejected`, the outcome that a signature attests. **ReviewConclusion** (enum):
`None`, `Approved`, or `Rejected`, the standing of a review as a whole.

**BallotConsolidationResult** (record): the consolidated standing.

- **Checks**: `IReadOnlyList<CheckConsolidation>` — one entry per check of every section that applies, in checklist
  order. Each holds the item and section identifiers, the **Consensus** (`CheckConsensus`: `Pending`, `Agreed`, or
  `Disputed`), the agreed **Outcome** (`ChecklistOutcome?`, set only when agreed), and the **Outcomes** that each
  submitted ballot gave, by slot, enumerated in ascending slot order whatever the order of the ballots.
- **Sections**: `IReadOnlyList<SectionConsolidation>` — one entry per section, in checklist order, with its
  **Consensus** (`SectionConsensus`: `Applicable`, `NotApplicable`, or `Disputed`).
- **Outcome**: `ReviewOutcome?` — the derived outcome, or null when it cannot be decided.
- **Agreed**, **Disputed**, **Pending**, **Failed**: `int` — the counts of checks (a disputed section counts as one
  dispute, so that the first three add up to the listed checks plus the disputed sections).
- **BallotsSubmitted**, **BallotsTotal**: `int`.

The record is immutable all the way down: the check list and the section list are read-only wrappers, and the
**Outcomes** map of every check is a read-only dictionary that keeps the ascending slot order, so a caller cannot change
the result through a cast.

**ConsolidatedView** (record): the consolidated standing of a record together with the checklist state that reads
from it.

- **State**: `ChecklistState` — the checklist state of the agreed standing: an agreed check is answered with the row of
  the lowest slot that answered it, an agreed not-applicable section has the row of the lowest slot that marked it, and
  a pending or disputed check has no answer. It carries the consolidation (`ChecklistState.Consolidation`).
- **Result**: `BallotConsolidationResult` — the consolidation that the state was built from.

#### Key Methods

**Consolidate**: Consolidates the ballots of a review against the checklist.

- *Parameters*: `ChecklistDefinition definition`; `IReadOnlyCollection<Ballot> ballots` — all ballots, drafts included,
  in any order.
- *Returns*: `BallotConsolidationResult`.
- *Preconditions*: Neither argument is null, `ballots` holds no null element, and no two ballots share a slot.
- *Postconditions*: The result follows the order of the checklist and does not depend on the order of the ballots.
- *Algorithm*:
  - Keep the submitted ballots and index the answers and the not-applicable sections of each once. The answers and
    section states of a draft, and a slot without a ballot, are ignored.
  - A section is `NotApplicable` when every submitted ballot marks it so, `Disputed` when only some do, and `Applicable`
    when none does or when no ballot is submitted. The checks of a section that is not applicable or is disputed are not
    listed, and a disputed section is counted as one dispute.
  - A check of an applicable section is `Pending` when no ballot is submitted or some submitted ballot has no answer
    for it. Otherwise it is `Agreed` when all submitted ballots gave the same outcome, and `Disputed` when they differ.
    Only the outcome is compared; justifications and notes are ignored. An item-level not-applicable answer is an
    outcome like the others: it can agree, it is not a failure, and it never blocks, but it is a dispute against a
    pass.
  - The outcome is null when no ballot is submitted or any check is pending or disputed or any section is disputed.
    Otherwise it is `Rejected` when an agreed check is a fail and `Approved` if none is, also for a checklist without
    checks that has a submitted ballot.

An agreed standing is provisional: slots that have not submitted never change it, but a slot that submits later and
disagrees turns an agreed check into a disputed one.

**IsComplete**: Tells whether a ballot answers every check that it requires.

- *Parameters*: `ChecklistDefinition definition`; `Ballot ballot`.
- *Returns*: `bool` — true when every check of every section that the ballot treats as applicable has an answer of any
  outcome.
- *Preconditions*: Neither argument is null.
- *Algorithm*: For each section that the ballot does not mark as not applicable, look for an answer for each item. The
  status of the ballot is not looked at, so that a draft can be asked whether it is ready to be submitted.

**BallotProjection.Consolidate**: Consolidates the ballots of a record and builds the checklist state that reads from
the agreed standing.

- *Signature*: `ConsolidatedView? Consolidate(ReviewRecord record, int? slotCount = null, bool authorBlocked = false,
  bool noSlot = false)`.
- *Parameters*: `record` — the record, whose `Ballots` hold all ballots, drafts included; `slotCount` — the number of
  slots of the review when the sole ballot of a single-slot review is to count as submitted for progress and blockers,
  and `null` (the default) to read the submitted ballots only, as the digest does; `authorBlocked` and `noSlot` — the
  values of `ChecklistState.AuthorBlocked` and `ChecklistState.NoSlot` for the state.
- *Returns*: `ConsolidatedView?` — `null` when the review has no checklist.
- *Preconditions*: `record` is not null.
- *Algorithm*:
  - Parse the frozen checklist of the record. When `slotCount` is 1 and the record holds exactly one ballot that is a
    draft, read that ballot as submitted. A review with one slot has no second reviewer to agree with and no separate
    hand-in step, so its partly answered checklist shows its true progress; the digest never does this.
  - Call `BallotConsolidation.Consolidate` for the checklist and the ballots.
  - For every agreed check take the answer row of the lowest slot that answered it, and for every agreed
    not-applicable section the section row of the lowest submitted slot that marked it. Drafts never contribute, and a
    pending or disputed check has no row.
  - Build the `ChecklistState` from the sections and the answers, with the consolidation, `AuthorBlocked`, and
    `NoSlot`.

**BallotProjection.ForBallot**: Builds the checklist state of one ballot, a draft included.

- *Signature*: `ChecklistState ForBallot(ChecklistDefinition definition, int slotId, Ballot? ballot,
  bool authorBlocked = false)`.
- *Parameters*: `definition` — the frozen checklist; `slotId` — the slot that the ballot belongs to; `ballot` — the
  ballot, or `null` when the slot has none yet; `authorBlocked` — the value of `ChecklistState.AuthorBlocked`.
- *Returns*: `ChecklistState` — the rows of the ballot (an empty state for no ballot), marked as the viewer's own
  ballot (`IsOwnBallot`), with the slot, and the ballot state: `Draft` or `Submitted`, or `NotStarted` for no ballot.
- *Preconditions*: `definition` is not null.
- *Postconditions*: No consolidation is attached to the state, and it never holds the rows of another slot.

**BallotEdits.WithBallot**: Gives the record with the ballot of a slot replaced or added.

- *Signature*: `ReviewRecord WithBallot(ReviewRecord record, Ballot ballot)`.
- *Parameters*: `record` — the record; `ballot` — the ballot to put in the record.
- *Returns*: `ReviewRecord` — a copy of the record whose ballots are those of the other slots plus the new ballot,
  ordered by slot identity.
- *Preconditions*: Neither argument is null.
- *Postconditions*: The input record is unchanged.

**BallotEdits.WithoutBallot**: Gives the record without the ballot of a slot.

- *Signature*: `ReviewRecord WithoutBallot(ReviewRecord record, int slotId)`.
- *Returns*: `ReviewRecord` — a copy of the record without the ballot of the slot; the other ballots are as they were,
  also when the slot has none.
- *Preconditions*: `record` is not null.

**BallotEdits.WithAnswer**: Gives the ballot a slot would have after an answer is stored.

- *Signature*: `Ballot WithAnswer(Ballot? current, int slotId, ChecklistAnswer answer)`.
- *Parameters*: `current` — the ballot as stored, or `null` when the slot has none yet; `slotId`; `answer` — the answer
  row.
- *Returns*: `Ballot` — a draft of the slot (no hand-in time or user) whose answers are the earlier answers except the
  one for the same check, plus the new answer, ordered by check identifier; the section states are kept.
- *Preconditions*: `answer` is not null.

**BallotEdits.WithSectionState**: Gives the ballot a slot would have after a section state is stored.

- *Signature*: `Ballot WithSectionState(Ballot? current, int slotId, ChecklistSectionState state)`.
- *Returns*: `Ballot` — a draft of the slot whose section states are the earlier states except the one for the same
  section, plus the new state, ordered by section identifier; the answers are kept.
- *Preconditions*: `state` is not null.

**BallotEdits.Reassign**: Gives the ballot a slot would have after a reviewer leaves it.

- *Signature*: `Ballot Reassign(Ballot ballot, IReadOnlyCollection<string> departingUsernames)`.
- *Parameters*: `ballot` — the ballot as stored; `departingUsernames` — the user names of the reviewers who leave.
- *Returns*: `Ballot` — a draft of the slot without the answers and section states that a departing reviewer gave. A
  row carried from an earlier review is kept whoever it names, because it keeps the user name of the earlier author.
  User names are compared ignoring case.
- *Preconditions*: Neither argument is null.

#### Error Handling

`Consolidate` and `IsComplete` raise `ArgumentNullException` for a null argument, and `Consolidate` raises
`ArgumentException` for a ballot list that holds a null element or two ballots of one slot. No other error arises: any
other combination of ballots has a defined standing. Should a ballot repeat the answer to a check or the state of a
section, the last entry wins. `BallotProjection.Consolidate` raises `ArgumentNullException` for a null record and
`BallotProjection.ForBallot` for a null definition; the projection otherwise adds the errors of `Consolidate`, and
`IsComplete` as they are. Every `BallotEdits` function raises `ArgumentNullException` for a null record, ballot, answer,
section state, or list of departing user names (the current ballot of `WithAnswer` and `WithSectionState` may be null);
it has no other error.

#### Dependencies

- **ChecklistDefinition**, **ChecklistAnswer**, **ChecklistSectionState**, **ChecklistOutcome** — Core.
- **ReviewRecord**, **ReviewChecklist**, and **ChecklistState** — Core; read by `BallotProjection` and `BallotEdits`.

#### Callers

- **RecordDigest** — consolidates the submitted ballots of a record to write the standing of the checks, the sections,
  and the outcome.
- **BallotProjection** — read by `ChecklistService`, `SigningService`, `InboxRules`, `FollowUpCarry`, and
  `AttestationService`, which build their view of the checklist from the consolidation of the submitted ballots, and
  shows a reviewer the reviewer's own ballot.
- **Ballot storage and the review pages** — consolidate the ballots that are stored for a review and test a ballot
  before it is submitted (`IsComplete`), through `ChecklistService`.
- **BallotEdits** — called by `ChecklistService` and `SigningService` to preview the record that a ballot edit, a
  hand-in, a ballot taken back, or a slot change would lead to, before the signature impact is computed.
