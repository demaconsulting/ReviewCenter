### InboxService

![Core Structure](CoreView.svg)

#### Purpose

`InboxService` builds the inbox of a user: the reviews that wait for the user, in four groups, and the number of
reviews that need action. It also keeps what the user has seen of each review, which marks new comments and new
changes. The inbox is also the notification list. There is no notification record: every row is computed from the
review data when the inbox is read, and the single `ReviewUserState` row of a user and a review says what is new. The
pure rules (which slot needs a signature, which threads wait for whom, which comment is unread) are in the internal
helper `InboxRules`, so they can be tested without a store. The unit also holds the domain types `InboxItem`,
`InboxView`, `InboxSection`, `InboxCandidate`, `InboxSeenMark`, `ReviewUserState`, and the interface `IInboxStore`.

#### Data Model

The unit holds no state of its own beyond its injected stores, the access rules, and the clock. It is safe for
concurrent use as long as the stores are.

**InboxSection**: `ToSign`, `RepliesWaiting`, `FailedChecks` (these three need action and are counted), and `Updates`
(information only).

**InboxItem**: Section, project, project name, review identifier, title, a short chip, an unread flag, the due date, an
overdue flag, and an optional detail sentence. **InboxView**: the items ordered by section, then project name, then
review identifier, and `ActionCount`, the number of distinct reviews in the three action groups.

**ReviewUserState**: user, project, review, `LastSeenCommentId` (the highest comment identity the user has seen; zero
for none), and `LastSeenAt`, the time read before the review was loaded when the user last saw it. Comment identities
are assigned in posting order and never reused, so one integer per review tells which comments are new.

#### Key Methods

**GetInboxAsync**: Builds the inbox of an actor.

- *Parameters*: The actor, or null, and a cancellation token.
- *Returns*: The `InboxView`; empty for a null actor, an actor that is not a known enabled user (the command line, an
  unknown name), a user who must change a temporary password, and a user who may see no project.
- *Algorithm*: Find the user from the actor. List the projects the user may see (through `IProjectAccess`) and the
  reviews in which the user is a reviewer or the creator (`IInboxStore.ListInvolvedReviewsAsync`), keep those of visible
  projects, and read the read-state rows of the user. For each such review load one snapshot, because the conclusion
  depends on the record digest and on the open threads (a review is concluded, as approved or as rejected, only when
  every slot is signed for the current record with the outcome the record has now and no thread is open), and add rows
  by these rules:
  - *To sign*: the user may contribute and is a member, the review is not concluded, and a slot of the user (primary
    or alternate) is not signed. The chip is "Not signed", or "Out of date, sign again" when every such slot has a
    signature that no longer covers the record. The state of a slot is the one the approval of the review uses.
  - *Replies waiting*: the user may contribute, the review is not concluded, and there is an open thread whose last
    comment is by someone else (a comment of the system counts as someone else) in which the user, holding a slot of
    the review, wrote a comment, or of whose review the user is the creator. The chip gives the number of such threads.
    A thread counts whether its comments are chat or included in the report, and a review whose slots are all signed
    but that has an open thread is not concluded, so it still lists the thread here.
  - *Failed checks*: the user created the review, it is not concluded, and it has checks answered Fail in a section
    that applies. The chip gives the number, and the detail sentence is "A Fail the reviewers agree on makes the review
    reject; a Fail only one reviewer gave is a disagreement to resolve." because a Fail answer no longer blocks signing
    but, when the ballots agree on it, leads to a rejected outcome. A concluded review has no
    failed-checks row and no to-sign row.
  - *Updates*: the review is concluded and its completing signature (the latest of the current digest) is by someone
    else and newer than `LastSeenAt`, or it was reopened by someone else after that time. The chip is "Approved"
    (`ApprovedChip`) for an approved review and "Rejected" (`RejectedChip`) for a review concluded as rejected, so a
    rejection is shown as an update and never as an action. A signature of the user's
    own is no news. Updates are always unread, carry no due mark, and are not counted as action.

  The due date is shown on the action rows, as overdue when it is past and the review is not concluded.
- *Unread*: a row is unread when the user has no read-state row, or the reason is newer than the row: a comment of
  someone else with a higher identity than `LastSeenCommentId`, a signature or a reopening after `LastSeenAt`, or a
  Fail answered by someone else after `LastSeenAt`. A To sign row follows the same rule, so it is unread when
  someone else signed or answered a check after `LastSeenAt`, wrote a comment newer than `LastSeenCommentId`, or
  reopened the review after `LastSeenAt`; the missing row is not the only reason.

**CountAsync**: The `ActionCount` of the inbox, for the home page. It never raises for an unknown actor.

**GetReviewUnreadAsync**: Reads the baseline for the unread markers of a review page: the `LastSeenCommentId` of the
user for the review, zero when there is no row or the user is not a known user. The caller checks the access first.

**MarkSeenAsync**: Records that the user saw a review up to a comment, never moving back.

- *Returns*: True when the state was stored; false, with nothing stored, when the actor is not a known user or may not
  see the project or the review does not exist. The access is checked at the time of the call.

**MarkAllReadAsync**: Marks every review of the inbox as seen.

- *Postconditions*: The clock is read before the inbox is loaded, so a change that happens while the inbox is built stays
  new. One `MarkSeenManyAsync` call stores the highest comment identity of each review, counting only the comments
  written at or before that time, and the time, so a comment posted while the inbox is built is not stored as seen.
  Returns the number of reviews marked.

**IsUnread**, **CountUnread**, **MaxCommentId**: Public static helpers, forwarded to `InboxRules`, that the review page
uses to mark comments. A comment is unread when its author is someone else (compared without regard to case) and its
identity is higher than the baseline; the user's own comments are never unread. `MaxCommentId` has a second form with
a cutoff time that ignores the comments created after the cutoff; it is the watermark to store with that time.

#### Error Handling

Failures of the stores propagate unchanged. A null actor is not an error: the inbox is empty and nothing is stored. Null
arguments of the other methods raise `ArgumentNullException`. The unit never writes an audit entry, and nothing it stores
enters the record digest.

#### Dependencies

- **IInboxStore** — the read-state rows and the involved reviews (implemented by `InboxStore`).
- **IReviewStore** — the snapshot of a review: threads, answers, signatures, reopenings.
- **IProjectAccess** — decides which projects the user may see and whether the user may contribute.
- **IUserStore** — finds the user of an actor.
- **RecordDigest** and **ReviewLock** — the current digest and the conclusion (`ReviewLock.ConcludedOutcome`) of a
  review, so
  the inbox and the review page never disagree.
- **TimeProvider** — the read times and the due dates.

#### Callers

- **InboxPage** — shows the inbox and marks all as read.
- **HomePage** — shows the number of reviews that need action.
- **ReviewPage** — reads the baseline and stores what the user has seen.
- **ServerHost** — registers the unit.
