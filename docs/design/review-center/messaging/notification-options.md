### NotificationOptions

![Messaging Structure](MessagingView.svg)

#### Purpose

`NotificationOptions` holds the settings of the optional e-mail notifications, checks them when the server starts, lists
the settings that deserve a second look, resolves the password of the SMTP server, and registers the settings and the
sender that they choose. The unit is the file `NotificationOptions.cs` (the settings types, the check
`NotificationOptionsValidator`, and the enumerations) together with `NotificationRegistration.cs` (the registration).
Its single responsibility is to turn the `Notifications` configuration section into a checked, ready-to-use
configuration of the mail transport.

#### Data Model

The settings are plain holders that are bound by the options system and are not thread-safe while they are being bound;
once the server runs they are only read. Nothing prints them, so the password never reaches a log, a message or a page.

- `NotificationOptions` (section `Notifications`): `Enabled` (default off), `Sender` (`NotificationSenderKind`: `None`,
  `Smtp`, `File`; default `None`), `BaseUrl`, `FromAddress`, `FromName` (default is the default instance name of
  `NotificationSettings`), `Smtp`, `File`, `DigestHour` (default 8), `TimeZone` (default `UTC`), `BatchWindowSeconds`
  (default 300), `PollSeconds` (default 60), `IncludeTitles` (default true), and `MaxMailsPerHour` (default of
  `NotificationSettings`). The constants name the longest accepted values: the sender name (80 characters), the base
  address (512), host, user name and file path (512), and password (512).
- `SmtpNotificationOptions` (section `Notifications:Smtp`): `Host`, `Port` (default 587), `Tls` (`SmtpTlsMode`:
  `None`, `StartTls`, `Implicit`; default `StartTls`), `Username`, `Password`, `PasswordFile`, and `TimeoutSeconds`
  (default 30).
- `FileNotificationOptions` (section `Notifications:File`): `Directory`.

The accepted bounds are: port 1 to 65535, time-out 5 to 120 seconds, digest hour 0 to 23, batch window 30 to 3600
seconds, poll interval 10 to 3600 seconds, and mail limit 1 to 10000 per hour. The settings of the schedule are
checked here and are in effect: the digest hour and the time zone give the daily summary its time, the batch window is
the time in which changes are collected into one mail, the poll interval is the time between two notification passes,
`IncludeTitles` decides whether a mail names the titles of reviews, and the mail limit bounds the server per hour.

#### Key Methods

**NotificationOptionsValidator.Validate**: Checks the settings the way the options system does.

- *Parameters*: The name of the options instance (not used) and the settings; the settings must not be null.
- *Returns*: Success while `Enabled` is off, whatever the other values hold. Otherwise success when the settings are
  usable, and a failure with the message of the first problem when they are not.
- *Postconditions*: The checks run in a fixed order: the transport, the sender address (present, and exactly one valid
  address, so a line break is refused), the sender name (length and control characters), the base address (present,
  absolute, bounded, no query, fragment or user information, https or http for a loopback host), the settings of the
  chosen transport, the digest hour, the time zone (known to the operating system), the batch window, the poll
  interval, and the mail limit. The SMTP checks cover the host (present, no white space or control characters, bounded),
  the port, the TLS mode (defined), the time-out, the user name, and the password. A user name needs a password and a
  password needs a user name; both an inline password and a password file are refused; a user name with TLS mode `None`
  for a host that is not a loopback host is refused. The checks of the file transport require a folder. Every message
  names the setting and the rule and holds no value.

**ResolvePassword**: Resolves the SMTP password.

- *Returns*: The inline password, the trimmed contents of the password file, or null when neither is set.
- *Postconditions*: The file is read at each call, so a rotated password is picked up without a restart. The length
  (at most 512 characters) and the absence of control characters are checked on every call, so a file that changes
  after the start-up check cannot slip past them.
- *Errors*: An `InvalidOperationException` when both settings are set, when the file is missing, unreadable, empty or
  too
  large (reported by `SecretSource`), or when the password is too long or holds a control character; the message
  names the
  setting and never holds the password.

**GetWarnings**: Lists the settings that work but that an operator should reconsider.

- *Returns*: One sentence per warning, empty while notifications are off, while another transport than SMTP is chosen,
  or when nothing is doubtful. No sentence holds a value.
- *Postconditions*: A warning is given for a password that is set inline (a file is easier to protect) and for TLS mode
  `None` to a host that is not a loopback host (the mail travels unencrypted).

**IsLoopbackHost**: Tells whether a host name or address is the local machine: `localhost` or a loopback IP address,
with optional brackets and white space ignored. Null and empty are not loopback.

**NotificationRegistration.AddNotifications**: Registers the settings, the validator, the Core `NotificationSettings`,
and the `INotificationSender`.

- *Parameters*: The service collection and the configuration, neither of which may be null.
- *Returns*: The same service collection.
- *Postconditions*: The settings are bound to the `Notifications` section and are checked when first used. The
  `NotificationSettings` record is enabled only when notifications are on and the transport is `Smtp` or `File`.
  The sender
  is `NoNotificationSender` while notifications are off or no transport is chosen, `SmtpNotificationSender` for `Smtp`,
  and `FileNotificationSender` for `File`.

**NotificationRegistration.AddNotificationWorker**: Registers the background worker that mails the notifications.

- *Parameters*: The service collection and the configuration, neither of which may be null.
- *Returns*: The same service collection.
- *Postconditions*: Only while `Notifications:Enabled` is true and the transport is `Smtp` or `File` is exactly one
  hosted `NotificationWorker` registered; otherwise (off, no section, or the transport `None`) nothing is registered, so
  no timer runs and no mail is composed. When the worker is first built it gets a `NotificationComposer` with the
  instance name, the base address, and `IncludeTitles`; a `DigestSchedule` with the digest hour and the time zone; a
  `NotificationDispatcher` with the store, the inbox, the audit store, the sender, the settings, the clock, the batch
  window of `BatchWindowSeconds`, and a callback that logs only the type of a failure; and the poll interval of
  `PollSeconds`. The method belongs to the web host only: `ServerHost` calls it from `CreateApplication` and never
  from `AddServices`, so a command and the self-test get no worker.

#### Error Handling

An invalid configuration is reported by the validator as a failed options validation that names the setting and never
holds a value; `ServerHost` turns it into the message of the failed start. `ResolvePassword` raises
`InvalidOperationException` with the same guarantee. Null arguments raise `ArgumentNullException`. The password is never
written to a log, a message or a warning.

#### Dependencies

- **NotificationAddress, NotificationSettings and INotificationSender (Core)** - the address check, the default
  values, and the interface that the registered sender implements.
- **SecretSource (Utilities)** - the inline-or-file resolution of the password.
- **Microsoft.Extensions.Options and dependency injection** - the binding, the validation, and the registration.
- **SmtpNotificationSender and FileNotificationSender** - the senders the registration creates.
- **NotificationWorker, NotificationDispatcher, NotificationComposer, and DigestSchedule** - the parts that the worker
  registration builds from the settings.

#### Callers

- **ServerHost** - registers the notifications, registers the worker (web host only), forces the check at start-up, and
  logs the warnings.
- **ConfiguredNotificationsCheck of the self-validation** - runs the same check and the same password resolution.
- **SmtpNotificationSender and FileNotificationSender** - read the settings and resolve the password.
- **NotificationPreferenceService and the Account page** - use the Core `NotificationSettings` and the registered
  sender to
  tell whether e-mail notifications are available.
