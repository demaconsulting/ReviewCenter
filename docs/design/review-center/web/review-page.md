### ReviewPage

![Web Structure](WebView.svg)

#### Purpose

`ReviewPage` is the page at `/projects/{id}/reviews/{reviewId}`, which also answers the file, line and thread addresses
described under Deep links below. Its responsibility is to show one review, with the reader's work first. From the top it
shows a breadcrumb (muted, with "/" separators, leading to the projects list and the owning project, and ending in the
review identifier as text instead of a link), the title with an approval badge (Approved, Rejected, or In review) and a
History
button, and a one-sentence next step, which is the single hint of the page about what to do next. For an approved
review a line below the title offers the download of the signed attestation (see Attestation download below). Below it
is a
compact summary strip (`review-summary`) of up to three cells: the checklist (the progress summary such as "37 of 58
checks answered", a progress bar of the answered checks over all checks, and a link to the checklist page; absent when
the review has no checklist), the reviewers ("No reviewer slots yet", or "N of M signed"), and the threads ("N open
· M resolved"). Then come the files with the size and number of open threads of each, the thread index of all files, the
selected file as a diff against its baseline (or as numbered lines), the comment threads on that file, which a reader
can answer, resolve, and reopen (a resolved thread is a one-line header until expanded), and the form that starts a new
thread. After the reading come the Reviewers and signing card (the reviewer slots with their signature status, the
form that adds a slot, collapsed until a reviewer asks for it unless the review has no slot yet or the last attempt
was refused, and, for a reviewer, the form that signs) and, last, in a native `<details>` element that is collapsed by
default, the review's identifier, state, content fingerprint, record digest, and the hash of each file; for a review
from a repository it also shows the repository name, the baseline and head commits, and the selection; for a review that
was planned from a review-set it also shows the identifier of that set (`review-set-id`, the "Review set" row). Each card
has one primary action; navigation buttons such as History, Open checklist, and the way back after an address that names
nothing are outline buttons. The page uses the wide page container, because it shows tables and code. It is a Razor
component split into presentation (`ReviewPage.razor`) and behavior (`ReviewPage.razor.cs`). It reads reviews through
`ReviewService` and works with threads only through `ThreadService`, so every comment rule is applied there. It works
with reviewer slots, signatures, and the approval status only through `SigningService`, so the page contains no signing
rules. It requires a signed-in user, and every comment, slot change, and signature is made on behalf of that user. The
History button (`history-link`) leads to the history page of the review, where the audit entries of the review are
listed.

An approved or rejected (concluded) review is locked. The page shows a Locked badge (`review-locked`) next to the
approval badge and, above the
reading, a banner (`review-locked-banner`) that says why: "This review is concluded and locked. Its content, checklist
and reviewers can no longer change.", and a project administrator can reopen it. While the review is locked every
control that would
change reviewed content or the
reviewer slots is disabled with that explanation (the add-slot selects and button, the slot edit controls, the thread
resolve and reopen toggle, the line-comment buttons of the diff, through
the `CommentsDisabled` parameter of `DiffView`); the forms that could never be sent (the whole-file new-thread form, the
form under a diff row, and the reply box of each thread) are not rendered; reading, the attestation download, and the
evidence index are not
affected. For a project administrator or system administrator the banner is followed by the reopen form
(`reopen-form`): a required reason (`reopen-reason`, at most `ReviewLimits.MaxReopenReasonLength` characters), the
button `reopen-button` ("Reopen review"), and a message area `reopen-error`. When the review has been reopened before, a
Reopen history card (`reopen-history`) lists every reopen with who, when, the reason, and the short previous digest. The
Reviewers card also tells the review author, while the project requires independent reviewers, that the author cannot be
a reviewer (`author-independence-hint`), and the Reviewers card does not offer the author as a candidate. While the
project does not let the person who reopened a review sign it again, a signed-in user who occupies a slot and reopened
the review (now or earlier) sees, in place of the sign form of that slot, the notice `reopener-cannot-sign-{id}`
("You reopened this review; another reviewer must sign."). The page holds none of these
rules; Core decides, and the page only shows the result.

#### Data Model

The page instance is created per browser connection, so its state is per user.

**ProjectId**, **ReviewId**: the route parameters. **FilePath**, **ThreadId**, **LineText**, **SideText**: the optional
route and query values of a deep link. **HighlightLine**, **HighlightSide**, **DeepLinkMessage**: the line the address
named and the message for an address that names nothing; all three are cleared whenever the reader chooses a file.

**Loaded**, **Review**, **ProjectName**: whether loading finished, the review with its file metadata (`null` when
it does not exist, or when the project of the review is not visible to the user, in which case nothing of the review is
loaded), and the name of the owning project shown in the breadcrumb (the project identity is shown
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

**Threads**: all threads of the review, for every file and about every check, in creation order. The summary and the
per-file counts are
calculated from this list, and the selected file's threads are the ones with its path. A thread about a check of the
checklist (`CheckItemId` set, empty `FilePath`) belongs to no file, so it is never among the threads of a selected file;
the summary, the index, and the unread markers count it like any other thread.

**FollowUps**: the identifiers of the reviews of the project that follow this one (their `FollowsReviewId` is this
review), oldest first, taken from `ReviewService.ListAsync` when the page loads. The page shows "Follows X" with the
count of what was carried, and "Followed by" with these links; both are plain links, so nothing is offered that a
user may not do.

**Checklist**: the `ChecklistState` of the review, or `null` when it has none. The page only reads it for the summary
strip; the answers are made on `ChecklistPage`.

**Approval**: the `ReviewApproval` of the review from `SigningService.GetApprovalAsync`: how it is concluded
(`Conclusion`: approved, rejected, or none), the outcome a signature given now would attest (`Outcome`), the
record digest, the slots with their signature state, the blockers that prevent signing, and the next-step sentence.
It is `null` until loaded. **Candidates**: the members of the project that can be chosen for a slot (`SlotMember` values
from
`SigningService.ListCandidatesAsync(actor, projectId)`, so a user who is no member is never offered, and no password
hash ever reaches the page). **ReadOnly**: true for a member who may not contribute (the Auditor role); the page then
shows a `readonly-notice` and none of the controls that change the review (the slot and signing forms, the new
comment form, the reply box, the resolve toggle, and the line comment controls); the reopening and due date forms
stay hidden by their own rights. The diff gets `ReadOnlyAccess` so that it says "read-only access" instead of "review
is locked", the inline composer is never selected while `ReadOnly`, and a reload that finds `ReadOnly` clears the
new comment form and its draft. Each assignee of a slot (a `SlotMember` of the approval) also carries `IsMember`, whether
that user is still a member of the project; the page shows a badge "No longer a project member" next to a primary or
alternate who is not, with a hint under the names that such a reviewer cannot sign until added to the project again.
The candidate lists offer enabled members only, so a primary or alternate who is disabled or no longer a member would
have no matching option and the select would show blank; in the change fields the page therefore adds one extra option
for the stored choice (`UnavailableLabel`: "{display name} ({user name}), disabled or no longer a member", or, for an
enabled member who is left out only because of the independence rule, that is, the review author who holds a slot
(the rule is not retroactive), "{display name} ({user name}), not offered for a new choice (review author)"), with the
test ids `slot-primary-unavailable-{id}` and `slot-alternate-unavailable-{id}`, and offers no extra option when the
stored choice is among the candidates.
**CurrentUsername**: the user name
of the signed-in user, from the authentication state, used to tell which slots the user occupies.
**IsLocked**: whether the review is locked, which is `Approval.IsConcluded` (approved or rejected); **ConclusionLabel**
and **ConclusionBadgeClass**: the text ("Approved", "Rejected", or "In review") and the style of the status badge;
**SigningOutcome** and **SigningOutcomeLabel**: the outcome a signature given now attests (`Approval.Outcome`, approved
when there is none) and its words ("APPROVED" or "REJECTED"); **LockedMarker**: the text `true` or `false`
written to the `data-locked` attribute of the disabled controls, so that tests can tell a locked control.
**CanSetDueDate**: whether the signed-in user may change the due date, asked with `ReviewService.CanSetDueDateAsync` in
`RefreshAsync` (a project or system administrator, or the user who created the review, never an API token);
**CanEditDueDate**: `CanSetDueDate` and not `IsLocked`, which decides whether the form is drawn; **IsOverdue**: the
answer of `ReviewService.IsOverdue` for the review and `IsLocked`; **DueDateInput**, **DueDateError**: the text of the
date field (`yyyy-MM-dd`) and the message of a refusal.
**CanWriteConclusion**: whether the signed-in user may write the review conclusion text, asked with
`ChecklistService.CanSetReviewConclusionTextAsync` in `RefreshAsync` (a primary or alternate reviewer of a slot, or the
lead of the review: the creator or a project administrator; never an auditor, a non-member, or an API token);
**ShowConclusionEditor**: `CanWriteConclusion` and not `ReadOnly` and not `IsLocked`, which decides whether the editor
is drawn; **ConclusionInput**, **ConclusionError**: the text of the conclusion field and the message of a refusal.
**RequireIndependent**: the `RequireIndependentReviewers` setting of the project, read with the project
in `RefreshAsync`; **VisibleCandidates**: `Candidates` without the review author (`Review.CreatedBy`) when
`RequireIndependent` is on. **RequireNonReopener**: the `RequireNonReopenerSigning` setting of the project, read in
the same place; **ReopenerBlocked**: whether the signed-in user is in the reopen history of the approval while that
setting is on (a fact about the viewer, so it is not one of the blockers of the approval). **CanReopen**: whether the
signed-in user is a project administrator of the project or
a system administrator, asked with `IProjectAccess.CanAdministerAsync` in `RefreshAsync`. **ReopenReason**,
**ReopenError**: the text typed into the reopen form and its message.

**Warning**: the pending `PendingSignatureWarning` (`null` when there is none): which form or thread it belongs to, the
affected slots, and the call to repeat when the person confirms. Only one exists at a time. It is cleared when the route
parameters change, when the review is found locked, and when the new-thread form is cleared.

**AttestationKeys**: the `IAttestationKeyProvider` of the `Evidence` subsystem, injected so that the page can ask for the
status of the signing key (`Describe`) without ever reading the key.

**NewSlotPrimary**, **NewSlotAlternate**, **NewSlotError**: the add-slot form choices (user identities as text, blank
for nobody) and its message. **SlotPrimaryChoices**, **SlotAlternateChoices**, **SlotErrors**: the choices made in
the edit fields and the message of each slot, by slot identity; until the reader changes a choice the stored
primary and alternate are shown. **SignPasswords**, **SignErrors**: the password typed into the sign form and the
message of each slot, by slot identity. **SignTotpCodes**: the one-time code typed into the sign form of a slot, by
slot identity, and **CurrentHasTotp**: whether the signed-in local user has a second factor, asked with
`TotpService.IsEnrolledAsync` when the page loads (it is false for an account of an identity provider).
**PasswordKey**: a counter that makes the browser build a fresh, empty
password field after every signing attempt. **CurrentIsExternal**, **ConfirmedSlots**, **ReauthText**,
**ReauthReportDismissed**: whether the signed-in user signs in with an identity provider, the slots that have a live
confirmation (asked from `SigningService` when the page loads and after each change, never spent by the page), the
`reauth` query value (`ok` or `failed`) with which the identity endpoint sent the browser back, and whether the user
has acted since so that a failure report is no longer shown. The settings of the provider are injected
(`IOptions<OidcOptions>`) for its display name and whether it is on.

**NewStartLine**, **NewEndLine**, **NewText**, **NewThreadError**: the new-thread form fields and its message.
**ReplyTexts**, **ReplyErrors**: the reply text and the message of each thread, by thread identity.

**InteractiveMarker**: as for the other pages.

**Unread markers**: **UnreadBaselineCommentId** is the highest comment identity the user had seen before this visit,
read by `InboxService.GetReviewUnreadAsync` when the review is loaded (zero for a first visit). A comment of somebody
else with a higher identity is new for the whole life of the page, so the markers stay while the reader works. The page
shows `index-unread-{id}` (a "New" badge on a row of the thread index, the suffix is the thread identity),
`thread-unread-{id}` (a "New" badge on a thread with a new comment), and `review-unread-summary` (the
sentence with the number of new comments), and shows none of them for the user's own comments or when nothing is new.
**SeenAt** is the time read before the review was loaded, and **MarkSeenPending** says that the state still has to be
stored.
**IndexFilter**: the state the thread index shows (`Open` by default, `Resolved`, or `null` for all).
**ExpandedThreads**: the identities of the resolved threads the reader has expanded; an open thread is always shown
expanded. **FocusedThreadId**: the thread chosen in the index, whose lines and card are highlighted. The page also
keeps the test id to scroll to after the next render and the test id to focus after the next render (the comment
field, set when a `+` or Comment button opens the composer); the focus goes through `reviewCenter.focusTestId`, defined
by `App`, and a closed circuit during that call is ignored. All of these are cleared when the route parameters change.

The comment fields (`thread-text-input` and every `reply-text-{id}`) carry the attribute `data-ctrl-enter-submits`, which
names the test id of their submit button (`start-thread-button` or `reply-button-{id}`); a listener defined by `App`
presses that button when Ctrl+Enter (or Cmd+Enter) is pressed in the field and the button is enabled, so the shortcut
costs no server round trip per key. A hint "Ctrl+Enter to submit" (`thread-submit-hint`, `reply-submit-hint-{id}`)
stands next to the button. In a locked review the whole-file form, the form under a diff row, and the reply box of
every thread are not rendered at all (they could never be sent), the `+` and Comment buttons stay and are disabled with
`data-locked="true"`, and the diff hint reads "This review is locked; comments are read-only."

Test identifiers: `review-page` (root), `breadcrumb-projects-link`, `project-link`, `review-loading`,
`review-not-found`, `review-title`, `review-approval`, `review-locked`, `review-locked-banner`, `review-due-section`,
`review-conclusion`, `review-conclusion-text`, `review-conclusion-empty`, `review-conclusion-form`,
`review-conclusion-input`,
`review-conclusion-save`, `review-conclusion-error`, `attestation-rejected-note`,
`review-due-date`, `review-overdue`, `review-due-none`, `due-date-form`, `review-due-input`, `review-due-save`,
`review-due-clear`, `review-due-error`, `reopen-form`,
`reopen-reason`, `reopen-button`, `reopen-error`, `reopen-history`, and `reopen-entry-{n}`, `reopen-by-{n}`,
`reopen-time-{n}`, `reopen-reason-{n}`, `reopen-digest-{n}` (by the position of the reopen, starting at 0),
`author-independence-hint`, `slots-locked-hint`, `next-step`, `review-summary` (the summary strip),
`checklist-card` (its checklist cell), `checklist-progress-summary`, `checklist-link`, `summary-reviewers`,
`summary-threads`, `reviewers-card`, `slots-empty`, `slot-{id}`, `slot-label-{id}`, `slot-primary-{id}`,
`slot-alternate-{id}`, `slot-primary-not-member-{id}`, `slot-alternate-not-member-{id}`, `slot-not-member-hint-{id}`,
`slot-primary-unavailable-{id}`, `slot-alternate-unavailable-{id}`,
`slot-status-{id}`, `slot-primary-select-{id}`, `slot-alternate-select-{id}`,
`slot-save-{id}`, `slot-remove-{id}`, `slot-error-{id}`, `sign-blockers-{id}`, `reopener-cannot-sign-{id}`,
`sign-form-{id}`, `sign-outcome-{id}`, `sign-meaning-{id}`,
`sign-password-{id}`, `sign-totp-{id}` (only for a user with a second factor), `sign-error-{id}`, `sign-button-{id}`
(all by slot identity), and for an external user
`sign-reauth-form-{id}`, `sign-reauth-{id}`, `sign-reauth-hint-{id}`, `sign-reauth-off-{id}`, and `sign-confirmed-{id}`,
`slot-add-form`,
`new-slot-primary-select`, `new-slot-alternate-select`, `new-slot-add-button`, `new-slot-error`,
`review-details`, `review-details-summary`, `review-id`,
`review-fingerprint`, `record-digest`, `file-hash-path-{index}`, `file-hash-{index}`, `thread-index`,
`thread-summary` (the filter group), `index-filter-all`, `index-filter-open`, `index-filter-resolved`,
`threads-all-count`, `threads-open-count`, `threads-resolved-count`, `thread-index-list`, `thread-index-empty`,
`index-row-{id}`, `index-file-{id}`, `index-anchor-{id}`, `index-state-{id}`, `index-count-{id}`,
`index-author-{id}`, `index-activity-{id}`, `files-list`, `file-row-{index}`, `file-path-{index}`,
`file-size-{index}`, `file-open-threads-{index}`, `file-select-{index}`,
`selected-file-path`, `file-binary-notice`, `file-empty-notice`, `new-thread-form`,
`thread-start-line-input`, `thread-end-line-input`, `thread-target`, `thread-text-input`, `thread-error`,
`start-thread-button`, `thread-submit-hint`, `thread-cancel-button` and `thread-whole-file-button` (only in the form
under a diff row), `signature-warning`, `signature-warning-slots`, `signature-warning-confirm` and
`signature-warning-cancel` (while a refusal is pending; one banner at a time),
`file-content`, `code-line-{n}`, `line-comment-{n}`, `line-no-{n}`, `line-marker-{n}`,
`thread-link-{id}`,
`thread-copy-link-{id}`, `thread-copy-status-{id}`,
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
`review-follows`, `review-follows-link`, `review-carried-counts`, `review-followed-by`, `review-followed-by-link-{id}`
(only for a follow-up, or a review that has follow-ups), `thread-carried-{id}` and `thread-carried-link-{id}` (a thread
carried over from another review),
`review-head-sha`, `review-selection` (in the details of a review from
a repository), `threads-empty`, `threads-list`, `thread-{id}`,
`thread-anchor-{id}`, `thread-state-{id}`, `thread-count-{id}`, `thread-expand-{id}` (resolved threads only),
`thread-toggle-{id}`, `comment-{id}-{n}`, `comment-author-{id}-{n}`,
`comment-time-{id}-{n}`, `comment-text-{id}-{n}` (by thread identity and the position of the comment in the
thread), `reply-text-{id}`, `reply-button-{id}`, `reply-submit-hint-{id}`, and `reply-error-{id}`. The comments,
`thread-toggle-{id}` and the
reply controls of a resolved thread exist only while it is expanded.

#### Key Methods

**OnParametersSetAsync**: Clears the displayed review, project name, file selection, threads, approval, and everything
typed (including the slot choices, messages, and typed passwords), and sets `Loaded` to false before the first await,
then loads the review and calls `RefreshAsync`. The router reuses
the component when only the route changes, so following a link to another review shows the loading state and never
the previous review, its file content, or a half-typed comment. Each file's `View` button has an `aria-label` of
`View {path}` so that screen-reader users can tell the buttons apart.

**RefreshAsync**: Reloads everything the record depends on: the threads, the checklist, the approval (with the reopen
history), the candidates for a slot, the review rule and the administrator right for the reopen form, and the user name
of the signed-in user. The user name is read straight from the authentication
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
when there are rows, the hint "Click + to comment on a line." (or, while `CommentsDisabled`, "This review is locked;
comments are read-only."). The rows are grouped by `LineDiff.Segment`, and a run
of unchanged lines that is collapsed shows a button "Show N unchanged lines" that expands it. The rows of threads and
of the selected line are pinned, so they are never hidden; a thread pins both its first and its last row, because the
thread index and links scroll to the first row. The first change is therefore near the top of the view,
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
controls are the ones of the thread list), `Composer` (the new-thread form, drawn under the selected row), `CanReveal`
(a check that the page runs before a collapsed run is expanded, so a user who lost the access is not shown loaded
lines), `CommentsDisabled` (true while the review is locked: the line-comment buttons are drawn disabled, with
`data-locked`), and the event `OnComment`, which carries a `DiffCommentRequest(Side, Line, Extend)`. The control of a left
cell asks for the
Baseline side and its baseline line number (a removed line, or the baseline copy of an unchanged line); the control
of a right cell asks for the Head side and its head line number. The control is a button in the gutter of every cell
that is revealed by the stylesheet when the row is hovered, has keyboard focus inside it (`:focus-within`), or is the
selected row, and is always shown on a device without hover (`@media (hover: none)`). It is hidden with `opacity`, not
`display` or `visibility`, so it keeps its place and its tab stop and a driver such as Puppet can still click it
(`puppet hover diff-row-{i}`, then `puppet click diff-comment-{i}`). The Head view keeps its always visible
`line-comment-{n}` button. A thread is drawn under the row of the last line of
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

**Signature warning**: `StartThreadAsync`, `ReplyAsync`, and `ToggleThreadAsync` call the service without an
acknowledgement. The page posts chat only (the comment is not included in the report) and resolving or reopening a
Question or reopening a Finding changes nothing the digest covers, so in this version the service does not refuse these
calls and the banner stays hidden; the handling below is kept for the actions that later pages add (included comments,
promote, demote, and resolving a Finding). When the service refuses with `SignaturesWouldBecomeOutOfDate`, the page
keeps the request and shows the
`SignatureWarning` component (`signature-warning`) where the action was taken: under the new-thread form, in the thread
of the reply, or in the head of the toggled thread. The banner names every affected slot and its signer
(`signature-warning-slots`, for example "Slot 1 (Ann)"). Nothing is stored and everything typed stays. **Confirm
change**
(`signature-warning-confirm`) repeats the call with the identities of the affected slots as the acknowledgement. When
another signature appeared meanwhile, the service refuses again and the page shows the banner again with the slots of
both refusals merged, so a person confirms only what they have been shown. **Cancel** (`signature-warning-cancel`) only
removes the banner; the typed text and an unsaved choice stay. The page holds one banner at a time, so the test
identifiers stay unique; a new refusal replaces the earlier banner, and a reload of a locked review removes it. Confirm
runs the session check of the page again, so an expired session is handled like any other action. A refusal for any
other rule is shown as before and no banner is shown. The reopen of a whole review (`ReopenReviewAsync`) is not part of
the acknowledgement and does not show the banner.

**SetFilter**: Sets the state the thread index shows. Each index row is a button whose accessible name states the
anchor, file, state, comment count, first author, and last activity, so a screen-reader user hears what a sighted
user sees. **ToggleThreadExpanded**: Expands or collapses a resolved
thread.

**Check discussions**: when the review has threads about a check, a card with the heading "Check discussions"
(`check-discussions`) lists them with the same thread card as the threads of a file (the card is one render fragment
that both places use), with the anchor worded `Check {id}`, the state, and the comments as plain text. Replying and
resolving work as for any thread, with the same acknowledgement handling. The index row of such a thread shows
"Check discussion" in place of a file path, and choosing it (or a deep link to it) focuses the thread without selecting
a file (`SelectThreadAsync` and the address handling skip the file selection for a thread about a check). The page has no
form to start one yet; the entry comes with the ballot screen. The card is absent when the review has none.

**SelectThreadAsync**: Shows a thread chosen in the index. The thread's file is selected through `SelectFileAsync`
only when it is not already shown, so text typed in the new-thread form is kept. A file that has a diff is shown in
the Diff view; a too-large file is shown in the Baseline or the Head view, whichever the thread's side names. The
thread becomes the focused
thread, a resolved thread is expanded, and the first line of its anchor (a row of the diff, a baseline line, a head
line, or the thread card for a whole-file thread) becomes the scroll target. A file that can be compared words the
anchor of a thread and of an index row as "Before: line N" or "After: lines N-M" (the side is the thread's
`ThreadSide`; this applies to every file of a review with a baseline commit, an added file included); other files
keep "Line N". Each thread card has a `thread-link-{id}` anchor (class `thread-link action outline small`, so it is
button-styled like the buttons beside it) to the thread's address, and a `thread-copy-link-{id}` button ("Copy link")
that copies the absolute address, with a `thread-copy-status-{id}` live region for the feedback. The index rows come
from `ThreadService.BuildIndex` and the next-step line from
`Approval.NextStep` (which `SigningService` builds with `ThreadService.NextStep`, given the checklist progress and the
number of checks answered Fail, the number of disputed checks, and whether a conclusion text is needed, and words itself
for missing slots and signatures), so the page holds no ordering or
wording rules of its own.
The summary of the checklist cell is "{answered} of {total} checks answered", or "Checklist complete" when nothing
is left to answer, and its progress bar shows the answered checks over all checks. The reviewers cell reads "No reviewer
slots yet" when the review has no slot, and otherwise "{signed} of {slots} signed", where a slot counts as signed only
while its signature is current; the threads cell counts the open and the resolved threads from `Threads`. The strip is
drawn from the data the page already holds, so it changes after every change made on the page.

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
review has blockers (no checklist, unanswered or disputed checks, open threads, or a rejected outcome without a
conclusion text), the page draws the list of blockers
headed "This review cannot be signed yet:" instead of the form. A check answered Fail is not a blocker. Otherwise the
form first states the outcome the signature will attest (`sign-outcome-{id}`: "You are signing: APPROVED" or "You are
signing: REJECTED", from `Approval.Outcome`) and shows the meaning text of that outcome
(`SigningService.MeaningFor`, `sign-meaning-{id}`: the approval text, or the rejection text for a rejected outcome) and
a password field. For a local user with a second factor (`CurrentHasTotp`)
the form also has a field "Current code from your authenticator app" with the hint "If you just signed in with this
code, wait for the next one.", because Core refuses a code that was already used in the same time step. For a user who
signs in with an identity provider
(`CurrentIsExternal`, from `UserService.GetAsync`) the password field is replaced: until a confirmation exists for the
slot (`ConfirmedSlots`, from `SigningService.HasReauthProofAsync`, which spends nothing) the page draws a form that
posts to `/auth/oidc/reauth` with the project, review, slot, and the address of this review, and the button "Confirm
identity with {provider}" with a hint; when the provider is switched off it says that instead and offers no button.
After the confirmation the page shows "Identity confirmed. Review the record, then sign." and the same Sign button,
with no password field; a failed confirmation is shown next to the button until the user acts, and the text depends on
the `reauth` value of the address: `reauth=failed` shows "We could not confirm your identity. Sign in again at
{provider} when asked."; `reauth=missing-auth-time` shows "{provider} did not report a usable sign-in time
(auth_time). An administrator must check that it sends auth_time as a whole number of seconds since 1970 (see the
Identity Providers chapter of the user guide)." (the same text for an `auth_time` that is absent and for one that is
present but not a whole number of seconds or out of range);
`reauth=stale-auth-time` shows "Your sign-in at {provider} was not recent enough. Try again and sign in when asked.";
any other value shows nothing, so the address cannot put text on the page. On submit the page
calls `SigningService.SignWithCredentialsAsync`
with the signed-in user, the slot, the password typed (empty for an external user, whose proof is held by Core), the
code typed (`SignerCredentials.TotpCode`, null without a second factor), and the
record digest it displayed (`Approval.Digest`), so what
is signed is exactly what the reader saw; a record that changed in the meantime is refused by Core, and the page shows
that message. If a slot holds the review author while the project requires independent reviewers (for example because
the rule was switched on after the slot was filled), the sign form is still drawn, and Core refuses the signature with
`ReviewerNotIndependent`; the page shows that message in `sign-error-{id}`. If the signed-in user reopened the review
while the project does not let a reopener sign again, the sign form is replaced by the notice, and a stale page that
still sends the signature gets `ReopenerCannotSign` from Core. A `DomainRuleException` (wrong password,
a user who is not in the slot, a locked-out user, a changed
record) is shown in `sign-error-{id}`; the wrong-password message is the generic one Core produces, and a wrong or
missing code gets the same message ("The password or the code is incorrect."). A reviewer who is no
longer a member of the project (`SlotMember.IsMember` false) cannot sign: Core refuses, and the page shows that message
and the badge and hint described above. The password is
only held in the component field `SignPasswords` while it is typed (and the code in `SignTotpCodes`): the `finally`
block removes both after every
attempt, whatever the outcome, and increments `PasswordKey`, which is part of the `@key` of the field so that the
browser builds a new, empty input. It is never rendered as a value, logged, or kept. After the attempt the page calls
`RefreshAsync`, so a signature shows as signed, locks the slot, and may turn the badge to Approved or Rejected.

**Due date**: Under the title the page shows the due date as `Due yyyy-MM-dd (UTC)` (`review-due-date`), or `No due
date` (`review-due-none`); the date is a calendar day in UTC and the page says so. While the date is before today (UTC)
and the review is not concluded (`IsOverdue`) it also shows the Overdue badge `review-overdue`; a concluded review keeps
its date, without the badge, as history. The form `due-date-form` (a date input, Save, and Clear) is drawn only when
`CanEditDueDate` holds, so never for a user who may not change the date and never for a locked review.

**Review conclusion**: A card `review-conclusion` shows the review conclusion text, the reasons that the reviewers
state, which a rejected review cannot be signed without. The card is always drawn, for everyone who can read the
review. A person who may write it (`ShowConclusionEditor`: a slot owner or the lead of the review, who may
change the review, while the review is not locked) gets the form `review-conclusion-form` with a text area
`review-conclusion-input` (at most `ReviewLimits.MaxConclusionTextLength` characters) and the button
`review-conclusion-save`; everybody else, and everybody once the review is locked, sees the text read-only in
`review-conclusion-text`, or, when no text is written, the empty state `review-conclusion-empty` ("No conclusion
written."). **SaveConclusionAsync** (behind the form) calls `GuardAsync` and then
`ChecklistService.SetReviewConclusionTextAsync` with the signed-in user and the typed text. A change that would make a
current signature out of date (the text is part of the record digest) is not stored at once: Core refuses it with
`SignaturesWouldBecomeOutOfDate`, the page shows the `SignatureWarning` confirmation inside the form with the affected
slots, and a confirmation repeats the call with the acknowledged slots; cancelling stores nothing. Any other
`DomainRuleException` is shown in `review-conclusion-error` with the typed text kept; for `RecordChanged` (someone else
changed the text) and `ReviewLocked` the page also reloads the review so that the stored text and the lock are shown.
Success reloads through `RefreshAsync`.

**ChangeDueDateAsync** (behind `SaveDueDateAsync` and `ClearDueDateAsync`): after `GuardAsync`, reads the text with
`ReviewService.ParseDueDate` (an empty text clears the date) and calls `ReviewService.SetDueDateAsync` with the
signed-in user. A `DomainRuleException` (text that is not a date, a date too far ahead, a review that was locked
meanwhile, a user who may not change it, a date that someone else changed) is shown in `review-due-error` with the typed
text kept; for `RecordChanged` and `ReviewLocked` the page also reloads the review so that the stored date and the lock
are shown. Success reloads the review and the approval through `RefreshAsync`.

**ReopenAsync**: Reopens a locked review on behalf of the signed-in user through `SigningService.ReopenReviewAsync` with
the typed reason. The form is drawn only for an administrator (`CanReopen`) of a locked review, and Core checks the same
again (an API token is never involved, because the page works with the signed-in user). A violated rule (an empty or
too long reason, a review that is no longer locked, a user who is no administrator) is a `DomainRuleException` shown in
`reopen-error`, with the typed reason kept. A reopening that would leave a slot with nobody who may sign it
(`ReopenWouldStrandSlot`, only while the project does not let a reopener sign again) is shown there too, with the
message that names the slot. Success clears the reason and reloads through `RefreshAsync`, so the badge
turns to In review, the signatures show as out of date, and the history lists the reopen.

**Locked controls**: When `IsLocked` is true the markup disables the add-slot selects and button and shows the hint
`slots-locked-hint` with them, and disables the thread toggle, the reply field and button, the new-thread fields and
button, and the line-comment buttons. The slot edit controls are drawn only for a slot with no signature, and a locked
review has none. The handlers also stay correct if a stale page still fires one, because Core refuses the change with
`ReviewLocked` and the message is shown in the usual place. A page that was loaded before another user signed the last
slot learns about the lock at its next reload or change, and is refused, not corrupted, until then.

**ShortDigest**: Shortens the record digest to its first 16 characters; the Details section shows the short value
with the full 64 characters in the `title` attribute.

**Attestation download**: Below the status of an approved review the page asks `AttestationKeys.Describe()` for the
state of the signing key. When the key is available it draws the link "Download attestation (PDF)" (an anchor whose
address is `ReviewLinks.ForAttestation`, with the file name from `AttestationService.FileNameFor`, so that the browser
downloads it instead of navigating), a note, and a link to the help topic `signed-attestation`. The note says "Signed
with a self-signed key generated by this server - not an organization key." for a self-signed key of the `SelfSigned`
provider; "Signed with a self-signed certificate supplied by the administrator - not an
organization key and not issued by a certificate authority." for a self-signed key of any other provider (a
certificate from the `CertificateStore` or `File` provider); and otherwise names
the subject and the start of the thumbprint of the certificate. Each warning of the status (for example that the
certificate ends soon) follows as a status notice with the test id `attestation-key-warning`. When the key cannot be
used the page shows "Attestation
is unavailable:" with the reason of the provider instead of the link. A review that is not approved shows nothing, except
that a review concluded as rejected shows the note `attestation-rejected-note`: "The review was rejected. A signed
attestation of a rejected review is not available yet." The
page never signs: the download is a plain authenticated `GET` handled by `AttestationEndpoint`, and the page only
decides whether to offer it.

**OnParametersSetAsync** (unread state): The time is read before anything is loaded and kept as the time seen. After the
review is loaded for a signed-in user, `InboxService.GetReviewUnreadAsync` gives the baseline and the page sets the
mark-seen flag. A user who may not see the project gets a baseline of zero and nothing is stored.

**OnAfterRenderAsync**: Marks the page interactive after the first render on a live connection and, when the mark-seen
flag is set, clears it and calls
`InboxService.MarkSeenAsync` with the highest comment identity of the loaded threads written at or before the time
seen, so a comment posted after the load began stays new. The flag is
checked only here and not while the page is rendered first on the server, so only a review that was shown on a live
connection counts as
seen. The service checks the access again, so a user who may not view the review leaves no row behind. A change of the
route to another review clears the baseline and the flag and reads them again. Then, when a
replacement address is pending, the method calls the browser function `reviewCenter.replaceUrl` (defined in `App`)
with it. Then,
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
Head, Baseline) is not part of the address, so a reload shows the Diff view. Each thread card has the anchor
`thread-link-{id}` with the link of the thread (class `thread-link action outline small`) and the button
`thread-copy-link-{id}` (the label stays "Copy link"). **CopyThreadLinkAsync** makes the address absolute with `NavigationManager.ToAbsoluteUri`,
calls the browser function `reviewCenter.copyText` (defined in `App`) and shows "Copied" in the status region next to
the button, or "Copy failed" when the browser answers false, throws, or is gone. The feedback is cleared two seconds
after the latest click on that thread: each thread has its own timer (a `CancellationTokenSource` used with
`Task.Delay` on the injected `TimeProvider`), a new click cancels and replaces the previous timer, and the page cancels
all timers when it is disposed. The timer runs in a continuation that does not block the click handler and ignores a
page that no longer exists. **ReviewLinks**
builds all of these addresses: it escapes the review
identifier and each path segment with `Uri.EscapeDataString` and joins them with `/`, and writes `?line=N&side=...`
only when a line is given.

Comments, approval, and slots are read when the page loads and after each change made on the page. A comment or
signature made in another browser appears when this page is next loaded or changed; nothing is pushed. The page
signs the digest it displayed, so a page that is out of date cannot sign a newer record. The checklist is read when
the page loads and after each change.

#### Error Handling

A review that does not exist, or whose project the user may not see, is shown as the same message on the page. A file
that has disappeared from the store since
the page loaded is shown as empty text rather than failing the connection. Comment, slot, and signing rule violations
are shown as messages next to the control that caused them and never end the connection. An action that meets an
authentication state that has already ended (the user was disabled, removed, or changed since the page was opened,
and the page has not yet been replaced) is refused by `GetActorAsync` with a rule violation, which the same handlers
show as the message "You are no longer signed in, so nothing was saved. Reload the page to sign in again." The handlers
that change something without a service that takes an actor (resolving and reopening a thread) ask for the actor first
for the same reason. The page
raises no other errors of its own.

**Access re-check**: Every handler that reads or stores something starts with `GuardAsync`, which asks
`ProjectViewGuard.GuardViewAsync` whether the signed-in user may still see the project (`ShowNoAccess` is the callback).
The view-only handlers (thread filter, expanding a thread, the diff, head, and baseline views, and the comment
buttons) are wrapped at their binding by `GuardedAsync`, and expanding a collapsed run of the diff asks the same check
through the `CanReveal` parameter of `DiffView` before it shows lines that were loaded but not yet displayed;
cancelling a comment and copying a thread link show no project
data and are not guarded. When access is lost, `ShowNoAccess` resets the component state exactly as a change of address
does (review, project name, files, selection, threads, checklist progress, approval, candidates, and the forms), shows
the same message as for a review that does not exist, and the handler returns without calling a service. An ended
sign-in state (nobody signed in) counts as no access, so the message replaces the "no longer signed in" message on this
page.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content, so the page is replaced by a "Session ended" message when the
  sign-in state of the open page ends.
- **IProjectAccess** (Core) — through `ProjectViewGuard`, asked before every handler whether the user may still see the
  project; also asked (`CanAdministerAsync`) whether the user may see the reopen form.
- **ReviewService** — reads the review, the head and baseline bytes of a file, and the comparison of a file
  (`GetDiffAsync`), and lists the reviews of the project to find the follow-ups of the review.
- **DiffView** — draws the Diff view; it is a part of this unit.
- **SignatureWarning** — draws the signature warning banner and holds the pending request (`PendingSignatureWarning`);
  it is a part of this unit.
- **LineDiff** — groups the rows of a comparison into shown regions and collapsed runs, and arranges them side by side.
- **ThreadService** — starts (on the Head or the Baseline side), answers, resolves, reopens, and lists threads, and
  shapes them for the index.
- **ChecklistService** — reads the checklist progress for the summary.
- **InboxService** — reads what the user has seen of the review before the visit, stores what the user has seen after
  the review was shown, and decides which comments are new (`CountUnread`, `MaxCommentId`).
- **SigningService** — computes the approval status (blockers, record digest, next step, lock, reopen history), lists
  the users that can fill a slot, adds, changes, and removes slots, signs, and reopens a locked review; it holds every
  signing, lock, and reopen rule. It also tells
  (`HasReauthProofAsync`) whether an external user has confirmed the identity for a slot.
- **UserService** — tells whether the signed-in user is an account of an identity provider.
- **TotpService** — tells whether the signed-in user has a second factor, so that the sign form asks for a code.
- **OidcSignIn** and **OidcOptions** (Identity) — the endpoint `/auth/oidc/reauth` that the confirmation form posts to,
  the `reauth` query values, and the display name and state of the provider.
- **FileText** — decides whether a file is text and splits it into lines.
- **SignIn** — `GetActorAsync` supplies the signed-in user from the authentication state.
- **ProjectService** — reads the project for the signed-in user (nothing is loaded when the user may not see it) and its
  name for the breadcrumb.
- **ASP.NET Core Blazor components** — the component base class, events, JavaScript interop, `NavigationManager` (to
  make a thread address absolute), the router, and the interactive server render mode.
- **App** — defines the scroll, address replacement and clipboard copy functions that the page calls.
- **ReviewLinks** — builds the addresses of the review, its files and lines, and its threads, and the address of the
  attestation download; it is a part of this unit.
- **IAttestationKeyProvider**, **AttestationKeyInfo** (`Evidence`) — the status and description of the signing key that
  decide whether the download is offered and what the note says.
- **AttestationService** (`Evidence`) — only the pure file name `FileNameFor` of the download.

#### Callers

- **App** — the router renders the page for the review route.
- **ProjectPage** — navigates to the page after a review is created.
- **Any reader of a shared link** — the file, line and thread addresses of the page are the links people send.
- **HistoryPage** — links back to the page.
