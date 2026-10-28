### RepositoriesPage

![Web Structure](WebView.svg)

#### Purpose

`RepositoriesPage` is the page at `/projects/{ProjectId}/repositories`. Its responsibilities are to list the git
repositories of that project, lead whoever manages the project to the page that adds a repository, let that user edit a
repository's name, location, and credentials and enable or disable it, and show the outcome of each action next to the
control that was used. Whoever manages the project is a system administrator or a project admin of the project (decided
by `IProjectAccess.CanAdministerAsync`). The page needs a signed-in user, which an anonymous visitor is sent to the
login page for; any other signed-in user, a plain member or a user who is no member, is shown the message
"Project N does not exist." as for a project that does not exist, so the page does not reveal a hidden project, and
nothing of the project. There is no registry of repositories outside the projects. Repositories are never deleted. The
page never shows a token:
it shows only whether one is set, and a token that is typed in is sent to the service and cleared from the page state at
once. It is a Razor component split into presentation (`RepositoriesPage.razor`) and behavior
(`RepositoriesPage.razor.cs`).

The unit also contains `NewRepositoryPage`, the page at `/projects/{ProjectId}/repositories/new` that holds only the
form that registers a repository in the project, split into presentation (`NewRepositoryPage.razor`) and behavior
(`NewRepositoryPage.razor.cs`). It has the
same access rule as the list page, a Cancel link back to the list, and returns to the list after a repository has
been registered; no status message is shown for the registration, because the new repository is in the list.

#### Layout

The list page uses the wide page container with a breadcrumb (Projects, the project, Repositories) and a heading row
that holds the title and the "+ Add repository" button.
Each repository is a row card: the name in bold, the location muted in a monospace font, and badges for the enabled
state and, only where they are set, the token ("token set") and the user name ("user NAME"). The Disable or Enable
button and the Edit button are small outline buttons, so that the single primary button of the page is the add button.
When the project has no repository a muted hint points to the add button. `NewRepositoryPage` uses the narrow page
container for forms and starts with a breadcrumb (muted, with "/" separators, and the current page as text instead of
a link) that leads back to the projects, the project, and the list.

#### Data Model

The page instance is created per browser connection, so its state is per user.

**ProjectId**: the project, from the page address. **Project**: the project shown, or `null` when it does not exist or
the user may not manage it. **Loaded**: whether the lookup has finished.

**Repositories**: the `SourceRepository` list of this project, ordered by name, loaded for the signed-in user who
manages the project.

**Add form** (`NewRepositoryPage`): `NewName`, `NewLocation`, `NewUsername`, `NewToken`, and its own `ErrorMessage`.

**Edit form**: `EditingId` (`null` when no row is being edited), `EditName`, `EditLocation`, `EditUsername`,
`EditTokenAction` (keep the token, replace it, or clear it), and `EditToken`.

**ErrorMessage**, **StatusMessage**, **MessageRepositoryId**: the last refusal or outcome and the row it belongs to
(`null` for the page). **InteractiveMarker**: as for the other pages.

The list shows for each repository the name, the location, whether it is enabled, and, where set, a token badge
("token set") and a user name badge. The status messages are "Saved repository 'X'.", "Disabled 'X'.", and
"Enabled 'X'.".

Every interactive element and key value has a stable `data-testid`:

- *Page and list*: `repositories-page` (root), `breadcrumb-projects-link`, `breadcrumb-project-link`,
  `project-not-found` (the message of a project that does not exist or that the user may not manage),
  `new-repository-button`, `repositories-list`, `repositories-empty`,
  `repositories-error`, and `repositories-status`.
- *Per row* (the suffix is the repository identity): `repository-row-{id}`, `repository-name-{id}`,
  `repository-location-{id}`, `repository-username-{id}`, `repository-token-state-{id}`, `repository-enabled-{id}`,
  `repository-toggle-enabled-{id}`, and `repository-edit-{id}`. The user name and token badges are present only when
  the value is set.
- *Edit form*: `repository-edit-form-{id}`, `edit-repository-name-input`, `edit-repository-location-input`,
  `edit-repository-username-input`, `edit-repository-token-action-select`, `edit-repository-token-input`,
  `edit-repository-save-button`, and `edit-repository-cancel-button`.
- *Add page*: `new-repository-page` (root), `breadcrumb-projects-link`, `breadcrumb-project-link`,
  `breadcrumb-repositories-link`, `project-not-found`, `repositories-error`,
  `new-repository-form`, `new-repository-name-input`, `new-repository-location-input`,
  `new-repository-username-input`, `new-repository-token-input`, `create-repository-button`, and
  `cancel-new-repository-link`.

#### Key Methods

**OnParametersSetAsync**: Loads the project and, only when the user manages it, the list of repositories of the project
(`RepositoryService.ListAsync` of that project, in the order of the name).

- *Postconditions*: With nobody signed in, or when `IProjectAccess.CanAdministerAsync` says the user may not manage the
  project, `Project` is `null` and the page shows only the message "Project N does not exist.", so a plain member and a
  user who is no member see what they would see for a missing project.

**RunAsync** (private): The common path of every action.

- *Algorithm*: Clear the previous messages; read the signed-in user from the authentication state (an ended session shows
  its message and changes nothing); re-check through `IProjectAccess.CanAdministerAsync` that the user still manages
  the project, otherwise show "Project administrator rights are required."; run the action; show the status message, or
  the message of a
  `DomainRuleException`, next to the control that was used; reload the list (also after a refusal, so the rows show
  what is
  stored). An ended session or a user who no longer manages the project is refused without a reload. The check is
  kept on every action although
  the page shows its content only to whoever manages the project and is replaced when the sign-in state ends (see
  `SessionGate`),
  because an open page can keep its state for up to one revalidation interval after the user was demoted or disabled.

**CreateRepositoryAsync** (`NewRepositoryPage`): Registers the repository in the project from the form with
`RepositoryService.CreateAsync`, after the same checks as `RunAsync` (the signed-in state has not ended and the user
still manages the project). The token field is cleared before the call, whether or not it succeeds. On success the
page navigates to `/projects/{ProjectId}/repositories`; on a refusal the page stays, shows the message above the form,
and keeps the other
fields so that the user can correct them.

**StartEdit** and **CancelEdit**: Open the edit form for a row, filled from the registration with the token action set to
keep, and close it without a change.

**SaveEditAsync**: Updates the repository with `RepositoryService.UpdateAsync` and a `TokenChange` built from the chosen
action. The token field is cleared before the call, and the form is closed on success and stays open on a refusal.

**ToggleEnabledAsync**: Enables a disabled repository or disables an enabled one with `RepositoryService.SetEnabledAsync`.

**OnAfterRender**: Marks the page interactive after the first render on a live connection.

#### Error Handling

A refusal from the service (an invalid name, location, or credential, a name already used, an unknown or unreadable
repository) is shown as its message next to the control and not raised. A user who no longer manages the project is
refused with a message and nothing is changed. The page handles no other exception of its own. The token fields are
password inputs, and neither a token nor an encrypted token is ever rendered into the page.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content.
- **RepositoryService** — lists the repositories of the project, creates, updates, and enables or disables them.
- **ProjectService** — reads the project for its name in the breadcrumb.
- **IProjectAccess** (Core) — decides whether the user manages the project, on load and before every action.
- **AuthenticationStateProvider** (ASP.NET Core) — gives the signed-in user.
- **NavigationManager** (ASP.NET Core) — used by `NewRepositoryPage` to return to the list.
- **ASP.NET Core Blazor components** — the component base class, forms, events, authorization attribute, and the
  interactive server render mode.

#### Callers

- **App** — the router renders the pages for `/projects/{ProjectId}/repositories` and
  `/projects/{ProjectId}/repositories/new`.
- **ProjectPage** — has a Repositories button for whoever manages the project.
