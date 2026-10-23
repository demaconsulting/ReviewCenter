## Playwright Verification

This document provides the verification evidence for the Playwright OTS software item. Requirements for this OTS item
are defined in the Playwright OTS Software Requirements document and belong to the ReviewCenterPuppet system.

### Required Functionality

Playwright for .NET with its Chromium build is the browser automation library used by the Puppet developer tool. What
the project needs of it, and can prove in every environment without a browser, is that the package delivers its driver
and its `playwright.ps1` installation script with the build output, and that the driver starts and reports a failed
browser launch as a `PlaywrightException`. Puppet's missing-Chromium message, with its install command, depends on
both.

### Verification Approach

Playwright is verified by two browser-free tests defined in `PlaywrightOtsTests.cs`; they never skip and run in every
environment, including pipelines without Chromium. The first inspects the test output folder for the installation
script and the driver. The second starts the Playwright driver and asks it to launch Chromium from an executable
path that cannot exist, which must fail with a `PlaywrightException` whether or not a browser is installed.

### Test Environment

Standard test environment. No Chromium installation is needed. The driver is started as a child process, which needs
permission to run the bundled Node.js executable.

### Acceptance Criteria

- The build output contains `playwright.ps1` and a non-empty `.playwright/node` driver folder.
- Launching Chromium from a missing executable path throws a `PlaywrightException`.

### Test Scenarios

#### PlaywrightOts_BuildOutput_Inspected_ContainsInstallScriptAndDriver

**Scenario**: The folder the tests run from is inspected for `playwright.ps1` and for the `.playwright/node` folder.

**Expected**: The script exists and the driver folder contains files.

**Requirement coverage**: `ReviewCenterPuppet-OTS-Playwright-Driver`.

#### PlaywrightOts_Chromium_MissingExecutable_ThrowsPlaywrightException

**Scenario**: Playwright is created and asked to launch headless Chromium from a path inside a temporary folder where
no executable exists.

**Expected**: The launch fails with a `PlaywrightException`.

**Requirement coverage**: `ReviewCenterPuppet-OTS-Playwright-LaunchFailure`.

### Limitations

Launching a real Chromium, loading pages, clicking, reading text, and taking screenshots are not requirement-level
evidence for this OTS item, because that evidence needs a browser that the build pipeline does not install. Those
behaviors are exercised only by the supplemental end-to-end test
`PuppetEndToEnd_HelloAndCounter_DrivenThroughCommands_ShowGreetingAndCount`, which is skipped where Chromium cannot be
launched and is not linked to any requirement; every other Puppet test replaces Playwright with fakes. Linking the
end-to-end test to requirements would need a pipeline step that installs Chromium.
