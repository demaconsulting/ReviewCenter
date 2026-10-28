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

The default run has eight checks:

- **GitAvailableCheck** (`ReviewCenter_GitAvailable`): runs one `git --version` through the production
  `GitRunner.CheckVersionAsync`, which enforces the minimum version and returns the version text; the same
  runner does not repeat the version check on its first command.
- **GitRoundTripCheck** (`ReviewCenter_GitRoundTrip`): creates a local repository with `SelfTestGit`, mirrors it
  with the production `GitSource` into a temporary cache, resolves the branch, lists the changes and reads a file
  as a raw blob.
- **ReviewWorkflowCheck** (`ReviewCenter_ReviewWorkflow`): on an embedded SQLite database in a temporary folder,
  with the services registered by `SelfTestServices`, creates a user, a project and a review, answers the
  checklist, signs the review with a local-password re-authentication, and reads the evidence index back with the
  fingerprint and the record digest. The detail says `embedded SQLite`. To keep the run short, the local origin
  repository (four git commands) is created at the same time as the services are built, and the git version check
  of the services' `GitRunner` starts as soon as they exist; both are awaited before the repository is registered,
  and neither is left running when the check ends. The embedded database uses `FastCommitInterceptor` (below).
- **FingerprintCheck** (`ReviewCenter_Fingerprint`): hashes three files of fixed bytes with the production
  `Fingerprint` and compares each hash and the combined fingerprint with literals computed once, independently
  (the fingerprint literal is `13e028da06299fcedffff47b2b55cca46a68111f3124d77d355364339c4215f2`), so a platform
  difference is noticed.
- **PasswordHashCheck** (`ReviewCenter_PasswordHash`): the production `PasswordHasher` accepts the right password
  and refuses a wrong one.
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

The opt-in ninth check is run only with `--validate --check-config`:

- **ConfiguredDatabaseCheck** (`ReviewCenter_ConfiguredDatabase`): loads the configuration the way the server does
  (`appsettings.json` and the environment variables), recognizes the provider, opens the database read-only, and
  compares the tables and columns of the entity model (including the columns of the complex properties) with the
  database. It never writes, never creates the database, and reports problems by table and column name only. If
  nothing is configured and no database exists at the default location, it throws `ValidationSkippedException`.
  The database has no schema version number, so the comparison of tables and columns is the version check.

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
  checks never reimplement the production logic.
- **System.Security.Cryptography.Pkcs** — the independent verification of the attestation signature.

#### Callers

- **Validation** — `CreateChecks` lists the checks and the runner executes them.
