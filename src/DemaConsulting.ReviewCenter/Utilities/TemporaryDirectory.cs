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

namespace DemaConsulting.ReviewCenter.Utilities;

/// <summary>
///     A disposable temporary directory that is deleted, with everything in it, when disposed.
/// </summary>
/// <remarks>
///     The directory is created directly under <see cref="Path.GetTempPath()"/> and is named with a new GUID, so
///     two instances never share a folder and a leftover folder cannot be confused with the data of a user. The
///     self-validation uses one instance per check so that every file a check creates (databases, key files, git
///     repositories) is removed when the check ends. Disposal is best-effort: a folder that cannot be removed is
///     left to the operating system's temporary-file cleanup instead of failing the work that was already done.
///     Instances are not thread-safe; use one per unit of work.
/// </remarks>
internal sealed class TemporaryDirectory : IDisposable
{
    /// <summary>
    ///     Gets the full path of the temporary directory.
    /// </summary>
    public string DirectoryPath { get; }

    /// <summary>
    ///     Initializes a new instance of the <see cref="TemporaryDirectory"/> class and creates the directory.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the directory cannot be created because of an <see cref="IOException"/>,
    ///     <see cref="UnauthorizedAccessException"/> or <see cref="ArgumentException"/>.
    /// </exception>
    public TemporaryDirectory()
    {
        DirectoryPath = PathHelpers.SafePathCombine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(DirectoryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new InvalidOperationException($"Failed to create temporary directory: {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Returns the full path of a file inside the temporary directory and creates the folders above it.
    /// </summary>
    /// <param name="relativePath">The path of the file relative to the directory. Must not be null.</param>
    /// <returns>The combined full path, which is always inside <see cref="DirectoryPath"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="relativePath"/> is null.</exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="relativePath"/> would leave the temporary directory.
    /// </exception>
    /// <remarks>
    ///     The escape check is <see cref="PathHelpers.SafePathCombine"/>, so a caller-supplied name can never reach
    ///     outside the directory that is deleted on disposal. Side effect: creates the missing parent folders.
    /// </remarks>
    public string GetFilePath(string relativePath)
    {
        // The security boundary: refuse anything that resolves outside the directory
        var path = PathHelpers.SafePathCombine(DirectoryPath, relativePath);

        // Callers write straight to the returned path, so its folders must exist
        var directory = Path.GetDirectoryName(path);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        return path;
    }

    /// <summary>
    ///     Deletes the temporary directory and all of its contents.
    /// </summary>
    /// <remarks>
    ///     <see cref="IOException"/> and <see cref="UnauthorizedAccessException"/> are swallowed on purpose: a
    ///     cleanup failure must not turn a finished piece of work into a failure. Read-only flags are cleared first,
    ///     because git writes its object files read-only and Windows refuses to delete such files. Calling the method
    ///     more than once is safe.
    /// </remarks>
    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(DirectoryPath))
            {
                return;
            }

            // Git object files are read-only, which blocks the recursive delete on Windows
            foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore cleanup errors during disposal
        }
    }
}
