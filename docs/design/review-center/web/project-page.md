### ProjectPage

![Web Structure](WebView.svg)

#### Purpose

`ProjectPage` is the page at `/projects/{id}`. Its responsibilities are to show one project and its reviews, each with
its approval status, and to
create a review, either from files selected in the browser (enforcing the upload limits while the files are read) or
from the changes in a registered git repository. It requires
a signed-in user. It is a
Razor component split into presentation (`ProjectPage.razor`) and behavior (`ProjectPage.razor.cs`). Above the list of
reviews it shows a link to the plan page (`/projects/{id}/plan`) when at least one repository is enabled, and a short
note that says to ask an administrator when none is.

#### Data Model

The page instance is created per browser connection, so its state is per user.

**ProjectId**: `int` — the route parameter.

**Loaded**, **Project**, **Reviews**: whether loading finished, the project (`null` when it does not exist), and
its reviews.

**ApprovedReviews**: the identifiers of the reviews that are approved (at least one reviewer slot, and every slot has
a current signature), filled while loading.

**ReviewId**, **ReviewTitle**: the values typed into the form.

**SelectedFiles**: the files read from the file input, as `UploadedFile` values held in memory.

**ErrorMessage**: the message of the last rejected selection or submission.

**Repositories**, **FromRepository**: the enabled repositories (`RepositorySummary` values from
`RepositoryService.ListEnabledAsync`, which carry no location and no token) and whether the form creates the review
from a repository. **RepositoryIdText**, **HeadRevision**, **BaselineRevision**, **Selection**: the repository form
values; the selection starts as `**/*`. **Busy**: set while a creation runs, so a second submit is ignored.
**CreatedReview**, **SkippedEntries**: the review just created from a repository and the entries that were skipped,
shown before the review is opened.

**InteractiveMarker**: as for the other pages.

Test identifiers: `project-page` (root), `home-link`, `projects-link`, `project-loading`, `project-not-found`,
`project-name`, `project-description`, `reviews-list`, `reviews-empty`, `review-row-{id}`, `review-link-{id}`,
`review-title-{id}`, `review-state-{id}`, `review-approval-{id}`, `new-review-form`, `review-id-input`,
`review-title-input`, `review-source` (the source choice), `source-upload-radio`, `source-repository-radio`,
`no-repository-hint`, `repository-select`, `head-revision-input`, `baseline-revision-input`, `selection-input`,
`review-files-input`, `selected-files-list`, `selected-file-{index}`, `selected-file-path-{index}`,
`selected-file-size-{index}`, `review-error`, `create-review-button`, `review-created`, `skipped-entries-list`,
`skipped-entry-{index}`, `open-created-review-link`, `plan-reviews-link` (the link to the plan page, shown when at
least one repository is enabled), and `plan-reviews-unavailable` (the note shown when none is).

The form offers one choice, "Upload files" (the default) or "From a repository", and shows only the fields of the
chosen source, so the page stays short. The repository choice is disabled, with a hint, when no repository is
enabled. The repository fields are the repository (a drop-down of enabled repositories by name), the head revision,
the optional baseline revision (blank: every file at the head counts as added), and the selection (a text area with
one pattern per line, `!` to exclude).

#### Key Methods

**OnParametersSetAsync**: Loads the project and its reviews, then asks `SigningService.GetApprovalAsync` once for each
review to learn whether it is approved (N+1 lookups, accepted because the list of reviews of a project is short); a
project that does not exist leaves `Project` null and
the page shows a message. Each review row shows, next to its state, a badge that reads "Approved" when the review is
in `ApprovedReviews` and "In review" otherwise; a review without slots is therefore In review. When the project
identity differs from the one already loaded (the router reuses the
component), it first discards the previous project's data, form values, selected files and error, so files chosen
for one project can never be submitted into another.

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

For the repository source the handler calls `ReviewService.CreateFromRepositoryAsync` with the chosen repository,
the head and baseline revisions, and the selection. A missing choice of repository is reported as "Choose a
repository." A refusal (an unknown revision, a selection that matches no change, which reads "Nothing to review: no
files matching the selection changed between X and Y", a repository disabled in the meantime, a limit) is shown in
`review-error` with every value kept, and no review exists. When the creation skipped symbolic links or submodules,
the page does not navigate at once: it shows `review-created` with the list of skipped entries and a link to open the
review, because these entries are not stored with the review and this is the only time to tell the creator.

**ChooseSource**: Switches between the two sources and clears the message; the repository source cannot be chosen
when no repository is enabled.

**OnAfterRender**: Marks the page interactive after the first render on a live connection.

#### Error Handling

A `DomainRuleException` from `ReviewService`, or from the check of the current user, is caught and its message shown
in the page. A selection that breaks
an upload limit is shown as an error message. Other exceptions are not handled by the page.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content, so the page is replaced by a "Session ended" message when the
  sign-in state of the open page ends.
- **ProjectService** — reads the project.
- **ReviewService** — lists and creates reviews.
- **RepositoryService** — lists the enabled repositories for the drop-down (names only, never a location or token).
- **SigningService** — computes whether each review is approved.
- **ReviewLimits** (Core) — the upload limits.
- **ASP.NET Core Blazor components** — the component base class, file input, navigation, the authorization
  attribute, and the interactive
  server render mode.

#### Callers

- **App** — the router renders the page for `/projects/{id}`; an anonymous visitor is sent to the login page first.
