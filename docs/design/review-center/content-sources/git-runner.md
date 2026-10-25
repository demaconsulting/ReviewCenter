### GitRunner

![ContentSources Structure](ContentSourcesView.svg)

#### Purpose

`GitRunner` runs the git command-line tool for the rest of the subsystem. Its single responsibility is to start git in a
way that cannot be abused by the repository content, the remote server, the arguments, or the environment of the server
account, and to give back the result with every secret removed. It also owns the settings type `GitOptions`.

#### Data Model

**GitOptions** (supporting type): the settings bound from the `Git` configuration section, read only after start-up.

- **CachePath**: `string` — the folder of the mirrors; empty until `Hosting` fills in the default (a `git-cache`
  folder beside the database).
- **TimeoutSeconds**: `int` — the time limit of one git command; 120 by default.
- **Timeout**: `TimeSpan` — the limit as a duration; a value below 1 second counts as the default.

**GitResult** (supporting type): the exit code, the standard output as bytes, and the standard error as redacted text.

**Constants**: the default output limit of a command (64 MiB), the number of bytes of error output that are kept (the
last 8 KiB), and the mask `***` that replaces a secret.

**State**: whether the git version was checked. The check is made once, before the first command, and a failed check is
repeated at the next call.

**Environment of the child process**: built by `BuildStartInfo`, never inherited.

- *Copied from the server*: `PATH`, `SystemRoot`, `SYSTEMDRIVE`, `windir`, `ComSpec`, `PATHEXT`, `TEMP`, `TMP`,
  `TMPDIR`, `SSL_CERT_FILE`, `SSL_CERT_DIR`, and the proxy variables (`HTTP_PROXY`, `HTTPS_PROXY`, `NO_PROXY`, in both
  cases).
- *Set by the runner*: `HOME`, `USERPROFILE`, and `XDG_CONFIG_HOME` pointing at a private `.git-home` folder under the
  cache path (the temporary folder when no cache path is set), `GIT_TERMINAL_PROMPT=0`, empty `GIT_ASKPASS` and
  `SSH_ASKPASS`, `GIT_CONFIG_NOSYSTEM=1`, `GIT_CONFIG_GLOBAL` set to the null device, `GIT_ALLOW_PROTOCOL=file:http:https`,
  `GCM_INTERACTIVE=never`, and `LC_ALL=C`.
- *Set only for a call with credentials*: `GIT_CONFIG_COUNT=1`, `GIT_CONFIG_KEY_0=http.<location>.extraHeader`, and
  `GIT_CONFIG_VALUE_0=Authorization: Basic <base64 of user:token>`. They are set only when both a user name and a token
  are present.

**Hardening options** put in front of every command with `-c`: `core.fsmonitor=false`, `core.hooksPath=<null device>`,
`protocol.ext.allow=never`, `http.followRedirects=false`, `core.quotepath=false`, `diff.renames=false`, and
`credential.helper=` (empty, so no helper is consulted).

#### Key Methods

**BuildStartInfo**: Builds the process start description for one git call.

- *Parameters*: `IReadOnlyList<string> arguments` — the git arguments; `RepositoryAccess? access` — the repository, or
  `null` for a call without credentials.
- *Returns*: `ProcessStartInfo` — with no shell, a closed standard input, redirected output and error, the arguments as
  an argument list, and the environment described above.
- *Preconditions*: `arguments` is not null.
- *Postconditions*: The token occurs in no argument and in no variable other than `GIT_CONFIG_VALUE_0`.

**RunAsync**: Runs git and returns the result for any exit code.

- *Parameters*: `IReadOnlyList<string> arguments`, `RepositoryAccess? access`, `long maxOutputBytes` — the largest
  standard output accepted, `CancellationToken cancellationToken`.
- *Returns*: `GitResult` — the exit code, the output bytes, and the error text, redacted with the credentials and the
  location of `access` (the location is replaced by the repository name).
- *Preconditions*: `arguments` is not null.
- *Postconditions*: The process has ended and its streams are drained.
- *Algorithm*: Check the git version once; start the process; read standard output up to the limit and keep the tail of
  standard error, both concurrently; wait for the exit under a time limit linked to the cancellation token.
  If the limit passes, kill the whole process tree and throw `GitTimeout`; if the output is larger than the limit,
  kill the process tree and throw `GitAccessFailed`; if the caller cancels, kill the process tree and rethrow the
  cancellation.

**RunCheckedAsync**: Runs git and requires success.

- *Parameters*: as for `RunAsync`, with a required `RepositoryAccess access`.
- *Returns*: `byte[]` — the standard output.
- *Postconditions*: A non-zero exit code has been turned into the exception built by `FailureFor`.

**FailureFor**: Describes a failed call as an exception that is safe to show.

- *Parameters*: `GitResult result`, `RepositoryAccess access`.
- *Returns*: `DomainRuleException` for `GitAccessFailed`.
- *Algorithm*: Redact the error text again (the secrets and the location, so a result that was not redacted by the
  runner is safe too). When the text contains a typical authentication failure (authentication failed, could not
  read the user name, terminal prompts disabled, an HTTP 401 or 403 answer, access denied, invalid credentials), the
  message says that access to the repository was denied and an administrator must check the user name and token.
  Otherwise the message says that the repository could not be read and adds the first line of the redacted error, cut
  to 300 characters. The message names the repository by its name and never contains its location.

**Redact**: Removes secrets, and the repository location, from text.

- *Parameters*: `string? text`, `string? username`, `string? token`, and optionally `string? location` and
  `string? name`.
- *Returns*: `string` — the text with each secret replaced by `***` and the location replaced by the repository name; the
  empty string for null text.
- *Algorithm*: Replace, in this order, the user information of any address (`://user:password@`, also in the form git
  prints with a redacted password), the location (see `ReplaceLocation`), the base64 form of `user:token`, the text
  `user:token`, the URI-escaped token, and the token itself. Nothing is replaced for a missing or empty token or user
  name, and no location is replaced when none is given.

**ReplaceLocation**: Replaces the registered location of a repository in text by the repository name, so that a message
never shows where a repository is kept.

- *Parameters*: `string text`, `string location`, `string name`.
- *Returns*: `string` — the text with every known form of the location replaced by the name.
- *Algorithm*: Build the forms git prints for the location: as registered, without user information, with masked user
  information, with a trailing slash or `.git` added or removed, with forward and back slashes for a local path, the
  local path of a `file://` URL, and for a web address the host name and the host name with the port (git names the host
  when it cannot connect). Match them in one pass, longest first, ignoring case, together with one trailing slash. A
  location that git prints in a form that is not listed here (for example a relative or shortened path) is not found.

**CheckVersionAsync** (private): Runs `git --version` once and requires a minimum version; the numbered configuration
variables that carry the token need it.

#### Error Handling

All failures are `DomainRuleException` values, except a cancellation requested by the caller, which propagates as
`OperationCanceledException`.

- A git that cannot be started (missing, not executable) or is older than the minimum is `GitUnavailable` with a
  message that asks an administrator to install a current git, and no inner exception, so a path or a system message is
  not exposed.
- A home folder (`<cache>/.git-home`) that cannot be created, because of an I/O or permission failure, is
  `GitUnavailable` with a message that asks an administrator to check the `Git:CachePath` setting and the folder
  permissions; it names the setting, not the path, and has no inner exception.
- A command that exceeds the time limit is `GitTimeout` ("Git did not finish within N seconds and was stopped.").
- Output beyond the limit is `GitAccessFailed` ("Git returned more data than the server accepts.").
- A non-zero exit code is turned into `GitAccessFailed` by `FailureFor`.

A null options object or argument list raises `ArgumentNullException`.

**Known limitation**: git failures are not written to the server log. The redacted first line of the error is shown to
the user in the exception. The registered location (including any user information, with or without a trailing slash or
`.git`, with either kind of slash for a local path, and the host name of a web address) is replaced in that text by the
repository name, so an ordinary user sees the name and never the location; a form of the location that git prints
differently from every form listed under `ReplaceLocation` could still appear. A logger can be added later without
changing the contract.

#### Dependencies

- **RepositoryAccess**, **DomainRuleException**, **DomainRule** — Core types.
- **git** — the child process.
- **.NET** — process, stream, and cancellation support.

#### Callers

- **GitSource** — runs every git command of a mirror and a session.
- **ServerHost** — registers the runner and the options as singletons.
