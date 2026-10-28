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
///     Unit tests for <see cref="ProjectAccess"/> and the membership operations of <see cref="ProjectService"/>,
///     using in-memory stores.
/// </summary>
public class ProjectAccessTests
{
    /// <summary>
    ///     The password of every arranged user.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The instant reported by the clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Test that every kind of actor gets the right answers for the project it belongs to and for another one.
    /// </summary>
    /// <param name="who">The actor to test.</param>
    /// <param name="ownView">Whether the actor may see the project it belongs to.</param>
    /// <param name="ownAdminister">Whether the actor may administer that project.</param>
    /// <param name="otherView">Whether the actor may see the other project.</param>
    /// <param name="otherAdminister">Whether the actor may administer the other project.</param>
    /// <param name="systemAdmin">Whether the actor is a system administrator.</param>
    [Theory]
    [InlineData("root", true, true, true, true, true)]
    [InlineData("padm", true, true, false, false, false)]
    [InlineData("usr", true, false, false, false, false)]
    [InlineData("out", false, false, false, false, false)]
    [InlineData("off", false, false, false, false, false)]
    [InlineData("offadm", false, false, false, false, false)]
    [InlineData("ghost", false, false, false, false, false)]
    [InlineData("", false, false, false, false, false)]
    public async Task ProjectAccess_Matrix_ActorKinds_GetTheirRights(
        string who,
        bool ownView,
        bool ownAdminister,
        bool otherView,
        bool otherAdminister,
        bool systemAdmin)
    {
        // Arrange: a system administrator, a project administrator, a user, a non-member, and disabled accounts
        var world = await WorldAsync();
        var actor = new Actor(who, who);

        // Act
        var results = new[]
        {
            await world.Access.CanViewAsync(actor, world.Own, TestContext.Current.CancellationToken),
            await world.Access.CanAdministerAsync(actor, world.Own, TestContext.Current.CancellationToken),
            await world.Access.CanViewAsync(actor, world.Other, TestContext.Current.CancellationToken),
            await world.Access.CanAdministerAsync(actor, world.Other, TestContext.Current.CancellationToken),
            await world.Access.IsSystemAdminAsync(actor, TestContext.Current.CancellationToken)
        };

        // Assert
        Assert.Equal([ownView, ownAdminister, otherView, otherAdminister, systemAdmin], results);
    }

    /// <summary>
    ///     Test that no actor, the command line and a project that does not exist get nothing, not even a system
    ///     administrator's rights.
    /// </summary>
    [Fact]
    public async Task ProjectAccess_Matrix_AnonymousCommandLineAndMissingProject_GetNothing()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act, Assert: nobody and the command line see nothing
        Assert.False(await world.Access.CanViewAsync(null, world.Own, cancellationToken));
        Assert.False(await world.Access.CanViewAsync(Actor.CommandLine, world.Own, cancellationToken));
        Assert.False(await world.Access.IsSystemAdminAsync(null, cancellationToken));
        Assert.Empty(await world.Access.ListVisibleProjectsAsync(null, cancellationToken));
        Assert.Empty(await world.Access.ListVisibleProjectsAsync(Actor.CommandLine, cancellationToken));

        // Assert: even a system administrator sees no project that does not exist
        Assert.False(await world.Access.CanViewAsync(new Actor("root", "Root"), 9999, cancellationToken));
        Assert.False(await world.Access.CanAdministerAsync(new Actor("root", "Root"), 9999, cancellationToken));
    }

    /// <summary>
    ///     Test that the listing contains everything for a system administrator, only the member projects for a
    ///     member and nothing for a non-member.
    /// </summary>
    [Fact]
    public async Task ProjectAccess_ListVisibleProjectsAsync_EachKind_SeesOnlyItsProjects()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var forAdmin = await world.Access.ListVisibleProjectsAsync(new Actor("root", "Root"), cancellationToken);
        var forMember = await world.Access.ListVisibleProjectsAsync(new Actor("usr", "Usr"), cancellationToken);
        var forOutsider = await world.Access.ListVisibleProjectsAsync(new Actor("out", "Out"), cancellationToken);
        var forDisabled = await world.Access.ListVisibleProjectsAsync(new Actor("off", "Off"), cancellationToken);

        // Assert
        Assert.Equal([world.Own, world.Other], forAdmin.Select(p => p.Id).Order());
        Assert.Equal([world.Own], forMember.Select(p => p.Id));
        Assert.Empty(forOutsider);
        Assert.Empty(forDisabled);
    }

    /// <summary>
    ///     Test that the failures name a missing project and a hidden project alike, and that a plain member who
    ///     tries to administer is told so.
    /// </summary>
    [Fact]
    public async Task ProjectAccess_Require_HiddenMissingAndNotAdministrator_FailWithTheRightRule()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var outsider = new Actor("out", "Out");
        var member = new Actor("usr", "Usr");

        // Act
        var hidden = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Access.RequireViewAsync(outsider, world.Own, cancellationToken));
        var missing = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Access.RequireViewAsync(outsider, 9999, cancellationToken));
        var hiddenAdmin = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Access.RequireAdministerAsync(outsider, world.Own, cancellationToken));
        var notAdmin = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Access.RequireAdministerAsync(member, world.Own, cancellationToken));
        var notSystem = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Access.RequireSystemAdminAsync(new Actor("padm", "Padm"), cancellationToken));
        await world.Access.RequireViewAsync(member, world.Own, cancellationToken);
        await world.Access.RequireAdministerAsync(new Actor("padm", "Padm"), world.Own, cancellationToken);
        await world.Access.RequireSystemAdminAsync(new Actor("root", "Root"), cancellationToken);

        // Assert: a hidden project is told apart from a missing one by nothing at all
        Assert.Equal(DomainRule.ProjectNotFound, hidden.Rule);
        Assert.Equal(DomainRule.ProjectNotFound, missing.Rule);
        Assert.Equal(missing.Message, hidden.Message);
        Assert.Equal(DomainRule.ProjectNotFound, hiddenAdmin.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, notAdmin.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, notSystem.Rule);
    }

    /// <summary>
    ///     Test that taking rights away works at once: a removed membership, a disabled account and a demotion
    ///     all change the next answer, because nothing is cached.
    /// </summary>
    [Fact]
    public async Task ProjectAccess_Revocation_TakesEffectOnTheNextCall()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = new Actor("usr", "Usr");
        var padm = new Actor("padm", "Padm");
        Assert.True(await world.Access.CanViewAsync(member, world.Own, cancellationToken));
        Assert.True(await world.Access.CanAdministerAsync(padm, world.Own, cancellationToken));

        // Act: remove the membership, and demote the project administrator to user
        await world.Store.RemoveMembershipAsync(
            world.Own, world.Ids["usr"], AuditTestExtensions.Draft(), false, cancellationToken);
        await world.Store.SetMembershipRoleAsync(
            world.Own, world.Ids["padm"], ProjectRole.User, AuditTestExtensions.Draft(), false, cancellationToken);

        // Assert
        Assert.False(await world.Access.CanViewAsync(member, world.Own, cancellationToken));
        Assert.True(await world.Access.CanViewAsync(padm, world.Own, cancellationToken));
        Assert.False(await world.Access.CanAdministerAsync(padm, world.Own, cancellationToken));

        // Act: disable the system administrator's account (a second one keeps the invariant)
        await world.Users.SetEnabledAsync("root", false, TestActors.Ann, cancellationToken);

        // Assert
        Assert.False(await world.Access.CanViewAsync(new Actor("root", "Root"), world.Own, cancellationToken));
    }

    /// <summary>
    ///     Test that an actor made from a personal access token, which carries the user name of its owner, has the
    ///     rights of the owner and no more.
    /// </summary>
    [Fact]
    public async Task ProjectAccess_TokenActor_HasTheMembershipsOfItsOwner()
    {
        // Arrange: the actor a token produces is named like its owner
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var viaToken = new Actor("usr", "Usr (token 'ci')");

        // Act, Assert
        Assert.True(await world.Access.CanViewAsync(viaToken, world.Own, cancellationToken));
        Assert.False(await world.Access.CanViewAsync(viaToken, world.Other, cancellationToken));
        Assert.False(await world.Access.CanAdministerAsync(viaToken, world.Own, cancellationToken));
    }

    /// <summary>
    ///     Test that only a system administrator creates a project, and that the new project has no members.
    /// </summary>
    [Fact]
    public async Task ProjectService_CreateAsync_OnlySystemAdmin_NewProjectHasNoMembers()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var refused = new List<DomainRule>();
        foreach (var name in new[] { "padm", "usr", "out", "off" })
        {
            var exception = await Assert.ThrowsAsync<DomainRuleException>(
                () => world.Projects.CreateAsync("New", null, new Actor(name, name), cancellationToken));
            refused.Add(exception.Rule);
        }

        var created = await world.Projects.CreateAsync("New", null, new Actor("root", "Root"), cancellationToken);

        // Assert
        Assert.All(refused, rule => Assert.Equal(DomainRule.ActorNotAllowed, rule));
        Assert.Empty(await world.Store.ListMembershipsAsync(created.Id, cancellationToken));
        Assert.Empty(await world.Projects.ListMembersAsync(new Actor("root", "Root"), created.Id, cancellationToken));
    }

    /// <summary>
    ///     Test that a system administrator adds, re-roles and removes members, that each change is audited with the
    ///     project, and that a plain member or an outsider may not.
    /// </summary>
    [Fact]
    public async Task ProjectService_Members_SystemAdminManages_OthersAreRefused()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = new Actor("root", "Root");
        var auditBefore = world.Store.Audits.Count;

        // Act: the system administrator adds the non-member, promotes and removes them
        await world.Projects.AddMemberAsync(root, world.Own, "out", ProjectRole.User, cancellationToken);
        await world.Projects.SetMemberRoleAsync(root, world.Own, "out", ProjectRole.ProjectAdmin, cancellationToken);
        var members = await world.Projects.ListMembersAsync(root, world.Own, cancellationToken);
        await world.Projects.RemoveMemberAsync(root, world.Own, "out", cancellationToken);

        // Assert
        Assert.Equal(["off", "offadm", "out", "padm", "usr"], members.Select(m => m.Username));
        Assert.Equal(ProjectRole.ProjectAdmin, members.Single(m => m.Username == "out").Role);
        var drafts = world.Store.Audits.Skip(auditBefore).ToList();
        Assert.Equal(["project.member-added", "project.member-role-changed", "project.member-removed"], drafts.Select(d => d.Action));
        Assert.All(drafts, d => Assert.Equal(world.Own, d.ProjectId));

        // Act, Assert: a plain member may not manage members; an outsider is told the project does not exist
        foreach (var (name, expected) in new[]
                 {
                     ("usr", DomainRule.ActorNotAllowed),
                     ("out", DomainRule.ProjectNotFound)
                 })
        {
            var actor = new Actor(name, name);
            var add = await Assert.ThrowsAsync<DomainRuleException>(
                () => world.Projects.AddMemberAsync(actor, world.Own, "out", ProjectRole.User, cancellationToken));
            var remove = await Assert.ThrowsAsync<DomainRuleException>(
                () => world.Projects.RemoveMemberAsync(actor, world.Own, "usr", cancellationToken));
            var role = await Assert.ThrowsAsync<DomainRuleException>(
                () => world.Projects.SetMemberRoleAsync(actor, world.Own, "usr", ProjectRole.ProjectAdmin, cancellationToken));
            Assert.All([add, remove, role], e => Assert.Equal(expected, e.Rule));
        }
    }

    /// <summary>
    ///     Test that a project administrator lists the members of their project only, and a plain member or an
    ///     outsider cannot.
    /// </summary>
    [Fact]
    public async Task ProjectService_ListMembersAsync_ProjectAdminOwnProjectOnly()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var padm = new Actor("padm", "Padm");

        // Act
        var own = await world.Projects.ListMembersAsync(padm, world.Own, cancellationToken);
        var other = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.ListMembersAsync(padm, world.Other, cancellationToken));
        var plain = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.ListMembersAsync(new Actor("usr", "Usr"), world.Own, cancellationToken));

        // Assert: the other project looks like a missing one; a plain member is told it may not
        Assert.Equal(["off", "offadm", "padm", "usr"], own.Select(m => m.Username));
        Assert.Equal(DomainRule.ProjectNotFound, other.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, plain.Rule);
    }

    /// <summary>
    ///     Test that a membership of an unknown user or an unknown project and a doubled or missing membership are
    ///     refused with their own rules.
    /// </summary>
    [Fact]
    public async Task ProjectService_Members_UnknownDoubledAndMissing_ViolateTheirRules()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = new Actor("root", "Root");

        // Act
        var unknownUser = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.AddMemberAsync(root, world.Own, "nobody", ProjectRole.User, cancellationToken));
        var unknownProject = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.AddMemberAsync(root, 9999, "out", ProjectRole.User, cancellationToken));
        var doubled = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.AddMemberAsync(root, world.Own, "usr", ProjectRole.User, cancellationToken));
        var notMember = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.RemoveMemberAsync(root, world.Own, "out", cancellationToken));
        var badRole = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => world.Projects.AddMemberAsync(root, world.Own, "out", (ProjectRole)99, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.UserNotFound, unknownUser.Rule);
        Assert.Equal(DomainRule.ProjectNotFound, unknownProject.Rule);
        Assert.Equal(DomainRule.MembershipExists, doubled.Rule);
        Assert.Equal(DomainRule.MembershipNotFound, notMember.Rule);
        Assert.NotNull(badRole);
    }

    /// <summary>
    ///     Test that the project reads are filtered: a hidden project reads as <c>null</c>, like a missing one.
    /// </summary>
    [Fact]
    public async Task ProjectService_GetAsync_HiddenProject_ReadsAsMissing()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act, Assert
        Assert.NotNull(await world.Projects.GetAsync(new Actor("usr", "Usr"), world.Own, cancellationToken));
        Assert.Null(await world.Projects.GetAsync(new Actor("usr", "Usr"), world.Other, cancellationToken));
        Assert.Null(await world.Projects.GetAsync(new Actor("out", "Out"), world.Own, cancellationToken));
        Assert.Null(await world.Projects.GetAsync(new Actor("root", "Root"), 9999, cancellationToken));
        Assert.Equal(
            [world.Own],
            (await world.Projects.ListAsync(new Actor("usr", "Usr"), cancellationToken)).Select(p => p.Id));
    }

    /// <summary>
    ///     Test that the constructor refuses missing dependencies.
    /// </summary>
    [Fact]
    public void ProjectAccess_Constructor_NullArguments_Throws()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentNullException>(() => new ProjectAccess(null!, new FakeReviewStore()));
        Assert.Throws<ArgumentNullException>(() => new ProjectAccess(new FakeUserStore(), null!));
    }

    /// <summary>
    ///     Arranges the stores, two projects and the users the matrix needs.
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
                     ("root", true), ("ann", true), ("padm", false), ("usr", false), ("out", false), ("off", false),
                     ("offadm", false)
                 })
        {
            ids[name] = (await users.CreateAsync(name, name, admin, Password, Actor.CommandLine, cancellationToken)).Id;
        }

        // The disabled accounts: one plain, one that is a project administrator of the own project
        await users.SetEnabledAsync("off", false, Actor.CommandLine, cancellationToken);
        await users.SetEnabledAsync("offadm", false, Actor.CommandLine, cancellationToken);

        var access = new ProjectAccess(userStore, store);
        var projects = new ProjectService(store, userStore, access, new FakePasswordHasher(), clock);
        var own = await projects.CreateAsync("Own", null, new Actor("root", "Root"), cancellationToken);
        var other = await projects.CreateAsync("Other", null, new Actor("root", "Root"), cancellationToken);
        await store.AddMembershipAsync(ids["padm"], own.Id, ProjectRole.ProjectAdmin, cancellationToken);
        await store.AddMembershipAsync(ids["usr"], own.Id, ProjectRole.User, cancellationToken);
        await store.AddMembershipAsync(ids["off"], own.Id, ProjectRole.User, cancellationToken);
        await store.AddMembershipAsync(ids["offadm"], own.Id, ProjectRole.ProjectAdmin, cancellationToken);
        return new World(store, users, access, projects, own.Id, other.Id, ids);
    }

    /// <summary>
    ///     The arranged stores, services and projects.
    /// </summary>
    /// <param name="Store">The review store.</param>
    /// <param name="Users">The user service.</param>
    /// <param name="Access">The access service under test.</param>
    /// <param name="Projects">The project service.</param>
    /// <param name="Own">The project the member and project administrator belong to.</param>
    /// <param name="Other">The project nobody but the system administrators belongs to.</param>
    /// <param name="Ids">The identities of the users by user name.</param>
    private sealed record World(
        FakeReviewStore Store,
        UserService Users,
        ProjectAccess Access,
        ProjectService Projects,
        int Own,
        int Other,
        IReadOnlyDictionary<string, int> Ids);
}
