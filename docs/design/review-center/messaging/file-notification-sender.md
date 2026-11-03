### FileNotificationSender

![Messaging Structure](MessagingView.svg)

#### Purpose

`FileNotificationSender` implements the Core interface `INotificationSender` by writing one `.eml` file for each message
into a folder, for demonstrations, development and tests. Its single responsibility is to show, as a file that a mail
client can open, exactly the message that an SMTP server would have received.

#### Data Model

The unit keeps only the settings that it is given (`NotificationOptions`) and a logger. Nothing is kept between
sends, so
the class is thread-safe. `IsConfigured` is always true. Each file has a new name made of the UTC time and a random
identifier, for example `20260101T120000000-0123456789abcdef0123456789abcdef.eml`, and holds the MIME message built by
`SmtpNotificationSender.BuildMessage`.

#### Key Methods

**SendAsync**: Writes one message as a file.

- *Parameters*: The message, which must not be null, and a cancellation token.
- *Returns*: A task that completes when the file is written.
- *Postconditions*: The folder of the settings is created when it does not exist. The file is created exclusively
  (`CreateNew`), so an existing file is never overwritten and sends at the same moment never collide. The file parses
  back as a mail message with the sender of the settings, the recipient and the subject of the message, and its text and
  HTML parts.
- *Errors*: A failure to create the folder or to write the file (input and output, access, an unsupported or an invalid
  path, or a security failure) is written to the operator log as the exception type and is reported as a
  `NotificationSendException` with the fixed text "The e-mail file could not be written."; the text holds no path. A
  canceled token passes through as an `OperationCanceledException`. A null message raises `ArgumentNullException`.

#### Error Handling

The caller receives a `NotificationSendException` that never holds the path of the folder. The operator log receives
only the exception type, because the message of an exception can repeat the path. Other failures, which indicate a
mistake in the program, propagate unchanged.

#### Dependencies

- **MimeKit (OTS)** - writes the MIME message to the file stream (see the design of MailKit).
- **SmtpNotificationSender** - `BuildMessage` builds the message, so that the file and a real delivery agree.
- **NotificationOptions** - the settings, which name the sender and the folder.
- **NotificationMessage, INotificationSender and NotificationSendException (Core)** - the message, the interface that is
  implemented, and the failure that is raised.

#### Callers

- **NotificationRegistration** - creates the sender when the settings choose the file transport; the rest of the system
  (`NotificationPreferenceService` of `Core`, and through it the Account page) uses it through the Core interface.
