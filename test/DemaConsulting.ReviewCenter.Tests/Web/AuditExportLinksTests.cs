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
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the export links of the audit pages (<c>AuditFilterForm</c>), rendered in memory with bUnit: they
///     appear on both pages, carry the current filter and no paging values, and encode hostile values.
/// </summary>
public class AuditExportLinksTests : PageTestBase
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Creates the system administrator <c>root</c>, signs in as that user and creates the project Apollo.
    /// </summary>
    /// <returns>The identity of the project.</returns>
    private async Task<int> ArrangeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Users.CreateAsync("root", "Root", UserRole.Admin, Password, cancellationToken);
        SignInAs("root", "Root", UserRole.Admin);
        return (await Projects.CreateAsync("Apollo", null, cancellationToken)).Id;
    }

    /// <summary>
    ///     Renders the whole-trail page at an address.
    /// </summary>
    /// <param name="query">The query string with its leading question mark, or empty.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<AuditPage> RenderGlobal(string query = "")
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/audit" + query);
        return Render<AuditPage>();
    }

    /// <summary>
    ///     Renders the project audit page at an address.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="query">The query string with its leading question mark, or empty.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ProjectAuditPage> RenderProject(int projectId, string query = "")
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/projects/{projectId}/audit{query}");
        return Render<ProjectAuditPage>(parameters => parameters.Add(p => p.ProjectId, projectId));
    }

    /// <summary>
    ///     Test that the whole-trail page links to the export, with the filter and without paging values.
    /// </summary>
    [Fact]
    public async Task AuditExportLinks_GlobalPage_CarryTheFilterWithoutPagingValues()
    {
        // Arrange
        var projectId = await ArrangeAsync();

        // Act
        var cut = RenderGlobal($"?project={projectId}&actor=ann&action=review.&review=R1&from=2026-01-02&to=2026-03-04&size=100&before=5");

        // Assert
        var csv = cut.Find("[data-testid='audit-export-csv']").GetAttribute("href");
        var json = cut.Find("[data-testid='audit-export-json']").GetAttribute("href");
        Assert.Equal($"/audit/export?format=csv&project={projectId}&actor=ann&action=review.&review=R1&from=2026-01-02&to=2026-03-04", csv);
        Assert.Equal($"/audit/export?format=json&project={projectId}&actor=ann&action=review.&review=R1&from=2026-01-02&to=2026-03-04", json);
        Assert.DoesNotContain("size", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("before", csv, StringComparison.Ordinal);
        Assert.Contains("not only the page shown", cut.Find("[data-testid='audit-export-note']").TextContent, StringComparison.Ordinal);
        Assert.Contains("chain", cut.Find("[data-testid='audit-export-note']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='audit-chain-status']"));
    }

    /// <summary>
    ///     Test that the project page links to the export of its own project, with the filter, and does not carry a
    ///     project from the query.
    /// </summary>
    [Fact]
    public async Task AuditExportLinks_ProjectPage_PointToTheProjectExportWithTheFilter()
    {
        // Arrange
        var projectId = await ArrangeAsync();

        // Act
        var cut = RenderProject(projectId, "?project=99&actor=ann&from=2026-01-02&size=25&after=3");

        // Assert
        Assert.Equal(
            $"/projects/{projectId}/audit/export?format=csv&actor=ann&from=2026-01-02",
            cut.Find("[data-testid='audit-export-csv']").GetAttribute("href"));
        Assert.Equal(
            $"/projects/{projectId}/audit/export?format=json&actor=ann&from=2026-01-02",
            cut.Find("[data-testid='audit-export-json']").GetAttribute("href"));
        Assert.NotNull(cut.Find("[data-testid='audit-export-note']"));
    }

    /// <summary>
    ///     Test that the links exist without any filter, as plain export links.
    /// </summary>
    [Fact]
    public async Task AuditExportLinks_NoFilter_AreBareExportLinks()
    {
        // Arrange
        var projectId = await ArrangeAsync();

        // Act
        var global = RenderGlobal();
        var project = RenderProject(projectId);

        // Assert
        Assert.Equal("/audit/export?format=csv", global.Find("[data-testid='audit-export-csv']").GetAttribute("href"));
        Assert.Equal($"/projects/{projectId}/audit/export?format=json", project.Find("[data-testid='audit-export-json']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that hostile filter values are encoded in the links: they can neither add parameters nor break out of
    ///     the attribute.
    /// </summary>
    [Fact]
    public async Task AuditExportLinks_HostileValues_AreEncoded()
    {
        // Arrange
        var projectId = await ArrangeAsync();
        var hostile = Uri.EscapeDataString("\"><script>alert(1)</script>&format=json&x=y");

        // Act
        var cut = RenderProject(projectId, $"?actor={hostile}&review={hostile}");

        // Assert
        var href = cut.Find("[data-testid='audit-export-csv']").GetAttribute("href") ?? string.Empty;
        Assert.StartsWith($"/projects/{projectId}/audit/export?format=csv&actor=%22%3E%3", href, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", href, StringComparison.Ordinal);
        Assert.DoesNotContain("&format=json", href, StringComparison.Ordinal);
        Assert.DoesNotContain("\"><script>", cut.Markup, StringComparison.Ordinal);
    }
}
