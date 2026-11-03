### NotificationStore

![Storage Structure](StorageView.svg)

#### Purpose

`NotificationStore` implements the Core interface `INotificationStore` on top of `ReviewCenterDbContext`. Its single
responsibility is to persist the e-mail address of a user for notifications, the pending confirmation of a new
address, and the log of the mails that were sent, and to make every decision that must have exactly one winner one
transaction or one conditional database statement. The audit entry of each change is stored in the same transaction.

#### Data Model

The unit is stateless apart from the injected context factory and the shared `AuditAppender`. Every operation creates
its own short-lived context from the factory and disposes it before returning. A change that is audited goes through
`AuditAppender.ExecuteAsync`, so it is serialized by the process-wide audit gate and stored in a `Serializable`
transaction together with its audit entry.

The tables are `NotificationPreferences` (one row per user, keyed by the user) and `NotificationSendLog` (one row per
mail that was sent, with the user, the recipient address, the kind, and the time as a fixed width UTC text, and with
indexes on the user and time, on the address and time, and on the time alone). Both are deleted with the user (see
`ReviewCenterDbContext`). Only the preference row holds the code, and only as a salted hash.

The decisions with one winner are conditional `ExecuteUpdate` statements whose affected-row count is the answer, so no
entity is loaded and written back and two concurrent requests can never both succeed. Their conditions use only
equality, null checks, and integer comparison, because the SQLite provider cannot compare `DateTimeOffset` values in a
query.

#### Key Methods

**IsAvailableAsync**: Tells whether both tables can be read.

- *Returns*: True when they can; false when the database was created before the tables existed and has to be
  recreated, so the server can switch the feature off instead of failing on each request.

**GetAsync**: Reads the preference of a user, or null. The read is untracked and returns a new object.

**ReserveConfirmationAsync**: Reserves a confirmation for an address.

- *Parameters*: The request (the user, the normalized address, the hash and salt of the code, the time, the expiry,
  the pause, and the three limits), the audit draft, and a cancellation token.
- *Returns*: The outcome (reserved, too soon, user limit, address limit, or server limit) and the identity of the new
  send log entry (zero when nothing was reserved).
- *Postconditions*: In one transaction the unit first deletes the log entries that are older than one day, because no
  limit looks back further and the log holds the target address in clear. Then it reads the preference row (the time the
  last code was mailed), the
  number of confirmations of the user and of the address in the last 24 hours, and the number of mails of the server in
  the last hour, and lets the request decide in this order: pause, user, address, server. Only when it is reserved does
  the unit insert the log entry, insert or update the preference row, and save the audit entry. The pending address,
  hash, salt, expiry, and send time are set and the count of attempts is reset to zero. A confirmed address in the row
  is kept. When the request is refused only the deletion of old log entries is kept. Of several parallel requests for
  one user exactly one is reserved and the others are refused as too soon.

**AbortConfirmationAsync**: Takes a reservation back after the mail could not be sent.

- *Parameters*: The identity of the user, the identity of the log entry, and the hash of the code.
- *Postconditions*: The log entry is deleted and the pending fields are cleared, but only for the row whose pending
  hash is the given one, so a late abort never destroys a newer reservation.

**TryCountAttemptAsync**: Counts one typed code against the current pending code.

- *Parameters*: The identity of the user, the hash of the pending code, and the limit.
- *Returns*: True when the update changed one row, false when the code is not the current one or the limit is reached.
- *Postconditions*: One conditional update (`PendingCodeHash = @hash AND PendingAttempts < @max`) adds one. Of several
  parallel callers exactly the limit succeed.

**ConfirmAsync**: Turns the pending address into the confirmed one.

- *Parameters*: The identity of the user, the hash of the pending code, the time, the audit draft, and a cancellation
  token.
- *Returns*: True for exactly one caller; false when the hash does not match or nothing is pending (nothing is
  changed or audited).
- *Postconditions*: One conditional update sets the confirmed address and clears the pending address, hash, salt,
  expiry, send time, and the count of attempts; the audit entry is stored in the same transaction.

**RemoveAsync**: Removes the preference row of a user.

- *Returns*: True when a row was removed; false, with nothing audited, when the user had none.
- *Postconditions*: The row and the audit entry are handled in one transaction. The send log entries are kept until
  the next confirmation request deletes them after one day, so the limits still count the mails that were sent and the
  removed address stays in the log for at most about one day. Deleting the account removes its log rows by cascade.

#### Error Handling

Any database failure propagates unchanged to the caller, except in `IsAvailableAsync`, where a failure of the
database that points to a missing table is the answer false. Null arguments raise `ArgumentNullException`. The unit
never sees a code: it stores the hash and the salt it is given, and neither reaches a log or an audit entry.

#### Dependencies

- **ReviewCenterDbContext** — the schema and the context factory.
- **AuditAppender** — runs every audited change together with its audit entry.
- **Core domain types and INotificationStore** — the `NotificationPreference` and `NotificationSendLogEntry` types
  stored and the interface implemented.
- **Entity Framework Core** — queries and the single-statement updates and deletes.

#### Callers

- **NotificationPreferenceService** — through the Core interface; it is the only unit that changes the tables.
- **ServerHost** — registers the store as the implementation of the Core interface.
