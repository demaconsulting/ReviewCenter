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
///     Unit tests of the record content rules of <see cref="ThreadService"/>: included comments, resolution notes,
///     promoting and demoting threads, the permissions for them, the lock of a concluded review and the signature
///     impact, using an in-memory store.
/// </summary>
/// <remarks>
///     The tests of permissions use the real access rules with the users root (the lead), usr, alt (a reviewer of a
///     slot), mem (a plain member), out (not a member) and aud (an auditor). The tests of signature impact use the
///     signed-review world, in which everybody is a lead.
/// </remarks>
public class ThreadServiceReportTests
{
    /// <summary>
    ///     The password of every user.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The identifier of the review of both worlds.
    /// </summary>
    private const string ReviewId = "rev-1";

    /// <summary>
    ///     The instant reported by the clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The anchor of every thread: the whole file.
    /// </summary>
    private static readonly ThreadAnchor _anchor = new("a.txt");

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
    ///     Arranges the users, a project with them and a review led by root, with a slot for alt.
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
                     ("root", true), ("usr", false), ("alt", false), ("mem", false), ("out", false), ("aud", false)
                 })
        {
            ids[name] = (await users.CreateAsync(name, name, admin, Password, Actor.CommandLine, cancellationToken)).Id;
        }

        var access = new ProjectAccess(userStore, store);
        var projects = new ProjectService(store, userStore, access, new FakePasswordHasher(), clock);
        var root = Who("root");
        var project = await projects.CreateAsync("Own", null, root, cancellationToken);
        foreach (var name in new[] { "usr", "alt", "mem" })
        {
            await store.AddMembershipAsync(ids[name], project.Id, ProjectRole.User, cancellationToken);
        }

        await store.AddMembershipAsync(ids["aud"], project.Id, ProjectRole.Auditor, cancellationToken);
        var repositories = new RepositoryService(new FakeRepositoryStore(), new FakeCredentialProtector(), access, clock);
        var reviews = new ReviewService(store, access, clock, repositories, new FakeGitRepositoryProvider());
        await reviews.CreateDraftAsync(
            project.Id, ReviewId, "First", [new UploadedFile("a.txt", [1, 2, 3])], root, cancellationToken);
        var signing = new SigningService(store, users, access, clock);
        await signing.AddSlotAsync(root, project.Id, ReviewId, ids["alt"], null, cancellationToken);
        return new World(store, new ThreadService(store, access, userStore, clock), project.Id);
    }

    /// <summary>
    ///     Starts a thread.
    /// </summary>
    /// <param name="world">The arranged world.</param>
    /// <param name="by">The user name of the author.</param>
    /// <param name="include">Whether the starting comment is included in the report.</param>
    /// <returns>The identity of the thread.</returns>
    private static async Task<int> StartAsync(World world, string by = "usr", bool include = false)
    {
        var thread = await world.Threads.StartThreadAsync(
            world.Project,
            ReviewId,
            _anchor,
            Who(by),
            "Please fix",
            TestContext.Current.CancellationToken,
            includeInReport: include);
        return thread.Id;
    }

    /// <summary>
    ///     Reads a thread of the review as the store holds it.
    /// </summary>
    /// <param name="world">The arranged world.</param>
    /// <param name="threadId">The identity of the thread.</param>
    /// <returns>The thread.</returns>
    private static async Task<ReviewThread> GetAsync(World world, int threadId)
    {
        var threads = await world.Threads.ListAsync(world.Project, ReviewId, TestContext.Current.CancellationToken);
        return threads.Single(thread => thread.Id == threadId);
    }

    /// <summary>
    ///     Counts the audit entries of one action.
    /// </summary>
    /// <param name="world">The arranged world.</param>
    /// <param name="action">The action code.</param>
    /// <returns>The number of entries.</returns>
    private static int AuditCount(World world, string action)
    {
        return world.Store.Audits.Count(audit => audit.Action == action);
    }

    /// <summary>
    ///     Test that starting a thread with the starting comment included stores a Finding whose starting comment is
    ///     flagged.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_IncludeInReport_StoresFindingWithFlaggedStartingComment()
    {
        // Arrange, Act
        var world = await WorldAsync();
        var id = await StartAsync(world, include: true);
        var thread = await GetAsync(world, id);

        // Assert
        Assert.Equal(ThreadKind.Finding, thread.Kind);
        var comment = Assert.Single(thread.Comments);
        Assert.True(comment.IncludeInReport);
        Assert.Equal(ReviewCommentKind.Comment, comment.Kind);
    }

    /// <summary>
    ///     Test that a thread started without the flag is a Question and its comment is chat.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_Default_StoresQuestion()
    {
        // Arrange, Act
        var world = await WorldAsync();
        var thread = await GetAsync(world, await StartAsync(world));

        // Assert
        Assert.Equal(ThreadKind.Question, thread.Kind);
        Assert.False(Assert.Single(thread.Comments).IncludeInReport);
    }

    /// <summary>
    ///     Test that an included reply flags the reply only and leaves the kind of the thread alone.
    /// </summary>
    [Fact]
    public async Task ThreadService_ReplyAsync_IncludeInReport_FlagsOnlyTheReply()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);

        // Act
        await world.Threads.ReplyAsync(world.Project, ReviewId, id, Who("alt"), "Agreed", cancellationToken, null, true);
        var thread = await GetAsync(world, id);

        // Assert
        Assert.Equal(ThreadKind.Question, thread.Kind);
        Assert.Equal([false, true], thread.Comments.Select(comment => comment.IncludeInReport));
    }

    /// <summary>
    ///     Test that listing returns the flags and the kind of the comments and of the thread.
    /// </summary>
    [Fact]
    public async Task ThreadService_ListAsync_Comments_CarryFlagAndKind()
    {
        // Arrange: a Finding with an included reply, a chat reply and a resolution note
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world, include: true);
        await world.Threads.ReplyAsync(world.Project, ReviewId, id, Who("alt"), "In", cancellationToken, null, true);
        await world.Threads.ReplyAsync(world.Project, ReviewId, id, Who("mem"), "Chat", cancellationToken);
        await world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken, null, "Fixed");

        // Act
        var thread = await GetAsync(world, id);

        // Assert
        Assert.Equal(ThreadKind.Finding, thread.Kind);
        Assert.Equal([true, true, false, true], thread.Comments.Select(comment => comment.IncludeInReport));
        Assert.Equal(
            [
                ReviewCommentKind.Comment,
                ReviewCommentKind.Comment,
                ReviewCommentKind.Comment,
                ReviewCommentKind.Resolution
            ],
            thread.Comments.Select(comment => comment.Kind));
    }

    /// <summary>
    ///     Test that a Finding cannot be resolved without a note and that nothing is stored by the refusal.
    /// </summary>
    [Fact]
    public async Task ThreadService_ResolveAsync_FindingWithoutNote_ThrowsResolutionNoteRequiredAndStoresNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world, include: true);
        var audits = world.Store.Audits.Count;

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ResolutionNoteRequired, ex.Rule);
        var thread = await GetAsync(world, id);
        Assert.Equal(ReviewThreadState.Open, thread.State);
        Assert.Single(thread.Comments);
        Assert.Equal(audits, world.Store.Audits.Count);
    }

    /// <summary>
    ///     Test that resolving a Finding with a note stores the trimmed note as an included resolution comment.
    /// </summary>
    [Fact]
    public async Task ThreadService_ResolveAsync_FindingWithNote_StoresIncludedResolutionComment()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world, include: true);

        // Act
        await world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("mem"), cancellationToken, null, "  Fixed it  ");
        var thread = await GetAsync(world, id);

        // Assert
        Assert.Equal(ReviewThreadState.Resolved, thread.State);
        var note = thread.Comments[^1];
        Assert.Equal(ReviewCommentKind.Resolution, note.Kind);
        Assert.True(note.IncludeInReport);
        Assert.Equal("Fixed it", note.Text);
        Assert.Equal("mem", note.AuthorUsername);
    }

    /// <summary>
    ///     Test that a note on a Question is stored as a resolution comment that is not in the report.
    /// </summary>
    [Fact]
    public async Task ThreadService_ResolveAsync_QuestionWithNote_StoresExcludedResolutionComment()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);

        // Act
        await world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken, null, "Answered");
        var thread = await GetAsync(world, id);

        // Assert
        var note = thread.Comments[^1];
        Assert.Equal(ReviewCommentKind.Resolution, note.Kind);
        Assert.False(note.IncludeInReport);
        Assert.Equal(ThreadKind.Question, thread.Kind);
    }

    /// <summary>
    ///     Test that a Question can be resolved without a note, which adds no comment.
    /// </summary>
    [Fact]
    public async Task ThreadService_ResolveAsync_QuestionWithoutNote_Resolves()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);

        // Act
        await world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken);
        var thread = await GetAsync(world, id);

        // Assert
        Assert.Equal(ReviewThreadState.Resolved, thread.State);
        Assert.Single(thread.Comments);
    }

    /// <summary>
    ///     Test that a blank note counts as no note: a Question resolves without a comment.
    /// </summary>
    [Fact]
    public async Task ThreadService_ResolveAsync_BlankNoteOnQuestion_IsNoNote()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);

        // Act
        await world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken, null, "   ");
        var thread = await GetAsync(world, id);

        // Assert
        Assert.Equal(ReviewThreadState.Resolved, thread.State);
        Assert.Single(thread.Comments);
    }

    /// <summary>
    ///     Test that a note longer than a comment may be is refused and the thread stays open.
    /// </summary>
    [Fact]
    public async Task ThreadService_ResolveAsync_NoteTooLong_ThrowsCommentTextInvalid()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);
        var note = new string('x', ReviewLimits.MaxCommentLength + 1);

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken, null, note));

        // Assert
        Assert.Equal(DomainRule.CommentTextInvalid, ex.Rule);
        Assert.Equal(ReviewThreadState.Open, (await GetAsync(world, id)).State);
    }

    /// <summary>
    ///     Test that a promote or a demote that lands between the snapshot of the service and the write of the store
    ///     is not accepted silently: the note, the acknowledgement and the report flag were decided for the other
    ///     kind, so the resolve fails with <see cref="DomainRule.RecordChanged"/> and stores and audits nothing.
    /// </summary>
    /// <param name="promote">
    ///     <c>true</c> when a Question is promoted meanwhile; <c>false</c> when a Finding is demoted meanwhile.
    /// </param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThreadService_ResolveAsync_KindChangedBeforeStoreWrite_ThrowsRecordChangedAndStoresNothing(bool promote)
    {
        // Arrange: a Question resolved without a note (promoted meanwhile) or a Finding resolved with one (demoted)
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world, include: !promote);
        world.Store.BeforeResolveThread = async () =>
        {
            world.Store.BeforeResolveThread = null;
            await world.Store.SetThreadKindAsync(
                world.Project,
                ReviewId,
                id,
                promote ? ThreadKind.Finding : ThreadKind.Question,
                AuditDraft.By(Who("root"), "thread.kind-changed", $"thread {id}", world.Project, ReviewId),
                cancellationToken);
        };
        var audits = world.Store.Audits.Count;
        var comments = (await GetAsync(world, id)).Comments.Count;

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Threads.ResolveAsync(
            world.Project, ReviewId, id, Who("usr"), cancellationToken, null, promote ? null : "Fixed it"));

        // Assert: the thread is still open with its comments, and only the kind change was audited
        Assert.Equal(DomainRule.RecordChanged, ex.Rule);
        var thread = await GetAsync(world, id);
        Assert.Equal(ReviewThreadState.Open, thread.State);
        Assert.Equal(promote ? ThreadKind.Finding : ThreadKind.Question, thread.Kind);
        Assert.Equal(comments, thread.Comments.Count);
        Assert.Equal(audits + 1, world.Store.Audits.Count);
        Assert.DoesNotContain(world.Store.Audits, draft => draft.Action == "thread.resolved");
    }

    /// <summary>
    ///     Test that resolving a resolved thread does nothing: no note, no entry in the audit trail.
    /// </summary>
    [Fact]
    public async Task ThreadService_ResolveAsync_AlreadyResolved_IsNoOpWithoutNoteOrAudit()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);
        await world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken);
        var audits = world.Store.Audits.Count;

        // Act
        await world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken, null, "Again");

        // Assert
        Assert.Single((await GetAsync(world, id)).Comments);
        Assert.Equal(audits, world.Store.Audits.Count);
    }

    /// <summary>
    ///     Test that resolving a reopened Finding adds a second resolution comment, so the history stays.
    /// </summary>
    [Fact]
    public async Task ThreadService_ReopenThenResolveFinding_AddsSecondResolutionComment()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world, include: true);

        // Act
        await world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken, null, "First try");
        await world.Threads.ReopenAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken);
        await world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken, null, "Second try");
        var thread = await GetAsync(world, id);

        // Assert
        var notes = thread.Comments.Where(comment => comment.Kind == ReviewCommentKind.Resolution).ToList();
        Assert.Equal(["First try", "Second try"], notes.Select(note => note.Text));
        Assert.All(notes, note => Assert.True(note.IncludeInReport));
        Assert.Equal(ReviewThreadState.Resolved, thread.State);
    }

    /// <summary>
    ///     Test that reopening a Finding keeps the earlier resolution comment.
    /// </summary>
    [Fact]
    public async Task ThreadService_ReopenAsync_Finding_KeepsEarlierResolutionComment()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world, include: true);
        await world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken, null, "Done");

        // Act
        await world.Threads.ReopenAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken);
        var thread = await GetAsync(world, id);

        // Assert
        Assert.Equal(ReviewThreadState.Open, thread.State);
        Assert.Equal(ReviewCommentKind.Resolution, thread.Comments[^1].Kind);
        Assert.Equal("Done", thread.Comments[^1].Text);
    }

    /// <summary>
    ///     Test that the person who started a thread, a reviewer of the review and the lead may each promote a thread.
    /// </summary>
    [Fact]
    public async Task ThreadService_PromoteAsync_StarterSlotOwnerAndLead_Allowed()
    {
        // Arrange: three chat threads started by usr
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var byStarter = await StartAsync(world);
        var bySlotOwner = await StartAsync(world);
        var byLead = await StartAsync(world);

        // Act
        await world.Threads.PromoteAsync(world.Project, ReviewId, byStarter, Who("usr"), cancellationToken);
        await world.Threads.PromoteAsync(world.Project, ReviewId, bySlotOwner, Who("alt"), cancellationToken);
        await world.Threads.PromoteAsync(world.Project, ReviewId, byLead, Who("root"), cancellationToken);

        // Assert
        foreach (var id in new[] { byStarter, bySlotOwner, byLead })
        {
            Assert.Equal(ThreadKind.Finding, (await GetAsync(world, id)).Kind);
        }
    }

    /// <summary>
    ///     Test that a member who is neither the starter, a reviewer nor the lead cannot promote a thread.
    /// </summary>
    [Fact]
    public async Task ThreadService_PromoteAsync_OtherMember_ThrowsActorNotAllowed()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Threads.PromoteAsync(world.Project, ReviewId, id, Who("mem"), cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ActorNotAllowed, ex.Rule);
        Assert.Equal(ThreadKind.Question, (await GetAsync(world, id)).Kind);
        Assert.Equal(0, AuditCount(world, "thread.kind-changed"));
    }

    /// <summary>
    ///     Test that promoting a resolved thread also includes its earlier resolution notes.
    /// </summary>
    [Fact]
    public async Task ThreadService_PromoteAsync_AlsoIncludesExistingResolutionComments()
    {
        // Arrange: a resolved Question with a note
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);
        await world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken, null, "Answered");

        // Act
        await world.Threads.PromoteAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken);
        var thread = await GetAsync(world, id);

        // Assert
        Assert.Equal([true, true], thread.Comments.Select(comment => comment.IncludeInReport));
        Assert.Equal(ReviewCommentKind.Resolution, thread.Comments[1].Kind);
    }

    /// <summary>
    ///     Test that a resolved Question without a resolution note cannot be promoted, because the result would be a
    ///     resolved Finding without the note that its resolution needs; reopening the thread makes it possible.
    /// </summary>
    [Fact]
    public async Task ThreadService_PromoteAsync_ResolvedQuestionWithoutNote_ThrowsResolutionNoteRequired()
    {
        // Arrange: a Question that was resolved without a note
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);
        await world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken);

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Threads.PromoteAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken));
        var refused = await GetAsync(world, id);
        await world.Threads.ReopenAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken);
        await world.Threads.PromoteAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken);

        // Assert: nothing changed by the refusal, and the reopened thread can be promoted
        Assert.Equal(DomainRule.ResolutionNoteRequired, ex.Rule);
        Assert.Equal(ThreadKind.Question, refused.Kind);
        Assert.Equal(ReviewThreadState.Resolved, refused.State);
        Assert.Equal(ThreadKind.Finding, (await GetAsync(world, id)).Kind);
        Assert.Equal(1, AuditCount(world, "thread.kind-changed"));
    }

    /// <summary>
    ///     Test that promoting a Finding does nothing, not even an entry in the audit trail.
    /// </summary>
    [Fact]
    public async Task ThreadService_PromoteAsync_AlreadyFinding_IsNoOp()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world, include: true);

        // Act
        await world.Threads.PromoteAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken);

        // Assert
        Assert.Equal(ThreadKind.Finding, (await GetAsync(world, id)).Kind);
        Assert.Equal(0, AuditCount(world, "thread.kind-changed"));
    }

    /// <summary>
    ///     Test that the person who started a Finding and the lead may demote it.
    /// </summary>
    [Fact]
    public async Task ThreadService_DemoteAsync_StarterAndLead_Allowed()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var byStarter = await StartAsync(world, include: true);
        var byLead = await StartAsync(world, include: true);

        // Act
        await world.Threads.DemoteAsync(world.Project, ReviewId, byStarter, Who("usr"), cancellationToken);
        await world.Threads.DemoteAsync(world.Project, ReviewId, byLead, Who("root"), cancellationToken);

        // Assert
        Assert.Equal(ThreadKind.Question, (await GetAsync(world, byStarter)).Kind);
        Assert.Equal(ThreadKind.Question, (await GetAsync(world, byLead)).Kind);
    }

    /// <summary>
    ///     Test that a reviewer who did not start the thread cannot demote it.
    /// </summary>
    [Fact]
    public async Task ThreadService_DemoteAsync_SlotOwnerWhoIsNotStarter_ThrowsActorNotAllowed()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world, include: true);

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Threads.DemoteAsync(world.Project, ReviewId, id, Who("alt"), cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ActorNotAllowed, ex.Rule);
        Assert.Equal(ThreadKind.Finding, (await GetAsync(world, id)).Kind);
    }

    /// <summary>
    ///     Test that demoting takes the starting comment and the resolution note out of the report and keeps the flag
    ///     of an included reply.
    /// </summary>
    [Fact]
    public async Task ThreadService_DemoteAsync_ExcludesResolutionButKeepsReplyFlags()
    {
        // Arrange: a resolved Finding with an included reply
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world, include: true);
        await world.Threads.ReplyAsync(world.Project, ReviewId, id, Who("alt"), "In", cancellationToken, null, true);
        await world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken, null, "Done");

        // Act
        await world.Threads.DemoteAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken);
        var thread = await GetAsync(world, id);

        // Assert: the starting comment and the note are out, the reply stays
        Assert.Equal([false, true, false], thread.Comments.Select(comment => comment.IncludeInReport));
        Assert.Equal(ThreadKind.Question, thread.Kind);
    }

    /// <summary>
    ///     Test that demoting a Question does nothing.
    /// </summary>
    [Fact]
    public async Task ThreadService_DemoteAsync_AlreadyQuestion_IsNoOp()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);

        // Act
        await world.Threads.DemoteAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken);

        // Assert
        Assert.Equal(ThreadKind.Question, (await GetAsync(world, id)).Kind);
        Assert.Equal(0, AuditCount(world, "thread.kind-changed"));
    }

    /// <summary>
    ///     Test that the author of a reply and the lead can include the reply and take it out again.
    /// </summary>
    [Fact]
    public async Task ThreadService_SetCommentIncludedAsync_ReplyByAuthorOrLead_Toggles()
    {
        // Arrange: a chat thread with a chat reply by alt
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);
        var reply = await world.Threads.ReplyAsync(world.Project, ReviewId, id, Who("alt"), "Reply", cancellationToken);

        // Act
        await world.Threads.SetCommentIncludedAsync(world.Project, ReviewId, id, reply.Id, true, Who("alt"), cancellationToken);
        var included = (await GetAsync(world, id)).Comments[1].IncludeInReport;
        await world.Threads.SetCommentIncludedAsync(world.Project, ReviewId, id, reply.Id, false, Who("root"), cancellationToken);
        var excluded = (await GetAsync(world, id)).Comments[1].IncludeInReport;

        // Assert
        Assert.True(included);
        Assert.False(excluded);
    }

    /// <summary>
    ///     Test that somebody who did not write the reply and does not lead the review cannot toggle it, not even the
    ///     person who started the thread.
    /// </summary>
    [Fact]
    public async Task ThreadService_SetCommentIncludedAsync_OtherMember_ThrowsActorNotAllowed()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);
        var reply = await world.Threads.ReplyAsync(world.Project, ReviewId, id, Who("alt"), "Reply", cancellationToken);

        // Act, Assert
        foreach (var name in new[] { "mem", "usr" })
        {
            var ex = await Assert.ThrowsAsync<DomainRuleException>(
                () => world.Threads.SetCommentIncludedAsync(
                    world.Project, ReviewId, id, reply.Id, true, Who(name), cancellationToken));
            Assert.Equal(DomainRule.ActorNotAllowed, ex.Rule);
        }

        Assert.False((await GetAsync(world, id)).Comments[1].IncludeInReport);
    }

    /// <summary>
    ///     Test that the starting comment and a resolution note cannot be toggled on their own, even by the lead.
    /// </summary>
    [Fact]
    public async Task ThreadService_SetCommentIncludedAsync_StartingOrResolutionComment_ThrowsCommentNotTogglable()
    {
        // Arrange: a thread with a resolution note
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);
        await world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken, null, "Answered");
        var thread = await GetAsync(world, id);

        // Act, Assert
        foreach (var comment in thread.Comments)
        {
            var ex = await Assert.ThrowsAsync<DomainRuleException>(
                () => world.Threads.SetCommentIncludedAsync(
                    world.Project, ReviewId, id, comment.Id, true, Who("root"), cancellationToken));
            Assert.Equal(DomainRule.CommentNotTogglable, ex.Rule);
        }
    }

    /// <summary>
    ///     Test that a comment that is not in the thread, and a thread that does not exist, are reported as a missing
    ///     thread.
    /// </summary>
    [Fact]
    public async Task ThreadService_SetCommentIncludedAsync_UnknownComment_ThrowsThreadNotFound()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);

        // Act
        var comment = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Threads.SetCommentIncludedAsync(
                world.Project, ReviewId, id, 9999, true, Who("root"), cancellationToken));
        var thread = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Threads.SetCommentIncludedAsync(
                world.Project, ReviewId, 9999, 1, true, Who("root"), cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ThreadNotFound, comment.Rule);
        Assert.Equal(DomainRule.ThreadNotFound, thread.Rule);
    }

    /// <summary>
    ///     Test that setting the flag to the value it has does nothing, not even an entry in the audit trail.
    /// </summary>
    [Fact]
    public async Task ThreadService_SetCommentIncludedAsync_SameValue_IsNoOp()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);
        var reply = await world.Threads.ReplyAsync(world.Project, ReviewId, id, Who("alt"), "Reply", cancellationToken);

        // Act
        await world.Threads.SetCommentIncludedAsync(world.Project, ReviewId, id, reply.Id, false, Who("alt"), cancellationToken);

        // Assert
        Assert.False((await GetAsync(world, id)).Comments[1].IncludeInReport);
        Assert.Equal(0, AuditCount(world, "thread.comment-report-changed"));
    }

    /// <summary>
    ///     Test that an auditor is refused every mutation of the record content as not allowed, and that nothing
    ///     changes.
    /// </summary>
    [Fact]
    public async Task ThreadService_ReportMutations_Auditor_ThrowActorNotAllowedOrProjectNotFoundAsBefore()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world, include: true);
        var auditor = Who("aud");
        var audits = world.Store.Audits.Count;
        var attempts = new Func<Task>[]
        {
            () => world.Threads.StartThreadAsync(
                world.Project, ReviewId, _anchor, auditor, "x", cancellationToken, null, true),
            () => world.Threads.ReplyAsync(world.Project, ReviewId, id, auditor, "x", cancellationToken, null, true),
            () => world.Threads.ResolveAsync(world.Project, ReviewId, id, auditor, cancellationToken, null, "x"),
            () => world.Threads.PromoteAsync(world.Project, ReviewId, id, auditor, cancellationToken),
            () => world.Threads.DemoteAsync(world.Project, ReviewId, id, auditor, cancellationToken),
            () => world.Threads.SetCommentIncludedAsync(world.Project, ReviewId, id, 1, false, auditor, cancellationToken)
        };

        // Act, Assert
        foreach (var attempt in attempts)
        {
            var ex = await Assert.ThrowsAsync<DomainRuleException>(attempt);
            Assert.Equal(DomainRule.ActorNotAllowed, ex.Rule);
        }

        Assert.Equal(audits, world.Store.Audits.Count);
        Assert.Single(world.Store.Threads);
    }

    /// <summary>
    ///     Test that a user outside the project is told the project does not exist for every mutation of the record
    ///     content.
    /// </summary>
    [Fact]
    public async Task ThreadService_ReportMutations_OutsideUser_ThrowsProjectNotFound()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world, include: true);
        var outsider = Who("out");
        var attempts = new Func<Task>[]
        {
            () => world.Threads.StartThreadAsync(
                world.Project, ReviewId, _anchor, outsider, "x", cancellationToken, null, true),
            () => world.Threads.ReplyAsync(world.Project, ReviewId, id, outsider, "x", cancellationToken, null, true),
            () => world.Threads.ResolveAsync(world.Project, ReviewId, id, outsider, cancellationToken, null, "x"),
            () => world.Threads.PromoteAsync(world.Project, ReviewId, id, outsider, cancellationToken),
            () => world.Threads.DemoteAsync(world.Project, ReviewId, id, outsider, cancellationToken),
            () => world.Threads.SetCommentIncludedAsync(world.Project, ReviewId, id, 1, false, outsider, cancellationToken)
        };

        // Act, Assert
        foreach (var attempt in attempts)
        {
            var ex = await Assert.ThrowsAsync<DomainRuleException>(attempt);
            Assert.Equal(DomainRule.ProjectNotFound, ex.Rule);
        }
    }

    /// <summary>
    ///     Test that every thread action is refused on a concluded review and changes nothing.
    /// </summary>
    /// <param name="action">The action to try.</param>
    [Theory]
    [InlineData("start")]
    [InlineData("reply")]
    [InlineData("resolve")]
    [InlineData("reopen")]
    [InlineData("promote")]
    [InlineData("demote")]
    [InlineData("toggle")]
    public async Task ThreadService_AllThreadActions_ConcludedReview_ThrowReviewLocked(string action)
    {
        // Arrange: a resolved Finding with an included reply, and all three slots signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        var project = world.ProjectId;
        var thread = await world.Threads.StartThreadAsync(
            project, ReviewId, _anchor, TestActors.Ann, "Please fix", cancellationToken, null, true);
        var reply = await world.Threads.ReplyAsync(
            project, ReviewId, thread.Id, TestActors.Bob, "In", cancellationToken, null, true);
        await world.Threads.ResolveAsync(project, ReviewId, thread.Id, TestActors.Ann, cancellationToken, null, "Done");
        await world.SignAsync(1);
        await world.SignAsync(2);
        await world.SignAsync(3);
        var attempts = new Dictionary<string, Func<Task>>
        {
            ["start"] = () => world.Threads.StartThreadAsync(
                project, ReviewId, _anchor, TestActors.Ann, "x", cancellationToken),
            ["reply"] = () => world.Threads.ReplyAsync(
                project, ReviewId, thread.Id, TestActors.Ann, "x", cancellationToken),
            ["resolve"] = () => world.Threads.ResolveAsync(
                project, ReviewId, thread.Id, TestActors.Ann, cancellationToken, null, "x"),
            ["reopen"] = () => world.Threads.ReopenAsync(project, ReviewId, thread.Id, TestActors.Ann, cancellationToken),
            ["promote"] = () => world.Threads.PromoteAsync(project, ReviewId, thread.Id, TestActors.Ann, cancellationToken),
            ["demote"] = () => world.Threads.DemoteAsync(project, ReviewId, thread.Id, TestActors.Ann, cancellationToken),
            ["toggle"] = () => world.Threads.SetCommentIncludedAsync(
                project, ReviewId, thread.Id, reply.Id, false, TestActors.Bob, cancellationToken)
        };
        var audits = world.Store.Audits.Count;

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(attempts[action]);

        // Assert
        Assert.True((await world.ApprovalAsync()).IsApproved);
        Assert.Equal(DomainRule.ReviewLocked, ex.Rule);
        Assert.Equal(ReviewLock.LockedMessage, ex.Message);
        Assert.Equal(audits, world.Store.Audits.Count);
        Assert.Equal(3, Assert.Single(world.Store.Threads).Comments.Count);
    }

    /// <summary>
    ///     Test that a concluded review reports the lock, not a problem with the anchor, when a thread is started on a
    ///     file that is not in the review or on lines the file does not have: the lock is checked before the file.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_ConcludedReviewAndInvalidAnchor_ReportsReviewLocked()
    {
        // Arrange: all three slots signed and no thread open, so the review is concluded
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        await world.SignAsync(2);
        await world.SignAsync(3);
        var anchors = new[] { new ThreadAnchor("missing.txt"), new ThreadAnchor("a.txt", 5000, 5001) };
        var audits = world.Store.Audits.Count;

        // Act, Assert
        foreach (var anchor in anchors)
        {
            var ex = await Assert.ThrowsAsync<DomainRuleException>(
                () => world.Threads.StartThreadAsync(
                    world.ProjectId, ReviewId, anchor, TestActors.Ann, "x", cancellationToken));
            Assert.Equal(DomainRule.ReviewLocked, ex.Rule);
        }

        Assert.True((await world.ApprovalAsync()).IsApproved);
        Assert.Equal(audits, world.Store.Audits.Count);
        Assert.Empty(world.Store.Threads);
    }

    /// <summary>
    ///     Test that a review with every remaining slot signed but a thread open is not locked: the thread can be
    ///     answered and resolved, and resolving it concludes the review.
    /// </summary>
    [Fact]
    public async Task ThreadService_Mutations_OpenThreadReview_NotLockedEvenWhenAllSigned()
    {
        // Arrange: slots 1 and 2 signed, a chat thread opened, then the unsigned slot 3 removed
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        await world.SignAsync(2);
        var thread = await world.Threads.StartThreadAsync(
            world.ProjectId, ReviewId, _anchor, TestActors.Ann, "Question", cancellationToken);
        await world.Signing.RemoveSlotAsync(
            new Actor("rita", "Rita"), world.ProjectId, ReviewId, world.Slots[2].Id, cancellationToken);

        // Act
        var before = await world.ApprovalAsync();
        await world.Threads.ReplyAsync(world.ProjectId, ReviewId, thread.Id, TestActors.Bob, "Answer", cancellationToken);
        await world.Threads.ResolveAsync(world.ProjectId, ReviewId, thread.Id, TestActors.Ann, cancellationToken);
        var after = await world.ApprovalAsync();

        // Assert
        Assert.False(before.IsApproved);
        Assert.All(before.Slots, slot => Assert.Equal(SlotSignatureState.Signed, slot.State));
        Assert.True(after.IsApproved);
    }

    /// <summary>
    ///     Test that a chat thread started after a signature needs no acknowledgement and keeps the signature.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_ChatAfterSignature_NeedsNoAcknowledgementAndKeepsSignature()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);

        // Act
        await world.Threads.StartThreadAsync(
            world.ProjectId, ReviewId, _anchor, TestActors.Bob, "A question", cancellationToken);

        // Assert
        Assert.Single(world.Store.Threads);
        Assert.Equal(SlotSignatureState.Signed, (await world.ApprovalAsync()).Slots[0].State);
    }

    /// <summary>
    ///     Test that a chat reply after a signature needs no acknowledgement and keeps the signature.
    /// </summary>
    [Fact]
    public async Task ThreadService_ReplyAsync_ChatAfterSignature_KeepsSignature()
    {
        // Arrange: a resolved thread, then slot 1 signs
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ResolvedChatThreadSignedAsync();

        // Act
        await world.World.Threads.ReplyAsync(
            world.World.ProjectId, ReviewId, world.ThreadId, TestActors.Bob, "Thanks", cancellationToken);

        // Assert
        Assert.Equal(2, Assert.Single(world.World.Store.Threads).Comments.Count);
        Assert.Equal(SlotSignatureState.Signed, (await world.World.ApprovalAsync()).Slots[0].State);
    }

    /// <summary>
    ///     Test that a thread started with its comment in the report needs the acknowledgement of a current signature.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_IncludedAfterSignature_NeedsAcknowledgement()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Threads.StartThreadAsync(
            world.ProjectId, ReviewId, _anchor, TestActors.Bob, "Finding", cancellationToken, null, true));
        var stored = world.Store.Threads.Count;
        await world.Threads.StartThreadAsync(
            world.ProjectId, ReviewId, _anchor, TestActors.Bob, "Finding", cancellationToken,
            await world.SignedSlotIdsAsync(), true);

        // Assert
        Assert.Equal(DomainRule.SignaturesWouldBecomeOutOfDate, ex.Rule);
        Assert.Equal(0, stored);
        Assert.Single(world.Store.Threads);
        Assert.Equal(SlotSignatureState.OutOfDate, (await world.ApprovalAsync()).Slots[0].State);
    }

    /// <summary>
    ///     Test that an included reply needs the acknowledgement of a current signature.
    /// </summary>
    [Fact]
    public async Task ThreadService_ReplyAsync_IncludedAfterSignature_NeedsAcknowledgement()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ResolvedChatThreadSignedAsync();
        var project = world.World.ProjectId;

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.World.Threads.ReplyAsync(
            project, ReviewId, world.ThreadId, TestActors.Bob, "Included", cancellationToken, null, true));
        await world.World.Threads.ReplyAsync(
            project, ReviewId, world.ThreadId, TestActors.Bob, "Included", cancellationToken,
            await world.World.SignedSlotIdsAsync(), true);

        // Assert
        Assert.Equal(DomainRule.SignaturesWouldBecomeOutOfDate, ex.Rule);
        Assert.Equal(2, Assert.Single(world.World.Store.Threads).Comments.Count);
        Assert.Equal(SlotSignatureState.OutOfDate, (await world.World.ApprovalAsync()).Slots[0].State);
    }

    /// <summary>
    ///     Test that promoting a thread needs the acknowledgement of a current signature.
    /// </summary>
    [Fact]
    public async Task ThreadService_PromoteAsync_AfterSignature_NeedsAcknowledgement()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ResolvedChatThreadSignedAsync(withNote: true);
        var project = world.World.ProjectId;

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.World.Threads.PromoteAsync(project, ReviewId, world.ThreadId, TestActors.Ann, cancellationToken));
        await world.World.Threads.PromoteAsync(
            project, ReviewId, world.ThreadId, TestActors.Ann, cancellationToken, await world.World.SignedSlotIdsAsync());

        // Assert
        Assert.Equal(DomainRule.SignaturesWouldBecomeOutOfDate, ex.Rule);
        Assert.Equal("Rita", Assert.Single(ex.AffectedSlots).SignerDisplayName);
        Assert.Equal(ThreadKind.Finding, Assert.Single(world.World.Store.Threads).Kind);
        Assert.Equal(SlotSignatureState.OutOfDate, (await world.World.ApprovalAsync()).Slots[0].State);
    }

    /// <summary>
    ///     Test that demoting a thread needs the acknowledgement of a current signature.
    /// </summary>
    [Fact]
    public async Task ThreadService_DemoteAsync_AfterSignature_NeedsAcknowledgement()
    {
        // Arrange: a resolved Finding, then slot 1 signs
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        var project = world.ProjectId;
        var thread = await world.Threads.StartThreadAsync(
            project, ReviewId, _anchor, TestActors.Ann, "Finding", cancellationToken, null, true);
        await world.Threads.ResolveAsync(project, ReviewId, thread.Id, TestActors.Ann, cancellationToken, null, "Done");
        await world.SignAsync(1);

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Threads.DemoteAsync(project, ReviewId, thread.Id, TestActors.Ann, cancellationToken));
        await world.Threads.DemoteAsync(
            project, ReviewId, thread.Id, TestActors.Ann, cancellationToken, await world.SignedSlotIdsAsync());

        // Assert
        Assert.Equal(DomainRule.SignaturesWouldBecomeOutOfDate, ex.Rule);
        Assert.Equal(ThreadKind.Question, Assert.Single(world.Store.Threads).Kind);
        Assert.Equal(SlotSignatureState.OutOfDate, (await world.ApprovalAsync()).Slots[0].State);
    }

    /// <summary>
    ///     Test that including a reply needs the acknowledgement of a current signature.
    /// </summary>
    [Fact]
    public async Task ThreadService_SetCommentIncludedAsync_AfterSignature_NeedsAcknowledgement()
    {
        // Arrange: a resolved chat thread with a chat reply, then slot 1 signs
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        var project = world.ProjectId;
        var thread = await world.Threads.StartThreadAsync(
            project, ReviewId, _anchor, TestActors.Ann, "Question", cancellationToken);
        var reply = await world.Threads.ReplyAsync(project, ReviewId, thread.Id, TestActors.Bob, "Answer", cancellationToken);
        await world.Threads.ResolveAsync(project, ReviewId, thread.Id, TestActors.Ann, cancellationToken);
        await world.SignAsync(1);

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Threads.SetCommentIncludedAsync(
            project, ReviewId, thread.Id, reply.Id, true, TestActors.Bob, cancellationToken));
        await world.Threads.SetCommentIncludedAsync(
            project, ReviewId, thread.Id, reply.Id, true, TestActors.Bob, cancellationToken,
            await world.SignedSlotIdsAsync());

        // Assert
        Assert.Equal(DomainRule.SignaturesWouldBecomeOutOfDate, ex.Rule);
        Assert.True(Assert.Single(world.Store.Threads).Comments[1].IncludeInReport);
        Assert.Equal(SlotSignatureState.OutOfDate, (await world.ApprovalAsync()).Slots[0].State);
    }

    /// <summary>
    ///     Test that resolving a Finding with a note after a signature needs the acknowledgement, because the note is
    ///     part of the report.
    /// </summary>
    [Fact]
    public async Task ThreadService_ResolveAsync_FindingAfterSignature_NeedsAcknowledgement()
    {
        // Arrange: a Finding that was resolved, signed and reopened (reopening needs nothing)
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        var project = world.ProjectId;
        var thread = await world.Threads.StartThreadAsync(
            project, ReviewId, _anchor, TestActors.Ann, "Finding", cancellationToken, null, true);
        await world.Threads.ResolveAsync(project, ReviewId, thread.Id, TestActors.Ann, cancellationToken, null, "First");
        await world.SignAsync(1);
        await world.Threads.ReopenAsync(project, ReviewId, thread.Id, TestActors.Ann, cancellationToken);

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Threads.ResolveAsync(
            project, ReviewId, thread.Id, TestActors.Ann, cancellationToken, null, "Second"));
        await world.Threads.ResolveAsync(
            project, ReviewId, thread.Id, TestActors.Ann, cancellationToken, await world.SignedSlotIdsAsync(), "Second");

        // Assert
        Assert.Equal(DomainRule.SignaturesWouldBecomeOutOfDate, ex.Rule);
        Assert.Equal(ReviewThreadState.Resolved, Assert.Single(world.Store.Threads).State);
        Assert.Equal(SlotSignatureState.OutOfDate, (await world.ApprovalAsync()).Slots[0].State);
    }

    /// <summary>
    ///     Test that resolving a Question after a signature needs no acknowledgement, with or without a note.
    /// </summary>
    [Fact]
    public async Task ThreadService_ResolveAsync_QuestionAfterSignature_NeedsNoAcknowledgement()
    {
        // Arrange: a signed review and a chat thread opened afterwards, then a second one
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        var project = world.ProjectId;
        await world.SignAsync(1);
        var plain = await world.Threads.StartThreadAsync(
            project, ReviewId, _anchor, TestActors.Bob, "One", cancellationToken);
        var noted = await world.Threads.StartThreadAsync(
            project, ReviewId, _anchor, TestActors.Bob, "Two", cancellationToken);

        // Act
        await world.Threads.ResolveAsync(project, ReviewId, plain.Id, TestActors.Bob, cancellationToken);
        await world.Threads.ResolveAsync(project, ReviewId, noted.Id, TestActors.Bob, cancellationToken, null, "Answered");

        // Assert
        Assert.All(world.Store.Threads, thread => Assert.Equal(ReviewThreadState.Resolved, thread.State));
        Assert.Equal(SlotSignatureState.Signed, (await world.ApprovalAsync()).Slots[0].State);
    }

    /// <summary>
    ///     Test that reopening a Finding after a signature needs no acknowledgement and keeps the signature, because
    ///     the state of a thread is not part of the digest.
    /// </summary>
    [Fact]
    public async Task ThreadService_ReopenAsync_FindingAfterSignature_NeedsNoAcknowledgement()
    {
        // Arrange: a resolved Finding, then slot 1 signs
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        var project = world.ProjectId;
        var thread = await world.Threads.StartThreadAsync(
            project, ReviewId, _anchor, TestActors.Ann, "Finding", cancellationToken, null, true);
        await world.Threads.ResolveAsync(project, ReviewId, thread.Id, TestActors.Ann, cancellationToken, null, "Done");
        await world.SignAsync(1);

        // Act
        await world.Threads.ReopenAsync(project, ReviewId, thread.Id, TestActors.Ann, cancellationToken);

        // Assert
        Assert.Equal(ReviewThreadState.Open, Assert.Single(world.Store.Threads).State);
        Assert.Equal(SlotSignatureState.Signed, (await world.ApprovalAsync()).Slots[0].State);
    }

    /// <summary>
    ///     Test that promoting and then demoting a thread restores the original digest, so the signature that the
    ///     promotion made out of date is current again.
    /// </summary>
    [Fact]
    public async Task ThreadService_PromoteThenDemoteWithAcknowledgement_RestoresOriginalDigest()
    {
        // Arrange: a resolved chat thread, then slot 1 signs
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await ResolvedChatThreadSignedAsync(withNote: true);
        var project = world.World.ProjectId;
        var original = (await world.World.ApprovalAsync()).Digest;

        // Act
        await world.World.Threads.PromoteAsync(
            project, ReviewId, world.ThreadId, TestActors.Ann, cancellationToken, await world.World.SignedSlotIdsAsync());
        var promoted = await world.World.ApprovalAsync();
        await world.World.Threads.DemoteAsync(project, ReviewId, world.ThreadId, TestActors.Ann, cancellationToken);
        var demoted = await world.World.ApprovalAsync();

        // Assert
        Assert.NotEqual(original, promoted.Digest);
        Assert.Equal(SlotSignatureState.OutOfDate, promoted.Slots[0].State);
        Assert.Equal(original, demoted.Digest);
        Assert.Equal(SlotSignatureState.Signed, demoted.Slots[0].State);
    }

    /// <summary>
    ///     Test that promoting and demoting are audited with the names of the kinds, and by the actor.
    /// </summary>
    [Fact]
    public async Task ThreadService_PromoteAndDemoteAsync_Audit_NamesKindChange()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);

        // Act
        await world.Threads.PromoteAsync(world.Project, ReviewId, id, Who("alt"), cancellationToken);
        await world.Threads.DemoteAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken);

        // Assert
        var entries = world.Store.Audits.Where(audit => audit.Action == "thread.kind-changed").ToList();
        Assert.Equal([$"thread {id}: question -> finding", $"thread {id}: finding -> question"], entries.Select(e => e.Detail));
        Assert.Equal(["alt", "usr"], entries.Select(e => e.ActorUsername));
        Assert.All(entries, entry => Assert.Equal(ReviewId, entry.ReviewId));
    }

    /// <summary>
    ///     Test that including a reply and taking it out are audited with the thread and comment and the change.
    /// </summary>
    [Fact]
    public async Task ThreadService_SetCommentIncludedAsync_Audit_NamesCommentAndChange()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);
        var reply = await world.Threads.ReplyAsync(world.Project, ReviewId, id, Who("alt"), "Reply", cancellationToken);

        // Act
        await world.Threads.SetCommentIncludedAsync(world.Project, ReviewId, id, reply.Id, true, Who("alt"), cancellationToken);
        await world.Threads.SetCommentIncludedAsync(world.Project, ReviewId, id, reply.Id, false, Who("root"), cancellationToken);

        // Assert
        var details = world.Store.Audits
            .Where(audit => audit.Action == "thread.comment-report-changed")
            .Select(audit => audit.Detail);
        Assert.Equal(
            [$"thread {id}, comment {reply.Id}: included in report", $"thread {id}, comment {reply.Id}: taken out of report"],
            details);
    }

    /// <summary>
    ///     Test that resolving with a note audits the hash of the note and never its text.
    /// </summary>
    [Fact]
    public async Task ThreadService_ResolveAsync_WithNote_Audit_HasNoteHashButNotText()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world, include: true);

        // Act
        await world.Threads.ResolveAsync(world.Project, ReviewId, id, Who("usr"), cancellationToken, null, "Secret remedy");

        // Assert
        var entry = Assert.Single(world.Store.Audits, audit => audit.Action == "thread.resolved");
        Assert.StartsWith($"thread {id}; note sha256 ", entry.Detail);
        Assert.DoesNotContain("Secret remedy", entry.Detail);
    }

    /// <summary>
    ///     Test that starting a thread whose comment is in the report is audited as in the report, and a chat thread is
    ///     not.
    /// </summary>
    [Fact]
    public async Task ThreadService_StartThreadAsync_IncludeInReport_Audit_MarksInReport()
    {
        // Arrange, Act
        var world = await WorldAsync();
        await StartAsync(world, include: true);
        await StartAsync(world);

        // Assert
        var details = world.Store.Audits.Where(audit => audit.Action == "thread.started").Select(audit => audit.Detail).ToList();
        Assert.Equal(2, details.Count);
        Assert.EndsWith("; in report", details[0]);
        Assert.DoesNotContain("in report", details[1]);
    }

    /// <summary>
    ///     Test that changes that change nothing are not recorded in the audit trail.
    /// </summary>
    [Fact]
    public async Task ThreadService_NoOpReportMutations_RecordNothing()
    {
        // Arrange: a Finding, a Question with a chat reply, and a resolved Question
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var finding = await StartAsync(world, include: true);
        var question = await StartAsync(world);
        var reply = await world.Threads.ReplyAsync(world.Project, ReviewId, question, Who("alt"), "r", cancellationToken);
        var resolved = await StartAsync(world);
        await world.Threads.ResolveAsync(world.Project, ReviewId, resolved, Who("usr"), cancellationToken);
        var audits = world.Store.Audits.Count;

        // Act
        await world.Threads.PromoteAsync(world.Project, ReviewId, finding, Who("usr"), cancellationToken);
        await world.Threads.DemoteAsync(world.Project, ReviewId, question, Who("usr"), cancellationToken);
        await world.Threads.SetCommentIncludedAsync(world.Project, ReviewId, question, reply.Id, false, Who("alt"), cancellationToken);
        await world.Threads.ResolveAsync(world.Project, ReviewId, resolved, Who("usr"), cancellationToken, null, "Again");

        // Assert
        Assert.Equal(audits, world.Store.Audits.Count);
    }

    /// <summary>
    ///     Arranges the signed-review world with a chat thread that was started and resolved before slot 1 signed.
    /// </summary>
    /// <param name="withNote">Whether the thread is resolved with a note, which a promotion to a Finding needs.</param>
    /// <returns>The world and the identity of the thread.</returns>
    private static async Task<(SignedReviewWorld World, int ThreadId)> ResolvedChatThreadSignedAsync(bool withNote = false)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        var thread = await world.Threads.StartThreadAsync(
            world.ProjectId, ReviewId, _anchor, TestActors.Ann, "Question", cancellationToken);
        await world.Threads.ResolveAsync(
            world.ProjectId, ReviewId, thread.Id, TestActors.Ann, cancellationToken, null, withNote ? "Answered" : null);
        await world.SignAsync(1);
        return (world, thread.Id);
    }

    /// <summary>
    ///     The arranged services of the permission tests.
    /// </summary>
    /// <param name="Store">The review store.</param>
    /// <param name="Threads">The thread service over the real access rules.</param>
    /// <param name="Project">The identity of the project.</param>
    private sealed record World(FakeReviewStore Store, ThreadService Threads, int Project);
}
