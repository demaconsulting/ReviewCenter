### NotificationStore

#### Verification Approach

`NotificationStore` is verified with unit tests defined in `NotificationStoreTests.cs`. The tests use the real store over
a real SQLite database file in a temporary folder (`TestStore`), add the users to the users table directly, and read
the rows and the audit entries back with a second context. A real database is used because the reservation with its
limits, the counting of attempts, and the confirmation must have exactly one winner, which only the database can
show; parallel requests (eight for the reservation, ten for the attempts, and several for the confirmation) are
released together to prove it. The tests of the `ReviewCenterDbContext` model for the two tables are in the same file
and are described in the verification of that unit. How the service uses the store is verified by the
`NotificationPreferenceService` tests over an in-memory stand-in.

#### Test Environment

N/A - standard test environment (a temporary SQLite database file is created by each test and removed afterwards).

#### Acceptance Criteria

- All tests pass with zero failures.
- A new database reports the store as available, and a database without the tables reports it as not available; any
  other database failure propagates to the caller.
- A user without a row has no preference.
- A reservation stores the pending address, the hash, the salt, the expiry, the send time, and zero attempts, adds a log
  entry, and writes one audit entry; a confirmed address is kept and the attempts are reset.
- A reservation within the pause, over the limit of the user, over the limit of the address, or over the limit of the
  server is refused and changes nothing; the limit of the user ends after a day.
- Of eight parallel reservations for one user exactly one is reserved, the others are refused as too soon, and one log
  entry exists.
- An abort removes the log entry and the pending fields, and leaves a row with another pending hash alone.
- Attempts are counted up to the limit for the current code only; of ten parallel attempts with a limit of five exactly
  five succeed.
- A confirmation with the right hash moves the pending address to the confirmed one and writes one audit entry; another
  hash changes nothing and writes no entry; a code at the moment it expires, or after it, changes nothing and writes
  no entry, and a code one tick before its expiry is still confirmed; of parallel confirmations exactly one wins.
- Removing deletes the row with one audit entry, removing again records nothing, and the send log is kept.
- A reservation deletes the log entries that are older than one day and keeps the newer ones.
- The mode is stored with its audit entry in one transaction; `None` deletes the notified items; an unchanged mode
  writes nothing; a new row reads as `None`.
- Only users with a mode and a confirmed address are listed as recipients, with their flags and dispatch state.
- A commit of a mail is atomic, writes no log row, is refused for a user without a mode or a row, or whose address or
  generation is not the current one, keeps one row per key and kind, and prunes old log rows; the mails sent are
  counted per user and for all users.
- A reservation of a mail is refused at the limit of the server (rows of every kind and user in the last hour) and at
  the limit of the user (rows of every kind in the last day), is shared with the confirmation mails, lets only one of
  two parallel reservations pass at the limit minus one, and is freed by a release.
- A switch between `Daily` and `Every` keeps a pending baseline pending, and `None` clears it.
- The generation rises with a change of the mode and with a confirmed address, and the items go away with their review.
- A database without the table of notified items, the mode column, or the generation column is reported as not
  available.

#### Test Scenarios

**NotificationStore_IsAvailableAsync_NewDatabase_ReturnsTrue**: In a new database the store reports that it is
available. This scenario is tested by `NotificationStore_IsAvailableAsync_NewDatabase_ReturnsTrue`.

**NotificationStore_IsAvailableAsync_TablesMissing_ReturnsFalse**: After the tables are dropped, the store reports that
it is not available. This scenario is tested by `NotificationStore_IsAvailableAsync_TablesMissing_ReturnsFalse`.

**NotificationStore_IsAvailableAsync_OtherDatabaseFailure_Propagates**: The database file is replaced by a file that is
not a database, so the check fails with an error other than a missing table. The store raises the database exception
instead of answering false. This scenario is tested by
`NotificationStore_IsAvailableAsync_OtherDatabaseFailure_Propagates`.

**NotificationStore_GetAsync_NoRow_ReturnsNull**: A user without a row has no preference. This scenario is tested by
`NotificationStore_GetAsync_NoRow_ReturnsNull`.

**NotificationStore_ReserveConfirmationAsync_Reserved_StoresPendingLogAndAudit**: A reservation stores the pending
fields, adds a log entry with the address and the time, and writes one `notification.settings-changed` entry. This
scenario is tested by `NotificationStore_ReserveConfirmationAsync_Reserved_StoresPendingLogAndAudit`.

**NotificationStore_ReserveConfirmationAsync_OldLogEntries_AreDeleted**: With one log entry that is 25 hours old and
one that is 1 hour old, a reservation deletes the old entry and keeps the recent one and the new one. This scenario is
tested by `NotificationStore_ReserveConfirmationAsync_OldLogEntries_AreDeleted`.

**NotificationStore_ReserveConfirmationAsync_AfterConfirmed_KeepsConfirmedAndResetsAttempts**: With a confirmed address
and a counted attempt, a reservation for a new address keeps the confirmed address and resets the attempts. This
scenario is tested by `NotificationStore_ReserveConfirmationAsync_AfterConfirmed_KeepsConfirmedAndResetsAttempts`.

**NotificationStore_ReserveConfirmationAsync_WithinCooldown_ReturnsTooSoon**: A second reservation within the pause is
refused as too soon; the pending hash, the log, and the audit entries are as before. This scenario is tested by
`NotificationStore_ReserveConfirmationAsync_WithinCooldown_ReturnsTooSoon`.

**NotificationStore_ReserveConfirmationAsync_UserLimit_ReturnsUserLimitReached**: The reservation over the limit of the
user is refused, and one after a day is reserved. This scenario is tested by
`NotificationStore_ReserveConfirmationAsync_UserLimit_ReturnsUserLimitReached`.

**NotificationStore_ReserveConfirmationAsync_AddressLimit_ReturnsAddressLimitReached**: The reservation of another user
for an address over its limit is refused and stores nothing. This scenario is tested by
`NotificationStore_ReserveConfirmationAsync_AddressLimit_ReturnsAddressLimitReached`.

**NotificationStore_ReserveConfirmationAsync_ServerLimit_ReturnsGlobalLimitReached**: The reservation over the hourly
limit of the server is refused. This scenario is tested by
`NotificationStore_ReserveConfirmationAsync_ServerLimit_ReturnsGlobalLimitReached`.

**NotificationStore_ReserveConfirmationAsync_Concurrent_HasExactlyOneWinner**: Eight parallel reservations for one user
give one reserved and seven too soon, with one log entry. This scenario is tested by
`NotificationStore_ReserveConfirmationAsync_Concurrent_HasExactlyOneWinner`.

**NotificationStore_AbortConfirmationAsync_RemovesLogAndPending**: An abort removes the log entry and clears the
pending fields. This scenario is tested by `NotificationStore_AbortConfirmationAsync_RemovesLogAndPending`.

**NotificationStore_AbortConfirmationAsync_OtherHash_LeavesPendingAlone**: An abort that names another hash leaves the
pending fields in place. This scenario is tested by `NotificationStore_AbortConfirmationAsync_OtherHash_LeavesPendingAlone`.

**NotificationStore_TryCountAttemptAsync_CountsUpToTheLimitForTheCurrentCode**: Attempts for another hash are refused,
the first two for the current hash are counted, and the third over a limit of two is refused. This scenario is tested
by `NotificationStore_TryCountAttemptAsync_CountsUpToTheLimitForTheCurrentCode`.

**NotificationStore_TryCountAttemptAsync_Concurrent_NeverExceedsTheLimit**: Ten parallel attempts with a limit of five
give exactly five successes and a count of five. This scenario is tested by
`NotificationStore_TryCountAttemptAsync_Concurrent_NeverExceedsTheLimit`.

**NotificationStore_ConfirmAsync_MovesPendingToConfirmedWithAudit**: Confirming moves the pending address to the
confirmed one, clears the pending fields, and writes one `notification.email-confirmed` entry. This scenario is tested
by `NotificationStore_ConfirmAsync_MovesPendingToConfirmedWithAudit`.

**NotificationStore_ConfirmAsync_OtherCode_ChangesNothing**: A confirmation with another hash returns false, leaves the
address unconfirmed, and writes no entry. This scenario is tested by `NotificationStore_ConfirmAsync_OtherCode_ChangesNothing`.

**NotificationStore_ConfirmAsync_ExpiredCode_ChangesNothing**: With a code that lives for 30 minutes, a confirmation at
exactly 30 minutes and one at 31 minutes (a theory with both values) return false, leave the address unconfirmed and
the pending fields in place, and write no entry. This scenario is tested by
`NotificationStore_ConfirmAsync_ExpiredCode_ChangesNothing`.

**NotificationStore_ConfirmAsync_LastInstantBeforeExpiry_Confirms**: A confirmation one tick before the code expires
returns true and the address is confirmed. This scenario is tested by
`NotificationStore_ConfirmAsync_LastInstantBeforeExpiry_Confirms`.

**NotificationStore_ConfirmAsync_Concurrent_HasExactlyOneWinner**: Parallel confirmations with the same hash give one
success and one audit entry. This scenario is tested by `NotificationStore_ConfirmAsync_Concurrent_HasExactlyOneWinner`.

**NotificationStore_RemoveAsync_DeletesRowWithAuditOnce**: Removing deletes the row with one `notification.email-removed`
entry; a second removal returns false. This scenario is tested by `NotificationStore_RemoveAsync_DeletesRowWithAuditOnce`.

**NotificationStore_RemoveAsync_KeepsTheSendLog**: After a removal the send log entry is still there. This scenario is
tested by `NotificationStore_RemoveAsync_KeepsTheSendLog`.

**Mode and dispatch scenarios**: The tests of the mode, the recipients, the notified items, and the commit of a mail
are in `NotificationStoreModeTests.cs`, over the same real database.

**Mode**: A new row reads as `None`, with no pending baseline and an empty dispatch state; the mode is read back by a
new store instance; a failing audit write leaves the mode unchanged; switching on from `None` sets the baseline flag
and clears the batch window and the failure state; switching between `Daily` and `Every` keeps the items, leaves a
recorded baseline recorded (flag clear), and keeps a pending baseline pending (flag set); `None` deletes the notified
items and the daily slot memory and clears a pending baseline; a switch on again after `None` sets the flag again; and
a missing row, a missing address, or an
unchanged mode changes nothing, and none of them writes an audit entry. These scenarios are tested by
`NotificationStore_Get_NewRow_ModeNone`, `NotificationStore_Get_NewRow_NoPendingBaseline`,
`NotificationStore_Get_NewRow_EmptyDispatchState`, `NotificationStore_SetMode_Persisted_ReadBack`,
`NotificationStore_SetMode_AuditFailure_ModeUnchanged`, `NotificationStore_SetMode_NoneToDaily_FlagSet`,
`NotificationStore_SetMode_NoneToDaily_BatchWindowCleared`, `NotificationStore_SetMode_NoneToDaily_FailureStateCleared`,
`NotificationStore_SetMode_DailyToEvery_KeepsItems`, `NotificationStore_SetMode_DailyToEvery_NoBaselineFlag`,
`NotificationStore_SetMode_DailyToEveryBeforeBaseline_KeepsBaselinePending`,
`NotificationStore_SetMode_EveryToDailyAfterBaseline_KeepsBaselineCleared`,
`NotificationStore_SetMode_ToNoneBeforeBaseline_ClearsBaselinePending`,
`NotificationStore_SetMode_ActiveToNoneToActive_SetsBaselinePendingAgain`,
`NotificationStore_SetMode_None_DeletesNotified`, `NotificationStore_SetMode_None_ClearsDailySlotMemory`,
`NotificationStore_SetMode_NoRow_ReturnsFalse`, `NotificationStore_SetMode_NoConfirmedAddress_ReturnsFalse`,
`NotificationStore_SetMode_SameMode_ReturnsFalse`, and `NotificationStore_SetMode_NoChangePossible_NoAuditEntry`.

**Recipients**: Only users with a mode other than `None` and a confirmed address are listed, the disabled flag and the
temporary-password flag are joined, the stored dispatch state is returned, and the timestamps are stored in the
sortable UTC text form and read back unchanged. These scenarios are tested by
`NotificationStore_ListRecipients_Mixed_OnlyEligible`, `NotificationStore_ListRecipients_Flags_Joined`,
`NotificationStore_ListRecipients_MustChangePassword_Joined`, `NotificationStore_ListRecipients_State_RoundTrips`,
`NotificationStore_Apply_State_TimestampsStoredAsUtcText`, and `NotificationStore_Apply_State_TimestampsRoundTrip`.

**Commit of a mail**: A commit inserts new items, updates existing ones, removes named keys, can replace all items,
writes no log row, and is atomic (a failing item write rolls back the state); a commit for a user whose
mode is `None` returns false and writes nothing; a commit for a user without a row returns false; the same key keeps
one row, also by the unique key of the database itself; old log rows are pruned. These scenarios are tested by
`NotificationStore_Apply_Upserts_InsertsNewItem`, `NotificationStore_Apply_Upserts_UpdatesExistingItem`,
`NotificationStore_Apply_RemoveKeys_Deleted`, `NotificationStore_Apply_ReplaceAll_ReplacesSet`,
`NotificationStore_Apply_Commit_WritesNoLogRow`, `NotificationStore_Apply_Failure_NothingPersisted`,
`NotificationStore_Apply_ModeNone_ReturnsFalse`, `NotificationStore_Apply_ModeNone_WritesNothing`,
`NotificationStore_Apply_NoRow_ReturnsFalse`, `NotificationStore_Apply_SameKeyTwice_OneRow`,
`NotificationStore_Create_Database_NotifiedItemsKeyUnique`, and `NotificationStore_Apply_OldLogRows_Pruned`.

**Reservation of the send of a mail**: The tests are in `NotificationStoreSendTests.cs`, over the same real database.
A reservation under the limits is reserved, returns the identity of a new log row of the given kind, address, and
time, and is counted. A reservation is refused as the server limit when the log holds the limit in rows of the last
hour, whatever their kind (a confirmation row counts) and whatever their user, and rows older than an hour do not
count. A reservation is refused as the user limit when the log holds the limit in rows of that user in the last day,
of every kind. Two parallel reservations at the limit minus one give exactly one reserved, for the server limit and
for the user limit. A release deletes the row and frees the place; a release of a row that is gone, or that belongs to
another user, changes nothing. A reservation deletes the rows that are older than a day. A confirmation that was
reserved earlier counts against a notification mail, and a notification mail that was reserved earlier counts
against a confirmation. The decision itself is a pure function, `SendLimits.Decide`, which checks the server limit
first; a theory covers its combinations. These scenarios are tested by
`NotificationStore_ReserveSend_UnderLimits_ReservedAndCounted`,
`NotificationStore_ReserveSend_HourlyLimit_CountsOtherKindsAndUsers`,
`NotificationStore_ReserveSend_HourlyLimit_IgnoresOlderMails`,
`NotificationStore_ReserveSend_UserDailyLimit_CountsOwnMailsOfEveryKind`,
`NotificationStore_ReserveSend_ParallelAtHourlyLimit_OneReserved`,
`NotificationStore_ReserveSend_ParallelAtUserLimit_OneReserved`, `NotificationStore_ReleaseSend_Reserved_FreesSlot`,
`NotificationStore_ReleaseSend_GoneOrForeign_NoOp`, `NotificationStore_ReserveSend_OldRows_AreDeleted`,
`NotificationStore_ReserveSend_AfterConfirmationReserved_SharesHourlyCount`,
`NotificationStore_ReserveSend_BeforeConfirmation_SharesHourlyCount`, and `SendLimits_Decide_Counts_ReturnsOutcome`.

**Notified items and counts**: A new database has the table; only the items of the asked user are returned; the items
go away with the user and with the removal of the address; and the mails of one user or of all users since an instant
are counted. These scenarios are tested by `NotificationStore_Create_Database_HasNotifiedItems`,
`NotificationStore_GetNotified_ReturnsOnlyOwnItems`, `NotificationStore_Delete_User_CascadesNotifiedItems`,
`NotificationStore_Remove_DeletesNotified`, `NotificationStore_CountSent_User_CountsOwn`, and
`NotificationStore_CountSent_All_CountsEverything`.

**Generation and stale commits**: The generation of a preference rises by one when the mode changes and when an address
is confirmed, and it is reported with each recipient. A commit whose generation differs from the current one, or whose
address differs from the confirmed address, returns false and writes nothing (no item, no state), and a
commit with the current address and generation is applied. These scenarios are tested by
`NotificationStore_SetMode_Changed_RaisesGeneration`, `NotificationStore_Confirm_Address_RaisesGeneration`,
`NotificationStore_ListRecipients_Generation_Joined`, `NotificationStore_Apply_StaleGeneration_WritesNothing`,
`NotificationStore_Apply_ChangedAddress_WritesNothing`, and
`NotificationStore_Apply_CurrentAddressAndGeneration_Applied`.

**Kind and review of the items**: Two items of one section with different kinds are two rows, removing the key of one
kind keeps the row of the other kind, the items go away with the deletion of their review, and a review that is deleted
and created again has no items. The tests seed a project and a review, because the items now relate to the review.
These scenarios are tested by `NotificationStore_Apply_SameSectionOtherKind_TwoRows`,
`NotificationStore_Apply_RemoveOneKind_KeepsOtherKind`, `NotificationStore_Delete_Review_CascadesNotifiedItems`, and
`NotificationStore_Delete_ReviewRecreated_HasNoNotifiedItems`.

**Availability of the new parts**: A database without the table of notified items, without the mode column, or without
the generation column is reported as not available (a database of an earlier version of the notifications). These
scenarios are tested by `NotificationStore_IsAvailable_NoNotifiedItems_False`,
`NotificationStore_IsAvailable_NoModeColumn_False`, and `NotificationStore_IsAvailable_NoGenerationColumn_False`.
