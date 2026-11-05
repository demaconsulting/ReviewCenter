## Utilities

### Verification Approach

The `Utilities` subsystem is verified by integration tests defined in
`UtilitiesSubsystemTests.cs`. Each test exercises `PathHelpers`, `TemporaryDirectory`, or `SecretSource` through
realistic workflows to confirm that valid paths are resolved correctly, traversal attacks
are rejected, the resulting paths can be used for actual directory creation, a temporary
directory with files in it is removed when it is disposed, and a secret in a file in a temporary directory is read
trimmed. `PathHelpers`
has no dependencies on other tool units, so no mocking is required.

### Test Environment

N/A - standard test environment.

### Acceptance Criteria

- All integration tests pass with zero failures.
- Valid relative paths are combined with the base directory into full paths that lie inside it, and the paths can be
  passed directly to `Directory.CreateDirectory` (`SafePaths`).
- Path traversal patterns (e.g., `../`) cause `ArgumentException` to be thrown (`SafePaths-PathTraversal`).
- Absolute paths supplied as the relative argument cause `ArgumentException` to be thrown
  (`SafePaths-AbsolutePath`).
- Two `TemporaryDirectory` instances that exist at the same time have different paths (`TempFiles`).
- The paths that `GetFilePath` hands out, including those in nested folders, lie inside the directory
  (`TempFiles-InsideOnly`), the folders above a nested file exist so that it can be written at once
  (`TempFiles-Parents`), names that would leave the directory are rejected with `ArgumentException`
  (`TempFiles-Escape`), and a null name is rejected with `ArgumentNullException` that names the argument
  (`TempFiles-NullPath`).
- Files created through a `TemporaryDirectory`, including in nested folders, are gone after it is disposed while none
  of them is held open (`TempFiles-Removed`), and disposing a second time does not throw (`TempFiles-Removed-Repeated`).
- On Windows, a file that is released while the removal retries lets the whole directory be removed
  (`TempFiles-Removed-Retry`); a file that stays open makes the disposal return without an exception and leave the
  directory (`TempFiles-LockedFile`), in less than three seconds (`TempFiles-LockedFile-Bounded`), and a policy of one
  attempt does not pause (`TempFiles-LockedFile-NoFinalPause`). These tests skip on other platforms, where an open file
  does not block a delete.
- A retry policy with no attempt or a negative pause is rejected (`TempFiles-InvalidPolicy`).
- A secret file written to a `TemporaryDirectory` is resolved by `SecretSource` as its trimmed contents
  (`SecretSource`).
- An inline secret is returned as it is given (`SecretSource-Inline`), and no secret is returned, without an error,
  when neither setting is set (`SecretSource-Neither`).
- A secret file that is missing, is a directory, is empty, is larger than 8192 bytes, or is not UTF-8 is refused with a
  message that names the file setting (`SecretSource-Refusal`).
- A secret that is set inline and in a file is refused with a message that names both settings
  (`SecretSource-Conflict`).
- None of these refusals puts the secret or the contents of a file in its message (`SecretSource-NoDisclosure`).

### Test Scenarios

**UtilitiesSubsystem_TemporaryDirectoryWorkflow_TwoDirectories_UseDifferentPaths**: Two `TemporaryDirectory`
instances exist at the same time and each is asked for the same relative file name; the directories and the files in
them have different paths. This scenario is tested by
`UtilitiesSubsystem_TemporaryDirectoryWorkflow_TwoDirectories_UseDifferentPaths`.

**UtilitiesSubsystem_TemporaryDirectoryWorkflow_FilePaths_AreInsideDirectory**: `TemporaryDirectory.GetFilePath` is
asked for three names, two of them in nested folders; each path is the expected file in the directory and starts with
the directory path and a separator. This scenario is tested by
`UtilitiesSubsystem_TemporaryDirectoryWorkflow_FilePaths_AreInsideDirectory`.

**UtilitiesSubsystem_TemporaryDirectoryWorkflow_NestedFilePath_CanBeWrittenAtOnce**: The path of a file three folders
down is requested and the file is written at once, without creating a folder; the folder exists and the file holds the
data. This scenario is tested by `UtilitiesSubsystem_TemporaryDirectoryWorkflow_NestedFilePath_CanBeWrittenAtOnce`.

**UtilitiesSubsystem_TemporaryDirectoryWorkflow_EscapingFileNames_AreRejected**: `GetFilePath` is asked for
`../escape.txt`, `../../escape.txt`, and `sub/../../escape.txt`; each call throws `ArgumentException`. This scenario is
tested by `UtilitiesSubsystem_TemporaryDirectoryWorkflow_EscapingFileNames_AreRejected`.

**UtilitiesSubsystem_TemporaryDirectoryWorkflow_NullFileName_IsRejected**: `GetFilePath` is asked for a null name; it
throws `ArgumentNullException` whose parameter name is `relativePath`. This scenario is tested by
`UtilitiesSubsystem_TemporaryDirectoryWorkflow_NullFileName_IsRejected`.

**UtilitiesSubsystem_TemporaryDirectoryWorkflow_FilesCreated_AreRemovedOnDispose**: Files are created in nested
folders through `TemporaryDirectory.GetFilePath` and written, and after the instance is disposed neither the files
nor the directory exist. This scenario is tested by
`UtilitiesSubsystem_TemporaryDirectoryWorkflow_FilesCreated_AreRemovedOnDispose`.

**UtilitiesSubsystem_TemporaryDirectoryWorkflow_DisposedTwice_DoesNotThrow**: A directory with a file is disposed and
then disposed again; the second call does not throw and the directory is gone. This scenario is tested by
`UtilitiesSubsystem_TemporaryDirectoryWorkflow_DisposedTwice_DoesNotThrow`.

**UtilitiesSubsystem_TemporaryDirectoryWorkflow_FileReleasedDuringRetry_DirectoryIsRemoved**: On Windows, a file in a
nested folder is held open for about 100 ms while the directory, with 40 attempts 50 ms apart, is disposed; the
disposal does not throw and the whole directory is gone. This scenario is tested by
`UtilitiesSubsystem_TemporaryDirectoryWorkflow_FileReleasedDuringRetry_DirectoryIsRemoved`.

**UtilitiesSubsystem_TemporaryDirectoryWorkflow_FileHeldOpen_DisposeLeavesDirectoryWithoutThrowing**: On Windows, a
file stays open while a directory with the default policy is disposed; the disposal does not throw and the directory
is still there, and after the file is released a second disposal removes it. This scenario is tested by
`UtilitiesSubsystem_TemporaryDirectoryWorkflow_FileHeldOpen_DisposeLeavesDirectoryWithoutThrowing`.

**UtilitiesSubsystem_TemporaryDirectoryWorkflow_FileHeldOpen_DisposeReturnsInBoundedTime**: On Windows, a file stays
open while a directory with the default policy is disposed; the disposal returns in less than three seconds. This
scenario is tested by `UtilitiesSubsystem_TemporaryDirectoryWorkflow_FileHeldOpen_DisposeReturnsInBoundedTime`.

**UtilitiesSubsystem_TemporaryDirectoryWorkflow_SingleAttemptPolicy_DisposeDoesNotPause**: On Windows, a file stays
open while a directory with one attempt and a 30 second pause is disposed; the disposal returns in less than five
seconds, so the pause is never taken. This scenario is tested by
`UtilitiesSubsystem_TemporaryDirectoryWorkflow_SingleAttemptPolicy_DisposeDoesNotPause`.

**UtilitiesSubsystem_TemporaryDirectoryWorkflow_InvalidRetryPolicy_IsRejected**: Directories are created with 0
attempts, with -1 attempts, and with a pause of -1 ms; each creation throws `ArgumentOutOfRangeException`. This
scenario is tested by `UtilitiesSubsystem_TemporaryDirectoryWorkflow_InvalidRetryPolicy_IsRejected`.

**UtilitiesSubsystem_PathResolutionWorkflow_ValidPaths_ResolvesCorrectly**: Multiple valid
relative path arguments are combined with a base path using `PathHelpers.SafePathCombine`; all
results are correctly combined paths that remain within the base directory and no exception is
thrown. This scenario is tested by
`UtilitiesSubsystem_PathResolutionWorkflow_ValidPaths_ResolvesCorrectly`.

**UtilitiesSubsystem_PathTraversalValidation_DangerousPaths_ThrowsException**: Multiple path
traversal patterns (e.g., `../`, `subfolder/../../../`) are passed to `SafePathCombine`; an
`ArgumentException` is thrown for each traversal pattern and no traversal succeeds. This
scenario is tested by
`UtilitiesSubsystem_PathTraversalValidation_DangerousPaths_ThrowsException`.

**UtilitiesSubsystem_AbsolutePathRejection_ThrowsException**: Absolute paths (e.g.,
`/etc/passwd` and on Windows `C:\Windows\System32\file.txt`) are passed as the relative path
argument to `SafePathCombine`; an `ArgumentException` is thrown for each and no injection
succeeds. This scenario is tested by
`UtilitiesSubsystem_AbsolutePathRejection_ThrowsException`.

**UtilitiesSubsystem_PathResolutionWorkflow_NullArguments_ThrowsArgumentNullException**: `SafePathCombine` is called
once with a null relative path and once with a null base path; each call throws `ArgumentNullException` and the
exception names the missing argument. This scenario is tested by
`UtilitiesSubsystem_PathResolutionWorkflow_NullArguments_ThrowsArgumentNullException`.

**UtilitiesSubsystem_DirectoryCreationWorkflow_ValidPaths_CreatesDirectories**:
`PathHelpers.SafePathCombine` is used to compute a nested path, and the resulting path is
passed to `Directory.CreateDirectory`; the directory is created at the expected location within
the base directory. This scenario is tested by
`UtilitiesSubsystem_DirectoryCreationWorkflow_ValidPaths_CreatesDirectories`.

**UtilitiesSubsystem_SecretSourceWorkflow_FileInTemporaryDirectory_ResolvesTrimmedSecret**: A secret file with a
trailing line break is written inside a `TemporaryDirectory`; `SecretSource.Resolve` returns the trimmed secret. This
scenario is tested by `UtilitiesSubsystem_SecretSourceWorkflow_FileInTemporaryDirectory_ResolvesTrimmedSecret`.

**UtilitiesSubsystem_SecretSourceWorkflow_FileRemoved_IsRefusedNamingSetting**: The `TemporaryDirectory` that held a
secret file is disposed; the same `SecretSource.Resolve` call is refused with a message that names the file setting.
This scenario is tested by `UtilitiesSubsystem_SecretSourceWorkflow_FileRemoved_IsRefusedNamingSetting`.

**UtilitiesSubsystem_SecretSourceWorkflow_FileOfExactlyTheLimit_ResolvesSecret**: A secret file of exactly 8192 bytes is
written inside a `TemporaryDirectory`; `SecretSource.Resolve` returns the whole contents and throws nothing. This
scenario is tested by `UtilitiesSubsystem_SecretSourceWorkflow_FileOfExactlyTheLimit_ResolvesSecret`.

**UtilitiesSubsystem_SecretSourceWorkflow_UnusableFiles_AreRefusedNamingSetting**: In a `TemporaryDirectory`, a file
that is missing, a directory, an empty file, a file of 8193 bytes, and a file that is not UTF-8 (the last two start
with a recognizable text) are each given as the secret file; every one of the five calls throws
`InvalidOperationException` whose message names the file setting and does not contain the recognizable text. This
scenario is tested by `UtilitiesSubsystem_SecretSourceWorkflow_UnusableFiles_AreRefusedNamingSetting`.

**UtilitiesSubsystem_SecretSourceWorkflow_InlineValue_IsReturned**: Only an inline secret is given; `SecretSource.Resolve`
returns it unchanged. This scenario is tested by `UtilitiesSubsystem_SecretSourceWorkflow_InlineValue_IsReturned`.

**UtilitiesSubsystem_SecretSourceWorkflow_NeitherSet_ReturnsNoSecret**: Neither the inline secret nor the file is
given; `SecretSource.Resolve` returns no secret and throws nothing. This scenario is tested by
`UtilitiesSubsystem_SecretSourceWorkflow_NeitherSet_ReturnsNoSecret`.

**UtilitiesSubsystem_SecretSourceWorkflow_BothSet_RefusalNamesBothSettings**: A secret file is written inside a
`TemporaryDirectory` and an inline secret is given as well; `SecretSource.Resolve` throws `InvalidOperationException`
whose message names both settings. This scenario is tested by
`UtilitiesSubsystem_SecretSourceWorkflow_BothSet_RefusalNamesBothSettings`.

**UtilitiesSubsystem_SecretSourceWorkflow_BothSet_RefusalHasNoSecret**: The same two sources are given;
`SecretSource.Resolve` throws `InvalidOperationException` whose message contains neither the inline secret nor the
contents of the file. This scenario is tested by `UtilitiesSubsystem_SecretSourceWorkflow_BothSet_RefusalHasNoSecret`.
