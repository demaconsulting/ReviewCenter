# Introduction

This document provides the detailed design for the ReviewCenter, a web-based formal code
review service with a companion .NET command-line interface. The service is an ASP.NET Core
Blazor Server application started with the `serve` verb. This document covers local software items (systems,
subsystems, and units) and the OTS software items they consume.

## Purpose

The purpose of this document is to describe the internal design of each software unit that
comprises the ReviewCenter. It captures data models,
algorithms, key methods, and inter-unit interactions at a level of detail sufficient for formal
code review, compliance verification, and future maintenance. A reviewer should be able to
understand how each item satisfies its requirements without reading source code. The document
does not restate requirements; it explains how they are realized.

## Scope

Local items:

- **ReviewCenter**: the distributed software system. It is divided into the Program unit and the
  Cli, Hosting, Identity, Web, Core, Messaging, Storage, ContentSources, Evidence, Api, SelfTest, and Utilities
  subsystems.

OTS items:

- **ASP.NET Core**: integration and usage design (including cookie authentication and the Identity password hasher).
- **BuildMark**: integration and usage design.
- **bUnit**: integration and usage design.
- **Entity Framework Core**: integration and usage design (including its SQLite provider).
- **FileAssert**: integration and usage design.
- **Microsoft.AspNetCore.Authentication.OpenIdConnect**: integration and usage design (used for sign-in with an OpenID
  Connect provider).
- **Microsoft.AspNetCore.OpenApi**: integration and usage design (used to generate the OpenAPI document of the API).
- **MailKit**: integration and usage design (used to send e-mail through an SMTP server and to build the messages).
- **OpenIddict**: integration and usage design (used only by the tests as a fake identity provider).
- **Otp.NET**: integration and usage design (used to compute and check the one-time codes of the second factor).
- **Pandoc**: integration and usage design.
- **Playwright**: integration and usage design (used by the system tests to drive the web interface in a browser).
- **System.Security.Cryptography.Pkcs**: integration and usage design (used to sign the PDF attestations).
- **ReqStream**: integration and usage design.
- **ReviewMark**: integration and usage design.
- **SarifMark**: integration and usage design.
- **SonarMark**: integration and usage design.
- **SysML2Tools**: integration and usage design.
- **VersionMark**: integration and usage design.
- **WeasyPrint**: integration and usage design.
- **xUnit**: integration and usage design.
- **YamlDotNet**: integration and usage design (used to read the review-sets of a ReviewMark configuration file).

The following topics are out of scope:

- Design documents are not produced for the test projects, the test-support Puppeting library, or build pipeline CI
  configuration
- The Puppet command-line tool (`tools/puppet/`) is a non-shipping tool that lets AI agents drive the web interface;
  like other test tooling it is verified by its automated tests and is not part of the system design
- Subsystems of the target architecture that are not implemented yet have no design documents yet
- The internal design of OTS software items is excluded; only integration and usage design is documented

## Software Structure

The software structure is modeled in SysML2 under `docs/sysml2/` and rendered to the diagram
below by SysML2Tools as part of the build pipeline. AI agents should query the SysML2 model
directly (see the `sysml2tools-query` skill) rather than parsing this diagram or the prose
elsewhere in this document. The model captures the local software systems, subsystems, and
units; the OTS items listed in Scope above are documented separately (see `docs/design/ots.md`)
rather than modeled as SysML2 parts.

![Software Structure](SoftwareStructureView.svg)

The ReviewCenter system is divided as follows:

- **Program** (unit): entry point and execution orchestrator
- **Cli** (subsystem): argument parsing and output; contains the Context unit
- **Hosting** (subsystem): builds and runs the ASP.NET Core web host for the `serve` verb, creates users for the
  `user add` verb, verifies the audit trail for the `audit verify` verb, and rotates the Data Protection key for the
  `keys reprotect` verb; contains the ServerHost, KeyRingProtection, SecretScan, UserCommand, AuditCommand, and
  KeysCommand units
- **Identity** (subsystem): the web side of local accounts, cookie sign-in and authorization, and the periodic re-check
  of the user of an open page, the in-memory limit on failed sign-in attempts, sign-in with an OpenID Connect
  provider, and the optional one-time password second factor of local accounts; contains the PasswordHasher, SignIn,
  SignInThrottle, OidcSignIn, RevalidatingAuthenticationStateProvider, TotpAlgorithm, TotpSecretProtector, and
  TotpSignIn units
- **Web** (subsystem): the Blazor Server user interface; contains the App unit (HTML shell, router, page frame with
  the menu bar and the footer, and the SessionGate that replaces an open page when its user is no longer signed in),
  the HomePage, AboutPage, and HelpPage units, the ProjectsPage, ProjectPage (with the new review and member pages),
  PlanPage, ReviewPage, and ChecklistPage
  units, the LoginPage, TotpLoginPage,
  AccountPage, and UsersPage units, the HistoryPage and AuditPage (with the project audit page) units, the
  RepositoriesPage unit, and the InboxPage unit
- **Core** (subsystem): the domain model and rules for projects, reviews, review comments, review checklists,
  users, personal access tokens, second factor enrollments, reviewer signatures, the inbox and read state of a user,
  the e-mail address of a user for notifications, its confirmation, and the choice of when to get e-mail, the
  composition, scheduling, and dispatch of the notification mails, the hash-chained audit trail, and the
  registry of git
  repositories, free of web and database technology; contains the Fingerprint, RecordDigest, AuditChain, AuditService,
  ProjectService, EvidenceSettingsService, ProjectChecklistService, ReviewProgressService, ReviewService, ThreadService,
  ChecklistService,
  UserService,
  PersonalAccessTokenService,
  SigningService, ReauthProofStore, TotpService, TotpChallengeStore, RepositoryService, SelectionMatcher,
  RevisionValidator, ReviewPlanner, LineDiff, InboxService, UserImpactService, BallotConsolidation,
  BallotVisibility, ReviewStatusService, NotificationAddress, NotificationMessage, NotificationPreferenceService,
  NotificationComposer, DigestSchedule, and NotificationDispatcher units
- **Messaging** (subsystem): the optional sending of e-mail: the settings of the `Notifications` section, the sender
  that delivers a message through an SMTP server, the sender that writes the message to a file for development
  and demonstrations, and the background worker that runs the notification pass at the polling interval; contains the
  NotificationOptions, SmtpNotificationSender, FileNotificationSender, and NotificationWorker units
- **Storage** (subsystem): persistence of projects, reviews, comment threads, checklists, users, personal access
  tokens, second factor enrollments, reviewer slots, signatures, the read state of the inbox, the notification address,
  mode, and dispatch state of a user, the audit trail, and the
  registered repositories in a
  database
  through Entity
  Framework Core; contains the ReviewCenterDbContext, ReviewStore, UserStore, PersonalAccessTokenStore, AuditStore,
  RepositoryStore, TotpStore, InboxStore, NotificationStore, and DatabaseSetup units
- **ContentSources** (subsystem): reads content from outside the system, currently from git repositories, and protects
  the access tokens of private repositories; contains the GitRunner, GitSource, ReviewSetParser, and CredentialProtector
  units
- **Evidence** (subsystem): produces the signed PDF attestation of a complete review; contains the PdfDocumentWriter,
  AttestationRenderer, AttestationSigner, AttestationKeyProvider, AttestationService, AttestationEndpoint, and
  EvidenceIndexService units
- **Api** (subsystem): the JSON API under `/api` for scripts and pipelines, with authentication by personal
  access tokens (a bearer token, or the password of HTTP Basic credentials) that is limited to that API, its endpoints,
  including the evidence index for ReviewMark, and its OpenAPI document; contains the
  ApiTokenAuthentication, ApiEndpoints, and ApiDocument units
- **SelfTest** (subsystem): self-validation; contains the Validation and ValidationChecks units
- **Utilities** (subsystem): shared helpers; contains the PathHelpers, TemporaryDirectory, and SecretSource units

The model is deliberately partial. It covers only the items that are implemented so far: the Program unit, the
Cli, Hosting, Identity, Web, Core, Messaging, Storage, ContentSources, Evidence, Api, SelfTest, and Utilities
subsystems. The notification worker of the target architecture (`architecture.md`) is part of
the Messaging subsystem; the mails about reviews are optional and off by default. Other items of the target
architecture that are planned and deferred will be added, with their requirements and design documents, as they are
implemented.

## Folder Layout

- **src/** - source files and projects
  - **DemaConsulting.ReviewCenter/** - main application source
    - **Cli/** - command-line interface subsystem
    - **Hosting/** - web host subsystem
    - **Identity/** - local accounts, cookie sign-in, and authorization subsystem
    - **Web/** - Blazor Server user interface subsystem (Razor components)
    - **Messaging/** - e-mail notification settings, senders, and worker subsystem
    - **Storage/** - database persistence subsystem
    - **ContentSources/** - git content source and token protection subsystem
    - **Evidence/** - signed PDF attestation subsystem
    - **Api/** - token-authenticated JSON API subsystem
    - **SelfTest/** - self-validation subsystem
    - **Utilities/** - shared utilities subsystem
  - **DemaConsulting.ReviewCenter.Core/** - source of the Core subsystem, in a project of its own (see below)
- **tools/** - development tools that are not distributed
  - **puppet/** - source of the Puppet command-line tool for AI agents (not part of the system design)
  - **puppet.Tests/** - tests of the Puppet command-line tool
- **docs/api/** - the committed snapshot of the OpenAPI document of the API (`openapi.json`), compared by a test
- **docs/user_guide/** - the user guide, one Markdown file for each chapter (introduction, installation, first review,
  deployment, sign-in, identity providers, administration, reviewing, review practices, automation, reference),
  assembled in the order that `definition.yaml` lists
- **test/** - test projects
  - **DemaConsulting.ReviewCenter.Tests/** - tests of the ReviewCenter system, including the Core subsystem
    (`Core/` folder) and the Storage subsystem (`Storage/` folder)
  - **DemaConsulting.ReviewCenter.SystemTests/** - system tests of the ReviewCenter system that drive the real server
    in a real browser
  - **DemaConsulting.ReviewCenter.Puppeting/** - test-support library that drives the web interface in headless
    Chromium as an object in the test process
  - **DemaConsulting.ReviewCenter.Puppeting.Tests/** - tests of the Puppeting library

The Core subsystem deliberately deviates from the rule that a subsystem lives in a folder of the main project. It is
a separate class library (namespace `DemaConsulting.ReviewCenter.Core`) so that the domain rules and the fingerprint
calculation cannot depend on the web framework or on the database library: the compiler enforces that the project
has no such reference. The rule that normally holds the units of a subsystem together is kept in other respects: the
Core units are in the root of that project, and their requirements, design, verification, tests, and review-sets
follow the same tree as every other subsystem. The Core project is compiled into the single distributed executable
together with the main project, so the deviation has no effect on packaging.

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Section headings within each unit chapter follow a consistent structure: overview, data model,
  methods/algorithms, and interactions with other units.
- Text tables are used in preference to diagrams, which may not render in all PDF viewers.

## Companion Artifact Structure

Local software items have corresponding artifacts in parallel directory trees:

- Requirements: `docs/reqstream/{system}/.../{item}.yaml` (kebab-case)
- Design docs: `docs/design/{system}/.../{item}.md` (kebab-case)
- Verification design: `docs/verification/{system}/.../{item}.md` (kebab-case)
- Source code: `src/{System}/.../{Item}.cs` (PascalCase for C#); the Core subsystem keeps its source in the
  separate project `src/DemaConsulting.ReviewCenter.Core/` (see Folder Layout)
- Tests: `test/{System}.Tests/.../{Item}Tests.cs` (PascalCase for C#)

The system folder is `review-center`.

OTS items have integration/usage design documentation parallel to system folders:

- Requirements: `docs/reqstream/ots/{ots-name}.yaml`
- Design: `docs/design/ots/{ots-name}.md`
- Verification: `docs/verification/ots/{ots-name}.md`

Review-sets: defined in `.reviewmark.yaml`

## References

- ReviewCenter User Guide
- ReviewCenter Repository (<https://github.com/demaconsulting/ReviewCenter>)
