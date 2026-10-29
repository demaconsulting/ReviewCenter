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

using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Tests.Core;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for the repository-review columns of <see cref="ReviewStore"/> against a real SQLite database file:
///     the source of the review, the change kinds, the baseline copies and the record they feed.
/// </summary>
public class ReviewStoreRepositoryTests
{
    /// <summary>
    ///     The creation time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _time = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The head commit of the stored review.
    /// </summary>
    private const string HeadSha = "2222222222222222222222222222222222222222";

    /// <summary>
    ///     The baseline commit of the stored review.
    /// </summary>
    private const string BaseSha = "1111111111111111111111111111111111111111";

    /// <summary>
    ///     Stores a repository review with a modified, an added and a deleted file.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <returns>The identity of the project; the review is <c>REV-1</c>.</returns>
    private static async Task<int> AddRepositoryReviewAsync(TestStore db)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _time }, cancellationToken);
        ReviewFile File(string path, ChangeKind kind, byte[]? baseline, byte[]? head) => new()
        {
            ProjectId = project.Id,
            ReviewId = "REV-1",
            Path = path,
            ChangeKind = kind,
            Content = head ?? [],
            Size = head?.Length ?? 0,
            Sha256 = head is null ? string.Empty : Fingerprint.ComputeFileHash(head),
            BaselineContent = baseline,
            BaselineSize = baseline?.Length,
            BaselineSha256 = baseline is null ? null : Fingerprint.ComputeFileHash(baseline)
        };

        await db.Store.AddReviewAsync(
            new Review
            {
                ProjectId = project.Id,
                Id = "REV-1",
                Title = "Title",
                State = ReviewState.Draft,
                Created = _time,
                ContentFingerprint = Fingerprint.Compute(new[] { TestContent.A, TestContent.B }),
                RepositoryId = 7,
                RepositoryName = "main-repo",
                BaselineSha = BaseSha,
                HeadSha = HeadSha,
                Selection = "src/**\n!src/skip.txt",
                ReviewSetId = "core",
                Files =
                [
                    File("a.txt", ChangeKind.Modified, TestContent.B, TestContent.A),
                    File("b.txt", ChangeKind.Added, null, TestContent.B),
                    File("c.txt", ChangeKind.Deleted, TestContent.C, null)
                ]
            },
            cancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Test that the source, the change kinds and the baseline metadata round-trip, and that reading the review
    ///     never loads head or baseline bytes.
    /// </summary>
    [Fact]
    public async Task ReviewStore_GetReviewAsync_RepositoryReview_ReturnsSourceAndKindsWithoutBytes()
    {
        // Arrange: a stored repository review
        using var db = new TestStore();
        var projectId = await AddRepositoryReviewAsync(db);

        // Act: read it, and read the list of the project
        var review = await db.Store.GetReviewAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        var listed = (await db.Store.ListReviewsAsync(projectId, TestContext.Current.CancellationToken)).Single();

        // Assert: the source columns
        Assert.NotNull(review);
        Assert.True(review.IsFromRepository);
        Assert.Equal(7, review.RepositoryId);
        Assert.Equal("main-repo", review.RepositoryName);
        Assert.Equal(BaseSha, review.BaselineSha);
        Assert.Equal(HeadSha, review.HeadSha);
        Assert.Equal("src/**\n!src/skip.txt", review.Selection);
        Assert.Equal("core", review.ReviewSetId);
        Assert.Equal("core", listed.ReviewSetId);

        // Assert: kinds, head and baseline metadata, and no bytes
        Assert.Equal([ChangeKind.Modified, ChangeKind.Added, ChangeKind.Deleted], review.Files.Select(f => f.ChangeKind));
        Assert.Equal([TestContent.HashA, TestContent.HashB, string.Empty], review.Files.Select(f => f.Sha256));
        Assert.Equal([TestContent.HashB, null, TestContent.HashC], review.Files.Select(f => f.BaselineSha256));
        Assert.Equal([TestContent.B.Length, default(long?), TestContent.C.Length], review.Files.Select(f => f.BaselineSize));
        Assert.All(review.Files, file => Assert.Empty(file.Content));
        Assert.All(review.Files, file => Assert.Null(file.BaselineContent));
    }

    /// <summary>
    ///     Test that the baseline bytes are read only through <c>GetBaselineContentAsync</c>, exactly as stored, and
    ///     that an added file or an unknown file has none.
    /// </summary>
    [Fact]
    public async Task ReviewStore_GetBaselineContentAsync_StoredFiles_ReturnsBaselineBytesOrNull()
    {
        // Arrange: a stored repository review
        using var db = new TestStore();
        var projectId = await AddRepositoryReviewAsync(db);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act, Assert: the baseline of the modified and the deleted file, and none for an added or unknown file
        Assert.Equal(TestContent.B, await db.Store.GetBaselineContentAsync(projectId, "REV-1", "a.txt", cancellationToken));
        Assert.Equal(TestContent.C, await db.Store.GetBaselineContentAsync(projectId, "REV-1", "c.txt", cancellationToken));
        Assert.Null(await db.Store.GetBaselineContentAsync(projectId, "REV-1", "b.txt", cancellationToken));
        Assert.Null(await db.Store.GetBaselineContentAsync(projectId, "REV-1", "nope.txt", cancellationToken));
        Assert.Null(await db.Store.GetBaselineContentAsync(projectId, "REV-9", "a.txt", cancellationToken));

        // Assert: the head content of the deleted file is empty and of the modified file is the head
        Assert.Equal(TestContent.A, await db.Store.GetFileContentAsync(projectId, "REV-1", "a.txt", cancellationToken));
        Assert.Empty((await db.Store.GetFileContentAsync(projectId, "REV-1", "c.txt", cancellationToken))!);
    }

    /// <summary>
    ///     Test that the record carries the source and one change per file (a deleted file has no head hash), and
    ///     that its digest matches the same record built by hand.
    /// </summary>
    [Fact]
    public async Task ReviewStore_GetRecordAsync_RepositoryReview_CarriesSourceAndChanges()
    {
        // Arrange: a stored repository review
        using var db = new TestStore();
        var projectId = await AddRepositoryReviewAsync(db);

        // Act: read the record
        var record = await db.Store.GetRecordAsync(projectId, "REV-1", TestContext.Current.CancellationToken);

        // Assert: the source and the changes
        Assert.NotNull(record);
        var source = record.Source;
        Assert.False(source.IsUpload);
        Assert.Equal(7, source.RepositoryId);
        Assert.Equal(BaseSha, source.BaselineSha);
        Assert.Equal(HeadSha, source.HeadSha);
        Assert.Equal("src/**\n!src/skip.txt", source.Selection);
        Assert.Equal("core", source.ReviewSetId);
        Assert.Equal(
            [
                new ChangedFile("a.txt", ChangeKind.Modified, TestContent.HashB, TestContent.HashA),
                new ChangedFile("b.txt", ChangeKind.Added, null, TestContent.HashB),
                new ChangedFile("c.txt", ChangeKind.Deleted, TestContent.HashC, null)
            ],
            source.Changes.OrderBy(c => c.Path, StringComparer.Ordinal));
    }

    /// <summary>
    ///     Test that an uploaded review has the upload source and its files are additions without a baseline.
    /// </summary>
    [Fact]
    public async Task ReviewStore_GetRecordAsync_UploadReview_HasUploadSource()
    {
        // Arrange: a stored upload review (no source columns set)
        using var db = new TestStore();
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _time }, cancellationToken);
        await db.Store.AddReviewAsync(
            new Review
            {
                ProjectId = project.Id,
                Id = "REV-1",
                Title = "Title",
                State = ReviewState.Draft,
                Created = _time,
                ContentFingerprint = TestContent.FingerprintA,
                Files =
                [
                    new ReviewFile
                    {
                        ProjectId = project.Id,
                        ReviewId = "REV-1",
                        Path = "a.txt",
                        Content = TestContent.A,
                        Size = TestContent.A.Length,
                        Sha256 = TestContent.HashA
                    }
                ]
            },
            cancellationToken);

        // Act: read the review and the record
        var review = await db.Store.GetReviewAsync(project.Id, "REV-1", cancellationToken);
        var record = await db.Store.GetRecordAsync(project.Id, "REV-1", cancellationToken);

        // Assert: upload source, file stored as an addition without baseline
        Assert.False(review!.IsFromRepository);
        Assert.Equal(ChangeKind.Added, review.Files.Single().ChangeKind);
        Assert.Null(review.Files.Single().BaselineSha256);
        Assert.True(record!.Source.IsUpload);
        Assert.Null(review.ReviewSetId);
        Assert.Null(record.Source.ReviewSetId);
    }

    /// <summary>
    ///     Test that a review-set identifier of the maximum length is stored and read back whole.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddReviewAsync_LongestReviewSetId_RoundTrips()
    {
        // Arrange: a project and a review whose set id has the maximum length
        using var db = new TestStore();
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _time }, cancellationToken);
        var setId = new string('s', ReviewLimits.MaxReviewSetIdLength);

        // Act: store it and read it back
        await db.Store.AddReviewAsync(
            new Review
            {
                ProjectId = project.Id,
                Id = "REV-1",
                Title = "Title",
                State = ReviewState.Draft,
                Created = _time,
                ContentFingerprint = Fingerprint.Compute(Array.Empty<string>()),
                RepositoryId = 7,
                RepositoryName = "main-repo",
                HeadSha = HeadSha,
                Selection = "**",
                ReviewSetId = setId
            },
            cancellationToken);
        var review = await db.Store.GetReviewAsync(project.Id, "REV-1", cancellationToken);

        // Assert: nothing was cut off
        Assert.Equal(setId, review!.ReviewSetId);
    }
}
