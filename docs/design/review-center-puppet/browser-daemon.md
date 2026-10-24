## BrowserDaemon

![ReviewCenterPuppet Structure](ReviewCenterPuppetView.svg)

### Purpose

`BrowserDaemon` is the long-lived process that owns the browser. It runs for the hidden verb `daemon` and serves
commands until it is told to stop.

### Data Model

None - the daemon holds the `PlaywrightBrowser` and the listener for the duration of the run.

### Key Methods

**RunAsync**: Launches the browser, opens the loopback listener on a free port, writes the control file (only then,
so the file means the daemon can answer), and serves requests through `ControlChannel`. A start-up
`PuppetException`, such as a missing Chromium, is written to the control file as an error so that `start` can print
it. In a `finally` block the listener is stopped and the browser is closed.

**Dispatch**: Runs one request in the browser. A failure of the command is returned as a failed response with a
short message and never stops the daemon. Every response carries the current URL, the page title, and the browser
errors recorded since the previous response.

**Stop handling**: The `stop` request deletes the control file before it answers, so a caller that sees the answer
sees Puppet as stopped, then ends the serve loop; the daemon process exits after closing the browser.

### Error Handling

A command's `PuppetException` or `ArgumentException` (for example an empty selector) is reported as its own
message; any other exception is reported as `internal error: <type>: <first line>`. The daemon keeps running.

### Dependencies

- **ControlChannel**: serves the requests and writes the control file.
- **PlaywrightBrowser**: performs the commands.
