### HomePage

![Web Structure](WebView.svg)

#### Purpose

`HomePage` is the hello-world landing page served at the root URL. Its single responsibility
is to show the product name and two small interactions, a Hello button that reveals a greeting
and a counter button that counts clicks, and to report when the page is interactive. The unit
is a Razor component split into presentation (`HomePage.razor`) and behavior
(`HomePage.razor.cs`).

#### Data Model

The page instance is created per browser connection, so its state is per user.

**GreetingText**: `string` (constant) — `Hello from ReviewCenter!`; the greeting text.

**Greeting**: `string?` — `null` until the Hello button is clicked, then `GreetingText`.
Invariant: the greeting is shown if and only if the value is not null.

**Count**: `int` — number of counter button clicks on this connection; starts at 0 and only
increases by 1 per click.

**_interactive**: `bool` — `false` until the first render on a live connection has completed.
Once `true` it never returns to `false`.

**InteractiveMarker**: `string` (derived) — `"true"` or `"false"` according to `_interactive`;
rendered as the `data-interactive` attribute of the root element.

The markup gives each element a stable `data-testid` so that tests and tooling can find it:
`home-page` (root element), `hello-button`, `hello-message`, `counter-button`, and
`counter-value`.

#### Key Methods

**SayHello**: Handles a click of the Hello button.

- *Parameters*: None.
- *Returns*: `void`.
- *Preconditions*: None.
- *Postconditions*: `Greeting` equals `GreetingText`. Idempotent: further clicks leave a single
  greeting displayed.

**IncrementCounter**: Handles a click of the counter button.

- *Parameters*: None.
- *Returns*: `void`.
- *Preconditions*: None.
- *Postconditions*: `Count` has increased by 1.

**OnAfterRender**: Marks the page interactive after it has rendered.

- *Parameters*: `bool firstRender` — true for the first render of this page instance.
- *Returns*: `void`.
- *Preconditions*: None.
- *Postconditions*: After the first render, `_interactive` is true and a further render has
  been requested so that the marker reaches the browser.

The framework does not call `OnAfterRender` during static server-side rendering, so reaching
it proves that the live connection exists. Only the first render changes the marker, which
prevents render loops.

#### Error Handling

N/A - the unit contains no error-producing logic; the handlers only assign or increment page
state and cannot fail. Exceptions in the framework are handled outside this unit.

#### Dependencies

- **ASP.NET Core Blazor components** — the component base class, event handling, page title
  support, and the interactive server render mode.

#### Callers

- **App** — the router in the `App` unit renders `HomePage` for the root URL.
