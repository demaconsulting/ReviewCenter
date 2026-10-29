### TotpChallengeStore

#### Verification Approach

`TotpChallengeStore` is verified with unit tests defined in `TotpChallengeStoreTests.cs`. The tests create the store
directly with a movable test clock, so lifetimes are verified by advancing the clock and nothing waits in real time.
Single use under concurrency is verified with parallel takers released at the same moment. The use of the store by the
sign-in flow is verified by the `TotpService` tests.

#### Test Environment

N/A - standard test environment. The tests need no database, no files, and no network.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A token is random, 256 bits long, written as URL-safe text, and the store keeps only a hash of it.
- A pending sign-in can be taken once, even when many requests take it at the same time.
- A pending sign-in can be seen or taken only from the client address it was issued to; a missing address matches
  only a missing address.
- Missing and unknown tokens give nothing.
- A pending sign-in ends after its lifetime, and ending removes it.
- A pending sign-in given back after a failure is kept for fewer than the maximum number of failures, then ends.
- Invalidating a user drops that user's pending sign-ins, without regard to case, and no others.
- The number of pending sign-ins is capped, dropping ended ones first and then the oldest, and a pending sign-in
  given back never takes the store over the cap.
- A pending sign-in that is being checked when its user is invalidated is not given back, and the memory of
  invalidations is bounded in time and number.
- Missing arguments are refused.

#### Test Scenarios

**TotpChallengeStore_Issue_ReturnsRandomUrlSafeTokens**: Two challenges are issued; the tokens are 43 URL-safe
characters, differ, and the stored challenge holds only a hash, the user name, and the return address. This scenario
is tested by `TotpChallengeStore_Issue_ReturnsRandomUrlSafeTokens`.

**TotpChallengeStore_Take_Twice_GivesTheChallengeOnlyOnce**: Taking a challenge twice gives it only the first time and
it is gone afterwards. This scenario is tested by `TotpChallengeStore_Take_Twice_GivesTheChallengeOnlyOnce`.

**TotpChallengeStore_Take_Concurrently_GivesTheChallengeToExactlyOne**: Thirty-two takers are released together and
exactly one gets the challenge. This scenario is tested by
`TotpChallengeStore_Take_Concurrently_GivesTheChallengeToExactlyOne`.

**TotpChallengeStore_PeekAndTake_ForeignAddress_AreRefusedAndLeaveTheChallenge**: Another address can neither see nor
take the challenge, and the owner still has it. This scenario is tested by
`TotpChallengeStore_PeekAndTake_ForeignAddress_AreRefusedAndLeaveTheChallenge`.

**TotpChallengeStore_NullAddress_MatchesOnlyNullAddress**: A challenge issued without an address is found only by a
request without an address. This scenario is tested by `TotpChallengeStore_NullAddress_MatchesOnlyNullAddress`.

**TotpChallengeStore_UnknownOrMissingToken_GivesNothing**: A null, empty, and made-up token give nothing. This scenario
is tested by `TotpChallengeStore_UnknownOrMissingToken_GivesNothing`.

**TotpChallengeStore_AfterTheLifetime_TheChallengeIsGone**: A challenge is valid one second before the end of its
lifetime, gone at the end, and removed. This scenario is tested by
`TotpChallengeStore_AfterTheLifetime_TheChallengeIsGone`.

**TotpChallengeStore_Return_CountsFailuresAndEndsAtTheMaximum**: A challenge taken and returned repeatedly is kept for
the first four failures and ends on the fifth. This scenario is tested by
`TotpChallengeStore_Return_CountsFailuresAndEndsAtTheMaximum`.

**TotpChallengeStore_InvalidateUser_DropsOnlyThatUsersChallenges**: Invalidating a user name in another case drops two
challenges of that user and keeps the challenge of another user. This scenario is tested by
`TotpChallengeStore_InvalidateUser_DropsOnlyThatUsersChallenges`.

**TotpChallengeStore_Return_AfterTheLifetime_EndsTheChallenge**: A challenge given back after its end is not kept. This
scenario is tested by `TotpChallengeStore_Return_AfterTheLifetime_EndsTheChallenge`.

**TotpChallengeStore_Issue_BeyondTheCap_StaysWithinTheCapAndDropsTheOldest**: A full store accepts one more challenge,
stays within the cap, keeps the newest, and drops the oldest. This scenario is tested by
`TotpChallengeStore_Issue_BeyondTheCap_StaysWithinTheCapAndDropsTheOldest`.

**TotpChallengeStore_NullArguments_Throw**: Null arguments raise `ArgumentNullException`. This scenario is tested by
`TotpChallengeStore_NullArguments_Throw`.

**TotpChallengeStore_Return_AfterInvalidateUser_IsRefusedAndNotStored**: A challenge is taken, its user is invalidated
(in another case) while the code is being checked, and giving it back is refused and stores nothing. This scenario is
tested by `TotpChallengeStore_Return_AfterInvalidateUser_IsRefusedAndNotStored`.

**TotpChallengeStore_Return_AfterInvalidatingAnotherUser_StillStores**: Invalidating another user does not stop a taken
challenge from being given back. This scenario is tested by
`TotpChallengeStore_Return_AfterInvalidatingAnotherUser_StillStores`.

**TotpChallengeStore_Return_ChallengeTakenAfterTheInvalidation_IsStored**: A challenge taken after the invalidation is
given back and stored. This scenario is tested by
`TotpChallengeStore_Return_ChallengeTakenAfterTheInvalidation_IsStored`.

**TotpChallengeStore_Return_WhenStoreFilledAfterTake_DropsInsteadOfExceedingTheCap**: The store is filled to the cap
with live challenges after a challenge was taken; giving it back is refused, the count stays at the cap, and no other
challenge is evicted. This scenario is tested by
`TotpChallengeStore_Return_WhenStoreFilledAfterTake_DropsInsteadOfExceedingTheCap`.

**TotpChallengeStore_Return_WhenFullOfEndedEntries_DropsEndedAndStores**: When the full store holds ended challenges,
they are dropped and the returned challenge is stored within the cap. This scenario is tested by
`TotpChallengeStore_Return_WhenFullOfEndedEntries_DropsEndedAndStores`.

**TotpChallengeStore_InvalidateUser_TombstonesArePrunedAfterTheLifetime**: The remembered invalidations are gone after
the lifetime has passed on the test clock. This scenario is tested by
`TotpChallengeStore_InvalidateUser_TombstonesArePrunedAfterTheLifetime`.

**TotpChallengeStore_InvalidateUser_TombstonesAreBoundedAndOverflowFailsSafe**: Invalidating more users than the cap
keeps the number of remembered invalidations below the cap, and a challenge taken before the overflow is still
refused. This scenario is tested by `TotpChallengeStore_InvalidateUser_TombstonesAreBoundedAndOverflowFailsSafe`.
