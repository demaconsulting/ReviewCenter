## Core

![Core Structure](CoreView.svg)

### Overview

The `Core` subsystem is the domain of ReviewCenter: projects, reviews of uploaded files or of the changes in a git
repository, comment threads on
those files, the review checklist, local user accounts, reviewer slots with re-authenticated signatures, the rules
that protect them, the content fingerprint that identifies a review's files and the digest of a review record, and the
hash-chained audit trail of everything that happens. It
is
deliberately free of web and database technology so that the rules can be reviewed and tested on their own. It
contains fifteen units: `Fingerprint`, the content fingerprint algorithm; `RecordDigest`, the digest of a review
record that a signature binds to; `AuditChain`, the audit entry types, their hash, and the verifier of the chain;
`AuditService`, the recording, reading, and verification of the audit trail; `ProjectService`, the rules
for projects; `ReviewService`, the rules for reviews; `ThreadService`, the rules for review comments;
`ChecklistService`, the rules for the review checklist; `UserService`, the rules for users; `SigningService`, the
rules for reviewer slots and signatures; `RepositoryService`, the registry of git repositories that reviews can be
created from, with their credentials; `SelectionMatcher`, the file pattern language that selects the files of a
repository; `RevisionValidator`, the check that a revision name cannot be taken for an option or an expression;
`LineDiff`, the comparison of the lines of two versions of a file and its division into shown and collapsed regions;
and `ReviewPlanner`, the planning of the reviews that a head revision needs from the review-sets of the repository. The
domain types (`Project`, `Review`, `ReviewFile`, `ReviewState`, `UploadedFile`, `ReviewThread`, `ThreadSide`,
`ReviewComment`,
`ReviewThreadState`, the checklist types, `User`, `UserRole`, `Actor`, the signing types `ReviewerSlot`,
`ReviewSignature`, `SlotRole`, `ReviewRecord`, and `ReviewApproval`, the audit types `AuditEntry`, `AuditDraft`,
and `AuditVerificationResult`, and the repository types `SourceRepository`, `RepositorySummary`, `NewRepository`,
`RepositoryUpdate`, `TokenChange`, and `TokenAction`, and the git contract types `RepositoryAccess`, `GitChange`,
`ChangeKind`, `SkippedEntry`, and `GitChangeSet`, and the review-source types `ReviewSource`, `ChangedFile`, and
`RepositoryReviewResult`, and the diff types `FileDiff`, `FileDiffStatus`, `DiffLine`, `DiffLineKind`,
`LineDiffResult`, `DiffSegment`, and `DiffRow`, and the planning types `BaselineMode`, `PlanRowStatus`, `PlanRow`, `ReviewPlan`,
`PlanCreateRequest`, `PlanCreateOutcome`, `PlanCreateResult`, and `ReviewSetDefinition`), the text and line definition
(`FileText`), the limits
(`ReviewLimits`), the rule failure type
(`DomainRuleException` with the `DomainRule` enumeration), and the interfaces (`IReviewStore`, `IUserStore`,
`IAuditStore`, `IRepositoryStore`, `ICredentialProtector`, `IGitRepositoryProvider`, `IGitRepository`,
`IReviewSetParser`, and `IPasswordHasher`) are
supporting types, documented with the unit that owns them.

### Interfaces

**ProjectService, ReviewService, ThreadService, ChecklistService, UserService, and SigningService**: The operations
used by the `Web` and `Identity` subsystems and by the `Hosting` subsystem.

- *Type*: In-process .NET classes.
- *Role*: Provider.
- *Contract*: Create a project; list and read projects; create a draft review from uploaded files, or from the files
  that changed between an optional baseline revision and a head revision of a registered repository, restricted by a
  selection (the changes are fetched once and frozen in the review; nothing reads the repository again, and a selection
  that matches no change is refused with `NothingToReview`); list and
  read reviews; read the exact head bytes, and the frozen baseline bytes, of one file of a review; start a comment
  thread on a file or a line range of the head or, for a file with a baseline, of the baseline (a file deleted at
  the head takes whole-file comments and baseline line comments only), read the line differences between the
  baseline and the head of one file from the frozen contents (within fixed bounds on the size of the comparison),
  reply to it, resolve and reopen it, and list the threads of a review with their comments; read the frozen
  checklist of a review with its progress, set whether a section applies, and answer a check; create a user, check a
  user name and password, list and read users, enable and disable a user, set the role of a user, and change a
  password. Posting a comment or saving a checklist decision takes the signed-in user (an `Actor`), never free text.
  Add, change, and remove a reviewer slot of a review, sign a slot with the password entered again, read the
  approval status of a review (its slots, blockers, record digest, and next step), and list the users that can be
  chosen for a slot. `RecordDigest` calculates the digest of a record. Every operation that changes data takes
  the `Actor` and records the change in the audit trail; `AuditService` lists the trail newest first in pages
  and verifies its hash chain.
  Posted comments are
  immutable: there is no operation to edit or delete one. Rule violations are reported as a
  `DomainRuleException` that carries a `DomainRule` value and a message suitable for display to the user.
- *Constraints*: The services hold no state that matters between calls and are safe for concurrent use if the stores
  are; `UserService` serializes the operations that change users within the process.

**IReviewStore**: The store interface the subsystem requires for projects and reviews.

- *Type*: In-process .NET interface.
- *Role*: Consumer (implemented by `ReviewStore` in the `Storage` subsystem).
- *Contract*: Add and read projects; check, add, read, and list reviews; read one file's content; add a thread with
  its first comment; add a comment to a thread of a review; set the state of a thread; list the threads of a review
  with their comments; read the checklist snapshot, the section states, and the answers of a review; set the
  state of a section and the answer to a check; read the whole record of a review in one operation; add, list,
  update, and remove reviewer slots; and add and list signatures. Every operation that changes data takes an
  `AuditDraft` (or a function that builds one from the stored entity) that the store appends to the audit trail in the
  same transaction.
- *Constraints*: A review is added with all its files, and its checklist snapshot, in one operation, and an attempt
  to add a review whose
  identifier already exists in the project fails with a `DomainRuleException` for `ReviewIdDuplicate`. A thread is
  added with its first comment in one operation. Setting a section state or an answer replaces the earlier one.
  There is no operation to change or delete a signature. A signature is added only if the digest of the record is
  still the digest it carries, checked inside one transaction, and fails with a `DomainRuleException` for
  `RecordChanged` otherwise.

**IUserStore**: The store interface the subsystem requires for users.

- *Type*: In-process .NET interface.
- *Role*: Consumer (implemented by `UserStore` in the `Storage` subsystem).
- *Contract*: Add a user, read a user by user name without regard to case, list the users, and replace the stored
  state of a user. Adding a user, and an update that carries a draft, append that audit entry in the same transaction.
- *Constraints*: An attempt to add a user whose name exists fails with a `DomainRuleException` for `UserNameTaken`.
  Every read returns a new object.

**IAuditStore**: The store interface the subsystem requires for the audit trail.

- *Type*: In-process .NET interface.
- *Role*: Consumer (implemented by `AuditStore` in the `Storage` subsystem).
- *Contract*: Append one entry from a draft, list entries newest first (optionally of one review, before or after a
  sequence), and read entries in ascending batches after a sequence.
- *Constraints*: The interface has no operation that updates or deletes an entry. The entries of data changes are not
  appended through it: the review and user store operations take an `AuditDraft` and append it in the transaction of the
  change.

**IPasswordHasher**: The password hashing interface the subsystem requires.

- *Type*: In-process .NET interface.
- *Role*: Consumer (implemented by `PasswordHasher` in the `Identity` subsystem).
- *Contract*: Hash a password and verify a password against a hash.
- *Constraints*: The hash is not reversible and contains its own salt.

**RepositoryService**: The operations used by the `Web` subsystem to administer the registry of git repositories, and
by the `Hosting` subsystem to hand a registered repository to the git source.

- *Type*: In-process .NET class.
- *Role*: Provider.
- *Contract*: An administrator registers a repository (name, location, optional user name and token), changes its
  name, location, user name, or token, and enables or disables it; administrators list all repositories; any
  signed-in user lists the enabled ones by name. Every change takes the `Actor`, checks again that the actor is an
  enabled administrator, and is recorded in the audit trail without any secret. The access data needed to read a
  repository, including the decrypted token, is available only inside the assembly.
- *Constraints*: The token is write-only: no operation returns it, and the read model has no member for it.

**IRepositoryStore**: The store interface the subsystem requires for registered repositories.

- *Type*: In-process .NET interface.
- *Role*: Consumer (implemented by `RepositoryStore` in the `Storage` subsystem).
- *Contract*: Add a repository, read one, list all, read the encrypted token of one, and update one with a token
  action (keep, replace, or clear). Every change takes an `AuditDraft` (or a function that builds one from the stored
  entity) that the store appends to the audit trail in the same transaction.
- *Constraints*: A name that exists, without regard to case, fails with a `DomainRuleException` for
  `RepositoryNameTaken`. The store sees only encrypted token text.

**ICredentialProtector**: The token protection interface the subsystem requires.

- *Type*: In-process .NET interface.
- *Role*: Consumer (implemented by `CredentialProtector` in the `ContentSources` subsystem).
- *Contract*: Protect a token into text that is safe to store and unprotect that text again.
- *Constraints*: Text that cannot be decrypted fails with a `DomainRuleException` for `CredentialsUnreadable`.

**IGitRepositoryProvider and IGitRepository**: The git access interfaces the subsystem defines.

- *Type*: In-process .NET interfaces.
- *Role*: Consumer (implemented by `GitSource` in the `ContentSources` subsystem).
- *Contract*: Open a registered repository from a `RepositoryAccess` to get a session; the session resolves a revision
  name to a commit id, lists the files that changed between two commits (or all files when there is no baseline),
  reads the size of a blob, reads a blob with a byte limit, and tells whether one commit is an ancestor of another.
  Disposing the session releases the repository.
- *Constraints*: A session never exposes the token, and only commit and blob ids that are valid hexadecimal object
  names are accepted.

**SelectionMatcher and RevisionValidator**: Pure functions for the file patterns and the revision names that the
review creation from a repository uses.

- *Type*: In-process static .NET classes.
- *Role*: Provider.
- *Contract*: Parse the text of a file selection into patterns and decide whether a path is selected; check a revision
  name.
- *Constraints*: Invalid input fails with a `DomainRuleException` for `SelectionInvalid` or `RevisionInvalid`.

**ReviewPlanner**: The operations used by the `Web` subsystem to plan and create the reviews that a head revision of
a registered repository needs.

- *Type*: In-process .NET class.
- *Role*: Provider.
- *Contract*: Plan the reviews for a repository, a head revision, and a baseline choice (none, a given revision, or the
  last signed review of each set): one row for each review-set of the configuration file of the repository, as a full
  review or a change since a baseline, already filed, with nothing to review, or one that cannot be planned, with a
  plain note when
  a fallback was used. Create the picked rows through `ReviewService`, each in its own transaction, and report the
  outcome of each. Creating takes the `Actor` and records every review in the audit trail like any other review.
- *Constraints*: A missing or invalid configuration file fails with a `DomainRuleException` for
  `ReviewSetConfigMissing` or `ReviewSetConfigInvalid`; a problem with one set affects only its row.

**IReviewSetParser**: The configuration reading interface the subsystem requires.

- *Type*: In-process .NET interface.
- *Role*: Consumer (implemented by `ReviewMarkConfigParser` in the `ContentSources` subsystem).
- *Contract*: Read the text of a `.reviewmark.yaml` file into review-set definitions (identifier, title, and file
  patterns).
- *Constraints*: Text that cannot be read fails with a `DomainRuleException` for `ReviewSetConfigInvalid`.

**ReviewLimits**: The limits of the system as constants (see the `ProjectService` and `UserService` units).

- *Type*: In-process .NET constants.
- *Role*: Provider (to the `Web` and `Storage` subsystems).

### Design

The subsystem depends on nothing but the .NET runtime library. It is compiled as a separate class
library, `DemaConsulting.ReviewCenter.Core`, rather than as a folder of the main project, so that the compiler
prevents it from referencing the web framework or the database library. This is a deliberate deviation from the
folder layout rule described in the introduction; the other artifacts of the subsystem follow the normal layout.
The library is compiled into the single executable of the system and makes no difference to packaging.

The services depend on the store interfaces, `IPasswordHasher`, and a `TimeProvider`, all injected, so tests supply
fake stores and a fixed clock. A review is created in one pass: the services validate everything that
needs no I/O, check that the project exists and the review identifier is free, compute the hash of each
file and the fingerprint of the set, and then hand the complete review to the store. Nothing is
stored when a rule fails. The bytes of each file are kept exactly as uploaded; no encoding or
line-ending normalization is applied, since that would change the fingerprint.

A review from a repository is created in the same pass, with the repository access in the middle: after the checks that
need no I/O, the service opens the repository through `IGitRepositoryProvider`, resolves both revisions to commits,
lists the changes, keeps those that match the selection, checks the limits from the reported sizes, reads both sides of
each file, and closes the session; only then is the review stored. The review freezes the repository, both commits,
the selection, and for every file its path, change kind, and baseline and head bytes. A deleted file has no head
content, so it has empty head content, and it is left out of the fingerprint, which therefore equals the ReviewMark
fingerprint of the files that exist at the head. The record digest covers the source and every change, so a signature
binds to what the reviewer was shown.

Draft is the only review state at present. The state is a closed enumeration and is stored as text so that
states can be added without renumbering. A comment thread is `Open` or `Resolved`, stored the same way. Comments
are immutable by design: a posted comment can neither be edited nor deleted, so a discussion stays a reliable
record. The only change to an existing thread is its state. A thread has a side: `Head` for the version under review,
or `Baseline` for the version before the change, so that a reviewer can comment on a removed line; the line numbers of
a thread belong to its side, and a whole-file thread is always `Head`. The author of a comment is the signed-in user:
the web
layer passes an `Actor` (user name and display name) obtained from the authentication state, so the author cannot be
typed or forged on a page. The comment keeps both, the display name as it was at that time and the user name, so the
record stays readable when a display name changes later.

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
| `CommentTextInvalid` | The comment text is empty after trimming or is too long |
| `ThreadAnchorInvalid` | The file is not in the review, or the line range does not fit the file (see ThreadService) |
| `ThreadNotFound` | A reply, resolve, or reopen names a thread that does not exist in the review |
| `ChecklistNotFound` | An answer or section decision is made for a review that does not exist or has no checklist |
| `ChecklistItemNotFound` | The section or check is not in the review's frozen checklist |
| `ChecklistJustificationInvalid` | A justification (Fail, N/A) or section reason is empty after trimming, or too long |
| `UserNameInvalid` | The user name is not 3 to 50 characters of `A-Z`, `a-z`, `0-9`, `.`, `_`, `-` |
| `UserNameTaken` | A user with that name (without regard to case) exists |
| `DisplayNameInvalid` | The display name is empty after trimming or is too long |
| `PasswordInvalid` | The password is shorter than 12 or longer than 128 characters |
| `CurrentPasswordIncorrect` | The current password given when changing a password is wrong, or the user is locked out |
| `UserNotFound` | A change names a user that does not exist |
| `LastAdmin` | A change would disable or demote the last enabled administrator |
| `ReviewNotFound` | A slot or signature names a review that does not exist in the project |
| `SlotNotFound` | A slot change or signature names a slot that is not part of the review |
| `SlotUserInvalid` | A reviewer is not an enabled user, primary and alternate are the same, or a user is in two slots |
| `SlotSigned` | A slot that has any signature is changed or removed |
| `ActorNotAllowed` | The acting user is gone or disabled, or signs a slot of which they are not a reviewer |
| `SigningNotAllowed` | The slot is already signed for this record, or a blocker remains (checks, failures, threads) |
| `ReauthenticationFailed` | The password given with a signature is wrong, or the user is locked out |
| `RecordChanged` | The record changed after the signer saw it, so the digest differs |
| `RepositoryNameInvalid` | The name is not 1 to 40 of letters, digits, `.`, `_`, `-`, starting with a letter or digit |
| `RepositoryNameTaken` | A repository with that name (without regard to case) exists |
| `RepositoryLocationInvalid` | Location is empty, too long, or not an `https`, local `http`, or `file` URL or path |
| `RepositoryCredentialInvalid` | Invalid credential, non-`https` location, or token contained in name/user/location |
| `RepositoryNotFound` | A change or an access names a repository that does not exist |
| `RepositoryDisabled` | A disabled repository is opened for reading |
| `RevisionInvalid` | A revision name has a form that could be an option or an expression (see RevisionValidator) |
| `RevisionNotFound` | The revision does not exist in the repository |
| `SelectionInvalid` | The file selection is empty, has too many or too long lines, or has a pattern that is refused |
| `NothingToReview` | No file matching the selection changed between the baseline and the head |
| `GitUnavailable` | The server cannot run git, or the installed git is too old |
| `GitAccessFailed` | Git cannot read the repository (denied, missing, over a size limit, or another failure) |
| `GitTimeout` | A git operation did not finish within the configured time and was stopped |
| `CredentialsUnreadable` | A stored token cannot be decrypted, for example after the key ring changed |

The checklist of a review is the copy of the default checklist that the review received when it was created, so
changing the default never changes an existing review (see `ChecklistService`). The name saved with an answer or
section decision is the display name of the signed-in user, saved together with the user name, like the author of a
comment. The earlier rules for a free text author or name no longer exist, because nobody types an author. The
checklist snapshot, section states, and answers are part of the record that a signature binds to, so they are read
together with the threads (see `RecordDigest`).

Signing is a second kind of record beside the discussion. A review has reviewer slots, each with a primary and an
optional alternate reviewer, and a slot is signed by one of the two after the password is entered again. A signature is
bound to the digest of the record (the fingerprint, the source of the files with every change, the answers, the sections
that do not apply, and the threads with their comments) and is current only while that digest is unchanged. Slots
and signatures are never edited into a
different meaning: a signed slot cannot be changed or removed, a signature is never changed or deleted, and the
status of a review (Approved or In review) is computed from the record and the signatures each time and never stored.

The audit trail is the third kind of record: an append-only, hash-chained list of everything that happens, kept in the
same database. A change of data and its audit entry are stored in the same transaction by the store, so the services
decide what is recorded (they build an `AuditDraft` with the actor, the action code, and a one-line detail) and the stores
decide when. Events that change no data (sign-in, sign-out, failed sign-in, lockout) are recorded by `AuditService` on a
best-effort basis. The chain detects an edit, a deletion, an insertion, or a reordering of entries in the middle of the
history. It does not detect the removal of the newest entries, or a complete rewrite by someone with write access to
the database, unless the head hash was recorded elsewhere, and this version has no external anchoring. See the
`AuditChain` and `AuditService` units for the encoding, the verification, the action codes, and the known limitations.

The registry of git repositories is a fourth kind of record, and the first that does not describe a review. A
repository is registered by an administrator with a location and, for a private `https` repository, a user name and an
access token. The token is secret, so the subsystem treats it differently from every other field: `RepositoryService`
encrypts it through `ICredentialProtector` before it reaches the store, the read model has no member that could carry
it, and only an internal operation of the service decrypts it, at the moment a repository is opened. Its audit entry
says only whether a token is set or was replaced or cleared. The reading of the repository itself is behind the
`IGitRepositoryProvider` contract, implemented in the `ContentSources` subsystem, so the rules stay free of processes
and of the file system. Repositories are never deleted, only disabled, so the name of a repository that reviews were
created from stays meaningful.
