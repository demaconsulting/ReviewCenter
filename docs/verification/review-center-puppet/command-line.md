## CommandLine

### Verification Approach

`CommandLine` is verified with unit tests defined in `CommandLineTests.cs`. The class is pure parsing logic without
side effects, so the tests pass argument arrays to `CommandLine.Parse` and assert on the parsed properties or on the
`ArgumentException` that is thrown. No mocks or files are involved.

### Test Environment

N/A - standard test environment.

### Acceptance Criteria

- All unit tests pass with zero failures.
- The default timeout is 10 seconds, and `--timeout` overrides it.
- Timeouts of 0, 601, a negative number, and non-numeric text are rejected.
- Session ids with path characters, spaces, no characters, or more than 32 characters are rejected.
- `--out` and `--full-page` are accepted only for `screenshot`, and `--server` only for `start`.
- An unknown or missing command, a wrong number of arguments, an unknown option, and an option without a value are
  rejected.

### Test Scenarios

**CommandLine_Parse_NoTimeout_UsesTenSecondDefault**: A `click` command without options parses to the verb `click`
with one positional argument and a timeout of 10 seconds (10000 milliseconds). This scenario is tested by
`CommandLine_Parse_NoTimeout_UsesTenSecondDefault`.

**CommandLine_Parse_Timeout_OverridesDefault**: A `click` command with `--timeout 42` parses to a timeout of 42
seconds. This scenario is tested by `CommandLine_Parse_Timeout_OverridesDefault`.

**CommandLine_Parse_InvalidTimeout_ThrowsArgumentException**: The timeout values `0`, `601`, `-5`, and `abc` each
cause an `ArgumentException`. This scenario is tested by `CommandLine_Parse_InvalidTimeout_ThrowsArgumentException`.

**CommandLine_Parse_Session_IsCaptured**: `--session demo-1` is captured as the session id. This scenario is tested
by `CommandLine_Parse_Session_IsCaptured`.

**CommandLine_Parse_InvalidSessionId_ThrowsArgumentException**: Session ids with a path traversal, a slash, a space,
an empty id, and an id longer than 32 characters each cause an `ArgumentException`. This scenario is tested by
`CommandLine_Parse_InvalidSessionId_ThrowsArgumentException`.

**CommandLine_Parse_ScreenshotOptions_AreCaptured**: `screenshot --out shot.png --full-page` captures the output
path and the full-page flag. This scenario is tested by `CommandLine_Parse_ScreenshotOptions_AreCaptured`.

**CommandLine_Parse_OutOnNonScreenshot_ThrowsArgumentException**: `click` with `--out` causes an
`ArgumentException`. This scenario is tested by `CommandLine_Parse_OutOnNonScreenshot_ThrowsArgumentException`.

**CommandLine_Parse_ServerOnStart_IsCaptured**: `start --server x.dll` captures the server path, and `list --server
x.dll` is rejected. This scenario is tested by `CommandLine_Parse_ServerOnStart_IsCaptured`.

**CommandLine_Parse_UnknownCommand_ThrowsArgumentException**: The command `dance` causes an `ArgumentException`
whose message names the command. This scenario is tested by
`CommandLine_Parse_UnknownCommand_ThrowsArgumentException`.

**CommandLine_Parse_NoArguments_ThrowsArgumentException**: An empty argument array causes an `ArgumentException`.
This scenario is tested by `CommandLine_Parse_NoArguments_ThrowsArgumentException`.

**CommandLine_Parse_WrongArgumentCount_ThrowsArgumentException**: `click` without a selector, `click` with two
selectors, `fill` with one argument, and `stop` with an extra argument each cause an `ArgumentException`. This
scenario is tested by `CommandLine_Parse_WrongArgumentCount_ThrowsArgumentException`.

**CommandLine_Parse_UnknownOption_ThrowsArgumentException**: `list --bogus` causes an `ArgumentException`. This
scenario is tested by `CommandLine_Parse_UnknownOption_ThrowsArgumentException`.

**CommandLine_Parse_OptionWithoutValue_ThrowsArgumentException**: `stop --session` without a value causes an
`ArgumentException`. This scenario is tested by `CommandLine_Parse_OptionWithoutValue_ThrowsArgumentException`.
