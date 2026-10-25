## Hosting

![Hosting Structure](HostingView.svg)

### Overview

The `Hosting` subsystem turns a parsed command-line request into a running web server, creates users from the
command line, and verifies the audit trail from the command line. It owns
the composition of the ASP.NET Core application behind the `serve` verb: the listen address,
the logging configuration, the health endpoint, the sign-in configuration and endpoints of the `Identity`
subsystem, the mapping of the user interface provided
by the `Web` subsystem, and the connection of the pages to the database: reading the `Database` settings,
creating the database, and registering the `Core` services with the `Storage` stores. It also owns the
start, run, and stop lifecycle of that application and
the translation of start-up failures into ordinary error reports. The `user add` and `audit verify` verbs reuse the same
registration of the database and the services to work without starting a server. The subsystem contains three
units: `ServerHost`, together with its small options type `ServerHostOptions`, which is
documented with `ServerHost` as a supporting type, `UserCommand`, and `AuditCommand`.

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
- *Contract*: Registers the database settings and context factory, the clock, the stores, the password hasher, the Core
  services, and the git content source (its settings, the key ring of the Data Protection system, the token protector,
  and the repository registry) in a service collection built from a configuration. It is shared by the server,
  `UserCommand`, and `AuditCommand`.
- *Constraints*: None.

**UserCommand.Run**: Creates a user from the command line.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Takes the parsed `Context`, reads the password from the environment, standard input, or a prompt (never
  from the command line), creates the database if needed, and creates the user, or reports the reason of a failure
  through `Context.WriteError`.
- *Constraints*: The caller is `Program`.

**AuditCommand.Run**: Verifies the audit trail from the command line.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Takes the parsed `Context`, creates the database if needed, verifies the hash chain of the audit trail,
  and reports an intact chain with its entry count (exit code 0), or the first broken entry, or the failure to read the
  database, through `Context.WriteError` (exit code 1).
- *Constraints*: The caller is `Program`. It never changes the trail. Because it creates a database that does not exist,
  running it against a wrong location creates an empty database there.

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

**Storage and Core (consumed)**: `DatabaseSetup`, `ReviewCenterDbContext`, `ReviewStore`, `UserStore`, `AuditStore`,
`RepositoryStore`, and `AuditAppender` of the
`Storage` subsystem, and `ProjectService`, `ReviewService`, `ThreadService`, `ChecklistService`, `UserService`,
`SigningService`, `AuditService`, `RepositoryService`, and the store and hasher interfaces of the `Core` subsystem.

- *Type*: In-process .NET types.
- *Role*: Consumer.

**ContentSources (consumed)**: `GitOptions`, `GitRunner`, `GitSource`, and `CredentialProtector` of the
`ContentSources` subsystem.

- *Type*: In-process .NET types.
- *Role*: Consumer.

**Data protection settings**: The `DataProtection` configuration section.

- *Type*: Configuration (`appsettings.json` and environment variables).
- *Role*: Consumer.
- *Contract*: `KeysPath` is the folder of the key ring that protects the stored repository tokens (empty for a `keys`
  folder beside the database). The `Git` section sets `CachePath` (empty for a `git-cache` folder beside the database)
  and `TimeoutSeconds` (120 by default) of the git content source.
- *Constraints*: The server refuses to start when the key ring folder cannot be written.

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
and registers `TimeProvider.System`, `AuditAppender`, `ReviewStore`, `UserStore`, and `AuditStore` as the Core store
interfaces (the appender is a single instance shared by all three stores, which is what makes the audit gate
process-wide), `PasswordHasher` as
the Core hasher interface, and the seven Core services (`ProjectService`, `ReviewService`, `ThreadService`,
`ChecklistService`, `UserService`, `SigningService`, and `AuditService`) as singletons shared by all browser
connections. `AuditService` is created with a hook that writes a warning to the log when an event cannot be recorded
(the failure is never shown to the user), and `UserService` receives it so that failed sign-ins and lockouts are
recorded. `RepositoryStore` (as the Core repository store interface), `CredentialProtector` (as the Core token
protector), `RepositoryService`, `GitRunner`, and `GitSource` (as the Core git provider interface) are registered as
singletons in the same way, together with the `Git` and `DataProtection` settings. The
services hold no per-user state, and every
database operation opens its own short-lived context from the factory. The registration is the public method
`AddServices`, so that `UserCommand` and `AuditCommand` build exactly the same services.

The database is created right after the application is built, before the first request, so that a
problem with the settings or the file system stops the start-up with one clear message rather than
failing on the first page request. The sign-in settings (`Auth:SessionHours` and `Auth:RevalidationSeconds`) are
validated at the same point, for the
same reason: an invalid value stops the start-up with the message "Invalid sign-in settings: ...", instead of
failing at the first sign-in.

The key ring of the Data Protection system is persisted in a folder, because the stored repository tokens are
encrypted with it: with the default in-memory or profile-bound key ring, a restart or a move to another account could
leave every token unreadable. The folder is `DataProtection:KeysPath`, or a `keys` folder beside the database. Like the
database, the folder is checked once at start-up (`ValidateDataProtection`), by writing and deleting a probe file and
by encrypting and decrypting a test value, so that a folder that cannot be written stops the start-up with one clear message
naming the setting. The check is part of `CreateApplication`, so it applies to the `serve` verb only; the command-line
verbs register the same services but do not need the key ring unless a token is read. Three consequences are
documented for the administrator (see the `ContentSources` design and the user guide): losing or replacing the key
ring makes the stored tokens unreadable and they must be entered again; the keys are not encrypted at rest, so the
folder must be protected with file permissions and backed up with the database; and persisting the key ring changes
the keys of the sign-in cookies once, so users sign in again after the first start. The git cache folder is resolved
the same way (`Git:CachePath`, or `git-cache` beside the database) and is created by the git source when first used.

`UserCommand` does not start a server. It builds the configuration the same way (the same `appsettings.json` and
environment), registers the services with `AddServices`, creates the database if needed, and calls
`UserService.CreateAsync`; see its design. `AuditCommand` does the same and calls `AuditService.VerifyAsync`; see its
design.

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
