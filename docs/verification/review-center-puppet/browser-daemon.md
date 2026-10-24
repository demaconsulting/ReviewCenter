## BrowserDaemon

### Verification Approach

`BrowserDaemon` needs a real browser, so it is verified by the end-to-end test of the system rather than by a unit
test: the test starts the real daemon with `puppet start`, drives it, and stops it. That test is linked to the
unit requirement.

### Test Environment

Chromium installed with the Playwright installation script; CI installs it and fails the test when it is missing
(see the system verification).

### Acceptance Criteria

- `puppet start` returns only after the control file exists and the port answers.
- Every response carries the current URL and title.
- `puppet stop` removes the control file before it answers, and a second `puppet stop` finds nothing running.

### Test Scenarios

**PuppetEndToEnd_StartDriveStop_HelloAndCounter_WorksAgainstRealServer**: See the system verification. The start,
the commands, and the stop of that scenario exercise the daemon. Tested by
`windows@PuppetEndToEnd_StartDriveStop_HelloAndCounter_WorksAgainstRealServer` and
`ubuntu@PuppetEndToEnd_StartDriveStop_HelloAndCounter_WorksAgainstRealServer`.
