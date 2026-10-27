### ProjectPage

![Web Structure](WebView.svg)

#### Purpose

The `ProjectPage` unit consists of two pages. `ProjectPage` is the page at `/projects/{id}`: its responsibilities are
to show one project and its reviews as clickable rows, each with its state and approval status, to lead to the page
that creates a review, to lead to the plan page, and to report when it is interactive. `NewReviewPage` is the page
at `/projects/{id}/reviews/new`: it holds only the form that creates a review, either from files selected in the
browser (enforcing the upload limits while the files are read) or from the changes in a registered git repository.
Both require a signed-in user. Each is a Razor component split into presentation (`ProjectPage.razor`,
`NewReviewPage.razor`) and behavior (`ProjectPage.razor.cs`, `NewReviewPage.razor.cs`). Next to the heading,
`ProjectPage` shows a "+ New review" button that leads to the form page, and an outline "Plan reviews" link to the
plan page (`/projects/{id}/plan`) when at least one repository is enabled; when none is, a short note says to ask an
administrator.

Because `/projects/{id}/reviews/new` is the address of the form, the review identifier `new` (in any letter case) is
reserved: `NewReviewPage` refuses it, so a review that could never be opened is never created.

#### Data Model

The page instances are created per browser connection, so their state is per user.

**ProjectId**: `int` — the route parameter of both pages.

**Loaded**, **Project**: whether loading finished and the project (`null` when it does not exist), on both pages.

**Reviews** (`ProjectPage`): the reviews of the project.

**ApprovedReviews** (`ProjectPage`): the identifiers of the reviews that are approved (at least one reviewer slot,
and every slot has a current signature), filled while loading.

**Repositories**: the enabled repositories (`RepositorySummary` values from `RepositoryService.ListEnabledAsync`,
which carry no location and no token). `ProjectPage` uses them to decide whether to show the plan link;
`NewReviewPage` offers them for the repository source.

The remaining state belongs to `NewReviewPage`.

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
one repository is enabled), and `plan-reviews-unavailable` (the note shown when none is). Each review is one row of
`reviews-list`: the identifier is the link, followed by the title, the state and approval badges, and the creation
date.

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

**OnParametersSetAsync** (`ProjectPage`): Loads the project and its reviews, then asks
`SigningService.GetApprovalAsync` once for each review to learn whether it is approved (N+1 lookups, accepted because
the list of reviews of a project is short); a project that does not exist leaves `Project` null and the page shows a
message. Each review row shows, next to its state, a badge that reads "Approved" when the review is in
`ApprovedReviews` and "In review" otherwise; a review without slots is therefore In review.

**OnParametersSetAsync** (`NewReviewPage`): Loads the project and the enabled repositories. When the project
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

**OnAfterRender** (both pages): Marks the page interactive after the first render on a live connection.

#### Error Handling

A `DomainRuleException` from `ReviewService`, or from the check of the current user, is caught by `NewReviewPage`
and its message shown in the page. A selection that breaks an upload limit is shown as an error message. Other
exceptions are not handled by the pages.

#### Dependencies

- **SessionGate** (App) — wraps the whole content of each page, so the page is replaced by a "Session ended" message
  when the sign-in state of the open page ends.
- **ProjectService** — reads the project.
- **ReviewService** — lists reviews (`ProjectPage`) and creates them (`NewReviewPage`).
- **RepositoryService** — lists the enabled repositories (names only, never a location or token), for the plan link
  and for the drop-down.
- **SigningService** — computes whether each review is approved (`ProjectPage`).
- **ReviewLimits** (Core) — the upload limits.
- **ASP.NET Core Blazor components** — the component base class, file input, navigation, the authorization
  attribute, and the interactive server render mode.

#### Callers

- **App** — the router renders `ProjectPage` for `/projects/{id}` and `NewReviewPage` for
  `/projects/{id}/reviews/new`; an anonymous visitor is sent to the login page first.
