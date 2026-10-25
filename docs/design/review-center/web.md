## Web

![Web Structure](WebView.svg)

### Overview

The `Web` subsystem is the Blazor Server user interface of ReviewCenter. It owns what a browser
shows and how it responds to user actions. It contains twelve units: `App`, the HTML shell, the URL router, the
page frame with the sign-in header, and the `SessionGate` that replaces an open page when its user is no longer
signed in; `HomePage`, the interactive landing page served at the root URL; `ProjectsPage`,
`ProjectPage`, `ReviewPage`, and `ChecklistPage`, the pages for projects, reviews, and review checklists;
`LoginPage`, `AccountPage`, and `UsersPage`, the pages for signing in, changing one's own password, and managing
users; `HistoryPage` and `AuditPage`, the pages that show the history of a review and let an administrator
verify the audit trail; and `RepositoriesPage`, the page where an administrator registers the git repositories that
reviews can be created from. The subsystem
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
  the review, its fingerprint, its files, the content of a selected file, the progress of its checklist, its
  reviewer slots with their signature status, and an Approved or In review badge, and lets a reviewer sign;
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

**History route** (`GET /projects/{id}/reviews/{reviewId}/history`): The history of one review.

- *Type*: HTTP `GET`, followed by a live Blazor connection for interactivity.
- *Role*: Provider.
- *Contract*: Lists the audit entries of the review newest first in pages of 50 (time in UTC, actor, action, and
  detail), with Older and Newer links (the position is carried in the query values `before` and `after`), a message when
  the review does not exist, and a message when nothing was recorded.
- *Constraints*: Requires a signed-in user. It is reached from the link in the title row of the review page.

**Audit route** (`GET /audit`): The page where an administrator reads and verifies the whole audit trail.

- *Type*: HTTP `GET`, followed by a live Blazor connection for interactivity.
- *Role*: Provider.
- *Contract*: Lists the entries of the whole system newest first in pages of 50, with Older and Newer links (the
  position is carried in the query values `before` and `after`), and a Verify chain button that checks the whole hash
  chain and shows either "Chain intact" with the number of entries and the head hash, or the first broken entry and the
  head hash.
- *Constraints*: Requires the role `Admin`, which is re-checked against the stored user on load and on every
  verification. It is reached from the Audit link of the page frame, which only an administrator sees.

**Repositories route** (`GET /repositories`): The page where an administrator manages the registered git repositories.

- *Type*: HTTP `GET`, followed by a live Blazor connection for interactivity.
- *Role*: Provider.
- *Contract*: Lists the registered repositories with their location, user name, whether a token is set, and whether
  they are enabled; offers to add a repository, to edit one (name, location, user name, and keeping, replacing, or
  clearing the token), and to disable or enable one. A token is never shown. There is no delete.
- *Constraints*: Requires the role `Admin`, which is re-checked against the stored user before every action. It is
  reached from the Repositories link of the page frame, which only an administrator sees.

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

`ProjectsPage`, `ProjectPage`, `ReviewPage`, `ChecklistPage`, `AccountPage`, `UsersPage`, `HistoryPage`,
`AuditPage`, and `RepositoriesPage` follow the pattern of
`HomePage`: a Razor
presentation file and a code-behind file, interactive server rendering, and the `data-interactive`
marker. They obtain `ProjectService`, `ReviewService`, and the other Core services they need (`ReviewPage`:
`ThreadService`, `ChecklistService`, and `SigningService`; `ProjectPage`: also `SigningService`; `ChecklistPage`:
`ChecklistService`; `AccountPage` and `UsersPage`:
`UserService`; `HistoryPage` and `AuditPage`: `AuditService`, and `AuditPage` also the user store through
`UserService`; `RepositoriesPage`: `RepositoryService` and `UserService`) from the service container and call them
from their handlers. Each page loads its data when it is created, so a connection that is
established after the server-rendered snapshot shows current data. A rule violation reported by
Core as a `DomainRuleException` is shown as an error message next to the form, with the entered
values kept; the page never lets the exception escape. Forms are plain `<form>` elements handled
by `@onsubmit`, so Enter submits them and tests can submit them without a button click.
`LoginPage` is the exception to the pattern: it has no live connection, and its form is posted to the login endpoint.

#### Authorization and the signed-in user

Every page except the home page and the login page requires a signed-in user, declared on the page with an
authorization attribute; `UsersPage`, `AuditPage`, and `RepositoriesPage` require the role `Admin`. The checks are made
by the
authorization of the `Identity` subsystem before the page is rendered, so a page never shows data to an anonymous
visitor. The page frame (`MainLayout` in the `App` unit) shows the display name of the signed-in user with links to
the account page and, for an administrator, the users page, the repositories page, and the audit page, and a logout
button, or a link to the
login page when nobody is signed in. The pages that record something on behalf of a person (`ReviewPage` and
`ChecklistPage`) get the signed-in user
from the authentication state and pass it to Core as an `Actor`; there is no field in which a person could type
another name. A page that is already open makes no request, so the cookie check does not reach it; the
`RevalidatingAuthenticationStateProvider` of the `Identity` subsystem re-reads the user at a fixed interval and ends the
authentication state of the page when the user is no longer valid (see *Open pages and the end of a session* below).
`UsersPage` also re-checks the stored user before every action, `RepositoriesPage` does the same, and `AuditPage` on
load and before every
verification, so a user who is no longer an enabled
administrator is refused at once, and so does the signing of a review in Core. The other actions keep acting for the
original user until the next check, which is at most one revalidation interval, and what they record is attributed to
that user. Every action that changes something (creating a project or a review, starting, answering, resolving, and
reopening a thread, answering checklist checks, changing reviewer slots, signing, changing a password, and the user
administration actions) first asks `GetActorAsync` for the current user, even where its Core service takes no actor,
so an action that meets an authentication state that has already ended is refused with a message and saves nothing.

#### Open pages and the end of a session

Each protected page opts in to interactive rendering on its own, so each page is a root component of its own live
connection, while `App`, `Routes`, and `MainLayout` are served as static markup once. Neither the router nor the
header can react when the authentication state of an open page ends; only a component inside the page can. The
`SessionGate` component of the `App` unit therefore wraps the content of each of the nine protected pages
(`ProjectsPage`, `ProjectPage`, `ReviewPage`, `ChecklistPage`, `AccountPage`, `UsersPage`, `HistoryPage`,
`AuditPage`, `RepositoriesPage`). While a user is signed in it
adds no element, so the markup and the `data-interactive` marker of the page do not change. When the state becomes
anonymous, it removes the page content, and with it every handler, and shows a "Session ended" message with a link to
the login page that returns to the same page. `HomePage` and `LoginPage` are open to anonymous visitors, have no
protected actions, and are not wrapped. A test renders every protected page signed out and fails for a page that
lacks the gate.

The header keeps showing the old name and the Logout button until the next navigation or reload, because the layout
reads the state once. This is documented behavior, not a defect: the page body, which holds the data and the actions,
is what ends.

The pages enforce nothing that Core does not also enforce, with one exception: the file input of
`ProjectPage` checks the upload limits (file count, size of each file, total size) from the
sizes the browser reports, before reading any file, so that an oversized selection is rejected
without being read into memory. The limits themselves are defined once in Core.

Upload paths are the file name only. A browser file input does not expose folder paths, so
`ProjectPage` records each selected file under its browser file name; two selected files with
the same name are therefore refused by Core as a duplicate path. Directory-structured uploads are
a later feature.

#### Slots and signatures

The review page shows the reviewer slots of a review and lets a reviewer sign. The page contains no signing rules:
the approval status, the blockers, the record digest, and every refusal come from `SigningService` in Core. The
page's part is to ask for the approval and display it (the Approved or In review badge next to the title, the status
of each slot, and the digest in the Details section), to offer the controls that fit the signed-in user (slot
changes only for a slot without a signature; the sign form only to a user who occupies a slot that is not currently
signed, or the list of blockers while the review cannot be signed), and to pass the user's input to Core.
Signing re-authenticates the user: the form asks for the password, which the page holds only in a component field
while it is typed, passes to `SigningService.SignAsync`, and clears in a `finally` block after every attempt; it is
never rendered back into the page. The page also passes the record digest it displayed, so a signature always covers
the record the reader saw, and a record that changed since the page was drawn is refused. The project page shows the
approval status of every listed review, one lookup per review.

#### History and audit

`HistoryPage` and `AuditPage` only read. They show the audit entries through `AuditService`, which pages them 50 at a
time newest first, and the position is carried in the address, so the Older and Newer links are plain anchors. A page
shows what the trail says; it does not claim more. Only `AuditPage` verifies the chain, and its result text says what
was checked (the chain is intact, or the first entry that no longer fits) and shows the head hash so that an
administrator can record it elsewhere: the verification alone cannot tell that the newest entries were removed. The
history link in the title row of the review page is shown to every signed-in user, and the audit link of the page
frame only to an administrator, which is a convenience and not the protection: the page has its own authorization.

#### Repositories

`RepositoriesPage` administers the registry and contains no rules: the name, location, and credential rules and the
encryption of the token are in `RepositoryService`. The page's part is to collect the input, pass it with the actor,
and show the result or the refusal next to the control that was used. The token is write-only on the page: the token
fields are password inputs, the values are cleared right after every attempt, and the list shows only "token set" or
"no token". Because a stored token cannot be read back, editing offers three explicit choices (keep, replace, or
clear), and the default is to keep. Repositories can be disabled but not deleted.

#### Test identifiers

Every interactive element (`a`, `button`, `input`, `select`, `textarea`) of every page shall have a
stable, kebab-case `data-testid` attribute, so that every interactive element can be addressed by automated
tests and by the Puppet tool without depending on text, position, or style. Key displayed values have identifiers
too, and elements that repeat carry the review id, project id, or list index in the identifier. Each page root is
a `main` element with its own identifier. The rule is enforced by a test that renders every page and checks
every interactive element.

#### Limitations

An open page checks its user at an interval (`Auth:RevalidationSeconds`, 30 seconds by default), not continuously, so a
user who is disabled, removed, or changed can keep an open page for up to one interval; signing and the user
administration actions re-check the stored user on every action. A password change does not end other sessions of
the user. A check that cannot read the user ends the page (fail closed), which loses what was typed on it. The header
is not updated until the next navigation or reload. The lockout after failed sign-ins is per user
name (see the `Identity` subsystem design).
