## PuppetProgram

![ReviewCenterPuppet Structure](ReviewCenterPuppetView.svg)

### Purpose

`PuppetProgram` is the entry point of Puppet and the client side of it. It parses and validates one command line,
handles `start` by spawning the daemon, sends every other command to the running daemon through `ControlChannel`,
and prints the result. The hidden verb `daemon` runs `BrowserDaemon` in the same executable.

### Data Model

**Command table**: one entry per command with its usage text, the minimum and maximum number of positional
arguments, and the options it accepts. It drives both validation and `--help`. Flags (`--full`, `--headed`) take no
value; every other option takes one.

**Exit codes**: 0 the command worked; 1 it failed (including `start` when Puppet is already running or the daemon
could not start); 2 a usage error, Puppet is not running, or the daemon process could not be spawned.

**PuppetException**: the exception used for every error that is reported to the user as a plain message.

### Key Methods

**RunAsync**: Runs one invocation and returns the exit code. `--help` prints the usage and returns 0.

**ParseCommand**: Splits the arguments into positionals and options, rejects unknown options and wrong argument
counts, validates values (viewport, theme, coordinates, timeout, base URL), and makes screenshot and upload paths
absolute because the daemon runs in another working directory. An option name begins with `--`, so a `fill` text that
begins with `--` must be set with `eval` instead.

**Format**: Prints the result, then `@ <url>  "<title>"`, then one `! <event>` line per new browser error. Output of
a failed command goes to the error writer. Returns 0 or 1.

**StartAsync**: Refuses when the control file names a port that answers a ping (exit code 1). Otherwise deletes a stale
file, spawns the daemon, and polls every 100 ms for up to 30 seconds for the control file. A start-up error written
by the daemon, an early daemon exit, or the time-out (which also kills the daemon process) is reported with exit
code 1. Success prints one line with the port and base URL.

**Spawn**: Starts `dotnet <Puppet.dll> daemon ...` detached (see the lifecycle in the system chapter).

### Error Handling

A `PuppetException` is printed on the error writer and gives exit code 2. A missing control file, or one whose port
refuses the connection, gives exit code 2 with the hint to run `puppet start`.

### Dependencies

- **ControlChannel**: reads the control file and sends requests.
- **BrowserDaemon**: run for the `daemon` verb.
- **PlaywrightBrowser**: its viewport parser validates `--viewport` and `viewport`.
