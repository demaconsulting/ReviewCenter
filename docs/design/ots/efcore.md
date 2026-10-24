## Entity Framework Core

This document describes the integration and usage design for the `Entity Framework Core` OTS software item,
together with its SQLite provider (`Microsoft.EntityFrameworkCore.Sqlite`) and the SQLite data provider
(`Microsoft.Data.Sqlite`) that comes with it.

### Purpose

Entity Framework Core is the object-relational mapper of the `Storage` subsystem. It maps the Core domain types to
database tables, creates the schema, translates queries into SQL, and saves changes in transactions. The SQLite
provider lets the service keep its data in a single file without a database server. The library is a runtime
dependency of the distributed ReviewCenter and is also used by the tests, which run against real SQLite
databases rather than a substitute.

### Features Used

- Model configuration through the fluent API: keys (including composite keys), required values, maximum lengths,
  relationships with cascading delete, and conversion of an enumeration to text
- A context factory (`IDbContextFactory`), so each operation has its own short-lived context
- Queries with LINQ, without change tracking for reads, and projection of selected columns
- Saving a new object graph (a review with its files) in one transaction
- Schema creation with `EnsureCreated`; migrations are not used
- The SQLite provider, its connection string builder, and the extended error codes of constraint violations
- The native SQLite library, which the provider package brings as `e_sqlite3`

### Integration Pattern

The package `Microsoft.EntityFrameworkCore.Sqlite` is a reference of the main project, and `Core` has no
reference to it. `ServerHost` registers the context factory, and `DatabaseSetup` is the only code that selects
the provider, so a further provider is added in one place. The tests use temporary SQLite database files in a
private temporary folder, which is removed after the test run.

The native SQLite library cannot be embedded in the single-file executable, so the publish step places it as a
side file next to the executable (`e_sqlite3.dll` on Windows, `libe_sqlite3.so` on Linux). The README and the
user guide tell operators to keep it beside the executable. Entity Framework Core has no self-validation mode;
the passing storage tests that save and read back data from a real database are the integration evidence.
