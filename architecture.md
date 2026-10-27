# ReviewCenter Architecture

## Purpose

ReviewCenter is a self-hosted DEMA Consulting web service for performing formal code reviews in regulated projects.
It fits the Continuous Compliance approach: review evidence is produced, stored, and published as a routine part of
the delivery pipeline rather than assembled by hand at the end of a project.

- **What is reviewed**: reviewers examine a set of files, the "current files", optionally shown as a delta against an
  earlier "baseline" review.
- **Review activities**: reviewers discuss code in threaded comments anchored to a file or line range, answer a
  project checklist (Pass/Fail/N/A plus notes), and sign the outcome using 21 CFR Part 11 / EU Annex 11 style
  electronic signatures that require re-authentication.
- **Stakeholders**: authors, reviewers (primary and alternate), project admins, system admins, auditors, and CI
  pipelines running ReviewMark.
- **ReviewMark relationship**: ReviewMark (a separate DEMA tool) acts as the CLI front-end. It creates reviews
  through the API and consumes completed review evidence through its existing `evidence-source: type: url`
  index.json mechanism.

## Scope

**Included**:

- the ReviewCenter server software package (one System)
- the in-repo ReviewCenter.Puppet agent tool (development tooling, not distributed)
- tests: unit, subsystem, Playwright end-to-end system tests, and ReviewMark fingerprint/index contract tests

**Excluded or deferred**:

- ReviewMark itself (a separate repository and product)
- the PDF evidence export and PDF/A (deferred; the review record is viewed as a web report for now)
- EF Core migrations (deferred; schema created directly for now)
- AI-assisted review (deferred; likely an Azure OpenAI Service account)
- outgoing webhooks and git-host status posting
- multi-instance/high-availability deployment
- OpenTelemetry
- multi-tenancy (one organization per instance; separate instances per project are also allowed)
- additional login methods (LDAP, SAML, Windows)

Expected scale is small: under 50 users and tens of concurrent reviews per instance.

## Technology Stack

- **Platform and language**: C# on .NET 10 (net10.0 only).
- **Web**: ASP.NET Core with a Blazor Server (interactive server rendering) UI. The diff viewer uses the Monaco editor
  through JS interop, in side-by-side diff mode with line-range decorations for comment anchors.
- **Data**: EF Core with SQLite, PostgreSQL and SQL Server providers, selected in appsettings.json. File snapshots are
  stored in the database.
- **Identity**: ASP.NET Core Identity for local accounts (password policy, optional TOTP MFA) and OpenID Connect for
  external identity providers. ASP.NET Data Protection encrypts secrets at rest.
- **Git**: git CLI on the host, via a bare mirror cache.
- **Email**: SMTP.
- **Logging**: Microsoft.Extensions.Logging structured logging.
- **Testing**: xUnit v3. Playwright for .NET (headless Chromium) is used for E2E tests and Puppet.
- **Distribution**: self-contained, single-file executables (plus wwwroot assets) for win-x64 and linux-x64. There is
  no NuGet dotnet-tool package any more.
- **Hosting**: single instance behind a reverse proxy (nginx/IIS/Traefik) that terminates TLS, using ASP.NET
  forwarded-headers middleware.
- **Compliance tooling**: ReqStream, ReviewMark, SysML2Tools, Pandoc.

## Software Structure

ReviewCenter is delivered as one Software Package containing one System. The System is divided into the Subsystems
shown below. Units are defined within each Subsystem during detailed design.

```text
ReviewCenter (Software Package: self-contained executables win-x64 / linux-x64)
└── ReviewCenterSystem
    ├── Web (Blazor Server UI)
    │   ├── Pages - dashboard/inbox, projects, review list, review workspace, report view,
    │   │           project admin, system admin, personal access tokens
    │   ├── DiffViewer - Monaco interop; current files vs optional baseline review; line-range anchors
    │   ├── CommentThreads - threads anchored to file / line range; replies, @mentions, resolve/reopen
    │   ├── ChecklistPanel - Pass / Fail / N/A answers + notes
    │   ├── ChecklistEditor - project checklist authoring (versioned)
    │   ├── SignOff - verdict, signature manifest, re-authentication ceremony
    │   └── ReviewReport - read-only review record (the evidence view)
    ├── Api - REST (unversioned, OpenAPI published): create Draft review (upload | git ref + expected
    │         fingerprint), review status, evidence index.json for ReviewMark
    ├── Identity
    │   ├── LocalAccounts - ASP.NET Core Identity, password policy, optional TOTP MFA
    │   ├── OidcLogin - external identity provider login, claim mapping
    │   ├── ReAuthentication - fresh credential proof for signing (password+MFA, or OIDC
    │   │                      prompt=login/max_age=0 with auth_time check)
    │   ├── PersonalAccessTokens - per-user, hashed at rest, expiring, revocable; never valid for signing
    │   └── Authorization - system admin; per-project roles (project admin, author, reviewer-eligible)
    ├── Core (separate assembly)
    │   ├── Domain - Project, ChecklistTemplate/Version, RoleSlot, Review, FileSet, FileSnapshot,
    │   │            CommentThread, ChecklistAnswer, Verdict, Signature, ReviewLink (rework-of / baseline)
    │   ├── ReviewLifecycle - rule-driven state machine
    │   ├── Fingerprint - ReviewMark-compatible content fingerprint
    │   ├── RecordDigest - canonical serialization + SHA-256 of the review record
    │   ├── Signing - signature manifest, digest binding, invalidation
    │   ├── AuditTrail - append-only, hash-chained change log
    │   ├── DomainEvents - feeds AuditTrail, Notifications, future Reporting
    │   └── Notifications - event to recipient rules
    ├── Storage - EF Core DbContext; SQLite / PostgreSQL / SQL Server; snapshots in DB
    ├── ContentSources
    │   ├── UploadSource - uploaded bytes stored verbatim
    │   └── GitSource - git CLI, bare mirror cache, applies .gitattributes text/eol (LF checkout)
    ├── Evidence - ReviewMark-compatible index.json; report data for the ReviewReport view
    ├── Messaging - in-app inbox; SMTP email (built-in templates, per-user opt-out)
    └── Hosting - startup, appsettings binding + startup validation, forwarded headers,
                  structured logging, CLI verbs (serve; migrate reserved for later)

Development tooling (in repo, not distributed)
└── ReviewCenter.Puppet - Playwright persistent-browser helper for AI agents

OTS: ASP.NET Core, Blazor, EF Core + providers, ASP.NET Core Identity, Monaco editor, git CLI,
     Playwright, xUnit v3
External systems: ReviewMark, OIDC identity provider, git hosts, SMTP server, reverse proxy
```

### Subsystem Responsibilities

**Web** is the Blazor Server user interface. It renders pages, the Monaco-based diff viewer, comment threads, the
checklist panel and editor, the sign-off ceremony, and the read-only review report. It contains no business rules; all
state changes go through Core services and are authorized by Identity.

**Api** exposes the unversioned REST surface used by ReviewMark and other automation. It creates Draft reviews from an
upload or from a git reference with an expected fingerprint, reports review status, and serves the evidence
index.json. It authenticates callers with personal access tokens and never creates or signs anything on behalf of a
human reviewer.

**Identity** owns authentication and authorization. It provides local accounts, OpenID Connect login, the
re-authentication proofs required for signing, personal access tokens, and the system and per-project role checks. It
does not know about review content.

**Core** is a separate, non-distributed assembly holding the domain model and all rules: the review lifecycle state
machine, the content fingerprint, the record digest, signing and signature invalidation, the hash-chained audit
trail, domain events, and notification rules. Core has no dependency on the web framework, which gives the project a
clean test boundary.

**Storage** implements persistence with an EF Core DbContext over SQLite, PostgreSQL, or SQL Server, chosen in
configuration. It stores file snapshots in the database alongside the review record.

**ContentSources** turns an external reference into an immutable set of file snapshots behind one abstraction.
UploadSource keeps uploaded bytes verbatim. GitSource uses the git CLI and a bare mirror cache and applies
`.gitattributes` text and eol rules to reproduce an LF checkout.

**Evidence** produces the ReviewMark-compatible index.json and the report data behind the ReviewReport view. It reads
completed reviews and never modifies them.

**Messaging** delivers notifications to the in-app inbox and, with built-in templates and per-user opt-out, by SMTP
email. It consumes domain events and has no other inputs.

**Hosting** composes the application: startup, appsettings binding with startup validation, forwarded-headers
handling, structured logging, and the command-line verbs (`serve` now; `migrate` reserved for later).

### Review Lifecycle

```text
Draft → Open → Concluding (verdict proposed: Pass | Fail) → Signing → Closed (Pass | Fail)
  any change to signed content invalidates all signatures and returns the review to Concluding
Closed(Fail) ──rework-of──▶ new Review
```

A review is created as Draft (typically through the API), then a human assigns reviewer slots and opens it. Reviewers
comment and answer the checklist while it is Open. A verdict is proposed in Concluding, and signatures are collected
in Signing. The review is Closed only when every required signature carries the current record digest. A closed Fail
review is final; follow-up work is a new review linked to it by a rework-of reference. The state machine is rule
driven so that future workflows can be added without restructuring it.

### Puppet

ReviewCenter.Puppet is a small Playwright-based helper (`puppet start`, then one `puppet <command>` per shell call)
that keeps one persistent headless browser open so AI agents can drive the real user interface during development.
It is a minimal helper and is kept as small and simple as possible; see
`tools/puppet/README.md` and `docs/design/review-center-puppet.md`.

- **Selectors**: the UI exposes stable `data-testid` attributes.
- **Shared engine**: the same Playwright approach powers the E2E system tests.

## Architectural Decisions

1. **Single software package**: there is one software package. Core is a separate assembly, but it is a Subsystem and
   is not distributed. Reason: only the server consumes it, and the separate assembly gives a test boundary.
2. **ReviewMark as the CLI**: there is no ReviewCenter CLI client. Reason: avoids duplicating ReviewMark's review-set
   and fingerprint knowledge.
3. **Puppet is dev tooling**: Puppet is in-repo development tooling and is not shipped. Reason: it exists to let
   agents and tests exercise the UI, and end users have no need for it.
4. **Self-hosted, single organization per instance**: deployment is self-hosted with one organization per instance and
   many projects, and projects may also run separate instances. Reason: small scale, and regulated users host their
   own evidence.
5. **Blazor Server UI with Monaco**: Blazor Server plus Monaco. Reason: everything stays in C#, and Monaco provides a
   mature diff view.
6. **Three database providers, snapshots in the DB**: SQLite, PostgreSQL, and SQL Server are supported, with file
   snapshots stored in the DB. Reason: small scale and simple backup.
7. **Local accounts plus OIDC, Part 11 style signatures**: local accounts plus OIDC, with Part 11 / Annex 11 style
   signatures. Reason: regulated projects need attributable and binding approval. Details:
   - **Manifest**: printed name, UTC date/time, and meaning (for example "Reviewer - QA slot", "Author").
   - **Re-authentication**: required for every signature.
   - **Audit trail**: a hash-chained audit trail.
8. **Role-slot reviewer model**: the project defines role slots, and each slot has a primary and alternates; any one of
   them may fill it. The author also signs. Reason: projects need named responsibilities without blocking on one
   person's availability.
9. **The verdict is what is signed**: a Fail verdict is a valid, final record that later rework reviews reference. A
   Pass verdict requires no Fail checklist answers and all threads resolved. Reason: failed reviews are legitimate
   evidence.
10. **Per-project checklists**: one checklist per project, authored in the UI and versioned. Each review freezes the
    checklist version it uses. Reason: later edits must never alter past evidence.
11. **Two content sources behind one abstraction**: upload and git share one interface. Reason: callers should not
    care where files originate, and both must yield identical immutable evidence. Details:
    - **Upload**: bytes are used verbatim.
    - **Git**: the git CLI applies .gitattributes eol rules and assumes an LF checkout, matching the recommended
      `* text=auto eol=lf`.
    - **Snapshot at creation**: git content is snapshotted into the DB when the review is created. Reason: evidence
      never depends on the repository staying reachable.
    - **Fingerprint check**: the caller supplies the expected fingerprint, and a mismatch rejects creation.
12. **A review certifies only the current files**: the baseline is an optional reading aid chosen by picking a prior
    review. It defaults to the latest closed review with the same review-set id and is never part of the evidence
    claim. The baseline reference IS included in the record digest, for transparency. Reason: the evidence claim
    stays simple (these files were reviewed), while the record still shows what the reviewers were shown.
13. **Two-digest model**: a content fingerprint and a record digest. Reason: one digest links evidence to the code in
    CI, which has only the files; the other binds signatures to the full record, as Part 11 requires. Details:
    - **Content fingerprint**: uses ReviewMark's exact algorithm. Each file's raw bytes are hashed with SHA-256 and
      written as lowercase hex. The hex strings are sorted by ordinal comparison, concatenated with no separator,
      UTF-8 encoded and SHA-256 hashed again, giving 64 lowercase hex characters. It is content-only and ignores
      paths. It identifies WHICH files were reviewed and is published in index.json.
    - **Record digest**: SHA-256 over a canonical serialization of the review id, content fingerprint, baseline
      reference, file list (path + hash), checklist version, answers and notes, thread contents and states, verdict,
      and signature meaning. It is WHAT each signer approved.
    - **Approval condition**: the review is complete only when all signatures carry the current record digest. Any
      change clears all signatures.
14. **Fingerprint algorithm ownership**: the fingerprint algorithm is duplicated in ReviewCenter and guarded by
    contract tests with known vectors. Reason: ReviewMark keeps it internal.
15. **Personal access tokens**: any user, including admins, can create PATs. An admin places one in CI as a secret for
    ReviewMark (basic credentials via env vars). PATs act as their user, are hashed at rest, expire and are
    revocable, and can never sign. Reviews created through the API start as Draft until a human assigns slots and
    opens them in the UI. Reason: automation can start a review, but only people can take part in it.
16. **Unversioned API**: the API is unversioned. It is protected by a committed OpenAPI snapshot (CI fails on
    unintended diffs) and by ReviewMark contract tests (index.json schema, fingerprint vectors). Reason: there is a
    single known consumer, and the snapshot and contract tests make breaking changes visible.
17. **Evidence is a web report for now**: the review record is a web report view. Reason: it delivers usable evidence
    sooner than a PDF pipeline. Details:
    - **Report**: evidence is a web report view for now.
    - **index.json**: entries have `id`, `fingerprint`, `date`, `result` (pass|fail) and `file`, where `file` points
      to the report.
    - **PDF**: PDF/A export is deferred, and the library choice is open (PDFsharp went commercial).
18. **Database migrations deferred**: no EF migrations for now; the schema is created directly. There will be an
    explicit `migrate` CLI verb later. Tests run on SQLite only. Reason: the product is pre-release, so there is no
    deployed data to upgrade.
19. **Git credentials**: kept in an admin-only repository registry (a repository has a location and an optional user
    name and token; projects pick a registered repository), encrypted at rest with ASP.NET Data Protection, with the
    key ring stored outside the DB. The token is write-only in the UI and reaches git only through the environment of
    the git child process. The key ring is not encrypted at rest, so its folder must be protected by file permissions,
    and losing it makes the stored tokens unreadable. Reason: a database leak alone must not expose repository
    credentials.
20. **Hosting**: single instance behind a reverse proxy that terminates TLS. Re-authentication is application-level,
    so the proxy does not affect it. Forwarded headers must be configured so redirect URLs and cookies use the public
    HTTPS origin. Database backups are the operator's responsibility. Logging is structured only. Reason: this
    matches the small-scale, self-hosted deployment model and keeps operations simple.
21. **Distribution**: self-contained single-file net10.0 executables for win-x64 and linux-x64, with no NuGet tool.
    The CI/release pipeline builds them natively on each OS, packages them as a zip (Windows) or tar.gz (Linux) with a
    SHA-256 checksum, runs `--validate` and a `serve` smoke test against the extracted executable, and attaches the
    archives to the GitHub release. Reason: operators install a service, not a developer tool, and should not need a
    .NET runtime.
22. **Notifications**: in-app inbox plus SMTP email only. Reason: these cover the review workflow at this scale, and
    webhooks and git-host status posting are out of scope.
23. **Designed-in extension points**: ReviewLifecycle is a rule-driven state machine (future workflows, rounds,
    sign-off order). DomainEvents and AuditTrail are the data source for future reporting and metrics. AI-assisted
    review is a future API participant, flagged as AI and never able to sign. Reason: deferred features should not
    require restructuring the core.

## Implementation Status

The architecture above is the target design. Only the first two slices are implemented so far:

- **Implemented, slice 1 (platform)**: Hosting (the ASP.NET Core server host and its logging), Web (the Blazor Server
  home page with a health endpoint), the command-line interface (`serve`, `--urls`, and the template options), and
  the Puppet developer tool (`tools/puppet`), a minimal helper that drives the web interface through one persistent
  headless Chromium, one command per invocation.
- **Implemented, slice 2 (projects and reviews)**: Core (`Fingerprint`, `ProjectService`, `ReviewService`) in its own
  assembly, `DemaConsulting.ReviewCenter.Core`, with no dependency on the web or the database; Storage (EF Core
  with SQLite, `ReviewStore`, `DatabaseSetup`, schema created with `EnsureCreated`, no migrations; SQLite only, with
  the provider chosen by `Database:Provider` as the seam for PostgreSQL and SQL Server); and three Web pages
  (`/projects`, `/projects/{id}`, `/projects/{id}/reviews/{id}`). A review is created from uploaded files, is in the
  `Draft` state only, and has a ReviewMark-compatible content fingerprint (known answers verified against the
  ReviewMark `--elaborate` output). Upload limits are 2 MiB per file, 200 files and 20 MiB per review, and only the
  file name of an uploaded file is kept. Every interactive element has a `data-testid`, so tests and the Puppet can
  address it. The files are documented in `docs/design`, `docs/verification`, `docs/reqstream` and `docs/sysml2`.
- **Temporary limitation**: there is no authentication; the server has a single anonymous operator. Identity (decisions
  on roles and sign-off) comes in a later slice, and the server must only be exposed on `localhost` or a trusted
  network until then.
- **Implemented, slice 7a-1 (git content source)**: the repository registry (`RepositoryService`, admin-only
  `/repositories` page, write-only encrypted tokens, disable and enable only), the file selection patterns
  (`SelectionMatcher`), revision validation (`RevisionValidator`), and the ContentSources subsystem (`GitRunner`,
  `GitSource`, `CredentialProtector`), which reads repositories through the installed git into bare mirrors. The Data
  Protection key ring and the git cache are kept beside the database by
  default.
- **Implemented, slice 7a (reviews from a git repository)**: a review can be created from the files that changed between
  an optional baseline revision and a head revision of a registered repository and match a selection. The changed
  files (path, change kind, baseline and head content), both resolved commits, and the selection are frozen in the
  database, so nothing is read from the repository afterwards; a selection with no changed file is refused
  (`NothingToReview`). The fingerprint covers the files at the head; the record digest is now version 2 and binds the
  source, the selection, and every change; line comments are refused on deleted files. The project page has an upload
  or repository source choice, and the review page shows the change kind of each file and a Head or Baseline view of a
  modified file (no diff highlighting). The 7a-1 note that reviews do not use the repository registry no longer
  applies. Development databases created before this slice must be recreated (no migrations).
- **Implemented, slice 7b-1 (planning core)**: the Core `ReviewPlanner` plans the reviews that a head revision needs from
  the `reviews:` list of the repository's `.reviewmark.yaml` (read by the ContentSources `ReviewMarkConfigParser`, with
  the single new OTS dependency YamlDotNet): a full review, the changes since a given revision, or the changes since the
  last signed review of the same review-set, with a fallback to a full review when that baseline is not an ancestor
  (a new `IsAncestorAsync` on the git session) or the selection changed. A review records its review-set id, and the
  record digest is now version 3 and binds it. The plan page is slice 7b-2.
- **Implemented, slice 7b-2 (plan page)**: the Web `PlanPage` at `/projects/{id}/plan` (linked from the project page when
  a repository is enabled) takes a repository, a head revision, and a baseline choice (none, a given revision, or the
  last signed review), shows one summary sentence, the sets that need a review first (ticked), the sets that cannot be
  planned and those already filed only when present, and the empty sets collapsed, and creates the ticked reviews with a
  result for each. The review page shows the review-set of a planned review. The planner now turns an unexpected fault
  of one review into a failed result with a generic message, so the other reviews of the batch are kept.
- **Implemented, slice 7c (diff viewer and deep links)**: the Core `LineDiff` (Myers line diff, bounded by 10,000 lines
  per side and 2,000 changed lines) and `ReviewService.GetDiffAsync` give the review page a side-by-side Diff view for a
  changed file, with unchanged lines collapsed and a plain "too large" fallback. A thread is anchored to the Head or the
  Baseline side (`ThreadSide`, stored with the thread, shown as "Before" or "After"), and the record digest is now
  version 4 and binds the side. Links address one file, line (`?line=`, `&side=baseline`), or thread
  (`/reviews/{id}/threads/{n}`), survive a sign-in (`ReturnUrl`, open-redirect safe), and the address follows the
  selection (`replaceUrl`). Development databases created before this slice must be recreated.
- **Implemented, slice 8 (signed PDF attestation)**: the Evidence subsystem (`PdfDocumentWriter`,
  `AttestationRenderer`, `AttestationSigner`, `AttestationKeyProvider`, `AttestationService`, `AttestationEndpoint`)
  makes a signed PDF of an Approved review at `/projects/{id}/reviews/{reviewId}/attestation.pdf`, signed with a
  self-signed key (development and test) or a certificate from a certificate store, and records `attestation.generated`.
  Azure Key Vault and an RFC 3161 time stamp authority are planned, not implemented.
- **Planned and deferred**: Api, Messaging, the review lifecycle beyond `Draft`, and the rest of Core and
  Storage. They have no code, requirements,
  design documents, or verification documents yet, so the SysML2 model and the design documentation describe the
  implemented items only and are deliberately partial.
- **Pipeline**: the CI/release pipeline builds net10.0 only and publishes the self-contained win-x64 and linux-x64
  executables (see decision 21). The Core assembly is bundled into the single-file executable; the native SQLite
  library (`e_sqlite3.dll` or `libe_sqlite3.so`) stays a separate file beside the executable, next to the web assets,
  so the archive must be kept together. CI installs Chromium so the Puppet end-to-end test runs and is linked to the
  Puppet requirements. The release workflow attaches the archives and checksums to the GitHub release.

## Open Concerns

1. 🟡 **MEDIUM** PDF evidence library: the choice is deferred. Needs a non-commercial-license library that can write
   the Keywords metadata and eventually PDF/A.
2. 🟡 **MEDIUM** Database migrations: deferred, so there is no upgrade path until implemented. Schema changes before
   then require recreating the database. `EnsureCreated` also has a known race if two server instances start on a new
   database at the same time; run a single instance (decision 20).
3. 🟡 **MEDIUM** GitSource fidelity: verify early that the git CLI filter approach reproduces the CI checkout bytes,
   including LFS and binary files.
4. 🟢 **LOW** ReviewMark `file` field: confirm ReviewMark's url evidence source accepts a non-PDF report URL in
   `file`. Covered by the contract tests.
5. 🟢 **LOW** Provider coverage: testing on SQLite only leaves PostgreSQL/SQL Server provider differences unverified.
6. 🟢 **LOW** AI-assisted review: deferred, with identity and role details to be designed.
