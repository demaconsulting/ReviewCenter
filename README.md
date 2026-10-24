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

The `serve` verb runs ReviewCenter as an ASP.NET Core Blazor Server web application with a home page, a health
endpoint, projects, draft reviews made from uploaded files, and threaded review comments on those files.
Checklists, reviewers, and sign-off are still under development.

## Features

The current release provides:

- **Blazor Server Web Application**: `reviewcenter serve` hosts the web interface (home page at `/`) and a
  `/health` endpoint, listening on `http://localhost:5000` by default or on the address given with `--urls`
- **Projects**: create projects and list them at `/projects`; each project has its own page at `/projects/{id}`
- **Draft Reviews**: create a review in a project by uploading a set of files; the review page shows the files, the
  SHA-256 hash of each file, and a content fingerprint that is calculated exactly as the
  [ReviewMark][link-reviewmark] tool calculates it, and lets you read each file's content as numbered lines
- **Review Comments**: on the review page, start a comment thread on a whole file or on a line range, reply to it,
  and mark it resolved or reopen it; the file list shows the open threads per file and the page shows the open and
  resolved totals
- **Persistent Storage**: projects, reviews, the uploaded file contents, and the comment threads are kept in a
  SQLite database file
  (default `reviewcenter.db` in the per-user application data folder, see
  [Data Location](#data-location-and-database-settings))
- **Standardized Command-Line Interface**: Context class handling common arguments
  (`serve`, `--urls`, `--version`, `--help`, `--silent`, `--validate`, `--results`, `--depth`, `--log`)
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
| `-v`, `--version`                     | Display version information                                  |
| `-?`, `-h`, `--help`                  | Display help message                                         |
| `--silent`                            | Suppress console output                                      |
| `--validate`                          | Run self-validation                                          |
| `--results <file>`, `--result <file>` | Write results to `.trx` (TRX) or `.xml` (JUnit XML) file.    |
| `--depth <#>`                         | Set heading depth for markdown output (default: 1)           |
| `--log <file>`                        | Write output to log file                                     |

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

- **No authentication**: this release has a single anonymous operator. Anyone who can reach the server can create
  and read projects and reviews. Run it only on `localhost` or on a trusted network until user authentication is
  added.
- **Comment authors are free text**: because there is no sign-in yet, the author of a comment is a name that the
  reader types, and it is not verified. This is temporary until user authentication is added.
- **Comments cannot be edited or deleted**: a posted comment is permanent; only the thread state (open or resolved)
  can be changed.
- **Comments are not live**: comments posted by other people appear when you reload the page or take an action on it.
- **Upload limits**: at most 200 files per review, 2 MiB per file, and 20 MiB per review. Only the file name of each
  uploaded file is kept, so two files with the same name cannot be in one review.
- **No database migrations**: the database tables are created when the database file is new; a later release that
  changes the tables will need a migration step. A database file created by an earlier version, before comments
  existed, does not have the comment tables, so delete it and start again (development databases only).

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

- The remaining review features of the ReviewCenter web service: checklists, reviewers, sign-off,
  and review states beyond Draft
- User authentication
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
