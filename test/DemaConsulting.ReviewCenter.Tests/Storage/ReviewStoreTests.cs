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
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     A real SQLite database in a temporary file together with a <see cref="ReviewStore"/> over it.
/// </summary>
internal sealed class TestStore : IDisposable
{
    /// <summary>
    ///     The service provider that owns the context factory.
    /// </summary>
    private readonly ServiceProvider _provider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TestStore"/> class with an empty, created database.
    /// </summary>
    public TestStore()
    {
        var options = new DatabaseOptions { ConnectionString = TestDatabase.NewConnectionString() };
        _provider = new ServiceCollection()
            .AddDbContextFactory<ReviewCenterDbContext>(builder => DatabaseSetup.Configure(builder, options))
            .BuildServiceProvider();
        var factory = _provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
        DatabaseSetup.EnsureCreated(factory, options);
        Store = new ReviewStore(factory);
    }

    /// <summary>
    ///     Gets the store under test.
    /// </summary>
    public ReviewStore Store { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();
    }
}

/// <summary>
///     Tests for <see cref="ReviewStore"/> against a real SQLite database file.
/// </summary>
public class ReviewStoreTests
{
    /// <summary>
    ///     The creation time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _created = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Builds a review with the given files for the store tests.
    /// </summary>
    /// <param name="projectId">The owning project.</param>
    /// <param name="id">The review id.</param>
    /// <param name="files">The files as path and content pairs.</param>
    /// <returns>The review, ready to add.</returns>
    private static Review NewReview(int projectId, string id, params (string Path, byte[] Content)[] files)
    {
        return new Review
        {
            ProjectId = projectId,
            Id = id,
            Title = "Title " + id,
            State = ReviewState.Draft,
            Created = _created,
            ContentFingerprint = Fingerprint.Compute(files.Select(file => file.Content)),
            Files = files
                .Select(file => new ReviewFile
                {
                    ProjectId = projectId,
                    ReviewId = id,
                    Path = file.Path,
                    Content = file.Content,
                    Size = file.Content.Length,
                    Sha256 = Fingerprint.ComputeFileHash(file.Content)
                })
                .ToList()
        };
    }

    /// <summary>
    ///     Test that a project round-trips through the database with an assigned id.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddProjectAsync_ValidProject_RoundTrips()
    {
        // Arrange: an empty database
        using var db = new TestStore();

        // Act: add a project and read it back
        var added = await db.Store.AddProjectAsync(
            new Project { Name = "Apollo", Description = "Moon", Created = _created },
            TestContext.Current.CancellationToken);
        var read = await db.Store.GetProjectAsync(added.Id, TestContext.Current.CancellationToken);

        // Assert: the id was assigned and every value survived
        Assert.True(added.Id > 0);
        Assert.NotNull(read);
        Assert.Equal("Apollo", read.Name);
        Assert.Equal("Moon", read.Description);
        Assert.Equal(_created, read.Created);
    }

    /// <summary>
    ///     Test that an identity supplied with a new project is ignored: the database assigns its own.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddProjectAsync_SuppliedId_IsIgnoredAndDatabaseAssignsOwn()
    {
        // Arrange: a database with one project, and a new project that carries an identity nobody assigned
        using var db = new TestStore();
        var existing = await db.Store.AddProjectAsync(
            new Project { Name = "Existing", Created = _created },
            TestContext.Current.CancellationToken);
        var supplied = new Project { Id = existing.Id, Name = "New", Created = _created };

        // Act: add the project that claims the id of the existing one, and a second claiming an unused id
        var added = await db.Store.AddProjectAsync(supplied, TestContext.Current.CancellationToken);
        var other = await db.Store.AddProjectAsync(
            new Project { Id = 4242, Name = "Other", Created = _created },
            TestContext.Current.CancellationToken);

        // Assert: both get new database-assigned ids, the existing project is untouched, and 4242 was not used
        Assert.NotEqual(existing.Id, added.Id);
        Assert.NotEqual(4242, other.Id);
        Assert.Equal("Existing", (await db.Store.GetProjectAsync(existing.Id, TestContext.Current.CancellationToken))?.Name);
        Assert.Equal("New", (await db.Store.GetProjectAsync(added.Id, TestContext.Current.CancellationToken))?.Name);
        Assert.Null(await db.Store.GetProjectAsync(4242, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that projects are listed by name.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ListProjectsAsync_SeveralProjects_OrdersByName()
    {
        // Arrange: projects added out of name order
        using var db = new TestStore();
        foreach (var name in new[] { "Gemini", "Apollo", "Mercury" })
        {
            await db.Store.AddProjectAsync(
                new Project { Name = name, Created = _created },
                TestContext.Current.CancellationToken);
        }

        // Act: list the projects
        var projects = await db.Store.ListProjectsAsync(TestContext.Current.CancellationToken);

        // Assert: ordered by name
        Assert.Equal(["Apollo", "Gemini", "Mercury"], projects.Select(p => p.Name));
    }

    /// <summary>
    ///     Test that an unknown project id returns nothing.
    /// </summary>
    [Fact]
    public async Task ReviewStore_GetProjectAsync_UnknownId_ReturnsNull()
    {
        // Arrange: an empty database
        using var db = new TestStore();

        // Act: read a project that does not exist
        var project = await db.Store.GetProjectAsync(99, TestContext.Current.CancellationToken);

        // Assert: nothing is returned
        Assert.Null(project);
    }

    /// <summary>
    ///     Test that file bytes, including binary, CR LF and empty files, come back exactly as stored.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddReviewAsync_BinaryCrLfAndEmptyFiles_StoresBytesVerbatim()
    {
        // Arrange: a project and a review with binary, text and empty files
        using var db = new TestStore();
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _created }, TestContext.Current.CancellationToken);
        var review = NewReview(project.Id, "REV-1", ("c.bin", TestContent.C), ("a.txt", TestContent.A), ("empty.txt", []));

        // Act: add the review and read each file's content back
        await db.Store.AddReviewAsync(review, TestContext.Current.CancellationToken);
        var binary = await db.Store.GetFileContentAsync(project.Id, "REV-1", "c.bin", TestContext.Current.CancellationToken);
        var text = await db.Store.GetFileContentAsync(project.Id, "REV-1", "a.txt", TestContext.Current.CancellationToken);
        var empty = await db.Store.GetFileContentAsync(project.Id, "REV-1", "empty.txt", TestContext.Current.CancellationToken);

        // Assert: byte for byte identical, and the empty file is an empty array rather than null
        Assert.Equal(TestContent.C, binary);
        Assert.Equal(TestContent.A, text);
        Assert.NotNull(empty);
        Assert.Empty(empty);
    }

    /// <summary>
    ///     Test that reading a review returns its metadata and file list ordered by path, without file content.
    /// </summary>
    [Fact]
    public async Task ReviewStore_GetReviewAsync_StoredReview_ReturnsMetadataWithoutContent()
    {
        // Arrange: a stored review with two files added out of path order
        using var db = new TestStore();
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _created }, TestContext.Current.CancellationToken);
        await db.Store.AddReviewAsync(
            NewReview(project.Id, "REV-1", ("b.txt", TestContent.B), ("a.txt", TestContent.A)),
            TestContext.Current.CancellationToken);

        // Act: read the review
        var review = await db.Store.GetReviewAsync(project.Id, "REV-1", TestContext.Current.CancellationToken);

        // Assert: values are intact, files are in path order with size and hash, and no content is loaded
        Assert.NotNull(review);
        Assert.Equal("Title REV-1", review.Title);
        Assert.Equal(ReviewState.Draft, review.State);
        Assert.Equal(_created, review.Created);
        Assert.Equal(Fingerprint.Compute(new[] { TestContent.A, TestContent.B }), review.ContentFingerprint);
        Assert.Equal(["a.txt", "b.txt"], review.Files.Select(f => f.Path));
        Assert.Equal([TestContent.HashA, TestContent.HashB], review.Files.Select(f => f.Sha256));
        Assert.Equal([TestContent.A.Length, TestContent.B.Length], review.Files.Select(f => (int)f.Size));
        Assert.All(review.Files, file => Assert.Empty(file.Content));
    }

    /// <summary>
    ///     Test that the same review id can exist in two projects and each keeps its own files.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddReviewAsync_SameIdInTwoProjects_KeepsThemApart()
    {
        // Arrange: two projects
        using var db = new TestStore();
        var first = await db.Store.AddProjectAsync(new Project { Name = "One", Created = _created }, TestContext.Current.CancellationToken);
        var second = await db.Store.AddProjectAsync(new Project { Name = "Two", Created = _created }, TestContext.Current.CancellationToken);

        // Act: add REV-1 with the same path but different content in each project
        await db.Store.AddReviewAsync(NewReview(first.Id, "REV-1", ("f.txt", TestContent.A)), TestContext.Current.CancellationToken);
        await db.Store.AddReviewAsync(NewReview(second.Id, "REV-1", ("f.txt", TestContent.B)), TestContext.Current.CancellationToken);
        var firstContent = await db.Store.GetFileContentAsync(first.Id, "REV-1", "f.txt", TestContext.Current.CancellationToken);
        var secondContent = await db.Store.GetFileContentAsync(second.Id, "REV-1", "f.txt", TestContext.Current.CancellationToken);

        // Assert: each project sees only its own bytes
        Assert.Equal(TestContent.A, firstContent);
        Assert.Equal(TestContent.B, secondContent);
    }

    /// <summary>
    ///     Test that adding a second review with the same project and id is rejected by the database key and
    ///     reported as the duplicate-id rule, which backs up the service's check against a race.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddReviewAsync_DuplicateKey_ThrowsReviewIdDuplicate()
    {
        // Arrange: a stored review
        using var db = new TestStore();
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _created }, TestContext.Current.CancellationToken);
        await db.Store.AddReviewAsync(NewReview(project.Id, "REV-1", ("a.txt", TestContent.A)), TestContext.Current.CancellationToken);

        // Act: add another review with the same key directly, skipping the service pre-check
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => db.Store.AddReviewAsync(NewReview(project.Id, "REV-1", ("b.txt", TestContent.B)), TestContext.Current.CancellationToken));

        // Assert: reported as a duplicate id, and the original review is unchanged
        Assert.Equal(DomainRule.ReviewIdDuplicate, exception.Rule);
        var review = await db.Store.GetReviewAsync(project.Id, "REV-1", TestContext.Current.CancellationToken);
        Assert.Equal(["a.txt"], review?.Files.Select(f => f.Path));
    }

    /// <summary>
    ///     Test that a review in a project that does not exist is not stored (the foreign key is enforced).
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddReviewAsync_UnknownProject_IsNotStored()
    {
        // Arrange: an empty database
        using var db = new TestStore();

        // Act: add a review for project 7, which does not exist
        await Assert.ThrowsAsync<DbUpdateException>(
            () => db.Store.AddReviewAsync(NewReview(7, "REV-1", ("a.txt", TestContent.A)), TestContext.Current.CancellationToken));

        // Assert: the review does not exist afterwards
        Assert.False(await db.Store.ReviewExistsAsync(7, "REV-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that existence checks and listing are scoped to one project.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ListReviewsAsync_TwoProjects_ReturnsOnlyOwnReviewsOrderedById()
    {
        // Arrange: reviews in two projects
        using var db = new TestStore();
        var first = await db.Store.AddProjectAsync(new Project { Name = "One", Created = _created }, TestContext.Current.CancellationToken);
        var second = await db.Store.AddProjectAsync(new Project { Name = "Two", Created = _created }, TestContext.Current.CancellationToken);
        await db.Store.AddReviewAsync(NewReview(first.Id, "B-2", ("a.txt", TestContent.A)), TestContext.Current.CancellationToken);
        await db.Store.AddReviewAsync(NewReview(first.Id, "A-1", ("a.txt", TestContent.A)), TestContext.Current.CancellationToken);
        await db.Store.AddReviewAsync(NewReview(second.Id, "C-3", ("a.txt", TestContent.A)), TestContext.Current.CancellationToken);

        // Act: list the first project's reviews and check existence across projects
        var reviews = await db.Store.ListReviewsAsync(first.Id, TestContext.Current.CancellationToken);
        var existsInSecond = await db.Store.ReviewExistsAsync(second.Id, "A-1", TestContext.Current.CancellationToken);

        // Assert: only the first project's reviews, ordered by id; the existence check is project-scoped
        Assert.Equal(["A-1", "B-2"], reviews.Select(r => r.Id));
        Assert.False(existsInSecond);
    }

    /// <summary>
    ///     Test that an unknown review or file returns nothing.
    /// </summary>
    [Fact]
    public async Task ReviewStore_GetReviewAsync_UnknownReviewOrFile_ReturnsNull()
    {
        // Arrange: a project with one review
        using var db = new TestStore();
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _created }, TestContext.Current.CancellationToken);
        await db.Store.AddReviewAsync(NewReview(project.Id, "REV-1", ("a.txt", TestContent.A)), TestContext.Current.CancellationToken);

        // Act: read an unknown review and an unknown file of a known review
        var review = await db.Store.GetReviewAsync(project.Id, "NOPE", TestContext.Current.CancellationToken);
        var content = await db.Store.GetFileContentAsync(project.Id, "REV-1", "missing.txt", TestContext.Current.CancellationToken);

        // Assert: nothing is returned for either
        Assert.Null(review);
        Assert.Null(content);
    }

    /// <summary>
    ///     Builds a thread with one comment for the store tests.
    /// </summary>
    /// <param name="projectId">The owning project.</param>
    /// <param name="reviewId">The review id.</param>
    /// <param name="filePath">The file the thread is anchored to.</param>
    /// <param name="start">The first line, or <c>null</c> for the whole file.</param>
    /// <param name="end">The last line, or <c>null</c> for the whole file.</param>
    /// <param name="text">The text of the first comment.</param>
    /// <returns>The thread, ready to add.</returns>
    private static ReviewThread NewThread(int projectId, string reviewId, string filePath, int? start, int? end, string text)
    {
        return new ReviewThread
        {
            Id = 9001, // supplied identities must be ignored; the store assigns its own
            ProjectId = projectId,
            ReviewId = reviewId,
            FilePath = filePath,
            StartLine = start,
            EndLine = end,
            State = ReviewThreadState.Open,
            Created = _created,
            Comments = [new ReviewComment { Id = 9002, ThreadId = 9003, Author = "Ann", AuthorUsername = "ann", Text = text, Created = _created }]
        };
    }

    /// <summary>
    ///     Adds a project with a review of the files <c>a.txt</c> and <c>b.txt</c>.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="reviewId">The review id.</param>
    /// <returns>The identity of the project.</returns>
    private static async Task<int> AddReviewAsync(TestStore db, string reviewId = "REV-1")
    {
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _created }, TestContext.Current.CancellationToken);
        await db.Store.AddReviewAsync(
            NewReview(project.Id, reviewId, ("a.txt", TestContent.A), ("b.txt", TestContent.B)), TestContext.Current.CancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Test that whole-file and line-range threads round-trip with their comments and database-assigned
    ///     identities, whatever identities the caller supplied.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddThreadAsync_WholeFileAndRange_RoundTripsWithComments()
    {
        // Arrange: a review with two files
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);

        // Act: add a whole-file thread (with an ignored identity) and a range thread, then list them
        var whole = await db.Store.AddThreadAsync(
            NewThread(projectId, "REV-1", "a.txt", null, null, "Whole"), TestContext.Current.CancellationToken);
        var range = await db.Store.AddThreadAsync(
            NewThread(projectId, "REV-1", "b.txt", 2, 5, "Range"), TestContext.Current.CancellationToken);
        var listed = await db.Store.ListThreadsAsync(projectId, "REV-1", TestContext.Current.CancellationToken);

        // Assert: identities are assigned and growing, and every value survived
        Assert.True(whole.Id > 0);
        Assert.True(range.Id > whole.Id);
        Assert.DoesNotContain(9001, new[] { whole.Id, range.Id });
        Assert.Equal([whole.Id, range.Id], listed.Select(t => t.Id));
        Assert.Null(listed[0].StartLine);
        Assert.Null(listed[0].EndLine);
        Assert.Equal((2, 5), (listed[1].StartLine, listed[1].EndLine));
        Assert.Equal("b.txt", listed[1].FilePath);
        Assert.Equal(ReviewThreadState.Open, listed[1].State);
        Assert.Equal(_created, listed[1].Created);
        var comment = Assert.Single(listed[1].Comments);
        Assert.True(comment.Id > 0);
        Assert.NotEqual(9002, comment.Id);
        Assert.Equal(range.Id, comment.ThreadId);
        Assert.Equal(("Ann", "ann", "Range", _created), (comment.Author, comment.AuthorUsername, comment.Text, comment.Created));
    }

    /// <summary>
    ///     Test that comments are appended in posting order.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddCommentAsync_ExistingThread_AppendsInOrder()
    {
        // Arrange: a thread with one comment
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var thread = await db.Store.AddThreadAsync(
            NewThread(projectId, "REV-1", "a.txt", null, null, "First"), TestContext.Current.CancellationToken);

        // Act: add two comments and list
        var second = await db.Store.AddCommentAsync(
            projectId, "REV-1", thread.Id, new ReviewComment { Author = "Bob", Text = "Second", Created = _created }, TestContext.Current.CancellationToken);
        await db.Store.AddCommentAsync(
            projectId, "REV-1", thread.Id, new ReviewComment { Author = "Cy", Text = "Third", Created = _created }, TestContext.Current.CancellationToken);
        var listed = Assert.Single(await db.Store.ListThreadsAsync(projectId, "REV-1", TestContext.Current.CancellationToken));

        // Assert: the stored comment has an identity and the comments are in posting order
        Assert.NotNull(second);
        Assert.Equal(thread.Id, second.ThreadId);
        Assert.Equal(["First", "Second", "Third"], listed.Comments.Select(c => c.Text));
    }

    /// <summary>
    ///     Test that a comment cannot be added to an unknown thread or to a thread of another review.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddCommentAsync_UnknownOrOtherReviewThread_ReturnsNull()
    {
        // Arrange: a thread in REV-1 and a second review
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await db.Store.AddReviewAsync(NewReview(projectId, "REV-2", ("a.txt", TestContent.A)), TestContext.Current.CancellationToken);
        var thread = await db.Store.AddThreadAsync(
            NewThread(projectId, "REV-1", "a.txt", null, null, "First"), TestContext.Current.CancellationToken);
        var comment = new ReviewComment { Author = "Bob", Text = "Second", Created = _created };

        // Act: comment on an unknown thread and on the thread through the other review
        var unknown = await db.Store.AddCommentAsync(projectId, "REV-1", thread.Id + 100, comment, TestContext.Current.CancellationToken);
        var other = await db.Store.AddCommentAsync(projectId, "REV-2", thread.Id, comment, TestContext.Current.CancellationToken);

        // Assert: nothing is added
        Assert.Null(unknown);
        Assert.Null(other);
        Assert.Single(Assert.Single(await db.Store.ListThreadsAsync(projectId, "REV-1", TestContext.Current.CancellationToken)).Comments);
    }

    /// <summary>
    ///     Test that the state of a thread can be changed back and forth, and that an unknown thread reports false.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetThreadStateAsync_ExistingAndUnknown_ChangesStateOrReturnsFalse()
    {
        // Arrange: a thread
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var thread = await db.Store.AddThreadAsync(
            NewThread(projectId, "REV-1", "a.txt", null, null, "First"), TestContext.Current.CancellationToken);

        // Act: resolve, read, reopen, read, and change an unknown thread and a thread through another review
        var resolved = await db.Store.SetThreadStateAsync(projectId, "REV-1", thread.Id, ReviewThreadState.Resolved, TestContext.Current.CancellationToken);
        var afterResolve = (await db.Store.ListThreadsAsync(projectId, "REV-1", TestContext.Current.CancellationToken)).Single().State;
        var reopened = await db.Store.SetThreadStateAsync(projectId, "REV-1", thread.Id, ReviewThreadState.Open, TestContext.Current.CancellationToken);
        var afterReopen = (await db.Store.ListThreadsAsync(projectId, "REV-1", TestContext.Current.CancellationToken)).Single().State;
        var unknown = await db.Store.SetThreadStateAsync(projectId, "REV-1", thread.Id + 100, ReviewThreadState.Resolved, TestContext.Current.CancellationToken);
        var otherReview = await db.Store.SetThreadStateAsync(projectId, "REV-2", thread.Id, ReviewThreadState.Resolved, TestContext.Current.CancellationToken);

        // Assert: the states changed, and unknown threads report false
        Assert.True(resolved);
        Assert.Equal(ReviewThreadState.Resolved, afterResolve);
        Assert.True(reopened);
        Assert.Equal(ReviewThreadState.Open, afterReopen);
        Assert.False(unknown);
        Assert.False(otherReview);
    }

    /// <summary>
    ///     Test that listing returns only the threads of the asked review, in creation order.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ListThreadsAsync_TwoReviews_ReturnsOnlyOwnThreadsInOrder()
    {
        // Arrange: threads in two reviews of one project, interleaved
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await db.Store.AddReviewAsync(NewReview(projectId, "REV-2", ("a.txt", TestContent.A)), TestContext.Current.CancellationToken);
        await db.Store.AddThreadAsync(NewThread(projectId, "REV-1", "a.txt", null, null, "One"), TestContext.Current.CancellationToken);
        await db.Store.AddThreadAsync(NewThread(projectId, "REV-2", "a.txt", null, null, "Other"), TestContext.Current.CancellationToken);
        await db.Store.AddThreadAsync(NewThread(projectId, "REV-1", "b.txt", 1, 1, "Two"), TestContext.Current.CancellationToken);

        // Act: list each review
        var first = await db.Store.ListThreadsAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        var second = await db.Store.ListThreadsAsync(projectId, "REV-2", TestContext.Current.CancellationToken);
        var none = await db.Store.ListThreadsAsync(projectId, "REV-9", TestContext.Current.CancellationToken);

        // Assert: each review sees only its own threads, in creation order
        Assert.Equal(["One", "Two"], first.Select(t => t.Comments.Single().Text));
        Assert.Equal(["Other"], second.Select(t => t.Comments.Single().Text));
        Assert.Empty(none);
    }

    /// <summary>
    ///     Test that the database refuses a thread for a file that is not in the review (the foreign key is
    ///     enforced), and stores nothing.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddThreadAsync_UnknownFile_IsRefusedByDatabase()
    {
        // Arrange: a review with two files
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);

        // Act: add a thread for a file that does not exist
        await Assert.ThrowsAsync<DbUpdateException>(() => db.Store.AddThreadAsync(
            NewThread(projectId, "REV-1", "missing.txt", null, null, "Text"), TestContext.Current.CancellationToken));

        // Assert: no thread exists afterwards
        Assert.Empty(await db.Store.ListThreadsAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Builds a checklist snapshot for a review.
    /// </summary>
    /// <param name="projectId">The owning project.</param>
    /// <param name="reviewId">The review id.</param>
    /// <returns>The snapshot of the default checklist.</returns>
    private static ReviewChecklist NewChecklist(int projectId, string reviewId)
    {
        return new ReviewChecklist
        {
            ProjectId = projectId,
            ReviewId = reviewId,
            Version = DefaultChecklist.Version,
            Definition = DefaultChecklist.Current.ToJson()
        };
    }

    /// <summary>
    ///     Builds a checklist answer.
    /// </summary>
    /// <param name="projectId">The owning project.</param>
    /// <param name="itemId">The check identifier.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="justification">The justification, if any.</param>
    /// <returns>The answer.</returns>
    private static ChecklistAnswer NewAnswer(int projectId, string itemId, ChecklistOutcome outcome, string? justification)
    {
        return new ChecklistAnswer
        {
            ProjectId = projectId,
            ReviewId = "REV-1",
            ItemId = itemId,
            Outcome = outcome,
            Justification = justification,
            AnsweredBy = "Ann",
            AnsweredByUsername = "ann",
            AnsweredAt = _created
        };
    }

    /// <summary>
    ///     Test that a review added with a checklist snapshot stores the snapshot together with it.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddReview_WithChecklist_StoresSnapshotAtomically()
    {
        // Arrange: a project and a review that carries a checklist snapshot
        using var db = new TestStore();
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _created }, TestContext.Current.CancellationToken);
        var review = NewReview(project.Id, "REV-1", ("a.txt", TestContent.A));
        review = new Review
        {
            ProjectId = review.ProjectId,
            Id = review.Id,
            Title = review.Title,
            State = review.State,
            Created = review.Created,
            ContentFingerprint = review.ContentFingerprint,
            Files = review.Files,
            Checklist = NewChecklist(project.Id, "REV-1")
        };

        // Act: add the review and read the snapshot back
        await db.Store.AddReviewAsync(review, TestContext.Current.CancellationToken);
        var checklist = await db.Store.GetChecklistAsync(project.Id, "REV-1", TestContext.Current.CancellationToken);

        // Assert: the snapshot and its version are stored, and parse to the default checklist
        Assert.NotNull(checklist);
        Assert.Equal(DefaultChecklist.Version, checklist.Version);
        Assert.Equal(58, checklist.Parse().Sections.Sum(section => section.Items.Count));
    }

    /// <summary>
    ///     Test that a review stored without a snapshot, and an unknown review, have no checklist.
    /// </summary>
    [Fact]
    public async Task ReviewStore_GetChecklist_ReviewWithoutSnapshot_ReturnsNull()
    {
        // Arrange: a review without a snapshot
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);

        // Act, Assert: neither the review nor an unknown one has a checklist
        Assert.Null(await db.Store.GetChecklistAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Null(await db.Store.GetChecklistAsync(projectId, "nope", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that answering a check again replaces the answer instead of adding a second row.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetChecklistAnswer_UpsertsSingleRowPerItem()
    {
        // Arrange: a review
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);

        // Act: answer a check, answer it again, and answer another one
        await db.Store.SetChecklistAnswerAsync(NewAnswer(projectId, "REQ-01", ChecklistOutcome.Pass, null), TestContext.Current.CancellationToken);
        await db.Store.SetChecklistAnswerAsync(NewAnswer(projectId, "REQ-01", ChecklistOutcome.Fail, "Broken"), TestContext.Current.CancellationToken);
        await db.Store.SetChecklistAnswerAsync(NewAnswer(projectId, "REQ-02", ChecklistOutcome.NotApplicable, "None"), TestContext.Current.CancellationToken);
        var answers = await db.Store.ListChecklistAnswersAsync(projectId, "REV-1", TestContext.Current.CancellationToken);

        // Assert: one row per check, the first one holding the latest answer
        Assert.Equal(2, answers.Count);
        var first = answers.Single(a => a.ItemId == "REQ-01");
        Assert.Equal(ChecklistOutcome.Fail, first.Outcome);
        Assert.Equal("Broken", first.Justification);
    }

    /// <summary>
    ///     Test that setting the applicability of a section again replaces the state instead of adding a row.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetChecklistSectionState_UpsertsSingleRowPerSection()
    {
        // Arrange: a review
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        ChecklistSectionState State(bool applicable, string? reason) => new()
        {
            ProjectId = projectId,
            ReviewId = "REV-1",
            SectionId = "DES",
            Applicable = applicable,
            Reason = reason,
            SetBy = "Ann",
            SetByUsername = "ann",
            SetAt = _created
        };

        // Act: mark the section as not applicable, then as applicable again
        await db.Store.SetChecklistSectionStateAsync(State(false, "No design"), TestContext.Current.CancellationToken);
        var notApplicable = await db.Store.ListChecklistSectionStatesAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        await db.Store.SetChecklistSectionStateAsync(State(true, null), TestContext.Current.CancellationToken);
        var applicable = await db.Store.ListChecklistSectionStatesAsync(projectId, "REV-1", TestContext.Current.CancellationToken);

        // Assert: one row each time, holding the latest state
        Assert.Equal("No design", Assert.Single(notApplicable).Reason);
        var stored = Assert.Single(applicable);
        Assert.True(stored.Applicable);
        Assert.Equal(("Ann", "ann"), (stored.SetBy, stored.SetByUsername));
        Assert.Null(stored.Reason);
    }

    /// <summary>
    ///     Test that the outcome and the times of checklist rows round-trip, and that rows are kept per review.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ChecklistRows_RoundTripOutcomeAndTimestamps()
    {
        // Arrange: two reviews in one project
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await db.Store.AddReviewAsync(NewReview(projectId, "REV-2", ("a.txt", TestContent.A)), TestContext.Current.CancellationToken);

        // Act: answer a check of the first review with a time that has an offset of zero
        await db.Store.SetChecklistAnswerAsync(
            NewAnswer(projectId, "REQ-01", ChecklistOutcome.NotApplicable, "Not relevant"), TestContext.Current.CancellationToken);
        var first = await db.Store.ListChecklistAnswersAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        var second = await db.Store.ListChecklistAnswersAsync(projectId, "REV-2", TestContext.Current.CancellationToken);

        // Assert: outcome, name and time survived, and the other review has no answers
        var answer = Assert.Single(first);
        Assert.Equal(ChecklistOutcome.NotApplicable, answer.Outcome);
        Assert.Equal("Ann", answer.AnsweredBy);
        Assert.Equal("ann", answer.AnsweredByUsername);
        Assert.Equal(_created, answer.AnsweredAt);
        Assert.Empty(second);
    }
}
