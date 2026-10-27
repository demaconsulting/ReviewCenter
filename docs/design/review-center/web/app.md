### App

![Web Structure](WebView.svg)

#### Purpose

`App` is the HTML shell, URL router, and page frame of the web user interface. Its responsibilities are to wrap every
page in a complete HTML document, to apply the stored appearance before the first paint, to select the page component
for the requested URL, and to put the same menu bar header and the same footer around every page. A further part keeps
an open page from going on with data after its user has ended. The unit consists of five Razor components, `App` (the
shell), `Routes` (the router), `MainLayout` (the page frame, with a code-behind), `SessionGate` (the guard of an
open protected page), and `NotFoundPage` (the answer for an address that no page handles).

#### Data Model

`App` holds almost no state. `Routes`, `App`, and `SessionGate` are declarative markup with no fields of their own;
`MainLayout` holds the few values it works out once per render.

**App component**: Static server-rendered markup that emits the document type, the language attribute, the character
set and viewport declarations, an inline appearance script, the base address (`/`), the style sheet link, the page head
outlet, the `Routes` component, a second small inline script, and a script reference to `_framework/blazor.web.js`.

- The appearance script sits in the head, before the style sheet link, so that it runs before the first paint. It reads
  the appearance choice of the user (`system`, `light`, or `dark`) from the browser's local storage under the key
  `reviewcenter-theme`; any other value, a missing value, or storage that is blocked means `system`. It sets the
  attribute `data-theme` (`light` or `dark`), the attribute `data-theme-choice`, and the `color-scheme` style on the
  document element. The choice `system` follows the appearance preference of the operating system, and a change of that
  preference is followed while the page is open. Enhanced navigation can rebuild the attributes of the document element,
  so the script puts the attribute back when it goes missing. It also defines `window.reviewCenter.getTheme` and
  `window.reviewCenter.setTheme`, through which a page can read and store the choice; `system` is stored by removing the
  value. The script does not use the server and the choice never leaves the browser.
- The second script, at the end of the body, defines `window.reviewCenter.scrollToTestId` (the review page calls it to
  bring the selected file's code, a thread's first line, or the new-thread form into view; Blazor cannot scroll by
  itself) and `window.reviewCenter.replaceUrl` (the review page calls it to put the link of the chosen file or thread in
  the address bar with `history.replaceState`, which adds no history entry and does not run the router; it accepts only
  a path that starts with a single `/`).

**Routes component**: A router that searches the application assembly for page components. For a matched URL it
renders the page through an authorize route view, inside `MainLayout` as the default layout; for an unmatched URL it
renders `NotFoundPage` as the router's not-found page. The router prefers a fixed route to one with a parameter in the
same place, so
the addresses of the creation forms (`/projects/new`, and `/projects/{project}/reviews/new`, for example) reach their
own form pages and are not read as the address of a project or of a review called "new". The authorize route view
applies the authorization attributes of the page also when the user navigates inside an open interactive session, where
the endpoint check does not run (a plain route view would ignore them). When it refuses, the router shows, in the page
frame, "Access denied" for a signed-in user (with a link back to the projects) and "Sign in required" with a link to the
login page for a signed-out user; the link carries the requested address as `ReturnUrl`. The elements have
`data-testid` values `access-denied`, `access-denied-projects-link` and `access-denied-login-link`. In production the
router is part of the static shell, so the authorize route view only decides for the first request of a page; it cannot
react when the state of an open page ends (see `SessionGate`).

**NotFoundPage component**: The page at `/not-found`, open to everyone and rendered inside `MainLayout` like every page,
so it has the header and the footer. It shows the heading "Page not found", a short explanation, and a "Back to
projects" button. The elements have `data-testid` values `not-found` and `not-found-projects-link`. A request to the
server for an address that no page handles never reaches the router, because it matches no endpoint; the `UseNotFoundPage`
middleware of `ServerHost` therefore re-executes such a page request against `/not-found` and keeps the 404 status
(see the Hosting subsystem), so the browser receives this page with status 404. The same page is the router's answer for
an address that matches no page. Opening `/not-found` itself answers with status 200.

**MainLayout component**: The page frame, rendered as static server-side markup (it needs no live connection). It
reads the authentication state of the request once, so it keeps showing the name that was current when the page was
served until the next navigation or reload, even after the state of an open page has ended. It holds two values, set in
`OnInitializedAsync`: **Actor**, the signed-in user or `null` when nobody is signed in, and **IsAdmin**, true when the
user has the administrator role. A derived value, **ShortVersion**, is the version of the footer.

The header is a menu bar that spans the full width of the window and has two groups, which wrap onto further lines on a
narrow screen. The left group is the logo (an inline vector image that is hidden from assistive technology), the
product name `ReviewCenter` as the one link to the home page, and the primary navigation. The primary navigation is,
for a signed-in user, the tab Projects and, only for an administrator, the tabs Repositories, Audit, and Users; it is
empty when nobody is signed in. The right group starts with the tab Help, which everyone sees, and then, when nobody is
signed in, the "Sign in" link to the login page. When a user is signed in it continues with the display name of the
user, the tab Account, and a logout form that posts to `/auth/logout` with an antiforgery token (signing out changes
state, so it is a form and not a link). The tabs are navigation links that match by prefix, so the tab of the current
section (for example Projects on the page of a review) is marked active, with the current-page attribute that
assistive technology reads, and is drawn as the active tab. The pages themselves carry no link to the home page: the
brand link
is the only one.

The footer is on every page, and shows `ReviewCenter {version} · © DEMA Consulting · Web-based code review for regulated
projects` and a link "About" to `/about`. **ShortVersion** is the informational version of the program (the same value
the command line prints) up to the `+`, and, when a commit identifier follows the `+`, a `+` and the first seven
characters of it; a version without a `+` is shown as it is.

The header and footer elements have `data-testid` values: `home-link`, `projects-link`, `repositories-link`,
`audit-link`, `users-link`, `help-link`, `signin-link`, `current-user`, `account-link`, `logout-button`, `footer`,
`version-text`, and `about-link`. The layout only shows links; the access rules are enforced by the pages and the
authorization of the `Identity` subsystem, so a hidden link is a convenience and not a protection.

**SessionGate component**: A wrapper that each protected page (the pages that need a signed-in user, among them
`ProjectsPage`, `ProjectPage`, `PlanPage`, `ReviewPage`, `ChecklistPage`, `AccountPage`, `UsersPage`, `HistoryPage`,
`AuditPage`, and `RepositoriesPage`) puts around its whole content. It has one parameter, the child content, and holds
no state. It is an authorize view: while a user is signed in it renders the child content and adds no element of its
own, so the markup and the `data-interactive` marker of the page are unchanged. When the authentication state becomes
anonymous (see `RevalidatingAuthenticationStateProvider` in the `Identity` subsystem), it re-renders and replaces the
child content, together with all its event handlers, by a `main` element with the heading "Session ended", a short text
that the account was disabled, removed, or changed and that nothing done on the page is saved, and a link to the login
page whose return address is the current page. The elements have `data-testid` values `session-ended` and
`session-ended-login-link`. It exists in the page, not in `Routes` or `MainLayout`, because each interactive page is its
own root component of the live connection and neither the router nor the layout is part of it, so only a component
inside the page can react to the state of that connection. The help pages, the About page, the login page, and the
home page are open to everyone and do not use it.

#### Key Methods

**Render (App)**: Produces the HTML document for a request.

- *Parameters*: None (the request URL reaches `Routes` through the navigation services).
- *Returns*: The rendered document.
- *Preconditions*: The component is mapped as the root component of the server.
- *Postconditions*: The document contains the appearance script ahead of the style sheet link, the head outlet content
  (including the page title), the rendered page or not-found message, and the client script reference.

**Render (Routes)**: Selects the page for the current URL.

- *Parameters*: None (route data comes from the router).
- *Returns*: The rendered page, inside the page frame, or the not-found message.
- *Preconditions*: Page components declare their routes with the standard route attribute.
- *Postconditions*: Exactly one of the matched page, the access refusal message, or the not-found message is rendered.

**OnInitializedAsync (MainLayout)**: Works out who is signed in.

- *Parameters*: None (the user comes from the authentication state of the request).
- *Returns*: A task that completes when `Actor` and `IsAdmin` are set.
- *Preconditions*: The authentication state provider is available by injection.
- *Postconditions*: `Actor` is the signed-in user, or `null` when nobody is; `IsAdmin` is true only for a signed-in user
  with the administrator role.

**ShortVersion (MainLayout)**: Shortens the version for the footer.

- *Parameters*: None (it reads the informational version of the program).
- *Returns*: The version up to the `+`, followed by `+` and at most the first seven characters of the commit identifier
  that follows it; the version unchanged when it has no `+`; the part before the `+` alone when nothing follows it.
- *Preconditions*: None.
- *Postconditions*: The result is never longer than the full version.

**Render (MainLayout)**: Produces the header, the page body, and the footer for a request.

- *Parameters*: None.
- *Returns*: The rendered header, the page, and the footer.
- *Postconditions*: The header always has the home link and the help link. It has the sign-in link when nobody is
  signed in; otherwise the projects link, the display name, the account link, and the logout form, with the
  repositories, audit, and users links for an administrator only. The footer is always present.

**Render (SessionGate)**: Guards the content of a protected page.

- *Parameters*: The child content of the page.
- *Returns*: The child content while a user is signed in; otherwise the "Session ended" message with the login link.
- *Preconditions*: The authentication state is cascaded to the page (`AddCascadingAuthenticationState` in `ServerHost`).
- *Postconditions*: When the state is anonymous, no content and no event handler of the page is left in the render tree.

#### Error Handling

N/A - the unit contains no error-producing logic. An unmatched URL is not an error: `Routes` renders the not-found
message. A browser that blocks local storage makes the appearance script use `system`, and the script ignores any other
stored value. Failures in a rendered page are handled by the framework and are outside this unit.

Known limitations: the scroll function and the appearance script have no automated browser test. The review page tests
assert the arguments the scroll function is called with, the shell test asserts that the appearance script is in the
document ahead of the style sheet, and the Puppet acceptance walk exercises the real scrolling.

#### Dependencies

- **HomePage** — found by the router at the root route; `App` does not reference it directly.
- **SignIn** — `MainLayout` uses `GetActorAsync` to read the signed-in user and the role name of an administrator.
- **Program** — supplies the informational version that the footer shortens.
- **ASP.NET Core Blazor components** — `Router`, `AuthorizeRouteView`, `AuthorizeView`, `NavLink`, `HeadOutlet`, the
  authentication state (made available to the router by `AddCascadingAuthenticationState` in `ServerHost`), the
  antiforgery token component, and the base component types.

#### Callers

- **ServerHost** — maps `App` as the root component of the server with the interactive server render mode.
- **ProjectsPage**, **ProjectPage**, **PlanPage**, **ReviewPage**, **ChecklistPage**, **AccountPage**, **UsersPage**,
  **HistoryPage**, **AuditPage**, **RepositoriesPage** — wrap their content in `SessionGate`.
