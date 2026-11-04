### ReauthProofStore

#### Verification Approach

`ReauthProofStore` is verified with unit tests defined in `ReauthProofStoreTests.cs`. The tests create the class
directly with a clock they can move, so the lifetime can pass without waiting. No mocking is applied. How the signing
step and the sign-in callback use the store is verified by the `SigningService` tests and the `OidcSignIn` tests.

#### Test Environment

N/A - standard test environment. The tests need no database, no files, and no network.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A proof is spent exactly once; a later try is refused until a new proof is issued.
- A proof is accepted only for the user, project, review and slot it was issued for, and a try for another binding
  neither succeeds nor spends it.
- A proof is live just before its lifetime of five minutes ends and ended at the end of it; issuing again restarts it.
- The store never holds more than 10,000 proofs: when full, ended proofs are dropped first and then the oldest.
- A null clock or a null review identifier is refused.

#### Test Scenarios

**ReauthProofStore_TryConsume_SucceedsExactlyOnce**: A proof is seen with `Has` without being spent, is consumed once,
and the second consume and a later `Has` find nothing. This scenario is tested by
`ReauthProofStore_TryConsume_SucceedsExactlyOnce`.

**ReauthProofStore_Issue_AfterConsume_AllowsConsumingAgain**: A proof is consumed once and refused the second time; a
new proof issued for the same binding is seen and can be consumed once more, and then it is refused again. This
scenario is tested by `ReauthProofStore_Issue_AfterConsume_AllowsConsumingAgain`.

**ReauthProofStore_OtherBinding_IsRefusedAndKeepsTheProof**: A theory over another user, project, review and slot; each
is refused and the proof of the right binding can still be consumed. This scenario is tested by
`ReauthProofStore_OtherBinding_IsRefusedAndKeepsTheProof`.

**ReauthProofStore_Expiry_EndsTheProofAtTheLifetime**: A proof is live one second before the lifetime ends and neither
seen nor consumable at the end of it. This scenario is tested by `ReauthProofStore_Expiry_EndsTheProofAtTheLifetime`.

**ReauthProofStore_IssueAgain_RestartsTheLifetime**: A proof issued again just before it ends can be consumed after
the first lifetime would have ended. This scenario is tested by `ReauthProofStore_IssueAgain_RestartsTheLifetime`.

**ReauthProofStore_Full_DropsExpiredThenOldest**: With a full store one more proof pushes out only the oldest, every other
proof stays; after the lifetime a new proof drops every ended proof. This scenario is tested by
`ReauthProofStore_Full_DropsExpiredThenOldest`.

**ReauthProofStore_Full_EndedProofsMakeRoomAndLiveOnesStay**: In a full store whose first three proofs ended, three more
proofs are accepted and every one of the live proofs is still live. This scenario is tested by
`ReauthProofStore_Full_EndedProofsMakeRoomAndLiveOnesStay`.

**ReauthProofStore_Limits_AreFiveMinutesAndTenThousand**: The lifetime of a proof is read as five minutes and the
maximum number of proofs as 10,000. This scenario is tested by `ReauthProofStore_Limits_AreFiveMinutesAndTenThousand`.

**ReauthProofStore_NullArguments_Throw**: A null clock and a null review identifier in each method raise
`ArgumentNullException`. This scenario is tested by `ReauthProofStore_NullArguments_Throw`.
