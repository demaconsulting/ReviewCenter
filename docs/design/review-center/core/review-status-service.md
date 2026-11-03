### ReviewStatusService

![Core Structure](CoreView.svg)

#### Purpose

`ReviewStatusService` answers "Where are we?" for one viewer of a review. A review has several reviewers, several kinds
of open work, and a signature that can lapse, and a person who opens it wants to know what is left and what to do. The
service gathers that into one `ReviewStatus` that the review page shows at the top: who has handed in a ballot and who
has signed, which checks the reviewers answered differently, how many questions and findings are open, the one thing the
viewer should do next, and, for a reviewer who signed and then saw the report change, what changed since the signature.

The status is computed on demand from what the other services already give the same viewer, and nothing of it is
stored, so it cannot disagree with the rest of the page. It holds the state of a ballot, and the content of a ballot
(the answer and the justification given to a check the reviewers answered differently, the failed checks of a rejected
review) only for a viewer who may see the answers of the other reviewers (see *BallotVisibility*). While a viewer may
not see them yet it lists no disputed check, no answer, no failed check and no outcome, counts no discussion about a
check, and names no answer in a change.

The unit is a small family of types. `ReviewStatusService` builds the status. `ReviewStatus` and the records it holds are
the result. `ReviewNextAction` is the pure rule that decides the one next action. `ReviewChangeRules` is the internal,
pure rule that turns audit entries into the plain-language changes since a signature. All of them are stateless and
thread-safe.

#### Data Model

**ReviewStatus** (record): **IsMasked** (`bool`, the viewer may not see the others yet), **IsConcluded**,
**HasChecklist**, **Reviewers** (`IReadOnlyList<ReviewerStatus>`), **DisputedChecks** (`IReadOnlyList<DisputedCheck>`,
empty while masked), **OpenQuestions** and **OpenFindings** (`int`, among the threads the viewer may see),
**NextAction** (`ReviewNextAction`), **ChangesSinceSigning** (`IReadOnlyList<SignatureChanges>`), **Caution**
(`string?`, the early-signing caution: set only when the next action is to sign and another reviewer has not handed in a
ballot yet, naming that reviewer, otherwise `null`), and **OutcomeNote** (`string?`, the plain statement that the review
will be Rejected and which check the reviewers failed, taken from `ReviewApproval.RejectionNote`; `null` while masked,
when concluded, or when the review is not heading for Rejected).

**ReviewerStatus** (record): **SlotId**, **Number**, **Name**, **Ballot** (`BallotState`: not started, draft, or
submitted), **Signature** (`SlotSignatureState`: not signed, signed, or signed and then the review changed, which the
code names `OutOfDate`), and **IsYou**. The ballot state of a reviewer is process, not content, so it stays visible to
a masked viewer too: a draft shows that work has started and says nothing about any answer (the visibility rule hides
content, not progress).

**DisputedCheck** (record): **ItemId**, **Text**, **CanDiscuss** (the viewer may start a discussion about the check and
none is open), and **Answers** (`IReadOnlyList<BallotAnswerView>`, never `null`): the answer of every submitted ballot to
the check in slot order, including the viewer's own, each as `BallotAnswerView(SlotNumber, ReviewerName, Outcome,
Justification)`, where `Justification` is `null` when none was written. It is the same data as the differing answers of
the ballot view, so it exists only for a viewer who may see the answers of the others; a masked viewer gets no
`DisputedCheck` at all.

**SignatureChanges** (record): **SlotId**, **Number**, **SignerName**, **SignedAt**, and **Changes**
(`IReadOnlyList<ReviewChange>`, oldest first). **ReviewChange** (record): **At**, **By**, and **Reason**, a fixed
sentence in plain words.

**ReviewNextAction** (record): **Kind** (`NextActionKind`), **Label** (the words), **Target** (`NextActionTarget`: none,
the reviewers, the checklist, the disputed checks, the threads, or the conclusion), and **IsActionable** (there is a
place to go to; otherwise the label is only a sentence).

#### Key Methods

**ReviewStatusService.GetStatusAsync**: Builds the status for a viewer.

- *Parameters*: `Actor? actor`, `int projectId`, `string reviewId`, and a cancellation token.
- *Returns*: `ReviewStatus?`, `null` when the review does not exist.
- *Algorithm*:
  - Require that the viewer may see the project.
  - Read the approval as the viewer may see it, the threads the viewer may see, and what the viewer may start, then
    build the status from them as described for the overload below.
- *Preconditions*: The viewer may see the project; otherwise `ProjectNotFound` is raised.

**ReviewStatusService.GetStatusAsync (with values passed in)**: Builds the status for a viewer from values the caller
has already read, so the review page does not read them a second time.

- *Parameters*: `Actor? actor`, `int projectId`, `string reviewId`, the `ReviewApproval`, the list of `ReviewThread`
  and the `ThreadCapabilities` of the same viewer, and a cancellation token.
- *Returns*: `ReviewStatus?`, `null` when the review does not exist.
- *Algorithm*:
  - Require that the viewer may see the project.
  - Use the values passed in instead of reading them again, and continue in the builder that the other overload also
    uses.
  - Read the snapshot, the review and the ballot view. The viewer is masked when the approval or the ballot view says
    so.
  - List the reviewers in slot order with the state of the ballot and the signature; a review without a checklist has no
    ballot to hand in, so its slots count as handed in.
  - List the disputed checks from the ballot view, in checklist order, with `CanDiscuss` set when the viewer may start a
    check discussion and none is open for the check, and with the answers and justifications of the submitted ballots.
  - Count the open questions and findings with `ThreadCounts`, and count the open questions that wait for the viewer
    (the latest comment of the question is not by the viewer).
  - Decide the next action with `ReviewNextAction.Decide` and the facts about the viewer, including the names of the
    other reviewers and of those who have not signed.
  - Word the caution for a viewer whose next action is to sign when another reviewer has not handed in a ballot, and take
    the outcome note from the approval; both are left out for a masked viewer.
  - Derive the changes since signing for every slot whose signature is out of date.
- *Preconditions*: The viewer may see the project; otherwise `ProjectNotFound` is raised. The values passed in were
  read for the same viewer.

**ReviewNextAction.Decide**: The rule. It is pure and gives exactly one action.

- *Algorithm*, in order, first match wins:
  - The review is concluded: done, with a sentence.
  - The viewer cannot take part (reads only, an Auditor, or an outsider): a sentence that there is nothing to do.
  - A reviewer: answer the checklist while the own ballot is incomplete; hand the ballot in (and sign, when that is
    offered in one step); then, unless already signed, discuss the checks answered differently; resolve open findings;
    answer open questions, except that the person who asked every open question waits for the answer ("Waiting for Bob
    to answer your question.", a sentence without a button) and the other party is still asked to answer; a reviewer who
    has signed waits for the others, told whom ("You have signed. Waiting for Bob to sign."); write the conclusion when
    one is needed; sign (or "look at the changes and sign again" when the signature lapsed); a reviewer whose signature
    lapsed and who cannot sign yet is told that they can sign again when nothing blocks it; otherwise wait for the
    others.
  - A person without a slot: add the reviewers (the lead) or add yourself when the review has no slot; discuss the
    disputes; resolve findings; answer questions; write the conclusion; add yourself as a reviewer; otherwise wait, or
    say that no reviewer has been added yet.
- *Postconditions*: One action per viewer. Every fact is already reduced to what the viewer may see, so the rule cannot
  reveal a dispute or a discussion that the viewer may not see. The facts that tell who asked and who signed are
  optional; when they are not known the rule keeps the plainer sentences ("Answer the open questions", "Wait for the
  other reviewers.").

**ReviewChangeRules.Derive**: Explains, from the audit trail, what changed after a signature.

- *Signature*: `IReadOnlyList<ReviewChange> Derive(IReadOnlyList<AuditEntry> entries, IReadOnlyList<AuditEntry> shown,
  DateTimeOffset after, IReadOnlyList<ReviewThread>? threads = null)`.
- *Parameters*: `threads` — the threads of the record that the status reads, or `null` when they are not known.
- *Algorithm*: First seed the kind of every thread that was carried over from the followed review
  (`CarriedFromReviewId` is set), because the audit trail of this review holds no entry that started it: the seed is
  Finding when the first `thread.kind-changed` entry of the thread says that it changed from a Finding, else the
  current kind of the thread. Then replay all entries of the review, oldest first, to follow the kind of every thread.
  An entry after the
  signature is listed when its action is one of a fixed set: a ballot handed in, taken back, or given to another
  reviewer; a change of the conclusion text; a thread started in the report; a reply added to the report; a Finding
  resolved with a note; a change of the kind of a thread; a comment put in the report or taken out of it. A reply or a
  comment is listed only when its audit text says that it is in the report, and a resolution note only for a Finding.
  An entry that the audit masking hides from the viewer (`shown` holds a different object) is worded with one neutral
  sentence.
- *Postconditions*: The reasons are fixed sentences that never contain text of the entry. The list explains the usual
  reasons a signature no longer matches; it is not a proof, because the signature check compares the digest of the
  record and an entry may have changed nothing in the end. The page says so. The seeding only reads the threads of the
  record: it writes nothing to the audit trail and changes neither the audit entries nor the record digest.

#### Error Handling

A viewer who may not see the project is refused with the `ProjectNotFound` rule, so that the existence of a project is
not revealed. A review that does not exist gives `null`. Null arguments of the types raise `ArgumentNullException`. The
audit trail is read in batches to the end of the trail, however long it is, because the change rules replay every entry.

#### Dependencies

- **SigningService** — the approval as the viewer may see it: the slots, the signature states, the blockers, whether
  a conclusion is required, the failed checks and the rejection note, and the wording of the early-signing caution
  (`ReviewApproval.MissingBallotsText`).
- **ChecklistService** — the ballot view (disputes, hand-in counts, and the offers) and whether the viewer may write the
  conclusion.
- **ThreadService** — the threads the viewer may see and what the viewer may start.
- **IAuditStore** and **AuditMasking** — the entries after a signature, masked for the viewer.
- **BallotVisibility**, **ReviewActorRoles**, **IProjectAccess**, **IReviewStore**, **IUserStore** — Core.

#### Callers

- **ReviewPage** — shows the status in the `ReviewStatusPanel` at the top of the page, and the changes since a signature
  in the list of reviewers.
