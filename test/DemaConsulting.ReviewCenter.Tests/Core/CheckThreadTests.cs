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
///     Unit tests of the discussions about a check (a thread anchored to a check of the frozen checklist instead of a
///     file) in <see cref="ThreadService"/>: the shape of the anchor, the check, who may start one, the lock and the
///     signature impact, and that replying, resolving and reopening work as for any thread.
/// </summary>
/// <remarks>
///     The tests of permissions use the real access rules with the users root (a system administrator), creator (the
///     lead), prim and alt (the primary and alternate reviewer of the only slot), two (the reviewer of a second slot
///     in the tests that add one), mem (a plain member) and aud (an auditor). The tests of signature impact use the
///     signed-review world, in which everybody is a lead.
/// </remarks>
public class CheckThreadTests
{
    /// <summary>
    ///     The password of every user.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The identifier of the review of the world.
    /// </summary>
    private const string ReviewId = "rev-1";

    /// <summary>
    ///     The identifier of the review of the world that has no checklist.
    /// </summary>
    private const string PlainReviewId = "rev-plain";

    /// <summary>
    ///     The instant reported by the clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The identifier of a check that the checklist of the review has.
    /// </summary>
    private static readonly string _checkId = SignedReviewWorld.FirstItemId;

    /// <summary>
    ///     Builds an actor whose display name is the user name.
    /// </summary>
    /// <param name="name">The user name.</param>
    /// <returns>The actor.</returns>
    private static Actor Who(string name)
    {
        return new Actor(name, name);
    }

    /// <summary>
    ///     Arranges the users, a project with them, a review led by creator with one slot for prim and alt, and a
    ///     review without a checklist.
    /// </summary>
    /// <returns>The arranged world.</returns>
    private static async Task<World> WorldAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FixedTimeProvider(_now);
        var store = new FakeReviewStore();
        var userStore = new FakeUserStore();
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        var ids = new Dictionary<string, int>();
        foreach (var (name, admin) in new[]
                 {
                     ("root", true), ("creator", false), ("prim", false), ("alt", false), ("two", false),
                     ("mem", false), ("aud", false)
                 })
        {
            ids[name] = (await users.CreateAsync(name, name, admin, Password, Actor.CommandLine, cancellationToken)).Id;
        }

        var access = new ProjectAccess(userStore, store);
        var projects = new ProjectService(store, userStore, access, new FakePasswordHasher(), clock);
        var project = await projects.CreateAsync("Own", null, Who("root"), cancellationToken);
        foreach (var name in new[] { "creator", "prim", "alt", "two", "mem" })
        {
            await store.AddMembershipAsync(ids[name], project.Id, ProjectRole.User, cancellationToken);
        }

        await store.AddMembershipAsync(ids["aud"], project.Id, ProjectRole.Auditor, cancellationToken);
        var repositories = new RepositoryService(new FakeRepositoryStore(), new FakeCredentialProtector(), access, clock);
        var reviews = new ReviewService(store, access, clock, repositories, new FakeGitRepositoryProvider());
        await reviews.CreateDraftAsync(
            project.Id, ReviewId, "First", [new UploadedFile("a.txt", [1, 2, 3])], Who("creator"), cancellationToken);

        // A review stored without a checklist, as an old review would be
        await store.AddReviewAsync(
            new Review { ProjectId = project.Id, Id = PlainReviewId, Title = "Plain", Created = _now },
            cancellationToken);
        var signing = new SigningService(store, users, access, clock);
        await signing.AddSlotAsync(Who("root"), project.Id, ReviewId, ids["prim"], ids["alt"], cancellationToken);
        return new World(
            store,
            userStore,
            access,
            new ThreadService(store, access, userStore, clock),
            signing,
            TestServices.Checklists(store, clock, userStore),
            project.Id,
            ids);
    }

    /// <summary>
    ///     Starts a thread about the check of the tests.
    /// </summary>
    /// <param name="world">The arranged world.</param>
    /// <param name="by">The user name of the author.</param>
    /// <param name="include">Whether the starting comment is included in the report.</param>
    /// <returns>The stored thread.</returns>
    private static Task<ReviewThread> StartCheckAsync(World world, string by = "creator", bool include = false)
    {
        return world.Threads.StartThreadAsync(
            world.Project,
            ReviewId,
            ThreadAnchor.ForCheck(_checkId),
            Who(by),
            "Is this check right?",
            TestContext.Current.CancellationToken,
            includeInReport: include);
    }

    /// <summary>
    ///     Test that a check thread is stored with the check, an empty file, no lines and the head side.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_CheckAnchor_StoresCheckThreadWithEmptyFileAndNoLines()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var thread = await StartCheckAsync(world);

        // Assert
        var stored = Assert.Single(await world.Threads.ListAsync(world.Project, ReviewId, TestContext.Current.CancellationToken));
        Assert.Equal(thread.Id, stored.Id);
        Assert.Equal(_checkId, stored.CheckItemId);
        Assert.True(stored.IsCheckThread);
        Assert.Equal(string.Empty, stored.FilePath);
        Assert.Null(stored.StartLine);
        Assert.Null(stored.EndLine);
        Assert.Equal(ThreadSide.Head, stored.Side);
        Assert.Equal(ReviewThreadState.Open, stored.State);
        Assert.Equal("Is this check right?", Assert.Single(stored.Comments).Text);
    }

    /// <summary>
    ///     Test that a check that the frozen checklist does not have is refused and nothing is stored.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_CheckAnchorUnknownItem_ThrowsChecklistItemNotFound()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Threads.StartThreadAsync(
            world.Project, ReviewId, ThreadAnchor.ForCheck("no-such-check"), Who("creator"), "Text", cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ChecklistItemNotFound, ex.Rule);
        Assert.Empty(await world.Threads.ListAsync(world.Project, ReviewId, cancellationToken));
    }

    /// <summary>
    ///     Test that a review without a checklist has nothing to discuss and is refused.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_CheckAnchorWithoutChecklist_ThrowsChecklistNotFound()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Threads.StartThreadAsync(
            world.Project, PlainReviewId, ThreadAnchor.ForCheck(_checkId), Who("creator"), "Text", cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ChecklistNotFound, ex.Rule);
    }

    /// <summary>
    ///     Test that an anchor naming both a file and a check is refused.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_MixedFileAndCheckAnchor_ThrowsThreadAnchorInvalid()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Threads.StartThreadAsync(
            world.Project, ReviewId, new ThreadAnchor("a.txt", CheckItemId: _checkId), Who("creator"), "Text", cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ThreadAnchorInvalid, ex.Rule);
        Assert.Empty(await world.Threads.ListAsync(world.Project, ReviewId, cancellationToken));
    }

    /// <summary>
    ///     Test that a check anchor with lines or the baseline side is refused, as is one that names no check.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_CheckAnchorWithLinesOrBaselineSide_ThrowsThreadAnchorInvalid()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var anchors = new[]
        {
            new ThreadAnchor(string.Empty, 1, 2, ThreadSide.Head, _checkId),
            new ThreadAnchor(string.Empty, null, null, ThreadSide.Baseline, _checkId),
            new ThreadAnchor(string.Empty, null, null, ThreadSide.Head, string.Empty)
        };

        // Act, Assert
        foreach (var anchor in anchors)
        {
            var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Threads.StartThreadAsync(
                world.Project, ReviewId, anchor, Who("creator"), "Text", cancellationToken));
            Assert.Equal(DomainRule.ThreadAnchorInvalid, ex.Rule);
        }

        Assert.Empty(await world.Threads.ListAsync(world.Project, ReviewId, cancellationToken));
    }

    /// <summary>
    ///     Test that a check thread can be replied to, resolved with a note when it is a Finding, resolved and
    ///     reopened like any thread.
    /// </summary>
    [Fact]
    public async Task ThreadService_ReplyAsync_ResolveAsync_ReopenAsync_OnCheckThread_Work()
    {
        // Arrange: a chat thread and a Finding about checks, started by the lead
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var chat = await StartCheckAsync(world);
        var finding = await StartCheckAsync(world, include: true);

        // Act
        var reply = await world.Threads.ReplyAsync(world.Project, ReviewId, chat.Id, Who("mem"), "Agreed", cancellationToken);
        await world.Threads.ResolveAsync(world.Project, ReviewId, chat.Id, Who("creator"), cancellationToken);
        var resolved = (await world.Threads.ListAsync(world.Project, ReviewId, cancellationToken)).Single(t => t.Id == chat.Id);
        await world.Threads.ReopenAsync(world.Project, ReviewId, chat.Id, Who("creator"), cancellationToken);
        var reopened = (await world.Threads.ListAsync(world.Project, ReviewId, cancellationToken)).Single(t => t.Id == chat.Id);
        var needsNote = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Threads.ResolveAsync(world.Project, ReviewId, finding.Id, Who("creator"), cancellationToken));
        await world.Threads.ResolveAsync(
            world.Project, ReviewId, finding.Id, Who("creator"), cancellationToken, resolutionNote: "Fixed in the checklist");

        // Assert
        Assert.Equal(chat.Id, reply.ThreadId);
        Assert.Equal(ReviewThreadState.Resolved, resolved.State);
        Assert.Equal(ReviewThreadState.Open, reopened.State);
        Assert.Equal(_checkId, reopened.CheckItemId);
        Assert.Equal(2, reopened.Comments.Count);
        Assert.Equal(DomainRule.ResolutionNoteRequired, needsNote.Rule);
        var done = (await world.Threads.ListAsync(world.Project, ReviewId, cancellationToken)).Single(t => t.Id == finding.Id);
        Assert.Equal(ReviewThreadState.Resolved, done.State);
        Assert.Equal(ReviewCommentKind.Resolution, done.Comments[^1].Kind);
    }

    /// <summary>
    ///     Test that an auditor and a plain contributor who holds no slot and does not lead the review are refused.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_CheckAnchorByAuditorOrPlainContributor_ThrowsActorNotAllowed()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();

        // Act, Assert
        foreach (var name in new[] { "mem", "aud" })
        {
            var ex = await Assert.ThrowsAsync<DomainRuleException>(() => StartCheckAsync(world, name));
            Assert.Equal(DomainRule.ActorNotAllowed, ex.Rule);
        }

        Assert.Empty(await world.Threads.ListAsync(world.Project, ReviewId, cancellationToken));
    }

    /// <summary>
    ///     Test who may start a check thread among the roles of the review: the lead, an administrator and the only
    ///     reviewer, with a draft ballot, may; a reviewer of several slots needs the own ballot submitted.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_CheckAnchor_SlotOwnerNeedsSubmittedBallotWhenSeveralSlots()
    {
        // Arrange: one slot, so prim and alt are the only reviewers
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();

        // Act: the only reviewer, the alternate of the slot, the lead and a system administrator may start
        await StartCheckAsync(world, "prim");
        await StartCheckAsync(world, "alt");
        await StartCheckAsync(world, "creator");
        await StartCheckAsync(world, "root");

        // Arrange: a second slot makes the drafts of the others matter
        await world.Signing.AddSlotAsync(Who("root"), world.Project, ReviewId, world.Ids["two"], null, cancellationToken);
        var refused = await Assert.ThrowsAsync<DomainRuleException>(() => StartCheckAsync(world, "prim"));
        await AuditTestExtensions.AnswerAllAsync(world.Checklists, world.Project, ReviewId, Who("prim"), cancellationToken);
        await world.Checklists.SubmitBallotAsync(world.Project, ReviewId, Who("prim"), cancellationToken);
        await StartCheckAsync(world, "prim");

        // Assert: after the ballot is handed in, the reviewer may start; the second reviewer, with no ballot, may not
        Assert.Equal(DomainRule.ActorNotAllowed, refused.Rule);
        var other = await Assert.ThrowsAsync<DomainRuleException>(() => StartCheckAsync(world, "two"));
        Assert.Equal(DomainRule.ActorNotAllowed, other.Rule);
        Assert.Equal(5, (await world.Threads.ListAsync(world.Project, ReviewId, cancellationToken)).Count);
    }

    /// <summary>
    ///     Test that a concluded review refuses a new check thread, as it refuses every change to the discussion.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_CheckAnchorWhenConcluded_ThrowsReviewLocked()
    {
        // Arrange: every slot signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        await world.SignAsync(2);
        await world.SignAsync(3);

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Threads.StartThreadAsync(
            world.ProjectId,
            SignedReviewWorld.ReviewId,
            ThreadAnchor.ForCheck(SignedReviewWorld.FirstItemId),
            TestActors.Ann,
            "Late question",
            cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ReviewLocked, ex.Rule);
        Assert.Empty(world.Store.Threads);
    }

    /// <summary>
    ///     Test that a check thread whose starting comment is included in the report makes a current signature out of
    ///     date, so it needs the acknowledgement of the affected slots, and a chat check thread does not.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_CheckAnchorWouldOutdateSignature_RequiresAcknowledgement()
    {
        // Arrange: slot 1 signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        var anchor = ThreadAnchor.ForCheck(SignedReviewWorld.FirstItemId);

        // Act
        var chat = await world.Threads.StartThreadAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, anchor, TestActors.Ann, "Chat", cancellationToken);
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Threads.StartThreadAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, anchor, TestActors.Ann, "Finding", cancellationToken, includeInReport: true));
        var signed = await world.SignedSlotIdsAsync();
        await world.Threads.StartThreadAsync(
            world.ProjectId,
            SignedReviewWorld.ReviewId,
            anchor,
            TestActors.Ann,
            "Finding",
            cancellationToken,
            acknowledgedSlotIds: signed,
            includeInReport: true);

        // Assert
        Assert.Equal(DomainRule.SignaturesWouldBecomeOutOfDate, ex.Rule);
        Assert.Equal(2, world.Store.Threads.Count);
        Assert.Equal(SignedReviewWorld.FirstItemId, chat.CheckItemId);
        Assert.Empty(await world.SignedSlotIdsAsync());
    }

    /// <summary>
    ///     The arranged services of the tests of permissions.
    /// </summary>
    /// <param name="Store">The review store.</param>
    /// <param name="Users">The user store.</param>
    /// <param name="Access">The real access rules.</param>
    /// <param name="Threads">The thread service over the real access rules.</param>
    /// <param name="Signing">The signing service, to add slots.</param>
    /// <param name="Checklists">The checklist service, to hand in ballots.</param>
    /// <param name="Project">The identity of the project.</param>
    /// <param name="Ids">The identity of every user by user name.</param>
    private sealed record World(
        FakeReviewStore Store,
        FakeUserStore Users,
        ProjectAccess Access,
        ThreadService Threads,
        SigningService Signing,
        ChecklistService Checklists,
        int Project,
        IReadOnlyDictionary<string, int> Ids);
}
