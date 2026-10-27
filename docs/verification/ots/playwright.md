## Playwright Verification

This document provides the verification evidence for the Playwright OTS software item. Its requirement belongs to
the ReviewCenterPuppet system.

### Required Functionality

Playwright for .NET with its Chromium build is the browser automation library used by the Puppet developer tool. The
project needs it to drive a headless Chromium: load pages, click, hover, fill, read text, and capture screenshots.

### Verification Approach

Playwright is verified by the real-browser end-to-end test of the Puppet system, which starts Puppet and drives the
real ReviewCenter server in headless Chromium.

### Test Environment

Chromium must be installed with the Playwright installation script. The CI pipeline installs it (cached per
operating system and Playwright version) and sets `REVIEWCENTER_REQUIRE_CHROMIUM=true`, so the test fails instead of
skipping when the browser cannot be launched. Locally the test skips with a reason when Chromium is not installed.

### Acceptance Criteria

- The end-to-end test passes on Windows and Linux in CI.

### Test Scenarios

#### PuppetEndToEnd_StartDriveStop_SignInProjectAndTheme_WorksAgainstRealServer

**Scenario**: Puppet is started, navigates to the home page, hovers the sign-in link, fails and then succeeds at
signing in on the login page, clicks through to the projects page, creates a project, opens the account page and
switches the theme, reads texts, changes the viewport and color scheme, takes a screenshot, and is stopped.

**Expected**: Every command succeeds, the hover persists, the project page appears after the project is created,
the theme attribute of the page is `dark` after the theme button is clicked, and the screenshot is a PNG.

**Requirement coverage**: `ReviewCenterPuppet-OTS-Playwright-Chromium`.

### Limitations

None beyond the Chromium installation requirement above.
