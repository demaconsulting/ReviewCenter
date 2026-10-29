## SelfTest

![SelfTest Structure](SelfTestView.svg)

### Overview

The `SelfTest` subsystem provides the self-validation framework for the ReviewCenter.
It is invoked when the user passes `--validate` on the command line. The subsystem runs a
built-in suite of real, hermetic checks of the tool's own capabilities (git, a complete review
workflow, the content fingerprint, password hashing, the one-time password algorithm, the audit chain, the signed
attestation PDF
and the web host), prints a pass/fail summary with the elapsed time of every check, and
optionally writes the results to a TRX or JUnit XML file for CI/CD integration. With
`--check-config` it adds a tenth, read-only check of the configured database. The
`SelfTest` subsystem contains two units: `Validation` (the runner, the report and the results
file) and `ValidationChecks` (the checks themselves).

### Interfaces

**Validation.Run**: Runs the self-validation checks, prints a summary, and optionally writes a
results file.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Accepts a `Context` argument. Prints a Markdown-formatted heading (depth
  controlled by `context.HeadingDepth`) and a table of environment metadata, runs the nine
  default checks (ten with `context.CheckConfig`), prints one line per check with its outcome and
  elapsed time, prints aggregate totals (`Total Tests:`, `Passed:`, `Skipped:`, `Failed:`,
  `Total Time:`), and writes a results file if `context.ResultsFile` is set. Calls
  `context.WriteError` for each failed check and for unsupported results file extensions,
  causing `context.ExitCode` to return 1. A skipped check does not fail the run.
- *Constraints*: Throws `ArgumentNullException` if `context` is null. Every exception of a check
  is recorded as a failed test so that one failure does not prevent the remaining checks from
  running. The default run never reads the configured database or configuration and never binds
  the configured port; every file it creates lives in a temporary folder that is removed at the
  end.

### Design

The `SelfTest` subsystem contains the `Validation` unit and the `ValidationChecks` unit. When
`Program.Run` detects the `--validate` flag, it calls `Validation.Run(context)`. The flow within
`Validation.Run` is:

1. `PrintValidationHeader` writes a Markdown heading and a table containing tool version,
   machine name, OS description, .NET runtime description, and timestamp.
2. A `DemaConsulting.TestResults.TestResults` object is constructed to accumulate results.
3. `CreateChecks` lists the checks. Every check is started with `Task.Run`, so the checks run
   at the same time (the review workflow and the host start overlap instead of adding up), each
   one in its own `TemporaryDirectory`. `ValidationRunner.ExecuteAsync` runs one check under a
   60 second limit, times it, and turns its result or exception into a recorded outcome and a
   report line. `ValidationRunner.Report` then records the outcomes and prints the lines in the
   listed order, so the report is stable. The elapsed time of a line is that of the check
   itself, so with checks running at the same time the times overlap and do not add up to the
   total.
4. Totals are printed; `WriteError` is used for the failed count if any tests failed.
5. If `context.ResultsFile` is set, `WriteResultsFile` serializes the results. An unsupported
   file extension causes `WriteError` to be called and no file is written.

The checks are in the `SelfTest.Checks` namespace; see the `ValidationChecks` unit. The
`TemporaryDirectory` that holds the files of a check is a unit of the `Utilities` subsystem.
