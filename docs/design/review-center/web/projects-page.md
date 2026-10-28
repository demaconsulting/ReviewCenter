### ProjectsPage

![Web Structure](WebView.svg)

#### Purpose

The `ProjectsPage` unit consists of two pages. `ProjectsPage` is the page at `/projects`: its responsibilities are
to list the projects the user may see (every project for a system administrator, the projects the user is a member of
for anybody else), each as a clickable row, to lead a system administrator to the page that creates a project, and to
report when it is interactive. `NewProjectPage` is the page at `/projects/new`: it holds only the form that creates a
project, and reports when it is interactive. Both require a signed-in user, and `NewProjectPage` also the role
`SystemAdmin`. A new project has no members until a system administrator or a project admin adds some on the
page of the project. Each is
a Razor component split into presentation
(`ProjectsPage.razor`, `NewProjectPage.razor`) and behavior (`ProjectsPage.razor.cs`, `NewProjectPage.razor.cs`).

#### Data Model

The page instances are created per browser connection, so their state is per user.

**Projects** (`ProjectsPage`): the projects the user may see, from `ProjectService.ListAsync(actor)`, loaded when the
page is created. **IsSystemAdmin** (`ProjectsPage`): whether the user has the role `SignIn.AdminRole`; it decides
whether the New project button and the text of the empty list are those of a system administrator.

**NewName**, **NewDescription** (`NewProjectPage`): the values typed into the form.

**ErrorMessage** (`NewProjectPage`): the message of the last rejected submission; `null` when there is none.

**InteractiveMarker**: `"true"` or `"false"`, rendered as the `data-interactive` attribute of the root element of
each page; it becomes `true` after the first render on a live connection.

Every interactive element and key value has a stable `data-testid` (see the test identifier rule of the Web
subsystem). `ProjectsPage`: `projects-page` (root), `new-project-button` (the link that leads to `/projects/new`, only
for a system administrator), `projects-list`, `projects-empty`, `project-row-{id}`, `project-link-{id}`, and
`project-description-{id}`. Each
project is one row whose name is the link to `/projects/{id}`, with its description, when it has one, in muted text.
`projects-empty` says "You are not a member of any project yet. Ask an administrator to add you." to a user who is no
system administrator, and that there are no projects yet, with a hint to use + New project, to a system administrator.
`NewProjectPage`: `new-project-page` (root), `new-project-form`, `project-name-input`, `project-description-input`,
`project-error`, `create-project-button`, and `cancel-new-project-link` (an outline link back to `/projects`).

#### Key Methods

**OnInitializedAsync** (`ProjectsPage`): Loads the project list of the signed-in user.

**CreateProjectAsync** (`NewProjectPage`): Handles submission of the form.

- *Postconditions*: On success the project is created, the error is removed, and the browser is taken to the page of
  the new project. On a rule violation the entered values are kept, the message of the violation is shown, and the
  browser stays on the page. The handler first asks for the current user, so an ended sign-in state is refused with a
  message and nothing is created. A user who is no system administrator is refused by `ProjectService` with a message
  that names the missing right, and nothing is created.

**OnAfterRender** (both pages): Marks the page interactive after the first render on a live connection, as
`HomePage` does.

#### Error Handling

A `DomainRuleException` from `ProjectService`, or from the check of the current user, is caught by `NewProjectPage`
and its message shown in the page; no other exception is handled by the pages. The inputs carry the maximum lengths
as a convenience; the service remains the authority.

#### Dependencies

- **SessionGate** (App) — wraps the whole content of each page, so the page is replaced by a "Session ended" message
  when the sign-in state of the open page ends.
- **ProjectService** — lists the projects the user may see (`ProjectsPage`) and creates them (`NewProjectPage`).
- **AuthenticationStateProvider** (ASP.NET Core) — gives the signed-in user and the role claim.
- **ASP.NET Core Blazor components** — the component base class, events, navigation, the authorization attribute, and
  the interactive server render mode.

#### Callers

- **App** — the router renders `ProjectsPage` for `/projects` and `NewProjectPage` for `/projects/new`; an anonymous
  visitor is sent to the login page first.
