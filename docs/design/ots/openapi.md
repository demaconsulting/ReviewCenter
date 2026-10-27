## Microsoft.AspNetCore.OpenApi

This document describes the integration and usage design for the `Microsoft.AspNetCore.OpenApi` OTS software item
(NuGet package `Microsoft.AspNetCore.OpenApi`).

### Purpose

Microsoft.AspNetCore.OpenApi is the package of Microsoft that builds an OpenAPI document from the endpoints of an ASP.NET
Core application, using the metadata of the endpoints (route patterns, methods, parameters, the types of requests and
answers, and the declared status codes). ReviewCenter uses it in the distributed application to describe the JSON API
under `/api`, so that scripts and tools can learn the routes, the bodies, and the bearer security scheme from the running
server. The package is not part of the shared framework of ASP.NET Core, so it is a reference of the application
project, and its document model (`Microsoft.OpenApi`) comes with it.

### Features Used

- `AddOpenApi` with an `OpenApiOptions` object whose `ShouldInclude` filter limits the document to the routes that
  start with `api/`, and with a document transformer that writes the fixed parts of the document
- `MapOpenApi` at `/api/openapi.json`, to serve the generated document
- The document model (`OpenApiDocument`, `OpenApiInfo`, `OpenApiSecurityScheme`, `OpenApiSecurityRequirement`), to set the
  title, a fixed version, no servers, the HTTP bearer scheme, and the security requirement of every operation
- The endpoint metadata that the generator reads (names, summaries, declared results, and the exclusion of an endpoint
  from the description)

### Integration Pattern

Only the `ApiDocument` unit uses the package, so that no other unit depends on it. The unit registers the generator,
maps the document endpoint with anonymous access (the document holds no secret), and has the transformer set the title,
the fixed version `1`, an empty server list, and the bearer scheme. The generated output is made the same on every
machine and run by that choice of fixed values (no assembly version, no server address). `ApiEndpoints` contributes only
ordinary endpoint metadata. The package has no self-validation mode: the passing tests of `ApiDocument`, which fetch the
document from a running server, compare it with the snapshot kept in `docs/api/openapi.json`, and compare the output of
two separate servers, are the integration evidence. An upgrade of the package that changes the generated output shows
as a failing snapshot test and a reviewed change of the snapshot.
