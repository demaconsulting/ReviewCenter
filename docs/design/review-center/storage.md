## Storage

![Storage Structure](StorageView.svg)

### Overview

The `Storage` subsystem keeps projects (with their evidence settings), project memberships, reviews, review comment
threads, review checklists, users,
reviewer slots,
signatures, personal access tokens, and the hash-chained audit trail in a relational
database through Entity
Framework Core. It
implements the store interfaces of the `Core` subsystem and owns everything about the database: the schema, the
choice of provider, the location of the data, and the creation of the database. It contains seven units:
`ReviewCenterDbContext`, the mapping of the domain types to tables; `ReviewStore`, the implementation of the
review store interface; `UserStore`, the implementation of the user store interface; `AuditStore`, the implementation
of the audit store interface together with the `AuditAppender` that serializes and chains the entries;
`RepositoryStore`, the implementation of the repository store interface; `PersonalAccessTokenStore`, the implementation
of the personal access token store interface; and `DatabaseSetup`, the
provider selection, connection string resolution, and database
creation, together with its options type `DatabaseOptions`. The types of the subsystem are internal to the
application; the rest of the system sees only the Core store interfaces.

### Interfaces

**ReviewStore (as IReviewStore)**: The persistence operations of the domain.

- *Type*: In-process .NET class implementing the Core interface.
- *Role*: Provider.
- *Contract*: Stores and reads projects, reviews, file content, comment threads, checklists, reviewer slots, and
  signatures as described by the
  Core
  interface. A review is stored with all its files and its checklist snapshot in one transaction, and a thread with
  its first comment in one
  transaction. An attempt to store a review whose identifier exists in the project raises a `DomainRuleException`
  for `ReviewIdDuplicate`. Comments, answers, and section decisions are stored with the display name and the user name
  of the person who made them. A review from a repository is stored with its source (repository, both commits,
  selection, and the review-set it was planned for, if any) and, for every file, its change kind and frozen baseline
  content next to the head content; reads of file
  lists carry only metadata, never bytes, and the baseline bytes are read on request. The record of a review
  (fingerprint, source and changes, checklist, section states, answers, and threads
  with comments) is read as one snapshot. A signature is appended, never changed or deleted, in a transaction that
  recomputes the digest of the stored record: a digest that is not the current one raises a `DomainRuleException` for
  `RecordChanged` and stores nothing, a second signature of the same slot and digest raises one for
  `SigningNotAllowed`, and an unknown slot or review raises one for `SlotNotFound` or `ReviewNotFound`. Every change of
  data is stored together with its audit entry in one transaction, and a change that has no effect stores no entry.
  The membership of a user in a project (the project role `ProjectAdmin` or `User`) is added, changed, removed, read,
  and listed by project and by user; a second membership of the same user in the same project raises a
  `DomainRuleException` for `MembershipExists`, a membership of an unknown project or user is refused by the foreign
  keys of the database, and every change is audited with the project (`project.member-added`,
  `project.member-role-changed`, and `project.member-removed`) in the same transaction. A removal or a demotion can be
  made to require that another enabled project administrator remains: the check and the change are one step in the
  serialized transaction, and a violation raises a `DomainRuleException` for `LastProjectAdmin` and stores nothing.
  The evidence settings of a project are stored with the project (seven boolean columns, whose defaults are the default
  settings) and are read with every project; they are changed only by a compare-and-set
  (`UpdateEvidenceSettingsAsync`) that stores the new settings together with the audit entry
  `project.evidence-settings-changed` in one transaction when the stored settings still equal the expected ones, and
  otherwise changes nothing and stores no entry. The schema has no migration: a development database that was created
  before the settings existed is recreated.
- *Constraints*: Every operation uses its own short-lived database context, so it is safe for concurrent use.

**UserStore (as IUserStore)**: The persistence operations for users.

- *Type*: In-process .NET class implementing the Core interface.
- *Role*: Provider.
- *Contract*: Stores a user with its system administrator and must-change-password flags, reads a user by user name
  without regard to case, lists the users, and replaces the
  stored state of a user. An attempt to store a user whose name exists, in any capitalization, raises a
  `DomainRuleException` for `UserNameTaken`. A new user, and an update that carries an audit draft, are stored together
  with their audit entry in one transaction. A new user can also be stored together with a project membership in one
  transaction (`AddToProjectAsync`); neither exists when either fails.
- *Constraints*: Every operation uses its own short-lived database context, so it is safe for concurrent use.

**PersonalAccessTokenStore (as IPersonalAccessTokenStore)**: The persistence operations for personal access tokens.

- *Type*: In-process .NET class implementing the Core interface.
- *Role*: Provider.
- *Contract*: Stores a token, lists the tokens of a user newest first, finds a token with its owner by the hash of its
  value, revokes a token of a user, and records the moment of a use. A token is stored together with its audit entry,
  and so is a revocation; a revocation of an unknown or already revoked token stores nothing, and a use is not audited.
- *Constraints*: Every operation uses its own short-lived database context, so it is safe for concurrent use. The store
  holds and returns only the hash of a token value; the value itself never reaches it.

**AuditStore (as IAuditStore)**: The persistence operations for the audit trail.

- *Type*: In-process .NET class implementing the Core interface.
- *Role*: Provider.
- *Contract*: Appends one entry (for events that change no data), lists entries newest first, optionally of one
  project, of one review of it,
  before or after a sequence, and reads entries in ascending batches. A project filter alone returns every entry of
  that project and never an entry without a project. Each entry gets the next sequence number and
  the hash of its predecessor. There is no operation to update or delete an entry.
- *Constraints*: Appends from every store are serialized by one process-wide gate and by the database write lock.

**RepositoryStore (as IRepositoryStore)**: The persistence operations for the git repositories of the projects.

- *Type*: In-process .NET class implementing the Core interface.
- *Role*: Provider.
- *Contract*: Stores a repository of a project, reads one or lists the repositories of one project (by name, without
  regard to case), reads the encrypted token
  of one, and updates one with a token action (keep, replace, or clear). A name that exists in the same project, in any
  capitalization, raises a `DomainRuleException` for `RepositoryNameTaken` (the same name may exist in another
  project), and an update of an unknown repository one for
  `RepositoryNotFound`. Each change is stored together with its audit entry in one transaction.
- *Constraints*: Every operation uses its own short-lived database context, so it is safe for concurrent use. The
  store holds and returns only encrypted token text, and the read model has no token member.

**Database settings**: The `Database` configuration section.

- *Type*: Configuration (`appsettings.json` and environment variables).
- *Role*: Consumer.
- *Contract*: `Provider` (`Sqlite`, the default and only supported value, compared without regard to case) and
  `ConnectionString` (empty for the default location, a plain file path, or a complete connection string).
- *Constraints*: An unsupported provider is an error that stops the start-up.

**DatabaseSetup operations**: Used by the `Hosting` subsystem.

- *Type*: In-process .NET static methods.
- *Role*: Provider.
- *Contract*: Configures the database context for the selected provider and connection string, creates the
  database folder, file, and tables when they do not exist, and names the folder that holds the data, so that other
  server-side data (the git cache and the key ring) can be placed beside the database.
- *Constraints*: Raises `InvalidOperationException` for an unsupported provider; file system and database
  failures propagate to the caller.

### Design

**Technology and the provider seam**: The subsystem uses Entity Framework Core with the SQLite provider. SQLite
is the only supported database for now: it needs no server and suits a single-instance service. The choice of
provider is isolated in `DatabaseSetup`, which is the only place that names a provider: the provider setting is
validated there and the provider is registered there. Supporting PostgreSQL or SQL Server later means adding a
provider to that one place, a provider-specific duplicate-key test in `ReviewStore` and `UserStore`, and verifying
the key
lengths and the ordering behavior of that database. The mapping and the queries are otherwise written
provider-neutrally. The case-insensitive user name (a SQLite column collation) is SQLite specific as well and would need
an equivalent.

**Schema creation without migrations**: The schema is created with `EnsureCreated` when the server starts, not
with migrations. This is adequate while there is no released schema to upgrade: a new installation gets the
tables, and an existing database is left alone. The cost is that a database created by an earlier version is
never altered, so a change of the schema needs a migration strategy before the first release that must keep
data. This is a known limitation, recorded as an open decision in `architecture.md` and not yet resolved. It
already matters ten times: the tables for comment threads were added after the first schema, the tables for review
checklists after those, the users table and the user name columns of comments, answers, and section decisions
after those, the tables of reviewer slots and signatures after those, the audit table after those, the
`Repositories` table after that, the source and change columns of reviews from a repository after that, and the
`PersonalAccessTokens` table with the creator columns of reviews after that, and the `ProjectMemberships` table with
the project of a repository and the system administrator flag of a user after that, and the `MustChangePassword` column
of a user after that, so a database
created before them does not
have them and the pages fail until the
database file is deleted (or a new one is
named) and created again. Databases created before the identity slice must be deleted and recreated, and so must
databases created before the signature slice, before the audit trail, before the repository registry, before
reviews from a repository, before the personal access tokens, before the project access control, and before the
project members management (the `MustChangePassword` column); there is no
migration of their data, and no data of a released version is
affected. A
second limitation is that two processes starting at the same moment on a brand-new database file can collide
while creating it; a single service instance per database is assumed.

**Data location**: By default the database is the file `reviewcenter.db` in the `ReviewCenter` folder of the
per-user application data folder of the operating system. If the operating system reports no such folder (for
example for a service account without a profile), the file is placed in a `data` folder next to the executable.
The folder is created when needed. The same folder is the default home of the git cache (`git-cache`) and of the
Data Protection key ring (`keys`), both resolved by `Hosting` from the folder of the database file (see the
`DatabaseSetup` unit), so backing up the data folder covers the database and the key that decrypts the stored
tokens.

**Repositories and the token**: The `Repositories` table holds the git repositories registered with a project; every
row has a required `ProjectId` (a foreign key), so a repository belongs to exactly one project. The token is stored
only as the encrypted text produced by `ICredentialProtector`; the store never sees a plain token, and the read model
that leaves the store has a flag (`HasToken`) instead of a member for the text. Name uniqueness within a project is
enforced by a
unique index on `ProjectId` and `Name` with the case-insensitive collation of SQLite, and a violation is translated to
`RepositoryNameTaken`;
like the other stores, the translation is SQLite specific. Repositories are never deleted.

**Project memberships**: The `ProjectMemberships` table holds who may see which project and with which project role.
Its key is the user and the project together, so a user has at most one membership per project, and both columns are
foreign keys; the database refuses a membership of a project or user that does not exist. The membership is data of the
project: it carries no copy of the system administrator flag, which stays on the user, and the decision about
visibility is not made here (see `ProjectAccess` in the `Core` subsystem).

**Personal access tokens**: The `PersonalAccessTokens` table holds the tokens of the users. Only the lowercase
hexadecimal SHA-256 hash of a token value is stored, with a unique index that serves the lookup of every request to the
API; the value is never stored. A token has a separate, random display id for the list of the Account page, so no part of
the value is stored. The use of a token is recorded with a single-column update that is not audited, because it happens
on every request and the audit trail records only changes that matter. The `Reviews` table gains the creator columns
(`CreatedBy` and `CreatedViaApi`).

**Context lifetime and concurrency**: Blazor Server connections are long-lived and concurrent, and a database
context is not thread-safe. The store therefore obtains a new context from a context factory for every
operation and disposes it at the end, and never keeps a context in a page or in a service. `UserStore` follows the
same rule.

**Signing and the write lock**: A signature must name the exact record the reviewer saw, and the record can be
changed by other requests at any moment. `ReviewStore.AddSignatureAsync` therefore reads the record again, inside a
`Serializable` transaction, and compares its digest with the digest of the signature before it inserts. SQLite takes
the write lock when such a transaction begins, so no other writer can change the record between the check and the
insert; SQLite allows one writer at a time, so a second writer is refused (SQLITE_BUSY) while the transaction is
open (the test of the unit shows that a second connection is refused while such a transaction has begun). The digest
comes from the Core
code (`RecordDigest`), so the service and the store cannot disagree on what the record is. The foreign key from a
signature to its slot uses the
`NoAction` delete behavior, which still refuses to delete a signed slot but, unlike `Restrict`, lets SQLite delete a
whole review with its slots and signatures. This transaction mode and the foreign key behavior are SQLite specific
and would need an equivalent for another provider.

**The audit trail and its transaction**: Every audited change is stored together with its audit entry. All stores
share one `AuditAppender`, which owns a process-wide gate, a semaphore with one slot. A mutation takes the gate, opens a
`Serializable` transaction (for SQLite the write lock is taken when it begins, so another process, such as the
`audit verify` or `user add` command beside the server, is serialized by the database), makes the change, appends the
audit entry to the same context, saves, and commits. If anything fails, the change and its entry are rolled back
together, and a change that did not take effect (not found, nothing to do) appends no entry. The primary key of the
audit table, the sequence number, is assigned under the gate and is the backstop against a second entry with the same
position. The gate is the innermost lock of the system: it is acquired after the gates of `UserService` and
`SigningService`, nothing inside it calls a service, and it is released on every path. The entries have no foreign
keys, so they stay when the rows they describe are removed (for example a whole review). The `AuditStore` unit offers
no update or delete. See the `AuditStore` unit for the details and `AuditChain` for what the hash chain does and does
not prove.

**Native library**: SQLite needs a native library (`e_sqlite3.dll` on Windows, `libe_sqlite3.so` on Linux). The
single-file executable cannot embed it, so it is published as a side file next to the executable; the
executable must not be moved without it.
