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
///     Tests that the services enforce project membership themselves, with the real <see cref="ProjectAccess"/>: a
///     user who is not a member of the project of a review (a user of another project, a user of no project, and the
///     command line) cannot change anything in it, and is told what a missing project would be told.
/// </summary>
public class ProjectAccessGuardTests
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
    ///     Test that every operation on a review (thread, comment, checklist, slot, signature, candidates and review
    ///     creation) is refused as a missing project for anybody who is not a member, and changes nothing.
    /// </summary>
    /// <param name="who">The user name of the actor; the empty text stands for the command line.</param>
    [Theory]
    [InlineData("out")]
    [InlineData("oth")]
    [InlineData("")]
    public async Task ReviewOperations_NotAMember_AreRefusedAsAMissingProjectAndChangeNothing(string who)
    {
        // Arrange: a review with a thread, a slot, and a checklist in the project; the actor is not a member of it
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = who.Length == 0 ? Actor.CommandLine : new Actor(who, who);
        var project = world.Own;
        var item = DefaultChecklist.Current.Sections[0].Items[0].Id;
        var section = DefaultChecklist.Current.Sections[0].Id;
        var auditBefore = world.Store.Audits.Count;
        var operations = new Dictionary<string, Func<Task>>
        {
            ["create review"] = () => world.Reviews.CreateDraftAsync(
                project, "rev-2", "Second", [new UploadedFile("a.txt", [1])], actor, cancellationToken),
            ["start thread"] = () => world.Threads.StartThreadAsync(
                project, "rev-1", new ThreadAnchor("a.txt"), actor, "hello", cancellationToken),
            ["reply"] = () => world.Threads.ReplyAsync(project, "rev-1", world.ThreadId, actor, "hello", cancellationToken),
            ["resolve"] = () => world.Threads.ResolveAsync(project, "rev-1", world.ThreadId, actor, cancellationToken),
            ["reopen"] = () => world.Threads.ReopenAsync(project, "rev-1", world.ThreadId, actor, cancellationToken),
            ["answer"] = () => world.Checklists.SetAnswerAsync(
                project, "rev-1", item, ChecklistOutcome.Pass, null, actor, cancellationToken),
            ["applicability"] = () => world.Checklists.SetSectionApplicabilityAsync(
                project, "rev-1", section, false, "not needed", actor, cancellationToken),
            ["add slot"] = () => world.Signing.AddSlotAsync(
                actor, project, "rev-1", world.Ids["usr"], null, cancellationToken),
            ["update slot"] = () => world.Signing.UpdateSlotAsync(
                actor, project, "rev-1", world.SlotId, world.Ids["usr"], null, cancellationToken),
            ["remove slot"] = () => world.Signing.RemoveSlotAsync(actor, project, "rev-1", world.SlotId, cancellationToken),
            ["sign"] = () => world.Signing.SignAsync(
                actor, project, "rev-1", world.SlotId, "digest", Password, cancellationToken),
            ["candidates"] = () => world.Signing.ListCandidatesAsync(actor, project, cancellationToken)
        };

        // Act
        var rules = new Dictionary<string, DomainRule>();
        foreach (var (name, operation) in operations)
        {
            var exception = await Assert.ThrowsAsync<DomainRuleException>(operation);
            rules[name] = exception.Rule;
        }

        // Assert: each one is answered like a project that does not exist, and nothing was stored
        Assert.All(rules, pair => Assert.Equal(DomainRule.ProjectNotFound, pair.Value));
        Assert.Equal(operations.Count, rules.Count);
        Assert.Equal(auditBefore, world.Store.Audits.Count);
        Assert.Single(world.Store.Threads);
        Assert.Single(await world.Store.ListSlotsAsync(project, "rev-1", cancellationToken));
        Assert.Empty(await world.Store.ListSignaturesAsync(project, "rev-1", cancellationToken));
    }

    /// <summary>
    ///     Test that a member of a project can do the same things, so the refusals above are about membership and
    ///     not about the arranged data.
    /// </summary>
    [Fact]
    public async Task ReviewOperations_Member_AreAllowed()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = new Actor("usr", "usr");

        // Act
        var thread = await world.Threads.StartThreadAsync(
            world.Own, "rev-1", new ThreadAnchor("a.txt"), member, "hello", cancellationToken);
        await world.Checklists.SetAnswerAsync(
            world.Own, "rev-1", DefaultChecklist.Current.Sections[0].Items[0].Id, ChecklistOutcome.Pass, null, member, cancellationToken);
        var candidates = await world.Signing.ListCandidatesAsync(member, world.Own, cancellationToken);

        // Assert: the candidates are the members of this project only (not the outsider, not the other project's user)
        Assert.NotNull(thread);
        Assert.Equal(["usr"], candidates.Select(c => c.Username));
    }

    /// <summary>
    ///     Test that a repository of another project cannot be used to create a review or be changed by a member of
    ///     this project, even by guessing its identity, and that the repository lists of a project are not readable
    ///     by outsiders.
    /// </summary>
    [Fact]
    public async Task Repositories_OfAnotherProject_CannotBeUsedOrChanged()
    {
        // Arrange: a repository of the other project, and a member of the own project
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = new Actor("root", "root");
        var member = new Actor("usr", "usr");
        var foreign = await world.Repositories.CreateAsync(
            world.Other, "foreign", "https://example.com/org/foreign.git", null, null, root, cancellationToken);

        // Act
        var create = await Assert.ThrowsAsync<DomainRuleException>(() => world.Reviews.CreateFromRepositoryAsync(
            world.Own, "rev-3", "Third", new RepositorySource(foreign.Id, "main", null, "**/*"), member, cancellationToken));
        var plan = await Assert.ThrowsAsync<DomainRuleException>(() => world.Planner.PlanAsync(
            new Actor("out", "out"), world.Own, foreign.Id, "main", BaselineMode.Explicit, "v1", cancellationToken));
        var planForeign = await Assert.ThrowsAsync<DomainRuleException>(() => world.Planner.PlanAsync(
            member, world.Own, foreign.Id, "main", BaselineMode.Explicit, "v1", cancellationToken));
        var update = await Assert.ThrowsAsync<DomainRuleException>(() => world.Repositories.UpdateAsync(
            foreign.Id, "renamed", foreign.Location, null, TokenChange.Keep, member, cancellationToken));
        var disable = await Assert.ThrowsAsync<DomainRuleException>(() => world.Repositories.SetEnabledAsync(
            foreign.Id, false, member, cancellationToken));
        var listHidden = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Repositories.ListEnabledAsync(member, world.Other, cancellationToken));
        var listAdmin = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Repositories.ListAsync(member, world.Own, cancellationToken));

        // Assert: the foreign repository does not exist for them; a plain member may see the own list of names only
        Assert.Equal(DomainRule.RepositoryNotFound, create.Rule);
        Assert.Equal(DomainRule.ProjectNotFound, plan.Rule);
        Assert.Equal(DomainRule.RepositoryNotFound, planForeign.Rule);
        Assert.Equal(DomainRule.RepositoryNotFound, update.Rule);
        Assert.Equal(DomainRule.RepositoryNotFound, disable.Rule);
        Assert.Equal(DomainRule.ProjectNotFound, listHidden.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, listAdmin.Rule);
        Assert.Empty(await world.Repositories.ListEnabledAsync(member, world.Own, cancellationToken));
    }

    /// <summary>
    ///     Test that the last system administrator cannot be demoted or disabled, and that another one can be once a
    ///     second administrator exists.
    /// </summary>
    [Fact]
    public async Task Users_LastSystemAdministrator_CannotBeDemotedOrDisabled()
    {
        // Arrange: root is the only system administrator
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var demote = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Users.SetSystemAdminAsync("root", false, Actor.CommandLine, cancellationToken));
        var disable = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Users.SetEnabledAsync("root", false, Actor.CommandLine, cancellationToken));
        await world.Users.SetSystemAdminAsync("usr", true, Actor.CommandLine, cancellationToken);
        await world.Users.SetSystemAdminAsync("root", false, Actor.CommandLine, cancellationToken);
        var demoteLast = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Users.SetSystemAdminAsync("usr", false, Actor.CommandLine, cancellationToken));
        var disableLast = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Users.SetEnabledAsync("usr", false, Actor.CommandLine, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.LastAdmin, demote.Rule);
        Assert.Equal(DomainRule.LastAdmin, disable.Rule);
        Assert.Equal(DomainRule.LastAdmin, demoteLast.Rule);
        Assert.Equal(DomainRule.LastAdmin, disableLast.Rule);
    }

    /// <summary>
    ///     Arranges two projects, the users, a review of the first project with a thread and a slot.
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
        foreach (var (name, admin) in new[] { ("root", true), ("usr", false), ("out", false), ("oth", false) })
        {
            ids[name] = (await users.CreateAsync(name, name, admin, Password, Actor.CommandLine, cancellationToken)).Id;
        }

        var access = new ProjectAccess(userStore, store);
        var projects = new ProjectService(store, userStore, access, new FakePasswordHasher(), clock);
        var root = new Actor("root", "root");
        var own = await projects.CreateAsync("Own", null, root, cancellationToken);
        var other = await projects.CreateAsync("Other", null, root, cancellationToken);
        await store.AddMembershipAsync(ids["usr"], own.Id, ProjectRole.User, cancellationToken);
        await store.AddMembershipAsync(ids["oth"], other.Id, ProjectRole.User, cancellationToken);

        var repositories = new RepositoryService(new FakeRepositoryStore(), new FakeCredentialProtector(), access, clock);
        var git = new FakeGitRepositoryProvider();
        var reviews = new ReviewService(store, access, clock, repositories, git);
        var threads = new ThreadService(store, access, clock);
        var checklists = new ChecklistService(store, access, clock);
        var signing = new SigningService(store, users, access, clock);
        var planner = new ReviewPlanner(store, reviews, repositories, git, signing, new FakeReviewSetParser(), access);

        var member = new Actor("usr", "usr");

        // The review is created by the administrator: its author cannot be a reviewer, and the member is the reviewer
        await reviews.CreateDraftAsync(own.Id, "rev-1", "First", [new UploadedFile("a.txt", [1, 2, 3])], root, cancellationToken);
        var thread = await threads.StartThreadAsync(own.Id, "rev-1", new ThreadAnchor("a.txt"), member, "first", cancellationToken);
        var slot = await signing.AddSlotAsync(member, own.Id, "rev-1", ids["usr"], null, cancellationToken);
        return new World(
            store, users, repositories, reviews, threads, checklists, signing, planner, own.Id, other.Id, thread.Id, slot.Id, ids);
    }

    /// <summary>
    ///     The arranged services and data.
    /// </summary>
    /// <param name="Store">The review store.</param>
    /// <param name="Users">The user service.</param>
    /// <param name="Repositories">The repository service.</param>
    /// <param name="Reviews">The review service.</param>
    /// <param name="Threads">The thread service.</param>
    /// <param name="Checklists">The checklist service.</param>
    /// <param name="Signing">The signing service.</param>
    /// <param name="Planner">The review planner.</param>
    /// <param name="Own">The project with the review.</param>
    /// <param name="Other">The project of the user <c>oth</c>.</param>
    /// <param name="ThreadId">The thread of the review.</param>
    /// <param name="SlotId">The slot of the review.</param>
    /// <param name="Ids">The identities of the users by user name.</param>
    private sealed record World(
        FakeReviewStore Store,
        UserService Users,
        RepositoryService Repositories,
        ReviewService Reviews,
        ThreadService Threads,
        ChecklistService Checklists,
        SigningService Signing,
        ReviewPlanner Planner,
        int Own,
        int Other,
        int ThreadId,
        int SlotId,
        IReadOnlyDictionary<string, int> Ids);
}
