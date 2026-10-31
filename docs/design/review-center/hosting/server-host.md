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
- **IsolatedSettings**: `IReadOnlyDictionary<string, string?>?` — `null` by default; when set, the host is built
  from an empty builder that reads these settings and nothing else (see **CreateBuilder** below). Only the
  self-validation sets it.

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
that its provider replaces the framework default); applies the options callback; registers sign-in with an OpenID
Connect provider with `OidcSignIn.AddOidcSignIn` after the callback, because only then is the configuration final (it
registers nothing unless `Identity:Oidc:Enabled` is true); loads static web assets
when enabled; builds the application; calls `InitializeDatabase`, `ValidateAuthOptions`, `ValidateOidcOptions`,
`ValidateTotpOptions`,
`ValidateGitOptions`, and
`ValidateDataProtection`; adds `UseNotFoundPage` (the not-found middleware, below) and routing; adds authentication,
authorization, and request forgery protection, in that order, ahead of the endpoints they protect; maps static assets
when enabled; maps `/health` to a plain-text 200 response; maps the login and logout endpoints with
`SignIn.MapSignIn`; maps the endpoints of sign-in with a provider with `OidcSignIn.MapOidcSignIn` (only when it is
enabled); maps the second step of a sign-in with `TotpSignIn.MapTotpSignIn` (only when `Identity:Totp:Enabled` is
true); maps the signed attestation download; maps the JSON API with `MapApi` and its OpenAPI document with
`MapApiDocument` ahead of the user interface, so that an unknown address under `/api` is answered by the API's own JSON
404; maps `App` with the interactive
server render mode; and registers the start-up log callback. After the sign-in, it registers the token authentication
scheme and the `Api` policy with `ApiTokenAuthentication.AddApiTokenAuthentication` and the OpenAPI generator with
`ApiDocument.AddApiDocument`; the cookie stays the default scheme.

**CreateBuilder** (isolated mode): When `ServerHostOptions.IsolatedSettings` is set, the builder is created with
`WebApplication.CreateEmptyBuilder`, which registers no configuration source, no logging, no Kestrel and no
routing. The content root is the application directory and the environment name is fixed to `Production`. The
only configuration is the in-memory dictionary of the options (the listen address of the context is applied by the
shared registration, as in the normal mode); then `UseKestrelCore`, `AddRouting` (the full registration, because
the API routes use constraints)
and `AddLogging` are added explicitly. No `appsettings.json`, no `DOTNET_`/`ASPNETCORE_` or other environment
variable, no user secrets and no host default is read, so the host cannot be broken or redirected by the machine
that it runs on. In the normal mode (`IsolatedSettings` is `null`) `WebApplication.CreateBuilder` is used as
before. Everything after the builder is the same in both modes.

**UseNotFoundPage**: Answers a page request that no page handles with the framed "Page not found" page. A URL that
matches no endpoint never reaches the router, so without this the response is an empty 404. The middleware sits before
routing. When a GET or HEAD request outside the reserved prefixes (`/api`, `/_framework`, `/_blazor`, `/_content`,
`/signin-oidc`) and
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
  `DatabaseSetup.Configure`, `TimeProvider.System`, one `AuditAppender`, `ReviewStore`, `UserStore`,
  `PersonalAccessTokenStore`, `TotpStore`, `InboxStore`, and `AuditStore` as
  the Core store interfaces, `PasswordHasher` as the Core hasher interface, `TotpAlgorithm` and `TotpSecretProtector`
  as the Core second factor interfaces, `ProjectAccess` as the Core interface
  `IProjectAccess` (one singleton that every service and the API share, registered before the services that take it),
  and `ProjectService`, `ReviewService`,
  `ThreadService`, `ChecklistService`, `UserService`, `PersonalAccessTokenService`, `SigningService`, `ReauthProofStore`
  (the single-use proofs of users of an identity provider, shared by the sign-in and the signing service),
  `TotpChallengeStore` and `TotpService` (the second factor), `InboxService` (the inbox and the read state),
  `UserImpactService` (the offboarding impact of a user), and
  `AuditService`
  as singletons are registered. `TotpSignIn.AddTotpSignIn` binds the `Identity:Totp` settings.
  `AuditService` gets a hook that logs a warning when an event cannot be recorded, and the registered `TimeProvider`
  (its coalescing of repeated events uses the clock, so tests can move time). The `DataProtection` and `Git`
  settings are bound (an empty `Git:CachePath` becomes a `git-cache` folder beside the database, through
  `DatabaseSetup.ResolveDataDirectory`), the Data Protection system is registered with the application name
  `DemaConsulting.ReviewCenter` and a file system key ring in the folder from `ResolveKeysPath`, with the protection of
  the key files that `KeyRingProtection.Prepare` and `KeyRingProtection.Apply` chose from the `DataProtection` settings
  (the plan is made here, once, so the certificate is loaded when the services are registered; a problem in the
  settings is kept in the plan and reported by `ValidateDataProtection`, so that registering the services never
  throws), and `RepositoryStore`,
  `CredentialProtector`, `RepositoryService`, `GitRunner`, and `GitSource` are registered as singletons, the first
  two and the last as the Core interfaces `IRepositoryStore`, `ICredentialProtector`, and `IGitRepositoryProvider`.
  `ReviewMarkConfigParser` is registered as the Core interface `IReviewSetParser`, and `ReviewPlanner` as a singleton.
  The `Attestation` settings are bound, with an empty `SelfSignedFolder` becoming an `attestation-selfsigned` folder
  inside the folder from `ResolveKeysPath`, the key provider is registered through
  `AttestationKeyProviderFactory.Create` (with the `IDataProtectionProvider` of the container, which protects the
  self-signed key file, `allowCreate` set to `true`, and `requireEncryptedKeys` set to the value of
  `DataProtection:RequireEncryptedKeys`, so that the File provider refuses a key without a password; the provider is
  created on first use, which is the start-up
  validation of `serve`, so `UserCommand` and `AuditCommand` never create the key), and `AttestationRenderer`,
  `AttestationSigner`, and `AttestationService` are
  registered as singletons (the first two as the interfaces `IAttestationRenderer` and `IAttestationSigner`;
  `AttestationService` as the concrete class).

The method is shared with `UserCommand` and `AuditCommand`, so that the command line works with exactly the services of
the server. `UserService` must remain a single instance, because it serializes the changes to users in the process;
`PersonalAccessTokenService` must remain a single instance for the same reason, because it serializes the creation and
revocation of tokens with one gate, which is what keeps the limit of active tokens exact;
`SigningService` must remain a single instance for the same reason, because it serializes the changes to reviewer
slots and signatures with one gate. `TotpChallengeStore` must remain a single instance, because it holds the open
challenges of the process in memory. `RepositoryService` must remain a single instance for the same reason, because it
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
- *Postconditions*: The `Auth` and `Identity:Throttle` settings are valid, or the application is disposed and an
  `InvalidOperationException` beginning "Invalid sign-in settings:" is thrown with the validation failure as its
  inner exception.

The options are validated when they are first read, so reading them at start-up turns an invalid value (such as a
session length that is not positive, an interval for the re-check of open pages below 5 seconds, a sign-in throttle
limit that is not positive or a window outside 1 to 86400 seconds) into one clear
start-up message, which names the setting, rather than a failure at the first sign-in.

**ValidateOidcOptions**: Reads the settings of sign-in with an OpenID Connect provider of a built application once.

- *Parameters*: `WebApplication app` — supplies the options.
- *Returns*: `void`.
- *Postconditions*: The `Identity:Oidc` settings are valid or sign-in with a provider is off, or the application is
  disposed and an `InvalidOperationException` beginning "Invalid OpenID Connect sign-in settings:" is thrown with the
  validation failure as its inner exception.

The validator (see `OidcSignIn`) does nothing while `Enabled` is false, so a server that does not use the provider is
not changed. Otherwise a wrong value is reported with the setting that is wrong (for example `Identity:Oidc:Authority`)
and the rule, never the value and never the client secret, so the message can be logged. Reading the options at
start-up turns a wrong setting into one clear message instead of a failure at the first sign-in.

**ValidateTotpOptions**: Reads the settings of the second factor of a built application once.

- *Parameters*: `WebApplication app` — supplies the options.
- *Returns*: `void`.
- *Postconditions*: The `Identity:Totp` settings are valid, or the application is disposed and an
  `InvalidOperationException` beginning "Invalid two-factor sign-in settings:" is thrown with the validation failure
  as its inner exception.

The validator (see `TotpSignIn`) rejects an issuer name that is longer than 64 characters or contains a colon or a
control character, because the name is part of the `otpauth://` address. The message names the setting and never a
secret.

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

- *Parameters*: `KeyRingSettings settings` — holds `KeysPath` (see `KeyRingProtection`); `DatabaseOptions database` —
  locates the database.
- *Returns*: `string` — the configured `KeysPath` made absolute, or the `keys` folder in the folder of the database
  file.
- *Notes*: Internal, so that tests check the choice without starting the server.

**ValidateDataProtection**: Checks the key ring folder of a built application once.

- *Parameters*: `WebApplication app` — supplies the settings and the Data Protection provider.
- *Returns*: `void`.
- *Postconditions*: The key ring plan has no problem; the folder exists (on a system other than Windows with the Unix
  mode that only the owner can use; on Windows with `PermissionsOnly`, with access rules that admit only the account
  of the server and the system), a probe file can be written and deleted in it, the ring passes
  `KeyRingProtection.CheckRing` (before any key can be created), the lost-ring guard passes (below), a test value can be
  protected and unprotected, and the
  key files are restricted to the owner on Unix (those that exist now; later ones as Data Protection creates them);
  otherwise the application is disposed and an
  `InvalidOperationException` is thrown. A plan with a problem or a failed check throws a message that names the
  setting at fault ("Invalid key ring settings: ..." and the messages of `KeyRingProtection`); a folder that cannot be
  used throws a message that names the folder and says "Set DataProtection:KeysPath to a folder the server can write to
  and keep." On success the protection in force is logged (a warning for `PermissionsOnly`).

Runs after `ValidateAuthOptions` in `CreateApplication`, so it applies to the `serve` verb only. The folder, write, and
round trip failures (I/O, access, cryptographic, domain rule, argument, and not-supported errors) all become that one
message, so an administrator gets a clear start-up error instead of failing at the first token. The ring is checked
before the probe round trip because the round trip creates a key when there is none that can be read.

**GuardLostRing**: The lost-ring guard, called by `ValidateDataProtection` right after `CheckRing` and before the probe.

- *Parameters*: the application services, the settings, and the key folder.
- *Postconditions*: `SecretScan` reads the key files and unprotects the stored repository tokens and second-factor
  secrets with the production protectors (a read-only scan; nothing is written and no key is created). With no key file
  and at least one protected secret, an `InvalidOperationException` "Key ring '{folder}' is empty but the database holds
  {N} protected secrets. Restore the keys folder that belongs to this database, or set DataProtection:AllowNewKeyRing=true
  for one start to accept that they become unreadable." is thrown, unless `DataProtection:AllowNewKeyRing` is true, in
  which case a warning with the count is logged and the start goes on. With keys in the ring but some secrets that cannot
  be decrypted, an error is logged with the counts per kind, the ids of the repositories and users, and the remedy
  (enter the repository tokens again; an administrator resets the two-factor secret), and the start goes on. When the
  scan cannot complete because the database cannot be read (a `SqliteException` after the database was prepared, for
  example a lock or a table that cannot be read; a file that is not a database fails earlier, in `InitializeDatabase`, with
  "Failed to prepare the database", which creates no key either)
  and the folder holds no key file, an `InvalidOperationException` "Key ring '{folder}' is empty and the database
  (Database:ConnectionString) could not be read to check whether it holds protected secrets, so no key was created. Fix
  the access to the database and start again; run 'reviewcenter --validate --check-config' for the details." is thrown,
  whatever `DataProtection:AllowNewKeyRing` says: that setting accepts the loss of secrets that are known to exist, and
  here it is not known. With keys in the ring the same failure is logged as a warning and the start goes on, as the
  start-up that opens the database reports the problem.
- *Notes*: A database with no secrets (a fresh installation) never triggers the guard. `DataProtection:AllowNewKeyRing`
  covers only the stored secrets: it does not affect the protected attestation key file (see `ValidateAttestation`).
  The message holds the folder
  and a count only (the unreadable-database message holds the folder and the setting name only, no exception text, no
  connection string, and no database location), never a secret value, ciphertext, or key. It runs for the `serve`
  verb only, because it is part of
  `ValidateDataProtection`; `user add` and `audit verify` neither scan nor create a key.

**ValidateAttestation**: Obtains the attestation signing key of a built application once.

- *Parameters*: `WebApplication app` — supplies the key provider.
- *Returns*: `void`.
- *Postconditions*: The signing key can be obtained and meets the `RequireTrustedKey` rule; otherwise the application is
  disposed and an `InvalidOperationException` "The attestation signing key is not usable: {reason}" is thrown, whose
  reason names the `Attestation` setting or the file at fault (for the File provider: the missing path, file or
  password, the wrong password, a key without a password under `DataProtection:RequireEncryptedKeys`), or, for a
  protected self-signed key file that the key ring cannot
  decrypt, the file and the remedy (restore the ring or delete the file). With the default self-signed provider the
  protected key file is created here on the first start. An expiry warning of the key is logged here as well.
- *Notes*: The server never deletes or replaces a signing key, so `DataProtection:AllowNewKeyRing` does not help here: it
  only accepts the loss of stored secrets (`GuardLostRing`). A start with a lost ring and a surviving protected key
  file therefore fails here, with or without that setting. In the edge case of an empty ring, no database secrets,
  and a surviving protected attestation file, the lost-ring probe (`ValidateDataProtection`) has already created a new
  Data Protection key before this check refuses; the attestation file is left untouched, and the operator deletes it
  (the next start then creates a new signing key) or restores the ring that protected it. `GuardLostRing` does not
  look at the attestation file.

Runs after `ValidateDataProtection` in `CreateApplication`, so it applies to the `serve` verb only. It does not sign a
test value, so a smart card is not asked for its PIN at the start. The consequence of failing fast is that a server
whose key is in a hardware module that is not present does not start.

**RunAsync**: Builds and runs the web server until it is stopped.

- *Parameters*: `Context context` — parsed context; `ServerHostOptions? options` — optional build
  adjustments, passed on to `CreateApplication` (tests use it to select a temporary database);
  `CancellationToken cancellationToken` — requests a graceful shutdown. An overload without `options`
  takes the context and the cancellation token and uses the production defaults.
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
`InvalidOperationException` from building, which is how a database that cannot be prepared and invalid sign-in, OpenID
Connect sign-in, two-factor sign-in, or git
settings are reported, is
written through `Context.WriteError` as is ("Failed to prepare the database: {detail}", "Invalid sign-in
settings: {detail}", "Invalid OpenID Connect sign-in settings: {detail}", "Invalid two-factor sign-in
settings: {detail}", "Invalid git settings: {detail}", "Invalid key ring settings: {detail}", "The Data
Protection key ring folder '{path}' is not
writable: ...", or "The attestation signing key is not usable: {detail}") and returns 1; and an
`IOException`, which Kestrel uses to report an address that is in use or not bindable, is
reported as "Failed to start the server on {url}: {detail}" through `Context.WriteError` and
returns 1. Any other exception propagates to the caller (`Program.Main`). The application is
always disposed.

#### Dependencies

- **Context** — supplies `Urls` and `Silent`, and receives start-up error reports.
- **App** — the root component mapped as the user interface.
- **DatabaseSetup**, **ReviewCenterDbContext**, **ReviewStore**, **UserStore**, **PersonalAccessTokenStore**,
  **TotpStore**, **InboxStore**,
  **AuditStore**, **AuditAppender**,
  **RepositoryStore** — the database configuration, context, and stores registered for the pages.
- **ProjectService**, **ReviewService**, **ThreadService**, **ChecklistService**, **UserService**,
  **PersonalAccessTokenService**, **SigningService**, **TotpService**, **TotpChallengeStore**, **InboxService**,
  **UserImpactService**,
  **AuditService**, **RepositoryService** — the Core services registered for the pages and the API.
- **ProjectAccess**, **IProjectAccess** — Core; the one place that decides which project a user may see, registered as a
  singleton for the services, the pages, and the API.
- **ApiTokenAuthentication**, **ApiEndpoints**, **ApiDocument** — the `Api` subsystem, which is registered and mapped.
- **GitOptions**, **GitRunner**, **GitSource**, **CredentialProtector** — the git content source and the token protector.
- **AttestationOptions**, **AttestationKeyProviderFactory**, **AttestationKeyException**, **AttestationRenderer**,
  **AttestationSigner**, **AttestationService**, **AttestationEndpoint** — the signed attestation of the `Evidence`
  subsystem, which is registered, validated at the start, and mapped.
- **ASP.NET Core Data Protection** — the key ring and the protection of tokens.
- **KeyRingProtection**, **KeyRingSettings**, **KeyRingPlan** — the `DataProtection` settings, the protection of the key
  files, and the check of the ring.
- **SecretScan** — the read-only count of the key files and the protected secrets, used by the lost-ring guard.
- **PasswordHasher**, **SignIn** — the password hasher registered for the Core, and the cookie sign-in, authorization,
  and login and logout endpoints.
- **OidcSignIn**, **OidcOptions**, **ReauthProofStore** — sign-in with an OpenID Connect provider (registered, validated
  at the start, and mapped only when enabled) and the proofs of confirmed identity.
- **TotpSignIn**, **TotpOptions**, **TotpAlgorithm**, **TotpSecretProtector** — the second step of a sign-in
  (registered, validated at the start, and mapped only when enabled) and the Identity implementations of the second
  factor interfaces.
- **ASP.NET Core shared framework** — `WebApplication`, Kestrel, endpoint routing, authentication, authorization,
  request forgery protection, Blazor Server components, and `Microsoft.Extensions.Logging`.
- **Entity Framework Core** — the database context factory registration.

#### Callers

- **Program** — calls `RunAsync` when the `serve` verb is present.
- **UserCommand** and **AuditCommand** — call `AddServices` to register the same services.
- **Tests** — call `CreateApplication` with `ServerHostOptions` to run the production pipeline
  against an in-memory server.
