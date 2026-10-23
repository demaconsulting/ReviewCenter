## PlaywrightBrowser

### Verification Approach

The launch failure message of `PlaywrightBrowser` is verified with one browser-free unit test defined in
`PlaywrightBrowserTests.cs`. Driving a real Chromium is verified by the end-to-end test of the system, which is
linked to the system requirements.

### Test Environment

N/A - standard test environment. No Chromium is needed.

### Acceptance Criteria

- The test passes.
- Playwright's missing-browser error is described with the install script and command; any other launch error is
  described by its first line.

### Test Scenarios

**PlaywrightBrowser_DescribeLaunchFailure_ChromiumMissingOrOtherFailure_NamesFixOrFirstLine**: A Playwright error
with the "Executable doesn't exist" wording yields a message naming `playwright.ps1` and the install command; an
unrelated multi-line error yields only its first line. Tested by
`PlaywrightBrowser_DescribeLaunchFailure_ChromiumMissingOrOtherFailure_NamesFixOrFirstLine`.
