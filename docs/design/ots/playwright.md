## Playwright

This document describes the integration and usage design for the `Playwright` OTS software item.

### Purpose

Playwright for .NET (`Microsoft.Playwright`) together with the Chromium browser build it downloads is the browser
automation library used by the Puppet developer tool of the separate ReviewCenterPuppet system. It lets Puppet open
the ReviewCenter web interface in headless Chromium and click, type, read text, and take screenshots. It is used by
the development tool and its tests only and is not part of the distributed ReviewCenter package.

### Features Used

- Launching headless Chromium and opening a page
- Navigation to a URL and waiting for load
- Locator-based actions: click, fill, and reading element text, with a configurable timeout
- Page screenshots, including full-page screenshots
- ARIA accessibility snapshots of the page
- `playwright.ps1` installation script that downloads the Chromium build once per machine
- The driver and `playwright.ps1`, which the package delivers with the build output

### Integration Pattern

Playwright is consumed as a NuGet package reference of the `DemaConsulting.ReviewCenter.Puppet` project. The Puppet
`DaemonHost` unit reaches it only through the `IBrowserLauncher`, `IBrowserSession`, and `IBrowserPage` abstractions;
the `PlaywrightBrowser` adapter is the single place that calls the library, so all other Puppet logic is testable
with fakes. The adapter creates one Playwright instance and one browser per session and disposes them when the
session stops. Chromium must be installed once per machine with the `playwright.ps1` script that the build copies
next to the Puppet executable; when it is missing, session start fails with a clear message and exit code. Puppet
relies on a failed browser launch surfacing as a `PlaywrightException`, which the launcher translates, and on the
driver and the script being present in the build output; both facts are verified without a browser.
