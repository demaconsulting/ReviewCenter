### NotificationPreferenceService

![Core Structure](CoreView.svg)

#### Purpose

`NotificationPreferenceService` lets a signed-in person name an e-mail address for notifications and prove that the
address is theirs. The proof is a six digit code that the service mails to the address and that the person types back
on the Account page. Once the address is confirmed, the person chooses a notification mode: `None` (no mail, the
default), `Daily` (one summary a day), or `Every` (a mail a few minutes after changes). The service sends no other mail
than the code; the mails about news are sent by `NotificationDispatcher`. It owns every rule around this confirmation
and the mode: who may use it, how a code is made and kept, how long it is valid, how often it can be requested and
tried, how many mails the server may send, and which changes are audited. It also tells other units which address a
notification may be mailed to, which is the confirmed address and never a pending one.

#### Data Model

The service keeps no state of its own besides its settings. The state of a person is a `NotificationPreference` row,
held by `INotificationStore` (table `NotificationPreferences`, one row per user):

- `Email` — the confirmed address, or null;
- `PendingEmail` — the address that waits for its code, or null;
- `PendingCodeHash` and `PendingCodeSalt` — the salted hash of the mailed code and its random salt (a lower case hex
  text of 16 random bytes);
- `PendingCodeExpiresUtc` — the end of the lifetime of the code;
- `PendingAttempts` — the number of codes typed against the current pending code;
- `PendingSentUtc` — the time the current code was mailed, used for the resend pause;
- `Mode` — the `NotificationMode` the person chose (`None`, `Daily`, or `Every`; the default and the value of a row
  without a choice is `None`);
- `BaselinePending`, `BatchStartedUtc`, `DigestHandledUtc`, `FailureCount`, and `NextAttemptUtc` — the state of
  `NotificationDispatcher` for the person; the service never reads or writes them, only the store clears them when the
  mode changes;
- `Updated` — the time of the last change.

Each mail that is sent is also logged as a `NotificationSendLogEntry` (table `NotificationSendLog`: the user, the
recipient, the kind, which is `confirmation`, `update`, or `digest`, and the time). The limits are counted from that
log.

`NotificationSettings` holds the three settings: whether notifications are enabled, the instance name for the mails
(default `ReviewCenter`), and the number of mails the whole server may send per hour (default 200). A status is
reported as a `NotificationAddressStatus`: the confirmed address, the pending address, whether the pending code has
expired, and the mode.

Constants:

- code lifetime: 30 minutes (`CodeLifetime`);
- resend pause: 60 seconds (`ResendCooldown`);
- wrong codes per pending code: 5 (`MaxAttempts`);
- confirmation mails per user per day: 3 (`MaxConfirmationsPerUserPerDay`);
- confirmation mails per address per day: 3 (`MaxConfirmationsPerAddressPerDay`);
- confirmation mails per server per hour: the setting, default 200.

The code is one of the numbers 000000 to 999999 from a cryptographic random number generator. It is never stored. The
stored hash is the lower case hex text of SHA256 over a fixed label (`ReviewCenter.NotificationCode.v1`), the salt, the
identity of the user, and the code, so a hash is useless for another user or another salt.

#### Key Methods

**IsAvailable**: True when notifications are enabled in the settings and the sender is configured.

**GetStatusAsync**: Gets the status of a person.

- *Parameters*: The actor and a cancellation token.
- *Returns*: The confirmed address, the pending address, and whether the pending code has expired; all empty when the
  person never entered an address.

**RequestConfirmationAsync**: Starts the confirmation of an address.

- *Parameters*: The actor, the typed address (it may be null), and a cancellation token.
- *Postconditions*: The address is normalized with `NotificationAddress`. A new code and salt are made and the store
  reserves the confirmation in one step, with the audit entry `notification.settings-changed`: the pause, the limits
  per user, per address, and per server are checked there. After a successful reservation one mail with the code is
  sent. A confirmed address stays in use until the new one is confirmed. When the sender fails, the reservation is
  taken back (the pending address and the log entry are removed, using a cancellation token that is never canceled), so
  the failed mail costs nothing, and the person gets `NotificationSendFailed`. A canceled send or an unexpected
  failure of the sender also takes the reservation back, and the exception is passed on unchanged. A cancel after the
  mail was delivered still frees the counters; the user then asks for a new code, and only the hourly limit of the
  server bounds that case.

**ConfirmAsync**: Confirms the pending address with the typed code.

- *Parameters*: The actor, the typed code (it may be null; white space is ignored and at most 32 characters are
  used), and a cancellation token.
- *Postconditions*: Without a pending address the call fails. A code that is older than its lifetime fails as
  expired. Each attempt is counted in the store before the code is judged, and the sixth attempt fails even for the
  right code. The typed code is hashed with the stored salt and compared in fixed time. A wrong code fails and leaves
  the address pending. The right code makes the pending address the confirmed one, clears the pending fields, and
  records the audit entry `notification.email-confirmed`, in one transaction; of two requests with the same code
  exactly one succeeds.

**RemoveAddressAsync**: Removes the confirmed and the pending address of the person.

- *Returns*: True when there was something to remove. The audit entry `notification.email-removed` is recorded in the
  same transaction. The send log is kept. The mode, the dispatcher state, and the notified items of the person go with
  the row, so the person is back to the mode `None`.

**SetModeAsync**: Chooses the notification mode of a person.

- *Parameters*: The actor, the `NotificationMode`, and a cancellation token.
- *Returns*: True when the mode changed; false when it was already the chosen one (or `None` for a person without a
  row), in which case nothing is stored and no audit entry is written.
- *Postconditions*: The actor is checked like for every action. A value that is not `None`, `Daily`, or `Every` raises
  `ArgumentOutOfRangeException`. A mode other than `None` needs a confirmed address and fails with
  `NotificationAddressNotConfirmed` otherwise. The store saves the new mode with the audit entry
  `notification.settings-changed` (text "Notification mode set to X.", no address) in one transaction. Switching on
  from `None` marks the baseline as pending, so the dispatcher records the items that exist now as known before it
  mails anything; switching between `Daily` and `Every` keeps the known items; switching to `None` forgets them. Every
  change clears the batch window and the failure state.

**GetConfirmedAddressAsync**: Gets the address a notification may be mailed to.

- *Parameters*: The identity of the recipient and a cancellation token.
- *Returns*: The confirmed address; null when there is none, when only an address is pending, or when notifications are
  not available. This method does not need an actor.

The confirmation mail has the subject `<name>: your confirmation code`, a plain text part and an HTML part. Both give
the code, say that it is valid for 30 minutes, say that the mail can be ignored when it was not asked for, and hold no
link. The instance name comes from the settings, with control characters removed (the default name is used when
nothing is left), and it is HTML-encoded in the HTML part. The code is shown in this mail only: never on a page, in a
log, or in an audit entry.

Every action first checks the person. An actor that signed in with an API token is refused. A feature that is off or
a sender that is not configured is refused before the store is touched. A user that is unknown or disabled is
refused as not allowed, and so is a user that must still change a temporary password.

#### Error Handling

All refusals are `DomainRuleException` with a rule and a message for the person:

- `NotificationsNotAvailable` — an API token actor, the feature is off, no sender is configured, or the user must
  change a temporary password;
- `ActorNotAllowed` — the user is unknown or disabled;
- `NotificationAddressInvalid` — the typed text is not one valid address;
- `NotificationAddressNotConfirmed` — a mode other than `None` was chosen while the person has no confirmed address;
- `NotificationResendTooSoon` — the previous mail was sent less than 60 seconds ago;
- `NotificationLimitReached` — a limit per user, per address, or per server was reached;
- `NotificationSendFailed` — the sender failed;
- `NotificationNothingToConfirm` — no address is pending;
- `NotificationCodeExpired` — the code is older than 30 minutes;
- `NotificationTooManyAttempts` — five codes were already typed against the current code;
- `NotificationCodeIncorrect` — the typed code is wrong.

No message contains the address or the code, and the audit entries carry neither. A null actor raises
`ArgumentNullException`. Other failures of the store propagate unchanged.

#### Dependencies

- **INotificationStore** — the state, the limits, and the audited changes (implemented by `NotificationStore`).
- **INotificationSender** — delivers the mail.
- **UserService** — loads the user to check that the account may use the feature.
- **NotificationSettings** — the switches and the limit of the server.
- **NotificationAddress and NotificationMessage** — check the address and build the mail.
- **AuditService types** — the audit draft and the action names.
- **TimeProvider** — the clock, replaceable in tests.

#### Callers

- **Account page** — shows the status, requests a confirmation, confirms a code, removes the address, and sets the mode.
- **ServerHost** — registers the service with its settings.
