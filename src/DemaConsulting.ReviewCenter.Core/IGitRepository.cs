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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     What a repository session needs to reach a registered repository: where it is and, when it is private,
///     the credentials.
/// </summary>
/// <remarks>
///     The record holds the decrypted token, so it stays inside the server-side code that talks to git: it is
///     created by <see cref="RepositoryService.GetAccessAsync"/> and consumed by
///     <see cref="IGitRepositoryProvider"/>. It never reaches a page. <see cref="ToString"/> names only the
///     repository, so the token cannot end up in a log or an exception message by accident.
/// </remarks>
/// <param name="RepositoryId">The identity of the repository in the registry.</param>
/// <param name="Name">The name of the repository, for messages.</param>
/// <param name="Location">The location git reads from.</param>
/// <param name="Username">The user name, or <c>null</c> for anonymous access.</param>
/// <param name="Token">The decrypted token, or <c>null</c> for anonymous access.</param>
public sealed record RepositoryAccess(int RepositoryId, string Name, string Location, string? Username, string? Token)
{
    /// <summary>
    ///     Returns a short description that never includes the token.
    /// </summary>
    /// <returns>The text <c>Repository {id}</c>.</returns>
    public override string ToString() => $"Repository {RepositoryId}";
}

/// <summary>
///     The kind of change a file has between two revisions.
/// </summary>
public enum ChangeKind
{
    /// <summary>The file exists only in the head revision.</summary>
    Added,

    /// <summary>The file exists in both revisions with different content.</summary>
    Modified,

    /// <summary>The file exists only in the baseline revision.</summary>
    Deleted
}

/// <summary>
///     One file that differs between two revisions.
/// </summary>
/// <remarks>
///     A rename is reported as a deletion of the old path and an addition of the new one, so the review lists
///     both. The record is immutable and thread-safe.
/// </remarks>
/// <param name="Path">The repository path, using <c>/</c> as the separator.</param>
/// <param name="Kind">The kind of change.</param>
/// <param name="BaselineBlobSha">The blob identifier in the baseline revision, or <c>null</c> for an added file.</param>
/// <param name="HeadBlobSha">The blob identifier in the head revision, or <c>null</c> for a deleted file.</param>
public sealed record GitChange(string Path, ChangeKind Kind, string? BaselineBlobSha, string? HeadBlobSha);

/// <summary>
///     An entry that was left out of a change list, with the reason.
/// </summary>
/// <param name="Path">The repository path of the entry.</param>
/// <param name="Reason">The operator-readable reason, for example that symbolic links are not reviewed.</param>
public sealed record SkippedEntry(string Path, string Reason);

/// <summary>
///     The result of listing the changes between two revisions.
/// </summary>
/// <param name="Changes">The files that differ, ordered by path.</param>
/// <param name="Skipped">The entries left out (symbolic links, submodules and unusable paths), ordered by path.</param>
public sealed record GitChangeSet(IReadOnlyList<GitChange> Changes, IReadOnlyList<SkippedEntry> Skipped);

/// <summary>
///     Opens a session on a repository so its revisions and files can be read.
/// </summary>
/// <remarks>
///     The contract lets Core and the pages work with a fake in tests; the content sources subsystem implements
///     it by running git. Implementations must be thread-safe.
/// </remarks>
public interface IGitRepositoryProvider
{
    /// <summary>
    ///     Makes the repository ready (clones or updates the local mirror) and opens a session on it.
    /// </summary>
    /// <param name="access">Where the repository is and how to read it. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The session; the caller must dispose it.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.GitUnavailable"/>, <see cref="DomainRule.GitAccessFailed"/> or
    ///     <see cref="DomainRule.GitTimeout"/> when the repository cannot be made ready.
    /// </exception>
    Task<IGitRepository> OpenAsync(RepositoryAccess access, CancellationToken cancellationToken = default);
}

/// <summary>
///     A session on one repository: resolves revisions and reads changes and file contents.
/// </summary>
/// <remarks>
///     While a session is open no other session on the same repository runs, so the mirror cannot change
///     under it. Dispose the session as soon as the work is done.
/// </remarks>
public interface IGitRepository : IAsyncDisposable
{
    /// <summary>
    ///     Resolves a revision to the commit it names.
    /// </summary>
    /// <param name="revision">A revision accepted by <see cref="RevisionValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The commit identifier as lowercase hexadecimal.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.RevisionInvalid"/> or <see cref="DomainRule.RevisionNotFound"/>.
    /// </exception>
    Task<string> ResolveRevisionAsync(string revision, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Decides whether one commit is an ancestor of another (or the same commit).
    /// </summary>
    /// <remarks>
    ///     The planner uses this to decide whether the head of the last signed review can serve as the baseline of a
    ///     new review: after a force-push or on another branch it cannot.
    /// </remarks>
    /// <param name="ancestorSha">The commit that may be the ancestor, as a full lowercase hexadecimal identifier.</param>
    /// <param name="descendantSha">The commit that may descend from it, as a full lowercase hexadecimal identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when <paramref name="ancestorSha"/> is reachable from <paramref name="descendantSha"/> (or equal to it).</returns>
    /// <exception cref="ArgumentException">Thrown when an identifier is not a full lowercase hexadecimal object identifier.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.GitAccessFailed"/> or <see cref="DomainRule.GitTimeout"/> when git cannot
    ///     answer, for example because a commit is unknown to the mirror.
    /// </exception>
    Task<bool> IsAncestorAsync(string ancestorSha, string descendantSha, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the files that differ between two commits, or all files of a commit.
    /// </summary>
    /// <param name="baselineSha">The baseline commit, or <c>null</c> to list every file of the head commit as added.</param>
    /// <param name="headSha">The head commit.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The changes and the entries that were skipped.</returns>
    Task<GitChangeSet> ListChangesAsync(string? baselineSha, string headSha, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads the size of a blob without reading its content.
    /// </summary>
    /// <param name="blobSha">The blob identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The size in bytes.</returns>
    Task<long> GetBlobSizeAsync(string blobSha, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads the exact bytes of a blob.
    /// </summary>
    /// <param name="blobSha">The blob identifier.</param>
    /// <param name="maxBytes">The most bytes to read; a larger blob is an error, not a truncation.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The bytes, as stored in the repository (no line-ending or filter conversion).</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.GitAccessFailed"/> when the blob is larger than <paramref name="maxBytes"/>.</exception>
    Task<byte[]> ReadBlobAsync(string blobSha, long maxBytes, CancellationToken cancellationToken = default);
}
