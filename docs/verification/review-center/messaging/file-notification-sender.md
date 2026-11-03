### FileNotificationSender

#### Verification Approach

`FileNotificationSender` is verified with unit tests defined in `FileNotificationSenderTests.cs`. The tests use the real
sender with a private temporary folder that is removed after each test, read the files back from disk, and parse them
with MimeKit, so what is checked is what a mail client would open. Nothing is substituted. The failure of the write is
provoked by naming a regular file as the folder.

#### Test Environment

N/A - standard test environment (a temporary folder is created by each test and removed afterwards).

#### Acceptance Criteria

- All tests pass with zero failures.
- The sender reports that it is configured.
- A message becomes one `.eml` file, in a folder that did not exist, that parses back with the subject, the recipient,
  the sender, the text part, and the HTML part.
- Messages that are sent at the same moment never overwrite each other and never fail.
- A folder that cannot be created gives a send error whose message holds no path.
- A successful send leaves only the `.eml` file, and a failed or a canceled send leaves neither an `.eml` file nor a
  temporary file.
- A missing message is refused.

#### Test Scenarios

**FileNotificationSender_IsConfigured_IsTrue**: A sender is created and reports that it is configured. This scenario is
tested by `FileNotificationSender_IsConfigured_IsTrue`.

**FileNotificationSender_Send_WritesParseableEmlFile**: A message is sent to a folder that does not exist yet.
The folder
holds exactly one `.eml` file, which parses back with the subject, the recipient, the sender, the text part, and the
HTML
part. This scenario is tested by `FileNotificationSender_Send_WritesParseableEmlFile`.

**FileNotificationSender_Send_SeveralMessages_WritesSeparateFiles**: The same message is sent five times at the same
moment. The folder holds five files with different names and no send failed. This scenario is tested by
`FileNotificationSender_Send_SeveralMessages_WritesSeparateFiles`.

**FileNotificationSender_Send_FolderIsAFile_ThrowsWithoutPath**: A regular file is named as the folder.
The send raises a
send error and the message of the error does not contain the path. This scenario is tested by
`FileNotificationSender_Send_FolderIsAFile_ThrowsWithoutPath`.

**FileNotificationSender_Send_Success_LeavesOnlyTheEmlFile**: A message is sent. The folder holds exactly one entry,
and it is the `.eml` file, so no temporary file is left. This scenario is tested by
`FileNotificationSender_Send_Success_LeavesOnlyTheEmlFile`.

**FileNotificationSender_Send_Cancelled_LeavesNoFile**: A message is sent with a token that is already canceled. The
send raises a cancellation and the folder is empty. This scenario is tested by
`FileNotificationSender_Send_Cancelled_LeavesNoFile`.

**FileNotificationSender_Send_Failure_LeavesNoMessageOrTemporaryFile**: A regular file is named as the folder. The send
raises a send error and the only entry left in the folder that holds the blocking file is that file. This scenario is
tested by `FileNotificationSender_Send_Failure_LeavesNoMessageOrTemporaryFile`.

**FileNotificationSender_Send_NullMessage_ThrowsArgumentNull**: A missing message raises `ArgumentNullException`. This
scenario is tested by `FileNotificationSender_Send_NullMessage_ThrowsArgumentNull`.
