# Introduction

## Purpose

ReviewCenter is a web-based formal code review service with a companion command-line
interface. The service is an ASP.NET Core Blazor Server application started with the `serve`
verb. The current release lets you sign in, create projects and draft reviews made from uploaded files, shows the
content fingerprint of each review, lets you discuss the files in threaded comments, gives each review a
checklist, and lets reviewers sign a review with their password, with every action recorded under the signed-in
user. Every change is also recorded in a hash-chained audit trail that can be read and verified. The remaining review
feature (review states beyond Draft) is still under development. The command-line
interface is documented in
this guide. A separate development tool,
Puppet, is described near the end of this guide.

## Scope

This user guide covers:

- Installation instructions
- Usage examples for common tasks
- Running the web server, creating the first user, signing in, creating projects and reviews, and configuring the
  database
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
  when pressed, a counter button that increments a displayed count, and a link to the projects page. It is open to
  everyone.
- `/login` - the sign-in page (open to everyone).
- `/account` - your account, with a form to change your password.
- `/users` - the user administration page (administrators only).
- `/projects` - the list of projects, with a form to create a new project.
- `/projects/{id}` - one project, with its reviews and a form to create a review.
- `/projects/{id}/reviews/{reviewId}` - one review, with its files, their content, and comment threads.
- `/projects/{id}/reviews/{reviewId}/checklist` - the checklist of a review.
- `/health` - a health check that answers with status 200 and the text `Healthy` while the server is running (open
  to everyone).

Every page except `/`, `/login`, and `/health` needs a signed-in user; see [Users and Sign-in](#users-and-sign-in).

The `--urls` option is only valid together with `serve`; using it without `serve` is reported as an error.
The `--silent` option suppresses the server's console log output.

## Users and Sign-in

ReviewCenter has local user accounts. Each user has a user name, a display name (shown next to comments and
answers), a role, and a password, of which only a salted hash is stored. The role **Admin** may also manage users;
the role **Reviewer** may do everything else. Every comment, checklist answer, and change of section applicability is
recorded under the signed-in user, who cannot be chosen or typed in.

### Create the First Administrator

A new database has no users, so nobody can sign in yet. Create the first administrator on the command line, with the
server stopped or running:

```bash
reviewcenter user add --username admin --display-name "Ada Admin" --role Admin
```

The options are `--username` (3 to 50 letters, digits, `.`, `_`, or `-`; unique regardless of letter case),
`--display-name` (1 to 100 characters), and `--role` (`Admin` or `Reviewer`; `Reviewer` when omitted). The user is
created in the configured database (see [Database](#database)). The password is never given as an option, because a
command line is visible to other processes and is kept in shell history. It is read from the first of these that is
available:

1. The `REVIEWCENTER_PASSWORD` environment variable.
2. The first line of standard input, when input is redirected.
3. An interactive prompt that does not show what you type and asks twice.

```bash
# Linux: from the environment
REVIEWCENTER_PASSWORD='a long passphrase' reviewcenter user add --username admin --display-name "Ada Admin" --role Admin
```

```powershell
# Windows PowerShell: from standard input
"a long passphrase" | .\reviewcenter.exe user add --username admin --display-name "Ada Admin" --role Admin
```

The command prints `Created user 'admin' with the role Admin.` and never prints the password. When the user cannot
be created (the user name is taken, the password is too short, no password was provided, or the database cannot be
used) it prints an error and exits with a non-zero exit code.

### Sign In and Out

Open any page, or `/login`, enter your user name and password, and press Sign in. After signing in you are taken to
the page you asked for. A failed sign-in always shows the same message, whatever the reason (unknown user name, wrong
password, disabled account, or locked account), so the message does not reveal which user names exist. The page header
shows your display name, an Account link, and a Logout button; when nobody is signed in it shows a Sign in link
instead.

The sign-in is kept in a session cookie that lasts 8 hours of activity by default and is gone when the browser is
closed. Change the length with the `Auth:SessionHours` setting (environment variable `Auth__SessionHours`), a number
of hours above 0; the server does not start when the value is not valid. Disabling a user or changing the role of a
user takes effect the next time that user loads a page, and on a page that is already open at its next check (see
Session Ended on an Open Page).

| Setting                    | Environment variable        | Meaning                                       |
| -------------------------- | --------------------------- | --------------------------------------------- |
| `Auth:SessionHours`        | `Auth__SessionHours`        | Hours of sign-in session (default 8; > 0)     |
| `Auth:RevalidationSeconds` | `Auth__RevalidationSeconds` | Seconds between open-page checks (default 30) |

### Session Ended on an Open Page

A page that you leave open checks every 30 seconds by default that you are still allowed to use it. Change the
interval with the `Auth:RevalidationSeconds` setting (environment variable `Auth__RevalidationSeconds`), a number of
seconds that is at least 5; the server does not start when the value is lower. The check ends your session when your
user was disabled or removed, or when your user name, display name, or role was changed. When that happens the page
content is replaced by a **Session ended** message with a link to sign in again, and the page no longer shows data or
accepts actions. If the check cannot be completed, for example because the database cannot be read, the session is
treated as ended too, so a user who cannot be verified is never kept signed in.

Some things to know:

- The check is periodic, so for up to one interval after a change the open page still shows its content and accepts
  actions that are not re-checked, such as answering a checklist check or adding a comment. Signing a review and
  every action on the Users page check the account again each time and are refused at once. Once the page itself has
  ended, every action that changes something (for example creating a project or review, or resolving a thread) is
  refused with a message and saves nothing.
- The page header, which shows your display name, keeps the old name until you navigate to another page or reload.
  Anything you typed on the page that ended is lost, because the page content is replaced.
- Use the link in the message to sign in again, and you return to the page you were on.

### Password Rules and Lockout

- A password has 12 to 128 characters. There are no other composition rules, so a long phrase is a good password.
  Spaces are part of the password.
- After 5 failed sign-ins in a row for a user name, that user name is locked for 5 minutes, even for the right
  password. A successful sign-in before that resets the count.

### Account

Open `/account` (the Account link in the header) to change your password: enter the current password and the new
password, and press the change button. A wrong current password counts as a failed sign-in for the lockout. The
fields are cleared afterwards.

### Manage Users (Administrators)

Open `/users`; a user who is not an administrator is refused with an "Access denied" page that links back to the
projects. The page lists the users with their user name, display
name, role, and state. An administrator can:

1. Create a user with a user name, display name, role, and password.
2. Disable a user, which stops the user from signing in without deleting the record of what the user did, and enable
   the user again.
3. Change the role of a user.
4. Set a new password for a user, for example when the user has forgotten it. This also ends a lockout.

The last enabled administrator cannot be disabled or demoted; the page shows an error instead. If you are no longer
an enabled administrator, the page shows an error for every action.

### Secure Cookie Behind a Reverse Proxy

The sign-in cookie is marked Secure when the request reaches the server over HTTPS. When a reverse proxy ends TLS and
forwards plain HTTP to ReviewCenter, set the environment variable `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` for the
server, so that it trusts the forwarded scheme of the proxy and still marks the cookie Secure. Use HTTPS whenever the
server can be reached from other machines.

## Projects and Reviews

A project groups the reviews of one body of work. A review is a named set of files whose content is stored
unchanged, together with a fingerprint of that content.

1. Open `/projects`, enter a project name (up to 100 characters) and an optional description (up to 1000
   characters), and press the create button. The project appears in the list; select it to open its page.
2. On the project page, enter a review identifier (1 to 100 letters, digits, `.`, `_`, or `-`, starting with a
   letter or digit; unique within the project) and a title (up to 200 characters), choose the files to review, and
   press the create button. The chosen files are listed with their sizes before you submit.
3. The review page opens. From the top it shows the title, a line that says what to do next, the files with their
   size and open comment threads, and the thread index. Select a file to read
   its content as numbered lines (see [Review Comments](#review-comments)); the page scrolls to the code. Binary
   files, and files that are not valid UTF-8 text, show a notice instead of their content. At the bottom, the
   collapsed "Details" section holds the identifier, state (`Draft`; the only state in this release), the review
   fingerprint, and the SHA-256 hash of each file; open it when you need them.

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

### Limitations of Sign-in

- There is no password reset, no email, no multi-factor sign-in, and no external identity provider. An administrator
  sets a new password for a user who forgot it.
- The lockout counts failures per user name, not per source address, so someone can keep a user name locked by
  guessing wrongly on purpose.
- The sign-in state of an open page is re-checked only every `Auth:RevalidationSeconds` seconds (30 by default), so a
  disabled or changed user can still use some actions of an open page for up to that long; see Session Ended on an
  Open Page. Changing a password does not end the user's other sessions. Opening a page that needs a role from an
  already open page is checked against the role you had when the page was opened.

## Review Comments

Select a file on the review page to read it as numbered lines and to discuss it in comment threads. The line at the
top of the page says what is left to do: start a discussion, how many open threads are to be resolved, or that all
are resolved (it also counts the unanswered checks of the [Review Checklist](#review-checklist)). The file list shows
the number of open threads of each file.

The thread index lists the threads of all files with the file, the lines, the state, the number of comments, the
author of the first comment, and the time of the last activity, newest first. Use the All, Open, and Resolved
buttons (which show the totals) to filter it; it starts with Open. Press a row to show that thread: its file is
selected, its lines are highlighted, and the page scrolls to them.

1. To start a thread, either press the Comment button of a line, which fills in that line, or type a first and a last
   line number yourself (a first line without a last line means that one line), or leave both line fields empty to
   comment on the whole file. The form shows where the comment will go. Enter the comment text (1 to 4000
   characters) and press the start button. The author of the comment is you, the signed-in user.
2. A thread is listed below the file's code with the lines it covers (the whole file, a line, or a range of lines)
   and its comments in the order they were posted; the form to start a new thread follows it. Lines that have a
   thread are marked.
3. Any signed-in user can reply to a thread. Press the resolve button to mark a thread resolved, and the reopen
   button to open it again. A resolved thread is shown as one line (lines, state, and number of comments); press its
   expand button to read it, reply, or reopen it, and the collapse button to fold it again.

Lines are numbered from 1. Windows (CRLF) and Unix (LF) line endings count the same, a line ending at the very end
of a file does not add a line, and an empty file has no lines. A binary file, or a file that is not valid UTF-8, and
an empty file can be commented on as a whole only. A line number must be a whole number inside the file, and the
first line must not be after the last line. Leading and trailing spaces of the text are removed. A
problem is shown as a message next to the form (or inside the thread for a reply) and nothing you typed is lost.

Posted comments cannot be edited or deleted, so the record of the discussion stays complete; only a thread's state
changes. Comments posted by other people appear when you reload the page or take an action on it. Comment text is
always shown as plain text; any markup in it is displayed as typed.

## Review Checklist

Every new review gets a checklist: a copy of the default checklist, taken from the Continuous Compliance review
template, with 14 sections and 58 checks. The review page shows a Checklist card with a summary such as "37 of 58
checks answered" and a link to the checklist page; the review page's next-step line also counts the unanswered
checks (for example "12 checklist checks unanswered, 1 open thread to resolve.") and says "Checklist complete and
all threads resolved." when nothing is left.

The checklist page tells you what to do next at its top: how many checks are unanswered, the overall progress, and
a "Next unanswered check" button that opens the section of the first unanswered check and brings it into view.
Below that, every section is listed with its progress (for example "3 of 8"); only one section is open at a time,
and the first section with unanswered checks is open when you arrive.

1. Open a section and choose Pass, Fail, or N/A for each check. A Pass is saved at once. For Fail and N/A a
   justification (up to 2000 characters) is required: type it in the box that appears and press Save. A missing
   justification is reported next to the box, and nothing is saved until it is given. You can change an answer at any
   time. Each answer is stored under your user, together with the UTC time, and shown as "Answered by" with your
   display name.
2. If a section does not apply to the review, clear its "This section applies to the review" switch, give the reason
   (required), and press Save reason. A section that does not apply counts as done and its checks leave the totals.
   Switch it on again to answer its checks; the reason is then cleared.

The checklist is complete when every check of every applicable section is answered. A Fail is a valid answer: it
counts as answered, and its justification records the finding.

The checklist of a review is fixed when the review is created: a later release with a different default checklist
does not change reviews that already exist. Checklist answers are not linked to comment threads. The checklist
page is made for narrow screens too.

## Review Signatures

A review is approved when its reviewers have signed it. The review page shows an **Approved** or **In review** badge
next to the review title, and a **Reviewers** card with the reviewer slots. The project page shows the same badge for
each review.

**Reviewer slots.** A slot has a primary reviewer and, optionally, an alternate reviewer, chosen from the enabled
users. Either of the two can sign the slot. A review can have any number of slots, or none; a review without a slot
stays In review and its next step says "Add a reviewer slot.". Any signed-in user can add a slot (open **Add a slot**,
choose the reviewers and press **Add slot**) and change or remove a slot (open **Change reviewers**, then **Save** or
**Remove**). A user can be in only one slot of
a review, and the primary and the alternate must be different users. A slot that has been signed can no longer be
changed or removed, even if its signature is out of date; to change the reviewers, add a new slot.

**Signing.** The reviewer of a slot sees a signing form on the slot, if the review can be signed. The form shows
the statement of the signature, "I have reviewed this record and approve it.", and asks you to confirm your password.
Enter your password and press **Sign**. The password is checked like at sign-in, so wrong passwords count toward the
same lockout, and it is never stored or shown. A review can be signed only when:

- it has a checklist, and every check of an applicable section is answered;
- no check of an applicable section is answered Fail; and
- no thread is open.

While one of these holds back the signature, the slot lists what is left instead of the form (for example "2 threads
are open"). Each signature records your name, your role in the slot (primary or alternate), the UTC time, and the
statement.

**What a signature covers.** A signature is bound to the record as it was when you signed. The record is the content
fingerprint of the files, every checklist answer with its justification, every section marked as not applicable with its
reason, and every comment thread with its comments. The **Record digest** in the Details section (shortened; the full
value is its tooltip) identifies that record. Who answered a check, and when, is not part of it. If the record changes
while you are signing, the signature is refused and you are asked to review it and try again.

**Out of date and Approved.** A slot shows "Signed by *name* on *date* UTC" while it has a signature of the current
record, "Signature out of date" when it has signatures but the record has changed since (for example a new comment or
a changed answer), and "Not signed" otherwise. A review is **Approved** when it has at least one slot and every slot is
signed for the current record; it goes back to In review when a change makes a signature out of date. The reviewer then
signs the new record again; the earlier signatures stay as history.

A user who is disabled while a page is open is signed out of that page at its next check (every 30 seconds by
default), and the page is replaced by the Session ended message. Until then the page can still accept actions that are
not re-checked, such as answering checks and adding comments and threads. Signing and changing reviewer slots check
the account again on every action and are refused for a disabled user at once.

Approved is computed each time from the record and the signatures; it is not a stored state of the review. Every
signature is also recorded in the audit trail, which is described in the next section.

## Audit Trail

ReviewCenter records every change that the application makes to projects, reviews, comments, checklists, reviewer
slots, signatures, and users, and every sign-in, sign-out, failed sign-in, and lockout. An entry shows the time (UTC),
the person, the action, and a short detail. A change and its entry are stored in one database transaction, so the
application never leaves a change without its entry. A password is never recorded. The text of a comment or of a
justification is not recorded either; the detail holds only a digest prefix (eight hexadecimal characters of its
SHA-256 hash), which lets you match an entry with a text without copying the text. Entries cannot be edited or
deleted through the application.

### History of a Review

The review page has a **History** link. It opens the history page of the review, which any signed-in user can read.
The page lists the entries about that review, newest first, 50 at a time, with **Older** and **Newer** links. The
text of every entry is shown as plain text.

### The Audit Page (Administrators)

An administrator sees an **Audit** link in the page header. It opens `/audit`, which lists the whole trail (for all
projects and reviews, sign-ins, and user administration), newest first, 50 entries at a time. Other users are refused.
The page has a **Verify chain** button. It reads the whole trail once and shows either that the chain is intact, with
the number of entries and the **head hash**, or the first entry at which the chain is broken.

### The Chain

Each entry stores the SHA-256 hash of the entry before it and its own hash, which is calculated from the previous hash
and from every field of the entry. A change to an old entry, the removal of an entry from the middle, an inserted
entry, or a changed order makes a hash or a link no longer match, and verification reports the first entry where this
happens.

### Verify from the Command Line

```bash
reviewcenter audit verify
```

The command reads the configured database (the same settings as `serve`) and checks the chain. For an intact chain it
prints `Audit chain intact: N entries` and exits with code 0. For a broken chain it prints
`Audit chain BROKEN at entry N` to the error output and exits with code 1. If the audit trail cannot be read it prints
an error and exits with code 1. This makes the command usable in a scheduled job. The command does not print the head
hash; use the Audit page to see it. If the database file does not exist, the command creates an empty one.

### What the Chain Proves, and What It Does Not

The chain **detects** edits, deletions, insertions, and reordering of entries in the middle of the history.

The chain does **not** detect the removal of the newest entries, because the remaining entries are still a valid
chain. It also does **not** detect a complete rewrite by someone who can write to the database and who recalculates
all hashes, because the hashes use no secret key. To detect either, record the **head hash and the entry count**
shown on the Audit page outside the database, for example in a ticket, a signed email, or a write-once log, and
compare them with the values shown later: the entry count must not be lower, and while no new entries have been
added the head hash must be the same. This release has no external anchoring of its own; recording the
head hash is a manual step, and it is most useful after important events such as the approval of a review.

### Recorded Actions

| Group     | Action codes                                                                         |
| --------- | ------------------------------------------------------------------------------------ |
| Projects  | `project.created`, `review.created`                                                  |
| Slots     | `slot.added`, `slot.updated`, `slot.removed`, `signature.added`                      |
| Threads   | `thread.started`, `thread.replied`, `thread.resolved`, `thread.reopened`             |
| Checklist | `checklist.answered`, `checklist.applicability-changed`                              |
| Users     | `user.created`, `user.enabled`, `user.disabled`, `user.role-changed`                 |
| Passwords | `user.password-set-by-admin`, `user.password-changed`                                |
| Sign-in   | `auth.sign-in`, `auth.sign-out`, `auth.sign-in-failed`, `auth.locked-out`            |

### Limitations of the Audit Trail

- Sign-in, sign-out, failed sign-in, and lockout entries are recorded on a best-effort basis, in their own
  transaction. If an entry cannot be stored, the sign-in is still decided as usual and the failure is logged.
- Failed attempts on an account that is already locked are not recorded. Failed attempts for unknown user names are
  recorded without throttling, so the trail can grow quickly if someone guesses user names.
- A wrong current password at a signature or on the account page is recorded as `auth.sign-in-failed`, because it is
  checked in the same way as a sign-in and counts toward the same lockout.
- Comments, justifications, and the reason for marking a checklist section not applicable are recorded only as a short
  digest, never as text.
- There is no export and no retention or archiving of the trail, and no external anchoring.
- The audit trail is a new table. A development database created before it existed must be recreated (see the next
  section).

## Database

ReviewCenter keeps users, projects, reviews, file contents, comment threads, checklists, reviewer slots and
signatures, and the audit trail in a SQLite database
file. On first start
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
release: the tables are created only when the database is new. **A database file created by an earlier build, before
user accounts, reviewer signatures, or the audit trail existed, does not have the tables and columns that are
needed, and the pages cannot work with it: delete the file (or point `Database:ConnectionString` at a new one) and
start again, then create the first administrator.** Do this only with development data.

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
| `user add`                            | Create a user; password from environment, stdin, or prompt   |
| `--username <name>`                   | User name for `user add`                                     |
| `--display-name <text>`               | Display name for `user add`                                  |
| `--role <Admin\|Reviewer>`            | Role for `user add` (default: `Reviewer`)                    |
| `audit verify`                        | Verify the hash chain of the audit trail (exit code 0 or 1)  |
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
