### NotificationPreferenceService

#### Verification Approach

`NotificationPreferenceService` is verified with unit tests defined in `NotificationPreferenceServiceTests.cs`. The
tests use the real service with an in-memory stand-in for the store (`FakeNotificationStore`), a stand-in for the
sender that records the mails and gives back the last code (`FakeNotificationSender`), a real user service over an
in-memory user store, and a movable clock, so that the lifetime of a code, the pause, and the daily limits are
tested without waiting. Because the code is never stored, the tests read it from the mail that the stand-in sender
received. The atomic behavior of the real store, such as the exactly one winner under concurrency, is verified by the
`NotificationStore` tests.

#### Test Environment

N/A - standard test environment (no database and no mail server are needed).

#### Acceptance Criteria

- All tests pass with zero failures.
- A valid address gets one mail with a six digit code, in lower case, and is stored as pending with a salted hash only.
- The mail names the instance, states the lifetime of 30 minutes, says that it can be ignored, holds no link, and
  encodes the instance name in the HTML part.
- The right code, also with white space around it, confirms the address once; a wrong code, an expired code, a code
  without a pending address, and a code that was already used are refused.
- The sixth attempt is refused even with the right code.
- An invalid address is refused and nothing is sent.
- A second request within 60 seconds, a fourth request of a user within a day, a fourth request to an address within
  a day, and a request over the hourly limit of the server are refused; a new code after the pause stops the old
  code from working.
- A failed send takes the reservation back and the person gets a clear failure; a canceled send or an unexpected
  sender failure also takes the reservation back and the exception is passed on unchanged.
- A confirmed address stays in use while a new address is pending, and only a confirmed address is given for mail.
- Removing an address reports whether one existed.
- Each change is audited, and no audit entry holds the address or the code.
- An actor with an API token, a feature that is off, a sender that is not configured, a disabled or unknown user, a
  user with a temporary password, and a missing actor are refused.
- A user who never chose a mode has the mode `None`; the status shows the stored mode.
- A mode (`None`, `Daily`, or `Every`) is stored and audited by its actor; a mode other than `None` is refused while no
  address is confirmed, a value outside the enumeration is refused, and an API token actor, a disabled user, a user
  with a temporary password, a missing actor, and a feature that is not available are refused too.
- An audit entry of a mode change holds the mode and no address; choosing the mode that is stored changes nothing and
  writes no entry.
- Switching a mode on from `None` marks the baseline as pending; switching between `Daily` and `Every` does not;
  removing the address returns the user to `None`.

#### Test Scenarios

**NotificationPreferenceService_RequestConfirmation_ValidAddress_MailsCodeAndStoresPending**: A valid address in mixed
case gets one mail to the lower case address with a code of six digits; the address is pending and its code has not
expired. This scenario is tested by
`NotificationPreferenceService_RequestConfirmation_ValidAddress_MailsCodeAndStoresPending`.

**NotificationPreferenceService_RequestConfirmation_Mail_HasNameValidityAndNoLink**: The subject starts with the name
of the instance, both parts of the mail give the lifetime of 30 minutes and the hint to ignore the mail, and neither
part holds a link. This scenario is tested by
`NotificationPreferenceService_RequestConfirmation_Mail_HasNameValidityAndNoLink`.

**NotificationPreferenceService_RequestConfirmation_NameWithMarkup_IsEncodedInHtml**: An instance name with markup
appears encoded in the HTML part. This scenario is tested by
`NotificationPreferenceService_RequestConfirmation_NameWithMarkup_IsEncodedInHtml`.

**NotificationPreferenceService_Confirm_RightCode_ConfirmsAddress**: The code typed with spaces around it confirms the
address; nothing is pending, and the address is the one given for mail. This scenario is tested by
`NotificationPreferenceService_Confirm_RightCode_ConfirmsAddress`.

**NotificationPreferenceService_Confirm_WrongCode_ThrowsAndStaysPending**: A wrong code raises the rule for an
incorrect code and the address stays pending. This scenario is tested by
`NotificationPreferenceService_Confirm_WrongCode_ThrowsAndStaysPending`.

**NotificationPreferenceService_Confirm_AfterMaxWrongAttempts_RefusesTheRightCode**: After five wrong codes the right
code is refused with the rule for too many attempts. This scenario is tested by
`NotificationPreferenceService_Confirm_AfterMaxWrongAttempts_RefusesTheRightCode`.

**NotificationPreferenceService_Confirm_AfterLifetime_ThrowsExpired**: After the lifetime of the code the right code
is refused as expired and the status reports the expiry. This scenario is tested by
`NotificationPreferenceService_Confirm_AfterLifetime_ThrowsExpired`.

**NotificationPreferenceService_Confirm_NothingPending_Throws**: A code without a pending address raises the rule for
nothing to confirm. This scenario is tested by `NotificationPreferenceService_Confirm_NothingPending_Throws`.

**NotificationPreferenceService_Confirm_SameCodeTwice_SecondIsRefused**: After the code confirmed the address, the same
code is refused. This scenario is tested by `NotificationPreferenceService_Confirm_SameCodeTwice_SecondIsRefused`.

**NotificationPreferenceService_RequestConfirmation_InvalidAddress_ThrowsAndSendsNothing**: Invalid texts raise the
rule for an invalid address and no mail is sent. This scenario is tested by
`NotificationPreferenceService_RequestConfirmation_InvalidAddress_ThrowsAndSendsNothing`.

**NotificationPreferenceService_RequestConfirmation_WithinCooldown_ThrowsTooSoon**: A second request within the pause
raises the rule for too soon and sends no second mail. This scenario is tested by
`NotificationPreferenceService_RequestConfirmation_WithinCooldown_ThrowsTooSoon`.

**NotificationPreferenceService_RequestConfirmation_AfterCooldown_MailsNewCode**: After the pause a second request mails
a second code to the same address, and the address is still pending. This scenario is tested by
`NotificationPreferenceService_RequestConfirmation_AfterCooldown_MailsNewCode`.

**NotificationPreferenceService_RequestConfirmation_AfterCooldown_OldCodeStopsWorking**: After the pause a new code is
mailed, the old code is refused, and the new code works. This scenario is tested by
`NotificationPreferenceService_RequestConfirmation_AfterCooldown_OldCodeStopsWorking`.

**NotificationPreferenceService_RequestConfirmation_UserDailyLimit_ThrowsLimitReached**: A fourth request of one user
within a day raises the rule for a reached limit, and only three mails were sent. This scenario is tested by
`NotificationPreferenceService_RequestConfirmation_UserDailyLimit_ThrowsLimitReached`.

**NotificationPreferenceService_RequestConfirmation_AfterDayPassed_MailsAgain**: After the limit was reached and a day
passed, a request mails the new address again and that address is pending. This scenario is tested by
`NotificationPreferenceService_RequestConfirmation_AfterDayPassed_MailsAgain`.

**NotificationPreferenceService_RequestConfirmation_AddressDailyLimit_ThrowsLimitReached**: A fourth request for one
address, made by another user, raises the rule for a reached limit. This scenario is tested by
`NotificationPreferenceService_RequestConfirmation_AddressDailyLimit_ThrowsLimitReached`.

**NotificationPreferenceService_RequestConfirmation_ServerHourlyLimit_ThrowsLimitReached**: With a small hourly limit
of the server, the request over the limit raises the rule for a reached limit. This scenario is tested by
`NotificationPreferenceService_RequestConfirmation_ServerHourlyLimit_ThrowsLimitReached`.

**NotificationPreferenceService_RequestConfirmation_SendFails_RollsBackAndThrows**: When the sender fails, the person
gets the rule for a failed send, nothing is pending, and the failed mail does not count against the limits. This
scenario is tested by `NotificationPreferenceService_RequestConfirmation_SendFails_RollsBackAndThrows`.

**NotificationPreferenceService_RequestConfirmation_CanceledOrUnexpectedSendFailure_RollsBackAndRethrows**: When the
sender throws a cancellation or an unexpected exception, the same exception reaches the caller, nothing is pending, the
failed mail does not count against the limits, and an immediate new request succeeds. This scenario is tested by
`NotificationPreferenceService_RequestConfirmation_CanceledOrUnexpectedSendFailure_RollsBackAndRethrows`.

**NotificationPreferenceService_RequestConfirmation_NewAddress_KeepsConfirmedAddressUntilConfirmed**: With a confirmed
address, a new address is pending while the confirmed one stays in use until the new one is confirmed. This scenario
is tested by `NotificationPreferenceService_RequestConfirmation_NewAddress_KeepsConfirmedAddressUntilConfirmed`.

**NotificationPreferenceService_GetConfirmedAddress_OnlyPending_ReturnsNull**: An address that is only pending is not
given for mail. This scenario is tested by `NotificationPreferenceService_GetConfirmedAddress_OnlyPending_ReturnsNull`.

**NotificationPreferenceService_RemoveAddress_RemovesAddressAndReportsIt**: Removing reports true and the address is
gone; removing again reports false. This scenario is tested by
`NotificationPreferenceService_RemoveAddress_RemovesAddressAndReportsIt`.

**NotificationPreferenceService_Changes_AreAuditedWithoutAddressOrCode**: Entering, confirming, and removing an address
write the three audit entries in order with the right actor, and no entry contains the address or the code. This
scenario is tested by `NotificationPreferenceService_Changes_AreAuditedWithoutAddressOrCode`.

**NotificationPreferenceService_RequestConfirmation_StoresOnlyASaltedHashOfTheCode**: The stored hash is 64 lower case
hex characters, differs from the code, comes with a salt, and the expiry is the time of the request plus the lifetime.
This scenario is tested by `NotificationPreferenceService_RequestConfirmation_StoresOnlyASaltedHashOfTheCode`.

**NotificationPreferenceService_Actions_ApiTokenActor_AreRefused**: Every action of an actor with an API token is
refused with the rule for not available. This scenario is tested by
`NotificationPreferenceService_Actions_ApiTokenActor_AreRefused`.

**NotificationPreferenceService_Actions_FeatureOff_AreRefusedAndTouchNoStore**: With notifications switched off, each of
the four actions (request, confirm, remove, status) is refused with the rule for not available, no mail is sent, and the
store is not used. This scenario is tested by
`NotificationPreferenceService_Actions_FeatureOff_AreRefusedAndTouchNoStore`.

**NotificationPreferenceService_IsAvailable_SenderNotConfigured_IsFalse**: With a sender that is not configured the
service reports that it is not available, and each of the four actions is refused with the rule for not available. This
scenario is tested by `NotificationPreferenceService_IsAvailable_SenderNotConfigured_IsFalse`.

**NotificationPreferenceService_Actions_DisabledOrUnknownUser_AreRefused**: Each of the four actions of a disabled user
and of an unknown user is refused with the rule for an actor that is not allowed. This scenario is tested by
`NotificationPreferenceService_Actions_DisabledOrUnknownUser_AreRefused`.

**NotificationPreferenceService_Actions_TemporaryPassword_AreRefused**: Each of the four actions of a user who must
change a temporary password is refused with the rule for not available. This scenario is tested by
`NotificationPreferenceService_Actions_TemporaryPassword_AreRefused`.

**NotificationPreferenceService_Actions_NullActor_ThrowsArgumentNull**: A missing actor raises `ArgumentNullException`
in each of the four actions. This scenario is tested by
`NotificationPreferenceService_Actions_NullActor_ThrowsArgumentNull`.

**Mode scenarios**: The tests of the mode are in `NotificationPreferenceServiceModeTests.cs`; they use the same
stand-ins.

**NotificationPreferenceService_GetStatus_NoRow_ModeNone** and
**NotificationPreferenceService_GetStatus_ModeDaily_ShowsDaily**: A user without a row has the mode `None`, and the
status shows a stored mode. These scenarios are tested by `NotificationPreferenceService_GetStatus_NoRow_ModeNone` and
`NotificationPreferenceService_GetStatus_ModeDaily_ShowsDaily`.

**NotificationPreferenceService_SetMode_Daily_StoresDaily**, **..._Every_StoresEvery**, and **..._None_StoresNone**:
Each of the three modes is stored for a user with a confirmed address. These scenarios are tested by
`NotificationPreferenceService_SetMode_Daily_StoresDaily`, `NotificationPreferenceService_SetMode_Every_StoresEvery`,
and `NotificationPreferenceService_SetMode_None_StoresNone`.

**NotificationPreferenceService_SetMode_NoConfirmedAddress_Throws**: Without a confirmed address a mode other than
`None` raises the rule `NotificationAddressNotConfirmed`. This scenario is tested by
`NotificationPreferenceService_SetMode_NoConfirmedAddress_Throws`.

**SetMode refusals**: An API token actor, a feature that is not available, a disabled user, a user with a temporary
password, and a value outside the enumeration are refused, and a missing actor raises `ArgumentNullException`. These
scenarios are tested by `NotificationPreferenceService_SetMode_ApiTokenActor_Throws`,
`NotificationPreferenceService_SetMode_NotAvailable_Throws`,
`NotificationPreferenceService_SetMode_NotAvailable_StoreNotTouched`,
`NotificationPreferenceService_SetMode_DisabledUser_Throws`,
`NotificationPreferenceService_SetMode_MustChangePassword_Throws`,
`NotificationPreferenceService_SetMode_UndefinedValue_Throws`, and
`NotificationPreferenceService_SetMode_NullActor_ThrowsArgumentNull`.

**Audit of the mode**: A changed mode is audited by its actor, the text holds the mode and no address, and an unchanged
mode (also `None` without a row) writes nothing. These scenarios are tested by
`NotificationPreferenceService_SetMode_Changed_AuditEntryWritten`,
`NotificationPreferenceService_SetMode_Changed_AuditHasNoAddress`,
`NotificationPreferenceService_SetMode_Same_NoAuditNoChange`,
`NotificationPreferenceService_SetMode_Same_ReportsNoChange`, and
`NotificationPreferenceService_SetMode_NoneWithoutRow_NoChange`.

**Baseline and removal**: Switching on from `None` marks the baseline pending, switching between `Daily` and `Every`
does not, and removing the address returns the mode to `None`. These scenarios are tested by
`NotificationPreferenceService_SetMode_NoneToEvery_BaselinePending`,
`NotificationPreferenceService_SetMode_DailyToEvery_NoBaseline`,
`NotificationPreferenceService_SetMode_DailyToEvery_ItemsKept`, and
`NotificationPreferenceService_RemoveAddress_AfterMode_ModeNone`.
