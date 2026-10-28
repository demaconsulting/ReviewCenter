### Validation

![SelfTest Structure](SelfTestView.svg)

#### Purpose

`Validation` orchestrates the self-validation run. Its single responsibility is to decide which checks run,
run them, print a summary to the provided `Context`, and optionally serialize the results to a file. It does not
define the tool's requirements and it contains no check; it verifies, through the checks of the
`ValidationChecks` unit, that the tool behaves correctly in the deployment environment. The timing, the outcome
and the report line of one check are produced once, by the internal `ValidationRunner`, so the checks contain only
what they prove.

#### Data Model

`Validation` holds no instance state. The class is `internal static`; all state is local to `Run`. The supporting
types are in `ValidationRunner.cs`:

- `ValidationCheck` — a record of the test name and the asynchronous work (a delegate that receives a cancellation
  token and returns a short detail text).
- `ValidationFailedException` — thrown by a check when what it verified does not hold; its message is shown in the
  report.
- `ValidationSkippedException` — thrown by a check that cannot apply; it is recorded as skipped, not failed.
- `CheckOutcome` — a record of the recorded `TestResult` and the report line.

#### Key Methods

**CreateChecks**: Lists the checks of the run.

- *Parameters*: `bool includeConfiguredDatabase`.
- *Returns*: the eight default checks in the order `ReviewCenter_GitAvailable`, `ReviewCenter_GitRoundTrip`,
  `ReviewCenter_ReviewWorkflow`, `ReviewCenter_Fingerprint`, `ReviewCenter_PasswordHash`,
  `ReviewCenter_AuditChain`, `ReviewCenter_AttestationPdf`, `ReviewCenter_HostStart`; with the flag set, the
  opt-in `ReviewCenter_ConfiguredDatabase` is appended.

**Run(Context)**: Entry point for the self-validation run.

- *Parameters*: `Context context` — the output channel and results configuration.
- *Returns*: `void`.
- *Preconditions*: `context` is not null.
- *Postconditions*: A report and summary have been printed; if `context.ResultsFile` was set, a results file has
  been written or an error has been recorded for an unsupported extension.

Calls `Run(context, CreateChecks(context.CheckConfig))`.

**Run(Context, IReadOnlyList&lt;ValidationCheck&gt;)** (internal): The seam that lets tests run the report, the
summary and the results files with fast stand-in checks.

Calls `PrintValidationHeader`, constructs a `TestResults` object named `"ReviewCenter Self-Validation"`, starts
every check with `Task.Run(() => ValidationRunner.ExecuteAsync(check))` so that they run at the same time, then
takes the outcomes in the listed order and passes each to `ValidationRunner.Report`, which records it and prints
its line. It then prints the totals with `PrintSummary` and calls `WriteResultsFile` if `context.ResultsFile` is
set.

**ValidationRunner.ExecuteAsync**: Runs one check and turns its result or exception into a `CheckOutcome`.

Starts a stopwatch and runs the check under a 60 second `CancellationTokenSource`. A returned detail text is a
pass (`TestOutcome.Passed`, the text in `SystemOutput`). A `ValidationSkippedException` is a skip
(`TestOutcome.NotExecuted`, because the test results library has no separate skipped outcome). A timeout, a
`ValidationFailedException` or any other exception is a failure (`TestOutcome.Failed`, the message in
`ErrorMessage`). The line has the form `✓ Name - Passed (123 ms): detail`, with `-` and `SKIPPED` for a skip and
`✗` and `Failed` for a failure.

**ValidationRunner.Report**: Adds the result to the collection and prints the line; a failed check goes through
`context.WriteError`, which makes the exit code non-zero, and a pass or a skip through `context.WriteLine`.

**PrintSummary**: Prints `Total Tests:`, `Passed:`, `Skipped:`, `Failed:` and `Total Time:`. The failed count uses
`WriteError` if it is not zero.

**WriteResultsFile**: Serializes `testResults` to `context.ResultsFile`.

Determines the format from the file extension: `.trx` uses `TrxSerializer.Serialize`; `.xml` uses
`JUnitSerializer.Serialize`. Any other extension calls `context.WriteError` with a descriptive message and returns
without writing. File-write exceptions are caught by a broad `catch (Exception)` and reported via
`context.WriteError`.

#### Error Handling

`Run` throws `ArgumentNullException` if `context` or the check list is null. `ValidationRunner.ExecuteAsync`
catches every exception of a check, so a failing check is recorded and the remaining checks still run.
`WriteResultsFile` catches file-write exceptions and reports them via `context.WriteError`.

An unsupported `context.ResultsFile` extension is treated as a user error: `WriteError` is called with a
descriptive message (e.g., `"Error: Unsupported results file format '.json'. Use .trx or .xml extension."`) and
the method returns without writing a file, causing `context.ExitCode` to return 1.

#### Dependencies

- **Context** — output channel for header lines, test result lines, summary totals, and the `CheckConfig` option.
- **ValidationChecks** — the checks that `CreateChecks` lists.
- **DemaConsulting.TestResults** — `TestResults`, `TestResult`, and `TestOutcome` types for accumulating and
  representing self-validation results.
- **DemaConsulting.TestResults.IO** — `TrxSerializer` and `JUnitSerializer` for serializing results to `.trx` and
  `.xml` files.

#### Callers

- **Program** — calls `Validation.Run(context)` when the `--validate` flag is set.
