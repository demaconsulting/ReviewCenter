### NotificationDispatcher

![Core Structure](CoreView.svg)

#### Purpose

`NotificationDispatcher` decides, for every user who chose a notification mode, which items of the inbox are new, and
mails them. One call of `RunPassAsync` is one pass over all recipients. The unit owns the rules around the mails: who
is a recipient, what the recipient may be told, the baseline when a mode is switched on, the batch window of the mode
Every, the slot of the mode Daily, the record of what was told, the retries of a failed send, the limits on the number
of mails, and the cost of a pass on an idle server. It makes the content of a mail with `NotificationComposer`, and it
sends it through `INotificationSender`. It is called by the background worker of the `Messaging` subsystem.

#### Data Model

The dispatcher keeps in memory only what makes a pass cheap: the audit sequence that it saw at the end of the last
clean pass, and the time of the last full sweep. A pass that failed for a user resets both, so that the next pass
reads every inbox again and the failed user does not wait for the next sweep. Everything that must survive a restart is
kept by `INotificationStore` (table `NotificationPreferences` and its related tables), one record for each user:

- the mode (`None`, `Every`, or `Daily`) and the confirmed address;
- `Generation` — a counter that the store raises by one with every change of the mode and every confirmed address; the
  dispatcher carries it into its commit, and a commit whose generation is not the current one is discarded;
- `BaselinePending` — the mode was switched on and the backlog is not recorded yet;
- `DigestHandledUtc` — the instant at which the last daily slot was handled;
- `BatchStartedUtc` — the start of the running batch window of the mode Every, or null;
- `FailureCount` and `NextAttemptUtc` — the state of the retry of a failed send;
- the notified items: for each key (project id, review id, section, kind) the state that the user was told about. The
  kind is the part of the state before the first colon (`NotificationComposer.KindOf`). The inbox lists a standing item
  to answer and a "Question for you" item in the same section of one review, and the kind keeps them apart, so that a
  question that is asked after the item to answer was mailed is mailed too.

Each mail that is sent is a `NotificationSendLogEntry` of the kind `update` (the mode Every) or `digest` (the mode
Daily). The same log holds the confirmation mails of `NotificationPreferenceService`, with the kind `confirmation`.

`NotificationSettings` supplies the limit of mails for the whole server per hour (`MaxMailsPerHour`). The length of the
batch window is a constructor argument. The result of a pass is a `NotificationPassResult`: the numbers of mails sent,
failed, and deferred. It holds no address.

Constants:

- mails per user per day: 24 (`MaxMailsPerUserPerDay`);
- waits after a failed send: 5 minutes, 15 minutes, 1 hour, 4 hours, and 12 hours (`RetryDelays`); the sixth failure
  gives up;
- interval of the full sweep: 15 minutes (`SweepInterval`);
- the batch window of the mode Every: the constructor argument, made from the setting `BatchWindowSeconds` (default
  300 seconds).

#### Key Methods

**RunPassAsync**: Runs one pass over all recipients.

- *Parameters*: A cancellation token.
- *Returns*: The numbers of mails sent, failed, and deferred in the pass.
- *Postconditions*: Only one pass runs at a time; a second call waits for the first. The recipients are the users whose
  mode is not None and who have a confirmed address. Without a recipient the pass returns at once and does not read
  the audit trail. Otherwise the pass reads the latest audit sequence and decides whether this pass is a sweep, which
  it is when it is the first pass, when 15 minutes have passed since the last sweep, or when the sequence changed. A
  pass that is not a sweep still serves the users whose batch window has elapsed, whose daily slot is due, whose next
  attempt is due, or whose baseline is pending; it reads the inbox only for them. The sequence and the time of the sweep
  are remembered only when every recipient was handled without an error; a pass that failed for a user resets both, so
  the next pass reads every inbox again, also when the sequence is unchanged and no sweep is due. The reset does not
  shorten a retry wait: `NextAttemptUtc` of a user whose send failed still ends the work for that user (step 4). While
  an inbox read keeps failing, every pass therefore reads every inbox again; this is read-only, and the sends stay
  limited by the backoff and the caps. For each recipient the pass does the following steps in order.
  1. A disabled user and a user who must still change a temporary password are skipped.
  2. The inbox is read as the recipient: `InboxService.GetInboxAsync` is called with an actor that has the user name and
     the display name of the recipient and no API token. Because the inbox applies its own masking for that actor, a
     mail can not tell more than the recipient sees after the sign-in; in particular the answers of the others stay
     hidden while the own ballot is not handed in. The rows are reduced with `NotificationComposer.Classify`, and the
     candidates are made unique by their key. The read writes no audit entry.
  3. A pending baseline records the current candidates as notified with `ReplaceAll`, sets `DigestHandledUtc` to now,
     clears the flag, sends no mail, and writes no log row. The backlog is therefore never mailed, and a user who
     switches the daily summary on gets the first summary on the next local date.
  4. A next attempt in the future ends the work for the user.
  5. In the mode Every the first new candidate, seen in a sweep, starts the batch window and is stored. While the
     window runs nothing is sent. When it has elapsed the inbox is read again; when nothing is new any more, the
     window is cleared; otherwise one mail of the kind `update` is sent.
  6. In the mode Daily the slot is due according to `DigestSchedule.IsDue`. At the due slot a mail of the kind `digest`
     is sent when something is new, and the slot is used up whether or not a mail was sent. The outstanding
     candidates that the user was told about before are counted and not listed.
  7. A mail is sent by one common step. The limits are checked first: the number of log rows of any kind in the last
     hour against the setting for the server, and the number of rows of the user in the last day against 24. A mail
     that exceeds a limit is deferred: no state changes, the items stay new, and they go out when the limit allows it.
     Otherwise the mail is composed. Right before the send the dispatcher reads the preference of the user again and
     goes on only when the mode is still not None and the generation and the confirmed address are those that the batch
     was prepared for; otherwise the batch is discarded, nothing is sent, nothing is recorded, and the next pass starts
     again from the current preference. The mail is then sent. When the sender fails, `FailureCount` is raised and
     `NextAttemptUtc` is set according to the waits above; the sixth failure gives up, which records the items as
     notified, clears the
     failure state, and passes the error on to the callback once. When the sender succeeds, one call of
     `INotificationStore.ApplyAsync` records the new items, removes the keys of items that left the inbox, writes the
     log row, clears the batch window and the failure state, and, in the mode Daily, sets `DigestHandledUtc`. The
     commit carries the address and the generation of the recipient, and the store applies it only when both are still
     current, in the same conditional update; a commit that is stale because the user changed the address, or switched
     the mode off and on again, during the send is discarded and records nothing. One transaction means that a crash
     can repeat a mail but never lose the record of one. A change of the preference between the re-read and the
     send itself can not be excluded, because the send is outside of the database; in that case the mail has gone out
     and the commit is discarded, so that the new preference starts from its own state.

An item is new when `NotificationComposer.IsNew` says so for the stored state. A key that is not in the inbox any more
is forgotten, so an item that returns is mailed again.

- *Errors*: An error in the work for one recipient is passed to the error callback, counted as a failure of that user,
  and does not stop the pass. A canceled token ends the pass with an `OperationCanceledException`.

#### Error Handling

The dispatcher raises `ArgumentNullException` for a missing dependency and `ArgumentOutOfRangeException` for a batch
window that is not positive, in the constructor. During a pass it never
throws except for a cancellation: every other failure of a user (the store, the inbox, the composer, or the sender) is
caught, handed to the error callback, and leaves the state of that user as it was, or advances the retry state when
the failure was a failed send. The callback is the only place that sees the exception; the host logs only its type, so
no address reaches a log.

#### Dependencies

- **INotificationStore** — recipients, notified items, counts of the send log, and the one transaction that applies the
  result of a send (implemented by `NotificationStore`).
- **InboxService** — the inbox of the recipient, with its masking.
- **IAuditStore** — the latest audit sequence, which tells that something changed.
- **INotificationSender** — delivers the mail.
- **NotificationComposer** — classifies, decides what is new, and composes the mail.
- **DigestSchedule** — the slot of the daily summary.
- **NotificationSettings** — the limit of mails for the whole server.
- **TimeProvider** — the clock, replaceable in tests.

#### Callers

- **NotificationWorker** — runs a pass at each polling interval, through a delegate that
  `NotificationRegistration.AddNotificationWorker` wires to `RunPassAsync`.
