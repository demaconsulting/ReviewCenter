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
///     Tests for the review conclusion text of <see cref="ReviewStore"/> against a real SQLite database file: the
///     column, the compare-and-set update stored together with its audit entry, the refusal on a concluded review
///     inside the write transaction, and the text in the record that the digest is built from.
/// </summary>
/// <remarks>
///     The text is the free-text conclusion of the record, never the approved or rejected conclusion of a ballot.
/// </remarks>
public class ReviewStoreReviewConclusionTextTests
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
    ///     Builds an audit draft of a review conclusion text change.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The draft.</returns>
    private static AuditDraft Draft(int projectId)
    {
        return AuditDraft.By(
            TestActors.Ann, ChecklistService.ReviewConclusionTextChangedAction, "length 0 -> 11", projectId, "REV-1");
    }

    /// <summary>
    ///     Reads the review conclusion text of <c>REV-1</c> again.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The stored text, or <c>null</c> when there is none.</returns>
    private static async Task<string?> ReadTextAsync(TestStore db, int projectId)
    {
        var review = await db.Store.GetReviewAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        return review!.ReviewConclusionText;
    }

    /// <summary>
    ///     Counts the audit entries of the review conclusion text change.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <returns>The number of entries.</returns>
    private static async Task<int> AuditCountAsync(TestStore db)
    {
        var entries = await db.Audit.ReadAfterAsync(0, 10_000, TestContext.Current.CancellationToken);
        return entries.Count(entry => entry.Action == ChecklistService.ReviewConclusionTextChangedAction);
    }

    /// <summary>
    ///     Test that a review starts without text, that the update stores the text together with its audit entry, and
    ///     that the text reads back in the review and in the record the digest is built from, and changes the digest.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateReviewConclusionTextAsync_StoresTextAndAuditAndRecord()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var before = await db.Store.GetRecordAsync(projectId, "REV-1", cancellationToken);

        // Act
        var stored = await db.Store.UpdateReviewConclusionTextAsync(
            projectId, "REV-1", null, "Looks good.", Draft(projectId), cancellationToken);
        var after = await db.Store.GetRecordAsync(projectId, "REV-1", cancellationToken);

        // Assert
        Assert.Null(before!.ReviewConclusionText);
        Assert.True(stored);
        Assert.Equal("Looks good.", await ReadTextAsync(db, projectId));
        Assert.Equal("Looks good.", after!.ReviewConclusionText);
        Assert.NotEqual(RecordDigest.Compute(before), RecordDigest.Compute(after));
        Assert.Equal(1, await AuditCountAsync(db));
    }

    /// <summary>
    ///     Test that the update compares the text it expects: a stale expectation changes nothing and records no
    ///     audit entry, while the current text can be replaced and cleared.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateReviewConclusionTextAsync_CompareAndSet_RefusesStaleExpectation()
    {
        // Arrange: the text "First." is stored
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await db.Store.UpdateReviewConclusionTextAsync(projectId, "REV-1", null, "First.", Draft(projectId), cancellationToken);

        // Act: a writer that still expects no text, then one that expects the current text, then a clear
        var stale = await db.Store.UpdateReviewConclusionTextAsync(
            projectId, "REV-1", null, "Second.", Draft(projectId), cancellationToken);
        var staleText = await ReadTextAsync(db, projectId);
        var auditAfterStale = await AuditCountAsync(db);
        var replaced = await db.Store.UpdateReviewConclusionTextAsync(
            projectId, "REV-1", "First.", "Second.", Draft(projectId), cancellationToken);
        var cleared = await db.Store.UpdateReviewConclusionTextAsync(
            projectId, "REV-1", "Second.", null, Draft(projectId), cancellationToken);

        // Assert
        Assert.False(stale);
        Assert.Equal("First.", staleText);
        Assert.Equal(1, auditAfterStale);
        Assert.True(replaced);
        Assert.True(cleared);
        Assert.Null(await ReadTextAsync(db, projectId));
        Assert.Equal(3, await AuditCountAsync(db));
    }

    /// <summary>
    ///     Test that a missing review changes nothing and is reported as a stale expectation.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateReviewConclusionTextAsync_MissingReview_ReturnsFalse()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);

        // Act
        var stored = await db.Store.UpdateReviewConclusionTextAsync(
            projectId, "NOPE", null, "Text.", Draft(projectId), cancellationToken);

        // Assert
        Assert.False(stored);
        Assert.Equal(0, await AuditCountAsync(db));
    }

    /// <summary>
    ///     Test that a concluded review refuses the update as locked inside the store, and keeps the text and the
    ///     audit trail as they were.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateReviewConclusionTextAsync_ConcludedReview_ThrowsReviewLocked()
    {
        // Arrange: a text, then the only slot signs the record with that text
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await db.Store.UpdateReviewConclusionTextAsync(
            projectId, "REV-1", null, "Looks good.", Draft(projectId), cancellationToken);
        var slot = await db.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = projectId, ReviewId = "REV-1", PrimaryUserId = 1 }, cancellationToken);
        var record = await db.Store.GetRecordAsync(projectId, "REV-1", cancellationToken);
        await db.Store.AddSignatureAsync(
            new ReviewSignature
            {
                ProjectId = projectId,
                ReviewId = "REV-1",
                SlotId = slot.Id,
                UserId = 1,
                DisplayName = "Ann",
                Role = SlotRole.Primary,
                SignedAt = _time,
                RecordDigest = RecordDigest.Compute(record!),
                Meaning = SigningService.MeaningText
            },
            cancellationToken);
        var auditBefore = await AuditCountAsync(db);

        // Act
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.UpdateReviewConclusionTextAsync(
            projectId, "REV-1", "Looks good.", "Too late.", Draft(projectId), cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ReviewLocked, exception.Rule);
        Assert.Equal("Looks good.", await ReadTextAsync(db, projectId));
        Assert.Equal(auditBefore, await AuditCountAsync(db));
    }

    /// <summary>
    ///     Test that the column holds the longest allowed text, that it is limited to that length in the model, and
    ///     that a review added without a text has none.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Model_ReviewConclusionTextColumn_IsLimitedAndOptional()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var longest = new string('x', ReviewLimits.MaxConclusionTextLength);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);
        var property = context.Model.FindEntityType(typeof(Review))!.FindProperty(nameof(Review.ReviewConclusionText))!;

        // Act
        var empty = await ReadTextAsync(db, projectId);
        await db.Store.UpdateReviewConclusionTextAsync(projectId, "REV-1", null, longest, Draft(projectId), cancellationToken);

        // Assert
        Assert.Null(empty);
        Assert.Equal(ReviewLimits.MaxConclusionTextLength, property.GetMaxLength());
        Assert.True(property.IsNullable);
        Assert.Equal(longest, await ReadTextAsync(db, projectId));
    }
}
