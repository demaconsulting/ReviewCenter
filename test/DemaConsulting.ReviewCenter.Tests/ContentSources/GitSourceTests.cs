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

using System.Text;
using DemaConsulting.ReviewCenter.ContentSources;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.ContentSources;

/// <summary>
///     Tests for <see cref="GitSource"/> against real temporary git repositories: mirror clone and fetch, revision
///     resolution, change lists, blob reads and the clear errors.
/// </summary>
public class GitSourceTests
{
    /// <summary>
    ///     The text of the file that changes between the two commits.
    /// </summary>
    private const string FirstText = "first\n";

    /// <summary>
    ///     A path with spaces and a non-ASCII character.
    /// </summary>
    private const string UnicodePath = "added file \u00fc.txt";

    /// <summary>
    ///     A repository with two commits that exercise every kind of change.
    /// </summary>
    /// <param name="Folder">The working folder of the origin repository.</param>
    /// <param name="First">The first commit.</param>
    /// <param name="Second">The second commit.</param>
    private sealed record Origin(string Folder, string First, string Second);

    /// <summary>
    ///     Creates a source whose mirrors live in a folder.
    /// </summary>
    /// <param name="cache">The cache folder.</param>
    /// <returns>The source.</returns>
    private static GitSource NewSource(string cache)
    {
        var options = new GitOptions { CachePath = cache, TimeoutSeconds = 60 };
        return new GitSource(new GitRunner(options), options);
    }

    /// <summary>
    ///     Creates access to a local origin repository.
    /// </summary>
    /// <param name="location">The location of the origin.</param>
    /// <param name="id">The repository identity.</param>
    /// <returns>The access.</returns>
    private static RepositoryAccess Access(string location, int id = 1) =>
        new(id, "origin", location, null, null);

    /// <summary>
    ///     Creates an origin repository with a modification, an addition, a deletion, a rename, a symbolic link, a
    ///     submodule entry and a mode-only change between its two commits.
    /// </summary>
    /// <param name="root">The folder under which the origin is created.</param>
    /// <returns>The origin.</returns>
    private static Origin CreateOrigin(string root)
    {
        var folder = Path.Combine(root, "origin");
        Directory.CreateDirectory(folder);
        GitTestSupport.Init(folder);
        GitTestSupport.Write(folder, "a.txt", FirstText);
        GitTestSupport.Write(folder, "gone.txt", "gone\n");
        GitTestSupport.Write(folder, "old/name.txt", "a file that moves and keeps its content\n");
        GitTestSupport.Write(folder, "script.sh", "echo hi\n");
        var first = GitTestSupport.Commit(folder, "first");
        GitTestSupport.Git(folder, "tag", "v1");

        GitTestSupport.Write(folder, "a.txt", "second\n");
        GitTestSupport.Write(folder, UnicodePath, "unicode\n");
        File.Delete(Path.Combine(folder, "gone.txt"));
        Directory.CreateDirectory(Path.Combine(folder, "new"));
        File.Move(Path.Combine(folder, "old", "name.txt"), Path.Combine(folder, "new", "name.txt"));
        GitTestSupport.Git(folder, "add", "-A");
        GitTestSupport.Git(folder, "update-index", "--chmod=+x", "script.sh");
        var blob = HashText(folder, "a.txt");
        GitTestSupport.Git(folder, "update-index", "--add", "--cacheinfo", $"120000,{blob},link");
        GitTestSupport.Git(folder, "update-index", "--add", "--cacheinfo", $"160000,{first},sub");
        GitTestSupport.Git(folder, "commit", "-m", "second");
        var second = GitTestSupport.Git(folder, "rev-parse", "HEAD");
        return new Origin(folder, first, second);
    }

    /// <summary>
    ///     Returns the blob identifier of a file in the working folder.
    /// </summary>
    /// <param name="folder">The working folder.</param>
    /// <param name="relativePath">The path of the file.</param>
    /// <returns>The blob identifier.</returns>
    private static string HashText(string folder, string relativePath) =>
        GitTestSupport.Git(folder, "hash-object", "-w", relativePath);

    /// <summary>
    ///     Opens a session and disposes it at once, which makes or updates the mirror.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="access">The access.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the session is disposed.</returns>
    private static async Task OpenAndCloseAsync(GitSource source, RepositoryAccess access, CancellationToken cancellationToken)
    {
        var session = await source.OpenAsync(access, cancellationToken);
        await session.DisposeAsync();
    }

    /// <summary>
    ///     Test that revisions (branch, tag, full id) resolve to commit ids, and that unknown and unsafe revisions are
    ///     refused with the right rule.
    /// </summary>
    [Fact]
    public async Task GitSource_ResolveRevision_BranchTagAndId_ResolveAndBadOnesAreRefused()
    {
        // Arrange: an origin with a tag and a mirror of it
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var origin = CreateOrigin(root.Path);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var session = await NewSource(Path.Combine(root.Path, "cache")).OpenAsync(Access(origin.Folder), cancellationToken);

        // Act: resolve a branch, a tag, a full id and bad revisions
        var branch = await session.ResolveRevisionAsync("main", cancellationToken);
        var tag = await session.ResolveRevisionAsync("v1", cancellationToken);
        var id = await session.ResolveRevisionAsync(origin.First, cancellationToken);
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(() => session.ResolveRevisionAsync("no-such-branch", cancellationToken));
        var unsafeRevision = await Assert.ThrowsAsync<DomainRuleException>(() => session.ResolveRevisionAsync("--upload-pack=touch x", cancellationToken));
        var option = await Assert.ThrowsAsync<DomainRuleException>(() => session.ResolveRevisionAsync("-x", cancellationToken));

        // Assert: commits are resolved and the failures name their rules
        Assert.Equal(origin.Second, branch);
        Assert.Equal(origin.First, tag);
        Assert.Equal(origin.First, id);
        Assert.Equal(DomainRule.RevisionNotFound, unknown.Rule);
        Assert.Equal(DomainRule.RevisionInvalid, unsafeRevision.Rule);
        Assert.Equal(DomainRule.RevisionInvalid, option.Rule);
    }

    /// <summary>
    ///     Test that the change list between two commits has modifications, additions and deletions, reports a rename
    ///     as a delete and an add, skips links and submodules with a reason and ignores a mode-only change.
    /// </summary>
    [Fact]
    public async Task GitSource_ListChanges_BetweenCommits_ListsChangesAndSkipsSpecialEntries()
    {
        // Arrange: an origin with every kind of change
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var origin = CreateOrigin(root.Path);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var session = await NewSource(Path.Combine(root.Path, "cache")).OpenAsync(Access(origin.Folder), cancellationToken);

        // Act: list the changes
        var result = await session.ListChangesAsync(origin.First, origin.Second, cancellationToken);

        // Assert: ordered by ordinal path, with the expected kinds
        var summary = result.Changes.Select(change => $"{change.Kind}:{change.Path}").ToArray();
        Assert.Equal(
            [
                "Modified:a.txt",
                $"Added:{UnicodePath}",
                "Deleted:gone.txt",
                "Added:new/name.txt",
                "Deleted:old/name.txt"
            ],
            summary);
        var modified = result.Changes.Single(change => change.Path == "a.txt");
        Assert.NotNull(modified.BaselineBlobSha);
        Assert.NotNull(modified.HeadBlobSha);
        Assert.Null(result.Changes.Single(change => change.Path == "gone.txt").HeadBlobSha);
        Assert.Null(result.Changes.Single(change => change.Path == UnicodePath).BaselineBlobSha);
        Assert.DoesNotContain(result.Changes, change => change.Path == "script.sh");
        Assert.Equal(["link", "sub"], result.Skipped.Select(entry => entry.Path).ToArray());
        Assert.All(result.Skipped, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Reason)));
    }

    /// <summary>
    ///     Test that without a baseline every file of the head commit is listed as added.
    /// </summary>
    [Fact]
    public async Task GitSource_ListChanges_NoBaseline_ListsEveryFileAsAdded()
    {
        // Arrange: an origin and a session
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var origin = CreateOrigin(root.Path);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var session = await NewSource(Path.Combine(root.Path, "cache")).OpenAsync(Access(origin.Folder), cancellationToken);

        // Act: list the head commit alone
        var result = await session.ListChangesAsync(null, origin.Second, cancellationToken);

        // Assert: all regular files are added, links and submodules are skipped
        Assert.All(result.Changes, change => Assert.Equal(ChangeKind.Added, change.Kind));
        Assert.Equal(
            ["a.txt", UnicodePath, "new/name.txt", "script.sh"],
            result.Changes.Select(change => change.Path).ToArray());
        Assert.Equal(["link", "sub"], result.Skipped.Select(entry => entry.Path).ToArray());
    }

    /// <summary>
    ///     Test that blob sizes and exact bytes are read, and that a blob over the limit is an error, not a truncation.
    /// </summary>
    [Fact]
    public async Task GitSource_ReadBlob_ReadsExactBytesAndEnforcesTheLimit()
    {
        // Arrange: an origin with a file that has CRLF line ends and a non-UTF-8 byte
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var origin = CreateOrigin(root.Path);
        var bytes = new byte[] { 0x61, 0x0D, 0x0A, 0xFF, 0x00, 0x62 };
        await File.WriteAllBytesAsync(Path.Combine(origin.Folder, "raw.bin"), bytes, TestContext.Current.CancellationToken);
        var third = GitTestSupport.Commit(origin.Folder, "third");
        var blobSha = GitTestSupport.Git(origin.Folder, "rev-parse", "HEAD:raw.bin");
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var session = await NewSource(Path.Combine(root.Path, "cache")).OpenAsync(Access(origin.Folder), cancellationToken);

        // Act: read the size, the bytes, and with a limit that is too small
        var size = await session.GetBlobSizeAsync(blobSha, cancellationToken);
        var content = await session.ReadBlobAsync(blobSha, 1024, cancellationToken);
        var tooLarge = await Assert.ThrowsAsync<DomainRuleException>(() => session.ReadBlobAsync(blobSha, 3, cancellationToken));
        var changes = await session.ListChangesAsync(origin.Second, third, cancellationToken);

        // Assert: exact bytes, the size, the named rule and the blob id of the change
        Assert.Equal(bytes.Length, size);
        Assert.Equal(bytes, content);
        Assert.Equal(DomainRule.GitAccessFailed, tooLarge.Rule);
        Assert.Equal(blobSha, Assert.Single(changes.Changes).HeadBlobSha);
    }

    /// <summary>
    ///     Test that blob identifiers that are not object ids are refused before git runs.
    /// </summary>
    [Theory]
    [InlineData("--help")]
    [InlineData("HEAD:a.txt")]
    [InlineData("abc")]
    // cspell:disable-next-line
    [InlineData("ABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCD")]
    public async Task GitSource_BlobAndCommitIds_NotObjectIds_ThrowArgumentException(string value)
    {
        // Arrange: a session
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var origin = CreateOrigin(root.Path);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var session = await NewSource(Path.Combine(root.Path, "cache")).OpenAsync(Access(origin.Folder), cancellationToken);

        // Act and Assert: every method refuses the value
        await Assert.ThrowsAsync<ArgumentException>(() => session.GetBlobSizeAsync(value, cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => session.ReadBlobAsync(value, 10, cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => session.ListChangesAsync(null, value, cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => session.ListChangesAsync(value, origin.Second, cancellationToken));
    }

    /// <summary>
    ///     Test that opening again fetches new commits and prunes deleted branches.
    /// </summary>
    [Fact]
    public async Task GitSource_OpenAgain_FetchesNewCommitsAndPrunesDeletedBranches()
    {
        // Arrange: an origin with a feature branch, mirrored once
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var origin = CreateOrigin(root.Path);
        GitTestSupport.Git(origin.Folder, "branch", "feature");
        var source = NewSource(Path.Combine(root.Path, "cache"));
        var cancellationToken = TestContext.Current.CancellationToken;
        await OpenAndCloseAsync(source, Access(origin.Folder), cancellationToken);

        GitTestSupport.Write(origin.Folder, "later.txt", "later\n");
        var third = GitTestSupport.Commit(origin.Folder, "third");
        GitTestSupport.Git(origin.Folder, "branch", "-D", "feature");

        // Act: open again
        await using var session = await source.OpenAsync(Access(origin.Folder), cancellationToken);
        var head = await session.ResolveRevisionAsync("main", cancellationToken);
        var pruned = await Assert.ThrowsAsync<DomainRuleException>(() => session.ResolveRevisionAsync("feature", cancellationToken));

        // Assert: the new commit is visible and the deleted branch is gone
        Assert.Equal(third, head);
        Assert.Equal(DomainRule.RevisionNotFound, pruned.Rule);
    }

    /// <summary>
    ///     Test that a changed location makes a fresh mirror instead of mixing two repositories.
    /// </summary>
    [Fact]
    public async Task GitSource_LocationChanged_ClonesAgain()
    {
        // Arrange: two different origins and a mirror of the first under repository 1
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var first = CreateOrigin(root.Path);
        using var otherRoot = new TempFolder();
        var other = Path.Combine(otherRoot.Path, "other");
        Directory.CreateDirectory(other);
        GitTestSupport.Init(other);
        GitTestSupport.Write(other, "x.txt", "x\n");
        var otherCommit = GitTestSupport.Commit(other, "other");
        var source = NewSource(Path.Combine(root.Path, "cache"));
        var cancellationToken = TestContext.Current.CancellationToken;
        await OpenAndCloseAsync(source, Access(first.Folder), cancellationToken);

        // Act: open repository 1 at the other location
        await using var session = await source.OpenAsync(Access(other), cancellationToken);
        var resolved = await session.ResolveRevisionAsync("main", cancellationToken);
        var oldCommit = await Assert.ThrowsAsync<DomainRuleException>(() => session.ResolveRevisionAsync(first.First, cancellationToken));

        // Assert: only the new location's history is present
        Assert.Equal(otherCommit, resolved);
        Assert.Equal(DomainRule.RevisionNotFound, oldCommit.Rule);
    }

    /// <summary>
    ///     Test that a second session on the same repository waits until the first is disposed.
    /// </summary>
    [Fact]
    public async Task GitSource_SecondSessionOnSameRepository_WaitsForTheFirst()
    {
        // Arrange: an origin and an open session
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var origin = CreateOrigin(root.Path);
        var source = NewSource(Path.Combine(root.Path, "cache"));
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await source.OpenAsync(Access(origin.Folder), cancellationToken);

        // Act: start a second open and give it time to (wrongly) finish
        var second = source.OpenAsync(Access(origin.Folder), cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(700), cancellationToken);
        var waitedWhileOpen = !second.IsCompleted;
        await first.DisposeAsync();
        var session = await second.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
        await session.DisposeAsync();

        // Assert: it waited, then completed
        Assert.True(waitedWhileOpen);
    }

    /// <summary>
    ///     Test that two different repositories can be open at the same time.
    /// </summary>
    [Fact]
    public async Task GitSource_DifferentRepositories_OpenTogether()
    {
        // Arrange: an origin used for two registry entries
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var origin = CreateOrigin(root.Path);
        var source = NewSource(Path.Combine(root.Path, "cache"));
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: open both without disposing the first
        await using var one = await source.OpenAsync(Access(origin.Folder, 1), cancellationToken);
        var two = await source.OpenAsync(Access(origin.Folder, 2), cancellationToken).WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
        await using var secondSession = two;

        // Assert: both resolve the same commit
        Assert.Equal(
            await one.ResolveRevisionAsync("main", cancellationToken),
            await two.ResolveRevisionAsync("main", cancellationToken));
    }

    /// <summary>
    ///     Test that a location that does not exist gives a clear access error and leaves no half-made mirror behind.
    /// </summary>
    [Fact]
    public async Task GitSource_Open_LocationMissing_ThrowsGitAccessFailedAndCleansUp()
    {
        // Arrange: a location that does not exist
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var cache = Path.Combine(root.Path, "cache");
        var missing = Path.Combine(root.Path, "nothing-here");

        // Act: open it
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => NewSource(cache).OpenAsync(Access(missing), TestContext.Current.CancellationToken));

        // Assert: the rule is named, the repository is named and nothing is left behind
        Assert.Equal(DomainRule.GitAccessFailed, exception.Rule);
        Assert.Contains("'origin'", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(cache, "repo-1")));
        Assert.False(Directory.Exists(Path.Combine(cache, "repo-1.tmp")));
    }

    /// <summary>
    ///     Test that the mirror's configuration never holds the token, even when access carries one.
    /// </summary>
    [Fact]
    public async Task GitSource_Mirror_NeverStoresTheToken()
    {
        // Arrange: access to a local origin that carries credentials (a local clone ignores them)
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var origin = CreateOrigin(root.Path);
        var cache = Path.Combine(root.Path, "cache");
        var access = new RepositoryAccess(1, "origin", origin.Folder, "svc", "tkn-Never.Stored");
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: mirror the repository twice (clone, then fetch)
        await OpenAndCloseAsync(NewSource(cache), access, cancellationToken);
        await OpenAndCloseAsync(NewSource(cache), access, cancellationToken);

        // Assert: no file of the mirror's top level contains the token
        var mirror = Path.Combine(cache, "repo-1");
        foreach (var file in Directory.EnumerateFiles(mirror, "*", SearchOption.TopDirectoryOnly))
        {
            var text = await File.ReadAllTextAsync(file, Encoding.Latin1, cancellationToken);
            Assert.DoesNotContain("tkn-Never.Stored", text, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes("svc:tkn-Never.Stored")), text, StringComparison.Ordinal);
        }
    }
}
