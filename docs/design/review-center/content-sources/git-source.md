### GitSource

![ContentSources Structure](ContentSourcesView.svg)

#### Purpose

`GitSource` answers the questions of the Core git contract for a registered repository. Its single responsibility is to
keep a local bare mirror of the repository current and to resolve revisions, list changes, and read blobs from that
mirror, one session at a time per repository, without ever writing or executing a file of the repository.

#### Data Model

`GitSource` is a singleton and is safe for concurrent use. It implements `IGitRepositoryProvider`; the session it
returns, a private class `Session`, implements `IGitRepository`.

**State**: a dictionary of semaphores, one for each repository identity, created on first use and kept for the life of
the process. The semaphore has one slot and is the lock of that repository.

**Layout of the cache**: `<CachePath>/repo-<id>` is the bare mirror of the repository with that identity. A mirror in the
process of being cloned is in `<CachePath>/repo-<id>.tmp` and is moved into place when complete.

**Limits**: a change listing is read up to 64 MiB; a blob up to the limit given by the caller; a revision or object id as
described below.

**Types of the contract used here**: `RepositoryAccess` (the input), `GitChange`, `ChangeKind`, `SkippedEntry`, and
`GitChangeSet` (the result), all defined in `Core`.

#### Key Methods

**OpenAsync**: Makes the mirror of a repository current and opens a session on it.

- *Parameters*: `RepositoryAccess access`, `CancellationToken cancellationToken`.
- *Returns*: `IGitRepository` — the session; the caller disposes it.
- *Preconditions*: `access` is not null and `GitOptions.CachePath` is set (otherwise `InvalidOperationException`).
- *Postconditions*: The mirror is current and the repository lock is held by the returned session. If opening fails, the
  lock is released before the exception propagates.
- *Algorithm*: Wait for the lock of the repository. If the mirror folder exists, read its stored remote address with
  `git config --get remote.origin.url` (no credentials); when it equals the registered location exactly, run
  `git fetch --prune --force origin` with the credentials. When the folder does not exist or its address differs,
  delete the folder (first clearing read-only attributes, which git sets on some files on Windows), run
  `git clone --mirror -- <location> <folder>.tmp` with the credentials, and move the folder into place. A failed clone
  deletes the temporary folder.

**ResolveRevisionAsync** (session): Resolves a revision name to the commit it names.

- *Parameters*: `string revision`, `CancellationToken cancellationToken`.
- *Returns*: `string` — the commit id as lowercase hexadecimal (40 or 64 characters).
- *Preconditions*: `revision` passes `RevisionValidator.Validate`, otherwise `RevisionInvalid`.
- *Postconditions*: The result is an id git reported as an existing commit.
- *Algorithm*: Run `git rev-parse --verify --end-of-options <revision>^{commit}` against the mirror, without
  credentials. A failure, or an answer that is not a lowercase hexadecimal id, is `RevisionNotFound` ("The revision 'x'
  does not exist in repository 'X'.").

**ListChangesAsync** (session): Lists the files that differ between two commits, or all files of one commit.

- *Parameters*: `string? baselineSha`, `string headSha`, `CancellationToken cancellationToken`.
- *Returns*: `GitChangeSet` — the changes and the skipped entries, both ordered by path (ordinal).
- *Preconditions*: Both ids are valid object ids, otherwise `ArgumentException`.
- *Algorithm*: Without a baseline, run `git ls-tree -r -z --full-tree <head>`; every regular file is Added. With a
  baseline, run `git diff-tree -r -z --raw --no-renames --no-abbrev <baseline> <head> --`; the status letter gives Added,
  Deleted, or (for any other letter) Modified. Entries with the mode of a symbolic link or a submodule, paths longer than
  `ReviewLimits.MaxPathLength`, and paths with control or replacement characters are not changes; they become skipped
  entries with a reason ("symbolic links are not reviewed", "submodules are not reviewed", and so on). A change whose
  two blobs are the same (only the mode changed) is dropped.

**IsAncestorAsync** (session): Tells whether one commit is an ancestor of another.

- *Parameters*: `string ancestorSha`, `string descendantSha`, `CancellationToken cancellationToken`.
- *Returns*: `bool` — `true` when the first commit is the second or lies in its history.
- *Preconditions*: Both ids are valid object ids, otherwise `ArgumentException`.
- *Algorithm*: Run `git merge-base --is-ancestor <ancestor> <descendant>` against the mirror, without credentials, and
  read the exit code: 0 is yes, 1 is no, and any other code (an unknown commit, a damaged mirror) is a failure
  reported like every other git failure (`GitAccessFailed`, without paths). The planner uses it to find out whether
  the head of the last signed review can be the baseline of a new review.

**GetBlobSizeAsync** (session): Reads the size of a blob.

- *Parameters*: `string blobSha`, `CancellationToken cancellationToken`.
- *Returns*: `long` — the size in bytes, from `git cat-file -s`.
- *Preconditions*: `blobSha` is 40 or 64 lowercase hexadecimal characters, otherwise `ArgumentException`.

**ReadBlobAsync** (session): Reads the exact bytes of a blob.

- *Parameters*: `string blobSha`, `long maxBytes`, `CancellationToken cancellationToken`.
- *Returns*: `byte[]` — the bytes as stored.
- *Preconditions*: As for `GetBlobSizeAsync`.
- *Postconditions*: No filter, line-ending conversion, or large-file expansion was applied.
- *Algorithm*: Read the size first; a blob larger than `maxBytes` is `GitAccessFailed`. Then run
  `git cat-file blob <sha>` with the output limit set to `maxBytes`.

**DisposeAsync** (session): Releases the repository lock exactly once, however often it is called.

#### Error Handling

All failures are `DomainRuleException` values (`GitUnavailable`, `GitAccessFailed`, `GitTimeout`, `RevisionInvalid`,
`RevisionNotFound`), most of them produced by `GitRunner`. A malformed object id is a programming error and raises
`ArgumentException`; a missing cache path raises `InvalidOperationException`; a null argument raises
`ArgumentNullException`. A caller's cancellation propagates as `OperationCanceledException`. An I/O or permission
failure on the cache folder (creating, replacing, or moving a mirror folder), or a cache path that is malformed, not
supported, or too long (`ArgumentException`, `NotSupportedException`, `PathTooLongException`, `SecurityException`), is
`GitUnavailable` with an administrator-level message that names the `Git:CachePath` setting, not the path, and has no
inner exception; the repository lock is released first. The credentials are used only by the clone and the fetch; the
session works on a copy of the access data without the user name and token, so a failure in a session can never carry
them.

#### Dependencies

- **GitRunner** and **GitOptions** — run git and supply the cache path.
- **RevisionValidator**, **ReviewLimits**, **RepositoryAccess**, and the git contract types — Core.
- **.NET** — file system and concurrency primitives.

#### Callers

- **ServerHost** — registers it as the Core git provider.
- **Subsystem tests and future review creation** — obtain the access data from `RepositoryService` and open a session.
