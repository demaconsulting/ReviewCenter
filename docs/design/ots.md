# OTS Software Integration Design

This document provides the integration and usage design for all Off-The-Shelf (OTS) software
items used by the ReviewCenter.

## Scope

OTS items are third-party tools and libraries consumed by the project. This document covers the
integration pattern and usage design for each item. The internal design of OTS items is out of
scope; only how this project integrates and uses each item is documented.

## OTS Items

The following OTS items have integration design documentation:

- ASP.NET Core (_aspnetcore.md_) — web application platform and Blazor Server component framework
- BuildMark (_buildmark.md_) — build-notes documentation tool
- bUnit (_bunit.md_) — Blazor component testing library and in-memory test host
- FileAssert (_fileassert.md_) — document assertion tool
- FileSystemGlobbing (_filesystemglobbing.md_) — glob pattern library used by the tests as a reference for the
  selection matcher
- git (_git.md_) — source control program that the ContentSources subsystem runs to read repositories
- Entity Framework Core (_efcore.md_) — object-relational mapper and SQLite provider used for persistence
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

## References

N/A
