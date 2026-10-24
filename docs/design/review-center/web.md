## Web

![Web Structure](WebView.svg)

### Overview

The `Web` subsystem is the Blazor Server user interface of ReviewCenter. It owns what a browser
shows and how it responds to user actions. It contains nine units: `App`, the HTML shell, the URL router, and the
page frame with the sign-in header; `HomePage`, the interactive landing page served at the root URL; `ProjectsPage`,
`ProjectPage`, `ReviewPage`, and `ChecklistPage`, the pages for projects, reviews, and review checklists; and
`LoginPage`, `AccountPage`, and `UsersPage`, the pages for signing in, changing one's own password, and managing
users. The subsystem
does not start or configure the server; the `Hosting` subsystem maps `App` as the root
component of the server and supplies the endpoints around it, and the `Identity` subsystem supplies the sign-in and
the authorization the pages rely on. The pages use the `Core` subsystem for
all domain behavior and never access the database.

### Interfaces

**Root route** (`GET /`): The home page.

- *Type*: HTTP `GET`, followed by a live Blazor connection for interactivity.
- *Role*: Provider.
- *Contract*: Returns an HTML document titled "ReviewCenter" containing the home page with a
  Hello button, a counter button, and a count. After the client script has connected, button
  clicks are handled on the server and the page updates without reloading.
- *Constraints*: The document carries a `data-interactive` marker that is `false` in the
  server-rendered response and `true` once the live connection is established. The page is open to anonymous
  visitors.

**Login route** (`GET /login`): The sign-in page.

- *Type*: HTTP `GET`; there is no live connection, because the page is a plain server-rendered form.
- *Role*: Provider.
- *Contract*: Shows a form that posts the user name, the password, and the return address to the login endpoint of the
  `Identity` subsystem, and a generic failure message when the query value `error` is present.
- *Constraints*: Open to anonymous visitors.

**Project routes** (`GET /projects`, `GET /projects/{id}`, `GET /projects/{id}/reviews/{reviewId}`,
`GET /projects/{id}/reviews/{reviewId}/checklist`): The
pages for projects, reviews, and review checklists.

- *Type*: HTTP `GET`, followed by a live Blazor connection for interactivity.
- *Role*: Provider.
- *Contract*: `/projects` lists the projects and has a form to create one; `/projects/{id}` shows a
  project with its reviews and a form that creates a review from selected files; the review route shows
  the review, its fingerprint, its files, the content of a selected file, and the progress of its checklist;
  the checklist route shows the checklist, where each check is answered. A project or review that
  does not exist is reported on the page itself.
- *Constraints*: Every page carries the same `data-interactive` marker as the home page. Every page requires a
  signed-in user: an anonymous request is redirected to the login page.

**Account route** (`GET /account`): The page where the signed-in user changes their own password.

- *Type*: HTTP `GET`, followed by a live Blazor connection for interactivity.
- *Role*: Provider.
- *Contract*: A form with the current password and the new password twice; it reports success or the reason of a
  rejection.
- *Constraints*: Requires a signed-in user.

**Users route** (`GET /users`): The page where an administrator manages users.

- *Type*: HTTP `GET`, followed by a live Blazor connection for interactivity.
- *Role*: Provider.
- *Contract*: Lists the users and offers to create a user, disable or enable a user, set a role, and set a
  password.
- *Constraints*: Requires the role `Admin`; a signed-in user with another role gets HTTP status 403.

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
domain types, and the limits), on the `Identity` subsystem (the signed-in user, the roles, and the safe return
address), and on the ASP.NET Core Blazor components framework.

#### Pages and domain access

`ProjectsPage`, `ProjectPage`, `ReviewPage`, `ChecklistPage`, `AccountPage`, and `UsersPage` follow the pattern of
`HomePage`: a Razor
presentation file and a code-behind file, interactive server rendering, and the `data-interactive`
marker. They obtain `ProjectService`, `ReviewService`, and the other Core services they need (`ReviewPage`:
`ThreadService` and `ChecklistService`; `ChecklistPage`: `ChecklistService`; `AccountPage` and `UsersPage`:
`UserService`) from the service container and call them
from their handlers. Each page loads its data when it is created, so a connection that is
established after the server-rendered snapshot shows current data. A rule violation reported by
Core as a `DomainRuleException` is shown as an error message next to the form, with the entered
values kept; the page never lets the exception escape. Forms are plain `<form>` elements handled
by `@onsubmit`, so Enter submits them and tests can submit them without a button click.
`LoginPage` is the exception to the pattern: it has no live connection, and its form is posted to the login endpoint.

#### Authorization and the signed-in user

Every page except the home page and the login page requires a signed-in user, declared on the page with an
authorization attribute; `UsersPage` requires the role `Admin`. The checks are made by the authorization of the
`Identity` subsystem before the page is rendered, so a page never shows data to an anonymous visitor. The
page frame (`MainLayout` in the `App` unit) shows the display name of the signed-in user with links to the account
page and, for an administrator, the users page, and a logout button, or a link to the login page when nobody is signed
in. The pages that record something on behalf of a person (`ReviewPage` and `ChecklistPage`) get the signed-in user
from the authentication state and pass it to Core as an `Actor`; there is no field in which a person could type
another name. A page that is already open keeps the authentication state it started with until it is reloaded.
`UsersPage` therefore re-checks the stored user before every action, so a user who is no longer an enabled
administrator is refused at once. Other open pages (`ReviewPage`, `ChecklistPage`) still act for the original user
until reloaded, and what they record is attributed to that user (a known limitation).

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

#### Limitations

A circuit (the live connection of an open page) keeps the authentication state it started with until the page is
reloaded; only the user administration actions of `UsersPage` are re-checked against the stored user. A password
change does not end other sessions of the user. The lockout after failed sign-ins is per user
name (see the `Identity` subsystem design).
