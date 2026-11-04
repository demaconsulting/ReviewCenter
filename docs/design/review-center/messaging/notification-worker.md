### NotificationWorker

![Messaging Structure](MessagingView.svg)

#### Purpose

`NotificationWorker` is the background service that runs a notification pass at a fixed interval for as long as the
server runs. It is the host part of the notification feature: it knows the clock and the log, and it knows nothing of
the rules of the mails. Those are in `NotificationDispatcher` of the `Core` subsystem, which the worker reaches through
a delegate. The worker has one more duty: whatever happens in a pass, the loop goes on and the server stays up.

#### Data Model

The worker holds four values, all set in the constructor: the delegate that runs one pass and returns a
`NotificationPassResult`, the polling interval, the clock (`TimeProvider`) on which the waits are measured, and the
logger. It keeps no other state. It is a `BackgroundService`, so the host starts it with the server and stops it with
the server.

#### Key Methods

**ExecuteAsync**: The loop of the service.

- *Parameters*: The token that the host cancels when the server stops.
- *Returns*: A task that completes when the host stops the service.
- *Postconditions*: A pass runs at once when the service starts, so news that arrived while the server was down does not
  wait for a whole interval. After each pass the worker waits the polling interval on the clock and runs the next pass.
  A cancellation during the wait ends the loop, so a stop does not wait for the interval.

**The run of a pass (private)**: Runs one pass and logs the outcome. It never throws.

- *Postconditions*: A pass that sent or failed at least one mail is logged as one line with the numbers of mails sent,
  failed, and deferred. A pass that sent and failed nothing is not logged, so an idle server leaves the log quiet. A
  cancellation that comes from the stop of the host is silent: the pass is canceled and nothing is logged. Any other
  exception is logged by the name of its type only, as "A notification pass failed (<type>); the next pass will try
  again.", and the loop runs the next pass after the interval. The task of the service never faults because of a pass.

The constructor refuses a missing delegate, clock, or logger with an `ArgumentNullException`, and a polling interval
that is zero or negative with an `ArgumentOutOfRangeException`, because such an interval would be a busy loop.

**Design constraint: one host per database.** The worker is not coordinated between hosts, because the server is a
single instance by design. Exactly one host may run the worker for a database; two hosts on one database could send the
same mail twice. This is a known limit, not a supported topology.

#### Error Handling

A failing pass is expected: a mail server can be down and a database can be busy. The worker therefore catches every
exception of a pass. It logs only the type, because the message of an exception can repeat an address, and the result of
a pass is logged as numbers, so no address, subject, or text of a mail ever reaches a log. Cancellation by the host is
not an error. Argument errors in the constructor are mistakes of the host registration and stop the start-up.

#### Dependencies

- **NotificationDispatcher (Core)** — the delegate that the registration passes in is `RunPassAsync` of the dispatcher.
- **NotificationPassResult (Core)** — the numbers that are logged.
- **TimeProvider** — the clock of the waits, replaceable in tests.
- **ILogger** — the log of the operator.

#### Callers

- **NotificationRegistration** — `AddNotificationWorker` builds the dispatcher and registers the worker as a hosted
  service, only when e-mail notifications are enabled and the sender is Smtp or File. `ServerHost` calls it, and no
  command-line run does, so a command never starts a background loop.
