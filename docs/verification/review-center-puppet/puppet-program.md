## PuppetProgram

### Verification Approach

`PuppetProgram` is verified with unit tests defined in `PuppetProgramTests.cs`. Every test calls
`PuppetProgram.RunAsync` with captured output and error writers and a `PuppetServices` bundle built around a
`SessionStore` in a temporary folder. The store's liveness check and kill action are replaced by delegates, and the
daemon launcher, server runner, and browser launcher are fakes from `Fakes.cs`. A fake launcher can publish a
session file the moment it is launched, which stands in for a daemon that starts, fails, or never appears. One test
runs a real `DaemonHost` over fakes in the background, so that commands travel through the real control channel.

### Test Environment

N/A - standard test environment. Each test creates its own temporary session folder with the `TempDirectory`
helper, and the tests need permission to open loopback TCP ports for the one test that uses the control channel.

### Acceptance Criteria

- All unit tests pass with zero failures.
- `start` prints the session id and server URL when the daemon reports ready, generates an eight-character id when
  none is given, and refuses to replace a live session without launching anything.
- `start` returns exit code 4 with the reason when the daemon reports failure, dies early, or the server assembly is
  missing, and no failed session file remains.
- Of several start commands for one id, only one launches a daemon; the others return exit code 1 with a message
  that the session is already being started; a failed launch gives the id back.
- A start that times out, is cancelled, or whose daemon dies ends the launched daemon (identified by process id and
  start time, even when no session file exists), also ends the server and the browser a partial session file
  records, and leaves no session state behind.
- Commands are forwarded to the daemon and its answer is printed; a failed command returns exit code 1, and `stop`
  ends the daemon and removes the session file.
- `list` reports "No active sessions." when there are none and reports and removes stale sessions.
- An unknown session or no running session returns exit code 2; a stale session returns exit code 3 and its file is
  removed; invalid arguments return exit code 1 with a message.

### Test Scenarios

**PuppetProgram_Run_StartDaemonReady_PrintsSessionAndUrl**: The fake launcher writes a ready session when
launched; `start --session demo --server <existing file>` exits with code 0 and prints the session and the server
URL, and exactly the session `demo` was launched. This scenario is tested by
`PuppetProgram_Run_StartDaemonReady_PrintsSessionAndUrl`.

**PuppetProgram_Run_StartWithoutSession_GeneratesEightCharacterId**: `start` without `--session` launches one
session whose generated id has eight characters and prints it. This scenario is tested by
`PuppetProgram_Run_StartWithoutSession_GeneratesEightCharacterId`.

**PuppetProgram_Run_StartExistingLiveSession_ExitsOne**: A live session `busy` exists; `start --session busy` exits
with code 1, the message says that it is already running, and nothing was launched. This scenario is tested by
`PuppetProgram_Run_StartExistingLiveSession_ExitsOne`.

**PuppetProgram_Run_StartDaemonFailed_ExitsFourWithReason**: The fake daemon writes a failed session with the reason
"Chromium is not installed."; `start` exits with code 4, shows the reason, and the session file is removed. This
scenario is tested by `PuppetProgram_Run_StartDaemonFailed_ExitsFourWithReason`.

**PuppetProgram_Run_StartDaemonDiesEarly_ExitsFour**: The fake launcher reports a process id that does not exist
and writes no file; `start` exits with code 4 and says the daemon exited unexpectedly, and no session file remains.
This scenario is tested by `PuppetProgram_Run_StartDaemonDiesEarly_ExitsFour`.

**PuppetProgram_Run_StartWhileAnotherStartInProgress_FailsAndLaunchesOnlyOnce**: The launch of a first `start` is
held up by the test; a second `start` for the same id exits with code 1 and says that the session is already being
started; after the first is released it succeeds, and only one daemon was launched. This scenario is tested by
`PuppetProgram_Run_StartWhileAnotherStartInProgress_FailsAndLaunchesOnlyOnce`.

**PuppetProgram_Run_ConcurrentStartsOfSameSession_ExactlyOneSucceeds**: Eight `start` commands for one id run at the
same moment; exactly one succeeds and launches, and every other exits with code 1 and an explanation. This scenario
is tested by `PuppetProgram_Run_ConcurrentStartsOfSameSession_ExactlyOneSucceeds`.

**PuppetProgram_Run_StartLaunchFails_ReleasesTheSessionAndReportsError**: The launcher cannot create the process;
`start` exits with code 1 and shows the reason, no session file exists, and the id can be reserved again. This
scenario is tested by `PuppetProgram_Run_StartLaunchFails_ReleasesTheSessionAndReportsError`.

**PuppetProgram_Run_StartTimesOutWithNoSessionFile_TerminatesLaunchedDaemon**: The launched daemon stays alive but
never writes a session file and the start time-out is short; `start` exits with code 4 saying that the session did
not become ready, and only the launched daemon was terminated, identified by the process id and start time that the
launcher reported. This scenario is tested by
`PuppetProgram_Run_StartTimesOutWithNoSessionFile_TerminatesLaunchedDaemon`.

**PuppetProgram_Run_StartTimesOutWithRecordedProcesses_TerminatesBrowserServerAndDaemon**: The daemon has recorded a
server and a browser but never becomes ready; on the time-out the browser, the server, and the daemon are each
terminated with their own start time and the session file is removed. This scenario is tested by
`PuppetProgram_Run_StartTimesOutWithRecordedProcesses_TerminatesBrowserServerAndDaemon`.

**PuppetProgram_Run_StartCancelled_TerminatesLaunchedDaemonAndCleansUp**: The caller cancels the command while the
start is waiting; the cancellation is raised to the caller after the launched daemon has been terminated and the
session state removed. This scenario is tested by `PuppetProgram_Run_StartCancelled_TerminatesLaunchedDaemonAndCleansUp`.

**PuppetProgram_Run_StartMissingServerAssembly_ExitsFourWithHint**: `start --server` names a file that does not
exist; the exit code is 4 and the message says the assembly was not found. This scenario is tested by
`PuppetProgram_Run_StartMissingServerAssembly_ExitsFourWithHint`.

**PuppetProgram_Run_ClientCommandsAgainstDaemon_ClickMissingElementAndStop**: A real `DaemonHost` runs over fakes.
`click` and `text` succeed and print their results. After the fake page is set to fail, `click` exits with code 1
and prints "No element matching". `stop` exits with code 0, the daemon task ends with code 0, and the session file is
gone. This scenario is tested by `PuppetProgram_Run_ClientCommandsAgainstDaemon_ClickMissingElementAndStop`.

**PuppetProgram_Run_ListWithNoSessions_ReportsNone**: `list` over an empty store exits with code 0 and prints "No
active sessions." This scenario is tested by `PuppetProgram_Run_ListWithNoSessions_ReportsNone`.

**PuppetProgram_Run_ListWithStaleSession_ReportsAndCleansUp**: A stored session whose daemon is not running is
listed as stale and its file is removed; a second `list` reports no sessions. This scenario is tested by
`PuppetProgram_Run_ListWithStaleSession_ReportsAndCleansUp`.

**PuppetProgram_Run_UnknownSession_ExitsTwo**: `click` for a session that was never started exits with code 2 and
names the session. This scenario is tested by `PuppetProgram_Run_UnknownSession_ExitsTwo`.

**PuppetProgram_Run_NoSessionRunning_ExitsTwo**: `snapshot` without a session name, with no session environment
variable and no running session, exits with code 2 and hints at `start`. This scenario is tested by
`PuppetProgram_Run_NoSessionRunning_ExitsTwo`.

**PuppetProgram_Run_StaleSession_ExitsThreeAndCleansUp**: `click` for a stored session whose daemon is not running
exits with code 3, the message says that the session is stale, and the file is removed. This scenario is tested by
`PuppetProgram_Run_StaleSession_ExitsThreeAndCleansUp`.

**PuppetProgram_Run_BadArguments_ExitsOneWithMessage**: An unknown command `dance` exits with code 1 and the error
output names the command. This scenario is tested by `PuppetProgram_Run_BadArguments_ExitsOneWithMessage`.

**PuppetProgram_Run_Help_ListsCommandsAndExitsZero**: `--help` exits with code 0 and lists the commands and the
Chromium installation hint. This scenario is tested by `PuppetProgram_Run_Help_ListsCommandsAndExitsZero`; it is
supplemental evidence and is not linked to a requirement.
