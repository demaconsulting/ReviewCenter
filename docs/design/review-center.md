# ReviewCenter

![ReviewCenter Structure](ReviewCenterView.svg)

## Architecture

The ReviewCenter is an ASP.NET Core web service with a companion command-line interface,
built on .NET. It is structured as one system containing one top-level unit (`Program`) and
five subsystems (`Cli`, `SelfTest`, `Utilities`, `Hosting`, `Web`):

```mermaid
flowchart TD
    Program
    subgraph Cli
        Context
    end
    subgraph SelfTest
        Validation
    end
    subgraph Utilities
        PathHelpers
    end
    subgraph Hosting
        ServerHost
    end
    subgraph Web
        App
        HomePage
    end
    Program --> Context
    Program --> Validation
    Program --> ServerHost
    Validation --> Program
    Validation --> PathHelpers
    ServerHost --> Context
    ServerHost --> App
    App --> HomePage
```

`Program` is the entry point. It creates a `Context` from the `Cli` subsystem, dispatches to
`Validation` when `--validate` is passed, dispatches to `ServerHost` from the `Hosting`
subsystem when the `serve` verb is passed, and returns the exit code from `Context`.
`Validation` calls `Program.Run` recursively to exercise the tool during self-testing, and
uses `PathHelpers` to construct safe temporary file paths.

`ServerHost` builds and runs the ASP.NET Core web application: it reads the listen URL and
the silent option from `Context`, configures structured logging, maps the `/health` endpoint,
and maps the user interface defined by the `Web` subsystem. The `Web` subsystem is a Blazor
Server user interface: `App` is the HTML shell and router, and `HomePage` is the interactive
page served at the root URL. A browser connects to the server, receives a server-rendered
page, and then upgrades to a live connection over which user actions are handled on the
server.

## External Interfaces

**Command-Line Interface**: The primary input interface for tool invocation.

- *Type*: CLI.
- *Role*: Consumer (the host environment invokes the system with command-line arguments).
- *Contract*: Accepts the `serve` verb and the arguments `-v`/`--version`, `-?`/`-h`/`--help`,
  `--silent`, `--validate`, `--results <file>`, `--result <file>` (legacy alias for
  `--results`), `--depth <n>`, `--log <file>`, and `--urls <url>`. Returns exit code 0 for
  success and 1 for failures.
- *Constraints*: Unknown arguments cause exit code 1 and an error message on stderr.
  `--urls` is only valid together with `serve`, and its value must be an absolute http or
  https URL. When `serve` is used, the process runs until it is stopped (Ctrl+C or a
  termination signal) and then exits with code 0; a server that cannot start, for example
  because the address is in use, causes exit code 1 and an error message.

**Web Endpoints**: The HTTP interface of the running web server, present only while `serve`
is running.

- *Type*: HTTP (and the Blazor live connection used by the interactive user interface).
- *Role*: Provider.
- *Contract*: `GET /` returns the home page, an HTML document titled "ReviewCenter" with a
  Hello button and a counter button. `GET /health` returns HTTP status 200 with the plain text
  `Healthy`. Any other path returns the not-found message of the user interface. The client
  script for the live connection is served from `/_framework/blazor.web.js`.
- *Constraints*: The server listens on the URL given with `--urls`, by default
  `http://localhost:5000`, which is reachable only from the local machine. TLS termination is
  not performed by the server itself.

**Standard Output**: Normal program output written to `Console.Out`.

- *Type*: Standard I/O.
- *Role*: Provider.
- *Contract*: Writes version, banner, help text, validation summary, or a usage hint depending
  on the flags provided. While the web server runs, its structured log entries, including a
  start-up entry that names the listen URL, are also written to the console. Suppressed when
  `--silent` is active; the log file still receives all output written through `Context`.
- *Constraints*: Human-readable text; no machine-parseable format contract.

**Standard Error**: Error message output written to `Console.Error`.

- *Type*: Standard I/O.
- *Role*: Provider.
- *Contract*: Writes error messages in red when a failure occurs. Suppressed when `--silent`
  is active but the exit code is still set to 1.
- *Constraints*: Color output requires a terminal that supports `ConsoleColor`.

**Log File**: Optional persistent output file.

- *Type*: File.
- *Role*: Provider.
- *Contract*: When `--log <file>` is supplied, all `Context.WriteLine` and `Context.WriteError`
  output is written to the file regardless of `--silent`. The file is truncated at open.
- *Constraints*: The path must be writable; failure to open the file raises an
  `InvalidOperationException` and causes exit code 1.

**Results File**: Optional self-validation results file.

- *Type*: File.
- *Role*: Provider.
- *Contract*: When `--results <file>` is supplied alongside `--validate`, self-validation results
  are serialized to the file. Extension `.trx` selects MSTest TRX format; `.xml` selects JUnit
  XML format.
- *Constraints*: Any other extension causes an error message and exit code 1; no file is written.

## Dependencies

- **DemaConsulting.TestResults**: provides `TestResults`, `TestResult`, and `TestOutcome` for
  accumulating self-validation results.
- **DemaConsulting.TestResults.IO**: provides `TrxSerializer` and `JUnitSerializer` for writing
  results files.
- **ASP.NET Core shared framework**: provides the Kestrel web server, endpoint routing,
  Blazor Server components, and `Microsoft.Extensions.Logging` used by the `Hosting` and `Web`
  subsystems. It is supplied by the .NET runtime rather than as a package; see the ASP.NET Core
  integration design in the OTS chapter.

## Risk Control Measures

N/A - not a safety-classified software item.

## Data Flow

1. The host environment starts the tool process and passes command-line arguments to
   `Program.Main`.
2. `Program.Main` calls `Context.Create(args)`, which parses the arguments and opens the log
   file if `--log` was specified. An `ArgumentException` or `InvalidOperationException` at
   this point is caught, written to stderr, and causes exit code 1.
3. `Program.Run(context)` inspects the parsed flags and dispatches to one handler:
   - `--version` flag → `context.WriteLine(Version)`, then return.
   - Otherwise, `PrintBanner` is called first; then:
     - `--help` flag → `PrintHelp(context)`, then return.
     - `--validate` flag → `Validation.Run(context)`.
     - `serve` verb → `ServerHost.RunAsync(context, cancellationToken)`, which blocks until the
       server stops (see below).
     - No flags → `RunToolLogic(context)`.
4. `Program.Main` returns `context.ExitCode` (0 if no errors were reported, 1 otherwise).

For the `serve` verb, `ServerHost` builds the web application from the `Context` (listen URL and
silent option), starts it, and writes the start-up log entry. A browser request for `/` is
routed by `App` to `HomePage`, which is rendered on the server into an HTML document and
returned. The client script in that document opens a live connection back to the server, after
which button clicks are sent to the server, handled by `HomePage`, and the changed page parts
are sent back. A request for `/health` is answered directly by `ServerHost`. When the server is
stopped, `ServerHost.RunAsync` returns 0; if the server cannot start, it reports the reason
through `Context.WriteError` and returns 1, which makes `Context.ExitCode` 1.

## Design Constraints

- Platform: targets net10.0 only, on Windows and Linux.
- Threading: the command-line actions are single-threaded. The web server handles requests on
  the ASP.NET Core thread pool; each browser connection has its own page state, and all
  handlers of one connection run on a single synchronization context, so no shared mutable
  state exists between connections.
- Network exposure: by default the server listens on the loopback interface only; exposing it
  on other interfaces requires an explicit `--urls` value.
- Immutability: `Context` properties are set once at construction via `init` accessors and are
  read-only thereafter.
- Resource lifecycle: `Context` implements `IDisposable`; callers must dispose it to flush and
  close any open log file handle.
- Path safety: all caller-supplied path components are validated by `PathHelpers.SafePathCombine`
  before file-system use.
