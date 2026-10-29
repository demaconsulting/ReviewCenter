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
    ///     The number of times <see cref="Dispose"/> tries to delete the directory before it gives up.
    /// </summary>
    internal const int DefaultDeleteAttempts = 5;

    /// <summary>
    ///     The pause between two delete attempts; long enough for a scanner or a closing handle to let go.
    /// </summary>
    internal static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(75);

    /// <summary>
    ///     How many times <see cref="Dispose"/> tries to delete the directory.
    /// </summary>
    private readonly int _deleteAttempts;

    /// <summary>
    ///     How long <see cref="Dispose"/> waits between two attempts.
    /// </summary>
    private readonly TimeSpan _retryDelay;

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
        : this(DefaultDeleteAttempts, DefaultRetryDelay)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="TemporaryDirectory"/> class with its own retry policy; it
    ///     exists so that tests can exercise the retry without waiting for the production delay.
    /// </summary>
    /// <param name="deleteAttempts">How many times <see cref="Dispose"/> tries to delete the directory; at least 1.</param>
    /// <param name="retryDelay">The pause between two attempts; not negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="deleteAttempts"/> is less than 1 or <paramref name="retryDelay"/> is negative.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the directory cannot be created because of an <see cref="IOException"/>,
    ///     <see cref="UnauthorizedAccessException"/> or <see cref="ArgumentException"/>.
    /// </exception>
    internal TemporaryDirectory(int deleteAttempts, TimeSpan retryDelay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(deleteAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero);
        _deleteAttempts = deleteAttempts;
        _retryDelay = retryDelay;
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
    ///     Deletes the temporary directory and all of its contents, retrying a few times when something still holds it.
    /// </summary>
    /// <remarks>
    ///     A file can stay locked for a moment after the process or connection that used it was stopped (an
    ///     anti-virus scanner, the indexer, a SQLite handle that is released asynchronously), and Windows refuses to
    ///     delete such a file. The delete is therefore tried up to <c>_deleteAttempts</c> times, with a short pause
    ///     between the attempts, so the total wait is bounded (about 0.3 s by default). <see cref="IOException"/> and
    ///     <see cref="UnauthorizedAccessException"/> are swallowed on purpose after the last attempt: a cleanup
    ///     failure must not turn a finished piece of work into a failure, and a folder that is still locked is left
    ///     to the operating system's temporary-file cleanup. Read-only flags are cleared first, because git writes
    ///     its object files read-only and Windows refuses to delete such files. Calling the method more than once is
    ///     safe: it returns at once when the folder is gone. Everything that uses the folder (hosts, processes,
    ///     database connections) must be stopped before the call, which the callers do by disposing it last.
    /// </remarks>
    public void Dispose()
    {
        for (var attempt = 1; attempt <= _deleteAttempts; attempt++)
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
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Wait for the lock to go away, except after the last attempt, which gives up quietly
                if (attempt < _deleteAttempts)
                {
                    Thread.Sleep(_retryDelay);
                }
            }
        }
    }
}
