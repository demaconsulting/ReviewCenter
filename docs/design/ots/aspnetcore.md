## ASP.NET Core

This document describes the integration and usage design for the `ASP.NET Core` OTS software item.

### Purpose

ASP.NET Core (the `Microsoft.AspNetCore.App` shared framework, including the Kestrel web server and Blazor
Server) is the web platform on which the ReviewCenter service is built. It provides the web host, request routing,
the `/health` endpoint plumbing, static asset serving, and the Razor component model that renders the user
interface. ReviewCenter relies on it instead of implementing its own HTTP server or UI rendering.

### Features Used

- `WebApplication` host builder, dependency injection, configuration, and logging integration
- Kestrel HTTP server listening on the URLs given by the `serve` verb
- Minimal API endpoint mapping for the `/health` route
- Razor components with interactive server rendering (Blazor Server), including the router and `PageTitle`
- Request forgery protection middleware and static asset mapping required by Razor components
- `Microsoft.Extensions.Logging` structured logging

### Integration Pattern

ASP.NET Core is consumed through the web SDK of the `DemaConsulting.ReviewCenter` project, which references the shared
framework; no separate package reference is needed. The `ServerHost` unit builds and runs the `WebApplication`, and
the units of the `Web` subsystem are Razor components rendered by it. The shared framework ships with the .NET
runtime, so there is no initialization beyond the host startup performed by `ServerHost`; the host is disposed when
the server stops. Tests exercise the framework through the in-process test host and through a real server process
started with the `serve` verb.
