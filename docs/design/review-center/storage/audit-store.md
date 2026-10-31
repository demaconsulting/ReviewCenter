### AuditStore

![Storage Structure](StorageView.svg)

#### Purpose

`AuditStore` is the persistence of the audit trail. It consists of two tightly coupled classes in two files:
`AuditAppender`, which owns the process-wide gate and builds each new entry of the hash chain inside a transaction,
and `AuditStore`, the implementation of the Core `IAuditStore` interface (standalone append, paged read, and the
ordered batch read used by verification). Its single responsibility is to make sure that every entry is appended once,
in order, linked to its predecessor, and, for a change of data, in the same transaction as the change.

#### Data Model

**AuditAppender** is an internal sealed singleton, shared by `ReviewStore`, `UserStore`, and `AuditStore`, and is
thread-safe. It holds the clock (`TimeProvider`, injected so tests control time) and one **Gate**, a
`SemaphoreSlim` with a single slot.

**The table** is `AuditEntries`, mapped by `ReviewCenterDbContext`: `Sequence` is the primary key and is never generated
by the database (`ValueGeneratedNever`), `ActorName`, `Action`, and `Detail` are required with the maximum lengths of
`AuditText`, `PreviousHash` and `Hash` are required and 64 characters long, `TimestampUtc` is stored as fixed-width UTC
text (see *Decision on the date range* below), and four indexes serve the reads: project, review, and sequence for the
history of one review, and project and sequence, actor and sequence, and action and sequence for the filters of the audit
pages. There are **no foreign keys**: an entry is a snapshot of text and must survive
whatever happens to the rows it describes.

#### Key Methods

**AuditAppender.ExecuteAsync**: Runs one change and its audit entry in one transaction. Every audited method of
`ReviewStore` and `UserStore` is written as a call of this method.

- *Parameters*: the context factory; a function that makes the change in the given context and returns its result
  together with the `AuditDraft` (or `null`); a cancellation token.
- *Returns*: The result of the change.
- *Algorithm*:
  1. Wait for the gate.
  2. Create a context and begin a *Serializable* transaction (SQLite `BEGIN IMMEDIATE`: the write lock is taken at
     the start, so no other process can append between reading the head of the chain and saving the new entry).
  3. Run the change. The change may save by itself, for example to learn an assigned identity; all of it is inside the
     transaction.
  4. When the draft is not `null`, append the entry (see `AppendAsync`).
  5. Save, commit, and release the gate in a `finally` block.
- *Postconditions*: The change and the entry are both stored, or nothing is. A `null` draft means that the change did not
  take effect (or is not audited), so no entry is appended. The gate is released on every path, including cancellation
  and failure.

**AuditAppender.AppendAsync**: Adds the next entry to the context of the open transaction (the caller saves).

- *Algorithm*:
  1. Read the head of the chain: the entry with the highest sequence (its sequence and hash), or the genesis state of an
     empty trail (sequence 0 and the genesis hash).
  2. Resolve the acting user: look the user name up in the users table in the same context (the column compares without
     regard to case); the identity is `null` for the command line and for an unknown actor.
  3. Stamp the time with the clock. This happens inside the gate, so the time never goes backwards along the sequence.
  4. Clean the actor text, the action, and the detail with `AuditText` and build the entry with sequence = head + 1 and
     the previous hash = the head hash.
  5. Compute the hash of exactly the entry that will be stored (`AuditChain.ComputeHash`), and add the entry.

**AuditStore.AppendAsync**: The standalone path for events that change no data: gate, Serializable transaction,
`AppendAsync`, save, commit, release. Used through `AuditService.TryRecordAsync` for sign-in, sign-out, failed sign-in,
and lockout.

**AuditStore.ListAsync**: Reads entries newest first, narrowed by an `AuditFilter`, before or after a sequence, at most
a given number. Every set condition of the filter must hold:

- *Project and review*: a project filter returns every event of that project, whether or not it names a review, and
  never an entry without a project (the comparison is `ProjectId == x`, which is never true for `NULL`), which is what
  the project audit view relies on. The review is an exact comparison; with no project in the filter (the global audit
  page, for system administrators only) it matches that review identifier in every project.
- *Actor*: the identity is an exact comparison. The text is a case-insensitive substring of the stored actor name,
  written as `EF.Functions.Like(ActorName, '%' + escaped + '%', '\')`; the escape function doubles `\` first and then
  escapes `%` and `_`, so a typed percent sign or underscore is a literal. The `LIKE` of SQLite folds ASCII letters
  only, which is how the case-insensitivity of the filter is defined.
- *Action*: one exact action, or a group by prefix (`StartsWith`, whose translation also escapes the wildcards).
- *Date range*: `TimestampUtc >= start of the From day` and `TimestampUtc < start of the day after To`, in UTC. The
  upper bound is exclusive, so an entry at 23:59:59.9999999 of the To day is inside and one at 00:00:00 of the next day
  is outside.

The newer direction reads the nearest entries above the position in ascending order and returns them newest first.
Reads are untracked and never take the gate, and no count of the trail is made. The indexes that serve the filters
are `(ProjectId, ReviewId, Sequence)` for one review, `(ProjectId, Sequence)` for a project, `(ActorUserId, Sequence)`
for an actor and `(Action, Sequence)` for an action; each ends with `Sequence` so the keyset order needs no sort. The
actor text and the date range are residual conditions on the entries that the other conditions and the cursor leave,
and the page ends as soon as it is full.

**Decision on the date range (the spike).** `AuditEntry.TimestampUtc` is a `DateTimeOffset`, which the EF Core SQLite
provider stores as text with the offset, and it cannot translate a comparison of such a value (the query raises an
`InvalidOperationException`); text with offsets would not sort or compare correctly in any case. The design therefore
maps the property with a value converter to fixed-width UTC text (year, month, day, `T`, hour, minute, second, seven
fractional digits, and `Z`), which sorts
lexicographically in time order and reads back as the same instant with offset zero. The converter changes only the
stored form: the hash of an entry is computed in code from the ticks of the instant (`AuditChain.ComputeHash`), never
from the stored text, so no hash changes and the chain verifies as before (a test writes entries with offsets, reads the
raw text, and verifies the chain). The alternative, to map the days to a range of sequences with two lookups, was
rejected as more code for the same result. Databases created before this change store the time in the old form and must
be deleted and created again, like for every change of the schema (there are no migrations).

**AuditStore.ReadAfterAsync**: Reads entries in ascending order after a sequence, at most a given number, so that
verification can stream the trail in batches.

**AuditStore.ReadMatchingAsync**: The read of the export: entries that match an `AuditFilter`, in ascending sequence
order, with `Sequence > afterSequence` and `Sequence <= upToSequence`, at most a given number (a keyset batch; the caller
passes the last sequence it received). It applies exactly the conditions of `ListAsync` (the same private filter
function), so the export holds what the page shows and a project filter never returns an entry without a project. The
upper bound is the snapshot of the export: entries appended later are never read. The read is untracked, takes no
gate, and uses the indexes of the filter ending in `Sequence`.

**AuditStore.LatestSequenceAsync**: Returns the highest sequence of the trail, or 0 for an empty trail (the maximum of
the primary key). The export reads it to fix its snapshot before it verifies the chain.

**AuditStore.LastActivityAsync**: Returns, for one project and with one grouped query, the time of the newest entry of
each review of that project, by review identifier. The newest entry of a review is the one with the highest sequence
(the existing index on project, review, and sequence serves the grouping), and the time is read from those entries, so
no time value is aggregated. Entries without a review and entries of other projects are not included, and a review
without an entry is absent. The read is untracked, takes no gate, and changes nothing in the chain. The project overview
uses it as the "last activity" of a review. This is the exact last activity because the audit entries of a review are
never combined: only sign-in, token, and throttle events are coalesced (a window of one minute), and none of them
names a review.

#### Why The Design Is Safe

- **No fork**: within the process, the single gate means two appends never read the same head. Across processes (the
  `audit verify` and `user add` verbs run beside the server), the write lock of the Serializable transaction
  serializes them in the database.
- **No lost or invented entry**: a change and its entry are in one transaction, so a crash leaves both or neither. An
  entry is written only when the change took effect.
- **Backstop**: the primary key on `Sequence` refuses a duplicate sequence. A violation rolls back the whole
  transaction, the change included, and the gate is still released.
- **No deadlock**: the gate is acquired after any service gate and released first, and nothing inside it calls a
  service or waits for another lock.
- **Append-only**: neither the interface nor the store offers any operation that updates or deletes an entry, and a test
  checks the public surface by reflection. This is a property of the *application*; it does not stop someone with write
  access to the database file from changing it, and that is exactly what the hash chain is for (see `AuditChain`).

#### Error Handling

A database failure inside a transaction propagates to the caller after the transaction has rolled back and the gate has
been released; the caller (a service) lets it surface for a change of data, and the best-effort path
(`AuditService.TryRecordAsync`) catches and logs it for events that change no data. A failure of the draft function of a
store method also rolls everything back. Cancellation is honored while waiting for the gate and during the database calls.

#### Dependencies

- **ReviewCenterDbContext** — the `AuditEntries` table and the users table (for the actor identity).
- **AuditChain**, **AuditDraft**, and **AuditText** (Core) — the entry, the hash, and the text cleaning.
- **IAuditStore** (Core) — the interface this unit implements.
- **Entity Framework Core** — transactions and queries.
- **.NET runtime library** — `SemaphoreSlim` and `TimeProvider`.

#### Callers

- **ReviewStore** and **UserStore** — run every audited mutation through `AuditAppender.ExecuteAsync`.
- **AuditService** — appends, lists, and reads through `IAuditStore`.
- **ServerHost** — registers the appender and the store as singletons.
