### HistoryPage

![Web Structure](WebView.svg)

#### Purpose

`HistoryPage` is the page at `/projects/{projectId}/reviews/{reviewId}/history`. Its responsibilities are to show the
audit entries recorded for one review, newest first, 50 to a page, and to report when the page is interactive. It
requires a signed-in user: an anonymous visitor is sent to the login page. Every signed-in user may read the history of
a review, as every signed-in user may read the review itself. It changes nothing. It is a Razor component split into
presentation (`HistoryPage.razor`) and behavior (`HistoryPage.razor.cs`).

#### Data Model

The page instance is created per browser connection, so its state is per user.

**ProjectId**, **ReviewId**: the identity of the review, from the page address.

**Before**, **After**: the position in the trail, from the `before` and `after` query values (`long?`). They are
supplied from the address, so a page of the history can be bookmarked and the Older and Newer links are plain anchors.

**Loaded**, **Review**, **ProjectName**: whether the lookup has finished, the review shown (`null` when it does not
exist), and the project name for the breadcrumb.

**Page**: the `AuditEntryPage` shown (`null` when the review does not exist).

**InteractiveMarker**: as for the other pages.

The table shows, for each entry, the time in UTC (`yyyy-MM-dd HH:mm:ss`), the actor, the action code, and the detail.
All of this text comes from the trail and is rendered as plain text: Razor encodes it, and no raw markup is ever
rendered, so a hostile text in an entry (for example a display name that contains markup) is shown as text.

Every interactive element and key value has a stable `data-testid` (see the test identifier rule of the Web
subsystem): `history-page` (root), `home-link`, `projects-link`, `project-link`, `review-link`, `history-loading`,
`history-not-found`, `history-review-title`, `history-empty`, `history-table`, and per row `history-row`,
`history-time`, `history-actor`, `history-action`, and `history-detail`, then `history-newer`, `history-older`, and
`history-back`.

#### Key Methods

**OnParametersSetAsync**: Loads the review, the project name, and the page of entries whenever the address changes.

- *Postconditions*: The previous page is cleared before the first await, so a stale page is never shown under a new
  address. The entries are read with `AuditService.ListAsync` for the project and the review; a review that does not
  exist shows the message "Review '{id}' does not exist in project {n}." and no entries.

**OnAfterRender**: Marks the page interactive after the first render on a live connection.

Paging: **Older** links to `?before={sequence of the last entry}` and **Newer** to `?after={sequence of the first entry}`;
each is shown only when the service says there is something behind it. With no entries the page says "No history has
been recorded for this review."

#### Error Handling

An unknown review is shown as a message and is not an error. The page handles no exceptions of its own; a failure of the
database during loading surfaces as a failed connection like for the other pages. The page shows only what the service
returns; it cannot show a text that was never stored, and in particular never any comment text.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content, so the page is replaced by a "Session ended" message when the
  sign-in state of the open page ends.
- **AuditService** — reads the page of entries.
- **ReviewService** and **ProjectService** — read the review and the project name.
- **ASP.NET Core Blazor components** — the component base class, query parameters, authorization attribute, and the
  interactive server render mode.

#### Callers

- **App** — the router renders the page for the history address.
- **ReviewPage** — has a History link in its title row.
