## PuppetProgram

### Verification Approach

`PuppetProgram` is verified with unit tests defined in `PuppetProgramTests.cs`. `ParseCommand` and `Format` are
called directly; `RunAsync` is called with captured output and error writers and a control file that does not exist.
No daemon and no browser are involved; the whole `start` and command path is covered by the end-to-end test of the
system.

### Test Environment

N/A - standard test environment.

### Acceptance Criteria

- All unit tests pass with zero failures.
- Valid command lines become requests with split arguments and options, absolute file paths, and a start URL without
  a trailing slash.
- An unknown command or option, a wrong argument count, and a malformed value return exit code 2 with a message that
  names the problem; `--help` prints the usage and returns 0.
- The result, the URL and title line, and the event lines are printed in that order; a failed command prints its
  message on the error stream and returns 1.
- With no control file, or with one whose port is dead, a command returns 2 and points at `puppet start`.

### Test Scenarios

**PuppetProgram_ParseCommand_ValidCommands_BuildRequests**: Representative command lines (`goto`, `press` with and
without a selector, `wait --timeout`, `screenshot --full --selector`, `theme`, `start --url --viewport --headed`)
become the expected requests. Tested by `PuppetProgram_ParseCommand_ValidCommands_BuildRequests`.

**PuppetProgram_Run_BadArguments_ExitsTwoWithUsage**: Bad command lines each return exit code 2 and a message
containing the expected text; `--help` returns 0 and lists the commands. Tested by
`PuppetProgram_Run_BadArguments_ExitsTwoWithUsage`.

**PuppetProgram_Run_NotRunning_ExitsTwoWithStartHint**: A command with an absent control file and one with a stale
control file both return 2 and mention `puppet start`. Tested by `PuppetProgram_Run_NotRunning_ExitsTwoWithStartHint`.

**PuppetProgram_Format_ResultUrlTitleAndErrors_PrintsFooter**: A successful response prints the result lines, the URL
and title line, and the event line and returns 0; a failed response prints its message on the error stream and
returns 1. Tested by `PuppetProgram_Format_ResultUrlTitleAndErrors_PrintsFooter`.
