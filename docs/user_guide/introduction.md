# Introduction

## Purpose

ReviewCenter is a web-based formal code review service with a companion command-line interface. The service is an
ASP.NET Core Blazor Server application started with the `serve` verb. You sign in, create projects and reviews made
from uploaded files or a git repository, and discuss the files in threaded comments. A thread is either a Question (a
chat that does not change what is signed) or a Finding (it goes into the report, with a resolution note).

Each review has a checklist that every reviewer answers in their own ballot. A ballot stays a draft until the
reviewer submits it, and in a review with several reviewers the others' answers stay hidden until you have submitted
yours. The submitted ballots are consolidated into agreed, disputed, and pending checks. Reviewers then sign the
review with their password (or by signing in again at an identity provider), and every action is recorded under the
signed-in user.

A review is Approved when it has at least one reviewer slot and every slot is signed as approved for the current
record, with no disputed check and no open thread. It is Rejected when the reviewers agree on a Fail and every slot
is signed as rejected; a Rejected review needs a written conclusion with the reasons. An open Finding or Question
blocks signing until it is resolved. A concluded review is locked, and a project administrator can reopen it. Every
change is also recorded in a hash-chained audit trail that can be read and verified. Project roles include a
read-only Auditor, who can read a project and its audit trail and change nothing.

The web interface has a built-in Help with short guides to the main tasks, and the command-line interface is
documented in this guide. A separate development tool, Puppet, is for developers of ReviewCenter only; the
Command-Line Reference chapter describes it at the end.

## Scope

This user guide covers:

- Installation instructions
- A first-review walkthrough
- Deploying and securing the service: configuration, reverse proxy, running as a service, backup, and upgrade
- Usage examples for common tasks
- Running the web server, creating the first user, signing in, creating projects and reviews, and configuring the
  database
- Command-line options reference
- The Puppet development tool
- Practical examples for various scenarios

## Where to Start

New to ReviewCenter? Once the server runs, read *Your First Review*: a short, friendly walkthrough of one review from
start to finish, from creating it to the signed attestation (and what happens when it is rejected).

Each chapter of this guide then serves a group of readers:

- **Administrators who set up the service**: read *Installation* to download and start ReviewCenter, *Deployment* to
  configure, secure, and operate it, and *Users and Sign-in* to create the first administrator and understand how
  people sign in. *Administration* covers users, project members, evidence settings, the audit trail, the database,
  and repositories.
- **Reviewers**: read *Users and Sign-in* for the account and the second factor, *Reviewing* for projects, reviews,
  comments, the checklist, and signatures, and *Review Practices* for suggested ways of working.
- **Teams that automate**: read *Automation and API Tokens* for tokens, the JSON API, and the evidence index that
  ReviewMark reads.
- **Everyone**: *Command-Line Reference* lists every command-line option, the self-validation, and the error
  handling.

## Planned Work

These items are planned but not yet implemented:

- External anchoring, export, and retention of the audit trail
- Self-service password reset (an administrator can already set a new password), a required second factor (enforced for
  system administrators, or by a policy of a project), a QR code for the setup, and a connection to several identity
  providers at once
- EF Core database migrations with an explicit `migrate` command, and further database providers (PostgreSQL,
  SQL Server)
- PDF/A evidence export of review records
- Signing attestations with a key in Azure Key Vault (the key stays in the vault), and RFC 3161 time stamps for them
- AI-assisted review (planned to use an Azure OpenAI Service account)

## References

N/A

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
