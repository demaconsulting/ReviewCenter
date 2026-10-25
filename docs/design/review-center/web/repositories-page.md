### RepositoriesPage

![Web Structure](WebView.svg)

#### Purpose

`RepositoriesPage` is the page at `/repositories`. Its responsibilities are to list the registered git repositories,
let an administrator add a repository, edit its name, location, and credentials, and enable or disable it, and to show
the outcome of each action next to the control that was used. It requires the `Admin` role: a signed-in user with another
role is shown the access denied message of the router instead of the page, and an anonymous visitor is sent to the login
page. Repositories are never deleted. The page never shows a token: it shows only whether one is set, and a token
that is typed in is sent to the service and cleared from the page state at once. It is a Razor component split into
presentation (`RepositoriesPage.razor`) and behavior (`RepositoriesPage.razor.cs`).

#### Data Model

The page instance is created per browser connection, so its state is per user.

**Repositories**: the `SourceRepository` list shown, ordered by name, loaded for the signed-in administrator.

**Add form**: `NewName`, `NewLocation`, `NewUsername`, `NewToken`.

**Edit form**: `EditingId` (`null` when no row is being edited), `EditName`, `EditLocation`, `EditUsername`,
`EditTokenAction` (keep the token, replace it, or clear it), and `EditToken`.

**ErrorMessage**, **StatusMessage**, **MessageRepositoryId**: the last refusal or outcome and the row it belongs to
(`null` for the add form or the page). **InteractiveMarker**: as for the other pages.

The list shows for each repository the name, the location, the user name, a token badge ("token set" or "no token"), and
whether it is enabled. The status messages are "Added repository 'X'.", "Saved repository 'X'.", "Disabled 'X'.", and
"Enabled 'X'.".

Every interactive element and key value has a stable `data-testid`:

- *Page and list*: `repositories-page` (root), `home-link`, `repositories-list`, `repositories-error`, and
  `repositories-status`.
- *Per row* (the suffix is the repository identity): `repository-row-{id}`, `repository-name-{id}`,
  `repository-location-{id}`, `repository-username-{id}`, `repository-token-state-{id}`, `repository-enabled-{id}`,
  `repository-toggle-enabled-{id}`, and `repository-edit-{id}`.
- *Edit form*: `repository-edit-form-{id}`, `edit-repository-name-input`, `edit-repository-location-input`,
  `edit-repository-username-input`, `edit-repository-token-action-select`, `edit-repository-token-input`,
  `edit-repository-save-button`, and `edit-repository-cancel-button`.
- *Add form*: `new-repository-form`, `new-repository-name-input`, `new-repository-location-input`,
  `new-repository-username-input`, `new-repository-token-input`, and `create-repository-button`.

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

**CreateRepositoryAsync**: Registers the repository from the add form with `RepositoryService.CreateAsync`. The token
field is cleared before the call, whether or not it succeeds; the other fields are cleared on success and kept on a
refusal so that the administrator can correct them.

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
- **ASP.NET Core Blazor components** — the component base class, forms, events, authorization attribute, and the
  interactive server render mode.

#### Callers

- **App** — the router renders the page for `/repositories`.
- **App** (`MainLayout`) — the header has a link to this page while an administrator is signed in.
