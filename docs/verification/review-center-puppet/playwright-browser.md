## PlaywrightBrowser

### Verification Approach

The browser-free helpers of `PlaywrightBrowser` are verified with two unit tests defined in
`PlaywrightBrowserTests.cs`. Driving a real Chromium is verified by the end-to-end test of the system, which is
linked to the system requirements.

### Test Environment

N/A - standard test environment. No Chromium is needed.

### Acceptance Criteria

- The tests pass.
- Playwright's missing-browser error is described with the install script and command; any other launch error is
  described by its first line.
- A path is resolved against the base URL, an absolute URL is kept, and a path without a base URL is refused.

### Test Scenarios

**PlaywrightBrowser_DescribeLaunchFailure_ChromiumMissingOrOtherFailure_NamesFixOrFirstLine**: A Playwright error
with the "Executable doesn't exist" wording yields a message naming `playwright.ps1` and the install command; an
unrelated multi-line error yields only its first line. Tested by
`PlaywrightBrowser_DescribeLaunchFailure_ChromiumMissingOrOtherFailure_NamesFixOrFirstLine`.

**PlaywrightBrowser_ResolveUrl_PathAndAbsoluteAndMissingBase_ResolvesOrThrows**: `/about` and `about` join the base
URL (with or without a trailing slash), an absolute URL is untouched, and a path with no base URL raises a
`PuppetException`. Tested by `PlaywrightBrowser_ResolveUrl_PathAndAbsoluteAndMissingBase_ResolvesOrThrows`.
