## Cli

![Cli Structure](CliView.svg)

### Overview

The `Cli` subsystem is the boundary between the host environment's command-line invocation and
the tool's internal logic. It owns argument parsing, output channel management, and exit-code
tracking. All other subsystems receive a `Context` object from this subsystem to read parsed
flags and write output. The `Cli` subsystem contains one unit: `Context`.

### Interfaces

**Context.Create**: Factory method that constructs a `Context` from a command-line argument array.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Parses `string[] args` into flag properties and opens the log file if `--log` is
  present. Returns a fully initialized `Context`. Accepts `--result` as a legacy alias for
  `--results`, the `serve` verb, and `--urls <url>`, which sets the listen URL for `serve`
  (default `http://localhost:5000`).
- *Constraints*: Throws `ArgumentException` for unknown or malformed arguments, including
  `--urls` without a value, `--urls` with a value that is not an absolute http or https URL,
  and `--urls` without the `serve` verb; throws `InvalidOperationException` when the log file
  cannot be opened.

**Context.Serve** and **Context.Urls**: Read-only properties describing the serve request.

- *Type*: In-process .NET properties.
- *Role*: Provider.
- *Contract*: `Serve` is true when the `serve` verb was supplied. `Urls` is the URL supplied
  with `--urls`, or the default when none was supplied.
- *Constraints*: Read-only; consumed by `Program` and by the `Hosting` subsystem.

**Context.WriteLine**: Writes a message to stdout and to the log file.

- *Type*: In-process .NET instance method.
- *Role*: Provider.
- *Contract*: Writes `message` to `Console.Out` and to the log file if one is open. Stdout
  output is suppressed when `Silent` is true; the log file always receives the message.
- *Constraints*: None.

**Context.WriteError**: Writes an error message and sets the error exit code.

- *Type*: In-process .NET instance method.
- *Role*: Provider.
- *Contract*: Sets `_hasErrors` to true, writes `message` in red to `Console.Error`, and writes
  to the log file if one is open. Stderr output is suppressed when `Silent` is true, but
  `ExitCode` is set to 1 regardless.
- *Constraints*: Once set, `ExitCode` cannot return to 0 within the same invocation.

**Context.ExitCode**: Derived property returning 0 or 1.

- *Type*: In-process .NET property.
- *Role*: Provider.
- *Contract*: Returns 1 if `WriteError` has been called at least once; returns 0 otherwise.
- *Constraints*: Read-only.

**Context.Dispose**: Releases the log file `StreamWriter`.

- *Type*: In-process .NET method (`IDisposable`).
- *Role*: Provider.
- *Contract*: Disposes `_logWriter` and sets it to null; flushes any buffered content. Callers
  must use a `using` statement to guarantee disposal.
- *Constraints*: Safe to call multiple times (idempotent after first call).

### Design

The `Cli` subsystem contains only the `Context` unit; there is no subsystem-level code of its
own. All behavior is provided by `Context`. The `Program` unit creates a `Context` at the start
of each invocation and passes it to all other units that produce output.

The subsystem has no dependencies on other tool subsystems; it uses only .NET BCL types
(`Console`, `StreamWriter`, `Uri`). The `Hosting` subsystem depends on it in the other
direction: `ServerHost` reads the listen URL and the silent option from `Context`, and reports
a failure to start through `Context.WriteError`.

Error handling flows from `Context.Create` to `Program.Main`: argument parsing errors propagate
as `ArgumentException`; log-file errors propagate as `InvalidOperationException`. Both are
caught and handled in `Program.Main`, which writes the error to stderr and returns exit code 1.

When `--log` is active, `Context` holds an open `StreamWriter` for the duration of the
invocation. The `Program.Main` call site wraps the `Context` in a `using` statement to ensure
the file handle is released and buffered content is flushed on exit.
