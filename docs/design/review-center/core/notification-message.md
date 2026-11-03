### NotificationMessage

![Core Structure](CoreView.svg)

#### Purpose

`NotificationMessage` is one e-mail message to one recipient. Together with the sender contract
(`INotificationSender`), the implementation that is used when no mail server is set up (`NoNotificationSender`), and
the failure type of a send (`NotificationSendException`), it is the whole of what Core knows about sending mail. Core
composes the message, and the real sender, which lives in the host, delivers it. The message checks its parts when it
is built, so that no value that could change the header of a mail (a line break in the subject, a second recipient)
can reach a sender.

#### Data Model

`NotificationMessage` is immutable. It has four properties:

- the recipient (`ToAddress`), always in the normal form given by `NotificationAddress`;
- the subject, from 1 to 200 characters (`MaxSubjectLength`), with no control character at all;
- the plain text body and the HTML body, each not blank, at most 100,000 characters (`MaxBodyLength`), and with no NUL
  character. Line breaks are allowed in a body.

`INotificationSender` has the property `IsConfigured` and the method `SendAsync`. `NoNotificationSender` is a sealed
class without state. `NotificationSendException` derives from `Exception` and has a constructor with a message and a
constructor with a message and an inner exception.

#### Key Methods

**NotificationMessage (constructor)**: Builds a message from the recipient, the subject, the plain text body, and the
HTML body.

- *Returns*: The message, with the recipient normalized by `NotificationAddress`.
- *Postconditions*: An invalid recipient raises `ArgumentException` for `toAddress`, a bad subject for `subject`, and a
  bad body for `textBody` or `htmlBody`. No message text contains the refused value.

**INotificationSender.IsConfigured**: True when the sender can deliver mail. The settings page and the service use it to
decide whether the feature is offered.

**INotificationSender.SendAsync**: Delivers one message.

- *Parameters*: The message and a cancellation token.
- *Postconditions*: When the message was handed over, the task completes. When it could not be delivered, the task
  fails with a `NotificationSendException`. An implementation must not put a password or another secret in the message
  of the exception.

**NoNotificationSender.IsConfigured**: Always false.

**NoNotificationSender.SendAsync**: Always fails with a `NotificationSendException` that says that e-mail notifications
are not configured. A null message raises `ArgumentNullException`.

#### Error Handling

Construction errors are `ArgumentException` with the name of the parameter, and the text never echoes the refused
value, because the value may be attacker controlled or an address. A failed send is always a `NotificationSendException`,
so a caller has one failure to handle and learns nothing about the mail server.

#### Dependencies

- **NotificationAddress** — checks and normalizes the recipient.

#### Callers

- **NotificationPreferenceService** — builds the confirmation message and calls the sender.
- **The sender implementations in the host** — implement the sender contract (an SMTP sender and a file sender).
- **ServerHost** — registers the sender, and `NoNotificationSender` when no mail server is set up.
