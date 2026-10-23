## DaemonHost

### Verification Approach

`DaemonHost` is verified with unit tests defined in `DaemonHostTests.cs`. The host runs over `FakeServerRunner` and
`FakeBrowserLauncher` from `Fakes.cs`, which record how often they are disposed and can be told to fail. The control
server is real, and a session store over a temporary folder is real, so the published session file and the
commands traveling through the loopback channel are exercised. Tests run the host on a background task, wait for
the ready session file, and then talk to it with `ControlClient`.

The production adapters of this unit, `ServerProcessRunner` and the Playwright classes `PlaywrightBrowserLauncher`
and `PlaywrightPage`, cannot be fully verified with fakes. The one adapter behavior that needs no browser, the
translation of a failed or timed-out screenshot into a `PuppetException` and the classification of a `/health`
answer, is verified with a Playwright `IPage` created with a dispatch proxy whose screenshot fails and with loopback
listeners that return canned answers; the rest is exercised by the real-browser end-to-end test
`PuppetEndToEnd_HelloAndCounter_DrivenThroughCommands_ShowGreetingAndCount`, which starts the real ReviewCenter
server, launches headless Chromium, runs the Puppet executable as separate processes, and drives the hello and
counter buttons. That test is requirement-linked evidence for the Puppet system requirements; CI installs Chromium and
fails it, rather than skipping it, when the browser cannot be launched.

### Test Environment

The unit tests use the standard test environment and need permission to open loopback TCP ports. Because they bind
ports, they run in the serial `Sequential` xUnit collection of the Puppet test project, one at a time and never beside
another port-, process-, or timing-sensitive test. The end-to-end test
additionally needs the built ReviewCenter server assembly and an installed Chromium; Chromium is installed once with
the Playwright installation script (CI does this on every platform, with the browser cached). Setting
`REVIEWCENTER_REQUIRE_CHROMIUM=true` makes a missing browser a failure; without it the test skips with a reason.

### Acceptance Criteria

- All unit tests pass with zero failures.
- The ready session file contains the server URL, the process ids and start times of the daemon, the server, and the
  browser, a control token, and a control port.
- A command sent through the control channel reaches the page, and `stop` closes the browser, stops the server, and
  deletes the session file, with exit code 0.
- When the browser or the server cannot be started, the host returns exit code 4, records a failed session with the
  reason, and stops whatever was already started.
- With no requests, the host shuts down after the idle timeout, disposes the browser and the server, and removes the
  session file.
- A command that fails unexpectedly (a crashed browser during a screenshot) gets a failed response with exit code 1
  naming the problem; the session keeps serving, and a later `stop` ends it with exit code 0 after disposing the
  browser and the server.
- An unexpected exception while launching the browser gives exit code 4, a failed session record that starts with
  "Unexpected error", and the server disposed.
- A server process that does not exit after the first kill is killed again together with its process tree; one that
  still does not exit is reported as a `PuppetException` that names the process, and the host records a failed
  session and returns exit code 4 rather than reporting a completed cleanup.
- A failed Playwright screenshot is reported as a `PuppetException` that names the screenshot and keeps only the
  first line of the Playwright message; the command timeout is handed to Playwright, and a screenshot that times out
  is reported as a timeout.
- The server counts as ready only when `/health` answers status 200 with the body `Healthy`; an error status or a
  refused connection means "not ready yet", and any other success answer is a mismatch that fails the start; a
  healthy answer received after the server process has exited fails the start too.
- A daemon whose session id was taken over by another session stops without changing that session's record; a
  daemon that cannot write its failure record still disposes the server and browser and returns exit code 4.
- The temporary DevTools session used to learn the browser's process identity is detached after the query, also when
  the query fails or the browser has already closed, and a failing detach is tolerated.
- The end-to-end test passes where Chromium is available. Where it is not, the test skips with a reason, except
  when `REVIEWCENTER_REQUIRE_CHROMIUM=true` (set in CI), where it fails.

### Test Scenarios

**DaemonHost_Run_FakeBrowserClickCommand_ReturnsOk**: The host runs in the background; a `click` request with the
session token returns a successful response and the fake page recorded a click on `[data-testid="hello-button"]`. A
`stop` request then returns success, the host returns 0, the browser and the server were each disposed once, and the
session file is gone. This scenario is tested by `DaemonHost_Run_FakeBrowserClickCommand_ReturnsOk`.

**DaemonHost_Run_Ready_RecordsServerAndDaemonDetails**: Once the session is ready, its record has the fake server
URL, the process id of the test process as the daemon id, a non-empty token, and a control port greater than 0. The
host ends with exit code 0 after it is stopped. This scenario is tested by
`DaemonHost_Run_Ready_RecordsServerAndDaemonDetails`.

**DaemonHost_Run_Ready_RecordsBrowserProcessIdentity**: The fake browser reports a process id and start time; once
the session is ready, its record holds exactly those values as the browser identity. This scenario is tested by
`DaemonHost_Run_Ready_RecordsBrowserProcessIdentity`.

**DaemonHost_ServerStop_ExitsAfterFirstKill_KillsOnce**: The stop routine of the server runner kills the process
once, the process exits within the wait, and no escalation happens. This scenario is tested by
`DaemonHost_ServerStop_ExitsAfterFirstKill_KillsOnce`.

**DaemonHost_ServerStop_ExitsAfterEscalation_KillsTwice**: The process is still alive after the first wait; the
routine kills the process tree a second time and the process then exits, so it succeeds. This scenario is tested by
`DaemonHost_ServerStop_ExitsAfterEscalation_KillsTwice`.

**DaemonHost_ServerStop_NeverExits_ThrowsPuppetException**: The process never exits; after the escalation the
routine throws a `PuppetException` that names the process. This scenario is tested by
`DaemonHost_ServerStop_NeverExits_ThrowsPuppetException`.

**DaemonHost_ServerStopTree_PartialTreeKill_IsReportedUnlessALaterAttemptEndsTheTree**: A kill reports a partial tree
failure (`AggregateException`) although the root exits: the stop throws a `PuppetException` that names the server and
its child processes. When the first attempt is partial and the second ends the whole tree, nothing is reported, and a
kill refused because the process is already gone is decided by the exit as before. This scenario is tested by
`DaemonHost_ServerStopTree_PartialTreeKill_IsReportedUnlessALaterAttemptEndsTheTree`.

**DaemonHost_Run_ServerCannotBeStopped_ReportsFailureAndReturnsFour**: Disposing the fake server fails; the host
still closes the browser, records a failed session with the reason, and returns 4. This scenario is tested by
`DaemonHost_Run_ServerCannotBeStopped_ReportsFailureAndReturnsFour`.

**DaemonHost_Run_BrowserLaunchFails_WritesFailedSessionAndReturnsFour**: The browser launcher fails with "Chromium is
not installed"; the host returns 4, the session file has the status `Failed` with that reason, and the server that
was already started was disposed. This scenario is tested by
`DaemonHost_Run_BrowserLaunchFails_WritesFailedSessionAndReturnsFour`.

**DaemonHost_Run_ServerStartFails_WritesFailedSessionAndReturnsFour**: The server runner fails because the server
does not become healthy; the host returns 4 and the session file records the reason. This scenario is tested by
`DaemonHost_Run_ServerStartFails_WritesFailedSessionAndReturnsFour`.

**DaemonHost_Run_Idle_ShutsDownAndRemovesSession**: With a very short idle timeout and no callers, the host returns
0, the server and the browser were each disposed once, and the session file is removed. This scenario is tested by
`DaemonHost_Run_Idle_ShutsDownAndRemovesSession`.

**DaemonHost_Run_ReplacementPublishedDuringShutdown_IsNotDeleted**: While the fake server is being disposed, a
different session record (another daemon and control token) is published for the same id. After the host returns 0,
the replacement record is still present and unchanged. This scenario is tested by
`DaemonHost_Run_ReplacementPublishedDuringShutdown_IsNotDeleted`.

**DaemonHost_Run_StartLockHeldDuringShutdown_LeavesRecordForTheStart**: A start lock for the id is held when the
host finishes shutting down. The host still returns 0 and releases the browser, and leaves the record in place for
that start (or the next lookup) to clean up. This scenario is tested by
`DaemonHost_Run_StartLockHeldDuringShutdown_LeavesRecordForTheStart`.

**DaemonHost_Run_ScreenshotThrowsUnexpected_ReturnsFailureAndKeepsServing**: The fake page throws an
`InvalidOperationException` with a two-line message when a screenshot is taken. The `screenshot` request returns a
failed response with exit code 1 that names the exception type and the first line only; a following `click` still
succeeds; `stop` succeeds, the host returns 0, and the browser and the server were each disposed once. This scenario
is tested by `DaemonHost_Run_ScreenshotThrowsUnexpected_ReturnsFailureAndKeepsServing`.

**DaemonHost_Run_BrowserLaunchThrowsUnexpected_WritesFailedSessionAndReturnsFour**: The browser launcher throws an
`InvalidOperationException`; the host returns 4, the session file has the status `Failed` with an "Unexpected error"
description containing the message, and the server was disposed. This scenario is tested by
`DaemonHost_Run_BrowserLaunchThrowsUnexpected_WritesFailedSessionAndReturnsFour`.

**DaemonHost_PlaywrightPage_ScreenshotFails_ThrowsPuppetException**: A `PlaywrightPage` over a proxy `IPage` whose
screenshot faults with a `PlaywrightException` with a two-line message throws a `PuppetException` that mentions the
screenshot and the first line only. No browser is needed. This scenario is tested by
`DaemonHost_PlaywrightPage_ScreenshotFails_ThrowsPuppetException`.

**DaemonHost_PlaywrightPage_ScreenshotTimesOut_AppliesTimeoutAndThrowsPuppetException**: A `PlaywrightPage` over a
proxy `IPage` that records the screenshot options and whose screenshot faults with a timeout exception is asked for
a full-page screenshot with a 2000 millisecond timeout. The options the proxy received carry a timeout of 2000 and
the full-page flag, and the `PuppetException` says the screenshot timed out after 2 s. No browser is needed. This
scenario is tested by `DaemonHost_PlaywrightPage_ScreenshotTimesOut_AppliesTimeoutAndThrowsPuppetException`.

**DaemonHost_HealthProbe_ExactHealthyAnswer_IsHealthy**: A loopback listener answers `/health` with status 200 and
the body `Healthy`; the probe reports healthy. This scenario is tested by
`DaemonHost_HealthProbe_ExactHealthyAnswer_IsHealthy`.

**DaemonHost_HealthProbe_SuccessWithoutExactBody_IsMismatchNamingTheAnswer**: A loopback listener answers `/health`
with status 200 and the body `Unhealthy`, with status 200 and an empty body, and with status 204. Each answer is
reported as a mismatch whose detail names the status and the expected text, so the start fails instead of driving
another service. This scenario is tested by `DaemonHost_HealthProbe_SuccessWithoutExactBody_IsMismatchNamingTheAnswer`.

**DaemonHost_HealthProbe_Redirect_IsMismatchAndNotFollowed**: A loopback listener answers `/health` with a 302
redirect to a second loopback listener. The probe, created by the readiness client factory, reports a mismatch that
names status 302, and the second listener never receives a connection, so a redirect cannot lead the probe to
another endpoint that answers like a healthy server. This scenario is tested by
`DaemonHost_HealthProbe_Redirect_IsMismatchAndNotFollowed`.

**DaemonHost_HealthProbe_ErrorStatusOrNoListener_IsNotReady**: A loopback listener answers `/health` with status 503,
and a second probe targets a port nothing listens on. Both are reported as not ready, so the runner keeps polling.
This scenario is tested by `DaemonHost_HealthProbe_ErrorStatusOrNoListener_IsNotReady`.

**DaemonHost_WaitUntilHealthy_ServerExitedBeforeHealthyAnswer_Fails**: A loopback listener answers `/health` with
the exact healthy answer, but the server process is reported as exited (exit code 7) on the check made after the
answer. The wait fails with a `PuppetException` naming the exit code and the fact that another service answered,
instead of treating the answer as the server's. This scenario is tested by
`DaemonHost_WaitUntilHealthy_ServerExitedBeforeHealthyAnswer_Fails`.

**DaemonHost_WaitUntilHealthy_ServerRunningAndHealthy_Completes**: The same listener with a server process that
keeps running completes the wait without error. This scenario is tested by
`DaemonHost_WaitUntilHealthy_ServerRunningAndHealthy_Completes`.

**DaemonHost_Run_SessionTakenOverWhileStarting_StopsAndLeavesOtherRecord**: While the fake server starts, another
session's record is written under the same id. The host returns exit code 4, disposes the server it started, and the
file still holds the other session's record, unchanged (no `Failed` record overwrote it). This scenario is tested by
`DaemonHost_Run_SessionTakenOverWhileStarting_StopsAndLeavesOtherRecord`.

**DaemonHost_Run_FailureRecordCannotBeWritten_StillTearsDownAndReturnsFour**: While the fake server starts, a
directory takes the place of the session file, so no later record can be written, and the browser fails to launch.
The host still disposes the server and returns exit code 4 instead of ending with an unhandled error. This
scenario is tested by `DaemonHost_Run_FailureRecordCannotBeWritten_StillTearsDownAndReturnsFour`.

**DaemonHost_PlaywrightBrowser_ProcessQuery_DetachesProtocolSession**: A fake DevTools session answers the process
query with a gpu process and a browser process that is the test process. The query returns the browser's process id
and a start time, and the session was detached exactly once. This scenario is tested by
`DaemonHost_PlaywrightBrowser_ProcessQuery_DetachesProtocolSession`.

**DaemonHost_PlaywrightBrowser_DetachFailsBecauseBrowserClosed_IsTolerated**: A fake DevTools session fails both the
query and the detach with "Target closed". The query returns no identity and throws nothing, and the detach was
attempted once. This scenario is tested by `DaemonHost_PlaywrightBrowser_DetachFailsBecauseBrowserClosed_IsTolerated`.

**PuppetEndToEnd_HelloAndCounter_DrivenThroughCommands_ShowGreetingAndCount**: Requirement-linked evidence for
`ReviewCenterPuppet-System-Lifecycle`, `-Commands`, and `-Errors` on Windows and Linux. The test starts a session with
the real executable, navigates to the home page, clicks the hello
button, reads the greeting, clicks the counter button twice and reads the count 2, takes a screenshot that is a real
image, fails a click on an element that does not exist with exit code 1, stops the session, and checks that the
session is no longer listed. Because the commands are separate processes, it also demonstrates that the detached
daemon survives between commands. The test is skipped when Chromium is unavailable, unless the environment variable
`REVIEWCENTER_REQUIRE_CHROMIUM` is `true` (CI), in which case it fails. This scenario is tested by
`PuppetEndToEnd_HelloAndCounter_DrivenThroughCommands_ShowGreetingAndCount`.
