### NotificationMessage

#### Verification Approach

`NotificationMessage` is verified with unit tests defined in `NotificationMessageTests.cs`. The tests build messages
directly and check the kept parts and the exceptions that refuse a bad part, including that the refused value is not
repeated in the message of the exception. `NoNotificationSender` is verified in the same file by calling it. No stand-in
and no database are needed. How the service uses the sender contract, and what happens when a send fails, is verified
by the `NotificationPreferenceService` tests over an in-memory sender.

#### Test Environment

N/A - standard test environment (no special fixtures).

#### Acceptance Criteria

- All tests pass with zero failures.
- A message keeps its subject and bodies, and its recipient is in the normal form of an address.
- A recipient that is a list, has a line break, is not an address, or is empty is refused without repeating the value.
- A subject with a line break, a tab, or a NUL is refused without repeating the value.
- An empty subject and a subject of more than 200 characters are refused, and one of 200 characters is accepted.
- A blank body, a body with a NUL character, and a body of more than 100,000 characters are refused, and a body with
  line breaks is accepted.
- An empty plain text body and an empty HTML body are refused.
- The sender that is used when no mail server is set up reports that it is not configured and fails a send with a send
  exception.

#### Test Scenarios

**NotificationMessage_Constructor_ValidParts_KeepsThemAndNormalizesRecipient**: A message with valid parts keeps the
subject and both bodies, and the recipient is in lower case. This scenario is tested by
`NotificationMessage_Constructor_ValidParts_KeepsThemAndNormalizesRecipient`.

**NotificationMessage_Constructor_InvalidRecipient_ThrowsWithoutEchoingTheValue**: A recipient with a second header
behind a line break, a list, a text that is not an address, or an empty text raises an exception for `toAddress` whose
message does not contain the value. This scenario is tested by
`NotificationMessage_Constructor_InvalidRecipient_ThrowsWithoutEchoingTheValue`.

**NotificationMessage_Constructor_ControlCharacterInSubject_ThrowsWithoutEchoingTheValue**: A subject with a line
break and a second header, a bare line break, a carriage return, a tab, or a NUL raises an exception for `subject`
whose message does not contain the injected header. This scenario is tested by
`NotificationMessage_Constructor_ControlCharacterInSubject_ThrowsWithoutEchoingTheValue`.

**NotificationMessage_Constructor_SubjectLength_RefusesEmptyAndTooLong**: A blank subject and a subject that is one
character too long are refused; a subject of the maximum length is accepted. This scenario is tested by
`NotificationMessage_Constructor_SubjectLength_RefusesEmptyAndTooLong`.

**NotificationMessage_Constructor_InvalidBody_Throws**: An empty plain text body, an HTML body with a NUL, and a plain
text body that is one character too long raise an exception for the matching body parameter. This scenario is tested
by `NotificationMessage_Constructor_InvalidBody_Throws`.

**NotificationMessage_Constructor_EmptyBody_Throws**: An empty plain text body and an empty HTML body each raise an
exception for the matching body parameter. This scenario is tested by `NotificationMessage_Constructor_EmptyBody_Throws`.

**NotificationMessage_Constructor_BodyWithLineBreaks_Succeeds**: A body of several lines is accepted and kept. This
scenario is tested by `NotificationMessage_Constructor_BodyWithLineBreaks_Succeeds`.

**NoNotificationSender_SendAsync_ThrowsSendException**: The sender reports that it is not configured, and a send fails
with a send exception. This scenario is tested by `NoNotificationSender_SendAsync_ThrowsSendException`.
