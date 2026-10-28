### ValidationChecks

#### Verification Approach

The self-validation checks are verified with unit tests defined in `ValidationChecksTests.cs`, and
the whole of them by the real default validation, which is run once in the test process
(`RealValidationRun`) and once as a separate process (`IntegrationTests.cs`). Each individual
check is exercised at most once by the suite, because the real checks are the slow part of the
suite (they start git, a database, a signer and a web host):

- The light checks (`Fingerprint`, `PasswordHash`, `AuditChain`) are run directly.
- The checks that start git, a complete review workflow, a signer, or the web host are verified by
  the real run, whose log and results file must show each of them passed with the detail it
  reports (for example `embedded SQLite` for the review workflow and `stopped cleanly` for the
  host).
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
- The real default run reports eight passed tests, with elapsed times, and none skipped or failed.
- The `Fingerprint` check passes and compares with the literal
  `13e028da06299fcedffff47b2b55cca46a68111f3124d77d355364339c4215f2`.
- The `PasswordHash` check accepts the right password and rejects a wrong one.
- The `AuditChain` check reports that a changed entry was detected.
- `ConfiguredDatabase` is skipped when nothing is configured, passes for a database with the
  current schema without changing the file, fails naming the missing table for a database with a
  table dropped, fails for a missing file without creating it, and fails naming an unsupported
  provider; none of its messages contains the folder or the connection string.
- The default run never creates the configured database and never runs `ConfiguredDatabase`.

#### Test Scenarios

**ValidationChecks_Fingerprint_Run_MatchesPinnedLiteral**: The fingerprint check is run; it passes
and its detail starts with the first characters of the pinned fingerprint, which the test also
holds as a literal. This scenario is tested by
`ValidationChecks_Fingerprint_Run_MatchesPinnedLiteral`.

**ValidationChecks_PasswordHash_Run_AcceptsRightAndRejectsWrongPassword**: The password hash check
is run; it passes, which means the production hasher accepted the right password and rejected a
wrong one. This scenario is tested by
`ValidationChecks_PasswordHash_Run_AcceptsRightAndRejectsWrongPassword`.

**ValidationChecks_AuditChain_Run_DetectsTamperedEntry**: The audit chain check is run; it passes
and reports that the change of an entry was detected by the verifier. This scenario is tested by
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
