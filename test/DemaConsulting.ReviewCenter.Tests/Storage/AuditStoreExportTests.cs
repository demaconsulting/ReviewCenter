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
///     Tests for the export reads of the audit store against a real SQLite database file:
///     <see cref="IAuditStore.ReadMatchingAsync"/> and <see cref="IAuditStore.LatestSequenceAsync"/>.
/// </summary>
public class AuditStoreExportTests
{
    /// <summary>
    ///     Appends the given number of entries, alternating between project 1, project 2 and no project.
    /// </summary>
    /// <param name="db">The test store.</param>
    /// <param name="count">The number of entries.</param>
    /// <returns>A task that completes when the entries are appended.</returns>
    private static async Task Seed(TestStore db, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            int? project = (i % 3) switch { 1 => 1, 2 => 2, _ => null };
            await db.Audit.AppendAsync(
                AuditDraft.By(TestActors.Ann, "test.event", "event " + i, project),
                TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Test that the latest sequence is 0 for an empty trail and the highest sequence otherwise.
    /// </summary>
    [Fact]
    public async Task AuditStore_LatestSequenceAsync_EmptyThenFilled_ReturnsHighestSequence()
    {
        // Arrange
        using var db = new TestStore();

        // Act
        var empty = await db.Audit.LatestSequenceAsync(TestContext.Current.CancellationToken);
        await Seed(db, 4);
        var filled = await db.Audit.LatestSequenceAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, empty);
        Assert.Equal(4, filled);
    }

    /// <summary>
    ///     Test that the matching read is ascending, keyset-paged, limited by the snapshot and the filter, and that a
    ///     project filter never returns an entry without a project.
    /// </summary>
    [Fact]
    public async Task AuditStore_ReadMatchingAsync_FilterAndSnapshot_AscendingKeysetBatches()
    {
        // Arrange: sequences 1, 4, 7, 10, 13 belong to project 1; 3, 6, 9, 12, 15 to none
        using var db = new TestStore();
        await Seed(db, 15);
        var project = new AuditFilter(ProjectId: 1);

        // Act
        var first = await db.Audit.ReadMatchingAsync(project, 0, 12, 2, TestContext.Current.CancellationToken);
        var second = await db.Audit.ReadMatchingAsync(project, first[^1].Sequence, 12, 2, TestContext.Current.CancellationToken);
        var third = await db.Audit.ReadMatchingAsync(project, second[^1].Sequence, 12, 2, TestContext.Current.CancellationToken);
        var all = await db.Audit.ReadMatchingAsync(AuditFilter.None, 0, 15, 100, TestContext.Current.CancellationToken);
        var none = await db.Audit.ReadMatchingAsync(AuditFilter.None, 15, 15, 100, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([1L, 4L], first.Select(e => e.Sequence));
        Assert.Equal([7L, 10L], second.Select(e => e.Sequence));
        Assert.Empty(third);
        Assert.Equal(Enumerable.Range(1, 15).Select(i => (long)i), all.Select(e => e.Sequence));
        Assert.Empty(none);
    }

    /// <summary>
    ///     Test that the matching read applies the actor text, the action, the action prefix, and the review of the
    ///     filter, like the listing.
    /// </summary>
    [Fact]
    public async Task AuditStore_ReadMatchingAsync_ActorActionAndReviewFilters_ReturnOnlyMatches()
    {
        // Arrange: four entries of project 1 with different actors, actions, and reviews
        using var db = new TestStore();
        var token = TestContext.Current.CancellationToken;
        await db.Audit.AppendAsync(new AuditDraft("review.created", null, "Ann", 1, "r1", "d1"), token);
        await db.Audit.AppendAsync(new AuditDraft("review.signed", null, "Bob", 1, "r2", "d2"), token);
        await db.Audit.AppendAsync(new AuditDraft("project.created", null, "Ann", 1, null, "d3"), token);
        await db.Audit.AppendAsync(new AuditDraft("review.created", null, "Ann", 1, "r1", "d4"), token);

        // Act
        var byActor = await db.Audit.ReadMatchingAsync(new AuditFilter(ActorText: "bob"), 0, 4, 100, token);
        var byAction = await db.Audit.ReadMatchingAsync(new AuditFilter(Action: "review.created"), 0, 4, 100, token);
        var byPrefix = await db.Audit.ReadMatchingAsync(new AuditFilter(ActionPrefix: "review."), 0, 4, 100, token);
        var byReview = await db.Audit.ReadMatchingAsync(new AuditFilter(ReviewId: "r1"), 0, 4, 100, token);

        // Assert
        Assert.Equal([2L], byActor.Select(e => e.Sequence));
        Assert.Equal([1L, 4L], byAction.Select(e => e.Sequence));
        Assert.Equal([1L, 2L, 4L], byPrefix.Select(e => e.Sequence));
        Assert.Equal([1L, 4L], byReview.Select(e => e.Sequence));
    }
}
