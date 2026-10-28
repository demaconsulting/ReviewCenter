### RevalidatingAuthenticationStateProvider

#### Verification Approach

`RevalidatingAuthenticationStateProvider` is verified with unit tests defined in
`RevalidatingAuthenticationStateProviderTests.cs`. The rule is tested by calling `IsCurrentAsync` directly with states
built from the same claims as the cookie principal, over the real `UserService` with an in-memory user store and a fake
password hasher; a user is stored, changed (disabled, demoted, renamed), or left alone, and the answer is checked. The
failure cases use a scripted user store that throws, or that cancels, on a read. The framework loop that owns the
timer is exercised with the internal constructor and an interval of 50 milliseconds, far below the configured minimum,
so no test waits for 30 seconds; the tests poll the state with a generous limit of 10 seconds instead of sleeping for a
fixed time, except where they must show that nothing changes, where they wait for about ten intervals. No mocking
framework is used. The registration of the provider in the real pipeline is verified by the `Identity` subsystem
test (see the *Identity Verification* document).

#### Test Environment

N/A - standard test environment. The loop tests use real timers with short intervals; no external service is needed.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A user that exists, is enabled, and has the same system administrator state and display name is still valid.
- A user that is disabled, removed, has another system administrator state, or has another display name is not valid,
  as is a state without a
  user name.
- A user that cannot be read is not valid (fail closed), and the loop makes the state anonymous.
- A cancelled read is reported as a cancellation that carries the token, not as a failed check.
- The loop makes the state anonymous after the user is disabled, leaves a valid user signed in, and stops when the
  provider is disposed.
- Missing arguments are refused.

#### Test Scenarios

**RevalidatingAuthenticationStateProvider_IsCurrentAsync_EnabledUserSameRole_ReturnsTrue**: An enabled user is
stored and the state is signed in as that user with the same display name and system administrator state; the check
answers `true`. This scenario
is tested by `RevalidatingAuthenticationStateProvider_IsCurrentAsync_EnabledUserSameRole_ReturnsTrue`.

**RevalidatingAuthenticationStateProvider_IsCurrentAsync_UserDisabled_ReturnsFalse**: The stored user is disabled after
the state was created; the check answers `false`. This scenario is tested by
`RevalidatingAuthenticationStateProvider_IsCurrentAsync_UserDisabled_ReturnsFalse`.

**RevalidatingAuthenticationStateProvider_IsCurrentAsync_ExternalUserDisabled_ReturnsFalse**: An account created by a
sign-in at an OpenID Connect provider is valid while enabled and, once it is disabled, the check answers `false`. This
scenario is tested by `RevalidatingAuthenticationStateProvider_IsCurrentAsync_ExternalUserDisabled_ReturnsFalse`.

**RevalidatingAuthenticationStateProvider_IsCurrentAsync_UserDeleted_ReturnsFalse**: The store has no user of the
name in the state; the check answers `false`. This scenario is tested by
`RevalidatingAuthenticationStateProvider_IsCurrentAsync_UserDeleted_ReturnsFalse`.

**RevalidatingAuthenticationStateProvider_IsCurrentAsync_RoleChanged_ReturnsFalse**: A theory over a system
administrator who was demoted and an ordinary user who was promoted; the check answers `false` in both cases, because
the presence of the `SystemAdmin` role claim no longer equals the stored flag. This scenario is tested by
`RevalidatingAuthenticationStateProvider_IsCurrentAsync_RoleChanged_ReturnsFalse`.

**RevalidatingAuthenticationStateProvider_IsCurrentAsync_DisplayNameChanged_ReturnsFalse**: The stored display name
differs from the one in the state; the check answers `false`, as the cookie check does. This scenario is tested by
`RevalidatingAuthenticationStateProvider_IsCurrentAsync_DisplayNameChanged_ReturnsFalse`.

**RevalidatingAuthenticationStateProvider_IsCurrentAsync_NoNameClaim_ReturnsFalse**: The state has no user name, so it
cannot be matched to a stored user; the check answers `false`. This scenario is tested by
`RevalidatingAuthenticationStateProvider_IsCurrentAsync_NoNameClaim_ReturnsFalse`.

**RevalidatingAuthenticationStateProvider_IsCurrentAsync_LookupThrows_ReturnsFalseFailClosed**: The store throws on the
read; the check answers `false` and does not throw, so a user who cannot be verified does not stay signed in. A warning
naming the user, and containing no password or hash, is logged. This
scenario is tested by `RevalidatingAuthenticationStateProvider_IsCurrentAsync_LookupThrows_ReturnsFalseFailClosed`.

**RevalidatingAuthenticationStateProvider_IsCurrentAsync_CancelledLookup_ThrowsTaskCanceledWithToken**: The token is
cancelled while the store read is running; the check throws a `TaskCanceledException` whose token is the cancelled
token, which is the exception the framework loop treats as a clean stop. This scenario is tested by
`RevalidatingAuthenticationStateProvider_IsCurrentAsync_CancelledLookup_ThrowsTaskCanceledWithToken`.

**RevalidatingAuthenticationStateProvider_Loop_UserDisabled_StateBecomesAnonymous**: A signed-in state is checked every
50 milliseconds; the user is disabled; within the limit of 10 seconds the state is no longer authenticated. This
scenario is tested by `RevalidatingAuthenticationStateProvider_Loop_UserDisabled_StateBecomesAnonymous`.

**RevalidatingAuthenticationStateProvider_Loop_LookupFails_StateBecomesAnonymous**: The store fails on every read;
within the limit the state is no longer authenticated. This scenario is tested by
`RevalidatingAuthenticationStateProvider_Loop_LookupFails_StateBecomesAnonymous`.

**RevalidatingAuthenticationStateProvider_Loop_UserStaysValid_StateStaysAuthenticated**: A valid user is checked about
ten times; the state is still authenticated. This scenario is tested by
`RevalidatingAuthenticationStateProvider_Loop_UserStaysValid_StateStaysAuthenticated`.

**RevalidatingAuthenticationStateProvider_Dispose_StopsLoop**: The provider is disposed, then the user is disabled and
about ten intervals pass; the state is unchanged, because the loop no longer runs. This scenario is tested by
`RevalidatingAuthenticationStateProvider_Dispose_StopsLoop`.

**RevalidatingAuthenticationStateProvider_Constructor_NullArguments_Throw**: A missing user service or missing options
is passed to a constructor; an `ArgumentNullException` is thrown. This scenario is tested by
`RevalidatingAuthenticationStateProvider_Constructor_NullArguments_Throw`.
