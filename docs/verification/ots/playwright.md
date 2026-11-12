## Playwright Verification

This document provides the verification evidence for the Playwright OTS software item.

### Required Functionality

Playwright for .NET with its Chromium build is the browser automation library that the system tests use, through the
Puppeting test-support library, to drive the web interface in a real browser. The system tests need it to drive a
headless Chromium: load pages, click, hover, fill, read text, emulate the color scheme, and capture screenshots.

### Verification Approach

Playwright is verified by a real-browser system test that drives the real ReviewCenter server in headless Chromium
in-process, through the same `PuppetBrowser` class that the other system tests use.

### Test Environment

Chromium must be installed with the Playwright installation script. The CI pipeline installs it (cached per
operating system and Playwright version) and sets `REVIEWCENTER_REQUIRE_CHROMIUM=true`, so the test fails instead of
skipping when the browser cannot be launched. Locally the test skips with a reason when Chromium is not installed.

### Acceptance Criteria

- The real-browser system test passes on Windows and Linux in CI.

### Test Scenarios

#### ReviewCenter_RealBrowser_SignInCreateProjectAndTheme_WorksOverTheLiveConnection

**Scenario**: The browser loads the home page, hovers the sign-in link and moves the mouse away, fails and then
succeeds at signing in on the login page, clicks through to the projects page, creates a project, emulates the dark
and the light color scheme, takes a screenshot of the page and of one element, and clicks an element that does not
exist.

**Expected**: The home page answers `HTTP 200`, the hover holds until the mouse moves, the error and the user name
are read from the page, the project page appears after the project is created, the media query of the page follows
the emulated scheme, both screenshots are PNG files, and the missing element fails with a message that names it.

**Requirement coverage**: `ReviewCenter-OTS-Playwright-Chromium`, `ReviewCenter-OTS-Playwright-Chromium-Click`,
`ReviewCenter-OTS-Playwright-Chromium-Hover`, `ReviewCenter-OTS-Playwright-Chromium-Fill`,
`ReviewCenter-OTS-Playwright-Chromium-ReadText`, `ReviewCenter-OTS-Playwright-Chromium-ColorScheme`,
`ReviewCenter-OTS-Playwright-Chromium-Screenshot`.

### Limitations

None beyond the Chromium installation requirement above.
