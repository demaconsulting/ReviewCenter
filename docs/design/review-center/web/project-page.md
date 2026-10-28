### ProjectPage

![Web Structure](WebView.svg)

#### Purpose

The `ProjectPage` unit consists of five pages. `ProjectPage` is the page at `/projects/{id}`: its responsibilities are
to show one project and its reviews as clickable rows, each with its state and approval status, to lead to the page
that creates a review, to lead to the plan page and, for whoever administers the project, to the repositories page, the
audit trail of the project, and the evidence settings page, to let an administrator of the project (a project admin or a
system administrator)
manage the members of the project (change a role, remove, and lead to the page that adds one), and to report when it is
interactive. `EvidenceSettingsPage` is the page at `/projects/{id}/evidence-settings`: it shows and changes the
evidence settings of the project (see below). `NewMemberPage` is the page at `/projects/{id}/members/new`: it adds an
existing user to the project by
the exact user name. `NewMemberAccountPage` is the page at `/projects/{id}/members/account`, reached by a button of
`NewMemberPage`: it creates a local account as part of adding it to the project and shows the generated temporary
password once. `NewReviewPage` is the
page at `/projects/{id}/reviews/new`: it holds only the form that creates a review, either from files selected in the
browser (enforcing the upload limits while the files are read) or from the changes in a git repository of the
project.
All of them require a signed-in user, and all show only the message "Project N does not exist." for a project that does
not exist and, in just the same way, for a project the user may not see (the project is read with
`ProjectService.GetAsync(actor, id)`, which returns nothing for it), so a user who is no member cannot tell a hidden
project from a missing one; the two member pages and the evidence settings page show the same message to a user who
sees the project but does not administer it. The response is a normal page with HTTP status 200. Each is a Razor
component split into
presentation (`ProjectPage.razor`, `NewReviewPage.razor`, `NewMemberPage.razor`, `NewMemberAccountPage.razor`,
`EvidenceSettingsPage.razor`) and
behavior (the matching `.razor.cs` files). Next to the heading,
`ProjectPage` shows a "+ New review" button that leads to the form page, an outline "Plan reviews" link to the
plan page (`/projects/{id}/plan`) when at least one repository of the project is enabled, and, for a system
administrator and a project admin of the project, a "Repositories" button to `/projects/{id}/repositories`, an
"Audit trail" button to `/projects/{id}/audit`, and an "Evidence settings" button to
`/projects/{id}/evidence-settings`; when the project has
no enabled repository, a short note says to add one with the Repositories button (to whoever manages the project) or
to ask a project administrator (to anybody else). The rare actions sit on their own pages behind buttons, so the
members section holds only a "+ Add member" button, and the page never loads a list of users.

Because `/projects/{id}/reviews/new` is the address of the form, the review identifier `new` (in any letter case) is
reserved: `NewReviewPage` refuses it, so a review that could never be opened is never created.

`EvidenceSettingsPage` (`EvidenceSettingsPage.razor` and `.razor.cs`) is the page of the per-project evidence settings,
which only a project admin or a system administrator can see and change. It shows seven switches in two groups, with
anonymous access first because it is the riskier one, and one "Save settings" button for all of them, so the stored
settings are always one consistent set. Anonymous access: **Anonymous evidence index** and **Anonymous attestation
PDFs**, both off by default and independent of each other; the description of the index says that it alone is useless
to ReviewMark unless anonymous PDFs are on too or the client has credentials. PDF content, which applies to every
attestation PDF of the project: **Include review discussion threads**, **Include reviewer names**, **Include checklist
wording**, **Include file list and hashes**, and **Include git context**, all on by default. The page decides nothing
about the effect of a switch: the settings and their rules are `EvidenceSettings` and `EvidenceSettingsService` in
`Core`, and the PDF and the API read them there. The page's part is to show them, to warn while a choice is made, and
to pass the choice to the service.

The page follows the human-factors rules of the product. Where am I: the heading "Evidence settings" with the project
name in the breadcrumb. What can I do: each switch has a one-sentence description of what it does. What happened: a
saved change is confirmed in words and a refusal is shown in words. The warnings follow the switches at once, before
saving:

- A content switch that is off shows a warning of its own below it (for example "Omitting reviewer names may not
  satisfy your regulatory regime. Confirm with your quality or compliance lead."); each of the five says what is lost.
- A notice at the top lists every section the choices leave out of the PDFs (`EvidenceSettings.OmittedSections`).
- While an anonymous switch is on, an exposure warning says that anyone who can reach the server can read what was
  opened, and the request limit per minute and address for anonymous requests. When the anonymous PDFs are on and the
  discussion threads or the reviewer names are still included, the warning adds a stronger sentence that names what the
  anonymous readers will see (the review discussion, the reviewer names, or both).

#### Data Model

The page instances are created per browser connection, so their state is per user.

**ProjectId**: `int` — the route parameter of both pages.

**Loaded**, **Project**: whether loading finished and the project (`null` when it does not exist or the user may not
see it), on both pages.

**Reviews** (`ProjectPage`): the reviews of the project.

**ApprovedReviews** (`ProjectPage`): the identifiers of the reviews that are approved (at least one reviewer slot,
and every slot has a current signature), filled while loading.

**Repositories**: the enabled repositories of this project (`RepositorySummary` values from
`RepositoryService.ListEnabledAsync(actor, projectId)`, which carry no location and no token, and none of another
project). `ProjectPage` uses them to decide whether to show the plan link;
`NewReviewPage` offers them for the repository source.

**IsSystemAdmin** and **Candidates**, the inline add form, and its state no longer exist: `ProjectPage` neither lists
users nor holds a form for adding one. **CanAdminister** (`ProjectPage`): whether the user administers the project (a
system administrator or a project admin), from `IProjectAccess`. **Members** (`ProjectPage`): the `ProjectMember` list
from `ProjectService.ListMembersAsync`, loaded only when `CanAdminister`.
**MemberError**, **MemberStatus**: the refusal (for example the last-project-admin message) or the outcome of the last
membership change.

The remaining state belongs to `NewReviewPage`, `NewMemberPage`, `NewMemberAccountPage`, and `EvidenceSettingsPage`.

**Project**, **Loaded** (`EvidenceSettingsPage`): the project, which is null unless the user administers it, and whether
the lookup finished. **Saved** (`EvidenceSettingsPage`): the `EvidenceSettings` as stored when the page loaded or last
saved; a save is based on them. **Draft**: the settings as the switches show them now; the warnings and the summary are
computed from it. **ErrorMessage**, **StatusMessage**: the message of the last refused save, and the confirmation of the
last save.

**Project**, **Loaded** (`NewMemberPage`, `NewMemberAccountPage`): the project, which is null unless the user
administers it, and whether the lookup finished. **Username**, **AsProjectAdmin**, **ErrorMessage** (both member pages):
the exact user name typed, the project admin checkbox, and the message of the last refusal. **DisplayName**
(`NewMemberAccountPage`): the display name typed. **Created**, **CopyStatus** (`NewMemberAccountPage`): the
`NewMemberAccount` that was just created (the member and its temporary password) and the result of the last copy; they
exist only in the memory of the page instance, are cleared by Done, by a lost right, and when the page is left, and are
never stored or logged.

**ReviewId**, **ReviewTitle**: the values typed into the form.

**SelectedFiles**: the files read from the file input, as `UploadedFile` values held in memory.

**ErrorMessage**: the message of the last rejected selection or submission.

**FromRepository**: whether the form creates the review from a repository. **RepositoryIdText**, **HeadRevision**,
**BaselineRevision**, **Selection**: the repository form values; the selection starts as `**/*`. **Busy**: set while
a creation runs, so a second submit is ignored. **CreatedReview**, **SkippedEntries**: the review just created from a
repository and the entries that were skipped, shown before the review is opened.

**InteractiveMarker**: as for the other pages; each page has its own.

Test identifiers of `ProjectPage`: `project-page` (root), `breadcrumb-projects-link`, `project-loading`, `project-not-found`,
`project-name`, `project-description`, `new-review-button` (the link that leads to the form page), `reviews-list`,
`reviews-empty`, `review-row-{id}`, `review-link-{id}`, `review-title-{id}`, `review-state-{id}`,
`review-approval-{id}`, `review-created-{id}`, `plan-reviews-link` (the link to the plan page, shown when at least
one repository of the project is enabled), `plan-reviews-unavailable` (the note shown when none is),
`repositories-link` (the Repositories button, shown to whoever administers the project), `project-audit-link` (the
"Audit trail" button, shown to the same), and, for an administrator of the project only, the Members section:
`member-error`, `member-status`, `members-empty`, `members-list`, and per member (by user
name) `member-row-{username}`, `member-name-{username}`, `member-role-{username}`, `member-toggle-role-{username}`,
and `member-remove-{username}`, then `add-member-button` (the "+ Add member" link button to the add member page). Each
review is one row
of `reviews-list`: the identifier is the link, followed by the title, the state and approval badges, and the creation
date. `evidence-settings-link` is the "Evidence settings" button, shown to whoever administers the project.

Test identifiers of `EvidenceSettingsPage`: `evidence-settings-page` (root), `breadcrumb-projects-link`,
`breadcrumb-project-link`, `evidence-settings-loading`, `project-not-found`, `evidence-settings-form`,
`evidence-settings-error`, `evidence-settings-status`, `evidence-reduced-banner` (the summary of what is left out),
`evidence-anonymous-warning` and `evidence-anonymous-people-warning` (the exposure warning and its stronger sentence),
`evidence-settings-save`, `evidence-settings-back`, and per switch, with the key `anonymous-index`,
`anonymous-attestations`, `threads`, `reviewer-names`, `checklist-wording`, `files`, or `git-context`:
`evidence-setting-{key}` (the row), `evidence-{key}-switch` (the checkbox), and, for the five content switches,
`evidence-{key}-warning` (shown while the switch is off).

Test identifiers of `NewMemberPage`: `new-member-page` (root), `breadcrumb-projects-link`, `breadcrumb-project-link`,
`new-member-loading`, `project-not-found`, `new-member-form`, `new-member-username-input`,
`new-member-admin-checkbox`, `new-member-error`, `new-member-submit`, `new-member-cancel`, and
`new-member-create-account-link` (the button that leads to the create account page).

Test identifiers of `NewMemberAccountPage`: `new-account-page` (root), `breadcrumb-projects-link`,
`breadcrumb-project-link`, `breadcrumb-add-member-link`, `new-account-loading`, `project-not-found`,
`new-account-form`, `new-account-username-input`, `new-account-display-name-input`, `new-account-admin-checkbox`,
`new-account-error`, `new-account-submit`, `new-account-cancel`, and, once the account exists,
`new-account-password-box`, `new-account-username-value`, `new-account-password-value`,
`copy-account-password-button`, `account-password-copy-status`, and `done-account-button`.

Test identifiers of `NewReviewPage`: `new-review-page` (root), `breadcrumb-projects-link`, `project-link`, `project-loading`,
`project-not-found`, `new-review-form`, `review-id-input`, `review-title-input`, `review-source` (the source choice),
`source-upload-radio`, `source-repository-radio`, `no-repository-hint`, `repository-select`, `head-revision-input`,
`baseline-revision-input`, `selection-input`, `review-files-input`, `selected-files-list`, `selected-file-{index}`,
`selected-file-path-{index}`, `selected-file-size-{index}`, `review-error`, `create-review-button`,
`cancel-new-review-link` (an outline link back to the project), `review-created`, `skipped-entries-list`,
`skipped-entry-{index}`, and `open-created-review-link`.

The form offers one choice as two selectable option cards, "Upload files" (the default) or "From a repository", and
shows only the fields of the chosen source, so the page stays short. The repository choice is disabled, with a hint,
when no repository is enabled. The repository fields are the repository (a drop-down of enabled repositories by
name), the head revision, the optional baseline revision (blank: every file at the head counts as added), and the
selection (a text area with one pattern per line, `!` to exclude).

#### Key Methods

**OnParametersSetAsync** (`ProjectPage`): Loads the project for the actor with `ProjectService.GetAsync(actor, id)`.
When the project does not exist or the user may not see it, `Project` is null, nothing else is loaded, and the page
shows the same message for both. Otherwise it loads the reviews, the enabled repositories of the project, and whether
the user administers the project, and, only for an administrator, the members, then asks
`SigningService.GetApprovalAsync` once for each review to learn whether it is approved (N+1 lookups, accepted because
the list of reviews of a project is short). Each review row shows, next to its state, a badge that reads "Approved" when
the review is in
`ApprovedReviews` and "In review" otherwise; a review without slots is therefore In review.

**RemoveMemberAsync**, **ToggleRoleAsync** (`ProjectPage`): Remove a member (`ProjectService.RemoveMemberAsync`) and
switch a member between `ProjectRole.User` and `ProjectRole.ProjectAdmin` (`SetMemberRoleAsync`). Each goes through one
shared step (`RunMemberChangeAsync`) that first runs `GuardMembersAsync`, then asks for the current user, runs the
change, shows the outcome in `member-status` (only after the members were reloaded) or the message of a
`DomainRuleException` in `member-error` (only when the guard, asked again, still allows it), and reloads the members.
The page holds no membership rule; the refusal to remove or demote the last project administrator comes from
`ProjectService` and is shown as the error of the section.

**GuardMembersAsync** (`ProjectPage`): Asks `ProjectViewGuard.GuardViewAsync` whether the user still sees the project;
when not, `ShowNoAccess` drops the project, the reviews, the approved set, the repositories, and the member state, and
the page shows the not-found message. Then it asks `ProjectViewGuard.GuardAdministerAsync` whether the user still
administers the project; when not, `ShowMembersLost` clears `CanAdminister`, the members, and the member messages (and
with them the Members section and the administration buttons), and the reviews stay, because the user may still be a
member. The action is then not run. The members are data only an administrator may read, so an error shown above them
would not be enough.

**OnParametersSetAsync** (`NewMemberPage`, `NewMemberAccountPage`): Reset the form values and messages (the router
reuses the component), read the actor, and load the project only when the user administers it
(`CanAdministerAsync`, then `ProjectService.GetAsync`); anyone else sees the same "does not exist" message and no form.

**AddMemberAsync** (`NewMemberPage`): Runs `GuardAsync` (`ProjectViewGuard.GuardAdministerAsync`; when the right is
gone `ShowNoAccess` clears the project and the typed values and the page shows the not-found message), asks for the
current user, and calls `ProjectService.AddMemberAsync` with the typed user name and the chosen role. The user name is
never looked up by the page and no list of users is loaded. On success the browser goes back to the project. A
`DomainRuleException` (the one generic message of the service for an unknown, disabled, or already added user, the
throttle message, or the refusal of a right lost during the call) is shown in `new-member-error` only when the guard,
asked again, still passes, and the typed values are kept. The "Create a new account" button leads to the create account
page.

**CreateAccountAsync** (`NewMemberAccountPage`): The same guard and actor steps, then
`ProjectService.CreateMemberAccountAsync` with the user name, the display name, and the role. On success the form is
replaced by `new-account-password-box`, which shows the user name and the temporary password once, with the reminder
that the user must choose a new password at the first sign-in. A refusal is shown in `new-account-error` under the same
rule as for `AddMemberAsync`. **CopyPasswordAsync** asks the guard again and then copies the password with the script
`reviewCenter.copyText`, showing "Copied." or a manual-copy hint in `account-password-copy-status`. **Done** clears the
page state (including the password) and returns to the project. **ShowNoAccess** clears the password as well.

**OnParametersSetAsync** (`EvidenceSettingsPage`): Resets the settings and messages (the router reuses the component),
asks `GuardAdministerAsync` of the shared guard whether the user administers the project, and only then loads the
project (`ProjectService.GetAsync`) and the stored settings (`EvidenceSettingsService.GetAsync`) into `Saved` and
`Draft`; anyone else sees the same "does not exist" message and no form.

**ChangeAsync** (`EvidenceSettingsPage`): Runs `GuardAsync` first (`ProjectViewGuard.GuardAdministerAsync`). When the
right is gone `ShowNoAccess` clears the project, the settings and the messages and the page shows the not-found
message, so no switch, warning or banner stays; otherwise it writes the switch into `Draft` and does nothing else.

**SaveAsync** (`EvidenceSettingsPage`): Clears the messages, runs `GuardAsync` (`ProjectViewGuard.GuardAdministerAsync`;
when the right is gone `ShowNoAccess` clears the project, the settings and the messages and the page shows the
not-found message), asks for the current user, and calls `EvidenceSettingsService.SetAsync` with the user, the project,
`Saved`, and `Draft`. The service stores the draft only when the stored settings still equal `Saved`, writes the audit
entry `project.evidence-settings-changed` (the project, the changed settings with their old and new values, and the
user) in the same step, and returns whether anything changed. The page then shows "Evidence settings saved." or
"Nothing to save: the settings are unchanged." and stays open, with `Saved` set to the draft. A `DomainRuleException`
is shown in `evidence-settings-error` only when the guard, asked again, still passes. When the rule is
`DomainRule.RecordChanged`, someone else saved first: nothing was stored, the message says so
(`EvidenceSettingsService.ChangedByOthersMessage`), and the page loads the stored settings again and shows them instead
of the stale draft.

**Switches** (`EvidenceSettingsPage`): A switch is one entry of a list that holds its key, its title, its description,
its regulatory warning (the content switches only), and how it reads and writes `Draft`, so the seven rows, the warnings,
and the test identifiers come from one table. A change first re-checks the access (`ChangeAsync`) and then replaces
`Draft` and changes nothing else; saving is the only action that reaches the service.

**OnParametersSetAsync** (`NewReviewPage`): Loads the project (for the actor, so a project the user may not see shows
the not-found message and no form) and the enabled repositories of the project. When the project
identity differs from the one already loaded (the router reuses the component), it first discards the previous
project's form values, selected files, and error, so files chosen for one project can never be submitted into
another.

**OnFilesSelectedAsync**: Reads the selected files into memory.

- *Postconditions*: `SelectedFiles` holds the new selection, or is empty and `ErrorMessage` is set when the
  selection breaks a limit. A new selection replaces the previous one.

Rejects more than 200 files before touching any of them; rejects a file larger than 2 MiB or a total larger than
20 MiB from the sizes the browser reports, before reading anything; then reads each file with a read limit of the
file size limit while counting the bytes actually received, because the reported sizes are client-supplied, and
stops with the same total-size rejection as soon as the received bytes pass 20 MiB, so memory use stays bounded
even for a client that understates its sizes. The path recorded for a file is its browser file name, since a
browser does not expose folders,
so two files with the same name are later refused by `ReviewService` as a duplicate path. A selection that breaks
a limit is rejected as a whole, so a half-read selection can never be submitted. Submitting the form after such a
rejection does nothing and leaves the rejection reason visible, rather than replacing it with a "no files" message.

**CreateReviewAsync**: Handles submission of the form.

- *Postconditions*: On success the browser is taken to the page of the new review. On a rule violation the entered
  values and files are kept and the message is shown. The handler first asks for the current user, so an ended
  sign-in state is refused with a message and no review is created.

Before it creates anything the handler refuses the identifier `new`, compared without regard to letter case and ignoring
surrounding blanks, with the message "The review id 'new' is reserved. Choose another id." and creates nothing.

For the repository source the handler calls `ReviewService.CreateFromRepositoryAsync` with the chosen repository,
the head and baseline revisions, and the selection. A missing choice of repository is reported as "Choose a
repository." A refusal (an unknown revision, a selection that matches no change, which reads "Nothing to review: no
files matching the selection changed between X and Y", a repository disabled in the meantime, a limit) is shown in
`review-error` with every value kept, and no review exists. When the creation skipped symbolic links or submodules,
the page does not navigate at once: it shows `review-created` with the list of skipped entries and a link to open the
review, because these entries are not stored with the review and this is the only time to tell the creator.

**ChooseSource** (`NewReviewPage`): Switches between the two sources and clears the message; the repository source
cannot be chosen when no repository is enabled.

**OnAfterRender** (all pages): Marks the page interactive after the first render on a live connection.

#### Error Handling

A `DomainRuleException` from `ReviewService`, or from the check of the current user, is caught by `NewReviewPage`
and its message shown in the page; a `DomainRuleException` from `ProjectService` is caught by the member pages and by
the member changes of `ProjectPage` and shown next to the form or the list, but only while the access re-check still
passes (otherwise the page drops its data and shows the not-found message). `EvidenceSettingsPage` does the same for a
`DomainRuleException` of `EvidenceSettingsService`, including the refusal of a save over a change by someone else, which
also reloads the stored settings. A selection that breaks an upload limit is
shown as an error message. Other exceptions are not handled by the pages.

**Access re-check (`NewReviewPage`)**: The creation of the review, and the choice of the source, start with
`GuardAsync`, which asks `ProjectViewGuard.GuardViewAsync` whether the signed-in user may still see the project. When
access is lost, `ShowNoAccess` clears the project, the repositories, the form, and the result, and shows the same message
as for a project that does not exist; no review is created. An ended sign-in state counts as no access, so this message
replaces the "no longer signed in" message on this page. `ProjectPage` already re-checks as described in the design of
the subsystem.

#### Dependencies

- **SessionGate** (App) — wraps the whole content of each page, so the page is replaced by a "Session ended" message
  when the sign-in state of the open page ends.
- **ProjectService** — reads the project for the actor, lists the members, adds a member by exact user name, creates a
  member account with a temporary password, removes a member, and changes a role.
- **ReviewService** — lists reviews (`ProjectPage`) and creates them (`NewReviewPage`).
- **RepositoryService** — lists the enabled repositories of the project (names only, never a location or token), for
  the plan link and for the drop-down.
- **IProjectAccess** (Core) — tells whether the user administers the project (`ProjectPage`, the two member pages, and
  `EvidenceSettingsPage`, before they load the project), and, through `ProjectViewGuard`, whether the user may still
  see the project (all pages) and still administers it (the members section, the member pages, and the evidence
  settings page).
- **EvidenceSettingsService** and **EvidenceSettings** (Core) — read and change the evidence settings of the project
  (`EvidenceSettingsPage`); the service holds the rules, the audit entry, and the refusal of a stale save.
- **SigningService** — computes whether each review is approved (`ProjectPage`).
- **AnonymousRateLimit** (Api) — the request limit named in the exposure warning.
- **ReviewLimits** (Core) — the upload limits.
- **JavaScript runtime** — the `reviewCenter.copyText` script that copies the temporary password
  (`NewMemberAccountPage`).
- **ASP.NET Core Blazor components** — the component base class, file input, navigation, the authorization
  attribute, and the interactive server render mode.

#### Callers

- **App** — the router renders `ProjectPage` for `/projects/{id}`, `NewReviewPage` for
  `/projects/{id}/reviews/new`, `NewMemberPage` for `/projects/{id}/members/new`, `NewMemberAccountPage` for
  `/projects/{id}/members/account`, and `EvidenceSettingsPage` for `/projects/{id}/evidence-settings`; an anonymous
  visitor is sent to the login page first.
