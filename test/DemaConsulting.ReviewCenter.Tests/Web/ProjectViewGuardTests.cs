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
///     Unit tests for <see cref="ProjectViewGuard"/>, the one re-check of project view access that the project pages
///     run at the start of every action on an open circuit.
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
}
