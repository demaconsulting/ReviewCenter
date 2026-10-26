### GitSource

#### Verification Approach

`GitSource` is verified with tests defined in `GitSourceTests.cs`, against real git repositories that the tests create in
private temporary folders with the installed git (through the helper `GitTestSupport`). An origin repository with two
commits exercises every kind of change: a modification, an addition, a deletion, a rename, a symbolic link, a
submodule entry, a mode-only change, and a path with spaces and a non-ASCII character. The source clones and fetches
from that origin through the real `GitRunner` into a cache folder of the test, so the mirror, the revisions, the change
lists, and the blobs are verified as git reports them. Serialization is verified by opening sessions on the same and on
different repositories and observing whether the second open waits. The absence of the token from the mirror is verified
by reading the configuration file of the mirror after an open with a token. Tests skip themselves when git is not
installed.

#### Test Environment

N/A - standard test environment (the tests need git, and skip themselves when it is not installed).

#### Acceptance Criteria

- All tests pass with zero failures (or skip because git is not installed).
- A branch, a tag, and a full commit id resolve to commit ids; an unknown revision is `RevisionNotFound`, and an unsafe
  one is `RevisionInvalid`.
- The change list between two commits shows modifications, additions, and deletions in path order, reports a rename as a
  deletion and an addition, skips symbolic links and submodules with a reason, and ignores a mode-only change; without
  a baseline every file of the commit is listed as added.
- A blob is read as the exact bytes that were committed; a blob over the limit is an error and not a truncation; an
  identifier that is not an object id is refused before git runs.
- The ancestor check says yes for a commit in the history of another (and for the commit itself), no for a later or a
  diverged commit, and fails with a path-free error for a commit that does not exist.
- Opening again fetches new commits and prunes deleted branches; a changed location makes a fresh mirror.
- A second session on the same repository waits for the first to be disposed; sessions on different repositories are
  open at the same time.
- A location that does not exist gives a clear access error and leaves no half-made mirror.
- A cache folder that cannot be written, or whose path is malformed or too long, is `GitUnavailable` with a message that
  names the setting and not the path, and the repository lock is released.
- The mirror configuration never holds the token.

#### Test Scenarios

**GitSource_ResolveRevision_BranchTagAndId_ResolveAndBadOnesAreRefused**: A branch, a tag, and a full commit id resolve
to
the commit ids; an unknown name is refused with `RevisionNotFound` and an option-like or expression-like name with
`RevisionInvalid`. This scenario is tested by `GitSource_ResolveRevision_BranchTagAndId_ResolveAndBadOnesAreRefused`.

**GitSource_ListChanges_BetweenCommits_ListsChangesAndSkipsSpecialEntries**: The change list between the two commits has
the modification, the addition, and the deletion with their blob ids, shows the rename as a deletion and an addition,
lists the symbolic link and the submodule as skipped entries with their reasons, and leaves out the mode-only change.
This scenario is tested by `GitSource_ListChanges_BetweenCommits_ListsChangesAndSkipsSpecialEntries`.

**GitSource_ListChanges_NoBaseline_ListsEveryFileAsAdded**: Without a baseline every file of the head commit is listed as
added. This scenario is tested by `GitSource_ListChanges_NoBaseline_ListsEveryFileAsAdded`.

**GitSource_ReadBlob_ReadsExactBytesAndEnforcesTheLimit**: The size and the exact bytes of a blob are read, and a blob
over the limit is `GitAccessFailed`, not a truncated result. This scenario is tested by
`GitSource_ReadBlob_ReadsExactBytesAndEnforcesTheLimit`.

**GitSource_IsAncestor_History_AnswersYesAndNo**: In a repository with a side branch, a commit is an ancestor of a
later commit and of itself, a later commit is not an ancestor of an earlier one, and a commit on another branch is not
an ancestor of the main branch. This scenario is tested by `GitSource_IsAncestor_History_AnswersYesAndNo`.

**GitSource_IsAncestor_UnknownCommit_ThrowsGitAccessFailedWithoutPaths**: A well-formed commit id that does not exist in
the repository gives `GitAccessFailed`, and the message shows neither the location nor the folder of the mirror. This
scenario is tested by `GitSource_IsAncestor_UnknownCommit_ThrowsGitAccessFailedWithoutPaths`.

**GitSource_BlobAndCommitIds_NotObjectIds_ThrowArgumentException**: Blob and commit identifiers, including those given
to `IsAncestorAsync`, that are not object ids
(an option, a revision name, a wrong length, or uppercase) are refused with an `ArgumentException` before git runs. This
scenario is tested by `GitSource_BlobAndCommitIds_NotObjectIds_ThrowArgumentException`.

**GitSource_OpenAgain_FetchesNewCommitsAndPrunesDeletedBranches**: After a commit is added and a branch is deleted in the
origin, opening again makes the new commit resolvable and the deleted branch unknown. This scenario is tested by
`GitSource_OpenAgain_FetchesNewCommitsAndPrunesDeletedBranches`.

**GitSource_LocationChanged_ClonesAgain**: The same repository identity opened with another location gives the content of
the new location and not of the old. This scenario is tested by `GitSource_LocationChanged_ClonesAgain`.

**GitSource_SecondSessionOnSameRepository_WaitsForTheFirst**: While a session is open, a second open of the same
repository does not complete; it completes when the first session is disposed. This scenario is tested by
`GitSource_SecondSessionOnSameRepository_WaitsForTheFirst`.

**GitSource_DifferentRepositories_OpenTogether**: Two different repositories are open at the same time. This scenario is
tested by `GitSource_DifferentRepositories_OpenTogether`.

**GitSource_Open_LocationMissing_ThrowsGitAccessFailedAndCleansUp**: A location that does not exist gives
`GitAccessFailed`, no temporary or half-made mirror folder remains, and the repository lock is released. This scenario is
tested by `GitSource_Open_LocationMissing_ThrowsGitAccessFailedAndCleansUp`.

**GitSource_Open_CacheFolderNotWritable_ThrowsGitUnavailable**: A cache path below a regular file cannot be created; the
open fails with `GitUnavailable`, the message names `Git:CachePath` but not the path, no inner exception is chained,
and a second open fails the same way instead of waiting, which shows the repository lock was released. The same mapping
for the git home folder is tested by `GitRunner_RunAsync_HomeFolderNotWritable_ThrowsGitUnavailable`. This scenario is
tested by `GitSource_Open_CacheFolderNotWritable_ThrowsGitUnavailable`.

**GitSource_Open_MalformedCachePath_ThrowsGitUnavailable**: A cache path with a null character, or with a name that is
too long for any file system, makes the open fail with `GitUnavailable` instead of an unhandled exception; the message
names `Git:CachePath` but not the path, no inner exception is chained, and a second open fails the same way, which shows
the repository lock was released. This scenario is tested by `GitSource_Open_MalformedCachePath_ThrowsGitUnavailable`.

**GitSource_Mirror_NeverStoresTheToken**: After opening with an access that carries a token, the configuration of the
mirror does not contain it. This scenario is tested by `GitSource_Mirror_NeverStoresTheToken`.
