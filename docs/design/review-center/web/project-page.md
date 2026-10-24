### ProjectPage

![Web Structure](WebView.svg)

#### Purpose

`ProjectPage` is the page at `/projects/{id}`. Its responsibilities are to show one project and its reviews, and to
create a review from files selected in the browser, enforcing the upload limits while the files are read. It is a
Razor component split into presentation (`ProjectPage.razor`) and behavior (`ProjectPage.razor.cs`).

#### Data Model

The page instance is created per browser connection, so its state is per user.

**ProjectId**: `int` — the route parameter.

**Loaded**, **Project**, **Reviews**: whether loading finished, the project (`null` when it does not exist), and
its reviews.

**ReviewId**, **ReviewTitle**: the values typed into the form.

**SelectedFiles**: the files read from the file input, as `UploadedFile` values held in memory.

**ErrorMessage**: the message of the last rejected selection or submission.

**InteractiveMarker**: as for the other pages.

Test identifiers: `project-page` (root), `home-link`, `projects-link`, `project-loading`, `project-not-found`,
`project-name`, `project-description`, `reviews-list`, `reviews-empty`, `review-row-{id}`, `review-link-{id}`,
`review-title-{id}`, `review-state-{id}`, `new-review-form`, `review-id-input`, `review-title-input`,
`review-files-input`, `selected-files-list`, `selected-file-{index}`, `selected-file-path-{index}`,
`selected-file-size-{index}`, `review-error`, and `create-review-button`.

#### Key Methods

**OnParametersSetAsync**: Loads the project and its reviews; a project that does not exist leaves `Project` null and
the page shows a message. When the project identity differs from the one already loaded (the router reuses the
component), it first discards the previous project's data, form values, selected files and error, so files chosen
for one project can never be submitted into another.

**OnFilesSelectedAsync**: Reads the selected files into memory.

- *Postconditions*: `SelectedFiles` holds the new selection, or is empty and `ErrorMessage` is set when the
  selection breaks a limit. A new selection replaces the previous one.

Rejects more than 200 files before touching any of them; rejects a file larger than 2 MiB or a total larger than
20 MiB from the sizes the browser reports, before reading anything; then reads each file with a read limit of the
file size limit. The path recorded for a file is its browser file name, since a browser does not expose folders,
so two files with the same name are later refused by `ReviewService` as a duplicate path. A selection that breaks
a limit is rejected as a whole, so a half-read selection can never be submitted. Submitting the form after such a
rejection does nothing and leaves the rejection reason visible, rather than replacing it with a "no files" message.

**CreateReviewAsync**: Handles submission of the form.

- *Postconditions*: On success the browser is taken to the page of the new review. On a rule violation the entered
  values and files are kept and the message is shown.

**OnAfterRender**: Marks the page interactive after the first render on a live connection.

#### Error Handling

A `DomainRuleException` from `ReviewService` is caught and its message shown in the page. A selection that breaks
an upload limit is shown as an error message. Other exceptions are not handled by the page.

#### Dependencies

- **ProjectService** — reads the project.
- **ReviewService** — lists and creates reviews.
- **ReviewLimits** (Core) — the upload limits.
- **ASP.NET Core Blazor components** — the component base class, file input, navigation, and the interactive
  server render mode.

#### Callers

- **App** — the router renders the page for `/projects/{id}`.
