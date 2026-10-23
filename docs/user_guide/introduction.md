# Introduction

## Purpose

ReviewCenter is a web-based formal code review service with a companion command-line
interface. The service is an ASP.NET Core Blazor Server application started with the `serve`
verb. The review features themselves are still under development; the current release provides
the web host with a home page and a health endpoint, plus the command-line interface documented
in this guide. A separate development tool, Puppet, is described near the end of this guide.

## Scope

This user guide covers:

- Installation instructions
- Usage examples for common tasks
- Running the web server
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

ReviewCenter is under development and no release has been published yet. To run it, build it from source with the
.NET 10 SDK (a .NET runtime that supports `net10.0` is required):

```bash
dotnet build
dotnet run --project src/DemaConsulting.ReviewCenter -- serve
```

The planned distribution is self-contained executables for win-x64 and linux-x64, which will not need a separate
.NET runtime. The examples in this guide use `reviewcenter`, the command name of the published executable; when
running from source, replace `reviewcenter` with `dotnet run --project src/DemaConsulting.ReviewCenter --`.

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
  when pressed, and a counter button that increments a displayed count.
- `/health` - a health check that answers with status 200 and the text `Healthy` while the server is running.

The `--urls` option is only valid together with `serve`; using it without `serve` is reported as an error.
The `--silent` option suppresses the server's console log output.

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

Puppet is a developer tool, not part of the distributed ReviewCenter package. It lets a person, or a coding
agent, look at and drive the ReviewCenter web interface from the command line. It starts the ReviewCenter server on
a random local port, opens it in headless Chromium, and then accepts short commands such as `click`, `text`, and
`screenshot`. Puppet is its own software system in this repository, with its own requirements, design, and
verification documents.

## Building and Setting Up Puppet

Build Puppet from the repository root, then download Chromium once per machine with the Playwright script that the
build places next to the Puppet executable:

```bash
dotnet build tools/puppet
pwsh tools/puppet/bin/Debug/net10.0/playwright.ps1 install chromium
```

Replace `Debug` with `Release` if that configuration was built. If Chromium is missing, `start` fails with exit
code 4 and prints the install command.

## Puppet Commands

Run Puppet with `dotnet tools/puppet/bin/Debug/net10.0/DemaConsulting.ReviewCenter.Puppet.dll <command>`:

| Command                                   | What it does                                                   |
| ----------------------------------------- | -------------------------------------------------------------- |
| `start [--session <id>]`                  | Starts the server and browser; prints the session id and URL   |
| `goto <path>`                             | Navigates inside the session's server, for example `goto /`    |
| `click <selector>`                        | Clicks an element                                              |
| `fill <selector> <text>`                  | Types text into an input                                       |
| `text <selector>`                         | Prints the text of an element                                  |
| `screenshot [--out <file>] [--full-page]` | Saves a PNG image and prints its absolute path                 |
| `snapshot`                                | Prints an accessibility snapshot, title, URL, and test ids     |
| `stop`                                    | Stops the session: browser, server, and background daemon      |
| `list`                                    | Lists sessions and cleans up leftovers of dead sessions        |

Every command except `start`, `stop`, and `list` accepts `--session <id>` to choose the session and
`--timeout <seconds>` (1 to 600, default 10) to limit how long it waits for the page. When `--session` is omitted,
Puppet uses the `REVIEWCENTER_PUPPET_SESSION` environment variable, or the only running session.

A selector made only of letters, digits, hyphens, and underscores is a test id, so `hello-button` means the element
whose `data-testid` is `hello-button`. Any other selector, such as `#main` or `text=Hello`, is passed to Playwright
unchanged.

## Puppet Example Workflow

```powershell
$puppet = "dotnet tools/puppet/bin/Debug/net10.0/DemaConsulting.ReviewCenter.Puppet.dll"
& $puppet start --session demo
& $puppet goto / --session demo
& $puppet click hello-button --session demo
& $puppet text hello-message --session demo
& $puppet click counter-button --session demo
& $puppet text counter-value --session demo
& $puppet screenshot --out demo.png --session demo
& $puppet stop --session demo
```

## Puppet Exit Codes

| Code | Meaning                                                                                     |
| ---- | ------------------------------------------------------------------------------------------- |
| 0    | The command succeeded                                                                       |
| 1    | Bad arguments, or the command failed (element not found, timeout, navigation refused)       |
| 2    | The session does not exist                                                                  |
| 3    | The session was stale (its daemon had died); leftovers were cleaned up                      |
| 4    | The session could not be started (server did not start, or Chromium is not installed)       |

Puppet only navigates inside the server that belongs to its session. See the `README.md` in `tools/puppet` for
more detail on how sessions work.

## References

N/A
