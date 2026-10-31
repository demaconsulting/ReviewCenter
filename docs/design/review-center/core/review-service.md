### ReviewService

![Core Structure](CoreView.svg)

#### Purpose

`ReviewService` owns the rules for reviews: it creates a draft review from uploaded files or from the changes in a
registered git repository, computes each file's hash and the review fingerprint, and reads reviews and file content
back. It is also the home of the review types: `Review`, `ReviewFile`, `ReviewState`, `ChangeKind`, and
`UploadedFile`, and of the result type `RepositoryReviewResult`.

#### Data Model

**Review**: a data class (init-only properties) of `ProjectId`, `Id` (the review identifier, unique within its project),
`Title`, `State`, `Created`, `ContentFingerprint`, `Files` (a list of `ReviewFile`), and `Checklist` (the frozen
checklist copy of the review, see `ChecklistService`; set when the review is created and not loaded by reads of
the review). A review from a repository also has `RepositoryId`, `RepositoryName` (a display copy), `BaselineSha`
(null when no baseline was given), `HeadSha`, `Selection` (the normalized selection text), and `ReviewSetId` (the
review-set of `.reviewmark.yaml` the review was planned from, null when it was not planned from a set; at most 100
characters); all six are null for an uploaded review. Every review also records `CreatedBy` (the user name of the actor
that created it, null for a review stored before this field existed) and `CreatedViaApi` (true when the actor
authenticated with a personal access token). Both are metadata of the review: they are not part of the record digest,
so a signature does not depend on them, and the review list and detail of the API show them. A review also has an
optional `DueDate` (a `DateOnly?`, a calendar day that the product reads as a day in UTC; null when none was given).
The due date is planning information and is likewise not part of the record digest, so a change of it never makes a
signature out of date. A review that was created as a follow-up (see *Follow-up reviews* below) also has
`FollowsReviewId` (the identifier of the one earlier review it follows, at most 100 characters, a plain column without
a foreign key; null for every other review), `CarriedAnswerCount` and `CarriedThreadCount` (how many answers and open
threads were taken over; 0 otherwise), and the unmapped `CarryOver` (the rows to store with the review, only while it
is being created, never loaded). `FollowsReviewId` is part of the record digest, the two counts are not (they can be
derived from the carried rows, which the digest covers).

**ReviewFile**: a data class (init-only properties) of `ProjectId`, `ReviewId`, `Path`, `Content` (the bytes), `Size`,
and `Sha256`, and for a review from a repository `ChangeKind`, `BaselineContent`, `BaselineSize` and
`BaselineSha256`. Lists read back from storage carry the metadata but not the bytes. An uploaded file is stored as
`Added` without baseline columns; a `Deleted` file has empty `Content` and `Sha256` (it has no head content) and its
baseline columns set.

**ChangeKind**: `Added`, `Modified`, or `Deleted`.

**ReviewState**: a closed enumeration whose only value is `Draft`.

**UploadedFile**: the input record of a path and the exact bytes of one file.

**RepositoryReviewResult**: the created `Review` and the list of `SkippedEntry` values (symbolic links, submodules,
and unusable paths that matched the selection). The skipped entries are not stored; they are returned so the page
can tell the creator once, and their number is recorded in the audit detail.

**FileDiff**: the answer of `GetDiffAsync`: a `FileDiffStatus` (`Diff`, `Binary`, `TooLarge`, or `NoBaseline`), the
`LineDiffResult` (only for `Diff`), and `OnlyLineEndingsDiffer`.

Rules for the input: the review identifier consists of 1 to 100 ASCII letters, digits, dots, underscores, and
hyphens and starts with a letter or digit; the title is not empty after trimming and has at most 200
characters; a review has 1 to 200 files; each path is not empty and has at most 400 characters; paths are
unique under an ordinal comparison; each file has at most 2 MiB and all files together at most 20 MiB (the
constants are in `ReviewLimits`; see `ProjectService`).

#### Key Methods

**CreateDraftAsync**: Creates a review in the draft state.

- *Parameters*: `int projectId`, `string? id`, `string? title`, `IReadOnlyList<UploadedFile> files`, an `Actor`, a
  cancellation token, an optional `DateOnly? dueDate`, an optional `string? followsReviewId`, and an optional
  `bool acknowledgeDroppedThreads` (all added last, after the token, so that existing calls are unchanged).
- *Returns*: `Task<Review>` — the stored review with its fingerprint and the hash and size of each file.
- *Preconditions*: `files` and `actor` are not null.
- *Postconditions*: The review is stored in the `Draft` state with the creation time from the clock; the bytes
  are stored exactly as given, together with a copy of the checklist of the project (see below) as the review's
  checklist, in
  the same transaction. Nothing is stored when a rule fails.

The first check is that the actor may work in the project (`IProjectAccess.RequireContributeAsync`): a project that
does not exist and a project the actor may not see are refused alike with `ProjectNotFound`, before the input is
examined, so a validation message never reveals a hidden project; a read-only member (an auditor) is refused with
`ActorNotAllowed`. Any member of the project who may work in it (and a system administrator) can create
reviews. The other rules are checked in this order, and the first violation is reported: the identifier
(`ReviewIdInvalid`);
the title (`ReviewTitleInvalid`); the files (`NoFiles`, `TooManyFiles`, `PathInvalid`, `DuplicatePath`,
`FileTooLarge`, `TotalSizeExceeded`); and that the identifier is
free in the project (`ReviewIdDuplicate`). Checks that need no I/O come first so that a bad request fails
cheaply. The hash of each file and the fingerprint are then calculated with `Fingerprint`, and the review is
handed to the store. The check for a duplicate identifier is repeated by the store's key constraint, which
covers two requests that pass the check at the same time.

**CreateFromRepositoryAsync**: Creates a draft review from the files that changed in a registered repository.

- *Parameters*: `int projectId`, `string? id`, `string? title`, a `RepositorySource source` (the `RepositoryId`, the
  `HeadRevision`, the `BaselineRevision` - null or blank: no baseline, every file at the head counts as added - and
  the `Selection`; the four values of the repository source that the new-review form and the API request carry
  together), an `Actor`, a cancellation token, an optional `DateOnly? dueDate`, an optional `string? followsReviewId`,
  and an optional `bool acknowledgeDroppedThreads`. A null `source` or `actor` raises
  `ArgumentNullException`.
- *Returns*: `Task<RepositoryReviewResult>`.
- *Postconditions*: The review is stored with the frozen copies described below, or nothing is stored.

The service is constructed with an optional `RepositoryService` and `IGitRepositoryProvider`; without them the
method throws `InvalidOperationException`. The first check is that the actor may work in the project
(`ProjectNotFound`, or `ActorNotAllowed` for a read-only member,
as for a draft). The checks that need no I/O come next (identifier, title, the head and
baseline revision names through `RevisionValidator`, the selection (a `null` selection means every changed file and
becomes `**/*`; empty or blank text is refused) through `SelectionMatcher.Parse` and its
length limit `ReviewLimits.MaxSelectionLength` of 2000 characters), then the free identifier, then
`RepositoryService.GetAccessAsync(projectId, repositoryId)` (which refuses a disabled repository, and an unknown
repository or one that belongs to another project, with `RepositoryNotFound`). Inside one git session the
service resolves both revisions to commits, lists the changes between them, keeps those whose path matches the
selection (a deleted file is matched by its path like any other), and orders them by path. When no change is left it
throws `NothingToReview` with the message `Nothing to review: no files matching the selection changed between X
and Y`, where X is the baseline (or `the beginning of the history`) and Y the head. Then it checks the file count
and, from the sizes the git layer reports and before any blob is read, the per-file limit (for the baseline and the
head side) and the total limit (both sides together); then it reads the bytes of both sides of every change. Nothing
reads the repository after that. `CreateFromRepositoryAsync` opens its own
session and then calls the internal `CreateFromSessionAsync`, which does everything from listing the changes to
storing the review; `ReviewPlanner` calls it directly with one session that it opened for a whole batch, so the
repository is fetched once for all the reviews of a plan. The session therefore stays open while the review is
stored.

The review stores for every file its path, kind, baseline bytes (none for an added file) and head bytes (none for a
deleted file), and on the review the repository identity and name, both commits, the normalized selection text, and
for a review planned from a review-set (only the planner passes one) the `ReviewSetId` (1 to 100 characters, else
`ReviewSetConfigInvalid`). The fingerprint is calculated with `Fingerprint` over the hashes of the files that exist
at the head (`Added` and `Modified`), so it equals the ReviewMark fingerprint of the head files of the selection. The
record digest (see `RecordDigest`) additionally covers the source, the review-set, and every change.

Skipped entries (symbolic links, submodules, and paths that cannot be reviewed) are not part of the review; those
that match the selection are returned in the result and counted in the audit detail.

**Checklist of the project**: both creation paths (`CreateDraftAsync` and `CreateFromSessionAsync`, which also serves
`CreateFromRepositoryAsync` and `ReviewPlanner`) take the checklist of the review in one place. The service reads the
project; when it has no definition of its own the copy is made from `DefaultChecklist.Current`, otherwise the stored
text is parsed and checked with `ChecklistValidator.ParseOrThrow` at that moment. A text that cannot be used fails
closed: the review is not created, nothing is stored, and the call raises `ChecklistDefinitionInvalid` with a message
that names the project and the first errors; the service never falls back to the default in that case. A project that
does not exist raises `ProjectNotFound`. The copy is stored with the review and read from there afterwards (see
`ChecklistService`), so a later change of the checklist of the project, an approval, or a lock changes neither an
existing review, nor its checklist wording, nor its record digest.

**GetAsync**: Reads one review with its files' metadata, ordered by path; returns null when it does not exist.

**Follow-up reviews**: `CreateDraftAsync` and `CreateFromRepositoryAsync` take an optional `followsReviewId` and
`CreateFromSessionAsync` takes the same two values through `SessionReviewRequest.FollowsReviewId` and
`SessionReviewRequest.AcknowledgeDroppedThreads`. The planner passes neither, so the planned review of a set is not a
follow-up (the planner option of a later slice will use the same two values). Blank text means no follow-up. The
actor needs no right beyond creating a review (`RequireContributeAsync`), because the earlier review is read from the
same project only. After the checks that need no I/O, and after the identifier and the checklist of the new review
are known (on the repository path also after the git session has read the files, because the files of the new review
must be known), the private `PrepareFollowUpAsync` does the following in order, and every failure is `FollowUpInvalid`
with nothing stored:

1. The earlier review is read with the project identity of the new review. A review that does not exist and a review
   of another project give the same message, "The review 'X' does not exist in this project and cannot be followed
   up.", so the message never tells whether the identifier exists elsewhere.
2. `FollowUpCarry.CheckFit` decides whether the two reviews belong together. When both have a `ReviewSetId` the sets
   must be equal ("another review-set"); otherwise both must be uploads or both from the same repository ("not the
   same kind of source", "another repository"), and, in every case, they must share at least one file path ("no file
   in common"). The state of the earlier review does not matter: a review that is in review and one that is approved
   (both are the one stored state `Draft`) can be followed, and so can a follow-up (a chain). The new review always
   points to its immediate source; what it carried from older reviews keeps the mark of the immediate source.
3. `IReviewStore.GetRecordAsync` reads the record of the earlier review and `FollowUpCarry.Compute` works out what is
   carried (below).
4. Open threads of the earlier review whose file is not in the new review are never carried. Unless
   `acknowledgeDroppedThreads` is true they refuse the creation with the message from `FollowUpCarry.DroppedMessage`,
   which lists the files (at most 10, then "and N more"). When it is true they are dropped and counted in the audit
   text. An acknowledgement without such threads changes nothing.

The review, the carried answers, the carried section states, and the carried threads with copies of their comments are
then handed to `IReviewStore.AddReviewAsync` in the one call that stores the review (through `Review.CarryOver`), so the
store saves them in one transaction with the audit entry and a failure creates nothing.

**FollowUpCarry** (a `public static` class of the ReviewService unit, with the record `ReviewCarryOver` for its result;
it is pure, reads no store and no clock, and is tested by itself). `Compute` takes the record of the earlier review,
its files, the new review's files and checklist, and the creation time, and returns:

- *Answers*: every Pass and N/A answer of the earlier review whose check identifier is in the checklist of the new
  review with the same text (an ordinal comparison; the earlier review's frozen checklist is used). Outcome,
  justification, answerer name, user name and time are kept, and the answer is marked `CarriedFromReviewId` with the
  earlier review. Fail answers are not carried (they are the reason for the follow-up and are answered again). When the
  earlier review has no checklist nothing is carried.
- *Section states*: every not-applicable section of the earlier review whose section identifier is in the new
  checklist with the same title, with its reason, setter and time, marked in the same way.
- *Threads*: in the order of the earlier threads. A resolved thread is counted (`ResolvedThreadsNotCarried`) and not
  carried. An open thread whose file is not in the new review goes to `DroppedThreadFiles` (sorted, distinct). Any
  other open thread is carried open with every comment verbatim (author, user name, text, time), its original
  creation time, `CarriedFromReviewId` and `CarriedFromThreadId` (the number it had there).
- *Anchors*: a thread keeps its anchor when it is anchored to the whole file, or when the file has the identical,
  non-empty hash on the side the thread is anchored to in both reviews (the head side compares `Sha256`, the baseline
  side compares `BaselineSha256`; a file that was not there counts as different). Otherwise the thread becomes a
  whole-file thread on the head side (the model has no "nearest line", and a wrong line would be worse than none) and a
  last comment is added by the author `ReviewCenter` with the user name `(system)`, dated with the creation of the new
  review, that says the thread was carried, that the file is not the same, and which lines on which side it was
  anchored to. The note is added after the original comments (not before them) so that the carried comments keep their
  order and the first comment stays the one that was written first.
- Reviewer slots, signatures, the reopenings, and the person who created the follow-up are never part of the carry:
  the carried answers keep the original answerer, so the creator does not become the answerer of anything, and the
  independence rule still looks at `Review.CreatedBy` only.

A carried answer counts as an answer (the progress of the checklist and `SigningService.Assess` need no change), and a
carried thread is an open thread, so it blocks approval until it is resolved. Answering a carried check again, or
setting a carried section again, stores the new value without the mark (see `ChecklistService`).

**Audit of a follow-up**: the detail of `review.created` gets `, follows 'X'; carried N answers, M threads` (after the
files, and on the repository path after the fingerprint; before the set, due date and token text), followed by
`, R resolved threads not carried` when R is greater than zero and `, open threads on F files dropped` when the creator
acknowledged a drop. `X` is the whole identifier of the earlier review (up to 100 characters, never shortened), so two
follow-ups of reviews whose identifiers share a prefix are told apart in the trail. A review that follows nothing has
exactly the detail as before. The title of a follow-up is cleaned to 100 characters for an upload (an upload that
follows nothing is unchanged), and to 30 characters fewer than before (30, or 10 with a token) for a repository, so
that the whole identifier always fits within the 500 characters of the audit detail; in the extreme case (a 100
character identifier together with a token, a 40 character repository name, a review-set and a due date) the audit
store cuts the end of the line, which is the review-set and the due date, and marks the cut with `...`. No new action
code is needed.

**Due date at creation**: `CreateDraftAsync`, `CreateFromRepositoryAsync`, and (through `SessionReviewRequest.DueDate`)
`CreateFromSessionAsync` check the optional date after the title and before the files and any I/O: a date before today
or more than two years ahead (`MaxDueDateYearsAhead`) is refused with `DueDateInvalid`, and nothing is stored. Today is
the date of the clock in UTC, so the same date means the same day to every user and to the server. The date is stored
with the review. When a date is set the detail of `review.created` ends with `, due yyyy-MM-dd` (after the set and the
token text); without a date the detail is exactly as before.

**ParseDueDate**: a static helper that reads the text of the API and of the pages (`yyyy-MM-dd` only); null or blank
text is no date, any other text raises `DueDateInvalid`.

**IsOverdue**: `IsOverdue(Review review, bool isApproved)` is true only when the review has a date that is before
today (UTC) and the review is not approved. The caller supplies `isApproved` (the review page takes it from
`SigningService`, the list endpoint of the API from `ListApprovedWithPastDueDateAsync`), so an approved review keeps
its date as history and is never overdue; the date `today` itself is
not overdue.

**ListApprovedWithPastDueDateAsync**: `ListApprovedWithPastDueDateAsync(int projectId, IEnumerable<Review> reviews,
CancellationToken)` is what a list uses to show the overdue state without reading every review in full. It picks the
reviews whose due date is before today (UTC), asks `IReviewStore.ListApprovedReviewIdsAsync` once for all of them, and
returns the identifiers that are approved; with no past date the store is asked about no review and answers at once.
The list endpoint of the API passes `isApproved` to `IsOverdue` from this set.

**CanSetDueDateAsync**: tells whether an actor may change the due date of a review: never for a missing actor or an
actor that came through a personal access token (a token does not change the settings of a review), and for a project
administrator or a system administrator (`IProjectAccess.CanAdministerAsync`) and for the user who created the review
(`Review.CreatedBy`, compared without regard to case, and only while that user can still see the project). Pages use it
to show or hide the form; `SetDueDateAsync` repeats it, so the pages are never the only guard.

**SetDueDateAsync**: `SetDueDateAsync(int projectId, string reviewId, DateOnly? dueDate, Actor actor, CancellationToken)`
sets, changes, or clears (`null`) the due date after creation and returns whether anything changed.

- *Preconditions*: the actor may see the project (`ProjectNotFound` otherwise, as for every action), the review exists
  (`ReviewNotFound`), and `CanSetDueDateAsync` holds (`ActorNotAllowed` otherwise).
- *Rules*: a date in the past is accepted (a lead may record that something was due last week) but a date more than
  two years ahead is `DueDateInvalid`; an approved review is refused with `ReviewLocked` and the message "reopen the
  review first" (the lock is the same `ReviewLock` check as for every other change, and the store repeats it in its
  transaction); a date that equals the stored one changes nothing and writes no entry.
- *Postconditions*: `IReviewStore.UpdateDueDateAsync` stores the date and the audit entry `review.due-date-changed`
  with the detail `<old|none> -> <new|none>` (each side `yyyy-MM-dd` or the word `none`) in one transaction, comparing
  the stored date with the one the service read; when the store reports a different stored date the service raises
  `RecordChanged`.

**ListAsync**: Lists the reviews of a project ordered by identifier, without files.

**GetFileContentAsync**: Reads the exact bytes of one file of a review by its path (the head content); returns null
when the review or file does not exist.

**GetBaselineContentAsync**: Reads the frozen baseline bytes of one file; returns null when the review or file does
not exist or the file has no baseline (an added file).

**GetDiffAsync**: Gives the line differences between the baseline and the head of one file of a review.

- *Parameters*: `int projectId`, `string reviewId`, `string path`, and a cancellation token.
- *Returns*: `Task<FileDiff?>` — null when the review or the file does not exist; otherwise a `FileDiff` whose
  `Status` is `Diff` (with the `LineDiffResult` and `OnlyLineEndingsDiffer`), `NoBaseline`, `Binary`, or `TooLarge`.
- *Postconditions*: Nothing is stored, and git is not opened: both contents come from the store, where they were
  frozen when the review was created, so the answer does not depend on the repository and is the same every time.

The status is decided in this order: `NoBaseline` when the review is not from a repository or has no baseline commit
(there is nothing to compare against); otherwise the head content (empty for a deleted file) and the baseline content
(empty for an added file) are split into lines with `FileText.TryReadLines`, and the status is `Binary` when either
side is binary; otherwise the lines are compared with `LineDiff.Compute`, and the status is `TooLarge` when the
comparison exceeds its bounds, else `Diff`. `OnlyLineEndingsDiffer` is true when the two sides have the same lines
but not the same bytes (line endings or the final line ending changed), so the page can say so instead of showing a
diff without changes. An added file is therefore all added rows and a deleted file all removed rows.

**Audit**: `CreateDraftAsync` takes the `Actor` and passes the draft `review.created` (detail with the title, the
fingerprint, and the number of files) to the store, which saves it in the same transaction as the review and its files.
`CreateFromRepositoryAsync` records `review.created` with the detail `review '{title}' from repository '{name}',
baseline {sha|none}, head {sha}, {n} file(s): {a} added, {m} modified, {d} deleted, {s} skipped, fingerprint {fp}`
followed, for a review planned from a review-set, by `, set '{id}'`; the title and the set identifier are shortened to
60 characters so the commits and the fingerprint always fit.
When the actor authenticated with a personal access token (`Actor.ViaApiToken`), the detail of `review.created` also
names the display id of the token: `, created via API token '{id}'` is added at the end of the detail of an uploaded
review, and right after the title in the detail of a review from a repository (where the title is shortened to 40
characters instead of 60, so the text still fits). A review created in the web pages reads exactly as before.
A refused creation records nothing. See the `AuditService` unit for the action codes.

#### Error Handling

Rule violations raise `DomainRuleException` with the matching `DomainRule`; a follow-up that does not fit, whose
source is unknown, or that would drop open threads without an acknowledgement raises `FollowUpInvalid`. A null `files`
argument, and a null
store or clock at construction, raise `ArgumentNullException`. Store failures propagate unchanged.

#### Dependencies

- **IReviewStore** — checks, stores, and reads reviews (injected).
- **IProjectAccess** — requires that the actor may work in the project before a review is created (injected).
- **TimeProvider** — supplies the creation time (injected).
- **Fingerprint** — calculates the file hashes and the review fingerprint.
- **RepositoryService** and **IGitRepositoryProvider** — resolve the access to a repository and open the git session
  (injected, optional; needed only for `CreateFromRepositoryAsync`).
- **FileText** and **LineDiff** — split the frozen contents into lines and compare them (for `GetDiffAsync`).
- **RevisionValidator** and **SelectionMatcher** — validate the revision names and match the selection.

#### Callers

- **ProjectPage** — creates reviews (uploaded or from a repository) and lists the reviews of a project.
- **ReviewPlanner** — creates the reviews of a plan through `CreateFromSessionAsync`, one session for the batch.
- **ReviewPage** — reads a review, the head and baseline content of a file, and (in the diff view) its differences.
- **ApiEndpoints** — lists and reads reviews, and creates reviews (uploaded or from a repository) for a token actor.
- **ServerHost** — registers the service for the pages.
