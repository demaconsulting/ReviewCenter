## Messaging

### Verification Approach

The `Messaging` subsystem is verified by subsystem tests defined in `MessagingSubsystemTests.cs`. The tests build a real
service provider with the real notification registration over an in-memory configuration of the `Notifications` section,
so that the settings, the check of the settings, and the senders work together in the way the server uses them. They
resolve the registered `INotificationSender` and the `NotificationSettings` of `Core` and use them. The only substitute
is a logger factory that discards everything. The file transport tests write into a private temporary folder that is
removed after each test. The SMTP transport is only resolved in the subsystem tests, not used, because the delivery
through an SMTP server is verified in detail by the unit tests of `SmtpNotificationSender`, against a small SMTP server
that listens on the local machine. The units are verified by their own unit tests.

### Test Environment

N/A - standard test environment (the file transport tests create a temporary folder and remove it afterwards).

### Acceptance Criteria

- All subsystem tests pass with zero failures.
- With the default settings, which are off, the registered sender reports that it is not configured, a send fails with a
  send error, and the notification settings are not enabled.
- With the file transport selected, the registered sender delivers a message as one `.eml` file in the folder, and the
  notification settings are enabled.
- With the SMTP transport selected, the registered sender reports that it is configured.
- Settings that are not valid are rejected when they are first used, and the message names the invalid setting.

### Test Scenarios

**MessagingSubsystem_NotificationsOff_SenderIsNotConfiguredAndSendFails**: A service provider is built over empty
settings. The sender reports that it is not configured, the notification settings are not enabled, and sending a message
raises a send error. This scenario is tested by `MessagingSubsystem_NotificationsOff_SenderIsNotConfiguredAndSendFails`.

**MessagingSubsystem_FileTransport_SettingsChooseFileSenderThatDeliversMessage**: A service provider is built over
settings that switch notifications on, select the file transport, and give a sender address, a base address, and a
folder. A message is sent. The registered sender is the file sender, the notification settings are enabled, and the
folder holds one `.eml` file. This scenario is tested by
`MessagingSubsystem_FileTransport_SettingsChooseFileSenderThatDeliversMessage`.

**MessagingSubsystem_SmtpTransport_SettingsChooseSmtpSender**: A service provider is built over settings that switch
notifications on, select the SMTP transport, and give a sender address, a base address, and a host.
The registered sender
is the SMTP sender and reports that it is configured. This scenario is tested by
`MessagingSubsystem_SmtpTransport_SettingsChooseSmtpSender`.

**MessagingSubsystem_InvalidSettings_AreRefusedNamingTheSetting**: A service provider is built over SMTP settings
without
a host. Resolving the sender fails, and the message of the failure names `Notifications:Smtp:Host`. This scenario is
tested by `MessagingSubsystem_InvalidSettings_AreRefusedNamingTheSetting`.
