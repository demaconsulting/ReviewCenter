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
  1 and 3600 are accepted; without the setting the default of 120 seconds applies.
- An anonymous visitor is served the home page and the login page, and a protected page redirects to the login page
  with the return address.
- Invalid settings of sign-in with an OpenID Connect provider (a missing or insecure authority, a bad client
  identifier, scopes without `openid`, a long display name, a bad user name claim, an age outside 10 to 900 seconds, or
  a control character in the secret) stop the start with an error that names the `Identity:Oidc` setting and never
  contains the value or the secret; with the provider off nothing is checked, and valid settings (https, or http on a
  loopback host) build the application.
- An unsupported database provider and an unusable database location are reported as the error "Failed to prepare
  the database", and `RunAsync` returns exit code 1 for each; the text of the error reaches the context.
- The repository store, the git source, and the token protector are registered as shared instances; the git cache and
  the Data Protection key ring are placed beside the database unless configured; the key ring is persisted, so a token
  encrypted before a restart is readable after it.
- The project access rules (`IProjectAccess`) are registered as one shared instance, shown by resolving the
  registration twice (`ServerHost_AddServices_Configured_RegistersProjectAccessAsSingleton`).
- The audit store, the audit appender, and the audit service are each registered as one shared instance, and an audit
  trail that cannot be written logs a warning and reports failure instead of throwing.
- Registering only the services (as the command-line verbs do) creates no key folder and no key file.
- A key ring folder that cannot be written stops the start of the server with an error that names the
  `DataProtection:KeysPath` setting and the folder.
- A certificate key ring is read again after a restart; invalid key ring settings, a certificate that cannot be loaded
  (never naming the password), a certificate that does not open the existing ring (without a second key being
  created), and a plain ring with `DataProtection:RequireEncryptedKeys` each stop the start with an error that names
  the setting at fault; `PermissionsOnly` logs a warning at every start.
- The lost-ring guard: a database with protected secrets and a key folder with no key file (or no folder) stops the
  start with an error that names the folder, the count, and `DataProtection:AllowNewKeyRing`, and no key is created;
  with the setting true the server starts and logs a warning with the count; a restart on the same ring, and a fresh
  installation (no secrets, empty ring), start without complaint; when the ring has keys but some secrets cannot be
  decrypted, the server logs an error with the counts and starts. A database that cannot be read stops the start when
  the ring has no key file (naming the folder and `Database:ConnectionString`, no exception text or database location,
  no key created, with or without the setting), and only logs a warning when the ring has keys.
- The personal access token service is one shared instance, the routes under `/api` are mapped, the OpenAPI document is
  served without a token, a protected route without a token is answered 401, and an unknown address under `/api` is
  answered with a JSON problem document instead of a page.
- A host that is built with `IsolatedSettings` listens only on the address of the context, takes its environment name
  (`Production`) and its database from the settings and not from the process environment (a poisoned
  `ASPNETCORE_URLS`, Kestrel endpoint variable, `DOTNET_ENVIRONMENT` and `Database__*` settings change nothing), and
  still serves routes with constraints.
- Invalid settings of the two-factor sign-in (a value of `Identity:Totp:Enabled` that is not a boolean, and an
  `Identity:Totp:Issuer` that is empty or holds a colon) stop the start with an error that says the two-factor
  settings are invalid and names the setting; valid settings provide the settings object to the services, with the
  feature on and the issuer `ReviewCenter` by default, and a switched-off feature with a trimmed issuer when set; the
  endpoint of the second step of the sign-in is mapped only while the feature is on.
- Invalid notification settings (a sender address that is not an address, an empty base address, an empty SMTP host, a
  port out of range, an hour out of range, and an unknown time zone) stop the start while notifications are on with an
  error that says the notification settings are invalid and names the setting, and that does not contain the SMTP
  password; with notifications off the same garbage is not checked; valid settings build the application and log one
  warning for an inline password that holds no password; a database without the notification tables stops the start
  while notifications are on and does not while they are off.
- The notification worker is part of the web host only: with notifications enabled and the file or the SMTP transport the
  application has the worker; with notifications off, with the transport `None` (which stops the start before any
  worker exists), with the self-test settings, and in the shared service registration of the commands it has none; the
  worker runs while the host runs and has stopped when the host has stopped.

#### Test Scenarios

**ServerHost_CreateApplication_IsolatedSettings_IgnoresPoisonedEnvironment**: The process environment is poisoned
(listen address, Kestrel endpoint, environment name, database settings and sentinel paths) and an isolated host is
started; it listens on one loopback address that is not the poisoned port. This scenario is tested by
`ServerHost_CreateApplication_IsolatedSettings_IgnoresPoisonedEnvironment`.

**ServerHost_CreateApplication_IsolatedSettings_RunsInProductionEnvironment**: The same poisoned environment names
the Development environment and an isolated host is started; the host reports the `Production` environment. This
scenario is tested by `ServerHost_CreateApplication_IsolatedSettings_RunsInProductionEnvironment`.

**ServerHost_CreateApplication_IsolatedSettings_LeavesAmbientDataPlacesUntouched**: The same poisoned environment
names a database, a key ring folder and a git cache folder in a sentinel folder and an isolated host is started; the
database exists only in the temporary folder of the host, and nothing is created in the sentinel folder. This scenario
is tested by `ServerHost_CreateApplication_IsolatedSettings_LeavesAmbientDataPlacesUntouched`.

**ServerHost_CreateApplication_WithoutIsolation_AmbientEndpointWins**: The same poisoned environment and a normal host;
the host listens on the poisoned port. This proves that the poison above is effective, so the isolated test cannot pass
by accident. This scenario is tested by `ServerHost_CreateApplication_WithoutIsolation_AmbientEndpointWins`.

**ServerHost_CreateApplication_IsolatedSettings_ConstrainedRoutesWork**: An isolated host is started and
`/api/projects/abc/reviews` and `/api/projects/1/reviews` are requested; the first is 404 (the constraint applies) and
the second is 401 (the route is reached without a token). This shows that the empty builder has the full routing. This
scenario is tested by `ServerHost_CreateApplication_IsolatedSettings_ConstrainedRoutesWork`.

**ServerHost_CreateApplication_HealthRoute_Returns200Healthy**: The real pipeline is started on
an in-memory server and the health route is requested; the response is 200 with the plain-text
body `Healthy`. The 200 status serves the health route requirement; because the request carries no credentials, the
same 200 also serves the requirement that the health route needs no sign-in. This scenario is tested by
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
with the database connection string pointing at a new file; the file and its folder exist afterwards. This scenario is
tested by `ServerHost_CreateApplication_DatabaseConfigured_CreatesFileAndServesDomainServices`.

**ServerHost_CreateApplication_DatabaseConfigured_ProjectServiceStoresProjects**: The pipeline is started with the
database connection string pointing at a new file; a project is created through the registered `ProjectService` and
read back. This scenario is tested by `ServerHost_CreateApplication_DatabaseConfigured_ProjectServiceStoresProjects`.

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
invalid `Identity:Throttle` value (a per-name limit of 0 or -1, or a per-address limit of 0); an
`InvalidOperationException` that says "Invalid sign-in settings" and names the setting is thrown. This scenario is
tested by `ServerHost_CreateApplication_InvalidThrottleSettings_ThrowsClearError`.

**ServerHost_CreateApplication_ThrottleWindowOutsideRange_ThrowsClearError**: A theory over 0 and 86401; the pipeline
is built with `Identity:Throttle:WindowSeconds` set to that value; an `InvalidOperationException` that says "Invalid
sign-in settings" and names the setting is thrown. This scenario is tested by
`ServerHost_CreateApplication_ThrottleWindowOutsideRange_ThrowsClearError`.

**ServerHost_CreateApplication_ThrottleAddressLimitBelowNameLimit_ThrowsClearError**: The pipeline is built with a
per-address limit of 4 and a per-name limit of 5; an `InvalidOperationException` that says "Invalid sign-in settings"
and names `Identity:Throttle:MaxFailuresPerAddress` is thrown. This scenario is tested by
`ServerHost_CreateApplication_ThrottleAddressLimitBelowNameLimit_ThrowsClearError`.

**ServerHost_CreateApplication_ValidThrottleSettings_BuildsTheApplication**: The pipeline is built with the default
throttle settings and with explicit valid ones (3, 3, and 86400 seconds); both are built. This scenario is tested by
`ServerHost_CreateApplication_ValidThrottleSettings_BuildsTheApplication`.

**ServerHost_CreateApplication_ExplicitValidThrottleSettings_BuildsTheApplication**: The pipeline is built with
explicit valid throttle settings (3, 3, and the largest window of 86400 seconds) and is built. This scenario is tested
by `ServerHost_CreateApplication_ExplicitValidThrottleSettings_BuildsTheApplication`.

**ServerHost_CreateApplication_InvalidOidcSettings_ThrowsClearErrorWithoutSecret**: A theory; with
`Identity:Oidc:Enabled` true and valid settings otherwise (and a secret), the pipeline is built with one invalid value:
an empty, http, malformed, or query-carrying authority, an empty or space-containing client identifier, scopes without
`openid`, a display name of 51 characters, a user name claim with a space, an age of 5 or 901 seconds, an empty or
white-space client secret, or a control character in the client secret. An `InvalidOperationException` that says
"Invalid OpenID Connect sign-in settings" and
names the setting is thrown, and its message never holds the secret. The exception type, the message and the setting
name serve the requirement that the start stops with an error that names the setting; the check of the message for
the secret serves the requirement that the error never shows the client secret. This scenario is tested by
`ServerHost_CreateApplication_InvalidOidcSettings_ThrowsClearErrorWithoutSecret`.

**ServerHost_CreateApplication_ClientSecretFileProblem_ThrowsClearErrorWithoutSecret**: A theory over a client secret
file that does not exist and a client secret file that exists and is set together with an inline secret; the start of
the application stops with a message that names `Identity:Oidc:ClientSecretFile`. In both cases the message does not
hold the secret, which serves the requirement that the error never shows the client secret; the case of the missing
file also serves the requirement that a missing client secret file stops the start with an error that names the
setting. This scenario is tested by
`ServerHost_CreateApplication_ClientSecretFileProblem_ThrowsClearErrorWithoutSecret`.

**ServerHost_CreateApplication_OidcOff_BuildsTheApplication**: The pipeline is built with sign-in with a provider off
and a garbage authority (not read); the application is built. This scenario is tested by
`ServerHost_CreateApplication_OidcOff_BuildsTheApplication`.

**ServerHost_CreateApplication_OidcOffOrValid_BuildsTheApplication**: The pipeline is built with sign-in on, an
https authority and the largest age of 900 seconds; the application is built. This scenario is tested by
`ServerHost_CreateApplication_OidcOffOrValid_BuildsTheApplication`.

**ServerHost_CreateApplication_OidcLoopbackHttpAuthority_BuildsTheApplication**: With sign-in on and an http authority
on `localhost`, the pipeline is built. This scenario is tested by
`ServerHost_CreateApplication_OidcLoopbackHttpAuthority_BuildsTheApplication`.

**ServerHost_CreateApplication_ClientSecretFileWithInlineSecret_ThrowsClearErrorWithoutSecret**: With sign-in on, an
existing secret file and an inline secret both set, the start of the application stops with a message that names
`Identity:Oidc:ClientSecretFile` and contains neither secret. This scenario is tested by
`ServerHost_CreateApplication_ClientSecretFileWithInlineSecret_ThrowsClearErrorWithoutSecret`.

**ServerHost_CreateApplication_GitTimeoutOutOfRange_ThrowsClearError**: A theory over 0, -5, and 3601: the pipeline is built
with the setting `Git:TimeoutSeconds` set to that value; an `InvalidOperationException` whose message names the setting
is thrown. **ServerHost_CreateApplication_GitTimeoutAtLimits_Starts**: a theory over 1 and 3600: the pipeline is built
and the configured value is in effect. These scenarios are tested by the tests of those names.

**ServerHost_AddServices_Configured_RegistersUserService**: The real pipeline is started on an in-memory server and
a user is created through the `UserService` taken from the application services; the user is stored with a database
identity. This scenario is tested by `ServerHost_AddServices_Configured_RegistersUserService`.

**ServerHost_AddServices_Configured_RegistersReviewService**: The real pipeline is started on an in-memory server and a
project is stored through the `ProjectService`; the `ReviewService` taken from the application services lists the
reviews of that project from the database and the list is empty. This scenario is tested by
`ServerHost_AddServices_Configured_RegistersReviewService`.

**ServerHost_AddServices_Configured_RegistersSigningService**: The real pipeline is started on an in-memory server and
the `SigningService` is taken from the application services twice; both calls return the same instance, and it reports
no approval for a review that does not exist. This scenario is tested by
`ServerHost_AddServices_Configured_RegistersSigningService`.

**ServerHost_AddServices_Configured_RegistersRepositoryServiceAsSingleton**: The real pipeline is started on an
in-memory server with a new database; the `RepositoryService` is taken from the application services twice and is the
same instance. This scenario is tested by `ServerHost_AddServices_Configured_RegistersRepositoryServiceAsSingleton`.

**ServerHost_AddServices_Configured_RegistersGitSource**: The real pipeline is started on an in-memory server with a
new database; the git content source is taken from the application services and is registered. This scenario is tested
by `ServerHost_AddServices_Configured_RegistersGitSource`.

**ServerHost_AddServices_Configured_RegistersRepositoryServicesWithDefaultFolders**: The real pipeline is started on an
in-memory server with a new database and without `Git:CachePath`; the git cache path is a `git-cache` folder beside
the database. This scenario is tested by
`ServerHost_AddServices_Configured_RegistersRepositoryServicesWithDefaultFolders`.

**ServerHost_AddServices_Configured_PlacesTheKeyRingBesideTheDatabase**: The real pipeline is started on an in-memory
server with a new database and without `DataProtection:KeysPath`; a key file exists in a `keys` folder beside the
database. This scenario is tested by `ServerHost_AddServices_Configured_PlacesTheKeyRingBesideTheDatabase`.

**ServerHost_AddServices_Configured_CredentialProtectorRoundTripsAToken**: The real pipeline is started on an
in-memory server with a new database; the credential protector protects a token, the protected value does not contain
the token, and it is unprotected to the token again. This scenario is tested by
`ServerHost_AddServices_Configured_CredentialProtectorRoundTripsAToken`.

**ServerHost_CreateApplication_ApiRoutes_AreMapped**: The real pipeline is started on an in-memory server and the
protected address `/api/projects` is requested without a token; the answer is not 404 and is not an HTML page, so the
API routes are mapped under `/api`. This scenario is tested by `ServerHost_CreateApplication_ApiRoutes_AreMapped`.

**ServerHost_CreateApplication_ApiRegistration_RegistersTokenServiceAsSingleton**: The real pipeline is started on an
in-memory server; the `PersonalAccessTokenService` is taken from the application services twice and is the same
instance. This scenario is tested by `ServerHost_CreateApplication_ApiRegistration_RegistersTokenServiceAsSingleton`.

**ServerHost_CreateApplication_ApiRoutes_WithoutTokenAreUnauthorized**: The real pipeline is started on an in-memory
server and `/api/projects` is requested without a token; the answer is 401. This scenario is tested by
`ServerHost_CreateApplication_ApiRoutes_WithoutTokenAreUnauthorized`.

**ServerHost_CreateApplication_OpenApiDocument_IsServedWithoutToken**: The real pipeline is started on an in-memory
server and `/api/openapi.json` is requested without a token; the answer is 200. This scenario is tested by
`ServerHost_CreateApplication_OpenApiDocument_IsServedWithoutToken`.

**ServerHost_CreateApplication_UnknownApiAddress_ReturnsJsonProblem404**: The real pipeline is started on an in-memory
server and the unknown address `/api/nothing` is requested; the answer is 404 with the media type
`application/problem+json`, not a page. This scenario is tested by
`ServerHost_CreateApplication_UnknownApiAddress_ReturnsJsonProblem404`.

**ServerHost_Restart_KeyRingPersisted_TokenStaysReadable**: A token is encrypted with the protector of one application
instance, which is then disposed; a second instance on the same database decrypts it. This scenario is tested by
`ServerHost_Restart_KeyRingPersisted_TokenStaysReadable`.

**ServerHost_AddServices_Configured_RegistersReviewPlannerAndParser**: The real pipeline is started on an in-memory
server and the `ReviewPlanner` is taken from the application services twice; both calls return the same instance. The
name of the test is kept from the time when it also checked the parser. This scenario is tested by
`ServerHost_AddServices_Configured_RegistersReviewPlannerAndParser`.

**ServerHost_AddServices_Configured_RegistersReviewSetParserAsSingleton**: The real pipeline is started on an
in-memory server and the `IReviewSetParser` is taken from the application services twice; both calls return the same
instance. This scenario is tested by `ServerHost_AddServices_Configured_RegistersReviewSetParserAsSingleton`.

**ServerHost_AddServices_Configured_RegistersInboxService**: The real pipeline is started on an in-memory server and the
`InboxService` and the `IInboxStore` are taken from the application services twice each; every call returns the same
instance, and the service answers zero for a user who is not known. This scenario is tested by
`ServerHost_AddServices_Configured_RegistersInboxService`.

**ServerHost_CreateApplication_KeyRingFolderNotWritable_ThrowsClearError**: The pipeline is built with
`DataProtection:KeysPath` set to an existing file; an `InvalidOperationException` whose message names the setting and the
path is thrown. This scenario is tested by `ServerHost_CreateApplication_KeyRingFolderNotWritable_ThrowsClearError`.

**ServerHost_ResolveKeysPath_ConfiguredOrDefault_ChoosesTheFolder**: A missing or blank `DataProtection:KeysPath`
gives a `keys` folder beside the database. The name of the test is kept from the time when it also checked a
configured folder. This scenario is tested by `ServerHost_ResolveKeysPath_ConfiguredOrDefault_ChoosesTheFolder`.

**ServerHost_ResolveKeysPath_Configured_ChoosesTheConfiguredFolder**: A configured key ring folder is used as given.
This scenario is tested by `ServerHost_ResolveKeysPath_Configured_ChoosesTheConfiguredFolder`.

**ServerHost_Restart_CertificateKeyRing_ProtectedValueStaysReadable**: An application is built with
`DataProtection:Protection` Auto and a certificate in a temporary folder; a value is protected, the application is
disposed, and a second application over the same key ring unprotects it. This scenario is tested by
`ServerHost_Restart_CertificateKeyRing_ProtectedValueStaysReadable`.

**ServerHost_CreateApplication_CertificateKeyRing_KeyFilesAreEncrypted**: An application is built with
`DataProtection:Protection` Auto and a certificate in a temporary folder, and a value is protected; the key file in
the key ring folder holds an encrypted secret and no master key in clear text. This scenario is tested by
`ServerHost_CreateApplication_CertificateKeyRing_KeyFilesAreEncrypted`.

**ServerHost_CreateApplication_CertificateKeyRing_DoesNotLogThePassword**: An application is built twice with
`DataProtection:Protection` Auto and a certificate with a password; the two start-up entries of the key ring
protection are logged at information level, and no captured log entry contains the password. This scenario is tested
by `ServerHost_CreateApplication_CertificateKeyRing_DoesNotLogThePassword`.

**ServerHost_CreateApplication_InvalidKeyRingSettings_ThrowsClearError**: An unknown protection, an unknown DPAPI
scope, and a certificate protection without a certificate each stop the build with an `InvalidOperationException` that
starts "Invalid key ring settings:" and names the setting. This scenario is tested by
`ServerHost_CreateApplication_InvalidKeyRingSettings_ThrowsClearError`.

**ServerHost_CreateApplication_MissingCertificate_ThrowsClearError**: A certificate file that does not exist stops the
build with a message that names `DataProtection:CertificatePath` and the path. This scenario is tested by
`ServerHost_CreateApplication_MissingCertificate_ThrowsClearError`.

**ServerHost_CreateApplication_WrongPasswordCertificate_ThrowsClearErrorWithoutPassword**: A certificate file that
exists is opened with a password that is not its password; the build stops with a message that says the password is
wrong or the file is not a certificate with a private key, and that contains neither the configured password nor the
password of the certificate. The first check serves the requirement for the wrong password; the two checks for the
passwords serve the requirement that the error never shows a password. This scenario is tested by
`ServerHost_CreateApplication_WrongPasswordCertificate_ThrowsClearErrorWithoutPassword`.

**ServerHost_CreateApplication_WrongCertificateForTheRing_ThrowsClearErrorWithoutSecondKey**: A ring that one
certificate protects is opened with another; the build fails with a message that names
`DataProtection:CertificatePath` and `DataProtection:PreviousCertificatePaths`, and no second key file exists. This
scenario is tested by `ServerHost_CreateApplication_WrongCertificateForTheRing_ThrowsClearErrorWithoutSecondKey`.

**ServerHost_CreateApplication_PermissionsOnly_WarnsAtEveryStart**: The build with `PermissionsOnly` is made twice over
the same folder; each start logs the warning that the ring is not encrypted at rest. This scenario is tested by
`ServerHost_CreateApplication_PermissionsOnly_WarnsAtEveryStart`.

**ServerHost_CreateApplication_RequireEncryptedKeysWithPlaintextRing_ThrowsClearError**: A ring with a plain key file
is started with `DataProtection:RequireEncryptedKeys` true; the build fails with a message that names the setting.
This scenario is tested by `ServerHost_CreateApplication_RequireEncryptedKeysWithPlaintextRing_ThrowsClearError`.

**ServerHost_CreateApplication_FreshInstallation_StartsAndCreatesRing**: A new database without secrets and an empty
key folder are started; the build succeeds, the ring now holds exactly one key, and the log holds no error entry and no
mention of `AllowNewKeyRing`. This scenario is tested by
`ServerHost_CreateApplication_FreshInstallation_StartsAndCreatesRing`.

**ServerHost_CreateApplication_LostRing_RefusesToStartAndCreatesNoKey**: A database holds a repository token and a
second-factor secret that a ring protected, and the key folder is empty or has been removed (a theory with both
cases). The build fails with the message "Key ring '...' is empty but the database holds 2 protected secrets" that
names `DataProtection:AllowNewKeyRing`. The name of the test is kept from the time when it also checked that no key
file was created. This scenario is tested by `ServerHost_CreateApplication_LostRing_RefusesToStartAndCreatesNoKey`.

**ServerHost_CreateApplication_LostRing_CreatesNoKey**: The same lost ring is started in both cases of the theory;
the build fails and no key file exists afterwards. This scenario is tested by
`ServerHost_CreateApplication_LostRing_CreatesNoKey`.

**ServerHost_CreateApplication_LostRing_ErrorShowsNoSecret**: The same lost ring is started in both cases of the
theory; the message of the error does not contain the stored token. This scenario is tested by
`ServerHost_CreateApplication_LostRing_ErrorShowsNoSecret`.

**ServerHost_CreateApplication_LostRingWithAllowNewKeyRing_StartsWithWarning**: The same lost ring is started with
`DataProtection:AllowNewKeyRing` true; the build succeeds and a warning that names the setting and the number of
secrets is logged. This scenario is tested by `ServerHost_CreateApplication_LostRingWithAllowNewKeyRing_StartsWithWarning`.

**ServerHost_CreateApplication_EmptyRingAndUnreadableDatabase_RefusesToStartAndCreatesNoKey**: A database file is
created that opens and holds a table (so the schema is not created again) but whose `Repositories` view reads a table
that does not exist, so the start-up database preparation succeeds and the scan fails with a `SqliteException` on every
platform, and the key folder has no key (a theory without and with `DataProtection:AllowNewKeyRing`). The build fails
with the message "Key
ring '...' is empty and the database (Database:ConnectionString) could not be read to check whether it holds protected
secrets, so no key was created", which holds neither the location of the database nor the text of the exception and has
no inner exception, and no key file exists afterwards. This scenario is tested by
`ServerHost_CreateApplication_EmptyRingAndUnreadableDatabase_RefusesToStartAndCreatesNoKey`.

**ServerHost_CreateApplication_RingWithKeysAndUnreadableDatabase_StartsWithWarning**: A server is started once, so that
the ring has a key, and the database file is then replaced by one that opens but whose `Repositories` view reads a
table that does not exist. The second build succeeds
with the same single key and logs a warning that the database could not be read. This scenario is tested by
`ServerHost_CreateApplication_RingWithKeysAndUnreadableDatabase_StartsWithWarning`.

**ServerHost_CreateApplication_RestartWithSameRing_StartsWithoutComplaint**: A server is started, its secrets are
stored, and a second start over the same database and ring succeeds without a warning or an error about the secrets,
and the key folder still holds exactly one key file.
This scenario is tested by `ServerHost_CreateApplication_RestartWithSameRing_StartsWithoutComplaint`.

**ServerHost_CreateApplication_SomeSecretsUnreadable_LogsErrorAndStarts**: The database holds secrets that another
ring protected and the ring in the folder has a key; the build succeeds and an error is logged that gives the counts
per kind and the remedy. This scenario is tested by `ServerHost_CreateApplication_SomeSecretsUnreadable_LogsErrorAndStarts`.

**KeyRingSettings_AllowNewKeyRing_DefaultsToFalse**: The setting that accepts a new ring is off unless it is set. This
scenario is tested by `KeyRingSettings_AllowNewKeyRing_DefaultsToFalse`.

The registration scenario of the command-line verbs is `ServerHost_AddServices_ServicesOnly_CreatesNoKeyFile`, above:
only `AddServices` runs, so neither a scan nor the use of the ring takes place and no key file is created.

**ServerHost_CreateApplication_DpapiOnUnix_ThrowsClearError**: On Linux and macOS only (skipped on Windows), explicit
DPAPI is refused with a message that names `DataProtection:Protection`. This scenario is tested by
`ServerHost_CreateApplication_DpapiOnUnix_ThrowsClearError`.

**ServerHost_CreateApplication_PermissionsOnlyOnUnix_RestrictsTheFolder**: On Linux and macOS only (skipped on
Windows), the key ring is protected by permissions only and the server is started; the key folder has mode 0700. This
scenario is tested by `ServerHost_CreateApplication_PermissionsOnlyOnUnix_RestrictsTheFolder`.

**ServerHost_CreateApplication_PermissionsOnlyOnUnix_RestrictsTheKeyFiles**: On Linux and macOS only (skipped on
Windows), the key ring is protected by permissions only, the key folder starts with loose permissions, and the
server is started, which writes the key file; the key file has mode 0600. This scenario is tested by
`ServerHost_CreateApplication_PermissionsOnlyOnUnix_RestrictsTheKeyFiles`.

The choice of `PermissionsOnly` by the settings of the isolated hosts of the self-validation is verified with the
validation checks unit and is not a scenario of this unit.

**ServerHost_CreateApplication_NoAttestationSettings_CreatesSelfSignedKeyBesideTheDataProtectionKeys**: The pipeline
is started on a database folder with no attestation setting. The key file exists in the `attestation-selfsigned`
folder inside the `keys` folder. This scenario is tested by
`ServerHost_CreateApplication_NoAttestationSettings_CreatesSelfSignedKeyBesideTheDataProtectionKeys`.

**ServerHost_CreateApplication_NoAttestationSettings_SelfSignedKeyPersistsAcrossRestart**: The pipeline is started
twice on the same database folder with no attestation setting; the key (its thumbprint) is the same after the
restart. This scenario is tested by
`ServerHost_CreateApplication_NoAttestationSettings_SelfSignedKeyPersistsAcrossRestart`.

**ServerHost_CreateApplication_AttestationKeyOfLostRing_ThrowsNamingTheFile**: The server is started once, which creates
the protected attestation key, then the key ring folder is deleted and the server is started again. The start fails with
a message that names `attestation-selfsigned.pfx.protected` and says to delete the file, and the file is still there.
This scenario is tested by `ServerHost_CreateApplication_AttestationKeyOfLostRing_ThrowsNamingTheFile`. That the
services alone create no attestation key is shown by `ServerHost_AddServices_ServicesOnly_CreatesNoKeyFile`: the
default attestation folder is inside the key folder, which stays absent.

**ServerHost_CreateApplication_AttestationKeyOfLostRingWithAllowNewKeyRing_StillThrowsNamingTheFile**: The server is
started once with secrets in the database, then the key ring folder is deleted and the server is started again with
`DataProtection:AllowNewKeyRing` set to `true`. The setting accepts the loss of the stored secrets, but the start still
fails with a message that names `attestation-selfsigned.pfx.protected` and says to delete the file, and the file is
unchanged (the server never deletes or replaces a signing key). This scenario is tested by
`ServerHost_CreateApplication_AttestationKeyOfLostRingWithAllowNewKeyRing_StillThrowsNamingTheFile`.

**ServerHost_CreateApplication_SelfSignedFolderConfigured_KeepsTheKeyThere**: `Attestation:SelfSignedFolder` names a
folder; the key file is created there and the provider reports an available key. This scenario is tested by
`ServerHost_CreateApplication_SelfSignedFolderConfigured_KeepsTheKeyThere`.

**ServerHost_CreateApplication_FileAttestationKey_StartsAndWarnsAboutExpiry**: The File provider is chosen with a
protected PFX that ends in 30 days and its password. The application is created, the status of the key is available and
names the File provider, a warning that the certificate expires is logged and shown in the status, and the password is
not in any log entry. This scenario is tested by `ServerHost_CreateApplication_FileAttestationKey_StartsAndWarnsAboutExpiry`.

**ServerHost_CreateApplication_UnprotectedFileAttestationKey_RefusedWithRequireEncryptedKeys**: The key ring is
protected by a certificate and the attestation key is a PFX without a password. With
`DataProtection:RequireEncryptedKeys` the start fails, naming that setting and saying that the key is not protected by
a password. This scenario is tested by
`ServerHost_CreateApplication_UnprotectedFileAttestationKey_RefusedWithRequireEncryptedKeys`.

**ServerHost_CreateApplication_UnprotectedFileAttestationKey_StartsWithoutRequireEncryptedKeys**: The same key ring
and attestation key are used without `DataProtection:RequireEncryptedKeys`; the server starts and the attestation key
provider reports that it is available. This scenario is tested by
`ServerHost_CreateApplication_UnprotectedFileAttestationKey_StartsWithoutRequireEncryptedKeys`.

**ServerHost_CreateApplication_UnprotectedFileAttestationKey_WarnsWithoutRequireEncryptedKeys**: The same key ring
and attestation key are used without `DataProtection:RequireEncryptedKeys`; the start logs a warning that the key is
not protected by a password. This scenario is tested by
`ServerHost_CreateApplication_UnprotectedFileAttestationKey_WarnsWithoutRequireEncryptedKeys`.

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

**ServerHost_CreateApplication_InvalidTotpSettings_ThrowsClearError**: A theory over `Identity:Totp:Enabled` set to
`maybe`, an empty `Identity:Totp:Issuer`, and an issuer `a:b`, in `TotpOptionsTests.cs`; an
`InvalidOperationException` with the text "Invalid two-factor sign-in settings" that names `Identity:Totp:` and the
setting is thrown. This scenario is tested by `ServerHost_CreateApplication_InvalidTotpSettings_ThrowsClearError`,
described with the `TotpSignIn` unit.

**ServerHost_CreateApplication_ValidTotpSettings_ProvideTheSettings**: With no settings the host provides the settings
with the feature on and the issuer `ReviewCenter`; with the feature off and an issuer with spaces around it the
settings carry the feature off and the trimmed issuer, in `TotpOptionsTests.cs`. This scenario is tested by
`ServerHost_CreateApplication_ValidTotpSettings_ProvideTheSettings`.

**ServerHost_CreateApplication_IssuerWithSurroundingSpaces_ProvidesTheTrimmedIssuer**: With `Identity:Totp:Issuer` set
to `Acme` the settings that the service uses carry the issuer `Acme`, in `TotpOptionsTests.cs`. This scenario is
tested by `ServerHost_CreateApplication_IssuerWithSurroundingSpaces_ProvidesTheTrimmedIssuer`.

**ServerHost_CreateApplication_InvalidNotificationSettings_ThrowsClearErrorWithoutSecret**: A theory over valid enabled
SMTP settings (with the inline password `top-secret-value`) in which one value is replaced: `FromAddress` set to text
that is not an address, an empty `BaseUrl`, an empty `Smtp:Host`, `Smtp:Port` 70000, `DigestHour` 24, and `TimeZone` set
to an unknown zone. An `InvalidOperationException` with the text "Invalid notification settings" that names
`Notifications:` and the setting is thrown, and its message does not contain the password. This scenario is tested by
`ServerHost_CreateApplication_InvalidNotificationSettings_ThrowsClearErrorWithoutSecret`.

**ServerHost_CreateApplication_NotificationsOff_DoesNotCheckTheSettings**: One application is built with
notifications off and an invalid `FromAddress`, over a new database; it is built. This scenario is tested by
`ServerHost_CreateApplication_NotificationsOff_DoesNotCheckTheSettings`.

**ServerHost_CreateApplication_NotificationsOnAndValid_BuildsTheApplication**: One application is built with valid
enabled SMTP settings over a new database; it is built. This scenario is tested by
`ServerHost_CreateApplication_NotificationsOnAndValid_BuildsTheApplication`.

**ServerHost_CreateApplication_NotificationsOffOrValid_BuildsAndWarnsAboutInlinePassword**: One application is built
with valid enabled SMTP settings and an inline password, over a new database and a capturing log provider. Exactly one
warning that starts with "Notification settings" is logged, and it names `Notifications:Smtp:PasswordFile`. The name of
the test is kept from the time when it also checked the two cases above. This scenario is tested by
`ServerHost_CreateApplication_NotificationsOffOrValid_BuildsAndWarnsAboutInlinePassword`.

**ServerHost_CreateApplication_NotificationsInlinePassword_WarningHidesThePassword**: The same application is built
with an inline password; the one warning about the notification settings does not contain the password. This scenario
is tested by `ServerHost_CreateApplication_NotificationsInlinePassword_WarningHidesThePassword`.

**ServerHost_CreateApplication_DatabaseWithoutNotificationTables_FailsOnlyWhenNotificationsAreOn**: A database file
that holds one unrelated table, so that the server does not create the model, is used with valid enabled settings; an
`InvalidOperationException` whose message contains "no notification tables" is thrown. The name of the test is kept
from the time when it also checked the case below. This scenario is tested by
`ServerHost_CreateApplication_DatabaseWithoutNotificationTables_FailsOnlyWhenNotificationsAreOn`.

**ServerHost_CreateApplication_DatabaseWithoutNotificationTables_StartsWhenNotificationsAreOff**: The same database
file is used with notifications off; the application is built. This scenario is tested by
`ServerHost_CreateApplication_DatabaseWithoutNotificationTables_StartsWhenNotificationsAreOff`.

**ServerHost_AddServices_Configured_RegistersProjectAccessAsSingleton**: The real pipeline is started on an in-memory
server and `IProjectAccess` is taken from the application services twice; both calls return the same instance. This
scenario is tested by `ServerHost_AddServices_Configured_RegistersProjectAccessAsSingleton`.

**ServerHost_AddServices_Configured_RegistersAuditServicesAsSingletons**: The real pipeline is started on an in-memory
server; the `AuditAppender`, the `IAuditStore`, and the `AuditService` are each taken from the application services
twice, and each pair is the same instance. This scenario is tested by
`ServerHost_AddServices_Configured_RegistersAuditServicesAsSingletons`.

**ServerHost_AddServices_AuditTrailUnwritable_LogsWarning**: The audit store is replaced by one whose append always
fails, and `TryRecordAsync` of the audit service is called; a warning that says "An audit entry could not be recorded"
is logged by the `ServerHost` category. This scenario is tested by
`ServerHost_AddServices_AuditTrailUnwritable_LogsWarning`.

**ServerHost_AddServices_AuditTrailUnwritable_ReturnsFalseWithoutThrowing**: The audit store is replaced by one whose
append always fails, and `TryRecordAsync` of the audit service is called; it throws nothing and returns false. This
scenario is tested by `ServerHost_AddServices_AuditTrailUnwritable_ReturnsFalseWithoutThrowing`.

**ServerHost_AddServices_ServicesOnly_CreatesNoKeyFile**: Only `AddServices` is called, as the command-line verbs do,
with a key folder that does not exist; the `UserService` and the `AuditService` are resolved and used. Neither the key
folder nor a key file exists afterwards. This scenario is tested by
`ServerHost_AddServices_ServicesOnly_CreatesNoKeyFile`.

**ServerHost_CreateApplication_AnonymousVisitor_ProtectedPageRedirectsToLogin**: The real pipeline is started and
`/projects` is requested without signing in; the answer is a redirect whose location is the login page with the return
address `/projects`. This scenario is tested by
`ServerHost_CreateApplication_AnonymousVisitor_ProtectedPageRedirectsToLogin`.

**ServerHost_CreateApplication_AnonymousVisitor_HomeAndLoginPagesAreServed**: The real pipeline is started and `/` and
`/login` are requested without signing in; both are answered 200. This scenario is tested by
`ServerHost_CreateApplication_AnonymousVisitor_HomeAndLoginPagesAreServed`.

**ServerHost_CreateApplication_NoGitSettings_UsesDefaultTimeout**: The pipeline is built without a `Git:TimeoutSeconds`
setting; the git options hold the default timeout of 120 seconds. This scenario is tested by
`ServerHost_CreateApplication_NoGitSettings_UsesDefaultTimeout`.

**ServerHost_RunAsync_UnsupportedProvider_ReportsTheError**: `RunAsync` is called with an unsupported database provider
and a context that writes a log file; after the context is disposed, the log holds "Failed to prepare the database" and
the provider name. This scenario is tested by `ServerHost_RunAsync_UnsupportedProvider_ReportsTheError`.

**ServerHost_CreateApplication_TotpOn_CodeEndpointIsMapped**: The pipeline is built with the second factor on; the
endpoint of the second step of the sign-in is among the routes of the application. This scenario is tested by
`ServerHost_CreateApplication_TotpOn_CodeEndpointIsMapped`.

**ServerHost_CreateApplication_TotpOff_CodeEndpointIsNotMapped**: The pipeline is built with `Identity:Totp:Enabled`
false; the endpoint of the second step of the sign-in is not among the routes. This scenario is tested by
`ServerHost_CreateApplication_TotpOff_CodeEndpointIsNotMapped`.

**Notification worker scenarios**: The tests of the worker registration of the composition root are in
`ServerHostNotificationWorkerTests.cs`. They build the real application with an in-memory server and look at the hosted
services in its container. The application has the worker when notifications are enabled with the file sender or with
SMTP, which is tested by `ServerHost_CreateApplication_NotificationsFile_HasWorker` and
`ServerHost_CreateApplication_NotificationsSmtp_HasWorker`; it has none while notifications are off, which is tested by
`ServerHost_CreateApplication_NotificationsOff_NoWorker`; enabled notifications without a transport are refused at
start-up, so no worker exists, which is tested by `ServerHost_CreateApplication_EnabledSenderNone_RefusedBeforeAnyWorker`.
The shared service registration of the commands adds no hosted service even when notifications are enabled, which is
tested by `ServerHost_AddServices_NotificationsEnabled_NoHostedService`, and the host of the self-test start check has no
worker, which is tested by `ServerHost_CreateApplication_SelfTestSettings_NoWorker`. The worker runs while the host runs
and has stopped when the host has stopped, which is tested by `ServerHost_RunAsync_Stop_WorkerStopped`.

**Audit services**: The registration of the audit store, the shared audit appender, and the audit service in
`AddServices` is exercised by the sign-in audit tests and the `AuditCommand` tests, which resolve them from the real
pipeline; there is no separate test of the registration.

**Scenarios whose assertions serve several requirements**: The following scenarios have one Act whose failure or
result is seen from several sides, so one test links to each requirement that it asserts. The assertion that serves
each requirement is:

- `ServerHost_CreateApplication_StartupLogging_EmitsStructuredEntry`: the one start-up entry of information level
  serves the start-up log entry requirement, and the named URL value of that entry serves the requirement for the
  listen URL.
- `ServerHost_CreateApplication_InvalidKeyRingSettings_ThrowsClearError`: the exception that names the setting serves
  the requirement for invalid key ring settings, and the absence of a key file afterwards serves the requirement that
  a failed check creates no key.
- `ServerHost_CreateApplication_WrongCertificateForTheRing_ThrowsClearErrorWithoutSecondKey`: the message that names
  the two settings serves the wrong certificate requirement, and the absence of a second key file serves the
  requirement that a failed check creates no key.
- `ServerHost_CreateApplication_KeyRingFolderNotWritable_ThrowsClearError`: the exception serves the requirement that
  names `DataProtection:KeysPath`, and the folder in its message serves the requirement that names the folder.
- `ServerHost_CreateApplication_InvalidNotificationSettings_ThrowsClearErrorWithoutSecret`: the exception serves the
  requirement that an invalid setting stops the start, the setting in its message serves the requirement that names the
  setting, and the check that the SMTP password is absent from the message serves the requirement that hides it.
- `ServerHost_CreateApplication_AttestationKeyOfLostRing_ThrowsNamingTheFile`: the exception serves the requirement
  that the start fails, the file name and the advice to delete it serve the requirement that names the file, and the
  file that is still there serves the requirement that the key file is kept; the variant with
  `DataProtection:AllowNewKeyRing` shows the same error and the file in place for the requirement about that setting.
- `ServerHost_CreateApplication_LostRingWithAllowNewKeyRing_StartsWithWarning`: the successful build serves the
  requirement that the start goes on with a new key, and the warning with the setting and the count serves the warning
  requirement.
- `ServerHost_CreateApplication_FreshInstallation_StartsAndCreatesRing`: the successful build and the one key serve the
  requirement for a fresh installation, and the log without an error entry serves the requirement that no error is
  logged.
- `ServerHost_CreateApplication_RestartWithSameRing_StartsWithoutComplaint`: the quiet start serves the requirement
  that the start is without complaint, and the key folder that still holds exactly one key file serves the requirement
  that no second key is created.
- `ServerHost_CreateApplication_EmptyRingAndUnreadableDatabase_RefusesToStartAndCreatesNoKey`: the message that names
  the folder and `Database:ConnectionString` serves the requirement for the refusal, the absence of the database
  location, of the exception text and of an inner exception serves the sanitizing requirement, and the empty key folder
  afterwards serves the requirement that no key file is created.
- `ServerHost_CreateApplication_RingWithKeysAndUnreadableDatabase_StartsWithWarning`: the successful build serves the
  requirement that the start goes on, and the warning that the database could not be read serves the warning
  requirement.
- `ServerHost_CreateApplication_SomeSecretsUnreadable_LogsErrorAndStarts`: the successful build serves the requirement
  that the start goes on, and the error with the counts and the remedy serves the requirement for the error entry.
- `ServerHost_CreateApplication_HealthRoute_Returns200Healthy`: see above; the 200 status serves both the health route
  requirement and the requirement that the route needs no sign-in, because the request carries no credentials.
