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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests of the follow-up review in <see cref="ReviewService"/>: the eligibility of the earlier review, what is
///     carried over at both create paths, the acknowledgement of dropped threads, the audit text, and the way carried
///     rows behave afterwards (re-answering, independence, signing blockers, the lock) over in-memory stores.
/// </summary>
public class ReviewServiceFollowUpTests
{
    /// <summary>
    ///     The password every test user has.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The head commit of the repository reviews.
    /// </summary>
    private const string HeadOne = "1111111111111111111111111111111111111111";

    /// <summary>
    ///     A second head commit, where <c>src/code.txt</c> has other content.
    /// </summary>
    private const string HeadTwo = "2222222222222222222222222222222222222222";

    /// <summary>
    ///     The instant at which the clock starts.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The user who administers checklists and slots in the tests.
    /// </summary>
    private static readonly Actor _cyd = new("cyd", "Cyd");

    /// <summary>
    ///     The arranged world: services over fakes, three users, two projects and two repositories.
    /// </summary>
    /// <param name="Store">The review store.</param>
    /// <param name="Clock">The clock.</param>
    /// <param name="Reviews">The review service.</param>
    /// <param name="Checklists">The checklist service.</param>
    /// <param name="Threads">The thread service.</param>
    /// <param name="Signing">The signing service.</param>
    /// <param name="ProjectChecklists">The service that changes the checklist of a project.</param>
    /// <param name="Repositories">The repository service.</param>
    /// <param name="Git">The fake git provider.</param>
    /// <param name="ProjectId">The project the tests work in.</param>
    /// <param name="OtherProjectId">Another project.</param>
    /// <param name="RepositoryId">The first repository of the project.</param>
    /// <param name="OtherRepositoryId">The second repository of the project.</param>
    /// <param name="Ids">The identities of the users, by user name.</param>
    private sealed record World(
        FakeReviewStore Store,
        MovableTimeProvider Clock,
        ReviewService Reviews,
        ChecklistService Checklists,
        ThreadService Threads,
        SigningService Signing,
        ProjectChecklistService ProjectChecklists,
        RepositoryService Repositories,
        FakeGitRepositoryProvider Git,
        int ProjectId,
        int OtherProjectId,
        int RepositoryId,
        int OtherRepositoryId,
        IReadOnlyDictionary<string, int> Ids)
    {
        /// <summary>
        ///     Gets or sets the stored text of the checklist definition of the project, for the next change.
        /// </summary>
        public string? ChecklistJson { get; set; }
    }

    /// <summary>
    ///     Builds a checklist definition with two sections (<c>a</c> with two checks, <c>b</c> with one).
    /// </summary>
    /// <param name="version">The version label.</param>
    /// <param name="firstText">The wording of the check <c>a-01</c>.</param>
    /// <returns>The JSON text of the definition.</returns>
    private static string Definition(string version = "v1", string firstText = "First")
    {
        return new ChecklistDefinition(
            version,
            [
                new ChecklistSection("a", "Alpha", string.Empty, [new ChecklistItem("a-01", firstText), new ChecklistItem("a-02", "Second")]),
                new ChecklistSection("b", "Beta", string.Empty, [new ChecklistItem("b-01", "Third")])
            ]).ToJson();
    }

    /// <summary>
    ///     Arranges the world with the checklist <see cref="Definition"/> set on the project.
    /// </summary>
    /// <returns>The world.</returns>
    private static async Task<World> CreateAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var clock = new MovableTimeProvider(_now);
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), clock);
        var ids = new Dictionary<string, int>();
        foreach (var (username, name) in new[] { ("ann", "Ann"), ("bob", "Bob"), ("cyd", "Cyd") })
        {
            ids[username] = (await users.CreateAsync(username, name, UserRole.Reviewer, Password, cancellationToken)).Id;
        }

        var projects = TestServices.Projects(store, clock);
        var project = await projects.CreateAsync("Apollo", null, cancellationToken);
        var other = await projects.CreateAsync("Other", null, cancellationToken);
        foreach (var id in ids.Values)
        {
            await store.AddMembershipAsync(id, project.Id, cancellationToken);
            await store.AddMembershipAsync(id, other.Id, cancellationToken);
        }

        var repositories = TestServices.Repositories(store, clock);
        var first = await repositories.CreateAsync(project.Id, "main-repo", "https://example.com/org/repo.git", null, null, TestActors.Ann, cancellationToken);
        var second = await repositories.CreateAsync(project.Id, "other-repo", "https://example.com/org/other.git", null, null, TestActors.Ann, cancellationToken);
        var git = new FakeGitRepositoryProvider();
        git.Revisions["main"] = HeadOne;
        git.Revisions["next"] = HeadTwo;
        git.Script(null, HeadOne, [("src/code.txt", ChangeKind.Added, null, "alpha\n"), ("src/other.txt", ChangeKind.Added, null, "beta\n")]);
        git.Script(null, HeadTwo, [("src/code.txt", ChangeKind.Added, null, "changed\n")]);
        var world = new World(
            store,
            clock,
            TestServices.Reviews(store, clock, repositories, git),
            TestServices.Checklists(store, clock),
            TestServices.Threads(store, clock),
            TestServices.Signing(store, users, clock),
            TestServices.ProjectChecklists(store),
            repositories,
            git,
            project.Id,
            other.Id,
            first.Id,
            second.Id,
            ids);
        await SetChecklistAsync(world, Definition());
        return world;
    }

    /// <summary>
    ///     Sets the checklist definition of the project.
    /// </summary>
    /// <param name="w">The world.</param>
    /// <param name="json">The new definition.</param>
    /// <returns>A task that completes when the definition is stored.</returns>
    private static async Task SetChecklistAsync(World w, string json)
    {
        Assert.True(await w.ProjectChecklists.SetAsync(
            _cyd, w.ProjectId, ProjectChecklistService.HashOf(w.ChecklistJson), json, TestContext.Current.CancellationToken));
        w.ChecklistJson = json;
    }

    /// <summary>
    ///     Creates an upload review of one or more files.
    /// </summary>
    /// <param name="w">The world.</param>
    /// <param name="id">The review id.</param>
    /// <param name="files">The paths of the files; every file has the content <see cref="TestContent.A"/>.</param>
    /// <param name="follows">The review to follow, or <c>null</c>.</param>
    /// <param name="acknowledge">Whether dropped threads are acknowledged.</param>
    /// <param name="creator">Who creates the review; Ann when omitted.</param>
    /// <param name="projectId">The project; the main project when omitted.</param>
    /// <returns>The created review.</returns>
    private static Task<Review> UploadAsync(
        World w,
        string id,
        string[]? files = null,
        string? follows = null,
        bool acknowledge = false,
        Actor? creator = null,
        int? projectId = null)
    {
        return w.Reviews.CreateDraftAsync(
            projectId ?? w.ProjectId,
            id,
            "Review " + id,
            (files ?? ["a.txt"]).Select(path => new UploadedFile(path, TestContent.A)).ToList(),
            creator ?? TestActors.Ann,
            TestContext.Current.CancellationToken,
            null,
            follows,
            acknowledge);
    }

    /// <summary>
    ///     Creates a review from a repository.
    /// </summary>
    /// <param name="w">The world.</param>
    /// <param name="id">The review id.</param>
    /// <param name="revision">The head revision: <c>main</c> or <c>next</c>.</param>
    /// <param name="follows">The review to follow, or <c>null</c>.</param>
    /// <param name="repositoryId">The repository; the first one when omitted.</param>
    /// <param name="selection">The file selection.</param>
    /// <param name="acknowledge">Whether dropped threads are acknowledged.</param>
    /// <returns>The created review.</returns>
    private static async Task<Review> FromRepositoryAsync(
        World w,
        string id,
        string revision = "main",
        string? follows = null,
        int? repositoryId = null,
        string selection = "**/*",
        bool acknowledge = false)
    {
        var result = await w.Reviews.CreateFromRepositoryAsync(
            w.ProjectId,
            id,
            "Review " + id,
            new RepositorySource(repositoryId ?? w.RepositoryId, revision, null, selection),
            TestActors.Ann,
            TestContext.Current.CancellationToken,
            null,
            follows,
            acknowledge);
        return result.Review;
    }

    /// <summary>
    ///     Answers a check.
    /// </summary>
    /// <param name="w">The world.</param>
    /// <param name="review">The review.</param>
    /// <param name="item">The check id.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="by">Who answers; Bob when omitted.</param>
    /// <returns>A task that completes when the answer is stored.</returns>
    private static Task AnswerAsync(World w, string review, string item, ChecklistOutcome outcome = ChecklistOutcome.Pass, Actor? by = null)
    {
        return w.Checklists.SetAnswerAsync(
            w.ProjectId,
            review,
            item,
            outcome,
            outcome == ChecklistOutcome.Pass ? null : "Because",
            by ?? TestActors.Bob,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Answers every check of the checklist as Pass.
    /// </summary>
    /// <param name="w">The world.</param>
    /// <param name="review">The review.</param>
    /// <returns>A task that completes when all checks are answered.</returns>
    private static async Task AnswerAllAsync(World w, string review)
    {
        foreach (var item in new[] { "a-01", "a-02", "b-01" })
        {
            await AnswerAsync(w, review, item);
        }
    }

    /// <summary>
    ///     Starts a thread on a whole file.
    /// </summary>
    /// <param name="w">The world.</param>
    /// <param name="review">The review.</param>
    /// <param name="path">The file.</param>
    /// <param name="line">The line to anchor to, or <c>null</c> for the whole file.</param>
    /// <returns>The thread.</returns>
    private static Task<ReviewThread> ThreadAsync(World w, string review, string path = "a.txt", int? line = 1)
    {
        return w.Threads.StartThreadAsync(
            w.ProjectId,
            review,
            new ThreadAnchor(path, line, line),
            TestActors.Bob,
            "Please look",
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Reads the record of a review.
    /// </summary>
    /// <param name="w">The world.</param>
    /// <param name="review">The review.</param>
    /// <returns>The record.</returns>
    private static async Task<ReviewRecord> RecordAsync(World w, string review)
    {
        return (await w.Store.GetRecordAsync(w.ProjectId, review, TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    ///     Gets the audit detail of the creation of a review.
    /// </summary>
    /// <param name="w">The world.</param>
    /// <param name="review">The review.</param>
    /// <returns>The detail.</returns>
    private static string CreatedDetail(World w, string review)
    {
        return Assert.Single(w.Store.Audits, a => a.Action == "review.created" && a.ReviewId == review).Detail;
    }

    /// <summary>
    ///     Runs a creation that must be refused and checks the rule.
    /// </summary>
    /// <param name="action">The creation.</param>
    /// <returns>The exception.</returns>
    private static async Task<DomainRuleException> RefusedAsync(Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<DomainRuleException>(action);
        Assert.Equal(DomainRule.FollowUpInvalid, exception.Rule);
        return exception;
    }

    /// <summary>
    ///     Test that an upload follow-up carries the Pass and not-applicable answers of the earlier review with the
    ///     original answerer, drops the Fail, keeps the section that does not apply, counts the carried rows on the
    ///     review, and leaves the earlier review as it was.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_FollowUp_CarriesAnswersAndSections()
    {
        // Arrange: Bob answered Pass, Fail and N/A, and section b does not apply
        var w = await CreateAsync();
        await UploadAsync(w, "rev-1");
        await AnswerAsync(w, "rev-1", "a-01");
        await AnswerAsync(w, "rev-1", "a-02", ChecklistOutcome.Fail);
        await w.Checklists.SetSectionApplicabilityAsync(w.ProjectId, "rev-1", "b", false, "Not relevant", TestActors.Bob, TestContext.Current.CancellationToken);
        var before = RecordDigest.Compute(await RecordAsync(w, "rev-1"));

        // Act: Ann creates the follow-up
        var review = await UploadAsync(w, "rev-2", follows: "rev-1");

        // Assert: one answer (a-01) and the section are carried; the Fail is open again
        var record = await RecordAsync(w, "rev-2");
        var answer = Assert.Single(record.Answers);
        Assert.Equal("a-01", answer.ItemId);
        Assert.Equal("Bob", answer.AnsweredBy);
        Assert.Equal("bob", answer.AnsweredByUsername);
        Assert.Equal("rev-1", answer.CarriedFromReviewId);
        var state = Assert.Single(record.SectionStates);
        Assert.Equal(("b", "Not relevant", "rev-1"), (state.SectionId, state.Reason, state.CarriedFromReviewId));
        Assert.Equal("rev-1", record.FollowsReviewId);
        Assert.Equal("rev-1", review.FollowsReviewId);
        Assert.Equal(1, review.CarriedAnswerCount);
        Assert.Equal(0, review.CarriedThreadCount);
        Assert.Equal("rev-1", (await w.Reviews.GetAsync(w.ProjectId, "rev-2", TestContext.Current.CancellationToken))!.FollowsReviewId);
        Assert.Equal(before, RecordDigest.Compute(await RecordAsync(w, "rev-1")));
        Assert.Null((await RecordAsync(w, "rev-1")).FollowsReviewId);

        // Assert: the checklist state names the review a section was carried from, and nothing for the others
        var checklist = (await w.Checklists.GetAsync(w.ProjectId, "rev-2", TestContext.Current.CancellationToken))!;
        Assert.Equal("rev-1", checklist.CarriedFromFor("b"));
        Assert.Null(checklist.CarriedFromFor("a"));
    }

    /// <summary>
    ///     Test that the carried answers make the new review's own checklist count them as answered, and that the
    ///     creator did not become the answerer of anything.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_FollowUp_CarriedAnswersCountAsAnswered()
    {
        // Arrange: everything passed in the earlier review
        var w = await CreateAsync();
        await UploadAsync(w, "rev-1");
        await AnswerAllAsync(w, "rev-1");

        // Act
        await UploadAsync(w, "rev-2", follows: "rev-1");

        // Assert: the checklist is complete and nobody but Bob answered
        var state = (await w.Checklists.GetAsync(w.ProjectId, "rev-2", TestContext.Current.CancellationToken))!;
        Assert.True(state.IsComplete);
        Assert.All((await RecordAsync(w, "rev-2")).Answers, a => Assert.Equal("bob", a.AnsweredByUsername));
    }

    /// <summary>
    ///     Test that a check whose wording changed in the project's checklist, and a check that the new checklist
    ///     does not have, are not carried at the create path of an upload.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_FollowUp_ChangedChecklistWording_IsNotCarried()
    {
        // Arrange: the earlier review passed a-01 and a-02; the project then rewords a-01
        var w = await CreateAsync();
        await UploadAsync(w, "rev-1");
        await AnswerAsync(w, "rev-1", "a-01");
        await AnswerAsync(w, "rev-1", "a-02");
        await SetChecklistAsync(w, Definition("v2", "First, reworded"));

        // Act
        await UploadAsync(w, "rev-2", follows: "rev-1");

        // Assert: only the unchanged check is carried
        Assert.Equal(["a-02"], (await RecordAsync(w, "rev-2")).Answers.Select(a => a.ItemId));
    }

    /// <summary>
    ///     Test that an open thread is carried with its comments and a resolved one is not, and that the audit text
    ///     of the creation names the review, the counts and the resolved threads that were not carried.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_FollowUp_CarriesOpenThreadsAndAuditsCounts()
    {
        // Arrange: an open thread with a reply, and a resolved thread
        var w = await CreateAsync();
        await UploadAsync(w, "rev-1");
        var open = await ThreadAsync(w, "rev-1");
        await w.Threads.ReplyAsync(w.ProjectId, "rev-1", open.Id, TestActors.Ann, "Reply", TestContext.Current.CancellationToken);
        var done = await ThreadAsync(w, "rev-1", line: null);
        await w.Threads.ResolveAsync(w.ProjectId, "rev-1", done.Id, TestActors.Bob, TestContext.Current.CancellationToken);
        await AnswerAsync(w, "rev-1", "a-01");

        // Act
        var review = await UploadAsync(w, "rev-2", follows: "rev-1");

        // Assert: one thread, in the state Open, with both comments
        var thread = Assert.Single((await RecordAsync(w, "rev-2")).Threads);
        Assert.Equal(ReviewThreadState.Open, thread.State);
        Assert.Equal("rev-1", thread.CarriedFromReviewId);
        Assert.Equal(open.Id, thread.CarriedFromThreadId);
        Assert.Equal(["Please look", "Reply"], thread.Comments.Select(c => c.Text));
        Assert.Equal(["bob", "ann"], thread.Comments.Select(c => c.AuthorUsername));
        Assert.Equal(1, review.CarriedThreadCount);
        Assert.Contains(", follows 'rev-1'; carried 1 answer, 1 thread, 1 resolved thread not carried", CreatedDetail(w, "rev-2"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the audit text names the whole identifier of the earlier review: two follow-ups of reviews whose
    ///     identifiers (100 characters) share a long prefix read differently at both create paths, and the line stays
    ///     within the limit of the audit detail.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateReview_FollowUp_AuditNamesTheWholeSourceId()
    {
        // Arrange: four sources with 100-character identifiers that share their first 57 characters
        var w = await CreateAsync();
        var prefix = new string('a', 57);
        var uploadOne = prefix + new string('b', 43);
        var uploadTwo = prefix + new string('c', 43);
        var repoOne = prefix + new string('d', 43);
        var repoTwo = prefix + new string('e', 43);
        await UploadAsync(w, uploadOne);
        await UploadAsync(w, uploadTwo);
        await FromRepositoryAsync(w, repoOne);
        await FromRepositoryAsync(w, repoTwo);

        // Act
        await UploadAsync(w, "up-1", follows: uploadOne);
        await UploadAsync(w, "up-2", follows: uploadTwo);
        await FromRepositoryAsync(w, "rp-1", follows: repoOne);
        await FromRepositoryAsync(w, "rp-2", follows: repoTwo);

        // Assert
        var details = new[] { "up-1", "up-2", "rp-1", "rp-2" }.Select(id => CreatedDetail(w, id)).ToArray();
        Assert.Contains($", follows '{uploadOne}';", details[0], StringComparison.Ordinal);
        Assert.Contains($", follows '{uploadTwo}';", details[1], StringComparison.Ordinal);
        Assert.Contains($", follows '{repoOne}';", details[2], StringComparison.Ordinal);
        Assert.Contains($", follows '{repoTwo}';", details[3], StringComparison.Ordinal);
        Assert.NotEqual(details[0], details[1]);
        Assert.NotEqual(details[2], details[3]);
        Assert.All(details, d => Assert.True(d.Length <= AuditText.MaxDetailLength, $"The detail has {d.Length} characters."));
    }

    /// <summary>
    ///     Test that an ordinary review has no follow-up text in the audit and no carried data.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_NoFollowUp_HasNoCarriedData()
    {
        // Arrange, Act
        var w = await CreateAsync();
        var review = await UploadAsync(w, "rev-1", follows: "  ");

        // Assert: a blank follow-up identifier is no follow-up
        Assert.Null(review.FollowsReviewId);
        Assert.Equal(0, review.CarriedAnswerCount);
        Assert.DoesNotContain("follows", CreatedDetail(w, "rev-1"), StringComparison.Ordinal);
        Assert.Null((await RecordAsync(w, "rev-1")).FollowsReviewId);
    }

    /// <summary>
    ///     Test that a source that does not exist, and a source that exists only in another project, are refused with
    ///     the same message, which names nothing of the other project, and that nothing is stored.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_UnknownOrForeignSource_IsRefusedWithoutRevealing()
    {
        // Arrange: a review in the other project
        var w = await CreateAsync();
        await UploadAsync(w, "foreign", projectId: w.OtherProjectId);

        // Act
        var missing = await RefusedAsync(() => UploadAsync(w, "rev-1", follows: "nothing"));
        var foreign = await RefusedAsync(() => UploadAsync(w, "rev-2", follows: "foreign"));

        // Assert
        Assert.Contains("does not exist in this project", missing.Message, StringComparison.Ordinal);
        Assert.Contains("does not exist in this project", foreign.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Other", foreign.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(w.Store.Reviews, r => r.ProjectId == w.ProjectId);
    }

    /// <summary>
    ///     Test that uploads need a file in common, and that an earlier review in review is accepted.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_UploadFollowUp_NeedsACommonFile()
    {
        // Arrange: the earlier review has a.txt and b.txt, none of them signed
        var w = await CreateAsync();
        await UploadAsync(w, "rev-1", ["a.txt", "b.txt"]);

        // Act
        var refused = await RefusedAsync(() => UploadAsync(w, "rev-2", ["c.txt"], follows: "rev-1"));
        var accepted = await UploadAsync(w, "rev-3", ["b.txt", "c.txt"], follows: "rev-1");

        // Assert
        Assert.Contains("no file in common", refused.Message, StringComparison.Ordinal);
        Assert.Equal("rev-1", accepted.FollowsReviewId);
        Assert.DoesNotContain(w.Store.Reviews, r => r.Id == "rev-2");
    }

    /// <summary>
    ///     Test that an upload and a repository review cannot follow one another, in either direction, and that
    ///     reviews of two repositories cannot either.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFollowUp_UploadRepositoryAndOtherRepository_AreRefused()
    {
        // Arrange: an upload review and a repository review with files of the same path
        var w = await CreateAsync();
        await UploadAsync(w, "up-1", ["src/code.txt"]);
        await FromRepositoryAsync(w, "repo-1");

        // Act
        var repositoryAfterUpload = await RefusedAsync(() => FromRepositoryAsync(w, "repo-2", follows: "up-1"));
        var uploadAfterRepository = await RefusedAsync(() => UploadAsync(w, "up-2", ["src/code.txt"], follows: "repo-1"));
        var otherRepository = await RefusedAsync(() => FromRepositoryAsync(w, "repo-3", follows: "repo-1", repositoryId: w.OtherRepositoryId));

        // Assert
        Assert.Contains("kind of source", repositoryAfterUpload.Message, StringComparison.Ordinal);
        Assert.Contains("kind of source", uploadAfterRepository.Message, StringComparison.Ordinal);
        Assert.Contains("another repository", otherRepository.Message, StringComparison.Ordinal);
        Assert.Equal(["repo-1", "up-1"], w.Store.Reviews.Select(r => r.Id).Order());
    }

    /// <summary>
    ///     Test that a repository follow-up carries the answers, keeps the anchor of a thread on a file whose content
    ///     is identical, and moves the anchor of a thread on a changed file to the whole file with a system comment
    ///     that names the original anchor.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_FollowUp_KeepsOrMovesAnchors()
    {
        // Arrange: threads on both files of the first review; code.txt changes in the second commit
        var w = await CreateAsync();
        await FromRepositoryAsync(w, "repo-1");
        await ThreadAsync(w, "repo-1", "src/code.txt");
        await ThreadAsync(w, "repo-1", "src/other.txt");
        await AnswerAsync(w, "repo-1", "b-01");

        // Act: a follow-up of the other file only drops the thread on code.txt; the new commit has code.txt only
        var dropped = await RefusedAsync(() => FromRepositoryAsync(w, "repo-2", follows: "repo-1", selection: "src/other.txt"));
        var review = await FromRepositoryAsync(w, "repo-3", "next", "repo-1", acknowledge: true);

        // Assert: the dropped file was named; then the code.txt thread moved to the whole file, other.txt was dropped
        Assert.Contains("'src/code.txt'", dropped.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'src/other.txt'", dropped.Message, StringComparison.Ordinal);
        var record = await RecordAsync(w, "repo-3");
        var thread = Assert.Single(record.Threads);
        Assert.Equal("src/code.txt", thread.FilePath);
        Assert.Null(thread.StartLine);
        Assert.Equal(2, thread.Comments.Count);
        Assert.Equal(FollowUpCarry.SystemAuthor, thread.Comments[^1].Author);
        Assert.Contains("head line 1", thread.Comments[^1].Text, StringComparison.Ordinal);
        Assert.Equal(1, review.CarriedThreadCount);
        Assert.Equal("repo-1", review.FollowsReviewId);
        Assert.Contains("open threads on 1 file dropped", CreatedDetail(w, "repo-3"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the anchor of a thread stays when the file has the same content in the new review, at the
    ///     repository path, and that the audit text names the carried counts.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_FollowUp_SameContent_KeepsTheLineAnchor()
    {
        // Arrange
        var w = await CreateAsync();
        await FromRepositoryAsync(w, "repo-1");
        await ThreadAsync(w, "repo-1", "src/code.txt");
        await ThreadAsync(w, "repo-1", "src/other.txt", line: null);

        // Act
        var review = await FromRepositoryAsync(w, "repo-2", follows: "repo-1");

        // Assert
        var threads = (await RecordAsync(w, "repo-2")).Threads;
        Assert.Equal(2, threads.Count);
        Assert.Equal(1, threads.Single(t => t.FilePath == "src/code.txt").StartLine);
        Assert.Equal(2, review.CarriedThreadCount);
        Assert.Contains(", follows 'repo-1'; carried 0 answers, 2 threads", CreatedDetail(w, "repo-2"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that open threads on files that are not in the new review need the acknowledgement of the creator: the
    ///     refusal lists the files and stores nothing; with the acknowledgement the review is created without them.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_DroppedThreads_NeedAcknowledgement()
    {
        // Arrange: open threads on b.txt, and a resolved one on c.txt that never matters
        var w = await CreateAsync();
        await UploadAsync(w, "rev-1", ["a.txt", "b.txt", "c.txt"]);
        await ThreadAsync(w, "rev-1", "b.txt");
        var resolved = await ThreadAsync(w, "rev-1", "c.txt");
        await w.Threads.ResolveAsync(w.ProjectId, "rev-1", resolved.Id, TestActors.Bob, TestContext.Current.CancellationToken);

        // Act
        var refused = await RefusedAsync(() => UploadAsync(w, "rev-2", ["a.txt"], follows: "rev-1"));
        var review = await UploadAsync(w, "rev-3", ["a.txt"], follows: "rev-1", acknowledge: true);

        // Assert
        Assert.Contains("'b.txt'", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'c.txt'", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(w.Store.Reviews, r => r.Id == "rev-2");
        Assert.Equal(0, review.CarriedThreadCount);
        Assert.Empty((await RecordAsync(w, "rev-3")).Threads);
        Assert.Contains("1 resolved thread not carried, open threads on 1 file dropped", CreatedDetail(w, "rev-3"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an acknowledgement without dropped threads changes nothing.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_AcknowledgementWithoutDroppedThreads_IsHarmless()
    {
        // Arrange
        var w = await CreateAsync();
        await UploadAsync(w, "rev-1");
        await ThreadAsync(w, "rev-1");

        // Act
        var review = await UploadAsync(w, "rev-2", follows: "rev-1", acknowledge: true);

        // Assert
        Assert.Equal(1, review.CarriedThreadCount);
    }

    /// <summary>
    ///     Test that an earlier review that is in review and one that is approved can both be followed, and that a chain
    ///     works: the third review follows the second, and what it carries is marked with the second review, whatever
    ///     the first one was.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_ApprovedSourceAndChain_PointToTheImmediateSource()
    {
        // Arrange: rev-1 is approved (all answers, one slot signed by Bob); rev-2 follows it
        var w = await CreateAsync();
        await UploadAsync(w, "rev-1");
        await AnswerAllAsync(w, "rev-1");
        var slot = await w.Signing.AddSlotAsync(_cyd, w.ProjectId, "rev-1", w.Ids["bob"], null, TestContext.Current.CancellationToken);
        var digest = (await w.Signing.GetApprovalAsync(w.ProjectId, "rev-1", TestContext.Current.CancellationToken))!.Digest;
        await w.Signing.SignAsync(TestActors.Bob, w.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken);
        Assert.True((await w.Signing.GetApprovalAsync(w.ProjectId, "rev-1", TestContext.Current.CancellationToken))!.IsApproved);

        // Act
        await UploadAsync(w, "rev-2", follows: "rev-1");
        await UploadAsync(w, "rev-3", follows: "rev-2");

        // Assert: the approved source's slots and signatures are not carried; markers name the immediate source
        Assert.All((await RecordAsync(w, "rev-2")).Answers, a => Assert.Equal("rev-1", a.CarriedFromReviewId));
        Assert.All((await RecordAsync(w, "rev-3")).Answers, a => Assert.Equal("rev-2", a.CarriedFromReviewId));
        Assert.Equal("rev-2", (await RecordAsync(w, "rev-3")).FollowsReviewId);
        Assert.False((await w.Signing.GetApprovalAsync(w.ProjectId, "rev-2", TestContext.Current.CancellationToken))!.IsApproved);
        Assert.Empty((await w.Signing.GetApprovalAsync(w.ProjectId, "rev-2", TestContext.Current.CancellationToken))!.Slots);
    }

    /// <summary>
    ///     Test that the review-set rule is applied: reviews planned from the same set fit whatever their files, and
    ///     reviews planned from different sets do not.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromSessionAsync_ReviewSet_MustMatch()
    {
        // Arrange: a review planned from the set "core"
        var w = await CreateAsync();
        var access = await w.Repositories.GetAccessAsync(w.ProjectId, w.RepositoryId, TestContext.Current.CancellationToken);
        await using var session = await w.Git.OpenAsync(access, TestContext.Current.CancellationToken);
        var (patterns, text) = ReviewService.ParseSelection("src/code.txt");
        SessionReviewRequest Request(string id, string head, string set, string? follows = null)
        {
            return new SessionReviewRequest(id, "Planned " + id, head, null, patterns, text, set, "main", null, null, follows);
        }

        await w.Reviews.CreateFromSessionAsync(session, access, w.ProjectId, Request("set-1", HeadOne, "core"), TestActors.Ann, TestContext.Current.CancellationToken);

        // Act
        var other = await RefusedAsync(() => w.Reviews.CreateFromSessionAsync(
            session, access, w.ProjectId, Request("set-2", HeadOne, "docs", "set-1"), TestActors.Ann, TestContext.Current.CancellationToken));
        var same = await w.Reviews.CreateFromSessionAsync(
            session, access, w.ProjectId, Request("set-3", HeadTwo, "core", "set-1"), TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("another review-set", other.Message, StringComparison.Ordinal);
        Assert.Equal("set-1", same.Review.FollowsReviewId);
    }

    /// <summary>
    ///     Test that re-answering a carried check clears the carried marker and makes the user the answerer, and that
    ///     setting a carried section state again clears its marker, while the other carried rows keep theirs.
    /// </summary>
    [Fact]
    public async Task ReviewService_FollowUp_ReAnsweringOrResettingSection_ClearsTheCarriedMarker()
    {
        // Arrange: a follow-up that carries two answers and a section state
        var w = await CreateAsync();
        await UploadAsync(w, "rev-1");
        await AnswerAsync(w, "rev-1", "a-01");
        await AnswerAsync(w, "rev-1", "a-02");
        await w.Checklists.SetSectionApplicabilityAsync(w.ProjectId, "rev-1", "b", false, "Not relevant", TestActors.Bob, TestContext.Current.CancellationToken);
        await UploadAsync(w, "rev-2", follows: "rev-1");
        var digest = RecordDigest.Compute(await RecordAsync(w, "rev-2"));

        // Act: Cyd answers a-01 again, and sets section b again
        await AnswerAsync(w, "rev-2", "a-01", by: _cyd);
        await w.Checklists.SetSectionApplicabilityAsync(w.ProjectId, "rev-2", "b", false, "Not relevant", _cyd, TestContext.Current.CancellationToken);

        // Assert
        var record = await RecordAsync(w, "rev-2");
        var renewed = record.Answers.Single(a => a.ItemId == "a-01");
        Assert.Null(renewed.CarriedFromReviewId);
        Assert.Equal("cyd", renewed.AnsweredByUsername);
        Assert.Equal("rev-1", record.Answers.Single(a => a.ItemId == "a-02").CarriedFromReviewId);
        Assert.Null(record.SectionStates.Single().CarriedFromReviewId);
        Assert.Equal("cyd", record.SectionStates.Single().SetByUsername);
        Assert.NotEqual(digest, RecordDigest.Compute(record));
    }

    /// <summary>
    ///     Test that the digest of the follow-up differs from the digest of an identical review that does not follow
    ///     another, and that a change of the project's checklist afterwards changes nothing in the follow-up.
    /// </summary>
    [Fact]
    public async Task ReviewService_FollowUp_DigestBindsToFollowAndIsImmuneToLaterChecklistChange()
    {
        // Arrange: a follow-up and an ordinary review with the same content and, after answering, the same answers
        var w = await CreateAsync();
        await UploadAsync(w, "rev-1");
        await AnswerAllAsync(w, "rev-1");
        await UploadAsync(w, "rev-2", follows: "rev-1");
        await UploadAsync(w, "plain");
        await AnswerAllAsync(w, "plain");
        var digest = RecordDigest.Compute(await RecordAsync(w, "rev-2"));

        // Act: the project reworded a check
        await SetChecklistAsync(w, Definition("v2", "Reworded"));

        // Assert
        Assert.Equal(digest, RecordDigest.Compute(await RecordAsync(w, "rev-2")));
        Assert.NotEqual(digest, RecordDigest.Compute(await RecordAsync(w, "plain")));
        Assert.Equal("First", (await w.Checklists.GetAsync(w.ProjectId, "rev-2", TestContext.Current.CancellationToken))!.Definition.Sections[0].Items[0].Text);
    }

    /// <summary>
    ///     Test that carrying makes nobody independent: the creator of the follow-up cannot be seated as reviewer
    ///     (although the answers carried are Bob's), while Bob, who answered, can be.
    /// </summary>
    [Fact]
    public async Task ReviewService_FollowUp_CreatorStaysTheAuthorForIndependence()
    {
        // Arrange: Ann creates the follow-up of a review that Bob answered
        var w = await CreateAsync();
        await UploadAsync(w, "rev-1", creator: TestActors.Bob);
        await AnswerAllAsync(w, "rev-1");
        await UploadAsync(w, "rev-2", follows: "rev-1");

        // Act
        var refused = await Assert.ThrowsAsync<DomainRuleException>(() => w.Signing.AddSlotAsync(
            _cyd, w.ProjectId, "rev-2", w.Ids["ann"], null, TestContext.Current.CancellationToken));
        var slot = await w.Signing.AddSlotAsync(_cyd, w.ProjectId, "rev-2", w.Ids["bob"], null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DomainRule.ReviewerNotIndependent, refused.Rule);
        Assert.Equal(w.Ids["bob"], slot.PrimaryUserId);
    }

    /// <summary>
    ///     Test that a carried open thread blocks signing like any open thread while carried answers do not block it,
    ///     and that resolving the carried thread removes the blocker.
    /// </summary>
    [Fact]
    public async Task ReviewService_FollowUp_CarriedOpenThreadBlocksSigning_CarriedAnswersDoNot()
    {
        // Arrange: every check passed in the earlier review, which still has one open thread
        var w = await CreateAsync();
        await UploadAsync(w, "rev-1");
        await AnswerAllAsync(w, "rev-1");
        await ThreadAsync(w, "rev-1");
        await UploadAsync(w, "rev-2", follows: "rev-1");

        // Act
        var blocked = (await w.Signing.GetApprovalAsync(w.ProjectId, "rev-2", TestContext.Current.CancellationToken))!;
        var thread = Assert.Single((await RecordAsync(w, "rev-2")).Threads);
        await w.Threads.ResolveAsync(w.ProjectId, "rev-2", thread.Id, _cyd, TestContext.Current.CancellationToken);
        var free = (await w.Signing.GetApprovalAsync(w.ProjectId, "rev-2", TestContext.Current.CancellationToken))!;

        // Assert
        Assert.Equal(["1 thread is open"], blocked.Blockers);
        Assert.Empty(free.Blockers);
    }

    /// <summary>
    ///     Test that a follow-up is locked when approved and can change again when reopened, like any review, and that
    ///     the lock of the earlier review does not stop the follow-up from being created.
    /// </summary>
    [Fact]
    public async Task ReviewService_FollowUp_ApprovedIsLockedAndCanBeReopened()
    {
        // Arrange: a follow-up with all answers carried, approved by Bob
        var w = await CreateAsync();
        await UploadAsync(w, "rev-1");
        await AnswerAllAsync(w, "rev-1");
        await UploadAsync(w, "rev-2", follows: "rev-1");
        var slot = await w.Signing.AddSlotAsync(_cyd, w.ProjectId, "rev-2", w.Ids["bob"], null, TestContext.Current.CancellationToken);
        var digest = (await w.Signing.GetApprovalAsync(w.ProjectId, "rev-2", TestContext.Current.CancellationToken))!.Digest;
        await w.Signing.SignAsync(TestActors.Bob, w.ProjectId, "rev-2", slot.Id, digest, Password, TestContext.Current.CancellationToken);

        // Act
        var locked = await Assert.ThrowsAsync<DomainRuleException>(() => AnswerAsync(w, "rev-2", "a-01", ChecklistOutcome.Fail, _cyd));
        await w.Signing.ReopenReviewAsync(_cyd, w.ProjectId, "rev-2", "Needs another look", TestContext.Current.CancellationToken);
        await AnswerAsync(w, "rev-2", "a-01", ChecklistOutcome.Fail, _cyd);

        // Assert
        Assert.Equal(DomainRule.ReviewLocked, locked.Rule);
        Assert.Equal(ChecklistOutcome.Fail, (await RecordAsync(w, "rev-2")).Answers.Single(a => a.ItemId == "a-01").Outcome);
        Assert.Equal("rev-1", (await RecordAsync(w, "rev-2")).FollowsReviewId);
    }

    /// <summary>
    ///     Test that a follow-up that would reuse the id of its earlier review is refused as a duplicate and changes
    ///     nothing: no review and no carried row is added, and the earlier review is untouched.
    /// </summary>
    [Fact]
    public async Task ReviewService_FollowUp_DuplicateId_IsRefusedAndNothingChanges()
    {
        // Arrange: an earlier review with an answer and a thread
        var w = await CreateAsync();
        await UploadAsync(w, "rev-1");
        await AnswerAsync(w, "rev-1", "a-01");
        await ThreadAsync(w, "rev-1");

        // Act: the id of the follow-up is the id of the earlier review
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => UploadAsync(w, "rev-1", follows: "rev-1"));

        // Assert: refused for the duplicate id; only the original rows exist
        Assert.Equal(DomainRule.ReviewIdDuplicate, exception.Rule);
        Assert.Single(w.Store.Reviews);
        Assert.Single((await RecordAsync(w, "rev-1")).Answers);
        Assert.Single((await RecordAsync(w, "rev-1")).Threads);
    }

    /// <summary>
    ///     Test that the preview counts exactly what the creation then carries: the Pass and not-applicable answers, the
    ///     not-applicable section, the open threads, the resolved threads that are left out, and the files whose open
    ///     threads would be dropped; and that the Fail answer is not counted.
    /// </summary>
    [Fact]
    public async Task ReviewService_PreviewFollowUpAsync_CountsWhatCreationCarries()
    {
        // Arrange: Pass, Fail and N/A answers, a section that does not apply, an open thread on a.txt, a resolved one on
        // a.txt and an open one on b.txt, which the new files do not have
        var w = await CreateAsync();
        await UploadAsync(w, "rev-1", ["a.txt", "b.txt"]);
        await AnswerAsync(w, "rev-1", "a-01");
        await AnswerAsync(w, "rev-1", "a-02", ChecklistOutcome.Fail);
        await w.Checklists.SetSectionApplicabilityAsync(w.ProjectId, "rev-1", "b", false, "Not relevant", TestActors.Bob, TestContext.Current.CancellationToken);
        await ThreadAsync(w, "rev-1", "a.txt");
        var resolved = await ThreadAsync(w, "rev-1", "a.txt", null);
        await w.Threads.ResolveAsync(w.ProjectId, "rev-1", resolved.Id, TestActors.Bob, TestContext.Current.CancellationToken);
        await ThreadAsync(w, "rev-1", "b.txt");

        // Act: preview, then create with the acknowledgement
        var preview = await w.Reviews.PreviewFollowUpAsync(
            w.ProjectId, " rev-1 ", [new UploadedFile("a.txt", TestContent.A)], TestActors.Ann, TestContext.Current.CancellationToken);
        var review = await UploadAsync(w, "rev-2", ["a.txt"], follows: "rev-1", acknowledge: true);

        // Assert: the preview is the creation
        Assert.Equal(1, preview.CarriedAnswers);
        Assert.Equal(1, preview.CarriedSections);
        Assert.Equal(1, preview.CarriedThreads);
        Assert.Equal(1, preview.ResolvedThreadsNotCarried);
        Assert.Equal(["b.txt"], preview.DroppedThreadFiles);
        Assert.Equal(preview.CarriedAnswers, review.CarriedAnswerCount);
        Assert.Equal(preview.CarriedThreads, review.CarriedThreadCount);
        Assert.Equal(preview.CarriedSections, (await RecordAsync(w, "rev-2")).SectionStates.Count);
    }

    /// <summary>
    ///     Test that the preview writes nothing and audits nothing: no review, no record row and no audit entry is added,
    ///     and the earlier review keeps its digest.
    /// </summary>
    [Fact]
    public async Task ReviewService_PreviewFollowUpAsync_ChangesNothing()
    {
        // Arrange
        var w = await CreateAsync();
        await UploadAsync(w, "rev-1");
        await AnswerAsync(w, "rev-1", "a-01");
        await ThreadAsync(w, "rev-1");
        var reviews = w.Store.Reviews.Count;
        var audits = w.Store.Audits.Count;
        var threads = w.Store.Threads.Count;
        var digest = RecordDigest.Compute(await RecordAsync(w, "rev-1"));

        // Act
        await w.Reviews.PreviewFollowUpAsync(
            w.ProjectId, "rev-1", [new UploadedFile("a.txt", TestContent.A)], TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(reviews, w.Store.Reviews.Count);
        Assert.Equal(audits, w.Store.Audits.Count);
        Assert.Equal(threads, w.Store.Threads.Count);
        Assert.Equal(digest, RecordDigest.Compute(await RecordAsync(w, "rev-1")));
    }

    /// <summary>
    ///     Test that the preview refuses what creation refuses, with the same rule: nothing named, a review that does not
    ///     exist or belongs to another project, and a review that shares no file with the new ones.
    /// </summary>
    [Fact]
    public async Task ReviewService_PreviewFollowUpAsync_UnfitSource_IsRefusedWithFollowUpInvalid()
    {
        // Arrange: a review in the other project, and one here with a file of its own
        var w = await CreateAsync();
        await UploadAsync(w, "foreign", projectId: w.OtherProjectId);
        await UploadAsync(w, "rev-1");
        var files = new[] { new UploadedFile("a.txt", TestContent.A) };

        // Act, Assert
        foreach (var follows in new[] { null, " ", "missing", "foreign" })
        {
            await RefusedAsync(() => w.Reviews.PreviewFollowUpAsync(
                w.ProjectId, follows, files, TestActors.Ann, TestContext.Current.CancellationToken));
        }

        var noCommonFile = await RefusedAsync(() => w.Reviews.PreviewFollowUpAsync(
            w.ProjectId, "rev-1", [new UploadedFile("z.txt", TestContent.A)], TestActors.Ann, TestContext.Current.CancellationToken));
        Assert.Contains("no file in common", noCommonFile.Message, StringComparison.Ordinal);
    }
}
