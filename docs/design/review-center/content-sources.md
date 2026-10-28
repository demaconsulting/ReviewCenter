## ContentSources

![ContentSources Structure](ContentSourcesView.svg)

### Overview

The `ContentSources` subsystem reads content from outside the system. At present it reads git repositories, so that
a review can be created from the files of a repository revision (the `ReviewService` of `Core` does that), and it
protects the access tokens that private
repositories need. It implements two contracts that the `Core` subsystem defines and holds no domain rule of its own: the
repositories of each project, their names, and who may change them belong to `Core` and `Storage`. It contains four units:
`GitRunner`, which runs the git command-line tool safely and reports its result; `GitSource`, which keeps a local
mirror of each registered repository and answers the questions of the Core git contract; `ReviewSetParser` (the class
`ReviewMarkConfigParser`), which reads the review-sets out of the ReviewMark configuration file of a repository; and
`CredentialProtector`,
which encrypts and decrypts tokens with the ASP.NET Core Data Protection system. The units are internal to the
application, except the options type `GitOptions`, which is documented with `GitRunner`; the rest of the system sees
only the Core interfaces.

### Interfaces

**GitSource (as IGitRepositoryProvider and IGitRepository)**: The read access to a registered repository.

- *Type*: In-process .NET class implementing the Core interfaces.
- *Role*: Provider.
- *Contract*: Opens a repository from the access data that `RepositoryService` supplies (location, user name, and the
  decrypted token), which makes the local mirror current, and returns a session. The session resolves a revision name to
  a commit id, lists the files that changed between two commits (or every file of one commit), reads the size of a blob,
  and reads the exact bytes of a blob up to a limit, and tells whether one commit is an ancestor of another. Disposing
  the session releases the repository.
- *Constraints*: At most one session per repository is open at a time, so a second `OpenAsync` for the same repository
  waits until the first session is disposed; sessions on different repositories run in parallel. Failures are reported as
  a `DomainRuleException` for `GitUnavailable`, `GitAccessFailed`, `GitTimeout`, `RevisionInvalid`, or `RevisionNotFound`.
  The token is never part of a session or of any message.

**ReviewMarkConfigParser (as IReviewSetParser)**: The reading of the review-sets of a repository.

- *Type*: In-process .NET class implementing the Core interface.
- *Role*: Provider.
- *Contract*: Takes the bytes of the `.reviewmark.yaml` file of a repository and returns its review-sets, each with an
  identifier, a title, and the ordered include and exclude patterns, in file order. Every other key is ignored.
- *Constraints*: A file that is too large, has aliases, too many sets, names or paths that are too long, a set without
  an identifier or a title, a repeated identifier, or is not valid YAML is reported as a `DomainRuleException` for
  `ReviewSetConfigInvalid` with a plain message.

**CredentialProtector (as ICredentialProtector)**: The protection of tokens at rest.

- *Type*: In-process .NET class implementing the Core interface.
- *Role*: Provider.
- *Contract*: Turns a token into protected text that is safe to store in the database, and turns that text back into
  the token.
- *Constraints*: Text that cannot be decrypted, which happens after the key ring was lost or replaced, is reported as a
  `DomainRuleException` for `CredentialsUnreadable`.

**Git settings**: The `Git` configuration section, bound to `GitOptions`.

- *Type*: Configuration (`appsettings.json` and environment variables).
- *Role*: Consumer.
- *Contract*: `CachePath` is the folder of the mirrors (empty for a `git-cache` folder beside the database) and
  `TimeoutSeconds` is the longest time a single git command may run (120 by default; it must be from 1 to 3600, or
  the server refuses to start with "Invalid git settings: Git:TimeoutSeconds must be between 1 and 3600 seconds.").
- *Constraints*: A `TimeoutSeconds` outside 1 to 3600 is refused at start-up by `Hosting`. A value below 1 can only
  reach `GitOptions` when the class is built directly, and then counts as the default (120 seconds).

**git (consumed)**: The installed git command-line tool.

- *Type*: Child process.
- *Role*: Consumer.
- *Contract*: Run with fixed arguments, a curated environment, and no terminal. A minimum git version is required,
  because the token reaches git through numbered configuration variables of the environment.
- *Constraints*: A missing or too old git is reported as `GitUnavailable` with an instruction to install a current git.

**ASP.NET Core Data Protection (consumed)**: The `IDataProtectionProvider` registered by `Hosting`.

- *Type*: In-process .NET interface.
- *Role*: Consumer.
- *Contract*: Creates a protector for a fixed purpose and protects or unprotects text with the key ring.
- *Constraints*: The key ring is a folder that the administrator must keep (see Design).

### Design

**Data flow**: An administrator of a project (a project administrator, or a system administrator) registers a repository
with that project on `RepositoriesPage`; `RepositoryService` checks that the user may administer the project, validates
the input,
has `CredentialProtector` encrypt the token, and hands the registration to `RepositoryStore`, which stores it with its
audit entry. When a repository is read, `RepositoryService.GetAccessAsync` loads the registration, checks that it is
enabled, decrypts the token, and returns the access data. `GitSource.OpenAsync` takes the per-repository lock, makes the
mirror current through `GitRunner` (a clone the first time, a fetch afterwards), and returns a session. The session
answers its questions from the local mirror only, using `GitRunner` again, and releases the lock when it is disposed.
Nothing in `ContentSources` reads the database or decides who may use a repository: the registration of a project
reaches it only through the access data, and the project check is made before.

**Running git without trusting its surroundings**: Repository content and server answers are untrusted input, and git
is a large program with many ways to run other programs. `GitRunner` therefore never uses a shell: the arguments are
passed as a list, not as a command line, so no value can be taken for a second command; every revision and every
object id is validated before it is passed (see `RevisionValidator` and the object id check of `GitSource`), and the
`--end-of-options` marker keeps a value from being read as an option. The environment of the child process is built from
nothing: only a short list of variables that git needs to run and reach the network (the path, the system and temporary
folders, certificate locations, and proxy settings) is copied, and git is told not to read the system or global
configuration, not to ask for a password on a terminal, and not to run a credential helper or an askpass program. Hooks,
the file system monitor, external protocols, and redirects are switched off by configuration, and only the file,
`http`, and `https` transports are allowed. The home folder of the child is a private folder under the cache, so a
user configuration of the server account is never read.

**The token and where it goes**: The token is the only secret of the subsystem, and it is placed where it is least
likely to leak. It is never written to the command line, which any user of the machine can read, to a file, or to the
configuration of a mirror. It is sent only as the value of an `Authorization` header that is given to git through
the environment variables `GIT_CONFIG_COUNT`, `GIT_CONFIG_KEY_0`, and `GIT_CONFIG_VALUE_0` of the child process, scoped
to the registered location, and only for the commands that talk to the remote (the clone and the fetch). Reads from the
local mirror run without any credential: the session holds a copy of the access data with the user name and the token
removed. The text that git writes to its error output, and every message built from it, is redacted before it is kept:
the token, the user name and token pair, their encoded forms, and any user information in an address are replaced by
`***`. The registered location of the repository is replaced in the same text by the repository name, in every form git
prints it (see `GitRunner`), so a message shows which repository failed but never where it is kept. The messages shown
to a user say what happened (for example that access was denied and an administrator must
check the user name and token) and never contain the output of git verbatim beyond its first redacted line.

**Limits and time**: Every git command runs under a time limit (`Git:TimeoutSeconds`). When the limit is reached, the
whole process tree is killed, not only the git process, because git starts helper processes for the network, and the
failure is reported as `GitTimeout`. The standard output of a command is read with a byte limit, and a command that
produces more is stopped and reported, so a large repository cannot exhaust the memory of the server; the error output
keeps only its last few kilobytes. A blob is read only after its size was read and found within the limit the caller
gave. The change list is capped in the same way.

**The mirror cache**: Each registered repository has one bare mirror folder under the cache path, named by the identity
of the registration, `repo-{id}`, so a name chosen by a project administrator never becomes a path. A mirror is made
with a clone into a temporary folder that is moved into place only when it is complete, so a failed clone leaves nothing
behind, and it is kept current with a fetch that prunes deleted branches and forces updated ones. When the location of
the registration no longer equals the one stored in the mirror, the mirror is deleted and cloned again, so a changed
location can never serve the content of the old one. A semaphore for each repository serializes the sessions on it: the
mirror cannot change under a session that is reading, and two administrators or two requests cannot fetch into the same
folder at once. The semaphore is released when the session is disposed, and also when opening fails.

**What a revision listing contains**: A change list is built from the object database, not from a working copy, so
there is no checkout and no file of the repository is ever written to the disk of the server or executed. Files are
read as the bytes that git stores: no line-ending conversion, no filters, and no large-file expansion, so a blob that is
read is the blob that was committed. Symbolic links and submodules are not files of the repository content; they are
left out of the list and reported as skipped entries with a reason, as are paths that are too long or contain control or
replacement characters. A rename is shown as a deletion and an addition. Entries whose only change is the file mode are
dropped.

**Token encryption and the key ring**: `CredentialProtector` uses a fixed purpose string that includes a version, so a
value protected for another purpose cannot be unprotected as a token. The keys of the Data Protection system are kept in
a
folder (`DataProtection:KeysPath`, by default a `keys` folder beside the database), which `Hosting` creates and checks
when the server starts. Three facts about that folder matter for operations. First, **losing or replacing the key ring
makes every stored token unreadable**: the repositories stay registered, and an administrator of each project must
enter each token
again; reading such a repository fails with an error that says so. Second, **the keys are not encrypted at rest**: anyone
who can read the folder and the database can decrypt the tokens, so the folder must be protected with file permissions
(the server restricts it to its owner on systems that support it), and it must be backed up together with the database.
Third, using a persistent key ring changes the keys that protect the sign-in cookies once, so users sign in again after
the first start with it.

**Reading the review-sets**: The configuration file is the one that ReviewMark reads, so a repository defines its
review-sets once for both tools. `ReviewMarkConfigParser` uses YamlDotNet, the library that ReviewMark uses, so quoting,
flow style, and block scalars mean the same here. Only the top-level `reviews` list is read, and of each entry only `id`,
`title`, and `paths`. The file comes from other people, so its size is checked before it is read, the events of the YAML
are scanned first to refuse an alias (an alias is expanded at every use, so a small file could otherwise describe an
enormous document), and the counts and lengths are checked after it is read. A problem of the library never reaches the
user: it becomes a plain message that names the line and column or the position of the entry. The blob is read through
`GitSource`, and the choice of the file and of the commit belongs to the planner in `Core`.

**Known limitations**: The subsystem needs a usable git on the server and reads only `https`, `http`, and `file`
locations; SSH is not supported, because no safe way is available to give it a key. The token is sent as a basic
authorization header to the registered location only, and a server that redirects to another host is not followed. The
mirror is a full copy of the repository, so its size is the size of the repository. The tests that use a real git skip
themselves when git is not installed.
