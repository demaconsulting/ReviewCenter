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
- A sign-in throttle setting that is not positive, a window outside 1 to 86400 seconds, or a per-address limit below
  the per-name limit is reported with an error that names the `Identity:Throttle` setting; the defaults and a valid
  custom set are accepted.
- A git timeout (`Git:TimeoutSeconds`) outside 1 to 3600 seconds is reported with an error that names the setting;
  1 and 3600 are accepted.
- An unsupported database provider and an unusable database location are reported as the error "Failed to prepare
  the database", and `RunAsync` returns exit code 1 for each.
- The repository store, the git source, and the token protector are registered as shared instances; the git cache and
  the Data Protection key ring are placed beside the database unless configured; the key ring is persisted, so a token
  encrypted before a restart is readable after it.
- The project access rules (`IProjectAccess`) are registered as one shared instance. No unit test resolves the
  registration alone; it is shown by the real pipeline serving the domain services
  (`ServerHost_CreateApplication_DatabaseConfigured_CreatesFileAndServesDomainServices`) and by the API refusing a
  non-member (`ApiProjectAccess_EveryProjectRoute_NonMemberToken_AnswersLikeAMissingProject`).
- A key ring folder that cannot be written stops the start of the server with an error that names the
  `DataProtection:KeysPath` setting and the folder.
- The personal access token service is one shared instance, the routes under `/api` are mapped, the OpenAPI document is
  served without a token, a protected route without a token is answered 401, and an unknown address under `/api` is
  answered with a JSON problem document instead of a page.

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

**ServerHost_CreateApplication_InvalidThrottleSettings_ThrowsClearError**: A theory; the pipeline is built with one
invalid `Identity:Throttle` value (a per-name limit of 0 or -1, a per-address limit of 0, a per-address limit of 4 with
a per-name limit of 5, and a window of 0 or 86401 seconds); an `InvalidOperationException` that says "Invalid sign-in
settings" and names the setting is thrown. This scenario is tested by
`ServerHost_CreateApplication_InvalidThrottleSettings_ThrowsClearError`.

**ServerHost_CreateApplication_ValidThrottleSettings_BuildsTheApplication**: The pipeline is built with the default
throttle settings and with explicit valid ones (3, 3, and 86400 seconds); both are built. This scenario is tested by
`ServerHost_CreateApplication_ValidThrottleSettings_BuildsTheApplication`.

**ServerHost_CreateApplication_GitTimeoutOutOfRange_ThrowsClearError**: A theory over 0, -5, and 3601: the pipeline is built
with the setting `Git:TimeoutSeconds` set to that value; an `InvalidOperationException` whose message names the setting
is thrown. **ServerHost_CreateApplication_GitTimeoutAtLimits_Starts**: a theory over 1 and 3600: the pipeline is built
and the configured value is in effect. These scenarios are tested by the tests of those names.

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

**ServerHost_CreateApplication_ApiRoutes_AreMapped**: The real pipeline is started on an in-memory server; the
`PersonalAccessTokenService` is taken from the application services twice and is the same instance. A request for
`/api/projects` without a token is answered 401, the OpenAPI document at `/api/openapi.json` is answered 200 without a
token, and an unknown address `/api/nothing` is answered 404 with the media type `application/problem+json`, not with a
page. This scenario is tested by `ServerHost_CreateApplication_ApiRoutes_AreMapped`.

**ServerHost_Restart_KeyRingPersisted_TokenStaysReadable**: A token is encrypted with the protector of one application
instance, which is then disposed; a second instance on the same database decrypts it. This scenario is tested by
`ServerHost_Restart_KeyRingPersisted_TokenStaysReadable`.

**ServerHost_AddServices_Configured_RegistersReviewPlannerAndParser**: The real pipeline is started on an in-memory
server and the `ReviewPlanner` and the `IReviewSetParser` are taken from the application services twice each; every
call returns the same instance. This scenario is tested by `ServerHost_AddServices_Configured_RegistersReviewPlannerAndParser`.

**ServerHost_CreateApplication_KeyRingFolderNotWritable_ThrowsClearError**: The pipeline is built with
`DataProtection:KeysPath` set to an existing file; an `InvalidOperationException` whose message names the setting and the
path is thrown. This scenario is tested by `ServerHost_CreateApplication_KeyRingFolderNotWritable_ThrowsClearError`.

**ServerHost_ResolveKeysPath_ConfiguredOrDefault_ChoosesTheFolder**: A configured key ring folder is used as given; a
missing or blank setting gives a `keys` folder beside the database. This scenario is tested by
`ServerHost_ResolveKeysPath_ConfiguredOrDefault_ChoosesTheFolder`.

**ServerHost_CreateApplication_NoAttestationSettings_CreatesSelfSignedKeyBesideTheDataProtectionKeys**: The pipeline
is started twice on the same database folder with no attestation setting. The key file exists in the
`attestation-selfsigned` folder inside the `keys` folder, and the key (its thumbprint) is the same after the restart.
This scenario is tested by
`ServerHost_CreateApplication_NoAttestationSettings_CreatesSelfSignedKeyBesideTheDataProtectionKeys`.

**ServerHost_CreateApplication_SelfSignedFolderConfigured_KeepsTheKeyThere**: `Attestation:SelfSignedFolder` names a
folder; the key file is created there and the provider reports an available key. This scenario is tested by
`ServerHost_CreateApplication_SelfSignedFolderConfigured_KeepsTheKeyThere`.

**ServerHost_CreateApplication_UnknownAttestationProvider_ThrowsClearError**: The provider setting names a provider that
does not exist; an `InvalidOperationException` "The attestation signing key is not usable" that names
`Attestation:Provider` is thrown. This scenario is tested by
`ServerHost_CreateApplication_UnknownAttestationProvider_ThrowsClearError`.

**ServerHost_CreateApplication_CertificateStoreWithoutThumbprint_ThrowsClearError**: The certificate store is chosen
and no thumbprint is set; the message names `Attestation:CertificateThumbprint`. This scenario is tested by
`ServerHost_CreateApplication_CertificateStoreWithoutThumbprint_ThrowsClearError`.

**ServerHost_CreateApplication_CertificateNotInStore_ThrowsClearError**: The thumbprint names a certificate that is not
in the store; the message holds the thumbprint and "was not found". This scenario is tested by
`ServerHost_CreateApplication_CertificateNotInStore_ThrowsClearError`.

**ServerHost_CreateApplication_RequireTrustedKeyWithSelfSignedProvider_ThrowsClearError**: `RequireTrustedKey` is set
and the provider stays self-signed; the message names `Attestation:RequireTrustedKey` and the self-signed key. This
scenario is tested by `ServerHost_CreateApplication_RequireTrustedKeyWithSelfSignedProvider_ThrowsClearError`.

**ServerHost_CreateApplication_SelfSignedFolderIsAFile_ThrowsClearError**: A file stands where the folder of the key
should be; the message names `Attestation:SelfSignedFolder`. This scenario is tested by
`ServerHost_CreateApplication_SelfSignedFolderIsAFile_ThrowsClearError`.

**Audit services**: The registration of the audit store, the shared audit appender, and the audit service in
`AddServices` is exercised by the sign-in audit tests and the `AuditCommand` tests, which resolve them from the real
pipeline; there is no separate test of the registration.
