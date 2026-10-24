### App

![Web Structure](WebView.svg)

#### Purpose

`App` is the HTML shell and URL router of the web user interface. Its single responsibility
is to wrap every page in a complete HTML document and to select the page component for the
requested URL. The unit consists of two Razor components, `App` (the shell) and `Routes` (the
router).

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
For a matched URL it renders the page through a route view; for an unmatched URL it renders
the message "Page not found.".

#### Key Methods

**Render (App)**: Produces the HTML document for a request.

- *Parameters*: None (the request URL reaches `Routes` through the navigation services).
- *Returns*: The rendered document.
- *Preconditions*: The component is mapped as the root component of the server.
- *Postconditions*: The document contains the head outlet content (including the page title),
  the rendered page or not-found message, and the client script reference.

**Render (Routes)**: Selects the page for the current URL.

- *Parameters*: None (route data comes from the router).
- *Returns*: The rendered page or the not-found message.
- *Preconditions*: Page components declare their routes with the standard route attribute.
- *Postconditions*: Exactly one of the matched page or the not-found message is rendered.

#### Error Handling

N/A - the unit contains no error-producing logic. An unmatched URL is not an error: `Routes`
renders the not-found message. Failures in a rendered page are handled by the framework
and are outside this unit.

Known limitation: the scroll function itself has no automated browser test. The review page tests assert the
arguments it is called with, and the Puppet acceptance walk exercises the real scrolling.

#### Dependencies

- **HomePage** — found by the router at the root route; `App` does not reference it directly.
- **ASP.NET Core Blazor components** — `Router`, `RouteView`, `HeadOutlet`, and the base
  component types.

#### Callers

- **ServerHost** — maps `App` as the root component of the server with the interactive server
  render mode.
