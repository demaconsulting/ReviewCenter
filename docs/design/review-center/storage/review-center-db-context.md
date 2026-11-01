### ReviewCenterDbContext

![Storage Structure](StorageView.svg)

#### Purpose

`ReviewCenterDbContext` is the Entity Framework Core context that maps the Core domain types to database tables.
Its single responsibility is to define the schema: the tables, keys, relationships, column limits, and
conversions. It contains no queries; those belong to `ReviewStore`.

#### Data Model

The context exposes nineteen sets and maps the Core types directly, without separate persistence classes (the
repository registry is the one exception, see `Repositories`).

- **Projects** (`Project`): key `Id`, assigned by the database. `Name` is required, at most 100 characters;
  `Description` is required (may be empty), at most 1000; `Created`. The evidence settings of the project
  (`Project.Evidence`, an `EvidenceSettings` value) are a complex property mapped to seven boolean columns of the same
  table, `EvidenceAnonymousIndex`, `EvidenceAnonymousAttestations`, `EvidenceIncludeThreads`,
  `EvidenceIncludeReviewerNames`, `EvidenceIncludeChecklistWording`, `EvidenceIncludeFiles`, and
  `EvidenceIncludeGitContext`. The column defaults are the default settings: the two anonymous switches are false and
  the five content switches are true, so a project that was never given other settings reads back with
  `EvidenceSettings.Default`. `RequireIndependentReviewers` is a required boolean column of the same table. No
  database default is declared for it: the value always comes from the entity, whose initializer is `true`, so a
  project that was never given another value requires independent reviewers (this avoids the pitfall where Entity
  Framework treats the CLR default of a boolean as "not set" and substitutes the database default). The two further
  review rules, `RequireNonAuthorChecklistAnswers` and `RequireNonReopenerSigning`, are required boolean columns of the
  same table that are declared the same way (no database default; the entity initializer is `true`). `ChecklistDefinition`
  is a nullable text column with no length limit: the JSON of the checklist the project gives to its new reviews (a
  definition is up to `ReviewLimits.MaxChecklistBytes`, 256 KiB), and `NULL` stands for the built-in default checklist.
  There is no version column; the version label is part of the JSON.
- **Reviews** (`Review`): key `ProjectId` plus `Id`. `Title` is required, at most 200; `State` is stored as text, at
  most 32; `ContentFingerprint` is required, at most 64; `Created`; `CreatedBy` (the user name of the creator, at most
  the user name limit, null for a review stored before the column existed) and `CreatedViaApi` (default false, true
  for a review created with a personal access token) and `DueDate` (the optional due date, a calendar day stored as
  text in the form `yyyy-MM-dd`, null for a review without one; it is not part of the record digest) and
  `ReviewConclusionText` (the optional free-text conclusion of the review, at most 4000 characters, null for none; it
  is part of the record digest and is not the approved or rejected conclusion). `CreatedBy` has
  the case-insensitive collation `NOCASE` and an index, so the inbox finds the reviews of a creator without regard to
  case and without a scan. A review from
  a repository also has
  `RepositoryId` (a plain number, not a foreign key, so the review keeps its reference whatever happens to the
  registration), `RepositoryName` (a display copy, at most the repository name limit), `BaselineSha` and `HeadSha` (at
  most 64 characters), `Selection` (at most `ReviewLimits.MaxSelectionLength`), and `ReviewSetId` (the review-set
  that a planned review was made for, at most `ReviewLimits.MaxReviewSetIdLength`, 100 characters); all six are null
  for an uploaded review, and `ReviewSetId` is also null for a review made by hand. An index on `ProjectId`,
  `RepositoryId`, and `ReviewSetId` serves the planner's search for the last review of a set. A review that was
  created as a follow-up also has `FollowsReviewId` (the earlier review it follows, at most the review identifier
  limit, 100; a plain column, not a foreign key, and null otherwise), `CarriedAnswerCount` and `CarriedThreadCount`
  (numbers, 0 otherwise); an index on `ProjectId` and `FollowsReviewId` serves the search for the reviews that follow
  a review. `Review.CarryOver` (the rows to write with a new review) is not mapped. A review belongs to a
  project and is deleted with it.
- **ReviewFiles** (`ReviewFile`): key `ProjectId`, `ReviewId`, and `Path`. `Content` is required and stored as a
  binary column (empty for a deleted file); `Sha256` is required, at most 64; `Size`. `ChangeKind` is stored as text,
  at most 32 (an uploaded file is `Added`); `BaselineContent` (binary), `BaselineSize`, and `BaselineSha256` (at most
  64) are null when the file has no baseline. A file belongs to a review and is deleted with it.
- **Threads** (`ReviewThread`): key `Id`, assigned by the database. `ProjectId`, `ReviewId`, and `FilePath` name
  the file the thread is about and form a relationship to that file, so the database refuses a thread for a file
  that does not exist, and a thread is deleted with its file. `Side` is stored as text (`Head` or `Baseline`), at most
  32, with `Head` as the default, so a row written without a side reads as a head thread. `StartLine` and `EndLine`
  are null for a thread on the whole file. `State` is stored as text, at most 32; `Created`. A thread carried into a
  follow-up review also has `CarriedFromReviewId` (at most 100) and `CarriedFromThreadId` (a number), plain columns
  without a relationship, null for any other thread.
- **Comments** (`ReviewComment`): key `Id`, assigned by the database, so it grows with the posting order.
  `ThreadId` relates the comment to its thread, and a comment is deleted with its thread. `Author` (the display name
  at the time) is required, at most 100 characters; `AuthorUsername` (the user name) is required, at most 50;
  `Text` is required, at most 4000; `Created`; `IncludeInReport` (a boolean, default false: chat; true puts the comment
  in the report that the record digest covers); `Kind`, stored as text of at most 32 with the default `Comment` (the
  other value is `Resolution`). The kind of a thread is derived from its starting comment and is not stored.
- **ReviewChecklists** (`ReviewChecklist`): key `ProjectId` plus `ReviewId`, one frozen snapshot per review.
  `Version` is required, at most 64; `Definition` is required text (the JSON of the checklist). It belongs to its
  review (a one-to-one relationship, so the snapshot is saved with the review) and is deleted with it.
- **ChecklistSectionStates** (`ChecklistSectionState`): key `ProjectId`, `ReviewId`, `SlotId`, and `SectionId` (at most
  32), so a review has at most one state per section and slot. `SlotId` is the identity of a reviewer slot for the rows
  of its ballot (the record is built from `ReviewRecord.Ballots`), or 0 (`ChecklistAnswer.BaselineSlotId`) for the
  baseline that a follow-up review carries: such a row belongs to no ballot and is no part of the record or the
  digest. It is a plain column without a foreign key, so the store deletes the rows of a removed slot itself. `Applicable`;
  `Reason` at most 2000; `SetBy` (the display name) is
  required, at most 100; `SetByUsername` is required, at most 50; `SetAt`; `CarriedFromReviewId` (at most 100, null
  unless the state was carried into a follow-up review). It belongs to a review and is deleted
  with it.
- **ChecklistAnswers** (`ChecklistAnswer`): key `ProjectId`, `ReviewId`, `SlotId`, and `ItemId` (at most 32), so a check
  has at most one answer per slot (`SlotId` as for the section states). `Outcome` is stored as text, at most 32;
  `Justification` at most 2000; `AnsweredBy` (the display
  name) is required, at most 100; `AnsweredByUsername` is required, at most 50; `AnsweredAt`; `CarriedFromReviewId`
  (at most 100, null unless the answer was carried into a follow-up review). It belongs to a review
  and is deleted with it.
- **ChecklistBallots** (`ChecklistBallot`): key `ProjectId`, `ReviewId`, and `SlotId`, one status row for each reviewer
  slot that started a ballot (no row means not started; slot 0 never has one). `Status` is stored as text, at most 32
  (`Draft` or `Submitted`); `SubmittedAt` and `SubmittedByUsername` (at most 50) are set for a submitted ballot and null
  for a draft. The row is deleted with its review and with its slot (cascade), and an index on `SlotId` serves the
  cascade.
- **Users** (`User`): key `Id`, assigned by the database. `Username` is required, at most 50, with the
  case-insensitive collation of SQLite and a unique index, so a user name is unique without regard to case and a
  lookup by user name
  ignores case; `DisplayName` is required, at most 100; `IsSystemAdmin` (false for an ordinary user); `Enabled`;
  `PasswordHash` is required; `FailedLoginCount`; `LockoutUntil` (null when the user is not locked out);
  `MustChangePassword` (true for an account created with a temporary password until the user changes it; false for
  every other account); `LastSignInUtc` (null until the user has signed in); `Created`; `ExternalIssuer` (at most 512)
  and `ExternalSubject` (at most 256), both null for a
  local account and set together for the account of an external identity provider, with the default binary collation
  so that they compare exactly. A unique index on the pair makes one account per identity; SQLite treats nulls as
  distinct, so any number of local accounts is allowed.
- **ProjectMemberships** (`ProjectMembership`): key `UserId` plus `ProjectId`, so a user has at most one membership
  per project and the database refuses a second. Both columns are foreign keys, to `Users` and to `Projects`, and a
  membership is deleted with its user or its project. `Role` (the project role, `ProjectAdmin` or `User`) is stored as
  text, at most 32. An index on `ProjectId` serves the list of the members of a project.
- **PersonalAccessTokens** (`PersonalAccessToken`): key `Id`, assigned by the database. `UserId` relates the token to its
  owner, and a token is deleted with its user (an index on `UserId` serves the list of a user). `Name` is required, at
  most 100; `DisplayId` is required, at most 16 (the stored text has 8 characters; the column is wider); `TokenHash` is
  required, at most 64 (a SHA-256 value written in hexadecimal), with a unique index, which is what makes a hash unique
  in the database and what the lookup of a presented token uses; `Created`, `Expires`, `LastUsed` and `Revoked` (the
  last two are null until they happen). The value of a token is never a column.
- **UserTotps** (`UserTotp`): key `UserId`, never generated by the database (`ValueGeneratedNever`), so a user has at
  most one second factor enrollment and a second row is impossible. `ProtectedSecret` is required, at most the width of
  an encrypted value (the output of the secret protector, never the secret); `Created`; `ConfirmedAt` (null while the
  enrollment is pending); `LastAcceptedStep` (the time step of the last accepted code, the replay state). `IsActive`
  is a computed property and not a column. A foreign key to `Users` deletes the row with its user. The state is a
  table of its own, not columns of the user, because the user store rewrites every column of a user from a copy it read
  earlier, and a stale copy must never be able to move `LastAcceptedStep` backwards.
- **TotpRecoveryCodes** (`TotpRecoveryCode`): key `Id`, assigned by the database. `UserId` is a foreign key to `Users`
  with cascade delete. `CodeHash` is required, at most 64 (a SHA-256 value written in hexadecimal); a unique index on
  `UserId` plus `CodeHash` makes a duplicate hash of one user impossible. `Created`; `UsedAt` is null until the code is
  spent. A
  recovery code itself is never a column.
- **ReviewerSlots** (`ReviewerSlot`): key `Id`, assigned by the database. `ProjectId` and `ReviewId` relate the slot
  to its review, so the database refuses a slot for a review that does not exist, and a slot is deleted with its
  review. `PrimaryUserId` and `AlternateUserId` (null when there is no alternate) hold user identities only; they are
  not relationships, so a slot never depends on the users table. Both columns are indexed, so the inbox finds the
  slots of a user without a scan.
- **Signatures** (`ReviewSignature`): key `Id`, assigned by the database. `ProjectId` and `ReviewId` relate the
  signature to its review, and a signature is deleted with its review. `SlotId` relates the signature to its slot
  (see below). `UserId` is the identity of the signer; `DisplayName` (the name at the time) is required, at most 100;
  `Role` (the role in the slot, primary or alternate) is stored as text, at most 32; `SignedAt`; `RecordDigest` is
  required, at most 64 (a SHA-256 value written in hexadecimal); `Outcome` (the outcome the signer attested, approved
  or rejected) is required and stored as its number (`ReviewOutcome` promises never to renumber), with the default
  approved; `Meaning` is required, at most 256. A unique index
  on `SlotId` plus `RecordDigest` allows one signature of a record per slot. Signatures are append-only: the mapping
  has no update path and the application never updates or deletes a row.
- **ReviewReopens** (`ReviewReopen`): key `Id`, assigned by the database, so it grows with the order of the reopens.
  `ProjectId` and `ReviewId` (at most the review identifier limit) relate the row to its review (cascade delete with
  the review); an index on `ProjectId` plus `ReviewId` serves the history of a review. `ReopenedAt`; `ReopenedBy` (the
  user name) is required, at most the user name limit; `ReopenedByDisplayName` is required, at most the display name
  limit; `Reason` is required, at most `ReviewLimits.MaxReopenReasonLength` (250); `PreviousDigest` is required, at
  most 64 (a SHA-256 value written in hexadecimal). The table is append-only: the application never updates or deletes
  a row. The number of rows of a review is the reopen count of its record.
- **ReviewUserStates** (`ReviewUserState`): composite key `UserId`, `ProjectId`, and `ReviewId` (at most the review
  identifier limit), so a user has at most one row per review and two parallel writes cannot store two rows. `UserId`
  is a foreign key to `Users` and `ProjectId` plus `ReviewId` a foreign key to `Reviews`; both cascade, so the rows go
  with the user and with the review. `LastSeenCommentId` is the highest comment identity the user has seen and
  `LastSeenAt` the time of the last visit. The table is the only storage of the inbox, which also serves as the list of
  notifications: there is no notification table. Its rows are not audited and not part of the record digest.
- **AuditEntries** (`AuditEntry`): key `Sequence`, which is never generated by the database (`ValueGeneratedNever`): the
  appender assigns it under its gate as the previous sequence plus one, so the key is also the backstop that refuses a
  second entry at the same position. `TimestampUtc` is stored as fixed-width UTC text through a value converter (see
  the `AuditStore` unit); `ActorUserId` (null for the command line, an unknown user, or
  a removed user); `ActorName` is required, at most 200; `Action` is required, at most 100; `ProjectId` and `ReviewId`
  (at most the review identifier limit) are null for events that belong to no review; `Detail` is required, at most
  500; `PreviousHash` and `Hash` are required, 64 characters of hexadecimal. There are no foreign keys, on purpose: an
  entry is a snapshot of text that must survive whatever happens to the rows it describes, such as the deletion of a
  whole review. Four indexes, each ending with `Sequence`, serve the reads: `ProjectId`, `ReviewId`, and `Sequence`
  for the
  history of one review, `ProjectId` and `Sequence` for the audit page of a project, `ActorUserId` and `Sequence` for
  an actor, and `Action` and `Sequence` for an action. The mapping has
  no update path, and the application never updates or deletes a row.
- **Repositories** (`RepositoryRecord`, an internal row type of the unit `RepositoryStore`, not a Core type, because it
  alone holds the encrypted token): key `Id`, assigned by the database. `ProjectId` is required and a foreign key to
  `Projects`, so a repository belongs to exactly one project and the database refuses one for a project that does not
  exist. `Name` is required, at most 40 characters,
  with the case-insensitive collation of SQLite; a unique index on `ProjectId` plus `Name` makes a repository name
  unique within a project without regard to case; `Location` is required, at most 500; `Username` at most 200 (null for
  anonymous access); `EncryptedToken` at
  most 4000 (null when no token is set; the text is the output of the token protector, never the token); `Enabled`;
  `CreatedAt` and `UpdatedAt`. There is no delete path.

The relationship from a signature to its slot uses the `NoAction` delete behavior, not `Restrict`. SQLite checks a
`RESTRICT` foreign key immediately, at the statement that deletes the parent row, so deleting a review would fail
when its cascade removes the slots before it removes the signatures. A `NO ACTION` foreign key is checked at the end
of the statement, when the signatures of the deleted review are gone too, so deleting a whole review still works,
while a direct delete of a slot that has a signature is still refused. The test
`ReviewStore_RemoveSlotAsync_SignedSlot_IsRefusedByDatabase` shows the refusal.

The limits of project, review, path, author, user name, display name, and comment columns use the constants of
`ReviewLimits`, so the schema
and the rules cannot drift apart. The widths that are a property of the storage representation rather than of a
rule - the state name and the role name (32), a SHA-256 value written in hexadecimal (64), and the meaning text of a
signature (256) - are named constants of
the unit.
The
composite key of a review (project and review identifier) makes the review identifier unique within a project and
allows the same identifier in two projects. The composite key of a file makes its path unique within a review, and the
composite key of a membership makes a user a member of a project at most once.
These keys are what enforces uniqueness in the database even when two requests race; the only separate unique indexes
are the one on the user name, the one on the external issuer and subject of a user, the one on the project and name of
a repository, and the one on the slot and digest of a
signature. The state is stored by name, so
adding states later does not change the meaning of stored rows. The
tables of threads and comments are new: a database created before they existed does not have them and is not
altered, so it must be deleted and created again (see the `Storage` design). The same holds for the three
checklist tables, and for the users table and the user name columns of comments, answers, and section decisions: a
database created before the identity slice must be deleted and created again, and its data is not migrated. The same
holds for the tables of reviewer slots and signatures: a database created before the signature slice does not have
them and must be deleted and created again. The same holds for the audit table: a database created before the audit
trail does not have it and must be deleted and created again (there are no migrations). The same holds for the
`Repositories` table: a database created before the repository registry does not have it and must be deleted and
created again. The same holds for the review columns of a repository review (source columns on a review, change kind
and baseline columns on a file): a database created before the review-from-git slice does not have them and must be
deleted and created again (there are no migrations; the development database is simply recreated). The same holds for
the `PersonalAccessTokens` table and the creator columns of a review (`CreatedBy`, `CreatedViaApi`): a database created
before the personal-access-token slice does not have them and must be deleted and created again. The same holds for the
`DueDate` column of a review: there is no migration for it either, `EnsureCreated` does not add a column to an existing
table, and development databases are recreated. The same holds for the `ReviewConclusionText` column of a review and for
the `Outcome` column of a signature. The
same holds for the follow-up columns (`FollowsReviewId`,
`CarriedAnswerCount`, `CarriedThreadCount` of a review, `CarriedFromReviewId` of an answer, a section state and a
thread, `CarriedFromThreadId` of a thread) and the index on `ProjectId` and `FollowsReviewId`. The same holds for the
`IncludeInReport` and `Kind` columns of a comment. The same holds for the `ProjectMemberships` table, the
`IsSystemAdmin` column of a user (which replaces the role column), and the `ProjectId` of a repository: a database
created before the project access slice does not have them and must be deleted and created
again. The same holds for the `LastSignInUtc` column of a user (users page): there is no migration, so a development
database created before it must be deleted and created again. The same holds for the `MustChangePassword` column of a
user (project members management): the schema is created
with `EnsureCreated` and has no migrations, so a development database created before that column existed does not have
it and must be deleted and created again. The same holds for the seven evidence settings columns of a project: there is
no migration for them either, `EnsureCreated` does not add a column to an existing table, and development databases
are recreated. The same holds for the `ExternalIssuer` and `ExternalSubject` columns of a user and their unique index
(sign-in with an identity provider): a development database created before them must be deleted and created again. The
same holds for the `UserTotps` and `TotpRecoveryCodes` tables (second factor sign-in): they have no migration, so a
development database created before them must be deleted and created again. The same holds for the `ReviewReopens`
table and the `RequireIndependentReviewers` column of a project (approval lock and independent reviewers): there is no
migration and `EnsureCreated` does not add a table or column to an existing database, so a development database created
before them must be deleted and created again. The same holds for the `RequireNonAuthorChecklistAnswers` and
`RequireNonReopenerSigning` columns of a project (review rule switches): a development database created before them
must be deleted and created again. The same holds for the `ReviewUserStates` table, the indexes on the users of the
reviewer slots and on the creator of the reviews, and the `NOCASE` collation of the creator column (the inbox): there
is no migration, so a development database created before them must be deleted and created again. The checklist keys
are what makes "one answer per check" a rule of the database.
The section and
check identifiers are not related to a table of their own: they name entries inside the snapshot, and the service
checks them.

#### Key Methods

**OnModelCreating**: Configures the model as described by the table above.

- *Parameters*: `ModelBuilder modelBuilder`.
- *Returns*: `void`.
- *Preconditions*: `modelBuilder` is not null.
- *Postconditions*: Keys, relationships, limits, and the state conversion are configured.

#### Error Handling

N/A - the unit only declares the model. A null model builder raises `ArgumentNullException`. Violations of the
mapped constraints surface as database update exceptions to `ReviewStore`.

#### Dependencies

- **Core domain types** — `Project`, `EvidenceSettings`, `Review`, `ReviewFile`, `ReviewState`, `ReviewThread`, `ReviewComment`,
  `ReviewThreadState`, `ReviewChecklist`, `ChecklistSectionState`, `ChecklistAnswer`, `ChecklistOutcome`, `User`,
  `ProjectMembership`, `ProjectRole`, `ReviewerSlot`, `ReviewSignature`, `ReviewReopen`, `SlotRole`, `AuditEntry`, `PersonalAccessToken`,
  `UserTotp`, `TotpRecoveryCode`, `ReviewUserState`, `AuditText` (the column
  limits), and
  `ReviewLimits`, and `RepositoryService` (the length limits of the repository columns).
- **Entity Framework Core** — the context base class and the model configuration API.

#### Callers

- **ReviewStore**, **UserStore**, **PersonalAccessTokenStore**, **TotpStore**, **AuditStore**, **InboxStore**, and
  **RepositoryStore** —
  open a context
  for each operation.
- **DatabaseSetup** — configures the context options and creates the schema.
