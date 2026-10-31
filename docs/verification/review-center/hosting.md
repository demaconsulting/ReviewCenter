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
- The health endpoint answers HTTP status 200 with the body `Healthy`.
- The health route and the home route are both registered and answer.
- Start-up writes a structured log entry with a named property, and framework logging reaches
  the same logger providers.
- The database is created for a configured location, the stored data is served again by a second host on the
  same database (to a signed-in user), and an unsupported database provider ends the run with exit code 1.
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

### Test Scenarios

**HostingSubsystem_HealthEndpoint_Requested_ReturnsHealthy**: The host is started in memory and
`/health` is requested; the response has a success status and the body `Healthy`. This scenario
is tested by `HostingSubsystem_HealthEndpoint_Requested_ReturnsHealthy`.

**HostingSubsystem_Startup_LoggingConfigured_EmitsStructuredEntry**: The host is started with a
capturing logger provider and one request is made; the application's start-up entry carries a
named property, and entries from framework components reach the same provider. This scenario is
tested by `HostingSubsystem_Startup_LoggingConfigured_EmitsStructuredEntry`.

**HostingSubsystem_Routes_BothRegistered_HomeAndHealthAnswer**: The host is started in memory
and both `/` and `/health` are requested; both answer HTTP status 200. This scenario is tested
by `HostingSubsystem_Routes_BothRegistered_HomeAndHealthAnswer`.

**HostingSubsystem_Startup_DatabaseConfigured_ServesStoredProjectsAfterRestart**: A host is started on a new
database file and a project is stored through its services; the host is stopped and a second host is started on
the same file. A user signed in to the second host requests `/projects`, which lists the stored project. This scenario
is tested by
`HostingSubsystem_Startup_DatabaseConfigured_ServesStoredProjectsAfterRestart`.

**HostingSubsystem_Startup_UnsupportedDatabaseProvider_FailsWithExitCodeOne**: The server is run through
`ServerHost.RunAsync` with an unsupported database provider; it returns 1 and the context records the failure,
and no unhandled exception escapes. This scenario is tested by
`HostingSubsystem_Startup_UnsupportedDatabaseProvider_FailsWithExitCodeOne`.

**Repository services and the key ring**: The registration of the repository store, the git source, and the token
protector, the default folders beside the database, the persistence of the key ring so that a token stays readable after
a restart, and the clear error for a key ring folder that cannot be written are verified by the unit tests of
`ServerHost`: `ServerHost_AddServices_Configured_RegistersRepositoryServicesWithDefaultFolders`,
`ServerHost_Restart_KeyRingPersisted_TokenStaysReadable`,
`ServerHost_CreateApplication_KeyRingFolderNotWritable_ThrowsClearError`, and
`ServerHost_ResolveKeysPath_ConfiguredOrDefault_ChoosesTheFolder`. The protection of the key ring at rest and its
start-up refusals are verified by the unit tests of `KeyRingProtection` and by
`ServerHost_Restart_CertificateKeyRing_ProtectedValueStaysReadable`,
`ServerHost_CreateApplication_InvalidKeyRingSettings_ThrowsClearError`, and
`ServerHost_CreateApplication_WrongCertificateForTheRing_ThrowsClearErrorWithoutSecondKey`. The refusal of a lost key
ring and its escape hatch are verified by `ServerHost_CreateApplication_LostRing_RefusesToStartAndCreatesNoKey` and
`ServerHost_CreateApplication_LostRingWithAllowNewKeyRing_StartsWithWarning`, and the scan that feeds them by the unit
tests of `SecretScan`. The registration of the
review planner and of the parser of the review-set file is verified by
`ServerHost_AddServices_Configured_RegistersReviewPlannerAndParser`.

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
is verified by the unit test of `ServerHost`, `ServerHost_CreateApplication_ApiRoutes_AreMapped`; the behavior of the
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
`ServerHost_CreateApplication_OidcOffOrValid_BuildsTheApplication`; both are unit tests of `ServerHost`. With the
provider off, its endpoints answer 404, which is tested by `OidcSignIn_Disabled_EndpointsAreNotMappedAndNoButtonIsShown`
(see the `OidcSignIn` verification).

**Second factor settings**: Starting the real pipeline with an invalid setting of the second factor throws an
`InvalidOperationException` that names the setting, which is tested by
`ServerHost_CreateApplication_InvalidTotpSettings_ThrowsClearError`; valid settings are provided to the services, which
is tested by `ServerHost_CreateApplication_ValidTotpSettings_ProvideTheSettings`; both are unit tests of `ServerHost`.
