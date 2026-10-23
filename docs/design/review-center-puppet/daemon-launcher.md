## DaemonLauncher

![ReviewCenterPuppet Structure](ReviewCenterPuppetView.svg)

### Purpose

`DaemonLauncher` starts the session daemon as a background process that outlives the command that requested it. The
daemon must keep running after the terminal, script, or tool call that ran `start` has finished, and must not be
killed with it. `IDaemonLauncher` is the abstraction that lets `PuppetProgram` be verified without starting
processes, and `NativeMethods` holds the small part of the Windows API that is needed to create a detached process.

### Data Model

**IDaemonLauncher**: `Launch(sessionId, serverPath)` starts the daemon and returns its process identity: the process
id and the start time, which let the caller end exactly that process later without risk of hitting a recycled id.

**Daemon command line**: the executable itself with the hidden `__daemon` verb, `--session <id>`, and
`--server <path>`. When the process was started through the `dotnet` host, the tool assembly path is passed first.

**Creation flags** (Windows): `DETACHED_PROCESS` (no console), `CREATE_NEW_PROCESS_GROUP` (not stopped by a Ctrl+C
sent to the caller), and `CREATE_BREAKAWAY_FROM_JOB` (outside the job object of the caller).

### Key Methods

**Launch**: Builds the argument list and starts the daemon through `LaunchProcess`, using the Windows path on Windows
and the portable path elsewhere. Returns the process identity (id and start time) of the very process that was
created: on Windows the creation time is read from the process handle that `CreateProcessW` returned, before it is
closed; elsewhere it is read from the `Process` object that started the child. The identity is never looked up again
by process id after the launch, because a daemon that exits at once could have its id recycled by then. A process
that has already exited by the time its start time is read, or whose time cannot be read, is reported as a start
failure; before that failure propagates, `CaptureIdentity` ends the created process (through the creation handle on
Windows, through the `Process` object elsewhere), because a daemon without a recorded identity could never be cleaned
up. The termination is best effort and never replaces the original failure, except that a partial tree kill (the
root ended but a descendant could not be) is reported as a failure that says child processes may remain.

**BuildCommandLine**: Builds a Windows command line. The executable and every argument are enclosed in double quotes,
so a path with spaces stays one argument. Inside the quotes, each double quote is escaped with a backslash and every
run of backslashes that precedes a quote, including the closing one, is doubled, which are the rules that the
Windows command-line splitter and the .NET runtime apply when they read the line back. A path that ends in a
backslash therefore cannot swallow its closing quote, and a hostile value cannot split into extra arguments or inject
an option. These mechanics implement the requirement that every value arrives at the daemon unchanged.

**Windows launch**: Calls `CreateProcessW` through `NativeMethods` with the flags above and without handle
inheritance, in the folder of the tool. An agent harness commonly wraps each command in a job object that ends all
children on exit, so the breakaway flag is requested first. A job that forbids breakaway rejects the flag, and the
launcher then retries without it. The creation time is read from the process handle with `GetProcessTimes` and then
the process and thread handles are closed.

**Portable launch**: Starts the process with the standard API, with no window, and closes the redirected standard
streams so that the daemon never blocks on, or holds open, the pipes of the caller.

### Error Handling

A failure to create the process, including a missing executable path, is raised as a `PuppetException` with the
operating system's message. `PuppetProgram` reports it as a start failure.

Only the command-line construction can be verified in isolation, including, on Windows, a round trip through the
operating system's own command-line splitter, and the reported identity of a real, harmless child process. Starting
a detached daemon, and the survival of
the daemon after the launching command has ended, are exercised by the real-browser end-to-end test and by a manual
demonstration.

### Dependencies

- **DaemonHost**: the daemon that the launched process runs, started through the `__daemon` verb of `PuppetProgram`.
- **.NET runtime services**: process creation and platform invocation of the Windows API.

### Callers

- **PuppetProgram**: calls `Launch` for the `start` verb.
