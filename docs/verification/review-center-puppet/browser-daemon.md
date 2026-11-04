## BrowserDaemon

### Verification Approach

`BrowserDaemon` needs a real browser for everything except a failure to start. That failure path is verified by a unit
test defined in `BrowserDaemonTests.cs`, which runs the daemon with a malformed viewport so that it fails before any
browser is started. Launching, serving commands, and stopping are verified by the end-to-end test of the system: the
test starts the real daemon with `puppet start`, drives it, and stops it. That test is linked to the system
requirements.

### Test Environment

N/A for the unit test (no Chromium is needed). The end-to-end test needs Chromium installed with the Playwright
installation script; CI installs it and fails the test when it is missing (see the system verification).

### Acceptance Criteria

- The unit test passes.
- A daemon that cannot start writes a control file with the error message and no port, and exits with code 1.
- In the end-to-end test, `puppet start` returns only after the control file exists and the port answers, every
  response carries the current URL and title, `puppet stop` removes the control file before it answers, and a second
  `puppet stop` finds nothing running (see the system verification).

### Test Scenarios

**BrowserDaemon_RunAsync_BadViewport_PublishesStartFailureWithoutPort**: The daemon runs with the viewport `abc` and a
private control file. It returns exit code 1, and the control file holds an error that says the viewport must look like
`1280x800` and no port. Tested by `BrowserDaemon_RunAsync_BadViewport_PublishesStartFailureWithoutPort`.

**PuppetEndToEnd_StartDriveStop_SignInProjectAndTheme_WorksAgainstRealServer**: See the system verification. The
start, the commands, and the stop of that scenario exercise the daemon. It is linked to the system requirements, not
to a unit requirement.
