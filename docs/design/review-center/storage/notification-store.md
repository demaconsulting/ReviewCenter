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
`NotifiedItems` (one row per user, project, review, inbox section, and kind that the user was told about, with the
state token, the flag `GaveUp` that marks a state the server gave up mailing, and the time as a fixed width UTC text),
and `NotificationSendLog` (one row per mail that was sent or is
on its way, with the
user, the recipient address, the kind, and the time as a fixed width UTC text, and with indexes on the user and time,
on the address and time, and on the time alone). All are deleted with the user, and `NotifiedItems` also with the
review (see `ReviewCenterDbContext`). Only the
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
  mode and dispatcher columns of `NotificationPreferences` (including the generation), the send log, and
  `NotifiedItems` (including its kind and its `GaveUp` flag), so a database of
  an earlier version of the feature, which lacks the new table and columns, is reported as old. Only the failure of a missing
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
  PendingCodeExpiresUtc > @now`) sets the confirmed address, raises `Generation` by one, and clears the pending
  address, hash, salt, expiry, send
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
- *Postconditions*: In one transaction the mode is set, `Generation` is raised by one, the batch window and the failure
  state are cleared, and the
  audit entry is stored. Switching on from `None` sets the baseline flag (`BaselinePending`). Switching to `None`
  deletes the notified items of the user and the time of the last digest, and clears the baseline flag. Switching
  between `Daily` and `Every` keeps the notified items and keeps the baseline flag as it is: a baseline that is still
  pending stays pending, so the first pass still records the backlog and does not mail it, and a baseline that was
  recorded is not taken again. If the audit entry cannot be stored, the mode stays as it was.

**ListRecipientsAsync**: Lists the users whom the dispatcher may mail.

- *Returns*: The users with a mode other than `None` and a confirmed address, ordered by user identity. Each entry has
  the user name, the display name, the address, the mode, the generation, the dispatcher state, and whether the user is
  disabled or
  must still change a temporary password. The state timestamps are read back as the same instants. A read is untracked.

**GetNotifiedAsync**: Gets the items a user was told about, ordered by identity. Only the items of that user are read.

**ApplyAsync**: Records the result of a dispatcher pass for one user.

- *Parameters*: The identity of the user, a `NotificationCommit` (the time, the address and the generation that the mail
  was prepared for, the items to insert or update with their `GaveUp` flag, the keys to forget, an optional replace-all
  set, and the new
  dispatcher state), and a cancellation token.
- *Returns*: True when applied; false, with nothing recorded, when the row is gone, its mode is `None` again, its
  confirmed address is not the address of the commit, or its generation is not the generation of the commit, which can
  happen when the user changed the mode or the address while the pass ran.
- *Postconditions*: In one transaction the unit first runs a conditional update on the preference row that requires
  the mode not to be `None` and the address and the generation to be those of the commit; when it changes no row,
  nothing is written. Then it deletes the forgotten items, updates the state and the `GaveUp` flag of known items and
  inserts new ones (one row per user, project, review, section, and kind), or replaces the whole set for a baseline;
  stores the dispatcher state; and deletes the log rows that are older than one day. The commit writes no send log
  row, because the row of a mail is reserved by `ReserveSendAsync` before the send.
  No audit
  entry is written. If any part fails, nothing of the commit remains.

**ReserveSendAsync**: Reserves the place of one notification mail under the limits, and writes its send log row.

- *Parameters*: The identity of the user, the kind of the mail (`update` or `digest`), the target address, the current
  time, the limit of the server for one hour, the limit of the user for one day, and a cancellation token.
- *Returns*: The outcome (reserved, server limit reached, or user limit reached) and the identity of the new send log
  row (zero when nothing was reserved).
- *Postconditions*: In one serialized transaction, like `ReserveConfirmationAsync`, the unit first deletes the log rows
  that are older than one day. Then it counts the rows of all kinds and all users in the last hour (the confirmation
  mails included), counts the rows of all kinds of the user in the last day, and lets `SendLimits.Decide` choose, the
  server limit first. Only when it is reserved does the unit insert the row. Because the count and the insert are one
  transaction, of several parallel senders, in this process or another one that shares the database, no more are
  reserved than the limits leave places for. A refused reservation keeps only the deletion of old rows.

**ReleaseSendAsync**: Takes the reservation of a mail back after the mail was not sent.

- *Parameters*: The identity of the user, the identity of the log row, and a cancellation token.
- *Postconditions*: The row is deleted, but only when it belongs to the user. A row that is gone, or that belongs to
  another user, is left alone and no error is raised.

**CountSentAsync**: Counts the rows of the send log since an instant, of all kinds, for one user or, with no user, for
all users. The reservation counts the same rows; this method reads them for callers and tests.

#### Error Handling

Any database failure propagates unchanged to the caller, except in `IsAvailableAsync`, where only the failure of a
missing table (SQLite error 1, "no such table") is the answer false. Null arguments raise `ArgumentNullException`.
The unit never sees a code: it stores the hash and the salt it is given, and neither reaches a log or an audit entry.

#### Dependencies

- **ReviewCenterDbContext** — the schema and the context factory.
- **AuditAppender** — runs every audited change together with its audit entry.
- **Core domain types and INotificationStore** — the `NotificationPreference`, `NotifiedItem`,
  `NotificationRecipient`, `NotificationCommit`, `NotificationSendLogEntry`, and `SendLimits` types stored or used and
  the interface implemented.
- **Entity Framework Core** — queries and the single-statement updates and deletes.

#### Callers

- **NotificationPreferenceService** — through the Core interface, for the address, the confirmation, and the mode.
- **NotificationDispatcher** — through the Core interface, to list the recipients, read the notified items, reserve
  and release the send of a mail, and commit the result of a pass.
- **ServerHost** — registers the store as the implementation of the Core interface.
