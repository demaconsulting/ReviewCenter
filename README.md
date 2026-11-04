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

![A reviewer's inbox with the reviews to answer and the reviews ready to sign][image-review-inbox]

![A review page with the Where are we? status panel and its next step][image-review-status-panel]

![A side-by-side diff with a Question thread and a Finding thread][image-review-diff-thread]

![An approved review with the signed attestation PDF download][image-review-signed]

See the [User Guide][link-guide] for a tour of the screens.

## Overview

ReviewCenter is a DEMA Consulting ASP.NET Core web service, with a companion command-line interface, for performing
formal code reviews of file sets against earlier revisions. Reviewers discuss the files in threaded comments, answer
a project checklist in their own ballot, and sign the review with their credentials. Every action is recorded in a
hash-chained audit trail.

## Features

- 🔍 **File-Set Reviews** - Review uploaded files or git revision changes
- 🌿 **Git Sources** - Plan reviews from repository review-sets
- 📅 **Due Dates** - Optional due dates with Overdue badge
- 📊 **Progress Overview** - Stage, signatures, blockers, and who waits
- 🔁 **Follow-up Reviews** - Carry agreed answers into follow-ups
- 💬 **Questions and Findings** - Discuss files, lines, or checks
- 🧭 **Where Are We?** - Shows ballots, disputes, and next step
- 📥 **Inbox** - What waits for you, by section
- 📧 **E-mail Notifications** - Optional mail, never with answers
- ✅ **Review Checklist** - Per-reviewer ballots consolidated into agreed answers
- 👥 **Reviewer Slots** - Primary and alternate reviewers per slot
- 🙈 **Independent Ballots** - Answers stay hidden until you submit
- ✍️ **Re-authenticated Signatures** - Identity-confirmed sign-off on agreed record
- 🔒 **Conclusion Lock** - Concluded reviews lock; admins can reopen
- 🔗 **Tamper-Evident Audit Trail** - Hash-chained history with filtered CSV/JSON export
- 📄 **Signed PDF Attestation** - Verifiable PDFs for approved or rejected reviews
- ⚙️ **Evidence Settings** - Control PDF contents and anonymous access
- 🛡️ **Access Control** - Project roles, including read-only Auditor
- 🪪 **OpenID Connect Sign-in** - Single sign-on with any provider
- 🔐 **Two-Factor Sign-in** - Optional authenticator codes for local accounts
- 🔑 **API Tokens** - Personal tokens for API and ReviewMark
- 🌗 **Light and Dark Theme** - Follows system or account setting
- 🤖 **Puppet UI Automation** - Lets AI agents drive the UI
- 📦 **Self-Contained Executable** - Single Windows and Linux executable
- 🗄️ **SQLite Storage** - Everything in one auto-created file
- 🔄 **Key Rotation** - Move stored secrets to new key
- 🧪 **Self-Validation** - Built-in tests with TRX or JUnit output

## Installation

Download the archive for your platform from the [GitHub Releases][link-releases] page, verify it against the
matching `.sha256` file, and extract it:

- `reviewcenter-<version>-win-x64.zip` (Windows)
- `reviewcenter-<version>-linux-x64.tar.gz` (Linux)

Keep the extracted files together: the executable needs the `wwwroot` folder, the static-web-assets endpoint file,
`appsettings.json`, and the native SQLite library in the same folder. No release has been published yet; until one is,
build from source with the .NET 10 SDK:

```bash
dotnet build
dotnet run --project src/DemaConsulting.ReviewCenter -- serve
```

See the [Installation][link-guide-installation] chapter of the user guide for the verification commands, and the
[Deployment][link-guide-deployment] chapter for running the server for a team.

## Quick Start

The examples use `reviewcenter`, the command name of the published executable (when running from source, use
`dotnet run --project src/DemaConsulting.ReviewCenter --` instead).

```bash
# Create the first administrator (the password is read from REVIEWCENTER_PASSWORD, from standard input, or prompted)
reviewcenter user add --username admin --display-name "Ada Admin" --system-admin

# Run the web server (default http://localhost:5000, or choose an address with --urls)
reviewcenter serve
```

Open `http://localhost:5000` in a browser, sign in as the system administrator, create a project, and add its members.
The built-in **Help** in the page header guides you through creating and signing a review, and
[Your First Review][link-guide-first-review] in the user guide walks through one from start to finish.

## Documentation

The [User Guide][link-guide] describes everything in detail, including:

- [Your First Review][link-guide-first-review]: a short walkthrough from the new review to the signed attestation
- [Deployment][link-guide-deployment]: where the data lives, configuration, running as a service, TLS and a reverse
  proxy, backup, and upgrades
- [Users and Sign-in][link-guide-users]: accounts, system administrators, lockout, and the sign-in throttle
- [Access and Visibility][link-guide-access]: roles, project membership, and who can see and do what
- [Projects and Reviews][link-guide-projects]: uploads, reviews from a git repository, and review-sets
- [Planning Reviews][link-guide-planning]: create the reviews of all review-sets of a repository in one go
- [Review Signatures][link-guide-signatures]: reviewer slots, ballots, signing, and the outcome
- [Signed Attestation][link-guide-attestation]: the PDF, its signing key, and what the signature proves
- [Review Practices][link-guide-practices]: suggested good practices for running a review
- [Evidence Settings][link-guide-evidence-settings]: anonymous access to the evidence and what the PDFs contain
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

<!-- Image References -->
[image-review-inbox]: https://raw.githubusercontent.com/demaconsulting/ReviewCenter/main/docs/user_guide/images/review-inbox.png
[image-review-status-panel]: https://raw.githubusercontent.com/demaconsulting/ReviewCenter/main/docs/user_guide/images/review-status-panel.png
[image-review-diff-thread]: https://raw.githubusercontent.com/demaconsulting/ReviewCenter/main/docs/user_guide/images/review-diff-thread.png
[image-review-signed]: https://raw.githubusercontent.com/demaconsulting/ReviewCenter/main/docs/user_guide/images/review-signed.png

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
[link-guide-first-review]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/first-review.md
[link-guide-installation]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/installation.md#installation
[link-guide-deployment]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/deployment.md
[link-guide-users]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/sign-in.md#users-and-sign-in
[link-guide-access]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/administration.md#access-and-visibility
[link-guide-projects]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/reviewing.md#projects-and-reviews
[link-guide-planning]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/reviewing.md#planning-reviews
[link-guide-signatures]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/reviewing.md#review-signatures
[link-guide-attestation]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/reviewing.md#signed-attestation
[link-guide-practices]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/review-practices.md
[link-guide-evidence-settings]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/administration.md#evidence-settings
[link-guide-audit]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/administration.md#audit-trail
[link-guide-database]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/administration.md#database
[link-guide-repositories]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/administration.md#repositories-and-credentials
[link-guide-options]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/reference.md#command-line-options
[link-guide-planned]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/introduction.md#planned-work
[link-guide-validation]: https://github.com/demaconsulting/ReviewCenter/blob/main/docs/user_guide/reference.md#self-validation
