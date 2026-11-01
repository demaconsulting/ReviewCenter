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
///     Unit tests of the per-thread capabilities of <see cref="ThreadService.GetCapabilitiesAsync"/> and of
///     <see cref="ThreadCounts"/>: who may promote, demote, resolve and toggle a reply into the report, and how the
///     threads are counted by kind and state.
/// </summary>
/// <remarks>
///     The permission tests use the real access rules with the users root (the lead), usr (starts the threads), alt
///     (a reviewer of a slot), mem (a plain member), out (not a member) and aud (an auditor).
/// </remarks>
public class ThreadCapabilitiesTests
{
    /// <summary>
    ///     The password of every user.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The identifier of the review.
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
    ///     Test that a Question may be promoted by its starter, a slot owner and the lead, but not by another member,
    ///     and that nobody may demote it.
    /// </summary>
    [Fact]
    public async Task ThreadCapabilities_Promote_StarterSlotOwnerAndLead()
    {
        // Arrange
        var world = await WorldAsync();
        var id = await StartAsync(world, "usr");

        // Act
        var starter = await CapabilitiesAsync(world, "usr");
        var slotOwner = await CapabilitiesAsync(world, "alt");
        var lead = await CapabilitiesAsync(world, "root");
        var member = await CapabilitiesAsync(world, "mem");

        // Assert
        Assert.True(starter.CanPromote(id));
        Assert.True(slotOwner.CanPromote(id));
        Assert.True(lead.CanPromote(id));
        Assert.False(member.CanPromote(id));
        Assert.False(starter.CanDemote(id) || slotOwner.CanDemote(id) || lead.CanDemote(id) || member.CanDemote(id));
    }

    /// <summary>
    ///     Test that a Finding may be demoted by its starter and the lead only, and that nobody may promote it.
    /// </summary>
    [Fact]
    public async Task ThreadCapabilities_Demote_StarterAndLeadOnly()
    {
        // Arrange
        var world = await WorldAsync();
        var id = await StartAsync(world, "usr", include: true);

        // Act
        var starter = await CapabilitiesAsync(world, "usr");
        var lead = await CapabilitiesAsync(world, "root");
        var slotOwner = await CapabilitiesAsync(world, "alt");
        var member = await CapabilitiesAsync(world, "mem");

        // Assert
        Assert.True(starter.CanDemote(id));
        Assert.True(lead.CanDemote(id));
        Assert.False(slotOwner.CanDemote(id));
        Assert.False(member.CanDemote(id));
        Assert.False(starter.CanPromote(id) || lead.CanPromote(id) || slotOwner.CanPromote(id));
    }

    /// <summary>
    ///     Test that a resolved Question can be promoted only when it carries a resolution note, as the service
    ///     demands.
    /// </summary>
    [Fact]
    public async Task ThreadCapabilities_ResolvedQuestion_PromoteNeedsResolutionNote()
    {
        // Arrange: a resolved Question without a note and one with a note
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var plain = await StartAsync(world, "usr");
        var noted = await StartAsync(world, "usr");
        await world.Threads.ResolveAsync(world.Project, ReviewId, plain, Who("usr"), cancellationToken);
        await world.Threads.ResolveAsync(world.Project, ReviewId, noted, Who("usr"), cancellationToken, null, "Answered");

        // Act
        var capabilities = await CapabilitiesAsync(world, "usr");

        // Assert
        Assert.False(capabilities.CanPromote(plain));
        Assert.True(capabilities.CanPromote(noted));
    }

    /// <summary>
    ///     Test that an auditor, an outsider and nobody are offered nothing, not even per thread.
    /// </summary>
    [Fact]
    public async Task ThreadCapabilities_Auditor_NothingAllowed()
    {
        // Arrange
        var world = await WorldAsync();
        var id = await StartAsync(world, "usr", include: true);
        await world.Threads.ReplyAsync(
            world.Project, ReviewId, id, Who("alt"), "Reply", TestContext.Current.CancellationToken);

        // Act
        var auditor = await CapabilitiesAsync(world, "aud");
        var outsider = await CapabilitiesAsync(world, "out");
        var nobody = await world.Threads.GetCapabilitiesAsync(
            null, world.Project, ReviewId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ThreadCapabilities.None, auditor);
        Assert.Equal(ThreadCapabilities.None, outsider);
        Assert.Equal(ThreadCapabilities.None, nobody);
        Assert.False(auditor.CanPromote(id) || auditor.CanDemote(id) || auditor.CanResolve(id));
        Assert.Empty(auditor.Threads);
    }

    /// <summary>
    ///     Test that a personal access token is offered what its user is offered, as the service enforces the rules by
    ///     user name, except the rights of the lead, which a token never holds.
    /// </summary>
    [Fact]
    public async Task ThreadCapabilities_ApiToken_ActsForItsUserButIsNeverLead()
    {
        // Arrange: a Question by usr; alt holds a slot, root leads
        var world = await WorldAsync();
        var id = await StartAsync(world, "usr");

        // Act
        var slotOwnerToken = await world.Threads.GetCapabilitiesAsync(
            Token("alt"), world.Project, ReviewId, TestContext.Current.CancellationToken);
        var leadToken = await world.Threads.GetCapabilitiesAsync(
            Token("root"), world.Project, ReviewId, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(slotOwnerToken.CanPromote(id));
        Assert.False(leadToken.CanPromote(id));
        Assert.False(leadToken.CanDemote(id));
    }

    /// <summary>
    ///     Test that a concluded review is locked, so nobody is offered anything.
    /// </summary>
    [Fact]
    public async Task ThreadCapabilities_Concluded_NothingAllowed()
    {
        // Arrange: a Finding, resolved, and all three slots signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        var thread = await world.Threads.StartThreadAsync(
            world.ProjectId, ReviewId, _anchor, TestActors.Ann, "Please fix", cancellationToken, null, true);
        await world.Threads.ResolveAsync(
            world.ProjectId, ReviewId, thread.Id, TestActors.Ann, cancellationToken, null, "Done");
        var before = await world.Threads.GetCapabilitiesAsync(TestActors.Ann, world.ProjectId, ReviewId, cancellationToken);
        await world.SignAsync(1);
        await world.SignAsync(2);
        await world.SignAsync(3);

        // Act
        var after = await world.Threads.GetCapabilitiesAsync(TestActors.Ann, world.ProjectId, ReviewId, cancellationToken);

        // Assert
        Assert.True(before.CanStartThread);
        Assert.True((await world.ApprovalAsync()).IsApproved);
        Assert.Equal(ThreadCapabilities.None, after);
        Assert.False(after.CanDemote(thread.Id));
    }

    /// <summary>
    ///     Test that a reply can be added to the report or taken out by its author and the lead only, and that the
    ///     starting comment can never be toggled.
    /// </summary>
    [Fact]
    public async Task ThreadCapabilities_ToggleReply_AuthorOrLead()
    {
        // Arrange: a Question by usr with a reply of alt
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world, "usr");
        var reply = await world.Threads.ReplyAsync(world.Project, ReviewId, id, Who("alt"), "Reply", cancellationToken);
        var first = (await world.Threads.ListAsync(world.Project, ReviewId, cancellationToken)).Single().Comments[0];

        // Act
        var author = await CapabilitiesAsync(world, "alt");
        var lead = await CapabilitiesAsync(world, "root");
        var starter = await CapabilitiesAsync(world, "usr");
        var member = await CapabilitiesAsync(world, "mem");

        // Assert
        Assert.True(author.CanToggleReport(id, reply.Id));
        Assert.True(lead.CanToggleReport(id, reply.Id));
        Assert.False(starter.CanToggleReport(id, reply.Id));
        Assert.False(member.CanToggleReport(id, reply.Id));
        Assert.False(lead.CanToggleReport(id, first.Id));
        Assert.False(starter.CanToggleReport(id, first.Id));
    }

    /// <summary>
    ///     Test that every contributor may resolve a thread and that an unknown thread offers nothing.
    /// </summary>
    [Fact]
    public async Task ThreadCapabilities_Resolve_AnyContributor_UnknownThreadNothing()
    {
        // Arrange
        var world = await WorldAsync();
        var id = await StartAsync(world, "usr");

        // Act
        var member = await CapabilitiesAsync(world, "mem");

        // Assert
        Assert.True(member.CanResolve(id));
        Assert.False(member.CanResolve(id + 100));
        Assert.Equal(ThreadActions.None, member.ForThread(id + 100));
    }

    /// <summary>
    ///     Test that the counts are split by kind and state, that the sums agree and that the include filter leaves
    ///     threads out.
    /// </summary>
    [Fact]
    public void ThreadCounts_SplitByKindAndState()
    {
        // Arrange: two open Findings, one open Question, one resolved Question, one resolved Finding about a check
        var threads = new[]
        {
            Thread(1, finding: true, open: true),
            Thread(2, finding: true, open: true),
            Thread(3, finding: false, open: true),
            Thread(4, finding: false, open: false),
            Thread(5, finding: true, open: false, check: "c1")
        };

        // Act
        var all = ThreadCounts.From(threads);
        var withoutChecks = ThreadCounts.From(threads, thread => !thread.IsCheckThread);

        // Assert
        Assert.Equal(new ThreadCounts(1, 2, 1, 1), all);
        Assert.Equal(3, all.Open);
        Assert.Equal(2, all.Resolved);
        Assert.Equal(2, all.Questions);
        Assert.Equal(3, all.Findings);
        Assert.Equal(5, all.Total);
        Assert.Equal("2 open findings, 1 open question", all.OpenText);
        Assert.Equal(new ThreadCounts(1, 2, 1, 0), withoutChecks);
    }

    /// <summary>
    ///     Test the wording of open threads for none, one and several of each kind.
    /// </summary>
    /// <param name="questions">The number of open Questions.</param>
    /// <param name="findings">The number of open Findings.</param>
    /// <param name="expected">The expected text.</param>
    [Theory]
    [InlineData(0, 0, "no open threads")]
    [InlineData(1, 0, "1 open question")]
    [InlineData(2, 0, "2 open questions")]
    [InlineData(0, 1, "1 open finding")]
    [InlineData(1, 3, "3 open findings, 1 open question")]
    public void ThreadCounts_OpenPhrase_NamesTheKinds(int questions, int findings, string expected)
    {
        // Act, Assert
        Assert.Equal(expected, ThreadCounts.OpenPhrase(questions, findings));
    }

    /// <summary>
    ///     Test the wording of a single kind of open thread, which names a zero count (the summary strip needs it).
    /// </summary>
    [Fact]
    public void ThreadCounts_KindPhrases_NameZeroOneAndMany()
    {
        // Act, Assert
        Assert.Equal("0 open questions", ThreadCounts.OpenQuestionsPhrase(0));
        Assert.Equal("1 open question", ThreadCounts.OpenQuestionsPhrase(1));
        Assert.Equal("4 open questions", ThreadCounts.OpenQuestionsPhrase(4));
        Assert.Equal("0 open findings", ThreadCounts.OpenFindingsPhrase(0));
        Assert.Equal("1 open finding", ThreadCounts.OpenFindingsPhrase(1));
        Assert.Equal("2 open findings", ThreadCounts.OpenFindingsPhrase(2));
    }

    /// <summary>
    ///     Test that counting rejects a missing list.
    /// </summary>
    [Fact]
    public void ThreadCounts_From_NullThreads_Throws()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ThreadCounts.From(null!));
    }

    /// <summary>
    ///     Test that equal capabilities compare equal by value, including the per-thread actions.
    /// </summary>
    [Fact]
    public void ThreadCapabilities_Equality_ComparesPerThreadActionsByValue()
    {
        // Arrange
        var first = new ThreadCapabilities(true, false)
        {
            Threads = [new ThreadActions(1, true, false, true, [2, 3])]
        };
        var same = new ThreadCapabilities(true, false)
        {
            Threads = [new ThreadActions(1, true, false, true, [2, 3])]
        };
        var other = new ThreadCapabilities(true, false)
        {
            Threads = [new ThreadActions(1, true, false, true, [2])]
        };

        // Act, Assert
        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, other);
        Assert.NotEqual(first, ThreadCapabilities.None);
    }

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
    ///     Builds an actor who comes with a personal access token of the user.
    /// </summary>
    /// <param name="name">The user name.</param>
    /// <returns>The actor.</returns>
    private static Actor Token(string name)
    {
        return new Actor(name, name) { ApiTokenId = "tok-1" };
    }

    /// <summary>
    ///     Builds a thread for counting.
    /// </summary>
    /// <param name="id">The identity of the thread.</param>
    /// <param name="finding">Whether the starting comment is in the report.</param>
    /// <param name="open">Whether the thread is open.</param>
    /// <param name="check">The check the thread is about, or <c>null</c>.</param>
    /// <returns>The thread.</returns>
    private static ReviewThread Thread(int id, bool finding, bool open, string? check = null)
    {
        return new ReviewThread
        {
            Id = id,
            CheckItemId = check,
            State = open ? ReviewThreadState.Open : ReviewThreadState.Resolved,
            Comments = [new ReviewComment { Id = id * 10, ThreadId = id, IncludeInReport = finding }]
        };
    }

    /// <summary>
    ///     Reads the capabilities of a user in the review.
    /// </summary>
    /// <param name="world">The arranged world.</param>
    /// <param name="name">The user name.</param>
    /// <returns>The capabilities.</returns>
    private static Task<ThreadCapabilities> CapabilitiesAsync(World world, string name)
    {
        return world.Threads.GetCapabilitiesAsync(
            Who(name), world.Project, ReviewId, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Starts a thread.
    /// </summary>
    /// <param name="world">The arranged world.</param>
    /// <param name="by">The user name of the author.</param>
    /// <param name="include">Whether the starting comment is included in the report.</param>
    /// <returns>The identity of the thread.</returns>
    private static async Task<int> StartAsync(World world, string by, bool include = false)
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
        return new World(new ThreadService(store, access, userStore, clock), project.Id);
    }

    /// <summary>
    ///     The arranged services of the permission tests.
    /// </summary>
    /// <param name="Threads">The thread service over the real access rules.</param>
    /// <param name="Project">The identity of the project.</param>
    private sealed record World(ThreadService Threads, int Project);
}
