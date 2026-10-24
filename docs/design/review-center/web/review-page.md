### ReviewPage

![Web Structure](WebView.svg)

#### Purpose

`ReviewPage` is the page at `/projects/{id}/reviews/{reviewId}`. Its responsibility is to show one review: its
identifier, title, state, and content fingerprint; its files with the size and hash of each; and the content of a
selected file. It is a Razor component split into presentation (`ReviewPage.razor`) and behavior
(`ReviewPage.razor.cs`), and it is read-only.

#### Data Model

The page instance is created per browser connection, so its state is per user.

**ProjectId**, **ReviewId**: the route parameters.

**Loaded**, **Review**, **ProjectName**: whether loading finished, the review with its file metadata (`null` when
it does not exist), and the name of the owning project shown in the breadcrumb (the project identity is shown
when the project cannot be found).

**SelectedPath**: the path of the file whose content is shown; `null` when none is selected.

**SelectedText**: the text of the selected file; `null` when no file is selected or the file is shown as binary.

**InteractiveMarker**: as for the other pages.

Test identifiers: `review-page` (root), `home-link`, `projects-link`, `project-link`, `review-loading`,
`review-not-found`, `review-title`, `review-id`, `review-state`, `review-fingerprint`, `files-list`,
`file-row-{index}`, `file-path-{index}`, `file-size-{index}`, `file-hash-{index}`, `file-select-{index}`,
`selected-file-path`, `file-content`, and `file-binary-notice`.

#### Key Methods

**OnParametersSetAsync**: Clears the displayed review, project name and file selection and sets `Loaded` to
false before the first await, then loads the review. The router reuses the component when only the route changes,
so following a link to another review shows the loading state and never the previous review or its file content.
Each file's `View` button has an `aria-label` of `View {path}` so that screen-reader users can tell the buttons
apart.

**SelectFileAsync**: Shows one file.

- *Parameters*: `int index` — the position in the displayed list; an index outside the list is ignored.
- *Postconditions*: `SelectedPath` is the file's path and only that file's content has been loaded.

Only the selected file is loaded, so that opening a review with many or large files does not read them all. The
bytes are shown as raw text in a preformatted element if they contain no zero byte and are valid UTF-8 (an empty
file is shown as empty text); otherwise a notice that the file is binary is shown. The text is not altered, so
line endings and white space are as uploaded; Blazor renders the text as encoded DOM text, so file content cannot inject
markup.

**OnAfterRender**: Marks the page interactive after the first render on a live connection.

#### Error Handling

A review that does not exist is shown as a message on the page. A file that has disappeared from the store since
the page loaded is shown as empty text rather than failing the connection. The page raises no other errors of its
own.

#### Dependencies

- **ReviewService** — reads the review and the bytes of a file.
- **ProjectService** — reads the name of the owning project for the breadcrumb.
- **ASP.NET Core Blazor components** — the component base class, events, and the interactive server render mode.

#### Callers

- **App** — the router renders the page for the review route.
- **ProjectPage** — navigates to the page after a review is created.
