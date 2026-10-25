### ServerHost

#### Verification Approach

`ServerHost` is verified with unit tests defined in `ServerHostTests.cs`. The tests call
`ServerHost.CreateApplication` with a real `Context` created from controlled arguments and a
`ServerHostOptions` that disables static asset mapping and selects an in-memory test server, so
the production pipeline runs without binding a network port. A capturing logger provider records
log entries, and the registered logger providers are inspected through the application services.
No mocking is applied.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The health route answers HTTP status 200 with the body `Healthy`.
- The start-up log entry exposes the listen URL as a named value.
- The console logging provider is registered unless the context is silent.
- A null context is rejected with an `ArgumentNullException`.
- The configured database is created and the Core services (including the user service and the signing service) work
  against it; data
  survives a second application instance on the same file. The signing service is a single shared instance.
- A session length that is not positive is reported with an error that names the `Auth:SessionHours` setting.
- A re-check interval of open pages below 5 seconds is reported with an error that names the
  `Auth:RevalidationSeconds` setting.
- An unsupported database provider and an unusable database location are reported as the error "Failed to prepare
  the database", and `RunAsync` returns exit code 1 for each.
- The repository registry, the git source, and the token protector are registered as shared instances; the git cache and
  the Data Protection key ring are placed beside the database unless configured; the key ring is persisted, so a token
  encrypted before a restart is readable after it.
- A key ring folder that cannot be written stops the start of the server with an error that names the
  `DataProtection:KeysPath` setting and the folder.

#### Test Scenarios

**ServerHost_CreateApplication_HealthRoute_Returns200Healthy**: The real pipeline is started on
an in-memory server and the health route is requested; the response is 200 with the plain-text
body `Healthy`. This scenario is tested by
`ServerHost_CreateApplication_HealthRoute_Returns200Healthy`.

**ServerHost_CreateApplication_StartupLogging_EmitsStructuredEntry**: The pipeline is started
with an explicit URL; the captured start-up entry carries the URL as a named value that a log
consumer can read separately from the message text. This scenario is tested by
`ServerHost_CreateApplication_StartupLogging_EmitsStructuredEntry`.

**ServerHost_CreateApplication_Silent_OmitsConsoleProvider**: The pipeline is started from a
silent context; no console logger provider is registered. This scenario is tested by
`ServerHost_CreateApplication_Silent_OmitsConsoleProvider`.

**ServerHost_CreateApplication_NotSilent_IncludesConsoleProvider**: The pipeline is started from
a context that is not silent; the console logger provider is registered. This scenario is tested
by `ServerHost_CreateApplication_NotSilent_IncludesConsoleProvider`.

**ServerHost_CreateApplication_NullContext_ThrowsArgumentNullException**: `CreateApplication` is
called with a null context; an `ArgumentNullException` is thrown. This scenario is tested by
`ServerHost_CreateApplication_NullContext_ThrowsArgumentNullException`.

**ServerHost_CreateApplication_DatabaseConfigured_CreatesFileAndServesDomainServices**: The pipeline is started
with the database connection string pointing at a new file; a project is created through the registered
`ProjectService`. The file exists afterwards and the project is stored in it. This scenario is tested by
`ServerHost_CreateApplication_DatabaseConfigured_CreatesFileAndServesDomainServices`.

**ServerHost_CreateApplication_RestartedOnSameDatabase_KeepsData**: A project is created through one application
instance, which is then disposed; a second instance on the same database file lists the project. This scenario is
tested by `ServerHost_CreateApplication_RestartedOnSameDatabase_KeepsData`.

**ServerHost_CreateApplication_UnsupportedProvider_ThrowsClearError**: The pipeline is built with an unsupported
database provider; an `InvalidOperationException` whose message begins "Failed to prepare the database" and names
the provider is thrown. This scenario is tested by
`ServerHost_CreateApplication_UnsupportedProvider_ThrowsClearError`.

**ServerHost_RunAsync_UnsupportedProvider_ReturnsExitCodeOne**: `RunAsync` is called with an unsupported database
provider; it returns 1, and the context holds the error. This scenario is tested by
`ServerHost_RunAsync_UnsupportedProvider_ReturnsExitCodeOne`.

**ServerHost_RunAsync_UnusableDatabaseLocation_ReturnsExitCodeOne**: `RunAsync` is called with a database path that
is an existing folder; it returns 1, and the context holds the error. This scenario is tested by
`ServerHost_RunAsync_UnusableDatabaseLocation_ReturnsExitCodeOne`.

**ServerHost_CreateApplication_NonPositiveSessionHours_ThrowsClearError**: The pipeline is built with the setting
`Auth:SessionHours` set to 0; an `InvalidOperationException` whose message names the setting is thrown. This scenario
is tested by `ServerHost_CreateApplication_NonPositiveSessionHours_ThrowsClearError`.

**ServerHost_CreateApplication_RevalidationSecondsBelowMinimum_ThrowsClearError**: The pipeline is built with the
setting `Auth:RevalidationSeconds` set to 4, below the minimum of 5; an `InvalidOperationException` whose message names
the setting is thrown. This scenario is tested by
`ServerHost_CreateApplication_RevalidationSecondsBelowMinimum_ThrowsClearError`.

**ServerHost_AddServices_Configured_RegistersUserService**: The real pipeline is started on an in-memory server and
a user is created through the `UserService` taken from the application services; the user is stored with a database
identity. This scenario is tested by `ServerHost_AddServices_Configured_RegistersUserService`.

**ServerHost_AddServices_Configured_RegistersSigningService**: The real pipeline is started on an in-memory server and
the `SigningService` is taken from the application services twice; both calls return the same instance, and it reports
no approval for a review that does not exist. This scenario is tested by
`ServerHost_AddServices_Configured_RegistersSigningService`.

**ServerHost_AddServices_Configured_RegistersRepositoryServicesWithDefaultFolders**: The real pipeline is started on an
in-memory server with a new database; the `RepositoryService` is taken from the application services twice and is the
same instance, the git source and the token protector are registered, the git cache path is a `git-cache` folder beside
the database, a key file exists in a `keys` folder beside the database, and a protected value does not contain the
token and is unprotected to it. This scenario is tested by
`ServerHost_AddServices_Configured_RegistersRepositoryServicesWithDefaultFolders`.

**ServerHost_Restart_KeyRingPersisted_TokenStaysReadable**: A token is encrypted with the protector of one application
instance, which is then disposed; a second instance on the same database decrypts it. This scenario is tested by
`ServerHost_Restart_KeyRingPersisted_TokenStaysReadable`.

**ServerHost_CreateApplication_KeyRingFolderNotWritable_ThrowsClearError**: The pipeline is built with
`DataProtection:KeysPath` set to an existing file; an `InvalidOperationException` whose message names the setting and the
path is thrown. This scenario is tested by `ServerHost_CreateApplication_KeyRingFolderNotWritable_ThrowsClearError`.

**ServerHost_ResolveKeysPath_ConfiguredOrDefault_ChoosesTheFolder**: A configured key ring folder is used as given; a
missing or blank setting gives a `keys` folder beside the database. This scenario is tested by
`ServerHost_ResolveKeysPath_ConfiguredOrDefault_ChoosesTheFolder`.

**Audit services**: The registration of the audit store, the shared audit appender, and the audit service in
`AddServices` is exercised by the sign-in audit tests and the `AuditCommand` tests, which resolve them from the real
pipeline; there is no separate test of the registration.
