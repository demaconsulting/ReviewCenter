# ReviewCenter

<!-- IMPORTANT: All links in this file must be absolute URLs.
     This file is distributed in packages and relative links will not resolve. -->

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]

Web service and command-line tool for formal code reviews with a tamper-evident audit trail

## Overview

ReviewCenter is a DEMA Consulting ASP.NET Core web service, with a companion command-line interface, for performing
formal code reviews of file sets against earlier revisions. Reviewers discuss the files in threaded comments, answer
a project checklist, and sign the review with their password. Every action is recorded in a hash-chained audit trail.

## Features

- 🔍 **File-Set Reviews** - Review uploaded files or the changes between two revisions of a git repository
- 🌿 **Git Sources** - Register repositories with encrypted access tokens and plan reviews from `.reviewmark.yaml`
- 💬 **Threaded Comments** - Discuss a whole file or a line range, reply, resolve, and share a link to any thread
- ✅ **Review Checklist** - Every review gets a 14-section checklist answered Pass, Fail, or N/A
- 👥 **Primary and Alternate Reviewers** - Any number of reviewer slots, each signed by the primary or the alternate
- ✍️ **Re-authenticated Signatures** - Reviewers sign again with their password, bound to the exact record reviewed
- 🔗 **Tamper-Evident Audit Trail** - Every change is chained by SHA-256 hashes and can be verified at any time
- 📄 **Signed PDF Attestation** - Approved reviews download as a digitally signed PDF that any reader can verify
- 🔐 **Local Accounts** - Admin and Reviewer roles, sign-in lockout and throttling, and password changes
- 🌗 **Light and Dark Theme** - Follows the system setting, or choose a theme in your account
- 🤖 **Puppet UI Automation** - A developer tool that lets AI agents drive the web interface and take screenshots
- 📦 **Self-Contained Executable** - A single executable for Windows (win-x64) and Linux (linux-x64), no runtime needed
- 🗄️ **SQLite Storage** - Reviews, users, and the audit trail in one database file created on first start
- 🧪 **Self-Validation** - Built-in validation tests with TRX and JUnit output

## Installation

Download the archive for your platform from the [GitHub Releases][link-releases] page, verify it against the
matching `.sha256` file, and extract it:

- `reviewcenter-<version>-win-x64.zip` (Windows)
- `reviewcenter-<version>-linux-x64.tar.gz` (Linux)

Keep the extracted files together: the executable serves the web assets that sit beside it, and the native SQLite
library is a separate file that must stay in the same folder. No release has been published yet; until one is,
build from source with the .NET 10 SDK:

```bash
dotnet build
dotnet run --project src/DemaConsulting.ReviewCenter -- serve
```

See the [Installation][link-guide-installation] section of the user guide for the verification commands.

## Quick Start

The examples use `reviewcenter`, the command name of the published executable (when running from source, use
`dotnet run --project src/DemaConsulting.ReviewCenter --` instead).

```bash
# Create the first administrator (the password is read from REVIEWCENTER_PASSWORD, from standard input, or prompted)
reviewcenter user add --username admin --display-name "Ada Admin" --role Admin

# Run the web server (default http://localhost:5000, or choose an address with --urls)
reviewcenter serve
```

Open `http://localhost:5000` in a browser, sign in as the administrator, and create a project. The built-in **Help** in
the page header guides you through creating and signing a review.

## Documentation

The [User Guide][link-guide] describes everything in detail, including:

- [Users and Sign-in][link-guide-users]: accounts, roles, lockout, and the sign-in throttle
- [Projects and Reviews][link-guide-projects]: uploads, reviews from a git repository, and review-sets
- [Planning Reviews][link-guide-planning]: create the reviews of all review-sets of a repository in one go
- [Review Signatures][link-guide-signatures]: reviewer slots, signing, and approval
- [Signed Attestation][link-guide-attestation]: the PDF, its signing key, and what the signature proves
- [Audit Trail][link-guide-audit]: the hash chain, verification, and its limits
- [Database][link-guide-database]: data location and database settings
- [Repositories and Credentials][link-guide-repositories]: git access and token protection
- [Command-Line Options][link-guide-options]: all options of the `reviewcenter` command
- [Planned Work][link-guide-planned]: what is not implemented yet

## Self Validation

Run `reviewcenter --validate` to produce a validation report, and add `--results results.trx` (or `.xml` for JUnit) to
save it. See the [Self-Validation][link-guide-validation] section of the user guide for the report and its tests.

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
[link-guide]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/introduction.md
[link-guide-installation]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/introduction.md#installation
[link-guide-users]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/introduction.md#users-and-sign-in
[link-guide-projects]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/introduction.md#projects-and-reviews
[link-guide-planning]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/introduction.md#planning-reviews
[link-guide-signatures]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/introduction.md#review-signatures
[link-guide-attestation]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/introduction.md#signed-attestation
[link-guide-audit]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/introduction.md#audit-trail
[link-guide-database]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/introduction.md#database
[link-guide-repositories]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/introduction.md#repositories-and-credentials
[link-guide-options]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/introduction.md#command-line-options
[link-guide-planned]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/introduction.md#planned-work
[link-guide-validation]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/introduction.md#self-validation
