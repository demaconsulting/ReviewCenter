### ValidationChecks

![SelfTest Structure](SelfTestView.svg)

#### Purpose

`ValidationChecks` is the set of checks that the self-validation runs. Each check proves one thing about the
deployed tool by calling the real production code, on data that it creates itself in a temporary folder, so the
default run is hermetic and fast: it never reads or changes the configured database or configuration, never binds
the configured port, uses no network beyond the loopback interface, and needs neither Docker nor a browser.

#### Data Model

Every check is a `static` class in the `SelfTest.Checks` namespace with a `Name` constant (the test name in the
report and the results file, always starting with `ReviewCenter_`) and a `RunAsync(CancellationToken)` method that
returns a short detail text. A check reports a failed expectation by throwing `ValidationFailedException` and a
skip by throwing `ValidationSkippedException`. The checks hold no state. The helpers `SelfTestServices` and
`SelfTestGit` are shared by several checks.

#### Key Methods

The default run has nine checks:

- **GitAvailableCheck** (`ReviewCenter_GitAvailable`): runs one `git --version` through the production
  `GitRunner.CheckVersionAsync`, which enforces the minimum version and returns the version text; the same
  runner does not repeat the version check on its first command.
- **GitRoundTripCheck** (`ReviewCenter_GitRoundTrip`): creates a local repository with `SelfTestGit`, mirrors it
  with the production `GitSource` into a temporary cache, resolves the branch, lists the changes and reads a file
  as a raw blob.
- **ReviewWorkflowCheck** (`ReviewCenter_ReviewWorkflow`): on an embedded SQLite database in a temporary folder,
  with the services registered by `SelfTestServices`, creates two users and a project (the system administrator
  `selftest`, who creates the review and so is its author, and the ordinary project member `selftest-reviewer`) and a
  review, answers the checklist as the author, and shows the review integrity rules: adding the author as a reviewer is
  refused with `ReviewerNotIndependent`; with the independent reviewers rule switched off the author is seated, and
  after it is switched on again the author's signature is refused with `ReviewerNotIndependent` and the author's slot is
  removed; the separate reviewer is added to a slot and signs the review with a local-password re-authentication; the
  evidence index is read back with the fingerprint and the record digest; then the approved review is shown to be
  locked (changing a checklist answer is refused with `ReviewLocked`), the administrator reopens it with a reason, and
  the approval must then be not approved with one reopen. The detail says `embedded SQLite; review signed by a separate
  reviewer; author signature refused; approved review locked and reopened by an administrator; evidence index
  fingerprint {short fingerprint}, record digest {short digest}`. To keep the run short, the local origin
  repository (four git commands) is created at the same time as the services are built, and the git version check
  of the services' `GitRunner` starts as soon as they exist; both are awaited before the repository is registered,
  and neither is left running when the check ends. The embedded database uses `FastCommitInterceptor` (below).
- **FingerprintCheck** (`ReviewCenter_Fingerprint`): hashes three files of fixed bytes with the production
  `Fingerprint` and compares each hash and the combined fingerprint with literals computed once, independently
  (the fingerprint literal is `13e028da06299fcedffff47b2b55cca46a68111f3124d77d355364339c4215f2`), so a platform
  difference is noticed.
- **PasswordHashCheck** (`ReviewCenter_PasswordHash`): the production `PasswordHasher` accepts the right password
  and refuses a wrong one.
- **TotpCheck** (`ReviewCenter_Totp`): the production `TotpAlgorithm` produces the one-time codes of the SHA-1
  test vectors of RFC 6238 (the secret `12345678901234567890`, eight digits, six times from 59 to 20000000000) and the
  six-digit code of the first vector. It requires that a code one time step away is accepted and one two steps away is
  refused, that a wrong or malformed code is refused, that the secret has the expected Base32 form, and that the
  `otpauth://totp/` address starts as expected. The check needs no database and no clock; its detail text names the
  vectors, the window of one step, and the replay step that the algorithm returns.
- **AuditChainCheck** (`ReviewCenter_AuditChain`): appends entries through the production `AuditService`, verifies
  the chain, changes one stored entry with a direct SQL statement, and requires the production verifier to report
  it.
- **AttestationPdfCheck** (`ReviewCenter_AttestationPdf`): creates a throw-away self-signed key, renders and signs
  an attestation with the production renderer and signer, verifies the CMS signature and the DocMDP certification
  independently with `System.Security.Cryptography.Pkcs`, then changes one byte and requires the verification to
  fail.
- **HostStartCheck** (`ReviewCenter_HostStart`): starts the production host (`ServerHost.CreateApplication`) in
  isolated mode (`ServerHostOptions.IsolatedSettings` holds the in-memory settings and a temporary SQLite database;
  the host is built from an empty builder, so no `appsettings.json`, environment variable or host default is read)
  on the address `http://127.0.0.1:0` (the operating system picks a free port). `ExpectOneLoopbackAddress` requires
  that the host bound exactly one loopback address with a real port; the check requests `/login` and an embedded
  static asset, expects HTTP 200 for both, and stops the host cleanly before the temporary folder is disposed. The
  host is started once.

The opt-in tenth and eleventh checks are run only with `--validate --check-config`:

- **ConfiguredDatabaseCheck** (`ReviewCenter_ConfiguredDatabase`): loads the configuration the way the server does
  (`appsettings.json` and the environment variables), recognizes the provider, opens the database read-only, and
  compares the tables and columns of the entity model (including the columns of the complex properties) with the
  database. It never writes, never creates the database, and reports problems by table and column name only. If
  nothing is configured and no database exists at the default location, or if the configured database file does not
  exist yet (a fresh installation creates it at the first start), it throws `ValidationSkippedException` with that
  reason; a database file that exists but cannot be opened or has the wrong schema still fails.
  The database has no schema version number, so the comparison of tables and columns is the version check.
- **ConfiguredKeysCheck** (`ReviewCenter_ConfiguredKeys`, the eleventh): answers whether the key ring that the server is
  configured to use belongs to the database, which is the question after a restore. It loads the configuration the way
  the server does, registers the production services with automatic key generation of Data Protection switched off, and
  reads the ring and the database read-only. It runs `KeyRingProtection.CheckRing` when the key folder exists (the
  newest key must be decrypted, and a plain key file is refused with `DataProtection:RequireEncryptedKeys`), then
  `SecretScan`, and reports one line: the protection mode, the number of key files and how many of them are not
  encrypted, the number of protected secrets (repository tokens and second-factor secrets) and how many cannot be
  decrypted. It fails when a secret cannot be decrypted (the message gives the counts per kind and the ids of the rows),
  when the ring is empty but the database holds secrets, when the ring is not encrypted although encryption is
  required, when its newest key cannot be decrypted, and when the key ring settings cannot be used (a fixed message that
  names the settings, because the reason can hold a certificate path). It throws `ValidationSkippedException` when no
  database is configured, when the database file does not exist yet, and when the key folder does not exist yet and the
  database holds no secret, because a fresh installation creates both at the first start. It never creates a key, a
  folder, or a file, and never prints a key, a secret, a password, or a path other than the key folder. It then adds
  one clause for the attestation key (`DescribeAttestationKey`), built with the production factory with `allowCreate`
  set to `false`, from the `Attestation` settings: for the SelfSigned provider the subject, thumbprint, and validity of
  the key, or "will be created on first start" when the protected file does not exist yet; a plaintext
  `attestation-selfsigned.pfx` of an earlier version is added as a warning note (it is ignored, delete it). It fails,
  naming only the file `attestation-selfsigned.pfx.protected`, when the key ring cannot decrypt that file, and fails
  with the path-free message of the key (`AttestationKeyException.MessageWithoutPath`, which names a setting or a file
  name but never the folder) when another problem makes the key unusable. The clause does not mention what
  `Attestation:RequireTrustedKey` would do to a key that does not exist yet: with that setting true and no key, the
  check still passes with "will be created on first start", while the server refuses to start because the new
  self-signed key is not trusted (the check does not create a key, so it cannot apply the trust policy to it). For the
  CertificateStore and File providers the clause gives the same description of the key (the File provider reads the
  file, which is not a change), followed by `; warning:` and the expiry warning when the certificate ends within
  `Attestation:ExpiryWarningDays` or has ended, and the warnings that the provider noted (for example a key without a
  password, or a file that others can read); the File provider receives `DataProtection:RequireEncryptedKeys` and, with
  that setting, fails for a key without a password. A missing path, file or password, a wrong password and a key
  that `RequireTrustedKey` refuses fail with the path-free message, so the password and the folder of the file are
  never printed. The store provider is not covered by a test of the check; it relies on the describe path of the
  provider itself (`GetSigningKey` and its trust policy), which is tested in the `Evidence` subsystem. The clause is
  only added when the check runs, not when it is skipped.

**FastCommitInterceptor** (internal, `SelfTestServices`): an EF Core connection interceptor that sets
`synchronous=OFF` and `journal_mode=MEMORY` when a connection opens, so that the many commits of the review
workflow do not wait for the disk. It is created with the required `allowedFolder` (the temporary folder of the
check; null or white space is an `ArgumentException`) and relaxes only a writable file database whose full path is
inside that folder. A read-only or in-memory connection, and a path outside the folder (including a sibling
folder with the same name prefix and a path with `..`), is left alone. It is registered only by
`SelfTestServices`, never by the production `AddServices`, so a stored review database keeps the default
commit behavior.

**Disposal order**: every check creates its `TemporaryDirectory` first and stops everything that uses it (host,
child processes, database connections, services) before the directory is disposed. `Dispose` retries a lock that
goes away a moment later but does not close handles.

#### Error Handling

A check lets its exceptions escape; `ValidationRunner.ExecuteAsync` records them as failures, so no check contains
its own `try`/`catch` scaffolding. Messages never contain the connection string, the database path, a password or
another secret. Every check receives a cancellation token from the runner, which also enforces the 60 second deadline
itself: a check that blocks a thread or ignores its token is recorded as `Timed out after 60 s` and the run goes on,
so a hung git process or host ends as a failure instead of hanging the run.

#### Dependencies

- **TemporaryDirectory** — one per check; holds every file the check creates and deletes it at the end.
- **ValidationRunner** — the types `ValidationFailedException` and `ValidationSkippedException`.
- **Core, Storage, ContentSources, Evidence and the web host** — the production services the checks call; the
  checks never reimplement the production logic. `TotpCheck` calls `TotpAlgorithm` of the `Identity` subsystem.
  `ConfiguredKeysCheck` calls `SecretScan` and `KeyRingProtection` of the `Hosting` subsystem, and
  `AttestationKeyProviderFactory` of the `Evidence` subsystem.
- **System.Security.Cryptography.Pkcs** — the independent verification of the attestation signature.

#### Callers

- **Validation** — `CreateChecks` lists the checks and the runner executes them.
