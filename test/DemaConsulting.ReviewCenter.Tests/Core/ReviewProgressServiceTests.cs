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

using System.Diagnostics;
using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests of <see cref="ReviewProgressService"/>: the derived stage, the signature counts, the blocker counts
///     (which must equal the sentences of the approval status), who a review waits on, the overdue rule, the last
///     activity from the audit trail, the constant number of reads and who may look. The tests use the real project
///     access rules over in-memory stores.
/// </summary>
public partial class ReviewProgressServiceTests
{
    /// <summary>
    ///     The password every test user has.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The instant at which the clock starts: 4 March 2025, so today (UTC) is that day.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Today as a UTC calendar day.
    /// </summary>
    private static readonly DateOnly _today = new(2025, 3, 4);

    /// <summary>
    ///     The system administrator.
    /// </summary>
    private static readonly Actor _root = new("root", "root");

    /// <summary>
    ///     A project administrator of the project.
    /// </summary>
    private static readonly Actor _projectAdmin = new("padm", "padm");

    /// <summary>
    ///     The member who creates the reviews.
    /// </summary>
    private static readonly Actor _creator = new("creator", "creator");

    /// <summary>
    ///     A member who is a reviewer of some slots and answers checklists.
    /// </summary>
    private static readonly Actor _member = new("member", "member");

    /// <summary>
    ///     The first reviewer.
    /// </summary>
    private static readonly Actor _reviewerOne = new("reviewer1", "reviewer1");

    /// <summary>
    ///     The read-only member of the project.
    /// </summary>
    private static readonly Actor _auditor = new("auditor", "auditor");

    /// <summary>
    ///     Someone who is no member of the project.
    /// </summary>
    private static readonly Actor _stranger = new("stranger", "stranger");

    /// <summary>
    ///     The arranged world.
    /// </summary>
    /// <param name="Store">The review store.</param>
    /// <param name="Clock">The clock all services use; a test can move it.</param>
    /// <param name="Audit">The audit store behind the last activity.</param>
    /// <param name="Reviews">The review service over the real access rules.</param>
    /// <param name="Signing">The signing service over the same rules.</param>
    /// <param name="Checklists">The checklist service.</param>
    /// <param name="Threads">The thread service.</param>
    /// <param name="Users">The user service, to disable an account.</param>
    /// <param name="Progress">The service under test.</param>
    /// <param name="ProjectId">The project.</param>
    /// <param name="Ids">The identities of the users, by user name.</param>
    private sealed record World(
        FakeReviewStore Store,
        MovableTimeProvider Clock,
        FakeAuditStore Audit,
        ReviewService Reviews,
        SigningService Signing,
        ChecklistService Checklists,
        ThreadService Threads,
        UserService Users,
        ReviewProgressService Progress,
        int ProjectId,
        Dictionary<string, int> Ids)
    {
        /// <summary>
        ///     Gets the reviews marked as answered: the primary reviewer of each of their slots answers and hands the
        ///     ballot in as soon as the slot exists.
        /// </summary>
        public HashSet<string> Answered { get; } = [];
    }

    /// <summary>
    ///     Arranges the users, the project and the services; no review exists yet.
    /// </summary>
    /// <param name="audit">The audit store the progress service reads, or <c>null</c> for a fake one.</param>
    /// <returns>The world.</returns>
    private static async Task<World> WorldAsync(Func<FakeAuditStore, IAuditStore>? audit = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new MovableTimeProvider(_now);
        var store = new FakeReviewStore();
        var userStore = new FakeUserStore(store);
        var ids = new Dictionary<string, int>();
        foreach (var (name, admin) in new[]
                 {
                     ("root", true), ("padm", false), ("creator", false), ("member", false),
                     ("reviewer1", false), ("reviewer2", false), ("auditor", false), ("stranger", false)
                 })
        {
            var stored = await userStore.AddAsync(
                new User
                {
                    Username = name,
                    DisplayName = name,
                    IsSystemAdmin = admin,
                    PasswordHash = "hash:" + Password,
                    Created = _now
                },
                created => AuditDraft.By(Actor.CommandLine, "user.created", created.Username),
                cancellationToken);
            ids[name] = stored.Id;
        }

        var access = new ProjectAccess(userStore, store);
        var projects = new ProjectService(store, userStore, access, new FakePasswordHasher(), clock);
        var project = await projects.CreateAsync("Own", null, _root, cancellationToken);
        await store.AddMembershipAsync(ids["padm"], project.Id, ProjectRole.ProjectAdmin, cancellationToken);
        foreach (var name in new[] { "creator", "member", "reviewer1", "reviewer2" })
        {
            await store.AddMembershipAsync(ids[name], project.Id, ProjectRole.User, cancellationToken);
        }

        await store.AddMembershipAsync(ids["auditor"], project.Id, ProjectRole.Auditor, cancellationToken);

        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        var reviews = new ReviewService(store, access, clock);
        var signing = new SigningService(store, users, access, clock);
        var auditStore = new FakeAuditStore(clock);
        var progress = new ReviewProgressService(reviews, signing, audit?.Invoke(auditStore) ?? auditStore, access);
        return new World(
            store,
            clock,
            auditStore,
            reviews,
            signing,
            new ChecklistService(store, access, userStore, clock),
            new ThreadService(store, access, userStore, clock),
            users,
            progress,
            project.Id,
            ids);
    }

    /// <summary>
    ///     Creates a review as the creator.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="id">The review identifier.</param>
    /// <param name="dueDate">The due date, or <c>null</c>.</param>
    /// <returns>A task that completes when the review exists.</returns>
    private static async Task CreateReviewAsync(World world, string id, DateOnly? dueDate = null)
    {
        await world.Reviews.CreateDraftAsync(
            world.ProjectId,
            id,
            "Title of " + id,
            [new UploadedFile("a.txt", TestContent.A)],
            _creator,
            TestContext.Current.CancellationToken,
            dueDate: dueDate);
    }

    /// <summary>
    ///     Marks a review as answered: the primary reviewer of every slot of it, now or added later through
    ///     <see cref="AddSlotAsync"/>, answers every check as Pass in the own ballot and hands it in.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="id">The review identifier.</param>
    /// <returns>A task that completes when the existing slots have handed their ballots in.</returns>
    private static async Task AnswerAllAsync(World world, string id)
    {
        world.Answered.Add(id);
        foreach (var slot in await world.Store.ListSlotsAsync(world.ProjectId, id, TestContext.Current.CancellationToken))
        {
            await AnswerAsPrimaryAsync(world, id, slot);
        }
    }

    /// <summary>
    ///     Lets the primary reviewer of a slot answer every check as Pass and hand the ballot in.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="id">The review identifier.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>A task that completes when the ballot is handed in.</returns>
    private static async Task AnswerAsPrimaryAsync(World world, string id, ReviewerSlot slot)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var username = world.Ids.First(pair => pair.Value == slot.PrimaryUserId).Key;
        var owner = new Actor(username, username);
        await AuditTestExtensions.AnswerAllAsync(world.Checklists, world.ProjectId, id, owner, cancellationToken);
        await world.Checklists.SubmitBallotAsync(world.ProjectId, id, owner, cancellationToken);
    }

    /// <summary>
    ///     Adds a reviewer slot to a review; when the review is marked answered by <see cref="AnswerAllAsync"/>, the
    ///     primary reviewer of the slot answers every check and hands the ballot in.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="id">The review identifier.</param>
    /// <param name="primary">The user name of the primary reviewer.</param>
    /// <param name="alternate">The user name of the alternate reviewer, or <c>null</c>.</param>
    /// <returns>The identity of the slot.</returns>
    private static async Task<int> AddSlotAsync(World world, string id, string primary, string? alternate = null)
    {
        var slot = await world.Signing.AddSlotAsync(
            _projectAdmin,
            world.ProjectId,
            id,
            world.Ids[primary],
            alternate is null ? null : world.Ids[alternate],
            TestContext.Current.CancellationToken);
        if (world.Answered.Contains(id))
        {
            await AnswerAsPrimaryAsync(world, id, slot);
        }

        return slot.Id;
    }

    /// <summary>
    ///     Signs a slot as the reviewer.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="id">The review identifier.</param>
    /// <param name="slotId">The slot.</param>
    /// <param name="reviewer">The reviewer who signs.</param>
    /// <returns>A task that completes when the signature is stored.</returns>
    private static async Task SignAsync(World world, string id, int slotId, Actor reviewer)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var digest = (await world.Signing.GetApprovalAsync(world.ProjectId, id, cancellationToken))!.Digest;
        await world.Signing.SignAsync(reviewer, world.ProjectId, id, slotId, digest, Password, cancellationToken);
    }

    /// <summary>
    ///     Adds a slot of the first reviewer whose ballot answers every check Pass except the first, which is a Fail,
    ///     hands the ballot in and optionally writes the conclusion text.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="id">The review identifier.</param>
    /// <param name="withText"><c>true</c> to write the conclusion text.</param>
    /// <returns>The identity of the slot.</returns>
    private static async Task<int> AddRejectingSlotAsync(World world, string id, bool withText)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var slot = await world.Signing.AddSlotAsync(_projectAdmin, world.ProjectId, id, world.Ids["reviewer1"], null, cancellationToken);
        await AuditTestExtensions.AnswerAllAsync(world.Checklists, world.ProjectId, id, _reviewerOne, cancellationToken);
        var first = DefaultChecklist.Current.Sections.SelectMany(section => section.Items).First();
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, id, first.Id, ChecklistOutcome.Fail, "Wrong.", _reviewerOne, cancellationToken);
        await world.Checklists.SubmitBallotAsync(world.ProjectId, id, _reviewerOne, cancellationToken);
        if (withText)
        {
            await world.Checklists.SetReviewConclusionTextAsync(world.ProjectId, id, "Not ready.", _reviewerOne, cancellationToken);
        }

        return slot.Id;
    }

    /// <summary>
    ///     Reads the progress of the project as the project administrator and picks one review.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="id">The review identifier.</param>
    /// <returns>The progress of the review.</returns>
    private static async Task<ReviewProgress> ProgressOfAsync(World world, string id)
    {
        var all = await world.Progress.GetProgressAsync(_projectAdmin, world.ProjectId, TestContext.Current.CancellationToken);
        return Assert.Single(all, p => p.ReviewId == id);
    }

    /// <summary>
    ///     Reads the number in front of the sentence of a blocker that contains a word.
    /// </summary>
    /// <param name="approval">The approval status.</param>
    /// <param name="word">The word that identifies the sentence.</param>
    /// <returns>The number, or zero when there is no such blocker.</returns>
    private static int CountInBlocker(ReviewApproval approval, string word)
    {
        var sentence = approval.Blockers.FirstOrDefault(b => b.Contains(word, StringComparison.Ordinal));
        return sentence is null ? 0 : int.Parse(LeadingNumber().Match(sentence).Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Matches the digits at the start of a sentence.
    /// </summary>
    /// <returns>The regular expression.</returns>
    [GeneratedRegex("^[0-9]+")]
    private static partial Regex LeadingNumber();

    /// <summary>
    ///     Test that a review whose only slot signed an agreed rejection with a conclusion text has the stage Rejected
    ///     and is neither blocked nor waiting on anybody.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_RejectedConcluded_StageRejectedNotBlocked()
    {
        // Arrange: one slot whose ballot is a Fail, with the conclusion text, signed
        var world = await WorldAsync();
        await CreateReviewAsync(world, "rev-1");
        var slotId = await AddRejectingSlotAsync(world, "rev-1", withText: true);
        await SignAsync(world, "rev-1", slotId, _reviewerOne);

        // Act
        var progress = await ProgressOfAsync(world, "rev-1");

        // Assert: concluded as rejected, which is not an approval
        Assert.Equal(ReviewStage.Rejected, progress.Stage);
        Assert.Equal(ReviewConclusion.Rejected, progress.Conclusion);
        Assert.False(progress.IsApproved);
        Assert.False(progress.IsBlocked);
        Assert.Empty(progress.WaitingOn);
        Assert.Equal(1, progress.SlotsSigned);
    }

    /// <summary>
    ///     Test that an agreed Fail is no blocker of its own: the progress keeps the failed count, lists no Fail
    ///     blocker, and asks only for the conclusion text while there is none.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_RejectedBlankText_BlockedConclusionRequired()
    {
        // Arrange: one slot whose ballot is a Fail, without the conclusion text
        var world = await WorldAsync();
        await CreateReviewAsync(world, "rev-1");
        await AddRejectingSlotAsync(world, "rev-1", withText: false);

        // Act
        var progress = await ProgressOfAsync(world, "rev-1");
        var approval = (await world.Signing.GetApprovalAsync(world.ProjectId, "rev-1", TestContext.Current.CancellationToken))!;

        // Assert
        Assert.Equal(ReviewStage.InReview, progress.Stage);
        Assert.True(progress.ConclusionRequired);
        Assert.True(progress.IsBlocked);
        Assert.Equal(1, progress.FailedChecks);
        Assert.Contains(SigningService.ConclusionRequiredText, approval.Blockers);
        Assert.DoesNotContain(approval.Blockers, text => text.Contains("Fail", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that once a conclusion text is written, an agreed Fail leaves nothing that blocks.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_FailOnlyAgreed_NotBlocked()
    {
        // Arrange: one slot whose ballot is a Fail, with the conclusion text, not yet signed
        var world = await WorldAsync();
        await CreateReviewAsync(world, "rev-1");
        await AddRejectingSlotAsync(world, "rev-1", withText: true);

        // Act
        var progress = await ProgressOfAsync(world, "rev-1");

        // Assert
        Assert.Equal(ReviewStage.InReview, progress.Stage);
        Assert.False(progress.ConclusionRequired);
        Assert.False(progress.IsBlocked);
        Assert.Equal(1, progress.FailedChecks);
        Assert.Empty((await world.Signing.GetApprovalAsync(world.ProjectId, "rev-1", TestContext.Current.CancellationToken))!.Blockers);
    }

    /// <summary>
    ///     Test that the stage is derived from the slots and signatures: no slot is Setup, an unsigned slot is In review,
    ///     and every slot signed is Approved, whatever the stored state says.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_SlotsAndSignatures_DeriveTheStage()
    {
        // Arrange: a review without slots, one with an unsigned slot and one signed by its only reviewer
        var world = await WorldAsync();
        await CreateReviewAsync(world, "rev-1");
        await CreateReviewAsync(world, "rev-2");
        await AddSlotAsync(world, "rev-2", "reviewer1");
        await CreateReviewAsync(world, "rev-3");
        await AnswerAllAsync(world, "rev-3");
        var slot = await AddSlotAsync(world, "rev-3", "reviewer1");
        await SignAsync(world, "rev-3", slot, _reviewerOne);

        // Act: read the progress
        var all = await world.Progress.GetProgressAsync(_projectAdmin, world.ProjectId, TestContext.Current.CancellationToken);

        // Assert: ordered by identifier, with the derived stages and the signature counts
        Assert.Equal(["rev-1", "rev-2", "rev-3"], all.Select(p => p.ReviewId));
        Assert.Equal([ReviewStage.Setup, ReviewStage.InReview, ReviewStage.Approved], all.Select(p => p.Stage));
        Assert.Equal([0, 1, 1], all.Select(p => p.SlotsRequired));
        Assert.Equal([0, 0, 1], all.Select(p => p.SlotsSigned));
        Assert.Equal([false, false, true], all.Select(p => p.IsApproved));
        Assert.Equal("Title of rev-1", all[0].Title);
        Assert.Empty(all[2].WaitingOn);
    }

    /// <summary>
    ///     Test that a project without reviews gives an empty list.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_ProjectWithoutReviews_IsEmpty()
    {
        // Arrange: a project without reviews
        var world = await WorldAsync();

        // Act: read the progress
        var all = await world.Progress.GetProgressAsync(_projectAdmin, world.ProjectId, TestContext.Current.CancellationToken);

        // Assert: nothing
        Assert.Empty(all);
    }

    /// <summary>
    ///     Test that the blocker counts equal the numbers in the blocker sentences of the approval status, for an
    ///     unanswered check and an open thread, that a Fail answer is counted but is no blocker, and that an approved
    ///     review is not blocked.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_BlockerCounts_EqualTheApprovalBlockers()
    {
        // Arrange: a review with one Fail answer, the other checks unanswered, and two open threads; an approved review
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        await CreateReviewAsync(world, "rev-1");
        var first = DefaultChecklist.Current.Sections[0].Items[0].Id;
        await AddSlotAsync(world, "rev-1", "member");
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, "rev-1", first, ChecklistOutcome.Fail, "Wrong", _member, cancellationToken);
        await world.Threads.StartThreadAsync(world.ProjectId, "rev-1", new ThreadAnchor("a.txt"), _member, "One", cancellationToken);
        await world.Threads.StartThreadAsync(world.ProjectId, "rev-1", new ThreadAnchor("a.txt"), _member, "Two", cancellationToken);
        await CreateReviewAsync(world, "rev-2");
        await AnswerAllAsync(world, "rev-2");
        await SignAsync(world, "rev-2", await AddSlotAsync(world, "rev-2", "reviewer1"), _reviewerOne);

        // Act: read the progress and the approval status of the first review
        var progress = await ProgressOfAsync(world, "rev-1");
        var approval = (await world.Signing.GetApprovalAsync(world.ProjectId, "rev-1", cancellationToken))!;
        var approved = await ProgressOfAsync(world, "rev-2");

        // Assert: the counts are what the sentences say, the review is blocked, the approved one is not
        Assert.Equal(2, progress.OpenThreads);
        Assert.Equal(CountInBlocker(approval, "question"), progress.OpenThreads);
        Assert.Equal(2, progress.OpenQuestions);
        Assert.Equal(0, progress.OpenFindings);
        Assert.Equal(CountInBlocker(approval, "unanswered"), progress.UnansweredChecks);
        Assert.True(progress.UnansweredChecks > 0);
        Assert.Equal(1, progress.FailedChecks);
        Assert.DoesNotContain(approval.Blockers, text => text.Contains("Fail", StringComparison.Ordinal));
        Assert.Equal(approval.OpenThreads, progress.OpenThreads);
        Assert.Equal(approval.UnansweredChecks, progress.UnansweredChecks);
        Assert.Equal(approval.FailedChecks, progress.FailedChecks);
        Assert.True(progress.IsBlocked);
        Assert.False(approved.IsBlocked);
        Assert.Equal(0, approved.OpenThreads + approved.UnansweredChecks + approved.FailedChecks);
    }

    /// <summary>
    ///     Test that the progress counts open questions and open findings apart, keeps <c>OpenThreads</c> as their sum,
    ///     and words them by kind.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_CountsOpenQuestionsAndFindings()
    {
        // Arrange: two open findings, one open question and a resolved finding
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        await CreateReviewAsync(world, "rev-1");
        await AddSlotAsync(world, "rev-1", "member");
        var anchor = new ThreadAnchor("a.txt");
        await world.Threads.StartThreadAsync(world.ProjectId, "rev-1", anchor, _member, "Q", cancellationToken);
        await world.Threads.StartThreadAsync(
            world.ProjectId, "rev-1", anchor, _member, "F1", cancellationToken, includeInReport: true);
        await world.Threads.StartThreadAsync(
            world.ProjectId, "rev-1", anchor, _member, "F2", cancellationToken, includeInReport: true);
        var done = await world.Threads.StartThreadAsync(
            world.ProjectId, "rev-1", anchor, _member, "F3", cancellationToken, includeInReport: true);
        await world.Threads.ResolveAsync(world.ProjectId, "rev-1", done.Id, _member, cancellationToken, null, "Fixed");

        // Act
        var progress = await ProgressOfAsync(world, "rev-1");
        var approval = (await world.Signing.GetApprovalAsync(world.ProjectId, "rev-1", cancellationToken))!;

        // Assert
        Assert.Equal(1, progress.OpenQuestions);
        Assert.Equal(2, progress.OpenFindings);
        Assert.Equal(3, progress.OpenThreads);
        Assert.Equal("2 open findings, 1 open question", progress.OpenThreadsText);
        Assert.Contains("2 open findings, 1 open question to resolve", approval.Blockers);
    }

    /// <summary>
    ///     Test that the progress words a thread count without a split as open threads.
    /// </summary>
    [Fact]
    public void ReviewProgress_OpenThreadsText_WithoutKindCounts_FallsBackToThreads()
    {
        // Arrange
        var progress = new ReviewProgress(
            "rev-1",
            "T",
            ReviewStage.InReview,
            1,
            0,
            0,
            [],
            3,
            0,
            0,
            false,
            _now,
            _now,
            null,
            false,
            ReviewConclusion.None,
            false,
            true,
            null);

        // Act, Assert
        Assert.Equal("3 open threads", progress.OpenThreadsText);
    }

    /// <summary>
    ///     Test that a stored review without a checklist is blocked and flagged as having no checklist, while a review
    ///     with a checklist and no blockers is neither.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_ReviewWithoutChecklist_IsBlocked()
    {
        // Arrange: a review without a checklist (a stored review that has none) and a complete review with a slot
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        await world.Store.AddReviewAsync(
            new Review { ProjectId = world.ProjectId, Id = "rev-none", Title = "No checklist", Created = _now },
            cancellationToken);
        await CreateReviewAsync(world, "rev-ok");
        await AnswerAllAsync(world, "rev-ok");
        await AddSlotAsync(world, "rev-ok", "reviewer1");

        // Act: read the progress and the approval status
        var none = await ProgressOfAsync(world, "rev-none");
        var ok = await ProgressOfAsync(world, "rev-ok");
        var approval = (await world.Signing.GetApprovalAsync(world.ProjectId, "rev-none", cancellationToken))!;

        // Assert: blocked by the missing checklist alone, in line with the approval; the other is not blocked
        Assert.True(none.NoChecklist);
        Assert.True(none.IsBlocked);
        Assert.False(none.IsApproved);
        Assert.Equal((0, 0, 0), (none.OpenThreads, none.UnansweredChecks, none.FailedChecks));
        Assert.Contains("This review has no checklist.", approval.Blockers);
        Assert.Equal(approval.MissingChecklist, none.NoChecklist);
        Assert.False(ok.NoChecklist);
        Assert.False(ok.IsBlocked);
    }

    /// <summary>
    ///     Test that a comment made after a signature makes the signature out of date, which the progress counts
    ///     as out of date and not as signed.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_CommentAfterSignature_CountsOutOfDate()
    {
        // Arrange: a review with two slots, the first signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        await CreateReviewAsync(world, "rev-1");
        await AnswerAllAsync(world, "rev-1");
        var first = await AddSlotAsync(world, "rev-1", "reviewer1");
        await AddSlotAsync(world, "rev-1", "reviewer2");
        await SignAsync(world, "rev-1", first, _reviewerOne);
        var before = await ProgressOfAsync(world, "rev-1");

        // Act: somebody comments, acknowledging the signature this withdraws, which changes the record
        await world.Threads.StartThreadAsync(
            world.ProjectId,
            "rev-1",
            new ThreadAnchor("a.txt"),
            _member,
            "Look",
            cancellationToken,
            await world.Store.CurrentlySignedSlotIdsAsync(world.ProjectId, "rev-1"),
            true);
        var after = await ProgressOfAsync(world, "rev-1");

        // Assert: one signed before; afterwards that signature is out of date and the thread blocks
        Assert.Equal((2, 1, 0), (before.SlotsRequired, before.SlotsSigned, before.SlotsOutOfDate));
        Assert.Equal((2, 0, 1), (after.SlotsRequired, after.SlotsSigned, after.SlotsOutOfDate));
        Assert.Equal(1, after.OpenThreads);
        Assert.Equal(ReviewStage.InReview, after.Stage);
    }

    /// <summary>
    ///     Test that a review whose every slot is signed but that has a thread open is in review and blocked, not
    ///     approved, in line with the approval status.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_AllSignedOpenThread_IsInReviewAndBlocked()
    {
        // Arrange: reviewer1 signs, a chat thread is opened, and the unsigned slot of reviewer2 is removed
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        await CreateReviewAsync(world, "rev-1");
        await AnswerAllAsync(world, "rev-1");
        var first = await AddSlotAsync(world, "rev-1", "reviewer1");
        var second = await AddSlotAsync(world, "rev-1", "reviewer2");
        await SignAsync(world, "rev-1", first, _reviewerOne);
        await world.Threads.StartThreadAsync(
            world.ProjectId, "rev-1", new ThreadAnchor("a.txt"), _member, "Question", cancellationToken);
        await world.Signing.RemoveSlotAsync(_projectAdmin, world.ProjectId, "rev-1", second, cancellationToken);

        // Act
        var progress = await ProgressOfAsync(world, "rev-1");
        var approval = (await world.Signing.GetApprovalAsync(world.ProjectId, "rev-1", cancellationToken))!;

        // Assert
        Assert.Equal((1, 1), (progress.SlotsRequired, progress.SlotsSigned));
        Assert.Equal(ReviewStage.InReview, progress.Stage);
        Assert.False(progress.IsApproved);
        Assert.True(progress.IsBlocked);
        Assert.Equal(1, progress.OpenThreads);
        Assert.Equal(approval.IsApproved, progress.IsApproved);
    }

    /// <summary>
    ///     Test that a review that was reopened after its approval is flagged as reopened and is not approved, and
    ///     that a review never reopened is not flagged.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_ReopenedReview_IsFlaggedAndNotApproved()
    {
        // Arrange: two approved reviews
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        foreach (var id in new[] { "rev-1", "rev-2" })
        {
            await CreateReviewAsync(world, id);
            await AnswerAllAsync(world, id);
            await SignAsync(world, id, await AddSlotAsync(world, id, "reviewer1"), _reviewerOne);
        }

        // Act: reopen the first
        await world.Signing.ReopenReviewAsync(_projectAdmin, world.ProjectId, "rev-1", "One more thing", cancellationToken);
        var reopened = await ProgressOfAsync(world, "rev-1");
        var untouched = await ProgressOfAsync(world, "rev-2");

        // Assert: flagged, in review, its signature out of date; the other is approved and not flagged
        Assert.True(reopened.Reopened);
        Assert.False(reopened.IsApproved);
        Assert.Equal(ReviewStage.InReview, reopened.Stage);
        Assert.Equal(1, reopened.SlotsOutOfDate);
        Assert.False(untouched.Reopened);
        Assert.True(untouched.IsApproved);
    }

    /// <summary>
    ///     Test that the people a review waits on are the primary and a different alternate of every slot that is not
    ///     signed, each once, that a signed slot is left out, and that a disabled account is marked.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_WaitingOn_NamesPrimaryAndAlternateOfOpenSlots()
    {
        // Arrange: slot 1 with an alternate, slot 2 with the member as primary
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        await CreateReviewAsync(world, "rev-1");
        await AnswerAllAsync(world, "rev-1");
        var first = await AddSlotAsync(world, "rev-1", "reviewer1", "reviewer2");
        await AddSlotAsync(world, "rev-1", "member");

        // Act: read, then let the first reviewer sign, then disable the member's account
        var all = await ProgressOfAsync(world, "rev-1");
        await SignAsync(world, "rev-1", first, _reviewerOne);
        var signed = await ProgressOfAsync(world, "rev-1");
        await world.Users.SetEnabledAsync("member", false, cancellationToken);
        var disabled = await ProgressOfAsync(world, "rev-1");

        // Assert: everybody who could still sign, then only the member, then the member marked as disabled
        Assert.Equal(["reviewer1", "reviewer2", "member"], all.WaitingOn);
        Assert.Equal(["member"], signed.WaitingOn);
        Assert.Equal(["member" + ReviewProgressService.DisabledSuffix], disabled.WaitingOn);
        Assert.Equal(" (disabled)", ReviewProgressService.DisabledSuffix);
    }

    /// <summary>
    ///     Test that a review is overdue from the day after its due date and not on the day itself, and that an
    ///     approved review is never overdue.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_DueDate_OverdueFromTheDayAfter()
    {
        // Arrange: reviews due today, due yesterday and without a date, and an approved review due yesterday
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        await CreateReviewAsync(world, "rev-1", _today);
        await CreateReviewAsync(world, "rev-2", _today.AddDays(1));
        await CreateReviewAsync(world, "rev-3");
        await CreateReviewAsync(world, "rev-4");
        await world.Reviews.SetDueDateAsync(world.ProjectId, "rev-4", _today.AddDays(-1), _projectAdmin, cancellationToken);
        await AnswerAllAsync(world, "rev-4");
        await SignAsync(world, "rev-4", await AddSlotAsync(world, "rev-4", "reviewer1"), _reviewerOne);

        // Act: move rev-2 into the past (allowed when changing), and read
        await world.Reviews.SetDueDateAsync(world.ProjectId, "rev-2", _today.AddDays(-1), _projectAdmin, cancellationToken);
        var all = await world.Progress.GetProgressAsync(_projectAdmin, world.ProjectId, cancellationToken);

        // Assert: only the one due yesterday and not approved is overdue; the dates are carried
        Assert.Equal([false, true, false, false], all.Select(p => p.IsOverdue));
        Assert.Equal(_today, all[0].DueDate);
        Assert.Equal(_today.AddDays(-1), all[1].DueDate);
        Assert.Null(all[2].DueDate);
        Assert.True(all[3].IsApproved);
    }

    /// <summary>
    ///     Test that the last activity is the time of the newest audit entry of the review, the creation time when
    ///     the trail has none, and that entries of other reviews, other projects and without a review do not count.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_LastActivity_ComesFromTheAuditTrail()
    {
        // Arrange: two reviews; the audit trail has two entries for rev-1 an hour and two hours later, and entries that
        // must not count
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        await CreateReviewAsync(world, "rev-1");
        await CreateReviewAsync(world, "rev-2");
        world.Clock.Advance(TimeSpan.FromHours(1));
        var first = world.Clock.GetUtcNow();
        await world.Audit.AppendAsync(AuditDraft.By(_member, "thread.started", "x", world.ProjectId, "rev-1"), cancellationToken);
        world.Clock.Advance(TimeSpan.FromHours(1));
        var second = world.Clock.GetUtcNow();
        await world.Audit.AppendAsync(AuditDraft.By(_member, "thread.started", "x", world.ProjectId, "rev-1"), cancellationToken);
        world.Clock.Advance(TimeSpan.FromHours(1));
        await world.Audit.AppendAsync(AuditDraft.By(_member, "project.changed", "x", world.ProjectId), cancellationToken);
        await world.Audit.AppendAsync(AuditDraft.By(_member, "thread.started", "x", world.ProjectId + 1, "rev-2"), cancellationToken);

        // Act: read the progress
        var one = await ProgressOfAsync(world, "rev-1");
        var two = await ProgressOfAsync(world, "rev-2");

        // Assert: the newest entry of rev-1; the creation time for rev-2
        Assert.Equal(second, one.LastActivity);
        Assert.NotEqual(first, one.LastActivity);
        Assert.Equal(two.Created, two.LastActivity);
        Assert.Equal(_now, two.Created);
    }

    /// <summary>
    ///     Test that who may look is decided by the project access rules: members, read-only members and system
    ///     administrators see the progress, a stranger and a missing actor get the same refusal as for a missing project.
    /// </summary>
    /// <param name="who">The kind of caller.</param>
    /// <param name="allowed">Whether the caller may look.</param>
    [Theory]
    [InlineData("member", true)]
    [InlineData("auditor", true)]
    [InlineData("projectAdmin", true)]
    [InlineData("systemAdmin", true)]
    [InlineData("stranger", false)]
    [InlineData("nobody", false)]
    public async Task ReviewProgressService_GetProgressAsync_ActorKinds_AreAllowedOrNotFound(string who, bool allowed)
    {
        // Arrange: a project with one review and the caller
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        await CreateReviewAsync(world, "rev-1");
        Actor? actor = who switch
        {
            "member" => _member,
            "auditor" => _auditor,
            "projectAdmin" => _projectAdmin,
            "systemAdmin" => _root,
            "stranger" => _stranger,
            _ => null
        };

        // Act: ask
        var attempt = Record.ExceptionAsync(() => world.Progress.GetProgressAsync(actor, world.ProjectId, cancellationToken));
        var exception = await attempt;

        // Assert: the same single row for everybody who may look, otherwise the project is not found
        if (allowed)
        {
            Assert.Null(exception);
            Assert.Single(await world.Progress.GetProgressAsync(actor, world.ProjectId, cancellationToken));
        }
        else
        {
            var refusal = Assert.IsType<DomainRuleException>(exception);
            Assert.Equal(DomainRule.ProjectNotFound, refusal.Rule);
        }
    }

    /// <summary>
    ///     Test that the progress of a project with many reviews reads the audit trail once and finishes in a loose
    ///     time bound, so the overview does not grow into one query per review.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_ManyReviews_ReadsActivityOnceAndIsFast()
    {
        // Arrange: 300 reviews, a third with a slot, over an audit store that counts the last activity reads
        CountingAuditStore? counting = null;
        var world = await WorldAsync(fake => counting = new CountingAuditStore(fake));
        for (var i = 0; i < 300; i++)
        {
            var id = $"rev-{i:D3}";
            await CreateReviewAsync(world, id);
            if (i % 3 == 0)
            {
                await AddSlotAsync(world, id, "reviewer1");
            }
        }

        // Act: read the progress and time it
        var watch = Stopwatch.StartNew();
        var all = await world.Progress.GetProgressAsync(_projectAdmin, world.ProjectId, TestContext.Current.CancellationToken);
        watch.Stop();

        // Assert: every review is there, one read of the activity, and a loose time bound (not a benchmark)
        Assert.Equal(300, all.Count);
        Assert.Equal(100, all.Count(p => p.Stage == ReviewStage.InReview));
        Assert.Equal(1, counting!.LastActivityCalls);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}");
    }

    /// <summary>
    ///     Test that the overload that is given the list of reviews returns the same progress as the one that lists
    ///     them itself, and reads the review list no further time: the original lists once, the overload not at all.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_GivenList_EqualsOriginalAndReadsNoList()
    {
        // Arrange: a project with an unsigned, a signed and a dated review, and the list a caller already holds
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        await CreateReviewAsync(world, "rev-1", _today.AddDays(1));
        await CreateReviewAsync(world, "rev-2");
        await AddSlotAsync(world, "rev-2", "reviewer1", "reviewer2");
        await CreateReviewAsync(world, "rev-3");
        await AnswerAllAsync(world, "rev-3");
        var slot = await AddSlotAsync(world, "rev-3", "reviewer1");
        await SignAsync(world, "rev-3", slot, _reviewerOne);
        var list = await world.Reviews.ListAsync(world.ProjectId, cancellationToken);

        // Act: the original overload (one list read), then the overload that is given the list
        var before = world.Store.ListReviewsCalls;
        var original = await world.Progress.GetProgressAsync(_projectAdmin, world.ProjectId, cancellationToken);
        var afterOriginal = world.Store.ListReviewsCalls;
        var given = await world.Progress.GetProgressAsync(_projectAdmin, world.ProjectId, list, cancellationToken);
        var afterGiven = world.Store.ListReviewsCalls;

        // Assert: the same rows, the original read the list once, the overload made no list read
        Assert.Equal(1, afterOriginal - before);
        Assert.Equal(afterOriginal, afterGiven);
        Assert.Equal(3, given.Count);
        Assert.Equal(Describe(original), Describe(given));
        Assert.Equal(_today.AddDays(1), given[0].DueDate);
        Assert.Equal(["reviewer1", "reviewer2"], given[1].WaitingOn);

        static List<string> Describe(IReadOnlyList<ReviewProgress> rows)
        {
            return [.. rows.Select(p => string.Join(
                "|",
                p.ReviewId, p.Title, p.Stage, p.SlotsRequired, p.SlotsSigned, p.SlotsOutOfDate,
                string.Join(",", p.WaitingOn), p.OpenThreads, p.UnansweredChecks, p.FailedChecks, p.NoChecklist,
                p.Created.UtcTicks, p.LastActivity.UtcTicks, p.DueDate, p.IsOverdue, p.IsApproved, p.Reopened,
                p.IsBlocked, p.FollowsReviewId))];
        }
    }

    /// <summary>
    ///     Test that the overload that is given the list uses exactly that list: a subset gives only those reviews, in
    ///     the order given.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_GivenSubset_ReturnsOnlyThoseInGivenOrder()
    {
        // Arrange: three reviews and a list with two of them, the later one first
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        await CreateReviewAsync(world, "rev-1");
        await CreateReviewAsync(world, "rev-2");
        await CreateReviewAsync(world, "rev-3");
        var all = await world.Reviews.ListAsync(world.ProjectId, cancellationToken);
        IReadOnlyList<Review> subset = [all.Single(r => r.Id == "rev-3"), all.Single(r => r.Id == "rev-1")];

        // Act
        var progress = await world.Progress.GetProgressAsync(_projectAdmin, world.ProjectId, subset, cancellationToken);

        // Assert: only the given reviews, in the given order
        Assert.Equal(["rev-3", "rev-1"], progress.Select(p => p.ReviewId));
    }

    /// <summary>
    ///     Test that an empty list gives an empty result without reading the approvals or the last activity.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_GivenEmptyList_IsEmptyAndReadsNothingElse()
    {
        // Arrange: a project with a review, but an empty list from the caller
        CountingAuditStore? counting = null;
        var world = await WorldAsync(fake => counting = new CountingAuditStore(fake));
        await CreateReviewAsync(world, "rev-1");

        // Act
        var progress = await world.Progress.GetProgressAsync(_projectAdmin, world.ProjectId, [], TestContext.Current.CancellationToken);

        // Assert: nothing, and the last activity was not read
        Assert.Empty(progress);
        Assert.Equal(0, counting!.LastActivityCalls);
    }

    /// <summary>
    ///     Test that the overload that is given the list still checks the access, and refuses a stranger or a missing
    ///     actor as if the project did not exist, and that a null list is refused.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_GivenList_ChecksAccessAndRefusesNullList()
    {
        // Arrange: a project with a review and its list
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        await CreateReviewAsync(world, "rev-1");
        var list = await world.Reviews.ListAsync(world.ProjectId, cancellationToken);

        // Act: a stranger and nobody ask with a valid list
        var stranger = await Record.ExceptionAsync(() => world.Progress.GetProgressAsync(_stranger, world.ProjectId, list, cancellationToken));
        var nobody = await Record.ExceptionAsync(() => world.Progress.GetProgressAsync(null, world.ProjectId, list, cancellationToken));

        // Assert: both get the project-not-found refusal; a member may look; a null list is an argument error
        Assert.Equal(DomainRule.ProjectNotFound, Assert.IsType<DomainRuleException>(stranger).Rule);
        Assert.Equal(DomainRule.ProjectNotFound, Assert.IsType<DomainRuleException>(nobody).Rule);
        Assert.Single(await world.Progress.GetProgressAsync(_member, world.ProjectId, list, cancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => world.Progress.GetProgressAsync(_projectAdmin, world.ProjectId, null!, cancellationToken));
    }

    /// <summary>
    ///     Test that the constructor refuses missing collaborators.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_Constructor_NullArgument_Throws()
    {
        // Arrange: a world to take real collaborators from
        var world = await WorldAsync();
        var access = new AllowAllProjectAccess(world.Store);

        // Act and assert: each missing collaborator is refused
        Assert.Throws<ArgumentNullException>(() => new ReviewProgressService(null!, world.Signing, world.Audit, access));
        Assert.Throws<ArgumentNullException>(() => new ReviewProgressService(world.Reviews, null!, world.Audit, access));
        Assert.Throws<ArgumentNullException>(() => new ReviewProgressService(world.Reviews, world.Signing, null!, access));
        Assert.Throws<ArgumentNullException>(() => new ReviewProgressService(world.Reviews, world.Signing, world.Audit, null!));
    }

    /// <summary>
    ///     An audit store that counts how often the last activity is read and otherwise passes on to a fake one.
    /// </summary>
    /// <param name="inner">The store to pass on to.</param>
    private sealed class CountingAuditStore(FakeAuditStore inner) : IAuditStore
    {
        /// <summary>
        ///     Gets the number of reads of the last activity.
        /// </summary>
        public int LastActivityCalls { get; private set; }

        /// <inheritdoc />
        public Task<AuditEntry> AppendAsync(AuditDraft draft, CancellationToken cancellationToken = default)
        {
            return inner.AppendAsync(draft, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<AuditEntry>> ListAsync(
            AuditFilter filter,
            long? beforeSequence,
            long? afterSequence,
            int take,
            CancellationToken cancellationToken = default)
        {
            return inner.ListAsync(filter, beforeSequence, afterSequence, take, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<AuditEntry>> ReadAfterAsync(
            long afterSequence,
            int count,
            CancellationToken cancellationToken = default)
        {
            return inner.ReadAfterAsync(afterSequence, count, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<AuditEntry>> ReadMatchingAsync(
            AuditFilter filter,
            long afterSequence,
            long upToSequence,
            int count,
            CancellationToken cancellationToken = default)
        {
            return inner.ReadMatchingAsync(filter, afterSequence, upToSequence, count, cancellationToken);
        }

        /// <inheritdoc />
        public Task<long> LatestSequenceAsync(CancellationToken cancellationToken = default)
        {
            return inner.LatestSequenceAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyDictionary<string, DateTimeOffset>> LastActivityAsync(
            int projectId,
            CancellationToken cancellationToken = default)
        {
            LastActivityCalls++;
            return inner.LastActivityAsync(projectId, cancellationToken);
        }
    }
}
