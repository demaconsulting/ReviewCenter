### AuditPage

![Web Structure](WebView.svg)

#### Purpose

The `AuditPage` unit consists of two pages, a form, and a shared component. `AuditPage` is the page at `/audit`. Its
responsibilities are to show the whole audit trail of the system, newest first, 25, 50 (the default) or 100 entries to
a page, to let a system administrator narrow it with the filter form, and to let a system administrator verify the hash
chain with the *Verify chain* button and read the
result, including the head hash. It requires the `SystemAdmin` role: a signed-in user without it is shown the
access denied message of the router instead of the page, and an anonymous visitor is sent to the login page. It
changes nothing. `ProjectAuditPage` is the page at `/projects/{id}/audit`: it shows the audit entries of one project,
newest first, to the project administrators and the system administrators (and to Auditors once that role can be
given; the page asks the audit-read access of `ProjectViewGuard`, `GuardAuditAsync`, and not the administer right),
with the same filter form (without the project selector) and paging, and it has no verification (the chain verifies as
a whole on `/audit`; the project view is a filtered
read of the same chain). The entries of a project are those that name the project, among them the member changes and
`project.evidence-settings-changed`, which the evidence settings page causes through `EvidenceSettingsService`; the pages
show every entry in the same way and know no action by name. It requires a signed-in user and shows the same "Project N
does not exist." message as the
other project pages to a project that does not exist, to a user who may not see it, and to a user who sees it but may
not read its audit trail. `AuditFilterForm` is the form that both pages use to narrow the entries, and
`AuditFilterState` is the parsed and cleaned filter that the address carries. `AuditEntryTable` is the component that
both pages use to show one page of entries with the paging
buttons; it holds no state and no access rule, because its parent decides who sees the entries and clears them when the
right is lost. Each is a Razor component split into presentation (`AuditPage.razor`, `ProjectAuditPage.razor`,
`AuditFilterForm.razor`, `AuditEntryTable.razor`) and behavior (the matching `.razor.cs` files).

#### Data Model

The page instance is created per browser connection, so its state is per user.

**Before**, **After**: the position in the trail, from the `before` and `after` query values (`long?`), on both pages.

**ActorQuery**, **ActionQuery**, **ReviewQuery**, **FromQuery**, **ToQuery**, **SizeQuery**, **ProjectQuery**
(query values, `AuditPage`; the project page has no `ProjectQuery`): the raw `actor`, `action`, `review`, `from`, `to`,
`size`, and `project` query values. They are
strings and are never parsed by the framework; `AuditFilterState.FromQuery` reads them, so a value that is not usable
can never raise.

**Filter** (`AuditFilterState`): the cleaned filter: the actor and review texts are trimmed and cut to the width of the
stored field, the action is accepted only when it is a known action or a known group (`AuditActions`), the dates only
as `yyyy-MM-dd` in the invariant culture, the project only as a positive integer, and the page size only when it is 25,
50 or 100 (otherwise 50). A From date after the To date can never match, so both dates are dropped and a message says
so (`audit-filter-message`). `ToFilter` builds the `AuditFilter`, and `ToQuery` writes the state and a position back
into a query string, escaped, so the Older and Newer links keep the filter.

**Loaded**, **Page**: whether the lookup has finished and the `AuditEntryPage` shown (`null` while not loaded, or when the
user is refused), on both pages.

**Result** (`AuditPage`): the `AuditVerificationResult` of the last verification (`null` when none was run).
**ErrorMessage** (`AuditPage`): the
message of the last refusal, or `null`. **InteractiveMarker**: as for the other pages. **Projects** (`AuditPage`): the
projects the system administrator may see, for the project selector.

**ProjectId**, **Project**, **BasePath** (`ProjectAuditPage`): the route parameter, the project (null unless the user
may read its audit trail), and the address `/projects/{id}/audit` that the paging buttons extend.

**Page**, **BasePath**, **EmptyText**, **Filter** (`AuditEntryTable`): the parameters of the component: the entries to
show, the address of the parent page without a query (`/audit` or `/projects/{id}/audit`), the sentence for an empty
page (by default "Nothing has been recorded yet."), and the filter whose query the Older and Newer links carry. When
the filter narrows the trail and nothing matches, the table says "No entry matches the filters." instead.

**State**, **BasePath**, **Projects** (`AuditFilterForm`): the filter shown, the address the form is submitted to
(a GET, so every filtered view has an address), and the projects of the selector (`null` on the project page, which
has no selector).

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
`audit-detail`, then `audit-newer` and `audit-older` (the table, the rows, `audit-empty`, and the paging buttons come
from `AuditEntryTable`, so both pages have them). `AuditFilterForm` has `audit-filter-form`, `audit-filter-actor`,
`audit-filter-action`, `audit-filter-review`, `audit-filter-from`, `audit-filter-to`, `audit-filter-size`,
`audit-filter-apply`, `audit-filter-clear` (only while a value differs from the default), `audit-filter-message`, and,
on the whole-trail page only, `audit-filter-project`. `ProjectAuditPage` has `project-audit-page` (root),
`breadcrumb-projects-link`, `breadcrumb-project-link`, `project-audit-loading`, and `project-not-found`.

#### Key Methods

**OnParametersSetAsync** (`AuditPage`): Loads the page of entries whenever the address changes, for a system
administrator only.

- *Postconditions*: The previous page is cleared before the first await. The filter is read from the query values with
  `AuditFilterState.FromQuery`, and the entries are read with `AuditService.ListAsync(filter, before, after, size)` for
  the whole trail (or the chosen project); the project selector lists the projects the user may see. A user who is not
  an enabled system administrator gets the message "System administrator rights are required." and no entries.

**OnParametersSetAsync** (`ProjectAuditPage`): Loads the page of entries whenever the address changes, for a user who
may read the audit trail of the project only.

- *Postconditions*: The project and the page are cleared before the first await. `ProjectViewGuard.GuardAuditAsync`
  (the audit-read access of `IProjectAccess`: a project administrator or a system administrator, and an Auditor once
  that role can be given) decides whether to load at all; when it refuses, `ShowNoAccess` leaves no project and no
  entries, and the page shows the not-found message. Otherwise the project is read with `ProjectService.GetAsync` and
  the entries with `AuditService.ListAsync(filter, before, after, size)`, where the filter always carries the project
  of the route whatever the address says, so the page returns only the entries that carry the project (never those of
  another project or those without one). The page holds no other access rule; every load goes through the shared
  guard, so a user who lost the right sees nothing stale.

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

**DetailContent** (private, `AuditEntryTable`): Builds the content of the detail cell from `SplitDetail`, which splits the
detail into plain
text parts and runs of 24 or more hexadecimal characters. A run is written as a `span` with the class `audit-hash`, the
full value as its `title`, and `ShortHash` of it as the content; text parts are added as content, so Razor encodes them.
The concatenated text of the parts is the original detail.

**OnAfterRender** (both pages): Marks the page interactive after the first render on a live connection.

#### Error Handling

A refusal is shown as a message ("System administrator rights are required." or the message of the ended sign-in state)
and not
raised; `ProjectAuditPage` shows the not-found message instead. A value of the address that is not usable is dropped
and never raised (a `from` after `to` is reported as a message); the filter built from the state is valid by
construction, so the service never refuses it (`AuditFilterInvalid` is the guard for other callers). The pages handle
no other exception of their own. A
long trail is read in pages, the page size is capped, and the verification streams the
trail in batches, so neither holds the whole trail in memory; the Verify button is available to system administrators
only.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content.
- **AuditService** — reads the page of entries (the whole trail, or one project) and verifies the chain.
- **AuditFilter** and **AuditActions** (Core) — the filter that is passed to the service and the vocabulary of the
  action choices.
- **UserService** — looks up the signed-in user for the system administrator check (`AuditPage`).
- **ProjectService** and **IProjectAccess** (Core) — read the project for the actor and, through `ProjectViewGuard`,
  decide whether the user may read its audit trail (`ProjectAuditPage`); `ProjectService` also lists the projects for
  the selector of `AuditPage`.
- **AuthenticationStateProvider** (ASP.NET Core) — gives the signed-in user.
- **ASP.NET Core Blazor components** — the component base class, events, query parameters, authorization attribute, and
  the interactive server render mode.

#### Callers

- **App** — the router renders `AuditPage` for `/audit` and `ProjectAuditPage` for `/projects/{id}/audit`.
- **App** (`MainLayout`) — the header has a link to `/audit` while a system administrator is signed in.
- **ProjectPage** — the "Audit trail" button for an administrator of the project leads to `/projects/{id}/audit`; the
  entries of the evidence settings page (`project.evidence-settings-changed`) are shown there.
- **HelpAuditPage** (`HelpPage` unit) — describes both views.
