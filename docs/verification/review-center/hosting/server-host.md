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
- An unsupported database provider and an unusable database location are reported as the error "Failed to prepare
  the database", and `RunAsync` returns exit code 1 for each.

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

**ServerHost_AddServices_Configured_RegistersUserService**: The real pipeline is started on an in-memory server and
a user is created through the `UserService` taken from the application services; the user is stored with a database
identity. This scenario is tested by `ServerHost_AddServices_Configured_RegistersUserService`.

**ServerHost_AddServices_Configured_RegistersSigningService**: The real pipeline is started on an in-memory server and
the `SigningService` is taken from the application services twice; both calls return the same instance, and it reports
no approval for a review that does not exist. This scenario is tested by
`ServerHost_AddServices_Configured_RegistersSigningService`.
