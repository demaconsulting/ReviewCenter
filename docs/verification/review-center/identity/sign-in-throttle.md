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
- A flood of distinct keys cannot grow the table past its cap.
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

**SignInThrottle_Reset_ClearsAllCounters**: After the limit is reached, `Reset` is called; the next attempt is
allowed and only its two keys are held. This scenario is tested by `SignInThrottle_Reset_ClearsAllCounters`.

**SignInThrottleOptions_Defaults_AreFiveTwentyAndFiveMinutes**: A new options object has the section name
`Identity:Throttle`, 5 per name, 20 per address, and a window of 300 seconds. This scenario is tested by
`SignInThrottleOptions_Defaults_AreFiveTwentyAndFiveMinutes`. The refusal of invalid settings at start-up is verified by
the `Hosting` unit test `ServerHost_CreateApplication_InvalidThrottleSettings_ThrowsClearError`.
