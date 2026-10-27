### AboutPage

![Web Structure](WebView.svg)

#### Purpose

`AboutPage` is the page at `/about`. Its responsibilities are to say in a few sentences what ReviewCenter does, and
to show which version is running, who holds the copyright, and where the project lives. It is reached from the About
link in the footer of every page. It is rendered on the server as plain markup and uses no live connection. It is a
Razor component with presentation only (`AboutPage.razor`, no code-behind), and it is open to anonymous visitors.

#### Data Model

The page holds no state. It shows these elements:

- the heading "About ReviewCenter" and the tagline;
- a card "What ReviewCenter does": a paragraph saying that a set of files is reviewed against an earlier revision, that
  reviewers discuss the changes in threaded comments, work through a checklist, and are assigned to reviewer slots with
  alternates, that each reviewer signs off with a re-authenticated signature, and that every action is kept in a
  hash-chained audit trail that can be verified;
- a card "Version": the full informational version of the program (the text of `Program.Version`, including the commit
  identifier after the `+`), the copyright line for DEMA Consulting, and a link to the project page on GitHub.

The markup gives each element a stable `data-testid` so that tests and tooling can find it: `about-page` (root
element), `about-tagline`, `about-version`, and `about-github-link`. There is no `data-interactive` marker, because the
page has no live connection.

#### Key Methods

N/A - the unit has no handlers; the link is followed by the browser.

#### Error Handling

N/A - the unit contains no error-producing logic.

#### Dependencies

- **Program** — supplies the full informational version that the page shows.
- **ASP.NET Core Blazor components** — page routing and page title support.

#### Callers

- **App** — the router in the `App` unit renders `AboutPage` for `/about`, and the footer of `MainLayout` links to it.
- **HelpPage** — the help index links to it.
