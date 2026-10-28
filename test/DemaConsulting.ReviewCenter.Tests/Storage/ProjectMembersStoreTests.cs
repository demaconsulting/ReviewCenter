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
using DemaConsulting.ReviewCenter.Tests.Core;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests of the member administration of the stores against a real SQLite database file: the last project
///     administrator rule under concurrency, the atomic creation of an account together with its membership, the
///     project-only audit listing, and the stored password-change flag.
/// </summary>
public class ProjectMembersStoreTests
{
    /// <summary>
    ///     The creation time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _created = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Builds a user for the tests.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="mustChangePassword">Whether the user has to change its password.</param>
    /// <returns>The user.</returns>
    private static User NewUser(string username, bool mustChangePassword = false)
    {
        return new User
        {
            Username = username,
            DisplayName = username,
            Enabled = true,
            PasswordHash = "hash",
            MustChangePassword = mustChangePassword,
            Created = _created
        };
    }

    /// <summary>
    ///     Adds a project with two administrators.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <returns>The identities of the two administrators and the project.</returns>
    private static async Task<(int Ann, int Bob, int Apollo)> ArrangeTwoAdminsAsync(TestStore db)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ann = await db.Users.AddAsync(NewUser("ann"), cancellationToken);
        var bob = await db.Users.AddAsync(NewUser("bob"), cancellationToken);
        var apollo = await db.Store.AddProjectAsync(
            new Project { Name = "Apollo", Created = _created }, cancellationToken);
        await db.Store.AddMembershipAsync(ann.Id, apollo.Id, ProjectRole.ProjectAdmin, cancellationToken);
        await db.Store.AddMembershipAsync(bob.Id, apollo.Id, ProjectRole.ProjectAdmin, cancellationToken);
        return (ann.Id, bob.Id, apollo.Id);
    }

    /// <summary>
    ///     Builds a membership audit draft.
    /// </summary>
    /// <param name="action">The audit action.</param>
    /// <param name="projectId">The project.</param>
    /// <returns>The draft.</returns>
    private static AuditDraft Draft(string action, int projectId)
    {
        return AuditDraft.By(TestActors.Ann, action, "detail", projectId);
    }

    /// <summary>
    ///     Test that two administrators removing each other at the same time cannot remove both: exactly one removal
    ///     succeeds, one administrator remains, and only the successful removal is audited.
    /// </summary>
    [Fact]
    public async Task ReviewStore_RemoveMembershipAsync_TwoAdminsRemoveEachOther_ExactlyOneSucceeds()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var (ann, bob, apollo) = await ArrangeTwoAdminsAsync(db);

        // Act: both removals race, each demanding that another administrator remains
        var results = await Task.WhenAll(
            TryAsync(() => db.Store.RemoveMembershipAsync(
                apollo, ann, Draft("project.member-removed", apollo), true, cancellationToken)),
            TryAsync(() => db.Store.RemoveMembershipAsync(
                apollo, bob, Draft("project.member-removed", apollo), true, cancellationToken)));

        // Assert
        Assert.Single(results, r => r is null);
        var refused = Assert.Single(results, r => r is not null);
        Assert.Equal(DomainRule.LastProjectAdmin, refused!.Rule);
        var remaining = await db.Store.ListMembershipsAsync(apollo, cancellationToken);
        Assert.Equal(ProjectRole.ProjectAdmin, Assert.Single(remaining).Role);
        var events = await db.Audit.ListAsync(apollo, null, null, null, 100, cancellationToken);
        Assert.Single(events, e => e.Action == "project.member-removed");
    }

    /// <summary>
    ///     Test that one administrator removing the other while that one demotes the first cannot leave the project
    ///     without an administrator.
    /// </summary>
    [Fact]
    public async Task ReviewStore_MembershipChanges_RemoveRacesDemote_LeavesAnAdministrator()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var (ann, bob, apollo) = await ArrangeTwoAdminsAsync(db);

        // Act
        var results = await Task.WhenAll(
            TryAsync(() => db.Store.RemoveMembershipAsync(
                apollo, ann, Draft("project.member-removed", apollo), true, cancellationToken)),
            TryAsync(() => db.Store.SetMembershipRoleAsync(
                apollo, bob, ProjectRole.User, Draft("project.member-role-changed", apollo), true, cancellationToken)));

        // Assert: exactly one refused, and one administrator remains
        Assert.Single(results, r => r is not null);
        var remaining = await db.Store.ListMembershipsAsync(apollo, cancellationToken);
        Assert.Single(remaining, m => m.Role == ProjectRole.ProjectAdmin);
    }

    /// <summary>
    ///     Test that a refused change of the last administrator changes nothing and writes no audit event, while the
    ///     same change without the rule (a system administrator) goes through.
    /// </summary>
    [Fact]
    public async Task ReviewStore_RemoveMembershipAsync_LastAdmin_RefusedAndRolledBackUnlessExempt()
    {
        // Arrange: ann is the only administrator
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var (ann, bob, apollo) = await ArrangeTwoAdminsAsync(db);
        await db.Store.RemoveMembershipAsync(
            apollo, bob, Draft("project.member-removed", apollo), false, cancellationToken);

        // Act: the rule refuses the removal and the demotion
        var remove = await Assert.ThrowsAsync<DomainRuleException>(
            () => db.Store.RemoveMembershipAsync(
                apollo, ann, Draft("project.member-removed", apollo), true, cancellationToken));
        var demote = await Assert.ThrowsAsync<DomainRuleException>(
            () => db.Store.SetMembershipRoleAsync(
                apollo, ann, ProjectRole.User, Draft("project.member-role-changed", apollo), true, cancellationToken));

        // Assert: still an administrator, and only the first removal (of bob) was audited
        Assert.Equal(DomainRule.LastProjectAdmin, remove.Rule);
        Assert.Equal(DomainRule.LastProjectAdmin, demote.Rule);
        var annMembership = await db.Store.GetMembershipAsync(ann, apollo, cancellationToken);
        Assert.Equal(ProjectRole.ProjectAdmin, annMembership!.Role);
        var events = await db.Audit.ListAsync(apollo, null, null, null, 100, cancellationToken);
        Assert.Single(events, e => e.Action.StartsWith("project.member-", StringComparison.Ordinal));

        // Act: the exempt removal
        var removed = await db.Store.RemoveMembershipAsync(
            apollo, ann, Draft("project.member-removed", apollo), false, cancellationToken);

        // Assert
        Assert.True(removed);
        Assert.Empty(await db.Store.ListMembershipsAsync(apollo, cancellationToken));
    }

    /// <summary>
    ///     Test that a disabled administrator does not count as the other administrator.
    /// </summary>
    [Fact]
    public async Task ReviewStore_RemoveMembershipAsync_OtherAdminDisabled_IsRefused()
    {
        // Arrange: bob is disabled, so ann is the only usable administrator
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var (ann, _, apollo) = await ArrangeTwoAdminsAsync(db);
        var bobUser = (await db.Users.GetByUsernameAsync("bob", cancellationToken))!;
        bobUser.Enabled = false;
        await db.Users.UpdateAsync(bobUser, null, cancellationToken);

        // Act
        var refused = await Assert.ThrowsAsync<DomainRuleException>(
            () => db.Store.RemoveMembershipAsync(
                apollo, ann, Draft("project.member-removed", apollo), true, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.LastProjectAdmin, refused.Rule);
        Assert.Equal(2, (await db.Store.ListMembershipsAsync(apollo, cancellationToken)).Count);
    }

    /// <summary>
    ///     Test that an account is stored together with its membership and one audit event, and that the flag
    ///     round-trips.
    /// </summary>
    [Fact]
    public async Task UserStore_AddToProjectAsync_StoresAccountMembershipAndOneEventTogether()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var apollo = await db.Store.AddProjectAsync(
            new Project { Name = "Apollo", Created = _created }, cancellationToken);

        // Act
        var user = await db.Users.AddToProjectAsync(
            NewUser("newbie", mustChangePassword: true),
            apollo.Id,
            ProjectRole.ProjectAdmin,
            u => AuditDraft.By(TestActors.Ann, "project.account-created", "created " + u.Username, apollo.Id),
            cancellationToken);

        // Assert
        var stored = (await db.Users.GetByUsernameAsync("newbie", cancellationToken))!;
        Assert.Equal(user.Id, stored.Id);
        Assert.True(stored.MustChangePassword);
        Assert.Equal(
            new ProjectMembership(user.Id, apollo.Id, ProjectRole.ProjectAdmin),
            await db.Store.GetMembershipAsync(user.Id, apollo.Id, cancellationToken));
        var events = (await db.Audit.ListAsync(apollo.Id, null, null, null, 100, cancellationToken))
            .Where(e => e.Action == "project.account-created");
        Assert.Equal("created newbie", Assert.Single(events).Detail);
    }

    /// <summary>
    ///     Test that a name that is already taken, or a project that does not exist, leaves no account, no
    ///     membership and no event behind.
    /// </summary>
    [Fact]
    public async Task UserStore_AddToProjectAsync_Failure_StoresNothing()
    {
        // Arrange: ann exists
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var apollo = await db.Store.AddProjectAsync(
            new Project { Name = "Apollo", Created = _created }, cancellationToken);
        await db.Users.AddAsync(NewUser("ann"), cancellationToken);
        var before = (await db.Audit.ReadAfterAsync(0, 1000, cancellationToken)).Count;

        // Act
        var taken = await Assert.ThrowsAsync<DomainRuleException>(
            () => db.Users.AddToProjectAsync(
                NewUser("ann"),
                apollo.Id,
                ProjectRole.User,
                _ => Draft("project.account-created", apollo.Id),
                cancellationToken));
        await Assert.ThrowsAnyAsync<Exception>(
            () => db.Users.AddToProjectAsync(
                NewUser("ghost"),
                9999,
                ProjectRole.User,
                _ => Draft("project.account-created", 9999),
                cancellationToken));

        // Assert: nothing was stored, and the gate is free for the next change
        Assert.Equal(DomainRule.UserNameTaken, taken.Rule);
        Assert.Null(await db.Users.GetByUsernameAsync("ghost", cancellationToken));
        Assert.Empty(await db.Store.ListMembershipsAsync(apollo.Id, cancellationToken));
        Assert.Equal(before, (await db.Audit.ReadAfterAsync(0, 1000, cancellationToken)).Count);
        await db.Users.AddAsync(NewUser("later"), cancellationToken);
    }

    /// <summary>
    ///     Test that the project listing returns the events of that project only, never those of another project or
    ///     without a project, and that the hash chain over everything still verifies.
    /// </summary>
    [Fact]
    public async Task AuditStore_ListAsync_ProjectOnly_IsIsolatedAndTheChainStillVerifies()
    {
        // Arrange: events of project 1, of project 2 and without a project
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        await db.Audit.AppendAsync(AuditDraft.By(TestActors.Ann, "test.event", "a1", 1), cancellationToken);
        await db.Audit.AppendAsync(AuditDraft.By(TestActors.Ann, "test.event", "a2", 1, "review-1"), cancellationToken);
        await db.Audit.AppendAsync(AuditDraft.By(TestActors.Ann, "test.event", "b1", 2), cancellationToken);
        await db.Audit.AppendAsync(AuditDraft.By(TestActors.Ann, "test.event", "global"), cancellationToken);

        // Act
        var first = await db.Audit.ListAsync(1, null, null, null, 100, cancellationToken);
        var second = await db.Audit.ListAsync(2, null, null, null, 100, cancellationToken);
        var none = await db.Audit.ListAsync(3, null, null, null, 100, cancellationToken);
        var everything = await db.Audit.ReadAfterAsync(0, 100, cancellationToken);

        // Assert
        Assert.Equal(["a2", "a1"], first.Select(e => e.Detail));
        Assert.Equal("b1", Assert.Single(second).Detail);
        Assert.Empty(none);
        Assert.True(AuditChain.Verify(everything).IsIntact);
        Assert.Equal(4, everything.Count);
    }

    /// <summary>
    ///     Runs a store call and returns the refusal, if it is one.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <returns>The rule violation, or <c>null</c> when the call succeeded.</returns>
    private static async Task<DomainRuleException?> TryAsync(Func<Task> call)
    {
        try
        {
            await call();
            return null;
        }
        catch (DomainRuleException ex)
        {
            return ex;
        }
    }
}
