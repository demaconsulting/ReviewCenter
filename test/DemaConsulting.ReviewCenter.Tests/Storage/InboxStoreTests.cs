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
///     Tests for <see cref="DemaConsulting.ReviewCenter.Storage.InboxStore"/> against a real SQLite database file,
///     where the read state must never move backwards, even when several writers race.
/// </summary>
public class InboxStoreTests
{
    /// <summary>
    ///     The time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Gets the cancellation token of the running test.
    /// </summary>
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Creates a user in the database.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="username">The user name.</param>
    /// <returns>The identity of the user.</returns>
    private static async Task<int> AddUserAsync(TestStore db, string username)
    {
        var user = await db.Users.AddAsync(
            new User { Username = username, DisplayName = username, PasswordHash = "hash", Created = _now },
            Token);
        return user.Id;
    }

    /// <summary>
    ///     Creates a project in the database.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="name">The project name.</param>
    /// <returns>The identity of the project.</returns>
    private static async Task<int> AddProjectAsync(TestStore db, string name = "Apollo")
    {
        var project = await db.Store.AddProjectAsync(new Project { Name = name, Created = _now }, Token);
        return project.Id;
    }

    /// <summary>
    ///     Creates a review with one file in the database.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project.</param>
    /// <param name="id">The review id.</param>
    /// <param name="createdBy">The creator, or <c>null</c>.</param>
    /// <param name="content">The content of the single file.</param>
    /// <returns>A task that completes when the review is stored.</returns>
    private static Task AddReviewAsync(
        TestStore db,
        int projectId,
        string id,
        string? createdBy,
        byte[]? content = null)
    {
        content ??= [1, 2, 3];
        var review = new Review
        {
            ProjectId = projectId,
            Id = id,
            Title = "Title " + id,
            Created = _now,
            CreatedBy = createdBy,
            DueDate = new DateOnly(2025, 6, 1),
            ContentFingerprint = Fingerprint.Compute([content]),
            Files =
            [
                new ReviewFile
                {
                    ProjectId = projectId,
                    ReviewId = id,
                    Path = "a.txt",
                    Content = content,
                    Size = content.Length,
                    Sha256 = Fingerprint.ComputeFileHash(content)
                }
            ]
        };
        return db.Store.AddReviewAsync(review, AuditTestExtensions.Draft(), Token);
    }

    /// <summary>
    ///     Test that a review is found when the user is the primary reviewer, the alternate or the creator (the
    ///     creator by any spelling of the case), and that reviews of other people are left out.
    /// </summary>
    [Fact]
    public async Task InboxStore_ListInvolvedReviewsAsync_PrimaryAlternateAndCreator_AreFound()
    {
        // Arrange: ann is primary of r1, alternate of r2 and creator of r3 (stored as "ANN"); r4 is nobody's
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var project = await AddProjectAsync(db);
        await AddReviewAsync(db, project, "r1", "bob");
        await AddReviewAsync(db, project, "r2", "bob");
        await AddReviewAsync(db, project, "r3", "ANN");
        await AddReviewAsync(db, project, "r4", "bob");
        await db.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = project, ReviewId = "r1", PrimaryUserId = ann },
            _ => AuditTestExtensions.Draft(),
            Token);
        await db.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = project, ReviewId = "r2", PrimaryUserId = bob, AlternateUserId = ann },
            _ => AuditTestExtensions.Draft(),
            Token);
        await db.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = project, ReviewId = "r4", PrimaryUserId = bob },
            _ => AuditTestExtensions.Draft(),
            Token);

        // Act
        var found = await db.Inbox.ListInvolvedReviewsAsync(ann, "ann", Token);

        // Assert: r1, r2 and r3 in order, with the values of the review; r4 is not there
        Assert.Equal(["r1", "r2", "r3"], found.Select(candidate => candidate.ReviewId));
        Assert.All(found, candidate => Assert.Equal(project, candidate.ProjectId));
        var third = found[2];
        Assert.Equal("Title r3", third.Title);
        Assert.Equal("ANN", third.CreatedBy);
        Assert.Equal(new DateOnly(2025, 6, 1), third.DueDate);
        Assert.Equal(["r4"], (await db.Inbox.ListInvolvedReviewsAsync(bob, "nobody", Token))
            .Where(candidate => candidate.ReviewId == "r4").Select(candidate => candidate.ReviewId));
    }

    /// <summary>
    ///     Test that the listing returns light records, not the reviews with their files: a review with a large
    ///     file is described by the same few values as one without.
    /// </summary>
    [Fact]
    public async Task InboxStore_ListInvolvedReviewsAsync_DoesNotLoadFiles_ReturnsLightRecords()
    {
        // Arrange: a review with a megabyte of content, created by ann
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var project = await AddProjectAsync(db);
        await AddReviewAsync(db, project, "big", "ann", new byte[1_000_000]);

        // Act
        var found = await db.Inbox.ListInvolvedReviewsAsync(ann, "ann", Token);

        // Assert: the record type carries only identity, title, creator and due date
        var candidate = Assert.Single(found);
        Assert.IsType<InboxCandidate>(candidate);
        Assert.Equal("big", candidate.ReviewId);
        Assert.Equal(
            ["ProjectId", "ReviewId", "Title", "CreatedBy", "DueDate"],
            typeof(InboxCandidate).GetProperties()
                .Where(property => property.Name != "EqualityContract")
                .Select(property => property.Name));
    }

    /// <summary>
    ///     Test that the first mark inserts a row, a later one updates it, and an older one changes nothing.
    /// </summary>
    [Fact]
    public async Task InboxStore_MarkSeenAsync_InsertsThenUpdatesAndNeverMovesBackwards()
    {
        // Arrange: a user and a review
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var project = await AddProjectAsync(db);
        await AddReviewAsync(db, project, "r1", "bob");
        Assert.Null(await db.Inbox.GetStateAsync(ann, project, "r1", Token));

        // Act and Assert: insert
        await db.Inbox.MarkSeenAsync(ann, project, "r1", 5, _now, Token);
        var inserted = await db.Inbox.GetStateAsync(ann, project, "r1", Token);
        Assert.Equal(new ReviewUserState(ann, project, "r1", 5, _now), inserted);

        // Act and Assert: newer values win
        await db.Inbox.MarkSeenAsync(ann, project, "r1", 9, _now.AddMinutes(1), Token);
        Assert.Equal(
            new ReviewUserState(ann, project, "r1", 9, _now.AddMinutes(1)),
            await db.Inbox.GetStateAsync(ann, project, "r1", Token));

        // Act and Assert: older values change nothing, and a mix keeps the higher of each
        await db.Inbox.MarkSeenAsync(ann, project, "r1", 2, _now.AddMinutes(-5), Token);
        Assert.Equal(
            new ReviewUserState(ann, project, "r1", 9, _now.AddMinutes(1)),
            await db.Inbox.GetStateAsync(ann, project, "r1", Token));
        await db.Inbox.MarkSeenAsync(ann, project, "r1", 3, _now.AddMinutes(7), Token);
        Assert.Equal(
            new ReviewUserState(ann, project, "r1", 9, _now.AddMinutes(7)),
            await db.Inbox.GetStateAsync(ann, project, "r1", Token));

        // Act and Assert: the many-call marks every listed review
        await AddReviewAsync(db, project, "r2", "bob");
        await db.Inbox.MarkSeenManyAsync(
            ann, [new InboxSeenMark(project, "r1", 1), new InboxSeenMark(project, "r2", 4)], _now, Token);
        Assert.Equal(2, (await db.Inbox.ListStatesAsync(ann, Token)).Count);
        Assert.Equal(4, (await db.Inbox.GetStateAsync(ann, project, "r2", Token))!.LastSeenCommentId);
    }

    /// <summary>
    ///     Test that many racing writers neither fail nor lose the highest value.
    /// </summary>
    [Fact]
    public async Task InboxStore_MarkSeenAsync_ParallelCalls_NoExceptionAndHighestWins()
    {
        // Arrange: a user and a review without a row yet
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var project = await AddProjectAsync(db);
        await AddReviewAsync(db, project, "r1", "bob");

        // Act: twenty writers with different values, started together
        var writers = Enumerable.Range(1, 20)
            .Select(n => Task.Run(
                () => db.Inbox.MarkSeenAsync(ann, project, "r1", n, _now.AddMinutes(n), Token), Token))
            .ToArray();
        await Task.WhenAll(writers);

        // Assert: one row with the highest values
        var states = await db.Inbox.ListStatesAsync(ann, Token);
        var state = Assert.Single(states);
        Assert.Equal(20, state.LastSeenCommentId);
        Assert.Equal(_now.AddMinutes(20), state.LastSeenAt);
    }

    /// <summary>
    ///     Test that the listing returns only the rows of the asked user.
    /// </summary>
    [Fact]
    public async Task InboxStore_ListStatesAsync_ReturnsOnlyTheUsersRows()
    {
        // Arrange: two users who both read r1
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var project = await AddProjectAsync(db);
        await AddReviewAsync(db, project, "r1", "cy");
        await db.Inbox.MarkSeenAsync(ann, project, "r1", 1, _now, Token);
        await db.Inbox.MarkSeenAsync(bob, project, "r1", 2, _now, Token);

        // Act
        var states = await db.Inbox.ListStatesAsync(ann, Token);

        // Assert
        var state = Assert.Single(states);
        Assert.Equal(ann, state.UserId);
        Assert.Equal(1, state.LastSeenCommentId);
        Assert.Empty(await db.Inbox.ListStatesAsync(int.MaxValue, Token));
    }

    /// <summary>
    ///     Test that deleting a review deletes the read states of that review only (cascade).
    /// </summary>
    [Fact]
    public async Task InboxStore_DeletingTheReview_DeletesTheStates()
    {
        // Arrange: states for two reviews
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var project = await AddProjectAsync(db);
        await AddReviewAsync(db, project, "r1", "bob");
        await AddReviewAsync(db, project, "r2", "bob");
        await db.Inbox.MarkSeenAsync(ann, project, "r1", 1, _now, Token);
        await db.Inbox.MarkSeenAsync(ann, project, "r2", 1, _now, Token);

        // Act: delete one review row directly
        await using (var context = await db.Factory.CreateDbContextAsync(Token))
        {
            await context.Reviews.Where(review => review.Id == "r1").ExecuteDeleteAsync(Token);
        }

        // Assert: only the state of the other review is left
        var left = Assert.Single(await db.Inbox.ListStatesAsync(ann, Token));
        Assert.Equal("r2", left.ReviewId);
    }

    /// <summary>
    ///     Test that deleting a user deletes the read states of that user (cascade).
    /// </summary>
    [Fact]
    public async Task InboxStore_DeletingTheUser_DeletesTheStates()
    {
        // Arrange: a state for ann
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var project = await AddProjectAsync(db);
        await AddReviewAsync(db, project, "r1", "bob");
        await db.Inbox.MarkSeenAsync(ann, project, "r1", 1, _now, Token);

        // Act: delete the user row directly
        await using (var context = await db.Factory.CreateDbContextAsync(Token))
        {
            await context.Users.Where(user => user.Id == ann).ExecuteDeleteAsync(Token);
        }

        // Assert: the table is empty
        await using var check = await db.Factory.CreateDbContextAsync(Token);
        Assert.Empty(await check.ReviewUserStates.ToListAsync(Token));
    }

    /// <summary>
    ///     Test that null and empty arguments are refused.
    /// </summary>
    [Fact]
    public async Task InboxStore_NullArguments_Throw()
    {
        // Arrange: a store
        using var db = new TestStore();

        // Act and Assert: each misuse is refused
        await Assert.ThrowsAsync<ArgumentNullException>(() => db.Inbox.ListInvolvedReviewsAsync(1, null!, Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => db.Inbox.GetStateAsync(1, 1, null!, Token));
        await Assert.ThrowsAsync<ArgumentException>(() => db.Inbox.GetStateAsync(1, 1, "", Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => db.Inbox.MarkSeenAsync(1, 1, null!, 1, _now, Token));
        await Assert.ThrowsAsync<ArgumentException>(() => db.Inbox.MarkSeenAsync(1, 1, "", 1, _now, Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => db.Inbox.MarkSeenManyAsync(1, null!, _now, Token));
    }
}
