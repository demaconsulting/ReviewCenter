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
///     Subsystem tests for the Utilities subsystem covering PathHelpers integration workflows.
/// </summary>
[Collection("Sequential")]
public class UtilitiesSubsystemTests
{
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
            foreach (var rootDir in new[] { rootDir1, rootDir2 })
            {
                if (Directory.Exists(rootDir))
                {
                    Directory.Delete(rootDir, true);
                }
            }
        }
    }
}

