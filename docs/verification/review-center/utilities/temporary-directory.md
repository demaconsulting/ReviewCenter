### TemporaryDirectory

#### Verification Approach

`TemporaryDirectory` is verified with unit tests defined in `TemporaryDirectoryTests.cs`. The tests
use the real file system under the system temporary path and the real `PathHelpers`. The only
stand-in is the pause between two delete attempts: the retry tests pass a recorder (`PauseRecorder`)
through the internal constructor, so they observe the pauses that the policy asks for, and release a
locked file during the first pause, instead of measuring how long a disposal takes. Each test
disposes the directory it creates, so the tests leave nothing behind.

#### Test Environment

N/A - standard test environment. The scenario that holds a file open to block the delete is
meaningful on Windows, where an open file cannot be deleted; on Linux the delete succeeds and
the test still confirms that disposal never throws.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A new instance creates a directory directly under the system temporary path whose name is a GUID (`Unique`).
- Two instances that exist at the same time never share a directory (`Unique-PerInstance`).
- `GetFilePath` returns the full path of the file inside the directory (`GetFilePath`).
- `GetFilePath` creates the folders above a file in a nested relative path (`GetFilePath-Parents`).
- `GetFilePath` throws `ArgumentException` for a path that would leave the directory (`GetFilePath-Escape`).
- `GetFilePath` throws `ArgumentNullException` for a null path (`GetFilePath-NullPath`).
- `Dispose` removes the directory with all its files, including read-only files (`Dispose`).
- `Dispose` does not throw when a file in the directory is held open (`Dispose-NoThrow`).
- `Dispose` can be called twice without an exception (`Dispose-Repeated`).
- `Dispose` retries a locked directory and removes it when the lock is released within the retry time
  (`BoundedRetry`).
- The default retry policy tries a failing delete five times, with a pause of 75 ms between two attempts, before
  giving up (`BoundedRetry-Default`).
- A permanent lock is given up quietly after the bounded attempts (`BoundedRetry-GiveUp`).
- No pause is taken after the last attempt (`BoundedRetry-NoFinalPause`).
- A retry policy with fewer than one attempt or a negative pause is refused (`BoundedRetry-InvalidPolicy`).

#### Test Scenarios

**TemporaryDirectory_Constructor_Default_CreatesGuidNamedDirectoryUnderTempPath**: An instance is
created; its directory exists, its parent is the system temporary path, and its name parses as a
GUID. This scenario is tested by
`TemporaryDirectory_Constructor_Default_CreatesGuidNamedDirectoryUnderTempPath`.

**TemporaryDirectory_Constructor_TwoInstances_UseDifferentDirectories**: Two instances are
created at the same time; their paths differ. This scenario is tested by
`TemporaryDirectory_Constructor_TwoInstances_UseDifferentDirectories`.

**TemporaryDirectory_Dispose_DefaultPolicyFileLockedPermanently_TriesFiveTimesWithShortPauses**: On Windows a file
stays held open for the whole disposal of a directory with the default policy; no exception is thrown, the directory
stays, and exactly four pauses of 75 ms were asked for between the five attempts. This scenario is tested by
`TemporaryDirectory_Dispose_DefaultPolicyFileLockedPermanently_TriesFiveTimesWithShortPauses`.

**TemporaryDirectory_GetFilePath_NestedRelativePath_ReturnsPathInsideAndCreatesParents**: A file
two folders down is requested; the returned path is the full path of that file inside the directory, its
parent folder exists, and the file itself is not created. This scenario is tested by
`TemporaryDirectory_GetFilePath_NestedRelativePath_ReturnsPathInsideAndCreatesParents`.

**TemporaryDirectory_GetFilePath_PathEscapesDirectory_ThrowsArgumentException**: A path that
starts with `..` is requested; an `ArgumentException` is thrown. This scenario is tested by
`TemporaryDirectory_GetFilePath_PathEscapesDirectory_ThrowsArgumentException`.

**TemporaryDirectory_GetFilePath_NullPath_ThrowsArgumentNullException**: A null path is
requested; an `ArgumentNullException` is thrown. This scenario is tested by
`TemporaryDirectory_GetFilePath_NullPath_ThrowsArgumentNullException`.

**TemporaryDirectory_Dispose_DirectoryWithReadOnlyFile_DeletesEverything**: A read-only file is
created in a nested folder, as git does for its object files, and the directory is disposed; the
directory no longer exists. This scenario is tested by
`TemporaryDirectory_Dispose_DirectoryWithReadOnlyFile_DeletesEverything`.

**TemporaryDirectory_Dispose_CalledTwice_DoesNotThrow**: The directory is disposed twice; no
exception is thrown. This scenario is tested by
`TemporaryDirectory_Dispose_CalledTwice_DoesNotThrow`.

**TemporaryDirectory_Dispose_FileInUse_DoesNotThrow**: A file in the directory is held open
exclusively while the directory is disposed; no exception is thrown, and after the file is
released a second disposal removes what is left. This scenario is tested by
`TemporaryDirectory_Dispose_FileInUse_DoesNotThrow`.

**TemporaryDirectory_Dispose_FileLockedBriefly_RetriesAndDeletesDirectory**: On Windows a file is
held open while the directory is disposed and released during the first pause, so the first attempt
always fails; the single `Dispose` call still removes the whole directory, which a single attempt could not.
This scenario is tested by `TemporaryDirectory_Dispose_FileLockedBriefly_RetriesAndDeletesDirectory`.

**TemporaryDirectory_Dispose_FileLockedPermanently_GivesUpWithoutThrowingAfterTheAttempts**: On
Windows a file stays held open for the whole disposal of a directory with an explicit policy of three attempts 50 ms
apart; no exception is thrown, the directory stays, and exactly two pauses of 50 ms were asked for. This scenario is
tested by `TemporaryDirectory_Dispose_FileLockedPermanently_GivesUpWithoutThrowingAfterTheAttempts`.

**TemporaryDirectory_Constructor_InvalidRetryPolicy_ThrowsArgumentOutOfRangeException**: Zero
attempts and a negative pause are refused. This scenario is tested by
`TemporaryDirectory_Constructor_InvalidRetryPolicy_ThrowsArgumentOutOfRangeException`.

**TemporaryDirectory_Dispose_SingleAttemptPolicy_DoesNotPause**: With one attempt and a long pause
a locked directory is given up without any pause being asked for, so the pause is only used between attempts. This
scenario is
tested by `TemporaryDirectory_Dispose_SingleAttemptPolicy_DoesNotPause`.
