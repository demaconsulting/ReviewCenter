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
///     Unit tests of the member management of <see cref="ProjectService"/> for project administrators: who may do
///     what, no user enumeration, the throttle, the last-project-administrator rule and account creation.
/// </summary>
public class ProjectMemberServiceTests
{
    /// <summary>
    ///     The password of every arranged user.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The instant at which the clock starts.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Test that the actions on members are allowed for a system administrator and for a project administrator
    ///     of the project only; a plain member is told it may not and everybody else sees no project.
    /// </summary>
    /// <param name="who">The user name of the actor.</param>
    /// <param name="refusal">The rule that refuses the actor, or <c>null</c> when the actor is allowed.</param>
    [Theory]
    [InlineData("root", null)]
    [InlineData("pa", null)]
    [InlineData("mem", DomainRule.ActorNotAllowed)]
    [InlineData("out", DomainRule.ProjectNotFound)]
    [InlineData("padm", DomainRule.ProjectNotFound)]
    public async Task ProjectService_MemberActions_ActorKinds_AreAllowedOrRefused(string who, DomainRule? refusal)
    {
        // Arrange, Act, Assert: every action on a world of its own, so one action cannot disturb the next
        foreach (var action in new[] { "add", "remove", "role", "create", "list" })
        {
            var world = await WorldAsync();
            var before = await world.MembershipsAsync();
            var actor = new Actor(who, who);
            var task = RunAsync(world, actor, action);
            if (refusal is null)
            {
                await task;
                continue;
            }

            var exception = await Assert.ThrowsAsync<DomainRuleException>(() => task);
            Assert.Equal(refusal, exception.Rule);

            // A refusal changes nothing: no membership, no account
            Assert.Equal(before, await world.MembershipsAsync());
            Assert.Null(await world.UserStore.GetByUsernameAsync("fresh", TestContext.Current.CancellationToken));
        }
    }

    /// <summary>
    ///     Test that a project administrator cannot make a system administrator: the account that is created is
    ///     none, and the roles offered are project roles only.
    /// </summary>
    [Fact]
    public async Task ProjectService_CreateMemberAccountAsync_ProjectAdmin_CreatesNoSystemAdministrator()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        await world.Projects.CreateMemberAccountAsync(
            new Actor("pa", "pa"), world.Project, "fresh", "Fresh Person", ProjectRole.ProjectAdmin, cancellationToken);

        // Assert
        var created = await world.UserStore.GetByUsernameAsync("fresh", cancellationToken);
        Assert.False(created!.IsSystemAdmin);
        Assert.True(created.Enabled);
        var membership = await world.Store.GetMembershipAsync(created.Id, world.Project, cancellationToken);
        Assert.Equal(ProjectRole.ProjectAdmin, membership!.Role);
    }

    /// <summary>
    ///     Test that adding a name that does not exist and adding the name of a disabled user are answered with the
    ///     same rule and the same words, so the answer does not reveal which names exist.
    /// </summary>
    [Fact]
    public async Task ProjectService_AddMemberAsync_UnknownAndDisabledName_AreIndistinguishable()
    {
        // Arrange: a disabled user and an enabled one
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = new Actor("pa", "pa");

        // Act
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.AddMemberAsync(
                actor, world.Project, "nobody-here", ProjectRole.User, cancellationToken));
        var disabled = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.AddMemberAsync(actor, world.Project, "off", ProjectRole.User, cancellationToken));
        var added = await world.Projects.AddMemberAsync(
            actor, world.Project, "  CAROL ", ProjectRole.User, cancellationToken);

        // Assert: the same rule and message; a partial name finds nothing; the exact name (any case) does
        Assert.Equal(unknown.Rule, disabled.Rule);
        Assert.Equal(unknown.Message, disabled.Message);
        Assert.DoesNotContain("nobody-here", unknown.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.AddMemberAsync(actor, world.Project, "ca", ProjectRole.User, cancellationToken));
        Assert.Equal("carol", added.Username);
    }

    /// <summary>
    ///     Test that removing or changing the role of an unknown name and a user who is no member of the project are
    ///     answered alike.
    /// </summary>
    [Fact]
    public async Task ProjectService_RemoveAndSetRole_UnknownNameAndNonMember_AreIndistinguishable()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = new Actor("pa", "pa");

        // Act
        var removeUnknown = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.RemoveMemberAsync(actor, world.Project, "nobody-here", cancellationToken));
        var removeOutsider = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.RemoveMemberAsync(actor, world.Project, "out", cancellationToken));
        var roleUnknown = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.SetMemberRoleAsync(
                actor, world.Project, "nobody-here", ProjectRole.User, cancellationToken));
        var roleOutsider = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.SetMemberRoleAsync(actor, world.Project, "out", ProjectRole.User, cancellationToken));

        // Assert
        Assert.All(
            [removeUnknown, removeOutsider, roleUnknown, roleOutsider],
            e => Assert.Equal(DomainRule.MembershipNotFound, e.Rule));
        Assert.Single(
            new[] { removeUnknown, removeOutsider, roleUnknown, roleOutsider }.Select(e => e.Message).Distinct());
    }

    /// <summary>
    ///     Test that the actor is stopped after ten failed attempts, that the limit counts per actor, that a system
    ///     administrator is never stopped, and that the window ends after ten minutes.
    /// </summary>
    [Fact]
    public async Task ProjectService_AddMemberAsync_TooManyFailures_ThrottlesPerActorUntilTheWindowEnds()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = new Actor("pa", "pa");
        for (var i = 0; i < ProjectService.MaxFailedAttempts; i++)
        {
            await Assert.ThrowsAsync<DomainRuleException>(
                () => world.Projects.AddMemberAsync(
                    actor, world.Project, $"guess-{i}", ProjectRole.User, cancellationToken));
        }

        // Act: the eleventh attempt, even with a name that exists; another project administrator; a system
        // administrator
        var stopped = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.AddMemberAsync(actor, world.Project, "carol", ProjectRole.User, cancellationToken));
        var createStopped = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.CreateMemberAccountAsync(
                actor, world.Project, "fresh", "Fresh", ProjectRole.User, cancellationToken));
        var other = await world.Projects.AddMemberAsync(
            new Actor("pb", "pb"), world.Project, "carol", ProjectRole.User, cancellationToken);
        for (var i = 0; i < ProjectService.MaxFailedAttempts + 2; i++)
        {
            await Assert.ThrowsAsync<DomainRuleException>(() => world.Projects.AddMemberAsync(
                new Actor("root", "root"), world.Project, $"guess-{i}", ProjectRole.User, cancellationToken));
        }

        // Assert: stopped with the throttle rule, and the others are not
        Assert.Equal(DomainRule.TooManyAttempts, stopped.Rule);
        Assert.Equal(DomainRule.TooManyAttempts, createStopped.Rule);
        Assert.Equal("carol", other.Username);

        // Act: the window passes
        world.Clock.Advance(ProjectService.ThrottleWindow);
        await world.Store.RemoveMembershipAsync(
            world.Project, other.UserId, AuditTestExtensions.Draft(), false, cancellationToken);
        var again = await world.Projects.AddMemberAsync(
            actor, world.Project, "carol", ProjectRole.User, cancellationToken);

        // Assert: the actor may try again
        Assert.Equal("carol", again.Username);
    }

    /// <summary>
    ///     Test that parallel attempts of one actor cannot get past the limit while their lookups are still running:
    ///     the attempt is reserved before the lookup, so exactly the limit is admitted and the rest is refused with the
    ///     throttle rule before it reaches the lookup.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ProjectService_AddMemberAsync_ConcurrentAttempts_AreAdmittedUpToTheLimitOnly()
    {
        // Arrange: a lookup that holds every attempt that is admitted
        var world = await WorldAsync(gatedPrefix: "guess-");
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = new Actor("pa", "pa");
        const int attempts = ProjectService.MaxFailedAttempts + 15;

        // Act: many attempts at once; the refused ones finish while the admitted ones wait in the lookup
        var tasks = Enumerable.Range(0, attempts)
            .Select(i => Task.Run(
                () => world.Projects.AddMemberAsync(
                    actor, world.Project, $"guess-{i}", ProjectRole.User, cancellationToken),
                cancellationToken))
            .ToList();
        await world.Gate!.WhenEnteredAsync(ProjectService.MaxFailedAttempts);
        await WhenCompletedAsync(tasks, attempts - ProjectService.MaxFailedAttempts);
        var refusedBeforeRelease = tasks.Count(t => t.IsCompleted);
        var enteredBeforeRelease = world.Gate.Entered;
        world.Gate.Open();
        var rules = new List<DomainRule>();
        foreach (var task in tasks)
        {
            rules.Add((await Assert.ThrowsAsync<DomainRuleException>(() => task)).Rule);
        }

        // Assert: exactly the limit got to the lookup (and failed there); the others were refused without it
        Assert.Equal(attempts - ProjectService.MaxFailedAttempts, refusedBeforeRelease);
        Assert.Equal(ProjectService.MaxFailedAttempts, enteredBeforeRelease);
        Assert.Equal(ProjectService.MaxFailedAttempts, world.Gate.Entered);
        Assert.Equal(ProjectService.MaxFailedAttempts, rules.Count(r => r == DomainRule.UserNotFound));
        Assert.Equal(attempts - ProjectService.MaxFailedAttempts, rules.Count(r => r == DomainRule.TooManyAttempts));
        Assert.Equal(
            ProjectService.MaxFailedAttempts,
            world.AuditStore.Entries.Count(e => e.Action == "project.member-add-failed"));
    }

    /// <summary>
    ///     Test that a system administrator is not limited by the reservation either: every parallel attempt
    ///     reaches the lookup.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ProjectService_AddMemberAsync_ConcurrentAttemptsOfSystemAdministrator_AreNeverThrottled()
    {
        // Arrange
        var world = await WorldAsync(gatedPrefix: "guess-");
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = new Actor("root", "root");
        const int attempts = ProjectService.MaxFailedAttempts + 15;

        // Act
        var tasks = Enumerable.Range(0, attempts)
            .Select(i => Task.Run(
                () => world.Projects.AddMemberAsync(
                    actor, world.Project, $"guess-{i}", ProjectRole.User, cancellationToken),
                cancellationToken))
            .ToList();
        await world.Gate!.WhenEnteredAsync(attempts);
        world.Gate.Open();

        // Assert: all of them were looked up and failed with the one message
        foreach (var task in tasks)
        {
            Assert.Equal(DomainRule.UserNotFound, (await Assert.ThrowsAsync<DomainRuleException>(() => task)).Rule);
        }
    }

    /// <summary>
    ///     Test that an attempt whose lookup throws stays counted, so a failing lookup cannot be used to try names
    ///     without limit.
    /// </summary>
    [Fact]
    public async Task ProjectService_AddMemberAsync_LookupThrows_KeepsTheAttemptCounted()
    {
        // Arrange: a lookup that fails as soon as it runs
        var world = await WorldAsync(gatedPrefix: "guess-");
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = new Actor("pa", "pa");
        world.Gate!.LookupFailure = new InvalidOperationException("The store is down.");
        world.Gate.Open();

        // Act
        for (var i = 0; i < ProjectService.MaxFailedAttempts; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => world.Projects.AddMemberAsync(
                actor, world.Project, $"guess-{i}", ProjectRole.User, cancellationToken));
        }

        var stopped = await Assert.ThrowsAsync<DomainRuleException>(() => world.Projects.AddMemberAsync(
            actor, world.Project, "guess-next", ProjectRole.User, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.TooManyAttempts, stopped.Rule);
    }

    /// <summary>
    ///     Test that a success gives its reservation back: after nine failures any number of created accounts
    ///     succeeds, one more failure is still allowed, and then the actor is stopped.
    /// </summary>
    [Fact]
    public async Task ProjectService_CreateMemberAccountAsync_Successes_DoNotCountAgainstTheLimit()
    {
        // Arrange: one failure short of the limit
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = new Actor("pa", "pa");
        for (var i = 0; i < ProjectService.MaxFailedAttempts - 1; i++)
        {
            await Assert.ThrowsAsync<DomainRuleException>(() => world.Projects.AddMemberAsync(
                actor, world.Project, $"guess-{i}", ProjectRole.User, cancellationToken));
        }

        // Act: more successes than the limit, an invalid input (not a failed guess), the last allowed failure
        for (var i = 0; i < ProjectService.MaxFailedAttempts + 5; i++)
        {
            await world.Projects.CreateMemberAccountAsync(
                actor, world.Project, $"fresh-{i}", "Fresh Person", ProjectRole.User, cancellationToken);
        }

        await Assert.ThrowsAsync<DomainRuleException>(() => world.Projects.CreateMemberAccountAsync(
            actor, world.Project, "x", "Fresh Person", ProjectRole.User, cancellationToken));
        var lastFailure = await Assert.ThrowsAsync<DomainRuleException>(() => world.Projects.AddMemberAsync(
            actor, world.Project, "guess-last", ProjectRole.User, cancellationToken));
        var stopped = await Assert.ThrowsAsync<DomainRuleException>(() => world.Projects.AddMemberAsync(
            actor, world.Project, "carol", ProjectRole.User, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.UserNotFound, lastFailure.Rule);
        Assert.Equal(DomainRule.TooManyAttempts, stopped.Rule);
    }

    /// <summary>
    ///     Test that every failed attempt to add a member is audited with the project and the name typed, also for a
    ///     system administrator, and that a success writes no failure entry.
    /// </summary>
    [Fact]
    public async Task ProjectService_AddMemberAsync_FailedAttempts_AreAuditedWithTheProject()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: a failure of a project administrator, one of a system administrator, then a success
        await Assert.ThrowsAsync<DomainRuleException>(() => world.Projects.AddMemberAsync(
            new Actor("pa", "pa"), world.Project, "ghost", ProjectRole.User, cancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(() => world.Projects.AddMemberAsync(
            new Actor("root", "root"), world.Project, "phantom", ProjectRole.User, cancellationToken));
        await world.Projects.AddMemberAsync(
            new Actor("pa", "pa"), world.Project, "carol", ProjectRole.User, cancellationToken);

        // Assert
        var failures = world.AuditStore.Entries.Where(e => e.Action == "project.member-add-failed").ToList();
        Assert.Equal(2, failures.Count);
        Assert.All(failures, e => Assert.Equal(world.Project, e.ProjectId));
        Assert.Contains("ghost", failures[0].Detail, StringComparison.Ordinal);
        Assert.Contains("pa", failures[0].ActorName, StringComparison.Ordinal);
        Assert.Contains("phantom", failures[1].Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test the last-project-administrator rule for a project administrator: refused for the only administrator
    ///     (also themselves, by removal and by demotion), allowed once another enabled administrator exists, and a
    ///     disabled administrator does not count.
    /// </summary>
    [Fact]
    public async Task ProjectService_RemoveAndDemote_LastProjectAdmin_IsRefusedForAProjectAdmin()
    {
        // Arrange: pa is the only enabled administrator; offadm is a disabled one
        var world = await WorldAsync(withSecondAdmin: false);
        var cancellationToken = TestContext.Current.CancellationToken;
        var pa = new Actor("pa", "pa");

        // Act: the only enabled administrator removes or demotes themselves
        var remove = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.RemoveMemberAsync(pa, world.Project, "pa", cancellationToken));
        var demote = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.SetMemberRoleAsync(pa, world.Project, "pa", ProjectRole.User, cancellationToken));

        // Assert: refused with an explanation, and nothing is stored or audited
        Assert.Equal(DomainRule.LastProjectAdmin, remove.Rule);
        Assert.Equal(DomainRule.LastProjectAdmin, demote.Rule);
        Assert.Contains("administrator", remove.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProjectRole.ProjectAdmin, (await world.RoleOfAsync("pa"))!.Value);
        Assert.DoesNotContain(
            world.Store.Audits, d => d.Action is "project.member-removed" or "project.member-role-changed");

        // Act: with a second enabled administrator the first may demote themselves, then the second is the last
        await world.Projects.AddMemberAsync(pa, world.Project, "pb", ProjectRole.ProjectAdmin, cancellationToken);
        await world.Projects.SetMemberRoleAsync(pa, world.Project, "pa", ProjectRole.User, cancellationToken);
        var lastOne = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.RemoveMemberAsync(new Actor("pb", "pb"), world.Project, "pb", cancellationToken));

        // Assert
        Assert.Equal(ProjectRole.User, await world.RoleOfAsync("pa"));
        Assert.Equal(DomainRule.LastProjectAdmin, lastOne.Rule);
    }

    /// <summary>
    ///     Test that a system administrator may remove the last project administrator, that a disabled project
    ///     administrator can be removed, and that a disabled administrator does not count as another one.
    /// </summary>
    [Fact]
    public async Task ProjectService_RemoveMemberAsync_SystemAdminAndDisabledTarget_AreNotBlocked()
    {
        // Arrange: pa is the only enabled administrator, offadm a disabled one
        var world = await WorldAsync(withSecondAdmin: false);
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = new Actor("root", "root");
        var pa = new Actor("pa", "pa");

        // Act: a project administrator removes the disabled one, which never blocks
        await world.Projects.RemoveMemberAsync(pa, world.Project, "offadm", cancellationToken);
        var refused = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.RemoveMemberAsync(pa, world.Project, "pa", cancellationToken));
        await world.Projects.RemoveMemberAsync(root, world.Project, "pa", cancellationToken);

        // Assert: only the system administrator emptied the project of administrators
        Assert.Equal(DomainRule.LastProjectAdmin, refused.Rule);
        Assert.Null(await world.RoleOfAsync("pa"));
        Assert.Null(await world.RoleOfAsync("offadm"));
    }

    /// <summary>
    ///     Test that an account created for a project has a temporary password of sixteen characters that differs
    ///     from call to call, is stored only as a hash, never reaches the audit trail, and must be changed.
    /// </summary>
    [Fact]
    public async Task ProjectService_CreateMemberAccountAsync_TemporaryPassword_IsRandomHashedAndNeverAudited()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = new Actor("pa", "pa");

        // Act
        var first = await world.Projects.CreateMemberAccountAsync(
            actor, world.Project, "fresh-1", "Fresh One", ProjectRole.User, cancellationToken);
        var second = await world.Projects.CreateMemberAccountAsync(
            actor, world.Project, "fresh-2", "Fresh Two", ProjectRole.User, cancellationToken);

        // Assert: the password, the hash that verifies, the flag, the membership and the one audit entry
        Assert.Equal(16, first.TemporaryPassword.Length);
        Assert.NotEqual(first.TemporaryPassword, second.TemporaryPassword);
        var stored = (await world.UserStore.GetByUsernameAsync("fresh-1", cancellationToken))!;
        Assert.True(new FakePasswordHasher().Verify(stored.PasswordHash, first.TemporaryPassword));
        Assert.NotEqual(first.TemporaryPassword, stored.PasswordHash);
        Assert.True(stored.MustChangePassword);
        Assert.Equal(first.Member.UserId, stored.Id);
        Assert.Equal(ProjectRole.User, await world.RoleOfAsync("fresh-1"));
        var drafts = world.UserStore.Audits.Where(d => d.Action == "project.account-created").ToList();
        Assert.Equal(2, drafts.Count);
        Assert.All(drafts, d => Assert.Equal(world.Project, d.ProjectId));
        Assert.DoesNotContain(
            world.UserStore.Audits.Concat(world.Store.Audits),
            d => d.Detail.Contains(first.TemporaryPassword, StringComparison.Ordinal)
                 || d.Detail.Contains(second.TemporaryPassword, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that the text of a new account never contains its temporary password, so that logging or printing the
    ///     value cannot leak it.
    /// </summary>
    [Fact]
    public async Task NewMemberAccount_ToString_DoesNotContainTemporaryPassword()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await world.Projects.CreateMemberAccountAsync(
            new Actor("pa", "pa"), world.Project, "fresh-3", "Fresh Three", ProjectRole.User, cancellationToken);
        var known = new NewMemberAccount(created.Member, "correct horse battery");

        // Act
        var text = known.ToString();
        var interpolated = $"{known}";
        var real = created.ToString();

        // Assert: neither the known value nor the generated password appears, and the member still does
        Assert.DoesNotContain("correct horse battery", text, StringComparison.Ordinal);
        Assert.DoesNotContain("correct horse battery", interpolated, StringComparison.Ordinal);
        Assert.DoesNotContain(created.TemporaryPassword, real, StringComparison.Ordinal);
        Assert.Contains(nameof(NewMemberAccount.Member), text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a taken user name, an invalid user name and an invalid display name create no account and no
    ///     membership, and that a taken name is answered generically, counted and audited.
    /// </summary>
    [Fact]
    public async Task ProjectService_CreateMemberAccountAsync_TakenAndInvalidInput_CreatesNothing()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = new Actor("pa", "pa");
        var before = await world.MembershipsAsync();
        var usersBefore = (await world.UserStore.ListAsync(cancellationToken)).Count;

        // Act
        var taken = await Assert.ThrowsAsync<DomainRuleException>(() => world.Projects.CreateMemberAccountAsync(
            actor, world.Project, "OUT", "Whoever", ProjectRole.User, cancellationToken));
        var badName = await Assert.ThrowsAsync<DomainRuleException>(() => world.Projects.CreateMemberAccountAsync(
            actor, world.Project, "a b", "Fine", ProjectRole.User, cancellationToken));
        var badDisplay = await Assert.ThrowsAsync<DomainRuleException>(() => world.Projects.CreateMemberAccountAsync(
            actor, world.Project, "fresh", "  ", ProjectRole.User, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.UserNameTaken, taken.Rule);
        Assert.Equal(DomainRule.UserNameInvalid, badName.Rule);
        Assert.Equal(DomainRule.DisplayNameInvalid, badDisplay.Rule);
        Assert.Equal(before, await world.MembershipsAsync());
        Assert.Equal(usersBefore, (await world.UserStore.ListAsync(cancellationToken)).Count);
        Assert.Single(world.AuditStore.Entries, e => e.Action == "project.member-add-failed");
    }

    /// <summary>
    ///     Test that the project service refuses missing dependencies.
    /// </summary>
    [Fact]
    public void ProjectService_Constructor_NullArguments_Throws()
    {
        // Arrange
        var store = new FakeReviewStore();
        var users = new FakeUserStore(store);
        var access = new AllowAllProjectAccess(store);
        var hasher = new FakePasswordHasher();
        var clock = new FixedTimeProvider(_now);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new ProjectService(null!, users, access, hasher, clock));
        Assert.Throws<ArgumentNullException>(() => new ProjectService(store, null!, access, hasher, clock));
        Assert.Throws<ArgumentNullException>(() => new ProjectService(store, users, null!, hasher, clock));
        Assert.Throws<ArgumentNullException>(() => new ProjectService(store, users, access, null!, clock));
        Assert.Throws<ArgumentNullException>(() => new ProjectService(store, users, access, hasher, null!));
    }

    /// <summary>
    ///     Runs one action on the members of the project.
    /// </summary>
    /// <param name="world">The arranged world.</param>
    /// <param name="actor">The actor.</param>
    /// <param name="action">One of add, remove, role, create or list.</param>
    /// <returns>A task that completes when the action did.</returns>
    private static async Task RunAsync(World world, Actor actor, string action)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        switch (action)
        {
            case "add":
                await world.Projects.AddMemberAsync(actor, world.Project, "carol", ProjectRole.User, cancellationToken);
                break;
            case "remove":
                await world.Projects.RemoveMemberAsync(actor, world.Project, "mem", cancellationToken);
                break;
            case "role":
                await world.Projects.SetMemberRoleAsync(
                    actor, world.Project, "mem", ProjectRole.ProjectAdmin, cancellationToken);
                break;
            case "create":
                await world.Projects.CreateMemberAccountAsync(
                    actor, world.Project, "fresh", "Fresh", ProjectRole.User, cancellationToken);
                break;
            default:
                await world.Projects.ListMembersAsync(actor, world.Project, cancellationToken);
                break;
        }
    }

    /// <summary>
    ///     Waits until a number of tasks have completed, without polling: each wait ends when one more task finishes,
    ///     so the test does not depend on how fast the parallel work runs.
    /// </summary>
    /// <param name="tasks">The tasks.</param>
    /// <param name="count">The number of tasks that must have completed.</param>
    /// <returns>A task that completes when that many tasks have completed.</returns>
    private static async Task WhenCompletedAsync(IReadOnlyList<Task> tasks, int count)
    {
        while (tasks.Count(t => t.IsCompleted) < count)
        {
            await Task.WhenAny(tasks.Where(t => !t.IsCompleted));
        }
    }

    /// <summary>
    ///     Arranges the stores, the users and one project with its members.
    /// </summary>
    /// <param name="withSecondAdmin">Whether <c>pb</c> is a second enabled project administrator.</param>
    /// <param name="gatedPrefix">
    ///     When set, the service reads users through a <see cref="GatedUserStore"/> that holds back the lookup of the
    ///     names that start with it.
    /// </param>
    /// <returns>The arranged world.</returns>
    private static async Task<World> WorldAsync(bool withSecondAdmin = true, string? gatedPrefix = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new MovableTimeProvider(_now);
        var store = new FakeReviewStore();
        var userStore = new FakeUserStore(store);
        var hasher = new FakePasswordHasher();
        var ids = new Dictionary<string, int>();
        foreach (var (name, admin) in new[]
                 {
                     ("root", true), ("pa", false), ("pb", false), ("mem", false), ("out", false), ("padm", false),
                     ("carol", false), ("off", false), ("offadm", false)
                 })
        {
            // Stored directly: the short names of the actors would not pass the validation of a new account
            var stored = await userStore.AddAsync(
                new User
                {
                    Username = name,
                    DisplayName = name,
                    IsSystemAdmin = admin,
                    Enabled = !name.StartsWith("off", StringComparison.Ordinal),
                    PasswordHash = hasher.Hash(Password),
                    Created = _now
                },
                created => AuditDraft.By(Actor.CommandLine, "user.created", created.Username),
                cancellationToken);
            ids[name] = stored.Id;
        }

        var auditStore = new FakeAuditStore(clock);
        var access = new ProjectAccess(userStore, store);
        var gate = gatedPrefix is null ? null : new GatedUserStore(userStore, gatedPrefix);
        var projects = new ProjectService(
            store,
            gate is null ? userStore : gate, access, new FakePasswordHasher(), clock, new AuditService(auditStore));
        var project = await projects.CreateAsync("Own", null, new Actor("root", "root"), cancellationToken);
        var other = await projects.CreateAsync("Other", null, new Actor("root", "root"), cancellationToken);
        await store.AddMembershipAsync(ids["pa"], project.Id, ProjectRole.ProjectAdmin, cancellationToken);
        if (withSecondAdmin)
        {
            await store.AddMembershipAsync(ids["pb"], project.Id, ProjectRole.ProjectAdmin, cancellationToken);
        }

        await store.AddMembershipAsync(ids["mem"], project.Id, ProjectRole.User, cancellationToken);
        await store.AddMembershipAsync(ids["off"], project.Id, ProjectRole.User, cancellationToken);
        await store.AddMembershipAsync(ids["offadm"], project.Id, ProjectRole.ProjectAdmin, cancellationToken);
        await store.AddMembershipAsync(ids["padm"], other.Id, ProjectRole.ProjectAdmin, cancellationToken);
        return new World(store, userStore, projects, auditStore, clock, project.Id, ids, gate);
    }

    /// <summary>
    ///     The arranged stores, service and project.
    /// </summary>
    /// <param name="Store">The review store.</param>
    /// <param name="UserStore">The user store.</param>
    /// <param name="Projects">The project service under test, over the real <see cref="ProjectAccess"/>.</param>
    /// <param name="AuditStore">The store that receives the audit entries of the service.</param>
    /// <param name="Clock">The clock the service reads.</param>
    /// <param name="Project">The project the tests work on.</param>
    /// <param name="Ids">The identities of the users by user name.</param>
    /// <param name="Gate">The user store that holds back lookups, when the world was arranged with one.</param>
    private sealed record World(
        FakeReviewStore Store,
        FakeUserStore UserStore,
        ProjectService Projects,
        FakeAuditStore AuditStore,
        MovableTimeProvider Clock,
        int Project,
        IReadOnlyDictionary<string, int> Ids,
        GatedUserStore? Gate = null)
    {
        /// <summary>
        ///     Reads the memberships of the project as text, in a stable order.
        /// </summary>
        /// <returns>One entry per membership.</returns>
        public async Task<IReadOnlyList<string>> MembershipsAsync()
        {
            var list = await Store.ListMembershipsAsync(Project, TestContext.Current.CancellationToken);
            return list.Select(m => $"{m.UserId}:{m.Role}").Order().ToList();
        }

        /// <summary>
        ///     Reads the role a user has in the project.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <returns>The role, or <c>null</c> when the user is no member.</returns>
        public async Task<ProjectRole?> RoleOfAsync(string username)
        {
            var user = await UserStore.GetByUsernameAsync(username, TestContext.Current.CancellationToken);
            var membership = user is null
                ? null
                : await Store.GetMembershipAsync(user.Id, Project, TestContext.Current.CancellationToken);
            return membership?.Role;
        }
    }
}
