## Web

![Web Structure](WebView.svg)

### Overview

The `Web` subsystem is the Blazor Server user interface of ReviewCenter. It owns what a browser
shows and how it responds to user actions. It contains two units: `App`, the HTML shell and
URL router, and `HomePage`, the interactive landing page served at the root URL. The subsystem
does not start or configure the server; the `Hosting` subsystem maps `App` as the root
component of the server and supplies the endpoints around it.

### Interfaces

**Root route** (`GET /`): The home page.

- *Type*: HTTP `GET`, followed by a live Blazor connection for interactivity.
- *Role*: Provider.
- *Contract*: Returns an HTML document titled "ReviewCenter" containing the home page with a
  Hello button, a counter button, and a count. After the client script has connected, button
  clicks are handled on the server and the page updates without reloading.
- *Constraints*: The document carries a `data-interactive` marker that is `false` in the
  server-rendered response and `true` once the live connection is established.

**Unknown routes**: Any other URL.

- *Type*: HTTP `GET`.
- *Role*: Provider.
- *Contract*: The shell is returned with a "Page not found." message in place of a page.
- *Constraints*: None.

**Client script** (`/_framework/blazor.web.js`): Referenced by the shell.

- *Type*: HTTP `GET` of a static web asset.
- *Role*: Consumer (the asset is served by the host).
- *Contract*: Opens the live connection that makes interactive pages work.
- *Constraints*: Must be served by the host; without it pages render but do not respond to
  clicks.

**App (mapped)**: The root component mapped by `ServerHost`.

- *Type*: In-process .NET type.
- *Role*: Provider.
- *Contract*: Renders the HTML shell and routes the URL to a page component.
- *Constraints*: None.

### Design

The subsystem uses the standard Blazor Server hosting model. `App` is rendered as static
server-side markup: it emits the HTML document, the page head outlet, the router, and the
client script reference. `Routes` (part of the `App` unit) matches the requested URL to a page
component, with `HomePage` registered at the root route, and renders a plain not-found
message for unmatched URLs. `HomePage` opts in to interactive server rendering, so only the
page, not the shell, holds a live connection.

The first response to a browser is a server-rendered snapshot of `HomePage` with the
`data-interactive` marker set to `false`. When the client script connects, the page is
rendered again on a live connection, its first render completes, and the marker becomes
`true`. Drivers such as tests and tooling wait for the marker instead of using fixed delays.
Each connection has its own page instance, so the page state is per user and needs no
synchronization.

The subsystem has no dependency on `Context` or on any other ReviewCenter subsystem. It
depends only on the ASP.NET Core Blazor components framework.
