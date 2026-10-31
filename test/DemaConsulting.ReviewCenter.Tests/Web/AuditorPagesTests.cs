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
using Microsoft.Extensions.DependencyInjection;
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests of the pages for a member whose role is auditor (read-only), rendered in memory with bUnit: every page
///     that has controls that change something shows a read-only notice instead of them, the project page does not read
///     the repositories, the audit trail is readable, and the members card names the three roles explicitly.
/// </summary>
public class AuditorPagesTests : PageTestBase
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Counts the reads of the repository list of the project, to show that a page does not make them.
    /// </summary>
    private CountingRepositoryStore? _counting;

    /// <summary>
    ///     Creates the project Apollo with the project admin <c>pam</c>, the user <c>ann</c> (who has an unsigned slot
    ///     in the review), the user <c>bob</c> (who has no slot), the auditor <c>aud</c>, an outsider <c>out</c> and a
    ///     review with a thread, and replaces the repository service by one that counts the reads of the list.
    /// </summary>
    /// <returns>The identity of Apollo.</returns>
    private async Task<int> ArrangeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var pam = await Users.CreateAsync("pam", "Pam", UserRole.Reviewer, Password, cancellationToken);
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, cancellationToken);
        var bob = await Users.CreateAsync("bob", "Bob", UserRole.Reviewer, Password, cancellationToken);
        var aud = await Users.CreateAsync("aud", "Aud", UserRole.Reviewer, Password, cancellationToken);
        await Users.CreateAsync("out", "Out", UserRole.Reviewer, Password, cancellationToken);
        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        await Store.AddMembershipAsync(pam.Id, project.Id, ProjectRole.ProjectAdmin, cancellationToken);
        await Store.AddMembershipAsync(ann.Id, project.Id, ProjectRole.User, cancellationToken);
        await Store.AddMembershipAsync(bob.Id, project.Id, ProjectRole.User, cancellationToken);
        await Store.AddMembershipAsync(aud.Id, project.Id, ProjectRole.Auditor, cancellationToken);
        await Reviews.CreateDraftAsync(project.Id, "REV-1", "First review", [new("a.txt", TestContent.A)], cancellationToken);
        await Threads.StartThreadAsync(project.Id, "REV-1", new ThreadAnchor("a.txt"), TestActors.Ann, "first", cancellationToken);
        await Signing.AddSlotAsync(TestActors.Ann, project.Id, "REV-1", ann.Id, null, cancellationToken);

        _counting = new CountingRepositoryStore(RepositoryStore);
        Services.AddSingleton(new RepositoryService(_counting, new FakeCredentialProtector(), Access, Time));
        JSInterop.Mode = JSRuntimeMode.Loose;
        return project.Id;
    }

    /// <summary>
    ///     Test that the project page shows an auditor the reviews and the audit trail link and a read-only notice, but
    ///     no control that creates or plans anything, no members card, and that it does not read the repositories; and
    ///     that the page of a user who may work still does.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_Auditor_ShowsReadOnlyNoticeAndReadsNoRepositories()
    {
        // Arrange
        var projectId = await ArrangeAsync();

        // Act
        SignInAs("aud", "Aud", false);
        var asAuditor = Render<ProjectPage>(parameters => parameters.Add(page => page.ProjectId, projectId));
        var auditorReads = _counting!.ListCalls;
        SignInAs("ann", "Ann", false);
        var asUser = Render<ProjectPage>(parameters => parameters.Add(page => page.ProjectId, projectId));

        // Assert
        Assert.Equal(0, auditorReads);
        Assert.NotEmpty(asAuditor.FindAll("[data-testid='readonly-notice']"));
        Assert.NotEmpty(asAuditor.FindAll("[data-testid='review-link-REV-1']"));
        Assert.Equal($"/projects/{projectId}/audit", asAuditor.Find("[data-testid='project-audit-link']").GetAttribute("href"));
        foreach (var testId in new[]
                 {
                     "new-review-button", "plan-reviews-link", "plan-reviews-unavailable", "members-list",
                     "add-member-button", "checklist-link", "evidence-settings-link"
                 })
        {
            Assert.Empty(asAuditor.FindAll($"[data-testid='{testId}']"));
        }

        Assert.Empty(asUser.FindAll("[data-testid='readonly-notice']"));
        Assert.NotEmpty(asUser.FindAll("[data-testid='new-review-button']"));
        Assert.True(_counting.ListCalls > auditorReads);
    }

    /// <summary>
    ///     Test that the review page shows an auditor the review with a read-only notice and none of the controls that
    ///     change something (slots, signing, comments, replies, resolving), and shows them to a user who may work.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_Auditor_ShowsReadOnlyNoticeAndNoWriteControls()
    {
        // Arrange
        var projectId = await ArrangeAsync();

        // Act
        SignInAs("aud", "Aud", false);
        var asAuditor = Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, "REV-1"));
        SignInAs("ann", "Ann", false);
        var asUser = Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, "REV-1"));

        // Assert: the review is there, the notice is there, the write controls are not
        Assert.NotEmpty(asAuditor.FindAll("[data-testid='review-fingerprint']"));
        Assert.NotEmpty(asAuditor.FindAll("[data-testid='readonly-notice']"));
        foreach (var selector in new[]
                 {
                     "[data-testid='reopen-form']", "[data-testid='slot-add-form']", "[data-testid='new-thread-form']",
                     "[data-testid^='sign-form-']", "[data-testid^='slot-save-']", "[data-testid^='slot-remove-']",
                     "[data-testid^='thread-toggle-']", "[data-testid^='reply-button-']",
                     "[data-testid='due-date-form']", "[data-testid='new-slot-primary-select']"
                 })
        {
            Assert.Empty(asAuditor.FindAll(selector));
        }

        Assert.Empty(asUser.FindAll("[data-testid='readonly-notice']"));
        Assert.NotEmpty(asUser.FindAll("[data-testid='slot-add-form']"));
    }

    /// <summary>
    ///     Test that the checklist page shows an auditor the checklist with a read-only notice and every control that
    ///     answers or changes it disabled, and leaves them enabled for a user who may work.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Render_Auditor_ShowsReadOnlyNoticeAndDisablesEveryControl()
    {
        // Arrange
        var projectId = await ArrangeAsync();
        const string firstItem = "req-01";

        // Act
        SignInAs("aud", "Aud", false);
        var asAuditor = Render<ChecklistPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, "REV-1"));
        SignInAs("ann", "Ann", false);
        var asUser = Render<ChecklistPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, "REV-1"));

        // Assert
        Assert.NotEmpty(asAuditor.FindAll("[data-testid='readonly-notice']"));
        Assert.True(asAuditor.Find($"[data-testid='check-pass-{firstItem}']").HasAttribute("disabled"));
        Assert.True(asAuditor.Find($"[data-testid='check-fail-{firstItem}']").HasAttribute("disabled"));
        Assert.Empty(asUser.FindAll("[data-testid='readonly-notice']"));
        Assert.False(asUser.Find($"[data-testid='check-pass-{firstItem}']").HasAttribute("disabled"));
    }

    /// <summary>
    ///     Test that the page that creates a review and the page that plans reviews show an auditor a read-only notice
    ///     instead of the form, and read neither the repositories nor anything else that is for people who work.
    /// </summary>
    [Fact]
    public async Task NewReviewPageAndPlanPage_Render_Auditor_ShowReadOnlyNoticeInsteadOfTheForm()
    {
        // Arrange
        var projectId = await ArrangeAsync();

        // Act
        SignInAs("aud", "Aud", false);
        var newReview = Render<NewReviewPage>(parameters => parameters.Add(page => page.ProjectId, projectId));
        var plan = Render<PlanPage>(parameters => parameters.Add(page => page.ProjectId, projectId));

        // Assert
        Assert.Equal(0, _counting!.ListCalls);
        Assert.NotEmpty(newReview.FindAll("[data-testid='readonly-notice']"));
        Assert.Empty(newReview.FindAll("[data-testid='new-review-form']"));
        Assert.Empty(newReview.FindAll("[data-testid='create-review-button']"));
        Assert.NotEmpty(plan.FindAll("[data-testid='readonly-notice']"));
        Assert.Empty(plan.FindAll("[data-testid='plan-form']"));
        Assert.Empty(plan.FindAll("[data-testid='plan-no-repository']"));
    }

    /// <summary>
    ///     Test that an auditor sees the audit trail page of the project, and that the page of another project, which
    ///     the auditor is no member of, is not found.
    /// </summary>
    [Fact]
    public async Task ProjectAuditPage_Render_Auditor_ShowsTheTrailOfOwnProjectOnly()
    {
        // Arrange
        var projectId = await ArrangeAsync();
        var other = await Projects.CreateAsync("Gemini", null, TestContext.Current.CancellationToken);
        await Audit.RecordAsync(
            AuditDraft.By(TestActors.Ann, "test.event", "apollo event", projectId), TestContext.Current.CancellationToken);

        // Act
        SignInAs("aud", "Aud", false);
        var own = Render<ProjectAuditPage>(parameters => parameters.Add(page => page.ProjectId, projectId));
        var foreign = Render<ProjectAuditPage>(parameters => parameters.Add(page => page.ProjectId, other.Id));

        // Assert
        Assert.Contains("apollo event", own.FindAll("[data-testid='audit-detail']").Select(e => e.TextContent));
        Assert.Empty(own.FindAll("[data-testid='project-not-found']"));
        Assert.NotEmpty(foreign.FindAll("[data-testid='project-not-found']"));
    }

    /// <summary>
    ///     Test that the members card names each role (member, auditor, project admin), that an auditor is only
    ///     changed by an explicit choice of the new role and never by a toggle, and that the changes are stored.
    /// </summary>
    [Fact]
    public async Task ProjectPage_MemberRoles_ThreeRoles_AreNamedAndChangedExplicitly()
    {
        // Arrange: the project administrator has the page open
        var projectId = await ArrangeAsync();
        SignInAs("pam", "Pam", false);
        var cut = Render<ProjectPage>(parameters => parameters.Add(page => page.ProjectId, projectId));

        // Assert: the labels and the controls of each role
        Assert.Equal("Project admin", cut.Find("[data-testid='member-role-pam']").TextContent);
        Assert.Equal("Member", cut.Find("[data-testid='member-role-bob']").TextContent);
        Assert.Equal("Auditor", cut.Find("[data-testid='member-role-aud']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='member-toggle-role-aud']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='member-set-role-aud-user']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='member-set-role-aud-projectadmin']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='member-set-role-bob-auditor']"));

        // Act: a member without work becomes an auditor, who becomes a project admin, then a member again
        await cut.Find("[data-testid='member-set-role-bob-auditor']").ClickAsync();
        var madeAuditor = cut.Find("[data-testid='member-role-bob']").TextContent;
        await cut.Find("[data-testid='member-set-role-bob-projectadmin']").ClickAsync();
        var madeAdmin = cut.Find("[data-testid='member-role-bob']").TextContent;
        await cut.Find("[data-testid='member-toggle-role-bob']").ClickAsync();
        var madeMember = cut.Find("[data-testid='member-role-bob']").TextContent;
        await cut.Find("[data-testid='member-set-role-aud-user']").ClickAsync();

        // Assert
        Assert.Equal(["Auditor", "Project admin", "Member", "Member"], [madeAuditor, madeAdmin, madeMember, cut.Find("[data-testid='member-role-aud']").TextContent]);
        var aud = (await UserStore.GetByUsernameAsync("aud", TestContext.Current.CancellationToken))!;
        Assert.Equal(
            ProjectRole.User,
            (await Store.GetMembershipAsync(aud.Id, projectId, TestContext.Current.CancellationToken))!.Role);
    }

    /// <summary>
    ///     Test that a project admin who makes themselves an auditor (the member list can no longer be read) keeps no
    ///     right to work, keeps the audit trail, and that one who makes themselves a plain member keeps working.
    /// </summary>
    /// <param name="role">The role the admin gives themselves.</param>
    /// <param name="canWork">Whether the admin may still create reviews afterwards.</param>
    [Theory]
    [InlineData("auditor", false)]
    [InlineData("user", true)]
    public async Task ProjectPage_MemberRoles_AdminChangesOwnRole_RightsAreRecomputed(string role, bool canWork)
    {
        // Arrange: a second project admin exists, so the last-admin rule allows the change
        var projectId = await ArrangeAsync();
        SignInAs("pam", "Pam", false);
        var cut = Render<ProjectPage>(parameters => parameters.Add(page => page.ProjectId, projectId));
        await cut.Find("[data-testid='member-toggle-role-bob']").ClickAsync();

        // Act
        var selector = role == "user" ? "member-toggle-role-pam" : $"member-set-role-pam-{role}";
        await cut.Find($"[data-testid='{selector}']").ClickAsync();

        // Assert: the administration is gone for both; the other rights follow the new role
        Assert.Empty(cut.FindAll("[data-testid='members-list']"));
        Assert.Equal(canWork, cut.FindAll("[data-testid='new-review-button']").Count > 0);
        Assert.Equal(!canWork, cut.FindAll("[data-testid='readonly-notice']").Count > 0);
        if (!canWork)
        {
            Assert.NotEmpty(cut.FindAll("[data-testid='project-audit-link']"));
        }
    }

    /// <summary>
    ///     Test that a member who still has an unsigned slot on a review that is not approved is refused the auditor
    ///     role with the reason on the page, that the role stays, and that the last project administrator is refused
    ///     with the usual explanation.
    /// </summary>
    [Fact]
    public async Task ProjectPage_MemberRoles_OpenWorkAndLastAdmin_AreRefusedWithAMessage()
    {
        // Arrange
        var projectId = await ArrangeAsync();
        SignInAs("pam", "Pam", false);
        var cut = Render<ProjectPage>(parameters => parameters.Add(page => page.ProjectId, projectId));

        // Act: ann has an unsigned slot in REV-1
        await cut.Find("[data-testid='member-set-role-ann-auditor']").ClickAsync();
        var openWork = cut.Find("[data-testid='member-error']").TextContent;
        var role = cut.Find("[data-testid='member-role-ann']").TextContent;
        await cut.Find("[data-testid='member-set-role-pam-auditor']").ClickAsync();
        var lastAdmin = cut.Find("[data-testid='member-error']").TextContent;

        // Assert
        Assert.Contains("'REV-1' (First review)", openWork, StringComparison.Ordinal);
        Assert.Equal("Member", role);
        Assert.Contains(ProjectService.LastAdminMessage, lastAdmin, StringComparison.Ordinal);
        Assert.Equal("Project admin", cut.Find("[data-testid='member-role-pam']").TextContent);
    }

    /// <summary>
    ///     Test that the add-member form and the create-account form offer the auditor role, add the user with it, and
    ///     refuse to combine it with the project admin role.
    /// </summary>
    [Fact]
    public async Task NewMemberPages_Auditor_AreAddedWithTheRoleAndNotCombinedWithAdmin()
    {
        // Arrange
        var projectId = await ArrangeAsync();
        SignInAs("pam", "Pam", false);
        var add = Render<NewMemberPage>(parameters => parameters.Add(page => page.ProjectId, projectId));
        var create = Render<NewMemberAccountPage>(parameters => parameters.Add(page => page.ProjectId, projectId));

        // Act: both together are refused; then the auditor alone is accepted by both forms
        await add.Find("[data-testid='new-member-auditor-checkbox']").ChangeAsync(true);
        await add.Find("[data-testid='new-member-admin-checkbox']").ChangeAsync(true);
        await add.Find("[data-testid='new-member-username-input']").InputAsync("out");
        await add.Find("[data-testid='new-member-submit']").ClickAsync();
        var both = add.Find("[data-testid='new-member-error']").TextContent;
        await add.Find("[data-testid='new-member-admin-checkbox']").ChangeAsync(false);
        await add.Find("[data-testid='new-member-submit']").ClickAsync();
        await create.Find("[data-testid='new-account-auditor-checkbox']").ChangeAsync(true);
        await create.Find("[data-testid='new-account-username-input']").InputAsync("fresh");
        await create.Find("[data-testid='new-account-display-name-input']").InputAsync("Fresh One");
        await create.Find("[data-testid='new-account-submit']").ClickAsync();

        // Assert
        Assert.Contains("either", both, StringComparison.Ordinal);
        foreach (var name in new[] { "out", "fresh" })
        {
            var user = (await UserStore.GetByUsernameAsync(name, TestContext.Current.CancellationToken))!;
            Assert.Equal(
                ProjectRole.Auditor,
                (await Store.GetMembershipAsync(user.Id, projectId, TestContext.Current.CancellationToken))!.Role);
        }
    }

    /// <summary>
    ///     Wraps the repository store of the tests and counts the reads of the repository list.
    /// </summary>
    /// <param name="inner">The store to wrap.</param>
    private sealed class CountingRepositoryStore(IRepositoryStore inner) : IRepositoryStore
    {
        /// <summary>
        ///     Gets the number of reads of the repository list.
        /// </summary>
        public int ListCalls { get; private set; }

        /// <inheritdoc />
        public Task<SourceRepository> AddAsync(
            NewRepository repository,
            Func<SourceRepository, AuditDraft> audit,
            CancellationToken cancellationToken = default)
        {
            return inner.AddAsync(repository, audit, cancellationToken);
        }

        /// <inheritdoc />
        public Task<SourceRepository?> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            return inner.GetAsync(id, cancellationToken);
        }

        /// <inheritdoc />
        public Task<string?> GetEncryptedTokenAsync(int id, CancellationToken cancellationToken = default)
        {
            return inner.GetEncryptedTokenAsync(id, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<SourceRepository>> ListAsync(int projectId, CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return inner.ListAsync(projectId, cancellationToken);
        }

        /// <inheritdoc />
        public Task<SourceRepository> UpdateAsync(
            int id,
            RepositoryUpdate update,
            AuditDraft audit,
            CancellationToken cancellationToken = default)
        {
            return inner.UpdateAsync(id, update, audit, cancellationToken);
        }
    }
}
