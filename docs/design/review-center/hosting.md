## Hosting

![Hosting Structure](HostingView.svg)

### Overview

The `Hosting` subsystem turns a parsed command-line request into a running web server, creates users from the
command line, and verifies the audit trail from the command line. It owns
the composition of the ASP.NET Core application behind the `serve` verb: the listen address,
the logging configuration, the health endpoint, the sign-in configuration and endpoints of the `Identity`
subsystem (including the optional sign-in with an OpenID Connect provider, off unless `Identity:Oidc:Enabled` is
true), the optional second factor for password accounts, the mapping of the user interface provided
by the `Web` subsystem, the registration and mapping of the JSON API of the `Api` subsystem, and the connection of
the pages to the database: reading the `Database` settings,
creating the database, and registering the `Core` services with the `Storage` stores. It also owns the
start, run, and stop lifecycle of that application and
the translation of start-up failures into ordinary error reports. The `user add`, `audit verify`, and `keys reprotect`
verbs reuse the same
registration of the database and the services to work without starting a server. The subsystem contains six
units: `ServerHost`, together with its small options type `ServerHostOptions`, which is
documented with `ServerHost` as a supporting type, `KeyRingProtection`, `SecretScan`, `UserCommand`,
`AuditCommand`, and `KeysCommand`.

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
  ("Failed to prepare the database: ..." or "Invalid sign-in settings: ..." or "Invalid OpenID Connect sign-in
  settings: ..." or "Invalid key ring settings: ...") when the database cannot be prepared or
  the sign-in, the OpenID Connect, or the key ring settings are invalid, or the key ring cannot be used. Nothing is
  bound to a network port until the returned application is started.

**ServerHost.AddServices**: Registers the persistence and the Core services.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Registers the database settings and context factory, the clock, the stores, the password hasher, the
  project access rules (`ProjectAccess` as the Core `IProjectAccess`), the store of single-use confirmation proofs
  (`ReauthProofStore`), the Core
  services, and the git content source (its settings, the key ring of the Data Protection system, the token protector,
  and the repository store) in a service collection built from a configuration. It is shared by the server,
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
- *Contract*: Takes the parsed `Context`, checks that the configured database file exists, verifies the hash chain of
  the audit trail, and reports an intact chain with its entry count (exit code 0), or the first broken entry, a database
  file that does not exist, or the failure to read the database, through `Context.WriteError` (exit code 1).
- *Constraints*: The caller is `Program`. It never changes the trail and never creates a database, so running it
  against a wrong location only reports that the file does not exist.

**KeysCommand.Run**: Rotates the Data Protection key and protects the stored secrets again.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Takes the parsed `Context`, runs the secret scan, and refuses with exit code 1 and without any change when
  a protected secret cannot be read, when the key ring cannot be used, or when the database file does not exist.
  Otherwise it creates a new Data Protection key with the configured protection, protects every repository token and
  second-factor secret again with it (one transaction per table), and reports the new key id, the counts, the keys
  that are still referenced or no longer referenced, and the next steps (exit code 0).
- *Constraints*: The caller is `Program`. It is the only verb that writes the key ring and the stored repository token
  and second-factor secret columns, and the server must be
  stopped while it runs. It never prints a secret.

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
`/auth/login` and `/auth/logout`), `OidcSignIn` and `OidcOptions` (the optional OpenID Connect sign-in, its endpoints,
and the settings), `TotpSignIn` and `TotpOptions` (the second step of a sign-in, its endpoint, and the settings), and
`PasswordHasher` of the `Identity` subsystem.

- *Type*: In-process .NET types.
- *Role*: Consumer.

**Identity:Totp settings**: The `Identity:Totp` configuration section, described in the `Identity` design.

- *Type*: Configuration (`appsettings.json` and environment variables such as `Identity__Totp__Enabled`).
- *Role*: Consumer.
- *Contract*: On by default (`Enabled`, and an `Issuer` name for the authenticator apps, `ReviewCenter` by default).
  The server validates the settings at start-up and maps the login-code endpoint only while it is enabled.
- *Constraints*: An invalid setting stops the start-up with an error that names it.

**Identity:Oidc settings**: The `Identity:Oidc` configuration section, described in the `Identity` design.

- *Type*: Configuration (`appsettings.json` and environment variables such as `Identity__Oidc__Enabled`).
- *Role*: Consumer.
- *Contract*: Off by default. When `Enabled` is true the server validates the settings at start-up, registers the
  provider and its endpoints, and reserves `/signin-oidc` for the callback.
- *Constraints*: An invalid setting stops the start-up with an error that names it and never shows a value or the
  secret.

**Storage and Core (consumed)**: `DatabaseSetup`, `ReviewCenterDbContext`, `ReviewStore`, `UserStore`, `AuditStore`,
`RepositoryStore`, `PersonalAccessTokenStore`, `TotpStore`, and `AuditAppender` of the
`Storage` subsystem, and `ProjectService`, `ReviewService`, `ThreadService`, `ChecklistService`, `UserService`,
`SigningService`, `AuditService`, `RepositoryService`, `PersonalAccessTokenService`, `TotpService`,
`TotpChallengeStore`, and the store, hasher, second factor, and
`IProjectAccess` interfaces
of the `Core` subsystem, and `PersonalAccessTokenStore` of the `Storage` subsystem.

- *Type*: In-process .NET types.
- *Role*: Consumer.

**Api (consumed)**: `AddApiTokenAuthentication`, `AddApiDocument`, `MapApi`, and `MapApiDocument` of the `Api`
subsystem.

- *Type*: In-process .NET extension methods.
- *Role*: Consumer.

**ContentSources (consumed)**: `GitOptions`, `GitRunner`, `GitSource`, and `CredentialProtector` of the
`ContentSources` subsystem.

- *Type*: In-process .NET types.
- *Role*: Consumer.

**Evidence (consumed)**: `AttestationOptions`, `AttestationKeyProviderFactory`, `AttestationKeyException`,
`AttestationRenderer`, `AttestationSigner`, `AttestationService`, and the `MapAttestation` endpoint of the `Evidence`
subsystem.

- *Type*: In-process .NET types.
- *Role*: Consumer.

**Attestation settings**: The `Attestation` configuration section (for example `Attestation__Provider`), described in
the `Evidence` design.

- *Type*: Configuration (`appsettings.json` and environment variables).
- *Role*: Consumer.
- *Contract*: `Provider` (`SelfSigned` by default, `CertificateStore` or `File`), `RequireTrustedKey`,
  `CertificateThumbprint`, `StoreLocation`, `StoreName`, `CertificatePath`, `PrivateKeyPath`, `CertificatePassword`
  (a secret), `CertificatePasswordFile`, `ExpiryWarningDays` (30 by default, 0 for no warning), and `SelfSignedFolder`
  (empty for an `attestation-selfsigned` folder inside the key ring folder).
- *Constraints*: The server refuses to start when the signing key cannot be used.

**Data protection settings**: The `DataProtection` configuration section.

- *Type*: Configuration (`appsettings.json` and environment variables).
- *Role*: Consumer.
- *Contract*: `KeysPath` is the folder of the key ring that protects the stored repository tokens (empty for a `keys`
  folder beside the database). The `Git` section sets `CachePath` (empty for a `git-cache` folder beside the database)
  and `TimeoutSeconds` (120 by default, 1 to 3600) of the git content source.
- *Constraints*: The server refuses to start when the key ring folder cannot be written, and when the key ring folder
  holds no key but the database holds protected secrets (the lost-ring refusal), unless `AllowNewKeyRing` is true. The
  other `DataProtection` settings (protection, certificates, `RequireEncryptedKeys`) are described in the
  `KeyRingProtection` design; `AllowNewKeyRing` (false by default) is the escape hatch of the lost-ring refusal.

**SecretScan (consumed and provided)**: The read-only count of the protected secrets of the database and of the files
of the key ring, described in its own design. The server calls it before the probe round trip; the self-validation
check `ConfiguredKeys` of the `SelfTest` subsystem calls it too.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Takes the services of the install and the folder of the key ring and returns the numbers of key files
  and of readable and unreadable secrets; never creates a key and never writes.

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
the Core hasher interface, `ProjectAccess` as the Core `IProjectAccess` (a singleton, registered before the services
that take it, so that every service and the API decide visibility with the same instance), and the seven Core services
(`ProjectService`, `ReviewService`, `ThreadService`,
`ChecklistService`, `UserService`, `SigningService`, and `AuditService`) as singletons shared by all browser
connections, and `ReauthProofStore` as a singleton so that the Identity endpoints and `SigningService` share one set of
proofs. `AuditService` is created with a hook that writes a warning to the log when an event cannot be recorded
(the failure is never shown to the user), and `UserService` receives it so that failed sign-ins and lockouts are
recorded. `RepositoryStore` (as the Core repository store interface), `CredentialProtector` (as the Core token
protector), `RepositoryService`, `GitRunner`, and `GitSource` (as the Core git provider interface) are registered as
singletons in the same way, together with the `Git` and `DataProtection` settings. `ReviewMarkConfigParser` (as the Core
review-set parser interface) and `ReviewPlanner` are registered as singletons too. The
services hold no per-user state, and every
database operation opens its own short-lived context from the factory. The registration is the public method
`AddServices`, so that `UserCommand`, `AuditCommand`, and `KeysCommand` build exactly the same services as the server.

The attestation of the `Evidence` subsystem is registered in the same place: the `Attestation` settings, the key
provider that the factory chooses from them, the renderer, the signer, and `AttestationService`, all singletons. The
self-signed key is kept in `Attestation:SelfSignedFolder`, or in an `attestation-selfsigned` folder inside the folder of
the Data Protection key ring, so that it persists and is backed up with the other keys. `CreateApplication` calls
`ValidateAttestation` right after the key ring check: it obtains the signing key, and an `AttestationKeyException`
(an unknown provider, a missing thumbprint, a certificate that is not in the store, `RequireTrustedKey` with a
self-signed key or a certificate without a signing key usage, an unusable folder, or a protected key file that the key
ring cannot decrypt, which names the file) becomes an
`InvalidOperationException` "The attestation signing key is not
usable: ..." that names the setting. The same reasoning applies as for the database and the key ring: an operator who
chose a certificate must learn at the start that the choice does not work. The price is that a server whose key is in a
hardware module that is not present does not start. The endpoint `/projects/{projectId}/reviews/{reviewId}/attestation.pdf`
is mapped after the sign-in endpoints and requires a signed-in user.

The `Api` subsystem is registered after the sign-in: `AddApiTokenAuthentication` adds the bearer token scheme and the
`Api` policy, and `AddApiDocument` adds the OpenAPI document generation. `MapApi` and `MapApiDocument` come after
`MapAttestation` and before the Razor components, so the routes under `/api` are matched before the pages and an
unknown address under `/api` is a JSON 404 rather than the page of the user interface. The token store and
`PersonalAccessTokenService` are registered by `AddServices`, so the command-line verbs get the same services.

The database is created right after the application is built, before the first request, so that a
problem with the settings or the file system stops the start-up with one clear message rather than
failing on the first page request. The sign-in settings (`Auth:SessionHours` and `Auth:RevalidationSeconds`) are
validated at the same point, for the
same reason: an invalid value stops the start-up with the message "Invalid sign-in settings: ...", instead of
failing at the first sign-in. The settings of the OpenID Connect provider (`ValidateOidcOptions`) are checked right
after them, by reading the options once so that their validator runs: an invalid value stops the start-up with
"Invalid OpenID Connect sign-in settings: ...", which names the setting and never a value or the secret, and while the
provider is off nothing is read. `OidcSignIn.AddOidcSignIn` registers the provider only when it is enabled, and
`MapOidcSignIn` maps its endpoints next to the sign-in endpoints; `/signin-oidc`, the callback of the handler, is a
reserved address, so the pages and the API never answer it. The settings of the second factor (`ValidateTotpOptions`)
are checked next the same way: an invalid `Identity:Totp:Issuer` stops the start-up with "Invalid two-factor sign-in
settings: ...". `MapTotpSignIn` maps
`POST /auth/login-totp` only while `Identity:Totp:Enabled` is true (the default); with it off, accounts with a second
factor sign in with the password alone, which is a documented trade-off for an administrator who lost access. The
second factor services (`TotpSignIn.AddTotpSignIn` for the settings, `TotpStore`, `TotpAlgorithm`,
`TotpSecretProtector`, `TotpChallengeStore`, and `TotpService`)
are registered by `AddServices`, so the command-line verbs get the same services. The git setting `Git:TimeoutSeconds`
is checked the same
way and must be from 1 to 3600:
any other number stops the start-up with "Invalid git settings: Git:TimeoutSeconds must be between 1 and 3600
seconds."

The key ring of the Data Protection system is persisted in a folder, because the stored repository tokens are
encrypted with it: with the default in-memory or profile-bound key ring, a restart or a move to another account could
leave every token unreadable. The folder is `DataProtection:KeysPath`, or a `keys` folder beside the database. The
key files in the folder are protected at rest as the `KeyRingProtection` unit decides (see its design): with Windows
DPAPI, with a certificate that the operator supplies, or, without a certificate, by the permissions
of the folder and the files alone (0700 and 0600 on Linux and macOS, restricted as the files are created; on Windows
with `PermissionsOnly` an owner-only access rule of the folder), with a warning at every start. `AddServices` reads the
`DataProtection` section once (`KeyRingProtection.Prepare`), applies the protection to the Data Protection builder, and
keeps the manual `XmlRepository` for the folder, wrapped so that new key files are restricted as they are created. Like
the
database, the folder is checked once at start-up (`ValidateDataProtection`): a plan with a problem or a ring that the
certificate does not open (`KeyRingProtection.CheckRing`, which must run before anything can create a key) stops the
start-up with one message that names the setting; then, still before anything can create a key, `SecretScan` counts the
protected secrets of the database (`GuardLostRing`): when the folder holds no key file but the database holds secrets,
the ring was lost or replaced, Data Protection would silently create a new key, and every stored secret would become
unreadable, so the start-up is refused with "Key ring '...' is empty but the database holds N protected secrets..."
unless `DataProtection:AllowNewKeyRing` is true (one start, with a warning that gives the count). When the ring has
keys but some secrets cannot be decrypted (the ring belongs to another database or was replaced), the server logs an
error with the counts per kind and the remedy, and starts, because the rest of the data is usable. The folder is
then checked by writing and deleting a probe file and
by encrypting and decrypting a test value, so that a folder that cannot be read or written stops the start-up with one
clear message
naming the setting. The check is part of `CreateApplication`, so it applies to the `serve` verb only; the command-line
verbs register the same services (and therefore the same protection plan for the key files) but do not use the key
ring, so
they neither check, scan, nor probe it and report no problem of the plan. Three consequences are
documented for the administrator (see the `ContentSources` design and the user guide): losing or replacing the key
ring makes the stored tokens unreadable and they must be entered again; a ring protected with DPAPI cannot be restored
on another machine or account, and a ring protected by a certificate cannot be read without it, so the certificate must
be backed up apart from the ring; and persisting the key ring changes
the keys of the sign-in cookies once, so users sign in again after the first start. The git cache folder is resolved
the same way (`Git:CachePath`, or `git-cache` beside the database) and is created by the git source when first used.

`UserCommand` does not start a server. It builds the configuration the same way (the same `appsettings.json` and
environment), registers the services with `AddServices`, creates the database if needed, and calls
`UserService.CreateAsync`; see its design. `AuditCommand` does the same, but never creates the database (it reports a
missing file as an error), and calls `AuditService.VerifyAsync`; see its
design.

The composition is shared by production and tests. `ServerHostOptions` lets a test switch off
static asset mapping, which needs a manifest that only the real application has, and attach an
in-memory server and a capturing logger provider. It also lets a test point the server at a temporary
database through the configuration. This keeps the tested pipeline identical to
the shipped one.

Error handling is local to the subsystem: an address that is unavailable at start-up, which
Kestrel reports as an `IOException`, is converted into a message on `Context.WriteError` and a
result of 1; a database that cannot be prepared (an unsupported provider, an unusable location) or sign-in settings
(including the OpenID Connect settings) that are invalid raise
an `InvalidOperationException` while building, which `RunAsync` converts into the same kind of error
report and a result of 1; and a cancellation requested before start-up completes is treated as a normal
stop. Any other exception propagates to `Program.Main`.

Structured logging is built on `Microsoft.Extensions.Logging` message templates with named
placeholders, so the listen URL is available as a named property to any log sink that
understands structured entries. Framework components, such as request handling, write to the
same logging pipeline.
