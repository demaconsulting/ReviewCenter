### Validation

#### Verification Approach

`Validation` is verified with unit tests defined in `ValidationTests.cs`. Tests supply a real
`Context` object (not mocked) with controlled arguments and assert on exit codes, output
content, and result files. The checks themselves are replaced by instant stand-ins
(`FastValidationChecks`) through the `Validation.Run(Context, IReadOnlyList<ValidationCheck>)`
overload, so the report, summary, timing, skip handling, and result files are tested without
starting git, a database, or a web host; the real checks are verified separately (see the
`ValidationChecks` verification). `Program` and `PathHelpers` execute their real implementations.
Temporary directories are used for result file paths to keep tests isolated.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- `Validation.Run` throws `ArgumentNullException` for a null context or a null list of checks.
- The validation summary output contains "Total Tests:", "Passed:", "Skipped:", "Failed:", and the
  total time.
- `context.ExitCode` is 0 when all checks pass or are skipped.
- A failing or throwing check is reported as failed, sets a non-zero exit code, and does not stop
  the other checks.
- A skipped check is reported as skipped, recorded as not executed in TRX and as skipped in
  JUnit XML, and does not fail the run.
- Every line shows the elapsed time of its check, and lines appear in the listed order even when
  the first check finishes last.
- A check that blocks its thread and ignores its token is recorded as `Timed out after N s` when
  the deadline passes, without the runner waiting for it; a cancellation is reported as `Cancelled`,
  and a late exception of an abandoned check is observed.
- TRX and JUnit XML result files are created with the correct XML root elements.
- An unsupported result file extension produces no file and an error message on the context.
- `Validation.CreateChecks` lists the eight default checks, and adds the configured-database
  check last only for `--check-config`.

#### Test Scenarios

**Validation_Run_NullContext_ThrowsArgumentNullException**: `Validation.Run` is called with a
null context argument; an `ArgumentNullException` is thrown, confirming the null guard at the
unit boundary. This scenario is tested by
`Validation_Run_NullContext_ThrowsArgumentNullException`.

**Validation_Run_NullChecks_ThrowsArgumentNullException**: `Validation.Run` is called with a null
list of checks; an `ArgumentNullException` is thrown. This scenario is tested by
`Validation_Run_NullChecks_ThrowsArgumentNullException`.

**Validation_Run_WithSilentContext_PrintsSummary**: `Validation.Run` is called with a silent
context (output captured separately); the summary contains "Total Tests:", "Passed:", "Skipped:",
"Failed:", and "Total Time:", confirming the summary is always produced. This scenario is tested
by `Validation_Run_WithSilentContext_PrintsSummary`.

**Validation_Run_WithSilentContext_ExitCodeIsZero**: `Validation.Run` is called with a silent
context and passing checks; `context.ExitCode` is 0 after the run. This scenario is tested by
`Validation_Run_WithSilentContext_ExitCodeIsZero`.

**Validation_Run_WithTrxResultsFile_WritesTrxFile**: `Validation.Run` is called with a context
whose `ResultsFile` points to a temporary `.trx` path; a file is created at the specified path
and it contains a `<TestRun` XML element. This scenario is tested by
`Validation_Run_WithTrxResultsFile_WritesTrxFile`.

**Validation_Run_WithXmlResultsFile_WritesXmlFile**: `Validation.Run` is called with a context
whose `ResultsFile` points to a temporary `.xml` path; a file is created at the specified path
and it contains a `<testsuites` XML element. This scenario is tested by
`Validation_Run_WithXmlResultsFile_WritesXmlFile`.

**Validation_Run_WithUnsupportedResultsFormat_DoesNotWriteFile**: `Validation.Run` is called
with a context whose `ResultsFile` has a `.json` extension (unsupported); no file is created,
no exception is thrown, and an error message indicating the unsupported format is written to
the context. This scenario is tested by
`Validation_Run_WithUnsupportedResultsFormat_DoesNotWriteFile`.

**Validation_Run_FailingCheck_ReportsFailureAndSetsExitCode**: One of the checks fails with a
message; the line shows the failure and the message, the exit code is non-zero, and the other
checks still run. This scenario is tested by
`Validation_Run_FailingCheck_ReportsFailureAndSetsExitCode`.

**Validation_Run_ThrowingCheck_RecordsFailureWithExceptionType**: One of the checks throws an
unexpected exception; it is recorded as failed with the exception type and message, and the run
completes. This scenario is tested by
`Validation_Run_ThrowingCheck_RecordsFailureWithExceptionType`.

**Validation_Run_SkippedCheck_ReportsSkippedAndDoesNotFail**: One of the checks reports that
there is nothing to check; the line reads `SKIPPED`, TRX records `NotExecuted`, JUnit XML records
`<skipped`, the summary counts it separately, and the exit code stays 0. This scenario is tested
by `Validation_Run_SkippedCheck_ReportsSkippedAndDoesNotFail`.

**Validation_Run_SlowCheck_ReportsElapsedTime**: A check that takes at least 150 ms is run; its
line shows an elapsed time of at least that long and the duration is recorded in the results.
This scenario is tested by `Validation_Run_SlowCheck_ReportsElapsedTime`.

**Validation_Run_SlowFirstCheck_ReportsInListedOrder**: The first check finishes after the
others; the report still lists the checks in the listed order. This scenario is tested by
`Validation_Run_SlowFirstCheck_ReportsInListedOrder`.

**Validation_CreateChecks_Default_ListsEightHermeticChecks**: `Validation.CreateChecks(false)`
returns the eight default checks, in order, with the `ReviewCenter_` names. This scenario is
tested by `Validation_CreateChecks_Default_ListsEightHermeticChecks`.

**Validation_CreateChecks_CheckConfig_AddsConfiguredDatabaseLast**: `Validation.CreateChecks(true)`
returns nine checks with `ReviewCenter_ConfiguredDatabase` last. This scenario is tested by
`Validation_CreateChecks_CheckConfig_AddsConfiguredDatabaseLast`.

**ValidationRunner_ExecuteAsync_CheckBlocksAndIgnoresToken_FailsAsTimedOutWithoutWaiting**: A check
blocks its thread synchronously and ignores its token; with a deadline of 100 ms the runner returns
a failed outcome `Timed out after 0.1 s` long before the check ends. Without the independent
deadline the call would not return, and a guard turns that into a failure. This scenario is tested
by `ValidationRunner_ExecuteAsync_CheckBlocksAndIgnoresToken_FailsAsTimedOutWithoutWaiting`.

**ValidationRunner_ExecuteAsync_DeadlinePasses_CancelsTheTokenOfTheCheck**: A cooperative check
waits on its token; after the deadline it has seen the cancellation, and the outcome is the timeout
message. This scenario is tested by
`ValidationRunner_ExecuteAsync_DeadlinePasses_CancelsTheTokenOfTheCheck`.

**ValidationRunner_ExecuteAsync_CheckThrowsCancellation_ReportsCancelledNotTimedOut**: A check
throws `OperationCanceledException` before the deadline; the message is `Cancelled`, not a timeout.
This scenario is tested by
`ValidationRunner_ExecuteAsync_CheckThrowsCancellation_ReportsCancelledNotTimedOut`.

**ValidationRunner_ExecuteAsync_CallerCancels_ReportsCancelledWithoutWaitingForTheCheck**: The
caller cancels while a check that ignores its token still runs; the outcome is `Cancelled` and the
runner does not wait for the check. This scenario is tested by
`ValidationRunner_ExecuteAsync_CallerCancels_ReportsCancelledWithoutWaitingForTheCheck`.

**ValidationRunner_ExecuteAsync_CheckThrowsTimeoutException_ReportsTheCheckOwnFailure**: A check
throws a `TimeoutException` of its own; it is reported with its own type and message and not as the
deadline. This scenario is tested by
`ValidationRunner_ExecuteAsync_CheckThrowsTimeoutException_ReportsTheCheckOwnFailure`.

**ValidationRunner_ExecuteAsync_AbandonedCheckThrowsLater_ExceptionIsObserved**: An abandoned check
throws after the deadline; after garbage collection no unobserved task exception is raised. This
scenario is tested by `ValidationRunner_ExecuteAsync_AbandonedCheckThrowsLater_ExceptionIsObserved`.

**ValidationRunner_ExecuteAsync_NonPositiveTimeout_ThrowsArgumentOutOfRangeException**: A zero or
negative deadline is refused. This scenario is tested by
`ValidationRunner_ExecuteAsync_NonPositiveTimeout_ThrowsArgumentOutOfRangeException`.

**Validation_Run_HungCheck_ReportsTimeoutAndOtherChecksStillPass**: A whole run with one hung check
and a short deadline reports only that check as `Timed out`, passes the others, sets the exit code
to 1 and returns. This scenario is tested by
`Validation_Run_HungCheck_ReportsTimeoutAndOtherChecksStillPass`.
