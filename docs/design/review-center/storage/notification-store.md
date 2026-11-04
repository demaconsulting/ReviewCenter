### NotificationStore

![Storage Structure](StorageView.svg)

#### Purpose

`NotificationStore` implements the Core interface `INotificationStore` on top of `ReviewCenterDbContext`. Its single
responsibility is to persist the e-mail address of a user for notifications, the pending confirmation of a new
address, the notification mode of the user, the state of the dispatcher, the items the user was told about, and the log
of the mails that were sent, and to make every decision that must have exactly one winner one transaction or one
conditional database statement. The audit entry of each audited change is stored in the same transaction.

#### Data Model

The unit is stateless apart from the injected context factory and the shared `AuditAppender`. Every operation creates
its own short-lived context from the factory and disposes it before returning. A change that is audited goes through
`AuditAppender.ExecuteAsync`, so it is serialized by the process-wide audit gate and stored in a `Serializable`
transaction together with its audit entry. The commit of a notification pass (`ApplyAsync`) goes through the same
gate and transaction but writes no audit entry.

The tables are `NotificationPreferences` (one row per user, keyed by the user, with the mode and the dispatcher state),
`NotifiedItems` (one row per user, project, review, and inbox section that the user was told about, with the state
token and the time as a fixed width UTC text), and `NotificationSendLog` (one row per mail that was sent, with the
user, the recipient address, the kind, and the time as a fixed width UTC text, and with indexes on the user and time,
on the address and time, and on the time alone). All are deleted with the user (see `ReviewCenterDbContext`). Only the
preference row holds the code, and only as a salted hash. The timestamps of the dispatcher state
(`BatchStartedUtc`, `DigestHandledUtc`, `NextAttemptUtc`) are stored the same way, as UTC text of the form
`yyyy-MM-ddTHH:mm:ss.fffffffZ`, so that an instant keeps its meaning and the database can compare them.

The decisions with one winner are conditional `ExecuteUpdate` statements whose affected-row count is the answer, so no
entity is loaded and written back and two concurrent requests can never both succeed. Their conditions use only
equality, null checks, and integer comparison, except the expiry of a code in `ConfirmAsync` and the send times of the
limits, which compare fixed width UTC text (stored that way through a value converter of `ReviewCenterDbContext`),
because the SQLite provider cannot compare `DateTimeOffset` values in a query.

#### Key Methods

**IsAvailableAsync**: Tells whether the tables and columns of notifications can be read.

- *Returns*: True when they can; false when the database was created before they existed and has to be recreated, so
  the server can switch the feature off (or refuse to start) instead of failing on each request. The check reads the
  mode and dispatcher columns of `NotificationPreferences`, the send log, and `NotifiedItems`, so a database of the
  first version of the feature, which lacks the new table and columns, is reported as old. Only the failure of a missing
  table or column (SQLite error 1, "no such table" or "no such column") gives false; every other database failure, such
  as a locked, read-only, or corrupt file, propagates, so a real fault is never mistaken for an old database.

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

- *Parameters*: The identity of the user, the hash of the pending code, the current time, the audit draft, and a
  cancellation token.
- *Returns*: True for exactly one caller; false when the hash does not match, nothing is pending, or the code has
  expired at the given time, that is, its expiry is not later than the time (nothing is changed or audited).
- *Postconditions*: One conditional update (`PendingCodeHash = @hash AND PendingEmail IS NOT NULL AND
  PendingCodeExpiresUtc > @now`) sets the confirmed address and clears the pending address, hash, salt, expiry, send
  time, and the count of attempts; the audit entry is stored in the same transaction. The expiry is checked in the
  same statement as the change, so a request that was delayed after the service read the row can not confirm a code
  that expired in the meantime.

**RemoveAsync**: Removes the preference row of a user.

- *Returns*: True when a row was removed; false, with nothing audited, when the user had none.
- *Postconditions*: The row, the notified items of the user, and the audit entry are handled in one transaction, so the
  mode and the dispatcher state go with the row. The send log entries are kept until
  the next confirmation request deletes them after one day, so the limits still count the mails that were sent and the
  removed address stays in the log for at most about one day. Deleting the account removes its log rows by cascade.

**SetModeAsync**: Sets the notification mode of a user.

- *Parameters*: The identity of the user, the mode, the current time, the audit draft, and a cancellation token.
- *Returns*: True when the mode changed. False, with nothing changed and nothing audited, when the user has no row,
  when a mode other than `None` is asked for a row without a confirmed address, or when the row already has the mode.
- *Postconditions*: In one transaction the mode is set, the batch window and the failure state are cleared, and the
  audit entry is stored. Switching on from `None` sets the baseline flag (`BaselinePending`). Switching to `None`
  deletes the notified items of the user and the time of the last digest. Switching between `Daily` and `Every` keeps
  the notified items and sets no flag. If the audit entry cannot be stored, the mode stays as it was.

**ListRecipientsAsync**: Lists the users whom the dispatcher may mail.

- *Returns*: The users with a mode other than `None` and a confirmed address, ordered by user identity. Each entry has
  the user name, the display name, the address, the mode, the dispatcher state, and whether the user is disabled or
  must still change a temporary password. The state timestamps are read back as the same instants. A read is untracked.

**GetNotifiedAsync**: Gets the items a user was told about, ordered by identity. Only the items of that user are read.

**ApplyAsync**: Records the result of a dispatcher pass for one user.

- *Parameters*: The identity of the user, a `NotificationCommit` (the time, the items to insert or update, the keys to
  forget, an optional replace-all set, the new dispatcher state, and an optional send log record), and a cancellation
  token.
- *Returns*: True when applied; false, with nothing recorded, when the row is gone or its mode is `None` again, which
  can happen when the user changed the mode while the pass ran.
- *Postconditions*: In one transaction the unit deletes the forgotten items, updates the state of known items and
  inserts new ones (one row per user, project, review, and section), or replaces the whole set for a baseline; stores
  the dispatcher state; inserts the send log row, if any; and deletes the log rows that are older than one day. No audit
  entry is written. If any part fails, nothing of the commit remains.

**CountSentAsync**: Counts the rows of the send log since an instant, of all kinds, for one user or, with no user, for
all users. The daily limit of a user and the hourly limit of the server are counted from it.

#### Error Handling

Any database failure propagates unchanged to the caller, except in `IsAvailableAsync`, where only the failure of a
missing table (SQLite error 1, "no such table") is the answer false. Null arguments raise `ArgumentNullException`.
The unit never sees a code: it stores the hash and the salt it is given, and neither reaches a log or an audit entry.

#### Dependencies

- **ReviewCenterDbContext** — the schema and the context factory.
- **AuditAppender** — runs every audited change together with its audit entry.
- **Core domain types and INotificationStore** — the `NotificationPreference`, `NotifiedItem`,
  `NotificationRecipient`, `NotificationCommit`, and `NotificationSendLogEntry` types stored and the interface
  implemented.
- **Entity Framework Core** — queries and the single-statement updates and deletes.

#### Callers

- **NotificationPreferenceService** — through the Core interface, for the address, the confirmation, and the mode.
- **NotificationDispatcher** — through the Core interface, to list the recipients, read the notified items, commit the
  result of a pass, and count the sent mails.
- **ServerHost** — registers the store as the implementation of the Core interface.
