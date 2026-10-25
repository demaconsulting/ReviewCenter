### ReviewService

![Core Structure](CoreView.svg)

#### Purpose

`ReviewService` owns the rules for reviews: it creates a draft review from uploaded files, computes each file's
hash and the review fingerprint, and reads reviews and file content back. It is also the home of the review
types: `Review`, `ReviewFile`, `ReviewState`, and `UploadedFile`.

#### Data Model

**Review**: a data class (init-only properties) of `ProjectId`, `Id` (the review identifier, unique within its project),
`Title`, `State`, `Created`, `ContentFingerprint`, `Files` (a list of `ReviewFile`), and `Checklist` (the frozen
checklist copy of the review, see `ChecklistService`; set when the review is created and not loaded by reads of
the review).

**ReviewFile**: a data class (init-only properties) of `ProjectId`, `ReviewId`, `Path`, `Content` (the bytes), `Size`, and
`Sha256`. Lists read back from storage carry the metadata but not the bytes.

**ReviewState**: a closed enumeration whose only value is `Draft`.

**UploadedFile**: the input record of a path and the exact bytes of one file.

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

**GetAsync**: Reads one review with its files' metadata, ordered by path; returns null when it does not exist.

**ListAsync**: Lists the reviews of a project ordered by identifier, without files.

**GetFileContentAsync**: Reads the exact bytes of one file of a review by its path; returns null when the
review or file does not exist.

**Audit**: `CreateDraftAsync` takes the `Actor` and passes the draft `review.created` (detail with the title, the
fingerprint, and the number of files) to the store, which saves it in the same transaction as the review and its files.
A refused creation records nothing. See the `AuditService` unit for the action codes.

#### Error Handling

Rule violations raise `DomainRuleException` with the matching `DomainRule`. A null `files` argument, and a null
store or clock at construction, raise `ArgumentNullException`. Store failures propagate unchanged.

#### Dependencies

- **IReviewStore** — checks, stores, and reads reviews (injected).
- **TimeProvider** — supplies the creation time (injected).
- **Fingerprint** — calculates the file hashes and the review fingerprint.

#### Callers

- **ProjectPage** — creates reviews and lists the reviews of a project.
- **ReviewPage** — reads a review and the content of a file.
- **ServerHost** — registers the service for the pages.
