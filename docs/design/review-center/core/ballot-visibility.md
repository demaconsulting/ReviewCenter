### BallotVisibility

![Core Structure](CoreView.svg)

#### Purpose

`BallotVisibility` decides whether a viewer may see what the other reviewers of a review answered, and the services use
it to keep the answers, and everything that would reveal them, from a viewer who may not. In a review with more than
one reviewer slot, a viewer who has not handed in their own ballot sees who has handed in and who has signed, which is
progress, and nothing of the verdict: no answer of another reviewer, no count of disputed or failed checks, no
provisional outcome, no conclusion text, no discussion about a check, and no audit detail that names an answer. The
point is independent judgment: an answer that is read before the own answer is given turns two verdicts into one.

The unit is a small family of types. `BallotVisibility` holds the rule and the facts it needs. `BallotView` is the
result that the checklist page reads for one viewer. `ThreadCapabilities` tells a page which discussions the viewer may
start. `AuditMasking` replaces the detail of the audit entries that name an answer. The rule is pure and stateless and
all four are thread-safe. Masking is applied where the data is read, never where it is stored: the record, its
signatures, the audit chain, and the export that proves it are untouched, and the enforcement of signing never looks at
the mask, so that a signer is always held to the true standing.

#### Data Model

**BallotView** (record): what the checklist page needs to know about the ballots, for one viewer.

- **IsMasked**: `bool` — the viewer may not see the answers of the others yet.
- **Differing**: `IReadOnlyDictionary<string, IReadOnlyList<BallotAnswerView>>` — the checks that the submitted
  ballots answer differently, by check identifier; empty while the viewer is masked.
- **BallotsSubmitted**, **BallotsTotal**: `int` — how many ballots are handed in out of how many slots; never hidden.
- **CanSubmitAndSign**: `bool` — the own ballot is a complete draft of a review with several slots and nothing the
  viewer may see blocks the signature once it is handed in.
- **CanAssignMe**: `bool` — the viewer is a member who may be a reviewer and holds no slot.

**BallotAnswerView** (record): one answer of a differing check, in slot order: **SlotNumber** (`int`, from 1),
**ReviewerName**, **Outcome** (`ChecklistOutcome`), and **Justification** (`string?`).

**ThreadCapabilities** (record): **CanStartThread** and **CanStartCheckThread**, both `bool`; `ThreadCapabilities.None`
is the value for a viewer who may do neither.

**ChecklistState.IsMasked** and **ReviewApproval.IsMasked**: `bool` — set when the state was built for a viewer who may
not see the others, so that a page shows a note in place of the standing.

#### Key Methods

**BallotVisibility.CanSeeOthers**: The rule.

- *Signature*: `bool CanSeeOthers(bool seesAll, int slotCount, BallotState? ownBallot, bool concluded)`, and an
  overload `bool CanSeeOthers(bool seesAll, ReviewSnapshot snapshot, int? viewerUserId)` that gathers the facts from a
  snapshot.
- *Parameters*: `seesAll` — the viewer leads the review, administers the project or the system, or is an Auditor;
  `slotCount` — the number of slots; `ownBallot` — the state of the ballot of the viewer's slot, or `null` when the
  viewer holds none; `concluded` — the review is approved or rejected.
- *Returns*: `bool`.
- *Algorithm*:
  - A review with at most one slot, and a concluded review, shows everything.
  - Then a viewer who sees everything sees everything.
  - Everybody else, a project member who holds no slot included, sees the others only when the own ballot is
    `Submitted`.
- *Preconditions*: `snapshot` is not null for the overload.

**BallotVisibility.SeesAllInProjectAsync** and **SeesAllInReviewAsync**: The facts of `seesAll`. The first is true for a
viewer who may audit the project (a system administrator, a project administrator, or an Auditor) and is not an API
token. The second adds the lead of the review.

**ChecklistService.GetBallotViewAsync**: Builds the `BallotView` for a viewer.

- *Parameters*: `Actor? viewer`, `int projectId`, `string reviewId`.
- *Returns*: `BallotView?` — `null` when the review or its checklist does not exist.
- *Algorithm*: Read one snapshot; decide the visibility; when the viewer may see the others, consolidate the submitted
  ballots and list, in slot order, the answers of every check on which they differ; count the submitted ballots; decide
  `CanAssignMe` (the viewer may contribute, is enabled, holds no slot, is not the author while independent reviewers are
  required, and is a user or project administrator member) and `CanSubmitAndSign` (the viewer's ballot is a complete
  draft, the review has several slots, and the approval service reports nothing but the missing hand-in).

**ChecklistService.GetAsync** (with the viewer): Gives a viewer who holds no slot and may not see the others an empty
state with `IsMasked` set and no consolidation, in place of the consolidated standing. Without a viewer the read is the
named system-caller path, **ChecklistService.GetForSystemAsync**: it is never masked, no page uses it, and a test pins
the choice. It exists for the callers that act for the system, which are the API and the evidence index today; they
stay unmasked until the slice that masks them (PR 5).

**SigningService.GetApprovalAsync** and **GetApprovalsAsync** (with the viewer): For a masked viewer, leave out the
counts of disputed and failed checks, the outcome, and the conclusion text from the blockers and the state, set
`IsMasked`, and give one fixed sentence that the standing stays hidden until the viewer hands in their own ballot. The
list gives the same result as a single read, and the viewer's role is looked up once for the whole list: the creator of
a review is recognized from the loaded reviews, and an API token is never the creator. A viewer that is not known is
masked in a review with several slots.

**ReviewProgressService** and **InboxService**: Apply the same rule, so that no progress line, blocker, or inbox row
carries a count of disputed or failed checks. The inbox also leaves the discussions about checks out of the waiting
threads and out of the count of news in the row of a masked viewer, so that a new hidden discussion does not make the
row unread. An API token is never the lead, so the creator's full view of the inbox is for a person only.
What stays visible to a masked viewer is progress, not verdict: how many ballots are handed in out of how many, the
names of the people who have not handed in (`ReviewProgress.BallotsSubmitted`, `BallotsRequired`,
`WaitingOnBallots`), the state of each slot's ballot (`SlotStatus.Ballot`), and the inbox rows To answer and Waiting
for others. `ReviewProgress.IsMasked` tells the page to say that the disputes are not shown yet. The inbox never
counts the disputes for a masked viewer (the Disputed row is not even decided for such a viewer), and a viewer who
has not handed in a ballot gets the To answer row, which uses nothing of the other reviewers.

**ThreadService.ListAsync** (with the viewer): For a masked viewer, leave the discussions about checks out of the list.
When the review vanishes between the thread read and the visibility check, the list fails closed: the discussions
about checks are left out.
**ThreadService.GetCapabilitiesAsync**: Gives `ThreadCapabilities` for a viewer: any thread may be started by a
contributor, and a discussion about a check by the lead, a system administrator, a slot owner who has handed in, and
the owner of the only slot.

**AuditMasking.ForViewer**: Masks the entries of an audit page.

- *Signature*: `IReadOnlyList<AuditEntry> ForViewer(IReadOnlyList<AuditEntry> entries, bool canSeeOthers,
  int? viewerUserId)`.
- *Returns*: the entries unchanged when `canSeeOthers` is true; otherwise the same entries in the same order, where
  the detail of an entry that names an answer (`checklist.answered`), a change of the applicability of a section
  (`checklist.applicability-changed`), or a discussion about a check (`thread.started` on a check) is replaced by the
  constants `HiddenAnswerDetail` and `HiddenDiscussionDetail`, and the detail of every later entry of a discussion
  (`thread.replied`, `thread.resolved`, `thread.reopened`, `thread.kind-changed`, `thread.comment-report-changed`) is
  replaced by `HiddenDiscussionActivityDetail`.
- *Algorithm*: Those later entries name only the thread (`thread {id}`). The threads whose `thread.started` entry is
  among the given entries and is not about a check are collected, by project, review and thread number, and a later
  entry keeps its detail only when its thread is one of them. A thread whose start is not among the entries (it is on
  another page) or whose number cannot be read is hidden, so the masking fails closed.
- *Postconditions*: The action, the actor, the time, and the hashes are kept, and an entry of the viewer themselves is
  never changed. A `viewerUserId` of `null` owns no entry.

#### Error Handling

`BallotVisibility.CanSeeOthers` raises `ArgumentNullException` for a null snapshot, and `AuditMasking.ForViewer` for a
null list of entries. A viewer that is not known fails closed: the overloads that take a viewer treat a `null` viewer
as one who has not handed in, so that a caller that forgets the viewer hides more and never less. The one exception is
the checklist read without a viewer, which is the named system-caller path (`GetForSystemAsync`) and is not masked.
Every other
combination of arguments has a defined answer, and the services add no error of their own for masking.

#### Dependencies

- **BallotConsolidation** and **BallotProjection** — the standing that is built for a viewer who may see it.
- **ReviewActorRoles**, **ReviewLock** — the own slot, the lead, and whether the review is concluded.
- **IProjectAccess** — the audit right that stands for an administrator or an Auditor.
- **ReviewSnapshot**, **AuditEntry**, **ChecklistState**, **ReviewApproval** — Core.

#### Callers

- **ChecklistService**, **SigningService**, **ReviewProgressService**, **InboxService**, and **ThreadService** — ask the
  rule when they build what a viewer reads.
- **ChecklistPage**, **ReviewPage**, **ProjectPage**, and **HistoryPage** — show the masked standing, the side-by-side
  answers, and the note that explains the hiding; `HistoryPage` masks the audit entries it reads with `AuditMasking`.
  `AuditPage` and `ProjectAuditPage` are open only to administrators and Auditors, who see every ballot, so they do not
  mask.
- The API, the attestation, and the evidence index use the unmasked reads; masking them is outside this unit for now
  (PR 5).
