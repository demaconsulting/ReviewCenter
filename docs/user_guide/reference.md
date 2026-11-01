# Command-Line Reference

This chapter is the reference for the command-line interface: the options, examples, the self-validation, silent
mode, logging, and error handling, followed by a note on the Puppet development tool.

## Command-Line Options

The following command-line options are supported:

| Option                                | Description                                                  |
| ------------------------------------- | ------------------------------------------------------------ |
| `serve`                               | Run the ReviewCenter web server until stopped                |
| `--urls <url>`                        | Listen URL for `serve` (default: `http://localhost:5000`)    |
| `user add`                            | Create a user; password from environment, stdin, or prompt   |
| `--username <name>`                   | User name for `user add`                                     |
| `--display-name <text>`               | Display name for `user add`                                  |
| `--system-admin`                      | Make the user of `user add` a system administrator           |
| `audit verify`                        | Verify the hash chain of the audit trail (exit code 0 or 1)  |
| `keys reprotect`                      | Roll a new Data Protection key and protect the secrets again |
| `-v`, `--version`                     | Display version information                                  |
| `-?`, `-h`, `--help`                  | Display help message                                         |
| `--silent`                            | Suppress console output                                      |
| `--validate`                          | Run self-validation                                          |
| `--check-config`                      | With `--validate`, also check the database and keys          |
| `--results <file>`, `--result <file>` | Write validation results to file (TRX or JUnit format)       |
| `--depth <#>`                         | Set heading depth for markdown output (default: 1)           |
| `--log <file>`                        | Write output to log file                                     |

## Examples

### Example 1: Basic Usage

```bash
reviewcenter
```

### Example 2: Self-Validation with Results

```bash
reviewcenter --validate --results validation-results.trx
```

### Example 3: Silent Mode with Logging

```bash
reviewcenter --silent --log tool-output.log
```

### Example 4: Run the Web Server on a Custom Port

```bash
reviewcenter serve --urls http://localhost:8080
```

Then open `http://localhost:8080` in a browser, or check `http://localhost:8080/health`.

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

When a validation test fails, the tool exits with a non-zero exit code.

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

✓ ReviewCenter_GitAvailable - Passed (432 ms): git version 2.51.0.windows.2 (minimum 2.31)
✓ ReviewCenter_GitRoundTrip - Passed (4363 ms): 2 commits, 2 changes, file read back unchanged
✓ ReviewCenter_ReviewWorkflow - Passed (8128 ms): embedded SQLite; author answer refused; reviewer without a slot
    refused; review answered and signed by a separate reviewer in the one slot; author signature refused;
    approved review locked and reopened by an administrator;
    evidence index fingerprint 13e028da0629…, record digest 5ab1c0de42f7…
✓ ReviewCenter_Fingerprint - Passed (28 ms): 3 files, fingerprint 13e028da06299fcedffff47b2b55cca46a68111f3124d77d355364339c4215f2
✓ ReviewCenter_PasswordHash - Passed (219 ms): right password accepted, wrong password refused, hashes salted
✓ ReviewCenter_Totp - Passed (12 ms): RFC 6238 SHA-1 vectors, window +/-1, replay step returned
✓ ReviewCenter_AuditChain - Passed (1152 ms): 3 entries intact; change of entry 2 detected
✓ ReviewCenter_AttestationPdf - Passed (343 ms): CMS signature and DocMDP verified; changed byte detected (75964 bytes)
✓ ReviewCenter_HostStart - Passed (1590 ms): 127.0.0.1:58493; /login and /_framework/blazor.web.js 200; stopped cleanly

Total Tests: 9
Passed: 9
Skipped: 0
Failed: 0
Total Time: 8.1 s
```

The detail text after each test and the times are examples; they differ from run to run (the detail of the workflow test
is wrapped here, and it is one line in the report). The tests run at the
same time, so their individual times overlap and do not add up to the total time.

### Validation Tests

Each test proves specific functionality works correctly. The default run is hermetic: every test creates its own
data in a uniquely named temporary folder that is deleted at the end. It never reads or changes the configured
database or the configuration, never listens on the configured port, uses no network except the loopback
interface of the machine, and needs neither Docker nor a browser. The names below are the names in the TRX and
JUnit files.

- **`ReviewCenter_GitAvailable`** - the installed git is found and is recent enough.
- **`ReviewCenter_GitRoundTrip`** - a local repository can be committed to, mirrored and read back by the git
  layer of the server.
- **`ReviewCenter_ReviewWorkflow`** - on an embedded SQLite database, a project with two members is created, and one
  member creates a review. The check then shows that the author is refused as a reviewer, as an answerer of the
  checklist, and as a signer, that a member without a reviewer slot cannot answer, and that the other member, who holds
  the one slot, answers its checklist and signs after a password re-authentication. It shows that the approved review is
  locked (a change of an answer is refused), that an administrator can reopen it with a reason, and that the evidence
  index shows the fingerprint and the record digest.
- **`ReviewCenter_Fingerprint`** - the content fingerprint of fixed files equals a pinned value on every platform.
- **`ReviewCenter_PasswordHash`** - the password hasher accepts the right password and refuses a wrong one.
- **`ReviewCenter_Totp`** - the one-time codes of the second factor (see
  Two-Factor Sign-in in the Users and Sign-in chapter) equal the published test vectors of RFC 6238, the window of
  one 30 second step either side is accepted and two steps are refused, and the secret has the form that
  authenticator apps read. A library or platform that computed other codes than the authenticator apps would lock
  people out, so this is checked.
- **`ReviewCenter_AuditChain`** - the audit trail verifies, and a changed entry is reported by the verifier.
- **`ReviewCenter_AttestationPdf`** - a signed attestation PDF is produced with a temporary key, its signature and
  its change-prevention certification verify independently, and a changed byte makes the verification fail.
- **`ReviewCenter_HostStart`** - the real web host starts from temporary settings alone on a free loopback port,
  answers `/login` and a static asset with 200, and stops cleanly. The host ignores an `appsettings.json`, the
  `ASPNETCORE_` and `DOTNET_` environment variables and any other ambient configuration, so a broken or unusual
  configuration of the machine cannot change this test.

Every test has a time limit of 60 seconds that the validation enforces itself. A test that does not finish in
time is reported as `Failed` with the message `Timed out after 60 s`, the other tests still complete, and the run
does not hang. The temporary folders are deleted at the end of each test; a file that is still locked for a moment
(for example by a virus scanner) is retried for a short time and, if it stays locked, left to the operating system's
temporary cleanup without failing the run.

### Checking the Configured Database and Keys

The default run never looks at your configuration. To also check that the database your server is configured to
use can be reached and has the schema of this version, and that the key ring decrypts what the database holds, add
`--check-config`:

```bash
reviewcenter --validate --check-config
```

This adds a tenth test, **`ReviewCenter_ConfiguredDatabase`**, and an eleventh, **`ReviewCenter_ConfiguredKeys`**.
The first loads the configuration the way `serve` does
(`appsettings.json` and the environment variables), recognizes the database provider, opens the database
**read-only**, and compares its tables and columns with the ones this version expects. It never writes to the
database and never creates it, and it never prints the connection string or other secrets; a problem is reported
by table and column name. A development database created before the tables and columns of this version existed (for
example `ReviewReopens`, `Projects.RequireIndependentReviewers`, and `Reviews.DueDate`) is reported that way; delete it
and let the server
create it again, because ReviewCenter has no migrations (see Database in the Administration chapter). If nothing is
configured and there is no database at the default location, the test is
**skipped**: the report shows `SKIPPED`, the summary counts it under `Skipped`, the results file records it as not
executed (TRX) or skipped (JUnit), and the run does not fail. `--check-config` is only valid together with
`--validate`. A database file that does not exist yet (a fresh installation, which creates it at the first start) is
also **skipped**, with that reason; a file that exists but is not a database of this version still fails.

**`ReviewCenter_ConfiguredKeys`** reads the key ring folder and the database **read-only** and creates nothing: no
key, no folder, no file. It reports the protection in force, the number of key files (and how many are not
encrypted), the number of protected secrets (stored repository tokens and second-factor secrets), and how many of them
the key ring cannot decrypt, for example `protection Dpapi; key files 1 (0 not encrypted); protected secrets 3
(repository tokens 2, second-factor secrets 1); unreadable 0; attestation key SelfSigned: subject 'CN=...', thumbprint
..., valid ... to ...`. The last clause describes the attestation signing key: the provider, subject, thumbprint, and
validity of the key, or `will be created on first start` when the self-signed key does not exist yet (with a note
`(expired; a new key is created at the first start)` for a key whose certificate has expired). It adds a warning when a
plaintext `attestation-selfsigned.pfx` of an earlier version is still in the attestation key folder (the server ignores
that file; delete it). It **fails** (exit code 1) when secrets cannot be
decrypted (the message gives the counts and the repository and user ids), when the key folder is empty or missing but
the database holds secrets, when the ring is not encrypted and `DataProtection:RequireEncryptedKeys` is true, when
the newest key cannot be decrypted, and when the protected attestation key file
`attestation-selfsigned.pfx.protected` cannot be decrypted with the key ring (restore the ring that protected it, or
delete the file to create a new key at the next start). With the `File` provider (or `CertificateStore`), the clause
describes the certificate, followed by `; warning:` and the text of each warning (the certificate ends within
`Attestation:ExpiryWarningDays` or has ended, a key without a password, a file that others can read), and the check
**fails** when the certificate path, the file, or the password is missing, the password is wrong, or the key is
refused by `Attestation:RequireTrustedKey` or `DataProtection:RequireEncryptedKeys`. With
`Attestation:RequireTrustedKey` set to true and no key yet, the check passes with `will be created on first start`, but
the server then refuses to start, because the new self-signed key is not trusted; use the `CertificateStore` or `File`
provider for a trusted key. It never prints a key, a secret, a password, or a path other than the key folder: a problem
with the attestation key names only the file name or the setting.
It is **skipped** when no database is configured or the database file does not exist yet, and when the database
holds no secret and the key folder does not exist yet. See Back Up and Restore in the Deployment chapter.

## Rotating the Data Protection Key

The `keys reprotect` verb moves every stored secret to a new Data Protection key, so that old key files and an old
certificate can be retired:

```bash
reviewcenter keys reprotect
```

It reads the same configuration as the server (`appsettings.json` and the environment variables), so run it with the
same environment, and **stop the server first**, and run only one copy of the command at a time: it is not safe to
run concurrently with itself or with the server. A running server may change a secret while the command works, and the
command then stops with an error for the table that it could not complete. It is the only verb that writes to the key
ring, and the only one that changes these stored secrets (other verbs, such as `user add`, write other data of the
database). It takes no option of its own; `--silent` and `--log <file>`
work as for the other verbs.

The command works in this order:

1. It first reads every stored repository token and second-factor secret with the key ring, as `--check-config` does. It
   **refuses**, with exit code 1, a message without any secret, and **no change at all** (no new key, no changed value),
   when a secret cannot be decrypted, when the key folder is empty but the database holds secrets, when the key ring
   settings are invalid, or when the database file does not exist. Re-protecting would not rescue what cannot be read;
   restore the right key folder first, and run `reviewcenter --validate --check-config` until it passes.
2. It creates a new key, protected the way the settings say (DPAPI, the certificate in
   `DataProtection:CertificatePath`, or the permissions of the folder), so switch the protection or the certificate
   **before** the command (see Rotate the Key Ring Protection in the Deployment chapter).
3. It protects every repository token and second-factor secret again with the newest key, in one transaction for each
   table: a table is either completely changed or not at all.
4. It prints the new key id, the number of tokens and secrets, the keys that stored secrets still reference (none after
   a successful run), the older keys of the ring that nothing references any more, how many key files are not encrypted,
   and what to do next. Keys are shown by their ids (GUIDs); a secret is never printed or logged.

A second run is fine: it creates another key and protects the secrets with it. The exit code is 0 after a successful
rotation and 1 after a refusal or a failure.

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

## Review Lock and Independence Reference

This section lists, in one place, the audit actions, messages, and settings of the lock of a concluded (approved or
rejected) review, of the ballots of the reviewers, and of the independent reviewers. The chapters Reviewing and
Administration explain how they work.

| Audit action                            | Detail                                                                    |
| --------------------------------------- | ------------------------------------------------------------------------- |
| `review.reopened`                       | `previous digest <digest>; reason: <reason>`                              |
| `checklist.answered`                    | `item <id>: <outcome>`, with a hash of the justification                  |
| `checklist.applicability-changed`       | `section <id>: applicable` or `not applicable`, with a hash of the reason |
| `ballot.submitted`                      | `slot <n>: ballot submitted (<a> answers, <s> section states)`            |
| `ballot.reopened`                       | `slot <n>: ballot reverted to draft by an edit`                           |
| `ballot.reassigned`                     | `slot <n>: ballot reverted to draft after a reassignment`                 |
| `project.independent-reviewers-changed` | `RequireIndependentReviewers on->off` (or `off->on`)                      |
| `project.review-rule-changed`           | `<setting> on->off` (or `off->on`)                                        |

For `project.review-rule-changed` the setting is `RequireNonAuthorChecklistAnswers` or `RequireNonReopenerSigning`.

The messages that the user sees:

- "This review is concluded and locked. A project administrator must reopen it before it can change." for a change of
  a concluded (approved or rejected) review, and for a signature on it.
- "The review is not locked, so there is nothing to reopen." for a reopen of a review that is not concluded.
- "A rejected review needs a conclusion stating the reasons" as the blocker of a signature, and as the refusal of a
  signature (rule ConclusionRequired, status 403), when the ballots agree on a Fail and the review has no conclusion
  text.
- "The review was rejected. A signed attestation of a rejected review is not available yet." for the attestation
  of a rejected review.
- "A reason of at most 250 characters is required to reopen a review." for a reopen with no reason or a longer one.
- "You created this review, so you cannot be a reviewer of it. A different project member must review and sign it, or
  a project administrator can switch off 'Require independent reviewers'." when the author tries to sign.
- "*Name* created this review and cannot be one of its reviewers while independent reviewers are required." when a
  slot is added or changed to hold the author.
- "You created this review, so you cannot answer its checklist in this project. Another project member must answer it,
  or a project administrator can switch off 'Authors cannot answer the checklist of their own review'." when the author
  answers a check or changes a section of the checklist.
- "You reopened this review, so you cannot sign it again. Another reviewer must sign it, or a project administrator can
  switch off 'The person who reopened a review cannot sign it again'." when the person who reopened a review tries to
  sign it, and "Slot 1 would have no one who may sign; ask another administrator or switch the rule off." (the slot
  numbers are listed) when a reopen would leave a slot without anyone who may sign.
- "You are not a reviewer of this review, so you cannot answer its checklist. Add yourself as a reviewer slot first."
  when a user who holds no reviewer slot (an administrator included) answers a check, changes a section, or hands in a
  ballot.
- "The ballot cannot be submitted while a check that applies has no answer." when an incomplete ballot is handed in.
- "Hand in your ballot before you sign: the signature covers the consolidated ballots, and yours is not among them yet."
  when a reviewer signs while the own ballot is a draft.
- "The setting was changed by someone else. Reload the page and try again." when the review rules were changed while
  the page was open.
- "Reviewer slots and signatures cannot be changed with an API token; sign in to the web pages." for a reopen with an
  API token.

The setting **Require independent reviewers** is a per-project setting on the project page, on by default, that only a
project administrator or a system administrator changes in the web pages. The author that it compares is the
ReviewCenter user who created the review (the owner of the token for a review created with an API token). Git commit
authors are not matched. Two further per-project settings, **Authors cannot answer the checklist of their own review**
and **The person who reopened a review cannot sign it again**, are on by default and changed in the same way;
ReviewCenter applies and audits them.

> **These rules apply to all existing projects.** The rules are on by default and apply to ALL existing projects, also
> those created before this version (the stored columns default to on). A project that needs one person to do
> everything switches them off on the project page.

A development database created before the approval lock existed lacks the table `ReviewReopens` and the column
`Projects.RequireIndependentReviewers`, and one created before the review rule switches lacks the columns
`Projects.RequireNonAuthorChecklistAnswers` and `Projects.RequireNonReopenerSigning`. ReviewCenter has no migrations,
so delete such a database and let the server
create it again (see Database in the Administration chapter). The same holds for a database created before the due date
of a review existed: it lacks the column `Reviews.DueDate`, which is created only for a new database (no migration). The
same holds for a database created before the follow-up review existed: it lacks the columns `Reviews.FollowsReviewId`,
`Reviews.CarriedAnswerCount`, `Reviews.CarriedThreadCount`, and the carried-from columns of the answers, the section
states, and the threads.

## Inbox Reference

This section lists the rules of the inbox (see Your Inbox in the Reviewing chapter) in one place.

| Group | A review is listed when | Needs action |
| ----- | ----------------------- | ------------ |
| To sign | You are primary or alternate of a slot that is not signed or must be signed again; not concluded | Yes |
| Replies waiting | Open thread ends with another's comment; you hold a slot and wrote in it, or authored it | Yes |
| Failed checks | You authored the review, it is not concluded, and the ballots agree a check is Fail | Yes |
| Updates | Someone else concluded (approved or rejected) or reopened the review after you last saw it | No |

- Only members who may contribute are given To sign and Replies waiting; an auditor gets neither. A user who is
  disabled, has a temporary password, or is not a known user gets an empty inbox.
- A failed-checks entry reads "A Fail the reviewers agree on makes the review reject; a Fail only one reviewer gave is
  a disagreement to resolve."; the chip of an update is
  "Approved", "Rejected", or "Reopened".
- A replies-waiting entry names the kinds of the threads that wait, for example "Waiting for your reply: 1 open finding,
  2 open questions." The counts of the review page and of the progress lists use the same words: a **Question** is
  conversation (chat), a **Finding** goes into the signed report with its resolution note, and a thread of either kind
  that is open blocks signing.
- The count on the home page is the number of distinct reviews in the first three groups, so a review in two groups
  counts once. Updates are never counted. The badge of the Inbox tab in the header shows the same count, read when a
  page is opened and kept for 30 seconds for each user, so it can be up to 30 seconds behind; it is hidden at zero and
  shown as 99+ above ninety-nine.
- A comment is unread when it is by someone else and newer than the last comment you saw in that review. Your own
  comments are never unread, whatever the case of your user name. A review to sign is marked New again when someone
  else signs it, hands in a ballot or (in a review with one slot) answers a check, writes a comment, or reopens it
  after you last saw it. Another reviewer's draft ballot never marks it.
- The read state is one row for each user and review, in the table `ReviewUserStates`, with the last time and the
  highest comment seen. It only moves forward, it is removed with its review or user, and it is neither audited nor
  part of the record digest.
- A development database created before the inbox existed lacks the table `ReviewUserStates` and the indexes on the
  reviewer slots and the creator of a review. ReviewCenter has no migrations, so delete such a database and let the
  server create it again (see Database in the Administration chapter).

## Puppet Development Tool

Puppet is a small developer helper for AI agents, not part of the distributed ReviewCenter executables. It keeps one
persistent headless Chromium open and drives it one command per shell call (go to a page, click, hover, fill, read
text, screenshot) against a running ReviewCenter web interface. See `tools/puppet/README.md` for the setup, the
commands, and the workflow. Each agent session can run its own browser by setting the environment variable
`REVIEWCENTER_PUPPET_CONTROL_FILE` to its own file path.
