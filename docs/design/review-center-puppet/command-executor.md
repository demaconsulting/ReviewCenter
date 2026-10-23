## CommandExecutor

![ReviewCenterPuppet Structure](ReviewCenterPuppetView.svg)

### Purpose

`CommandExecutor` runs one browser command against an abstract page and turns the outcome into a control response.
It is the only place that knows what each verb means. It has no sockets or processes of its own, so every verb and
every failure mode is verified with a fake page. `IBrowserPage` is the abstraction of the page; the production
implementation uses Playwright and belongs to `DaemonHost`.

### Data Model

**IBrowserPage**: the operations of the browser page: `GotoAsync(url, timeoutMs)`, `ClickAsync(selector, timeoutMs)`,
`FillAsync(selector, text, timeoutMs)`, `TextAsync(selector, timeoutMs)`, `ScreenshotAsync(fullPage)`, and
`SnapshotAsync(timeoutMs)`. Implementations report expected failures, such as an element that does not appear in time,
as a `PuppetException`.

**Configuration**: the page, the base URL of the session's server without a trailing slash, the session id, and the
default screenshot folder.

### Key Methods

**ExecuteAsync**: Executes one request.

- *Parameters*: a `ControlRequest` whose token has already been verified.
- *Returns*: a successful `ControlResponse` that carries the command output, or a failed response with exit code 1
  whose message explains the problem.
- *Postconditions*: Anticipated failures are returned, never thrown.

Behavior by command:

- `goto`: a path that starts with `/` is resolved against the base URL. Any other target must be the base URL or
  inside it. The page then navigates, and the response confirms the address.
- `click`: resolves the selector with `SelectorResolver` and clicks the element.
- `fill`: resolves the selector and enters the text; missing text is treated as an empty string.
- `text`: resolves the selector and returns the text of the element.
- `screenshot`: captures the page, optionally the full scrollable page, and writes the PNG to the output path. When no
  path is given the file is named `<session id>-<timestamp>.png` in the default folder. The response carries the
  absolute path of the file.
- `snapshot`: returns the text snapshot of the page.

### Error Handling

A `PuppetException` from the page or from the executor becomes a failed response with exit code 1 and the exception
message. The executor raises it itself for an unknown command, a missing selector, a missing `goto` target, a `goto`
target outside the session's server, and a screenshot file that cannot be written. Failures that are not
`PuppetException`, which indicate a defect, propagate to the caller.

### Dependencies

- **SelectorResolver**: resolves the selector text of `click`, `fill`, and `text`.
- **ControlChannel**: `ControlRequest`, `ControlResponse`, and `PuppetException`.

### Callers

- **DaemonHost**: creates the executor once the browser is ready and passes every authenticated request that is not
  `stop` to it.
