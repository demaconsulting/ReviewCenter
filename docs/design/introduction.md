# Introduction

This document provides the detailed design for the ReviewCenter, a web-based formal code
review service with a companion .NET command-line interface. The service is an ASP.NET Core
Blazor Server application started with the `serve` verb; the repository also holds a separate
development tool, Puppet, that drives a persistent headless browser against the web interface. This document
covers local software items (systems, subsystems, and units) and the OTS software items they
consume.

## Purpose

The purpose of this document is to describe the internal design of each software unit that
comprises the ReviewCenter and the Puppet development tool. It captures data models,
algorithms, key methods, and inter-unit interactions at a level of detail sufficient for formal
code review, compliance verification, and future maintenance. A reviewer should be able to
understand how each item satisfies its requirements without reading source code. The document
does not restate requirements; it explains how they are realized.

## Scope

Local items:

- **ReviewCenter**: the distributed software system. It is divided into the Program unit and the
  Cli, Hosting, Identity, Web, Core, Storage, SelfTest, and Utilities subsystems.
- **ReviewCenterPuppet**: a separate software system, a development tool that is built and reviewed
  in this repository but is not part of the distributed ReviewCenter package. It has no
  subsystems; its units are PuppetProgram, ControlChannel, BrowserDaemon, SelectorResolver, and
  PlaywrightBrowser.

OTS items:

- **ASP.NET Core**: integration and usage design (including cookie authentication and the Identity password hasher).
- **BuildMark**: integration and usage design.
- **bUnit**: integration and usage design.
- **Entity Framework Core**: integration and usage design (including its SQLite provider).
- **FileAssert**: integration and usage design.
- **Pandoc**: integration and usage design.
- **Playwright**: integration and usage design (used by ReviewCenterPuppet).
- **ReqStream**: integration and usage design.
- **ReviewMark**: integration and usage design.
- **SarifMark**: integration and usage design.
- **SonarMark**: integration and usage design.
- **SysML2Tools**: integration and usage design.
- **VersionMark**: integration and usage design.
- **WeasyPrint**: integration and usage design.
- **xUnit**: integration and usage design.

The following topics are out of scope:

- Design documents are not produced for the test projects or build pipeline CI configuration
- Subsystems of the target architecture that are not implemented yet (Api, ContentSources, Evidence,
  Messaging) have no design documents yet
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
- **Hosting** (subsystem): builds and runs the ASP.NET Core web host for the `serve` verb and creates users for the
  `user add` verb; contains the ServerHost and UserCommand units
- **Identity** (subsystem): the web side of local accounts, cookie sign-in and authorization, and the periodic re-check
  of the user of an open page; contains the PasswordHasher, SignIn, and RevalidatingAuthenticationStateProvider units
- **Web** (subsystem): the Blazor Server user interface; contains the App unit (HTML shell, router, page frame, and the
  SessionGate that replaces an open page when its user is no longer signed in),
  the HomePage unit, the ProjectsPage, ProjectPage, ReviewPage, and ChecklistPage units, and the LoginPage,
  AccountPage, and UsersPage units
- **Core** (subsystem): the domain model and rules for projects, reviews, review comments, review checklists,
  users, and reviewer signatures, free of web and
  database technology; contains the Fingerprint, RecordDigest, ProjectService, ReviewService, ThreadService,
  ChecklistService, UserService, and SigningService units
- **Storage** (subsystem): persistence of projects, reviews, comment threads, checklists, users, reviewer slots, and
  signatures in a database
  through Entity
  Framework Core; contains the ReviewCenterDbContext, ReviewStore, UserStore, and DatabaseSetup units
- **SelfTest** (subsystem): self-validation; contains the Validation unit
- **Utilities** (subsystem): shared helpers; contains the PathHelpers unit

The ReviewCenterPuppet system is a flat set of units that drive one persistent headless browser, one command per
invocation, against a ReviewCenter server and write screenshots.

The model is deliberately partial. It covers only the items that are implemented so far: the Program unit, the
Cli, Hosting, Identity, Web, Core, Storage, SelfTest, and Utilities subsystems, and the Puppet system. The Api,
ContentSources, Evidence, and Messaging subsystems of the target architecture (`architecture.md`) are planned and
deferred; they will be added to the model, with their requirements and design documents, as they are implemented.

## Folder Layout

- **src/** - source files and projects
  - **DemaConsulting.ReviewCenter/** - main application source
    - **Cli/** - command-line interface subsystem
    - **Hosting/** - web host subsystem
    - **Identity/** - local accounts, cookie sign-in, and authorization subsystem
    - **Web/** - Blazor Server user interface subsystem (Razor components)
    - **Storage/** - database persistence subsystem
    - **SelfTest/** - self-validation subsystem
    - **Utilities/** - shared utilities subsystem
  - **DemaConsulting.ReviewCenter.Core/** - source of the Core subsystem, in a project of its own (see below)
- **tools/** - development tools that are not distributed
  - **puppet/** - source of the ReviewCenterPuppet system
- **test/** - test projects
  - **DemaConsulting.ReviewCenter.Tests/** - tests of the ReviewCenter system, including the Core subsystem
    (`Core/` folder) and the Storage subsystem (`Storage/` folder)
  - **DemaConsulting.ReviewCenter.Puppet.Tests/** - tests of the ReviewCenterPuppet system

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
  separate project `src/DemaConsulting.ReviewCenter.Core/` (see Folder Layout), and the ReviewCenterPuppet system
  keeps its source in `tools/puppet/`
- Tests: `test/{System}.Tests/.../{Item}Tests.cs` (PascalCase for C#)

The system folders are `review-center` and `review-center-puppet`.

OTS items have integration/usage design documentation parallel to system folders:

- Requirements: `docs/reqstream/ots/{ots-name}.yaml`
- Design: `docs/design/ots/{ots-name}.md`
- Verification: `docs/verification/ots/{ots-name}.md`

Review-sets: defined in `.reviewmark.yaml`

## References

- ReviewCenter User Guide
- ReviewCenter Repository (<https://github.com/demaconsulting/ReviewCenter>)
