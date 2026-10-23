## PuppetProgram

### Verification Approach

`PuppetProgram` is verified with unit tests defined in `PuppetProgramTests.cs`. Every test calls
`PuppetProgram.RunAsync` with captured output and error writers and a `PuppetServices` bundle of fakes from
`Fakes.cs`.

### Test Environment

N/A - standard test environment. The tests use only fakes and a temporary folder.

### Acceptance Criteria

- All unit tests pass with zero failures.
- `--help` prints the usage text and returns 0.
- Missing or unknown verbs, a missing steps file, unknown options, a bad `--timeout`, a missing option value,
  `--url` together with `--server-dll`, and an unreadable steps file all return exit code 2 with a message that names
  the problem and start nothing.
- With `--url`, no server is started and the page is driven at the given URL.

### Test Scenarios

**PuppetProgram_Run_Help_PrintsUsageAndExitsZero**: `--help` prints a line starting with `Usage: puppet run` and
returns 0; the collaborators are not used. Tested by `PuppetProgram_Run_Help_PrintsUsageAndExitsZero`.

**PuppetProgram_Run_BadArguments_ExitsTwoWithMessage**: Eight bad command lines each return exit code 2 and a
message containing the expected text. Tested by `PuppetProgram_Run_BadArguments_ExitsTwoWithMessage`.

**PuppetProgram_Run_UrlOption_UsesGivenServerWithoutStartingOne**: A run with `--url` over a fake page succeeds, the
page is sent to the given URL, and the server starter is never called. Tested by
`PuppetProgram_Run_UrlOption_UsesGivenServerWithoutStartingOne`.
