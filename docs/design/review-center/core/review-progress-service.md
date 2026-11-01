### ReviewProgressService

![Core Structure](CoreView.svg)

#### Purpose

`ReviewProgressService` answers one question for the project overview and the API list: where does every review of this
project stand, and who is holding it up? For each review it reports the stage, how many signatures there are of the
number needed (and how many are out of date), the people the review waits on, what blocks its conclusion (open threads,
unanswered checks, checks on which the ballots disagree, a missing conclusion text of a rejected review), how many
checks are answered Fail (informational), when it last changed, its due date and whether it is overdue. The unit also
owns the `ReviewStage` and `ReviewProgress` types that carry the answer. It computes everything on demand from what is
already stored and keeps no state of its own, so the overview cannot disagree with the review page. Who may see a project
is decided by `ProjectAccess`; this unit only asks.

#### Data Model

**ReviewStage**: `Setup` (no reviewer slot yet), `InReview` (at least one slot, not concluded), `Approved`, and
`Rejected` (concluded with every signature attesting the outcome rejected). The stage
is derived and is never the stored state of the review, which only says whether a review is open: a review that was
reopened after it was concluded is open again, so it is `InReview`.

**ReviewProgress**: an immutable record with `ReviewId`, `Title`, `Stage`, `SlotsRequired`, `SlotsSigned`,
`SlotsOutOfDate`, `WaitingOn` (display names), `OpenThreads`, `UnansweredChecks`, `FailedChecks`, `NoChecklist`,
`Created`, `LastActivity`, `DueDate`, `IsOverdue`, `Conclusion` (a `ReviewConclusion`), `Reopened`, `IsBlocked`,
`FollowsReviewId`, `ConclusionRequired`, and `DisputedChecks` (the checks on which the submitted ballots disagree, so
a review blocked only by a dispute can say so). `IsApproved` (conclusion approved) and `IsConcluded` (approved or
rejected) are derived from `Conclusion`. `FailedChecks` is informational: a Fail answer does not block.
`IsBlocked` is true for a review that is not concluded and has no checklist, or has any open thread, unanswered or
disputed check, or a rejected outcome that still needs its conclusion text (`ConclusionRequired`).

**Blocker counts**: the counts and the flags are fields of `ReviewApproval` (`OpenThreads`, `UnansweredChecks`,
`FailedChecks`, `DisputedChecks`, `MissingChecklist`, `ConclusionRequired`; the missing-checklist flag is copied to
`NoChecklist`). The check counts are those
of the consolidated checklist (see *Ballot Consolidation Design*), which reads only the submitted ballots, so a draft
ballot does not change them (a review with a single slot is the exception: its one ballot counts before the hand-in).
`UnansweredChecks` is the number of pending checks (a check that not every submitted ballot has answered, which is also
every check while no ballot is submitted), `FailedChecks` the checks on which the submitted ballots agree on Fail, and
`DisputedChecks` the checks that the submitted ballots answered differently. The progress record carries
`DisputedChecks` too, so a review blocked only by a dispute can say so (the blocker sentence on the review page names
the number). `SigningService` calculates them in the one
step that also writes the blocker sentences (`BuildApproval`), so the numbers, the flag, and the sentences cannot
disagree: `MissingChecklist` is true exactly when "This review has no checklist." is a blocker. This unit only copies
them; it has no second rule for what blocks a review, and the later work list of the lead reuses the same counts.
`SlotMember.Enabled` carries whether the account of a person in a slot is enabled.

**Last activity**: the time of the newest audit entry that names the review, read for the whole project with one call of
`IAuditStore.LastActivityAsync(projectId)` (a dictionary of review identifier to the time of the entry with the highest
sequence). For a review the trail has no entry for, the creation time is used. No column is added to the review: the
audit trail is already complete and tamper evident, so a copy would only be a second record to keep true. This relies on
the fact that the audit entries of a review are never combined: only the sign-in, token, and throttle events are
coalesced (`AuditService.TryRecordCoalescedAsync` and `UserService.RecordCoalescedAsync`, a window of one minute), and
none of them names a review, so the newest entry of a review is its real last activity.

**Constants**: `DisabledSuffix` (" (disabled)"), appended to the name of a person in `WaitingOn` whose account is
disabled.

The service holds the injected collaborators and no other state, so it is safe for concurrent use when they are.

#### Key Methods

**GetProgressAsync**: Gets the progress of every review of a project.

- *Parameters*: the `Actor` (may be null), the project identity, and a cancellation token.
- *Returns*: `Task<IReadOnlyList<ReviewProgress>>`, ordered by review identifier; empty when the project has no reviews.
- *Preconditions*: the actor may see the project (`IProjectAccess.RequireViewAsync`). That is every member, a read-only
  auditor, a project administrator, a system administrator, and an actor that came through an API token. A project the
  actor may not see, a project that does not exist, a disabled or unknown user, and a missing actor give
  `ProjectNotFound`, with the same message for each.
- *Postconditions*: nothing is changed and nothing is recorded.

**GetProgressAsync (with the list of reviews)**: The same answer for a caller that already holds the review list, such
as the API list, which needs the list itself for the review fields. The caller passes the list that
`ReviewService.ListAsync` returned and the service does not read it again, so the reviews are still read once.

- *Parameters*: the `Actor` (may be null), the project identity, the list of `Review` (not null), and a cancellation
  token.
- *Returns*: the progress of each given review that still exists, in the order given; empty for an empty list.
- *Preconditions*: the same access check as above, made before anything is computed; a null list raises
  `ArgumentNullException`. The caller must pass the reviews of that project; the service does not check it.
- *Postconditions*: nothing is changed and nothing is recorded; the audit trail and the approvals are not read for an
  empty list.

The number of reads does not depend on the number of reviews: one read of the reviews (`ReviewService.ListAsync`, made
by the caller that passes the list in), one
batch read of the approval statuses (`SigningService.GetApprovalsAsync`, which itself takes one set-based read of the
snapshots of all the reviews, `IReviewStore.ListSnapshotsAsync`, and one of the reopen history of the reviews that were
reopened), and one read of the last activity. A review
that is removed between the first two reads is left out. The page that lists the reviews of a project used to ask for the
approval of each review on its own; it now uses this one call.

**Stage**: no slot gives `Setup`; otherwise an approved review gives `Approved`, a rejected review gives `Rejected`, and
every other `InReview`. A review whose slots are all signed but that has an open thread, a pending or disputed check, or
a ballot that is not handed in is not concluded, so it is `InReview` (the blockers of the first kind are counted and
block it until they are settled).

**WaitingOn**: for a review that is not concluded, the primary reviewer and, when it is another person, the alternate
reviewer of
every slot that is not signed or whose signature is out of date, each person once, in slot order. A person whose account
is disabled is shown with `DisabledSuffix`, because the slot has to be reassigned and not waited for. A concluded review
waits on nobody.

**Overdue**: decided by `ReviewService.IsOverdue`, the rule of the review page (the day after the due date, until the
review is concluded), so the overview and the review page agree.

#### Error Handling

`DomainRuleException` with `ProjectNotFound` when the actor may not see the project. Constructing the service with a null
argument raises `ArgumentNullException`. Failures of the stores propagate unchanged.

#### Known Limitations

- The progress is a snapshot of three reads that are not one transaction. A change between the reads can make one review
  show the activity of a moment later than its signatures; the next read is consistent.
- The service reads the whole project. A project with several thousand reviews makes one larger answer; the page limits
  what it renders (25 to a page), not what it reads.

#### Dependencies

- **ReviewService** — lists the reviews and decides whether one is overdue (injected).
- **SigningService** — the approval status of every review with its slots, signatures, and blocker counts (injected).
- **IAuditStore** — the last activity of every review of the project (injected).
- **IProjectAccess** — decides who may see the project (injected).

#### Callers

- **ProjectPage** — the overview table and its summary.
- **ApiEndpoints** — the list of the reviews of a project.
- **ServerHost** — registers the service as a single instance.
