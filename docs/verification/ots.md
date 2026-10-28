# OTS Software Verification

This section provides verification evidence for all Off-The-Shelf (OTS) software items
used by the ReviewCenter.

## Scope

Each OTS item is verified by a combination of:

- The OTS tool's own self-validation suite (where the tool supports `--validate`)
- Pipeline output assertions performed by FileAssert on documents produced by the tool
- Integration evidence from the project's own tests for libraries and frameworks that have no self-validation mode
  (ASP.NET Core, bUnit, Entity Framework Core, FileSystemGlobbing, git, Microsoft.AspNetCore.OpenApi,
  Microsoft.AspNetCore.Authentication.OpenIdConnect, OpenIddict, Playwright, System.Security.Cryptography.Pkcs,
  xUnit, and YamlDotNet)

Internal OTS tool design is out of scope; only integration and usage evidence is documented.

## OTS Items

The following OTS items have verification evidence in this section:

- ASP.NET Core (_aspnetcore.md_) — web application platform and Blazor Server component framework
- BuildMark (_buildmark.md_) — build-notes documentation tool
- bUnit (_bunit.md_) — Blazor component testing library and in-memory test host
- Entity Framework Core (_efcore.md_) — object-relational mapper and SQLite provider used by the Storage subsystem
- FileAssert (_fileassert.md_) — document assertion tool
- FileSystemGlobbing (_filesystemglobbing.md_) — glob pattern library used by the tests as a reference for the
  selection matcher
- git (_git.md_) — source control program that the ContentSources subsystem runs to read repositories
- Microsoft.AspNetCore.OpenApi (_openapi.md_) — OpenAPI document generator used by the Api subsystem
- Microsoft.AspNetCore.Authentication.OpenIdConnect (_openidconnect.md_) — OpenID Connect sign-in handler used by the
  Identity subsystem
- OpenIddict (_openiddict.md_) — OpenID Connect server library used by the tests as a fake identity provider
- Pandoc (_pandoc.md_) — Markdown-to-HTML conversion tool
- System.Security.Cryptography.Pkcs (_pkcs.md_) — CMS signing library used to sign the PDF attestations
- Playwright (_playwright.md_) — browser automation library used by the Puppet developer tool
- ReqStream (_reqstream.md_) — requirements traceability tool
- ReviewMark (_reviewmark.md_) — file review enforcement tool
- SarifMark (_sarifmark.md_) — SARIF report conversion tool
- SonarMark (_sonarmark.md_) — SonarCloud quality report tool
- SysML2Tools (_sysml2tools.md_) — architecture model validation and diagram rendering tool
- VersionMark (_versionmark.md_) — tool-version documentation tool
- WeasyPrint (_weasyprint.md_) — HTML-to-PDF conversion tool
- xUnit (_xunit.md_) — unit-testing framework
- YamlDotNet (_yamldotnet.md_) — YAML library that reads the review-sets of the configuration file of a repository
