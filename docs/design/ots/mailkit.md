## MailKit

This document describes the integration and usage design for the `MailKit` OTS software item (NuGet package `MailKit`,
which brings the `MimeKit` package).

### Purpose

MailKit is a library for mail protocols, and MimeKit, which it depends on, is a library for mail messages in the MIME
format. ReviewCenter uses them to deliver the optional e-mail notifications: MailKit connects to an SMTP server,
protects
the connection with TLS, signs in, and sends a message; MimeKit builds the message (sender, recipient, subject, a
plain-text part and an HTML part), encodes it, and writes it to a stream. Both libraries are open source with a
permissive
license.

### Features Used

- `SmtpClient` to connect to an SMTP server with a time-out, to sign in with a user name and a password, to send a
  message,
  and to disconnect, in the asynchronous forms that take a cancellation token
- `SecureSocketOptions` to select no protection, the upgrade of the connection to TLS, or TLS from the first byte
- `MimeMessage`, `MailboxAddress` and `BodyBuilder` to build a message with a sender and a display name, a recipient, a
  subject, and a text and an HTML body
- The writing of a message to a stream, and the loading of a message from a stream or a file (used by the tests to check
  what was delivered)

### Integration Pattern

Only the `SmtpNotificationSender` unit of the `Messaging` subsystem uses the SMTP client, and only that unit and
`FileNotificationSender` use the message types. The rest of the product, including `Core`, uses the Core interface
`INotificationSender` and the type `NotificationMessage`, so the libraries can be replaced without touching the rules.
The registration creates the sender only when the settings choose it; while e-mail notifications are off, or the file
transport is chosen, nothing of the SMTP client is used.

A client is created for each send and is disposed at the end of it, so no connection is kept and no state is shared. The
unit validates what the libraries do not: the recipient and the subject must have no control characters (checked by
`NotificationMessage`), and the settings, including the rule that a login needs TLS unless the server is on the local
machine, are checked by `NotificationOptions`. The libraries encode the display name and the subject, so special
characters cannot add a header. A failure of the libraries is caught by the sender, written to the operator log, and
reported as a fixed `NotificationSendException`, so that no text of the server, address, or password reaches a page.

The packages have no self-validation mode. The evidence that they behave as the product needs is the passing tests of
`SmtpNotificationSender` and `FileNotificationSender`: the first runs the real client against a small SMTP server that
listens on the local machine, and both parse the messages that were produced. A change of behavior after an upgrade of
the packages shows as a failing test.
