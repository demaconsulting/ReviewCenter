### ReviewCenterDbContext

![Storage Structure](StorageView.svg)

#### Purpose

`ReviewCenterDbContext` is the Entity Framework Core context that maps the Core domain types to database tables.
Its single responsibility is to define the schema: the tables, keys, relationships, column limits, and
conversions. It contains no queries; those belong to `ReviewStore`.

#### Data Model

The context exposes five sets and maps the Core types directly, without separate persistence classes.

- **Projects** (`Project`): key `Id`, assigned by the database. `Name` is required, at most 100 characters;
  `Description` is required (may be empty), at most 1000; `Created`.
- **Reviews** (`Review`): key `ProjectId` plus `Id`. `Title` is required, at most 200; `State` is stored as text, at
  most 32; `ContentFingerprint` is required, at most 64; `Created`. A review belongs to a project and is deleted
  with it.
- **ReviewFiles** (`ReviewFile`): key `ProjectId`, `ReviewId`, and `Path`. `Content` is required and stored as a
  binary column; `Sha256` is required, at most 64; `Size`. A file belongs to a review and is deleted with it.
- **Threads** (`ReviewThread`): key `Id`, assigned by the database. `ProjectId`, `ReviewId`, and `FilePath` name
  the file the thread is about and form a relationship to that file, so the database refuses a thread for a file
  that does not exist, and a thread is deleted with its file. `StartLine` and `EndLine` are null for a thread on
  the whole file. `State` is stored as text, at most 32; `Created`.
- **Comments** (`ReviewComment`): key `Id`, assigned by the database, so it grows with the posting order.
  `ThreadId` relates the comment to its thread, and a comment is deleted with its thread. `Author` is required, at
  most 100 characters; `Text` is required, at most 4000; `Created`.

The limits of project, review, path, author, and comment columns use the constants of `ReviewLimits`, so the schema
and the rules cannot drift apart. The widths that are a property of the storage representation rather than of a
rule - the state name (32) and a SHA-256 value written in hexadecimal (64) - are named constants of the unit.
The
composite key of a review (project and review identifier) makes the review identifier unique within a project and
allows the same identifier in two projects. The composite key of a file makes its path unique within a review.
These keys are what enforces uniqueness in the database even when two requests race; no separate unique index
exists. The state is stored by name, so adding states later does not change the meaning of stored rows. The
tables of threads and comments are new: a database created before they existed does not have them and is not
altered, so it must be deleted and created again (see the `Storage` design).

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

- **Core domain types** — `Project`, `Review`, `ReviewFile`, `ReviewState`, `ReviewThread`, `ReviewComment`,
  `ReviewThreadState`, and `ReviewLimits`.
- **Entity Framework Core** — the context base class and the model configuration API.

#### Callers

- **ReviewStore** — opens a context for each operation.
- **DatabaseSetup** — configures the context options and creates the schema.
