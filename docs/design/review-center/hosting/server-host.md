### ServerHost

![Hosting Structure](HostingView.svg)

#### Purpose

`ServerHost` is the composition root of the web server. Its single responsibility is to build,
configure, and run the ASP.NET Core application behind the `serve` verb, so that service
registration, logging, and endpoint mapping live in one reviewable place.

#### Data Model

`ServerHost` is a stateless static class; every call builds an independent application, so it
is thread-safe.

**HealthPath**: `string` (constant) — `/health`; the route of the health endpoint.

**HealthyText**: `string` (constant) — `Healthy`; the body returned by the health endpoint.

**ServerHostOptions** (supporting type): An immutable options object with two optional
settings that let a caller adjust the build.

- **MapStaticAssets**: `bool` — `true` by default; when `false`, static web assets such as the
  Blazor client script are not mapped. Tests set it to `false` because an in-memory host has no
  asset manifest of its own.
- **ConfigureBuilder**: `Action<WebApplicationBuilder>?` — `null` by default; invoked after the
  standard services are registered and before the application is built. Tests use it to select
  the in-memory server and to attach a capturing logger provider.

#### Key Methods

**CreateApplication**: Builds the web application for a context without starting it.

- *Parameters*: `Context context` — supplies the listen URL and the silent option;
  `ServerHostOptions? options` — optional build adjustments, `null` for production defaults.
- *Returns*: `WebApplication` — configured and not started; the caller owns and disposes it.
- *Preconditions*: `context` is not null.
- *Postconditions*: The application listens on `context.Urls` once started; console logging is
  registered unless `context.Silent` is true; `/health` and the `App` user interface are mapped;
  a structured start-up entry is written when the application has started.

Anchors the content root at the application directory; clears the default logging providers
and adds the console provider only when the context is not silent; registers Blazor Server
components; applies the options callback; loads static web assets when enabled; builds the
application; adds request forgery protection ahead of the component endpoints; maps static assets
when enabled; maps `/health` to a plain-text 200 response; maps `App` with the interactive
server render mode; and registers the start-up log callback.

**RunAsync**: Builds and runs the web server until it is stopped.

- *Parameters*: `Context context` — parsed context; `CancellationToken cancellationToken` —
  requests a graceful shutdown.
- *Returns*: `Task<int>` — 0 for a clean stop, 1 when the server could not start.
- *Preconditions*: `context` is not null.
- *Postconditions*: The application is stopped and disposed; a start-up failure has been
  reported through `context.WriteError`.

**LogStarted**: Writes the structured start-up entry.

- *Parameters*: `ILogger logger`, `string urls` — the listen URL.
- *Returns*: `void`.

Writes an information-level entry from a message template with the named placeholder `Urls`,
rather than from an interpolated string, so the URL stays available as a structured property.
This is the mechanism behind the requirement that the listen URL be exposed as a named value.

#### Error Handling

`CreateApplication` and `RunAsync` throw `ArgumentNullException` for a null context. `RunAsync`
handles two environmental conditions locally: an `OperationCanceledException` raised while the
caller has requested cancellation is treated as a normal stop and returns 0, and an
`IOException`, which Kestrel uses to report an address that is in use or not bindable, is
reported as "Failed to start the server on {url}: {detail}" through `Context.WriteError` and
returns 1. Any other exception propagates to the caller (`Program.Main`). The application is
always disposed.

#### Dependencies

- **Context** — supplies `Urls` and `Silent`, and receives start-up error reports.
- **App** — the root component mapped as the user interface.
- **ASP.NET Core shared framework** — `WebApplication`, Kestrel, endpoint routing, request forgery protection,
  Blazor Server components, and `Microsoft.Extensions.Logging`.

#### Callers

- **Program** — calls `RunAsync` when the `serve` verb is present.
- **Tests** — call `CreateApplication` with `ServerHostOptions` to run the production pipeline
  against an in-memory server.
