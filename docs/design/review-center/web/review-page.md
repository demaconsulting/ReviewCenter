### ReviewPage

![Web Structure](WebView.svg)

#### Purpose

`ReviewPage` is the page at `/projects/{id}/reviews/{reviewId}`. Its responsibility is to show one review: its
identifier, title, state, and content fingerprint; its files with the size, hash, and number of open threads of
each; the numbered lines of a selected file; and the comment threads on that file, which a reader can start, answer,
resolve, and reopen. It is a Razor component split into presentation (`ReviewPage.razor`) and behavior
(`ReviewPage.razor.cs`). It reads reviews through `ReviewService` and works with threads only through
`ThreadService`, so every comment rule is applied there.

#### Data Model

The page instance is created per browser connection, so its state is per user.

**ProjectId**, **ReviewId**: the route parameters.

**Loaded**, **Review**, **ProjectName**: whether loading finished, the review with its file metadata (`null` when
it does not exist), and the name of the owning project shown in the breadcrumb (the project identity is shown
when the project cannot be found).

**SelectedPath**: the path of the file whose content is shown; `null` when none is selected.

**SelectedLines**: the lines of the selected file as defined by `FileText`; `null` when no file is selected or the
file is shown as binary.

**Threads**: all threads of the review, for every file, in creation order. The summary and the per-file counts are
calculated from this list, and the selected file's threads are the ones with its path.

**AuthorName**: the author typed once in the "Your name" field and used for new threads and replies. There is no
sign-in yet, so it is free text that is not verified.

**NewStartLine**, **NewEndLine**, **NewText**, **NewThreadError**: the new-thread form fields and its message.
**ReplyTexts**, **ReplyErrors**: the reply text and the message of each thread, by thread identity.

**InteractiveMarker**: as for the other pages.

Test identifiers: `review-page` (root), `home-link`, `projects-link`, `project-link`, `review-loading`,
`review-not-found`, `review-title`, `review-id`, `review-state`, `review-fingerprint`, `thread-summary`,
`threads-open-count`, `threads-resolved-count`, `files-list`, `file-row-{index}`, `file-path-{index}`,
`file-size-{index}`, `file-hash-{index}`, `file-open-threads-{index}`, `file-select-{index}`,
`selected-file-path`, `file-binary-notice`, `file-empty-notice`, `new-thread-form`, `comment-author-input`,
`thread-start-line-input`, `thread-end-line-input`, `thread-target`, `thread-text-input`, `thread-error`,
`start-thread-button`, `file-content`, `code-line-{n}`, `line-comment-{n}`, `line-no-{n}`, `line-marker-{n}`,
`line-text-{n}` (all by 1-based line number), `threads-empty`, `threads-list`, `thread-{id}`,
`thread-anchor-{id}`, `thread-state-{id}`, `thread-toggle-{id}`, `comment-{id}-{n}`, `comment-author-{id}-{n}`,
`comment-time-{id}-{n}`, `comment-text-{id}-{n}` (by thread identity and the position of the comment in the
thread), `reply-text-{id}`, `reply-button-{id}`, and `reply-error-{id}`.

#### Key Methods

**OnParametersSetAsync**: Clears the displayed review, project name, file selection, threads, and everything
typed, and sets `Loaded` to false before the first await, then loads the review and its threads. The router reuses
the component when only the route changes, so following a link to another review shows the loading state and never
the previous review, its file content, or a half-typed comment. Each file's `View` button has an `aria-label` of
`View {path}` so that screen-reader users can tell the buttons apart.

**SelectFileAsync**: Shows one file.

- *Parameters*: `int index` — the position in the displayed list; an index outside the list is ignored.
- *Postconditions*: `SelectedPath` is the file's path, only that file's content has been loaded, and the
  new-thread form is empty (the author name is kept).

Only the selected file is loaded, so that opening a review with many or large files does not read them all. If the
bytes contain no zero byte and are valid UTF-8, they are shown as numbered lines (`FileText.TryDecode` and
`SplitLines`; an empty file has no lines and shows a notice saying so); otherwise a notice that the file is binary
is shown. Each line has a
Comment button, a line number, and its text as plain text: line endings are not shown, and all other characters
and white space are as uploaded. Blazor renders the text as encoded DOM text, so file content cannot inject
markup. Lines covered by a thread are marked, and a number at the start line of a thread shows how many threads
begin there. Every line is a row of the page, so a file with very many lines makes the page large; the upload
limits bound this (measured: about 3 seconds for 5,000 lines, about 10 seconds for a 2 MiB file of 32,000 short
lines).

**CommentOnLine**: Fills the start and end line of the new-thread form with one line number.

**StartThreadAsync**: Starts a thread on the selected file from the form. Both line fields blank anchor the whole
file, only a start line anchors that line, and both anchor the range. A line field that is not a whole number is
reported as "Line numbers must be whole numbers." A violated comment rule is shown in `thread-error` next to the
form and everything typed stays. Success clears the text and line fields, keeps the author, and reloads the
threads.

**ReplyAsync**: Adds the typed reply to a thread. A violated rule is shown inside that thread; for a missing author
the message also says which field to fill in (the 'Your name' field), because that field is far from the thread.

**ToggleThreadAsync**: Resolves an open thread or reopens a resolved one, then reloads the threads. A thread that no
longer exists is reported inside the thread.

**OnAfterRender**: Marks the page interactive after the first render on a live connection.

Comments are read when the page loads and after each change made on the page. A comment posted in another browser
appears when this page is next loaded or changed; nothing is pushed.

#### Error Handling

A review that does not exist is shown as a message on the page. A file that has disappeared from the store since
the page loaded is shown as empty text rather than failing the connection. Comment rule violations are shown as
messages next to the control that caused them and never end the connection. The page raises no other errors of its
own.

#### Dependencies

- **ReviewService** — reads the review and the bytes of a file.
- **ThreadService** — starts, answers, resolves, reopens, and lists threads.
- **FileText** — decides whether a file is text and splits it into lines.
- **ProjectService** — reads the name of the owning project for the breadcrumb.
- **ASP.NET Core Blazor components** — the component base class, events, and the interactive server render mode.

#### Callers

- **App** — the router renders the page for the review route.
- **ProjectPage** — navigates to the page after a review is created.
