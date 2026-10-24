# ReviewCenter

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]

ReviewCenter is a DEMA Consulting ASP.NET Core web service, with a companion command-line interface,
for performing formal code reviews of file sets against earlier revisions. It supports threaded review
comments on code blocks and files, project-specific pass/fail/N/A checklists, primary and alternate
reviewers, and re-authenticated electronic sign-off.

The `serve` verb runs ReviewCenter as an ASP.NET Core Blazor Server web application with sign-in, a home page, a
health endpoint, projects, draft reviews made from uploaded files, threaded review comments on those files, and a
checklist for each review. Every comment and checklist answer is recorded under the signed-in user.
Reviewers and sign-off are still under development.

> **Warning: databases created by an earlier build must be recreated.** The user accounts add tables and columns
> that an earlier development database does not have, and the database is created by `EnsureCreated` without
> migrations. Delete the old database file before starting this version (development databases only).

## Features

The current release provides:

- **Blazor Server Web Application**: `reviewcenter serve` hosts the web interface (home page at `/`) and a
  `/health` endpoint, listening on `http://localhost:5000` by default or on the address given with `--urls`
- **Sign-in and Users**: local accounts with a user name and a password; every page except the home page, the
  login page, and `/health` needs a signed-in user; the roles are **Admin** (manages users) and **Reviewer**; users
  sign in at `/login`, sign out from the page header, and change their own password at `/account`; an administrator
  creates users, enables and disables them, changes roles, and sets passwords at `/users`; the first administrator
  is created with the `user add` command (see [Users and Sign-in](#users-and-sign-in))
- **Projects**: create projects and list them at `/projects`; each project has its own page at `/projects/{id}`
- **Draft Reviews**: create a review in a project by uploading a set of files; the review page shows the files and
  lets you read each file's content as numbered lines; a collapsed Details section holds the review id, state, the
  SHA-256 hash of each file, and a content fingerprint that is calculated exactly as the
  [ReviewMark][link-reviewmark] tool calculates it
- **Review Comments**: on the review page, start a comment thread on a whole file or on a line range, reply to it,
  and mark it resolved or reopen it; the author is the signed-in user; a next-step line says what is left to do, a
  thread index of all files (open by default, or all, or resolved) jumps to the thread's lines, resolved threads are
  collapsed to one line, and the file list shows the open threads per file
- **Review Checklist**: every new review gets a checklist of 14 sections and 58 checks, taken from the
  [Continuous Compliance review template][link-review-template]; a checklist page (linked from the review page,
  with a summary such as "37 of 58 checks answered") shows the sections with their progress, one section open at a
  time, and a Pass, Fail, or N/A choice for each check, recorded under the signed-in user; a Fail or N/A needs a
  justification, a section can be marked not applicable with a reason, and a "next unanswered check" button leads
  you on; the review page's next-step line counts the unanswered checks together with the open threads
- **Persistent Storage**: users, projects, reviews, the uploaded file contents, the comment threads, and the
  checklists are kept in a SQLite database file
  (default `reviewcenter.db` in the per-user application data folder, see
  [Data Location](#data-location-and-database-settings))
- **Standardized Command-Line Interface**: Context class handling common arguments
  (`serve`, `user add`, `--urls`, `--version`, `--help`, `--silent`, `--validate`, `--results`, `--depth`, `--log`)
- **Self-Validation**: Built-in validation tests with TRX/JUnit output
- **Multi-Platform Support**: Self-contained single-file executables for Windows (win-x64) and Linux (linux-x64)
- **Modern .NET**: Built on .NET 10 (`net10.0`); the runtime is bundled, so no installation is needed
- **Puppet Developer Tool**: A small in-repository helper for AI agents that drives one persistent headless browser
  one command at a time and takes screenshots (see `tools/puppet/README.md`); not part of the distributed executables
- **Comprehensive CI/CD**: GitHub Actions workflows with quality checks, builds, and
  integration tests of the published executables
- **Linting Enforcement**: markdownlint, cspell, and yamllint enforced on every CI run
- **Continuous Compliance**: Compliance evidence generated automatically on every CI run, following
  the [Continuous Compliance][link-continuous-compliance] methodology
- **SonarCloud Integration**: Quality gate and security analysis on every build
- **Documentation Generation**: Automated build notes, user guide, code quality reports,
  requirements, justifications, and trace matrix
- **Requirements Traceability**: Requirements linked to passing tests with auto-generated trace matrix

## Installation

ReviewCenter is distributed as self-contained single-file executables for **win-x64** and **linux-x64**. The .NET
runtime is bundled, so nothing else needs to be installed. Download the archive for your platform from the
[GitHub Releases][link-releases] page:

- `reviewcenter-<version>-win-x64.zip` (Windows)
- `reviewcenter-<version>-linux-x64.tar.gz` (Linux)

Each archive has a matching `.sha256` checksum file. Verify the download, extract it, and start the server:

```bash
# Linux: verify (in the download folder), extract, and run
sha256sum -c reviewcenter-<version>-linux-x64.tar.gz.sha256
tar -xzf reviewcenter-<version>-linux-x64.tar.gz
./reviewcenter-<version>-linux-x64/reviewcenter serve
```

```powershell
# Windows: verify (compare with the value in the .sha256 file), extract, and run
Get-FileHash reviewcenter-<version>-win-x64.zip -Algorithm SHA256
Expand-Archive reviewcenter-<version>-win-x64.zip -DestinationPath .
.\reviewcenter-<version>-win-x64\reviewcenter.exe serve
```

Keep the files of the archive together: the executable serves the web assets that sit beside it, and the native
SQLite library (`e_sqlite3.dll` on Windows, `libe_sqlite3.so` on Linux) is a separate file that must stay in the
same folder as the executable, so do not copy the executable on its own. No release has been
published yet; until one is, build from source with the .NET 10 SDK:

```bash
dotnet build
dotnet run --project src/DemaConsulting.ReviewCenter -- serve
```

The command-line examples below use `reviewcenter`, the command name of the published executable (put its folder on
your `PATH` or run it by path); when running from source, replace `reviewcenter` with
`dotnet run --project src/DemaConsulting.ReviewCenter --`.

## Usage

```bash
# Default behavior (no arguments) — displays banner and copyright
reviewcenter

# Display version
reviewcenter --version

# Display help
reviewcenter --help

# Run the web server (default http://localhost:5000; home page at /, projects at /projects, health at /health)
reviewcenter serve

# Run the web server on another address
reviewcenter serve --urls http://localhost:8080

# Create the first administrator (the password is read from REVIEWCENTER_PASSWORD, from standard input, or prompted)
reviewcenter user add --username admin --display-name "Ada Admin" --role Admin

# Run self-validation
reviewcenter --validate

# Save validation results (--result is an accepted alias for --results)
reviewcenter --validate --results results.trx
reviewcenter --validate --result results.xml

# Set heading depth for embedded validation output
reviewcenter --validate --depth 2

# Silent mode with logging
reviewcenter --silent --log output.log
```

## Command-Line Options

| Option                                | Description                                                  |
| ------------------------------------- | ------------------------------------------------------------ |
| `serve`                               | Run the ReviewCenter web server until stopped                |
| `--urls <url>`                        | Listen URL for `serve` (default: `http://localhost:5000`)    |
| `user add`                            | Create a user; see Users and Sign-in below                   |
| `--username <name>`                   | User name for `user add` (3-50 characters)                   |
| `--display-name <text>`               | Display name for `user add`                                  |
| `--role <Admin\|Reviewer>`            | Role for `user add` (default: `Reviewer`)                    |
| `-v`, `--version`                     | Display version information                                  |
| `-?`, `-h`, `--help`                  | Display help message                                         |
| `--silent`                            | Suppress console output                                      |
| `--validate`                          | Run self-validation                                          |
| `--results <file>`, `--result <file>` | Write results to `.trx` (TRX) or `.xml` (JUnit XML) file.    |
| `--depth <#>`                         | Set heading depth for markdown output (default: 1)           |
| `--log <file>`                        | Write output to log file                                     |

## Users and Sign-in

Users are local accounts stored in the database. A password is stored only as a salted hash. The two roles are
**Admin**, who can also manage users at `/users`, and **Reviewer**. The home page, the login page, and `/health` are
open to everyone; every other page sends a user who is not signed in to the login page and back again after sign-in.

A new database has no users, so create the first administrator with the `user add` command, which writes directly to
the database (the server may be running). The password is never taken from a command-line option. It is
read from the first source that has it:

1. the `REVIEWCENTER_PASSWORD` environment variable,
2. the first line of standard input, when input is redirected,
3. an interactive prompt (masked, asked twice).

```bash
# Linux: from the environment
REVIEWCENTER_PASSWORD='a long passphrase' reviewcenter user add --username admin --display-name "Ada Admin" --role Admin

# Windows PowerShell: from standard input
"a long passphrase" | .\reviewcenter.exe user add --username admin --display-name "Ada Admin" --role Admin

# Prompted (any platform, interactive)
reviewcenter user add --username admin --display-name "Ada Admin" --role Admin
```

The command prints `Created user 'admin' with the role Admin.` or an error and a non-zero exit code, for example
when the user name already exists. After that the administrator creates the other users at `/users`.

Rules:

- **User name**: 3 to 50 letters, digits, `.`, `_`, or `-`; unique regardless of letter case.
- **Password**: 12 to 128 characters, no other composition rules.
- **Lockout**: after 5 failed sign-ins in a row a user name is locked for 5 minutes; a successful sign-in resets the
  count.
- **Last administrator**: the last enabled administrator cannot be disabled or demoted.
- **Sign-in session**: a session cookie that lasts 8 hours of activity by default; change it with the `Auth` setting
  below.

| Setting             | Environment variable | Meaning                                                  |
| ------------------- | -------------------- | -------------------------------------------------------- |
| `Auth:SessionHours` | `Auth__SessionHours` | Hours of sign-in session (default 8; must be above 0)    |

The sign-in cookie is marked Secure when the request arrives over HTTPS. Behind a reverse proxy that ends TLS, set
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so that the server sees the original HTTPS scheme and marks the cookie
Secure.

## Data Location and Database Settings

ReviewCenter keeps its data in one SQLite database file, which is created (with its folder and tables) on first
start. The default file is `reviewcenter.db` in a `ReviewCenter` folder of the per-user application data folder
(`%LOCALAPPDATA%\ReviewCenter` on Windows, `~/.local/share/ReviewCenter` on Linux). If the account has no such
folder, a `data` folder beside the executable is used instead. Back up the data by copying that file while the
server is stopped.

The database is configured in the `Database` section of `appsettings.json` beside the executable, or with
environment variables, which take precedence:

| Setting                     | Environment variable         | Meaning                                         |
| --------------------------- | ---------------------------- | ----------------------------------------------- |
| `Database:Provider`         | `Database__Provider`         | Database provider; only `Sqlite` is supported   |
| `Database:ConnectionString` | `Database__ConnectionString` | Database file path or SQLite connection string  |

```powershell
# Windows: keep the data in another file
$env:Database__ConnectionString = "D:\reviews\reviewcenter.db"
.\reviewcenter.exe serve
```

If the database cannot be opened or the provider is not supported, the server prints an error and exits with a
non-zero exit code.

## Current Limitations

- **Basic local accounts only**: there is no password reset, no email, no multi-factor sign-in, and no external
  identity provider. An administrator sets a new password for a user who forgot it. Serve over HTTPS (for example
  behind a reverse proxy) when the server is reachable from other machines.
- **Lockout is per user name**: repeated failed sign-ins lock the user name, not the source address, so someone who
  guesses wrongly on purpose can keep a user locked out for 5 minutes at a time.
- **Sessions are not cut short at once**: a page that is already open keeps its sign-in state until it is reloaded,
  and changing a password does not end the user's other sessions. Disabling a user or changing a role takes effect
  on the next page load. The exception is user administration: every action there is re-checked and refused for a
  user who is no longer an enabled administrator.
- **Checklists are fixed per review**: every review gets a copy of the default checklist when it is created and
  keeps it; there is no project-level checklist editor or import yet. Checklist answers are not linked to comment
  threads.
- **Comments cannot be edited or deleted**: a posted comment is permanent; only the thread state (open or resolved)
  can be changed.
- **Comments are not live**: comments posted by other people appear when you reload the page or take an action on it.
- **Upload limits**: at most 200 files per review, 2 MiB per file, and 20 MiB per review. Only the file name of each
  uploaded file is kept, so two files with the same name cannot be in one review.
- **No database migrations**: the database tables are created when the database file is new; a later release that
  changes the tables will need a migration step. A database file created by an earlier version, before users,
  comments, or checklists existed, does not have the tables or columns that are needed, so **delete it and start
  again** (development databases only).

## Error Handling

Unrecognized arguments cause the tool to print an error message to standard error and exit
with a non-zero exit code. For example:

```text
Error: Unsupported argument '--unknown'
```

This behavior enables CI/CD pipelines to detect and surface misconfiguration failures
automatically.

## Self Validation

Running self-validation produces a report containing the following information:

```text
# DEMA Consulting ReviewCenter

| Information         | Value                                              |
| :------------------ | :------------------------------------------------- |
| Tool Version        | <version>                                          |
| Machine Name        | <machine-name>                                     |
| OS Version          | <os-version>                                       |
| DotNet Runtime      | <dotnet-runtime-version>                           |
| Time Stamp          | <timestamp> UTC                                    |

✓ ReviewCenter_VersionDisplay - Passed
✓ ReviewCenter_HelpDisplay - Passed

Total Tests: 2
Passed: 2
Failed: 0
```

Each test in the report proves:

- **`ReviewCenter_VersionDisplay`** - `--version` outputs a valid version string.
- **`ReviewCenter_HelpDisplay`** - `--help` outputs usage and options information.

Use `--depth <#>` to control the heading level of the validation output (default: `1`).
This is useful when embedding validation output into a larger markdown document:

```bash
# Embed validation at heading level 2
reviewcenter --validate --depth 2
```

See the [User Guide][link-guide] for more details on the self-validation tests.

On validation failure the tool will exit with a non-zero exit code.

## For AI Agents

`tools/puppet` holds **Puppet**, a small helper that lets an AI agent drive the web interface and take screenshots
(not part of the distributed executables). See `tools/puppet/README.md`.

## Documentation

Generated documentation includes:

- **Build Notes**: Release information and changes
- **User Guide**: Comprehensive usage documentation
- **Code Quality Report**: CodeQL and SonarCloud analysis results
- **Requirements**: Functional and non-functional requirements
- **Requirements Justifications**: Detailed requirement rationale
- **Trace Matrix**: Requirements to test traceability

## TODO

Planned but not yet implemented:

- The remaining review features of the ReviewCenter web service: reviewers, sign-off,
  and review states beyond Draft
- Password reset, multi-factor sign-in, and external identity providers
- EF Core database migrations with an explicit `migrate` command, and further database providers
  (PostgreSQL, SQL Server)
- PDF/A evidence export of review records
- AI-assisted review (planned to use an Azure OpenAI Service account)

## Contributing

See [CONTRIBUTING.md](https://github.com/demaconsulting/ReviewCenter/blob/main/CONTRIBUTING.md) for
guidelines on reporting bugs, suggesting features, and submitting pull requests.

## License

Copyright (c) DEMA Consulting. Licensed under the MIT License. See [LICENSE][link-license] for details.

By contributing to this project, you agree that your contributions will be licensed under the MIT License.

<!-- Badge References -->
[badge-forks]: https://img.shields.io/github/forks/demaconsulting/ReviewCenter?style=plastic
[badge-stars]: https://img.shields.io/github/stars/demaconsulting/ReviewCenter?style=plastic
[badge-contributors]: https://img.shields.io/github/contributors/demaconsulting/ReviewCenter?style=plastic
[badge-license]: https://img.shields.io/github/license/demaconsulting/ReviewCenter?style=plastic
[badge-build]: https://img.shields.io/github/actions/workflow/status/demaconsulting/ReviewCenter/build_on_push.yaml?style=plastic
[badge-quality]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_ReviewCenter&metric=alert_status
[badge-security]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_ReviewCenter&metric=security_rating

<!-- Link References -->
[link-forks]: https://github.com/demaconsulting/ReviewCenter/network/members
[link-stars]: https://github.com/demaconsulting/ReviewCenter/stargazers
[link-contributors]: https://github.com/demaconsulting/ReviewCenter/graphs/contributors
[link-license]: https://github.com/demaconsulting/ReviewCenter/blob/main/LICENSE
[link-build]: https://github.com/demaconsulting/ReviewCenter/actions/workflows/build_on_push.yaml
[link-quality]: https://sonarcloud.io/dashboard?id=demaconsulting_ReviewCenter
[link-security]: https://sonarcloud.io/dashboard?id=demaconsulting_ReviewCenter
[link-releases]: https://github.com/demaconsulting/ReviewCenter/releases
[link-reviewmark]: https://github.com/demaconsulting/ReviewMark
[link-guide]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/introduction.md
[link-continuous-compliance]: https://github.com/demaconsulting/ContinuousCompliance
[link-review-template]: https://github.com/demaconsulting/ContinuousCompliance/blob/main/docs/review-template/review-template.md
