### NotificationOptions

#### Verification Approach

`NotificationOptions` is verified with unit tests defined in `NotificationOptionsTests.cs`. The tests use the real
settings, the real `NotificationOptionsValidator`, and the real configuration binding over an in-memory configuration
source; nothing is substituted. Each validator test starts from complete enabled settings of one transport, breaks one
value, and checks that the check fails with a message that names the setting and does not contain the test password.
The registration of the sender is verified by the subsystem tests of `Messaging`, because it only makes sense together
with the senders. The registration of the worker is verified in `NotificationWorkerRegistrationTests.cs`.

#### Test Environment

N/A - standard test environment (the password file tests create a temporary folder and remove it afterwards).

#### Acceptance Criteria

- All tests pass with zero failures.
- A new settings object has e-mail notifications switched off, no transport, and the documented defaults.
- The settings bind from the `Notifications`, `Notifications:Smtp` and `Notifications:File` sections.
- While notifications are off, any other value is accepted; complete settings of either transport are accepted.
- Missing or wrong values are rejected with a message that names the setting: the transport, the sender address (also
  with a line break), the sender name with a line break, the base address (missing, relative, with a query, a fragment,
  or user information, and http for another machine), the SMTP host, port, TLS mode and time-out, a half login, both
  password forms at once, a missing password file, a login without TLS for another machine, the folder of the file
  transport, the digest hour, the time zone, the batch window, the poll interval, and the mail limit.
- A base address with http for the local machine and a login without TLS for the local machine are accepted.
- A password file is read and the trailing line break is removed.
- An inline password and a connection without TLS to another machine each give one warning that names the setting;
  good settings and settings that are switched off give none.
- No message and no warning contains the password.
- The worker is registered once with notifications enabled and the SMTP or the file sender, and not with notifications
  off, without a section, or with the sender `None`.
- The poll interval, the batch window, the digest hour and time zone, and the titles setting reach the worker.

#### Test Scenarios

**NotificationOptions_Defaults_AreOffAndDocumented**: A new settings object is checked for notifications off, no
transport, digest hour 8, time zone UTC, batch window 300 seconds, poll interval 60 seconds, titles included, SMTP port
587, and the TLS mode that upgrades the connection. This scenario is tested by
`NotificationOptions_Defaults_AreOffAndDocumented`.

**NotificationOptions_Bind_ReadsAllSections**: An in-memory configuration with values for both transports and the digest
hour is bound to the settings; all values arrive.
This scenario is tested by `NotificationOptions_Bind_ReadsAllSections`.

**NotificationOptionsValidator_Disabled_AcceptsAnything**: Switched-off settings with an undefined transport and an
impossible digest hour are accepted. This scenario is tested by `NotificationOptionsValidator_Disabled_AcceptsAnything`.

**NotificationOptionsValidator_CompleteSmtpSettings_AreAccepted**: Complete SMTP settings are accepted. This scenario is
tested by `NotificationOptionsValidator_CompleteSmtpSettings_AreAccepted`.

**NotificationOptionsValidator_CompleteFileSettings_AreAccepted**: Complete file settings are accepted. This scenario is
tested by `NotificationOptionsValidator_CompleteFileSettings_AreAccepted`.

**NotificationOptionsValidator_NoSender_IsRejected**: Enabled settings without a transport are rejected and the message
names `Notifications:Sender`. This scenario is tested by `NotificationOptionsValidator_NoSender_IsRejected`.

**NotificationOptionsValidator_MissingFromAddress_IsRejected**: An empty sender address is rejected and the message
names `Notifications:FromAddress`. This scenario is tested by
`NotificationOptionsValidator_MissingFromAddress_IsRejected`.

**NotificationOptionsValidator_FromAddressWithLineBreak_IsRejected**: A sender address that continues with a line break
and a header line is rejected. This scenario is tested by
`NotificationOptionsValidator_FromAddressWithLineBreak_IsRejected`.

**NotificationOptionsValidator_FromNameWithLineBreak_IsRejected**: A sender name with a line break is rejected and the
message names `Notifications:FromName`. This scenario is tested by
`NotificationOptionsValidator_FromNameWithLineBreak_IsRejected`.

**NotificationOptionsValidator_MissingBaseUrl_IsRejected**: An empty base address is rejected and the message names
`Notifications:BaseUrl`. This scenario is tested by `NotificationOptionsValidator_MissingBaseUrl_IsRejected`.

**NotificationOptionsValidator_RelativeBaseUrl_IsRejected**: A relative base address is rejected. This scenario is
tested by `NotificationOptionsValidator_RelativeBaseUrl_IsRejected`.

**NotificationOptionsValidator_BaseUrlWithExtras_IsRejected**: A theory over a base address with a query, with user
information, and with a fragment; each is rejected. This scenario is tested by
`NotificationOptionsValidator_BaseUrlWithExtras_IsRejected`.

**NotificationOptionsValidator_HttpBaseUrlForAnotherHost_IsRejected**: A base address with http for another machine is
rejected. This scenario is tested by `NotificationOptionsValidator_HttpBaseUrlForAnotherHost_IsRejected`.

**NotificationOptionsValidator_HttpBaseUrlForLoopback_IsAccepted**: A base address with http for `localhost` is
accepted. This scenario is tested by `NotificationOptionsValidator_HttpBaseUrlForLoopback_IsAccepted`.

**NotificationOptionsValidator_SmtpWithoutHost_IsRejected**: SMTP settings without a host are rejected and the message
names `Notifications:Smtp:Host`. This scenario is tested by `NotificationOptionsValidator_SmtpWithoutHost_IsRejected`.

**NotificationOptionsValidator_SmtpHostWithSpace_IsRejected**: A host with a space is rejected. This scenario is tested
by `NotificationOptionsValidator_SmtpHostWithSpace_IsRejected`.

**NotificationOptionsValidator_SmtpPortOutOfRange_IsRejected**: A theory over the ports 0, 65536 and -1; each is
rejected. This scenario is tested by `NotificationOptionsValidator_SmtpPortOutOfRange_IsRejected`.

**NotificationOptionsValidator_UnknownTlsMode_IsRejected**: A TLS mode that is not defined is rejected. This scenario is
tested by `NotificationOptionsValidator_UnknownTlsMode_IsRejected`.

**NotificationOptionsValidator_SmtpTimeoutOutOfRange_IsRejected**: A theory over the time-outs 4 and 121 seconds;
each is
rejected. This scenario is tested by `NotificationOptionsValidator_SmtpTimeoutOutOfRange_IsRejected`.

**NotificationOptionsValidator_UsernameWithoutPassword_IsRejected**: A user name without a password is rejected. This
scenario is tested by `NotificationOptionsValidator_UsernameWithoutPassword_IsRejected`.

**NotificationOptionsValidator_PasswordWithoutUsername_IsRejected**: A password without a user name is rejected, the
message names `Notifications:Smtp:Username`, and it does not contain the password. This scenario is tested by
`NotificationOptionsValidator_PasswordWithoutUsername_IsRejected`.

**NotificationOptionsValidator_PasswordAndPasswordFile_IsRejected**: An inline password together with a password file is
rejected, and the message does not contain the password. This scenario is tested by
`NotificationOptionsValidator_PasswordAndPasswordFile_IsRejected`.

**NotificationOptionsValidator_MissingPasswordFile_IsRejected**: A password file that does not exist is rejected and the
message names `Notifications:Smtp:PasswordFile`. This scenario is tested by
`NotificationOptionsValidator_MissingPasswordFile_IsRejected`.

**NotificationOptions_ResolvePassword_FromFile_ReturnsTrimmedPassword**: A password file with a trailing line break is
accepted by the check and resolved; the password has no line break. This scenario is tested by
`NotificationOptions_ResolvePassword_FromFile_ReturnsTrimmedPassword`.

**NotificationOptionsValidator_CredentialsWithoutTlsToRemoteHost_AreRejected**: A login without TLS to another
machine is
rejected and the message names `Notifications:Smtp:Tls`. This scenario is tested by
`NotificationOptionsValidator_CredentialsWithoutTlsToRemoteHost_AreRejected`.

**NotificationOptionsValidator_CredentialsWithoutTlsToLoopback_AreAccepted**: A login without TLS to `127.0.0.1` is
accepted. This scenario is tested by `NotificationOptionsValidator_CredentialsWithoutTlsToLoopback_AreAccepted`.

**NotificationOptionsValidator_FileWithoutDirectory_IsRejected**: File settings without a folder are rejected and the
message names `Notifications:File:Directory`. This scenario is tested by
`NotificationOptionsValidator_FileWithoutDirectory_IsRejected`.

**NotificationOptionsValidator_DigestHourOutOfRange_IsRejected**: A theory over the hours -1 and 24; each is rejected.
This scenario is tested by `NotificationOptionsValidator_DigestHourOutOfRange_IsRejected`.

**NotificationOptionsValidator_UnknownTimeZone_IsRejected**: A time zone that does not exist is rejected. This scenario
is tested by `NotificationOptionsValidator_UnknownTimeZone_IsRejected`.

**NotificationOptionsValidator_BatchWindowOutOfRange_IsRejected**: A theory over the windows 29 and 3601 seconds;
each is
rejected. This scenario is tested by `NotificationOptionsValidator_BatchWindowOutOfRange_IsRejected`.

**NotificationOptionsValidator_PollSecondsOutOfRange_IsRejected**: A theory over the intervals 9 and 3601 seconds;
each is
rejected. This scenario is tested by `NotificationOptionsValidator_PollSecondsOutOfRange_IsRejected`.

**NotificationOptionsValidator_MaxMailsPerHourOutOfRange_IsRejected**: A theory over the limits 0 and 10001; each is
rejected. This scenario is tested by `NotificationOptionsValidator_MaxMailsPerHourOutOfRange_IsRejected`.

**NotificationOptions_GetWarnings_InlinePassword_WarnsWithoutValue**: An inline password gives exactly one warning that
names the setting, advises the password file, and does not contain the password. This scenario is tested by
`NotificationOptions_GetWarnings_InlinePassword_WarnsWithoutValue`.

**NotificationOptions_GetWarnings_NoTlsToRemoteHost_Warns**: No TLS to another machine gives exactly one warning that
names `Notifications:Smtp:Tls`. This scenario is tested by `NotificationOptions_GetWarnings_NoTlsToRemoteHost_Warns`.

**NotificationOptions_GetWarnings_GoodOrDisabled_IsEmpty**: Good settings give no warning, and settings with an inline
password that are switched off give none either. This scenario is tested by
`NotificationOptions_GetWarnings_GoodOrDisabled_IsEmpty`.

**NotificationOptionsValidator_Messages_NeverContainThePassword**: Settings with a password and a wrong port are
rejected for the port, and the message does not contain the password. This scenario is tested by
`NotificationOptionsValidator_Messages_NeverContainThePassword`.

**Worker registration scenarios**: The tests of the registration of the worker are in
`NotificationWorkerRegistrationTests.cs`. They build a real service provider with the real registration over an
in-memory configuration, the real dispatcher, the composer and the schedule, an in-memory notification store, and a
recording sender; the worker runs on a clock that only moves when a test moves it, so the poll interval, the batch
window and the digest hour are observed through the mails that the real wiring sends.

**Registered or not**: With notifications enabled and the SMTP or the file sender, exactly one hosted worker is
registered; with notifications off, with no `Notifications` section, and with the sender `None`, none is. These
scenarios are tested by `NotificationRegistration_AddNotificationWorker_EnabledSmtp_Registers`,
`NotificationRegistration_AddNotificationWorker_EnabledFile_Registers`,
`NotificationRegistration_AddNotificationWorker_Off_NotRegistered`,
`NotificationRegistration_AddNotificationWorker_NoSection_NotRegistered`, and
`NotificationRegistration_AddNotificationWorker_EnabledSenderNone_NotRegistered`.

**Settings in effect**: The worker passes at the configured poll interval and not before; the dispatcher waits the
configured batch window before it sends; the daily summary follows the configured time zone and hour; titles are left
out of the mail when they are not to be included and are part of it by default. These scenarios are tested by
`NotificationRegistration_AddNotificationWorker_PollSeconds_Honored`,
`NotificationRegistration_AddNotificationWorker_BatchWindow_Honored`,
`NotificationRegistration_AddNotificationWorker_ZoneAndHour_Honored`,
`NotificationRegistration_AddNotificationWorker_IncludeTitles_Honored`, and
`NotificationRegistration_AddNotificationWorker_IncludeTitlesDefault_TitleShown`.

**NotificationRegistration_AddNotificationWorker_NullArgument_Throws**: A missing service collection or configuration
raises `ArgumentNullException`. This scenario is tested by
`NotificationRegistration_AddNotificationWorker_NullArgument_Throws`.
