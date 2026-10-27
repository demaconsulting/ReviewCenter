# ReviewCenterPuppet

## Verification Approach

Puppet is verified by unit tests of its units plus ONE real end-to-end test; there are no fakes and no test-only
abstractions. The unit tests call the units directly: argument parsing and result formatting in `PuppetProgram`,
the request/response protocol and the stale-file ping in `ControlChannel`, selector resolution, and the browser-free
helpers of `PlaywrightBrowser`.

The end-to-end test, `PuppetEndToEnd_StartDriveStop_SignInProjectAndTheme_WorksAgainstRealServer`, starts the real
ReviewCenter server on a free port with one seeded administrator, runs `puppet start`, signs in and creates a project
in real headless Chromium with separate commands, and stops Puppet again. It is requirement-linked evidence for the
three system requirements,
`ReviewCenterPuppet-BrowserDaemon-Lifecycle`, and `ReviewCenterPuppet-OTS-Playwright-Chromium` on both platforms.
The CI pipeline installs Chromium and sets `REVIEWCENTER_REQUIRE_CHROMIUM=true`, so a missing or broken browser
fails the test instead of skipping it. Without that variable, as on a developer machine without Chromium, the test
skips itself with a clear reason.

## Test Environment

The tests run on net10.0 on Windows and Linux. The end-to-end test starts the real server and the real daemon on
free loopback ports and uses a private control file in a temporary folder, so it never touches the control file of a
Puppet that a developer has running. Tests that bind ports or start processes belong to the serial `Sequential`
xUnit collection and run one at a time. In CI the two test assemblies also run one after the other
(`--max-parallel-test-modules 1`).

## Acceptance Criteria

- All tests pass with zero failures on all supported platforms.
- `puppet start` while Puppet is already running fails with exit code 1; `puppet stop` removes the control file and a
  second `puppet stop` exits with code 2.
- A hover stays in effect for later commands: after `hover`, a separate `screenshot` and `eval` still see the
  element hovered, until `mouse-move` moves the mouse away.
- A command that fails (for example an unknown element) exits with code 1 and a one-line message naming the selector.
- The end-to-end run signs in, creates a project over the live connection (the project page appears after a `wait`
  for the asynchronous update), switches the theme on the account page, and produces a PNG screenshot.

## Test Scenarios

**PuppetEndToEnd_StartDriveStop_SignInProjectAndTheme_WorksAgainstRealServer**: Starts Puppet, goes to the home page,
and checks that `snapshot` lists the sign-in link. After `hover` on that link, a `screenshot` and an `eval` of
`matches(':hover')` show it hovered, and after `mouse-move 0 0` it is not. On the login page a failed sign-in
(`fill` and `click` of the login button) shows the login error, and the right user name and password sign in and show
the display name `Puppet Admin`. A `click` on the projects link of the header, a `wait` for the page marked
interactive, a `click` on the new project button, a `fill` of the project name, and a `click` on the create button
create a project over the live connection, and `wait` and `text` show the project page. On the account page a
`click` on the dark theme button makes `eval` report the `dark` theme attribute on the page and in the stored
setting. A click on a missing element exits with 1; `screenshot` writes a PNG; `viewport` and
`theme dark` apply (checked with `eval`); `console` succeeds; `start` while running exits with 1; `stop` exits with 0
and removes the control file; a second `stop` exits with 2. Tested by
`windows@PuppetEndToEnd_StartDriveStop_SignInProjectAndTheme_WorksAgainstRealServer` and
`ubuntu@PuppetEndToEnd_StartDriveStop_SignInProjectAndTheme_WorksAgainstRealServer`.

**Unit-level coverage**: Argument handling and result formatting, the control channel, selector resolution, and the
launch failure message are verified by the unit tests of the five units.
