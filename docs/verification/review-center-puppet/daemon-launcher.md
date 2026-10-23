## DaemonLauncher

### Verification Approach

`DaemonLauncher` is verified with unit tests defined in `DaemonLauncherTests.cs`. Only the pure part of the unit,
`BuildCommandLine`, can be verified in isolation: the tests call it with arguments that contain spaces, quotes, and
trailing backslashes and compare the result with the expected command line text. On Windows, a further test splits
the built command line with the operating system's own splitter (`CommandLineToArgvW`) and checks that every
argument comes back unchanged, which is the behavior the daemon depends on.

Creating a detached process, the fallback when the caller's job object forbids breakaway, and the survival of the
daemon after the launching command has ended cannot be asserted in a unit test without leaving processes behind.
They are exercised by the real-browser end-to-end test
`PuppetEndToEnd_HelloAndCounter_DrivenThroughCommands_ShowGreetingAndCount`, in which every command is a separate
process and the daemon must therefore survive between them, and by a manual demonstration. The end-to-end test is
requirement-linked evidence for the Puppet system requirements, and CI runs it with Chromium installed.

### Test Environment

N/A - standard test environment. The command-line tests need no processes, files, or network access; the identity
tests use fakes except for one that starts a real child process. The class runs in the serial `Sequential` xUnit
collection because of that launch test.

### Acceptance Criteria

- All unit tests pass with zero failures.
- The identity reported for a launched process is that of the process that was created: it is alive by the session
  store's own check, and the identity-checked kill ends it.
- The session id and the server path reach the daemon unchanged, including when they contain spaces, quotes, or a
  trailing backslash (the round-trip scenario runs on Windows only).
- An executable path and arguments that contain spaces are each enclosed in double quotes.
- An embedded double quote is escaped so that it cannot split an argument or inject an option.

### Test Scenarios

**DaemonLauncher_BuildCommandLine_SpecialArguments_RoundTripThroughWindowsParsing**: On Windows, command lines are
built for arguments with spaces, embedded quotes, a quote followed by a backslash, trailing backslashes, an injected
option, and an empty value, followed by another argument. The Windows command-line splitter reads each line back and
returns the executable, the verb, the original argument, and the following argument unchanged. The test is skipped
with a stated reason on other platforms, so the requirement uses a Windows source filter. This scenario is tested
by `DaemonLauncher_BuildCommandLine_SpecialArguments_RoundTripThroughWindowsParsing`.

**DaemonLauncher_BuildCommandLine_PathsWithSpaces_AreQuoted**: An executable path and arguments that contain spaces
are built into a command line in which the executable and every argument are enclosed in double quotes. This
scenario is tested by `DaemonLauncher_BuildCommandLine_PathsWithSpaces_AreQuoted`.

**DaemonLauncher_BuildCommandLine_EmbeddedQuote_IsEscaped**: An argument that contains a quote and an injected
option is built into the command line `"p.exe" "a\" --evil \"b"`, which keeps it a single argument. This scenario is
tested by `DaemonLauncher_BuildCommandLine_EmbeddedQuote_IsEscaped`.

**DaemonLauncher_LaunchProcess_ReportsIdentityOfTheCreatedProcess**: A real idle executable (`ping` on Windows,
`sleep` elsewhere) is launched through `LaunchProcess`. The reported process id and start time are accepted by
`SessionStore.IsProcessAlive`, `SessionStore.KillProcess` with that identity ends the process, and the process is
then reported as gone; a `finally` block ends the process if an assertion fails first. This scenario is tested by
`DaemonLauncher_LaunchProcess_ReportsIdentityOfTheCreatedProcess`.

**DaemonLauncher_CaptureIdentity_IdentityCannotBeRead_EndsCreatedProcessAndRethrows**: The identity capture of a
just-created process fails (as when the start time cannot be read). The terminate action that stands for the created
process is run, and the caller receives the original `PuppetException`. This scenario is tested by
`DaemonLauncher_CaptureIdentity_IdentityCannotBeRead_EndsCreatedProcessAndRethrows`.

**DaemonLauncher_CaptureIdentity_TerminationFailsOrNotNeeded_OriginalOutcomeIsKept**: A failing capture whose
termination also fails (the process is already gone) still surfaces the original failure, and a successful capture
returns its identity without ending the process. This scenario is tested by
`DaemonLauncher_CaptureIdentity_TerminationFailsOrNotNeeded_OriginalOutcomeIsKept`.
