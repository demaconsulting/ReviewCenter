### ReviewStore

![Storage Structure](StorageView.svg)

#### Purpose

`ReviewStore` implements the Core store interface on top of `ReviewCenterDbContext`. Its single responsibility is
to persist projects, reviews, file content, comment threads, and checklists, and to read them back, translating database
behavior into the terms of the domain.

#### Data Model

The unit is stateless apart from the injected context factory. Every operation creates its own short-lived
context from the factory and disposes it before returning, so concurrent operations never share a context.
Reads use no change tracking.

#### Key Methods

**AddProjectAsync**: Stores a project and returns it with the identity assigned by the database. The unit copies the
name, description, and creation time into a new entity, so an identity supplied by the caller is ignored and can
neither be persisted nor collide with an existing project; the returned project is the stored entity.

**GetProjectAsync**: Reads one project by identity; null when absent.

**ListProjectsAsync**: Lists all projects ordered by name and then identity. SQLite cannot order by a
`DateTimeOffset`, so the creation time is not used for ordering.

**ReviewExistsAsync**: Reports whether a review with the identifier exists in a project.

**AddReviewAsync**: Stores a review, all its files, and its checklist snapshot (when it has one) in one save, which
is one transaction.

- *Postconditions*: Either the review, all its files, and its snapshot are stored, or nothing is. An identifier that already
  exists in the project, whether found by an earlier check or by a race, is reported as a `DomainRuleException`
  for `ReviewIdDuplicate`. A review for a project that does not exist is not stored; the relationship constraint
  of the database refuses it.

**GetReviewAsync**: Reads one review with the metadata of its files (path, size, and hash) ordered by path, and
without file bytes, so that showing a review with large files does not load them; null when absent.

**ListReviewsAsync**: Lists the reviews of a project ordered by identifier, without files.

**GetFileContentAsync**: Reads the bytes of one file of a review, exactly as stored; null when absent.

**AddThreadAsync**: Stores a thread together with its first comment in one save, which is one transaction. Fresh
entities are copied from the argument, so identities supplied by the caller are ignored and the database assigns
them. A thread for a file that does not exist in the review is refused by the relationship constraint of the
database.

**AddCommentAsync**: Appends a comment to a thread. It first checks that the thread belongs to the stated project
and review and returns null when it does not, so a thread identity from another review cannot be commented on.

**SetThreadStateAsync**: Sets the state of a thread of the stated review with a single update statement and
returns whether a thread was found.

**ListThreadsAsync**: Lists the threads of one review, for every file, with their comments. Threads and comments
are ordered by identity, which grows with the order of creation; the creation time is not used for ordering because
SQLite cannot order by a `DateTimeOffset`. All threads and comments of the review are read at once: there is no cap
on their number and no pagination, which is deferred until real volumes are known.

The bytes of files are stored and returned verbatim, including empty files and files containing carriage
returns or zero bytes.

**GetChecklistAsync**: Reads the checklist snapshot of a review; null when the review has none.

**ListChecklistSectionStatesAsync** and **ListChecklistAnswersAsync**: List the stored section states and the
answers of a review, with names and UTC times as saved.

**SetChecklistSectionStateAsync** and **SetChecklistAnswerAsync**: Replace the state of a section or the answer to
a check. Each first tries to overwrite the existing row with one update statement and adds a row only when there
was none; if two requests add the same row at the same moment, the loser of the race overwrites instead, so
there is always exactly one row and neither request fails.

#### Error Handling

A duplicate review identifier is detected from the SQLite primary key and unique constraint error codes (1555 and
2067) and reported as the domain rule; this is the one provider-specific piece of knowledge in the unit. Any other
database failure propagates unchanged to the caller. Null arguments raise `ArgumentNullException`.

#### Dependencies

- **ReviewCenterDbContext** — the schema and the context factory.
- **Core domain types and IReviewStore** — the types stored and the interface implemented.
- **Entity Framework Core and Microsoft.Data.Sqlite** — queries and the error code of a constraint violation.

#### Callers

- **ProjectService**, **ReviewService**, **ThreadService**, and **ChecklistService** — through the Core interface.
- **ServerHost** — registers the store as the implementation of the Core interface.
