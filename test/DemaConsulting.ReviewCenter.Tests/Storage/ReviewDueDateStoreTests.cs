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
using DemaConsulting.ReviewCenter.Tests.Core;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for the due date of <see cref="DemaConsulting.ReviewCenter.Storage.ReviewStore"/> against a real
///     SQLite database file: the column, the compare-and-set update with its audit entry, and the race between
///     two writers.
/// </summary>
public class ReviewDueDateStoreTests
{
    /// <summary>
    ///     The creation time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _created = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The audit action used by the tests.
    /// </summary>
    private const string Action = "review.due-date-changed";

    /// <summary>
    ///     Adds a project with one review that has the given due date.
    /// </summary>
    /// <param name="db">The test database.</param>
    /// <param name="due">The due date of the review, or <c>null</c>.</param>
    /// <returns>The id of the project.</returns>
    private static async Task<int> AddReviewAsync(TestStore db, DateOnly? due)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await db.Store.AddProjectAsync(new Project { Name = "Apollo", Created = _created }, cancellationToken);
        byte[] content = [1, 2, 3];
        await db.Store.AddReviewAsync(
            new Review
            {
                ProjectId = project.Id,
                Id = "REV-1",
                Title = "Title",
                State = ReviewState.Draft,
                Created = _created,
                DueDate = due,
                ContentFingerprint = Fingerprint.Compute([content]),
                Files =
                [
                    new ReviewFile
                    {
                        ProjectId = project.Id,
                        ReviewId = "REV-1",
                        Path = "a.txt",
                        Content = content,
                        Size = content.Length,
                        Sha256 = Fingerprint.ComputeFileHash(content)
                    }
                ]
            },
            cancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Test that a review is stored with the due date it was given, and without one by default.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddReviewAsync_DueDate_RoundTripsAsCalendarDay()
    {
        // Arrange, Act
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var withDate = await AddReviewAsync(db, new DateOnly(2030, 12, 31));

        // Assert
        Assert.Equal(new DateOnly(2030, 12, 31), (await db.Store.GetReviewAsync(withDate, "REV-1", cancellationToken))!.DueDate);
        using var plain = new TestStore();
        var without = await AddReviewAsync(plain, null);
        Assert.Null((await plain.Store.GetReviewAsync(without, "REV-1", cancellationToken))!.DueDate);
    }

    /// <summary>
    ///     Test that a change and a clear are persisted, survive a reload, and write one audit entry each, of the
    ///     review, in the same transaction, whose chain still verifies.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateDueDateAsync_ChangeAndClear_ArePersistedAndAudited()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db, null);
        var first = new DateOnly(2031, 1, 2);
        var draft = AuditDraft.By(TestActors.Ann, Action, "none -> 2031-01-02", projectId, "REV-1");

        // Act: set, then clear
        var set = await db.Store.UpdateDueDateAsync(projectId, "REV-1", null, first, draft, cancellationToken);
        var afterSet = (await db.Store.GetReviewAsync(projectId, "REV-1", cancellationToken))!.DueDate;
        var cleared = await db.Store.UpdateDueDateAsync(projectId, "REV-1", first, null, draft, cancellationToken);

        // Assert
        Assert.True(set);
        Assert.Equal(first, afterSet);
        Assert.True(cleared);
        Assert.Null((await db.Store.GetReviewAsync(projectId, "REV-1", cancellationToken))!.DueDate);
        var entries = (await db.Audit.ListAsync(projectId, null, null, null, 100, cancellationToken))
            .Where(e => e.Action == Action)
            .ToList();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal("REV-1", e.ReviewId));
        Assert.True(AuditChain.Verify(await db.Audit.ReadAfterAsync(0, 100, cancellationToken)).IsIntact);
    }

    /// <summary>
    ///     Test that a change based on a due date that is no longer stored, and a change to a review that does not
    ///     exist, change nothing and write no audit entry.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateDueDateAsync_ExpectedDiffers_ChangesNothing()
    {
        // Arrange: a review that has a date
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var current = new DateOnly(2031, 1, 2);
        var projectId = await AddReviewAsync(db, current);
        var draft = AuditDraft.By(TestActors.Ann, Action, "x", projectId, "REV-1");

        // Act: a stale caller who saw no date, one who saw another date, and an unknown review
        var staleNone = await db.Store.UpdateDueDateAsync(projectId, "REV-1", null, new DateOnly(2032, 1, 1), draft, cancellationToken);
        var staleOther = await db.Store.UpdateDueDateAsync(
            projectId, "REV-1", new DateOnly(2030, 5, 5), new DateOnly(2032, 1, 1), draft, cancellationToken);
        var unknown = await db.Store.UpdateDueDateAsync(projectId, "REV-9", current, null, draft, cancellationToken);

        // Assert
        Assert.False(staleNone);
        Assert.False(staleOther);
        Assert.False(unknown);
        Assert.Equal(current, (await db.Store.GetReviewAsync(projectId, "REV-1", cancellationToken))!.DueDate);
        Assert.DoesNotContain(await db.Audit.ListAsync(null, null, null, null, 100, cancellationToken), e => e.Action == Action);
        Assert.True(AuditChain.Verify(await db.Audit.ReadAfterAsync(0, 100, cancellationToken)).IsIntact);
    }

    /// <summary>
    ///     Test that of two writers that start from the same stored due date at the same time exactly one wins
    ///     and the other is told it lost, with one audit entry and an intact chain.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateDueDateAsync_ConcurrentWriters_ExactlyOneWins()
    {
        // Arrange: two writers with the same expected value and different new dates, released together
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db, null);
        var start = new TaskCompletionSource();

        async Task<bool> WriteAsync(DateOnly due)
        {
            await start.Task;
            return await db.Store.UpdateDueDateAsync(
                projectId,
                "REV-1",
                null,
                due,
                AuditDraft.By(TestActors.Ann, Action, due.ToString("yyyy-MM-dd"), projectId, "REV-1"),
                cancellationToken);
        }

        var first = Task.Run(() => WriteAsync(new DateOnly(2031, 1, 1)), cancellationToken);
        var second = Task.Run(() => WriteAsync(new DateOnly(2032, 2, 2)), cancellationToken);

        // Act
        start.SetResult();
        var results = await Task.WhenAll(first, second);

        // Assert: one winner, whose date is stored, one entry, and an intact chain
        Assert.Single(results, won => won);
        var stored = (await db.Store.GetReviewAsync(projectId, "REV-1", cancellationToken))!.DueDate;
        Assert.Equal(results[0] ? new DateOnly(2031, 1, 1) : new DateOnly(2032, 2, 2), stored);
        Assert.Single(
            await db.Audit.ListAsync(projectId, null, null, null, 100, cancellationToken),
            e => e.Action == Action);
        Assert.True(AuditChain.Verify(await db.Audit.ReadAfterAsync(0, 100, cancellationToken)).IsIntact);
    }
}
