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
health endpoint, projects, draft reviews made from uploaded files or from the changes in a registered git repository,
threaded review comments on those files, a
checklist for each review, reviewer slots with re-authenticated signatures, and a hash-chained audit trail. Every
comment, checklist answer, and signature is recorded under the signed-in user. Review states beyond Draft are still
under development.

> **Warning: databases created by an earlier build must be recreated.** Reviews from a git repository add columns to
> the review tables, and the review record has a new digest format (version 4) that also covers the review-set a
> review was planned for and the side of every line comment; an earlier development database does not have these
> columns, and the database is created by `EnsureCreated` without migrations. Delete the old database file before
> starting this version (development databases only). The server also keeps a Data Protection key ring in a `keys`
> folder beside the database (see [Repositories and Credentials](#repositories-and-credentials)); starting this
> version once signs everybody out, because the cookie keys now persist in that folder.

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
- **Draft Reviews**: create a review in a project by uploading a set of files, or from a registered git
  repository; the review page shows the files and
  lets you read each file's content as numbered lines; a collapsed Details section holds the review id, state, the
  SHA-256 hash of each file, and a content fingerprint that is calculated exactly as the
  [ReviewMark][link-reviewmark] tool calculates it
- **Reviews from a Repository**: on the project page choose **From a repository**, pick an enabled repository by
  name, and enter a head revision, an optional baseline revision, and a selection (patterns, one per line, `!` to
  exclude, default `**/*`). The review covers the files that changed between the baseline and the head and match the
  selection; if none does, no review is created and you are told so. The changed files, their baseline and head
  content, the two commits, and the selection are copied into the database when the review is created, so later
  commits do not change the review. The review page shows each file as Added, Modified, or Deleted; every file of a
  review that has a baseline commit opens as a **Diff** (the baseline and the head side by side, unchanged lines
  collapsed, a **+** on every line to comment on it, shown when you point at or tab to the line; an added file shows
  all its lines as added, a deleted file all
  as removed) and can also be read as **Head** (not for a deleted file) or as a read-only **Baseline** (not for an
  added file). Uploads and reviews without a baseline commit show the numbered head only.
  A signature binds the repository, both commits, the selection, and every change (see
  [Reviews from a Repository](#reviews-from-a-repository))
- **Planning Reviews**: if the repository has review-sets in its `.reviewmark.yaml`, choose **Plan reviews from a
  repository** on the project page, enter a head revision, and choose a baseline: none, a given revision, or the last
  signed review of each set. The page shows one plain summary (for example "3 sets need review, 1 already filed, 9 have
  nothing to review"), the sets to review first and ticked, sets that cannot be planned or are already filed (with a
  link) only when there are some, and the sets with nothing to review collapsed; **Create** makes a review for every
  ticked set and lists the result of each (see [Planning Reviews](#planning-reviews))
- **Review Comments**: on the review page, start a comment thread on a whole file or on a line range, reply to it,
  and mark it resolved or reopen it; the author is the signed-in user; a next-step line says what is left to do, a
  thread index of all files (open by default, or all, or resolved) jumps to the thread's lines, resolved threads are
  collapsed to one line, and the file list shows the open threads per file
- **Sharing Links**: every file, line and thread of a review has a stable link; the address bar always holds one, each
  thread has a **Link** and a **Copy link** button, and signing in brings you back to the link you opened (see
  [Sharing links](#sharing-links))
- **Review Checklist**: every new review gets a checklist of 14 sections and 58 checks, taken from the
  [Continuous Compliance review template][link-review-template]; a checklist page (linked from the review page,
  with a summary such as "37 of 58 checks answered") shows the sections with their progress, one section open at a
  time, and a Pass, Fail, or N/A choice for each check, recorded under the signed-in user; a Fail or N/A needs a
  justification, a section can be marked not applicable with a reason, and a "next unanswered check" button leads
  you on; the review page's next-step line counts the unanswered checks and the checks answered Fail together with
  the open threads
- **Reviewer Slots and Signatures**: a review has any number of reviewer slots, each with a primary and an optional
  alternate reviewer; either can sign the slot after entering their password again; a signature is bound to the
  exact record that was reviewed (the content fingerprint, the checklist answers, and the threads with their
  comments), so a later change makes it out of date and the slot can be signed again; the review page and the
  project page show the review as Approved or In review (see [Reviewers and Signatures](#reviewers-and-signatures))
- **Signed Attestation**: once a review is Approved, the review page offers **Download attestation (PDF)**: a PDF of
  the review (content, checklist, comment threads, sign-offs, and the head of the audit trail) that the server signs
  digitally with a key it holds, so that any PDF reader can show whether the file was changed after signing. The key is
  a self-signed key by default (development and test only) or a certificate from a certificate store; every download is
  recorded in the audit trail (see [Signed Attestation](#signed-attestation))
- **Audit Trail**: every change (projects, reviews, comments, checklist answers, reviewer slots, signatures, and
  users) and every sign-in, sign-out, failed sign-in, and lockout is recorded with the time, the person, the action,
  and a short detail, and is stored in the same database transaction as the change; entries are chained by SHA-256
  hashes; a History page for each review is open to every signed-in user, and administrators see the whole trail at
  `/audit` and can verify the chain there or with `reviewcenter audit verify` (see
  [Audit Trail](#audit-trail))
- **Repositories and Credentials**: an administrator registers git repositories at `/repositories` (an https, http, or
  file location or a local path, with an optional user name and access token for private repositories); the token is
  encrypted before it is stored, is write-only in the page (only "token set" or "no token" is shown), and reaches git
  only through the environment of the git process; a repository can be disabled and enabled but not deleted; ordinary
  users can only see the names of enabled repositories. ReviewCenter reads repositories with the installed `git`
  (on the `PATH`), into a bare mirror cache, and can validate a revision, list the files changed between two
  revisions, and read a file with the same size limits as an upload; reviews are created from a repository (see
  [Reviews from a Repository](#reviews-from-a-repository) and
  [Repositories and Credentials](#repositories-and-credentials))
- **Persistent Storage**: users, projects, reviews, the uploaded file contents, the comment threads, the
  checklists, the reviewer slots and signatures, and the audit trail are kept in a SQLite database file
  (default `reviewcenter.db` in the per-user application data folder, see
  [Data Location](#data-location-and-database-settings))
- **Standardized Command-Line Interface**: Context class handling common arguments
  (`serve`, `user add`, `audit verify`, `--urls`, `--version`, `--help`, `--silent`, `--validate`, `--results`,
  `--depth`, `--log`)
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

# Verify the hash chain of the audit trail (exit code 0 when intact, 1 when broken or unreadable)
reviewcenter audit verify

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
| `audit verify`                        | Verify the audit trail hash chain; see Audit Trail below     |
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
- **Open pages**: a page that stays open re-checks its user every 30 seconds by default. When the user was disabled,
  removed, or had the name or role changed, the page is replaced by a "Session ended" message with a link to sign in
  again. If the user cannot be checked (for example the database is unavailable), the session is treated as ended.
  Change the interval with the `Auth` setting below.

| Setting                    | Environment variable        | Meaning                                                   |
| -------------------------- | --------------------------- | --------------------------------------------------------- |
| `Auth:SessionHours`        | `Auth__SessionHours`        | Hours of sign-in session (default 8; above 0)             |
| `Auth:RevalidationSeconds` | `Auth__RevalidationSeconds` | Seconds between open-page checks (default 30; at least 5) |

The sign-in cookie is marked Secure when the request arrives over HTTPS. Behind a reverse proxy that ends TLS, set
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so that the server sees the original HTTPS scheme and marks the cookie
Secure.

## Reviewers and Signatures

A review is approved by its reviewers signing it. On the review page, any signed-in user can add a reviewer slot,
choosing a primary reviewer and, optionally, an alternate from the enabled users. A review can have any number of
slots, and none at all. A user can sit in only one slot of a review, and the primary and the alternate of a slot
must be different users.

A reviewer signs a slot, as primary or as alternate, by entering their own password again and pressing the sign
button. A signature records who signed, in which role, when, the text "I have reviewed this record and approve it.",
and a digest of the record at that moment. The digest covers the content fingerprint of the files, every checklist
answer and justification, every section marked not applicable with its reason, and every comment thread with its
comments. For a review from a repository it also covers the repository, the baseline and head commits, the selection,
and every changed file with its change kind and the hashes of its baseline and head content (see
[Reviews from a Repository](#reviews-from-a-repository)). It does not cover who answered a check or when. A wrong
password at a signature counts toward the same
lockout as a wrong password at sign-in.

A review can be signed only when it has a checklist, every check of an applicable section is answered, no such check
is answered Fail, and no thread is open; the page lists what is blocking. The review is **Approved** when it has at
least one slot and every slot has a signature of the record as it is now; otherwise it is **In review**. When
anything the digest covers changes, for example a new comment or a changed answer, the signatures no longer match:
the slots show "Signature out of date", the review is In review again, and the reviewers sign again. Earlier
signatures are kept. A slot that has any signature, current or out of date, cannot be changed or removed.

If the record changes while a reviewer is signing, the signature is refused and the page asks the reviewer to review
the record again.

An Approved review can be downloaded as a digitally signed PDF; see [Signed Attestation](#signed-attestation).

## Audit Trail

Every change that the application makes to projects, reviews, comments, checklists, reviewer slots, signatures, and
users is recorded in an audit trail in the database, and so is every sign-in, sign-out, failed sign-in, and lockout.
An entry names the time, the person, the action, and a short detail. A change and its entry are stored in the same
database transaction, so a change without an entry, or an entry without a change, cannot be left behind by the
application. A password is never recorded, and the text of a comment or a justification is recorded only as a short
digest prefix (eight hexadecimal characters of its SHA-256 hash). The entries cannot be edited or deleted through
the application.

Each entry carries the SHA-256 hash of the previous entry and its own hash, which covers the previous hash and every
field of the entry. This is the chain. The **History** link on the review page opens the history of that review for
every signed-in user, newest first, 50 entries at a time. Administrators have an **Audit** link in the page header
that opens the whole trail at `/audit`, with Older and Newer links and a **Verify chain** button. Verification reads
the whole trail once and reports either "intact" with the number of entries and the head hash (the hash of the newest
entry), or the first entry at which the chain is broken. The same check runs from the command line:

```bash
reviewcenter audit verify
```

It prints `Audit chain intact: N entries` and exits with code 0 for an intact chain, and prints
`Audit chain BROKEN at entry N` and exits with code 1 for a broken chain. It also exits with code 1 when the audit
trail cannot be read. The command line does not print the head hash; the Audit page does.

The recorded actions are:

| Group     | Action codes                                                                                  |
| --------- | --------------------------------------------------------------------------------------------- |
| Projects  | `project.created`, `review.created`                                                           |
| Slots     | `slot.added`, `slot.updated`, `slot.removed`, `signature.added`                               |
| Threads   | `thread.started`, `thread.replied`, `thread.resolved`, `thread.reopened`                      |
| Checklist | `checklist.answered`, `checklist.applicability-changed`                                       |
| Users     | `user.created`, `user.enabled`, `user.disabled`, `user.role-changed`                          |
| Passwords | `user.password-set-by-admin`, `user.password-changed`                                         |
| Sign-in   | `auth.sign-in`, `auth.sign-out`, `auth.sign-in-failed`, `auth.locked-out`                     |
| Evidence  | `attestation.generated`                                                                       |

### What the Chain Proves, and What It Does Not

The chain **detects** an edited entry, a deleted entry, an inserted entry, and a reordering of entries in the middle
of the history, because each of these makes a later hash or link not match.

The chain does **not** detect the removal of the newest entries, because what remains is still a valid chain. It also
does not detect a complete rewrite of the trail by someone who can write to the database and who recalculates every
hash, because the hash has no secret key. Both can be found only if the head hash and the entry count were recorded
somewhere else earlier, and are compared with the values now. This release has no external anchoring. An
administrator who wants that protection should record the head hash and the entry count from the Audit page outside
the database (for example in a ticket, a signed email, or a write-once log) at times that matter, such as after a
review has been approved.

Sign-in, sign-out, failed sign-in, and lockout entries are recorded on a best-effort basis in their own transaction;
if that cannot be stored, the sign-in itself is not blocked, and the failure is logged.

## Signed Attestation

When every reviewer slot of a review has a current signature (the review is **Approved**), the review page shows
**Download attestation (PDF)**, with a note on the key that signs it. The PDF is made when it is requested and is not
stored. It states the project, the review, the repository and commits, the content fingerprint and the hash of every
file, the checklist with every answer and comment, the comment threads, the sign-off of every reviewer slot with the
record digest it bound to, and the head of the audit trail (entry count and head hash) at that moment. It carries a
digital signature that PDF readers such as Adobe Acrobat show and verify. A review that is not Approved has no
download; a direct request is answered with HTTP status 409, which is also the answer when the review changes while
the PDF is being prepared (nothing is signed or recorded then; ask again). Checklist comments are printed in full; of
the resolved threads the first 100 are listed (counts stay exact) and a line says how many more there are. Each
download is recorded in the audit trail
(`attestation.generated`).

### What the Signature Proves, and What It Does Not

The signature **proves** that the PDF has not been changed since it was signed, and that it was signed by the holder of
the key named in the file (the subject and thumbprint of the certificate are printed in the document and are part of
the signature).

It does **not** prove who the reviewers are. Reviewers are identified by their own signatures in the review record,
which ReviewCenter made after asking each one for the password again; the PDF lists them, and vouches for them only as
far as the signing key is trusted. It does **not** prove when the file was signed: the signing time is the clock of the
server, and there is no time stamp authority in this release. It does not make the content of the review correct; it
shows what the record said when the PDF was made.

### Configuration

The key is chosen in the `Attestation` section of `appsettings.json`, or with environment variables, which take
precedence (a double underscore stands for the colon, for example `Attestation__Provider`):

`Provider` is `SelfSigned` or `CertificateStore`; the settings marked "Store" apply to `CertificateStore` only.

| Setting                             | Environment variable                 | Meaning                               |
| ----------------------------------- | ------------------------------------ | ------------------------------------- |
| `Attestation:Provider`              | `Attestation__Provider`              | Key source; default `SelfSigned`      |
| `Attestation:RequireTrustedKey`     | `Attestation__RequireTrustedKey`     | Refuse unsuitable keys; default false |
| `Attestation:CertificateThumbprint` | `Attestation__CertificateThumbprint` | Store: certificate thumbprint         |
| `Attestation:StoreLocation`         | `Attestation__StoreLocation`         | Store: `CurrentUser` or `LocalMachine`|
| `Attestation:StoreName`             | `Attestation__StoreName`             | Store: store name; default `My`       |
| `Attestation:SelfSignedFolder`      | `Attestation__SelfSignedFolder`      | Folder of the self-signed key file    |

```powershell
# Windows: sign with an organization certificate of the account that runs the server
$env:Attestation__Provider = "CertificateStore"
$env:Attestation__CertificateThumbprint = "0123456789ABCDEF0123456789ABCDEF01234567"
$env:Attestation__RequireTrustedKey = "true"
.\reviewcenter.exe serve
```

The server checks the key when it starts. A setting that cannot work (an unknown provider, a missing thumbprint, a
certificate that is not in the store, or `RequireTrustedKey` with a self-signed key or a certificate without a signing
key usage) stops the start with a message that names the setting. A certificate whose private key is in a hardware
module that is not present therefore also stops the start. If the key becomes unusable later (for example the
certificate expires while the server runs), the review page shows the problem instead of the button.

### Choosing the Key

- **Self-signed (the default)**: on first use the server creates an RSA key with a self-signed certificate, and keeps it
  in `attestation-selfsigned/attestation-selfsigned.pfx` inside the folder of the Data Protection keys (see
  [Repositories and Credentials](#repositories-and-credentials)), or in `Attestation:SelfSignedFolder`. It is **for
  development and test only**: the file has no password, so protect the folder with file permissions and back it up
  with the keys, and PDF readers will report that the signer is not trusted. The review page and the PDF say that the
  key is self-signed and is not an organization key. (A self-signed certificate that an administrator supplies through
  the certificate store is described on the review page as supplied by the administrator, not as generated by the
  server.)
- **Certificate store**: for real use, set `Attestation:Provider` to `CertificateStore` and name the certificate by its
  thumbprint. The account that runs the server must be able to use the private key. The key is used where it is and is
  **never exported**, so it can be in the Windows key store, on a smart card, or in a hardware module. Use a
  **document-signing certificate**, one whose key usage allows digital signatures or non-repudiation. A certificate
  that is for code signing only is not suited to signing a PDF; the server logs a warning for it. A certificate from an
  internal certificate authority is fine, as long as the readers of the PDF trust that authority: distribute its root
  certificate to them.
- **`RequireTrustedKey`**: when `true`, the server refuses a self-signed key, a certificate whose key usage does not
  allow digital signatures or non-repudiation, and a certificate that is not valid now. This is all it checks. It does
  **not** check that the
  certificate chains to a root that the readers trust and does **not** check revocation, because the server cannot know
  whom its readers trust.

### Finding the Thumbprint

On Windows, run `Get-ChildItem Cert:\CurrentUser\My` in PowerShell (or `Cert:\LocalMachine\My`), or open the certificate
and read the **Thumbprint** on the **Details** tab. Spaces in the value are ignored.

### Limitations

- The default key is self-signed and is not an organization key; there is no signing without a key of the server.
- There is no time stamp authority: the signing time is the server clock, and the signature cannot prove the time.
- A key vault is not supported yet; the key must be in a certificate store the server can open. Signing with a key in
  Azure Key Vault and with an RFC 3161 time stamp authority are planned, not implemented.
- Whether the signing key is trusted is for the reader and the organization to decide; ReviewCenter does not validate
  the chain or the revocation of the certificate.

## Repositories and Credentials

Administrators open **Repositories** in the page header (`/repositories`) to register the git repositories that reviews
will be made from. A repository has a name (letters, digits, dot, underscore, and hyphen), a location (an `https://` or
`file://` URL, an `http://` URL for `localhost`, `127.0.0.1`, or `[::1]` only, or an absolute local path), and, for a
private repository, a user name and an access token. A user name or token is accepted only with an `https://` location.
A repository can be disabled and enabled again; it cannot be deleted, so that earlier work keeps its reference. A
disabled repository cannot be read. Signed-in users who are not administrators can only be offered the names of enabled
repositories, never the location or the credentials; a git error that is shown to them names the repository instead of
its location. A token must not be contained in (a part of) the repository name, the user name, or the location, nor may
its URL-escaped form.

The token is **write-only**: it is typed into a password field, encrypted with ASP.NET Core Data Protection, and never
shown again. The list shows only "token set" or "no token". When you edit a repository you choose to keep, replace, or
clear the token. At run time the token is passed to `git` only in the environment of the git process (never on the
command line), and it is removed from every error message and log line. Git itself must be installed and on the `PATH`
(version 2.31 or later). If git is missing, too old, cannot sign in, does not know a revision, or takes too long, the
error message says so.

Repositories are read into a bare mirror under the git cache folder, one folder for each repository, and one operation
at a time runs on a repository. Revisions are validated before they reach git (for example `--upload-pack=x`, `-x`, or
a name with a space is refused). The files of a review are chosen with patterns, one per line, as in the
[ReviewMark][link-reviewmark] file lists: `*` stays in one folder, `**` spans folders, `!` excludes, and `?` matches one
character. Matching is case-sensitive.

| Setting                   | Environment variable       | Meaning                                                 |
| ------------------------- | -------------------------- | ------------------------------------------------------- |
| `DataProtection:KeysPath` | `DataProtection__KeysPath` | Key ring folder (default `keys` beside the database)    |
| `Git:CachePath`           | `Git__CachePath`           | Mirror folder (default `git-cache` beside the database) |
| `Git:TimeoutSeconds`      | `Git__TimeoutSeconds`      | Git call time limit in seconds (default 120; 1 to 3600) |

> **Warning: back up the key ring, and protect it.** The tokens are only as safe as the key ring folder. The keys are
> **not** encrypted at rest, so on Linux protect the folder with file permissions (the server creates it readable only
> by its own account) and keep it out of the database backup. If the key ring is lost, every stored token becomes
> unreadable and an administrator must enter the tokens again. The server checks at start that the folder can be
> written and refuses to start with a clear message if it cannot.

## Reviews from a Repository

On the project page, choose **From a repository** instead of **Upload files**. Pick one of the enabled repositories
by name (you never see its location), enter the **head revision** (a branch, a tag, or a commit), optionally a
**baseline revision**, and a **selection**. Without a baseline, every file of the head that matches the selection is
reviewed as Added.

The selection is a list of patterns, one per line, with the same syntax as the ReviewMark file lists: `*` stays in one
folder, `**` spans folders, `?` matches one character, a line that starts with `!` excludes, and matching is
case-sensitive. The default is `**/*`. For example, `src/**/*.cs` followed by `!src/**/Generated/**` reviews the C#
files below `src` except those in `Generated` folders. Unlike ReviewMark, the patterns are applied to the files that
**changed** between the two revisions, not to every file of the head.

On submit, the server fetches the repository, resolves both revisions to commits, and keeps in the database, for
every changed file that matches, its path, its change kind (Added, Modified, or Deleted), and its baseline and head
content. Nothing is read from the repository afterwards, so later commits, a force push, or a disabled repository
change nothing in the review. If no changed file matches, the review is not created and you are told
"Nothing to review: no files matching the selection changed between X and Y". The size limits are those of an
upload (200 files, 2 MiB per file, 20 MiB per review), counted over both versions. Symbolic links and submodules are
not reviewed; those that match the selection are listed once after the review is created and are counted in the audit
entry.

The review page shows the change kind of every file. Every file of a review that has a baseline commit opens as a
**Diff**: "x added, y removed", the
baseline on the left and the head on the right (stacked on a narrow screen), each with its own line numbers, a run of
removed lines next to the run of added lines that replaces it, and the unchanged lines collapsed behind **Show N
unchanged lines**. Point at a line (or tab to it) and click the **+** that appears to comment on it; the left side is
commented on in the baseline and the right side in the head, and the thread list says "Before: line N" or
"After: line N". A modified file can also be read as
**Head** (the numbered lines with their comments) or as **Baseline** (read-only), and a file too large to compare
line by line (more than 10,000 lines on either side, or more than 2,000 added and removed lines together) offers
those two. A deleted file opens on its removed lines and accepts comments on them or on the whole file; it offers
**Baseline** but no **Head**. An added file opens on its lines shown as added and accepts comments on them or on the
whole file; it offers **Head** but no **Baseline**, because nothing existed there. An upload, and a review created
without a baseline revision, show the numbered head only. The
content fingerprint covers the files that exist at the head (Added and Modified), calculated as ReviewMark does. The
Details section shows the repository name, the two commit SHAs, and the selection.

The record digest of a review from a repository covers the repository, the baseline and head commits, the selection,
the review-set the review was planned for (if any), and every change with the hashes of its baseline and head content,
so a signature binds to exactly what was selected. The digest has a new format (version 4, which also covers the side
of every line comment); a database from an earlier build cannot be used and must be recreated.

### Review-Sets in the Repository

A repository can state which files are reviewed together in the `reviews:` list of its `.reviewmark.yaml` file in the
root of the repository (the file that ReviewMark reads). ReviewCenter reads only these fields of each entry and ignores
every other key:

```yaml
reviews:
  - id: Core
    title: Review of the Core subsystem
    paths:
      - "src/Core/**/*.cs"
      - "!src/Core/Generated/**"
```

The `id` and `title` are required. A review-set without paths, or whose paths select nothing valid, cannot be planned.
The paths use the selection syntax described above.
ReviewCenter reads the file of the head revision of the repository as it is, with these limits: the file is at most
256 KiB, it has at most 500 review-sets, an `id` has at most 100 characters, a `title` at most 200, a review-set at most
100 paths, and a path at most 400 characters. Two entries with the same `id`, an entry that is not valid, YAML
aliases, and a file that is not valid YAML are refused with a message that names the problem. A review made for a
review-set keeps the `id` of the set, so that the next review of the same set can start from the last signed one.

## Planning Reviews

When the repository has review-sets, choose **Plan reviews from a repository** on the project page (it is shown when a
repository is enabled). Enter the **head revision** and choose what to review: the changes since the last signed review
of each set (the default), the changes since a branch, tag, or commit, or all files of each set. **Show plan** reads
the repository and creates nothing yet.

The plan starts with one plain sentence, for example "3 sets need review, 1 already filed, 9 have nothing to review.",
and says what to do next. The sets that need a review come first, each ticked, with a note about its baseline and the
number of changed files; clear the tick of the ones you do not want now, and choose **Create N reviews**. A set that
cannot be planned (for example because its patterns are not valid) and a set that is already filed (with a link to its
review) are listed only when there are some, and the sets with nothing to review are collapsed behind a count. After
the creation the page lists one result per set, a link to the new review or the reason it failed; a failed set does not
stop the others, and **Plan again** offers it once more. When the plan cannot be made (the repository is disabled, the
revision is unknown, or there is no valid `.reviewmark.yaml`) the page says so in plain words and what to do. The
review page shows the set of a planned review as **Review set** in the Details.

## Sharing Links

Every file, line and thread of a review has a stable link, and the address bar of the browser always holds the link of
what is shown (choosing a file or a thread changes it without adding history entries):
`.../reviews/{reviewId}/files/{path}` opens a file, `?line=N&side=head|baseline` adds a highlighted line, and
`.../reviews/{reviewId}/threads/{threadId}` opens the thread. Each thread has a **Link** and a **Copy link** button. A
link to something that does not exist shows a message and a way back to the file list, and a signed-out visitor is
returned to the same link after signing in. The user guide has the details.

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
- **Sessions are not cut short at once**: a user who is disabled, removed, or changed is signed out of an open page
  only at its next check, so for up to the interval (30 seconds by default; `Auth:RevalidationSeconds`) the open
  page still accepts actions that are not re-checked, such as answering checks and adding comments and threads.
  Signing, changing reviewer slots, and user administration re-check the account on every action and are refused at
  once. Once the page's sign-in state has ended, every action that changes something (including creating projects and
  reviews and resolving threads) is refused with a message and saves nothing. The name in the page header stays the
  same until you navigate or reload the page, and changing a password
  does not end the user's other sessions.
- **Checklists are fixed per review**: every review gets a copy of the default checklist when it is created and
  keeps it; there is no project-level checklist editor or import yet. Checklist answers are not linked to comment
  threads.
- **Comments cannot be edited or deleted**: a posted comment is permanent; only the thread state (open or resolved)
  can be changed.
- **Signed slots cannot be changed**: a reviewer slot that has any signature cannot be reassigned or removed; to
  change the reviewers after a signature, add a new slot. Anyone who is signed in can add and change unsigned slots.
- **Audit trail limits**: the hash chain detects changes in the middle of the history, but not the removal of the
  newest entries or a complete rewrite by someone with write access to the database, unless the head hash was
  recorded elsewhere (see [Audit Trail](#audit-trail)); there is no external anchoring, no export, and no retention
  or archiving of the trail. Failed attempts on an account that is already locked are not recorded. A wrong current
  password at a signature or on the account page is recorded as `auth.sign-in-failed`, as it counts toward the same
  lockout. Sign-in events are recorded on a best-effort basis.
- **Approval is computed**: the Approved or In review status is calculated from the record and its signatures each
  time it is shown; it is not a stored review state, and the review state is still Draft.
- **Attestation limits**: the default signing key is self-signed (development and test only), the signing time is the
  server clock with no time stamp authority, and the signature does not identify the reviewers; see
  [Signed Attestation](#signed-attestation).
- **Comments are not live**: comments posted by other people appear when you reload the page or take an action on it.
- **Upload limits**: at most 200 files per review, 2 MiB per file, and 20 MiB per review. Only the file name of each
  uploaded file is kept, so two files with the same name cannot be in one review.
- **Repository review limits**: a review from a repository has the same limits as an upload, counted over the baseline
  and head content of the changed files. Symbolic links and submodules are not reviewed; the ones that match the
  selection are listed once when the review is created and counted in the audit entry. `HEAD~1`-style revisions
  are not supported; use a branch, a tag, or a commit.
- **Repositories**: there is no Linux or Windows secret store for the key
  ring, no
  credential helper or SSH key support, and no repository deletion.
- **No database migrations**: the database tables are created when the database file is new; a later release that
  changes the tables will need a migration step. A database file created by an earlier version, before users,
  comments, checklists, reviewer signatures, or the audit trail existed, does not have the tables or columns that are
  needed, so **delete it and start again** (development databases only).

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

- The remaining review features of the ReviewCenter web service: review states beyond Draft
- External anchoring, export, and retention of the audit trail
- Password reset, multi-factor sign-in, and external identity providers
- EF Core database migrations with an explicit `migrate` command, and further database providers
  (PostgreSQL, SQL Server)
- PDF/A evidence export of review records
- Signing attestations with a key in Azure Key Vault (the key stays in the vault), and RFC 3161 time stamps for them
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
