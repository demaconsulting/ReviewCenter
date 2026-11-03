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
///     Tests for the schema that <see cref="ReviewCenterDbContext"/> creates, against a real SQLite database file: the
///     relationships, the cascades, the defaults and the column mappings that the store tests exercise only on the
///     way, each checked by reading or writing the tables directly.
/// </summary>
public class ReviewCenterDbContextTests
{
    /// <summary>
    ///     The time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _time = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     A digest-shaped text of the full column width.
    /// </summary>
    private static readonly string _digest = new('a', 64);

    /// <summary>
    ///     Adds a project with the review <c>REV-1</c> of the file <c>a.txt</c>.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <returns>The identity of the project.</returns>
    private static async Task<int> AddReviewAsync(TestStore db)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _time }, cancellationToken);
        await db.Store.AddReviewAsync(NewReview(project.Id, "REV-1", null), cancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Builds a review of the file <c>a.txt</c>.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="id">The review identifier.</param>
    /// <param name="followsReviewId">The review that this one follows, or <c>null</c>.</param>
    /// <param name="dueDate">The due date, or <c>null</c>.</param>
    /// <returns>The review, ready to add.</returns>
    private static Review NewReview(int projectId, string id, string? followsReviewId, DateOnly? dueDate = null)
    {
        return new Review
        {
            ProjectId = projectId,
            Id = id,
            Title = "Title",
            State = ReviewState.Draft,
            Created = _time,
            FollowsReviewId = followsReviewId,
            DueDate = dueDate,
            ContentFingerprint = TestContent.FingerprintA,
            Files =
            [
                new ReviewFile
                {
                    ProjectId = projectId,
                    ReviewId = id,
                    Path = "a.txt",
                    Content = TestContent.A,
                    Size = TestContent.A.Length,
                    Sha256 = TestContent.HashA
                }
            ]
        };
    }

    /// <summary>
    ///     Adds an open file thread with one comment to <c>REV-1</c>.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The stored thread.</returns>
    private static async Task<ReviewThread> AddThreadAsync(TestStore db, int projectId)
    {
        return await db.Store.AddThreadAsync(
            new ReviewThread
            {
                ProjectId = projectId,
                ReviewId = "REV-1",
                FilePath = "a.txt",
                State = ReviewThreadState.Open,
                Created = _time,
                Comments = [new ReviewComment { Author = "Ann", AuthorUsername = "ann", Text = "Text", Created = _time }]
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Builds a user for the tests.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <returns>The user.</returns>
    private static User NewUser(string username)
    {
        return new User
        {
            Username = username,
            DisplayName = username,
            Enabled = true,
            PasswordHash = "hash",
            Created = _time
        };
    }

    /// <summary>
    ///     Adds a reviewer slot to <c>REV-1</c> directly in the table.
    /// </summary>
    /// <param name="context">The context to use.</param>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The identity of the slot.</returns>
    private static async Task<int> AddSlotAsync(ReviewCenterDbContext context, int projectId)
    {
        var slot = new ReviewerSlot { ProjectId = projectId, ReviewId = "REV-1", PrimaryUserId = 1 };
        context.ReviewerSlots.Add(slot);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return slot.Id;
    }

    /// <summary>
    ///     Builds a signature of the given slot and review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <returns>The signature, ready to add.</returns>
    private static ReviewSignature NewSignature(int projectId, string reviewId, int slotId)
    {
        return new ReviewSignature
        {
            ProjectId = projectId,
            ReviewId = reviewId,
            SlotId = slotId,
            UserId = 1,
            DisplayName = "Ann",
            Role = SlotRole.Primary,
            SignedAt = _time,
            RecordDigest = _digest,
            Meaning = "Meaning"
        };
    }

    /// <summary>
    ///     Test that deleting a thread deletes its comments and leaves the comments of other threads.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Comment_ThreadDeleted_DeletesItsComments()
    {
        // Arrange: a review with two threads of one comment each
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var first = await AddThreadAsync(db, projectId);
        var second = await AddThreadAsync(db, projectId);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);

        // Act: delete the first thread directly
        await context.Threads.Where(t => t.Id == first.Id).ExecuteDeleteAsync(cancellationToken);

        // Assert: only the comment of the second thread is left
        Assert.Equal([second.Id], await context.Comments.Select(c => c.ThreadId).ToListAsync(cancellationToken));
    }

    /// <summary>
    ///     Test that a thread row that names no side reads back with the side Head, stored as text.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Thread_SideNotGiven_DefaultsToHeadText()
    {
        // Arrange: a review, and a thread row inserted without the side column
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO Threads (ProjectId, ReviewId, FilePath, State, Created) VALUES ({projectId}, 'REV-1', 'a.txt', 'Open', '2025-03-04 05:06:07+00:00')",
            cancellationToken);

        // Act: read the raw column and the thread
        var sides = await context.Database
            .SqlQueryRaw<string>("SELECT Side AS Value FROM Threads")
            .ToListAsync(cancellationToken);
        var thread = Assert.Single(await db.Store.ListThreadsAsync(projectId, "REV-1", cancellationToken));

        // Assert: the stored text is Head
        Assert.Equal(["Head"], sides);
        Assert.Equal(ThreadSide.Head, thread.Side);
    }

    /// <summary>
    ///     Test that the table of threads has no column for the kind of a thread, which is derived from its comments.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Thread_Table_HasNoKindColumn()
    {
        // Arrange: a new database
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);

        // Act: read the column names of the threads table
        var columns = await context.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Threads')")
            .ToListAsync(cancellationToken);

        // Assert: the side and the state are columns, the kind is not
        Assert.Contains("Side", columns);
        Assert.Contains("State", columns);
        Assert.DoesNotContain("Kind", columns);
    }

    /// <summary>
    ///     Test that deleting a review deletes its checklist snapshot, its answers and its section states.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Checklist_ReviewDeleted_DeletesSnapshotAnswersAndSectionStates()
    {
        // Arrange: a review with a checklist snapshot, an answer and a section state
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);
        context.ReviewChecklists.Add(new ReviewChecklist { ProjectId = projectId, ReviewId = "REV-1", Version = "1", Definition = "{}" });
        context.ChecklistAnswers.Add(new ChecklistAnswer
        {
            ProjectId = projectId,
            ReviewId = "REV-1",
            ItemId = "SEC-1",
            Outcome = ChecklistOutcome.Pass,
            AnsweredBy = "Ann",
            AnsweredByUsername = "ann",
            AnsweredAt = _time
        });
        context.ChecklistSectionStates.Add(new ChecklistSectionState
        {
            ProjectId = projectId,
            ReviewId = "REV-1",
            SectionId = "sec",
            SetBy = "Ann",
            SetByUsername = "ann",
            SetAt = _time
        });
        await context.SaveChangesAsync(cancellationToken);

        // Act: delete the review directly
        await context.Reviews.Where(r => r.ProjectId == projectId && r.Id == "REV-1").ExecuteDeleteAsync(cancellationToken);

        // Assert: nothing of the checklist is left
        Assert.Equal(0, await context.ReviewChecklists.CountAsync(cancellationToken));
        Assert.Equal(0, await context.ChecklistAnswers.CountAsync(cancellationToken));
        Assert.Equal(0, await context.ChecklistSectionStates.CountAsync(cancellationToken));
    }

    /// <summary>
    ///     Test that deleting a review deletes the ballot status rows of its slots.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Ballot_ReviewDeleted_DeletesTheStatusRows()
    {
        // Arrange: a review with a slot that has started a ballot
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);
        var slotId = await AddSlotAsync(context, projectId);
        context.ChecklistBallots.Add(new ChecklistBallot
        {
            ProjectId = projectId,
            ReviewId = "REV-1",
            SlotId = slotId,
            Status = BallotStatus.Draft
        });
        await context.SaveChangesAsync(cancellationToken);

        // Act: delete the review directly
        await context.Reviews.Where(r => r.ProjectId == projectId && r.Id == "REV-1").ExecuteDeleteAsync(cancellationToken);

        // Assert: the status row is gone
        Assert.Equal(0, await context.ChecklistBallots.CountAsync(cancellationToken));
    }

    /// <summary>
    ///     Test that deleting the review that another review follows keeps the follow-up and its reference.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Review_FollowedReviewDeleted_KeepsTheFollowUp()
    {
        // Arrange: a review and a review that follows it
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await db.Store.AddReviewAsync(NewReview(projectId, "REV-2", "REV-1"), cancellationToken);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);

        // Act: delete the followed review directly
        await context.Reviews.Where(r => r.ProjectId == projectId && r.Id == "REV-1").ExecuteDeleteAsync(cancellationToken);

        // Assert: the follow-up is still there and still names the review it followed
        var followUp = await db.Store.GetReviewAsync(projectId, "REV-2", cancellationToken);
        Assert.Equal("REV-1", followUp?.FollowsReviewId);
    }

    /// <summary>
    ///     Test that the database refuses a signature for a review that does not exist.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Signature_UnknownReview_IsRefusedByDatabase()
    {
        // Arrange: a review with a slot
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);
        var slotId = await AddSlotAsync(context, projectId);

        // Act: add a signature of the existing slot that names a review that does not exist
        context.Signatures.Add(NewSignature(projectId, "NO-SUCH-REVIEW", slotId));

        // Assert: refused, and no signature exists
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(cancellationToken));
        context.ChangeTracker.Clear();
        Assert.Equal(0, await context.Signatures.CountAsync(cancellationToken));
    }

    /// <summary>
    ///     Test that the database refuses a signature for a slot that does not exist.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Signature_UnknownSlot_IsRefusedByDatabase()
    {
        // Arrange: a review without slots
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);

        // Act: add a signature of an existing review for a slot that does not exist
        context.Signatures.Add(NewSignature(projectId, "REV-1", 9999));

        // Assert: refused, and no signature exists
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(cancellationToken));
        context.ChangeTracker.Clear();
        Assert.Equal(0, await context.Signatures.CountAsync(cancellationToken));
    }

    /// <summary>
    ///     Test that an audit entry stays when the project and the review it describes are deleted.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_AuditEntry_ProjectDeleted_KeepsTheEntry()
    {
        // Arrange: an entry about a review
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);
        context.AuditEntries.Add(new AuditEntry
        {
            Sequence = 1000,
            TimestampUtc = _time,
            ActorName = "Ann",
            Action = "test.action",
            ProjectId = projectId,
            ReviewId = "REV-1",
            Detail = "Detail",
            Hash = _digest
        });
        await context.SaveChangesAsync(cancellationToken);

        // Act: delete the project, which deletes its reviews
        await context.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync(cancellationToken);

        // Assert: the review is gone and the entry is unchanged
        Assert.Equal(0, await context.Reviews.CountAsync(cancellationToken));
        var entry = await context.AuditEntries.AsNoTracking()
            .SingleAsync(e => e.Sequence == 1000, cancellationToken);
        Assert.Equal(
            ("test.action", projectId, "REV-1", "Detail"),
            (entry.Action, entry.ProjectId, entry.ReviewId, entry.Detail));
    }

    /// <summary>
    ///     Test that the database refuses a repository for a project that does not exist.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Repository_UnknownProject_IsRefusedByDatabase()
    {
        // Arrange: an empty database
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);

        // Act: add a repository of project 9999
        context.Repositories.Add(new RepositoryRecord
        {
            ProjectId = 9999,
            Name = "alpha",
            Location = "https://example.com/alpha.git",
            CreatedAt = _time,
            UpdatedAt = _time
        });

        // Assert: refused, and no repository exists
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(cancellationToken));
        context.ChangeTracker.Clear();
        Assert.Equal(0, await context.Repositories.CountAsync(cancellationToken));
    }

    /// <summary>
    ///     Test that the role of a membership is stored by its name.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Membership_Role_IsStoredByName()
    {
        // Arrange: a user and a project
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var ann = await db.Users.AddAsync(NewUser("ann"), cancellationToken);
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _time }, cancellationToken);

        // Act: make the user a project administrator and read the raw column
        await db.Store.AddMembershipAsync(ann.Id, project.Id, ProjectRole.ProjectAdmin, cancellationToken);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);
        var roles = await context.Database
            .SqlQueryRaw<string>("SELECT Role AS Value FROM ProjectMemberships")
            .ToListAsync(cancellationToken);

        // Assert: the name, not a number
        Assert.Equal(["ProjectAdmin"], roles);
    }

    /// <summary>
    ///     Test that the database refuses a membership for a user that does not exist.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Membership_UnknownUser_IsRefusedByDatabase()
    {
        // Arrange: a project and no user 9999
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _time }, cancellationToken);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);

        // Act: add a membership of the unknown user
        context.ProjectMemberships.Add(new ProjectMembership(9999, project.Id, ProjectRole.User));

        // Assert: refused, and no membership exists
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(cancellationToken));
        context.ChangeTracker.Clear();
        Assert.Equal(0, await context.ProjectMemberships.CountAsync(cancellationToken));
    }

    /// <summary>
    ///     Test that deleting a project deletes its memberships and leaves those of other projects.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Membership_ProjectDeleted_DeletesItsMemberships()
    {
        // Arrange: a user who is a member of two projects
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var ann = await db.Users.AddAsync(NewUser("ann"), cancellationToken);
        var apollo = await db.Store.AddProjectAsync(new Project { Name = "Apollo", Created = _time }, cancellationToken);
        var gemini = await db.Store.AddProjectAsync(new Project { Name = "Gemini", Created = _time }, cancellationToken);
        await db.Store.AddMembershipAsync(ann.Id, apollo.Id, cancellationToken);
        await db.Store.AddMembershipAsync(ann.Id, gemini.Id, cancellationToken);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);

        // Act: delete the first project directly
        await context.Projects.Where(p => p.Id == apollo.Id).ExecuteDeleteAsync(cancellationToken);

        // Assert: only the membership of the other project is left
        Assert.Equal([gemini.Id], await context.ProjectMemberships.Select(m => m.ProjectId).ToListAsync(cancellationToken));
    }

    /// <summary>
    ///     Test that deleting a user deletes the memberships of the user and leaves those of other users.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Membership_UserDeleted_DeletesTheMemberships()
    {
        // Arrange: two members of one project
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var ann = await db.Users.AddAsync(NewUser("ann"), cancellationToken);
        var bob = await db.Users.AddAsync(NewUser("bob"), cancellationToken);
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _time }, cancellationToken);
        await db.Store.AddMembershipAsync(ann.Id, project.Id, cancellationToken);
        await db.Store.AddMembershipAsync(bob.Id, project.Id, cancellationToken);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);

        // Act: delete the first user directly
        await context.Users.Where(u => u.Id == ann.Id).ExecuteDeleteAsync(cancellationToken);

        // Assert: only the membership of the other user is left
        Assert.Equal([bob.Id], await context.ProjectMemberships.Select(m => m.UserId).ToListAsync(cancellationToken));
    }

    /// <summary>
    ///     Test that the database holds one second-factor enrollment per user and refuses a second one.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_UserTotp_SecondEnrollmentOfUser_IsRefusedByDatabase()
    {
        // Arrange: a user with an enrollment
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var ann = await db.Users.AddAsync(NewUser("ann"), cancellationToken);
        await using (var first = await db.Factory.CreateDbContextAsync(cancellationToken))
        {
            first.UserTotps.Add(new UserTotp { UserId = ann.Id, ProtectedSecret = "secret-1", Created = _time });
            await first.SaveChangesAsync(cancellationToken);
        }

        // Act: add another enrollment for the same user
        await using var second = await db.Factory.CreateDbContextAsync(cancellationToken);
        second.UserTotps.Add(new UserTotp { UserId = ann.Id, ProtectedSecret = "secret-2", Created = _time });

        // Assert: refused, and the first enrollment is unchanged
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync(cancellationToken));
        await using var check = await db.Factory.CreateDbContextAsync(cancellationToken);
        Assert.Equal("secret-1", (await check.UserTotps.SingleAsync(cancellationToken)).ProtectedSecret);
    }

    /// <summary>
    ///     Test that a project that is added with independent reviewers switched off reads back with the rule off.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Project_AddedWithIndependentReviewersOff_ReadsBackOff()
    {
        // Arrange: an empty database
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();

        // Act: add a project with the rule off and read it back
        var added = await db.Store.AddProjectAsync(
            new Project { Name = "P", Created = _time, RequireIndependentReviewers = false },
            cancellationToken);
        var read = await db.Store.GetProjectAsync(added.Id, cancellationToken);
        var listed = (await db.Store.ListProjectsAsync(cancellationToken)).Single(p => p.Id == added.Id);

        // Assert: the stored false is not turned into the default
        Assert.False(read!.RequireIndependentReviewers);
        Assert.False(listed.RequireIndependentReviewers);
    }

    /// <summary>
    ///     Test that the database refuses a reopening of a review that does not exist.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_ReviewReopen_UnknownReview_IsRefusedByDatabase()
    {
        // Arrange: a project without that review
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);

        // Act: add a reopening of a review that does not exist
        context.ReviewReopens.Add(new ReviewReopen(0, projectId, "NO-SUCH-REVIEW", _time, "root", "Root", "Again", _digest));

        // Assert: refused, and no reopening exists
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(cancellationToken));
        context.ChangeTracker.Clear();
        Assert.Equal(0, await context.ReviewReopens.CountAsync(cancellationToken));
    }

    /// <summary>
    ///     Test that deleting a review deletes its reopenings.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_ReviewReopen_ReviewDeleted_DeletesTheReopenings()
    {
        // Arrange: a review with a reopening
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);
        context.ReviewReopens.Add(new ReviewReopen(0, projectId, "REV-1", _time, "root", "Root", "Again", _digest));
        await context.SaveChangesAsync(cancellationToken);

        // Act: delete the review directly
        await context.Reviews.Where(r => r.ProjectId == projectId && r.Id == "REV-1").ExecuteDeleteAsync(cancellationToken);

        // Assert: the reopening is gone
        Assert.Equal(0, await context.ReviewReopens.CountAsync(cancellationToken));
    }

    /// <summary>
    ///     Test that the due date of a review is stored as the calendar day in text form.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Review_DueDate_IsStoredAsCalendarDayText()
    {
        // Arrange: a review with a due date
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _time }, cancellationToken);
        await db.Store.AddReviewAsync(
            NewReview(project.Id, "REV-1", null, new DateOnly(2030, 12, 31)),
            cancellationToken);

        // Act: read the raw column
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);
        var texts = await context.Database
            .SqlQueryRaw<string>("SELECT DueDate AS Value FROM Reviews")
            .ToListAsync(cancellationToken);

        // Assert: the day, with no time and no zone
        Assert.Equal(["2030-12-31"], texts);
    }
}
