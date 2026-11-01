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
using Microsoft.EntityFrameworkCore;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for check-anchored threads in <see cref="ReviewStore"/> and <see cref="ReviewCenterDbContext"/> against a
///     real SQLite database file: the nullable check column, a check thread that needs no file row, the anchor rules
///     the store enforces now that a thread is tied to its review rather than to a file, the cascade from the review
///     and the open-thread rule of the final signature for a check thread.
/// </summary>
public class ReviewStoreCheckThreadTests
{
    /// <summary>
    ///     The time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _time = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Adds a project with the review <c>REV-1</c> of the file <c>a.txt</c>.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <returns>The identity of the project.</returns>
    private static async Task<int> AddReviewAsync(TestStore db)
    {
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
        return project.Id;
    }

    /// <summary>
    ///     Builds an open thread of <c>REV-1</c> with one chat comment.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="filePath">The file path of the anchor (empty for a check thread).</param>
    /// <param name="checkItemId">The check of the anchor, or <c>null</c> for a file thread.</param>
    /// <param name="startLine">The first line, or <c>null</c>.</param>
    /// <returns>The thread, ready to add.</returns>
    private static ReviewThread NewThread(int projectId, string filePath, string? checkItemId, int? startLine = null)
    {
        return new ReviewThread
        {
            ProjectId = projectId,
            ReviewId = "REV-1",
            FilePath = filePath,
            CheckItemId = checkItemId,
            StartLine = startLine,
            EndLine = startLine,
            State = ReviewThreadState.Open,
            Created = _time,
            Comments =
            [
                new ReviewComment { Author = "Ann", AuthorUsername = "ann", Text = "Starting comment", Created = _time }
            ]
        };
    }

    /// <summary>
    ///     Test that a check thread is stored without any file row, reads back with its check and an empty path, takes
    ///     a reply and can be resolved, while a file thread beside it reads back without a check.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddThreadAsync_CheckThread_RoundTripsCheckItemId()
    {
        // Arrange: a review with one file
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);

        // Act: add a check thread and a file thread, reply to the check thread and resolve it
        var check = await db.Store.AddThreadAsync(NewThread(projectId, string.Empty, "SEC-1"), cancellationToken);
        var file = await db.Store.AddThreadAsync(NewThread(projectId, "a.txt", null, 1), cancellationToken);
        await db.Store.AddCommentAsync(
            projectId,
            "REV-1",
            check.Id,
            new ReviewComment { Author = "Bob", AuthorUsername = "bob", Text = "Reply", Created = _time },
            cancellationToken);
        await db.Store.ResolveThreadAsync(projectId, "REV-1", check.Id, ThreadKind.Question, cancellationToken);
        var listed = await db.Store.ListThreadsAsync(projectId, "REV-1", cancellationToken);

        // Assert: the check thread keeps its check, no file and no lines; the file thread has no check
        var storedCheck = listed.Single(t => t.Id == check.Id);
        var storedFile = listed.Single(t => t.Id == file.Id);
        Assert.Equal("SEC-1", storedCheck.CheckItemId);
        Assert.True(storedCheck.IsCheckThread);
        Assert.Equal(string.Empty, storedCheck.FilePath);
        Assert.Null(storedCheck.StartLine);
        Assert.Equal(ThreadSide.Head, storedCheck.Side);
        Assert.Equal(ReviewThreadState.Resolved, storedCheck.State);
        Assert.Equal(2, storedCheck.Comments.Count);
        Assert.Null(storedFile.CheckItemId);
        Assert.False(storedFile.IsCheckThread);
        Assert.Equal("a.txt", storedFile.FilePath);
        Assert.Equal(1, storedFile.StartLine);
    }

    /// <summary>
    ///     Test that a file thread that names a file which is not in the review is refused by the store (the database
    ///     no longer has a foreign key to the file) and nothing is stored.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddThreadAsync_FileThreadOnMissingFile_IsRefused()
    {
        // Arrange: a review with one file
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);

        // Act: add a file thread for a file that does not exist
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.Store.AddThreadAsync(NewThread(projectId, "missing.txt", null), cancellationToken));

        // Assert: no thread exists afterwards
        Assert.Empty(await db.Store.ListThreadsAsync(projectId, "REV-1", cancellationToken));
    }

    /// <summary>
    ///     Test that a thread with both anchors, with neither, or with a check and lines is refused by the store and
    ///     nothing is stored.
    /// </summary>
    /// <param name="filePath">The file path of the anchor.</param>
    /// <param name="checkItemId">The check of the anchor.</param>
    /// <param name="startLine">The first line of the anchor.</param>
    [Theory]
    [InlineData("a.txt", "SEC-1", null)]
    [InlineData("", null, null)]
    [InlineData("", "SEC-1", 3)]
    [InlineData("", "", null)]
    public async Task ReviewStore_AddThreadAsync_ThreadWithBothOrNeitherAnchor_IsRefused(
        string filePath, string? checkItemId, int? startLine)
    {
        // Arrange: a review with one file
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);

        // Act: add the malformed thread
        await Assert.ThrowsAsync<ArgumentException>(
            () => db.Store.AddThreadAsync(NewThread(projectId, filePath, checkItemId, startLine), cancellationToken));

        // Assert: no thread exists afterwards
        Assert.Empty(await db.Store.ListThreadsAsync(projectId, "REV-1", cancellationToken));
    }

    /// <summary>
    ///     Test that the database refuses a thread of a review that does not exist (the foreign key now points at the
    ///     review), and that deleting a review removes its file and check threads together with their comments.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Thread_ReviewForeignKey_RejectsThreadOfUnknownReview()
    {
        // Arrange: a review with a file thread and a check thread
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await db.Store.AddThreadAsync(NewThread(projectId, "a.txt", null), cancellationToken);
        await db.Store.AddThreadAsync(NewThread(projectId, string.Empty, "SEC-1"), cancellationToken);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);

        // Act: insert a thread of an unknown review directly, then delete the review
        context.Threads.Add(new ReviewThread
        {
            ProjectId = projectId,
            ReviewId = "NO-SUCH-REVIEW",
            CheckItemId = "SEC-1",
            State = ReviewThreadState.Open,
            Created = _time
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(cancellationToken));
        context.ChangeTracker.Clear();
        await context.Reviews.Where(r => r.ProjectId == projectId && r.Id == "REV-1").ExecuteDeleteAsync(cancellationToken);

        // Assert: the orphan was refused, and the review took its threads and comments with it
        Assert.Equal(0, await context.Threads.CountAsync(cancellationToken));
        Assert.Equal(0, await context.Comments.CountAsync(cancellationToken));
    }

    /// <summary>
    ///     Test that an open check thread stops the final signature inside the write transaction, although it is chat
    ///     and leaves the digest unchanged, and that the store stays unconcluded.
    /// </summary>
    [Fact]
    public async Task ReviewStore_Conclude_OpenCheckThread_IsRefusedInTheTransaction()
    {
        // Arrange: two slots, the first signed, then an open check thread (the digest does not change)
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var first = await db.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = projectId, ReviewId = "REV-1", PrimaryUserId = 1 }, cancellationToken);
        var second = await db.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = projectId, ReviewId = "REV-1", PrimaryUserId = 2 }, cancellationToken);
        var record = await db.Store.GetRecordAsync(projectId, "REV-1", cancellationToken);
        var digest = RecordDigest.Compute(record!);
        await db.Store.AddSignatureAsync(Signature(projectId, first.Id, digest), cancellationToken);
        await db.Store.AddThreadAsync(NewThread(projectId, string.Empty, "SEC-1"), cancellationToken);

        // Act: the second signer signs the digest they saw
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => db.Store.AddSignatureAsync(Signature(projectId, second.Id, digest), cancellationToken));

        // Assert: refused by the open-thread rule; one signature, not concluded
        Assert.Equal(DomainRule.SigningNotAllowed, exception.Rule);
        Assert.Single(await db.Store.ListSignaturesAsync(projectId, "REV-1", cancellationToken));
        var snapshot = await db.Store.GetSnapshotAsync(projectId, "REV-1", cancellationToken);
        Assert.False(ReviewLock.IsConcluded(snapshot!.Record, snapshot.Slots, snapshot.Signatures));
    }

    /// <summary>
    ///     Builds a signature of the given digest.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <param name="digest">The digest the signature binds itself to.</param>
    /// <returns>The signature.</returns>
    private static ReviewSignature Signature(int projectId, int slotId, string digest)
    {
        return new ReviewSignature
        {
            ProjectId = projectId,
            ReviewId = "REV-1",
            SlotId = slotId,
            UserId = 1,
            DisplayName = "Ann",
            Role = SlotRole.Primary,
            SignedAt = _time,
            RecordDigest = digest,
            Meaning = SigningService.MeaningText
        };
    }
}
