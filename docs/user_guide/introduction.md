# Introduction

## Purpose

ReviewCenter is a web-based formal code review service with a companion command-line
interface. The service is an ASP.NET Core Blazor Server application started with the `serve`
verb. The current release lets you create projects and draft reviews made from uploaded files, shows the
content fingerprint of each review, and lets you discuss the files in threaded comments; the remaining review
features (checklists, reviewers, sign-off) are still under development. The command-line interface is documented in
this guide. A separate development tool,
Puppet, is described near the end of this guide.

## Scope

This user guide covers:

- Installation instructions
- Usage examples for common tasks
- Running the web server, creating projects and reviews, and configuring the database
- Command-line options reference
- The Puppet development tool
- Practical examples for various scenarios

# Continuous Compliance

ReviewCenter follows the
[Continuous Compliance](https://github.com/demaconsulting/ContinuousCompliance) methodology, which ensures
compliance evidence is generated automatically on every CI run.

## Key Practices

- **Requirements Traceability**: Every requirement is linked to passing tests, and a trace matrix is
  auto-generated on each release
- **Linting Enforcement**: markdownlint, cspell, and yamllint are enforced before any build proceeds
- **Automated Audit Documentation**: Each release ships with generated requirements, justifications,
  trace matrix, and quality reports
- **CodeQL and SonarCloud**: Security and quality analysis runs on every build

# Installation

ReviewCenter is distributed as self-contained single-file executables for win-x64 and linux-x64, so no .NET runtime
needs to be installed. Download the archive for your platform from the project's GitHub Releases page:

- `reviewcenter-<version>-win-x64.zip` (Windows)
- `reviewcenter-<version>-linux-x64.tar.gz` (Linux)

Each archive is accompanied by a `.sha256` checksum file. Verify the download before extracting it:

```bash
# Linux (run in the download folder)
sha256sum -c reviewcenter-<version>-linux-x64.tar.gz.sha256
tar -xzf reviewcenter-<version>-linux-x64.tar.gz
```

```powershell
# Windows: the printed hash must equal the value in the .sha256 file
Get-FileHash reviewcenter-<version>-win-x64.zip -Algorithm SHA256
Expand-Archive reviewcenter-<version>-win-x64.zip -DestinationPath .
```

Keep the extracted files together, because the executable serves the web assets that sit beside it, and the native
SQLite library (`e_sqlite3.dll` on Windows, `libe_sqlite3.so` on Linux) is a separate file that must stay in the
same folder as the executable. Either add the
extracted folder to your `PATH`, or run the executable by its path (`./reviewcenter-<version>-linux-x64/reviewcenter`
or `.\reviewcenter-<version>-win-x64\reviewcenter.exe`). Start the server with `reviewcenter serve`
(`reviewcenter.exe serve` on Windows) once it is on your `PATH`. No release has been published yet; until one is, build
it from source with the .NET 10 SDK:

```bash
dotnet build
dotnet run --project src/DemaConsulting.ReviewCenter -- serve
```

The examples in this guide use `reviewcenter`, the command name of the published executable; when running from
source, replace `reviewcenter` with `dotnet run --project src/DemaConsulting.ReviewCenter --`.

# Usage

## Display Version

Display the tool version:

```bash
reviewcenter --version
```

## Default Behavior

When invoked with no arguments, the tool displays the banner and copyright notice:

```bash
reviewcenter
```

## Display Help

Display usage information:

```bash
reviewcenter --help
```

## Run the Web Server

The `serve` verb starts the ReviewCenter web server and keeps it running until it is stopped with Ctrl+C:

```bash
reviewcenter serve
```

By default the server listens on `http://localhost:5000`. Use `--urls <url>` to listen on another address or
port; the value must be an absolute `http` or `https` URL:

```bash
reviewcenter serve --urls http://localhost:8080
```

The server provides these routes:

- `/` - the ReviewCenter home page. It shows the title `ReviewCenter`, a Hello button that displays a greeting
  when pressed, a counter button that increments a displayed count, and a link to the projects page.
- `/projects` - the list of projects, with a form to create a new project.
- `/projects/{id}` - one project, with its reviews and a form to create a review.
- `/projects/{id}/reviews/{reviewId}` - one review, with its files, their content, and comment threads.
- `/health` - a health check that answers with status 200 and the text `Healthy` while the server is running.

The `--urls` option is only valid together with `serve`; using it without `serve` is reported as an error.
The `--silent` option suppresses the server's console log output.

## Projects and Reviews

A project groups the reviews of one body of work. A review is a named set of files whose content is stored
unchanged, together with a fingerprint of that content.

1. Open `/projects`, enter a project name (up to 100 characters) and an optional description (up to 1000
   characters), and press the create button. The project appears in the list; select it to open its page.
2. On the project page, enter a review identifier (1 to 100 letters, digits, `.`, `_`, or `-`, starting with a
   letter or digit; unique within the project) and a title (up to 200 characters), choose the files to review, and
   press the create button. The chosen files are listed with their sizes before you submit.
3. The review page opens. It shows the identifier, title, and state (`Draft`; the only state in this release), the
   review fingerprint, and the files with their size, SHA-256 hash, and open comment threads. Select a file to read
   its content as numbered lines (see [Review Comments](#review-comments)). Binary
   files, and files that are not valid UTF-8 text, show a notice instead of their content.

The review fingerprint is calculated exactly as in the ReviewMark tool (see the "Fingerprinting" section of the
ReviewMark User Guide): each file is hashed with SHA-256, the hashes are sorted, and the sorted hashes are hashed
again. It depends only on the content of the files, not on their names or order, and it is not affected by line
ending conversion because the bytes are never changed.

Error messages (for example a blank name, an identifier already used in the project, or too many files) are shown
on the page, and nothing is created.

### Upload Limits

- At most 200 files per review, 2 MiB per file, and 20 MiB for all files of a review.
- Only the file name of each chosen file is kept (not its folder), so two files with the same name cannot be in the
  same review; the page reports this when you submit.

### Limitation: No Authentication

This release has a single anonymous operator: there are no user accounts or permissions, and anyone who can reach
the server can create and read all projects and reviews, and add comments to them. The author of a comment is a name
that you type; it is not verified, so anyone can use any name. Run the server only on `localhost` or on a trusted
network until authentication is added in a later release.

## Review Comments

Select a file on the review page to read it as numbered lines and to discuss it in comment threads. The file list
shows the number of open threads of each file, and the page shows the totals of open and resolved threads of the
whole review.

1. Enter your name in the "Your name" field (1 to 100 characters). It is used for every comment you post and stays
   filled in while you work on the page.
2. To start a thread, either press the Comment button of a line, which fills in that line, or type a first and a last
   line number yourself (a first line without a last line means that one line), or leave both line fields empty to
   comment on the whole file. The form shows where the comment will go. Enter the comment text (1 to 4000
   characters) and press the start button.
3. A thread is listed below the file with the lines it covers (the whole file, a line, or a range of lines) and its
   comments in the order they were posted. Lines that have a thread are marked.
4. Anyone can reply to a thread. Press the resolve button to mark a thread resolved, and the reopen button to open
   it again.

Lines are numbered from 1. Windows (CRLF) and Unix (LF) line endings count the same, a line ending at the very end
of a file does not add a line, and an empty file has no lines. A binary file, or a file that is not valid UTF-8, and
an empty file can be commented on as a whole only. A line number must be a whole number inside the file, and the
first line must not be after the last line. Leading and trailing spaces of the name and the text are removed. A
problem is shown as a message next to the form (or inside the thread for a reply) and nothing you typed is lost.

Posted comments cannot be edited or deleted, so the record of the discussion stays complete; only a thread's state
changes. Comments posted by other people appear when you reload the page or take an action on it. Comment text is
always shown as plain text; any markup in it is displayed as typed.

## Database

ReviewCenter keeps projects, reviews, file contents, and comment threads in a SQLite database file. On first start
it creates the
folder, the file, and the tables. The default file is `reviewcenter.db` in the `ReviewCenter` folder of the
per-user application data folder (`%LOCALAPPDATA%\ReviewCenter` on Windows, `~/.local/share/ReviewCenter` on
Linux); when the account has no such folder, the file is `data/reviewcenter.db` beside the executable. To back up
the data, stop the server and copy the file.

Configure the database in the `Database` section of the `appsettings.json` file that sits beside the executable, or
with environment variables (which take precedence):

| Setting                     | Environment variable         | Meaning                                                 |
| --------------------------- | ---------------------------- | ------------------------------------------------------- |
| `Database:Provider`         | `Database__Provider`         | Database provider; only `Sqlite` is supported           |
| `Database:ConnectionString` | `Database__ConnectionString` | Database file path or SQLite connection string          |

A value without an `=` sign is treated as a file path; an empty value selects the default file. An in-memory
SQLite database (`:memory:`) is not supported and is refused at startup. For example:

```bash
# Linux: keep the data in another file
Database__ConnectionString=/srv/reviewcenter/reviews.db reviewcenter serve
```

If the database cannot be prepared (for example the provider is not supported or the location cannot be used), the
server writes an error message and exits with a non-zero exit code. Database migrations are not provided in this
release: the tables are created only when the database is new. A database file created before review comments
existed does not have the comment tables, and the review page cannot work with it; delete the file (or point
`Database:ConnectionString` at a new one) to start again. Do this only with development data.

## Self-Validation

Self-validation produces a report demonstrating that ReviewCenter is functioning
correctly. This is useful in regulated industries where tool validation evidence is required.

### Running Validation

To perform self-validation:

```bash
reviewcenter --validate
```

To save validation results to a file:

```bash
reviewcenter --validate --results results.trx
```

The `--result` option is an accepted alias for `--results`.

The results file format is determined by the file extension: `.trx` for TRX (MSTest) format,
or `.xml` for JUnit format.

### Heading Depth

Use `--depth <#>` to control the heading level of the validation output (default: `1`).
This is useful when embedding the validation report into a larger markdown document:

```bash
# Embed validation at heading level 2
reviewcenter --validate --depth 2
```

### Validation Report

The validation report contains the tool version, machine name, operating system version,
.NET runtime version, timestamp, and test results.

Example validation report:

```text
# DEMA Consulting ReviewCenter

| Information         | Value                                              |
| :------------------ | :------------------------------------------------- |
| Tool Version        | 1.0.0                                              |
| Machine Name        | BUILD-SERVER                                       |
| OS Version          | Ubuntu 22.04.3 LTS                                 |
| DotNet Runtime      | .NET 10.0.0                                        |
| Time Stamp          | 2024-01-15 10:30:00 UTC                            |

✓ ReviewCenter_VersionDisplay - Passed
✓ ReviewCenter_HelpDisplay - Passed

Total Tests: 2
Passed: 2
Failed: 0
```

### Validation Tests

Each test proves specific functionality works correctly:

- **`ReviewCenter_VersionDisplay`** - `--version` outputs a valid version string.
- **`ReviewCenter_HelpDisplay`** - `--help` outputs usage and options information.

## Silent Mode

Suppress console output:

```bash
reviewcenter --silent
```

## Logging

Write output to a log file:

```bash
reviewcenter --log output.log
```

## Error Handling

Unrecognized arguments cause the tool to print an error message to standard error and exit
with a non-zero exit code. For example:

```text
Error: Unsupported argument '--unknown'
```

This behavior enables automated scripts and CI/CD pipelines to detect and surface
misconfiguration failures automatically.

# Command-Line Options

The following command-line options are supported:

| Option                                | Description                                                  |
| ------------------------------------- | ------------------------------------------------------------ |
| `serve`                               | Run the ReviewCenter web server until stopped                |
| `--urls <url>`                        | Listen URL for `serve` (default: `http://localhost:5000`)    |
| `-v`, `--version`                     | Display version information                                  |
| `-?`, `-h`, `--help`                  | Display help message                                         |
| `--silent`                            | Suppress console output                                      |
| `--validate`                          | Run self-validation                                          |
| `--results <file>`, `--result <file>` | Write validation results to file (TRX or JUnit format)       |
| `--depth <#>`                         | Set heading depth for markdown output (default: 1)           |
| `--log <file>`                        | Write output to log file                                     |

# Examples

## Example 1: Basic Usage

```bash
reviewcenter
```

## Example 2: Self-Validation with Results

```bash
reviewcenter --validate --results validation-results.trx
```

## Example 3: Silent Mode with Logging

```bash
reviewcenter --silent --log tool-output.log
```

## Example 4: Run the Web Server on a Custom Port

```bash
reviewcenter serve --urls http://localhost:8080
```

Then open `http://localhost:8080` in a browser, or check `http://localhost:8080/health`.

# Puppet Development Tool

Puppet is a small developer helper for AI agents, not part of the distributed ReviewCenter executables. It keeps one
persistent headless Chromium open and drives it one command per shell call (go to a page, click, hover, fill, read
text, screenshot) against a running ReviewCenter web interface. See `tools/puppet/README.md` for the setup, the
commands, and the workflow.

## References

N/A
