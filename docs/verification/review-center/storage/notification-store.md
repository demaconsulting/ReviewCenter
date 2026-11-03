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
