# Introduction

## Purpose

ReviewCenter is a web-based formal code review service with a companion command-line
interface. The service is an ASP.NET Core Blazor Server application started with the `serve`
verb. The current release lets you sign in, create projects and draft reviews made from uploaded files, shows the
content fingerprint of each review, lets you discuss the files in threaded comments, gives each review a
checklist, and lets reviewers sign a review with their password (or by signing in again at an identity provider), with
every action recorded under the signed-in
user. A review is Approved when every reviewer has signed the current record; it is then locked, and a project
administrator can reopen it. Every change is also recorded in a hash-chained audit trail that can be read and verified.
The web interface has a built-in Help with short
guides to the main tasks. The command-line
interface is documented in
this guide. A separate development tool,
Puppet, is described near the end of this guide.

## Scope

This user guide covers:

- Installation instructions
- Deploying and securing the service: configuration, reverse proxy, running as a service, backup, and upgrade
- Usage examples for common tasks
- Running the web server, creating the first user, signing in, creating projects and reviews, and configuring the
  database
- Command-line options reference
- The Puppet development tool
- Practical examples for various scenarios

## Where to Start

Each chapter of this guide serves a group of readers:

- **Administrators who set up the service**: read *Installation* to download and start ReviewCenter, *Deployment* to
  configure, secure, and operate it, and *Users and Sign-in* to create the first administrator and understand how
  people sign in. *Administration* covers users, project members, evidence settings, the audit trail, the database,
  and repositories.
- **Reviewers**: read *Users and Sign-in* for the account and the second factor, and *Reviewing* for projects,
  reviews, comments, the checklist, and signatures, and *Review Practices* for suggested ways of working.
- **Teams that automate**: read *Automation and API Tokens* for tokens, the JSON API, and the evidence index that
  ReviewMark reads.
- **Everyone**: *Command-Line Reference* lists every command-line option, the self-validation, and the error
  handling.

## Planned Work

These items are planned but not yet implemented:

- Further review states (for example a signed "rejected" outcome); today a review is In review or Approved and locked
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
