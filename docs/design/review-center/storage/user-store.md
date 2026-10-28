### UserStore

![Storage Structure](StorageView.svg)

#### Purpose

`UserStore` implements the Core interface `IUserStore` on top of `ReviewCenterDbContext`. Its single responsibility is
to persist users and read them back, translating the unique user name constraint of the database into the terms of the
domain. The system administrator flag and the must-change-password flag of a user are ordinary stored columns that
`AddAsync` stores and `UpdateAsync` replaces; a user has no role of any other kind, because the project roles are
memberships (see `ReviewStore`). `AddToProjectAsync` creates a user and a membership together.

#### Data Model

The unit is stateless apart from the injected context factory and the shared `AuditAppender`. Every read creates its own
short-lived context from the factory and disposes it before returning, so concurrent operations never share a context.
Reads use no change tracking, so every read returns a new `User` object. Every change goes through
`AuditAppender.ExecuteAsync`, so it is serialized by the process-wide audit gate and stored in a `Serializable`
transaction together with its audit entry.

The user name is unique without regard to case: the column has the case-insensitive collation of SQLite, and the unique
index on it uses that collation, so the same collation also makes every lookup by user name case-insensitive (see
`ReviewCenterDbContext`).

The issuer and the subject of an account of an external identity provider (see `UserService`) are stored in two
nullable columns of the user, both `null` for a local account. The columns keep the default binary collation, so the
lookup compares exactly (case-sensitive), unlike the user name.

#### Key Methods

**AddAsync**: Stores a user and returns it with the identity assigned by the database. The unit copies the fields of
the argument (including the must-change-password flag and the external issuer and subject) into a new entity through
the private `InsertAsync`, so an
identity supplied by the caller is ignored and can neither be persisted nor
collide with an existing user; the returned user is the stored entity. The audit draft is built from the stored user (so
it can name the assigned identity) and saved in the same transaction. A user name that already exists, in any
capitalization, is reported as a `DomainRuleException` for `UserNameTaken`, and nothing is stored. A second user with the
same issuer and subject violates the unique index of the pair and is reported the same way.

**AddToProjectAsync**: Stores a user and the membership of that user in a project, with the audit entry built from the
stored user, in one audit transaction: the user is inserted and saved (so the identity is known), the membership is
added to the same context, and both are committed with the entry. When the user name exists the whole transaction is
rolled back and `UserNameTaken` is reported; when the project does not exist the foreign key of the membership refuses
it and nothing is stored either. A user therefore never exists without the membership it was created for.

**GetByUsernameAsync**: Reads one user by user name, without regard to case; null when absent.

**GetByExternalAsync**: Reads the user of an identity at an external provider by the exact pair of issuer and subject;
null when there is none. Nothing else (no name, no e-mail address) is matched. A null issuer or subject raises
`ArgumentNullException`.

**ListAsync**: Lists all users ordered by user name and then identity.

**UpdateAsync**: Stores the changed state of an existing user. The user is a detached copy that was read earlier, and
the update replaces every stored column of that row (identity, user name, and creation time do not change in
practice, because the domain type does not let them change). The audit draft is saved in the same transaction; a null
draft means the change is not audited here (the failure counter and the lockout state, which have their own events),
but the update still passes through the gate, so that all writes stay serialized.

#### Error Handling

A duplicate user name, and a duplicate pair of external issuer and subject, are detected from the SQLite unique
constraint error code (2067) and reported as the domain rule; like `ReviewStore`, the unit holds this
provider-specific knowledge. Any other database failure
propagates unchanged to the caller. Null arguments raise `ArgumentNullException`. The password hash is stored and read
but never logged.

#### Dependencies

- **ReviewCenterDbContext** — the schema and the context factory.
- **AuditAppender** — runs every change together with its audit entry.
- **Core domain types and IUserStore** — the `User` type stored and the interface implemented.
- **Entity Framework Core and Microsoft.Data.Sqlite** — queries and the error code of a constraint violation.

#### Callers

- **ProjectService** and **UserService** — through the Core interface (`UserService` also finds an external account
  with `GetByExternalAsync`).
- **ServerHost** — registers the store as the implementation of the Core interface.
