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
///     Unit tests of the roles that the report rules of <see cref="ThreadService"/> depend on: who leads a review
///     (<c>ReviewActorRoles.IsLeadAsync</c>) and who owns a reviewer slot (<c>ReviewActorRoles.IsSlotOwnerAsync</c>),
///     directly and through the thread actions, using the real access rules over in-memory stores.
/// </summary>
/// <remarks>
///     Unlike the world of <see cref="ThreadServiceReportTests"/>, the creator of the review is not an administrator
///     here, so the two ways to lead a review can be told apart. The users are: creator (a project user who created
///     the review), manager (a project administrator who did not), sysadmin (a system administrator who is not a member
///     of the project), prim and alt (the primary and the alternate reviewer of the only slot), mem (a plain member),
///     and root (who owns the project and arranges the slot).
/// </remarks>
public class ReviewActorRolesTests
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
    ///     Builds an actor whose display name is the user name.
    /// </summary>
    /// <param name="name">The user name.</param>
    /// <returns>The actor.</returns>
    private static Actor Who(string name)
    {
        return new Actor(name, name);
    }

    /// <summary>
    ///     Builds an actor that was authenticated with a personal access token.
    /// </summary>
    /// <param name="name">The user name.</param>
    /// <returns>The actor.</returns>
    private static Actor ViaToken(string name)
    {
        return new Actor(name, name) { ApiTokenId = "tok-1" };
    }

    /// <summary>
    ///     Arranges the users, the project, the review created by creator, and one slot for prim with alt as the
    ///     alternate.
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
        foreach (var (name, systemAdmin) in new[]
                 {
                     ("root", true), ("sysadmin", true), ("creator", false), ("manager", false), ("prim", false),
                     ("alt", false), ("mem", false)
                 })
        {
            ids[name] = (await users.CreateAsync(name, name, systemAdmin, Password, Actor.CommandLine, cancellationToken)).Id;
        }

        var access = new ProjectAccess(userStore, store);
        var projects = new ProjectService(store, userStore, access, new FakePasswordHasher(), clock);
        var project = await projects.CreateAsync("Own", null, Who("root"), cancellationToken);
        foreach (var name in new[] { "creator", "prim", "alt", "mem" })
        {
            await store.AddMembershipAsync(ids[name], project.Id, ProjectRole.User, cancellationToken);
        }

        await store.AddMembershipAsync(ids["manager"], project.Id, ProjectRole.ProjectAdmin, cancellationToken);
        var repositories = new RepositoryService(new FakeRepositoryStore(), new FakeCredentialProtector(), access, clock);
        var reviews = new ReviewService(store, access, clock, repositories, new FakeGitRepositoryProvider());
        await reviews.CreateDraftAsync(
            project.Id, ReviewId, "First", [new UploadedFile("a.txt", [1, 2, 3])], Who("creator"), cancellationToken);
        var signing = new SigningService(store, users, access, clock);
        var slot = await signing.AddSlotAsync(
            Who("root"), project.Id, ReviewId, ids["prim"], ids["alt"], cancellationToken);
        var review = (await store.GetReviewAsync(project.Id, ReviewId, cancellationToken))!;
        var threads = new ThreadService(store, access, userStore, clock);
        return new World(store, access, userStore, threads, project.Id, review, slot, ids);
    }

    /// <summary>
    ///     Starts a chat thread.
    /// </summary>
    /// <param name="world">The arranged world.</param>
    /// <param name="by">The user name of the author.</param>
    /// <param name="include">Whether the starting comment is included in the report.</param>
    /// <returns>The identity of the thread.</returns>
    private static async Task<int> StartAsync(World world, string by = "mem", bool include = false)
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
    ///     Reads the kind of a thread as the store holds it.
    /// </summary>
    /// <param name="world">The arranged world.</param>
    /// <param name="threadId">The identity of the thread.</param>
    /// <returns>The kind.</returns>
    private static async Task<ThreadKind> KindAsync(World world, int threadId)
    {
        var threads = await world.Threads.ListAsync(world.Project, ReviewId, TestContext.Current.CancellationToken);
        return threads.Single(thread => thread.Id == threadId).Kind;
    }

    /// <summary>
    ///     Test that the creator of the review leads it, whatever the case of the user name.
    /// </summary>
    [Fact]
    public async Task ReviewActorRoles_IsLeadAsync_Creator_IsLeadWithoutRegardToCase()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();

        // Act
        var exact = await ReviewActorRoles.IsLeadAsync(world.Access, Who("creator"), world.Review, cancellationToken);
        var otherCase = await ReviewActorRoles.IsLeadAsync(world.Access, Who("CREATOR"), world.Review, cancellationToken);

        // Assert
        Assert.True(exact);
        Assert.True(otherCase);
    }

    /// <summary>
    ///     Test that a project administrator who did not create the review leads it.
    /// </summary>
    [Fact]
    public async Task ReviewActorRoles_IsLeadAsync_ProjectAdminWhoIsNotCreator_IsLead()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var lead = await ReviewActorRoles.IsLeadAsync(
            world.Access, Who("manager"), world.Review, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual("manager", world.Review.CreatedBy);
        Assert.True(lead);
    }

    /// <summary>
    ///     Test that a system administrator who is not a member of the project leads its reviews.
    /// </summary>
    [Fact]
    public async Task ReviewActorRoles_IsLeadAsync_SystemAdminWhoIsNotMember_IsLead()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var lead = await ReviewActorRoles.IsLeadAsync(
            world.Access, Who("sysadmin"), world.Review, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(await world.Access.IsMemberAsync(
            world.Ids["sysadmin"], world.Project, TestContext.Current.CancellationToken));
        Assert.True(lead);
    }

    /// <summary>
    ///     Test that a creator who was made a read-only member of the project no longer leads the review.
    /// </summary>
    [Fact]
    public async Task ReviewActorRoles_IsLeadAsync_CreatorDowngradedToAuditor_IsNotLead()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        await world.Store.SetMembershipRoleAsync(
            world.Project,
            world.Ids["creator"],
            ProjectRole.Auditor,
            AuditDraft.By(Actor.CommandLine, "test", "downgrade", world.Project),
            false,
            cancellationToken);

        // Act
        var lead = await ReviewActorRoles.IsLeadAsync(world.Access, Who("creator"), world.Review, cancellationToken);

        // Assert
        Assert.Equal("creator", world.Review.CreatedBy);
        Assert.False(lead);
    }

    /// <summary>
    ///     Test that a member who neither created the review nor administers the project, and a user who is not a
    ///     member, do not lead it.
    /// </summary>
    [Fact]
    public async Task ReviewActorRoles_IsLeadAsync_PlainMemberAndOutsider_AreNotLead()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();

        // Act
        var member = await ReviewActorRoles.IsLeadAsync(world.Access, Who("mem"), world.Review, cancellationToken);
        var stranger = await ReviewActorRoles.IsLeadAsync(world.Access, Who("nobody"), world.Review, cancellationToken);

        // Assert
        Assert.False(member);
        Assert.False(stranger);
    }

    /// <summary>
    ///     Test that an actor who was authenticated with an API token, the command line, and no actor at all are never
    ///     a lead, not even when the user behind the token created the review or administers the project.
    /// </summary>
    [Fact]
    public async Task ReviewActorRoles_IsLeadAsync_ApiTokenCommandLineOrNoActor_IsNotLead()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();

        // Act
        var creatorToken = await ReviewActorRoles.IsLeadAsync(world.Access, ViaToken("creator"), world.Review, cancellationToken);
        var adminToken = await ReviewActorRoles.IsLeadAsync(world.Access, ViaToken("manager"), world.Review, cancellationToken);
        var systemToken = await ReviewActorRoles.IsLeadAsync(world.Access, ViaToken("sysadmin"), world.Review, cancellationToken);
        var commandLine = await ReviewActorRoles.IsLeadAsync(world.Access, Actor.CommandLine, world.Review, cancellationToken);
        var none = await ReviewActorRoles.IsLeadAsync(world.Access, null, world.Review, cancellationToken);

        // Assert
        Assert.False(creatorToken);
        Assert.False(adminToken);
        Assert.False(systemToken);
        Assert.False(commandLine);
        Assert.False(none);
    }

    /// <summary>
    ///     Test that a review without a recorded creator has no creator who leads it: only an administrator does.
    /// </summary>
    [Fact]
    public async Task ReviewActorRoles_IsLeadAsync_ReviewWithoutCreator_OnlyAdministratorsLead()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var anonymous = new Review { ProjectId = world.Project, Id = "old", CreatedBy = null };

        // Act
        var member = await ReviewActorRoles.IsLeadAsync(world.Access, Who("creator"), anonymous, cancellationToken);
        var admin = await ReviewActorRoles.IsLeadAsync(world.Access, Who("manager"), anonymous, cancellationToken);

        // Assert
        Assert.False(member);
        Assert.True(admin);
    }

    /// <summary>
    ///     Test that the primary and the alternate reviewer of a slot are slot owners.
    /// </summary>
    [Fact]
    public async Task ReviewActorRoles_IsSlotOwnerAsync_PrimaryAndAlternate_AreSlotOwners()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        IReadOnlyList<ReviewerSlot> slots = [world.Slot];

        // Act
        var primary = await ReviewActorRoles.IsSlotOwnerAsync(world.Users, Who("prim"), slots, cancellationToken);
        var alternate = await ReviewActorRoles.IsSlotOwnerAsync(world.Users, Who("alt"), slots, cancellationToken);

        // Assert
        Assert.Equal(world.Ids["alt"], world.Slot.AlternateUserId);
        Assert.True(primary);
        Assert.True(alternate);
    }

    /// <summary>
    ///     Test that a member without a slot, an unknown user, the command line, no actor, and a review without slots
    ///     have no slot owner.
    /// </summary>
    [Fact]
    public async Task ReviewActorRoles_IsSlotOwnerAsync_NoSlotCommandLineOrNoActor_IsNotSlotOwner()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        IReadOnlyList<ReviewerSlot> slots = [world.Slot];

        // Act
        var member = await ReviewActorRoles.IsSlotOwnerAsync(world.Users, Who("mem"), slots, cancellationToken);
        var unknown = await ReviewActorRoles.IsSlotOwnerAsync(world.Users, Who("nobody"), slots, cancellationToken);
        var commandLine = await ReviewActorRoles.IsSlotOwnerAsync(world.Users, Actor.CommandLine, slots, cancellationToken);
        var none = await ReviewActorRoles.IsSlotOwnerAsync(world.Users, null, slots, cancellationToken);
        var noSlots = await ReviewActorRoles.IsSlotOwnerAsync(world.Users, Who("prim"), [], cancellationToken);

        // Assert
        Assert.False(member);
        Assert.False(unknown);
        Assert.False(commandLine);
        Assert.False(none);
        Assert.False(noSlots);
    }

    /// <summary>
    ///     Test that being the alternate of a slot is enough to promote a thread that somebody else started, and being
    ///     a slot owner behind an API token is too, because the slot is the person's own.
    /// </summary>
    [Fact]
    public async Task ThreadService_PromoteAsync_AlternateOfSlot_Allowed()
    {
        // Arrange: two chat threads started by mem
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var byAlternate = await StartAsync(world);
        var byAlternateToken = await StartAsync(world);

        // Act
        await world.Threads.PromoteAsync(world.Project, ReviewId, byAlternate, Who("alt"), cancellationToken);
        await world.Threads.PromoteAsync(world.Project, ReviewId, byAlternateToken, ViaToken("alt"), cancellationToken);

        // Assert
        Assert.Equal(ThreadKind.Finding, await KindAsync(world, byAlternate));
        Assert.Equal(ThreadKind.Finding, await KindAsync(world, byAlternateToken));
    }

    /// <summary>
    ///     Test that a project administrator who is not the creator, a system administrator who is not a member, and
    ///     the creator of the review each lead it through the thread actions: promote, demote and toggle a reply.
    /// </summary>
    [Fact]
    public async Task ThreadService_ReportActions_LeadWhoIsNotStarterOrReviewer_Allowed()
    {
        // Arrange: a chat thread with a chat reply, both by mem
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);
        var reply = await world.Threads.ReplyAsync(world.Project, ReviewId, id, Who("mem"), "Reply", cancellationToken);

        // Act and Assert: every lead can promote, toggle the reply and demote
        foreach (var lead in new[] { "manager", "sysadmin", "creator" })
        {
            await world.Threads.PromoteAsync(world.Project, ReviewId, id, Who(lead), cancellationToken);
            Assert.Equal(ThreadKind.Finding, await KindAsync(world, id));
            await world.Threads.SetCommentIncludedAsync(world.Project, ReviewId, id, reply.Id, true, Who(lead), cancellationToken);
            await world.Threads.SetCommentIncludedAsync(world.Project, ReviewId, id, reply.Id, false, Who(lead), cancellationToken);
            await world.Threads.DemoteAsync(world.Project, ReviewId, id, Who(lead), cancellationToken);
            Assert.Equal(ThreadKind.Question, await KindAsync(world, id));
        }
    }

    /// <summary>
    ///     Test that the user behind an API token is not a lead through the thread actions: an administrator and the
    ///     creator are refused where the same people signed in with a password are allowed.
    /// </summary>
    [Fact]
    public async Task ThreadService_ReportActions_LeadViaApiToken_ThrowsActorNotAllowed()
    {
        // Arrange: a Finding and a Question, both started by mem, and a chat reply to the Finding
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var question = await StartAsync(world);
        var finding = await StartAsync(world, include: true);
        var reply = await world.Threads.ReplyAsync(world.Project, ReviewId, finding, Who("mem"), "Reply", cancellationToken);
        var audits = world.Store.Audits.Count;

        // Act and Assert
        foreach (var name in new[] { "manager", "sysadmin", "creator" })
        {
            var actor = ViaToken(name);
            var attempts = new Func<Task>[]
            {
                () => world.Threads.PromoteAsync(world.Project, ReviewId, question, actor, cancellationToken),
                () => world.Threads.DemoteAsync(world.Project, ReviewId, finding, actor, cancellationToken),
                () => world.Threads.SetCommentIncludedAsync(
                    world.Project, ReviewId, finding, reply.Id, true, actor, cancellationToken)
            };
            foreach (var attempt in attempts)
            {
                var ex = await Assert.ThrowsAsync<DomainRuleException>(attempt);
                Assert.Equal(DomainRule.ActorNotAllowed, ex.Rule);
            }
        }

        Assert.Equal(ThreadKind.Question, await KindAsync(world, question));
        Assert.Equal(ThreadKind.Finding, await KindAsync(world, finding));
        Assert.Equal(audits, world.Store.Audits.Count);
    }

    /// <summary>
    ///     Test that the command line is refused by the access check before any role is looked at, as a project that
    ///     does not exist.
    /// </summary>
    [Fact]
    public async Task ThreadService_ReportActions_CommandLine_ThrowsProjectNotFound()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world);

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Threads.PromoteAsync(world.Project, ReviewId, id, Actor.CommandLine, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ProjectNotFound, ex.Rule);
        Assert.Equal(ThreadKind.Question, await KindAsync(world, id));
    }

    /// <summary>
    ///     Test that a creator who was made read-only is refused every report action as not allowed, even for the
    ///     thread that the same person started, so the creator no longer leads the review.
    /// </summary>
    [Fact]
    public async Task ThreadService_ReportActions_CreatorDowngradedToAuditor_ThrowsActorNotAllowed()
    {
        // Arrange: a Question started by creator, then creator is made an auditor
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var id = await StartAsync(world, "creator");
        await world.Store.SetMembershipRoleAsync(
            world.Project,
            world.Ids["creator"],
            ProjectRole.Auditor,
            AuditDraft.By(Actor.CommandLine, "test", "downgrade", world.Project),
            false,
            cancellationToken);

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Threads.PromoteAsync(world.Project, ReviewId, id, Who("creator"), cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ActorNotAllowed, ex.Rule);
        Assert.Equal(ThreadKind.Question, await KindAsync(world, id));
    }

    /// <summary>
    ///     The arranged services of the role tests.
    /// </summary>
    /// <param name="Store">The review store.</param>
    /// <param name="Access">The real access rules.</param>
    /// <param name="Users">The user store.</param>
    /// <param name="Threads">The thread service over the real access rules.</param>
    /// <param name="Project">The identity of the project.</param>
    /// <param name="Review">The review, created by creator.</param>
    /// <param name="Slot">The only slot of the review: prim as the primary and alt as the alternate reviewer.</param>
    /// <param name="Ids">The identity of every user by user name.</param>
    private sealed record World(
        FakeReviewStore Store,
        ProjectAccess Access,
        FakeUserStore Users,
        ThreadService Threads,
        int Project,
        Review Review,
        ReviewerSlot Slot,
        IReadOnlyDictionary<string, int> Ids);
}
