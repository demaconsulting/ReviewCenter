### SmtpNotificationSender

![Messaging Structure](MessagingView.svg)

#### Purpose

`SmtpNotificationSender` implements the Core interface `INotificationSender` on top of an SMTP server, with the MailKit
library. Its single responsibility is to turn a `NotificationMessage` into a MIME message and to hand it to the mail
server of the settings.

#### Data Model

The unit keeps only the settings that it is given (`NotificationOptions`) and a logger. Each send opens one connection,
signs in when a user name is set, sends the message, and closes the connection, so nothing is kept between sends and the
class is thread-safe. `IsConfigured` is always true.

The sender name and address come from the settings; the recipient, the subject, and the two bodies come from the
`NotificationMessage`. That type, in `Core`, has already refused line breaks and other control characters in the
recipient and the subject, so no header can be injected through them; the validator of `NotificationOptions` does the
same for the sender address and name.

#### Key Methods

**SendAsync**: Delivers one message.

- *Parameters*: The message, which must not be null, and a cancellation token.
- *Returns*: A task that completes when the server has accepted the message.
- *Postconditions*: The MIME message is built, the password is resolved, and one connection is opened with the time-out
  of the settings and the protection of the TLS mode. The user name and the password are sent when a user name is set.
  The message is sent and the connection is closed. The server then holds one mail with the sender, the recipient, the
  subject, and the text and HTML parts of the message.
- *Errors*: Any failure (connection, login, refused recipient, protocol, or an unusable password) is written to the
  operator log as the exception type and the SMTP status code, never the exception message, and is reported as a
  `NotificationSendException` with the fixed text "The e-mail could not be sent through the SMTP server." A canceled
  token passes through as an `OperationCanceledException`, not as a send failure. A null message raises
  `ArgumentNullException`.

**BuildMessage**: Builds the MIME message.

- *Parameters*: The settings, which name the sender, and the message; neither may be null.
- *Returns*: A message whose `From` header has the sender name and address, whose `To` header has the recipient, whose
  subject is that of the message, and whose body is a plain-text part and an HTML part. The caller supplies the HTML
  already encoded.
- *Postconditions*: The library encodes the display name and the subject, so a comma, a quote, an angle bracket, or a
  character outside ASCII stays inside its own header and cannot add a sender, a recipient, or a header. The method is
  shared with `FileNotificationSender`.

**MapTls**: Maps the TLS mode of the settings to the connection option of the library: `None` to no protection,
`StartTls`
to the upgrade of the connection to TLS, and `Implicit` to TLS from the first byte. A value that is not defined raises
`ArgumentOutOfRangeException`, so an unknown mode never falls back to weaker protection.

#### Error Handling

The caller receives a `NotificationSendException` whose message is fixed: it never holds the password, the address of
the recipient, the text of the server, or the host. The operator log receives only the exception type and the SMTP
status code, because the message of an exception can repeat the rejected address; the log never holds the recipient, the
password, the text of the server, or the host. The password is read from the settings at each send through
`NotificationOptions.ResolvePassword` and is never logged.

#### Dependencies

- **MailKit and MimeKit (OTS)** - the SMTP client and the MIME message (see the design of MailKit).
- **NotificationOptions** - the settings, `ResolvePassword`, and the TLS mode.
- **NotificationMessage, INotificationSender and NotificationSendException (Core)** - the message, the interface that is
  implemented, and the failure that is raised.

#### Callers

- **NotificationRegistration** - creates the sender when the settings choose the SMTP transport; the rest of the system
  (`NotificationPreferenceService` of `Core`, and through it the Account page) uses it through the Core interface.
- **FileNotificationSender** - uses `BuildMessage`.
