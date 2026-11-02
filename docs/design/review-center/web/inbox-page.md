### InboxPage

![Web Structure](WebView.svg)

#### Purpose

`InboxPage` is the page at `/inbox`. Its responsibilities are to list the reviews that wait for the signed-in user, in
seven groups, say in one sentence how much needs action, mark what is new, and let the user mark everything as read. The
inbox is also the notification list; nothing is stored for it beyond what the user has seen. The page needs a signed-in
user, which an anonymous visitor is sent to the login page for. It talks to the domain only through `InboxService`,
which returns only reviews of projects the user may see, so a user without access gets an empty inbox. It is a Razor
component split into presentation (`InboxPage.razor`) and behavior (`InboxPage.razor.cs`).

#### Layout

The page uses the standard page container with a heading row that holds the title and, when the inbox has rows, the
"Mark all as read" button. Below the heading a summary line says how many reviews need action and how many updates
there are, and, only when something waits, how many reviews wait for others. When no review needs action a muted line
says so. Each group that has rows has a heading and a list of row
cards (To answer, Disputed, Ready to sign, Replies waiting, Failed checks, Waiting for others, Updates, in this order).
A row shows the review title as a link (to the checklist for To answer and Disputed, to the review for the others),
the
project and the review identifier muted, an optional detail sentence, and on the right a "New" badge when the row is
unread, a chip with the reason (the Updates group includes the Rejected chip for a review concluded as rejected), and,
for the action groups, the due date, marked "Overdue" when it is past. The page shows what the service gives and
decides nothing about masking: a user who has not handed in a ballot is only given the To answer row, which holds no
verdict of the others.

#### Data Model

The page instance is created per browser connection, so its state is per user.

**Items**: the rows of the inbox. **ActionCount**: the number of reviews that need action. **InteractiveMarker**: as for
the other pages.

Every interactive element and key value has a stable `data-testid`: `inbox-page` (root), `inbox-summary`,
`inbox-empty`, `inbox-mark-read`, `inbox-section-{key}` and `inbox-list-{key}` (the key is `to-answer`, `disputed`,
`to-sign`, `replies-waiting`, `failed-checks`, `waiting-for-others`, or `updates`), and for each row, by its position
in the list, `inbox-row-{n}`,
`inbox-link-{n}`, `inbox-review-{n}`, `inbox-detail-{n}`, `inbox-unread-{n}`, `inbox-chip-{n}`, and `inbox-due-{n}`.

#### Key Methods

**OnInitializedAsync**: Loads the inbox of the signed-in user with `InboxService.GetInboxAsync`.

**MarkAllReadAsync**: Calls `InboxService.MarkAllReadAsync` for the signed-in user and loads the inbox again, so the
rows show without the "New" badge. Nothing is marked for nobody signed in.

**OnAfterRender**: Marks the page interactive after the first render on a live connection.

#### Error Handling

An empty inbox is shown as the message that nothing needs action, not as an error. A user who has lost access in the
meantime gets an empty inbox at the next load, because the service decides on the current access. The page handles no
other exception of its own.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content.
- **InboxService** — builds the inbox and marks all as read.
- **AuthenticationStateProvider** (ASP.NET Core) — gives the signed-in user.
- **ASP.NET Core Blazor components** — the component base class, events, authorization attribute, and the interactive
  server render mode.

#### Callers

- **App** — the router renders the page for `/inbox`.
- **HomePage** — has the Inbox card that links to the page.
