## Messaging

![Messaging Structure](MessagingView.svg)

### Overview

The `Messaging` subsystem delivers the e-mail of the optional e-mail notifications. It owns the `Notifications`
configuration section, the check of that section at start-up, and the two transports that deliver a message: an SMTP
server and a folder of files. It implements the Core interface `INotificationSender`, so that the domain services know
only the interface and a service can be tested with a recording fake. The subsystem contains four units:
`NotificationOptions`, the settings, their check, the warnings, the resolution of the SMTP password, and the
registration of the settings, of the sender, and of the worker (`NotificationRegistration`); `SmtpNotificationSender`,
which delivers a message through an SMTP server with the MailKit library; `FileNotificationSender`, which writes a
message as a file for demonstrations, development and tests; and `NotificationWorker`, the background service that runs
a notification pass at the polling interval. The types of the subsystem are internal to the application.

E-mail notifications are off unless `Notifications:Enabled` is set. While they are off nothing else of the section is
read, checked or registered, and the sender that the rest of the system receives is the Core `NoNotificationSender`,
which reports that it is not configured and refuses to send. The subsystem does not decide when a mail is sent, to
whom, or with which text; that is the work of the `Core` subsystem (`NotificationPreferenceService` for the
confirmation mail, `NotificationDispatcher` for the notification mails). The worker only runs the passes of the
dispatcher, survives a failed pass, and logs no personal data.

### Interfaces

**INotificationSender (Core)**: The delivery of one message.

- *Type*: In-process .NET interface implemented by `SmtpNotificationSender`, `FileNotificationSender` and, in `Core`,
  `NoNotificationSender`.
- *Role*: Provider.
- *Contract*: `IsConfigured` tells whether a transport is configured. `SendAsync` delivers exactly the
  `NotificationMessage` it is given (one recipient, a subject, a text body and an HTML body). A message that could
  not be
  delivered is reported as a `NotificationSendException` whose text holds no password, no address and no text from the
  server. A canceled token ends the send with an `OperationCanceledException`, which is not a send failure.
- *Constraints*: The registered sender is a singleton, so every implementation is thread-safe. Every send is
  independent; nothing is kept between sends.

**Notifications settings**: The `Notifications` configuration section.

- *Type*: Configuration (`appsettings.json` and environment variables such as `Notifications__Smtp__Host`).
- *Role*: Consumer.
- *Contract*: `Enabled`, `Sender` (`None`, `Smtp` or `File`), `FromAddress`, `FromName`, `BaseUrl`, the SMTP settings
  `Host`, `Port`, `Tls`, `Username`, `Password`, `PasswordFile` and `TimeoutSeconds`, the file setting `Directory`, and
  the schedule settings (`DigestHour`, `TimeZone`, `BatchWindowSeconds`, `PollSeconds`, `IncludeTitles`,
  `MaxMailsPerHour`).
- *Constraints*: Invalid settings stop the start-up with a message that names the setting and never holds a value.

**AddNotifications**: Used by the `Hosting` subsystem.

- *Type*: In-process .NET extension method of the service collection.
- *Role*: Provider.
- *Contract*: Registers the settings, the check of the settings, the Core `NotificationSettings` (what the pages and the
  preference service ask) and the `INotificationSender` that the settings choose.
- *Constraints*: The settings are checked when they are first used, which `ServerHost` forces at start-up, so a wrong
  configuration stops the server instead of failing at the first mail.

**NotificationOptions warnings and password**: Used by the `Hosting` and `SelfTest` subsystems.

- *Type*: In-process .NET methods.
- *Role*: Provider.
- *Contract*: `GetWarnings` lists the settings that work but deserve a second look, and `ResolvePassword` resolves the
  SMTP password the same way for the check and for the sender.
- *Constraints*: No sentence of a warning and no message of an exception holds the password.

### Design

**Choice of the transport**: `NotificationRegistration.AddNotifications` registers the sender as a singleton. While
`Enabled` is off the sender is `NoNotificationSender`; with `Sender` set to `Smtp` it is `SmtpNotificationSender`, with
`File` it is `FileNotificationSender`, and with `None` it is `NoNotificationSender` again. The `NotificationSettings`
record says that notifications are enabled only when they are switched on and a transport is chosen. The code that needs
mail therefore never tests for null: `NotificationPreferenceService`, which the `Account` page uses to let a user
confirm an address and choose notifications, asks that record and `IsConfigured`, and treats the feature as not
available when either says no.

**Settings and checks**: The settings are bound by the options system, so they can come from `appsettings.json` or from
the environment. `NotificationOptionsValidator` checks them once, when they are first used, and only while the feature
is on. It reports the first problem only. It requires a transport, a valid sender address and base address, an SMTP
host, port, TLS mode and time-out within bounds (or a folder for the file transport), a complete login (a user name
needs a password and the other way round), and bounds for the schedule settings and the mail limit. A base address
must be absolute and without query, fragment or user information, and must use https, except that http is accepted
for the local machine. A header injection through a line break in the sender address or name is refused here; for
the recipient and the subject it is refused where the message is built, in `NotificationMessage` of `Core`.

**Credentials and TLS**: The TLS mode is `None`, `StartTls` (the connection starts in clear text and is upgraded to TLS
by a command of the protocol; the default) or `Implicit` (TLS from the first byte). A login over `None` to a machine
other than the
local machine is rejected, because the password would cross the network in clear text; `None` to the local machine is
accepted so that a test mail server needs no certificate. A connection without TLS to another machine without a login
is accepted but warned about. The password is given inline (`Password`) or in a file (`PasswordFile`), never both; the
file form is resolved by `SecretSource` of the `Utilities` subsystem. `ResolvePassword` is the one resolver, used by
the check and by the sender, and the sender reads the file again at each send, so a rotated password needs no restart.

**Shared message building**: `SmtpNotificationSender.BuildMessage` builds the MIME message (sender with display name,
recipient, subject, and a body of a text part and an HTML part) with the MimeKit library. `FileNotificationSender`
uses the same method, so a file shows exactly what an SMTP server would receive. MimeKit encodes the display name
and the
subject, so special characters stay inside their own header.

**Errors**: Both senders catch the failures of their transport, write only the exception type (and, for SMTP, the status
code) to the operator log, and throw a `NotificationSendException` with a fixed message, so a password, an address or
text of the server never reaches a page. The operator log never holds the recipient, the password, the text of the
server, or a path, because exception messages can repeat them. Cancellation is passed on unchanged.

**Libraries**: MailKit (with MimeKit) is the only OTS item of the subsystem (see the design of MailKit). The senders are
the only units that reference it; the rest of the product uses the Core interface.

**The worker**: `NotificationRegistration.AddNotificationWorker` registers `NotificationWorker` as a hosted service
only when notifications are enabled and the sender is Smtp or File, and only the web host calls it, so a command-line run
never starts a background loop. The registration builds the Core `NotificationComposer`, `DigestSchedule`, and
`NotificationDispatcher` from the settings and gives the worker the method of the dispatcher that runs one pass. The
worker runs a pass at once, then at each polling interval (`PollSeconds`) on the clock of the host. A failed pass is
logged by the type of its exception only, and the loop goes on. A pass that sent or failed nothing is not logged. See
the `NotificationWorker` unit.
