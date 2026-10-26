### ReviewPage

![Web Structure](WebView.svg)

#### Purpose

`ReviewPage` is the page at `/projects/{id}/reviews/{reviewId}`, which also answers the file, line and thread addresses
described under Deep links below. Its responsibility is to show one review, with the
reader's work first. From the top it shows the title with an approval badge (Approved or In review), a one-sentence
next step, a checklist card (progress summary
such as "37 of 58 checks answered" and a link to the checklist page; absent when the review has no checklist), a
Reviewers card (the reviewer slots with their signature status, the form that adds a slot, and, for a reviewer, the
form that signs), the
files with the size and number
of open threads of each, the thread index of all files, the selected file as a diff against its baseline (or as numbered
lines), the comment
threads on that file, which a reader can answer, resolve, and reopen (a resolved thread is a one-line header
until expanded), and the form that starts a new thread. Last, in a native `<details>` element that is collapsed by
default, it shows the review's
identifier, state, content fingerprint, record digest, and the hash of each file; for a review from a repository it
also shows the repository name, the baseline and head commits, and the selection; for a review that was planned from
a review-set it also shows the identifier of that set (`review-set-id`, the "Review set" row). It is a Razor component
split into presentation
(`ReviewPage.razor`) and behavior (`ReviewPage.razor.cs`). It reads reviews through `ReviewService` and works with
threads only through `ThreadService`, so every comment rule is applied there. It works with reviewer slots,
signatures, and the approval status only through `SigningService`, so the page contains no signing rules. It requires
a signed-in user, and every comment, slot change, and signature is made on behalf of that user. The title row also
has a link to the history page of the review (`history-link`), where the audit entries of the review are listed.

#### Data Model

The page instance is created per browser connection, so its state is per user.

**ProjectId**, **ReviewId**: the route parameters. **FilePath**, **ThreadId**, **LineText**, **SideText**: the optional
route and query values of a deep link. **HighlightLine**, **HighlightSide**, **DeepLinkMessage**: the line the address
named and the message for an address that names nothing; all three are cleared whenever the reader chooses a file.

**Loaded**, **Review**, **ProjectName**: whether loading finished, the review with its file metadata (`null` when
it does not exist), and the name of the owning project shown in the breadcrumb (the project identity is shown
when the project cannot be found).

**SelectedPath**: the path of the file whose content is shown; `null` when none is selected.

**SelectedLines**: the lines of the selected file as defined by `FileText`; `null` when no file is selected or the
file is shown as binary.

**SelectedKind**, **CurrentView**, **Diff**, **BaselineLines**: the change kind of the selected file, the view shown
(`Diff`, `Head`, or `Baseline`), the `LineDiffResult` of the file from `ReviewService.GetDiffAsync` (`null` for a
file of an uploaded review or of a review without a baseline commit), and the lines of the baseline text (`null`
for binary content or when not yet
loaded). The baseline is loaded the first time it is shown. **CanCompare** is true when the review is from a
repository and froze a baseline commit (every file of it has a diff, an added file as all added lines; a file of an
uploaded review or of a review without a baseline commit has none), **HasHeadVersion** when the file has a head
version (every kind but deleted), **HasBaselineVersion** when it has a baseline version (`CanCompare` and not an
added file), and **ShowingDiff** when the Diff view is shown
for a diff that has rows. **NewSide**: the version (`Head` or `Baseline`) the new-thread form anchors to.

**Threads**: all threads of the review, for every file, in creation order. The summary and the per-file counts are
calculated from this list, and the selected file's threads are the ones with its path.

**Checklist**: the `ChecklistState` of the review, or `null` when it has none. The page only reads it for the summary
and the checklist card; the answers are made on `ChecklistPage`.

**Approval**: the `ReviewApproval` of the review from `SigningService.GetApprovalAsync`: whether it is approved, the
record digest, the slots with their signature state, the blockers that prevent signing, and the next-step sentence.
It is `null` until loaded. **Candidates**: the enabled users that can be chosen for a slot (`SlotMember` values from
`SigningService.ListCandidatesAsync`, so no password hash ever reaches the page). **CurrentUsername**: the user name
of the signed-in user, from the authentication state, used to tell which slots the user occupies.

**NewSlotPrimary**, **NewSlotAlternate**, **NewSlotError**: the add-slot form choices (user identities as text, blank
for nobody) and its message. **SlotPrimaryChoices**, **SlotAlternateChoices**, **SlotErrors**: the choices made in
the edit fields and the message of each slot, by slot identity; until the reader changes a choice the stored
primary and alternate are shown. **SignPasswords**, **SignErrors**: the password typed into the sign form and the
message of each slot, by slot identity. **PasswordKey**: a counter that makes the browser build a fresh, empty
password field after every signing attempt.

**NewStartLine**, **NewEndLine**, **NewText**, **NewThreadError**: the new-thread form fields and its message.
**ReplyTexts**, **ReplyErrors**: the reply text and the message of each thread, by thread identity.

**InteractiveMarker**: as for the other pages.

**IndexFilter**: the state the thread index shows (`Open` by default, `Resolved`, or `null` for all).
**ExpandedThreads**: the identities of the resolved threads the reader has expanded; an open thread is always shown
expanded. **FocusedThreadId**: the thread chosen in the index, whose lines and card are highlighted. The page also
keeps the test id to scroll to after the next render. All of these are cleared when the route parameters change.

Test identifiers: `review-page` (root), `home-link`, `projects-link`, `project-link`, `review-loading`,
`review-not-found`, `review-title`, `review-approval`, `next-step`, `checklist-card`, `checklist-progress-summary`,
`checklist-link`, `reviewers-card`, `slots-empty`, `slot-{id}`, `slot-label-{id}`, `slot-primary-{id}`,
`slot-alternate-{id}`, `slot-status-{id}`, `slot-primary-select-{id}`, `slot-alternate-select-{id}`,
`slot-save-{id}`, `slot-remove-{id}`, `slot-error-{id}`, `sign-blockers-{id}`, `sign-form-{id}`, `sign-meaning-{id}`,
`sign-password-{id}`, `sign-error-{id}`, `sign-button-{id}` (all by slot identity), `slot-add-form`,
`new-slot-primary-select`, `new-slot-alternate-select`, `new-slot-add-button`, `new-slot-error`,
`review-details`, `review-details-summary`, `review-id`,
`review-state`, `review-fingerprint`, `record-digest`, `file-hash-path-{index}`, `file-hash-{index}`, `thread-index`,
`thread-summary` (the filter group), `index-filter-all`, `index-filter-open`, `index-filter-resolved`,
`threads-all-count`, `threads-open-count`, `threads-resolved-count`, `thread-index-list`, `thread-index-empty`,
`index-row-{id}`, `index-file-{id}`, `index-anchor-{id}`, `index-state-{id}`, `index-count-{id}`,
`index-author-{id}`, `index-activity-{id}`, `files-list`, `file-row-{index}`, `file-path-{index}`,
`file-size-{index}`, `file-open-threads-{index}`, `file-select-{index}`,
`selected-file-path`, `file-binary-notice`, `file-empty-notice`, `new-thread-form`,
`thread-start-line-input`, `thread-end-line-input`, `thread-target`, `thread-text-input`, `thread-error`,
`start-thread-button`, `thread-cancel-button` and `thread-whole-file-button` (only in the form under a diff row),
`file-content`, `code-line-{n}`, `line-comment-{n}`, `line-no-{n}`, `line-marker-{n}`,
`line-text-{n}` (all by 1-based line number), `file-kind-{index}` (the change kind of a file in the list, shown for
reviews from a repository only), `selected-file-kind`, `file-version-toggle` (shown when the file can be compared),
`view-diff-button`, `view-head-button`, `view-baseline-button`, `baseline-note`, `baseline-content`,
`baseline-line-{n}`, `baseline-line-no-{n}`, `baseline-line-text-{n}`, `baseline-comment-note`,
`deleted-comment-note`, `diff-too-large`, `diff-too-large-head`, `diff-too-large-baseline`, and, from
`DiffView`, `diff-view`, `diff-summary`, `diff-hint`, `diff-content`, `diff-gap-{start}` (by the index of the first
collapsed row), `diff-pair-{i}` (a side-by-side row, by the index of its first line), `diff-row-{i}` (a cell, with
`data-kind` of `context`, `added`, or `removed`; the cell of a removed line, of an added line and the right cell of an
unchanged line), `diff-row-old-{i}` (the left cell of an unchanged line), `diff-comment-{i}`, `diff-comment-old-{i}`,
`diff-old-no-{i}`, `diff-new-no-{i}`, `diff-text-{i}`, `diff-text-old-{i}`, `diff-threads-{i}`,
`diff-empty-left-{i}`, `diff-empty-right-{i}` (the empty placeholders), and `diff-composer` (all by row index),
`review-repository`, `review-set-id` (only for a review planned from a review-set), `review-baseline-sha`,
`review-head-sha`, `review-selection` (in the details of a review from
a repository), `threads-empty`, `threads-list`, `thread-{id}`,
`thread-anchor-{id}`, `thread-state-{id}`, `thread-count-{id}`, `thread-expand-{id}` (resolved threads only),
`thread-toggle-{id}`, `comment-{id}-{n}`, `comment-author-{id}-{n}`,
`comment-time-{id}-{n}`, `comment-text-{id}-{n}` (by thread identity and the position of the comment in the
thread), `reply-text-{id}`, `reply-button-{id}`, and `reply-error-{id}`. The comments, `thread-toggle-{id}` and the
reply controls of a resolved thread exist only while it is expanded.

#### Key Methods

**OnParametersSetAsync**: Clears the displayed review, project name, file selection, threads, approval, and everything
typed (including the slot choices, messages, and typed passwords), and sets `Loaded` to false before the first await,
then loads the review and calls `RefreshAsync`. The router reuses
the component when only the route changes, so following a link to another review shows the loading state and never
the previous review, its file content, or a half-typed comment. Each file's `View` button has an `aria-label` of
`View {path}` so that screen-reader users can tell the buttons apart.

**RefreshAsync**: Reloads everything the record depends on: the threads, the checklist, the approval, the
candidates for a slot, and the user name of the signed-in user. The user name is read straight from the authentication
state, not through `GetActorAsync`, so that a reload that meets an ended sign-in state does not fail; the page is replaced
by the session-ended message instead. It runs when the page loads and after every change
made on the page (starting, replying, resolving, reopening, and every slot or signing action), and when a file is
selected, so the badge, the next-step sentence, the slot status, and the digest always describe the record as it
now is.

**SelectFileAsync**: Shows one file.

- *Parameters*: `int index` — the position in the displayed list; an index outside the list is ignored.
- *Postconditions*: `SelectedPath` is the file's path, only that file's content has been loaded, the
  new-thread form is empty, and the top of the file's code is the scroll target.

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

**ShowDiff**, **ShowHead**, and **ShowBaselineAsync**: Switch between the three views of a file that can be
compared (`CanCompare`). The Diff view is described below; the Head view is the numbered view above, which takes
comments; the Baseline view shows the frozen baseline as numbered lines without comment buttons, markers, or the
new-thread form, and with a note that comments are made in the Diff or Head view. Switching keeps the typed text
and clears the line selection. A modified file offers all three, an added file offers Diff and Head (no baseline
exists, and its diff has an empty left side), a deleted file offers Diff and Baseline (it has no
head), and a file of an uploaded review or of a review without a baseline commit has no choice and shows only the
head. Selecting
another file starts in its default view: Diff when the file can be compared, otherwise Head (a deleted file whose
diff is unavailable shows its baseline and accepts whole-file comments only, with the line fields left blank, a note
says so, and Core refuses a line range). The file
list shows the change kind (Added, Modified, or Deleted) of each file of a review from a repository, and the
size of a deleted file is its baseline size. Both the head and the baseline bytes are read from the frozen copies in
the store, never from the repository.

**Diff view**: `SelectFileAsync` asks `ReviewService.GetDiffAsync` for the comparison. The `DiffView` component
draws a `LineDiffResult` side by side: the baseline on the left and the head on the right. Each shown segment of
rows is arranged by `LineDiff.Pair`, so an unchanged line shows on both sides, a run of removed lines is paired row
by row with the run of added lines that follows it, and the leftover cells of the shorter run are empty
placeholders (muted, hidden from assistive technology). A cell has a comment control, the line number of its side, a
`-` or `+` sign, and the text as plain text, wrapped inside the cell. Removed and added cells are marked by color and
by their sign. Above the rows it shows "x added, y removed" (or a sentence that only the line endings differ), and,
when there are rows, the hint "Click + to comment on a line." The rows are grouped by `LineDiff.Segment`, and a run
of unchanged lines that is collapsed shows a button "Show N unchanged lines" that expands it. The rows of threads and
of the selected line are pinned, so they are never hidden. The first change is therefore near the top of the view,
because the unchanged lines before it (apart from three lines of context) are collapsed; when a file opens with such
a collapsed run above its first change, `SelectFileAsync` also sets that row as the scroll target (block `center`)
instead of the file path, using the page's existing `reviewCenter.scrollToTestId` helper of `App.razor`, which
deep links reuse. The style sheet gives the review page a wider column (80 rem) so each side has room; below 40 rem
it stacks the left cell above the right cell of a row and hides the empty placeholders and the left copy of an
unchanged line, so every line shows once and the page never scrolls sideways. A result that is `TooLarge` shows "Too
large to compare line by line." and offers the Head and the Baseline view that the file has (an added file only the
Head view); a binary file shows the binary notice.

`DiffView` has the parameters `Result`, `OnlyLineEndingsDiffer`, `Threads`, `FocusedThreadId`, `SelectedSide`,
`SelectedStart`, `SelectedEnd`, `ThreadTemplate` (how the page draws a thread, so the reply, resolve and reopen
controls are the ones of the thread list), `Composer` (the new-thread form, drawn under the selected row), and the
event `OnComment`, which carries a `DiffCommentRequest(Side, Line, Extend)`. The control of a left cell asks for the
Baseline side and its baseline line number (a removed line, or the baseline copy of an unchanged line); the control
of a right cell asks for the Head side and its head line number. A thread is drawn under the row of the last line of
its anchor, across the full width, and the cell of its side is marked. The component holds only which collapsed runs
the reader has expanded; every other state is the page's.

**CommentOnDiffLine**: Handles an `OnComment` request. A plain click selects the line on its side and opens the
new-thread form under its row; Shift-click, or the "Through line (optional)" field, extends the selection to a
range, but only on the same side, because the two sides have different numbering. The form says what it will do in
plain words ("Comment on line 10 (before)", "Comment on line 12 (after)"), and offers Cancel
(`CancelComment`) and "Comment on the whole file instead" (`CommentOnWholeFile`), which clears the selection and
keeps the typed text.

**CommentOnLine**: Fills the start and end line of the new-thread form with one line number, and makes the form the
scroll target, because the form sits below the code and the threads.

**StartThreadAsync**: Starts a thread on the selected file from the form, on behalf of the signed-in user, who comes
from the authentication state (`SignIn.GetActorAsync`), on the side `NewSide` (Head unless the form was opened from a
left cell of the diff). Both line fields blank anchor the whole
file, only a start line anchors that line, and both anchor the range. A line field that is not a whole number is
reported as "Line numbers must be whole numbers." A violated comment rule is shown in `thread-error` next to the
form and everything typed stays. Success clears the text and line fields and reloads the
threads.

**ReplyAsync**: Adds the typed reply to a thread on behalf of the signed-in user. A violated rule is shown inside that
thread.

**ToggleThreadAsync**: Resolves an open thread or reopens a resolved one, then reloads the threads. Resolving also
collapses the thread. A thread that no longer exists is reported inside the thread.

**SetFilter**: Sets the state the thread index shows. Each index row is a button whose accessible name states the
anchor, file, state, comment count, first author, and last activity, so a screen-reader user hears what a sighted
user sees. **ToggleThreadExpanded**: Expands or collapses a resolved
thread.

**SelectThreadAsync**: Shows a thread chosen in the index. The thread's file is selected through `SelectFileAsync`
only when it is not already shown, so text typed in the new-thread form is kept. A file that has a diff is shown in
the Diff view; a too-large file is shown in the Baseline or the Head view, whichever the thread's side names. The
thread becomes the focused
thread, a resolved thread is expanded, and the first line of its anchor (a row of the diff, a baseline line, a head
line, or the thread card for a whole-file thread) becomes the scroll target. A file that can be compared words the
anchor of a thread and of an index row as "Before: line N" or "After: lines N-M" (the side is the thread's
`ThreadSide`; this applies to every file of a review with a baseline commit, an added file included); other files
keep "Line N". Each thread card has a `thread-link-{id}` anchor, a plain, small, muted text link (class
`thread-link`, not the button-style `action`) to the thread's address, so it does not compete with Resolve and
Reply. The index rows come from `ThreadService.BuildIndex` and the next-step line from
`Approval.NextStep` (which `SigningService` builds with `ThreadService.NextStep`, given the checklist progress and the
number of checks answered Fail, and words itself for missing slots and signatures), so the page holds no ordering or
wording rules of its own.
The summary of the checklist card is "{answered} of {total} checks answered", or "Checklist complete" when nothing
is left to answer.

**AddSlotAsync**, **SaveSlotAsync**, **RemoveSlotAsync**: Add a slot from the add-slot form, save the primary and
alternate chosen for a slot, and remove a slot, on behalf of the signed-in user through `SigningService`. The select
options carry the user identity as value and "{display name} ({user name})" as text. A missing primary is reported by
the page ("Choose a primary reviewer."); every other violated rule (the same user twice, a user who is not enabled, a
slot that has a signature) is a `DomainRuleException` from Core and is shown in `new-slot-error` or in the
`slot-error-{id}` of that slot, with the choices kept. Success reloads through `RefreshAsync`. The edit controls
(selects, Save, Remove) are drawn only for a slot with no signature at all, current or out of date; Core refuses the
change as well.

**SignAsync**: Signs the review for one slot. The sign form is drawn for a signed-in user who is the primary or the
alternate of a slot that is not currently signed (decided from the user name in the authentication state). While the
review has blockers (no checklist, unanswered or failed checks, open threads), the page draws the list of blockers
headed "This review cannot be signed yet:" instead of the form. Otherwise the form shows the meaning text of the
signature (`SigningService.MeaningText`) and a password field. On submit the page calls `SigningService.SignAsync`
with the signed-in user, the slot, the password typed, and the record digest it displayed (`Approval.Digest`), so what
is signed is exactly what the reader saw; a record that changed in the meantime is refused by Core, and the page shows
that message. A `DomainRuleException` (wrong password, a user who is not in the slot, a locked-out user, a changed
record) is shown in `sign-error-{id}`; the wrong-password message is the generic one Core produces. The password is
only held in the component field `SignPasswords` while it is typed: the `finally` block removes it after every
attempt, whatever the outcome, and increments `PasswordKey`, which is part of the `@key` of the field so that the
browser builds a new, empty input. It is never rendered as a value, logged, or kept. After the attempt the page calls
`RefreshAsync`, so a signature shows as signed, locks the slot, and may turn the badge to Approved.

**ShortDigest**: Shortens the record digest to its first 16 characters; the Details section shows the short value
with the full 64 characters in the `title` attribute.

**OnAfterRenderAsync**: Marks the page interactive after the first render on a live connection, and, when a
replacement address is pending, calls the browser function `reviewCenter.replaceUrl` (defined in `App`) with it. Then,
when a scroll
target is set, it calls the browser function `reviewCenter.scrollToTestId` (defined in `App`) once with the test id and
where it should land (`start` or `center`) and clears the target. Highlighting is done by the page itself (a
`focused` class), so only the scroll needs the browser; a
connection that is already gone is ignored.

**Deep links**: The page has three routes: the review, `/projects/{id}/reviews/{reviewId}/files/{**FilePath}` (with the
optional query values `line` and `side`, `head` or `baseline`), and `/projects/{id}/reviews/{reviewId}/threads/{id}`.
`OnParametersSetAsync` applies the address after the load and before `Loaded` becomes true, so the first response (and
therefore a reload) already shows the file, the highlight and, on a live connection, the scroll. `ApplyAddressAsync`
selects the file (or the file of the thread) through the ordinary `SelectFileAsync` or `SelectThreadAsync`. For a
line it parses `line` as digits only, reads `side` (anything but `baseline` is the head), checks that a baseline line
is asked only of a file that has a baseline version (never an added file), and checks that the line exists; the diff
row of the line is pinned so it
is not hidden in a collapsed run, and `DiffView` gets `HighlightSide` and `HighlightLine`. A file that has no diff is
shown in the Head or the Baseline view, with the line marked there. The router decodes the file path once, so the page
never decodes it again; the one exception is that the router also turns a literal `%2F` of a file name into a slash,
so a file path is also matched with `%2F` in the names read as `/` when that matches exactly one file. An address that
names nothing (an unknown file or thread, a thread of another review, a baseline line of a file without a baseline, a
line that is not a number or does not exist) sets `DeepLinkMessage`, shown above the file list with the link
`deep-link-back` to the plain review address; nothing is selected and the page works as usual. Choosing a file
(`ViewFileAsync`) or a thread in the index (`ViewThreadAsync`) selects as before and sets the pending address to the
link of the file or thread; the page replaces the address of the browser by it with `history.replaceState`, so the
address bar always holds a shareable link and the back button is not filled with entries. `NavigateTo` with
replacement is not used, because the router would re-initialize the page and lose what is typed. The view mode (Diff,
Head, Baseline) is not part of the address, so a reload shows the Diff view. Each thread card has the plain anchor
`thread-link-{id}` with the link of the thread (a plain, small, muted text link, class `thread-link`). **ReviewLinks**
builds all of these addresses: it escapes the review
identifier and each path segment with `Uri.EscapeDataString` and joins them with `/`, and writes `?line=N&side=...`
only when a line is given.

Comments, approval, and slots are read when the page loads and after each change made on the page. A comment or
signature made in another browser appears when this page is next loaded or changed; nothing is pushed. The page
signs the digest it displayed, so a page that is out of date cannot sign a newer record. The checklist is read when
the page loads and after each change.

#### Error Handling

A review that does not exist is shown as a message on the page. A file that has disappeared from the store since
the page loaded is shown as empty text rather than failing the connection. Comment, slot, and signing rule violations
are shown as messages next to the control that caused them and never end the connection. An action that meets an
authentication state that has already ended (the user was disabled, removed, or changed since the page was opened,
and the page has not yet been replaced) is refused by `GetActorAsync` with a rule violation, which the same handlers
show as the message "You are no longer signed in, so nothing was saved. Reload the page to sign in again." The handlers
that change something without a service that takes an actor (resolving and reopening a thread) ask for the actor first
for the same reason. The page
raises no other errors of its own.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content, so the page is replaced by a "Session ended" message when the
  sign-in state of the open page ends.
- **ReviewService** — reads the review, the head and baseline bytes of a file, and the comparison of a file
  (`GetDiffAsync`).
- **DiffView** — draws the Diff view; it is a part of this unit.
- **LineDiff** — groups the rows of a comparison into shown regions and collapsed runs, and arranges them side by side.
- **ThreadService** — starts (on the Head or the Baseline side), answers, resolves, reopens, and lists threads, and
  shapes them for the index.
- **ChecklistService** — reads the checklist progress for the summary.
- **SigningService** — computes the approval status (blockers, record digest, next step), lists the users that can
  fill a slot, adds, changes, and removes slots, and signs; it holds every signing rule.
- **FileText** — decides whether a file is text and splits it into lines.
- **SignIn** — `GetActorAsync` supplies the signed-in user from the authentication state.
- **ProjectService** — reads the name of the owning project for the breadcrumb.
- **ASP.NET Core Blazor components** — the component base class, events, JavaScript interop, the router, and the
  interactive server render mode.
- **App** — defines the scroll function and the address replacement function that the page calls.
- **ReviewLinks** — builds the addresses of the review, its files and lines, and its threads; it is a part of this unit.

#### Callers

- **App** — the router renders the page for the review route.
- **ProjectPage** — navigates to the page after a review is created.
- **Any reader of a shared link** — the file, line and thread addresses of the page are the links people send.
- **HistoryPage** — links back to the page.
