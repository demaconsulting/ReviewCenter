## MailKit

This document provides the verification evidence for the MailKit OTS software item (with MimeKit, which it brings).
Requirements for this OTS item are defined in the MailKit OTS Software Requirements document.

### Required Functionality

MailKit connects to an SMTP server, hands it the sender, the recipient, and the message, signs in with a user name and a
password, reports the failures of the conversation, and ends a send when the cancellation token is canceled. MimeKit
builds a mail message with a sender and display name, a recipient, a subject, and a text and an HTML part, and writes it
to a stream, so that it parses back with the same content and so that special characters stay inside their own headers.
A
passing test confirms that the e-mail notifications of the product reach an SMTP server and a file in the way
the product
needs.

### Verification Approach

MailKit has no self-validation mode and is verified by the project's own tests, which call it through the
`SmtpNotificationSender` and `FileNotificationSender` units of the Messaging subsystem. The tests do not trust the
library. The sender tests run the real client against `FakeSmtpServer`, a small SMTP server of the test project that
listens on the local machine and records what it receives (the envelope, the credentials, and the message data), and
they
parse the recorded message with MimeKit. The file sender tests read the written file back and parse it.
No mail leaves the
machine. The encryption of a connection is the work of the library and of the runtime; the fake server speaks no TLS, so
the tests verify only that each TLS mode of the settings is mapped to an option of the library. Each scenario names a
specific test method that depends on the packages under verification. A passing pipeline run for the scenario
constitutes
evidence that the requirement is satisfied.

### Test Scenarios

#### SmtpNotificationSender_Send_DeliversMessageToServer

**Scenario**: A message with a subject, a plain text body, and an HTML body is sent to the fake server.
The server records
the envelope and the message data, which are then parsed.

**Expected**: The server received the configured sender and the recipient as its envelope. The parsed message has the
subject, the recipient, the sender name, the text part, and the HTML part.

**Requirement coverage**: `ReviewCenter-OTS-MailKit-Smtp`, `ReviewCenter-OTS-MailKit-EnvelopeSender`,
`ReviewCenter-OTS-MailKit-EnvelopeRecipient`,
`ReviewCenter-OTS-MailKit-MimeSender`, `ReviewCenter-OTS-MailKit-MimeText`,
`ReviewCenter-OTS-MailKit-MimeHtml`.

#### SmtpNotificationSender_Send_SpecialCharacters_DoNotLeaveTheirHeader

**Scenario**: A sender name with a comma, quotes, and angle brackets and a subject with characters outside ASCII and the
text of a header line are sent to the fake server and parsed.

**Expected**: The message has one sender with the configured address and name, the subject is unchanged,
no `Cc` or `Bcc`
header exists, and the server received one recipient.

**Requirement coverage**: `ReviewCenter-OTS-MailKit-MimeSenderName`, `ReviewCenter-OTS-MailKit-MimeSubjectEncoding`,
`ReviewCenter-OTS-MailKit-MimeSubjectHeader`.

#### SmtpNotificationSender_Send_WithCredentials_Authenticates

**Scenario**: A server that expects a password receives a send with a user name and that password.

**Expected**: The server records the user name and the password and the message.

**Requirement coverage**: `ReviewCenter-OTS-MailKit-Auth`.

#### SmtpNotificationSender_Send_WrongPassword_ThrowsWithoutSecret

**Scenario**: A server that expects another password receives a send with a wrong password.

**Expected**: The send fails and the server holds no message.

**Requirement coverage**: `ReviewCenter-OTS-MailKit-AuthRefused`.

#### SmtpNotificationSender_Send_RecipientRefused_ThrowsWithoutAddress

**Scenario**: A server that refuses every recipient receives a send.

**Expected**: The send fails and the server holds no message.

**Requirement coverage**: `ReviewCenter-OTS-MailKit-RecipientRefused`.

#### SmtpNotificationSender_Send_ServerUnreachable_Throws

**Scenario**: A send goes to a port on which nobody listens.

**Expected**: The send fails.

**Requirement coverage**: `ReviewCenter-OTS-MailKit-Unreachable`.

#### SmtpNotificationSender_Send_Canceled_ThrowsOperationCanceled

**Scenario**: A send is started with a cancellation token that is already canceled.

**Expected**: The send ends with a cancellation and the server holds no message.

**Requirement coverage**: `ReviewCenter-OTS-MailKit-Cancellation`.

#### SmtpNotificationSender_MapTls_MapsEveryMode

**Scenario**: Each TLS mode of the settings is mapped to a connection option of the library.

**Expected**: The three modes map to the options for no protection, the upgrade to TLS, and TLS from the first byte.

**Requirement coverage**: `ReviewCenter-OTS-MailKit-Tls`.

#### FileNotificationSender_Send_WritesParseableEmlFile

**Scenario**: A message is written to a file by the file sender, and the file is loaded with MimeKit.

**Expected**: The loaded message has the subject, the recipient, the sender, the text part, and the HTML part.

**Requirement coverage**: `ReviewCenter-OTS-MailKit-MimeSubject`, `ReviewCenter-OTS-MailKit-MimeRecipient`,
`ReviewCenter-OTS-MailKit-MimeSender`, `ReviewCenter-OTS-MailKit-MimeText`, `ReviewCenter-OTS-MailKit-MimeHtml`.
