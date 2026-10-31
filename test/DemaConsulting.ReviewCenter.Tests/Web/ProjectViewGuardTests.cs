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
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for <see cref="ProjectViewGuard"/>, the one re-check of access (view, contribute, administer, audit
///     and system administrator) that the pages run at the start of every action on an open circuit.
/// </summary>
public class ProjectViewGuardTests : PageTestBase
{
    /// <summary>
    ///     Creates the project <c>Apollo</c> with <c>mem</c> as a member, and the user <c>out</c> who is no member.
    /// </summary>
    /// <returns>The project identity.</returns>
    private async Task<int> ArrangeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        var member = await Users.CreateAsync("mem", "Mem", UserRole.Reviewer, "correct horse battery", cancellationToken);
        await Users.CreateAsync("out", "Out", UserRole.Reviewer, "correct horse battery", cancellationToken);
        await Store.AddMembershipAsync(member.Id, project.Id, cancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Runs the guard for the signed-in user.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="lost">Counts how many times the access-lost callback ran.</param>
    /// <returns><c>true</c> when the user may still see the project.</returns>
    private async Task<bool> GuardAsync(int projectId, Action lost)
    {
        return await Access.GuardViewAsync(
            Services.GetRequiredService<AuthenticationStateProvider>(), projectId, lost);
    }

    /// <summary>
    ///     Test that a member may continue and that the access-lost callback does not run.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardView_Member_ReturnsTrueWithoutCallback()
    {
        // Arrange: a member is signed in
        var projectId = await ArrangeAsync();
        SignInAs("mem", "Mem", UserRole.Reviewer);
        var lost = 0;

        // Act
        var allowed = await GuardAsync(projectId, () => lost++);

        // Assert
        Assert.True(allowed);
        Assert.Equal(0, lost);
    }

    /// <summary>
    ///     Test that a user who is no member is refused and that the access-lost callback runs once.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardView_NonMember_ReturnsFalseAndCallsBackOnce()
    {
        // Arrange: a user who is no member is signed in
        var projectId = await ArrangeAsync();
        SignInAs("out", "Out", UserRole.Reviewer);
        var lost = 0;

        // Act
        var allowed = await GuardAsync(projectId, () => lost++);

        // Assert
        Assert.False(allowed);
        Assert.Equal(1, lost);
    }

    /// <summary>
    ///     Test that a member whose membership is removed after a first successful check is refused by the next check.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardView_MembershipRemovedBetweenChecks_SecondCheckReturnsFalse()
    {
        // Arrange: a member passes the first check
        var projectId = await ArrangeAsync();
        SignInAs("mem", "Mem", UserRole.Reviewer);
        var lost = 0;
        Assert.True(await GuardAsync(projectId, () => lost++));

        // Act: the membership is removed and the guard runs again
        await RevokeMembershipAsync(projectId, "mem");
        var allowed = await GuardAsync(projectId, () => lost++);

        // Assert
        Assert.False(allowed);
        Assert.Equal(1, lost);
    }

    /// <summary>
    ///     Test that nobody being signed in is refused and that the access-lost callback runs once.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardView_NobodySignedIn_ReturnsFalseAndCallsBackOnce()
    {
        // Arrange: a project and nobody signed in
        var projectId = await ArrangeAsync();
        SignOut();
        var lost = 0;

        // Act
        var allowed = await GuardAsync(projectId, () => lost++);

        // Assert
        Assert.False(allowed);
        Assert.Equal(1, lost);
    }

    /// <summary>
    ///     Creates the project <c>Apollo</c> with <c>pam</c> as a project admin and <c>mem</c> as a plain member.
    /// </summary>
    /// <returns>The project identity.</returns>
    private async Task<int> ArrangeAdminAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        var pam = await Users.CreateAsync("pam", "Pam", UserRole.Reviewer, "correct horse battery", cancellationToken);
        var member = await Users.CreateAsync("mem", "Mem", UserRole.Reviewer, "correct horse battery", cancellationToken);
        await Store.AddMembershipAsync(pam.Id, project.Id, ProjectRole.ProjectAdmin, cancellationToken);
        await Store.AddMembershipAsync(member.Id, project.Id, cancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Runs the administer guard for the signed-in user.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="lost">Counts how many times the access-lost callback ran.</param>
    /// <returns><c>true</c> when the user may still administer the project.</returns>
    private async Task<bool> GuardAdministerAsync(int projectId, Action lost)
    {
        return await Access.GuardAdministerAsync(
            Services.GetRequiredService<AuthenticationStateProvider>(), projectId, lost);
    }

    /// <summary>
    ///     Runs the system administrator guard for the signed-in user.
    /// </summary>
    /// <param name="lost">Counts how many times the access-lost callback ran.</param>
    /// <returns><c>true</c> when the user is still a system administrator.</returns>
    private async Task<bool> GuardSystemAdminAsync(Action lost)
    {
        return await Access.GuardSystemAdminAsync(
            Services.GetRequiredService<AuthenticationStateProvider>(), lost);
    }

    /// <summary>
    ///     Test that a project admin may continue and that the access-lost callback does not run.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardAdminister_ProjectAdmin_ReturnsTrueWithoutCallback()
    {
        // Arrange: a project admin is signed in
        var projectId = await ArrangeAdminAsync();
        SignInAs("pam", "Pam", UserRole.Reviewer);
        var lost = 0;

        // Act
        var allowed = await GuardAdministerAsync(projectId, () => lost++);

        // Assert
        Assert.True(allowed);
        Assert.Equal(0, lost);
    }

    /// <summary>
    ///     Test that a plain member is refused and that the access-lost callback runs once.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardAdminister_PlainMember_ReturnsFalseAndCallsBackOnce()
    {
        // Arrange: a plain member is signed in
        var projectId = await ArrangeAdminAsync();
        SignInAs("mem", "Mem", UserRole.Reviewer);
        var lost = 0;

        // Act
        var allowed = await GuardAdministerAsync(projectId, () => lost++);

        // Assert
        Assert.False(allowed);
        Assert.Equal(1, lost);
    }

    /// <summary>
    ///     Test that a project admin who is demoted after a first successful check is refused by the next check.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardAdminister_AdminDemotedBetweenChecks_SecondCheckReturnsFalse()
    {
        // Arrange: a project admin passes the first check
        var projectId = await ArrangeAdminAsync();
        SignInAs("pam", "Pam", UserRole.Reviewer);
        var lost = 0;
        Assert.True(await GuardAdministerAsync(projectId, () => lost++));

        // Act: the project admin becomes a plain member and the guard runs again
        await ChangeMemberRoleAsync(projectId, "pam", ProjectRole.User);
        var allowed = await GuardAdministerAsync(projectId, () => lost++);

        // Assert
        Assert.False(allowed);
        Assert.Equal(1, lost);
    }

    /// <summary>
    ///     Test that nobody being signed in is refused by the administer guard and that the callback runs once.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardAdminister_NobodySignedIn_ReturnsFalseAndCallsBackOnce()
    {
        // Arrange: a project and nobody signed in
        var projectId = await ArrangeAdminAsync();
        SignOut();
        var lost = 0;

        // Act
        var allowed = await GuardAdministerAsync(projectId, () => lost++);

        // Assert
        Assert.False(allowed);
        Assert.Equal(1, lost);
    }

    /// <summary>
    ///     Test that a system administrator may continue and that the access-lost callback does not run.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardSystemAdmin_SystemAdmin_ReturnsTrueWithoutCallback()
    {
        // Arrange: a system administrator is signed in
        await Users.CreateAsync("root", "Root", UserRole.Admin, "correct horse battery", TestContext.Current.CancellationToken);
        SignInAs("root", "Root", UserRole.Admin);
        var lost = 0;

        // Act
        var allowed = await GuardSystemAdminAsync(() => lost++);

        // Assert
        Assert.True(allowed);
        Assert.Equal(0, lost);
    }

    /// <summary>
    ///     Test that a system administrator who is demoted or disabled after the page was opened is refused and that
    ///     the access-lost callback runs once.
    /// </summary>
    /// <param name="disable"><c>true</c> to disable the user; <c>false</c> to take the administrator right away.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectViewGuard_GuardSystemAdmin_DemotedOrDisabled_ReturnsFalseAndCallsBackOnce(bool disable)
    {
        // Arrange: two system administrators, one signed in and passing the first check
        var cancellationToken = TestContext.Current.CancellationToken;
        await Users.CreateAsync("root", "Root", UserRole.Admin, "correct horse battery", cancellationToken);
        await Users.CreateAsync("boss", "Boss", UserRole.Admin, "correct horse battery", cancellationToken);
        SignInAs("root", "Root", UserRole.Admin);
        var lost = 0;
        Assert.True(await GuardSystemAdminAsync(() => lost++));

        // Act: the signed-in user is demoted (or disabled) and the guard runs again
        if (disable)
        {
            await Users.SetEnabledAsync("root", false, cancellationToken);
        }
        else
        {
            await Users.SetRoleAsync("root", UserRole.Reviewer, cancellationToken);
        }

        var allowed = await GuardSystemAdminAsync(() => lost++);

        // Assert
        Assert.False(allowed);
        Assert.Equal(1, lost);
    }

    /// <summary>
    ///     Test that nobody being signed in is refused by the system administrator guard and that the callback runs
    ///     once.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardSystemAdmin_NobodySignedIn_ReturnsFalseAndCallsBackOnce()
    {
        // Arrange: nobody signed in
        SignOut();
        var lost = 0;

        // Act
        var allowed = await GuardSystemAdminAsync(() => lost++);

        // Assert
        Assert.False(allowed);
        Assert.Equal(1, lost);
    }

    /// <summary>
    ///     Creates the project <c>Apollo</c> with <c>pam</c> as a project admin, <c>mem</c> as a plain member and
    ///     <c>aud</c> as a read-only auditor.
    /// </summary>
    /// <returns>The project identity.</returns>
    private async Task<int> ArrangeAuditorAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = await ArrangeAdminAsync();
        var auditor = await Users.CreateAsync("aud", "Aud", UserRole.Reviewer, "correct horse battery", cancellationToken);
        await Store.AddMembershipAsync(auditor.Id, projectId, ProjectRole.Auditor, cancellationToken);
        return projectId;
    }

    /// <summary>
    ///     Runs the contribute guard for the signed-in user.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="lost">Counts how many times the access-lost callback ran.</param>
    /// <returns><c>true</c> when the user may still work in the project.</returns>
    private async Task<bool> GuardContributeAsync(int projectId, Action lost)
    {
        return await Access.GuardContributeAsync(
            Services.GetRequiredService<AuthenticationStateProvider>(), projectId, lost);
    }

    /// <summary>
    ///     Runs the audit guard for the signed-in user.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="lost">Counts how many times the access-lost callback ran.</param>
    /// <returns><c>true</c> when the user may still read the audit trail of the project.</returns>
    private async Task<bool> GuardAuditAsync(int projectId, Action lost)
    {
        return await Access.GuardAuditAsync(
            Services.GetRequiredService<AuthenticationStateProvider>(), projectId, lost);
    }

    /// <summary>
    ///     Test that a member who may work in the project continues and that the callback does not run.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardContribute_Member_ReturnsTrueWithoutCallback()
    {
        // Arrange: a plain member is signed in
        var projectId = await ArrangeAuditorAsync();
        SignInAs("mem", "Mem", UserRole.Reviewer);
        var lost = 0;

        // Act
        var allowed = await GuardContributeAsync(projectId, () => lost++);

        // Assert
        Assert.True(allowed);
        Assert.Equal(0, lost);
    }

    /// <summary>
    ///     Test that a read-only member is refused without the callback, so the page can keep its data and say the
    ///     access is read-only.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardContribute_Auditor_ReturnsFalseWithoutCallback()
    {
        // Arrange: an auditor is signed in
        var projectId = await ArrangeAuditorAsync();
        SignInAs("aud", "Aud", UserRole.Reviewer);
        var lost = 0;

        // Act
        var allowed = await GuardContributeAsync(projectId, () => lost++);

        // Assert: refused, but the project is still visible, so nothing is dropped
        Assert.False(allowed);
        Assert.Equal(0, lost);
    }

    /// <summary>
    ///     Test that a user who cannot see the project is refused and that the callback runs once.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardContribute_NonMember_ReturnsFalseAndCallsBackOnce()
    {
        // Arrange: a user who is no member is signed in
        var projectId = await ArrangeAuditorAsync();
        await Users.CreateAsync("out", "Out", UserRole.Reviewer, "correct horse battery", TestContext.Current.CancellationToken);
        SignInAs("out", "Out", UserRole.Reviewer);
        var lost = 0;

        // Act
        var allowed = await GuardContributeAsync(projectId, () => lost++);

        // Assert
        Assert.False(allowed);
        Assert.Equal(1, lost);
    }

    /// <summary>
    ///     Test that nobody being signed in is refused by the contribute guard and that the callback runs once.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardContribute_NobodySignedIn_ReturnsFalseAndCallsBackOnce()
    {
        // Arrange: a project and nobody signed in
        var projectId = await ArrangeAuditorAsync();
        SignOut();
        var lost = 0;

        // Act
        var allowed = await GuardContributeAsync(projectId, () => lost++);

        // Assert
        Assert.False(allowed);
        Assert.Equal(1, lost);
    }

    /// <summary>
    ///     Test that a project admin and an auditor may read the audit trail and that the callback does not run.
    /// </summary>
    /// <param name="who">The user name of the signed-in user.</param>
    [Theory]
    [InlineData("pam")]
    [InlineData("aud")]
    public async Task ProjectViewGuard_GuardAudit_AuditorAndProjectAdmin_ReturnTrueWithoutCallback(string who)
    {
        // Arrange
        var projectId = await ArrangeAuditorAsync();
        SignInAs(who, who, UserRole.Reviewer);
        var lost = 0;

        // Act
        var allowed = await GuardAuditAsync(projectId, () => lost++);

        // Assert
        Assert.True(allowed);
        Assert.Equal(0, lost);
    }

    /// <summary>
    ///     Test that a plain member may not read the audit trail and that the callback runs once.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardAudit_PlainMember_ReturnsFalseAndCallsBackOnce()
    {
        // Arrange: a plain member is signed in
        var projectId = await ArrangeAuditorAsync();
        SignInAs("mem", "Mem", UserRole.Reviewer);
        var lost = 0;

        // Act
        var allowed = await GuardAuditAsync(projectId, () => lost++);

        // Assert
        Assert.False(allowed);
        Assert.Equal(1, lost);
    }

    /// <summary>
    ///     Test that nobody being signed in is refused by the audit guard and that the callback runs once.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardAudit_NobodySignedIn_ReturnsFalseAndCallsBackOnce()
    {
        // Arrange: a project and nobody signed in
        var projectId = await ArrangeAuditorAsync();
        SignOut();
        var lost = 0;

        // Act
        var allowed = await GuardAuditAsync(projectId, () => lost++);

        // Assert
        Assert.False(allowed);
        Assert.Equal(1, lost);
    }

    /// <summary>
    ///     Test that a member who is revoked between the two lookups of the contribute guard is reported as lost,
    ///     not as read-only: the project is hidden, so the page must drop its data.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardContribute_RevokedBetweenLookups_IsLostNotReadOnly()
    {
        // Arrange: an auditor is signed in and the membership is removed right after the first lookup
        var projectId = await ArrangeAuditorAsync();
        SignInAs("aud", "Aud", UserRole.Reviewer);
        var revoking = new RevokingAfterFirstLookup(Access, () => RevokeMembershipAsync(projectId, "aud"));
        var lost = 0;

        // Act
        var allowed = await revoking.GuardContributeAsync(
            Services.GetRequiredService<AuthenticationStateProvider>(), projectId, () => lost++);

        // Assert: the second lookup sees the removal, so the page is told that the project is gone
        Assert.False(allowed);
        Assert.Equal(1, lost);
    }

    /// <summary>
    ///     Test that a member who may work and is revoked after the first lookup still gets the answer of that single
    ///     lookup, without a callback, so the result is consistent with one decision.
    /// </summary>
    [Fact]
    public async Task ProjectViewGuard_GuardContribute_MemberRevokedAfterFirstLookup_FollowsTheFirstAnswer()
    {
        // Arrange: a plain member is signed in and the membership is removed right after the first lookup
        var projectId = await ArrangeAuditorAsync();
        SignInAs("mem", "Mem", UserRole.Reviewer);
        var revoking = new RevokingAfterFirstLookup(Access, () => RevokeMembershipAsync(projectId, "mem"));
        var lost = 0;

        // Act
        var allowed = await revoking.GuardContributeAsync(
            Services.GetRequiredService<AuthenticationStateProvider>(), projectId, () => lost++);

        // Assert: the removal is caught by the next guard, not reported as read-only
        Assert.True(allowed);
        Assert.Equal(0, lost);
    }

    /// <summary>
    ///     An access decorator that runs an action once, right after the first view or contribute lookup, to model
    ///     a revocation that lands between two lookups of one guard call.
    /// </summary>
    /// <param name="inner">The access rules to delegate to.</param>
    /// <param name="afterFirst">The action to run once after the first lookup.</param>
    private sealed class RevokingAfterFirstLookup(IProjectAccess inner, Func<Task> afterFirst) : IProjectAccess
    {
        /// <summary>
        ///     Whether the action has run.
        /// </summary>
        private bool _done;

        /// <inheritdoc />
        public Task<bool> IsSystemAdminAsync(Actor? actor, CancellationToken cancellationToken = default)
            => inner.IsSystemAdminAsync(actor, cancellationToken);

        /// <inheritdoc />
        public async Task<bool> CanViewAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
        {
            var answer = await inner.CanViewAsync(actor, projectId, cancellationToken);
            await RunOnceAsync();
            return answer;
        }

        /// <inheritdoc />
        public async Task<bool> CanContributeAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
        {
            var answer = await inner.CanContributeAsync(actor, projectId, cancellationToken);
            await RunOnceAsync();
            return answer;
        }

        /// <inheritdoc />
        public Task<bool> CanAdministerAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
            => inner.CanAdministerAsync(actor, projectId, cancellationToken);

        /// <inheritdoc />
        public Task<bool> CanAuditAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
            => inner.CanAuditAsync(actor, projectId, cancellationToken);

        /// <inheritdoc />
        public Task<bool> IsMemberAsync(int userId, int projectId, CancellationToken cancellationToken = default)
            => inner.IsMemberAsync(userId, projectId, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<Project>> ListVisibleProjectsAsync(Actor? actor, CancellationToken cancellationToken = default)
            => inner.ListVisibleProjectsAsync(actor, cancellationToken);

        /// <inheritdoc />
        public Task RequireViewAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
            => inner.RequireViewAsync(actor, projectId, cancellationToken);

        /// <inheritdoc />
        public Task RequireContributeAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
            => inner.RequireContributeAsync(actor, projectId, cancellationToken);

        /// <inheritdoc />
        public Task RequireAdministerAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
            => inner.RequireAdministerAsync(actor, projectId, cancellationToken);

        /// <inheritdoc />
        public Task RequireAuditAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
            => inner.RequireAuditAsync(actor, projectId, cancellationToken);

        /// <inheritdoc />
        public Task RequireSystemAdminAsync(Actor? actor, CancellationToken cancellationToken = default)
            => inner.RequireSystemAdminAsync(actor, cancellationToken);

        /// <summary>
        ///     Runs the action the first time only.
        /// </summary>
        private async Task RunOnceAsync()
        {
            if (_done)
            {
                return;
            }

            _done = true;
            await afterFirst();
        }
    }
}
