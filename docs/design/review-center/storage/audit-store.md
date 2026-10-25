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
`AuditText`, `PreviousHash` and `Hash` are required and 64 characters long, and an index on project, review, and
sequence serves the history of one review. There are **no foreign keys**: an entry is a snapshot of text and must survive
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

**AuditStore.ListAsync**: Reads entries newest first, optionally of one review (project and review together), before or
after a sequence, at most a given number. The newer direction reads the nearest entries above the position in ascending
order and returns them newest first. Reads are untracked and never take the gate.

**AuditStore.ReadAfterAsync**: Reads entries in ascending order after a sequence, at most a given number, so that
verification can stream the trail in batches.

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
