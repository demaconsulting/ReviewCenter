### ServerHost

![Hosting Structure](HostingView.svg)

#### Purpose

`ServerHost` is the composition root of the web server. Its single responsibility is to build,
configure, and run the ASP.NET Core application behind the `serve` verb, so that service
registration, logging, and endpoint mapping live in one reviewable place.

#### Data Model

`ServerHost` is a stateless static class; every call builds an independent application, so it
is thread-safe.

**HealthPath**: `string` (constant) — `/health`; the route of the health endpoint.

**HealthyText**: `string` (constant) — `Healthy`; the body returned by the health endpoint.

**ServerHostOptions** (supporting type): An immutable options object with two optional
settings that let a caller adjust the build.

- **MapStaticAssets**: `bool` — `true` by default; when `false`, static web assets such as the
  Blazor client script are not mapped. Tests set it to `false` because an in-memory host has no
  asset manifest of its own.
- **ConfigureBuilder**: `Action<WebApplicationBuilder>?` — `null` by default; invoked after the
  standard services are registered and before the application is built. Tests use it to select
  the in-memory server, to attach a capturing logger provider, and to point the `Database` settings
  at a temporary database.

#### Key Methods

**CreateApplication**: Builds the web application for a context without starting it.

- *Parameters*: `Context context` — supplies the listen URL and the silent option;
  `ServerHostOptions? options` — optional build adjustments, `null` for production defaults.
- *Returns*: `WebApplication` — configured and not started; the caller owns and disposes it.
- *Preconditions*: `context` is not null.
- *Postconditions*: The application listens on `context.Urls` once started; console logging is
  registered unless `context.Silent` is true; `/health`, the login and logout endpoints, and the `App` user
  interface are mapped; the database exists, the Core services and the sign-in are registered; a structured start-up
  entry is written when the application has started.

Anchors the content root at the application directory; clears the default logging providers
and adds the console provider only when the context is not silent; registers Blazor Server
components; registers the persistence and the Core services with `AddServices`; registers cookie sign-in, and with it
the revalidating authentication state provider of open pages, with `SignIn.AddSignIn` (after the Razor components, so
that its provider replaces the framework default); applies the options callback; loads static web assets
when enabled; builds the application; calls `InitializeDatabase`, `ValidateAuthOptions`, `ValidateGitOptions`, and
`ValidateDataProtection`; adds `UseNotFoundPage` (the not-found middleware, below) and routing; adds authentication,
authorization, and request forgery protection, in that order, ahead of the endpoints they protect; maps static assets
when enabled; maps `/health` to a plain-text 200 response; maps the login and logout endpoints with
`SignIn.MapSignIn`; maps `App` with the interactive
server render mode; and registers the start-up log callback.

**UseNotFoundPage**: Answers a page request that no page handles with the framed "Page not found" page. A URL that
matches no endpoint never reaches the router, so without this the response is an empty 404. The middleware sits before
routing. When a GET or HEAD request outside the reserved prefixes (`/api`, `/_framework`, `/_blazor`, `/_content`) and
without a file extension ends in an empty 404, the request is re-executed against `/not-found` in a new service scope,
with the original status code kept and the original request published as `IStatusCodeReExecuteFeature`. Any other
status (a 400 or a 405, for example), any other method, a reserved prefix, and an address that ends in a file
extension keep the plain response. The built-in re-execution is not used because it answers every empty 4xx or 5xx
response.

**AddServices**: Registers the persistence and the Core services.

- *Parameters*: `IServiceCollection services`; `IConfiguration configuration` — holds the `Database` section.
- *Returns*: `void`.
- *Preconditions*: Neither argument is null.
- *Postconditions*: The `Database` options, a `ReviewCenterDbContext` factory configured by
  `DatabaseSetup.Configure`, `TimeProvider.System`, one `AuditAppender`, `ReviewStore`, `UserStore`, and `AuditStore` as
  the Core store interfaces, `PasswordHasher` as the Core hasher interface, and `ProjectService`, `ReviewService`,
  `ThreadService`, `ChecklistService`, `UserService`, `SigningService`, and `AuditService` as singletons are registered.
  `AuditService` gets a hook that logs a warning when an event cannot be recorded. The `DataProtection` and `Git`
  settings are bound (an empty `Git:CachePath` becomes a `git-cache` folder beside the database, through
  `DatabaseSetup.ResolveDataDirectory`), the Data Protection system is registered with the application name
  `DemaConsulting.ReviewCenter` and a file system key ring in the folder from `ResolveKeysPath`, and `RepositoryStore`,
  `CredentialProtector`, `RepositoryService`, `GitRunner`, and `GitSource` are registered as singletons, the first
  two and the last as the Core interfaces `IRepositoryStore`, `ICredentialProtector`, and `IGitRepositoryProvider`.
  `ReviewMarkConfigParser` is registered as the Core interface `IReviewSetParser`, and `ReviewPlanner` as a singleton.
  The `Attestation` settings are bound, with an empty `SelfSignedFolder` becoming an `attestation-selfsigned` folder
  inside the folder from `ResolveKeysPath`, the key provider is registered through
  `AttestationKeyProviderFactory.Create`, and `AttestationRenderer`, `AttestationSigner`, and `AttestationService` are
  registered as singletons (the first two as the interfaces `IAttestationRenderer` and `IAttestationSigner`;
  `AttestationService` as the concrete class).

The method is shared with `UserCommand` and `AuditCommand`, so that the command line works with exactly the services of
the server. `UserService` must remain a single instance, because it serializes the changes to users in the process;
`SigningService` must remain a single instance for the same reason, because it serializes the changes to reviewer
slots and signatures with one gate. `RepositoryService` must remain a single instance for the same reason, because it
serializes the changes to repositories. `AuditAppender` must remain a single instance, because its gate is what
serializes
every audited change of all stores in the process (the database write lock serializes other processes).

**InitializeDatabase**: Creates the database for a built application.

- *Parameters*: `WebApplication app` — supplies the options and the context factory.
- *Returns*: `void`.
- *Postconditions*: The database folder, file, and tables exist, or the application is disposed and
  an `InvalidOperationException` beginning "Failed to prepare the database:" is thrown with the original
  failure as its inner exception.

Calls `DatabaseSetup.EnsureCreated`. It converts an unsupported provider, a database error, a file system
error, an access error, an invalid connection string, and a missing native SQLite library into that one exception
type. For the missing library the message reports the root cause (the library that could not be loaded), because
the type-initializer message that SQLite raises is not useful. That case was checked by hand against a published
build with the native library removed (exit code 1 and the message above); no automated test removes the library.

**ValidateAuthOptions**: Reads the sign-in options of a built application once.

- *Parameters*: `WebApplication app` — supplies the options.
- *Returns*: `void`.
- *Postconditions*: The `Auth` settings are valid, or the application is disposed and an
  `InvalidOperationException` beginning "Invalid sign-in settings:" is thrown with the validation failure as its
  inner exception.

The options are validated when they are first read, so reading them at start-up turns an invalid value (such as a
session length that is not positive, or an interval for the re-check of open pages below 5 seconds) into one clear
start-up message, which names the setting, rather than a failure at the first sign-in.

**ValidateGitOptions**: Reads the git options of a built application once.

- *Parameters*: `WebApplication app` — supplies the options.
- *Returns*: `void`.
- *Postconditions*: The `Git` settings are valid, or the application is disposed and an
  `InvalidOperationException` beginning "Invalid git settings:" is thrown with the validation failure as its inner
  exception.

`Git:TimeoutSeconds` must be from 1 to 3600 (`GitOptions.MinimumTimeoutSeconds` and `MaximumTimeoutSeconds`); the
message "Git:TimeoutSeconds must be between 1 and 3600 seconds." names the setting. A value that is not a number
fails earlier, while the configuration is bound, with a message that names the key as well.

**ResolveKeysPath**: Chooses the folder of the Data Protection key ring.

- *Parameters*: `DataProtectionSettings settings` — holds `KeysPath`; `DatabaseOptions database` — locates the
  database.
- *Returns*: `string` — the configured `KeysPath` made absolute, or the `keys` folder in the folder of the database
  file.
- *Notes*: Internal, so that tests check the choice without starting the server.

**ValidateDataProtection**: Checks the key ring folder of a built application once.

- *Parameters*: `WebApplication app` — supplies the settings and the Data Protection provider.
- *Returns*: `void`.
- *Postconditions*: The folder exists (on a system other than Windows with the Unix mode that only the owner can use),
  a probe file can be written and deleted in it, and a test value can be protected and unprotected; otherwise the
  application is disposed and an `InvalidOperationException` is thrown whose message names the folder and says "Set
  DataProtection:KeysPath to a folder the server can write to and keep."

Runs after `ValidateAuthOptions` in `CreateApplication`, so it applies to the `serve` verb only. The folder, write, and
round trip failures (I/O, access, cryptographic, domain rule, argument, and not-supported errors) all become that one
message, so an administrator gets a clear start-up error instead of failing at the first token. The keys in the folder
are not encrypted at rest.

**ValidateAttestation**: Obtains the attestation signing key of a built application once.

- *Parameters*: `WebApplication app` — supplies the key provider.
- *Returns*: `void`.
- *Postconditions*: The signing key can be obtained and meets the `RequireTrustedKey` rule; otherwise the application is
  disposed and an `InvalidOperationException` "The attestation signing key is not usable: {reason}" is thrown, whose
  reason names the `Attestation` setting at fault. With the default self-signed provider the key file is created here on
  the first start.

Runs after `ValidateDataProtection` in `CreateApplication`, so it applies to the `serve` verb only. It does not sign a
test value, so a smart card is not asked for its PIN at the start. The consequence of failing fast is that a server
whose key is in a hardware module that is not present does not start.

**RunAsync**: Builds and runs the web server until it is stopped.

- *Parameters*: `Context context` — parsed context; `CancellationToken cancellationToken` —
  requests a graceful shutdown; `ServerHostOptions? options` — optional build adjustments, passed on to
  `CreateApplication` (tests use it to select a temporary database).
- *Returns*: `Task<int>` — 0 for a clean stop, 1 when the server could not start.
- *Preconditions*: `context` is not null.
- *Postconditions*: The application is stopped and disposed; a start-up failure has been
  reported through `context.WriteError`.

**LogStarted**: Writes the structured start-up entry.

- *Parameters*: `ILogger logger`, `string urls` — the listen URL.
- *Returns*: `void`.

Writes an information-level entry from a message template with the named placeholder `Urls`,
rather than from an interpolated string, so the URL stays available as a structured property.
This is the mechanism behind the requirement that the listen URL be exposed as a named value.

#### Error Handling

`CreateApplication` and `RunAsync` throw `ArgumentNullException` for a null context. `RunAsync`
handles three environmental conditions locally: an `OperationCanceledException` raised while the
caller has requested cancellation is treated as a normal stop and returns 0; an
`InvalidOperationException` from building, which is how a database that cannot be prepared and invalid sign-in or git
settings are reported, is
written through `Context.WriteError` as is ("Failed to prepare the database: {detail}", "Invalid sign-in
settings: {detail}", "Invalid git settings: {detail}", "The Data Protection key ring folder '{path}' is not
writable: ...", or "The attestation signing key is not usable: {detail}") and returns 1; and an
`IOException`, which Kestrel uses to report an address that is in use or not bindable, is
reported as "Failed to start the server on {url}: {detail}" through `Context.WriteError` and
returns 1. Any other exception propagates to the caller (`Program.Main`). The application is
always disposed.

#### Dependencies

- **Context** — supplies `Urls` and `Silent`, and receives start-up error reports.
- **App** — the root component mapped as the user interface.
- **DatabaseSetup**, **ReviewCenterDbContext**, **ReviewStore**, **UserStore**, **AuditStore**, **AuditAppender**,
  **RepositoryStore** — the database configuration, context, and stores registered for the pages.
- **ProjectService**, **ReviewService**, **ThreadService**, **ChecklistService**, **UserService**, **SigningService**,
  **AuditService**, **RepositoryService** — the Core services registered for the pages.
- **GitOptions**, **GitRunner**, **GitSource**, **CredentialProtector** — the git content source and the token protector.
- **AttestationOptions**, **AttestationKeyProviderFactory**, **AttestationKeyException**, **AttestationRenderer**,
  **AttestationSigner**, **AttestationService**, **AttestationEndpoint** — the signed attestation of the `Evidence`
  subsystem, which is registered, validated at the start, and mapped.
- **ASP.NET Core Data Protection** — the key ring and the protection of tokens.
- **PasswordHasher**, **SignIn** — the password hasher registered for the Core, and the cookie sign-in, authorization,
  and login and logout endpoints.
- **ASP.NET Core shared framework** — `WebApplication`, Kestrel, endpoint routing, authentication, authorization,
  request forgery protection, Blazor Server components, and `Microsoft.Extensions.Logging`.
- **Entity Framework Core** — the database context factory registration.

#### Callers

- **Program** — calls `RunAsync` when the `serve` verb is present.
- **UserCommand** and **AuditCommand** — call `AddServices` to register the same services.
- **Tests** — call `CreateApplication` with `ServerHostOptions` to run the production pipeline
  against an in-memory server.
