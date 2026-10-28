## Web

![Web Structure](WebView.svg)

### Overview

The `Web` subsystem is the Blazor Server user interface of ReviewCenter. It owns what a browser
shows and how it responds to user actions. It contains fifteen units: `App`, the HTML shell with the stored-theme
script, the URL router, the page frame (the menu bar with the product name, the primary navigation, the Help link
and the sign-in controls, and the footer with the version and the link to the About page), and the `SessionGate`
that replaces an open page when its user is no longer signed in; `HomePage`, the landing page served at the root URL,
with a Get started section for signed-in users; `AboutPage` and `HelpPage`, the pages that describe the product and
explain how to use it (`HelpPage` includes the index, a page for each topic, and the topic list `HelpTopics`);
`ProjectsPage`,
`ProjectPage`, `PlanPage`, `ReviewPage`, and `ChecklistPage`, the pages for projects, the planning of reviews from
the review-sets of a repository, reviews, and review checklists, where `ProjectsPage` includes `NewProjectPage` and
`ProjectPage` includes `NewReviewPage`, the pages that hold the creation forms;
`LoginPage`, `AccountPage`, and `UsersPage`, the pages for signing in, changing one's own password, choosing the
appearance and managing one's personal access tokens, and managing users and who is a system administrator
(`UsersPage` includes `NewUserPage`);
`HistoryPage` and `AuditPage`, the pages that
show the history of a review and let a system administrator
verify the audit trail; and `RepositoriesPage`, the page where whoever manages a project registers the git
repositories of that project that its reviews can be created from (it includes `NewRepositoryPage`). Which projects
a user sees is decided by project membership: a system administrator sees every project and any other user the
projects of which the user is a member, and the pages ask the `Core` access service instead of deciding for
themselves. The subsystem
does not start or configure the server; the `Hosting` subsystem maps `App` as the root
component of the server and supplies the endpoints around it, and the `Identity` subsystem supplies the sign-in and
the authorization the pages rely on. The pages use the `Core` subsystem for
all domain behavior and never access the database.

### Interfaces

**Root route** (`GET /`): The home page.

- *Type*: HTTP `GET`; there is no live connection, because the page is plain server-rendered markup.
- *Role*: Provider.
- *Contract*: Returns an HTML document titled "ReviewCenter" containing the home page: the product name and the
  tagline, and for a signed-in user a Get started section with a link card to the projects page and, for a
  system administrator, to the audit and users pages (three cards in all).
- *Constraints*: The page is open to anonymous visitors, who see only the tagline.

**About and Help routes** (`GET /about`, `GET /help`, `GET /help/{topic}`): The pages that describe the product.

- *Type*: HTTP `GET`; there is no live connection, because the pages are static server-rendered markup.
- *Role*: Provider.
- *Contract*: `/about` shows the product name, the tagline, the full version, the copyright holder, a link to the
  project on GitHub, and a short description of what the product does. `/help` is the index of the help topics, a card
  for each with its title and a one-line description. `/help/{topic}` is the page of one topic, with its text and
  screenshots and, at the bottom, links to the previous topic, the next topic, and the index. An unknown topic shows a
  friendly message with a link to the index.
- *Constraints*: Open to anonymous visitors. No link on these pages is an in-page anchor, because the base address of
  the HTML document would resolve it to the home page.

**Login route** (`GET /login`): The sign-in page.

- *Type*: HTTP `GET`; there is no live connection, because the page is a plain server-rendered form.
- *Role*: Provider.
- *Contract*: Shows a form that posts the user name, the password, and the return address to the login endpoint of the
  `Identity` subsystem, and a generic failure message when the query value `error` is present.
- *Constraints*: Open to anonymous visitors.

**Project routes** (`GET /projects`, `GET /projects/new`, `GET /projects/{id}`, `GET /projects/{id}/plan`,
`GET /projects/{id}/reviews/new`, `GET /projects/{id}/reviews/{reviewId}`,
`GET /projects/{id}/reviews/{reviewId}/checklist`): The
pages for projects, planning, reviews, and review checklists.

- *Type*: HTTP `GET`, followed by a live Blazor connection for interactivity.
- *Role*: Provider.
- *Contract*: `/projects` lists the projects the user may see as rows that link to them and, for a system
  administrator only, has a New project button; `/projects/new` holds the form that creates a project and opens it
  (system administrators only); `/projects/{id}` shows a
  project with its reviews as a list of rows and a New review button and a Plan reviews button, and, to a system
  administrator only, a Members section that lists the members with their role, switches a role, removes a member,
  and adds a member with the + Add member button, and, to whoever manages the project, a Repositories button that
  leads to `/projects/{id}/repositories`;
  `/projects/{id}/reviews/new` holds the form that creates a review from selected files or from a git repository
  of the project (the
  review identifier `new` is refused, because it would collide with this route);
  `/projects/{id}/plan` plans the
  reviews that a head revision of a repository of the project needs from the review-sets of its `.reviewmark.yaml`
  and creates the ticked ones; the review route shows a summary of the checklist progress, the reviewers, and the
  threads, then
  the review, its fingerprint, its files, the content of a selected file, its reviewer
  slots with their signature status, and an Approved or In review badge, and lets a reviewer sign;
  the checklist route shows the checklist, where each check is answered. A project that
  does not exist, and equally a project the user may not see, is reported on the page itself with the same
  message, "Project N does not exist.", with HTTP status 200, so that a user who is no member cannot tell a hidden
  project from a missing one; a review that does not exist is reported on the page itself.
- *Constraints*: Every interactive page carries the `data-interactive` marker. Every page requires a
  signed-in user: an anonymous request is redirected to the login page. List and detail pages use a wide container
  and form pages a narrow one. `/projects/new` requires the role `SystemAdmin`.

**Account route** (`GET /account`): The page where the signed-in user changes their own password, chooses the
appearance, and manages their personal access tokens.

- *Type*: HTTP `GET`, followed by a live Blazor connection for interactivity.
- *Role*: Provider.
- *Contract*: A form with the current password and the new password twice; it reports success or the reason of a
  rejection. An Appearance card offers System, Light, and Dark, which applies at once and is remembered by the
  browser. An API tokens card creates a token from a name and a lifetime of 30, 90, or 365 days and shows its value once
  with a copy button, lists the tokens with their state, and revokes one.
- *Constraints*: Requires a signed-in user.

**Users routes** (`GET /users`, `GET /users/new`): The pages where a system administrator manages users.

- *Type*: HTTP `GET`, followed by a live Blazor connection for interactivity.
- *Role*: Provider.
- *Contract*: `/users` lists the users, with a System admin badge for each system administrator, and offers to
  disable or enable a user, make a user a system administrator or take that away, and set a
  password, and has a New user button; `/users/new` holds the form that creates a user, with a System administrator
  checkbox.
- *Constraints*: Require the role `SystemAdmin`, which only a system administrator has; a signed-in user without it
  gets HTTP status 403. The last enabled system administrator cannot be disabled or demoted.

**History route** (`GET /projects/{id}/reviews/{reviewId}/history`): The history of one review.

- *Type*: HTTP `GET`, followed by a live Blazor connection for interactivity.
- *Role*: Provider.
- *Contract*: Lists the audit entries of the review newest first in pages of 50 (time in UTC, actor, action, and
  detail), with Older and Newer links (the position is carried in the query values `before` and `after`), a message when
  the review does not exist, and a message when nothing was recorded.
- *Constraints*: Requires a signed-in user who may see the project; for another user the page reads nothing of the
  review. It is reached from the link in the title row of the review page.

**Audit route** (`GET /audit`): The page where a system administrator reads and verifies the whole audit trail.

- *Type*: HTTP `GET`, followed by a live Blazor connection for interactivity.
- *Role*: Provider.
- *Contract*: Lists the entries of the whole system newest first in pages of 50, with Older and Newer links (the
  position is carried in the query values `before` and `after`), shows long hashes shortened to twelve characters
  with the full value in a tooltip, and a Verify chain button that checks the whole hash
  chain and shows either "Chain intact" with the number of entries and the head hash, or the first broken entry and the
  head hash.
- *Constraints*: Requires the role `SystemAdmin`, which is re-checked against the stored user on load and on every
  verification. It is reached from the Audit link of the page frame, which only a system administrator sees.

**Repositories routes** (`GET /projects/{id}/repositories`, `GET /projects/{id}/repositories/new`): The pages where
whoever manages a project manages the git repositories of that project. There is no registry of repositories outside
the projects.

- *Type*: HTTP `GET`, followed by a live Blazor connection for interactivity.
- *Role*: Provider.
- *Contract*: `/projects/{id}/repositories` lists the repositories of the project with their location, badges for a
  user name and a token when set, and whether they are enabled; offers to edit one (name, location, user name, and
  keeping, replacing, or clearing the token), and to disable or enable one, and has an Add repository button.
  `/projects/{id}/repositories/new` holds the form that registers a repository in the project. A token is never
  shown. There is no delete. Both pages show breadcrumb links back to the projects and to the project.
- *Constraints*: The pages need a signed-in user. Only a system administrator and a project admin of the project
  may manage the repositories, which is checked with the `Core` access service on load and again before every
  action; any other user, a plain member or a user who is no member, gets the page of a project that does not exist.
  They are reached from the Repositories button of the project page, which only whoever manages the project sees.

**Unknown routes**: Any other URL.

- *Type*: HTTP `GET`.
- *Role*: Provider.
- *Contract*: The shell is returned with status 404 and a "Page not found" page, inside the header and footer of the
  page frame, in place of a page (`/not-found` is re-executed for the request, see `UseNotFoundPage` of `ServerHost`).
  A request that is not a page request (a method other than GET or HEAD, a path under `/api`, `/_framework`,
  `/_blazor`, or `/_content`, or a path that ends in a file extension) keeps a plain, empty 404.
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
server-side markup: it emits the HTML document, the page head outlet, a small inline script that applies the stored
appearance before the first paint, the router, and the client script reference. `Routes` (part of the `App` unit)
matches the requested URL to a page component, with `HomePage` registered at the root route, and renders the not-found
page (`NotFoundPage`, registered at `/not-found`) inside `MainLayout` for unmatched URLs. The routes that end in `new`
(`/projects/new`, `/projects/{id}/reviews/new`,
`/projects/{id}/reviews/new`, `/projects/{id}/repositories/new`, `/users/new`) are matched before the parameterized
routes of the same shape. Each interactive
page opts in to interactive server rendering, so only the
page, not the shell, holds a live connection. `HomePage`, `AboutPage`, `LoginPage`, and the help pages are static
markup with no live connection.

The first response to a browser is a server-rendered snapshot of an interactive page with the
`data-interactive` marker set to `false`. When the client script connects, the page is
rendered again on a live connection, its first render completes, and the marker becomes
`true`. Drivers such as tests and tooling wait for the marker instead of using fixed delays.
Each connection has its own page instance, so the page state is per user and needs no
synchronization.

All pages share one look: a style sheet with color variables for a light and a dark palette. The `data-theme`
attribute of the `html` element, set by the inline script of `App` from the value that the `AccountPage` stores in
the browser, selects the palette; without a stored choice the palette follows the preference of the browser. List and
detail pages use a wide container and form pages a narrow one; a breadcrumb above the heading shows where a page is
below its parents.

The subsystem has no dependency on `Context`. It depends on the `Core` subsystem (the services, the
domain types, and the limits), on the `Identity` subsystem (the signed-in user, the roles, and the safe return
address), and on the ASP.NET Core Blazor components framework.

#### Pages and domain access

`ProjectsPage`, `ProjectPage`, `PlanPage`, `ReviewPage`, `ChecklistPage`, `AccountPage`, `UsersPage`, `HistoryPage`,
`AuditPage`, and `RepositoriesPage`, and the creation pages they include (`NewProjectPage`, `NewReviewPage`,
`NewRepositoryPage`, and `NewUserPage`), follow the pattern of
the interactive pages: a Razor
presentation file and a code-behind file, interactive server rendering, and the `data-interactive`
marker. They obtain `ProjectService`, `ReviewService`, and the other Core services they need (`ReviewPage`:
`ThreadService`, `ChecklistService`, and `SigningService`; `ProjectPage`: also `SigningService`, `RepositoryService`,
`UserService` for the candidates of a new member, and `IProjectAccess`; `PlanPage`:
`ProjectService`, `RepositoryService`, and `ReviewPlanner`; `ChecklistPage`:
`ChecklistService`; `AccountPage`: `UserService` and `PersonalAccessTokenService`; `UsersPage`:
`UserService`; `HistoryPage` and `AuditPage`: `AuditService`, and `AuditPage` also the user store through
`UserService`; `RepositoriesPage`: `RepositoryService`, `ProjectService`, and `IProjectAccess`; each creation page
the service of its parent page) from the service container and call them
from their handlers. Each page loads its data when it is created, so a connection that is
established after the server-rendered snapshot shows current data. A rule violation reported by
Core as a `DomainRuleException` is shown as an error message next to the form, with the entered
values kept; the page never lets the exception escape. Forms are plain `<form>` elements handled
by `@onsubmit`, so Enter submits them and tests can submit them without a button click.
`LoginPage` is the exception to the pattern: it has no live connection, and its form is posted to the login endpoint.

#### Authorization and the signed-in user

Every page except the home page, the login page, the About page, and the help pages requires a signed-in user,
declared on the page with an
authorization attribute; `UsersPage`, `AuditPage`, and `NewProjectPage`, and the creation page of the users, require
the role `SystemAdmin`, which the `Identity` subsystem puts into the claims of a system administrator only. The
repositories pages are open to every signed-in user by attribute, because whether a user may manage a project depends
on the project; they ask the `Core` access service. The checks are made
by the
authorization of the `Identity` subsystem before the page is rendered, so a page never shows data to an anonymous
visitor. The page frame (`MainLayout` in the `App` unit) is a menu bar with the product name, which links to the home
page, then a tab for the projects page and, for a system administrator, the audit page and the users
page (there is no tab for repositories, which belong to the projects), and at the right the Help link, the display
name of the signed-in user, a link to the account page, and a logout
button, or the Help link and a link to the login page when nobody is signed in. The tab of the section that is open
is marked as current. A footer shows the product name, the version, the copyright holder, and a link to the About
page. The pages that record something on behalf of a person (`ReviewPage` and
`ChecklistPage`) get the signed-in user
from the authentication state and pass it to Core as an `Actor`; there is no field in which a person could type
another name. A page that is already open makes no request, so the cookie check does not reach it; the
`RevalidatingAuthenticationStateProvider` of the `Identity` subsystem re-reads the user at a fixed interval and ends the
authentication state of the page when the user is no longer valid (see *Open pages and the end of a session* below).
`UsersPage` also re-checks the stored user before every action, `RepositoriesPage` re-checks with the access service
that the user still manages the project, and `AuditPage` re-checks on load and before every
verification, so a user who is no longer an enabled
system administrator is refused at once, and so does the signing of a review in Core. The other actions keep acting
for the
original user until the next check, which is at most one revalidation interval, and what they record is attributed to
that user. Every action that changes something (creating a project or a review, starting, answering, resolving, and
reopening a thread, answering checklist checks, changing reviewer slots, signing, changing a password, and the user
administration actions) first asks `GetActorAsync` for the current user, even where its Core service takes no actor,
so an action that meets an authentication state that has already ended is refused with a message and saves nothing.

#### Project access

What a user sees of the projects is decided in `Core` and not in the pages. `ProjectService.ListAsync(actor)` returns
only the projects the user may see, and `ProjectService.GetAsync(actor, id)` returns nothing for a project the user
may not see, exactly as for a project that does not exist. `ProjectsPage`, `ProjectPage`, `NewReviewPage`, `PlanPage`,
`ReviewPage`, `ChecklistPage`, `HistoryPage`, `RepositoriesPage`, and `NewRepositoryPage` ask with the actor from
the authentication state and, when they get nothing, show the same not-found text as for a project that does not
exist (for `ReviewPage`, `ChecklistPage`, and `HistoryPage` the not-found text of a review, with nothing of the review
loaded). The answer is a normal page with HTTP status 200, so that neither the status nor the text tells a user who is
no member that a hidden project exists. Whether the user is a system administrator is asked of `IProjectAccess` by
`ProjectPage` and read from the role claim (`SystemAdmin`) by `ProjectsPage`; whether the user may manage a project
(a system administrator or a project admin of it) is asked of
`IProjectAccess`, so `ProjectPage` offers the Repositories button, and `RepositoriesPage` and `NewRepositoryPage`
serve their content, only for whoever manages the project.

The Members section of `ProjectPage` is shown only to a system administrator. It lists the members of
`ProjectService.ListMembersAsync` with a badge for the role (Project admin or Member), a Disabled badge for a disabled
account, a button that switches the role (`SetMemberRoleAsync`), and a Remove button (`RemoveMemberAsync`). The
"Add member" button reveals an inline form with a drop-down of the enabled users who are not yet members
(`UserService`), a Project admin checkbox, and a Cancel button (`AddMemberAsync`); a message says that nobody is
left to add when there is no candidate. A project without members says that only system administrators can see it.
The page contains no membership rules; every refusal comes from `ProjectService`, and the page shows it next to the
list. The reviewer slots of `ReviewPage` offer only the members of the project, from
`SigningService.ListCandidatesAsync(actor, projectId)`.

#### Access on an open page

The access above is decided when a page loads. A page that stays open does not load again, so a user whose membership
was removed meanwhile would keep reading through it: the files, comments, checklist answers, and plans that the page
loads from the services in its handlers, and the data already in its component state. The pages of a project therefore
ask again, at the start of every handler of the open circuit, with one shared helper,
`ProjectViewGuard.GuardViewAsync` (an extension of `IProjectAccess`): it reads the actor from the authentication state
and returns whether `IProjectAccess.CanViewAsync` allows that actor to see the project; when it does not, it runs a
callback of the page, which resets the component state (so that no loaded data is kept) and shows the same not-found
page as for a project the user may not see. Nobody being signed in is treated as no access. The handler then returns
without calling a service, so nothing is read and nothing is stored.

The guard is the first statement of the handlers that call a service or read data: `ReviewPage` (view file, view
thread, start thread, reply, resolve or reopen, add, save and remove slot, and sign), `ChecklistPage` (answer, save,
and the not-applicable switch), `PlanPage` (show plan and create reviews), and `NewReviewPage` (create the review).
The handlers that only change what is shown (the thread filter, expanding a thread, the diff, head, and baseline views,
the open section of the checklist, the next unanswered check, the mode and ticks of `PlanPage`, and the source choice
of `NewReviewPage`) are wrapped at their binding in the Razor file by the page's `GuardedAsync`, so the first
interaction after a removal also drops the data. `HistoryPage` has no handler: its
`OnParametersSetAsync`, which asks `ProjectService.GetAsync(actor, id)` before it reads anything, runs again for every
new address, which is its one check. `ProjectPage` and the administration pages already re-check, as described above.
Three handlers are left unguarded on purpose because they show no project data: cancelling a comment form,
copying a thread link, and reading the file names of a browser selection.

The Core read services (`ReviewService.GetFileContentAsync` and the like) keep their signatures and take no actor:
every page reaches them only after the guard, so a second check inside them would only repeat it, and the services
that change something already require the actor to see the project. The limits are these. A removal that lands between
the check and the end of a single handler is not caught; the handler finishes with the access it had. There is no push
to the browser: what is already in the page stays visible until the user's next interaction, which then drops it. The
sign-in state of the page ends separately and at once (see *Open pages and the end of a session*); for a project page
that ended state also reads as no access, so the guard shows the not-found page where an action of the ended session
was refused with a message before.

#### Open pages and the end of a session

Each protected page opts in to interactive rendering on its own, so each page is a root component of its own live
connection, while `App`, `Routes`, and `MainLayout` are served as static markup once. Neither the router nor the
header can react when the authentication state of an open page ends; only a component inside the page can. The
`SessionGate` component of the `App` unit therefore wraps the content of each of the fourteen protected pages
(`ProjectsPage`, `NewProjectPage`, `ProjectPage`, `NewReviewPage`, `PlanPage`, `ReviewPage`, `ChecklistPage`,
`AccountPage`, `UsersPage`, `NewUserPage`, `HistoryPage`,
`AuditPage`, `RepositoriesPage`, `NewRepositoryPage`). While a user is signed in it
adds no element, so the markup and the `data-interactive` marker of the page do not change. When the state becomes
anonymous, it removes the page content, and with it every handler, and shows a "Session ended" message with a link to
the login page that returns to the same page. `HomePage`, `AboutPage`, the help pages, and `LoginPage` are open to
anonymous visitors, have no
protected actions, and are not wrapped. A test renders every protected page signed out and fails for a page that
lacks the gate.

The header keeps showing the old name and the Logout button until the next navigation or reload, because the layout
reads the state once. This is documented behavior, not a defect: the page body, which holds the data and the actions,
is what ends.

The pages enforce nothing that Core does not also enforce, with one exception: the file input of
`NewReviewPage` checks the upload limits (file count, size of each file, total size) from the
sizes the browser reports, before reading any file, so that an oversized selection is rejected
without being read into memory. The limits themselves are defined once in Core.

Upload paths are the file name only. A browser file input does not expose folder paths, so
`NewReviewPage` records each selected file under its browser file name; two selected files with
the same name are therefore refused by Core as a duplicate path. Directory-structured uploads are
a later feature.

#### Reviews from a repository

`NewReviewPage`, which the New review button of `ProjectPage` opens, offers two sources for a new review, shown as
two selectable option cards: "Upload files" (the default) and "From a repository". The second
shows a drop-down of the enabled repositories of the project (by name only; `RepositoryService.ListEnabledAsync`
returns a summary
that has no location and no token, and lists the repositories of the given project only), a head revision, an
optional baseline revision, and the selection (one pattern
per line, default `**/*`). The page passes the values to `ReviewService.CreateFromRepositoryAsync` and shows every
refusal, among them "Nothing to review: no files matching the selection changed between X and Y", in the form with the
values kept. Skipped symbolic links and submodules are listed once before the review is opened, since they are not
stored. `ReviewPage` shows the change kind of each file, the repository, both commits and the selection in the
details, and for a file of a review with a baseline commit a choice between the Diff view (the default), the Head view
(not for a deleted file), and the Baseline view (not for an added file):
the Diff view shows the changed lines side by side, the baseline on the left and the head on the right, with the
unchanged lines collapsed, and takes comments on a line of either
version, Head is the numbered view that takes comments, Baseline is the frozen baseline, read-only. An added file
opens on its Diff view (all lines added, an empty left side) and takes comments on its head lines and on the whole
file. A deleted file
opens on its Diff view (all lines removed) and takes whole-file comments and comments on its removed lines. A file
that is too large to compare, or binary, says so in plain words and offers the other views. In the Head or Baseline
view, a thread chosen in the index or opened by its address highlights its lines in the version it is on and in no
other. Everything the page shows
comes from the frozen copies in the store, never from the repository.

#### Sharing links

Every file, line and thread of a review has a stable address that opens the review page at that place:
`/projects/{id}/reviews/{reviewId}/files/{path}` (with an optional `?line=N&side=head|baseline`) and
`/projects/{id}/reviews/{reviewId}/threads/{threadId}`. `ReviewLinks` builds them, escaping the review identifier and
each path segment once. The page opens the file in the Diff view, scrolls to the line or the thread and highlights it,
and replaces the address of the browser with the link of whatever the reader chooses, without adding history entries,
so the address bar is always a link that can be shared. Each thread has a plain, small `Link` (a text link, not a
button) to its address. An address
that names nothing gives a plain message with a link back to the file list. A signed-out visitor is sent to the login
page and returned to the same address (the `SignIn` unit keeps the address as it was requested). No authorization
rule is added: the link opens only for a signed-in user, as the page always did.

#### Planning reviews

`PlanPage` is built for a quick read and for a clear next step. It asks for a repository of the project, a head
revision and a
baseline (None, Explicit revision, or Last reviewed, the default; the text box appears only for Explicit revision),
asks `ReviewPlanner` for the plan on behalf of the signed-in user, and shows one plain summary sentence first (for
example "3 sets need review, 1
already filed, 9 have nothing to review."), a line with the repository, head and baseline, and a sentence that says
what to do next. The sets that need a review come first, ticked by default; the sets with nothing to review are in a
collapsed list with a count; "Cannot be planned" and "Already filed" (with a link to the review) are groups that are
shown only when they are not empty. Creating replaces the plan by one result per review, a link for each review that
was created and the reason for each that failed. The page contains no planning rules: the baseline choice, the
classification, the identifiers and the creation are in `ReviewPlanner`. A failure is shown in plain English with what
to do next; the text git printed, a location, a credential and exception text are never shown. `ProjectPage` links to
the plan page when an enabled repository of the project exists, and `ReviewPage` shows the review-set of a planned
review in the details. `PlanPage` shows the not-found text of a project for a project the user may not see.

The review page shows the reviewer slots of a review and lets a reviewer sign. A summary strip at its top (the
state, the checklist progress with the link to the checklist, the reviewer slots with their signatures, and the open
thread count) comes before the files and the viewer, because reading them is the main work; the form that adds a slot
is collapsed until asked for. The page contains no signing rules:
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

#### Signed attestation

An approved review offers the download of its signed PDF attestation. The page only decides whether to offer it: it asks
`IAttestationKeyProvider.Describe` for the state of the signing key and shows the download link with a note on the key
(a self-signed key is labelled as not an organization key, as generated by the server for the `SelfSigned` provider and
as supplied by the administrator from the certificate store for any other provider; an organization key is named by its
subject and the start of
its thumbprint), or the reason why the key cannot be used. The link is a plain anchor to the endpoint of the `Evidence`
subsystem, built by `ReviewLinks.ForAttestation`; the document is made and signed there, never in the page. The Help
topic `signed-attestation` explains what the signature proves and does not prove.

#### Personal access tokens

The tokens card of `AccountPage` only collects input and shows results; the rules (the name, the choices of lifetime,
the limit of active tokens, and who may revoke) are in `PersonalAccessTokenService`. The value of a new token is held
only in the state of the open page, shown once, and never listed again; the card says so, offers a copy button that uses
a browser script of `App`, and falls back to a hint to copy it by hand. The list never has a value, only the display
id. The Help topic `api-tokens` explains how to use a token and links back to the card; the card links to the topic. A
token authenticates only the `Api` subsystem: the pages, including this one, accept the session cookie only.

#### History and audit

`HistoryPage` and `AuditPage` only read. They show the audit entries through `AuditService`, which pages them 50 at a
time newest first, and the position is carried in the address, so the Older and Newer links are plain anchors. A page
shows what the trail says; it does not claim more. Only `AuditPage` verifies the chain, and its result text says what
was checked (the chain is intact, or the first entry that no longer fits) and shows the head hash so that an
administrator can record it elsewhere: the verification alone cannot tell that the newest entries were removed. The
history link in the title row of the review page is shown to every signed-in user who may see the project, and the
audit link of the page
frame only to a system administrator, which is a convenience and not the protection: the page has its own
authorization.

#### Repositories

`RepositoriesPage` administers the repositories of one project and contains no rules: the name, location, and
credential rules and the
encryption of the token are in `RepositoryService`, and who may manage a project is decided by `IProjectAccess`. The
page's part is to collect the input, pass it with the actor and the project, and show the result or the refusal next
to the control that was used. A user who may not manage the project gets the not-found text of a project that does
not exist, so a repository page never confirms that a hidden project exists. The token is write-only on the page: the
token
fields are password inputs, the values are cleared right after every attempt, and the list shows only "token set" or
"no token". Because a stored token cannot be read back, editing offers three explicit choices (keep, replace, or
clear), and the default is to keep. Repositories can be disabled but not deleted. The pages show breadcrumb links to
the projects page and to the project.

#### Test identifiers

Every interactive element (`a`, `button`, `input`, `select`, `textarea`) of every page shall have a
stable, kebab-case `data-testid` attribute, so that every interactive element can be addressed by automated
tests and by the Puppet tool without depending on text, position, or style. Key displayed values have identifiers
too, and elements that repeat carry the review id, project id, or list index in the identifier. Each page root is
a `main` element with its own identifier. An identifier never repeats within a page: the header tabs have the
unprefixed identifiers `projects-link`, `audit-link`, and `users-link`, `repositories-link` is the identifier of the
Repositories button of the project page, and the breadcrumb links of the pages
below them are prefixed `breadcrumb-` (for example `breadcrumb-projects-link`). The rule is enforced by tests that
render every page and check every interactive element and the uniqueness of the identifiers.

#### Limitations

An open page checks its user at an interval (`Auth:RevalidationSeconds`, 30 seconds by default), not continuously, so a
user who is disabled, removed, or changed can keep an open page for up to one interval; signing, the user
administration actions, and the repository actions re-check the stored user on every action. A password change does
not end other sessions of
the user. A check that cannot read the user ends the page (fail closed), which loses what was typed on it. The header
is not updated until the next navigation or reload. The lockout after failed sign-ins is per user
name (see the `Identity` subsystem design).
