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

- **GitAvailableCheck** (`ReviewCenter_GitAvailable`): runs `git --version` through the production `GitRunner`,
  which enforces the minimum version.
- **GitRoundTripCheck** (`ReviewCenter_GitRoundTrip`): creates a local repository with `SelfTestGit`, mirrors it
  with the production `GitSource` into a temporary cache, resolves the branch, lists the changes and reads a file
  as a raw blob.
- **ReviewWorkflowCheck** (`ReviewCenter_ReviewWorkflow`): on an embedded SQLite database in a temporary folder,
  with the services registered by `SelfTestServices`, creates a user, a project and a review, answers the
  checklist, signs the review with a local-password re-authentication, and reads the evidence index back with the
  fingerprint and the record digest. The detail says `embedded SQLite`.
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
- **HostStartCheck** (`ReviewCenter_HostStart`): starts the production host (`ServerHost.CreateApplication`) over
  a replaced in-memory configuration, a temporary SQLite database and the address `http://127.0.0.1:0` (the
  operating system picks a free port), requests `/login` and an embedded static asset, expects HTTP 200 for both,
  and stops the host cleanly. The host is started once.

The opt-in ninth check is run only with `--validate --check-config`:

- **ConfiguredDatabaseCheck** (`ReviewCenter_ConfiguredDatabase`): loads the configuration the way the server does
  (`appsettings.json` and the environment variables), recognizes the provider, opens the database read-only, and
  compares the tables and columns of the entity model (including the columns of the complex properties) with the
  database. It never writes, never creates the database, and reports problems by table and column name only. If
  nothing is configured and no database exists at the default location, it throws `ValidationSkippedException`.
  The database has no schema version number, so the comparison of tables and columns is the version check.

#### Error Handling

A check lets its exceptions escape; `ValidationRunner.ExecuteAsync` records them as failures, so no check contains
its own `try`/`catch` scaffolding. Messages never contain the connection string, the database path, a password or
another secret. Every check receives a 60 second cancellation token from the runner, so a hung git process or host
ends as a failure instead of hanging the run.

#### Dependencies

- **TemporaryDirectory** — one per check; holds every file the check creates and deletes it at the end.
- **ValidationRunner** — the types `ValidationFailedException` and `ValidationSkippedException`.
- **Core, Storage, ContentSources, Evidence and the web host** — the production services the checks call; the
  checks never reimplement the production logic.
- **System.Security.Cryptography.Pkcs** — the independent verification of the attestation signature.

#### Callers

- **Validation** — `CreateChecks` lists the checks and the runner executes them.
