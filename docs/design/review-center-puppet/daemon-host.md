## DaemonHost

![ReviewCenterPuppet Structure](ReviewCenterPuppetView.svg)

### Purpose

`DaemonHost` is the body of the long-lived daemon process behind one session. Browser state, such as the loaded page,
a half-filled form, or a signed-in user, must survive between the separate commands a caller runs, so something has
to stay alive between them. The host owns the ReviewCenter server process, the browser, and the control channel
server. It publishes how to reach the session in the session file, executes commands one at a time, and tears
everything down on `stop`, on the idle timeout, or when start-up fails, so that no server or browser is left behind.

The unit also contains the abstractions through which the host reaches the outside world, and their production
adapters. The abstractions let the whole lifecycle be verified with fakes; the adapters are verified only by the
real-browser end-to-end test.

### Data Model

**IRunningServer**: a started server with its `Url`, `ProcessId`, and `StartTimeUtc`; disposing it stops the server
process tree and reports a failure when the process cannot be stopped.

**IServerRunner**: `StartAsync(serverPath, cancellationToken)` starts a server on a free loopback port and returns it
once it is healthy.

**IBrowserSession**: a launched browser with its `Page` (an `IBrowserPage`) and the `ProcessId` and `StartTimeUtc` of
the browser process (zero when they cannot be determined); disposing it closes the browser.

**IBrowserLauncher**: `LaunchAsync()` launches a headless browser with one blank page.

**DefaultIdleTimeout**: 30 minutes.

**ServerProcessRunner** (production `IServerRunner`): picks a free loopback port, runs
`dotnet <server assembly> serve --urls http://127.0.0.1:<port>` in the folder of the assembly, keeps the last 20
lines of its output, and waits up to 30 seconds for the `/health` endpoint to answer. The server is ready only when
`/health` answers status 200 with the exact body `Healthy` (the text `ServerHost` returns); a non-success status or a
refused connection means "not ready yet" and polling continues, while a success answer with any other status or body
(another process that took the port between choosing it and the server binding it, or a misconfigured endpoint) fails
the start at once with a `PuppetException` that names the answer and includes the captured output. The classification
is done by `ProbeHealthAsync`. The readiness client does not follow redirects (`CreateHealthClient`), and a 3xx answer
is a mismatch, so another process on the port cannot send the probe to a different endpoint that answers
`200 Healthy`. A healthy answer is also not trusted unless the server process is still running: the process is
checked for an exit before each probe and again right after a healthy answer, because a server that lost the port
race exits with an error while the other service on the port keeps answering. The free port is chosen by binding a
listener to port 0 and releasing it, so another process can still take it before the server binds (documented in the
audit table); the strict probe plus the exit check are what make that outcome a clear start failure instead of a
session driving the wrong service. Killing the server tree goes through `SessionStore.TryKill`, which absorbs the
"already gone" failures, including the `AggregateException` of a partial tree kill. The server runs as a separate
process so that Puppet exercises exactly what ships: the real assembly, its real start-up, and its real health
endpoint. The polling interval, the health request time-out, and the kill wait are named constants. Stopping the
server is verified: after the kill, the runner waits for the process to exit; if it has not exited it ends the
whole process tree and waits again; if it still has not exited it raises a `PuppetException` that names the process,
instead of silently disposing it and reporting the cleanup as complete.

**PlaywrightBrowserLauncher** (production `IBrowserLauncher`): launches headless Chromium through Microsoft
Playwright with a fixed 1280 by 720 viewport and one page. Playwright does not expose the browser's process id, so
the launcher asks the browser itself through a DevTools session and records the process id and start time, which lets
a session end that exact process later. The DevTools session serves that one query, so it is detached as soon as the
query has finished, also when the query fails; a detach that fails because the browser has already closed is
tolerated. When the identity cannot be determined the session records none, and the
launch still succeeds. When Chromium has not been installed it replaces the
cryptic Playwright message by one that names the `playwright.ps1` script next to the executable and the command
`install chromium`.

**PlaywrightPage** (production `IBrowserPage`): implements the page operations on a Playwright page. Every wait uses
the command timeout. After `goto` it waits for the `data-interactive='true'` marker when the page advertises one,
because a Blazor Server page ignores clicks until its live connection exists. Element commands wait for the element
to be attached before acting. The snapshot is the URL, the title, the accessibility tree of the body, and the list
of the test ids present. The screenshot uses the command timeout as the Playwright screenshot timeout, so a page that
never settles fails within the time the caller asked for. Playwright timeouts and failures, including a failed or
timed-out screenshot, become `PuppetException`
messages that name the selector (where there is one) and the timeout.

### Key Methods

**RunAsync**: Runs a session from start-up until it ends.

- *Parameters*: the session id, the server assembly path, and a cancellation token that stops the session from outside.
- *Returns*: 0 after a stop or an idle shutdown, 4 when start-up failed or an unexpected failure ended the session.

Sequence: create the control server with a new token; publish the session file as `Starting` with the control port,
token, and the daemon's process identity; start the server and record its URL and process identity; launch the
browser, record its process identity, and create the `CommandExecutor`; publish the session file as `Ready`; and
serve the control channel until it ends. Every record is published through `SessionStore.WriteIfOwner`, so the
daemon writes only while the file is missing, corrupt, or its own. If a record of a different session is found (the
id was force-stopped and reused while this daemon was still starting), the daemon is superseded: it writes nothing
more, not even a failure record, tears down its own server and browser, leaves the other session's file alone,
and returns 4. In a `finally` block that runs on every path, the browser is closed first and the
server second. After a normal end the session file is deleted conditionally (`DeleteIfSame`, under the start lock):
only the record this daemon published is removed, so a replacement session that another `start` published for the
same id is never deleted. When a `start` holds the lock, the record is left for that start (or the next lookup, which
finds this daemon gone) to clean up; the server and browser are already released by then.

**HandleAsync**: The handler of the control server. The `stop` command cancels the session and answers success.
Every other command goes to the `CommandExecutor`; while the session is still starting, such a command is answered
with a failed response asking the caller to retry shortly.

### Error Handling

A `PuppetException` while starting the server or the browser writes the session file as `Failed` with the message,
tears down whatever was started, and returns 4. An unexpected exception (any other type, except out-of-memory) in the
same phase, or later while serving, is handled the same way: the session file is written as `Failed` with the
`Unexpected error (<type>): <first line>` description and the host returns 4. The file is left behind on purpose so
that the waiting `start` command can read the reason and delete it; after the daemon has exited, the next lookup
finds the daemon gone, reports the session as stale, and cleans up. A command that fails unexpectedly while the
session is running is not fatal: the control server answers it with a failed response and the session keeps serving.
Cancellation during start-up behaves like a stop. The teardown runs in a `finally` block on every path; it closes the
browser inside a try block and the server in its own finally block, so a failure to close the browser cannot leave
the server running, and a failed teardown cannot replace the exit code of the original failure (when it is the only
failure, the session file is kept as `Failed` with the reason of the teardown failure, for example a server process
that could not be stopped, and the exit code is 4). Writing the failure record is best effort: a failure to write it
(the file is locked or unreadable, or another session owns it) is absorbed, so it can neither hide the original
failure nor skip the teardown. The adapters convert process, Playwright, and
timeout failures into `PuppetException`.

### Dependencies

- **SessionStore**: writes the session states and deletes the file.
- **ControlChannel**: `ControlServer` serves the commands.
- **CommandExecutor**: executes the browser commands; `IBrowserPage` is defined with it.
- **Microsoft.Playwright**: used only by the Playwright adapters.
- **ReviewCenter server executable**: started by `ServerProcessRunner`.

### Callers

- **PuppetProgram**: runs the host for the hidden `__daemon` verb.
