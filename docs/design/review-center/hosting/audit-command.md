### AuditCommand

![Hosting Structure](HostingView.svg)

#### Purpose

`AuditCommand` is the `audit verify` verb. Its single responsibility is to verify the hash chain of the audit trail in
the configured database from the command line and to report the result through the exit code, so that scripts and
scheduled jobs can check the trail without opening the web interface and can alert when it is broken. It never changes
the trail.

#### Data Model

`AuditCommand` is a stateless static class, so it is thread-safe. It has no password and no other input than the
Database settings: the database it reads is chosen the same way as for the server and for `user add`.

**Output**: an intact chain prints `Audit chain intact: N entries` (N is the number of entries) through
`Context.WriteLine` and the exit code stays 0. A broken chain prints `Audit chain BROKEN at entry N` (N is the first bad
entry) through `Context.WriteError`, which makes the exit code 1. A database that cannot be read prints
`Failed to read the audit trail: {detail}` through `Context.WriteError`, exit code 1.

The command prints the number of entries but, unlike the `Audit` page, **not** the head hash. Scripts that anchor the
head hash outside the database should read it from the page or from a database query; this slice does not record it
anywhere.

#### Key Methods

**Run**: Verifies the audit trail described by the command-line context.

- *Parameters*: `Context context` — receives the results and the errors; `Action<ConfigurationManager>? configure` —
  adjusts the configuration before the services are built (tests use it to select a temporary database).
- *Returns*: `void`.
- *Preconditions*: `context` is not null.
- *Postconditions*: One line is written, as described above; the exit code of the context says whether the chain is intact.

The command builds its configuration and services the same way the server and `user add` do: the same `appsettings.json`
and environment variables (for example `Database__ConnectionString`) and the same registration
(`ServerHost.AddServices`), with the host logging switched off because the command reports through the context. It calls
`DatabaseSetup.EnsureCreated`, so a database that does not exist yet is created empty (an empty trail is intact), and then
`AuditService.VerifyAsync`, which streams the trail in batches (see `AuditService`). A database created before the
audit trail existed has no `AuditEntries` table and must be deleted and created again (see the `Storage` design).

As documented for `AuditChain`, a successful result means that nothing in the stored history was changed, removed from
the middle, inserted, or reordered. It does not detect the removal of the newest entries or a complete rewrite by
someone with write access to the database; compare the reported count (and the head hash from the `Audit` page) with a
value recorded earlier outside the database for that.

#### Error Handling

A broken chain is not an exception: it is reported as an error with exit code 1. A database error, a file system
error, or an access error while preparing or reading the database is reported as
"Failed to read the audit trail: {detail}" with exit code 1. Any other exception propagates to `Program.Main`. A null
context is rejected with `ArgumentNullException`.

#### Dependencies

- **Context** — receives the results and errors.
- **ServerHost** — `AddServices` registers the persistence and the Core services for the command.
- **DatabaseSetup** — creates the database when needed.
- **AuditService** — verifies the chain.
- **ASP.NET Core shared framework** — `Host.CreateApplicationBuilder` for the configuration and the services.
- **Entity Framework Core** — the database context factory registration.

#### Callers

- **Program** — calls `Run` when the `audit verify` verb is present.
- **Tests** — call `Run` with a temporary database that is intact, empty, edited, or missing an entry, and with an
  unreachable database.
