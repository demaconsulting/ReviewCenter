### AuditPage

![Web Structure](WebView.svg)

#### Purpose

`AuditPage` is the page at `/audit`. Its responsibilities are to show the whole audit trail of the system, newest first,
50 entries to a page, and to let a system administrator verify the hash chain with the *Verify chain* button and read the
result, including the head hash. It requires the `SystemAdmin` role: a signed-in user without it is shown the
access denied message of the router instead of the page, and an anonymous visitor is sent to the login page. It
changes nothing. It is a Razor component split into presentation
(`AuditPage.razor`) and behavior (`AuditPage.razor.cs`).

#### Data Model

The page instance is created per browser connection, so its state is per user.

**Before**, **After**: the position in the trail, from the `before` and `after` query values (`long?`).

**Loaded**, **Page**: whether the lookup has finished and the `AuditEntryPage` shown (`null` while not loaded, or when the
user is refused).

**Result**: the `AuditVerificationResult` of the last verification (`null` when none was run). **ErrorMessage**: the
message of the last refusal, or `null`. **InteractiveMarker**: as for the other pages.

The table shows, for each entry, the sequence number, the time in UTC, the actor, the action code, the review (as
`{project}/{review}`, `project {id}`, or a dash), and the detail, all as plain text. The time, actor, action, and review
do not wrap. A run of 24 or more hexadecimal characters in the detail (a hash or a fingerprint) is shown in a monospace
font, shortened to its first 12 characters and an ellipsis, with the full value as the `title` tooltip of that part of
the detail; the rest of the detail is unchanged, and the full value is still part of the stored entry. The head hash of
a verification result is not shortened.

The page uses the wide page container, so the table has room, and a heading with a short lead line. The table scrolls
sideways inside its container on a narrow screen instead of widening the page.

The result text is `Chain intact: N entries, head hash <64 hex>` for an intact chain and
`Chain broken at entry N. Head hash <hash>` for a broken one, where the head hash of a broken chain is the hash of the
last entry that verified. The hash is shown in full so that an operator can copy it.

**What the result means.** An intact chain means that no entry of the stored history was changed, removed from the
middle, inserted, or reordered. It does **not** prove that the newest entries are all still there, and it does not
protect against a complete rewrite by someone with write access to the database: both are detectable only by comparing
the count and the head hash with a value that was recorded somewhere else earlier. A system administrator who wants that
protection records the head hash externally (for example in a ticket or a signed document) at agreed times; this slice
does not do that automatically and has no external anchoring.

Every interactive element and key value has a stable `data-testid`: `audit-page` (root), `audit-error`,
`audit-verify`, `audit-result`, `audit-head-hash`, `audit-empty` ("Nothing has been recorded yet."), `audit-table`,
and per row `audit-row`, `audit-sequence`, `audit-time`, `audit-actor`, `audit-action`, `audit-review`, and
`audit-detail`, then `audit-newer` and `audit-older`.

#### Key Methods

**OnParametersSetAsync**: Loads the page of entries whenever the address changes, for a system administrator only.

- *Postconditions*: The previous page is cleared before the first await. The entries are read with
  `AuditService.ListAsync` for the whole trail. A user who is not an enabled system administrator gets the message
  "System administrator rights are required." and no entries.

**VerifyAsync**: Verifies the whole chain and shows the result.

- *Postconditions*: The previous result and message are cleared; the system administrator check is repeated, and a
  refusal
  reads nothing. Otherwise `AuditService.VerifyAsync` runs and its result is shown. The verification is a click on the
  live connection and not a form post, so the framework's circuit protection applies instead of an antiforgery token.

**RequireAdministratorAsync**: Reads the signed-in user from the authentication state (through the `Identity` helper that
turns it into an `Actor`) and looks the user up in the user store; unless that user is enabled and is a system
administrator, the action is refused. The check is kept on load and on every verification although the route is
limited to
system administrators and the page is replaced when the sign-in state ends (see `SessionGate`), because an open page
can keep
its state for up to one revalidation interval after the user was demoted or disabled. A state that has already ended is
shown with the message of the refusal.

**DetailContent** (private): Builds the content of the detail cell from `SplitDetail`, which splits the detail into plain
text parts and runs of 24 or more hexadecimal characters. A run is written as a `span` with the class `audit-hash`, the
full value as its `title`, and `ShortHash` of it as the content; text parts are added as content, so Razor encodes them.
The concatenated text of the parts is the original detail.

**OnAfterRender**: Marks the page interactive after the first render on a live connection.

#### Error Handling

A refusal is shown as a message ("System administrator rights are required." or the message of the ended sign-in state)
and not
raised. The page handles no other exception of its own. A long trail is read in pages, and the verification streams the
trail in batches, so neither holds the whole trail in memory; the Verify button is available to system administrators
only.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content.
- **AuditService** — reads the page of entries and verifies the chain.
- **UserService** — looks up the signed-in user for the system administrator check.
- **AuthenticationStateProvider** (ASP.NET Core) — gives the signed-in user.
- **ASP.NET Core Blazor components** — the component base class, events, query parameters, authorization attribute, and
  the interactive server render mode.

#### Callers

- **App** — the router renders the page for `/audit`.
- **App** (`MainLayout`) — the header has a link to this page while a system administrator is signed in.
