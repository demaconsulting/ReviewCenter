## Storage

![Storage Structure](StorageView.svg)

### Overview

The `Storage` subsystem keeps projects, reviews, review comment threads, and review checklists in a relational
database through Entity
Framework Core. It
implements the store interface of the `Core` subsystem and owns everything about the database: the schema, the
choice of provider, the location of the data, and the creation of the database. It contains three units:
`ReviewCenterDbContext`, the mapping of the domain types to tables; `ReviewStore`, the implementation of the
store interface; and `DatabaseSetup`, the provider selection, connection string resolution, and database
creation, together with its options type `DatabaseOptions`. The types of the subsystem are internal to the
application; the rest of the system sees only the Core store interface.

### Interfaces

**ReviewStore (as IReviewStore)**: The persistence operations of the domain.

- *Type*: In-process .NET class implementing the Core interface.
- *Role*: Provider.
- *Contract*: Stores and reads projects, reviews, file content, comment threads, and checklists as described by the
  Core
  interface. A review is stored with all its files and its checklist snapshot in one transaction, and a thread with
  its first comment in one
  transaction. An attempt to store a review whose identifier exists in the project raises a `DomainRuleException`
  for `ReviewIdDuplicate`.
- *Constraints*: Every operation uses its own short-lived database context, so it is safe for concurrent use.

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
provider to that one place, a provider-specific duplicate-key test in `ReviewStore`, and verifying the key
lengths and the ordering behavior of that database. The mapping and the queries are otherwise written
provider-neutrally.

**Schema creation without migrations**: The schema is created with `EnsureCreated` when the server starts, not
with migrations. This is adequate while there is no released schema to upgrade: a new installation gets the
tables, and an existing database is left alone. The cost is that a database created by an earlier version is
never altered, so a change of the schema needs a migration strategy before the first release that must keep
data. This is a known limitation, recorded as an open decision in `architecture.md` and not yet resolved. It
already matters twice: the tables for comment threads were added after the first schema, and the tables for review
checklists after those, so a database created before them does not have them and the review page fails until the
database file is deleted (or a new one is
named) and created again. Databases kept for development must be recreated; no data of a released version is
affected. A
second limitation is that two processes starting at the same moment on a brand-new database file can collide
while creating it; a single service instance per database is assumed.

**Data location**: By default the database is the file `reviewcenter.db` in the `ReviewCenter` folder of the
per-user application data folder of the operating system. If the operating system reports no such folder (for
example for a service account without a profile), the file is placed in a `data` folder next to the executable.
The folder is created when needed.

**Context lifetime and concurrency**: Blazor Server connections are long-lived and concurrent, and a database
context is not thread-safe. The store therefore obtains a new context from a context factory for every
operation and disposes it at the end, and never keeps a context in a page or in a service.

**Native library**: SQLite needs a native library (`e_sqlite3.dll` on Windows, `libe_sqlite3.so` on Linux). The
single-file executable cannot embed it, so it is published as a side file next to the executable; the
executable must not be moved without it.
