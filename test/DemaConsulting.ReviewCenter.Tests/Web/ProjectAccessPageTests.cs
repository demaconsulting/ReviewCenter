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

using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Web;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Tests, rendered in memory with bUnit, of what the project pages show to a system administrator, to a member
///     and to a user who is no member of the project: the project list, the members section, project creation and
///     the repositories offered for a new review.
/// </summary>
public class ProjectAccessPageTests : PageTestBase
{
    /// <summary>
    ///     The password of the users the tests create.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Creates two projects, Apollo with the member <c>mem</c> and Gemini without members, and the outsider
    ///     <c>out</c>; the system administrator <c>ann</c> is the user who is signed in.
    /// </summary>
    /// <returns>The identities of the member, the outsider, Apollo and Gemini.</returns>
    private async Task<(int Member, int Outsider, int Apollo, int Gemini)> ArrangeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Users.CreateAsync("ann", "Ann", UserRole.Admin, Password, cancellationToken);
        var member = await Users.CreateAsync("mem", "Mem", UserRole.Reviewer, Password, cancellationToken);
        var outsider = await Users.CreateAsync("out", "Out", UserRole.Reviewer, Password, cancellationToken);
        var apollo = await Projects.CreateAsync("Apollo", null, cancellationToken);
        var gemini = await Projects.CreateAsync("Gemini", null, cancellationToken);
        await Store.AddMembershipAsync(member.Id, apollo.Id, cancellationToken);
        return (member.Id, outsider.Id, apollo.Id, gemini.Id);
    }

    /// <summary>
    ///     Renders the project page for a project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ProjectPage> RenderProject(int projectId)
    {
        return Render<ProjectPage>(parameters => parameters.Add(page => page.ProjectId, projectId));
    }

    /// <summary>
    ///     Test that the project list shows a member only the projects they belong to, and a system administrator
    ///     every project and the button that creates one.
    /// </summary>
    [Fact]
    public async Task ProjectsPage_Render_MemberAndSystemAdmin_ListOnlyWhatEachMaySee()
    {
        // Arrange: Apollo has the member, Gemini has nobody
        var (_, _, apollo, gemini) = await ArrangeAsync();

        // Act: render the list as the member
        SignInAs("mem", "Mem", UserRole.Reviewer);
        var asMember = Render<ProjectsPage>();

        // Assert: only Apollo, and no button to create a project
        Assert.NotEmpty(asMember.FindAll($"[data-testid='project-row-{apollo}']"));
        Assert.Empty(asMember.FindAll($"[data-testid='project-row-{gemini}']"));
        Assert.Empty(asMember.FindAll("[data-testid='new-project-button']"));

        // Act: render the list as the system administrator
        SignInAs("ann", "Ann");
        var asAdmin = Render<ProjectsPage>();

        // Assert: both projects and the button
        Assert.NotEmpty(asAdmin.FindAll($"[data-testid='project-row-{apollo}']"));
        Assert.NotEmpty(asAdmin.FindAll($"[data-testid='project-row-{gemini}']"));
        Assert.NotEmpty(asAdmin.FindAll("[data-testid='new-project-button']"));
    }

    /// <summary>
    ///     Test that a user without a project is told why the list is empty and what to do, and is not offered the
    ///     button that creates a project.
    /// </summary>
    [Fact]
    public async Task ProjectsPage_Render_UserWithoutMembership_ExplainsTheEmptyListAndOffersNoCreateButton()
    {
        // Arrange: a project the user does not belong to
        await ArrangeAsync();
        SignInAs("out", "Out", UserRole.Reviewer);

        // Act
        var cut = Render<ProjectsPage>();

        // Assert: the empty message names the way out, and nothing is listed
        Assert.Contains("administrator", cut.Find("[data-testid='projects-empty']").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("[data-testid='projects-list']"));
        Assert.Empty(cut.FindAll("[data-testid='new-project-button']"));
    }

    /// <summary>
    ///     Test that a user who is not a system administrator cannot create a project, even through the page.
    /// </summary>
    [Fact]
    public async Task NewProjectPage_CreateForm_MemberWhoIsNoSystemAdmin_IsRefusedAndCreatesNothing()
    {
        // Arrange: a member who fills in the form
        await ArrangeAsync();
        SignInAs("mem", "Mem", UserRole.Reviewer);
        var cut = Render<NewProjectPage>();
        await cut.Find("[data-testid='project-name-input']").InputAsync("Hidden");

        // Act
        await cut.Find("[data-testid='new-project-form']").SubmitAsync();

        // Assert: the refusal names the missing right, and no third project exists
        Assert.Contains("system administrator", cut.Find("[data-testid='project-error']").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, (await Projects.ListAsync(TestContext.Current.CancellationToken)).Count);
    }

    /// <summary>
    ///     Test that a project the user is no member of looks like a project that does not exist, on the project
    ///     page and on the page for a new review.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_NonMember_LooksLikeAMissingProject()
    {
        // Arrange: Apollo has a review, and the outsider is signed in
        var (_, _, apollo, _) = await ArrangeAsync();
        await Reviews.CreateDraftAsync(apollo, "REV-1", "Secret", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        SignInAs("out", "Out", UserRole.Reviewer);

        // Act
        var project = RenderProject(apollo);
        var missing = RenderProject(999);
        var newReview = Render<NewReviewPage>(parameters => parameters.Add(page => page.ProjectId, apollo));

        // Assert: the message is the one for a missing project and nothing of the project is shown
        Assert.Equal(
            missing.Find("[data-testid='project-not-found']").TextContent.Replace("999", "#"),
            project.Find("[data-testid='project-not-found']").TextContent.Replace(apollo.ToString(), "#"));
        Assert.DoesNotContain("Apollo", project.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", project.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(newReview.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(newReview.FindAll("[data-testid='new-review-form']"));
    }

    /// <summary>
    ///     Test that a member sees the project and its reviews, but neither the members section nor the repository
    ///     management that belongs to administrators.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_Member_SeesReviewsButNoMembersSection()
    {
        // Arrange: Apollo has a review, and its member is signed in
        var (_, _, apollo, _) = await ArrangeAsync();
        await Reviews.CreateDraftAsync(apollo, "REV-1", "First", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        SignInAs("mem", "Mem", UserRole.Reviewer);

        // Act
        var cut = RenderProject(apollo);

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='review-link-REV-1']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='new-review-button']"));
        Assert.Empty(cut.FindAll("[data-testid='members-list']"));
        Assert.Empty(cut.FindAll("[data-testid='add-member-button']"));
        Assert.Empty(cut.FindAll("[data-testid='repositories-link']"));
    }

    /// <summary>
    ///     Test that a system administrator sees the members of a project with their roles and the actions on
    ///     each, and that a project without members says what that means.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_SystemAdmin_ListsMembersWithRolesAndActions()
    {
        // Arrange: Apollo has a member and a project administrator, Gemini has none
        var (_, outsider, apollo, gemini) = await ArrangeAsync();
        await Store.AddMembershipAsync(outsider, apollo, ProjectRole.ProjectAdmin, TestContext.Current.CancellationToken);

        // Act
        var cut = RenderProject(apollo);
        var empty = RenderProject(gemini);

        // Assert: each member has a role badge and the two actions; the other project explains the empty list
        Assert.Equal("Member", cut.Find("[data-testid='member-role-mem']").TextContent);
        Assert.Equal("Project admin", cut.Find("[data-testid='member-role-out']").TextContent);
        Assert.Equal("Make project admin", cut.Find("[data-testid='member-toggle-role-mem']").TextContent);
        Assert.Equal("Make member", cut.Find("[data-testid='member-toggle-role-out']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='member-remove-mem']"));
        Assert.Contains("no members", empty.Find("[data-testid='members-empty']").TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll("[data-testid='repositories-link']"));
    }

    /// <summary>
    ///     Test that + Add member reveals the form, lists only users who are not members yet, and adds the chosen
    ///     user with the chosen role.
    /// </summary>
    [Fact]
    public async Task ProjectPage_AddMember_ChosenUser_BecomesMemberWithTheRole()
    {
        // Arrange: Apollo has the member mem; the outsider is a candidate
        var (_, outsider, apollo, _) = await ArrangeAsync();
        var cut = RenderProject(apollo);
        Assert.Empty(cut.FindAll("[data-testid='add-member-form']"));

        // Act: open the form
        await cut.Find("[data-testid='add-member-button']").ClickAsync();

        // Assert: ann (the system administrator) and out are offered, mem is not
        var options = cut.FindAll("[data-testid='add-member-user-select'] option").Select(o => o.GetAttribute("value")).ToList();
        Assert.Contains("out", options);
        Assert.DoesNotContain("mem", options);

        // Act: choose out as a project administrator and add
        await cut.Find("[data-testid='add-member-user-select']").ChangeAsync("out");
        await cut.Find("[data-testid='add-member-admin-checkbox']").ChangeAsync(true);
        await cut.Find("[data-testid='add-member-submit']").ClickAsync();

        // Assert: the membership is stored with the role, listed, and the form is closed again
        var stored = await Store.GetMembershipAsync(outsider, apollo, TestContext.Current.CancellationToken);
        Assert.Equal(ProjectRole.ProjectAdmin, stored!.Role);
        Assert.Equal("Project admin", cut.Find("[data-testid='member-role-out']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='add-member-form']"));
    }

    /// <summary>
    ///     Test that the role button switches a member between member and project administrator, and Remove takes
    ///     the membership away.
    /// </summary>
    [Fact]
    public async Task ProjectPage_MemberActions_ToggleRoleAndRemove_ChangeTheMembership()
    {
        // Arrange: Apollo has the member mem
        var (member, _, apollo, _) = await ArrangeAsync();
        var cut = RenderProject(apollo);

        // Act: make the member a project administrator
        await cut.Find("[data-testid='member-toggle-role-mem']").ClickAsync();

        // Assert
        Assert.Equal(ProjectRole.ProjectAdmin, (await Store.GetMembershipAsync(member, apollo, TestContext.Current.CancellationToken))!.Role);
        Assert.Equal("Make member", cut.Find("[data-testid='member-toggle-role-mem']").TextContent);

        // Act: remove the member
        await cut.Find("[data-testid='member-remove-mem']").ClickAsync();

        // Assert: the membership and the row are gone, and the page says that the project has no members
        Assert.Null(await Store.GetMembershipAsync(member, apollo, TestContext.Current.CancellationToken));
        Assert.Empty(cut.FindAll("[data-testid='member-row-mem']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='members-empty']"));
    }

    /// <summary>
    ///     Test that the form for a new review offers only the repositories of its own project.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_RepositorySelect_OffersOnlyRepositoriesOfThisProject()
    {
        // Arrange: a repository in each project
        var (_, _, apollo, gemini) = await ArrangeAsync();
        var admin = new Actor("ann", "Ann");
        await Repositories.CreateAsync(apollo, "apollo-repo", "https://example.com/a.git", null, null, admin, TestContext.Current.CancellationToken);
        await Repositories.CreateAsync(gemini, "gemini-repo", "https://example.com/g.git", null, null, admin, TestContext.Current.CancellationToken);

        // Act: choose the repository source on the form of Apollo
        var cut = Render<NewReviewPage>(parameters => parameters.Add(page => page.ProjectId, apollo));
        await cut.Find("[data-testid='source-repository-radio']").ChangeAsync(true);

        // Assert: only the repository of Apollo is offered
        var options = cut.FindAll("[data-testid='repository-select'] option").Select(option => option.TextContent).ToList();
        Assert.Equal(["Choose a repository", "apollo-repo"], options);
        Assert.DoesNotContain("gemini-repo", cut.Markup, StringComparison.Ordinal);
    }
}
