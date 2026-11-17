# OTS Software Integration Design

This document provides the integration and usage design for all Off-The-Shelf (OTS) software
items used by the ReviewCenter.

## Scope

OTS items are third-party tools and libraries consumed by the project. This document covers the
integration pattern and usage design for each item. The internal design of OTS items is out of
scope; only how this project integrates and uses each item is documented.

## OTS Items

The following OTS items have integration design documentation:

- ASP.NET Core (*aspnetcore.md*) — web application platform and Blazor Server component framework
- BuildMark (*buildmark.md*) — build-notes documentation tool
- bUnit (*bunit.md*) — Blazor component testing library and in-memory test host
- FileAssert (*fileassert.md*) — document assertion tool
- FileSystemGlobbing (*filesystemglobbing.md*) — glob pattern library used by the tests as a reference for the
  selection matcher
- git (*git.md*) — source control program that the ContentSources subsystem runs to read repositories
- Entity Framework Core (*efcore.md*) — object-relational mapper and SQLite provider used for persistence
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

## References

N/A
