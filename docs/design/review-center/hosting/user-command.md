### UserCommand

![Hosting Structure](HostingView.svg)

#### Purpose

`UserCommand` is the `user add` verb. Its single responsibility is to create a user in the configured database from the
command line, which is how the first administrator is created before anyone can sign in to the web server.

#### Data Model

`UserCommand` is a stateless static class, so it is thread-safe.

**PasswordVariable**: `string` (constant) — `REVIEWCENTER_PASSWORD`; the environment variable that supplies the
password.

**Password sources**: the password is never taken from the command line, where it would be visible in the process list
and in the shell history. It is read from the first of these that applies:

1. the `REVIEWCENTER_PASSWORD` environment variable, when it is set and not empty (for scripts and containers);
2. the first line of standard input, when standard input is redirected (for a pipe);
3. an interactive prompt that does not echo the typed characters, asked twice ("Password" and "Repeat password"),
   because a typo in the only password of the first administrator would lock the operator out.

The password is used as it is, so the line read from standard input is not trimmed. Neither the password nor its hash
is printed or logged.

#### Key Methods

**Run**: Creates the user described by the command-line context.

- *Parameters*: `Context context` — carries the user name, the display name, and the role (`Reviewer` unless
  `--role Admin` was given) and receives the results and errors; `Func<string?>? passwordSource` — supplies the
  password, `null` for the real sources above (tests pass a fixed value); `Action<ConfigurationManager>? configure` —
  adjusts the configuration before the services are built (tests use it to select a temporary database).
- *Returns*: `void`.
- *Preconditions*: `context` is not null.
- *Postconditions*: The user exists and a line "Created user '{name}' with the role {role}." is written; otherwise an
  error has been reported through `Context.WriteError`, which makes the exit code 1.

The password is read first, so that a missing one is reported before the database is touched. The command then builds
its configuration and services the same way the server does: the same `appsettings.json` and environment variables
(for example `Database__ConnectionString`) and the same registration (`ServerHost.AddServices`), with the host logging
switched off because the command reports through the context. It calls `DatabaseSetup.EnsureCreated`, so the database
is created when it does not exist yet, and then `UserService.CreateAsync`, so every user rule applies. A database that
was created before identity existed has no users table and must be deleted and created again (see the `Storage`
design).

#### Error Handling

Failures that the operator can fix are reported through `Context.WriteError` and end the command: no password
("No password was provided."), a violated user rule (the message of the `DomainRuleException`, for example a name that
exists), and a database that cannot be prepared ("Failed to prepare the database: {detail}"; a database error, a file
system error, or an access error). The two entries of the interactive prompt that differ raise
`InvalidOperationException` ("The passwords do not match."), which `Program.Main` reports as an error with exit code

1. Any other exception propagates to `Program.Main`.

#### Dependencies

- **Context** — supplies the user name, display name, and role, and receives the results and errors.
- **ServerHost** — `AddServices` registers the persistence and the Core services for the command.
- **DatabaseSetup** — creates the database when needed.
- **UserService** — creates the user.
- **ASP.NET Core shared framework** — `Host.CreateApplicationBuilder` for the configuration and the services.
- **Entity Framework Core** — the database context factory registration.

#### Callers

- **Program** — calls `Run` when the `user add` verb is present.
- **Tests** — call `Run` with a fixed password source and a temporary database, and start the real program with
  the environment variable and with standard input.
