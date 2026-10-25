### Context

![Cli Structure](CliView.svg)

#### Purpose

`Context` handles command-line argument parsing and program output for one tool invocation. Its
single responsibility is to parse the argument list, expose the parsed flags as read-only
properties, own the two output channels (console and log file), and derive the exit code from
whether any errors were reported.

#### Data Model

**_logWriter**: `StreamWriter?` — Log file writer; `null` when logging is not active.

**_hasErrors**: `bool` — Set to `true` on the first `WriteError` call; once set, cannot return
to `false` within the same invocation.

**Version**: `bool` — `true` when `-v` or `--version` was present in the argument list.

**Help**: `bool` — `true` when `-?`, `-h`, or `--help` was present in the argument list.

**Silent**: `bool` — `true` when `--silent` was present in the argument list.

**Validate**: `bool` — `true` when `--validate` was present in the argument list.

**ResultsFile**: `string?` — Path supplied after `--results` or `--result`, or `null` if
neither flag was present.

**HeadingDepth**: `int` — Heading depth for markdown output; valid range 1–6, default 1;
supplied via `--depth`.

**Serve**: `bool` — `true` when the `serve` verb was present in the argument list.

**Urls**: `string` — The URL the web server listens on; the value supplied via `--urls`, or
`DefaultUrls` when the option was not supplied. Always an absolute http or https URL.

**DefaultUrls**: `string` (constant) — `http://localhost:5000`; the loopback address used when
`--urls` is not supplied, so that a freshly started server is not reachable from other machines.

**UserAdd**: `bool` — `true` when the `user add` verb was present in the argument list.

**AuditVerify**: `bool` — `true` when the `audit verify` verb was present in the argument list.

**Username**: `string?` — The user name supplied via `--username`; set whenever `UserAdd` is `true`.

**DisplayName**: `string?` — The display name supplied via `--display-name`; set whenever `UserAdd` is `true`.

**Role**: `UserRole` — The role supplied via `--role` (`Admin` or `Reviewer`, without regard to case);
`Reviewer` when the option was not supplied.

**ExitCode**: `int` (derived) — Returns 1 if `_hasErrors` is true; returns 0 otherwise.

#### Key Methods

**Create**: Factory method that parses arguments and returns a fully initialized `Context`.

- *Parameters*: `string[] args` — raw command-line argument array.
- *Returns*: `Context` — a new instance with all flags set.
- *Preconditions*: `args` is not null.
- *Postconditions*: All flag properties reflect the parsed argument state; the log file is open
  if `--log` was supplied.

Delegates to the private `ArgumentParser` helper to parse flags, then opens the log file by
calling `OpenLogFile` if `--log` was present. The parser treats `serve` as a verb that sets
`Serve`, and `--urls` as an option whose value must parse as an absolute http or https URL.
It treats `user` as a verb that must be followed by the action `add`, which sets `UserAdd`, and `audit` as a verb that
must be followed by the action `verify`, which sets `AuditVerify`; `--username` and
`--display-name` take a text value and `--role` takes the name of a role (numbers are not accepted).
After all arguments are consumed, the parser rejects a `--urls` value given without `serve`, rejects the user options
given without `user add`, and rejects `user add` without both `--username` and `--display-name`.
The password is not an argument: it is never read from the command line (see `UserCommand`).
Throws `ArgumentException` for unknown or malformed arguments; throws
`InvalidOperationException` if the log file cannot be opened.

**WriteLine**: Writes a message to standard output and to the log file.

- *Parameters*: `string message` — the message to write.
- *Returns*: `void`.
- *Preconditions*: None.
- *Postconditions*: Message is on stdout (unless `Silent`) and in the log file (if open).

**WriteError**: Writes an error message, sets the error state, and records to the log file.

- *Parameters*: `string message` — the error message.
- *Returns*: `void`.
- *Preconditions*: None.
- *Postconditions*: `_hasErrors` is true; message is on stderr in red (unless `Silent`) and in
  the log file (if open).

**Dispose**: Disposes the log file writer.

- *Parameters*: None.
- *Returns*: `void`.
- *Preconditions*: None.
- *Postconditions*: `_logWriter` is disposed and set to null; any buffered log content is
  flushed.

#### Error Handling

`Create` throws `ArgumentException` ("Unsupported argument '{arg}'") for any unrecognized flag
or missing required value. For `--urls` it throws `ArgumentException` when the value is
missing, when the value is not an absolute http or https URL
("--urls requires an absolute http or https URL but was '{value}'"), and when `--urls` is
supplied without the `serve` verb ("--urls requires the serve verb"). For `user` it throws `ArgumentException` when
the action is missing or is not `add` ("user requires the add action"), for `--username` and `--display-name` when the
value is missing, for `--role` when the value is missing or is not `Admin` or `Reviewer`
("--role requires Admin or Reviewer but was '{value}'"), when a user option is supplied without `user add`
("--username, --display-name and --role require the user add verb"), and when `user add` lacks a name
("user add requires --username and --display-name"). For `audit` it throws `ArgumentException` when the action is
missing or is not `verify` ("audit requires the verify action"). It throws
`InvalidOperationException` ("Failed to open log file '{path}': {detail}") when the `--log`
file cannot be opened. Both exceptions propagate to `Program.Main`.

`WriteLine` and `WriteError` do not throw; they write to whichever output channels are
available.

`Dispose` does not throw; any disposal errors are silently ignored.

#### Dependencies

- **.NET BCL** — `Console`, `StreamWriter`, `Path`, and `Uri`.
- **UserRole** (Core) — the type of the `Role` property; no other tool units are used.

#### Callers

- **Program** — creates `Context` via `Context.Create` and calls `WriteLine` and `WriteError`.
- **Validation** — receives `Context` from `Program` and calls `WriteLine` and `WriteError`.
- **ServerHost** — reads `Urls` and `Silent`, and calls `WriteError` when the server cannot
  start.
- **UserCommand** — reads `Username`, `DisplayName`, and `Role`, and calls `WriteLine` and `WriteError`.
- **AuditCommand** — calls `WriteLine` for an intact chain and `WriteError` for a broken chain or a read failure.
