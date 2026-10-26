### ReviewStore

![Storage Structure](StorageView.svg)

#### Purpose

`ReviewStore` implements the Core store interface on top of `ReviewCenterDbContext`. Its single responsibility is
to persist projects, reviews, file content, comment threads, checklists, reviewer slots, and signatures, and to read
them back, translating database behavior into the terms of the domain.

#### Data Model

The unit is stateless apart from the injected context factory and the shared `AuditAppender`. Every read creates its own
short-lived context from the factory and disposes it before returning, so concurrent operations never share a context.
Reads use no change tracking. Every change goes through `AuditAppender.ExecuteAsync`, which takes the process-wide audit
gate, opens a `Serializable` transaction, runs the change, appends the audit entry to the same context, saves, and
commits (see the `AuditStore` unit).

#### Key Methods

**Audit transaction of every change**: `AddProjectAsync`, `AddReviewAsync`, `AddThreadAsync`, `AddCommentAsync`,
`SetThreadStateAsync`, `SetChecklistSectionStateAsync`, `SetChecklistAnswerAsync`, `AddSlotAsync`, `UpdateSlotAsync`,
`RemoveSlotAsync`, and `AddSignatureAsync` take the audit draft of the change (or a function that builds it from the
stored entity, when the draft names an identity that the database assigns). The change and its audit entry are
stored in the same transaction, so both exist or neither does. A change that did not take effect (a thread, slot, or
review that is not found) returns its null or false result and stores no entry. A failure of any kind, including a
duplicate review identifier or a refused signature, rolls back the change and the entry together. The descriptions below
are of the change itself.

**AddProjectAsync**: Stores a project and returns it with the identity assigned by the database. The unit copies the
name, description, and creation time into a new entity, so an identity supplied by the caller is ignored and can
neither be persisted nor collide with an existing project; the returned project is the stored entity.

**GetProjectAsync**: Reads one project by identity; null when absent.

**ListProjectsAsync**: Lists all projects ordered by name and then identity. SQLite cannot order by a
`DateTimeOffset`, so the creation time is not used for ordering.

**ReviewExistsAsync**: Reports whether a review with the identifier exists in a project.

**AddReviewAsync**: Stores a review, all its files, and its checklist snapshot (when it has one) in one save, which
is one transaction.

- *Postconditions*: Either the review, all its files, and its snapshot are stored, or nothing is. An identifier that already
  exists in the project, whether found by an earlier check or by a race, is reported as a `DomainRuleException`
  for `ReviewIdDuplicate`. A review for a project that does not exist is not stored; the relationship constraint
  of the database refuses it.

**GetReviewAsync**: Reads one review with its source columns and the metadata of its files (path, size, hash, change
kind, baseline size and hash) ordered by path, and without file bytes (neither head nor baseline), so that showing a
review with large files does not load them; null when absent.

**ListReviewsAsync**: Lists the reviews of a project ordered by identifier, without files.

**GetFileContentAsync**: Reads the bytes of one file of a review, exactly as stored; null when absent.

**GetBaselineContentAsync**: Reads the frozen baseline bytes of one file of a review; null when the review or file is
absent or the file has no baseline.

**AddThreadAsync**: Stores a thread together with its first comment in one save, which is one transaction. Fresh
entities are copied from the argument, so identities supplied by the caller are ignored and the database assigns
them; the comment keeps the display name and the user name of its author. A thread for a file that does not exist in
the review is refused by the relationship constraint of the
database.

**AddCommentAsync**: Appends a comment to a thread, with the display name and the user name of its author. It first
checks that the thread belongs to the stated project
and review and returns null when it does not, so a thread identity from another review cannot be commented on.

**SetThreadStateAsync**: Sets the state of a thread of the stated review with a single update statement and
returns whether a thread was found.

**ListThreadsAsync**: Lists the threads of one review, for every file, with their comments. Threads and comments
are ordered by identity, which grows with the order of creation; the creation time is not used for ordering because
SQLite cannot order by a `DateTimeOffset`. All threads and comments of the review are read at once: there is no cap
on their number and no pagination, which is deferred until real volumes are known.

The bytes of files are stored and returned verbatim, including empty files and files containing carriage
returns or zero bytes.

**GetChecklistAsync**: Reads the checklist snapshot of a review; null when the review has none.

**ListChecklistSectionStatesAsync** and **ListChecklistAnswersAsync**: List the stored section states and the
answers of a review, with display names, user names, and UTC times as saved.

**SetChecklistSectionStateAsync** and **SetChecklistAnswerAsync**: Replace the state of a section or the answer to
a check, including the display name and the user name of the person who made the change. Each first tries to
overwrite the existing row with one update statement and adds a row only when there
was none; if two requests add the same row at the same moment, the loser of the race overwrites instead, so
there is always exactly one row and neither request fails.

**GetRecordAsync**: Reads the record that a signature covers (`ReviewRecord`): the content fingerprint, the checklist
snapshot, the section states, the answers, the threads with their comments, and the source (`ReviewSource`: `Upload`,
or for a repository review the repository identity, both commits, the selection, the review-set identifier (null when
the review was not planned), and every change with its path,
kind, and baseline and head hashes read from the stored columns without loading any bytes); null when the review does
not exist.
The work is done by a private loader that takes the context, so the same code serves this method and
`AddSignatureAsync`. Threads and comments are ordered by identity, as for `ListThreadsAsync`. `GetRecordAsync` reads
inside one read transaction, so the parts come from one state of the database.

**GetSnapshotAsync**: Reads the record, the reviewer slots, and the signatures of a review (`ReviewSnapshot`) inside
one read transaction; null when the review does not exist. The transaction is a plain read transaction (not the
write-locking one of `AddSignatureAsync`), so a writer cannot commit between the reads and the three parts always
describe the same state. `SigningService.GetApprovalAsync` uses it, so the digest, the blockers, and the status of the
signatures never mix two states. Signing itself does not depend on it: `AddSignatureAsync` rechecks the digest inside
its own transaction.

**ListSlotsAsync**: Lists the reviewer slots of one review ordered by identity, which is the order they were added.

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

#### Error Handling

A duplicate review identifier is detected from the SQLite primary key and unique constraint error codes (1555 and
2067) and reported as the domain rule; this is the one provider-specific piece of knowledge in the unit. The same
detection turns the violation of the unique index on a slot and a digest into `SigningNotAllowed`. `AddSignatureAsync`
maps its other failures to domain rules: a digest that differs from the digest of the stored record is
`RecordChanged`, a review that does not exist is `ReviewNotFound`, and a slot that does not exist in the review is
`SlotNotFound`. In every failure the transaction is rolled back, so nothing is stored. Any other database failure
propagates unchanged to the caller, including the refusal to add a slot for a review that does not exist and the
refusal to delete a signed slot. Null arguments raise `ArgumentNullException`.

#### Dependencies

- **ReviewCenterDbContext** — the schema and the context factory.
- **AuditAppender** — runs every change together with its audit entry.
- **Core domain types and IReviewStore** — the types stored and the interface implemented, including `ReviewRecord`,
  `ReviewerSlot`, `ReviewSignature`, `RecordDigest`, and `AuditDraft`.
- **Entity Framework Core and Microsoft.Data.Sqlite** — queries and the error code of a constraint violation.

#### Callers

- **ProjectService**, **ReviewService**, **ThreadService**, **ChecklistService**, and **SigningService** — through the
  Core interface.
- **ServerHost** — registers the store as the implementation of the Core interface.
