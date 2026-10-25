### ReviewPage

![Web Structure](WebView.svg)

#### Purpose

`ReviewPage` is the page at `/projects/{id}/reviews/{reviewId}`. Its responsibility is to show one review, with the
reader's work first. From the top it shows the title with an approval badge (Approved or In review), a one-sentence
next step, a checklist card (progress summary
such as "37 of 58 checks answered" and a link to the checklist page; absent when the review has no checklist), a
Reviewers card (the reviewer slots with their signature status, the form that adds a slot, and, for a reviewer, the
form that signs), the
files with the size and number
of open threads of each, the thread index of all files, the numbered lines of a selected file, the comment
threads on that file, which a reader can answer, resolve, and reopen (a resolved thread is a one-line header
until expanded), and the form that starts a new thread. Last, in a native `<details>` element that is collapsed by
default, it shows the review's
identifier, state, content fingerprint, record digest, and the hash of each file. It is a Razor component split into
presentation
(`ReviewPage.razor`) and behavior (`ReviewPage.razor.cs`). It reads reviews through `ReviewService` and works with
threads only through `ThreadService`, so every comment rule is applied there. It works with reviewer slots,
signatures, and the approval status only through `SigningService`, so the page contains no signing rules. It requires
a signed-in user, and every comment, slot change, and signature is made on behalf of that user.

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
`start-thread-button`, `file-content`, `code-line-{n}`, `line-comment-{n}`, `line-no-{n}`, `line-marker-{n}`,
`line-text-{n}` (all by 1-based line number), `threads-empty`, `threads-list`, `thread-{id}`,
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

**CommentOnLine**: Fills the start and end line of the new-thread form with one line number, and makes the form the
scroll target, because the form sits below the code and the threads.

**StartThreadAsync**: Starts a thread on the selected file from the form, on behalf of the signed-in user, who comes
from the authentication state (`SignIn.GetActorAsync`). Both line fields blank anchor the whole
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
only when it is not already shown, so text typed in the new-thread form is kept; the thread becomes the focused
thread, a resolved thread is expanded, and the first line of its anchor (or the thread card for a whole-file
thread) becomes the scroll target. The index rows come from `ThreadService.BuildIndex` and the next-step line from
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

**OnAfterRenderAsync**: Marks the page interactive after the first render on a live connection, and, when a scroll
target is set, calls the browser function `reviewCenter.scrollToTestId` (defined in `App`) once with the test id and
where it should land (`start` or `center`) and clears the target. Highlighting is done by the page itself (a
`focused` class), so only the scroll needs the browser; a
connection that is already gone is ignored.

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
show as the message "You are no longer signed in, so nothing was saved. Reload the page to sign in again." The page
raises no other errors of its own.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content, so the page is replaced by a "Session ended" message when the
  sign-in state of the open page ends.
- **ReviewService** — reads the review and the bytes of a file.
- **ThreadService** — starts, answers, resolves, reopens, and lists threads, and shapes them for the index.
- **ChecklistService** — reads the checklist progress for the summary.
- **SigningService** — computes the approval status (blockers, record digest, next step), lists the users that can
  fill a slot, adds, changes, and removes slots, and signs; it holds every signing rule.
- **FileText** — decides whether a file is text and splits it into lines.
- **SignIn** — `GetActorAsync` supplies the signed-in user from the authentication state.
- **ProjectService** — reads the name of the owning project for the breadcrumb.
- **ASP.NET Core Blazor components** — the component base class, events, JavaScript interop, and the interactive
  server render mode.
- **App** — defines the scroll function that the page calls.

#### Callers

- **App** — the router renders the page for the review route.
- **ProjectPage** — navigates to the page after a review is created.
