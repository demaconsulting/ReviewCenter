### ProjectPage

![Web Structure](WebView.svg)

#### Purpose

The `ProjectPage` unit consists of six pages. `ProjectPage` is the page at `/projects/{id}`: its responsibilities are
to show one project and its reviews as clickable rows, each with its approval status, to lead to the page
that creates a review, to lead to the plan page and, for whoever administers the project, to the repositories page, the
audit trail of the project, the evidence settings page, and the checklist template page, to let an administrator of the
project (a project admin or a
system administrator)
manage the members of the project (change a role, remove, and lead to the page that adds one), and to report when it is
interactive. `EvidenceSettingsPage` is the page at `/projects/{id}/evidence-settings`: it shows and changes the
evidence settings of the project (see below). `ProjectChecklistPage` is the page at `/projects/{id}/checklist`: it
shows the checklist template of the project and lets an administrator export, import, check, replace, and reset it (see
below). `NewMemberPage` is the page at `/projects/{id}/members/new`: it adds an
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
project from a missing one; the two member pages, the evidence settings page, and the checklist template page show the
same message to a user who sees the project but does not administer it. The response is a normal page with HTTP status
200. Each is a Razor
component split into
presentation (`ProjectPage.razor`, `NewReviewPage.razor`, `NewMemberPage.razor`, `NewMemberAccountPage.razor`,
`EvidenceSettingsPage.razor`, `ProjectChecklistPage.razor`) and
behavior (the matching `.razor.cs` files). Next to the heading,
`ProjectPage` shows a "+ New review" button that leads to the form page, an outline "Plan reviews" link to the
plan page (`/projects/{id}/plan`) when at least one repository of the project is enabled, and, for a system
administrator and a project admin of the project, a "Repositories" button to `/projects/{id}/repositories`, an
"Audit trail" button to `/projects/{id}/audit`, an "Evidence settings" button to
`/projects/{id}/evidence-settings`, and a "Checklist" button to `/projects/{id}/checklist`; when the project has
no enabled repository, a short note says to add one with the Repositories button (to whoever manages the project) or
to ask a project administrator (to anybody else). The rare actions sit on their own pages behind buttons, so the
members section holds only a "+ Add member" button, and the page never loads a list of users. For an administrator
`ProjectPage` also shows a "Review rules" card above the members: the switch "Require independent reviewers" with a
hint that the person who created a review cannot be one of its reviewers and cannot sign it, that the switch can be
turned off for a project where one person has to do both, and that ReviewCenter only knows who created the review in
this tool and does not match the authors of commits, and a "Save review rules" button. Two more switches follow, "Authors
cannot answer the checklist of their own review" and "The person who reopened a review cannot sign it again", both on
by default; each shows a hint that says what the rule does, that it is on by default and applies to every project,
including the ones created before it was enforced, and that every change is audited. One "Save review rules" button
saves all three. The card also holds one line,
"Checklist: ...", that names the version label of the checklist new reviews start with, its number of sections and
checks, and whether it is the built-in default or custom, with a link to the checklist template page; the line is read
with `ProjectChecklistService.GetAsync` for an administrator only, and a stored definition that cannot be read shows a
short note instead of the counts.

`ProjectChecklistPage` (`ProjectChecklistPage.razor` and `.razor.cs`) is the page of the checklist template of a
project, which only a project admin or a system administrator can see and change. It shows the current checklist (the
version label, the number of sections and checks, and a "built-in default" badge when the project has no checklist of
its own) and offers five things:

- **Export**: "Export current checklist" and "Export built-in default" download the definition as an indented JSON file
  named `checklist-{version label}.json` (characters other than letters, digits, dot, underscore, and hyphen of the label
  become a hyphen). The download is started with the script `reviewCenter.downloadText` of `App.razor`, which makes a
  `Blob` and clicks a temporary link; the file never passes through an endpoint, and the text is made by the page from
  the checklist it has just read, after the access has been checked again.
- **Import**: the definition is pasted into a text box (up to `PasteLimit`, 20,000 characters, because the text
  travels in one message of the live connection) or read from a file chosen with a file input, which is limited to
  `ReviewLimits.MaxChecklistBytes` (256 KiB) and refused before it is read when larger.
- **Validate**: checks the imported text with `ProjectChecklistService.Validate` and lists every error with its path.
  Text that is not JSON, or nothing imported, gives one error. Nothing is stored. The button is always enabled so that
  the change of the text box (which the browser sends only on blur) has arrived before the check runs.
- **Preview and differences**: for a valid definition the page shows the sections and checks read-only with their
  counts, and the differences from the current checklist computed by `ChecklistDiff.Compare` (a pure helper in the
  unit): whether the version label changed, and the sections and checks added, removed, or changed, by identifier. A
  definition with the same content as the current one is reported as "nothing to replace"; a definition with other
  content but the same version label gets a warning, because the service would refuse it.
- **Replace** and **Reset to built-in default**: each asks for a confirmation that says that existing reviews keep the
  checklist they were created with and only new reviews use the new one, and only then calls
  `ProjectChecklistService.SetAsync` (with the hash of the checklist the page loaded, and the imported text) or
  `ResetAsync` (with the same hash). Replace is enabled only for a valid definition that differs from the current one,
  and Reset only while the project has a checklist of its own. The service stores the change and the audit entry
  `project.checklist-changed` in one step.

The page follows the same human-factors rules as the evidence settings page: the heading and breadcrumb say where the
user is, a link leads to the help topic, a refusal or a confirmation is shown in words, and the stale-save refusal
(`ProjectChecklistService.ChangedByOthersMessage`) is shown with the checklist that is stored now.

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

**Progress**, **Matching**, **PageRows** (`ProjectPage`): the `ReviewProgress` of every review of the project (one call
of `ReviewProgressService`), the reviews that match the filters in the chosen order, and the 25 of the page shown.
**StageQuery**, **OverdueQuery**, **BlockedQuery**, **SortQuery**, **DirectionQuery**, **SearchQuery**, and
**PageQuery** are the query-string parameters (`SupplyParameterFromQuery`) that hold the whole state of the view.
**CanContribute** (`ProjectPage`): whether the user may create and plan reviews; false for a read-only auditor.

**Repositories**: the enabled repositories of this project (`RepositorySummary` values from
`RepositoryService.ListEnabledAsync(actor, projectId)`, which carry no location and no token, and none of another
project). `ProjectPage` uses them to decide whether to show the plan link;
`NewReviewPage` offers them for the repository source.

**IsSystemAdmin** and **Candidates**, the inline add form, and its state no longer exist: `ProjectPage` neither lists
users nor holds a form for adding one. **CanAdminister** (`ProjectPage`): whether the user administers the project (a
system administrator or a project admin), from `IProjectAccess`. **Members** (`ProjectPage`): the `ProjectMember` list
from `ProjectService.ListMembersAsync`, loaded only when `CanAdminister`.
**MemberError**, **MemberStatus**: the refusal (for example the last-project-admin message) or the outcome of the last
membership change. **RequireIndependentChoice** (`ProjectPage`): the state of the switch as shown, which starts at the
stored `Project.RequireIndependentReviewers`. **RequireNonAuthorChoice**, **RequireNonReopenerChoice**
(`ProjectPage`): the
same for the two further switches, starting at `Project.RequireNonAuthorChecklistAnswers` and
`Project.RequireNonReopenerSigning`. **RulesError**, **RulesStatus**: the refusal or the outcome of the last
save of the review rules.

The remaining state belongs to `NewReviewPage`, `NewMemberPage`, `NewMemberAccountPage`, `EvidenceSettingsPage`, and
`ProjectChecklistPage`.

**ChecklistSummary** (`ProjectPage`): the one line about the checklist for the Review rules card; null unless the user
administers the project.

**Project**, **Loaded** (`ProjectChecklistPage`): the project, which is null unless the user administers it, and
whether the lookup finished. **Current**: the `ProjectChecklist` as read when the page loaded or last changed; its hash
is what a
replace or a reset is based on. **UnreadableMessage**: set instead of **Current** when the stored definition cannot be
read; the page then shows the message, still offers the built-in default for export, and can replace or reset the
definition from the stored text's hash. **PastedText**, **ImportText**, **ImportSource**: what is in the paste box, the
text that was imported (pasted or read from a file) and where it came from. **Validation**, **Diff**: the result of the
last validation and the differences from the current checklist; both are dropped when the import changes.
**ConfirmingReplace**, **ConfirmingReset**: which confirmation is waiting. **ErrorMessage**, **StatusMessage**: the
message of the last refused change, and the confirmation of the last change.

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

**ReviewId**, **ReviewTitle**: the values typed into the form. **DueDateText**: the optional due date typed into the
form (`yyyy-MM-dd`, empty for none), a calendar day in UTC that the form states next to the field.

**SelectedFiles**: the files read from the file input, as `UploadedFile` values held in memory.

**Candidates**, **FollowSourceId**, **FollowPreview**, **FollowRefusal**, **AcknowledgeDropped** (`NewReviewPage`): the
reviews of the project that can be offered as the earlier review (read with `ReviewService.ListAsync` when the project
loads), the identifier of the chosen earlier review (empty for none), what the follow-up would carry (a
`FollowUpPreview`, only for uploaded files), the message of a refused preview, and whether the creator has ticked that
the dropped open threads were checked. The acknowledgement is cleared whenever the choice, the files or the source change.

**ErrorMessage**: the message of the last rejected selection or submission.

**FromRepository**: whether the form creates the review from a repository. **RepositoryIdText**, **HeadRevision**,
**BaselineRevision**, **Selection**: the repository form values; the selection starts as `**/*`. **Busy**: set while
a creation runs, so a second submit is ignored. **CreatedReview**, **SkippedEntries**: the review just created from a
repository and the entries that were skipped, shown before the review is opened.

**InteractiveMarker**: as for the other pages; each page has its own.

Test identifiers of `ProjectPage`: `project-page` (root), `breadcrumb-projects-link`, `project-loading`, `project-not-found`,
`project-name`, `project-description`, `new-review-button` (the link that leads to the form page), `reviews-list`,
`reviews-empty`, `review-row-{id}`, `review-link-{id}`, `review-title-{id}`,
`review-approval-{id}`, `review-created-{id}`, `plan-reviews-link` (the link to the plan page, shown when at least
one repository of the project is enabled), `plan-reviews-unavailable` (the note shown when none is),
`repositories-link` (the Repositories button, shown to whoever administers the project), `project-audit-link` (the
"Audit trail" button, shown to the same), and, for an administrator of the project only, the Members section:
`member-error`, `member-status`, `members-empty`, `members-list`, and per member (by user
name) `member-row-{username}`, `member-name-{username}`, `member-role-{username}`, `member-toggle-role-{username}`,
and `member-remove-{username}`, then `add-member-button` (the "+ Add member" link button to the add member page). Each
review is one row
of `reviews-list`: the identifier is the link, followed by the title, the approval badge, and the creation
date. `evidence-settings-link` is the "Evidence settings" button, shown to whoever administers the project. The Review
rules card of an administrator has `review-rules-card`, `review-rule-independent-reviewers` (the row),
`independent-reviewers-switch` (the checkbox), `author-checklist-switch` and `reopener-signing-switch` (the two further
rules, each with its row `review-rule-author-checklist` or `review-rule-reopener-signing`),
`independent-reviewers-save`, `review-rules-status`, and `review-rules-error`. `checklist-link` is the "Checklist"
button, shown to whoever administers the project; the
Review rules card also has `review-rule-checklist` (the row), `project-checklist-summary` (the one line), and
`review-rules-checklist-link`.

Test identifiers of `ProjectChecklistPage`: `project-checklist-page` (root), `breadcrumb-projects-link`,
`breadcrumb-project-link`, `project-checklist-loading`, `project-not-found`, `project-checklist-error`,
`project-checklist-status`, `checklist-help-link`, `project-checklist-back`; the current checklist: `checklist-current`,
`checklist-current-version`, `checklist-current-counts`, `checklist-default-badge`, `checklist-unreadable`,
`checklist-export`, `checklist-export-default`, `checklist-reset`, `checklist-reset-confirmation`,
`checklist-reset-confirm`, and `checklist-reset-cancel`; the import: `checklist-import`, `checklist-import-text`,
`checklist-import-file`, `checklist-import-source`, and `checklist-validate`; the result of a check:
`checklist-validation-errors` with one `checklist-error` per error (with `checklist-error-path` and
`checklist-error-message`), or `checklist-valid`, `checklist-preview-version`, `checklist-diff` (with
`checklist-diff-same`, `checklist-diff-version`, `checklist-diff-sections-added`, `checklist-diff-sections-removed`,
`checklist-diff-sections-changed`, `checklist-diff-items-added`, `checklist-diff-items-removed`,
`checklist-diff-items-changed`, and `checklist-diff-label-warning`), `checklist-preview`, per section
`checklist-preview-section-{id}`, per check `checklist-preview-item-{id}`, and `checklist-replace`,
`checklist-replace-confirmation`, `checklist-replace-confirm`, and `checklist-replace-cancel`.

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
`project-not-found`, `new-review-form`, `review-id-input`, `review-title-input`, `due-date-input`, `review-source`
(the source choice), `source-upload-radio`, `source-repository-radio`, `no-repository-hint`, `repository-select`, `head-revision-input`,
`baseline-revision-input`, `selection-input`, `review-files-input`, `selected-files-list`, `selected-file-{index}`,
`selected-file-path-{index}`, `selected-file-size-{index}`, `review-error`, `create-review-button`,
`cancel-new-review-link` (an outline link back to the project), `review-created`, `skipped-entries-list`,
`skipped-entry-{index}`, and `open-created-review-link`. The follow-up part of the form has `follow-up` (the field),
`follow-source-select`, `follow-source-none` (the empty state), `follow-preview`, `follow-preview-hint` (for a
repository source), `follow-preview-error`, `follow-dropped-files`, and `follow-acknowledge-checkbox`. A review row of
the project list of a follow-up has `review-follows-{id}` and `review-follows-link-{id}`.

The form offers one choice as two selectable option cards, "Upload files" (the default) or "From a repository", and
shows only the fields of the chosen source, so the page stays short. The repository choice is disabled, with a hint,
when no repository is enabled. The repository fields are the repository (a drop-down of enabled repositories by
name), the head revision, the optional baseline revision (blank: every file at the head counts as added), and the
selection (a text area with one pattern per line, `!` to exclude).

#### Key Methods

**OnParametersSetAsync** (`ProjectPage`): Loads the project for the actor with `ProjectService.GetAsync(actor, id)`.
When the project does not exist or the user may not see it, `Project` is null, nothing else is loaded, and the page
shows the same message for both. Otherwise it reads the progress of all reviews with one call of
`ReviewProgressService.GetProgressAsync` (a constant number of reads however many reviews the project has; this replaces
the former ask of the approval status of each review), applies the filters, the sort order, and the paging of the query
string (`ApplyView`), and loads whether the user may contribute and whether the user administers the project. The
enabled repositories are read only for a user who may contribute, because a read-only member (an auditor) neither plans
nor creates reviews and the repository registry refuses such a read; for an administrator the members are loaded too.
The stored state of the review (`Draft` is its only value) is not shown on the row.

**Overview** (`ProjectPage`): Above the reviews a summary strip counts the reviews of the project in each stage and the
overdue ones (`project-summary-setup`, `project-summary-in-review`, `project-summary-approved`,
`project-summary-rejected` ("N rejected"), `project-summary-overdue`), counted over all reviews and not changed by the
filters. A filter form (`review-filter`)
holds the stage (`filter-stage`), "overdue only" (`filter-overdue`), "blocked only" (`filter-blocked`), a text search of
the identifier and title (`filter-search`), and Apply (`filter-apply`) and Clear (`filter-clear`) controls; it is a form
with the GET method, so that the view is always the address. The state of the view lives in the query string only:
`stage` (`setup`, `in-review`, `approved`, or `rejected`), `overdue=1`, `blocked=1`, `sort` (`id`, `stage`,
`activity`, `due`, or
`signatures`), `dir` (`asc` or `desc`), `q`, and `page`. A value that is not understood is ignored (a `page` that is
not a whole number is page 1), and a page out of range is brought into range. The text of `q` is shown encoded, never
as markup.
Values that equal the default are left out of the addresses the page builds. The filter form carries the sort state of
the view as hidden `sort` and `dir` inputs whenever the view is not the default (id ascending), also for a descending
order of the identifier (`sort=id`, `dir=desc`), so that applying a filter does not lose the order; the links of the
headers and of the paging keep it in `dir`. A project without reviews shows `reviews-empty`: an invitation to use
"+ New review" for a user who may contribute, and only "This project has no reviews yet." for a member who may only read,
who has no such button. The column headers (`sort-id`,
`sort-stage`, `sort-signatures`, `sort-activity`, `sort-due`) are links to the same view with that sort, and flip the
direction when the column is the current one; the last activity sorts newest first by default, every other column
ascending, a review without a due date comes last in either direction, and ties are broken by the review identifier.
The table shows 25 reviews to a page (`reviews-paging` with `reviews-range`, `reviews-prev`, and `reviews-next`, which
keep the filters), and `reviews-no-match` says so when the filters match nothing. Each row (`review-row-{id}`) shows the
review (link `review-link-{id}` and title), its stage badge (`review-approval-{id}`: Setup, In review, Approved, or
Rejected, and
a "Reopened" mark for a reopened review), the signatures (`review-progress-{id}`: signed of required, and "n out of
date" when any), what blocks it (`review-blockers-{id}`: "No checklist", open threads, unanswered checks, and
"conclusion needed" when a rejected outcome has no conclusion text, and the number of disputed checks, such as
"1 disputed"; a Fail answer is not a blocker and is not listed),
the people it
waits on (`review-waiting-{id}`), the last activity, and the due date with the Overdue badge
(`review-overdue-{id}`) of the rule of `ReviewService`. The created, last activity, and due dates, and their `datetime`
attributes, are written with `CultureInfo.InvariantCulture` (Gregorian digits such as `2026-03-04`), like the review page
and the API, so the culture of the request, for example the Thai Buddhist calendar, never changes the year. Everything
is read-only, so the same table is shown to a member, a member who may only read (the Auditor role), an
administrator, and a system administrator; the "+ New review" button, the "Plan
reviews" link, and the repository hint are shown only to a user who may contribute, and a read-only member sees a
`readonly-notice` instead.

**RemoveMemberAsync**, **ToggleRoleAsync**, **SetRoleAsync** (`ProjectPage`): Remove a member
(`ProjectService.RemoveMemberAsync`), switch a member between `ProjectRole.User` and `ProjectRole.ProjectAdmin`, and set
a role explicitly (`SetMemberRoleAsync`). The role label of a member (`member-role-{username}`) reads "Member",
"Auditor", or "Project admin". An `Auditor` row offers only explicit buttons (`member-set-role-{username}-user` and
`-projectadmin`), never a toggle, so an auditor is never promoted by accident; other rows keep the toggle
(`member-toggle-role-{username}`) and offer `member-set-role-{username}-auditor`. The new member pages have an auditor
checkbox (`new-member-auditor-checkbox`, `new-account-auditor-checkbox`) that cannot be combined with the project admin
box. Each goes through one
shared step (`RunMemberChangeAsync`) that first runs `GuardMembersAsync`, then asks for the current user, runs the
change, shows the outcome in `member-status` (only after the members were reloaded) or the message of a
`DomainRuleException` in `member-error` (only when the guard, asked again, still allows it), and reloads the members.
The page holds no membership rule; the refusal to remove or demote the last project administrator comes from
`ProjectService` and is shown as the error of the section.

**SaveReviewRulesAsync** (`ProjectPage`): Clears the two messages, runs `GuardMembersAsync`, asks for the current user,
and calls `ProjectService.SetReviewRuleAsync` with the user, the project, the rule, the value the page loaded as
the expected value, and the switch as the new value, for each of the three rules in turn (independent reviewers, authors
cannot answer the checklist, the person who reopened cannot sign) but only for a rule whose switch differs from the
loaded value. Core refuses it unless the user administers the project and refuses
an API token, performs the compare-and-set, and appends the audit entry (`project.independent-reviewers-changed` for the
independence rule and `project.review-rule-changed` for the two others) in the
same step. The page then reloads the project and shows one sentence per changed rule, joined with a space:
"Independent reviewers are now required." or "Independent
reviewers are no longer required."; "Authors cannot answer the checklist: now on." or "now off";
"The person who reopened a review cannot sign it again: now on." or "now off"; or, when no
switch was changed, "The review rules were
already set this way." Each rule is its own change, so when a later rule is refused the earlier ones were already
saved; the page then loads the stored values again. A `DomainRuleException` (for example the refusal of a save over a
change by someone else) is shown
in `review-rules-error` only when the guard, asked again, still passes, and the page loads the stored value again and
shows it in the switch.

**GuardMembersAsync** (`ProjectPage`): Asks `ProjectViewGuard.GuardViewAsync` whether the user still sees the project;
when not, `ShowNoAccess` drops the project, the progress, the repositories, and the member state, and
the page shows the not-found message. Then it asks `ProjectViewGuard.GuardAdministerAsync` whether the user still
administers the project; when not, `ShowMembersLost` clears `CanAdminister`, the members, and the member messages (and
with them the Members section and the administration buttons), and the reviews stay, because the user may still be a
member. Because the demotion may have changed the other rights too (an administrator who made themselves an Auditor
no longer contributes), `RefreshRightsAsync` recomputes `CanContribute` and `CanAudit` from `ProjectAccess` (and
clears the repositories without the contribute right), while a plain-member demotion keeps contribution. The action
is then not run. The members are data only an administrator may read, so an error shown above them
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

**OnParametersSetAsync** (`ProjectChecklistPage`): Resets all state (the router reuses the component), asks
`GuardAdministerAsync` of the shared guard whether the user administers the project, and only then loads the project
and the checklist (`ProjectChecklistService.GetAsync`); anyone else sees the same "does not exist" message and nothing
of the checklist.

**PasteChangedAsync**, **FileSelectedAsync** (`ProjectChecklistPage`): Each runs `GuardAsync` first and then takes the
text as the imported text. The file is read with the read limit `ReviewLimits.MaxChecklistBytes`; a larger file is an
`IOException` of the framework and is shown as a message, the text of a file is never put into the paste box, and a byte
order mark is removed. A new import drops the earlier validation, differences, and confirmation.

**ValidateAsync** (`ProjectChecklistPage`): Runs `GuardAsync`, then `ProjectChecklistService.Validate` on the imported
text (or one `json` error when nothing was imported) and, for a valid definition, `ChecklistDiff.Compare` against the
current checklist. It stores nothing.

**ReplaceAsync**, **ResetAsync** (`ProjectChecklistPage`): Each is reached through a confirmation (`AskReplaceAsync`,
`AskResetAsync`; the replace is possible only for a valid definition that differs from the current one) and starts with
`GuardAsync`; when the right is gone `ShowNoAccess` clears everything and the page shows the not-found message. Then
the handler asks for the current user and calls `ProjectChecklistService.SetAsync` or `ResetAsync` with the hash of
`Current`, reloads the checklist, and confirms the change in words (new reviews use the new checklist; existing reviews
keep theirs). A `DomainRuleException` is handled by `HandleRefusalAsync` only while the guard, asked again, still
passes: for `DomainRule.RecordChanged` the page says that someone else changed the checklist
(`ProjectChecklistService.ChangedByOthersMessage`), loads the stored checklist again, and checks the imported text
against it again; any other rule (for example `ChecklistDefinitionInvalid` for a changed wording under the same version
label) is shown in `project-checklist-error` with nothing stored.

**ExportCurrentAsync**, **ExportDefaultAsync** (`ProjectChecklistPage`): Run `GuardAsync` first, then make the indented
JSON of the definition with `ChecklistDiff.ToIndentedJson` and call the script `reviewCenter.downloadText` with the file
name, the text, and `application/json`; a user who lost the right gets nothing.

**ChecklistDiff** (`ChecklistDiff.cs`): A pure, immutable helper that compares two `ChecklistDefinition` values by
identifier: whether the version label differs, the identifiers of the sections added and removed, the sections whose
title or applicability hint changed, and the checks added, removed, or changed (another wording or another section).
Letter case and order of identifiers do not matter and a missing hint equals an empty one; `VersionChanged` and
`HasContentChanges` tell what differs. `ToIndentedJson` produces the export text through the normal JSON of the
definition, so an exported file passes
the validator when imported again.

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

The optional due date is read with `ReviewService.ParseDueDate` and passed to both creation calls (as the `dueDate`
argument); text that is not a date, or a date that `ReviewService` refuses (before today in UTC, more than two years
ahead), is shown in `review-error` like any other rule, with every value kept and no review created.

For the repository source the handler calls `ReviewService.CreateFromRepositoryAsync` with the chosen repository,
the head and baseline revisions, and the selection. A missing choice of repository is reported as "Choose a
repository." A refusal (an unknown revision, a selection that matches no change, which reads "Nothing to review: no
files matching the selection changed between X and Y", a repository disabled in the meantime, a limit) is shown in
`review-error` with every value kept, and no review exists. When the creation skipped symbolic links or submodules,
the page does not navigate at once: it shows `review-created` with the list of skipped entries and a link to open the
review, because these entries are not stored with the review and this is the only time to tell the creator.

**OnFollowSourceChangedAsync** (`NewReviewPage`): Handles the choice of the earlier review. It repeats the access check
of the page, suggests the title "Follow-up to {id}" as long as the creator has not typed another title, and then
refreshes the preview. The preview is asked from `ReviewService.PreviewFollowUpAsync` only for uploaded files that have
been selected; the same refresh runs when the files change. A refusal of the service (`FollowUpInvalid`) is shown in
`follow-preview-error`, and the service stays the authority for what fits: the selector only leaves out the reviews that
cannot fit by kind (for uploaded files the uploaded reviews, for a repository the reviews of the same repository), and
the files and the sets are judged by the service. For a repository source the files are known only after the git read,
so the form shows a hint instead of counts. When the preview names open threads on files the new review does not have,
the form lists the files and shows the checkbox `follow-acknowledge-checkbox`; the creation passes it as
`acknowledgeDroppedThreads`, and without it the service refuses the creation with its own message, shown in
`review-error`. The whole follow-up field is left out when the project has no review at all; when reviews exist but
none suits the chosen source, the field shows the message `follow-source-none`.

For both sources `CreateReviewAsync` passes the chosen identifier as `followsReviewId` and the acknowledgement to
`CreateDraftAsync` or `CreateFromRepositoryAsync`.

**ChooseSource** (`NewReviewPage`): Switches between the two sources and clears the message; the repository source
cannot be chosen when no repository is enabled. A chosen earlier review that does not suit the new source is dropped.

**OnAfterRender** (all pages): Marks the page interactive after the first render on a live connection.

#### Error Handling

A `DomainRuleException` from `ReviewService`, or from the check of the current user, is caught by `NewReviewPage`
and its message shown in the page; a `DomainRuleException` from `ProjectService` is caught by the member pages and by
the member changes of `ProjectPage` and shown next to the form or the list, but only while the access re-check still
passes (otherwise the page drops its data and shows the not-found message). `EvidenceSettingsPage` does the same for a
`DomainRuleException` of `EvidenceSettingsService`, including the refusal of a save over a change by someone else, which
also reloads the stored settings. `ProjectChecklistPage` does the same for a `DomainRuleException` of
`ProjectChecklistService` (the refusal of a changed definition under the same version label, and of a change over a
change by someone else, which also reloads the stored checklist); a file above the size limit is shown as a message.
A selection that breaks an upload limit is
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
  member account with a temporary password, removes a member, changes a role, and changes the review rules
  (`SetReviewRuleAsync`; `RequireIndependentReviewers` and the two further rules).
- **ReviewService** — lists the earlier reviews that can be followed, previews a follow-up, and creates reviews
  (`NewReviewPage`).
- **ReviewProgressService** (Core) — the progress of every review of the project in one call (`ProjectPage`): stage,
  signatures, blockers, whom a review waits on, last activity, and overdue state.
- **RepositoryService** — lists the enabled repositories of the project (names only, never a location or token), for
  the plan link and for the drop-down.
- **IProjectAccess** (Core) — tells whether the user administers the project (`ProjectPage`, the two member pages, and
  `EvidenceSettingsPage`, before they load the project), and, through `ProjectViewGuard`, whether the user may still
  see the project (all pages) and still administers it (the members section, the member pages, and the evidence
  settings page).
- **EvidenceSettingsService** and **EvidenceSettings** (Core) — read and change the evidence settings of the project
  (`EvidenceSettingsPage`); the service holds the rules, the audit entry, and the refusal of a stale save.
- **ProjectChecklistService**, **ChecklistValidator**, and **ChecklistDefinition** (Core) — read, validate, replace, and
  reset the checklist template of the project (`ProjectChecklistPage`, and the summary line of `ProjectPage`); the
  service holds the rules, the audit entry, and the refusal of a stale change.
- **SigningService** — not used by `ProjectPage` any more; the approval status of each review comes from
  `ReviewProgressService`.
- **AnonymousRateLimit** (Api) — the request limit named in the exposure warning.
- **ReviewLimits** (Core) — the upload limits.
- **JavaScript runtime** — the `reviewCenter.copyText` script that copies the temporary password
  (`NewMemberAccountPage`), and the `reviewCenter.downloadText` script that saves an exported checklist as a file
  (`ProjectChecklistPage`).
- **ASP.NET Core Blazor components** — the component base class, file input, navigation, the authorization
  attribute, and the interactive server render mode.

#### Callers

- **App** — the router renders `ProjectPage` for `/projects/{id}`, `NewReviewPage` for
  `/projects/{id}/reviews/new`, `NewMemberPage` for `/projects/{id}/members/new`, `NewMemberAccountPage` for
  `/projects/{id}/members/account`, `EvidenceSettingsPage` for `/projects/{id}/evidence-settings`, and
  `ProjectChecklistPage` for `/projects/{id}/checklist`; an anonymous
  visitor is sent to the login page first.
