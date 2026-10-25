## Core

![Core Structure](CoreView.svg)

### Overview

The `Core` subsystem is the domain of ReviewCenter: projects, reviews of uploaded files, comment threads on
those files, the review checklist, local user accounts, reviewer slots with re-authenticated signatures, the rules
that protect them, and the content fingerprint that identifies a review's files and the digest of a review record. It
is
deliberately free of web and database technology so that the rules can be reviewed and tested on their own. It
contains eight units: `Fingerprint`, the content fingerprint algorithm; `RecordDigest`, the digest of a review
record that a signature binds to; `ProjectService`, the rules
for projects; `ReviewService`, the rules for reviews; `ThreadService`, the rules for review comments;
`ChecklistService`, the rules for the review checklist; `UserService`, the rules for users; and `SigningService`, the
rules for reviewer slots and signatures. The
domain types (`Project`, `Review`, `ReviewFile`, `ReviewState`, `UploadedFile`, `ReviewThread`, `ReviewComment`,
`ReviewThreadState`, the checklist types, `User`, `UserRole`, `Actor`, and the signing types `ReviewerSlot`,
`ReviewSignature`, `SlotRole`, `ReviewRecord`, and `ReviewApproval`), the text and line definition
(`FileText`), the limits
(`ReviewLimits`), the rule failure type
(`DomainRuleException` with the `DomainRule` enumeration), and the interfaces (`IReviewStore`, `IUserStore`, and
`IPasswordHasher`) are
supporting types, documented with the unit that owns them.

### Interfaces

**ProjectService, ReviewService, ThreadService, ChecklistService, UserService, and SigningService**: The operations
used by the `Web` and `Identity` subsystems and by the `Hosting` subsystem.

- *Type*: In-process .NET classes.
- *Role*: Provider.
- *Contract*: Create a project; list and read projects; create a draft review from uploaded files; list and
  read reviews; read the exact bytes of one file of a review; start a comment thread on a file or a line range,
  reply to it, resolve and reopen it, and list the threads of a review with their comments; read the frozen
  checklist of a review with its progress, set whether a section applies, and answer a check; create a user, check a
  user name and password, list and read users, enable and disable a user, set the role of a user, and change a
  password. Posting a comment or saving a checklist decision takes the signed-in user (an `Actor`), never free text.
  Add, change, and remove a reviewer slot of a review, sign a slot with the password entered again, read the
  approval status of a review (its slots, blockers, record digest, and next step), and list the users that can be
  chosen for a slot. `RecordDigest` calculates the digest of a record.
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
  update, and remove reviewer slots; and add and list signatures.
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
  state of a user.
- *Constraints*: An attempt to add a user whose name exists fails with a `DomainRuleException` for `UserNameTaken`.
  Every read returns a new object.

**IPasswordHasher**: The password hashing interface the subsystem requires.

- *Type*: In-process .NET interface.
- *Role*: Consumer (implemented by `PasswordHasher` in the `Identity` subsystem).
- *Contract*: Hash a password and verify a password against a hash.
- *Constraints*: The hash is not reversible and contains its own salt.

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

Draft is the only review state at present. The state is a closed enumeration and is stored as text so that
states can be added without renumbering. A comment thread is `Open` or `Resolved`, stored the same way. Comments
are immutable by design: a posted comment can neither be edited nor deleted, so a discussion stays a reliable
record. The only change to an existing thread is its state. The author of a comment is the signed-in user: the web
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

The checklist of a review is the copy of the default checklist that the review received when it was created, so
changing the default never changes an existing review (see `ChecklistService`). The name saved with an answer or
section decision is the display name of the signed-in user, saved together with the user name, like the author of a
comment. The earlier rules for a free text author or name no longer exist, because nobody types an author. The
checklist snapshot, section states, and answers are part of the record that a signature binds to, so they are read
together with the threads (see `RecordDigest`).

Signing is a second kind of record beside the discussion. A review has reviewer slots, each with a primary and an
optional alternate reviewer, and a slot is signed by one of the two after the password is entered again. A signature is
bound to the digest of the record (the fingerprint, the answers, the sections that do not apply, and the threads with
their comments) and is current only while that digest is unchanged. Slots and signatures are never edited into a
different meaning: a signed slot cannot be changed or removed, a signature is never changed or deleted, and the
status of a review (Approved or In review) is computed from the record and the signatures each time and never stored.
The tamper-evident (hash-chained) audit trail of the history is not part of the system yet.
