### BallotConsolidation

![Core Structure](CoreView.svg)

#### Purpose

`BallotConsolidation` turns the ballots that the reviewer slots of a review hand in into one agreed standing of every
check and section of the checklist, derives the outcome that standing leads to, and decides whether one ballot answers
everything it has to. It is a pure, static unit with no state, no file system, database, or clock access, and it is
thread-safe. The record of a review, and so what a signature binds to, must depend only on what the reviewers have
handed in and agree on, which is why only submitted ballots take part.

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
  submitted ballot gave, by slot.
- **Sections**: `IReadOnlyList<SectionConsolidation>` — one entry per section, in checklist order, with its
  **Consensus** (`SectionConsensus`: `Applicable`, `NotApplicable`, or `Disputed`).
- **Outcome**: `ReviewOutcome?` — the derived outcome, or null when it cannot be decided.
- **Agreed**, **Disputed**, **Pending**, **Failed**: `int` — the counts of checks (a disputed section counts as one
  dispute, so that the first three add up to the listed checks plus the disputed sections).
- **BallotsSubmitted**, **BallotsTotal**: `int`.

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

#### Error Handling

`Consolidate` and `IsComplete` raise `ArgumentNullException` for a null argument, and `Consolidate` raises
`ArgumentException` for a ballot list that holds a null element or two ballots of one slot. No other error arises: any
other combination of ballots has a defined standing. Should a ballot repeat the answer to a check or the state of a
section, the last entry wins.

#### Dependencies

- **ChecklistDefinition**, **ChecklistAnswer**, **ChecklistSectionState**, **ChecklistOutcome** — Core.

#### Callers

- **Ballot storage and the review pages** (a later slice) — consolidate the ballots that are stored for a review and
  test a ballot before it is submitted.
