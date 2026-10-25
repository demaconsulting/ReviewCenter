## Cli

### Verification Approach

The `Cli` subsystem is verified by integration tests defined in `CliSubsystemTests.cs`. Each
test exercises `Context.Create` and `Program.Run` together, treating the pair as the observable
subsystem interface. Tests pass controlled argument arrays and assert on captured console output,
file-system side-effects, and exit codes. `Validation` (part of the `SelfTest` subsystem)
executes its real logic in scenarios that exercise the `--validate` path; no mocking is applied
at any level.

### Test Environment

N/A - standard test environment.

### Acceptance Criteria

- All integration tests pass with zero failures.
- All supported flags produce the correct observable behavior when passed through `Context.Create`
  and `Program.Run`.
- Invalid arguments produce exit code 1 and an error message.
- Log files and results files are created at the specified paths when the corresponding flags
  are used.
- The `serve` verb and the `--urls` option flow from `Context.Create` through `Program.Run` to a
  server that answers on the requested address, and `--urls` without `serve` is rejected.
- The `user add` verb and its options flow from `Context.Create` into the `Context`, the help text documents them
  and the password sources, and misuse is rejected.

### Test Scenarios

**CliSubsystem_VersionFlow_ContextAndProgram_DisplaysVersionAndExits**: Arguments `["--version"]`
are passed through `Context.Create` and `Program.Run`; standard output contains the version
string and exit code is 0. This scenario is tested by
`CliSubsystem_VersionFlow_ContextAndProgram_DisplaysVersionAndExits`.

**CliSubsystem_VersionFlow_ContextAndProgram_DisplaysVersionAndExits_WithShortVFlag**: Arguments
`["-v"]` are passed through `Context.Create` and `Program.Run`; standard output contains the
version string and exit code is 0. This scenario is tested by
`CliSubsystem_VersionFlow_ContextAndProgram_DisplaysVersionAndExits_WithShortVFlag`.

**CliSubsystem_HelpFlow_ContextAndProgram_DisplaysHelpAndExits**: Arguments `["--help"]` are
passed through `Context.Create` and `Program.Run`; standard output contains help text and exit
code is 0. This scenario is tested by
`CliSubsystem_HelpFlow_ContextAndProgram_DisplaysHelpAndExits`.

**CliSubsystem_HelpFlow_ContextAndProgram_DisplaysHelpAndExits_WithShortQuestionFlag**: Arguments
`["-?"]` are passed; standard output contains help text and exit code is 0. This scenario is
tested by
`CliSubsystem_HelpFlow_ContextAndProgram_DisplaysHelpAndExits_WithShortQuestionFlag`.

**CliSubsystem_HelpFlow_ContextAndProgram_DisplaysHelpAndExits_WithShortHFlag**: Arguments
`["-h"]` are passed; standard output contains help text and exit code is 0. This scenario is
tested by `CliSubsystem_HelpFlow_ContextAndProgram_DisplaysHelpAndExits_WithShortHFlag`.

**CliSubsystem_ValidateFlow_ContextAndProgram_RunsValidationAndExits**: Arguments `["--validate"]`
are passed; standard output contains `"Total Tests:"` and exit code is 0. This scenario is
tested by `CliSubsystem_ValidateFlow_ContextAndProgram_RunsValidationAndExits`.

**CliSubsystem_SilentFlow_ContextAndProgram_SuppressesOutput**: Arguments `["--version", "--silent"]`
are passed; standard output is empty and exit code is 0, confirming `--silent` suppresses all
console output. This scenario is tested by
`CliSubsystem_SilentFlow_ContextAndProgram_SuppressesOutput`.

**CliSubsystem_ResultsFlow_ContextAndProgram_WritesResultsFile**: Arguments
`["--validate", "--results", "<tmp>.trx"]` are passed; a results file is created at the
specified path and exit code is 0. This scenario is tested by
`CliSubsystem_ResultsFlow_ContextAndProgram_WritesResultsFile`.

**CliSubsystem_LogFlow_ContextAndProgram_WritesLogFile**: Arguments `["--log", "<tmp>.log"]` are
passed; a log file is created at the specified path and exit code is 0. This scenario is tested
by `CliSubsystem_LogFlow_ContextAndProgram_WritesLogFile`.

**CliSubsystem_InvalidArgs_ContextAndProgram_RejectsUnknownArgumentsAndExitsNonZero**: Arguments
`["--unknown-flag"]` are passed directly to `Program.Main`; exit code is 1 and standard error
contains an error message including the unknown flag. This scenario is tested by
`CliSubsystem_InvalidArgs_ContextAndProgram_RejectsUnknownArgumentsAndExitsNonZero`.

**CliSubsystem_ErrorOutput_ContextAndProgram_WritesErrorToStderr**: A `Context` is created with
no arguments and `WriteError` is called with a known message; standard error receives the
message and exit code is non-zero. This scenario is tested by
`CliSubsystem_ErrorOutput_ContextAndProgram_WritesErrorToStderr`.

**CliSubsystem_ResultAliasFlow_ContextAndProgram_WritesResultsFile**: Arguments
`["--validate", "--result", "<tmp>.trx"]` (legacy alias) are passed; a results file is created
at the specified path and exit code is 0. This scenario is tested by
`CliSubsystem_ResultAliasFlow_ContextAndProgram_WritesResultsFile`.

**CliSubsystem_DepthFlow_ContextAndProgram_AdjustsHeadingDepth**: Arguments `["--depth", "2"]`
are passed; the heading depth is set to 2 and exit code is 0, confirming the `--depth` flag is
correctly parsed and propagated through `Context`. This scenario is tested by
`CliSubsystem_DepthFlow_ContextAndProgram_AdjustsHeadingDepth`.

**CliSubsystem_ServeFlow_ContextAndProgram_ServesOnRequestedUrlAndStops**: Arguments
`["serve", "--urls", "<loopback url>", "--silent"]` are parsed by `Context.Create`, which records
the verb and the URL; `Program.RunWithCancellation` then runs on a background task, the health
endpoint answers on the requested URL, and the call returns after cancellation. This scenario is
tested by `CliSubsystem_ServeFlow_ContextAndProgram_ServesOnRequestedUrlAndStops`.

**CliSubsystem_UrlsWithoutServe_Context_RejectsArgumentCombination**: Arguments
`["--urls", "http://127.0.0.1:6001"]` are passed to `Context.Create`; an `ArgumentException` is
thrown because `--urls` requires the `serve` verb. This scenario is tested by
`CliSubsystem_UrlsWithoutServe_Context_RejectsArgumentCombination`.

**CliSubsystem_UserAddFlow_ContextAndProgram_ParsesOptionsDocumentsThemAndRejectsMisuse**: Arguments `["user",
"add", "--username", "ann", "--display-name", "Ann", "--role", "Admin"]` are parsed by `Context.Create`, which
records the verb and the three options. `Program.Run` with `--help` prints a help text that names `user add` and the
`REVIEWCENTER_PASSWORD` variable, `user add` without names is rejected, and `--username` without the verb is
rejected, each with an `ArgumentException`. This scenario is tested by
`CliSubsystem_UserAddFlow_ContextAndProgram_ParsesOptionsDocumentsThemAndRejectsMisuse`.

**Audit verify verb**: The unit tests verify the flow of the `audit verify` verb. `Context.Create` with the arguments
`audit verify` sets `AuditVerify`, which is tested by `Context_Create_AuditVerifyVerb_SetsAuditVerify`, and the help
text printed by `Program.Run` names `audit verify`, which is tested by `Program_Run_Help_DocumentsAuditVerify`. No
subsystem test runs the dispatch of `Program` to the command; that part is verified by the tests of
`AuditCommand` in the `Hosting` subsystem and by inspection.
