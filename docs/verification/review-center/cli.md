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
- Invalid arguments produce exit code 1 and an error message, for an unknown argument, for a malformed value, and for
  an option whose value is missing.
- Log files and results files are created at the specified paths when the corresponding flags
  are used, and a log file that cannot be opened produces exit code 1 and an error message.
- The `serve` verb and the `--urls` option flow from `Context.Create` through `Program.Run` to a
  server that answers on the requested address, and `--urls` without `serve` is rejected.
- The `user add` verb and its options flow from `Context.Create` into the `Context`, the help text documents them
  and the password sources, an ordinary user is requested without the switch, and misuse (missing names, options
  without the verb, a password option, a missing or other action) is rejected.
- The `audit verify` and `keys reprotect` verbs are recorded by `Context.Create`, are documented in the help text,
  and the verbs without their action or with another action are rejected with exit code 1.

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

**CliSubsystem_InvalidArgs_ContextAndProgram_RejectsMalformedValueAndExitsNonZero**: Each of `["--depth", "abc"]`,
`["--depth", "0"]`, `["--depth", "7"]`, `["serve", "--urls", "not-a-url"]`, and `["serve", "--urls",
"ftp://localhost:21"]` is passed directly to `Program.Main`; for each, exit code is 1 and standard error contains an
error message that names the option (`--depth` or `--urls`). This scenario is tested by
`CliSubsystem_InvalidArgs_ContextAndProgram_RejectsMalformedValueAndExitsNonZero`.

**CliSubsystem_InvalidArgs_ContextAndProgram_RejectsMissingValueAndExitsNonZero**: Each of `["--log"]`,
`["--results"]`, `["--result"]`, `["--depth"]`, and `["serve", "--urls"]` (the option is last, without its value) is
passed directly to `Program.Main`; for each, exit code is 1 and standard error contains an error message that names
the option. This scenario is tested by
`CliSubsystem_InvalidArgs_ContextAndProgram_RejectsMissingValueAndExitsNonZero`.

**CliSubsystem_LogFlow_ContextAndProgram_UnopenableLogFile_ReportsErrorAndExitsNonZero**: Arguments `["--version",
"--log", "<temp folder>"]` (a log path that is a directory) are passed directly to `Program.Main`; exit code is 1 and
standard error says that the log file could not be opened. This scenario is tested by
`CliSubsystem_LogFlow_ContextAndProgram_UnopenableLogFile_ReportsErrorAndExitsNonZero`.

**CliSubsystem_ErrorOutput_ContextAndProgram_WritesErrorToStderr**: A `Context` is created with
no arguments and `WriteError` is called with a known message; standard error receives the
message and exit code is non-zero. This scenario is tested by
`CliSubsystem_ErrorOutput_ContextAndProgram_WritesErrorToStderr`.

**CliSubsystem_ResultAliasFlow_ContextAndProgram_ParsesResultsFile**: Arguments
`["--validate", "--silent", "--result", "<tmp>.trx"]` (legacy alias) are parsed by `Context.Create`; the results
file is the given path, the validate flag is set, exit code is 0, and parsing writes no file. This scenario is tested
by `CliSubsystem_ResultAliasFlow_ContextAndProgram_ParsesResultsFile`.

**CliSubsystem_CheckConfigFlow_Context_WithValidate_IsParsed**: `["--validate", "--check-config", "--silent"]` is
parsed by `Context.Create` and sets both `Validate` and `CheckConfig`. This scenario is tested by
`CliSubsystem_CheckConfigFlow_Context_WithValidate_IsParsed`.

**CliSubsystem_CheckConfigFlow_ContextAndProgram_RequiresValidate**: `["--check-config"]` alone is parsed by
`Context.Create` and rejected with an `ArgumentException` that says `--check-config requires --validate`. This
scenario is tested by `CliSubsystem_CheckConfigFlow_ContextAndProgram_RequiresValidate`.

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

**CliSubsystem_UserAddFlow_ContextAndProgram_ParsesOptionsAndDocumentsThem**: Arguments `["user",
"add", "--username", "ann", "--display-name", "Ann", "--system-admin"]` are parsed by `Context.Create`, which
records the verb, the two options, and the switch. `Program.Run` with `--help` prints a help text that names
`user add` and the `REVIEWCENTER_PASSWORD` variable. This scenario is tested by
`CliSubsystem_UserAddFlow_ContextAndProgram_ParsesOptionsAndDocumentsThem`.

**CliSubsystem_UserAddFlow_ContextAndProgram_MissingNames_AreRejected**: `user add` without both names, with only the
user name, and with only the display name is parsed by `Context.Create`; each is rejected with an `ArgumentException`
whose message says that `--username` and `--display-name` are required. This scenario is tested by
`CliSubsystem_UserAddFlow_ContextAndProgram_MissingNames_AreRejected`.

**CliSubsystem_UserAddFlow_ContextAndProgram_OptionsWithoutVerb_AreRejected**: Each of `--username`, `--display-name`,
and `--system-admin` is parsed without the verb; each is rejected with an `ArgumentException` whose message says that
the user add verb is required. This scenario is tested by
`CliSubsystem_UserAddFlow_ContextAndProgram_OptionsWithoutVerb_AreRejected`.

**CliSubsystem_UserAddFlow_ContextAndProgram_WrongAction_RejectsAndExitsNonZero**: `["user"]` and `["user",
"remove"]` are passed directly to `Program.Main`; for each, exit code is 1 and standard error says that `user`
requires the add action. This scenario is tested by
`CliSubsystem_UserAddFlow_ContextAndProgram_WrongAction_RejectsAndExitsNonZero`.

**CliSubsystem_UserAddFlow_Context_WithoutSwitch_RequestsOrdinaryUser**: Arguments `["user", "add", "--username",
"ann", "--display-name", "Ann"]` are parsed by `Context.Create`; the verb is recorded and `SystemAdmin` is false, so an
ordinary user is requested. This scenario is tested by `CliSubsystem_UserAddFlow_Context_WithoutSwitch_RequestsOrdinaryUser`.

**CliSubsystem_UserAddFlow_ContextAndProgram_PasswordOption_IsRejected**: Arguments `["user", "add", "--username",
"ann", "--display-name", "Ann", "--password", "secret-value"]` are passed directly to `Program.Main`; exit code is 1,
standard error says that `--password` is an unsupported argument, and the password value is not echoed. This scenario
is tested by `CliSubsystem_UserAddFlow_ContextAndProgram_PasswordOption_IsRejected`.

**CliSubsystem_AuditVerifyFlow_ContextAndProgram_ParsesVerbAndDocumentsIt**: Arguments `["audit", "verify"]` are parsed
by `Context.Create`, which records `AuditVerify` and no other verb, and `Program.Run` with `--help` prints a help text
that names `audit verify`. This scenario is tested by
`CliSubsystem_AuditVerifyFlow_ContextAndProgram_ParsesVerbAndDocumentsIt`. The dispatch of `Program` to the command is
verified by the `Program` unit tests, and the command itself by the tests of `AuditCommand` in the `Hosting`
subsystem.

**CliSubsystem_AuditVerifyFlow_ContextAndProgram_RejectsMisuseAndExitsNonZero**: A theory over `audit` alone and
`audit export` passed directly to `Program.Main`; exit code is 1 and standard error says that `audit` requires the
`verify` action. This scenario is tested by
`CliSubsystem_AuditVerifyFlow_ContextAndProgram_RejectsMisuseAndExitsNonZero`.

**CliSubsystem_KeysReprotectFlow_ContextAndProgram_ParsesVerbAndDocumentsIt**: Arguments `["keys", "reprotect"]` are
parsed by `Context.Create`, which records `KeysReprotect` and no other verb, and `Program.Run` with `--help` prints a
help text that names `keys reprotect`. This scenario is tested by
`CliSubsystem_KeysReprotectFlow_ContextAndProgram_ParsesVerbAndDocumentsIt`. The dispatch of `Program` to the command
is verified by the `Program` unit tests, and the command itself by the tests of `KeysCommand` in the `Hosting`
subsystem.

**CliSubsystem_KeysReprotectFlow_ContextAndProgram_RejectsMisuseAndExitsNonZero**: A theory over `keys` alone and
`keys rotate` passed directly to `Program.Main`; exit code is 1 and standard error says that `keys` requires the
`reprotect` action. This scenario is tested by
`CliSubsystem_KeysReprotectFlow_ContextAndProgram_RejectsMisuseAndExitsNonZero`.
