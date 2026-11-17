# OTS Software Verification

This section provides verification evidence for all Off-The-Shelf (OTS) software items
used by the ReviewCenter.

## Scope

Each OTS item is verified by a combination of:

- The OTS tool's own self-validation suite (where the tool supports `--validate`)
- Pipeline output assertions performed by FileAssert on documents produced by the tool
- Integration evidence from the project's own tests for libraries and frameworks that have no self-validation mode
  (ASP.NET Core, bUnit, Entity Framework Core, FileSystemGlobbing, git, Microsoft.AspNetCore.OpenApi,
  Microsoft.AspNetCore.Authentication.OpenIdConnect, OpenIddict, Otp.NET, MailKit, Playwright,
  System.Security.Cryptography.Pkcs, xUnit, and YamlDotNet)

Internal OTS tool design is out of scope; only integration and usage evidence is documented.

## OTS Items

The following OTS items have verification evidence in this section:

- ASP.NET Core (*aspnetcore.md*) — web application platform and Blazor Server component framework
- BuildMark (*buildmark.md*) — build-notes documentation tool
- bUnit (*bunit.md*) — Blazor component testing library and in-memory test host
- Entity Framework Core (*efcore.md*) — object-relational mapper and SQLite provider used by the Storage subsystem
- FileAssert (*fileassert.md*) — document assertion tool
- FileSystemGlobbing (*filesystemglobbing.md*) — glob pattern library used by the tests as a reference for the
  selection matcher
- git (*git.md*) — source control program that the ContentSources subsystem runs to read repositories
- Microsoft.AspNetCore.OpenApi (*openapi.md*) — OpenAPI document generator used by the Api subsystem
- Microsoft.AspNetCore.Authentication.OpenIdConnect (*openidconnect.md*) — OpenID Connect sign-in handler used by the
  Identity subsystem
- OpenIddict (*openiddict.md*) — OpenID Connect server library used by the tests as a fake identity provider
- Otp.NET (*otpnet.md*) — one-time password library used by the Identity subsystem for the second factor
- MailKit (*mailkit.md*) — mail library used by the Messaging subsystem to send e-mail and to build the messages
- Pandoc (*pandoc.md*) — Markdown-to-HTML conversion tool
- System.Security.Cryptography.Pkcs (*pkcs.md*) — CMS signing library used to sign the PDF attestations
- Playwright (*playwright.md*) — browser automation library used by the system tests
- ReqStream (*reqstream.md*) — requirements traceability tool
- ReviewMark (*reviewmark.md*) — file review enforcement tool
- SarifMark (*sarifmark.md*) — SARIF report conversion tool
- SonarMark (*sonarmark.md*) — SonarCloud quality report tool
- SysML2Tools (*sysml2tools.md*) — architecture model validation and diagram rendering tool
- VersionMark (*versionmark.md*) — tool-version documentation tool
- WeasyPrint (*weasyprint.md*) — HTML-to-PDF conversion tool
- xUnit (*xunit.md*) — unit-testing framework
- YamlDotNet (*yamldotnet.md*) — YAML library that reads the review-sets of the configuration file of a repository
