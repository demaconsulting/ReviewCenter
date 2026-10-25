### AuditService

![Core Structure](CoreView.svg)

#### Purpose

`AuditService` is the way the rest of the system reads, records, and verifies the audit trail. Its responsibilities
are to record the events that change no data (sign-in, sign-out, failed sign-in, lockout) on a best-effort basis,
to read the trail one page at a time, and to verify the hash chain by streaming. It also defines the store contract
`IAuditStore` and the page type `AuditEntryPage`. The events that belong to a change of data are *not* recorded by this
service: they are recorded by the store in the same transaction as the change (see *Atomicity* below and the
`AuditStore` unit).

#### Atomicity

A change of data and its audit entry must exist together or not at all, and the chain must never fork. The design that
achieves this is:

1. **One writer at a time.** The `AuditAppender` (see the `AuditStore` unit) owns one process-wide gate, a semaphore with
   a single slot, shared by *all* stores (`ReviewStore`, `UserStore`, and `AuditStore`). Every transaction that writes an
   audit entry holds the gate, so two entries are never appended at the same time within the process, whichever store
   they come from.
2. **One transaction per mutation.** Inside the gate each audited store method opens a *Serializable* transaction (for
   SQLite this is `BEGIN IMMEDIATE`, which takes the database write lock when the transaction begins, so another
   process, for example the command line, is serialized by the database as well). The method makes the change,
   appends the audit entry to the same context, saves, and commits. If anything fails, everything rolls back: a change
   without an entry and an entry without a change both cannot be stored.
3. **A backstop in the database.** `Sequence` is the primary key of the table, assigned under the gate and never by the
   database, so even a defect in the gate could not store two entries with one sequence: the second insert fails and
   its whole transaction rolls back.
4. **Same transaction, no callbacks.** The store methods take an `AuditDraft` (or a function that builds one from the
   stored entity, when the draft needs an identity the database assigns), so the services decide *what* is recorded and
   the stores decide *when*. No store method calls a service while the gate is held, and the gate is always the
   innermost lock, after the gates of `UserService` and `SigningService`, so no lock cycle is possible.
5. **Only when the change took effect.** A store method that reports "not found" or "no change" (a `null` or `false`
   result) appends no entry.

Events that change no data take the standalone path, `IAuditStore.AppendAsync`: it takes the same gate and writes the
entry in its own transaction.

#### Data Model

The service is stateless and thread-safe (it relies on the store, which is).

**PageSize**: `int` (constant) — 50 entries on a page.

**AuditEntryPage**: the entries of one page, newest first, `HasOlder` and `HasNewer` (whether more entries lie beyond
either end), and the cursors `OlderBefore` (the sequence of the last entry of the page) and `NewerAfter` (the sequence of
the first entry), each `null` for an empty page.

**IAuditStore**: the contract the Storage subsystem implements. It has three operations: `AppendAsync` (one entry in its
own transaction), `ListAsync` (newest first, optionally of one review, before or after a sequence, with a size limit),
and `ReadAfterAsync` (ascending batches for verification). There is deliberately **no operation that updates or deletes
an entry**.

**Action codes and details**: every recorded event has an action code and a one-line detail. The project and review
identifiers are stored in their own fields, not in the detail. Details never contain passwords, password hashes,
tokens, comment text, or justification text; where an entry must identify such a text it carries `sha256` followed by
the short hash (8 hexadecimal characters) of the trimmed text (see `AuditChain`).

| Action | Detail |
| ------ | ------ |
| `project.created` | `project 'Name'` |
| `review.created` | `review 'Title', fingerprint <64 hex>, N file(s)` |
| `slot.added`, `slot.updated`, `slot.removed` | `slot {id}: primary ada, alternate bob` (`none` for no alternate) |
| `signature.added` | `slot {slotId}, role {Primary or Alternate}, record digest <64 hex>` |
| `thread.started` | `thread {id} on {path} {anchor}; comment {cid}, text sha256 {8 hex}` |
| `thread.replied` | `thread {id}, comment {cid}, text sha256 {8 hex}` |
| `thread.resolved`, `thread.reopened` | `thread {id}` |
| `checklist.answered` | `item {id}: {Outcome}`, plus `; justification sha256 {8 hex}` when there is one |
| `checklist.applicability-changed` | `section {id}:` then `applicable` or `not applicable, reason sha256 {hex}` |
| `user.created` | `user 'bob', role Reviewer` |
| `user.enabled`, `user.disabled` | `user 'bob'` |
| `user.role-changed` | `user 'bob': Reviewer -> Admin` |
| `user.password-set-by-admin` | `user 'bob'` |
| `user.password-changed` | `user 'bob'` (the actor is the user who changed their own password) |
| `auth.sign-in` | `signed in` |
| `auth.sign-out` | `signed out` |
| `auth.sign-in-failed` | `wrong password`, `account disabled`, or `unknown user` |
| `auth.locked-out` | `locked for 5 minutes after 5 failed attempts` |

The anchor of `thread.started` is `line N`, `lines S-E`, or `(whole file)`.

The actor of an entry is the signed-in user, taken on the server from the authentication state and never from form
input, stored as `Display Name (username)`. An action from the command line (`user add`) has the actor
`command line`. For `auth.sign-in-failed` with an unknown user name the actor text is the literal `unknown user`: the
name that was typed is never echoed into the trail, so the trail cannot be filled with attacker-chosen text. For a
failure on an existing account the existing user is the actor. `thread.resolved` and `thread.reopened` are recorded for
every successful call, even when the thread already had that state.

Changes are recorded in the same transaction as the change; sign-in, sign-out, failed sign-in, and lockout are recorded
on the best-effort path in their own transaction.

#### Key Methods

**RecordAsync**: Records one event and lets a failure propagate.

- *Parameters*: `AuditDraft draft`; a cancellation token.
- *Returns*: The stored `AuditEntry`.
- *Preconditions*: `draft` is not null.

**TryRecordAsync**: Records one event on a best-effort basis.

- *Parameters*: `AuditDraft draft`; a cancellation token.
- *Returns*: `true` when the entry was stored; `false` when storing failed.
- *Postconditions*: A storage failure is passed to the error hook given at construction (the host logs it) and
  swallowed; a failure of the hook itself is swallowed as well. Cancellation is **not** swallowed. The call is always
  awaited, so the entry exists when the call returns, but a failure never reaches the user: a problem with the trail
  cannot lock anybody out of signing in.

**ListAsync**: Reads one page of the trail, newest first.

- *Parameters*: `int? projectId` and `string? reviewId` (together they limit the page to one review; a `null` review
  gives the whole trail), `long? beforeSequence` (the Older page), `long? afterSequence` (the Newer page), and a
  cancellation token.
- *Returns*: An `AuditEntryPage` of at most 50 entries.
- *Postconditions*: When both positions are given, `beforeSequence` wins. `HasOlder` and `HasNewer` come from one cheap
  look past each end of the page, so a link is shown only when there is something behind it.

**VerifyAsync**: Verifies the whole chain.

- *Returns*: An `AuditVerificationResult`.
- *Postconditions*: The service reads ordered batches of 500 entries through `ReadAfterAsync`, feeds them to the
  `AuditChainVerifier`, and stops at the first failure. The whole trail is never held in memory, so the memory use is
  bounded by the batch, and the time grows linearly with the number of entries. Entries appended while the
  verification runs are tolerated: it ends when a batch comes back empty.

#### Error Handling

`RecordAsync` lets store failures propagate. `TryRecordAsync` swallows everything except cancellation, as described above;
this is a deliberate choice, because a failing trail must not disable sign-in, and the failure is logged. This means a
sign-in event can be missing from the trail when the write fails. Null arguments are rejected with
`ArgumentNullException`.

#### Known Limitations

- A failed attempt on an account that is **already locked** is not recorded; only the lockout itself is, which limits
  flooding of the trail for existing accounts. Failed attempts for unknown user names (recorded as `unknown user`) are
  not throttled in this slice, so an unauthenticated caller can grow the trail; rate limiting is a later hardening
  step.
- A wrong *current* password typed on the signing dialog or on the account page appears as `auth.sign-in-failed`,
  because it passes through the same failure path of `UserService`; the action code does not tell where it happened.
- The trail has no export and no retention or archiving function in this slice.
- The chain is tamper-*evident*, not tamper-*proof*; see *What The Chain Proves And What It Does Not* in `AuditChain`.
  Removal of the newest entries, or a complete rewrite by someone with write access to the database, is not detected
  unless the head hash was recorded elsewhere, and this slice does not record it anywhere.
- The schema is created without migrations, so an existing development database has no audit table and must be deleted
  and created again (see the `Storage` design).

#### Dependencies

- **IAuditStore** (Core, implemented by `AuditStore` in Storage) — persists and reads entries.
- **AuditChain** — the verifier and the draft and entry types.

#### Callers

- **UserService** — records failed sign-ins and lockouts through `TryRecordAsync` (when an `AuditService` was supplied).
- **SignIn** (Identity) — records sign-in and sign-out through `TryRecordAsync`.
- **HistoryPage** and **AuditPage** (Web) — list entries; the audit page also verifies the chain.
- **AuditCommand** (Hosting) — verifies the chain for the `audit verify` verb.
- **Services that change data** (`ProjectService`, `ReviewService`, `ThreadService`, `ChecklistService`,
  `SigningService`, `UserService`) — do not call this service for their changes; they pass drafts to their stores.
