### ReviewStore

![Storage Structure](StorageView.svg)

#### Purpose

`ReviewStore` implements the Core store interface on top of `ReviewCenterDbContext`. Its single responsibility is
to persist projects, project memberships, reviews, file content, comment threads, checklists, reviewer slots,
signatures, and review reopens, and to read
them back, translating database behavior into the terms of the domain. It also enforces, inside its write
transactions, that a concluded review is locked.

#### Data Model

The unit is stateless apart from the injected context factory and the shared `AuditAppender`. Every read creates its own
short-lived context from the factory and disposes it before returning, so concurrent operations never share a context.
Reads use no change tracking. Every change goes through `AuditAppender.ExecuteAsync`, which takes the process-wide audit
gate, opens a `Serializable` transaction, runs the change, appends the audit entry to the same context, saves, and
commits (see the `AuditStore` unit).

#### Key Methods

**Audit transaction of every change**: `AddProjectAsync`, `AddMembershipAsync`, `RemoveMembershipAsync`,
`SetMembershipRoleAsync`, `UpdateEvidenceSettingsAsync`, `UpdateRequireIndependentReviewersAsync`,
`UpdateReviewRuleAsync`, `AddReviewAsync`,
`AddThreadAsync`, `AddCommentAsync`,
`ReopenThreadAsync`, `ResolveThreadAsync`, `SetThreadKindAsync`, `SetCommentReportAsync`,
`SetBallotAnswerAsync`, `SetBallotSectionStateAsync`,
`SubmitBallotAsync`, `AddSlotAsync`, `UpdateSlotAsync`,
`RemoveSlotAsync`, `UpdateDueDateAsync`, `UpdateReviewConclusionTextAsync`, `AddSignatureAsync`, and
`ReopenReviewAsync` take the audit draft of the
change (or a function that builds it from the stored entity, when the draft names an identity that the database
assigns). The change and
its audit entry are
stored in the same transaction, so both exist or neither does. A change that did not take effect (a thread, slot, or
review that is not found) returns its null or false result and stores no entry. A failure of any kind, including a
duplicate review identifier or a refused signature, rolls back the change and the entry together. The descriptions below
are of the change itself.

**Approval lock inside the transaction**: `AddThreadAsync`, `AddCommentAsync`, `ReopenThreadAsync`,
`ResolveThreadAsync`, `SetThreadKindAsync`, `SetCommentReportAsync`,
`SetBallotAnswerAsync`, `SetBallotSectionStateAsync`,
`SubmitBallotAsync`, `AddSlotAsync`, `UpdateSlotAsync`, `RemoveSlotAsync`,
`UpdateDueDateAsync`, and `UpdateReviewConclusionTextAsync` call
the private `EnsureNotLockedAsync` first, inside the same serialized transaction as the change. It loads the slots, the
record (with its reopen count), and the signatures with the same code as `AddSignatureAsync`, and asks the Core
`ReviewLock.IsConcluded`, the one definition of "concluded" (every slot has a submitted ballot, the ballots agree with
the approved outcome, every slot signed for the current record, and no thread
open), so the store and the services cannot disagree. When the review
is concluded it raises `DomainRuleException` with `ReviewLocked` (the message of `ReviewLock.LockedMessage`), and the
transaction rolls back: nothing is stored and no audit entry is written. A review without slots is never concluded, and
the check returns early without loading the record. This is defense in depth behind the Core services and closes the
race with a concurrent final signature: because the write transaction holds the database write lock, a signature
cannot commit between the check and the change. `AddSignatureAsync` compares the digest inside its own transaction and,
after the digest check, refuses with `SigningNotAllowed` when any thread of the review is open (a thread that was
started or reopened after the signer loaded the page), and refuses with `BallotNotSubmitted` when the ballot of the
slot being signed is not submitted (see **Signature rule of the own ballot** below); reads are not affected.

**AddProjectAsync**: Stores a project and returns it with the identity assigned by the database. The unit copies the
name, description, and creation time into a new entity, so an identity supplied by the caller is ignored and can
neither be persisted nor collide with an existing project; the returned project is the stored entity. The evidence
settings of the project are copied too, and a project that was not given others has the defaults
(`EvidenceSettings.Default`). So is `RequireIndependentReviewers` (true unless the caller gave another value), and the
checklist definition (none unless the caller gave one). The two further review rules,
`RequireNonAuthorChecklistAnswers` and `RequireNonReopenerSigning`, are copied the same way (true unless the caller
gave another value).

**GetProjectAsync**: Reads one project by identity, with its evidence settings and its review rules
(`RequireIndependentReviewers` and the two further rules); null when absent.

**ListProjectsAsync**: Lists all projects ordered by name and then identity, with their evidence settings and review
rule. SQLite cannot
order by a `DateTimeOffset`, so the creation time is not used for ordering.

**UpdateEvidenceSettingsAsync**: Changes the seven evidence settings of one project if they still hold the values the
caller started from (compare-and-set).

- *Parameters*: the project identity, the `expected` settings, the `updated` settings, and the audit draft of the change.
- *Returns*: `Task<bool>` — `true` when the settings were changed; `false` when the project does not exist or its
  settings are no longer `expected`.
- *Preconditions*: `expected`, `updated`, and the audit draft are not null (`ArgumentNullException` otherwise).
- *Postconditions*: Either the new settings and the audit entry (`project.evidence-settings-changed`, carrying the
  project) are stored in the same serialized transaction, or nothing is.

The comparison is part of one update statement, `UPDATE ... WHERE Id = projectId AND` every one of the seven columns
equals the expected value, so the check and the change are one atomic step and two administrators who save at the same
time cannot both succeed: the second finds no matching row and gets `false`, with no change and no entry. Only the
columns of that project are written. The store also returns `false` when no project has the identity (no row
matches). The service turns the `false` of a project that was changed meanwhile into `RecordChanged`; a project that
does not exist is refused earlier by the access check with `ProjectNotFound` and never reaches the store.

**UpdateRequireIndependentReviewersAsync**: Changes the `RequireIndependentReviewers` setting of one project if it still
holds the value the caller started from (compare-and-set), in the same way.

- *Parameters*: the project identity, the `expected` value, the `updated` value, and the audit draft of the change.
- *Returns*: `Task<bool>` — `true` when the value was changed; `false` when the project does not exist or the stored
  value is no longer `expected`.
- *Postconditions*: Either the new value and the audit entry (`project.independent-reviewers-changed`) are stored in
  the same serialized transaction, or nothing is.

The comparison is part of one `ExecuteUpdate` statement (`WHERE Id = projectId AND RequireIndependentReviewers =
expected`), so two administrators who save at the same time cannot both succeed. `ProjectService` turns the `false`
into `RecordChanged`. This method is the same as `UpdateReviewRuleAsync` for the rule `IndependentReviewers`.

**UpdateReviewRuleAsync**: Changes one named review rule of one project (`ReviewRule`: `IndependentReviewers`,
`NonAuthorChecklistAnswers`, or `NonReopenerSigning`) if it still holds the value the caller started from
(compare-and-set).

- *Parameters*: the project identity, the rule, the `expected` value, the `updated` value, and the audit draft of the
  change.
- *Returns*: `Task<bool>` — `true` when the value was changed; `false` when the project does not exist or the stored
  value is no longer `expected`.
- *Preconditions*: the audit draft is not null (`ArgumentNullException`) and the rule is a defined value
  (`ArgumentOutOfRangeException`, checked before any work).
- *Postconditions*: Either the new value of that rule's column only and the audit entry are stored in the same
  serialized transaction, or nothing is.

The store picks the column from the rule with a switch and runs one `ExecuteUpdate` statement (`WHERE Id = projectId AND
<column> = expected`); the other rules of the project are not written.

**UpdateChecklistDefinitionAsync**: Changes the checklist definition of one project if the hash of the stored text still
equals the hash the caller started from (compare-and-set).

- *Parameters*: the project identity, the expected hash (`ProjectChecklistService.HashOf` of the stored text, which
  for no definition is the hash of the default's text), the new JSON text or `null` for the built-in default, and the
  audit draft of the change.
- *Returns*: `Task<bool>` — `true` when the definition was changed; `false` when the project does not exist or the
  stored text no longer has the expected hash.
- *Preconditions*: the expected hash and the audit draft are not null (`ArgumentNullException` otherwise).
- *Postconditions*: Either the new definition and the audit entry (`project.checklist-changed`, carrying the project)
  are stored in the same serialized transaction, so the hash chain stays intact, or nothing is.

A hash cannot be compared inside SQL, so the change reads the stored text inside the transaction of the audit gate,
compares its hash, and then writes with one `ExecuteUpdate` statement whose condition repeats the text that was
compared (`WHERE Id = projectId AND ChecklistDefinition = the text read`; a null text compares as `IS NULL`). The
transaction takes the write lock when it begins and the gate is process-wide, so two administrators who save at the same
time cannot both succeed: the second finds a different text, gets `false`, and leaves no change and no entry. Only the
column of that project is written. `ProjectChecklistService` turns the `false` into `RecordChanged`; a project that does
not exist is refused earlier by the access check and never reaches the store.

**UpdateDueDateAsync**: Changes the optional due date of one review if it still equals the date the caller started from
(compare-and-set).

- *Parameters*: the project identity, the review identifier, the expected date (`DateOnly?`, null for none), the new
  date (null to clear it), and the audit draft of the change.
- *Returns*: `Task<bool>` — `true` when the date was changed; `false` when the review does not exist or the stored date
  is no longer the expected one.
- *Preconditions*: the audit draft is not null (`ArgumentNullException` otherwise).
- *Postconditions*: Either the new date and the audit entry (`review.due-date-changed`, carrying the project and the
  review) are stored in the same serialized transaction, so the hash chain stays intact, or nothing is.

The change first runs the approval check of the transaction (`EnsureNotLockedAsync`), so a review that is concluded at
that moment is refused with `ReviewLocked` and nothing is stored. It then writes with one `ExecuteUpdate` statement
whose condition repeats the expected date (`WHERE ProjectId = p AND Id = r AND DueDate = expected`; a null compares as
`IS NULL`), so two people who save at the same time cannot both succeed: the second affects no row, gets `false`, and
leaves no change and no entry. Only the column of that review is written, and the date is not part of the record
digest. `ReviewService` turns the `false` into `RecordChanged`.

**UpdateReviewConclusionTextAsync**: Changes the optional review conclusion text of one review (the free text of the
record; not the approved or rejected conclusion) if it still equals the text the caller started from (compare-and-set).

- *Parameters*: the project identity, the review identifier, the expected text (`string?`, null for none), the new text
  (null to clear it), and the audit draft of the change.
- *Returns*: `Task<bool>` — `true` when the text was changed; `false` when the review does not exist or the stored text
  is no longer the expected one.
- *Preconditions*: the audit draft is not null (`ArgumentNullException` otherwise).
- *Postconditions*: Either the new text and the audit entry (`review.conclusion-changed`, carrying the project and the
  review, with lengths only) are stored in the same serialized transaction, so the hash chain stays intact, or nothing
  is.

The change first runs the approval check of the transaction (`EnsureNotLockedAsync`), so a review that is concluded at
that moment is refused with `ReviewLocked` and nothing is stored. It then writes with one `ExecuteUpdate` statement
whose condition repeats the expected text (`WHERE ProjectId = p AND Id = r AND ReviewConclusionText = expected`; a null
compares as `IS NULL`), so two people who save at the same time cannot both succeed: the second affects no row, gets
`false`, and leaves no change and no entry. Only the column of that review is written. Unlike the due date, the text is
part of the record digest, so `LoadRecordsAsync` (shared by `GetSnapshotAsync`, `GetRecordAsync` and the batch read)
selects the column and puts it into the `ReviewRecord`. `ChecklistService` turns the `false` into `RecordChanged`.

**ReviewExistsAsync**: Reports whether a review with the identifier exists in a project.

**AddMembershipAsync**: Stores the membership of a user in a project with its audit entry (`project.member-added`,
which carries the project) in one transaction. The key is the user and the project, so a second membership of the same
pair violates the key and is reported as `MembershipExists` ("The user is already a member of this project."), and
nothing is stored; a project or user that does not exist is refused by the foreign keys of the database and the failure
surfaces as the SQLite exception, unchanged.

**RemoveMembershipAsync** and **SetMembershipRoleAsync**: Delete the membership of a user in a project, or replace its
project role, with one statement each, and return whether a membership was found. The audit entry
(`project.member-removed` or `project.member-role-changed`, carrying the project) is stored only when a row was
affected, so a change that has no effect stores no entry. Both take `requireAnotherAdmin`. When it is set, the private
`EnsureAnotherAdminAsync` runs first, inside the same serialized audit transaction (so two concurrent changes of two
administrators cannot both pass it): when the target is an enabled project administrator (a plain member, a disabled
administrator, and a non-member do not count) and no OTHER enabled project administrator exists, the change is refused
with `DomainRuleException` (`LastProjectAdmin`), nothing is stored and no entry is written. A role change checks only
when it is a demotion (the new role is not `ProjectAdmin`).

**GetMembershipAsync**, **ListMembershipsAsync**, and **ListMembershipsForUserAsync**: Read one membership by user and
project (null when absent), the memberships of a project ordered by user, and the memberships of a user ordered by
project. The reads use no tracking and no gate.

**ListSoleAdminProjectsAsync**: Lists, for the offboarding impact of a user, the projects in which the user is an
enabled project administrator and no other enabled project administrator exists, ordered by name and then identity. A
disabled
user, a user who is no administrator, and an unknown user give an empty list. It applies the rule of
`EnsureAnotherAdminAsync` (a plain member, a disabled administrator, and a system administrator who is not a member
do not count as another administrator), in one query with no tracking, so a project in the list is one where removing or
demoting the user with the check required would be refused.

**AddReviewAsync**: Stores a review, all its files, and its checklist snapshot (when it has one) in one save, which
is one transaction.

- *Postconditions*: Either the review, all its files, and its snapshot are stored, or nothing is. An identifier that already
  exists in the project, whether found by an earlier check or by a race, is reported as a `DomainRuleException`
  for `ReviewIdDuplicate`. A review for a project that does not exist is not stored; the relationship constraint
  of the database refuses it.

A follow-up review arrives with `Review.CarryOver` (the carried answers, section states and threads with their
comments). The store adds those rows to the same context before the one save, so they are part of the same
transaction as the review and its audit entry: a failure anywhere (for example a carried thread whose file is not a
file of the review, which the relationship constraint refuses) stores nothing. Carried threads are copied with fresh
entities, as for `AddThreadAsync`, so the database assigns their identities and comments; the carried markers
(`CarriedFromReviewId`, `CarriedFromThreadId`), the original authors, texts and times are kept as given.
`FollowsReviewId`, `CarriedAnswerCount` and `CarriedThreadCount` are stored with the review and read back by
`GetReviewAsync` and `ListReviewsAsync`.

**GetReviewAsync**: Reads one review with its source columns and the metadata of its files (path, size, hash, change
kind, baseline size and hash) ordered by path, and without file bytes (neither head nor baseline), so that showing a
review with large files does not load them; null when absent.

**ListReviewsAsync**: Lists the reviews of a project ordered by identifier, without files.

**GetFileContentAsync**: Reads the bytes of one file of a review, exactly as stored; null when absent.

**GetBaselineContentAsync**: Reads the frozen baseline bytes of one file of a review; null when the review or file is
absent or the file has no baseline.

**AddThreadAsync**: Stores a thread together with its first comment in one save, which is one transaction. Fresh
entities are copied from the argument, so identities supplied by the caller are ignored and the database assigns
them; the thread keeps its side, and the comment keeps the display name and the user name of its author, its kind,
and its report flag. A thread for
a file that does not exist in
the review is refused by the relationship constraint of the
database.

**AddCommentAsync**: Appends a comment to a thread, with the display name, the user name of its author, and its report
flag. It first
checks that the thread belongs to the stated project
and review and returns null when it does not, so a thread identity from another review cannot be commented on.

**ReopenThreadAsync**: Sets a thread of the stated review to open with a single update statement and returns whether a
thread was found. `ThreadService` uses it to reopen a thread. It takes no state: it cannot resolve, so every resolution
of a thread goes through `ResolveThreadAsync` and its resolution-note rule, and the store has no other way to change
the state of a thread.

**ResolveThreadAsync**: Resolves an open thread and, when a resolution comment is given, stores it in the same
transaction. The caller passes the kind of the thread that it based the note, the acknowledgement of the signatures
and the report flag on. The store reads the thread and its starting comment inside the transaction and compares the
kind it finds there with the expected kind: a difference (a promote or demote landed in between) raises
`RecordChanged` and stores nothing, no state, no comment and no audit entry. When the kinds agree, the comment is
stored with the kind `Resolution` and its flag set to whether the thread is a Finding, and a Finding without a
comment raises `ResolutionNoteRequired`. A thread that does not exist or is already resolved returns `false` and
stores nothing, no comment and no audit entry.

**SetThreadKindAsync**: Promotes or demotes a thread by setting the report flag of its starting comment and of its
resolution comments (replies keep theirs) with `ExecuteUpdateAsync`, and returns `true` only when the thread had the
other kind. Promoting a resolved thread that has no resolution comment is refused with `ResolutionNoteRequired` inside
the transaction, so a resolved Finding always carries its note.

**SetCommentReportAsync**: Sets the report flag of one reply, with a single update. The starting comment and the
resolution comments are refused with `CommentNotTogglable`; a thread or comment that does not exist, or a flag that
already has the value, returns `false` and stores no entry.

**ListThreadsAsync**: Lists the threads of one review, for every file, with their comments. Threads and comments
are ordered by identity, which grows with the order of creation; the creation time is not used for ordering because
SQLite cannot order by a `DateTimeOffset`. All threads and comments of the review are read at once: there is no cap
on their number and no pagination, which is deferred until real volumes are known.

The bytes of files are stored and returned verbatim, including empty files and files containing carriage
returns or zero bytes.

**GetChecklistAsync**: Reads the checklist snapshot of a review; null when the review has none.

**ListChecklistSectionStatesAsync** and **ListChecklistAnswersAsync**: List the stored review-level section states and
answers of a review (slot identity 0, `ChecklistAnswer.BaselineSlotId`), with display names, user names, and UTC times
as saved. They are the legacy baseline: the rows a follow-up review carries from the review it follows. The rows of a
ballot are not listed. The store has no method that writes these rows other than the creation of a follow-up review
(`AddReviewAsync` with its carry): every answer and section state of a reviewer is written with the ballot methods
below, and answering a carried check again, or setting a carried section again, in a ballot replaces the carried
marker, because the row of the ballot is written from the incoming entity, which the services leave without one.

**Ballots**: A ballot is the set of answers and section states that one reviewer slot gave, with a status row
(`ChecklistBallot`: draft or submitted, and for a submitted one the time and the user name of the hand-in). Slot
identity 0 is the baseline, the review-level rows that the legacy readers above list; it is never a ballot, and
no status row exists for it. The record, the digest and the services read the ballots, not the baseline.

- **ListBallotsAsync** and **GetBallotAsync** read the ballots of a review (ordered by slot identity) or of one slot inside
  one read transaction, with the rows of each slot only; a slot without a status row has no ballot (null).
- **SetBallotAnswerAsync** and **SetBallotSectionStateAsync** refuse the identity 0 (`ArgumentOutOfRangeException`), then
  in one transaction check the approval lock (`ReviewLocked`), check that the slot belongs to the review
  (`SlotNotFound`), replace the row of the slot, so that there is one row for each slot and check or section, make the
  ballot a draft (a submitted ballot is reopened by the edit),
  and, when the review has exactly one slot and the caller's completeness test accepts the rows of the transaction, hand
  the ballot in at once with the time and user name of the edit. The audit draft of the edit is stored first; when the
  status changed in the net, a second entry follows: `ballot.reopened` or `ballot.submitted`. The second entry is saved
  before the transaction ends and the last entry is the audit of the transaction, so the chain stays intact. Each first
  tries to overwrite the existing row with one update statement and adds a row only when there was none; if two
  requests add the same row at the same moment, the loser of the race overwrites instead, so there is always exactly
  one row and neither request fails.
- **SubmitBallotAsync** is a compare-and-set from draft to submitted: a ballot that is already submitted is returned
  as it is, with no write and no entry; otherwise the completeness test is asked about the rows of the same
  transaction, an
  incomplete ballot raises `BallotIncomplete` and stores nothing, and a complete one is stored with the time and user
  name, the audit draft of the caller (`ballot.submitted`). A slot without any row counts as an empty ballot.
- **AddSlotAsync** copies the carried review-level rows (those with `CarriedFromReviewId`) of a follow-up review into rows
  of the new slot and stores a draft ballot for it; a review without carried rows gets no ballot. A carried answer never
  submits the ballot, and no extra entry is written.
- **UpdateSlotAsync** works out which reviewers leave the slot (old primary or alternate that is neither of the new
  ones; a role swap or an added alternate leaves nobody). Their own rows (not the carried ones) are deleted, a submitted
  ballot becomes a draft (a draft stays a draft, also when the slots drop to one), and `ballot.reassigned` is the
  audit of the transaction when a row was removed or the status changed.
- **RemoveSlotAsync** deletes the answers and the section states of the slot explicitly, because their key holds the slot
  identity without a foreign key, and the status row by cascade; a signed slot is still refused by the database, which
  rolls the deletes back.

**Signature rule of the own ballot**: `AddSignatureAsync` loads the record inside its write transaction, with the same
loader as `GetRecordAsync`, and, for a review that has a checklist, refuses a signature with `BallotNotSubmitted` and
stores nothing when the ballot of the slot being signed is not submitted (it has no status row or is a draft). The
digest covers only the submitted ballots, so a draft of the signer changes nothing in it and the digest comparison
cannot see it; the service checks the same rule before the transaction, and this recheck closes the gap to an edit that
lands in between. The ballots of the other slots do not matter. A review without a checklist has no ballots and keeps
the earlier rule.

**GetRecordAsync**: Reads the record that a signature covers (`ReviewRecord`): the content fingerprint, the checklist
snapshot, the ballots of its slots (every slot's status row with its answers and section states, drafts included, in
slot order; the baseline rows are not part of them), the threads with their comments, and the source
(`ReviewSource`: `Upload`,
or for a repository review the repository identity, both commits, the selection, the review-set identifier (null when
the review was not planned), and every change with its path,
kind, and baseline and head hashes read from the stored columns without loading any bytes), and `Reopens`, the number of
stored reopens of the review, and `FollowsReviewId`; null when the review
does not exist.
The record carries the drafts so that the services can show a reviewer the ballot of their own slot, but the digest
computed from the record (`RecordDigest`) covers the consolidation of the submitted ballots only, so an edit of a draft
changes no digest.
The work is done by a private loader that takes the context, so the same code serves this method and
`AddSignatureAsync`. Threads and comments are ordered by identity, as for `ListThreadsAsync`. `GetRecordAsync` reads
inside one read transaction, so the parts come from one state of the database.

**GetSnapshotAsync**: Reads the record, the reviewer slots, and the signatures of a review (`ReviewSnapshot`) inside
one read transaction; null when the review does not exist. The transaction is a plain read transaction (not the
write-locking one of `AddSignatureAsync`), so a writer cannot commit between the reads and the three parts always
describe the same state. `SigningService.GetApprovalAsync` uses it, so the digest, the blockers, and the status of the
signatures never mix two states. Signing itself does not depend on it: `AddSignatureAsync` rechecks the digest inside
its own transaction.

**ListSnapshotsAsync**: Reads the snapshots of several reviews of a project with a constant number of queries.

- *Parameters*: the project identity and the review identifiers (not null; duplicates are read once).
- *Returns*: `Task<IReadOnlyDictionary<string, ReviewSnapshot>>` — the snapshot of each review that exists, by review
  identifier, each equal to what `GetSnapshotAsync` returns for it; a review that does not exist is absent. An empty
  input gives an empty result without opening a database connection.
- *Postconditions*: Nothing is changed.

The method opens one read transaction (the same plain read transaction as `GetSnapshotAsync`) and reads the records of
all the reviews through the shared loader `LoadRecordsAsync`, which `GetSnapshotAsync` and `GetRecordAsync` use too (as
the batch of one review), so a batch entry cannot differ from the single read. The loader reads the headers, the files,
the checklists, the status rows of the ballots, the section states and answers of the ballots (the rows of the slots,
not the baseline), the threads with their comments, and the number of reopens of all the
reviews with one query each, filtered by project and by the identifier list, and assembles one record per review with
all its ballots, drafts included. The
slots and the signatures follow with one query each, ordered by identity. The identifiers are processed in chunks of
500, so no statement exceeds the parameter limit of SQLite; the number of queries grows with the number of chunks and
not with the number of reviews. `SigningService.GetApprovalsAsync` uses it, so the progress overview of a project reads
all its reviews in a constant number of round trips.

**ListSlotsAsync**: Lists the reviewer slots of one review ordered by identity, which is the order they were added.

**ListSlotsOfUserAsync**: Lists the reviewer slots in which a user is the primary or the alternate reviewer, across all
projects, as `UserReviewSlot` rows (project identity and name, review identifier and title, slot identity, and the role
of the user: primary when the user is the primary reviewer of the slot, otherwise alternate), ordered by project,
review, and slot identity. It is one join of the slots with their review and project, using the indexes of the primary
and the alternate user of the slots (`IX_ReviewerSlots_PrimaryUserId` and `IX_ReviewerSlots_AlternateUserId`, which
already exist for the inbox), with no tracking and without loading files. The store does not decide whether a slot is
open: that depends on the signatures and the record digest, which `UserImpactService` decides with the snapshot of the
review.

**AddSlotAsync**: Stores a slot and returns it. A fresh entity is copied from the argument (project, review, primary
user, alternate user), so an identity supplied by the caller is ignored and the database assigns it. A slot for a
review that does not exist is refused by the relationship constraint of the database and surfaces as a
`DbUpdateException`; the service checks the review first, so this is a backstop.

**UpdateSlotAsync**: Replaces the primary and the alternate user of a slot of the stated review with one update
statement and returns whether a slot was found. A slot of another review is not found.

**RemoveSlotAsync**: Deletes a slot of the stated review with one delete statement and returns whether a slot was
found. A slot that has a signature is not deleted: the database refuses it and the failure surfaces as the SQLite
exception, unchanged. The service refuses to remove a signed slot before it gets here, so this is a backstop.

**ListSignaturesAsync**: Lists the signatures of one review ordered by identity, which is the order they were added.
The store offers no update and no delete of a signature: signatures are append-only.

**AddSignatureAsync**: Stores a signature if and only if its digest is the digest of the record as stored at that
moment.

- *Parameters*: `ReviewSignature signature` — the signature to store, carrying the digest the signer saw.
- *Returns*: The stored signature, with the identity assigned by the database.
- *Preconditions*: `signature` is not null.
- *Postconditions*: Either one signature is stored, or nothing is and a `DomainRuleException` is raised.

The method runs in the write transaction that the appender begins with the `Serializable` isolation level (the same
one that stores the audit entry). With Microsoft.Data.Sqlite this begins the
transaction as an immediate write transaction, so the database write lock is taken at the begin and no other writer
can change the record until the transaction ends. The method reloads the record with the shared loader, recomputes
the digest with the Core `RecordDigest.Compute` (the same code the service used to compute the digest the signer
saw), and compares it with the digest of the signature. It then checks that the slot exists in the stated review,
inserts a fresh entity copied from the argument (so the database assigns the identity), saves, and commits. The test
`ReviewStore_SerializableTransaction_TakesWriteLockAtBegin` shows the lock behavior: while one connection has only
begun a `Serializable` transaction, a write from a second connection fails with SQLITE_BUSY.

**ReopenReviewAsync**: Reopens a concluded review: stores a `ReviewReopen` and its audit entry (`review.reopened`) in
one transaction.

- *Parameters*: the project identity, the review identifier, the `ReviewReopen` to store, and the audit draft.
- *Preconditions*: `reopen` and the audit draft are not null.
- *Postconditions*: Either one reopen row is stored with its audit entry, or nothing is and a `DomainRuleException` is
  raised.

Inside the write transaction the method checks that the review row exists (`ReviewNotFound` otherwise) and that the
review is still concluded by the same check as the lock (`ReviewNotLocked` otherwise), so two administrators who reopen
at the same moment cannot both succeed: the second one finds the review no longer concluded. It then inserts a fresh
entity copied from the argument (the database assigns the identity). Signatures are not touched: the stored reopen
raises the reopen count of the record, which changes the digest and so retires the earlier signatures while they stay
in the table as history. A reopen row is never updated or deleted by the store.

**ListReopensAsync**: Lists the reopens of one review ordered by identity, which grows with the order of the reopens
(SQLite cannot order by a `DateTimeOffset`); empty when there are none or the review does not exist.

**ListReopensAsync (several reviews)**: The batch overload takes the project identity and the review identifiers and
returns, by review identifier, the reopens of each review that has any, each list equal to the single-review read
(ordered by identity); a review without a reopen is absent, and an empty input gives an empty result without a database
read. The identifiers are chunked like those of `ListSnapshotsAsync`. `SigningService.GetApprovalsAsync` calls it only
for the reviews whose record counts at least one reopen.

#### Error Handling

A duplicate review identifier is detected from the SQLite primary key and unique constraint error codes (1555 and
2067) and reported as the domain rule; this is the one provider-specific piece of knowledge in the unit. The same
detection turns the violation of the unique index on a slot and a digest into `SigningNotAllowed`, and the violation of
the key of a membership into `MembershipExists`. The last-project-administrator check raises `LastProjectAdmin` inside
the transaction, which rolls back. `AddSignatureAsync`
maps its other failures to domain rules: a digest that differs from the digest of the stored record is
`RecordChanged`, a review that does not exist is `ReviewNotFound`, and a slot that does not exist in the review is
`SlotNotFound`. A change of content, threads, or slots of a concluded review is refused with `ReviewLocked` and a
reopen of a
review that is not concluded with `ReviewNotLocked`. In every failure the transaction is rolled back, so nothing is
stored. Any other database failure propagates unchanged to the caller, including the refusal to add a slot for a
review that does not exist and the
refusal to delete a signed slot. Null arguments raise `ArgumentNullException`.

#### Dependencies

- **ReviewCenterDbContext** — the schema and the context factory.
- **AuditAppender** — runs every change together with its audit entry.
- **Core domain types and IReviewStore** — the types stored and the interface implemented, including `ReviewRecord`,
  `ReviewerSlot`, `ReviewSignature`, `ReviewReopen`, `ReviewLock`, `RecordDigest`, and `AuditDraft`.
- **Entity Framework Core and Microsoft.Data.Sqlite** — queries and the error code of a constraint violation.

#### Callers

- **EvidenceSettingsService**, **ProjectChecklistService**, **ProjectService**, **ReviewService**, **ThreadService**,
  **ChecklistService**,
  **SigningService**, **UserImpactService**, and
  **ProjectAccess** — through the
  Core interface.
- **ServerHost** — registers the store as the implementation of the Core interface.
