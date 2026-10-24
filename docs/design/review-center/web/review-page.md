### ReviewPage

![Web Structure](WebView.svg)

#### Purpose

`ReviewPage` is the page at `/projects/{id}/reviews/{reviewId}`. Its responsibility is to show one review, with the
reader's work first. From the top it shows the title, a one-sentence next step, the files with the size and number
of open threads of each, the thread index of all files, the numbered lines of a selected file, the comment
threads on that file, which a reader can answer, resolve, and reopen (a resolved thread is a one-line header
until expanded), and the form that starts a new thread. Last, in a native `<details>` element that is collapsed by
default, it shows the review's
identifier, state, content fingerprint, and the hash of each file. It is a Razor component split into presentation
(`ReviewPage.razor`) and behavior (`ReviewPage.razor.cs`). It reads reviews through `ReviewService` and works with
threads only through `ThreadService`, so every comment rule is applied there.

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

**IndexFilter**: the state the thread index shows (`Open` by default, `Resolved`, or `null` for all).
**ExpandedThreads**: the identities of the resolved threads the reader has expanded; an open thread is always shown
expanded. **FocusedThreadId**: the thread chosen in the index, whose lines and card are highlighted. The page also
keeps the test id to scroll to after the next render. All of these are cleared when the route parameters change.

Test identifiers: `review-page` (root), `home-link`, `projects-link`, `project-link`, `review-loading`,
`review-not-found`, `review-title`, `next-step`, `review-details`, `review-details-summary`, `review-id`,
`review-state`, `review-fingerprint`, `file-hash-path-{index}`, `file-hash-{index}`, `thread-index`,
`thread-summary` (the filter group), `index-filter-all`, `index-filter-open`, `index-filter-resolved`,
`threads-all-count`, `threads-open-count`, `threads-resolved-count`, `thread-index-list`, `thread-index-empty`,
`index-row-{id}`, `index-file-{id}`, `index-anchor-{id}`, `index-state-{id}`, `index-count-{id}`,
`index-author-{id}`, `index-activity-{id}`, `files-list`, `file-row-{index}`, `file-path-{index}`,
`file-size-{index}`, `file-open-threads-{index}`, `file-select-{index}`,
`selected-file-path`, `file-binary-notice`, `file-empty-notice`, `new-thread-form`, `comment-author-input`,
`thread-start-line-input`, `thread-end-line-input`, `thread-target`, `thread-text-input`, `thread-error`,
`start-thread-button`, `file-content`, `code-line-{n}`, `line-comment-{n}`, `line-no-{n}`, `line-marker-{n}`,
`line-text-{n}` (all by 1-based line number), `threads-empty`, `threads-list`, `thread-{id}`,
`thread-anchor-{id}`, `thread-state-{id}`, `thread-count-{id}`, `thread-expand-{id}` (resolved threads only),
`thread-toggle-{id}`, `comment-{id}-{n}`, `comment-author-{id}-{n}`,
`comment-time-{id}-{n}`, `comment-text-{id}-{n}` (by thread identity and the position of the comment in the
thread), `reply-text-{id}`, `reply-button-{id}`, and `reply-error-{id}`. The comments, `thread-toggle-{id}` and the
reply controls of a resolved thread exist only while it is expanded.

#### Key Methods

**OnParametersSetAsync**: Clears the displayed review, project name, file selection, threads, and everything
typed, and sets `Loaded` to false before the first await, then loads the review and its threads. The router reuses
the component when only the route changes, so following a link to another review shows the loading state and never
the previous review, its file content, or a half-typed comment. Each file's `View` button has an `aria-label` of
`View {path}` so that screen-reader users can tell the buttons apart.

**SelectFileAsync**: Shows one file.

- *Parameters*: `int index` — the position in the displayed list; an index outside the list is ignored.
- *Postconditions*: `SelectedPath` is the file's path, only that file's content has been loaded, the
  new-thread form is empty (the author name is kept), and the top of the file's code is the scroll target.

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

**CommentOnLine**: Fills the start and end line of the new-thread form with one line number, and makes the form the
scroll target, because the form sits below the code and the threads.

**StartThreadAsync**: Starts a thread on the selected file from the form. Both line fields blank anchor the whole
file, only a start line anchors that line, and both anchor the range. A line field that is not a whole number is
reported as "Line numbers must be whole numbers." A violated comment rule is shown in `thread-error` next to the
form and everything typed stays. Success clears the text and line fields, keeps the author, and reloads the
threads.

**ReplyAsync**: Adds the typed reply to a thread. A violated rule is shown inside that thread; for a missing author
the message also says which field to fill in (the 'Your name' field), because that field is far from the thread.

**ToggleThreadAsync**: Resolves an open thread or reopens a resolved one, then reloads the threads. Resolving also
collapses the thread. A thread that no longer exists is reported inside the thread.

**SetFilter**: Sets the state the thread index shows. **ToggleThreadExpanded**: Expands or collapses a resolved
thread.

**SelectThreadAsync**: Shows a thread chosen in the index. The thread's file is selected through `SelectFileAsync`
only when it is not already shown, so text typed in the new-thread form is kept; the thread becomes the focused
thread, a resolved thread is expanded, and the first line of its anchor (or the thread card for a whole-file
thread) becomes the scroll target. The index rows come from `ThreadService.BuildIndex` and the next-step line from
`ThreadService.NextStep`, so the page holds no ordering or wording rules of its own.

**OnAfterRenderAsync**: Marks the page interactive after the first render on a live connection, and, when a scroll
target is set, calls the browser function `reviewCenter.scrollToTestId` (defined in `App`) once with the test id and
where it should land (`start` or `center`) and clears the target. Highlighting is done by the page itself (a
`focused` class), so only the scroll needs the browser; a
connection that is already gone is ignored.

Comments are read when the page loads and after each change made on the page. A comment posted in another browser
appears when this page is next loaded or changed; nothing is pushed.

#### Error Handling

A review that does not exist is shown as a message on the page. A file that has disappeared from the store since
the page loaded is shown as empty text rather than failing the connection. Comment rule violations are shown as
messages next to the control that caused them and never end the connection. The page raises no other errors of its
own.

#### Dependencies

- **ReviewService** — reads the review and the bytes of a file.
- **ThreadService** — starts, answers, resolves, reopens, and lists threads, and shapes them for the index and the
  next-step line.
- **FileText** — decides whether a file is text and splits it into lines.
- **ProjectService** — reads the name of the owning project for the breadcrumb.
- **ASP.NET Core Blazor components** — the component base class, events, JavaScript interop, and the interactive
  server render mode.
- **App** — defines the scroll function that the page calls.

#### Callers

- **App** — the router renders the page for the review route.
- **ProjectPage** — navigates to the page after a review is created.
