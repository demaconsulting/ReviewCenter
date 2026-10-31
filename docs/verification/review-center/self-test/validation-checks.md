### ValidationChecks

#### Verification Approach

The self-validation checks are verified with unit tests defined in `ValidationChecksTests.cs`, and
the whole of them by the real default validation, which is run once in the test process
(`RealValidationRun`) and once as a separate process (`IntegrationTests.cs`). Each individual
check is exercised at most once by the suite, because the real checks are the slow part of the
suite (they start git, a database, a signer and a web host):

- The light checks (`Fingerprint`, `PasswordHash`, `Totp`) are run directly.
- The checks that start git, a complete review workflow, a signer, or the web host, and the `AuditChain` check, are
  verified by the real run, whose log and results file must show each of them passed with the detail it
  reports (for example `review signed by a separate reviewer` for the review workflow and `stopped cleanly` for the
  host).
- The real run is started once, eagerly, at the start of the test assembly (`RealValidationRunFixture`), so
  the slow checks run while the rest of the suite runs; it exists twice: in the test process and as a child process
  of a copy of the build output in a hostile environment (see below).
- The hermetic start of the host is verified by a child process that runs a copy of the build output with a broken
  `appsettings.json` beside the program and in the working directory, a poisoned `ASPNETCORE_URLS`, Kestrel
  endpoint variable, `DOTNET_ENVIRONMENT` and `Database__*` settings, sentinel paths, and a private
  `TEMP`/`TMP`: the run must pass, the host must bind a loopback port that is not the poisoned one, the sentinels must
  not be created, and the private temporary folder must be empty afterwards.
- The opt-in `ConfiguredDatabase` and `ConfiguredKeys` checks are run directly against temporary databases and key
  rings that the test creates with the production schema and the production protectors (in-memory certificates; no
  DPAPI, certificate store, or home directory), and against the process configuration, which the test sets
  through the `Database__*` and `DataProtection__*` environment variables and
  restores afterwards.

No check is replaced by a test double: the production services, the production git source, the
production renderer and signer, and the production web host are what the checks call. The
independent verification inside the checks (the pinned fingerprint literal, the CMS signature
verified with `System.Security.Cryptography.Pkcs`) is part of the code under test.

#### Test Environment

`git` must be on the path, as for the tool itself. The tests need the loopback interface, which
every supported platform provides, and no network beyond it.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The real default run reports nine passed tests, with elapsed times, and none skipped or failed.
- The `Fingerprint` check passes and compares with the literal
  `13e028da06299fcedffff47b2b55cca46a68111f3124d77d355364339c4215f2`.
- The `PasswordHash` check accepts the right password and rejects a wrong one.
- The `Totp` check passes with the published RFC 6238 vectors and reports them in its detail.
- The `AuditChain` check reports that a changed entry was detected.
- `ConfiguredDatabase` is skipped when nothing is configured, passes for a database with the
  current schema without changing the file, fails naming the missing table for a database with a
  table dropped, fails for a missing file without creating it, and fails naming an unsupported
  provider; none of its messages contains the folder or the connection string.
- The default run never creates the configured database and never runs `ConfiguredDatabase` or `ConfiguredKeys`.
- `ConfiguredKeys` is skipped when nothing is configured, when the database file is missing (and nothing is created),
  and when the key folder is missing and the database holds no secret (the folder is not created). It passes for a
  ring that matches the database, also with a certificate ring, reports the mode, the file counts and the secret
  counts, and changes no file. It fails, without creating a key, for an empty ring or a lost folder next to a database
  with secrets, for secrets of another ring (with the counts and the row ids), for a ring that the configured
  certificate does not open, for a plain ring when `DataProtection:RequireEncryptedKeys` is true, for key ring
  settings that cannot be used, and for an unsupported database provider. None of its messages holds a secret, a
  password, a certificate path, or a path other than the key folder.
- The `HostStart` check passes in a hostile environment, binds one loopback address, and leaves no sentinel and no
  file in the private temporary folder.
- The interceptor of the self-test database relaxes only a writable file database inside the folder of the check:
  the embedded self-test database commits with `synchronous=OFF` and `journal_mode=MEMORY`, the production
  registration keeps the default WAL settings, and in-memory, read-only and outside-the-folder databases are untouched.

#### Test Scenarios

**ValidationChecks_Fingerprint_Run_MatchesPinnedLiteral**: The fingerprint check is run; it passes
and its detail starts with the first characters of the pinned fingerprint, which the test also
holds as a literal. This scenario is tested by
`ValidationChecks_Fingerprint_Run_MatchesPinnedLiteral`.

**ValidationChecks_PasswordHash_Run_AcceptsRightAndRejectsWrongPassword**: The password hash check
is run; it passes, which means the production hasher accepted the right password and rejected a
wrong one. This scenario is tested by
`ValidationChecks_PasswordHash_Run_AcceptsRightAndRejectsWrongPassword`.

**ValidationChecks_Totp_Run_PassesWithRfcVectors**: The two-factor check is run; it is named `ReviewCenter_Totp`,
it passes, and its detail mentions "RFC 6238", which means the production algorithm gave the published codes for the
vector times, accepted the window, and handled the Base32 text and the `otpauth` address. This scenario is tested by
`ValidationChecks_Totp_Run_PassesWithRfcVectors`.

**ValidationChecks_AuditChain_Run_DetectsTamperedEntry**: The log of the real run is read; the audit chain check shows
a passed line that reports that the change of an entry was detected by the verifier. This scenario is tested by
`ValidationChecks_AuditChain_Run_DetectsTamperedEntry`.

**ValidationChecks_RealRun_HeavyChecks_ReportTheirDetails**: The log of the real run is read; the
git, git round trip, review workflow (`embedded SQLite; review signed by a separate reviewer; author signature refused;
approved review locked and reopened by an administrator`), attestation PDF and host start (`/login`,
`stopped cleanly`) checks each show a passed line with their detail. This scenario is tested by
`ValidationChecks_RealRun_HeavyChecks_ReportTheirDetails`.

**ReviewWorkflowCheck_RunAsync_SeparateReviewerLockAndReopen_ReportsDetails**: The review workflow check is run over its
embedded database. It creates the review as the user `selftest` (the author) and seats a second user,
`selftest-reviewer`, as reviewer. It shows that the author cannot be a slot reviewer or sign while independent
reviewers are required, that the separate reviewer signs and the review is approved and locked, that a change to the
locked review is refused with `ReviewLocked`, and that an administrator reopens it with a reason, which leaves it in
review with one reopen. The check passes and its detail contains `review signed by a separate reviewer`,
`author signature refused`, and `approved review locked and reopened by an administrator`, followed by the evidence
index fingerprint and the record digest. This scenario is tested by
`ReviewWorkflowCheck_RunAsync_SeparateReviewerLockAndReopen_ReportsDetails`.

**ValidationChecks_ConfiguredDatabase_NothingConfigured_IsSkipped**: No connection string is
configured and no database exists at the default location; the check reports that it is skipped
with the reason. The scenario is itself skipped on a machine where a database exists at the
default location, so that it never depends on the state of the deployment. This scenario is tested
by `ValidationChecks_ConfiguredDatabase_NothingConfigured_IsSkipped`.

**ValidationChecks_ConfiguredDatabase_CurrentSchema_PassesWithoutWriting**: A database with the
current schema is configured; the check passes, reports `opened read-only` and `schema matches`,
does not contain the folder in its text, and the file's size and last write time are unchanged.
This scenario is tested by `ValidationChecks_ConfiguredDatabase_CurrentSchema_PassesWithoutWriting`.

**ValidationChecks_ConfiguredDatabase_MissingTable_FailsNamingTableOnly**: A table is dropped from
the configured database; the check fails with a message that names the table, and that contains
neither the folder nor `Data Source`. This scenario is tested by
`ValidationChecks_ConfiguredDatabase_MissingTable_FailsNamingTableOnly`.

**ValidationChecks_ConfiguredDatabase_MissingFile_IsSkippedWithoutCreatingIt**: The configured file does
not exist yet (a fresh installation); the check is skipped with the reason that the file is created at the first start,
without the path in the message, and the file is not created. This
scenario is tested by `ValidationChecks_ConfiguredDatabase_MissingFile_IsSkippedWithoutCreatingIt`.

**ValidationChecks_ConfiguredDatabase_UnsupportedProvider_Fails**: The configured provider is not
supported; the check fails and names the provider. This scenario is tested by
`ValidationChecks_ConfiguredDatabase_UnsupportedProvider_Fails`.

**ValidationChecks_ConfiguredKeys_NothingConfigured_IsSkipped**: No connection string is configured and no database
exists at the default location; the check is skipped with the reason. This scenario is tested by
`ValidationChecks_ConfiguredKeys_NothingConfigured_IsSkipped`.

**ValidationChecks_ConfiguredKeys_MissingDatabase_IsSkippedWithoutCreatingAnything** and
**ValidationChecks_ConfiguredKeys_NoKeyFolderAndNoSecrets_IsSkippedWithoutCreatingTheFolder**: A configured database
file that does not exist yet, and a database without secrets next to a key folder that does not exist yet (a fresh
installation), are skipped with the reason that the first start creates them; neither the database nor the folder is
created. These scenarios are tested by the tests of the same names.

**ValidationChecks_ConfiguredKeys_RingMatchesDatabase_PassesWithoutChangingAnything** and
**ValidationChecks_ConfiguredKeys_CertificateRing_PassesAndNeverPrintsThePassword**: A database with a repository token
and a second-factor secret next to the ring that protected them passes, with a ring that is not encrypted and with a
ring protected by a certificate; the line reports the mode, the file counts and the secret counts, the database and
the key folder are unchanged (the snapshot leaves out `-wal` and `-shm` side files, which SQLite may leave empty), and
no password appears. These scenarios are tested by the tests of the same names.

**ValidationChecks_ConfiguredKeys_EmptyRingWithSecrets_FailsAndCreatesNoKey**: An empty key folder, or none, next to a
database with secrets fails with the message of the lost ring, and no key file is created. This scenario is tested by
the test of the same name.

**ValidationChecks_ConfiguredKeys_SecretsOfAnotherRing_FailsWithCountsAndIds**: The ring has a key, but the secrets were
protected by another ring; the check fails with the counts per kind and the repository and user ids, and never the
secret. This scenario is tested by the test of the same name.

**ValidationChecks_ConfiguredKeys_WrongCertificate_FailsAndCreatesNoKey**: A ring protected by one certificate is
checked with another; the check fails and no second key is created. This scenario is tested by the test of the same
name.

**ValidationChecks_ConfiguredKeys_PlaintextRingWhenEncryptionRequired_Fails**: A plain ring with
`DataProtection:RequireEncryptedKeys` true fails. This scenario is tested by the test of the same name.

**ValidationChecks_ConfiguredKeys_UnusableSettings_FailsWithoutLeaking** and
**ValidationChecks_ConfiguredKeys_UnsupportedProvider_Fails**: Key ring settings that cannot be used (a missing
certificate file) fail with a fixed message that holds neither the path nor the password; an unsupported database
provider fails and names the provider. These scenarios are tested by the tests of the same names.

**ValidationChecks_ConfiguredKeys_AttestationKeyExists_ReportsSubjectThumbprintAndValidity**: A protected attestation
key was created with the key ring. The line holds the provider, the subject, the thumbprint, and the validity of the
key, and the check passes. When no key exists, the line says "will be created on first start" and the attestation
folder is not created; that is asserted in the test of the passing ring,
`ValidationChecks_ConfiguredKeys_RingMatchesDatabase_PassesWithoutChangingAnything`. This scenario is tested by the
test of the same name.

**ValidationChecks_ConfiguredKeys_LegacyAttestationFile_ReportsWarningAndPasses**: A plaintext
`attestation-selfsigned.pfx` of an earlier version is in the attestation folder and no protected file exists. The check
passes, the line says that a key will be created on first start and has a warning that the legacy file is ignored and
should be deleted, and nothing is changed. This scenario is tested by the test of the same name.

**ValidationChecks_ConfiguredKeys_UnreadableAttestationKey_FailsNamingTheFile**: The protected attestation key file
was written by another ring. The check fails with a message that names `attestation-selfsigned.pfx.protected` and the
remedy, and holds no folder path and no key material. This scenario is tested by the test of the same name.

**ValidationChecks_ConfiguredKeys_AttestationKeyIoError_FailsWithoutNamingTheFolder**: The protected attestation key file
is held open exclusively by another handle, so that reading it fails with an I/O error. The check fails with a message
that names `Attestation:SelfSignedFolder` and neither the attestation folder nor the data folder. This scenario is
tested by the test of the same name.

**ValidationChecks_ConfiguredKeys_UnsupportedAttestationProvider_FailsNamingTheSetting**: `Attestation:Provider` has a
value that does not exist. The check fails and names the setting. This scenario is tested by the test of the same name.

The hermetic behavior of the default run is verified at the subsystem level by
`SelfTestSubsystem_ValidationWorkflow_DefaultRun_IsHermetic`.

**ValidationChecks_HostStart_HostileEnvironment_PassesOnLoopbackAndTouchesNothingAmbient**: The child process
described above runs the default validation in the hostile environment; it exits with 0 and reports nine passed
checks, the `HostStart` line names a loopback address and a port that differs from the poisoned one, the sentinel
folder and the private temporary folder are empty. This scenario is tested by
`ValidationChecks_HostStart_HostileEnvironment_PassesOnLoopbackAndTouchesNothingAmbient`.

**HostStartCheck_ExpectOneLoopbackAddress_MoreThanOneAddress_ThrowsValidationFailed**,
**HostStartCheck_ExpectOneLoopbackAddress_NotLoopbackOrNoPort_ThrowsValidationFailed** and
**HostStartCheck_ExpectOneLoopbackAddress_NoAddressOrOneLoopback_RefusesOrAccepts**: The address check of the host
check refuses two addresses, a non-loopback address, a host name, port 0 and an empty list, and accepts exactly one
loopback address (IPv4 or IPv6) with a real port. These scenarios are tested by the tests of the same names.

**ValidationChecks_FastCommit_SelfTestServices_RelaxesTheEmbeddedDatabase**: The services of the self-test are built
and the database is opened through EF Core; `synchronous` is `0` and `journal_mode` is `memory`. This scenario is
tested by `ValidationChecks_FastCommit_SelfTestServices_RelaxesTheEmbeddedDatabase`.

**ValidationChecks_FastCommit_ProductionServices_KeepDefaultCommitSettings**: The production registration opens a
database; `synchronous` is `2` and `journal_mode` is `wal`, so no relaxation reaches production. This scenario is
tested by `ValidationChecks_FastCommit_ProductionServices_KeepDefaultCommitSettings`.

**ValidationChecks_FastCommit_Interceptor_RelaxesOnlyDatabasesInsideItsFolder**: A theory; a writable database in the
folder is relaxed, while one in a sibling folder with the same prefix, one reached through `..`, one outside the
folder and a read-only one keep the default settings. This scenario is tested by
`ValidationChecks_FastCommit_Interceptor_RelaxesOnlyDatabasesInsideItsFolder`.

**ValidationChecks_FastCommit_Interceptor_LeavesInMemoryDatabaseAlone**: An in-memory database is not touched. This
scenario is tested by `ValidationChecks_FastCommit_Interceptor_LeavesInMemoryDatabaseAlone`.

**ValidationChecks_FastCommit_Interceptor_RequiresAllowedFolder**: A theory; creating the interceptor with a null,
empty or white-space folder throws. This scenario is tested by
`ValidationChecks_FastCommit_Interceptor_RequiresAllowedFolder`.

**GitRunner_CheckVersionAsync_SupportedGit_ReturnsVersionAndIsNotRepeatedByRunAsync** and
**GitRunner_CheckVersionAsync_OldGit_ThrowsGitUnavailable**: With a counting stand-in for git, the version check
returns the version text and the next command does not check again (two git invocations in total); an old git is
refused. These scenarios are tested by the tests of the same names, which are in `GitRunnerTests.cs`.
