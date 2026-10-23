## PlaywrightBrowser

![ReviewCenterPuppet Structure](ReviewCenterPuppetView.svg)

### Purpose

`PlaywrightBrowser` is the only code that calls Playwright. It launches headless Chromium and implements the
`IBrowserPage` abstraction of `StepRunner`.

### Data Model

**PlaywrightBrowserLauncher**: static; launches Chromium and returns a page.

**PlaywrightPage**: the `IBrowserPage` implementation over one Playwright page with a 1280 by 720 viewport. Disposing
it closes the browser and the Playwright instance.

### Key Methods

**LaunchAsync**: Launches Chromium. A failed launch is reported with `DescribeLaunchFailure`.

**DescribeLaunchFailure**: Returns a message naming the command that installs Chromium when the browser is missing,
and the first line of the Playwright error otherwise.

**Goto / Click / Fill / Text / Screenshot**: Perform the action with the step time-out. After `goto` the page waits
until the Blazor circuit is interactive (`data-interactive='true'`); after `click` and `fill` it waits 300 ms,
because Blazor Server re-renders over a web socket after the action returns and a text read would otherwise see the
old value. Playwright errors and time-outs are mapped to short messages.

### Error Handling

A failed launch or a failed action raises a `PuppetException`; `StepRunner` turns an action failure into a failed
step result.

### Dependencies

- **Microsoft.Playwright** and an installed Chromium.
