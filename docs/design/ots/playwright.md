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
- The `playwright.ps1` installation script that downloads the Chromium build once per machine

### Integration Pattern

Playwright is consumed as a NuGet package reference of the `DemaConsulting.ReviewCenter.Puppet` project. The
`PlaywrightBrowser` unit is the single place that calls the library; the rest of Puppet works against the
`IBrowserPage` abstraction and is tested with fakes. The unit creates one Playwright instance and one browser per run
and disposes them when the run ends. Chromium must be installed once per machine with the `playwright.ps1` script that
the build copies next to the Puppet executable; when it is missing, the run fails with a message that names the
install command.
