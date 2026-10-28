# Introduction

This document provides the verification design for the ReviewCenter, a web-based formal code
review service with a companion .NET command-line interface. The service is an ASP.NET Core
Blazor Server application started with the `serve` verb; the repository also holds a separate
development tool, Puppet, that drives a persistent headless browser against the web interface.

## Purpose

The purpose of this document is to describe how each requirement for the ReviewCenter is
verified. For every software item — system, subsystem, and unit — this document names the
verification approach, identifies the test scenarios (including boundary conditions and error
paths), describes what is mocked or stubbed, and maps each requirement to at least one named
test scenario. The document does not restate design; it explains how the design is proven correct.

## Scope

This document covers the verification design for the following software items:

**Local items:**

- **ReviewCenter** — system, subsystem, and unit verification:
  - **Program** — entry point and execution orchestrator
  - **Cli** subsystem
    - **Context** — argument parser and I/O owner
  - **Hosting** subsystem
    - **ServerHost** — web host builder and runner
    - **UserCommand** — the `user add` verb
    - **AuditCommand** — the `audit verify` verb
  - **Identity** subsystem
    - **PasswordHasher** — password hashing and verification
    - **SignIn** — cookie sign-in, authorization, and the signed-in actor
    - **SignInThrottle** — in-memory limit on failed sign-in attempts
    - **OidcSignIn** — sign-in with an OpenID Connect provider and confirmation of identity for a signature
    - **RevalidatingAuthenticationStateProvider** — periodic re-check of the user of an open page
  - **Web** subsystem
    - **App** — HTML shell, router, page frame, and the session gate
    - **HomePage** — home page component
    - **ProjectsPage** — projects list and creation page
    - **ProjectPage** — project page with review creation from uploaded files
    - **PlanPage** — planning of reviews from the review-sets of a repository
    - **ReviewPage** — review, fingerprint, files, file content, comment threads, reviewers, and signatures page
    - **ChecklistPage** — review checklist page
    - **LoginPage** — sign-in page
    - **AccountPage** — password change, appearance, and personal access token page of the signed-in user
    - **UsersPage** — user administration page
    - **HistoryPage** — audit history of one review
    - **AuditPage** — whole audit trail and chain verification for administrators
    - **RepositoriesPage** — git repository registry administration page
  - **Api** subsystem
    - **ApiTokenAuthentication** — bearer personal access token sign-in of the API
    - **ApiEndpoints** — JSON routes under /api
    - **ApiDocument** — OpenAPI document of the API
  - **ContentSources** subsystem
    - **GitRunner** — safe git child-process runner
    - **GitSource** — git implementation of the repository provider
    - **ReviewSetParser** — reading of the review-sets of a ReviewMark configuration file
    - **CredentialProtector** — token encryption through Data Protection
  - **Evidence** subsystem
    - **PdfDocumentWriter** — PDF document writer with embedded standard fonts and text layout
    - **AttestationRenderer** — layout of the attestation document of a review
    - **AttestationSigner** — detached CMS signature of a PDF with the attestation key
    - **AttestationKeyProvider** — selection, loading, and description of the attestation signing key
    - **AttestationService** — approval check, rendering, signing, and audit of an attestation
    - **AttestationEndpoint** — authorized download endpoint of the attestation
  - **Core** subsystem
    - **Fingerprint** — ReviewMark-compatible content fingerprint
    - **RecordDigest** — digest of a review record that a signature binds to
    - **AuditChain** — audit entry types, canonical hash, and streaming chain verifier
    - **AuditService** — recording, reading, and verification of the audit trail
    - **ProjectService** — project rules
    - **EvidenceSettingsService** — evidence settings rules
    - **ReviewService** — review rules
    - **ThreadService** — review comment rules
    - **ChecklistService** — review checklist rules, default checklist, and progress
    - **UserService** — user, password, lockout, and last administrator rules
    - **SigningService** — reviewer slot, re-authenticated signature, and approval rules
    - **ReauthProofStore** — in-memory single-use proof of a confirmation of identity at a provider
    - **SelectionMatcher** — include and exclude pattern matching for file selection
    - **RevisionValidator** — git revision name validation
    - **RepositoryService** — repository registry rules
    - **ReviewPlanner** — review planning from review-sets, baselines, and fallbacks
    - **PersonalAccessTokenService** — personal access token rules
  - **Storage** subsystem
    - **ReviewCenterDbContext** — database schema mapping
    - **ReviewStore** — database implementation of the store interface
    - **UserStore** — database implementation of the user store interface
    - **AuditStore** — database implementation of the audit store interface and the shared audit appender
    - **RepositoryStore** — database implementation of the repository store interface
    - **PersonalAccessTokenStore** — database implementation of the personal access token store interface
    - **DatabaseSetup** — provider selection, connection resolution, and database creation
  - **SelfTest** subsystem
    - **Validation** — self-validation test runner
  - **Utilities** subsystem
    - **PathHelpers** — safe path combination utilities
- **ReviewCenterPuppet** — a separate development-tool system with its own system and unit verification:
  - **PuppetProgram** — command-line parsing, start, and result printing
  - **ControlChannel** — control file and loopback protocol
  - **BrowserDaemon** — the persistent browser process
  - **SelectorResolver** — element selector translation
  - **PlaywrightBrowser** — headless Chromium through Playwright

**OTS items:**

- **ASP.NET Core** — web application platform and Blazor Server component framework
- **BuildMark** — build-notes documentation tool
- **bUnit** — Blazor component testing library and in-memory test host
- **Entity Framework Core** — object-relational mapper and SQLite provider
- **FileAssert** — document assertion tool
- **Microsoft.AspNetCore.Authentication.OpenIdConnect** — OpenID Connect sign-in handler
- **Microsoft.AspNetCore.OpenApi** — OpenAPI document generation for the API
- **OpenIddict** — OpenID Connect server library used by the tests as a fake identity provider
- **Pandoc** — Markdown-to-HTML conversion tool
- **Playwright** — browser automation library used by the Puppet developer tool
- **ReqStream** — requirements traceability tool
- **ReviewMark** — file review enforcement tool
- **SarifMark** — SARIF report conversion tool
- **SonarMark** — SonarCloud quality report tool
- **SysML2Tools** — architecture model validation and diagram rendering tool
- **System.Security.Cryptography.Pkcs** — CMS signing library used to sign the PDF attestations
- **VersionMark** — tool-version documentation tool
- **WeasyPrint** — HTML-to-PDF conversion tool
- **xUnit** — unit-testing framework
- **YamlDotNet** — YAML reading library used for the review-set configuration file

The following topics are out of scope:

- Verification documents are not produced for the test projects themselves — they are the
  means of verification, not subjects of it
- Build pipeline CI configuration is excluded
- The internal implementation of OTS software items is excluded; only integration and usage
  are verified

## Folder Layout

The test folder structure mirrors the source subsystem breakdown. The Core subsystem has its own source project
but its tests are in the main test project, in the `Core/` folder, next to the `Storage/`, `ContentSources/`,
`Evidence/`, `Identity/`, `Web/`, `Api/`,
and `Hosting/` folders:

```text
test/
├── DemaConsulting.ReviewCenter.Tests/         — unit, subsystem, and integration tests of ReviewCenter
│   ├── Core/                                  — Core unit and subsystem tests
│   ├── Storage/                               — Storage unit and subsystem tests
│   ├── ContentSources/                        — ContentSources unit and subsystem tests
│   ├── Evidence/                              — Evidence unit and subsystem tests
│   ├── Identity/                              — Identity unit and subsystem tests
│   ├── Web/                                   — Web unit and subsystem tests
│   ├── Api/                                   — Api unit and subsystem tests
│   └── Hosting/                               — Hosting unit and subsystem tests
└── DemaConsulting.ReviewCenter.Puppet.Tests/  — unit, system, and end-to-end tests of ReviewCenterPuppet
```

Tests that touch a database use temporary SQLite files created by a shared test helper in a private temporary
folder, so no test reads or changes the real data folder of the user running the tests.

## Companion Artifact Structure

In-house items have corresponding artifacts in parallel directory trees:

- Requirements: `docs/reqstream/{system}/.../{item}.yaml` (kebab-case)
- Design docs: `docs/design/{system}/.../{item}.md` (kebab-case)
- Verification design: `docs/verification/{system}/.../{item}.md` (kebab-case)
- Source code: `src/{System}/.../{Item}.cs` (PascalCase for C#); the ReviewCenterPuppet system keeps
  its source in `tools/puppet/`
- Tests: `test/{System}.Tests/.../{Item}Tests.cs` (PascalCase for C#)

The system folders are `review-center` and `review-center-puppet`.

OTS items have parallel artifacts in:

- Requirements: `docs/reqstream/ots/{ots-name}.yaml` (kebab-case)
- Verification: `docs/verification/ots/{ots-name}.md` (kebab-case)

Review-sets: defined in `.reviewmark.yaml`

## References

- ReviewCenter Software Design Document
- ReviewCenter releases (<https://github.com/demaconsulting/ReviewCenter/releases>)
