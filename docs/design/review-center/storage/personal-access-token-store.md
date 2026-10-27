### PersonalAccessTokenStore

![Storage Structure](StorageView.svg)

#### Purpose

`PersonalAccessTokenStore` implements the Core interface `IPersonalAccessTokenStore` on top of `ReviewCenterDbContext`.
Its single responsibility is to persist personal access tokens (their hashes, never their values) and read them back,
with the audit entry of every change stored in the same transaction.

#### Data Model

The unit is stateless apart from the injected context factory and the shared `AuditAppender`. Every read creates its own
short-lived context from the factory and disposes it before returning. A change goes through `AuditAppender.ExecuteAsync`,
so it is serialized by the process-wide audit gate and stored in a `Serializable` transaction together with its audit
entry. The table has a unique index on the hash, an index on the owner, and a foreign key to the user with cascade
delete (see `ReviewCenterDbContext`).

#### Key Methods

**AddAsync**: Stores a token and the audit entry built from it.

- *Parameters*: The token and a function that builds the audit draft from the stored token (so the entry can name the
  assigned identity and the display id).
- *Returns*: The stored token.
- *Postconditions*: The token and the audit entry exist together, or neither does. The unit copies the fields into a new
  entity, so the database assigns the identity. A hash that is already stored is refused by the unique index, as a
  database failure, and nothing is audited.

**ListForUserAsync**: Lists the tokens of one user, newest first. The read is untracked. The order is made in memory,
by the creation time and then the identity, because SQLite cannot order a time with an offset.

**FindByHashAsync**: Reads one token by its hash with the owner loaded, or null. It uses the unique index.

**RevokeAsync**: Marks a token as revoked.

- *Parameters*: The identity of the token, the identity of the owner, the time, and the function that builds the audit
  draft.
- *Returns*: The revoked token, or null when no such token of that owner exists or it is already revoked (nothing is
  audited then).
- *Postconditions*: The revocation time and the audit entry are stored together.

**TouchAsync**: Sets the last-used time of a token, but only if the stored value is still the one the caller read.

- *Parameters*: The identity of the token, the last-used time the caller read (null for a token never used), the new
  time, and a cancellation token.
- *Returns*: True when the time was stored, false when another caller changed it first.
- *Postconditions*: One conditional update statement (`WHERE Id = @id AND LastUsed = @expected`) changes that column only,
  so of several concurrent callers that read the same value exactly one writes, and a revocation that happens at the
  same time is never overwritten. The condition is an equality because the SQLite provider cannot compare
  `DateTimeOffset` values with `<` or `>`. It is not audited and does not use the audit gate.

#### Error Handling

Any database failure propagates unchanged to the caller. Null arguments raise `ArgumentNullException`. The hash is stored
and read but never logged, and the value of a token never reaches this unit.

#### Dependencies

- **ReviewCenterDbContext** — the schema and the context factory.
- **AuditAppender** — runs every change together with its audit entry.
- **Core domain types and IPersonalAccessTokenStore** — the `PersonalAccessToken` type stored and the interface
  implemented.
- **Entity Framework Core** — queries and the single-column update.

#### Callers

- **PersonalAccessTokenService** — through the Core interface.
- **ServerHost** — registers the store as the implementation of the Core interface.
