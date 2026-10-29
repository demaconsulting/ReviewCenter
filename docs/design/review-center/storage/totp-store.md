### TotpStore

![Storage Structure](StorageView.svg)

#### Purpose

`TotpStore` implements the Core interface `ITotpStore` on top of `ReviewCenterDbContext`. Its single responsibility is
to persist the second factor enrollments of local users (the encrypted secret and the replay state) and their recovery
code hashes, and to make every decision that must have exactly one winner a single conditional database statement. The
audit entry of each change is stored in the same transaction.

#### Data Model

The unit is stateless apart from the injected context factory and the shared `AuditAppender`. Every read creates its own
short-lived context from the factory and disposes it before returning. A change that is audited goes through
`AuditAppender.ExecuteAsync`, so it is serialized by the process-wide audit gate and stored in a `Serializable`
transaction together with its audit entry. The tables are `UserTotps` (one row per user, keyed by the user) and
`TotpRecoveryCodes` (unique index on the user and the code hash); both are deleted with the user (see
`ReviewCenterDbContext`).

The decisions with one winner are `ExecuteUpdate` statements whose affected-row count is the answer, so no entity is
loaded and written back and two concurrent requests can never both succeed. Their conditions use only equality, null
checks, and integer comparison, because the SQLite provider cannot compare `DateTimeOffset` values in a query.

#### Key Methods

**GetAsync**: Reads the enrollment of a user, pending or confirmed, or null. The read is untracked and returns a new
object.

**ListActiveUserIdsAsync**: Reads the identities of the users with a confirmed enrollment, for the badge of the user
page.

**SavePendingAsync**: Stores a pending enrollment (no confirmation time, last accepted step 0).

- *Parameters*: The pending enrollment and a cancellation token.
- *Returns*: False when the user already has a confirmed enrollment (nothing is changed); otherwise true.
- *Postconditions*: The whole operation (delete the earlier pending row, check for a confirmed one, insert) runs in
  one `AuditAppender.ExecuteAsync` transaction without an audit entry, so concurrent starts for one user are
  serialized: the later one replaces the earlier pending enrollment, none of them fails, and exactly one pending row
  is left. An earlier pending enrollment of the user is replaced; a confirmed one is never touched. A failure of the
  database propagates unchanged.

**ConfirmAsync**: Confirms a pending enrollment and stores its recovery codes.

- *Parameters*: The identity of the user, the time step of the code that proved possession, the time, the hashes of the
  new recovery codes, the audit draft, and a cancellation token.
- *Returns*: True for exactly one caller; false when the enrollment is not pending (nothing is changed or audited).
- *Postconditions*: One conditional update sets the confirmation time and the last accepted step of a row that is still
  pending; the recovery code hashes and the audit entry are stored in the same transaction.

**TryAdvanceStepAsync**: Records the time step of an accepted code (the replay protection).

- *Parameters*: The identity of the user, the time step of the code, and a cancellation token.
- *Returns*: True when the update changed one row, false otherwise.
- *Postconditions*: One conditional update (`WHERE UserId = @id AND ConfirmedAt IS NOT NULL AND LastAcceptedStep <
  @step`) sets the last accepted step, a compare-and-set. Of several concurrent callers with the same step exactly one
  wins, a code can never be used twice, and an older code never works after a newer one was accepted. A user without a
  confirmed enrollment always gets false. It is not audited and does not use the audit gate (the sign-in records its
  own entry).

**TryConsumeRecoveryCodeAsync**: Spends a recovery code.

- *Parameters*: The identity of the user, the hash of the typed code, the time, the audit draft, and a cancellation
  token.
- *Returns*: True for exactly one caller; false when the code is unknown or already used (nothing is audited).
- *Postconditions*: One conditional update (`UsedAt IS NULL`) sets the use time; the audit entry is stored in the same
  transaction.

**ReplaceRecoveryCodesAsync**: Replaces all recovery codes of a confirmed enrollment.

- *Returns*: False when the user has no confirmed enrollment (nothing is changed); otherwise true.
- *Postconditions*: The old codes (used or not) are deleted, the new hashes are stored, and the audit entry is saved,
  all in one transaction.

**CountUnusedRecoveryCodesAsync**: Counts the codes of a user that are not used yet.

**RemoveAsync**: Removes the enrollment of a user with its recovery codes.

- *Returns*: True when a row was removed; false, with nothing audited, when the user had none.
- *Postconditions*: The codes, the row, and the audit entry are handled in one transaction.

**RemovePendingAsync**: Removes a pending enrollment (the user gave up) and returns true when there was one. A
confirmed enrollment is never touched.

#### Error Handling

Any database failure propagates unchanged to the caller. Null arguments raise `ArgumentNullException`. The unit never
sees a secret in plain text, a code, or a token: it stores the encrypted text and the hashes it is given, and neither
reaches a log or an audit entry.

#### Dependencies

- **ReviewCenterDbContext** — the schema and the context factory.
- **AuditAppender** — runs every audited change together with its audit entry.
- **Core domain types and ITotpStore** — the `UserTotp` and `TotpRecoveryCode` types stored and the interface
  implemented.
- **Entity Framework Core** — queries and the single-statement updates and deletes.

#### Callers

- **TotpService** — through the Core interface; it is the only unit that changes the tables.
- **UserService** — reads the enrollment through the Core interface to tell whether a second factor is required.
- **ServerHost** — registers the store as the implementation of the Core interface.
