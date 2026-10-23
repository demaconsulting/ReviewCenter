# ReviewCenterPuppet

## Verification Approach

System-level verification runs the whole Puppet command pipeline in a single test process through
`PuppetProgram.RunAsync`: command-line parsing, the session files on disk, the real loopback control channel, the
`DaemonHost`, and the `CommandExecutor` all take part. Only three collaborators are replaced by fakes from
`Fakes.cs`: the ReviewCenter server process (`FakeServerRunner`), the browser (`FakeBrowserLauncher` with a
recording `FakeBrowserPage`), and the daemon launcher (`FakeDaemonLauncher`). The fake launcher starts the
`DaemonHost` as a background task in the test process, and the session store reports the daemon alive for as long as
that task runs, so no real process is created.

The tests assert on exit codes, on captured standard output and error, on the sequence of calls that reached the
fake page, on files written, and on the session files left in the temporary session folder. Because no browser or
server is started, the system tests run on any machine, including build agents without Chromium.

The production adapters that are replaced by fakes, namely the server process runner, the Playwright browser, and
the detached process launch, are not part of these tests. They are exercised by the real-browser end-to-end test
`PuppetEndToEnd_HelloAndCounter_DrivenThroughCommands_ShowGreetingAndCount`, which starts the real ReviewCenter
server, launches Chromium, and drives the hello and counter buttons of the home page through the commands. That test
skips itself with a clear reason when Chromium is not installed. It is supplemental evidence and is deliberately not
linked to a requirement, so that a machine without Chromium does not fail the requirements report.

## Test Environment

The tests run on net10.0 on Windows, Linux, and macOS. Each test creates its own temporary session folder with the
`TempDirectory` helper and removes it afterwards, so tests do not share session files and can run in parallel. No
network access, browser, or installed ReviewCenter server is needed; the loopback control channel binds a random
local port, so the environment must allow opening loopback TCP ports.

## Acceptance Criteria

- All system tests pass with zero failures on all supported platforms; the owner-only file test runs on Linux and
  macOS and is skipped with a reason on Windows.
- Starting a session, running every command, and stopping it all return exit code 0, and the fake page receives the
  commands in order with plain selector names resolved to test ids.
- After `stop`, the daemon has ended with exit code 0 and `list` reports that no sessions are active.
- A command for an unknown session returns exit code 2, and a command whose element is not found within the
  timeout returns exit code 1; the exit codes differ.
- A session whose server cannot start returns exit code 4 with the reason, and no session file, start reservation,
  or process is left behind.
- Of several simultaneous starts for one session name exactly one succeeds and launches a daemon; the others return
  exit code 1 with an explanation.
- A start whose session never becomes ready returns exit code 4 and terminates the launched daemon, identified by
  process id and start time, leaving no session state behind.
- A command for a session whose daemon died returns exit code 3, ends the leftover server and browser that the
  session file records, and removes the session.
- A command with the wrong token is refused and never reaches the page, an unsafe session name is rejected, and on
  Linux and macOS the session folder and file are accessible only by their owner.
- The documented requirements that are decomposed into unit requirements are verified at the unit level, as
  described in the unit verification documents.

## Test Scenarios

**ReviewCenterPuppet_Session_StartDriveStop_RunsEveryCommandAndCleansUp**: A launcher that runs the daemon inside
the test process is wired to fake server and browser. The test runs `start` with a named session, then `goto /`,
`click hello-button`, `fill #name Ada`, `text hello-message`, `screenshot --out <file>`, `snapshot`, `stop`, and
`list`. Every command exits with code 0. The fake page recorded exactly the navigation to the server root, the click
on `[data-testid="hello-button"]` (a plain name became a test id), the fill of `#name` (selector syntax passed
through unchanged), the text read of `[data-testid="hello-message"]`, the screenshot, and the snapshot, in that order.
The screenshot file exists, `list` reports "No active sessions.", the daemon task ended with exit code 0, and the
fake server and the fake browser were each disposed once. This scenario is tested by
`ReviewCenterPuppet_Session_StartDriveStop_RunsEveryCommandAndCleansUp`.

**ReviewCenterPuppet_Errors_UnknownSessionAndMissingElement_ReportDistinctExitCodes**: A daemon runs over fakes and
the test waits until its session is ready. A `text` command for a session that does not exist returns exit code 2.
The fake page is then set to fail with a "No element matching" message, and a `click` with `--timeout 1` in the live
session returns exit code 1. The `stop` command still returns 0 and the daemon ends with exit code 0, which shows
that the failure did not harm the session. This scenario is tested by
`ReviewCenterPuppet_Errors_UnknownSessionAndMissingElement_ReportDistinctExitCodes`.

**ReviewCenterPuppet_Start_ServerCannotStart_ReportsStartFailureAndLeavesNothingBehind**: The fake server fails to
start. `start` returns exit code 4 with the server's reason, the daemon task ended with exit code 4, and neither a
session file nor a start reservation remains, so `list` reports no sessions. This scenario is tested by
`ReviewCenterPuppet_Start_ServerCannotStart_ReportsStartFailureAndLeavesNothingBehind`.

**ReviewCenterPuppet_Start_SimultaneousStartsOfSameSession_StartExactlyOneSession**: Several threads run `start`
for the same session name at once. Exactly one returns 0 and the launcher was called once; every other start returns
exit code 1 with a message that says the session is already starting or running. After a `stop`, the session is
gone. This scenario is tested by `ReviewCenterPuppet_Start_SimultaneousStartsOfSameSession_StartExactlyOneSession`.

**ReviewCenterPuppet_Start_NeverBecomesReady_TerminatesDaemonAndLeavesNothingBehind**: The fake server never
reports healthy and the start time-out is short. `start` returns exit code 4, the launched daemon was terminated
with the process id and start time the launcher reported, and no session file or reservation remains. This scenario
is tested by `ReviewCenterPuppet_Start_NeverBecomesReady_TerminatesDaemonAndLeavesNothingBehind`.

**ReviewCenterPuppet_Session_DaemonDiedWithoutCleanup_ReportsStaleAndEndsLeftoverProcesses**: A session file is
written for a daemon that no longer exists but whose server and browser are still recorded as alive. A command for
the session returns exit code 3; the browser and the server were terminated with their recorded start times, and
`list` shows no session. This scenario is tested by
`ReviewCenterPuppet_Session_DaemonDiedWithoutCleanup_ReportsStaleAndEndsLeftoverProcesses`.

**ReviewCenterPuppet_Security_WrongTokenAndUnsafeSessionId_AreRejected**: A session is running. A request sent
straight to the control port with a wrong token is refused and the fake page records no command from it. A session
name that would leave the session folder is rejected with exit code 1. The owner's own command with the
correct token still succeeds. This scenario is tested by
`ReviewCenterPuppet_Security_WrongTokenAndUnsafeSessionId_AreRejected`.

**ReviewCenterPuppet_Security_UnixPlatform_SessionFileIsOwnerOnly**: On Linux and macOS, a session is started and the
modes of the session folder and the session file are read; only the owner has access (0700 and 0600). On Windows
the test is skipped with a reason, because the session folder is in the per-user temporary folder, which only the
user can access. This scenario is tested by `ubuntu@ReviewCenterPuppet_Security_UnixPlatform_SessionFileIsOwnerOnly`
and `macos@ReviewCenterPuppet_Security_UnixPlatform_SessionFileIsOwnerOnly`.

**Unit-level coverage**: The timeout default and override, the loopback binding, idle shutdown, the start
reservation mechanics, the Windows argument quoting of the daemon launch, the browser and server stop escalation,
and every individual command are verified by the unit tests of `PuppetProgram`, `CommandLine`, `SessionStore`,
`ControlChannel`, `CommandExecutor`, `DaemonHost`, and `DaemonLauncher`. The system requirements are decomposed
into those unit requirements.
