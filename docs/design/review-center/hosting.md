## Hosting

![Hosting Structure](HostingView.svg)

### Overview

The `Hosting` subsystem turns a parsed command-line request into a running web server. It owns
the composition of the ASP.NET Core application behind the `serve` verb: the listen address,
the logging configuration, the health endpoint, the mapping of the user interface provided
by the `Web` subsystem, and the connection of the pages to the database: reading the `Database` settings,
creating the database, and registering the `Core` services with the `Storage` store. It also owns the
start, run, and stop lifecycle of that application and
the translation of start-up failures into ordinary error reports. The subsystem contains one
unit: `ServerHost`, together with its small options type `ServerHostOptions`, which is
documented with `ServerHost` as a supporting type.

### Interfaces

**ServerHost.RunAsync**: Builds and runs the web server until it is stopped.

- *Type*: In-process .NET static asynchronous method.
- *Role*: Provider.
- *Contract*: Takes the parsed `Context` and a cancellation token, runs the server, and
  returns 0 after a clean stop or 1 after a start-up failure (an unavailable address or a database
  that cannot be prepared) that was reported through `Context.WriteError`.
- *Constraints*: Binds a network port for the duration of the call and creates the database if it does
  not exist; the caller is `Program`.

**ServerHost.CreateApplication**: Builds the web application without starting it.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Returns a configured application for the supplied `Context`, with the database created.
  An optional
  `ServerHostOptions` adjusts the build, which lets tests run the production pipeline against
  an in-memory server and a temporary database.
- *Constraints*: Throws `ArgumentNullException` for a null context and `InvalidOperationException`
  ("Failed to prepare the database: ...") when the database cannot be prepared. Nothing is bound to a
  network port until the returned application is started.

**Health endpoint**: The `/health` route of the running server.

- *Type*: HTTP `GET`.
- *Role*: Provider.
- *Contract*: Answers HTTP status 200 with the plain text `Healthy` while the server runs.
- *Constraints*: Unauthenticated; carries no information beyond liveness.

**Context (consumed)**: The subsystem reads `Context.Urls` and `Context.Silent`, and calls
`Context.WriteError` to report a failure to start.

- *Type*: In-process .NET members.
- *Role*: Consumer.

**App (consumed)**: The root component of the `Web` subsystem, mapped as the user interface of
the server.

- *Type*: In-process .NET type.
- *Role*: Consumer.

**Storage and Core (consumed)**: `DatabaseSetup`, `ReviewCenterDbContext`, and `ReviewStore` of the
`Storage` subsystem, and `ProjectService`, `ReviewService`, and the store interface of the `Core` subsystem.

- *Type*: In-process .NET types.
- *Role*: Consumer.

### Design

`ServerHost` is the only unit. `RunAsync` calls `CreateApplication` and runs the resulting
application until the cancellation token is cancelled or the process receives a termination
signal. `CreateApplication` performs the composition in a fixed order: it sets the content
root to the application directory so that static web assets are found regardless of the
working directory; applies the listen URL; configures logging with the console provider
included unless the context is silent; registers Blazor Server components; registers the persistence
services (below); builds the
application; creates the database; enables request forgery protection; maps static web assets; maps the
`/health` route;
maps the `App` root component with the interactive server render mode; and registers a
callback that writes the structured start-up log entry once the server has started.

The persistence registration binds the `Database` configuration section (`appsettings.json` and
environment variables such as `Database__Provider` and `Database__ConnectionString`) to the options type
of the `Storage` subsystem, registers a `ReviewCenterDbContext` factory configured by `DatabaseSetup`,
and registers `TimeProvider.System`, `ReviewStore` as the Core store interface, and the two Core
services as singletons shared by all browser connections. Services hold no per-user state, and every
database operation opens its own short-lived context from the factory.

The database is created right after the application is built, before the first request, so that a
problem with the settings or the file system stops the start-up with one clear message rather than
failing on the first page request.

The composition is shared by production and tests. `ServerHostOptions` lets a test switch off
static asset mapping, which needs a manifest that only the real application has, and attach an
in-memory server and a capturing logger provider. It also lets a test point the server at a temporary
database through the configuration. This keeps the tested pipeline identical to
the shipped one.

Error handling is local to the subsystem: an address that is unavailable at start-up, which
Kestrel reports as an `IOException`, is converted into a message on `Context.WriteError` and a
result of 1; a database that cannot be prepared (an unsupported provider, an unusable location) raises
an `InvalidOperationException` while building, which `RunAsync` converts into the same kind of error
report and a result of 1; and a cancellation requested before start-up completes is treated as a normal
stop. Any other exception propagates to `Program.Main`.

Structured logging is built on `Microsoft.Extensions.Logging` message templates with named
placeholders, so the listen URL is available as a named property to any log sink that
understands structured entries. Framework components, such as request handling, write to the
same logging pipeline.
