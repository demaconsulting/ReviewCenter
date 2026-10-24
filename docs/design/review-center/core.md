## Core

![Core Structure](CoreView.svg)

### Overview

The `Core` subsystem is the domain of ReviewCenter: projects, reviews of uploaded files, comment threads on
those files, the rules that protect them, and the content fingerprint that identifies a review's files. It is
deliberately free of web and database technology so that the rules can be reviewed and tested on their own. It
contains four units: `Fingerprint`, the content fingerprint algorithm; `ProjectService`, the rules
for projects; `ReviewService`, the rules for reviews; and `ThreadService`, the rules for review comments. The
domain types (`Project`, `Review`, `ReviewFile`, `ReviewState`, `UploadedFile`, `ReviewThread`, `ReviewComment`,
`ReviewThreadState`), the text and line definition (`FileText`), the limits (`ReviewLimits`), the rule failure type
(`DomainRuleException` with the `DomainRule` enumeration), and the store interface (`IReviewStore`) are
supporting types, documented with the unit that owns them.

### Interfaces

**ProjectService, ReviewService, and ThreadService**: The operations used by the `Web` subsystem.

- *Type*: In-process .NET classes.
- *Role*: Provider.
- *Contract*: Create a project; list and read projects; create a draft review from uploaded files; list and
  read reviews; read the exact bytes of one file of a review; start a comment thread on a file or a line range,
  reply to it, resolve and reopen it, and list the threads of a review with their comments. Posted comments are
  immutable: there is no operation to edit or delete one. Rule violations are reported as a
  `DomainRuleException` that carries a `DomainRule` value and a message suitable for display to the user.
- *Constraints*: The services hold no state and are safe for concurrent use if the store is.

**IReviewStore**: The store interface the subsystem requires.

- *Type*: In-process .NET interface.
- *Role*: Consumer (implemented by `ReviewStore` in the `Storage` subsystem).
- *Contract*: Add and read projects; check, add, read, and list reviews; read one file's content; add a thread with
  its first comment; add a comment to a thread of a review; set the state of a thread; list the threads of a review
  with their comments.
- *Constraints*: A review is added with all its files in one operation, and an attempt to add a review whose
  identifier already exists in the project fails with a `DomainRuleException` for `ReviewIdDuplicate`. A thread is
  added with its first comment in one operation.

**ReviewLimits**: The limits of the system as constants (see the `ProjectService` unit).

- *Type*: In-process .NET constants.
- *Role*: Provider (to the `Web` and `Storage` subsystems).

### Design

The subsystem depends on nothing but the .NET runtime library. It is compiled as a separate class
library, `DemaConsulting.ReviewCenter.Core`, rather than as a folder of the main project, so that the compiler
prevents it from referencing the web framework or the database library. This is a deliberate deviation from the
folder layout rule described in the introduction; the other artifacts of the subsystem follow the normal layout.
The library is compiled into the single executable of the system and makes no difference to packaging.

The services depend on `IReviewStore` and on a `TimeProvider`, both injected, so tests supply a
fake store and a fixed clock. A review is created in one pass: the services validate everything that
needs no I/O, check that the project exists and the review identifier is free, compute the hash of each
file and the fingerprint of the set, and then hand the complete review to the store. Nothing is
stored when a rule fails. The bytes of each file are kept exactly as uploaded; no encoding or
line-ending normalization is applied, since that would change the fingerprint.

Draft is the only review state at present. The state is a closed enumeration and is stored as text so that
states can be added without renumbering. A comment thread is `Open` or `Resolved`, stored the same way. Comments
are immutable by design: a posted comment can neither be edited nor deleted, so a discussion stays a reliable
record. The only change to an existing thread is its state. The comment author is free text, because there is no
sign-in yet; it is not verified and will be replaced by the signed-in user when identity is added.

Rule failures are reported as `DomainRuleException` rather than as return codes, because the pages show the
message and keep the entered values, and there is a single, easily tested path for all rules. The rules are:

| `DomainRule` | Violated when |
| -------------- | --------------- |
| `ProjectNameInvalid` | The project name is empty after trimming or is too long |
| `ProjectDescriptionTooLong` | The project description is too long |
| `ProjectNotFound` | A review is created in a project that does not exist |
| `ReviewIdInvalid` | The review id is empty, too long, or contains other than letters, digits, `.`, `_`, `-` |
| `ReviewIdDuplicate` | The review id is already used in that project |
| `ReviewTitleInvalid` | The review title is empty after trimming or is too long |
| `NoFiles` | A review has no files |
| `TooManyFiles` | A review has more files than the limit |
| `PathInvalid` | A file path is empty or too long |
| `DuplicatePath` | Two files have the same path (ordinal comparison) |
| `FileTooLarge` | A file is larger than the per-file limit |
| `TotalSizeExceeded` | The files together are larger than the total limit |
| `CommentAuthorInvalid` | The comment author is empty after trimming or is too long |
| `CommentTextInvalid` | The comment text is empty after trimming or is too long |
| `ThreadAnchorInvalid` | The file is not in the review, or the line range does not fit the file (see ThreadService) |
| `ThreadNotFound` | A reply, resolve, or reopen names a thread that does not exist in the review |
