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
  (`TempFiles-InsideOnly`).
- Files created through a `TemporaryDirectory`, including in nested folders, are gone after it is disposed
  (`TempFiles-Removed`).
- A secret file written to a `TemporaryDirectory` is resolved by `SecretSource` as its trimmed contents
  (`SecretSource`).
- An inline secret is returned as it is given (`SecretSource-Inline`).
- Once the secret file is gone, resolving it is refused with a message that names the file setting
  (`SecretSource-Refusal`).
- A secret that is set inline and in a file is refused with a message that holds neither secret
  (`SecretSource-NoDisclosure`).

### Test Scenarios

**UtilitiesSubsystem_TemporaryDirectoryWorkflow_TwoDirectories_UseDifferentPaths**: Two `TemporaryDirectory`
instances exist at the same time and each is asked for the same relative file name; the directories and the files in
them have different paths. This scenario is tested by
`UtilitiesSubsystem_TemporaryDirectoryWorkflow_TwoDirectories_UseDifferentPaths`.

**UtilitiesSubsystem_TemporaryDirectoryWorkflow_FilesCreated_AreRemovedOnDispose**: Files are
created in nested folders through `TemporaryDirectory.GetFilePath` and written; every path lies inside the directory,
and after the instance is disposed neither the files nor the directory exist. This scenario is tested by
`UtilitiesSubsystem_TemporaryDirectoryWorkflow_FilesCreated_AreRemovedOnDispose`.

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

**UtilitiesSubsystem_SecretSourceWorkflow_InlineValue_IsReturned**: Only an inline secret is given; `SecretSource.Resolve`
returns it unchanged. This scenario is tested by `UtilitiesSubsystem_SecretSourceWorkflow_InlineValue_IsReturned`.

**UtilitiesSubsystem_SecretSourceWorkflow_BothSet_RefusalHasNoSecret**: A secret file is written inside a
`TemporaryDirectory` and an inline secret is given as well; `SecretSource.Resolve` throws `InvalidOperationException`
whose message names both settings and contains neither the inline secret nor the contents of the file. This scenario
is tested by `UtilitiesSubsystem_SecretSourceWorkflow_BothSet_RefusalHasNoSecret`.
