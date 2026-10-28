### SignInThrottle

#### Verification Approach

`SignInThrottle` is verified with unit tests defined in `SignInThrottleTests.cs`. The tests create the class directly
with small limits (3 per name, 5 per address, and a window of 60 seconds) and a movable clock, so the window can be
passed without waiting. No mocking is applied. The way the login endpoint uses the throttle (the redirect, the message,
the audit entries) is verified by the `SignIn` tests and the subsystem test, which run the real pipeline.

#### Test Environment

N/A - standard test environment. The tests need no database, no files, and no network.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Attempts within the limits are allowed.
- The attempts of one client address for one user name are limited, whatever the case of the name, and other names from
  the same address are not affected.
- The attempts of one client address over all names are limited.
- Another client address has its own counters.
- Attempts are allowed again after the window.
- A success clears the counter of the name and gives one slot back to the counter of the address.
- A refused attempt uses up no slot.
- A missing address or name is handled.
- A flood of distinct keys cannot grow the table past its cap; when the table is full, expired counters are removed
  first (all of them, even when the clock has stepped backwards), and only then the oldest live counters are evicted,
  one at a time and only as far as needed, so the counters of the current attempt and the recent ones are kept.
- An attempt that needs one new key does not evict anything for the key that exists.
- A success clears the counter of the name and gives the slot back to the address only when the window that was counted
  is still the current one; a window that has been replaced (after the window passed, an eviction, or `Reset`) is not
  touched, and a reservation gives its slot back only once.
- A null reservation is refused.
- The settings default to 5 per name, 20 per address, and 300 seconds, under the section `Identity:Throttle`.

#### Test Scenarios

**SignInThrottle_TryBegin_WithinLimits_AllowsAttempts**: Three attempts for one name and address are made; all are
allowed. This scenario is tested by `SignInThrottle_TryBegin_WithinLimits_AllowsAttempts`.

**SignInThrottle_TryBegin_SameNameTooOften_RefusesFurtherAttempts**: Three attempts are made with the name in
different cases; a fourth is refused, and an attempt with another name from the same address is allowed. This scenario
is tested by `SignInThrottle_TryBegin_SameNameTooOften_RefusesFurtherAttempts`.

**SignInThrottle_TryBegin_ManyNamesFromOneAddress_RefusesAtAddressLimit**: Five attempts with five different names are
allowed and the sixth with a new name is refused. This scenario is tested by
`SignInThrottle_TryBegin_ManyNamesFromOneAddress_RefusesAtAddressLimit`.

**SignInThrottle_TryBegin_OtherAddress_IsCountedSeparately**: After the name limit is reached for one address, the same
name from another address is allowed. This scenario is tested by
`SignInThrottle_TryBegin_OtherAddress_IsCountedSeparately`.

**SignInThrottle_TryBegin_AfterWindow_AllowsAgain**: After the limit is reached the clock is moved by the window; the
next attempt is allowed. This scenario is tested by `SignInThrottle_TryBegin_AfterWindow_AllowsAgain`.

**SignInThrottle_Succeeded_ClearsNameCounterAndReturnsAddressSlot**: With the address counter full, a success is
reported for one name; one attempt of a new name is then allowed (the returned slot), the name of the success is
counted from zero but the address is full again. This scenario is tested by
`SignInThrottle_Succeeded_ClearsNameCounterAndReturnsAddressSlot`.

**SignInThrottle_TryBegin_RefusedAttempt_DoesNotConsumeSlots**: Many attempts refused at the name limit are made; the
two address slots that were free before are still free afterward. This scenario is tested by
`SignInThrottle_TryBegin_RefusedAttempt_DoesNotConsumeSlots`.

**SignInThrottle_TryBegin_NullAddressOrName_IsHandled**: Attempts with a null address and a null name are counted
like any other key (three allowed, the fourth refused), and an empty address is accepted. This scenario is tested by
`SignInThrottle_TryBegin_NullAddressOrName_IsHandled`.

**SignInThrottle_TryBegin_ManyDistinctKeys_KeepsTableBounded**: As many distinct names as the cap are tried from one
address, with a high address limit; all are allowed and the number of keys never passes the cap. This scenario is
tested by `SignInThrottle_TryBegin_ManyDistinctKeys_KeepsTableBounded`.

**SignInThrottle_TryBegin_TableFull_EvictsOldestCountersAndKeepsRecentOnes**: The table is full of live keys and more
attempts arrive; the oldest counters are evicted and the recent ones, also the counter of the address, are kept. This
scenario is tested by `SignInThrottle_TryBegin_TableFull_EvictsOldestCountersAndKeepsRecentOnes`.

**SignInThrottle_TryBegin_OneKeyMissing_DoesNotEvictForTheExistingAddressKey**: The address key exists and only the
name key is new; the attempt makes room for one key, not two. This scenario is tested by
`SignInThrottle_TryBegin_OneKeyMissing_DoesNotEvictForTheExistingAddressKey`.

**SignInThrottle_TryBegin_TableFullOfExpiredKeys_PrunesExpiredBeforeEvictingLiveOnes**: The table is full, its keys
have expired, and a live key was added later; the expired keys are removed and the live counter survives. This
scenario is tested by `SignInThrottle_TryBegin_TableFullOfExpiredKeys_PrunesExpiredBeforeEvictingLiveOnes`.

**SignInThrottle_TryBegin_ClockSteppedBackwards_PrunesExpiredBeforeEvictingLiveOnes**: A live counter is created, the
clock is stepped back and the table is filled with counters that then expire, so the live counter is first in the
creation order but not the oldest by start time. A new attempt arrives when the table is full; all expired counters
are removed wherever they are in the order and the live counter survives. This scenario is tested by
`SignInThrottle_TryBegin_ClockSteppedBackwards_PrunesExpiredBeforeEvictingLiveOnes`.

**SignInThrottle_Succeeded_AfterWindowRolledOver_DoesNotClearNewNameCounter**: A reservation is taken, the window
passes, a new window of the name is counted, and then the success of the first reservation is reported; the counter of
the new window is kept. This scenario is tested by
`SignInThrottle_Succeeded_AfterWindowRolledOver_DoesNotClearNewNameCounter`.

**SignInThrottle_Succeeded_AfterWindowRolledOver_DoesNotRefundNewAddressCounter**: The same, for the address: the
success of the old reservation gives no slot back to the new window of the address. This scenario is tested by
`SignInThrottle_Succeeded_AfterWindowRolledOver_DoesNotRefundNewAddressCounter`.

**SignInThrottle_Succeeded_AfterEviction_DoesNotTouchRecreatedWindows**: The counters of a reservation are evicted and
created again; the success of the old reservation leaves the new windows as they are. This scenario is tested by
`SignInThrottle_Succeeded_AfterEviction_DoesNotTouchRecreatedWindows`.

**SignInThrottle_Succeeded_CalledTwiceWithOneReservation_RefundsOnce**: A success is reported twice for one
reservation; the address slot is given back only once. This scenario is tested by
`SignInThrottle_Succeeded_CalledTwiceWithOneReservation_RefundsOnce`.

**SignInThrottle_Succeeded_NullReservation_Throws**: A null reservation is refused with an `ArgumentNullException`.
This scenario is tested by `SignInThrottle_Succeeded_NullReservation_Throws`.

**SignInThrottle_TryBeginAddressOnly_NoNameLimit_RefusesOnlyAtAddressLimit**: With a limit of 3 per name and 5 per
address, four address-only attempts from one address all pass (past the limit per name), a named attempt takes the
fifth address slot, and the next address-only attempt and an attempt with another name are refused; another address
is not affected. This scenario is tested by `SignInThrottle_TryBeginAddressOnly_NoNameLimit_RefusesOnlyAtAddressLimit`.

**SignInThrottle_Succeeded_AddressOnlyReservation_ReturnsTheSlotOnce**: Four address-only attempts hold one key; a
success reported twice for one reservation gives back exactly one slot (two more attempts pass, the third is refused),
and the success of a lone attempt removes its counter. This scenario is tested by
`SignInThrottle_Succeeded_AddressOnlyReservation_ReturnsTheSlotOnce`.

**SignInThrottle_TryBeginAddressOnly_TableFull_EvictsOldestAndStaysAtCap**: With a full table, an address-only attempt
from a new address is allowed and the table stays at the cap. This scenario is tested by
`SignInThrottle_TryBeginAddressOnly_TableFull_EvictsOldestAndStaysAtCap`.

**SignInThrottle_Reset_DoesNotReuseGenerations**: A reservation is taken, `Reset` is called, and the same keys are
counted again; the success of the old reservation does not touch the new counters. This scenario is tested by
`SignInThrottle_Reset_DoesNotReuseGenerations`.

**SignInThrottle_Reset_ClearsAllCounters**: After the limit is reached, `Reset` is called; the next attempt is
allowed and only its two keys are held. This scenario is tested by `SignInThrottle_Reset_ClearsAllCounters`.

**SignInThrottleOptions_Defaults_AreFiveTwentyAndFiveMinutes**: A new options object has the section name
`Identity:Throttle`, 5 per name, 20 per address, and a window of 300 seconds. This scenario is tested by
`SignInThrottleOptions_Defaults_AreFiveTwentyAndFiveMinutes`. The refusal of invalid settings at start-up is verified by
the `Hosting` unit test `ServerHost_CreateApplication_InvalidThrottleSettings_ThrowsClearError`.
