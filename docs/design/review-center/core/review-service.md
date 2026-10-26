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
characters); all six are null for an uploaded review.

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

- *Parameters*: `int projectId`, `string? id`, `string? title`, `IReadOnlyList<UploadedFile> files`, and a
  cancellation token.
- *Returns*: `Task<Review>` — the stored review with its fingerprint and the hash and size of each file.
- *Preconditions*: `files` is not null.
- *Postconditions*: The review is stored in the `Draft` state with the creation time from the clock; the bytes
  are stored exactly as given, together with a copy of `DefaultChecklist.Current` as the review's checklist, in
  the same transaction. Nothing is stored when a rule fails.

The rules are checked in this order, and the first violation is reported: the identifier (`ReviewIdInvalid`);
the title (`ReviewTitleInvalid`); the files (`NoFiles`, `TooManyFiles`, `PathInvalid`, `DuplicatePath`,
`FileTooLarge`, `TotalSizeExceeded`); that the project exists (`ProjectNotFound`); and that the identifier is
free in the project (`ReviewIdDuplicate`). Checks that need no I/O come first so that a bad request fails
cheaply. The hash of each file and the fingerprint are then calculated with `Fingerprint`, and the review is
handed to the store. The check for a duplicate identifier is repeated by the store's key constraint, which
covers two requests that pass the check at the same time.

**CreateFromRepositoryAsync**: Creates a draft review from the files that changed in a registered repository.

- *Parameters*: `int projectId`, `string? id`, `string? title`, `int repositoryId`, `string? headRevision`,
  `string? baselineRevision` (null or blank: no baseline, every file at the head counts as added), `string?
  selection`, an `Actor`, and a cancellation token.
- *Returns*: `Task<RepositoryReviewResult>`.
- *Postconditions*: The review is stored with the frozen copies described below, or nothing is stored.

The service is constructed with an optional `RepositoryService` and `IGitRepositoryProvider`; without them the
method throws `InvalidOperationException`. The checks that need no I/O come first (identifier, title, the head and
baseline revision names through `RevisionValidator`, the selection through `SelectionMatcher.Parse` and its
length limit `ReviewLimits.MaxSelectionLength` of 2000 characters), then the project and the free identifier, then
`RepositoryService.GetAccessAsync` (which refuses a disabled or unknown repository). Inside one git session the
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

**GetAsync**: Reads one review with its files' metadata, ordered by path; returns null when it does not exist.

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
A refused creation records nothing. See the `AuditService` unit for the action codes.

#### Error Handling

Rule violations raise `DomainRuleException` with the matching `DomainRule`. A null `files` argument, and a null
store or clock at construction, raise `ArgumentNullException`. Store failures propagate unchanged.

#### Dependencies

- **IReviewStore** — checks, stores, and reads reviews (injected).
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
- **ServerHost** — registers the service for the pages.
