## CommandLine

![ReviewCenterPuppet Structure](ReviewCenterPuppetView.svg)

### Purpose

`CommandLine` parses the arguments of one Puppet invocation into a verb, its positional arguments, and the global
options. Parsing is strict because the tool is run by scripts and coding agents that cannot ask what a mistyped
command meant: anything unexpected is rejected with a message that names the problem.

### Data Model

**Verb**: `string` - one of `start`, `goto`, `click`, `fill`, `text`, `screenshot`, `snapshot`, `stop`, `list`, or the
hidden `__daemon` verb used to start the daemon process.

**Positionals**: `IReadOnlyList<string>` - the arguments of the verb: one for `goto`, `click`, and `text`; two
(selector and text) for `fill`; none for the others.

**SessionId**: `string?` - the value of `--session`, or `null` when it was omitted.

**TimeoutSeconds**: `int` - the value of `--timeout`, or `DefaultTimeoutSeconds` (10) when it was omitted.
`TimeoutMilliseconds` is the same value in milliseconds.

**OutputPath**: `string?` - the value of `--out` (screenshot only).

**FullPage**: `bool` - set by `--full-page` (screenshot only).

**ServerPath**: `string?` - the value of `--server` (start only).

**SessionEnvironmentVariable**: `REVIEWCENTER_PUPPET_SESSION`, the name of the environment variable that selects a
session when `--session` is omitted. The variable itself is read by `PuppetProgram`.

### Key Methods

**Parse**: Parses an argument array.

- *Parameters*: `string[] args`.
- *Returns*: a `CommandLine` instance.
- *Preconditions*: `args` is not null.
- *Postconditions*: The verb is known, the number of positional arguments matches the verb, and every option is
  valid for the verb.

Reads the first argument as the verb, then reads the remaining arguments as options with values or as positional
arguments.

**IsValidSessionId**: Returns true when an id is 1 to 32 letters, digits, hyphens, or underscores. The tight
character set keeps an id usable as a file name without any path traversal risk. `SessionStore` applies the same
rule before it builds a file path.

### Error Handling

`Parse` throws `ArgumentException` with a message for: no arguments, an unknown verb, a wrong number of positional
arguments, an unknown option, an option without its value, an invalid session id, a timeout that is not a whole
number from 1 to 600, `--out` or `--full-page` on a verb other than `screenshot`, and `--server` on a verb other
than `start`. The exception is caught by `PuppetProgram`, which reports it and exits with code 1.

### Dependencies

None - `CommandLine` uses only the .NET base class library.

### Callers

- **PuppetProgram**: calls `Parse` for every invocation.
- **SessionStore**: calls `IsValidSessionId` before building a session file path.
