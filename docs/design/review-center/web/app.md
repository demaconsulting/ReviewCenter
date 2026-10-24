### App

![Web Structure](WebView.svg)

#### Purpose

`App` is the HTML shell, URL router, and page frame of the web user interface. Its responsibilities
are to wrap every page in a complete HTML document, to select the page component for the
requested URL, and to put the same header, with the sign-in state, above every page. The unit consists of three Razor
components, `App` (the shell), `Routes` (the router), and `MainLayout` (the page frame).

#### Data Model

`App` holds no state. The components are declarative markup with no fields or properties of
their own.

**App component**: Static server-rendered markup that emits the document type, the language
attribute, the character set and viewport declarations, the base address (`/`), the page head
outlet, the `Routes` component, a small inline script that defines `window.reviewCenter.scrollToTestId` (the review
page calls it to bring the selected file's code, a thread's first line, or the new-thread form into view; Blazor
cannot scroll by itself), and a script reference to
`_framework/blazor.web.js`.

**Routes component**: A router that searches the application assembly for page components.
For a matched URL it renders the page through an authorize route view, inside `MainLayout` as the default layout; for an
unmatched URL it renders the message "Page not found.". The authorize route view applies the authorization attributes
of the page also when the user navigates inside an open interactive session, where the endpoint check does not run
(a plain route view would ignore them). When it refuses, the router shows, in the page frame, "Access denied" for a
signed-in user (with a link back to the projects) and "Sign in required" with a link to the login page for a signed-out
user; the link carries the requested address as `ReturnUrl`. The elements have `data-testid` values
`access-denied`, `access-denied-projects-link` and `access-denied-login-link`.

**MainLayout component**: The page frame, rendered as static server-side markup (it needs no live connection). It
reads the authentication state of the request once. When nobody is signed in, its header shows a "Sign in" link to
the login page. When a user is signed in, the header shows the display name of the user, a link to the account
page, a link to the users page only when the user is an administrator, and a logout form that posts to
`/auth/logout` with an antiforgery token (signing out changes state, so it is a form and not a link). The header
elements have `data-testid` values: `signin-link`, `current-user`, `account-link`, `users-link`, and `logout-button`.
The layout only shows links; the access rules are enforced by the pages and the authorization of the `Identity`
subsystem, so a hidden link is a convenience and not a protection.

#### Key Methods

**Render (App)**: Produces the HTML document for a request.

- *Parameters*: None (the request URL reaches `Routes` through the navigation services).
- *Returns*: The rendered document.
- *Preconditions*: The component is mapped as the root component of the server.
- *Postconditions*: The document contains the head outlet content (including the page title),
  the rendered page or not-found message, and the client script reference.

**Render (Routes)**: Selects the page for the current URL.

- *Parameters*: None (route data comes from the router).
- *Returns*: The rendered page, inside the page frame, or the not-found message.
- *Preconditions*: Page components declare their routes with the standard route attribute.
- *Postconditions*: Exactly one of the matched page, the access refusal message, or the not-found message is rendered.

**Render (MainLayout)**: Produces the header and the page body for a request.

- *Parameters*: None (the user comes from the authentication state of the request).
- *Returns*: The rendered header followed by the page.
- *Postconditions*: The header shows the sign-in link when nobody is signed in, and otherwise the display name, the
  account link, the users link for an administrator only, and the logout form.

#### Error Handling

N/A - the unit contains no error-producing logic. An unmatched URL is not an error: `Routes`
renders the not-found message. Failures in a rendered page are handled by the framework
and are outside this unit.

Known limitation: the scroll function itself has no automated browser test. The review page tests assert the
arguments it is called with, and the Puppet acceptance walk exercises the real scrolling.

#### Dependencies

- **HomePage** — found by the router at the root route; `App` does not reference it directly.
- **SignIn** — `MainLayout` uses `GetActorAsync` to read the signed-in user and the role name of an administrator.
- **ASP.NET Core Blazor components** — `Router`, `AuthorizeRouteView`, `HeadOutlet`, the authentication state (made
  available to the router by `AddCascadingAuthenticationState` in `ServerHost`), the
  antiforgery token component, and the base component types.

#### Callers

- **ServerHost** — maps `App` as the root component of the server with the interactive server
  render mode.
