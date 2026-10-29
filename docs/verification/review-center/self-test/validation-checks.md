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
  reports (for example `embedded SQLite` for the review workflow and `stopped cleanly` for the
  host).
- The real run is started once, eagerly, at the start of the test assembly (`RealValidationRunFixture`), so
  the slow checks run while the rest of the suite runs; it exists twice: in the test process and as a child process
  of a copy of the build output in a hostile environment (see below).
- The hermetic start of the host is verified by a child process that runs a copy of the build output with a broken
  `appsettings.json` beside the program and in the working directory, a poisoned `ASPNETCORE_URLS`, Kestrel
  endpoint variable, `DOTNET_ENVIRONMENT` and `Database__*` settings, sentinel paths, and a private
  `TEMP`/`TMP`: the run must pass, the host must bind a loopback port that is not the poisoned one, the sentinels must
  not be created, and the private temporary folder must be empty afterwards.
- The opt-in `ConfiguredDatabase` check is run directly against temporary databases that the test
  creates with the production schema, and against the process configuration, which the test sets
  through the `Database__ConnectionString` and `Database__Provider` environment variables and
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
- The default run never creates the configured database and never runs `ConfiguredDatabase`.
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
git, git round trip, review workflow (embedded SQLite), attestation PDF and host start (`/login`,
`stopped cleanly`) checks each show a passed line with their detail. This scenario is tested by
`ValidationChecks_RealRun_HeavyChecks_ReportTheirDetails`.

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

**ValidationChecks_ConfiguredDatabase_MissingFile_FailsWithoutCreatingIt**: The configured file does
not exist; the check fails without the path in the message and the file is not created. This
scenario is tested by `ValidationChecks_ConfiguredDatabase_MissingFile_FailsWithoutCreatingIt`.

**ValidationChecks_ConfiguredDatabase_UnsupportedProvider_Fails**: The configured provider is not
supported; the check fails and names the provider. This scenario is tested by
`ValidationChecks_ConfiguredDatabase_UnsupportedProvider_Fails`.

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
