### RepositoriesPage

![Web Structure](WebView.svg)

#### Purpose

`RepositoriesPage` is the page at `/repositories`. Its responsibilities are to list the registered git repositories,
lead an administrator to the page that adds a repository, let the administrator edit a repository's name, location, and
credentials and enable or disable it, and show the outcome of each action next to the control that was used. It requires
the `Admin` role: a signed-in user with another role is shown the access denied message of the router instead of the
page, and an anonymous visitor is sent to the login page. Repositories are never deleted. The page never shows a token:
it shows only whether one is set, and a token that is typed in is sent to the service and cleared from the page state at
once. It is a Razor component split into presentation (`RepositoriesPage.razor`) and behavior
(`RepositoriesPage.razor.cs`).

The unit also contains `NewRepositoryPage`, the page at `/repositories/new` that holds only the form that registers a
repository, split into presentation (`NewRepositoryPage.razor`) and behavior (`NewRepositoryPage.razor.cs`). It has the
same access rule as the list page, a Cancel link back to the list, and returns to the list after a repository has
been registered; no status message is shown for the registration, because the new repository is in the list.

#### Layout

The list page uses the wide page container with a heading row that holds the title and the "+ Add repository" button.
Each repository is a row card: the name in bold, the location muted in a monospace font, and badges for the enabled
state and, only where they are set, the token ("token set") and the user name ("user NAME"). The Disable or Enable
button and the Edit button are small outline buttons, so that the single primary button of the page is the add button.
When no repository is registered a muted hint points to the add button. `NewRepositoryPage` uses the narrow page
container for forms and starts with a breadcrumb (muted, with "/" separators, and the current page as text instead of
a link) that leads back to the list.

#### Data Model

The page instance is created per browser connection, so its state is per user.

**Repositories**: the `SourceRepository` list shown, ordered by name, loaded for the signed-in administrator.

**Add form** (`NewRepositoryPage`): `NewName`, `NewLocation`, `NewUsername`, `NewToken`, and its own `ErrorMessage`.

**Edit form**: `EditingId` (`null` when no row is being edited), `EditName`, `EditLocation`, `EditUsername`,
`EditTokenAction` (keep the token, replace it, or clear it), and `EditToken`.

**ErrorMessage**, **StatusMessage**, **MessageRepositoryId**: the last refusal or outcome and the row it belongs to
(`null` for the page). **InteractiveMarker**: as for the other pages.

The list shows for each repository the name, the location, whether it is enabled, and, where set, a token badge
("token set") and a user name badge. The status messages are "Saved repository 'X'.", "Disabled 'X'.", and
"Enabled 'X'.".

Every interactive element and key value has a stable `data-testid`:

- *Page and list*: `repositories-page` (root), `new-repository-button`, `repositories-list`, `repositories-empty`,
  `repositories-error`, and `repositories-status`.
- *Per row* (the suffix is the repository identity): `repository-row-{id}`, `repository-name-{id}`,
  `repository-location-{id}`, `repository-username-{id}`, `repository-token-state-{id}`, `repository-enabled-{id}`,
  `repository-toggle-enabled-{id}`, and `repository-edit-{id}`. The user name and token badges are present only when
  the value is set.
- *Edit form*: `repository-edit-form-{id}`, `edit-repository-name-input`, `edit-repository-location-input`,
  `edit-repository-username-input`, `edit-repository-token-action-select`, `edit-repository-token-input`,
  `edit-repository-save-button`, and `edit-repository-cancel-button`.
- *Add page*: `new-repository-page` (root), `breadcrumb-repositories-link`, `repositories-error`,
  `new-repository-form`, `new-repository-name-input`, `new-repository-location-input`,
  `new-repository-username-input`, `new-repository-token-input`, `create-repository-button`, and
  `cancel-new-repository-link`.

#### Key Methods

**OnInitializedAsync**: Loads the list of repositories for the signed-in administrator.

**RunAsync** (private): The common path of every action.

- *Algorithm*: Clear the previous messages; read the signed-in user from the authentication state (an ended session shows
  its message and changes nothing); re-check through `UserService` that the user is still an enabled administrator,
  otherwise show "Administrator rights are required."; run the action; show the status message, or the message of a
  `DomainRuleException`, next to the control that was used; reload the list (also after a refusal, so the rows show
  what is
  stored). An ended session or a user who is no longer an administrator is refused without a reload. The check is
  kept on every action although
  the route is limited to administrators and the page is replaced when the sign-in state ends (see `SessionGate`),
  because an open page can keep its state for up to one revalidation interval after the user was demoted or disabled.

**CreateRepositoryAsync** (`NewRepositoryPage`): Registers the repository from the form with
`RepositoryService.CreateAsync`, after the same checks as `RunAsync` (the signed-in state has not ended and the user is
still an enabled administrator). The token field is cleared before the call, whether or not it succeeds. On success the
page navigates to `/repositories`; on a refusal the page stays, shows the message above the form, and keeps the other
fields so that the administrator can correct them.

**StartEdit** and **CancelEdit**: Open the edit form for a row, filled from the registration with the token action set to
keep, and close it without a change.

**SaveEditAsync**: Updates the repository with `RepositoryService.UpdateAsync` and a `TokenChange` built from the chosen
action. The token field is cleared before the call, and the form is closed on success and stays open on a refusal.

**ToggleEnabledAsync**: Enables a disabled repository or disables an enabled one with `RepositoryService.SetEnabledAsync`.

**OnAfterRender**: Marks the page interactive after the first render on a live connection.

#### Error Handling

A refusal from the service (an invalid name, location, or credential, a name already used, an unknown or unreadable
repository) is shown as its message next to the control and not raised. A user who is no longer an administrator is
refused with a message and nothing is changed. The page handles no other exception of its own. The token fields are
password inputs, and neither a token nor an encrypted token is ever rendered into the page.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content.
- **RepositoryService** — lists, creates, updates, and enables or disables repositories.
- **UserService** — the administrator re-check.
- **AuthenticationStateProvider** (ASP.NET Core) — gives the signed-in user.
- **NavigationManager** (ASP.NET Core) — used by `NewRepositoryPage` to return to the list.
- **ASP.NET Core Blazor components** — the component base class, forms, events, authorization attribute, and the
  interactive server render mode.

#### Callers

- **App** — the router renders the pages for `/repositories` and `/repositories/new`.
- **App** (`MainLayout`) — the header has a link to this page while an administrator is signed in.
