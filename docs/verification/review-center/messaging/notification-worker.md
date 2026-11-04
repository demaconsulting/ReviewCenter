### NotificationWorker

#### Verification Approach

`NotificationWorker` is verified with unit tests defined in `NotificationWorkerTests.cs`. The tests build the real worker
with a pass that the test controls (a delegate that counts its calls, returns a result, waits, or throws), a step clock
(`StepClock`) that the test moves to let the polling interval elapse, and a logger that captures its entries. The worker
is started and stopped through the host interface of a `BackgroundService`, and the tests wait for the passes that they
expect, with a time limit, so a failure is a failed test and not a hang. No dispatcher, no database, and no real clock
are used: what is checked is the loop, its stop, and its log.

#### Test Environment

N/A - standard test environment (the step clock, the controlled pass, and the capturing logger are part of the test
code).

#### Acceptance Criteria

- All tests pass with zero failures.
- A pass runs when the worker starts, the next pass runs when the polling interval has elapsed, and no second pass runs
  before.
- The worker stops without delay, also while it waits for the next interval, and a pass that is running at the stop is
  canceled and not logged.
- A pass that throws does not stop the loop and does not fault the task of the worker.
- The log of a failed pass names the type of the exception and not its message, and no address is logged.
- A pass that sent or failed mails is logged with its counts, and a pass that did neither writes no entry.
- A missing argument and a polling interval that is not positive are refused.

#### Test Scenarios

**The loop**: A pass runs when the worker starts, the next pass runs when the polling interval has elapsed, and no
second pass runs before it has elapsed. These scenarios are tested by `NotificationWorker_ExecuteAsync_Started_RunsPass`,
`NotificationWorker_ExecuteAsync_IntervalElapsed_RunsNextPass`, and
`NotificationWorker_ExecuteAsync_BeforeInterval_NoSecondPass`.

**The stop**: A running worker completes when the host stops, a worker that waits for the next pass stops at once while
the clock does not move, and a pass that is running at the stop is canceled and nothing is logged. These scenarios are
tested by `NotificationWorker_StopAsync_Running_Completes`, `NotificationWorker_StopAsync_Waiting_ReturnsPromptly`,
`NotificationWorker_StopAsync_PassRunning_CancelsPass`, and `NotificationWorker_StopAsync_PassRunning_NothingLogged`.

**A failing pass**: After a pass that throws the next pass runs, the task of the worker is not faulted, and the log
entry holds the name of the type of the exception and not its message. These scenarios are tested by
`NotificationWorker_ExecuteAsync_PassThrows_RunsNextPass`, `NotificationWorker_ExecuteAsync_PassThrows_TaskNotFaulted`,
`NotificationWorker_ExecuteAsync_PassThrows_LogHasType`, and `NotificationWorker_ExecuteAsync_PassThrows_LogHasNoMessage`.

**The log of a pass**: A pass that sent or failed mails is logged with its counts and with nothing that looks like an
address, and a pass that sent and failed nothing writes no entry. These scenarios are tested by
`NotificationWorker_ExecuteAsync_PassSent_LogsCounts`, `NotificationWorker_ExecuteAsync_PassResultWithAddress_NotLogged`,
`NotificationWorker_ExecuteAsync_PassThrowsWithAddress_NoAddressAnywhereInLog` (the logger captures messages, state, and
exception text, and the failing pass throws an exception whose message holds an address),
and `NotificationWorker_ExecuteAsync_IdlePass_NothingLogged`.

**Refused arguments**: A missing pass, clock, or logger raises `ArgumentNullException`, and a polling interval that is
zero or negative raises `ArgumentOutOfRangeException`. These scenarios are tested by
`NotificationWorker_Constructor_NullArgument_Throws` and `NotificationWorker_Constructor_NonPositivePoll_Throws`.
