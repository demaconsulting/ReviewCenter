## Hosting

### Verification Approach

The `Hosting` subsystem is verified by integration tests defined in `HostingSubsystemTests.cs`.
Each test starts the real server pipeline, built by `ServerHost.CreateApplication`, on an
in-memory test server and exercises it over HTTP, so no port is bound and nothing is mocked.
A capturing logger provider, attached through `ServerHostOptions`, makes logged entries
observable. The `Web` subsystem components are real, so the tests also confirm that `Hosting`
and `Web` work together.

### Test Environment

N/A - standard test environment.

### Acceptance Criteria

- All integration tests pass with zero failures.
- The health endpoint answers HTTP status 200 with the body `Healthy`, also to a visitor who is not signed in.
- A signed-in user is served a protected page.
- The health route and the home route are both registered and answer.
- Start-up writes a structured log entry with a named property, and framework logging reaches
  the same logger providers.
- The database is created at the configured location, the domain services resolve from it, the stored data is served
  again by a second host on the same database (to a signed-in user), an unsupported database provider ends the run
  with exit code 1 and an error that names it, and an anonymous visitor is sent from a protected page to the sign-in
  page while the sign-in page itself is public.
- The repository store, the project access rules, and the git source are registered, and a stored token stays readable
  after a restart because
  the key ring is persisted; the behavior is verified in the unit tests of `ServerHost`. The project access rules have
  no registration test of their own: every project-scoped route of the `Api` subsystem reaches them, which
  `ApiProjectAccess_EveryProjectRoute_NonMemberToken_AnswersLikeAMissingProject` shows.
- The routes of the `Api` subsystem and the personal access token service are registered, and an unknown address under
  `/api` is answered with a JSON problem document; the behavior is verified in the unit tests of `ServerHost`.
- Optional sign-in with an OpenID Connect provider: invalid settings stop the start-up with an error that names the
  setting and never shows the secret, and the server starts when the provider is off or its settings are valid; the
  behavior is verified in the unit tests of `ServerHost`.
- Second factor settings: an invalid setting stops the start-up with an error that names it, and valid settings are
  provided to the services; the behavior is verified in the unit tests of `ServerHost`.
- Notification settings: while notifications are on, an invalid setting or a database without the notification tables
  stops the start-up with an error that names the problem and never shows the SMTP password, an inline password is
  logged as a warning, and valid settings build the application; while they are off nothing is checked; the behavior is
  verified in the unit tests of `ServerHost`.
- Notification worker: the web host runs the notification worker only while notifications are enabled with a transport;
  with notifications off, in a command-line run, and in the self-test there is none; the behavior is verified in the
  unit tests of `ServerHost` and in `HostingSubsystem_NotificationWorker_OnlyEnabledWebHost`.

### Test Scenarios

**HostingSubsystem_HealthEndpoint_Requested_ReturnsHealthy**: The host is started in memory and
`/health` is requested; the response has a success status and the body `Healthy`. This scenario
is tested by `HostingSubsystem_HealthEndpoint_Requested_ReturnsHealthy`.

**HostingSubsystem_Startup_LoggingConfigured_EmitsStructuredEntry**: The host is started with a
capturing logger provider; the application's start-up entry carries a named property. This scenario is
tested by `HostingSubsystem_Startup_LoggingConfigured_EmitsStructuredEntry`.

**HostingSubsystem_Startup_LoggingConfigured_FrameworkEntriesReachTheSameSinks**: The host is started with a
capturing logger provider and one request is made; entries from framework components reach the same provider. This
scenario is tested by `HostingSubsystem_Startup_LoggingConfigured_FrameworkEntriesReachTheSameSinks`.

**HostingSubsystem_Routes_BothRegistered_HomeAndHealthAnswer**: The host is started in memory
and both `/` and `/health` are requested; both answer HTTP status 200. This scenario is tested
by `HostingSubsystem_Routes_BothRegistered_HomeAndHealthAnswer`.

**HostingSubsystem_Startup_DatabaseConfigured_ServesStoredProjectsAfterRestart**: A host is started on a new
database file and a project is stored through its services; the host is stopped and a second host is started on
the same file. A user signed in to the second host requests `/projects`, which lists the stored project. This scenario
is tested by
`HostingSubsystem_Startup_DatabaseConfigured_ServesStoredProjectsAfterRestart`.

**HostingSubsystem_Startup_UnsupportedDatabaseProvider_FailsWithExitCodeOne**: The server is run through
`ServerHost.RunAsync` with an unsupported database provider; it returns 1, the context records the failure, and no
unhandled exception escapes. This scenario is tested by
`HostingSubsystem_Startup_UnsupportedDatabaseProvider_FailsWithExitCodeOne`.

**HostingSubsystem_Startup_UnsupportedDatabaseProvider_ReportsAnErrorNamingTheProvider**: The server is run through
`ServerHost.RunAsync` with an unsupported database provider and a log file; the log names the provider. This scenario
is tested by `HostingSubsystem_Startup_UnsupportedDatabaseProvider_ReportsAnErrorNamingTheProvider`.

**HostingSubsystem_Startup_DatabaseConfigured_CreatesTheDatabaseFile**: A host is started on a connection string that
points to a database file that does not exist; the file is created at the configured location. This scenario is tested
by `HostingSubsystem_Startup_DatabaseConfigured_CreatesTheDatabaseFile`.

**HostingSubsystem_Startup_DatabaseConfigured_DomainServicesResolve**: A host is started on a new database and the
project, review, user, signing, and inbox services are resolved from its service provider; each resolves to an
instance. This scenario is tested by `HostingSubsystem_Startup_DatabaseConfigured_DomainServicesResolve`.

**HostingSubsystem_Pages_AnonymousVisitor_ProtectedPageRedirectsToLogin**: A host is started in memory and the client,
which follows no redirect, requests `/projects` without signing in; the answer is a redirect to the sign-in page. This
scenario is tested by `HostingSubsystem_Pages_AnonymousVisitor_ProtectedPageRedirectsToLogin`.

**HostingSubsystem_HealthEndpoint_NotSignedIn_IsNotRedirectedToLogin**: A host is started in memory and the client,
which has no sign-in and follows no redirect, requests `/health`; the answer has HTTP status 200 and no redirect
location. This scenario is tested by `HostingSubsystem_HealthEndpoint_NotSignedIn_IsNotRedirectedToLogin`.

**HostingSubsystem_Pages_SignedInUser_ProtectedPageIsServed**: A host is started in memory and a user signs in; the
user requests `/projects` and the answer has HTTP status 200 with no redirect location. This scenario is tested by
`HostingSubsystem_Pages_SignedInUser_ProtectedPageIsServed`. The restart test signs in only as setup and is not
evidence for this criterion.

**HostingSubsystem_Pages_AnonymousVisitor_LoginPageIsServed**: A host is started in memory and the client requests
`/login` without signing in; the answer has HTTP status 200. This scenario is tested by
`HostingSubsystem_Pages_AnonymousVisitor_LoginPageIsServed`.

**Repository services and the key ring**: The registration of the repository store, the git source, and the token
protector, the default folders beside the database, the persistence of the key ring so that a token stays readable after
a restart, and the clear error for a key ring folder that cannot be written are verified by the unit tests of
`ServerHost`: `ServerHost_AddServices_Configured_RegistersRepositoryServicesWithDefaultFolders`,
`ServerHost_AddServices_Configured_RegistersRepositoryServiceAsSingleton`,
`ServerHost_AddServices_Configured_RegistersGitSource`,
`ServerHost_AddServices_Configured_CredentialProtectorRoundTripsAToken`,
`ServerHost_AddServices_Configured_PlacesTheKeyRingBesideTheDatabase`,
`ServerHost_ResolveKeysPath_Configured_ChoosesTheConfiguredFolder`,
`ServerHost_Restart_KeyRingPersisted_TokenStaysReadable`,
`ServerHost_CreateApplication_KeyRingFolderNotWritable_ThrowsClearError`, and
`ServerHost_ResolveKeysPath_ConfiguredOrDefault_ChoosesTheFolder`. The protection of the key ring at rest and its
start-up refusals are verified by the unit tests of `KeyRingProtection` and by
`ServerHost_Restart_CertificateKeyRing_ProtectedValueStaysReadable`,
`ServerHost_CreateApplication_InvalidKeyRingSettings_ThrowsClearError`, and
`ServerHost_CreateApplication_WrongCertificateForTheRing_ThrowsClearErrorWithoutSecondKey`. The refusal of a lost key
ring and its escape hatch are verified by `ServerHost_CreateApplication_LostRing_RefusesToStartAndCreatesNoKey`,
`ServerHost_CreateApplication_LostRing_CreatesNoKey`, `ServerHost_CreateApplication_LostRing_ErrorShowsNoSecret`, and
`ServerHost_CreateApplication_LostRingWithAllowNewKeyRing_StartsWithWarning`, and the scan that feeds them by the unit
tests of `SecretScan`. The registration of the
review planner and of the parser of the review-set file is verified by
`ServerHost_AddServices_Configured_RegistersReviewPlannerAndParser` and
`ServerHost_AddServices_Configured_RegistersReviewSetParserAsSingleton`.

**Attestation services and the start-up check**: Starting the real pipeline with no attestation setting creates the
self-signed key in an `attestation-selfsigned` folder beside the Data Protection keys and keeps the same key across a
restart, which is tested by
`ServerHost_CreateApplication_NoAttestationSettings_CreatesSelfSignedKeyBesideTheDataProtectionKeys`; a key file that
the key ring cannot decrypt stops the start naming the file, which is tested by
`ServerHost_CreateApplication_AttestationKeyOfLostRing_ThrowsNamingTheFile`; a chosen folder is
used, which is tested by `ServerHost_CreateApplication_SelfSignedFolderConfigured_KeepsTheKeyThere`. Each setting that
cannot work stops the start with a message that names it, which is tested by
`ServerHost_CreateApplication_UnknownAttestationProvider_ThrowsClearError`,
`ServerHost_CreateApplication_CertificateStoreWithoutThumbprint_ThrowsClearError`,
`ServerHost_CreateApplication_CertificateNotInStore_ThrowsClearError`,
`ServerHost_CreateApplication_RequireTrustedKeyWithSelfSignedProvider_ThrowsClearError`, and
`ServerHost_CreateApplication_SelfSignedFolderIsAFile_ThrowsClearError`. The registration of the certificate store
provider is verified by `CertificateStoreKeyProvider_GetSigningKey_ConfiguredForTheServer_StartsWithTheCertificate`,
which skips when the store is unavailable.

**API registration**: The routes of the API under `/api` are mapped, the personal access token service is registered once,
the OpenAPI document is served without a token, and an unknown address under `/api` is a JSON 404 instead of a page. This
is verified by the unit tests of `ServerHost`, `ServerHost_CreateApplication_ApiRoutes_AreMapped`,
`ServerHost_CreateApplication_ApiRegistration_RegistersTokenServiceAsSingleton`,
`ServerHost_CreateApplication_ApiRoutes_WithoutTokenAreUnauthorized`,
`ServerHost_CreateApplication_OpenApiDocument_IsServedWithoutToken`, and
`ServerHost_CreateApplication_UnknownApiAddress_ReturnsJsonProblem404`; the behavior of the
routes is verified in the `Api` subsystem.

**Audit verify verb**: The `AuditCommand` unit tests run the verb over a real database. An intact trail gives the
entry count and exit code 0, which is tested by `AuditCommand_Run_IntactTrail_ReportsEntryCountWithExitCodeZero`, and
a tampered entry gives the first bad entry and exit code 1, which is tested by
`AuditCommand_Run_TamperedEntry_ReportsFirstBadEntryWithExitCodeOne`. The registration of the audit services in
`ServerHost.AddServices` is exercised by the sign-in audit tests, not by a separate hosting test.

**Keys reprotect verb**: The `KeysCommand` unit tests run the verb over a real database and a real key folder. After a
run every secret is still readable and a new key is the only one that stored payloads reference, which is tested by
`KeysCommand_Run_SecretsOfTheRing_AreStillReadableAfterwards` and
`KeysCommand_Run_NewKey_IsTheOnlyOneReferencedAndOldKeyIsReported`; a secret that cannot be read makes the command
refuse with exit code 1 and no change, which is tested by `KeysCommand_Run_UnreadableSecret_RefusesAndChangesNothing`.

**OpenID Connect settings**: Starting the real pipeline with sign-in with a provider enabled and an invalid setting
throws an `InvalidOperationException` that names the setting and does not contain the client secret, which is tested
by `ServerHost_CreateApplication_InvalidOidcSettings_ThrowsClearErrorWithoutSecret`. With the provider off, or on with
valid settings, the application is built, which is tested by
`ServerHost_CreateApplication_OidcOffOrValid_BuildsTheApplication`, with the provider off by
`ServerHost_CreateApplication_OidcOff_BuildsTheApplication`, and with an http loopback authority by
`ServerHost_CreateApplication_OidcLoopbackHttpAuthority_BuildsTheApplication`; all are unit tests of `ServerHost`. With
the provider off, its endpoints answer 404, which is tested by
`OidcSignIn_Disabled_EndpointsAreNotMappedAndNoButtonIsShown` (see the `OidcSignIn` verification).

**Second factor settings**: Starting the real pipeline with an invalid setting of the second factor throws an
`InvalidOperationException` that names the setting, which is tested by
`ServerHost_CreateApplication_InvalidTotpSettings_ThrowsClearError`; valid settings are provided to the services, which
is tested by `ServerHost_CreateApplication_ValidTotpSettings_ProvideTheSettings`; both are unit tests of `ServerHost`.

**Notification settings**: Starting the real pipeline with notifications on and an invalid setting throws an
`InvalidOperationException` that says the notification settings are invalid, names the setting, and does not contain the
password, which is tested by `ServerHost_CreateApplication_InvalidNotificationSettings_ThrowsClearErrorWithoutSecret`.
With notifications off and an invalid address the application is built, and with valid settings and an inline password
it is built and logs a warning that names the password file setting and does not contain the password, which is tested
by `ServerHost_CreateApplication_NotificationsOffOrValid_BuildsAndWarnsAboutInlinePassword`. A database that already has
a table but no notification tables stops the start while notifications are on and does not while they are off, which is
tested by `ServerHost_CreateApplication_DatabaseWithoutNotificationTables_FailsOnlyWhenNotificationsAreOn`; all three
are unit tests of `ServerHost`. Each of these outcomes also has a test of its own:
`ServerHost_CreateApplication_NotificationsOnAndValid_BuildsTheApplication`,
`ServerHost_CreateApplication_NotificationsInlinePassword_WarningHidesThePassword`,
`ServerHost_CreateApplication_NotificationsOff_DoesNotCheckTheSettings`, and
`ServerHost_CreateApplication_DatabaseWithoutNotificationTables_StartsWhenNotificationsAreOff`.

**Notification worker**: The running web host with notifications enabled has the worker and the worker is running, which
is tested by `HostingSubsystem_NotificationWorker_OnlyEnabledWebHost`, and the same host with notifications off has
none, which is tested by `HostingSubsystem_NotificationWorker_NotEnabled_WebHostHasNoWorker`.
That a command and the self-test get no worker, that an enabled transport `None` is refused, and that the worker stops
with the host are tested by the unit tests of `ServerHost` in `ServerHostNotificationWorkerTests.cs`.
