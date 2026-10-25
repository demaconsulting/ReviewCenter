## Storage

![Storage Structure](StorageView.svg)

### Overview

The `Storage` subsystem keeps projects, reviews, review comment threads, review checklists, users, reviewer slots,
signatures, and the hash-chained audit trail in a relational
database through Entity
Framework Core. It
implements the store interfaces of the `Core` subsystem and owns everything about the database: the schema, the
choice of provider, the location of the data, and the creation of the database. It contains five units:
`ReviewCenterDbContext`, the mapping of the domain types to tables; `ReviewStore`, the implementation of the
review store interface; `UserStore`, the implementation of the user store interface; `AuditStore`, the implementation
of the audit store interface together with the `AuditAppender` that serializes and chains the entries; and
`DatabaseSetup`, the
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
  of the person who made them. The record of a review (fingerprint, checklist, section states, answers, and threads
  with comments) is read as one snapshot. A signature is appended, never changed or deleted, in a transaction that
  recomputes the digest of the stored record: a digest that is not the current one raises a `DomainRuleException` for
  `RecordChanged` and stores nothing, a second signature of the same slot and digest raises one for
  `SigningNotAllowed`, and an unknown slot or review raises one for `SlotNotFound` or `ReviewNotFound`. Every change of
  data is stored together with its audit entry in one transaction, and a change that has no effect stores no entry.
- *Constraints*: Every operation uses its own short-lived database context, so it is safe for concurrent use.

**UserStore (as IUserStore)**: The persistence operations for users.

- *Type*: In-process .NET class implementing the Core interface.
- *Role*: Provider.
- *Contract*: Stores a user, reads a user by user name without regard to case, lists the users, and replaces the
  stored state of a user. An attempt to store a user whose name exists, in any capitalization, raises a
  `DomainRuleException` for `UserNameTaken`. A new user, and an update that carries an audit draft, are stored together
  with their audit entry in one transaction.
- *Constraints*: Every operation uses its own short-lived database context, so it is safe for concurrent use.

**AuditStore (as IAuditStore)**: The persistence operations for the audit trail.

- *Type*: In-process .NET class implementing the Core interface.
- *Role*: Provider.
- *Contract*: Appends one entry (for events that change no data), lists entries newest first, optionally of one review,
  before or after a sequence, and reads entries in ascending batches. Each entry gets the next sequence number and
  the hash of its predecessor. There is no operation to update or delete an entry.
- *Constraints*: Appends from every store are serialized by one process-wide gate and by the database write lock.

**Database settings**: The `Database` configuration section.

- *Type*: Configuration (`appsettings.json` and environment variables).
- *Role*: Consumer.
- *Contract*: `Provider` (`Sqlite`, the default and only supported value, compared without regard to case) and
  `ConnectionString` (empty for the default location, a plain file path, or a complete connection string).
- *Constraints*: An unsupported provider is an error that stops the start-up.

**DatabaseSetup operations**: Used by the `Hosting` subsystem.

- *Type*: In-process .NET static methods.
- *Role*: Provider.
- *Contract*: Configures the database context for the selected provider and connection string, and creates the
  database folder, file, and tables when they do not exist.
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
already matters five times: the tables for comment threads were added after the first schema, the tables for review
checklists after those, the users table and the user name columns of comments, answers, and section decisions
after those, the tables of reviewer slots and signatures after those, and the audit table after those, so a database
created before them does not
have them and the pages fail until the
database file is deleted (or a new one is
named) and created again. Databases created before the identity slice must be deleted and recreated, and so must
databases created before the signature slice and before the audit trail; there is no
migration of their data, and no data of a released version is
affected. A
second limitation is that two processes starting at the same moment on a brand-new database file can collide
while creating it; a single service instance per database is assumed.

**Data location**: By default the database is the file `reviewcenter.db` in the `ReviewCenter` folder of the
per-user application data folder of the operating system. If the operating system reports no such folder (for
example for a service account without a profile), the file is placed in a `data` folder next to the executable.
The folder is created when needed.

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
