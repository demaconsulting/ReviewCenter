## PlaywrightBrowser

![ReviewCenterPuppet Structure](ReviewCenterPuppetView.svg)

### Purpose

`PlaywrightBrowser` is the only code that calls Playwright. It launches headless Chromium and performs one command
at a time through a single flat switch of thin wrappers. It also records the browser errors that every response
reports.

### Data Model

**Browser, context, and page**: one of each, with a remembered viewport and color scheme so that `reset` can create
a new context (clearing cookies and storage) with the same settings.

**Event buffer**: the last 200 console errors, page errors, and failed requests; each response reports the ones
recorded since the previous response, and `console` shows the last 50.

### Key Methods

**LaunchAsync**: Launches Chromium (headless unless `--headed`) with the viewport. Actions time out after 5
seconds and navigation after 30 seconds. A failed launch is reported with `DescribeLaunchFailure`.

**DescribeLaunchFailure**: Returns a message naming the command that installs Chromium when the browser is missing,
and the first line of the Playwright error otherwise.

**ResolveUrl**: Resolves a path against the base URL given at start; an absolute URL is used as is; a path without a
base URL is an error.

**ExecuteAsync**: Performs the command: `goto`, `reload`, `back`, `click`, `dblclick`, `hover`, `fill`, `press`,
`type`, `select`, `check`, `uncheck`, `upload`, `mouse-move`, `mouse-click`, `wait`, `text`, `snapshot`,
`screenshot`, `viewport`, `theme`, `eval`, `console`, and `reset`. After navigation the page waits until the Blazor
circuit is interactive (`data-interactive='true'`); there are no fixed sleeps, so the caller uses `wait` after an
asynchronous update. `snapshot` lists up to 200 visible interactive elements with tag, role, accessible name, test
id, and enabled or checked state. Screenshots use CSS pixels. `screenshot`, `viewport`, and `theme` never move the
mouse, so a hover persists.

### Error Handling

A Playwright time-out becomes `<command> timed out after <ms>ms (selector not found or not actionable): <arguments>`;
a failed navigation becomes `navigation failed: ...`; any other Playwright error becomes `<command> failed: <first
line>`. All are `PuppetException` instances with one-line messages.

### Dependencies

- **Microsoft.Playwright** and an installed Chromium.
- **SelectorResolver**: resolves the selector of every element command.
