## Hosting

![Hosting Structure](HostingView.svg)

### Overview

The `Hosting` subsystem turns a parsed command-line request into a running web server, and creates users from the
command line. It owns
the composition of the ASP.NET Core application behind the `serve` verb: the listen address,
the logging configuration, the health endpoint, the sign-in configuration and endpoints of the `Identity`
subsystem, the mapping of the user interface provided
by the `Web` subsystem, and the connection of the pages to the database: reading the `Database` settings,
creating the database, and registering the `Core` services with the `Storage` stores. It also owns the
start, run, and stop lifecycle of that application and
the translation of start-up failures into ordinary error reports. The `user add` verb reuses the same registration of
the database and the services to create a user without starting a server. The subsystem contains two
units: `ServerHost`, together with its small options type `ServerHostOptions`, which is
documented with `ServerHost` as a supporting type, and `UserCommand`.

### Interfaces

**ServerHost.RunAsync**: Builds and runs the web server until it is stopped.

- *Type*: In-process .NET static asynchronous method.
- *Role*: Provider.
- *Contract*: Takes the parsed `Context` and a cancellation token, runs the server, and
  returns 0 after a clean stop or 1 after a start-up failure (an unavailable address, a database
  that cannot be prepared, or invalid sign-in settings) that was reported through `Context.WriteError`.
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
  ("Failed to prepare the database: ..." or "Invalid sign-in settings: ...") when the database cannot be prepared or
  the sign-in settings are invalid. Nothing is bound to a
  network port until the returned application is started.

**ServerHost.AddServices**: Registers the persistence and the Core services.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Registers the database settings and context factory, the clock, the stores, the password hasher, and the
  Core services in a service collection built from a configuration. It is shared by the server and by `UserCommand`.
- *Constraints*: None.

**UserCommand.Run**: Creates a user from the command line.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Takes the parsed `Context`, reads the password from the environment, standard input, or a prompt (never
  from the command line), creates the database if needed, and creates the user, or reports the reason of a failure
  through `Context.WriteError`.
- *Constraints*: The caller is `Program`.

**Health endpoint**: The `/health` route of the running server.

- *Type*: HTTP `GET`.
- *Role*: Provider.
- *Contract*: Answers HTTP status 200 with the plain text `Healthy` while the server runs.
- *Constraints*: Unauthenticated; carries no information beyond liveness.

**Context (consumed)**: The subsystem reads `Context.Urls` and `Context.Silent`, the user options of `Context`, and
calls `Context.WriteLine` and `Context.WriteError` to report results and failures.

- *Type*: In-process .NET members.
- *Role*: Consumer.

**App (consumed)**: The root component of the `Web` subsystem, mapped as the user interface of
the server.

- *Type*: In-process .NET type.
- *Role*: Consumer.

**Identity (consumed)**: `SignIn` (registration of cookie authentication and authorization, and the endpoints
`/auth/login` and `/auth/logout`) and `PasswordHasher` of the `Identity` subsystem.

- *Type*: In-process .NET types.
- *Role*: Consumer.

**Storage and Core (consumed)**: `DatabaseSetup`, `ReviewCenterDbContext`, `ReviewStore`, and `UserStore` of the
`Storage` subsystem, and `ProjectService`, `ReviewService`, `ThreadService`, `ChecklistService`, `UserService`,
`SigningService`, and the store and hasher interfaces of the `Core` subsystem.

- *Type*: In-process .NET types.
- *Role*: Consumer.

### Design

`ServerHost` builds and runs the server. `RunAsync` calls `CreateApplication` and runs the resulting
application until the cancellation token is cancelled or the process receives a termination
signal. `CreateApplication` performs the composition in a fixed order: it sets the content
root to the application directory so that static web assets are found regardless of the
working directory; applies the listen URL; configures logging with the console provider
included unless the context is silent; registers Blazor Server components; registers the persistence
services (below) and the sign-in; builds the
application; creates the database; validates the sign-in settings; enables authentication, authorization, and
request forgery protection, in that order; maps static web assets; maps the
`/health` route; maps the sign-in endpoints; maps the `App` root component with the interactive server render mode;
and registers a
callback that writes the structured start-up log entry once the server has started.

The persistence registration binds the `Database` configuration section (`appsettings.json` and
environment variables such as `Database__Provider` and `Database__ConnectionString`) to the options type
of the `Storage` subsystem, registers a `ReviewCenterDbContext` factory configured by `DatabaseSetup`,
and registers `TimeProvider.System`, `ReviewStore` and `UserStore` as the Core store interfaces, `PasswordHasher` as
the Core hasher interface, and the six Core services (`ProjectService`, `ReviewService`, `ThreadService`,
`ChecklistService`, `UserService`, and `SigningService`) as singletons shared by all browser connections. The
services hold no per-user state, and every
database operation opens its own short-lived context from the factory. The registration is the public method
`AddServices`, so that `UserCommand` builds exactly the same services.

The database is created right after the application is built, before the first request, so that a
problem with the settings or the file system stops the start-up with one clear message rather than
failing on the first page request. The sign-in settings (`Auth:SessionHours` and `Auth:RevalidationSeconds`) are
validated at the same point, for the
same reason: an invalid value stops the start-up with the message "Invalid sign-in settings: ...", instead of
failing at the first sign-in.

`UserCommand` does not start a server. It builds the configuration the same way (the same `appsettings.json` and
environment), registers the services with `AddServices`, creates the database if needed, and calls
`UserService.CreateAsync`; see its design.

The composition is shared by production and tests. `ServerHostOptions` lets a test switch off
static asset mapping, which needs a manifest that only the real application has, and attach an
in-memory server and a capturing logger provider. It also lets a test point the server at a temporary
database through the configuration. This keeps the tested pipeline identical to
the shipped one.

Error handling is local to the subsystem: an address that is unavailable at start-up, which
Kestrel reports as an `IOException`, is converted into a message on `Context.WriteError` and a
result of 1; a database that cannot be prepared (an unsupported provider, an unusable location) or sign-in settings
that are invalid raise
an `InvalidOperationException` while building, which `RunAsync` converts into the same kind of error
report and a result of 1; and a cancellation requested before start-up completes is treated as a normal
stop. Any other exception propagates to `Program.Main`.

Structured logging is built on `Microsoft.Extensions.Logging` message templates with named
placeholders, so the listen URL is available as a named property to any log sink that
understands structured entries. Framework components, such as request handling, write to the
same logging pipeline.
