# Introduction

This document provides the verification design for the ReviewCenter, a web-based formal code
review service with a companion .NET command-line interface. The service is an ASP.NET Core
Blazor Server application started with the `serve` verb; the repository also holds a separate
development tool, Puppet, that runs steps files against the web interface.

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
  - **Web** subsystem
    - **App** — HTML shell and router
    - **HomePage** — home page component
  - **SelfTest** subsystem
    - **Validation** — self-validation test runner
  - **Utilities** subsystem
    - **PathHelpers** — safe path combination utilities
- **ReviewCenterPuppet** — a separate development-tool system with its own system and unit verification:
  - **PuppetProgram** — entry point and teardown
  - **StepRunner** — steps-file parsing and step execution
  - **SelectorResolver** — element selector translation
  - **ServerProcess** — server start and stop
  - **PlaywrightBrowser** — headless Chromium through Playwright

**OTS items:**

- **ASP.NET Core** — web application platform and Blazor Server component framework
- **BuildMark** — build-notes documentation tool
- **bUnit** — Blazor component testing library and in-memory test host
- **FileAssert** — document assertion tool
- **Pandoc** — Markdown-to-HTML conversion tool
- **Playwright** — browser automation library used by the Puppet developer tool
- **ReqStream** — requirements traceability tool
- **ReviewMark** — file review enforcement tool
- **SarifMark** — SARIF report conversion tool
- **SonarMark** — SonarCloud quality report tool
- **SysML2Tools** — architecture model validation and diagram rendering tool
- **VersionMark** — tool-version documentation tool
- **WeasyPrint** — HTML-to-PDF conversion tool
- **xUnit** — unit-testing framework

The following topics are out of scope:

- Verification documents are not produced for the test projects themselves — they are the
  means of verification, not subjects of it
- Build pipeline CI configuration is excluded
- The internal implementation of OTS software items is excluded; only integration and usage
  are verified

## Folder Layout

The test folder structure mirrors the source subsystem breakdown:

```text
test/
├── DemaConsulting.ReviewCenter.Tests/         — unit, subsystem, and integration tests of ReviewCenter
└── DemaConsulting.ReviewCenter.Puppet.Tests/  — unit, system, and end-to-end tests of ReviewCenterPuppet
```

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
