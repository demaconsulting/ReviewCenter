### HomePage

![Web Structure](WebView.svg)

#### Purpose

`HomePage` is the landing page served at the root URL. Its responsibilities are to show the product name and
tagline and, for a signed-in user, to offer a short "Get started" list of links to the pages the user can use. It is
rendered on the server as plain markup and uses no live connection. It is a Razor component with presentation only
(`HomePage.razor`, no code-behind), and it is open to anonymous visitors, who see only the name and the tagline.

#### Data Model

The page holds no state. It is built from two nested authorize views of the Blazor authorization components. The outer
one shows the "Get started" section to any signed-in user; the inner one, restricted to the administrator role named
by `SignIn.AdminRole`, adds the links that only an administrator can use. The section is a `card` and its entries are
an `item-list`, each entry being a link followed by a one-line description in the `muted` style; the tagline uses the
`muted` style too.

The entries are:

- **Projects**: for every signed-in user; links to `/projects`.
- **Repositories**: for an administrator; links to `/repositories`.
- **Audit**: for an administrator; links to `/audit`.
- **Users**: for an administrator; links to `/users`.

The markup gives each element a stable `data-testid` so that tests and tooling can find it: `home-page` (root
element), `home-get-started` (the section), and `home-card-projects`, `home-card-repositories`, `home-card-audit`,
and `home-card-users` (the links). There is no `data-interactive` marker, because the page has no live connection. The
page has no link to the home page itself; the brand link in the header of every page (see `App`) serves that purpose.

#### Key Methods

N/A - the unit has no handlers; the links are followed by the browser.

#### Error Handling

N/A - the unit contains no error-producing logic. The administrator links are a convenience that mirrors the header; the
access rules are enforced by the target pages and the authorization of the `Identity` subsystem, so a hidden link is
not a protection.

#### Dependencies

- **SignIn** — `SignIn.AdminRole` names the role that sees the administration links.
- **ASP.NET Core Blazor components** — page routing, page title support, and the authorize view that reads the
  authentication state.

#### Callers

- **App** — the router in the `App` unit renders `HomePage` for the root URL.
