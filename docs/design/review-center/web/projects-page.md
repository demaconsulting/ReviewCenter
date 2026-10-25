### ProjectsPage

![Web Structure](WebView.svg)

#### Purpose

`ProjectsPage` is the page at `/projects`. Its responsibilities are to list the projects, to offer a form that
creates a project, and to report when the page is interactive. It requires a signed-in user. It is a Razor component
split into presentation
(`ProjectsPage.razor`) and behavior (`ProjectsPage.razor.cs`).

#### Data Model

The page instance is created per browser connection, so its state is per user.

**Projects**: the projects shown, loaded when the page is created and reloaded after a project is created.

**NewName**, **NewDescription**: the values typed into the form.

**ErrorMessage**: the message of the last rejected submission; `null` when there is none.

**InteractiveMarker**: `"true"` or `"false"`, rendered as the `data-interactive` attribute of the root element; it
becomes `true` after the first render on a live connection.

Every interactive element and key value has a stable `data-testid` (see the test identifier rule of the Web
subsystem): `projects-page` (root), `home-link`, `projects-list`, `projects-empty`, `project-row-{id}`,
`project-link-{id}`, `project-description-{id}`, `new-project-form`, `project-name-input`,
`project-description-input`, `project-error`, and `create-project-button`.

#### Key Methods

**OnInitializedAsync**: Loads the project list.

**CreateProjectAsync**: Handles submission of the form.

- *Postconditions*: On success the project is created, the form is cleared, the error is removed, and the list is
  reloaded. On a rule violation the entered values are kept and the message of the violation is shown. The handler
  first asks for the current user, so an ended sign-in state is refused with a message and nothing is created.

**OnAfterRender**: Marks the page interactive after the first render on a live connection, as `HomePage` does.

#### Error Handling

A `DomainRuleException` from `ProjectService`, or from the check of the current user, is caught and its message shown
in the page; no other exception is
handled by the page. The inputs carry the maximum lengths as a convenience; the service remains the authority.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content, so the page is replaced by a "Session ended" message when the
  sign-in state of the open page ends.
- **ProjectService** — creates and lists projects.
- **ASP.NET Core Blazor components** — the component base class, events, the authorization attribute, and the
  interactive server render mode.

#### Callers

- **App** — the router renders the page for `/projects`; an anonymous visitor is sent to the login page first.
