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
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="ProjectAuditPage"/> component, rendered in memory with bUnit: who sees the audit
///     trail of a project, that it holds only the events of that project, paging, and that a revoked right drops the
///     entries.
/// </summary>
public class ProjectAuditPageTests : PageTestBase
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Creates the projects Apollo and Gemini, the project admin <c>padm</c> and the plain member <c>mem</c> of
    ///     Apollo, the outsider <c>out</c> and the system administrator <c>root</c>, and records one event for each
    ///     project and one without a project.
    /// </summary>
    /// <returns>The identities of Apollo and Gemini.</returns>
    private async Task<(int Apollo, int Gemini)> ArrangeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Users.CreateAsync("root", "Root", UserRole.Admin, Password, cancellationToken);
        var padm = await Users.CreateAsync("padm", "Padm", UserRole.Reviewer, Password, cancellationToken);
        var mem = await Users.CreateAsync("mem", "Mem", UserRole.Reviewer, Password, cancellationToken);
        await Users.CreateAsync("out", "Out", UserRole.Reviewer, Password, cancellationToken);
        var apollo = await Projects.CreateAsync("Apollo", null, cancellationToken);
        var gemini = await Projects.CreateAsync("Gemini", null, cancellationToken);
        await Store.AddMembershipAsync(padm.Id, apollo.Id, ProjectRole.ProjectAdmin, cancellationToken);
        await Store.AddMembershipAsync(mem.Id, apollo.Id, ProjectRole.User, cancellationToken);
        await Audit.RecordAsync(
            AuditDraft.By(TestActors.Ann, "test.event", "apollo event", apollo.Id), cancellationToken);
        await Audit.RecordAsync(
            AuditDraft.By(TestActors.Ann, "test.event", "gemini event", gemini.Id), cancellationToken);
        await Audit.RecordAsync(AuditDraft.By(TestActors.Ann, "test.event", "global event"), cancellationToken);
        return (apollo.Id, gemini.Id);
    }

    /// <summary>
    ///     Renders the page at its address, so that the query values reach it as in the browser.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="query">The query string with its leading question mark, or empty.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ProjectAuditPage> RenderAt(int projectId, string query = "")
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/projects/{projectId}/audit{query}");
        return Render<ProjectAuditPage>(parameters => parameters.Add(p => p.ProjectId, projectId));
    }

    /// <summary>
    ///     Test that a project admin sees the events of the project, and only those: not the events of another
    ///     project and not the events without a project.
    /// </summary>
    [Fact]
    public async Task ProjectAuditPage_Render_ProjectAdmin_ShowsOnlyThisProjectsEvents()
    {
        // Arrange
        var (apollo, _) = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);

        // Act
        var cut = RenderAt(apollo);

        // Assert
        var details = cut.FindAll("[data-testid='audit-detail']").Select(e => e.TextContent).ToList();
        Assert.Contains("apollo event", details);
        Assert.DoesNotContain("gemini event", details);
        Assert.DoesNotContain("global event", details);
        Assert.DoesNotContain("Gemini", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Equal("Apollo", cut.Find("[data-testid='breadcrumb-project-link']").TextContent);
    }

    /// <summary>
    ///     Test that a system administrator sees the trail of any project.
    /// </summary>
    [Fact]
    public async Task ProjectAuditPage_Render_SystemAdmin_ShowsTheProjectsEvents()
    {
        // Arrange: root is no member of Gemini
        var (_, gemini) = await ArrangeAsync();
        SignInAs("root", "Root", UserRole.Admin);

        // Act
        var cut = RenderAt(gemini);

        // Assert
        Assert.Contains("gemini event", cut.FindAll("[data-testid='audit-detail']").Select(e => e.TextContent));
        Assert.DoesNotContain("apollo event", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a plain member, a user who is no member and a missing project all give the same not-found state
    ///     without any entry.
    /// </summary>
    /// <param name="username">The signed-in user.</param>
    /// <param name="missing">Whether to ask for a project that does not exist.</param>
    [Theory]
    [InlineData("mem", false)]
    [InlineData("out", false)]
    [InlineData("padm", true)]
    public async Task ProjectAuditPage_Render_NoAdministerRight_ShowsNotFoundAndNoEntries(string username, bool missing)
    {
        // Arrange
        var (apollo, _) = await ArrangeAsync();
        SignInAs(username, username, isSystemAdmin: false);

        // Act
        var cut = RenderAt(missing ? 9999 : apollo);

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='audit-table']"));
        Assert.DoesNotContain("apollo event", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Apollo", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an empty trail says so, and that a long trail is paged by 50 with buttons that carry the position
    ///     in the address.
    /// </summary>
    [Fact]
    public async Task ProjectAuditPage_Render_ManyEvents_PagesByFiftyNewestFirst()
    {
        // Arrange: 60 events of the project besides the one that created it
        var (apollo, _) = await ArrangeAsync();
        for (var i = 1; i <= 60; i++)
        {
            await Audit.RecordAsync(
                AuditDraft.By(TestActors.Ann, "test.event", $"bulk {i}", apollo),
                TestContext.Current.CancellationToken);
        }

        SignInAs("padm", "Padm", isSystemAdmin: false);

        // Act
        var first = RenderAt(apollo);
        var older = first.Find("[data-testid='audit-older']").GetAttribute("href")!;
        var firstRows = first.FindAll("[data-testid='audit-row']").Count;
        var firstTop = first.FindAll("[data-testid='audit-detail']")[0].TextContent;
        var firstHasNewer = first.FindAll("[data-testid='audit-newer']").Count > 0;
        var second = RenderAt(apollo, older[older.IndexOf('?', StringComparison.Ordinal)..]);

        // Assert
        Assert.Equal(50, firstRows);
        Assert.Equal("bulk 60", firstTop);
        Assert.StartsWith($"/projects/{apollo}/audit?before=", older, StringComparison.Ordinal);
        Assert.False(firstHasNewer);
        Assert.NotEmpty(second.FindAll("[data-testid='audit-newer']"));
        Assert.True(second.FindAll("[data-testid='audit-row']").Count is > 0 and <= 50);
        Assert.DoesNotContain("bulk 60", second.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a project without events of its own beyond its creation still renders the table, and that a
    ///     project with none says so.
    /// </summary>
    [Fact]
    public async Task ProjectAuditPage_Render_ProjectWithoutEvents_SaysNothingWasRecorded()
    {
        // Arrange: a project that was stored without any audit event
        var cancellationToken = TestContext.Current.CancellationToken;
        await Users.CreateAsync("root", "Root", UserRole.Admin, Password, cancellationToken);
        var project = await Store.AddProjectAsync(
            new Project { Name = "Quiet", Created = DateTimeOffset.UtcNow }, cancellationToken);
        SignInAs("root", "Root", UserRole.Admin);

        // Act
        var cut = RenderAt(project.Id);

        // Assert
        Assert.Contains(
            "Nothing has been recorded",
            cut.Find("[data-testid='audit-empty']").TextContent,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a project admin whose membership is revoked while the page is open gets the not-found state, with
    ///     no entry left, when the page loads again.
    /// </summary>
    [Fact]
    public async Task ProjectAuditPage_Load_MembershipRevokedWhileOpen_DropsTheEntries()
    {
        // Arrange: the page is open on the trail
        var (apollo, _) = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderAt(apollo);
        Assert.NotEmpty(cut.FindAll("[data-testid='audit-row']"));
        await RevokeMembershipAsync(apollo, "padm");

        // Act: the page loads again for another position
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(
            ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ProjectAuditPage.Before)] = 1000L })));

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='audit-table']"));
        Assert.Empty(cut.FindAll("[data-testid='audit-row']"));
        Assert.DoesNotContain("apollo event", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Apollo", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a project admin who is demoted to a plain member while the page is open loses the entries, and
    ///     that a session that ended without the page knowing does too.
    /// </summary>
    /// <param name="sessionEnded">Whether the session ends instead of the role changing.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectAuditPage_Load_DemotedOrSessionEndedWhileOpen_DropsTheEntries(bool sessionEnded)
    {
        // Arrange: the page is open on the trail
        var (apollo, _) = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderAt(apollo);
        Assert.NotEmpty(cut.FindAll("[data-testid='audit-row']"));
        if (sessionEnded)
        {
            SignOutQuietly();
        }
        else
        {
            await ChangeMemberRoleAsync(apollo, "padm", ProjectRole.User);
        }

        // Act
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(ProjectAuditPage.After)] = 0L })));

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='audit-table']"));
        Assert.DoesNotContain("apollo event", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the interactive marker is set once the component has rendered on a live circuit.
    /// </summary>
    [Fact]
    public async Task ProjectAuditPage_AfterRender_Interactive_SetsMarker()
    {
        // Arrange
        var (apollo, _) = await ArrangeAsync();
        SignInAs("root", "Root", UserRole.Admin);

        // Act
        var cut = RenderAt(apollo);

        // Assert
        Assert.Equal("true", cut.Find("[data-testid='project-audit-page']").GetAttribute("data-interactive"));
    }
}
