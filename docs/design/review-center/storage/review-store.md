### ReviewStore

![Storage Structure](StorageView.svg)

#### Purpose

`ReviewStore` implements the Core store interface on top of `ReviewCenterDbContext`. Its single responsibility is
to persist projects, reviews, and file content, and to read them back, translating database behavior into the
terms of the domain.

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

**AddReviewAsync**: Stores a review and all its files in one save, which is one transaction.

- *Postconditions*: Either the review and all its files are stored, or nothing is. An identifier that already
  exists in the project, whether found by an earlier check or by a race, is reported as a `DomainRuleException`
  for `ReviewIdDuplicate`. A review for a project that does not exist is not stored; the relationship constraint
  of the database refuses it.

**GetReviewAsync**: Reads one review with the metadata of its files (path, size, and hash) ordered by path, and
without file bytes, so that showing a review with large files does not load them; null when absent.

**ListReviewsAsync**: Lists the reviews of a project ordered by identifier, without files.

**GetFileContentAsync**: Reads the bytes of one file of a review, exactly as stored; null when absent.

The bytes of files are stored and returned verbatim, including empty files and files containing carriage
returns or zero bytes.

#### Error Handling

A duplicate review identifier is detected from the SQLite primary key and unique constraint error codes (1555 and
2067) and reported as the domain rule; this is the one provider-specific piece of knowledge in the unit. Any other
database failure propagates unchanged to the caller. Null arguments raise `ArgumentNullException`.

#### Dependencies

- **ReviewCenterDbContext** — the schema and the context factory.
- **Core domain types and IReviewStore** — the types stored and the interface implemented.
- **Entity Framework Core and Microsoft.Data.Sqlite** — queries and the error code of a constraint violation.

#### Callers

- **ProjectService** and **ReviewService** — through the Core interface.
- **ServerHost** — registers the store as the implementation of the Core interface.
