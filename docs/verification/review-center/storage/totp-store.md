### TotpStore

#### Verification Approach

`TotpStore` is verified with unit tests defined in `TotpStoreTests.cs`. The tests use the real store over a real
SQLite database file in a temporary folder (`TestStore`), add the users to the users table directly, and read the rows
and the audit entries back with a second context. A real database is used because the conditional updates that carry
the replay protection must have exactly one winner, which only the database can show; parallel requests (32 for the
step, and several for a recovery code) are released together to prove it. The tests of the `ReviewCenterDbContext`
model for the two tables are in the same file and are described in the verification of that unit. How the service
uses the store is verified by the `TotpService` tests over an in-memory stand-in.

#### Test Environment

N/A - standard test environment (a temporary SQLite database file is created by each test and removed afterwards).

#### Acceptance Criteria

- All tests pass with zero failures.
- A pending enrollment round-trips, is not active, is replaced by a newer one, and is listed nowhere.
- Confirming makes the enrollment active, stores the recovery code hashes, and writes the audit entry in one
  transaction; a second confirmation, a restart of an active enrollment, and a confirmation without a pending
  enrollment change nothing and write no entry.
- A duplicate recovery code hash of one user is refused by the database and rolls back the whole confirmation,
  including its audit entry; the same hash for different users is allowed.
- The last accepted step moves only forward and only for a confirmed enrollment; of 32 parallel requests with the
  same step exactly one wins.
- A recovery code is spent once with one audit entry; unknown and foreign codes are refused without an entry; of
  parallel requests with the same code exactly one wins.
- Replacing the recovery codes removes all old codes and is refused without a confirmed enrollment.
- Removing deletes the enrollment and its codes with one audit entry, removing a pending enrollment leaves a confirmed
  one alone, and removing nothing records nothing.
- Every change keeps the audit hash chain intact, and deleting a user deletes the enrollment and its codes.
- Missing arguments are refused.

#### Test Scenarios

**TotpStore_SavePendingAsync_RoundTripsAndReplacesAPendingEnrollment**: A pending enrollment is saved and read back; it
is not active, has no confirmation time, is replaced by a second save, and is not listed among the active users. This
scenario is tested by `TotpStore_SavePendingAsync_RoundTripsAndReplacesAPendingEnrollment`.

**TotpStore_ConfirmAsync_ActivatesStoresCodesAndAuditsOnce**: Confirming activates the enrollment with the step and
the time, stores three recovery codes, lists the user as active, and writes one audit entry; a second confirmation and
a new pending enrollment are refused. This scenario is tested by
`TotpStore_ConfirmAsync_ActivatesStoresCodesAndAuditsOnce`.

**TotpStore_ConfirmAsync_NothingPending_ReturnsFalseAndStoresNothing**: Confirming without a pending enrollment returns
false and stores no code and no audit entry. This scenario is tested by
`TotpStore_ConfirmAsync_NothingPending_ReturnsFalseAndStoresNothing`.

**TotpStore_TryAdvanceStepAsync_OnlyForwardAndOnlyWhenConfirmed**: The same and an older step are refused, a newer step
is accepted once, and a pending or unknown enrollment is refused. This scenario is tested by
`TotpStore_TryAdvanceStepAsync_OnlyForwardAndOnlyWhenConfirmed`.

**TotpStore_TryAdvanceStepAsync_ThirtyTwoParallelRequests_HaveExactlyOneWinner**: Thirty-two parallel requests with the
same step are released together; exactly one wins and the stored step is that step. This scenario is tested by
`TotpStore_TryAdvanceStepAsync_ThirtyTwoParallelRequests_HaveExactlyOneWinner`.

**TotpStore_TryConsumeRecoveryCodeAsync_SpendsOnceAndAuditsOnce**: A recovery code is spent once with one audit entry;
a second use, an unknown hash, and the code of another user are refused, and the codes of both users stay as expected.
This scenario is tested by `TotpStore_TryConsumeRecoveryCodeAsync_SpendsOnceAndAuditsOnce`.

**TotpStore_TryConsumeRecoveryCodeAsync_ParallelRequests_HaveExactlyOneWinner**: Eight parallel requests with the same
recovery code; exactly one wins and exactly one audit entry exists. This scenario is tested by
`TotpStore_TryConsumeRecoveryCodeAsync_ParallelRequests_HaveExactlyOneWinner`.

**TotpStore_ReplaceRecoveryCodesAsync_ReplacesAllOldCodes**: Replacing gives the user the new codes only, an old code
no longer works, a new code does, another user is untouched, and a user with only a pending enrollment is refused.
This scenario is tested by `TotpStore_ReplaceRecoveryCodesAsync_ReplacesAllOldCodes`.

**TotpStore_RemoveAsync_DeletesEverythingOnceWithOneAuditEntry**: Removing deletes the enrollment and the codes with one
audit entry; a second removal returns false and records nothing. This scenario is tested by
`TotpStore_RemoveAsync_DeletesEverythingOnceWithOneAuditEntry`.

**TotpStore_RemovePendingAsync_RemovesPendingOnly**: Removing a pending enrollment removes it and leaves a confirmed
enrollment of another user in place. This scenario is tested by `TotpStore_RemovePendingAsync_RemovesPendingOnly`.

**TotpStore_ConfirmAsync_DuplicateHash_IsRefusedByTheDatabaseAndRolledBack**: Two identical hashes for one user are
refused by the unique index; the enrollment stays pending, no code is stored, and no audit entry remains. This
scenario is tested by `TotpStore_ConfirmAsync_DuplicateHash_IsRefusedByTheDatabaseAndRolledBack`.

**TotpStore_ConfirmAsync_SameHashForDifferentUsers_IsAllowed**: The same hash is stored for two users. This scenario is
tested by `TotpStore_ConfirmAsync_SameHashForDifferentUsers_IsAllowed`.

**TotpStore_Changes_KeepTheAuditChainIntact**: After every kind of second factor change the audit chain verifies as
intact. This scenario is tested by `TotpStore_Changes_KeepTheAuditChainIntact`.

**TotpStore_DeletingTheUser_DeletesTheEnrollmentAndCodes**: Deleting a user removes the enrollment and the codes by
cascade. This scenario is tested by `TotpStore_DeletingTheUser_DeletesTheEnrollmentAndCodes`.

**TotpStore_NullArguments_Throw**: Null arguments of the store operations raise `ArgumentNullException`. This scenario
is tested by `TotpStore_NullArguments_Throw`.
