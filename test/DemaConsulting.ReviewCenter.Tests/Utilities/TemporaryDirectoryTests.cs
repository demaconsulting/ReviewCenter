// Copyright (c) DEMA Consulting
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using DemaConsulting.ReviewCenter.Utilities;

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     Unit tests for the TemporaryDirectory class.
/// </summary>
public class TemporaryDirectoryTests
{
    /// <summary>
    ///     Test that the constructor creates a GUID-named directory under the system temporary path.
    /// </summary>
    [Fact]
    public void TemporaryDirectory_Constructor_Default_CreatesGuidNamedDirectoryUnderTempPath()
    {
        // Arrange: nothing to set up; the directory is created by the constructor

        // Act: create a temporary directory
        using var directory = new TemporaryDirectory();

        // Assert: the directory exists, is directly under the temp path and is named with a GUID
        var parent = Path.GetDirectoryName(directory.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(directory.DirectoryPath));
        Assert.Equal(
            Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(parent!).TrimEnd(Path.DirectorySeparatorChar));
        Assert.True(Guid.TryParseExact(Path.GetFileName(directory.DirectoryPath), "N", out _));
    }

    /// <summary>
    ///     Test that two instances never share a directory.
    /// </summary>
    [Fact]
    public void TemporaryDirectory_Constructor_TwoInstances_UseDifferentDirectories()
    {
        // Arrange: create the first directory
        using var first = new TemporaryDirectory();

        // Act: create a second directory
        using var second = new TemporaryDirectory();

        // Assert: the two paths differ
        Assert.NotEqual(first.DirectoryPath, second.DirectoryPath);
    }

    /// <summary>
    ///     Test that a directory created with the default retry policy tries a delete that keeps failing five times,
    ///     with a pause of 75 ms between two attempts and none after the last, and then gives up.
    /// </summary>
    [Fact]
    public void TemporaryDirectory_Dispose_DefaultPolicyFileLockedPermanently_TriesFiveTimesWithShortPauses()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "An open file blocks a delete only on Windows");

        // Arrange: a file that stays open for the whole test in a directory with the default policy, whose pauses
        // are recorded instead of waited for
        var pauses = new PauseRecorder();
        using var directory = new TemporaryDirectory(pauses.Pause);
        var path = directory.GetFilePath("locked.txt");
        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        // Act: dispose while the file is held
        var exception = Record.Exception(directory.Dispose);

        // Assert: nothing is thrown, the directory is left, and five attempts were made with four pauses of 75 ms
        Assert.Null(exception);
        Assert.True(Directory.Exists(directory.DirectoryPath));
        Assert.Equal(Enumerable.Repeat(TimeSpan.FromMilliseconds(75), 4), pauses.Pauses);

        // Cleanup: release the file and remove what is left
        stream.Dispose();
        directory.Dispose();
        Assert.False(Directory.Exists(directory.DirectoryPath));
    }

    /// <summary>
    ///     Test that GetFilePath returns a path inside the directory and creates the folders above it.
    /// </summary>
    [Fact]
    public void TemporaryDirectory_GetFilePath_NestedRelativePath_ReturnsPathInsideAndCreatesParents()
    {
        // Arrange: a temporary directory
        using var directory = new TemporaryDirectory();

        // Act: ask for a file two folders down
        var path = directory.GetFilePath("one/two/file.txt");

        // Assert: the path is the file inside the directory and its parent folder exists
        var expected = Path.GetFullPath(Path.Combine(directory.DirectoryPath, "one", "two", "file.txt"));
        Assert.Equal(expected, Path.GetFullPath(path));
        Assert.StartsWith(directory.DirectoryPath, path, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(directory.DirectoryPath, "one", "two")),
            Path.GetFullPath(Path.GetDirectoryName(path)!));
        Assert.False(File.Exists(path));
    }

    /// <summary>
    ///     Test that GetFilePath rejects a path that would leave the directory.
    /// </summary>
    [Fact]
    public void TemporaryDirectory_GetFilePath_PathEscapesDirectory_ThrowsArgumentException()
    {
        // Arrange: a temporary directory
        using var directory = new TemporaryDirectory();

        // Act: ask for a path that climbs out of the directory
        var exception = Record.Exception(() => directory.GetFilePath("../escape.txt"));

        // Assert: the escape is refused
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>
    ///     Test that GetFilePath rejects a null path.
    /// </summary>
    [Fact]
    public void TemporaryDirectory_GetFilePath_NullPath_ThrowsArgumentNullException()
    {
        // Arrange: a temporary directory
        using var directory = new TemporaryDirectory();

        // Act: ask for a null path
        var exception = Record.Exception(() => directory.GetFilePath(null!));

        // Assert: a null path is refused
        Assert.IsType<ArgumentNullException>(exception);
    }

    /// <summary>
    ///     Test that Dispose deletes the directory with its files, including read-only ones.
    /// </summary>
    [Fact]
    public void TemporaryDirectory_Dispose_DirectoryWithReadOnlyFile_DeletesEverything()
    {
        // Arrange: a directory that holds a read-only file in a nested folder, like a git object file
        using var directory = new TemporaryDirectory();
        var path = directory.GetFilePath("objects/ab/object");
        File.WriteAllText(path, "content");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var directoryPath = directory.DirectoryPath;

        // Act: dispose the directory
        directory.Dispose();

        // Assert: the whole directory is gone
        Assert.False(Directory.Exists(directoryPath));
    }

    /// <summary>
    ///     Test that Dispose may be called more than once.
    /// </summary>
    [Fact]
    public void TemporaryDirectory_Dispose_CalledTwice_DoesNotThrow()
    {
        // Arrange: a directory that is already disposed
        var directory = new TemporaryDirectory();
        directory.Dispose();

        // Act: dispose it again
        var exception = Record.Exception(directory.Dispose);

        // Assert: nothing is thrown
        Assert.Null(exception);
    }

    /// <summary>
    ///     Test that Dispose swallows an IO failure instead of throwing.
    /// </summary>
    [Fact]
    public void TemporaryDirectory_Dispose_FileInUse_DoesNotThrow()
    {
        // Arrange: a file that stays open, which blocks the delete on Windows
        using var directory = new TemporaryDirectory();
        var path = directory.GetFilePath("locked.txt");
        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        // Act: dispose while the file is open
        var exception = Record.Exception(directory.Dispose);

        // Assert: a cleanup failure never escapes
        Assert.Null(exception);

        // Cleanup: release the file and remove what is left
        stream.Dispose();
        directory.Dispose();
    }

    /// <summary>
    ///     Test that Dispose retries while a file is locked and then removes the directory. The file is released
    ///     during the first pause, so the first attempt always fails and only a retry can remove the directory.
    /// </summary>
    [Fact]
    public void TemporaryDirectory_Dispose_FileLockedBriefly_RetriesAndDeletesDirectory()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "An open file blocks a delete only on Windows");

        // Arrange: a file that stays open until the first pause between two delete attempts
        FileStream? locked = null;
        var pauses = new PauseRecorder(onFirstPause: () => locked!.Dispose(), wait: true);
        var directory = new TemporaryDirectory(40, TimeSpan.FromMilliseconds(50), pauses.Pause);
        var directoryPath = directory.DirectoryPath;
        locked = new FileStream(directory.GetFilePath("locked.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        // Act: dispose while the file is still open
        var exception = Record.Exception(directory.Dispose);
        locked.Dispose();

        // Assert: the first attempt failed, a retry followed the pause, and the whole directory is gone
        Assert.Null(exception);
        Assert.NotEmpty(pauses.Pauses);
        Assert.False(Directory.Exists(directoryPath));
    }

    /// <summary>
    ///     Test that a file that stays locked makes Dispose give up after the configured attempts, without throwing,
    ///     with one pause between two attempts.
    /// </summary>
    [Fact]
    public void TemporaryDirectory_Dispose_FileLockedPermanently_GivesUpWithoutThrowingAfterTheAttempts()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "An open file blocks a delete only on Windows");

        // Arrange: a file that stays open for the whole test and an explicit policy of 3 attempts, 50 ms apart
        var pauses = new PauseRecorder();
        using var directory = new TemporaryDirectory(3, TimeSpan.FromMilliseconds(50), pauses.Pause);
        var path = directory.GetFilePath("locked.txt");
        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        // Act: dispose while the file is held
        var exception = Record.Exception(directory.Dispose);

        // Assert: nothing is thrown, the directory is left, and the three attempts had two pauses between them
        Assert.Null(exception);
        Assert.True(Directory.Exists(directory.DirectoryPath));
        Assert.Equal([TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)], pauses.Pauses);

        // Cleanup: release the file and remove what is left
        stream.Dispose();
        directory.Dispose();
        Assert.False(Directory.Exists(directory.DirectoryPath));
    }

    /// <summary>
    ///     Test that the retry policy rejects values that would never delete or would wait a negative time.
    /// </summary>
    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(3, -1)]
    public void TemporaryDirectory_Constructor_InvalidRetryPolicy_ThrowsArgumentOutOfRangeException(
        int attempts,
        int delayMilliseconds)
    {
        // Act: create a directory with an invalid policy
        var exception = Record.Exception(
            () => new TemporaryDirectory(attempts, TimeSpan.FromMilliseconds(delayMilliseconds)));

        // Assert: the policy is refused
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>
    ///     Test that a policy of one attempt makes Dispose try exactly once and never pause.
    /// </summary>
    [Fact]
    public void TemporaryDirectory_Dispose_SingleAttemptPolicy_DoesNotPause()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "An open file blocks a delete only on Windows");

        // Arrange: a locked file and a policy of one attempt with a long pause that must never be taken
        var pauses = new PauseRecorder();
        using var directory = new TemporaryDirectory(1, TimeSpan.FromSeconds(30), pauses.Pause);
        var path = directory.GetFilePath("locked.txt");
        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        // Act: dispose
        var exception = Record.Exception(directory.Dispose);

        // Assert: the one attempt failed quietly and no pause was asked for
        Assert.Null(exception);
        Assert.True(Directory.Exists(directory.DirectoryPath));
        Assert.Empty(pauses.Pauses);

        // Cleanup
        stream.Dispose();
        directory.Dispose();
    }
}
