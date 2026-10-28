### TemporaryDirectory

![Utilities Structure](UtilitiesView.svg)

#### Purpose

`TemporaryDirectory` is a disposable helper that creates a uniquely named directory under the system temporary
path and deletes it, with everything in it, when disposed. Its single responsibility is to make sure that the
files of a unit of work never outlive it and never collide with another run. The self-validation uses one instance
per check.

#### Data Model

- `DirectoryPath` (`string`) — the full path of the directory, `Path.GetTempPath()` plus a new GUID in `N` format.

The class is `internal sealed`, implements `IDisposable`, and is not thread-safe; use one instance per unit of
work.

#### Key Methods

**Constructor**: Builds the path with `PathHelpers.SafePathCombine` and creates the directory. `IOException`,
`UnauthorizedAccessException` and `ArgumentException` are wrapped in `InvalidOperationException`.

**GetFilePath**: Returns the full path of a file inside the directory and creates the missing folders above it.

- *Parameters*: `string relativePath`.
- *Returns*: the combined path, always inside `DirectoryPath`.
- *Preconditions*: `relativePath` is not null.

The escape check is `PathHelpers.SafePathCombine`, so a caller-supplied name can never reach outside the directory
that is deleted on disposal.

**Dispose**: Deletes the directory tree. The read-only flag of every file is cleared first, because git writes its
object files read-only and Windows refuses to delete such files. `IOException` and `UnauthorizedAccessException`
are swallowed, because a cleanup failure must not turn finished work into a failure; calling it more than once is
safe.

#### Error Handling

`GetFilePath` throws `ArgumentNullException` for a null path and `ArgumentException` for a path that escapes the
directory. The constructor throws `InvalidOperationException` when the directory cannot be created. `Dispose`
never throws for I/O problems.

#### Dependencies

- **PathHelpers** — `SafePathCombine` builds the directory path and every file path.

#### Callers

- **ValidationChecks** — every check creates one instance for its files.
