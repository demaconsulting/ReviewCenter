### SmtpNotificationSender

#### Verification Approach

`SmtpNotificationSender` is verified with unit tests defined in `SmtpNotificationSenderTests.cs`. The tests use the real
sender, with the real MailKit client, against `FakeSmtpServer`, a small SMTP server of the test project that listens on
the loopback interface on a free port and records the envelope, the credentials, and the message data it receives. The
test then parses the recorded message with MimeKit. No mail leaves the machine,
and nothing of the sender is substituted;
the server can be told to refuse a password or every recipient. The settings use the TLS mode `None`, because the fake
server speaks no TLS; the mapping of the TLS modes to the options of the library is verified by its own test. The
encryption itself is the work of the library and of the runtime and is not tested here.

#### Test Environment

N/A - standard test environment (a loopback TCP port is opened by each test that needs a server and closed afterwards).

#### Acceptance Criteria

- All tests pass with zero failures.
- The sender reports that it is configured.
- A message arrives at the server with the sender and the recipient of the envelope, the subject, the sender name, and
  both body parts.
- A sender name with special characters and a subject with a header-like text stay inside their own headers, the subject
  keeps its characters outside ASCII, and the server gets exactly one recipient.
- The user name and the password are sent to the server when a user name is set.
- A wrong password, a refused recipient, and an unreachable server each give a send error, nothing is delivered after a
  refused login, and the message of the error holds no password, no recipient address, and no text of the server.
- A canceled token ends the send with a cancellation and delivers nothing.
- The operator log of a failed send holds no recipient address, no password, no text of the server, and no exception
  object.
- A missing message is refused.
- The three TLS modes map to the matching options of the library and an undefined mode is refused.

#### Test Scenarios

**SmtpNotificationSender_IsConfigured_IsTrue**: A sender is created and reports that it is configured. This scenario is
tested by `SmtpNotificationSender_IsConfigured_IsTrue`.

**SmtpNotificationSender_Send_DeliversMessageToServer**: A message is sent to the fake server. The server received the
configured sender and the recipient of the message as its envelope, and the message that was parsed from the received
data has the subject, the recipient, the sender name, the text part, and the HTML part. This scenario is tested by
`SmtpNotificationSender_Send_DeliversMessageToServer`.

**SmtpNotificationSender_Send_SpecialCharacters_DoNotLeaveTheirHeader**: A sender name with a comma, quotes and angle
brackets and a subject with non-ASCII text and the text `Bcc:` are sent. The parsed message has one sender with the
configured address and name, the subject unchanged, no `Cc` or `Bcc` header, and the server received one recipient. This
scenario is tested by `SmtpNotificationSender_Send_SpecialCharacters_DoNotLeaveTheirHeader`.

**SmtpNotificationSender_Send_WithCredentials_Authenticates**: A server that expects a password receives the user name
and the password of the settings and one message. This scenario is tested by
`SmtpNotificationSender_Send_WithCredentials_Authenticates`.

**SmtpNotificationSender_Send_WrongPassword_ThrowsWithoutSecret**: A server that expects another password refuses the
login. The send raises a send error, the server holds no message, and the message of the error contains neither the
password nor the text of the server. This scenario is tested by
`SmtpNotificationSender_Send_WrongPassword_ThrowsWithoutSecret`.

**SmtpNotificationSender_Send_RecipientRefused_ThrowsWithoutAddress**: A server that refuses every recipient. The send
raises a send error, the server holds no message, and the message of the error contains neither the address nor the text
of the server. This scenario is tested by `SmtpNotificationSender_Send_RecipientRefused_ThrowsWithoutAddress`.

**SmtpNotificationSender_Send_Failure_LogsNoRecipientOrPassword**: A server that refuses every recipient is used with a
login whose password is set. A recording logger holds exactly one entry for the failed send; it has no exception
object and its text contains neither the address, nor the password, nor the text of the server. This scenario is tested
by `SmtpNotificationSender_Send_Failure_LogsNoRecipientOrPassword`.

**SmtpNotificationSender_Send_ServerUnreachable_Throws**: The send goes to a port on which nobody listens and raises a
send error. This scenario is tested by `SmtpNotificationSender_Send_ServerUnreachable_Throws`.

**SmtpNotificationSender_Send_Canceled_ThrowsOperationCanceled**: A token that is already canceled ends the send
with a
cancellation, not with a send error, and the server holds no message. This scenario is tested by
`SmtpNotificationSender_Send_Canceled_ThrowsOperationCanceled`.

**SmtpNotificationSender_Send_NullMessage_ThrowsArgumentNull**: A missing message raises `ArgumentNullException`. This
scenario is tested by `SmtpNotificationSender_Send_NullMessage_ThrowsArgumentNull`.

**SmtpNotificationSender_MapTls_MapsEveryMode**: The modes `None`, `StartTls` and `Implicit` map to no protection, the
upgrade to TLS, and TLS from the first byte; a mode that is not defined raises `ArgumentOutOfRangeException`. This
scenario is tested by `SmtpNotificationSender_MapTls_MapsEveryMode`.
