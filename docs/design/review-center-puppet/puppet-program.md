## PuppetProgram

![ReviewCenterPuppet Structure](ReviewCenterPuppetView.svg)

### Purpose

`PuppetProgram` is the entry point and command dispatcher of Puppet. It parses the command line, runs the verb
that was requested, and maps every outcome to one of five exit codes. It orchestrates the `start`, `stop`, and
`list` verbs itself and forwards every browser verb to the session daemon. `PuppetServices` bundles the
collaborators it needs so that tests can replace them with fakes.

### Data Model

**Exit codes**: `ExitOk` (0), `ExitFailed` (1, a failed command or invalid arguments), `ExitUnknownSession` (2, no
such session or none running), `ExitStaleSession` (3, the daemon is gone and the file was cleaned up), and
`ExitStartFailed` (4, the session could not be started).

**PuppetServices**: a record holding the `SessionStore`, the `IDaemonLauncher`, the `IServerRunner`, and the
`IBrowserLauncher`, and optionally a start time-out that replaces the default. `PuppetServices.CreateDefault()`
builds the production set with the real adapters.

**ServerEnvironmentVariable**: `REVIEWCENTER_SERVER_DLL`, which overrides the default location of the server
assembly.

### Key Methods

**Main**: Entry point of the process. Calls `RunAsync` with the production services and returns its exit code.

**RunAsync**: Runs one command.

- *Parameters*: the argument array, the output and error writers, the `PuppetServices`, and a cancellation token.
- *Returns*: the exit code.

Parses the arguments with `CommandLine.Parse`. For `--help` style requests it prints the usage text and returns 0.
For the hidden `__daemon` verb it runs `DaemonHost`. For `start`, `list`, and the other verbs it calls the matching
private handler.

**ResolveServerPath**: Chooses the server assembly path. The `--server` option wins, then the
`REVIEWCENTER_SERVER_DLL` environment variable, then the path recorded in the build metadata of the tool. A path
that does not exist raises a `PuppetException` that tells the caller to build the solution or to set the option.

**Start handling**: Resolves the server assembly, then reserves the session id with `SessionStore.TryReserve`
(generating an eight-character id first when none was given). When the id is already reserved by another `start`, it
fails at once with exit code 1 and an error that says so, and launches nothing. While it holds the reservation, it
rejects an id that is already live, cleans up a stale file, launches the daemon through `IDaemonLauncher`, and polls
the session file every 200 milliseconds for up to 90 seconds. It prints the session id and the server URL when the
status is `Ready` and returns 4 with the reason when the status is `Failed`, when the daemon dies, or when the time
runs out. The reservation is released in a `finally` block on every path, including a failed launch. The existing
session is looked up with the reservation already held (`startLockHeld`), so the lookup can clean up a stale file
without competing with this start for the lock.

**Failed start**: The daemon publishes `Failed` before it releases the browser and the server, and the session file
is the only record of a server or browser that the teardown could not stop. `start` therefore does not delete the
file when it first sees `Failed`: it waits (polling every 100 milliseconds, up to the stop time-out) for the daemon
to exit, then runs the start abort described below, which terminates whatever the file still records and deletes
the file while this start still holds the reservation. The reason from the file is printed and the exit code is 4.

**Start abort**: When the wait ends without a ready session (time-out, cancellation, a dead daemon, or a failed
daemon that has been waited for), `start`
terminates the daemon it launched, identified by its process id and start time and known even though no session file
may have been written yet. It also terminates the server and the browser that a partly written session file records.
Termination is asynchronous and can fail, and the file is the only record that lets a later command find a survivor,
so the abort loops until no process of the daemon and the file is alive, and deletes the file only then. Each round
kills the known processes (the browser, the server, then the daemon; the launched daemon by its own identity even
when the file does not name it), polls every 100 milliseconds up to the stop time-out, repeating the kill on each poll
and ignoring caller cancellation, and re-reads the file after the poll. Because the daemon is still running while it
is being killed, it may record a server or browser after the abort last looked; when the record changed, the abort
follows it (a start holds the reservation, so only the aborted daemon can have written it) and kills what it now
names. The deletion is `DeleteIfSame` with the record last examined and the reservation held by this start: if it
reports `Replaced` the abort starts another round (at most three), and if it reports `Busy` the cleanup is
reported as unconfirmed with the file kept. There is no unconditional delete. When a
process survives, the file is kept and the error says that the cleanup could not be confirmed; a later `start`,
`stop`, or lookup finds the survivors through the file. A cancellation is re-raised after this cleanup. Nothing is
ended by name.

**Client command handling**: Resolves the session (the `--session` option, the `REVIEWCENTER_PUPPET_SESSION`
environment variable, or the only running session), looks it up in `SessionStore`, builds a `ControlRequest` with
the stored token, sends it with `ControlClient`, prints the message of the response, and returns its exit code. For
`stop` it also waits up to the stop time-out (30 seconds by default; `PuppetServices.StopTimeout` shortens it for
tests) for the daemon to end, then runs `ForceStop`. A daemon that cannot be asked at all goes straight to
`ForceStop`.

**ForceStop**: Re-reads the session file, because the daemon may have recorded more processes or marked the session
`Failed` since the stop began. When the file still describes the same session (`SessionStore.IsSameSession`), it
terminates the browser, the server, and the daemon from that fresh record, otherwise from the record the stop
examined; each is verified by process id and start time, so a newer session's processes are never touched. It uses
the same bounded terminate-and-confirm loop as the start abort, except that it follows a changed record only when it
is still the same session (`IsSameSession`): the daemon of that session may have recorded a server meanwhile, but a
record of another session is never followed, because no start reservation is held that would prove it is ours. Only
when every process is confirmed gone does it
delete the file with `SessionStore.DeleteIfSame`, which keeps a replacement session that a concurrent `start`
published in the meantime. When a process survives, the file is kept, `stop` prints that the stop could not be
confirmed, and returns exit code 1 so that the command can be repeated. The same path handles a daemon whose own
teardown failed and left a server running.

**Named values**: the session id length, the default start time-out, the stop time-out, the two polling intervals,
and the grace period added to a control exchange are named constants, so that their meaning is documented once.

**List handling**: Prints one line per live session with its server URL, reports when there are none, and removes
stale sessions it finds.

### Known Limitation

`stop` cannot interrupt a `start` that has not finished. The control loop of the daemon starts only after start-up
completes, and a command received earlier is answered with a "retry shortly" failure. The start-up is bounded by the
start time-out, after which the start is aborted as described above.

**List handling**: Prints one line per live session with its server URL, reports when there are none, and removes
stale sessions it finds.

### Error Handling

`ArgumentException` from `CommandLine` becomes exit code 1 with the message on the error writer. `PuppetException`
from the control channel or from path resolution becomes exit code 1, or 4 during `start`. A `start` for an id that
another `start` has reserved is exit code 1. An unknown session is
exit code 2 and a stale session is exit code 3. The message text always names the problem and, where possible, the
next step. Unexpected exceptions are not caught, so that the runtime records them.

### Dependencies

- **CommandLine**: parses the arguments.
- **SessionStore**: looks up, lists, and deletes sessions.
- **ControlChannel**: `ControlClient` sends the browser commands to the daemon.
- **DaemonLauncher**: starts the daemon.
- **DaemonHost**: runs inside the daemon for the `__daemon` verb.

### Callers

N/A - entry point, called by the host environment.
