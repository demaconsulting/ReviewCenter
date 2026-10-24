## Web

![Web Structure](WebView.svg)

### Overview

The `Web` subsystem is the Blazor Server user interface of ReviewCenter. It owns what a browser
shows and how it responds to user actions. It contains five units: `App`, the HTML shell and
URL router; `HomePage`, the interactive landing page served at the root URL; and `ProjectsPage`,
`ProjectPage`, and `ReviewPage`, the pages for projects and reviews. The subsystem
does not start or configure the server; the `Hosting` subsystem maps `App` as the root
component of the server and supplies the endpoints around it. The pages use the `Core` subsystem for
all domain behavior and never access the database.

### Interfaces

**Root route** (`GET /`): The home page.

- *Type*: HTTP `GET`, followed by a live Blazor connection for interactivity.
- *Role*: Provider.
- *Contract*: Returns an HTML document titled "ReviewCenter" containing the home page with a
  Hello button, a counter button, and a count. After the client script has connected, button
  clicks are handled on the server and the page updates without reloading.
- *Constraints*: The document carries a `data-interactive` marker that is `false` in the
  server-rendered response and `true` once the live connection is established.

**Project routes** (`GET /projects`, `GET /projects/{id}`, `GET /projects/{id}/reviews/{reviewId}`): The
pages for projects and reviews.

- *Type*: HTTP `GET`, followed by a live Blazor connection for interactivity.
- *Role*: Provider.
- *Contract*: `/projects` lists the projects and has a form to create one; `/projects/{id}` shows a
  project with its reviews and a form that creates a review from selected files; the review route shows
  the review, its fingerprint, its files, and the content of a selected file. A project or review that
  does not exist is reported on the page itself.
- *Constraints*: Every page carries the same `data-interactive` marker as the home page. There is no
  authentication: every visitor is the same single anonymous operator (a temporary limitation).

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

The subsystem has no dependency on `Context`. It depends on the `Core` subsystem (the services, the
domain types, and the limits) and on the ASP.NET Core Blazor components framework.

#### Pages and domain access

`ProjectsPage`, `ProjectPage`, and `ReviewPage` follow the pattern of `HomePage`: a Razor
presentation file and a code-behind file, interactive server rendering, and the `data-interactive`
marker. They obtain `ProjectService`, `ReviewService`, and (`ReviewPage`) `ThreadService` from the service
container and call them
from their handlers. Each page loads its data when it is created, so a connection that is
established after the server-rendered snapshot shows current data. A rule violation reported by
Core as a `DomainRuleException` is shown as an error message next to the form, with the entered
values kept; the page never lets the exception escape. Forms are plain `<form>` elements handled
by `@onsubmit`, so Enter submits them and tests can submit them without a button click.

The pages enforce nothing that Core does not also enforce, with one exception: the file input of
`ProjectPage` checks the upload limits (file count, size of each file, total size) from the
sizes the browser reports, before reading any file, so that an oversized selection is rejected
without being read into memory. The limits themselves are defined once in Core.

Upload paths are the file name only. A browser file input does not expose folder paths, so
`ProjectPage` records each selected file under its browser file name; two selected files with
the same name are therefore refused by Core as a duplicate path. Directory-structured uploads are
a later feature.

#### Test identifiers

Every interactive element (`a`, `button`, `input`, `select`, `textarea`) of every page shall have a
stable, kebab-case `data-testid` attribute, so that every interactive element can be addressed by automated
tests and by the Puppet tool without depending on text, position, or style. Key displayed values have identifiers
too, and elements that repeat carry the review id, project id, or list index in the identifier. Each page root is
a `main` element with its own identifier. The rule is enforced by a test that renders every page and checks
every interactive element.

#### Temporary limitations

The subsystem has no authentication or authorization yet. The whole interface is open to every
visitor, who acts as one anonymous operator. This is a deliberate, temporary limitation of the
first domain slice, and the server therefore listens on the loopback address by default. The
Identity subsystem will replace it.
