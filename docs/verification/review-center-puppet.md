# ReviewCenterPuppet

## Verification Approach

System-level verification runs the whole Puppet pipeline in one test process through `PuppetProgram.RunAsync`:
argument parsing, the steps file on disk, the `StepRunner`, selector resolution, and the teardown all take part. Only
the server and the browser are replaced by fakes from `Fakes.cs`, so the system tests run on any machine, including
build agents without Chromium. They assert on exit codes, captured output, the calls that reached the fake page, the
files written, and how often the fakes were disposed.

The production server and Playwright adapters are exercised by one real-browser end-to-end test,
`PuppetEndToEnd_StepsFile_HelloAndCounter_WritesScreenshotAndReportsText`, which runs a steps file against the real
ReviewCenter server in real headless Chromium. It is requirement-linked evidence for
`ReviewCenterPuppet-System-RunSteps` and `ReviewCenterPuppet-OTS-Playwright-Chromium` on both platforms. The CI
pipeline installs Chromium and sets `REVIEWCENTER_REQUIRE_CHROMIUM=true`, so a missing or broken browser fails the
test instead of skipping it. Without that variable, as on a developer machine without Chromium, the test skips
itself with a clear reason.

## Test Environment

The tests run on net10.0 on Windows and Linux. Each test uses its own temporary folder (`TempDirectory`). The
end-to-end test and the `ServerProcess` test start the real server on a free loopback port, so the environment must
allow that. Tests that bind ports or start processes belong to the serial `Sequential` xUnit collection and run one
at a time. In CI the two test assemblies also run one after the other (`--max-parallel-test-modules 1`).

## Acceptance Criteria

- All system tests pass with zero failures on all supported platforms.
- A run whose steps all succeed returns exit code 0, prints one result line per step, and stops the server and the
  browser once each.
- A run with a failing step returns exit code 1, prints only the steps that ran, writes `failure-step-N.png`, and
  still stops the server and the browser.
- A browser that cannot launch returns exit code 2 with the message and still stops the server.
- The end-to-end run produces a PNG screenshot and reports the greeting text and the counter value after two clicks.

## Test Scenarios

**ReviewCenterPuppet_Run_AllStepsSucceed_ExitsZeroAndStopsEverything**: A steps file with `goto`, `click`, and `text`
runs over a fake server and a fake page. The exit code is 0, three result lines are printed (the last with the text
`done`), the page was sent to the fake server URL, and the server and the page were each disposed once. Tested by
`ReviewCenterPuppet_Run_AllStepsSucceed_ExitsZeroAndStopsEverything`.

**ReviewCenterPuppet_Run_StepFails_ExitsOneWithFailureScreenshotAndStopsEverything**: The second of three steps
fails. The exit code is 1, two result lines are printed, `failure-step-2.png` exists, and the server and the page were
each disposed once. Tested by `ReviewCenterPuppet_Run_StepFails_ExitsOneWithFailureScreenshotAndStopsEverything`.

**ReviewCenterPuppet_Run_BrowserCannotLaunch_ExitsTwoAndStopsServer**: The browser launcher throws a
`PuppetException`. The exit code is 2, the message is on the error stream, and the server was disposed once. Tested
by `ReviewCenterPuppet_Run_BrowserCannotLaunch_ExitsTwoAndStopsServer`.

**PuppetEndToEnd_StepsFile_HelloAndCounter_WritesScreenshotAndReportsText**: A steps file clicks the hello button,
reads the greeting, clicks the counter button twice, reads the counter, and takes a screenshot, against the real
server in real Chromium. The exit code is 0, the results contain "Hello from ReviewCenter!" and "2", and the screenshot
is a PNG. Tested by `windows@PuppetEndToEnd_StepsFile_HelloAndCounter_WritesScreenshotAndReportsText` and
`ubuntu@PuppetEndToEnd_StepsFile_HelloAndCounter_WritesScreenshotAndReportsText`.

**Unit-level coverage**: Argument handling, steps-file validation, step execution, selector resolution, the server
lifecycle, and the launch failure message are verified by the unit tests of the five units.
