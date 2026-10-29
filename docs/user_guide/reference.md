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
| `-v`, `--version`                     | Display version information                                  |
| `-?`, `-h`, `--help`                  | Display help message                                         |
| `--silent`                            | Suppress console output                                      |
| `--validate`                          | Run self-validation                                          |
| `--check-config`                      | With `--validate`, also check the configured database        |
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
✓ ReviewCenter_ReviewWorkflow - Passed (8128 ms): embedded SQLite; review signed; evidence index read back
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

The detail text after each test and the times are examples; they differ from run to run. The tests run at the
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
- **`ReviewCenter_ReviewWorkflow`** - on an embedded SQLite database, a project and a review are created, the
  checklist is answered, the review is signed after a password re-authentication, and the evidence index shows the
  fingerprint and the record digest.
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

### Checking the Configured Database

The default run never looks at your configuration. To also check that the database your server is configured to
use can be reached and has the schema of this version, add `--check-config`:

```bash
reviewcenter --validate --check-config
```

This adds a tenth test, **`ReviewCenter_ConfiguredDatabase`**. It loads the configuration the way `serve` does
(`appsettings.json` and the environment variables), recognizes the database provider, opens the database
**read-only**, and compares its tables and columns with the ones this version expects. It never writes to the
database and never creates it, and it never prints the connection string or other secrets; a problem is reported
by table and column name. If nothing is configured and there is no database at the default location, the test is
**skipped**: the report shows `SKIPPED`, the summary counts it under `Skipped`, the results file records it as not
executed (TRX) or skipped (JUnit), and the run does not fail. `--check-config` is only valid together with
`--validate`.

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

## Puppet Development Tool

Puppet is a small developer helper for AI agents, not part of the distributed ReviewCenter executables. It keeps one
persistent headless Chromium open and drives it one command per shell call (go to a page, click, hover, fill, read
text, screenshot) against a running ReviewCenter web interface. See `tools/puppet/README.md` for the setup, the
commands, and the workflow.
