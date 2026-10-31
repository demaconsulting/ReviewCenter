### AuditService

![Core Structure](CoreView.svg)

#### Purpose

`AuditService` is the way the rest of the system reads, records, and verifies the audit trail. Its responsibilities
are to record the events that change no data (sign-in, sign-out, failed sign-in, attempts on a locked account, refusals
by the sign-in throttle, failed re-authentication, lockout) on a best-effort basis, with a coalescing variant for events
that a caller can repeat without limit,
to read the trail one page at a time, to verify the hash chain by streaming, and to stream an export of the filtered
trail as CSV or JSON (see *Export* below, with `AuditExportWriter`). It also defines the store contract
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

The service is thread-safe. It relies on the store for the trail, and it keeps one small piece of state of its own for
coalescing (see `TryRecordCoalescedAsync`): a dictionary from a key to the start of the current one-minute window and
the number of events counted but not recorded in it, behind one lock that is never held across an await. A second
piece of state tracks the current overflow window (when it started and how many events it suppressed) for the case
that the table is full. The state is
in memory of one process and is lost on restart. The service takes an optional `TimeProvider` (the system clock when
none is given) so tests can move time.

**DefaultPageSize**: `int` (constant) — 50 entries on a page when the caller gives no size. **MaxPageSize**: `int`
(constant) — 200; no page is ever larger. **PageSizes**: the sizes the pages offer (25, 50 and 100).

**AuditFilter**: the record of the conditions of a listing (see the **AuditFilter** and **AuditActions** section below).

**MaxCoalescedKeys**: `int` (constant) — 500; the most keys the coalescing table holds.

**OverflowAction**: `string` (constant) — `audit.events-suppressed`; the action of the entry that tells events of new
keys were suppressed because the table was full.

**ExportBatchSize**: `int` (constant) — 500; the number of entries `ExportAsync` reads, writes and flushes at a time.
**ExportedAction**: `string` (constant) — `audit.exported`; the action of the entry that records an export.

**AuditEntryPage**: the entries of one page, newest first, `HasOlder` and `HasNewer` (whether more entries lie beyond
either end), and the cursors `OlderBefore` (the sequence of the last entry of the page) and `NewerAfter` (the sequence of
the first entry), each `null` for an empty page.

**IAuditStore**: the contract the Storage subsystem implements. It has three operations: `AppendAsync` (one entry in its
own transaction), `ListAsync` (newest first, narrowed by an `AuditFilter`, before or after a sequence, with a size
limit), and `ReadAfterAsync` (ascending batches for verification). `ListAsync` is the read behind the whole-trail, the
project-level and the review-level view; the hash chain is unchanged by a filtered read of the same chain, and the
whole chain still verifies. There is deliberately **no operation that updates or deletes
an entry**.

**Action codes and details**: every recorded event has an action code and a one-line detail. The project and review
identifiers are stored in their own fields, not in the detail. Details never contain passwords, password hashes,
tokens, comment text, or justification text; where an entry must identify such a text it carries `sha256` followed by
the short hash (8 hexadecimal characters) of the trimmed text (see `AuditChain`). The typed user name of
`project.member-add-failed` is attacker-chosen text, cleaned and cut by the audit store like the typed name of a failed
sign-in; the number of such entries one actor can cause is bounded by the throttle of `ProjectService` (a system
administrator is not throttled but is a trusted actor).

| Action | Detail |
| ------ | ------ |
| `project.created` | `project 'Name'` |
| `project.member-added` | `user 'bob', role ProjectAdmin` (the project identifier is in its own field) |
| `project.member-role-changed` | `user 'bob', role User` (the new role; the project identifier is in its own field) |
| `project.member-removed` | `user 'bob'` (the project identifier is in its own field) |
| `project.account-created` | `user 'bob', role User, temporary password` (never the password; project id is separate) |
| `project.member-add-failed` | `user name '<typed>'` (failed add or account creation; best-effort; project id apart) |
| `project.evidence-settings-changed` | `AnonymousIndex off->on; IncludeThreads on->off` (changed keys; project apart) |
| `project.checklist-changed` | `version V, N sections, M items, sha256 {8 hex}, previous version P` (or reset) |
| `review.created` | `review 'Title', fingerprint <64 hex>, N file(s)`, plus follow-up and due date text (see below) |
| `review.due-date-changed` | `none -> 2030-01-31` (old and new due date, `none` for no date; review in its own field) |
| `slot.added`, `slot.updated`, `slot.removed` | `slot {number}: primary ada, alternate bob` (`none` for no alternate) |
| `signature.added` | `slot {number}, role {Primary or Alternate}, record digest <64 hex>` |
| `thread.started` | `thread {id} on {path} {anchor}; comment {cid}, text sha256 {8 hex}` |
| `thread.replied` | `thread {id}, comment {cid}, text sha256 {8 hex}` |
| `thread.resolved`, `thread.reopened` | `thread {id}` |
| `checklist.answered` | `item {id}: {Outcome}`, plus `; justification sha256 {8 hex}` when there is one |
| `checklist.applicability-changed` | `section {id}:` then `applicable` or `not applicable, reason sha256 {hex}` |
| `user.created` | `user 'bob'`, plus `, system administrator` when the account is one |
| `user.enabled`, `user.disabled` | `user 'bob'` |
| `user.admin-changed` | `user 'bob': system administrator False -> True` |
| `user.password-set-by-admin` | `user 'bob'` |
| `user.unlocked` | `user 'bob'` |
| `user.password-changed` | `user 'bob'` (the actor is the user who changed their own password) |
| `repository.created` | `repository 'name', location 'L', user name set` (or `none`), `token set` (or `none`) |
| `repository.updated` | `repository 'name'`, a colon, then the changes, such as `location`, `token replaced` |
| `repository.enabled`, `repository.disabled` | `repository 'name'` |
| `auth.sign-in` | `signed in`, or after step two `second factor: authenticator code` or `... recovery code` |
| `auth.sign-out` | `signed out` |
| `auth.sign-in-failed` | `wrong password`, `account disabled`, `second factor required`, `unknown user, name 'X'` |
| `auth.sign-in-locked` | `account locked` (an attempt on an account that is already locked) |
| `auth.sign-in-throttled` | `too many attempts, name '<typed>'`, or `too many attempts, second step, name '<typed>'` |
| `signature.reauth-failed` | as for sign-in, `password change pending`, `second factor required`, or `wrong code` |
| `auth.locked-out` | `locked for 5 minutes after 5 failed attempts` |
| `auth.totp-enrolled` | `second factor set up, 10 recovery codes issued` (same transaction as the change) |
| `auth.totp-disabled` | `second factor removed by its owner` (same transaction as the change) |
| `auth.totp-reset` | `second factor of user 'bob' reset` (actor is the administrator; same transaction) |
| `auth.totp-recovery-used` | `recovery code used to sign in`, or `... to remove the second factor` (same transaction) |
| `auth.totp-recovery-regenerated` | `10 new recovery codes issued` (same transaction as the change) |
| `auth.totp-failed` | `wrong or reused code (sign-in)`, `(enrollment)`, or `(account page)` (fixed text; best-effort) |
| `audit.events-suppressed` | `tracking table full, events of new keys are not recorded` (see below) |

The detail of `review.created` has, after the file text, the text `, follows 'X'; carried N answers, M threads` for a
follow-up (`X` is the whole identifier of the earlier review), with `, R resolved threads not carried` and
`, open threads on F files dropped` when they apply, and the text
`, due yyyy-MM-dd` when a due date is set.

An overflow entry (`audit.events-suppressed`) has the actor text `audit service`. It is written at most once per
minute, when the coalescing table is full; the second form of its detail,
`N events suppressed in the previous window, tracking table full`, carries the number of events of the previous
overflow window.

An event that is recorded through `TryRecordCoalescedAsync` can have the suffix `; N more in the minute after the
previous entry` on its detail, where N is the number of events of the same key that were counted and not recorded.
`auth.sign-in-locked`, `auth.sign-in-throttled`, and the unknown-user and disabled-account entries of
`auth.sign-in-failed` and `signature.reauth-failed` (and the locked-account entries of the latter) are recorded this
way; the wrong-password failures are not (they are bounded by the lockout).

The anchor of `thread.started` is `line N`, `lines S-E`, or `(whole file)`; a line anchor on the baseline side has the
suffix `(baseline)`, for example `line 3 (baseline)` and `lines 2-4 (baseline)`.

The actor of an entry is the signed-in user, taken on the server from the authentication state and never from form
input, stored as `Display Name (username)`. An action from the command line (`user add`) has the actor
`command line`. For a failure with an unknown user name the actor text is the literal `unknown user` and the detail
carries the typed name after it is cleaned: control characters become spaces, so it is one line, and it is cut to the
length of a user name (the same limit as for a real name) with the store's text rules. Typed names are
attacker-chosen text, so this cleaning and the cap are what keep the trail from being forged or filled with long text;
a password is never recorded. For a failure on an existing account the existing user is the actor, and the typed name
is not recorded again. For `auth.sign-in-throttled` there is no known account, so the actor text is the literal
`not signed in` and the detail carries the cleaned typed name. The address of the client is **not** recorded in any
entry: the entry has no field for it, and adding one would change the hash chain format; the address is only in the web
server log. `thread.resolved` and `thread.reopened` are recorded for
every successful call, even when the thread already had that state.

Changes are recorded in the same transaction as the change (this includes the second factor: `TotpService` takes no
`AuditService`, and the entries `auth.totp-enrolled`, `auth.totp-disabled`, `auth.totp-reset`,
`auth.totp-recovery-used`, and `auth.totp-recovery-regenerated` are built as drafts and saved by `TotpStore`);
sign-in, sign-out, failed sign-in, attempts on a locked
account, refusals by the throttle, failed re-authentication, a wrong second factor code (`auth.totp-failed`), and
lockout are recorded on the best-effort path in their
own transaction. A code, a secret, a recovery code, and a challenge token are never part of an entry.

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

**TryRecordCoalescedAsync**: Records a repeating event on a best-effort basis, at most once per key and minute.

- *Parameters*: `AuditDraft draft`; `string key` — identifies the repeating event (the callers use the action, the
  kind of refusal, and the lower-cased typed name); a cancellation token.
- *Returns*: `true` when an entry was stored; `false` when the event was only counted, suppressed, or storing failed.
- *Preconditions*: `draft` and `key` are not null.
- *Postconditions*: When cancellation is already requested the call throws `OperationCanceledException` before it
  looks at the table, so a cancelled request neither counts a repeat nor starts a window. The first event of a key is
  stored (as by `TryRecordAsync`) and starts a window of one minute.
  Repeats of the key inside the window are counted and not stored. The first event of the key after the window is
  stored with the number of counted repeats appended to its detail (`; N more in the minute after the previous
  entry`), because entries are immutable and a count can only ride on a later entry. At most `MaxCoalescedKeys` keys
  are tracked: when the table is full, keys whose window has passed are removed, and when it is still full the event
  of a new key is not stored but counted in the overflow window. The first suppressed event of an overflow window
  (one minute long) stores one `audit.events-suppressed` entry through `TryRecordAsync` and reports the saturation
  once to the error hook (the host logs it as a warning); the entry carries the number of events suppressed in the
  previous overflow window (it is written at the start of a window so that the saturation is visible at once, and
  entries are immutable). Further suppressed events of the window are only counted. Keys that are already tracked keep
  coalescing normally. The rows written for these events are therefore bounded by one per key and minute, at most
  `MaxCoalescedKeys` keys per minute, plus one overflow entry per minute.

Known and accepted losses: the count of the last window (of a key, and of the overflow) is never written when it does
not occur again; the window and the counts are lost on restart; and a cancellation that arrives while the first event
of a window is being stored loses that event for the window, as a store failure does. A failure of the store is
handled as in `TryRecordAsync`. The check and
the update of the table happen under the lock; the store is called after the lock is released.

**ListAsync**: Reads one page of the trail, newest first.

- *Parameters*: an `AuditFilter` (see below; `AuditFilter.None` gives the whole trail), `long? beforeSequence` (the
  Older page), `long? afterSequence` (the Newer page), an optional page size, and a cancellation token. A filter with a
  project gives every event of that project, whether or not it names a review, and never an event without a project;
  events without a project are reached only by a filter without a project, which only the whole audit page uses. The
  caller must have checked the right to see the project first.
- *Returns*: An `AuditEntryPage` of at most the page size: `DefaultPageSize` (50) when the size is less than 1 or not
  given, and never more than `MaxPageSize` (200); the pages offer 25, 50 and 100.
- *Postconditions*: When both positions are given, `beforeSequence` wins. `HasOlder` and `HasNewer` come from one cheap
  look past each end of the page (one more entry is asked for), so a link is shown only when there is something
  behind it, and no count of the trail is ever made. The filter is validated first, so a store is never given an
  invalid one.
- *Errors*: `DomainRuleException` with `DomainRule.AuditFilterInvalid` for an invalid filter (see below);
  `ArgumentNullException` for a missing filter.

**AuditFilter** (record, with the static `AuditFilter.None`): `ProjectId`, `ReviewId`, `ActorUserId` (exact),
`ActorText` (a case-insensitive text in the stored actor name), `Action` (exact), `ActionPrefix` (every action that
starts with it, for example `review.`), and `From` and `To` (UTC days, both included). All set conditions must hold.
`Validated()` trims the texts, turns an empty text into `null`, refuses a text longer than the stored field it is
compared with, and refuses `From` after `To` (the invalid-filter rule); `FromUtc` is the start of the From day and
`ToExclusiveUtc` the start of the day after To (`null` for the last day that exists), so the end day is included up
to its last instant.

**AuditActions** (static): the vocabulary of the action codes the product records, `All` (sorted, without the
`selftest.` entries of the self test, which never reach an installation), the groups `Prefixes` (`account.`,
`attestation.`, `audit.`, `auth.`, `checklist.`, `project.`, `repository.`, `review.`, `signature.`, `slot.`,
`thread.`, `token.`, `user.`), and the lookups `InGroup`, `IsKnownAction` and `IsKnownPrefix`. The pages offer these as
the choices of the action filter and accept no other value from an address. The list is kept in step with the code by
a test that scans the product sources for the action codes passed to an audit draft.

**VerifyAsync**: Verifies the whole chain.

- *Returns*: An `AuditVerificationResult`.
- *Postconditions*: The service reads ordered batches of 500 entries through `ReadAfterAsync`, feeds them to the
  `AuditChainVerifier`, and stops at the first failure. The whole trail is never held in memory, so the memory use is
  bounded by the batch, and the time grows linearly with the number of entries. Entries appended while the
  verification runs are tolerated: it ends when a batch comes back empty.

**PrepareExportAsync**: Starts an export and decides the status of the chain before any byte is written.

- *Parameters*: an `AuditFilter`, an `AuditExportFormat` (`Csv` or `Json`), the exporting `Actor`, and a cancellation
  token.
- *Returns*: an `AuditExport`: the format, the validated filter, the actor, the `AuditVerificationResult` of the chain,
  the snapshot sequence, the generation time, and the derived content type, file extension, and file name
  (`audit-<project-N|all>-<yyyyMMddTHHmmssZ>-chain-<intact|broken>.<ext>`).
- *Postconditions*: The filter is validated first (`AuditFilterInvalid` for an invalid one). The newest sequence of the
  trail is read **first** (`IAuditStore.LatestSequenceAsync`, 0 for an empty trail) and the whole chain is verified
  **once** afterwards with `VerifyAsync`, so every entry that the export can contain lies inside the part of the chain
  that was verified. The caller therefore knows the status (intact, or broken with the first broken sequence) before it
  writes the first byte, and can put it in response headers. A broken chain is *exported*, with its status.
- *Status and rows*: the verification covers the whole chain, including entries appended after the snapshot, so the
  status can be `broken` with a first broken sequence above every exported row; this is conservative by intent.
- *Cancellation*: a cancellation during the verification ends the call before any byte is written and leaves no audit
  entry, because no data left the system.

**ExportAsync**: Streams the export to a stream and audits it.

- *Parameters*: the `AuditExport` from `PrepareExportAsync`, the target `Stream`, and a cancellation token.
- *Returns*: the number of entries written.
- *Postconditions*: The writer writes the start of the document and flushes it, then entries are read with
  `IAuditStore.ReadMatchingAsync(filter, afterSequence, snapshotSequence, 500)` in ascending sequence order, written,
  and flushed batch by batch (a keyset cursor on the sequence), until a batch is short or empty. No more than one batch
  is in memory, whatever the size of the trail. Entries appended during or after the export, among them its own audit
  entry, have a higher sequence than the snapshot and are not part of it. Cancellation is checked before every batch.
  The end of the document is written last. The row count counts only entries that were flushed to the target.
- *Audit*: In a `finally` block, after the stream has ended (and so outside the snapshot), the export records one
  `audit.exported` entry through `TryRecordAsync` with `CancellationToken.None`, so that a cancelled or failed export is
  recorded too and a failure to store the entry never hides the outcome (it goes to the error hook). The actor is the
  exporting user; the project is the project of the filter, or none for an export of the whole trail. The detail reads
  `<csv|json> export, <N> rows, <complete|incomplete (cancelled or failed)>; <filter summary>`; the filter summary
  comes from `AuditFilter.Describe()` (for example `from 2026-01-01, project 3, action group "review.", actor
  contains "ann"`, or `no filter`). The store cuts a detail at 500 characters, and a filter with every text at its
  maximum length is longer than that, so the order is chosen to lose only the least important end: the format, the
  count and the state come first, then the date range, the project, the review and the actions, and the actor text
  (up to 200 characters) last.
- *Errors*: an exception of the target or a cancellation is rethrown after the entry was recorded.

**Export semantics**: The rules of the export are these.

- *Filter*: the listing filter, all conditions together. The cursors and the page size are not used: the export
  returns everything that matches.
- *Order*: ascending sequence, which is the order of the chain.
- *Snapshot*: entries up to the newest sequence at the start; later entries are left out.
- *Scope*: a project filter never returns an entry without a project, so a project export holds no global row.
- *Chain status*: `intact`, or `broken` with the first broken sequence; verified once, before the first byte.
- *What the status says*: the chain is **global**, so a project export says only whether the whole chain is sound and
  where it first breaks; it never carries a count, a head hash, or anything of the entries of other projects.
- *A filtered export* is a *view* of a verified chain: every row keeps its `previousHash` and `hash`, so `audit verify`
  (the existing verb) on the database, or a script that recomputes the hashes, can check it again.

**AuditExportWriter**: the pure writer of the two formats (no HTTP, no store). It writes into a small buffer that the
service empties into the target after every batch (`FlushToAsync`).

- *CSV*: UTF-8 with a byte order mark; the header row
  `sequence,timestampUtc,actor,action,project,review,detail,previousHash,hash`, then one row per entry; CRLF line ends;
  RFC 4180 quoting (a cell with a comma, a quote, CR or LF is quoted and quotes are doubled); an empty result is the
  header row only.
- *JSON*: UTF-8 without a byte order mark, written with `Utf8JsonWriter`, one streamed object
  `{ "export": { "generatedUtc", "filter": {...} }, "chain": { "status", "firstBrokenSequence"? },
  "entries": [ ... ] }`, the entries with the same nine names as the CSV columns (`project` and `review` are `null`
  when absent); the `filter` object lists only the conditions that are set; an empty result is a valid document with
  an empty `entries` array.
- *Timestamps*: ISO 8601 `O` form in UTC in both formats.
- *Lifetime*: the writer owns a buffer and, for JSON, a `Utf8JsonWriter` with pooled memory, so it is `IDisposable`;
  `ExportAsync` creates one writer per export and disposes it when the export ends, however it ends.

**CSV injection rule**: a spreadsheet runs a cell that starts with a formula character. In CSV every *text* cell
(`actor`, `action`, `review`, `detail`) whose first character is `=`, `+`, `-`, `@`, a tab, or a carriage return is
prefixed with an apostrophe (`'`) before the quoting. The generated cells (`sequence`, `timestampUtc`, `project`,
`previousHash`, `hash`) are never changed, so a hash can always be compared. The prefix means that a text that
legitimately starts with one of these characters is shown with the apostrophe in the CSV; the JSON form is not changed,
because a spreadsheet does not interpret it, and is the form to use when the exact text matters.

#### Error Handling

`RecordAsync` lets store failures propagate. `TryRecordAsync` swallows everything except cancellation, as described above;
this is a deliberate choice, because a failing trail must not disable sign-in, and the failure is logged. This means a
sign-in event can be missing from the trail when the write fails. Null arguments are rejected with
`ArgumentNullException`.

#### Known Limitations

- Events that are recorded coalesced (attempts on a locked or disabled account, refusals by the throttle, failures for
  unknown user names) are written once per key and minute, so the trail shows that an attack happened and roughly how
  long it went on, not every attempt. The count of the last window can be lost (see `TryRecordCoalescedAsync`). The
  wrong password failures of existing accounts are recorded every time; they are bounded by the lockout.
- When an attacker fills the coalescing table with live keys, events of new keys are not recorded; the trail shows one
  `audit.events-suppressed` entry per minute and the host log shows a warning, but not which events were lost.
- A wrong *current* password typed on the account page appears as `auth.sign-in-failed` and its refusals on a locked
  account are not recorded: that path (`ChangeOwnPasswordAsync`) does not audit them. A wrong code typed there for a
  user with a second factor is recorded as `auth.totp-failed`. A wrong password typed on the signing
  dialog is recorded as `signature.reauth-failed`.
- The address of the client is not in the trail (see above).
- The trail has no retention or archiving function in this slice; the export is a copy to keep elsewhere, and the
  trail still only grows.
- Risk: every export verifies the whole chain before the first byte (see `PrepareExportAsync`), and no result is
  cached or shared between exports, so an export of a long trail takes time proportional to the length of the chain,
  also when the filter matches only a few entries, and several exports at once repeat that work. This is accepted for
  this slice; caching a recent result or serializing the verifications would be the next step if it becomes a problem.
- The chain is tamper-*evident*, not tamper-*proof*; see *What The Chain Proves And What It Does Not* in `AuditChain`.
  Removal of the newest entries, or a complete rewrite by someone with write access to the database, is not detected
  unless the head hash was recorded elsewhere, and this slice does not record it anywhere.
- The schema is created without migrations, so an existing development database has no audit table and must be deleted
  and created again (see the `Storage` design).

#### Dependencies

- **IAuditStore** (Core, implemented by `AuditStore` in Storage) — persists and reads entries.
- **AuditChain** — the verifier and the draft and entry types.

#### Callers

- **UserService** — records failed sign-ins, attempts on locked accounts, failed re-authentications, and lockouts
  through `TryRecordAsync` and `TryRecordCoalescedAsync` (when an `AuditService` was supplied).
- **SignIn** (Identity) — records sign-in and sign-out through `TryRecordAsync`, and refusals by the throttle through
  `TryRecordCoalescedAsync`.
- **HistoryPage**, **AuditPage**, and **ProjectAuditPage** (Web) — list entries with an `AuditFilter` (the project audit
  page lists the events of one project for the project administrators and the system administrators, through the
  audit-read access); the audit page also verifies the chain.
- **ProjectService** — records failed member adds through `TryRecordAsync` (when an `AuditService` was supplied).
- **AuditCommand** (Hosting) — verifies the chain for the `audit verify` verb.
- **AuditExportEndpoints** (Api) — prepare and stream an export for the two export routes.
- **Services that change data** (`ProjectService`, `ReviewService`, `ThreadService`, `ChecklistService`,
  `SigningService`, `UserService`) — do not call this service for their changes; they pass drafts to their stores
  (the member add that fails is the one event `ProjectService` records through this service).
