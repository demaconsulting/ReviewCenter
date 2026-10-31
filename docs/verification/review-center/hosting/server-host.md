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
- Invalid settings of sign-in with an OpenID Connect provider (a missing or insecure authority, a bad client
  identifier, scopes without `openid`, a long display name, a bad user name claim, an age outside 10 to 900 seconds, or
  a control character in the secret) stop the start with an error that names the `Identity:Oidc` setting and never
  contains the value or the secret; with the provider off nothing is checked, and valid settings (https, or http on a
  loopback host) build the application. With the provider off the endpoints and the callback are not mapped.
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
  no key created, with or without the setting), and only logs a warning when the ring has keys. `user add` and
  `audit verify` create no key file.
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
  feature on and the issuer `ReviewCenter` by default, and a switched-off feature with a trimmed issuer when set.

#### Test Scenarios

**ServerHost_CreateApplication_IsolatedSettings_IgnoresPoisonedEnvironment**: The process environment is poisoned
(listen address, Kestrel endpoint, environment name, database settings and sentinel paths) and an isolated host is
started; it listens on one loopback address that is not the poisoned port, reports the `Production` environment,
creates its database only in the temporary folder, and touches nothing in the sentinel folder. This scenario is
tested by `ServerHost_CreateApplication_IsolatedSettings_IgnoresPoisonedEnvironment`.

**ServerHost_CreateApplication_WithoutIsolation_AmbientEndpointWins**: The same poisoned environment and a normal host;
the host listens on the poisoned port. This proves that the poison above is effective, so the isolated test cannot pass
by accident. This scenario is tested by `ServerHost_CreateApplication_WithoutIsolation_AmbientEndpointWins`.

**ServerHost_CreateApplication_IsolatedSettings_ConstrainedRoutesWork**: An isolated host is started and
`/api/projects/abc/reviews` and `/api/projects/1/reviews` are requested; the first is 404 (the constraint applies) and
the second is 401 (the route is reached without a token). This shows that the empty builder has the full routing. This
scenario is tested by `ServerHost_CreateApplication_IsolatedSettings_ConstrainedRoutesWork`.

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

**ServerHost_CreateApplication_InvalidOidcSettings_ThrowsClearErrorWithoutSecret**: A theory; with
`Identity:Oidc:Enabled` true and valid settings otherwise (and a secret), the pipeline is built with one invalid value:
an empty, http, malformed, or query-carrying authority, an empty or space-containing client identifier, scopes without
`openid`, a display name of 51 characters, a user name claim with a space, an age of 5 or 901 seconds, an empty or
white-space client secret, or a control character in the client secret. An `InvalidOperationException` that says
"Invalid OpenID Connect sign-in settings" and
names the setting is thrown, and its message never holds the secret. This scenario is tested by
`ServerHost_CreateApplication_InvalidOidcSettings_ThrowsClearErrorWithoutSecret`.

**ServerHost_CreateApplication_OidcOffOrValid_BuildsTheApplication**: The pipeline is built with the provider off and a
garbage authority (not read), with an https authority, and with an http loopback authority and the largest age of 900
seconds; all three are built. This scenario is tested by `ServerHost_CreateApplication_OidcOffOrValid_BuildsTheApplication`.

**OidcSignIn_Disabled_EndpointsAreNotMappedAndNoButtonIsShown**: With the provider off, the endpoints of the sign-in
and the callback address `/signin-oidc` are answered like an unknown address, and the login page shows no button. This
scenario is tested by `OidcSignIn_Disabled_EndpointsAreNotMappedAndNoButtonIsShown`, defined in
`OidcSignInTests.cs` and described with the `OidcSignIn` unit.

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

**ServerHost_Restart_CertificateKeyRing_ProtectedValueStaysReadable**: An application is built with
`DataProtection:Protection` Auto and a certificate in a temporary folder; a value is protected, the application is
disposed, and a second application over the same key ring unprotects it. The key file holds an encrypted secret. This
scenario is tested by `ServerHost_Restart_CertificateKeyRing_ProtectedValueStaysReadable`.

**ServerHost_CreateApplication_InvalidKeyRingSettings_ThrowsClearError**: An unknown protection, an unknown DPAPI
scope, and a certificate protection without a certificate each stop the build with an `InvalidOperationException` that
starts "Invalid key ring settings:" and names the setting. This scenario is tested by
`ServerHost_CreateApplication_InvalidKeyRingSettings_ThrowsClearError`.

**ServerHost_CreateApplication_MissingOrWrongPasswordCertificate_ThrowsClearErrorWithoutPassword**: A missing
certificate file and a wrong password each stop the build with a message that names
`DataProtection:CertificatePath` and does not contain the password. This scenario is tested by
`ServerHost_CreateApplication_MissingOrWrongPasswordCertificate_ThrowsClearErrorWithoutPassword`.

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
key folder are started; the build succeeds and the ring now holds a key. This scenario is tested by
`ServerHost_CreateApplication_FreshInstallation_StartsAndCreatesRing`.

**ServerHost_CreateApplication_LostRing_RefusesToStartAndCreatesNoKey**: A database holds a repository token and a
second-factor secret that a ring protected, and the key folder is empty or has been removed (a theory with both
cases). The build fails with the message "Key ring '...' is empty but the database holds 2 protected secrets" that names
`DataProtection:AllowNewKeyRing`, and no key file exists afterwards. This scenario is tested by
`ServerHost_CreateApplication_LostRing_RefusesToStartAndCreatesNoKey`.

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
stored, and a second start over the same database and ring succeeds without a warning or an error about the secrets.
This scenario is tested by `ServerHost_CreateApplication_RestartWithSameRing_StartsWithoutComplaint`.

**ServerHost_CreateApplication_SomeSecretsUnreadable_LogsErrorAndStarts**: The database holds secrets that another
ring protected and the ring in the folder has a key; the build succeeds and an error is logged that gives the counts
per kind and the remedy. This scenario is tested by `ServerHost_CreateApplication_SomeSecretsUnreadable_LogsErrorAndStarts`.

**KeyRingSettings_AllowNewKeyRing_DefaultsToFalse**: The setting that accepts a new ring is off unless it is set. This
scenario is tested by `KeyRingSettings_AllowNewKeyRing_DefaultsToFalse`.

**Command-line verbs and keys**: `user add` and `audit verify` run against a database and a key folder; no key file is
created, so neither verb scans nor uses the ring. This scenario is tested by `UserCommand_Run_ValidUser_CreatesNoKeyFile`
and `AuditCommand_Run_IntactTrail_CreatesNoKeyFile`.

**ServerHost_CreateApplication_DpapiOnUnix_ThrowsClearError** and
**ServerHost_CreateApplication_PermissionsOnlyOnUnix_RestrictsFolderAndFiles**: On Linux and macOS only (skipped on
Windows), explicit DPAPI is refused with a message that names `DataProtection:Protection`, and the key folder gets mode
0700 and the key files mode 0600. These scenarios are tested by the tests of the same names.

**SelfTestServices_CreateSettings_ChoosesPermissionsOnlyProtection**: The settings of the isolated hosts of the
self-validation choose `PermissionsOnly`, so that they never use the key store of the operating system. This scenario
is tested by `SelfTestServices_CreateSettings_ChoosesPermissionsOnlyProtection`.

**ServerHost_CreateApplication_NoAttestationSettings_CreatesSelfSignedKeyBesideTheDataProtectionKeys**: The pipeline
is started twice on the same database folder with no attestation setting. The key file exists in the
`attestation-selfsigned` folder inside the `keys` folder, and the key (its thumbprint) is the same after the restart.
This scenario is tested by
`ServerHost_CreateApplication_NoAttestationSettings_CreatesSelfSignedKeyBesideTheDataProtectionKeys`.

**ServerHost_CreateApplication_AttestationKeyOfLostRing_ThrowsNamingTheFile**: The server is started once, which creates
the protected attestation key, then the key ring folder is deleted and the server is started again. The start fails with
a message that names `attestation-selfsigned.pfx.protected` and says to delete the file, and the file is still there.
This scenario is tested by `ServerHost_CreateApplication_AttestationKeyOfLostRing_ThrowsNamingTheFile`. That `user add`
and `audit verify` create no attestation key is shown by `UserCommand_Run_ValidUser_CreatesNoKeyFile` and
`AuditCommand_Run_IntactTrail_CreatesNoKeyFile`: the default attestation folder is inside the key folder, which stays
absent.

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

**ServerHost_CreateApplication_UnprotectedFileAttestationKey_RefusedOnlyWithRequireEncryptedKeys**: The key ring is
protected by a certificate and the attestation key is a PFX without a password. With `DataProtection:RequireEncryptedKeys`
the start fails, naming that setting and saying that the key is not protected by a password. Without it, the server
starts and logs the warning. This scenario is tested by
`ServerHost_CreateApplication_UnprotectedFileAttestationKey_RefusedOnlyWithRequireEncryptedKeys`.

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

**Audit services**: The registration of the audit store, the shared audit appender, and the audit service in
`AddServices` is exercised by the sign-in audit tests and the `AuditCommand` tests, which resolve them from the real
pipeline; there is no separate test of the registration.
