## Program

![ReviewCenter Structure](ReviewCenterView.svg)

### Purpose

`Program` is the entry point and execution orchestrator for the ReviewCenter. Its single
responsibility is to create a `Context` from the command-line arguments, dispatch to the
appropriate handler based on the parsed flags, and return the exit code.

### Data Model

**Version**: `string` (static property) — The tool version read from
`AssemblyInformationalVersionAttribute` on every access, falling back to `AssemblyVersion`, then
`"0.0.0"`. No caching is applied; callers that need the value more than once should store it
locally.

### Key Methods

**Main**: Entry point for the tool process.

- *Parameters*: `string[] args` — command-line arguments from the host environment.
- *Returns*: `int` — exit code; 0 for success, 1 for expected errors.
- *Preconditions*: None.
- *Postconditions*: Exit code reflects whether any errors were reported during execution.

Creates a `Context` using `Context.Create(args)`, calls `Run(context)`, and returns
`context.ExitCode`. Catches `ArgumentException` and `InvalidOperationException` — writes
`"Error: {message}"` to stderr and returns 1. Catches any other `Exception` — writes
`"Unexpected error: {message}"` to stderr and re-throws so the runtime can record it.

**Run**: Dispatches execution based on parsed flags, with no external stop request.

- *Parameters*: `Context context` — the parsed context.
- *Returns*: `void`.
- *Preconditions*: `context` is not null.
- *Postconditions*: Exactly one handler has been called.

Inspects flags in priority order: (1) if `context.Version` is true, calls
`context.WriteLine(Version)` and returns; (2) calls `PrintBanner`; (3) if `context.Help` is
true, calls `PrintHelp` and returns; (4) if `context.Validate` is true, calls
`Validation.Run(context)`; (5) if `context.Serve` is true, runs the web server through
`ServerHost.RunAsync` and blocks until it stops; (6) if `context.UserAdd` is true, calls `UserCommand.Run(context)`;
(7) if `context.AuditVerify` is true, calls `AuditCommand.Run(context)`; (8) otherwise calls `RunToolLogic(context)`.
`Run` supplies no stop request; the process stops the server with Ctrl+C or a termination
signal.

**RunWithCancellation**: Dispatches execution like `Run`, with a request to stop long-running
work.

- *Parameters*: `Context context` — the parsed context; `CancellationToken cancellationToken` —
  requests a graceful stop of the web server started by the `serve` verb. Ignored by all other
  actions.
- *Returns*: `void`.
- *Preconditions*: `context` is not null.
- *Postconditions*: Exactly one handler has been called; for `serve`, the method returns only
  after the server has stopped, and any start-up failure has been reported through
  `context.WriteError`.

`Run` delegates to this method with `CancellationToken.None`. Tests use it to stop the server
deterministically.

**PrintBanner**: Writes the tool name, version, and copyright line to `context`.

- *Parameters*: `Context context` — output target.
- *Returns*: `void`.

**PrintHelp**: Writes the usage synopsis, the `serve`, `user add`, and `audit verify` verbs, and the options table
(including
`--urls`, `--username`, `--display-name`, and `--system-admin`) to `context`. The help text says that the password of
`user add` is read from the environment variable `REVIEWCENTER_PASSWORD`, from standard input, or from a prompt, and
that `audit verify` exits with 0 when the chain is intact and 1 when it is broken.

- *Parameters*: `Context context` — output target.
- *Returns*: `void`.

**RunToolLogic**: Default action when no action was requested; writes a hint to `context`
that points the user to the `serve` verb and to the help text.

- *Parameters*: `Context context` — output target.
- *Returns*: `void`.

### Error Handling

`Main` handles errors at two levels. Expected errors (`ArgumentException` and
`InvalidOperationException`) are written to stderr as `"Error: {message}"` and cause exit
code 1 without a stack trace. Unexpected errors (`Exception`) are written to stderr as
`"Unexpected error: {message}"` and re-thrown so the runtime can record them in event logs.
`Run`, `RunWithCancellation`, `PrintBanner`, `PrintHelp`, and `RunToolLogic` do not catch
exceptions; all errors propagate to `Main`. The exception is a web server that cannot start,
for example because the address is in use: `ServerHost.RunAsync` reports it through
`Context.WriteError` and returns a failure result, so `Program` treats it as an ordinary
error that yields exit code 1 rather than an unhandled exception.

### Dependencies

- **Context** — `Program` reads parsed flags from `Context` and calls `Context.WriteLine` and
  `Context.WriteError` for all output.
- **Validation** — `Program.Run` calls `Validation.Run(context)` when the `--validate` flag is
  set.
- **ServerHost** — `Program.RunWithCancellation` calls `ServerHost.RunAsync(context,
  cancellationToken)` when the `serve` verb is present.
- **UserCommand** — `Program.RunWithCancellation` calls `UserCommand.Run(context)` when the `user add` verb is
  present.
- **AuditCommand** — `Program.RunWithCancellation` calls `AuditCommand.Run(context)` when the `audit verify` verb is
  present.

### Callers

N/A - entry point, called by the host environment.
