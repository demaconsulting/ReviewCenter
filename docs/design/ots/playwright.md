## Playwright

This document describes the integration and usage design for the `Playwright` OTS software item.

### Purpose

Playwright for .NET (`Microsoft.Playwright`) together with the Chromium build it downloads is the browser automation
library that the system tests use to exercise the ReviewCenter web interface as a user does. It opens the web
interface in headless Chromium and clicks, hovers, types, reads text, and takes screenshots. It is used by the tests
and by the non-shipping Puppet tool only and is not part of the distributed ReviewCenter package.

### Features Used

- Launching headless Chromium and opening a page
- Navigation to a URL and waiting for load
- Locator-based actions: click, hover, fill, and reading element text, with a configurable timeout
- Mouse movement and clicks by coordinates, key presses, and color-scheme emulation
- Page screenshots, including full-page and single-element screenshots
- The `playwright.ps1` installation script that downloads the Chromium build once per machine

### Integration Pattern

Playwright is consumed as a NuGet package reference of the `DemaConsulting.ReviewCenter.Puppeting` test-support
library (`test/DemaConsulting.ReviewCenter.Puppeting/`). Its `PuppetBrowser` class is the single place that calls the
library: it creates one Playwright instance, one browser, and one page, offers each operation as a method, and
disposes them when it is disposed. The system tests (`test/DemaConsulting.ReviewCenter.SystemTests/`) create it
in-process for every browser session. Chromium must be installed once per machine with the `playwright.ps1` script
that the build copies into the output folder of every project that references the library; when it is missing,
starting the browser fails with a message that names the install command.
