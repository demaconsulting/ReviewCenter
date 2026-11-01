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
using Microsoft.EntityFrameworkCore;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for the follow-up review columns and the carry rows of <see cref="DemaConsulting.ReviewCenter.Storage.ReviewStore"/>
///     against a real SQLite database file: the round trip of every new column, the one-transaction guarantee of
///     the carry, the marker that a new answer clears, and the index on the followed review.
/// </summary>
public class ReviewFollowUpStoreTests
{
    /// <summary>
    ///     The creation time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _created = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The content of the file in both reviews.
    /// </summary>
    private static readonly byte[] _content = [1, 2, 3];

    /// <summary>
    ///     Builds a review of the file <c>a.txt</c> for the tests.
    /// </summary>
    /// <param name="projectId">The owning project.</param>
    /// <param name="id">The review id.</param>
    /// <param name="follows">The followed review, or <c>null</c>.</param>
    /// <param name="carry">The carried rows, or <c>null</c>.</param>
    /// <returns>The review, ready to add.</returns>
    private static Review NewReview(int projectId, string id, string? follows = null, ReviewCarryOver? carry = null)
    {
        return new Review
        {
            ProjectId = projectId,
            Id = id,
            Title = "Title " + id,
            State = ReviewState.Draft,
            Created = _created,
            ContentFingerprint = Fingerprint.Compute([_content]),
            FollowsReviewId = follows,
            CarriedAnswerCount = carry?.Answers.Count ?? 0,
            CarriedThreadCount = carry?.Threads.Count ?? 0,
            CarryOver = carry,
            Files =
            [
                new ReviewFile
                {
                    ProjectId = projectId,
                    ReviewId = id,
                    Path = "a.txt",
                    Content = _content,
                    Size = _content.Length,
                    Sha256 = Fingerprint.ComputeFileHash(_content)
                }
            ]
        };
    }

    /// <summary>
    ///     Builds the rows a follow-up of <c>REV-1</c> carries: one answer, one not-applicable section and one thread.
    /// </summary>
    /// <param name="projectId">The owning project.</param>
    /// <param name="threadFile">The file of the carried thread.</param>
    /// <returns>The carried rows.</returns>
    private static ReviewCarryOver NewCarry(int projectId, string threadFile = "a.txt")
    {
        return new ReviewCarryOver(
            [
                new ChecklistAnswer
                {
                    ProjectId = projectId,
                    ReviewId = "REV-2",
                    ItemId = "REQ-01",
                    Outcome = ChecklistOutcome.Pass,
                    AnsweredBy = "Ann",
                    AnsweredByUsername = "ann",
                    AnsweredAt = _created,
                    CarriedFromReviewId = "REV-1"
                }
            ],
            [
                new ChecklistSectionState
                {
                    ProjectId = projectId,
                    ReviewId = "REV-2",
                    SectionId = "docs",
                    Applicable = false,
                    Reason = "No documents",
                    SetBy = "Ann",
                    SetByUsername = "ann",
                    SetAt = _created,
                    CarriedFromReviewId = "REV-1"
                }
            ],
            [
                new ReviewThread
                {
                    Id = 9001, // supplied identities must be ignored; the store assigns its own
                    ProjectId = projectId,
                    ReviewId = "REV-2",
                    FilePath = threadFile,
                    State = ReviewThreadState.Open,
                    Created = _created,
                    CarriedFromReviewId = "REV-1",
                    CarriedFromThreadId = 7,
                    Comments = [new ReviewComment { Author = "Bob", AuthorUsername = "bob", Text = "Check this", Created = _created }]
                }
            ],
            1,
            []);
    }

    /// <summary>
    ///     Adds a project with the review <c>REV-1</c>.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <returns>The identity of the project.</returns>
    private static async Task<int> AddFirstAsync(TestStore db)
    {
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _created }, TestContext.Current.CancellationToken);
        await db.Store.AddReviewAsync(NewReview(project.Id, "REV-1"), TestContext.Current.CancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Test that a follow-up review with carried rows round-trips every new column: the followed review and the
    ///     counts, the carried marker of the answer, the section and the thread, the original thread number, the
    ///     comment, and the record that the signature covers; and that the review audit chain verifies.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddReviewAsync_FollowUp_RoundTripsEveryNewColumn()
    {
        // Arrange: a first review
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddFirstAsync(db);

        // Act: add the follow-up with its carried rows, then read everything back
        await db.Store.AddReviewAsync(NewReview(projectId, "REV-2", "REV-1", NewCarry(projectId)), cancellationToken);
        var review = await db.Store.GetReviewAsync(projectId, "REV-2", cancellationToken);
        var answers = await db.Store.ListChecklistAnswersAsync(projectId, "REV-2", cancellationToken);
        var sections = await db.Store.ListChecklistSectionStatesAsync(projectId, "REV-2", cancellationToken);
        var threads = await db.Store.ListThreadsAsync(projectId, "REV-2", cancellationToken);
        var record = await db.Store.GetRecordAsync(projectId, "REV-2", cancellationToken);
        var plain = await db.Store.GetReviewAsync(projectId, "REV-1", cancellationToken);

        // Assert: the review columns
        Assert.Equal("REV-1", review!.FollowsReviewId);
        Assert.Equal(1, review.CarriedAnswerCount);
        Assert.Equal(1, review.CarriedThreadCount);
        Assert.Null(plain!.FollowsReviewId);
        Assert.Equal(0, plain.CarriedAnswerCount);

        // Assert: the carried rows with their markers and their original values
        var answer = Assert.Single(answers);
        Assert.Equal("REV-1", answer.CarriedFromReviewId);
        Assert.Equal(ChecklistOutcome.Pass, answer.Outcome);
        Assert.Equal("ann", answer.AnsweredByUsername);
        Assert.Equal(_created, answer.AnsweredAt);
        var section = Assert.Single(sections);
        Assert.Equal("REV-1", section.CarriedFromReviewId);
        Assert.Equal("No documents", section.Reason);
        var thread = Assert.Single(threads);
        Assert.NotEqual(9001, thread.Id);
        Assert.Equal("REV-1", thread.CarriedFromReviewId);
        Assert.Equal(7, thread.CarriedFromThreadId);
        Assert.Equal(ReviewThreadState.Open, thread.State);
        var comment = Assert.Single(thread.Comments);
        Assert.Equal("Bob", comment.Author);
        Assert.Equal("Check this", comment.Text);

        // Assert: the record holds the thread marker, so the signature covers it, the carried rows belong to no ballot
        // yet, and the chain is intact
        Assert.Equal("REV-1", record!.FollowsReviewId);
        Assert.Empty(record.Ballots);
        Assert.Equal("REV-1", Assert.Single(record.Threads).CarriedFromReviewId);
        Assert.True(AuditChain.Verify(await db.Audit.ReadAfterAsync(0, 100, cancellationToken)).IsIntact);
    }

    /// <summary>
    ///     Test that a carry that fails halfway creates nothing: when a carried thread names a file that the review
    ///     does not have, the database refuses, and afterwards neither the review nor any carried row nor an audit
    ///     entry of the review exists, while the followed review is untouched.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddReviewAsync_FollowUpCarryFails_CreatesNothing()
    {
        // Arrange: a first review, and a carry whose thread names a file the new review does not have
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddFirstAsync(db);
        var broken = NewReview(projectId, "REV-2", "REV-1", NewCarry(projectId, "missing.txt"));

        // Act
        await Assert.ThrowsAsync<DbUpdateException>(() => db.Store.AddReviewAsync(broken, cancellationToken));

        // Assert: nothing of the follow-up exists
        Assert.Null(await db.Store.GetReviewAsync(projectId, "REV-2", cancellationToken));
        Assert.Empty(await db.Store.ListChecklistAnswersAsync(projectId, "REV-2", cancellationToken));
        Assert.Empty(await db.Store.ListChecklistSectionStatesAsync(projectId, "REV-2", cancellationToken));
        Assert.Empty(await db.Store.ListThreadsAsync(projectId, "REV-2", cancellationToken));
        Assert.DoesNotContain(
            await db.Audit.ListAsync(projectId, null, null, null, 100, cancellationToken),
            e => e.ReviewId == "REV-2");
        Assert.NotNull(await db.Store.GetReviewAsync(projectId, "REV-1", cancellationToken));
        Assert.True(AuditChain.Verify(await db.Audit.ReadAfterAsync(0, 100, cancellationToken)).IsIntact);
    }

    /// <summary>
    ///     Test that answering a carried check again, and setting a carried section again, in the ballot of a slot store
    ///     the new values without the carried marker, while an answer that is not replaced keeps its marker, and that
    ///     the review-level carried rows stay as they were.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetBallotAnswerAndSectionState_CarriedRows_AreReplacedWithoutMarker()
    {
        // Arrange: a follow-up that carried an answer and a section, and a slot that started from them
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddFirstAsync(db);
        await db.Store.AddReviewAsync(NewReview(projectId, "REV-2", "REV-1", NewCarry(projectId)), cancellationToken);
        var slot = await db.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = projectId, ReviewId = "REV-2", PrimaryUserId = 1 }, cancellationToken);
        var draft = AuditDraft.By(TestActors.Bob, "checklist.answered", "x", projectId, "REV-2");

        // Act: the answer and the section are set again by another person, without a marker
        await db.Store.SetBallotAnswerAsync(
            new ChecklistAnswer
            {
                ProjectId = projectId,
                ReviewId = "REV-2",
                SlotId = slot.Id,
                ItemId = "REQ-01",
                Outcome = ChecklistOutcome.Fail,
                Justification = "Wrong",
                AnsweredBy = "Bob",
                AnsweredByUsername = "bob",
                AnsweredAt = _created.AddHours(1)
            },
            draft,
            null,
            cancellationToken);
        await db.Store.SetBallotSectionStateAsync(
            new ChecklistSectionState
            {
                ProjectId = projectId,
                ReviewId = "REV-2",
                SlotId = slot.Id,
                SectionId = "docs",
                Applicable = false,
                Reason = "Still none",
                SetBy = "Bob",
                SetByUsername = "bob",
                SetAt = _created.AddHours(1)
            },
            draft,
            null,
            cancellationToken);

        // Assert: the ballot holds the new values without markers and no second row; the baseline rows are unchanged
        var ballot = (await db.Store.GetBallotAsync(projectId, "REV-2", slot.Id, cancellationToken))!;
        var answer = Assert.Single(ballot.Answers);
        Assert.Null(answer.CarriedFromReviewId);
        Assert.Equal(ChecklistOutcome.Fail, answer.Outcome);
        Assert.Equal("bob", answer.AnsweredByUsername);
        var section = Assert.Single(ballot.SectionStates);
        Assert.Null(section.CarriedFromReviewId);
        Assert.Equal("Still none", section.Reason);
        Assert.Equal("REV-1", Assert.Single(await db.Store.ListChecklistAnswersAsync(projectId, "REV-2", cancellationToken)).CarriedFromReviewId);
        Assert.Equal("REV-1", Assert.Single(await db.Store.ListChecklistSectionStatesAsync(projectId, "REV-2", cancellationToken)).CarriedFromReviewId);
        Assert.Equal("REV-1", Assert.Single(await db.Store.ListThreadsAsync(projectId, "REV-2", cancellationToken)).CarriedFromReviewId);
    }

    /// <summary>
    ///     Test that a carried Finding keeps its flags: the included starting comment and the included resolution
    ///     comment round-trip with their kinds, and the thread is a Finding when read back.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddReviewAsync_CarriedFinding_PersistsFlagsAndKind()
    {
        // Arrange: a first review and a carry whose thread is a Finding with an included resolution note
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddFirstAsync(db);
        var carry = NewCarry(projectId);
        var thread = carry.Threads[0];
        thread.Comments.Clear();
        thread.Comments.AddRange(
        [
            new ReviewComment
            {
                Author = "Bob",
                AuthorUsername = "bob",
                Text = "Check this",
                Created = _created,
                IncludeInReport = true
            },
            new ReviewComment
            {
                Author = "Ann",
                AuthorUsername = "ann",
                Text = "Fixed",
                Created = _created,
                IncludeInReport = true,
                Kind = ReviewCommentKind.Resolution
            },
            new ReviewComment { Author = "Cy", AuthorUsername = "cy", Text = "Chat", Created = _created }
        ]);

        // Act
        await db.Store.AddReviewAsync(NewReview(projectId, "REV-2", "REV-1", carry), cancellationToken);
        var stored = Assert.Single(await db.Store.ListThreadsAsync(projectId, "REV-2", cancellationToken));

        // Assert: the flags and kinds are stored per comment, and the thread is a Finding
        Assert.Equal(ThreadKind.Finding, stored.Kind);
        Assert.Equal([true, true, false], stored.Comments.Select(c => c.IncludeInReport));
        Assert.Equal(
            [ReviewCommentKind.Comment, ReviewCommentKind.Resolution, ReviewCommentKind.Comment],
            stored.Comments.Select(c => c.Kind));
    }

    /// <summary>
    ///     Test that the model has the index on the project and the followed review, which the lookup of the
    ///     reviews that follow a review will use.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Model_HasIndexOnProjectAndFollowedReview()
    {
        // Arrange
        using var db = new TestStore();

        // Act
        await using var context = await db.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var index = context.Model.FindEntityType(typeof(Review))!
            .GetIndexes()
            .SingleOrDefault(i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(Review.ProjectId), nameof(Review.FollowsReviewId)]));

        // Assert
        Assert.NotNull(index);
        Assert.Null(context.Model.FindEntityType(typeof(Review))!.FindProperty(nameof(Review.CarryOver)));
    }
}
