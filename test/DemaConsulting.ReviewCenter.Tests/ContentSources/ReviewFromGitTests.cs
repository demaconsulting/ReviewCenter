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
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Tests.Storage;

namespace DemaConsulting.ReviewCenter.Tests.ContentSources;

/// <summary>
///     End-to-end tests of creating a review from a real temporary git repository into a real database: the
///     changes found, the frozen copies and the fact that a later change in the repository does not touch them.
/// </summary>
public class ReviewFromGitTests
{
    /// <summary>
    ///     The acting user.
    /// </summary>
    private static readonly Actor _actor = new("adm", "Adm");

    /// <summary>
    ///     A provider that reads the origin folder instead of the registered location, so the test does not
    ///     depend on how a local path is written as a repository location.
    /// </summary>
    /// <param name="inner">The real git source.</param>
    /// <param name="folder">The origin folder.</param>
    private sealed class OriginProvider(IGitRepositoryProvider inner, string folder) : IGitRepositoryProvider
    {
        /// <inheritdoc />
        public Task<IGitRepository> OpenAsync(RepositoryAccess access, CancellationToken cancellationToken = default)
        {
            return inner.OpenAsync(access with { Location = folder }, cancellationToken);
        }
    }

    /// <summary>
    ///     Test that the review freezes the changes between a tag and a branch, and that a later commit in the
    ///     repository changes neither the review, nor its content, nor its record digest.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_RealGit_FreezesChangesAgainstLaterCommits()
    {
        // Arrange: an origin with a tag, a second commit (modify, add, delete and a symbolic link) and a database
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var origin = Path.Combine(root.Path, "origin");
        Directory.CreateDirectory(origin);
        GitTestSupport.Init(origin);
        GitTestSupport.Write(origin, "a.txt", "first\n");
        GitTestSupport.Write(origin, "gone.txt", "gone\n");
        var first = GitTestSupport.Commit(origin, "first");
        GitTestSupport.Git(origin, "tag", "v1");
        GitTestSupport.Write(origin, "a.txt", "second\n");
        GitTestSupport.Write(origin, "b.txt", "added\n");
        File.Delete(Path.Combine(origin, "gone.txt"));
        GitTestSupport.Git(origin, "add", "-A");
        var blob = GitTestSupport.Git(origin, "hash-object", "a.txt");
        GitTestSupport.Git(origin, "update-index", "--add", "--cacheinfo", $"120000,{blob},link");
        GitTestSupport.Git(origin, "commit", "-m", "second");
        var second = GitTestSupport.Git(origin, "rev-parse", "HEAD");

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = TimeProvider.System;
        using var db = new TestStore(fastCommits: true);
        var project = await TestServices.Projects(db.Store, clock).CreateAsync("Apollo", null, cancellationToken);
        var repositories = TestServices.Repositories(db.Store, clock);
        var repository = await repositories.CreateAsync(
            project.Id, "origin", "https://example.com/origin.git", null, null, _actor, cancellationToken);
        var options = new GitOptions { CachePath = Path.Combine(root.Path, "cache"), TimeoutSeconds = 60 };
        var provider = new OriginProvider(new GitSource(new GitRunner(options), options), origin);
        var service = TestServices.Reviews(db.Store, clock, repositories, provider);

        // Act: create the review from the tag to the branch, record its digest, then commit more in the origin
        var result = await service.CreateFromRepositoryAsync(
            project.Id, "rev-1", "Real", repository.Id, "main", "v1", "**/*", _actor, cancellationToken);
        var digestBefore = RecordDigest.Compute((await db.Store.GetRecordAsync(project.Id, "rev-1", cancellationToken))!);
        GitTestSupport.Write(origin, "a.txt", "third\n");
        GitTestSupport.Write(origin, "c.txt", "later\n");
        GitTestSupport.Commit(origin, "third");
        var review = await service.GetAsync(project.Id, "rev-1", cancellationToken);
        var head = await service.GetFileContentAsync(project.Id, "rev-1", "a.txt", cancellationToken);
        var baseline = await service.GetBaselineContentAsync(project.Id, "rev-1", "a.txt", cancellationToken);
        var digestAfter = RecordDigest.Compute((await db.Store.GetRecordAsync(project.Id, "rev-1", cancellationToken))!);

        // Assert: the changes, their kinds and the skipped link
        Assert.Equal(first, review!.BaselineSha);
        Assert.Equal(second, review.HeadSha);
        Assert.Equal(
            [("a.txt", ChangeKind.Modified), ("b.txt", ChangeKind.Added), ("gone.txt", ChangeKind.Deleted)],
            review.Files.Select(file => (file.Path, file.ChangeKind)));
        Assert.Equal("link", Assert.Single(result.Skipped).Path);

        // Assert: the frozen copies are those of the two commits, not of the later one
        Assert.Equal("second\n", Encoding.UTF8.GetString(head!));
        Assert.Equal("first\n", Encoding.UTF8.GetString(baseline!));
        Assert.Equal("gone\n", Encoding.UTF8.GetString((await service.GetBaselineContentAsync(project.Id, "rev-1", "gone.txt", cancellationToken))!));
        Assert.Equal(digestBefore, digestAfter);

        // Assert: a review made now sees the later commit, so the unchanged result above is not an accident
        var later = await service.CreateFromRepositoryAsync(
            project.Id, "rev-2", "Later", repository.Id, "main", "v1", "**/*", _actor, cancellationToken);
        Assert.Contains(later.Review.Files, file => file.Path == "c.txt");
        Assert.NotEqual(second, later.Review.HeadSha);
    }
}
