### DatabaseSetup

![Storage Structure](StorageView.svg)

#### Purpose

`DatabaseSetup` decides where and how the database is opened and creates it when it does not exist. Its single
responsibility is the provider seam: reading the `Database` settings, selecting the provider, resolving the
connection string, and creating the folder, file, and schema. `DatabaseOptions` is its supporting options type.

#### Data Model

**DatabaseOptions**: the settings bound from the `Database` configuration section.

- **Provider**: `string` — the database provider; defaults to `Sqlite`. Compared without regard to case.
- **ConnectionString**: `string` — empty by default; see the resolution rules below.

The section name is `Database`, so the settings can be given in `appsettings.json` or as the environment
variables `Database__Provider` and `Database__ConnectionString`.

**Default location**: the file `reviewcenter.db` in the `ReviewCenter` folder of the per-user local application
data folder. If the operating system reports that folder as empty, the file is `data/reviewcenter.db` under the
directory of the executable.

**Connection string resolution**: an empty value selects the default file; a value containing `=` is taken as a
complete connection string and used as it is; any other value is a file path and is wrapped as the data source.
An in-memory database (`:memory:`, `Mode=Memory`, or an empty data source) is refused with an
`InvalidOperationException` that asks for a file path, because every SQLite connection to an in-memory database
gets its own empty database, so the schema created at startup would be missing for the requests that follow.

#### Key Methods

**GetDefaultDatabasePath**: Returns the default database file path as described above.

**ResolveConnectionString**: Returns the SQLite connection string for the options, following the rules above.

- *Parameters*: `DatabaseOptions options`.
- *Returns*: `string` — the connection string.

**ResolveDataDirectory**: Returns the folder that holds the database file.

- *Parameters*: `DatabaseOptions options`.
- *Returns*: `string` — the folder of the data source of the resolved connection string; when the value is empty, an
  in-memory form, or a `file:` URI, the folder of the default database file.
- *Preconditions*: `options` is not null (otherwise `ArgumentNullException`).
- *Notes*: `ServerHost` uses it to place the git cache and the key ring beside the database, so one data folder holds
  everything that must be kept and backed up together.

**Configure**: Configures a database context options builder for the selected provider.

- *Parameters*: `DbContextOptionsBuilder builder`, `DatabaseOptions options`.
- *Postconditions*: The builder uses SQLite with the resolved connection string.
- *Notes*: This and `EnsureCreated` are the places that name the provider. Adding PostgreSQL or SQL Server adds
  a case here, in the provider check, and in the duplicate-key translation of `ReviewStore`.

**EnsureCreated**: Creates the database when it is new.

- *Parameters*: the context factory and `DatabaseOptions options`.
- *Postconditions*: The provider is supported, the folder of the database file exists, and the file and tables
  exist.

Checks the provider first, so that an unsupported setting fails before anything is created. It then creates the
folder of the file (not for a `file:` URI) and asks Entity Framework Core to create
the schema. An existing database is not changed, and no migrations are used. A database created before the tables
for comment threads existed therefore does not get them and must be deleted and created again; the same holds for a
database created before the audit trail, which has no `AuditEntries` table, and for a database created before the
repository registry, which has no `Repositories` table.

#### Error Handling

An unsupported provider raises `InvalidOperationException` with the message "The database provider '{name}' is not
supported. Supported providers: Sqlite." and nothing is created. An invalid connection string, a folder that
cannot be created, and a database error propagate to the caller as the exceptions of the runtime; `ServerHost`
turns them into one start-up error. Null arguments raise `ArgumentNullException`.

#### Dependencies

- **ReviewCenterDbContext** — the context created and configured.
- **Entity Framework Core** and **Microsoft.Data.Sqlite** — the SQLite provider and the connection string builder.

#### Callers

- **ServerHost** — configures the context factory and calls `EnsureCreated` while building the application, and calls
  `ResolveDataDirectory` to place the git cache and the key ring.
- **UserCommand** and **AuditCommand** — call `EnsureCreated` before they work, so the first administrator can be
  created in a new database. `AuditCommand` therefore creates an empty database when none exists at the configured
  location.
