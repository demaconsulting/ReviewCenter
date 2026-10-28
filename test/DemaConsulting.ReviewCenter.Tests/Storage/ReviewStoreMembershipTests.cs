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
///     Tests for the project memberships of <see cref="DemaConsulting.ReviewCenter.Storage.ReviewStore"/> against a
///     real SQLite database file.
/// </summary>
public class ReviewStoreMembershipTests
{
    /// <summary>
    ///     The creation time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _created = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Adds two users and two projects to a database.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <returns>The identities of the users and the projects.</returns>
    private static async Task<(int Ann, int Bob, int Apollo, int Gemini)> ArrangeAsync(TestStore db)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ann = await db.Users.AddAsync(NewUser("ann"), cancellationToken);
        var bob = await db.Users.AddAsync(NewUser("bob"), cancellationToken);
        var apollo = await db.Store.AddProjectAsync(new Project { Name = "Apollo", Created = _created }, cancellationToken);
        var gemini = await db.Store.AddProjectAsync(new Project { Name = "Gemini", Created = _created }, cancellationToken);
        return (ann.Id, bob.Id, apollo.Id, gemini.Id);
    }

    /// <summary>
    ///     Builds a user for the tests.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <returns>The user.</returns>
    private static User NewUser(string username)
    {
        return new User
        {
            Username = username,
            DisplayName = username,
            Enabled = true,
            PasswordHash = "hash",
            Created = _created
        };
    }

    /// <summary>
    ///     Test that a membership round-trips and is listed for the project and for the user, ordered.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddMembershipAsync_Membership_RoundTripsAndIsListed()
    {
        // Arrange: users and projects
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var (ann, bob, apollo, gemini) = await ArrangeAsync(db);

        // Act: make ann a project administrator of Apollo and a user of Gemini, and bob a user of Apollo
        await db.Store.AddMembershipAsync(ann, apollo, ProjectRole.ProjectAdmin, cancellationToken);
        await db.Store.AddMembershipAsync(ann, gemini, cancellationToken);
        await db.Store.AddMembershipAsync(bob, apollo, cancellationToken);

        // Assert
        Assert.Equal(
            new ProjectMembership(ann, apollo, ProjectRole.ProjectAdmin),
            await db.Store.GetMembershipAsync(ann, apollo, cancellationToken));
        Assert.Equal(
            [new ProjectMembership(ann, apollo, ProjectRole.ProjectAdmin), new ProjectMembership(bob, apollo, ProjectRole.User)],
            await db.Store.ListMembershipsAsync(apollo, cancellationToken));
        Assert.Equal(
            [new ProjectMembership(ann, apollo, ProjectRole.ProjectAdmin), new ProjectMembership(ann, gemini, ProjectRole.User)],
            await db.Store.ListMembershipsForUserAsync(ann, cancellationToken));
        Assert.Null(await db.Store.GetMembershipAsync(bob, gemini, cancellationToken));
    }

    /// <summary>
    ///     Test that a user can be a member of a project only once.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddMembershipAsync_SecondMembership_IsRejected()
    {
        // Arrange: a member
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var (ann, _, apollo, _) = await ArrangeAsync(db);
        await db.Store.AddMembershipAsync(ann, apollo, cancellationToken);

        // Act: add the membership again, with another role
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => db.Store.AddMembershipAsync(ann, apollo, ProjectRole.ProjectAdmin, cancellationToken));

        // Assert: refused, and the role is unchanged
        Assert.Equal(DomainRule.MembershipExists, ex.Rule);
        Assert.Equal(ProjectRole.User, (await db.Store.GetMembershipAsync(ann, apollo, cancellationToken))!.Role);
    }

    /// <summary>
    ///     Test that a membership can be given another role and removed, and that both report whether a membership
    ///     was there.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetAndRemoveMembershipAsync_ChangeRoleAndRemove_ReportWhetherFound()
    {
        // Arrange: a member
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var (ann, bob, apollo, _) = await ArrangeAsync(db);
        await db.Store.AddMembershipAsync(ann, apollo, cancellationToken);

        // Act
        var changed = await db.Store.SetMembershipRoleAsync(
            apollo, ann, ProjectRole.ProjectAdmin, AuditTestExtensions.Draft(), false, cancellationToken);
        var changedOther = await db.Store.SetMembershipRoleAsync(
            apollo, bob, ProjectRole.ProjectAdmin, AuditTestExtensions.Draft(), false, cancellationToken);
        var roleAfter = (await db.Store.GetMembershipAsync(ann, apollo, cancellationToken))!.Role;
        var removed = await db.Store.RemoveMembershipAsync(
            apollo, ann, AuditTestExtensions.Draft(), false, cancellationToken);
        var removedAgain = await db.Store.RemoveMembershipAsync(
            apollo, ann, AuditTestExtensions.Draft(), false, cancellationToken);

        // Assert
        Assert.True(changed);
        Assert.False(changedOther);
        Assert.Equal(ProjectRole.ProjectAdmin, roleAfter);
        Assert.True(removed);
        Assert.False(removedAgain);
        Assert.Null(await db.Store.GetMembershipAsync(ann, apollo, cancellationToken));
    }

    /// <summary>
    ///     Test that every membership change is audited with the project it belongs to, and that a change that
    ///     finds nothing writes no audit event.
    /// </summary>
    [Fact]
    public async Task ReviewStore_Membership_Changes_AreAuditedWithTheProject()
    {
        // Arrange: users and projects
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var (ann, bob, apollo, _) = await ArrangeAsync(db);

        // Act: add, change, remove, and remove again
        await db.Store.AddMembershipAsync(
            new ProjectMembership(ann, apollo, ProjectRole.User),
            AuditDraft.By(TestActors.Ann, "project.member-added", "x", apollo),
            cancellationToken);
        await db.Store.SetMembershipRoleAsync(
            apollo,
            ann,
            ProjectRole.ProjectAdmin,
            AuditDraft.By(TestActors.Ann, "project.member-role-changed", "x", apollo),
            false,
            cancellationToken);
        await db.Store.RemoveMembershipAsync(
            apollo,
            ann,
            AuditDraft.By(TestActors.Ann, "project.member-removed", "x", apollo),
            false,
            cancellationToken);
        await db.Store.RemoveMembershipAsync(
            apollo,
            bob,
            AuditDraft.By(TestActors.Ann, "project.member-removed", "x", apollo),
            false,
            cancellationToken);

        // Assert: three events, each for the project
        var events = (await db.Audit.ListAsync(null, null, null, null, 100, cancellationToken))
            .Where(e => e.Action.StartsWith("project.member-", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal(apollo, e.ProjectId));
    }

    /// <summary>
    ///     Test that a membership of a project or user that does not exist cannot be stored.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddMembershipAsync_UnknownProject_IsRefusedByTheDatabase()
    {
        // Arrange: a user and no project
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var (ann, _, _, _) = await ArrangeAsync(db);

        // Act and assert
        await Assert.ThrowsAnyAsync<Exception>(() => db.Store.AddMembershipAsync(ann, 9999, cancellationToken));
    }
}
