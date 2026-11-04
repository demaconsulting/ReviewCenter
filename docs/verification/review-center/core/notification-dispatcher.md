### NotificationDispatcher

#### Verification Approach

`NotificationDispatcher` is verified with unit tests defined in `NotificationDispatcherMaskingTests.cs`,
`NotificationDispatcherBaselineTests.cs`, `NotificationDispatcherEveryTests.cs`, `NotificationDispatcherDailyTests.cs`,
`NotificationDispatcherDedupeTests.cs`, `NotificationDispatcherRetryTests.cs`, `NotificationDispatcherCapTests.cs`,
`NotificationDispatcherStaleTests.cs`, and
`NotificationDispatcherIdleTests.cs`. All of them use one test world (`NotificationDispatcherWorld`) that builds the real
dispatcher, the real `NotificationComposer`, the real `DigestSchedule`, and the real `InboxService` over the in-memory
stand-ins that the other Core tests use (a fake review store, a fake inbox store, a fake user store, and a fake audit
store), plus a fake notification store and a recording fake sender that can be told to fail. The clock is a fake clock
that the test moves, so batch windows, daily slots, retry waits, and limits are tested without waiting. Counting
wrappers of the audit store and the inbox store show how often the audit sequence and the inbox are read. The tests
check the messages that the fake sender received (the recipient, the subject, the text, and the HTML), the state that
the fake notification store holds, and the result of the pass. No database, no network, and no real clock are used.

#### Test Environment

N/A - standard test environment (the in-memory stand-ins and the fake clock are part of the test code).

#### Acceptance Criteria

- All tests pass with zero failures.
- Only a user with a mode, a confirmed address, an enabled account, and no temporary password is mailed, and only at the
  confirmed address.
- The inbox is read as the recipient, no audit entry is written, answers of others stay hidden, and no detail text and
  no outcome reach a mail, also when a title holds a line break and a header.
- The backlog at the switch-on of a mode is recorded without a mail and without a log row, and news after it is mailed.
- A switch between Daily and Every before the baseline was recorded does not make the backlog news, and the first pass
  records it without a mail.
- In the mode Every the first new item starts a stored batch window, nothing is sent inside it, and one mail with the
  items that are still new goes out when it ends.
- In the mode Daily one summary goes out for each local date from the digest hour on, also after a downtime and on the
  days of a clock change, and an unused slot is consumed.
- An item in the same state is not mailed twice, a changed state is, the draft is no news, rising replies are news,
  an item that leaves and returns is mailed again, and the record is written only after a successful send or a
  give-up.
- A failed send waits 5 minutes, 15 minutes, 1 hour, 4 hours, and 12 hours, the sixth failure gives up, and a success
  resets the state.
- A mail beyond the limit of the server or of the user is deferred without a change of state, the row of a mail is
  reserved in the store before the send and released when the send fails or is cancelled, a mail that was sent keeps
  its row even when the commit turns out stale, and a sent mail has a log row of the kind `update` or `digest`.
- An idle pass reads one audit sequence, reads the inbox only when the sequence changed, a sweep is due, or a
  time-driven duty waits, a pass that failed for a user is followed by a full read, and a failure of one user does not
  stop the others.
- A question that is asked after the item to answer was mailed is mailed, and a mail that was prepared for an address
  or a mode that the user changed is neither sent nor recorded.
- A canceled pass raises a cancellation, and the result holds only counts.

#### Test Scenarios

**Recipients**: A user whose mode is None, a user without a confirmed address, a disabled user, and a user who must
change the password are not mailed, and a due mail goes to the confirmed address only. These scenarios are tested by
`NotificationDispatcher_RunPass_ModeNone_NothingSent`, `NotificationDispatcher_RunPass_NoConfirmedAddress_NothingSent`,
`NotificationDispatcher_RunPass_DisabledUser_NothingSent`, `NotificationDispatcher_RunPass_MustChangePassword_NothingSent`,
and `NotificationDispatcher_RunPass_Due_MessageToConfirmedAddress`.

**Own inbox**: The inbox is read as the recipient so that another user's review is not in the mail, the reads name the
identity of the recipient, the read is made as the person and never as an actor of an API token (a question that the
inbox shows only to the person is in the mail), the pass writes no audit entry, and a recipient whose own ballot is not
handed in gets no disputed item while a recipient who handed in does. These scenarios are tested by
`NotificationDispatcher_RunPass_Recipient_ReadsInboxAsRecipient`,
`NotificationDispatcher_RunPass_Recipient_ActorHasNoApiToken`,
`NotificationDispatcher_RunPass_Delivery_WritesNoAuditEntry`, and
`NotificationDispatcher_RunPass_HiddenBallotRecipient_NoDisputedItem`.

**Masking of the content**: A mail about an approved review and a mail about a rejected review say only that the review
was concluded, the detail text of a row, a comment, and a check text never reach a mail, a title with a line break and a
header can change neither the subject nor the address, the same holds for a project name and for a display name with
a line break, and the failed-checks row and the waiting-for-others row are not mailed. These scenarios are tested by
`NotificationDispatcher_RunPass_Concluded_MailHasNoOutcome`,
`NotificationDispatcher_RunPass_Rejected_MailHasNoOutcomeOrConclusionText`,
`NotificationDispatcher_RunPass_DetailText_NotInMail`,
`NotificationDispatcher_RunPass_TitleWithCrLfBcc_SubjectFixedAndAddressUnchanged`,
`NotificationDispatcher_RunPass_ProjectNameWithCrLfBcc_SubjectFixedAndAddressUnchanged`,
`NotificationDispatcher_RunPass_DisplayNameWithCrLfBcc_SubjectFixedAndAddressUnchanged`,
`NotificationDispatcher_RunPass_FailedChecksRow_NotMailed`, and `NotificationDispatcher_RunPass_WaitingForOthers_NotMailed`.

**Baseline**: The backlog of a user who switched a mode on is recorded as notified without a mail, the pending flag is
cleared, no log row is written, the digest day starts, an item that arrives later is mailed, and a pending baseline is
read although the audit sequence did not move. A user who switched from Daily to Every before the first pass recorded
the baseline still gets the backlog recorded without a mail, because the store keeps the pending baseline. These
scenarios are tested by
`NotificationDispatcher_RunPass_BaselinePending_RecordsBacklogNoMail`, `NotificationDispatcher_RunPass_BaselinePending_FlagCleared`,
`NotificationDispatcher_RunPass_Baseline_NoLogRow`, `NotificationDispatcher_RunPass_Baseline_DigestHandledNow`,
`NotificationDispatcher_RunPass_AfterBaseline_NewItemMailed`,
`NotificationDispatcher_RunPass_ModeSwitchedBeforeBaseline_BacklogNotMailed`, and
`NotificationDispatcher_RunPass_BaselinePendingSequenceUnchanged_ReadsInbox`.

**Batch window of the mode Every**: The first new item starts the window, nothing is sent inside it, a mail goes out
when it has elapsed, three items are one message, an item that arrives inside the window is included, an item that the
user handled is dropped, a window in which everything was handled sends nothing and is cleared, the window survives a
restart of the dispatcher, the configured length is honored, and a successful send clears the window. These scenarios
are tested by `NotificationDispatcher_RunPass_Every_FirstNewItem_StartsWindow`,
`NotificationDispatcher_RunPass_Every_InsideWindow_NothingSent`, `NotificationDispatcher_RunPass_Every_AfterWindow_Sends`,
`NotificationDispatcher_RunPass_Every_ThreeItems_OneMessage`, `NotificationDispatcher_RunPass_Every_ItemInWindow_Included`,
`NotificationDispatcher_RunPass_Every_ItemHandled_Dropped`,
`NotificationDispatcher_RunPass_Every_AllHandled_NoMailWindowCleared`,
`NotificationDispatcher_RunPass_Every_NewInstance_KeepsWindow`, `NotificationDispatcher_RunPass_Every_CustomWindow_Honored`,
and `NotificationDispatcher_RunPass_Every_AfterSend_WindowCleared`.

**Slot of the mode Daily**: No summary goes out before the digest hour, one goes out at the hour when something is new,
nothing is sent and the slot is used up when nothing is new, two passes on one local date give one mail, an item after
the slot waits for the next date, a downtime over the hour is made good as soon as the server runs, the hour is the
local hour of the zone, a day of a clock change gives one mail, the outstanding items are counted, a user who switched
from Every to Daily gets the next summary when the slot opens, and a user who just switched Daily on gets nothing on the
same date. These scenarios are tested by `NotificationDispatcher_RunPass_Daily_BeforeHour_NothingSent`,
`NotificationDispatcher_RunPass_Daily_AtHour_Sends`, `NotificationDispatcher_RunPass_Daily_NothingNew_NoMailSlotConsumed`,
`NotificationDispatcher_RunPass_Daily_TwoPassesSameDate_OneMail`,
`NotificationDispatcher_RunPass_Daily_ItemAfterSlot_SentNextDay`,
`NotificationDispatcher_RunPass_Daily_DowntimeOverHour_SendsOnRestart`,
`NotificationDispatcher_RunPass_Daily_ZoneOffset_UsesLocalHour`, `NotificationDispatcher_RunPass_Daily_DstChangeDay_OneMail`,
`NotificationDispatcher_RunPass_Daily_OutstandingCounted`, `NotificationDispatcher_RunPass_SwitchEveryToDaily_DueWhenSlotOpen`,
and `NotificationDispatcher_RunPass_DailyJustEnabled_NoMailSameDate`.

**No second mail**: The same item in the same state is not mailed twice, a changed state is mailed, "Not started" and
"Draft" are the same state, rising replies are mailed and falling replies are not, an item that left the inbox and came
back is mailed again, the key of an item that left is forgotten, a review that was concluded and is reopened is mailed
again, the items are recorded after a successful send and are not recorded after a failed one, and an update that the
user saw before the send is dropped. These scenarios are tested by
`NotificationDispatcher_RunPass_SameItemSameState_NotMailedTwice`, `NotificationDispatcher_RunPass_StateChanged_Mailed`,
`NotificationDispatcher_RunPass_NotStartedThenDraft_NotNew`, `NotificationDispatcher_RunPass_RepliesRise_Mailed`,
`NotificationDispatcher_RunPass_RepliesFall_NotMailed`, `NotificationDispatcher_RunPass_ItemLeavesAndReturns_Mailed`,
`NotificationDispatcher_RunPass_ItemLeaves_KeyRemoved`, `NotificationDispatcher_RunPass_ConcludedThenReopened_BothMailed`,
`NotificationDispatcher_RunPass_SendSucceeds_ItemsRecorded`, `NotificationDispatcher_RunPass_SendFails_ItemsNotRecorded`,
`NotificationDispatcher_RunPass_SendInProgress_ItemsNotYetRecorded` (the items are not recorded while the send is still
running; the log row is already reserved then), and `NotificationDispatcher_RunPass_UpdateSeenBeforeSend_Dropped`.

**Question and standing item**: The inbox returns a standing item to answer and a question for the user in the same
section of one review. A question that is asked after the item to answer was mailed is mailed, and when both were mailed
in the same state neither is mailed again. These scenarios are tested by
`NotificationDispatcher_RunPass_QuestionAfterToAnswerNotified_Mailed` and
`NotificationDispatcher_RunPass_QuestionAndToAnswerNotified_NotMailedAgain`.

**Concurrent change of the preference** (`NotificationDispatcherStaleTests.cs`): The fake store can change the
preference of a user at a chosen point of a pass. When the address is replaced before the send, nothing is sent to the
old address, and the next pass mails the new address. When the address is replaced during the send, no item is
recorded, and the reserved log row of the mail that went out stays. When the mode is switched off and on again during
the send, no item is recorded, and the log row stays. When the mode is switched
off before the send, nothing is sent. These scenarios are tested by
`NotificationDispatcher_RunPass_AddressReplacedBeforeSend_NothingSent`,
`NotificationDispatcher_RunPass_AddressReplacedBeforeSend_NextPassMailsNewAddress`,
`NotificationDispatcher_RunPass_AddressReplacedDuringSend_NothingRecorded`,
`NotificationDispatcher_RunPass_ModeFlippedDuringSend_NothingRecorded`, and
`NotificationDispatcher_RunPass_ModeSwitchedOffBeforeSend_NothingSent`.

**Retry**: A failed send stores the failure count and the time of the next attempt, the five waits are 5 minutes, 15
minutes, 1 hour, 4 hours, and 12 hours, no attempt is made before the next attempt time, a retry uses the inbox as it is
then, the sixth failure gives up and records the items, a successful retry clears the failure state, and a failed send
to one user does not keep another user from being mailed. These scenarios are tested by
`NotificationDispatcher_RunPass_SendFails_FailureCountStored`, `NotificationDispatcher_RunPass_SendFails_NextAttemptStored`,
`NotificationDispatcher_RunPass_Failure1_Waits5Min`,
`NotificationDispatcher_RunPass_Failure2_Waits15Min`, `NotificationDispatcher_RunPass_Failure3_Waits1Hour`,
`NotificationDispatcher_RunPass_Failure4_Waits4Hours`, `NotificationDispatcher_RunPass_Failure5_Waits12Hours`,
`NotificationDispatcher_RunPass_BeforeNextAttempt_NoSend`, `NotificationDispatcher_RunPass_Retry_UsesCurrentInbox`,
`NotificationDispatcher_RunPass_Failure6_GivesUp`, `NotificationDispatcher_RunPass_GiveUp_ItemsRecorded`,
`NotificationDispatcher_RunPass_RetrySucceeds_StateReset`, and `NotificationDispatcher_RunPass_OneSendFails_OtherUserMailed`.

**Limits and log**: A mail is deferred when the hourly limit of the server is reached, mails of every kind count against
it, a mail is deferred when the user had 24 mails in the last day, a deferred item stays new and is mailed when the limit
allows it, a deferral leaves the failure count and the next attempt as they were, and a sent mail writes a log row of
the kind `update` in the mode Every and `digest` in the mode Daily. The row is reserved in the store before the send:
while the sender runs, the row is visible from inside the sender; a refused reservation sends nothing and changes
nothing; a failed send and a cancelled send leave no row (the cancelled pass still raises the cancellation); and a
mail that was sent keeps its row. The fake store models the same rules as the real one with the shared `SendLimits`
decision. These scenarios are tested by
`NotificationDispatcher_RunPass_GlobalCapReached_Deferred`, `NotificationDispatcher_RunPass_ConfirmationMailsCounted`,
`NotificationDispatcher_RunPass_UserCapReached_Deferred`, `NotificationDispatcher_RunPass_Deferred_SentAfterWindowOfCap`,
`NotificationDispatcher_RunPass_Deferred_FailureCountUnchanged`, `NotificationDispatcher_RunPass_EverySent_LogKindUpdate`,
`NotificationDispatcher_RunPass_DailySent_LogKindDigest`,
`NotificationDispatcher_RunPass_SendInProgress_ReservationExists`,
`NotificationDispatcher_RunPass_ReservationRefused_NothingChanges`,
`NotificationDispatcher_RunPass_SendFails_ItemsNotRecorded`, and
`NotificationDispatcher_RunPass_SendCancelled_ReservationReleased`.

**Idle cost**: Without a recipient the audit sequence is not read, the first pass reads the inbox, an unchanged sequence
causes no inbox read, a changed sequence does, 15 minutes without a change cause one, an elapsed window is served
although the sequence did not move, and a pass in which a user failed does not remember the sequence or the time of
the sweep, so the next pass reads again even when the sequence is unchanged and no sweep is due, a retry wait is still
honored in that pass, and a clean pass after a failed one makes the next idle pass read nothing. The reset is proved
by two scenarios: a failed pass after a change of the audit trail, and a failed pass that was a time-driven sweep
(a clean pass, the sweep interval elapses with no audit change, one inbox fails, the failure is cleared, and one
minute later both inboxes are read again). These scenarios
are tested by `NotificationDispatcher_RunPass_NoRecipients_NoSequenceRead`, `NotificationDispatcher_RunPass_First_ReadsInbox`,
`NotificationDispatcher_RunPass_SequenceUnchanged_NoInboxRead`, `NotificationDispatcher_RunPass_SequenceChanged_ReadsInbox`,
`NotificationDispatcher_RunPass_15MinutesNoChange_ReadsInbox`,
`NotificationDispatcher_RunPass_WindowElapsedSequenceUnchanged_Sends`,
`NotificationDispatcher_RunPass_PassFails_NextPassReads`,
`NotificationDispatcher_RunPass_FailedAfterCleanPass_NextPassReadsAgain`,
`NotificationDispatcher_RunPass_FailedSweepAfterCleanPass_NextPassReadsAgain`,
`NotificationDispatcher_RunPass_FullReadAfterFailedPass_RetryWaitHonored`, and
`NotificationDispatcher_RunPass_CleanAfterFailedPass_NextPassIdle`.

**Resilience and result**: A failure of the inbox of one user does not keep another user from being mailed and is passed
to the error callback, a canceled pass raises a cancellation, and the result counts the sent, failed, and deferred mails
and holds no address. These scenarios are tested by `NotificationDispatcher_RunPass_InboxFails_OtherUserMailed`,
`NotificationDispatcher_RunPass_InboxFails_CallbackInvoked`, `NotificationDispatcher_RunPass_Cancelled_Throws`, and
`NotificationDispatcher_RunPass_Mixed_ResultCounts`, and `NotificationDispatcher_RunPass_Sent_ResultHoldsNoAddress`.
