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
///     Tests for the PathHelpers class.
/// </summary>
[Collection("Sequential")]
public class PathHelpersTests
{
    /// <summary>
    ///     Test that SafePathCombine correctly combines valid paths.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_ValidPaths_CombinesCorrectly()
    {
        // Arrange: setup valid base path and relative path for combining
        var basePath = "/home/user/project";
        var relativePath = "subfolder/file.txt";

        // Act: execute the operation being tested
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert: verify expected behavior
        Assert.Equal(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine throws ArgumentException for path traversal with double dots.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_PathTraversalWithDoubleDots_ThrowsArgumentException()
    {
        // Arrange: setup base path and dangerous path traversal using double dots
        var basePath = "/home/user/project";
        var relativePath = "../etc/passwd";

        // Act: attempt path traversal
        var exception = Record.Exception(() =>
            PathHelpers.SafePathCombine(basePath, relativePath));

        // Assert: verify ArgumentException is thrown with expected message
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Contains("Invalid path component", argumentException.Message);
    }

    /// <summary>
    ///     Test that SafePathCombine throws ArgumentException for path with double dots in middle.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_DoubleDotsInMiddle_ThrowsArgumentException()
    {
        // Arrange: setup base path and path with double dots in middle for traversal attempt
        var basePath = "/home/user/project";
        var relativePath = "subfolder/../../../etc/passwd";

        // Act: attempt path traversal in middle
        var exception = Record.Exception(() =>
            PathHelpers.SafePathCombine(basePath, relativePath));

        // Assert: verify ArgumentException is thrown
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Contains("Invalid path component", argumentException.Message);
    }

    /// <summary>
    ///     Test that SafePathCombine throws ArgumentException for absolute paths.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_AbsolutePath_ThrowsArgumentException()
    {
        // Arrange: setup Unix and Windows absolute path rejection inputs
        var unixBasePath = "/home/user/project";
        var unixRelativePath = "/etc/passwd";
        var windowsBasePath = "C:\\Users\\project";
        var windowsRelativePath = "C:\\Windows\\System32\\file.txt";

        // Act: attempt Unix absolute path rejection, and Windows rejection only on Windows
        var unixException = Record.Exception(() =>
            PathHelpers.SafePathCombine(unixBasePath, unixRelativePath));
        Exception? windowsException = null;
        if (OperatingSystem.IsWindows())
        {
            windowsException = Record.Exception(() =>
                PathHelpers.SafePathCombine(windowsBasePath, windowsRelativePath));
        }

        // Assert: both absolute paths are rejected with ArgumentException
        var unixArgumentException = Assert.IsType<ArgumentException>(unixException);
        Assert.Contains("Invalid path component", unixArgumentException.Message);
        if (OperatingSystem.IsWindows())
        {
            var windowsArgumentException = Assert.IsType<ArgumentException>(windowsException);
            Assert.Contains("Invalid path component", windowsArgumentException.Message);
        }
    }

    /// <summary>
    ///     Test that SafePathCombine correctly handles current directory reference.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_CurrentDirectoryReference_CombinesCorrectly()
    {
        // Arrange: setup base path and current directory reference for testing
        var basePath = "/home/user/project";
        var relativePath = "./subfolder/file.txt";

        // Act: execute the operation being tested
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert: verify expected behavior
        Assert.Equal(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine correctly handles nested paths.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_NestedPaths_CombinesCorrectly()
    {
        // Arrange: setup base path and deeply nested relative path
        var basePath = "/home/user/project";
        var relativePath = "level1/level2/level3/file.txt";

        // Act: execute the operation being tested
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert: verify expected behavior
        Assert.Equal(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine correctly handles empty relative path.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_EmptyRelativePath_ReturnsBasePath()
    {
        // Arrange: setup base path and empty relative path for testing edge case
        var basePath = "/home/user/project";
        var relativePath = "";

        // Act: execute the operation being tested
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert: verify expected behavior
        Assert.Equal(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine correctly handles paths with segments beginning with double dots (not traversal).
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_DotDotPrefixedName_CombinesCorrectly()
    {
        // Arrange: setup base path and directory name starting with double dots (not traversal)
        var basePath = "/home/user/project";
        var relativePath = "..data/file.txt";

        // Act: execute the operation being tested
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert: verify expected behavior
        Assert.Equal(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine throws ArgumentNullException when basePath is null.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_NullBasePath_ThrowsArgumentNullException()
    {
        // Arrange: no setup required; the null basePath is the input under test

        // Act: combine with a null basePath
        var exception = Record.Exception(() =>
            PathHelpers.SafePathCombine(null!, "file.txt"));

        // Assert: ArgumentNullException is thrown
        Assert.IsType<ArgumentNullException>(exception);
    }

    /// <summary>
    ///     Test that SafePathCombine throws ArgumentNullException when relativePath is null.
    /// </summary>
    [Fact]
    public void PathHelpers_SafePathCombine_NullRelativePath_ThrowsArgumentNullException()
    {
        // Arrange: no setup required; the null relativePath is the input under test

        // Act: combine with a null relativePath
        var exception = Record.Exception(() =>
            PathHelpers.SafePathCombine("/home/user", null!));

        // Assert: ArgumentNullException is thrown
        Assert.IsType<ArgumentNullException>(exception);
    }
}


