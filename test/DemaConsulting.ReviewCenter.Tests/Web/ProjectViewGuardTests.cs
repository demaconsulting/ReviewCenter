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
}
