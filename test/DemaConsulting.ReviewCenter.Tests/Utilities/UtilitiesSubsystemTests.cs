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
///     Subsystem tests for the Utilities subsystem covering PathHelpers, TemporaryDirectory, and SecretSource
///     integration workflows.
/// </summary>
[Collection("Sequential")]
public class UtilitiesSubsystemTests
{
    /// <summary>
    ///     Test that utilities subsystem resolves a secret from a file kept in a temporary directory, trimmed.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_SecretSourceWorkflow_FileInTemporaryDirectory_ResolvesTrimmedSecret()
    {
        // Arrange: a secret file with a trailing newline inside a temporary directory
        using var directory = new TemporaryDirectory();
        var path = directory.GetFilePath("secrets/client.txt");
        File.WriteAllText(path, "workflow-secret\n");

        // Act: resolve from the file
        var secret = SecretSource.Resolve(null, "Test:Secret", path, "Test:SecretFile");

        // Assert: the secret is the trimmed contents
        Assert.Equal("workflow-secret", secret);
    }

    /// <summary>
    ///     Test that utilities subsystem refuses a secret file once the temporary directory that held it is gone.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_SecretSourceWorkflow_FileRemoved_IsRefusedNamingSetting()
    {
        // Arrange: a secret file inside a temporary directory that is disposed afterwards
        string path;
        using (var directory = new TemporaryDirectory())
        {
            path = directory.GetFilePath("secrets/client.txt");
            File.WriteAllText(path, "workflow-secret\n");
        }

        // Act: resolve from the file that no longer exists
        var exception = Assert.Throws<InvalidOperationException>(
            () => SecretSource.Resolve(null, "Test:Secret", path, "Test:SecretFile"));

        // Assert: the refusal names the file setting
        Assert.Contains("Test:SecretFile", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that utilities subsystem can handle path combination workflows.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_PathResolutionWorkflow_ValidPaths_ResolvesCorrectly()
    {
        // Arrange: setup base path and test paths for path combination testing
        var basePath = Path.GetTempPath();
        var testPaths = new[]
        {
            "test.txt",
            "subdirectory/test.txt",
            "nested/dir/test.txt"
        };

        // Act: combine each test path with base path
        var fullBasePath = Path.GetFullPath(basePath);
        var combinedPaths = testPaths
            .Select(testPath => PathHelpers.SafePathCombine(basePath, testPath))
            .ToList();

        // Assert: verify results are valid absolute paths within the base directory
        for (var i = 0; i < testPaths.Length; i++)
        {
            var testPath = testPaths[i];
            var combinedPath = combinedPaths[i];
            var fullCombinedPath = Path.GetFullPath(combinedPath);
            var relativePath = Path.GetRelativePath(fullBasePath, fullCombinedPath);

            Assert.True(Path.IsPathFullyQualified(combinedPath),
                $"SafePathCombine should return absolute path for {testPath}");
            Assert.False(
                Path.IsPathRooted(relativePath) ||
                relativePath.Equals("..", StringComparison.Ordinal) ||
                relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal),
                $"Combined path should be within base directory for {testPath}");
        }
    }

    /// <summary>
    ///     Test that utilities subsystem rejects dangerous path traversal attempts.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_PathTraversalValidation_DangerousPaths_ThrowsException()
    {
        // Arrange: setup base path and dangerous path traversal attempts
        var basePath = Path.GetTempPath();
        var dangerousPaths = new[]
        {
            "../escape.txt",
            "../../escape.txt",
            "subdir/../../../escape.txt"
        };

        // Act: attempt path traversal
        var exceptions = dangerousPaths
            .Select(dangerousPath => Record.Exception(() =>
                PathHelpers.SafePathCombine(basePath, dangerousPath)))
            .ToList();

        // Assert: verify exceptions are thrown
        Assert.Equal(dangerousPaths.Length, exceptions.Count);
        Assert.All(exceptions, exception => Assert.IsType<ArgumentException>(exception));
    }

    /// <summary>
    ///     Test that utilities subsystem rejects absolute path injection at the subsystem level.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_AbsolutePathRejection_ThrowsException()
    {
        // Arrange: setup base path and absolute path injection attempts
        var basePath = Path.GetTempPath();
        var absolutePaths = new List<string> { "/etc/passwd" };
        if (OperatingSystem.IsWindows())
        {
            absolutePaths.Add(@"C:\Windows\System32\file.txt");
        }

        // Act: attempt absolute path injection
        var exceptions = absolutePaths
            .Select(absolutePath => Record.Exception(() =>
                PathHelpers.SafePathCombine(basePath, absolutePath)))
            .ToList();

        // Assert: verify exceptions are thrown
        Assert.Equal(absolutePaths.Count, exceptions.Count);
        Assert.All(exceptions, exception => Assert.IsType<ArgumentException>(exception));
    }

    /// <summary>
    ///     Test that utilities subsystem rejects a null base directory and a null relative path.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_PathResolutionWorkflow_NullArguments_ThrowsArgumentNullException()
    {
        // Arrange: a valid base path and a valid relative path
        var basePath = Path.GetTempPath();

        // Act: combine with a null relative path and with a null base path
        var nullRelative = Record.Exception(() => PathHelpers.SafePathCombine(basePath, null!));
        var nullBase = Record.Exception(() => PathHelpers.SafePathCombine(null!, "file.txt"));

        // Assert: each null is refused with the exception that names the missing argument
        var relativeException = Assert.IsType<ArgumentNullException>(nullRelative);
        Assert.Equal("relativePath", relativeException.ParamName);
        var baseException = Assert.IsType<ArgumentNullException>(nullBase);
        Assert.Equal("basePath", baseException.ParamName);
    }

    /// <summary>
    ///     Test that the temporary directory removes the nested files of a workflow and the directory on disposal.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_TemporaryDirectoryWorkflow_FilesCreated_AreRemovedOnDispose()
    {
        // Arrange: a temporary directory and the files a check would create in nested folders
        string directoryPath;
        string[] files;
        using (var directory = new TemporaryDirectory())
        {
            directoryPath = directory.DirectoryPath;
            files =
            [
                directory.GetFilePath("selftest.db"),
                directory.GetFilePath("keys/key.xml"),
                directory.GetFilePath("git-cache/repo-0/objects/pack/data")
            ];

            // Act: write the files and let the directory go out of scope
            foreach (var file in files)
            {
                File.WriteAllText(file, "data");
            }

            Assert.All(files, file => Assert.True(File.Exists(file)));
        }

        // Assert: nothing is left behind, neither the files nor the directory
        Assert.False(Directory.Exists(directoryPath));
        Assert.All(files, file => Assert.False(File.Exists(file)));
    }

    /// <summary>
    ///     Test that every path the temporary directory hands out lies inside the directory.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_TemporaryDirectoryWorkflow_FilePaths_AreInsideDirectory()
    {
        // Arrange: a temporary directory and the file names a workflow would ask for
        using var directory = new TemporaryDirectory();
        var names = new[] { "selftest.db", "keys/key.xml", "git-cache/repo-0/objects/pack/data" };

        // Act: ask for the path of each file
        var paths = names.Select(directory.GetFilePath).ToList();

        // Assert: every path is the file inside the directory
        Assert.Equal(names.Length, paths.Count);
        var prefix = directory.DirectoryPath + Path.DirectorySeparatorChar;
        for (var i = 0; i < names.Length; i++)
        {
            Assert.Equal(
                Path.GetFullPath(Path.Combine(directory.DirectoryPath, names[i])),
                Path.GetFullPath(paths[i]));
            Assert.StartsWith(prefix, paths[i], StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that a file in nested folders of the temporary directory can be written at once.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_TemporaryDirectoryWorkflow_NestedFilePath_CanBeWrittenAtOnce()
    {
        // Arrange: a temporary directory and the path of a file three folders down
        using var directory = new TemporaryDirectory();
        var path = directory.GetFilePath("one/two/three/file.txt");

        // Act: write the file without creating any folder
        File.WriteAllText(path, "data");

        // Assert: the folders above the file exist and the file holds the data
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
        Assert.Equal("data", File.ReadAllText(path));
    }

    /// <summary>
    ///     Test that file names that would leave the temporary directory are rejected.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_TemporaryDirectoryWorkflow_EscapingFileNames_AreRejected()
    {
        // Arrange: a temporary directory and names that climb out of it
        using var directory = new TemporaryDirectory();
        var names = new[] { "../escape.txt", "../../escape.txt", "sub/../../escape.txt" };

        // Act: ask for the path of each name
        var exceptions = names
            .Select(name => Record.Exception(() => directory.GetFilePath(name)))
            .ToList();

        // Assert: every name is rejected
        Assert.Equal(names.Length, exceptions.Count);
        Assert.All(exceptions, exception => Assert.IsType<ArgumentException>(exception));
    }

    /// <summary>
    ///     Test that a null file name is rejected by the temporary directory, naming the argument.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_TemporaryDirectoryWorkflow_NullFileName_IsRejected()
    {
        // Arrange: a temporary directory
        using var directory = new TemporaryDirectory();

        // Act: ask for the path of a null name
        var exception = Record.Exception(() => directory.GetFilePath(null!));

        // Assert: the null is rejected with the argument that was missing
        var nullException = Assert.IsType<ArgumentNullException>(exception);
        Assert.Equal("relativePath", nullException.ParamName);
    }

    /// <summary>
    ///     Test that disposing a temporary directory twice does not throw.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_TemporaryDirectoryWorkflow_DisposedTwice_DoesNotThrow()
    {
        // Arrange: a directory with a file, disposed once
        var directory = new TemporaryDirectory();
        File.WriteAllText(directory.GetFilePath("data.txt"), "data");
        directory.Dispose();

        // Act: dispose it again
        var exception = Record.Exception(directory.Dispose);

        // Assert: nothing is thrown and the directory is gone
        Assert.Null(exception);
        Assert.False(Directory.Exists(directory.DirectoryPath));
    }

    /// <summary>
    ///     Test that a file that is released while the removal retries lets the whole directory be removed.
    /// </summary>
    [Fact]
    public async Task UtilitiesSubsystem_TemporaryDirectoryWorkflow_FileReleasedDuringRetry_DirectoryIsRemoved()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "An open file blocks a delete only on Windows");

        // Arrange: a file in a nested folder that stays open for about 100 ms, while the removal retries every 50 ms
        // for up to 2 s; a dedicated thread releases the file so that a busy thread pool cannot delay the release
        var directory = new TemporaryDirectory(40, TimeSpan.FromMilliseconds(50));
        var directoryPath = directory.DirectoryPath;
        var stream = new FileStream(
            directory.GetFilePath("nested/locked.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Factory.StartNew(
            () =>
            {
                using var gate = new ManualResetEventSlim(false);
                gate.Wait(TimeSpan.FromMilliseconds(100));
                stream.Dispose();
            },
            TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        // Act: dispose while the file is open
        var exception = Record.Exception(directory.Dispose);
        await release;

        // Assert: the retry outlasted the lock and the whole directory is gone
        Assert.Null(exception);
        Assert.False(Directory.Exists(directoryPath));
    }

    /// <summary>
    ///     Test that disposing a temporary directory while a file stays open does not throw and leaves the directory.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_TemporaryDirectoryWorkflow_FileHeldOpen_DisposeLeavesDirectoryWithoutThrowing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "An open file blocks a delete only on Windows");

        // Arrange: a file that stays open for the whole test
        using var directory = new TemporaryDirectory();
        using var stream = new FileStream(
            directory.GetFilePath("locked.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        // Act: dispose while the file is open
        var exception = Record.Exception(directory.Dispose);

        // Assert: nothing is thrown and the directory is left behind
        Assert.Null(exception);
        Assert.True(Directory.Exists(directory.DirectoryPath));

        // Cleanup: release the file and remove what is left
        stream.Dispose();
        directory.Dispose();
        Assert.False(Directory.Exists(directory.DirectoryPath));
    }

    /// <summary>
    ///     Test that disposing a temporary directory while a file stays open stops waiting in less than three seconds.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_TemporaryDirectoryWorkflow_FileHeldOpen_DisposeReturnsInBoundedTime()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "An open file blocks a delete only on Windows");

        // Arrange: a file that stays open for the whole test
        using var directory = new TemporaryDirectory();
        using var stream = new FileStream(
            directory.GetFilePath("locked.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        // Act: dispose and measure how long it takes
        var watch = System.Diagnostics.Stopwatch.StartNew();
        directory.Dispose();
        watch.Stop();

        // Assert: the wait is bounded
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"Waited too long: {watch.Elapsed}");

        // Cleanup: release the file and remove what is left
        stream.Dispose();
        directory.Dispose();
    }

    /// <summary>
    ///     Test that a policy of one attempt returns without a pause, even with a long pause configured.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_TemporaryDirectoryWorkflow_SingleAttemptPolicy_DisposeDoesNotPause()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "An open file blocks a delete only on Windows");

        // Arrange: a file that stays open and a policy of one attempt with a pause that must never be taken
        using var directory = new TemporaryDirectory(1, TimeSpan.FromSeconds(30));
        using var stream = new FileStream(
            directory.GetFilePath("locked.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        // Act: dispose and measure how long it takes
        var watch = System.Diagnostics.Stopwatch.StartNew();
        directory.Dispose();
        watch.Stop();

        // Assert: it returned long before the pause
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Paused after the last attempt: {watch.Elapsed}");

        // Cleanup: release the file and remove what is left
        stream.Dispose();
        directory.Dispose();
    }

    /// <summary>
    ///     Test that a retry policy with no attempts or a negative pause is rejected when the directory is created.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_TemporaryDirectoryWorkflow_InvalidRetryPolicy_IsRejected()
    {
        // Arrange: policies with no attempt, a negative number of attempts and a negative pause
        var policies = new (int Attempts, TimeSpan Pause)[]
        {
            (0, TimeSpan.FromMilliseconds(10)),
            (-1, TimeSpan.FromMilliseconds(10)),
            (3, TimeSpan.FromMilliseconds(-1))
        };

        // Act: create a directory with each policy
        var exceptions = policies
            .Select(policy => Record.Exception(() => new TemporaryDirectory(policy.Attempts, policy.Pause)))
            .ToList();

        // Assert: every policy is rejected
        Assert.Equal(policies.Length, exceptions.Count);
        Assert.All(exceptions, exception => Assert.IsType<ArgumentOutOfRangeException>(exception));
    }

    /// <summary>
    ///     Test that two temporary directories that exist at the same time have different paths.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_TemporaryDirectoryWorkflow_TwoDirectories_UseDifferentPaths()
    {
        // Arrange: two temporary directories that exist at the same time
        using var first = new TemporaryDirectory();
        using var second = new TemporaryDirectory();

        // Act: ask each for the same relative file name
        var firstFile = first.GetFilePath("shared/name.txt");
        var secondFile = second.GetFilePath("shared/name.txt");

        // Assert: the directories and the files in them are different
        Assert.NotEqual(first.DirectoryPath, second.DirectoryPath);
        Assert.NotEqual(firstFile, secondFile);
    }

    /// <summary>
    ///     Test that utilities subsystem returns an inline secret as it is given.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_SecretSourceWorkflow_InlineValue_IsReturned()
    {
        // Arrange: an inline secret and no file setting

        // Act: resolve the secret
        var secret = SecretSource.Resolve("inline-secret", "Test:Secret", null, "Test:SecretFile");

        // Assert: the inline secret is returned unchanged
        Assert.Equal("inline-secret", secret);
    }

    /// <summary>
    ///     Test that utilities subsystem resolves to no secret, without an error, when neither setting is set.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_SecretSourceWorkflow_NeitherSet_ReturnsNoSecret()
    {
        // Arrange: neither the inline setting nor the file setting is set

        // Act: resolve the secret; an exception would fail the test
        var secret = SecretSource.Resolve(null, "Test:Secret", null, "Test:SecretFile");

        // Assert: no secret is returned
        Assert.Null(secret);
    }

    /// <summary>
    ///     Test that utilities subsystem refuses a secret that is set inline and in a file, naming both settings.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_SecretSourceWorkflow_BothSet_RefusalNamesBothSettings()
    {
        // Arrange: a secret file in a temporary directory and an inline secret
        using var directory = new TemporaryDirectory();
        var path = directory.GetFilePath("secrets/client.txt");
        File.WriteAllText(path, "file-secret\n");

        // Act: resolve with both settings set
        var exception = Assert.Throws<InvalidOperationException>(
            () => SecretSource.Resolve("inline-secret", "Test:Secret", path, "Test:SecretFile"));

        // Assert: the message names both settings
        Assert.Contains("Test:Secret", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Test:SecretFile", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that utilities subsystem refuses a secret that is set inline and in a file, without the secret in the
    ///     message.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_SecretSourceWorkflow_BothSet_RefusalHasNoSecret()
    {
        // Arrange: a secret file in a temporary directory and an inline secret
        using var directory = new TemporaryDirectory();
        var path = directory.GetFilePath("secrets/client.txt");
        File.WriteAllText(path, "file-secret\n");

        // Act: resolve with both settings set
        var exception = Assert.Throws<InvalidOperationException>(
            () => SecretSource.Resolve("inline-secret", "Test:Secret", path, "Test:SecretFile"));

        // Assert: the message holds neither secret
        Assert.DoesNotContain("inline-secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("file-secret", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that utilities subsystem refuses every kind of unusable secret file, naming the file setting and
    ///     without the contents of the file in the message.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_SecretSourceWorkflow_UnusableFiles_AreRefusedNamingSetting()
    {
        // Arrange: a file that is not there, a directory, an empty file, a file of 8193 bytes and a file that is not
        // UTF-8, the last two with a recognizable text at the start
        using var directory = new TemporaryDirectory();
        var unusable = new Dictionary<string, string>
        {
            ["missing"] = directory.GetFilePath("missing.txt"),
            ["directory"] = directory.DirectoryPath,
            ["empty"] = directory.GetFilePath("empty.txt"),
            ["too-large"] = directory.GetFilePath("large.txt"),
            ["not-utf8"] = directory.GetFilePath("binary.bin")
        };
        File.WriteAllText(unusable["empty"], string.Empty);
        File.WriteAllText(unusable["too-large"], "leaked-secret" + new string('x', 8193 - "leaked-secret".Length));
        File.WriteAllBytes(unusable["not-utf8"], [.. "leaked-secret"u8.ToArray(), 0xFF, 0xFE, 0xC3, 0x28]);

        // Act: resolve each file
        var refusals = unusable.ToDictionary(
            entry => entry.Key,
            entry => Record.Exception(() => SecretSource.Resolve(null, "Test:Secret", entry.Value, "Test:SecretFile")));

        // Assert: every file is refused with a message that names the file setting and holds no file contents
        Assert.Equal(5, refusals.Count);
        foreach (var (kind, refusal) in refusals)
        {
            var exception = Assert.IsType<InvalidOperationException>(refusal);
            Assert.True(exception.Message.Contains("Test:SecretFile", StringComparison.Ordinal),
                $"The refusal of the {kind} file does not name the file setting: {exception.Message}");
            Assert.DoesNotContain("leaked-secret", exception.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that utilities subsystem can handle directory creation workflows.
    /// </summary>
    [Fact]
    public void UtilitiesSubsystem_DirectoryCreationWorkflow_ValidPaths_CreatesDirectories()
    {
        // Arrange: setup temp directory and unique root directories for cleanup tracking
        var tempDir = Path.GetTempPath();
        var rootDir1 = PathHelpers.SafePathCombine(tempDir, $"test_{Guid.NewGuid()}");
        var rootDir2 = PathHelpers.SafePathCombine(tempDir, $"nested_{Guid.NewGuid()}");
        var testDirs = new[]
        {
            rootDir1,
            PathHelpers.SafePathCombine(rootDir2, "subdirectory")
        };

        try
        {
            // Act: create directories
            foreach (var testDir in testDirs)
            {
                Directory.CreateDirectory(testDir);
            }

            // Assert: verify they exist
            foreach (var testDir in testDirs)
            {
                Assert.True(Directory.Exists(testDir),
                    $"Directory should be created successfully: {testDir}");
            }
        }
        finally
        {
            // Cleanup: delete only the root directories created by this test
            foreach (var rootDir in new[] { rootDir1, rootDir2 }.Where(Directory.Exists))
            {
                Directory.Delete(rootDir, true);
            }
        }
    }
}

