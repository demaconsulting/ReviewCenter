## Program

### Verification Approach

`Program` is verified with unit tests defined in `ProgramTests.cs`. Because `Program` directly
instantiates `Context` from real arguments and calls `Validation.Run` when needed, no mocking
is required. Tests pass controlled argument arrays and assert on captured console output and
exit codes.

### Test Environment

N/A - standard test environment.

### Acceptance Criteria

- All unit tests pass with zero failures.
- Exit code 0 is returned for all valid flag combinations.
- Exit code 1 is returned when invalid arguments are supplied to `Program.Main`.
- `Program.Version` returns a non-empty, non-null string.
- The `serve` verb starts the server, which stops on cancellation with exit code 0; a start-up
  failure is reported through exit code 1 and not an exception.
- The help text lists the `serve` verb and the `--urls` option with its default value.
- The help text lists the `user add` verb, its options, and the password sources.
- The help text lists the `--check-config` option, the `audit verify` verb, and the `keys reprotect` verb.
- The `user add`, `audit verify`, and `keys reprotect` verbs are dispatched to their commands by `Program.Run`,
  against the database named by the `Database__ConnectionString` environment variable; the tests put the previous
  values of the environment variables back.

### Test Scenarios

**Program_Run_WithVersionFlag_DisplaysVersionOnly**: `Program.Run` is called with a context
created from `["--version"]`; the output contains the version string, "Copyright" does not
appear, the banner prefix does not appear, and exit code is 0, confirming version-only output
with no banner. This scenario is tested by `Program_Run_WithVersionFlag_DisplaysVersionOnly`.

**Program_Run_WithHelpFlag_DisplaysUsageInformation**: `Program.Run` is called with a context
from `["--help"]`; the output contains "Usage:", "Options:", "--version", and "--help" and exit
code is 0. This scenario is tested by `Program_Run_WithHelpFlag_DisplaysUsageInformation`.

**Program_Run_WithValidateFlag_RunsValidation**: `Program.Run` is called with a context from
`["--validate"]`; the output contains "Total Tests:" and exit code is 0, confirming the
self-validation suite is invoked and completes successfully. This scenario is tested by
`Program_Run_WithValidateFlag_RunsValidation`.

**Program_Run_NoArguments_DisplaysDefaultBehavior**: `Program.Run` is called with an empty
argument array; the output contains the banner line with the tool name and the version, the copyright notice, and
exit code is 0. This scenario is tested by `Program_Run_NoArguments_DisplaysDefaultBehavior`.

**Program_Version_ReturnsNonEmptyString**: The `Program.Version` static property is read; the
returned string is non-empty and non-null, confirming the version is resolvable from the
assembly attributes. This scenario is tested by `Program_Version_ReturnsNonEmptyString`.

**Program_Main_WithInvalidArgs_ReturnsNonZeroExitCode**: `Program.Main` is invoked with
`["--invalid-argument"]`; the exit code is 1, confirming that invalid arguments are caught and
handled without re-throwing. This scenario is tested by
`Program_Main_WithInvalidArgs_ReturnsNonZeroExitCode`.

**Program_Run_WithShortVersionFlag_DisplaysVersion**: `Program.Run` is called with a context
from `["-v"]`; the output contains the version string and exit code is 0. This scenario is
tested by `Program_Run_WithShortVersionFlag_DisplaysVersion`.

**Program_Run_WithShortHelpFlag_DisplaysUsage**: `Program.Run` is called with a context from
`["-h"]`; the output contains "Usage:" and "Options:" and exit code is 0. This scenario is
tested by `Program_Run_WithShortHelpFlag_DisplaysUsage`.

**Program_Run_WithQuestionMarkFlag_DisplaysUsage**: `Program.Run` is called with a context from
`["-?"]`; the output contains "Usage:" and "Options:" and exit code is 0. This scenario is
tested by `Program_Run_WithQuestionMarkFlag_DisplaysUsage`.

**Program_Run_WithServeVerb_StartsServerAndStopsOnCancellation**: A silent context is created
from `["serve", "--urls", "<loopback url>"]` using a free port, and `Program.RunWithCancellation`
runs on a background task; the health endpoint answers `Healthy`, and after the cancellation
token is cancelled the call returns. This scenario is tested by
`Program_Run_WithServeVerb_StartsServerAndStopsOnCancellation`.

**Program_Run_WithServeVerb_PortInUse_ReportsError**: A loopback port is occupied by a listener
and `Program.Run` is called with a silent context for `serve` on that port; the exit code is 1
and no exception escapes. This scenario is tested by
`Program_Run_WithServeVerb_PortInUse_ReportsError`.

**Program_Run_WithHelpFlag_ListsServeVerbAndUrlsOption**: `Program.Run` is called with a context
from `["--help"]`; the output contains "serve", "--urls", and the default address
`http://localhost:5000`. This scenario is tested by
`Program_Run_WithHelpFlag_ListsServeVerbAndUrlsOption`.

**Program_Run_WithHelpFlag_ListsCheckConfigOption**: `Program.Run` is called with a context from `["--help"]`; the
output contains "--check-config" and "--validate". This scenario is tested by
`Program_Run_WithHelpFlag_ListsCheckConfigOption`.

**Program_Run_WithHelpFlag_ListsUserAddVerbAndOptions**: `Program.Run` is called with a context from `["--help"]`;
the output contains "user add", "--username", "--display-name", and "--system-admin". This scenario is tested by
`Program_Run_WithHelpFlag_ListsUserAddVerbAndOptions`.

**Program_Run_WithHelpFlag_ListsUserAddPasswordSources**: `Program.Run` is called with a context from `["--help"]`;
the output states that the password is read from the `REVIEWCENTER_PASSWORD` environment variable, else standard
input, else a prompt. This scenario is tested by `Program_Run_WithHelpFlag_ListsUserAddPasswordSources`.

**Program_Run_Help_DocumentsAuditVerify**: `Program.Run` is called with a context from `["--help"]`; the output names
`audit verify`. This scenario is tested by `Program_Run_Help_DocumentsAuditVerify`.

**Program_Run_Help_DocumentsKeysReprotect**: `Program.Run` is called with a context from `["--help"]`; the output names
`keys reprotect`. This scenario is tested by `Program_Run_Help_DocumentsKeysReprotect`.

**Program_Run_WithUserAddVerb_PasswordFromEnvironment_CreatesUser**: The `Database__ConnectionString` and
`REVIEWCENTER_PASSWORD` environment variables are set to a new database and a password, and `Program.Run` is called
with a context from `["user", "add", "--username", "dispatch.user", "--display-name", "Dispatch User"]`; the exit
code is 0, the output reports the created user and does not contain the password, and the database holds exactly one
user with that name. The previous values of the variables are put back. This scenario is tested by
`Program_Run_WithUserAddVerb_PasswordFromEnvironment_CreatesUser`.

**Program_Run_WithUserAddVerb_DuplicateUser_ReportsErrorWithExitCodeOne**: The user of the previous scenario is
created, and `Program.Run` is called with the same arguments again; the exit code is 1, the error output contains
"already exists", the password is not printed, and the database still holds exactly one user with that name. This
scenario is tested by `Program_Run_WithUserAddVerb_DuplicateUser_ReportsErrorWithExitCodeOne`.

**Program_Run_WithAuditVerifyVerb_MissingDatabase_ReportsErrorWithExitCodeOne**: The `Database__ConnectionString`
environment variable names a database file in a folder that does not exist, and `Program.Run` is called with a
context from `["audit", "verify"]`; the exit code is 1, the error output contains "The database file does not exist",
and neither the file nor its folder was created. The message comes from `AuditCommand`, so the verb reached it. This
scenario is tested by `Program_Run_WithAuditVerifyVerb_MissingDatabase_ReportsErrorWithExitCodeOne`; the command
itself is verified by the `AuditCommand` tests.

**Program_Run_WithKeysReprotectVerb_MissingDatabase_ReportsErrorWithExitCodeOne**: The set-up is the same, and
`Program.Run` is called with a context from `["keys", "reprotect"]`; the exit code is 1, the error output contains
"The database file does not exist", and neither the file nor its folder was created. The message comes from
`KeysCommand`, so the verb reached it. This scenario is tested by
`Program_Run_WithKeysReprotectVerb_MissingDatabase_ReportsErrorWithExitCodeOne`; the command itself is verified by the
`KeysCommand` tests.
