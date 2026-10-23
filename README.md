# ReviewCenter

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]
[![NuGet][badge-nuget]][link-nuget]

ReviewCenter is a DEMA Consulting ASP.NET Core web service, with a companion command-line interface,
for performing formal code reviews of file sets against earlier revisions. It supports threaded review
comments on code blocks and files, project-specific pass/fail/N/A checklists, primary and alternate
reviewers, and re-authenticated electronic sign-off.

The `serve` verb runs ReviewCenter as an ASP.NET Core Blazor Server web application with a home page and a
health endpoint. The review features themselves are still under development.

## Features

The current release provides:

- **Blazor Server Web Application**: `reviewcenter serve` hosts the web interface (home page at `/`) and a
  `/health` endpoint, listening on `http://localhost:5000` by default or on the address given with `--urls`
- **Standardized Command-Line Interface**: Context class handling common arguments
  (`serve`, `--urls`, `--version`, `--help`, `--silent`, `--validate`, `--results`, `--depth`, `--log`)
- **Self-Validation**: Built-in validation tests with TRX/JUnit output
- **Multi-Platform Support**: Builds and runs on Windows, Linux, and macOS
- **Modern .NET**: Targets .NET 10 (`net10.0`)
- **Puppet Developer Tool**: A separate in-repository tool that drives the web interface in headless Chromium
  from the command line (see `tools/puppet/README.md`); it is not part of the distributed package
- **Comprehensive CI/CD**: GitHub Actions workflows with quality checks, builds, and
  integration tests
- **Linting Enforcement**: markdownlint, cspell, and yamllint enforced on every CI run
- **Continuous Compliance**: Compliance evidence generated automatically on every CI run, following
  the [Continuous Compliance][link-continuous-compliance] methodology
- **SonarCloud Integration**: Quality gate and security analysis on every build
- **Documentation Generation**: Automated build notes, user guide, code quality reports,
  requirements, justifications, and trace matrix
- **Requirements Traceability**: Requirements linked to passing tests with auto-generated trace matrix

## Installation

ReviewCenter is under development and no release has been published yet, so there is nothing to install from NuGet
or GitHub Releases. To run it, build it from source with the .NET 10 SDK:

```bash
dotnet build
dotnet run --project src/DemaConsulting.ReviewCenter -- serve
```

The planned distribution is self-contained executables for win-x64 and linux-x64 (see `architecture.md`); the
release pipeline has not been reworked for that yet. The command-line examples below use `reviewcenter`, the command
name of the published executable; when running from source, replace `reviewcenter` with
`dotnet run --project src/DemaConsulting.ReviewCenter --`.

## Usage

```bash
# Default behavior (no arguments) — displays banner and copyright
reviewcenter

# Display version
reviewcenter --version

# Display help
reviewcenter --help

# Run the web server (default http://localhost:5000; home page at /, health check at /health)
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

## Developer Tools

The `tools/puppet` folder contains **Puppet**, a developer tool that starts the ReviewCenter server, opens it in
headless Chromium, and lets a person or a coding agent look at and drive the web interface with short commands
such as `click`, `text`, and `screenshot`. Puppet is not part of the distributed package. To use it, build it and
download Chromium once:

```bash
dotnet build tools/puppet
pwsh tools/puppet/bin/Debug/net10.0/playwright.ps1 install chromium
```

See `tools/puppet/README.md` for the commands, exit codes, and an example session.

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

- The review features of the ReviewCenter web service (the current web application is a starting point)
- EF Core database migrations with an explicit `migrate` command
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
[badge-nuget]: https://img.shields.io/nuget/v/DemaConsulting.ReviewCenter?style=plastic

<!-- Link References -->
[link-forks]: https://github.com/demaconsulting/ReviewCenter/network/members
[link-stars]: https://github.com/demaconsulting/ReviewCenter/stargazers
[link-contributors]: https://github.com/demaconsulting/ReviewCenter/graphs/contributors
[link-license]: https://github.com/demaconsulting/ReviewCenter/blob/main/LICENSE
[link-build]: https://github.com/demaconsulting/ReviewCenter/actions/workflows/build_on_push.yaml
[link-quality]: https://sonarcloud.io/dashboard?id=demaconsulting_ReviewCenter
[link-security]: https://sonarcloud.io/dashboard?id=demaconsulting_ReviewCenter
[link-nuget]: https://www.nuget.org/packages/DemaConsulting.ReviewCenter
[link-guide]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/introduction.md
[link-continuous-compliance]: https://github.com/demaconsulting/ContinuousCompliance
