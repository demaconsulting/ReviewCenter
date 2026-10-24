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

using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Subsystem tests for the Web subsystem: the shell, router, and home page working together.
/// </summary>
public partial class WebSubsystemTests
{
    /// <summary>
    ///     Test that requesting the home route renders the title and all interactive controls.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_HomeRoute_Requested_RendersTitleAndControls()
    {
        // Arrange: start the real pipeline in memory
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request the home route
        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: 200, the document title, and every control the UI tests drive
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>ReviewCenter</title>", html);
        Assert.Contains("data-testid=\"hello-button\"", html);
        Assert.Contains("data-testid=\"counter-button\"", html);
        Assert.Contains("data-testid=\"counter-value\"", html);
        Assert.DoesNotContain("data-testid=\"hello-message\"", html);
    }

    /// <summary>
    ///     Test that the first server-rendered response is not yet marked interactive.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_HomeRoute_InitialResponse_IsNotYetInteractive()
    {
        // Arrange: start the real pipeline in memory
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request the home route (no circuit exists for a plain HTTP request)
        var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);

        // Assert: drivers must wait for the marker to flip before clicking
        Assert.Contains("data-interactive=\"false\"", html);
    }

    /// <summary>
    ///     Test the complete read path through the real pipeline: a project and a review created through the
    ///     domain services (as the forms do) are shown by the projects, project and review pages, with the
    ///     review's fingerprint, file list, sizes and per-file hashes.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_CreatedProjectAndReview_ArePresentedByThePages()
    {
        // Arrange: start the real pipeline and create a project with a review of three files
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var browser = await TestBrowser.SignedInAsync(app, "ann");
        var client = browser.Client;
        var project = await app.Services.GetRequiredService<ProjectService>()
            .CreateAsync("Apollo", "Moon landing", TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id,
            "rev-1",
            "First review",
            [new("a.txt", TestContent.A), new("b.txt", TestContent.B), new("c.bin", TestContent.C)],
            TestContext.Current.CancellationToken);

        // Act: request the three pages
        var projects = await ParseAsync(client, "/projects");
        var projectPage = await ParseAsync(client, $"/projects/{project.Id}");
        var reviewPage = await ParseAsync(client, $"/projects/{project.Id}/reviews/rev-1");

        // Assert: the project is listed and linked, the review is listed and linked, and the review page
        // shows the known fingerprint and the per-file values
        Assert.Equal("Apollo", projects.QuerySelector($"[data-testid='project-link-{project.Id}']")?.TextContent);
        Assert.Equal($"/projects/{project.Id}", projects.QuerySelector($"[data-testid='project-link-{project.Id}']")?.GetAttribute("href"));
        Assert.Equal("Apollo", projectPage.QuerySelector("[data-testid='project-name']")?.TextContent);
        Assert.Equal($"/projects/{project.Id}/reviews/rev-1", projectPage.QuerySelector("[data-testid='review-link-rev-1']")?.GetAttribute("href"));
        Assert.Equal("First review", reviewPage.QuerySelector("[data-testid='review-title']")?.TextContent);
        Assert.Equal("Draft", reviewPage.QuerySelector("[data-testid='review-state']")?.TextContent);
        Assert.Equal(TestContent.FingerprintABC, reviewPage.QuerySelector("[data-testid='review-fingerprint']")?.TextContent);
        Assert.Equal("a.txt", reviewPage.QuerySelector("[data-testid='file-path-0']")?.TextContent);
        Assert.Equal(TestContent.HashA, reviewPage.QuerySelector("[data-testid='file-hash-0']")?.TextContent);
        Assert.Equal(TestContent.HashC, reviewPage.QuerySelector("[data-testid='file-hash-2']")?.TextContent);
    }

    /// <summary>
    ///     Test that a thread created through the domain service (as the page does) is presented by the review
    ///     page through the real pipeline: the dependency injection, the database tables and the page agree.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_ReviewWithThreads_ArePresentedByThePage()
    {
        // Arrange: start the real pipeline, create a review and an open and a resolved thread on its file
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var browser = await TestBrowser.SignedInAsync(app, "ann");
        var client = browser.Client;
        var project = await app.Services.GetRequiredService<ProjectService>()
            .CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id, "rev-1", "First review", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        var threads = app.Services.GetRequiredService<ThreadService>();
        await threads.StartThreadAsync(project.Id, "rev-1", "a.txt", null, null, TestActors.Ann, "Open", TestContext.Current.CancellationToken);
        var resolved = await threads.StartThreadAsync(project.Id, "rev-1", "a.txt", 1, 1, TestActors.Bob, "Done", TestContext.Current.CancellationToken);
        await threads.ResolveAsync(project.Id, "rev-1", resolved.Id, TestContext.Current.CancellationToken);

        // Act: request the review page
        var page = await ParseAsync(client, $"/projects/{project.Id}/reviews/rev-1");

        // Assert: the summary and the file's open-thread count come from the stored threads
        Assert.Equal("1", page.QuerySelector("[data-testid='threads-open-count']")?.TextContent);
        Assert.Equal("1", page.QuerySelector("[data-testid='threads-resolved-count']")?.TextContent);
        Assert.Equal("1 open thread", page.QuerySelector("[data-testid='file-open-threads-0']")?.TextContent);

        // Assert: the next step, the index row of the open thread (activity read back from the database) and
        // the collapsed details
        Assert.Equal(
            "58 checklist checks unanswered, 1 open thread to resolve.",
            page.QuerySelector("[data-testid='next-step']")?.TextContent);
        Assert.Single(page.QuerySelectorAll("[data-testid='thread-index-list'] > li"));
        Assert.False(string.IsNullOrWhiteSpace(page.QuerySelector("[data-testid^='index-activity-']")?.TextContent));
        Assert.False(page.QuerySelector("[data-testid='review-details']")?.HasAttribute("open"));
    }

    /// <summary>
    ///     Test that checks answered through the domain service are presented by the checklist page and the
    ///     review page through the real pipeline, and are still there when the pages are requested again.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_ChecklistPage_AnsweredChecksSurviveReload()
    {
        // Arrange: start the real pipeline, create a review and answer two of its checks
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var browser = await TestBrowser.SignedInAsync(app, "ann");
        var client = browser.Client;
        var project = await app.Services.GetRequiredService<ProjectService>()
            .CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id, "rev-1", "First review", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        var checklists = app.Services.GetRequiredService<ChecklistService>();
        await checklists.SetAnswerAsync(
            project.Id, "rev-1", "REQ-01", ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        await checklists.SetAnswerAsync(
            project.Id, "rev-1", "REQ-02", ChecklistOutcome.Fail, "Broken", TestActors.Ann, TestContext.Current.CancellationToken);

        // Act: request the checklist page twice, and the review page
        var route = $"/projects/{project.Id}/reviews/rev-1/checklist";
        var first = await ParseAsync(client, route);
        var reloaded = await ParseAsync(client, route);
        var reviewPage = await ParseAsync(client, $"/projects/{project.Id}/reviews/rev-1");

        // Assert: both loads show the answers; the Fail keeps its justification
        foreach (var page in new[] { first, reloaded })
        {
            Assert.Equal("56 checks unanswered.", page.QuerySelector("[data-testid='checklist-status']")?.TextContent);
            Assert.Equal("2 of 8", page.QuerySelector("[data-testid='section-req-progress']")?.TextContent);
            Assert.Equal("true", page.QuerySelector("[data-testid='check-pass-req-01']")?.GetAttribute("aria-pressed"));
            Assert.Equal("Broken", page.QuerySelector("[data-testid='check-justification-req-02']")?.TextContent);
        }

        // Assert: the review page links the checklist and names the unanswered checks as the next step
        Assert.Equal("2 of 58 checks answered", reviewPage.QuerySelector("[data-testid='checklist-progress-summary']")?.TextContent);
        Assert.Equal(route, reviewPage.QuerySelector("[data-testid='checklist-link']")?.GetAttribute("href"));
        Assert.Equal("56 checklist checks unanswered.", reviewPage.QuerySelector("[data-testid='next-step']")?.TextContent);
    }

    /// <summary>
    ///     Test that every interactive element (a, button, input, select, textarea) on every page has a stable
    ///     kebab-case <c>data-testid</c>, so UI tests never need to depend on text or layout.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_AllPages_EveryInteractiveElementHasTestId()
    {
        // Arrange: start the real pipeline with data, so list rows and forms are all rendered
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var browser = await TestBrowser.SignedInAsync(app, "root", UserRole.Admin);
        var client = browser.Client;
        var project = await app.Services.GetRequiredService<ProjectService>()
            .CreateAsync("Apollo", "Moon landing", TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id, "rev-1", "First review", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        string[] routes =
        [
            "/",
            "/login",
            "/account",
            "/users",
            "/projects",
            $"/projects/{project.Id}",
            "/projects/9999",
            $"/projects/{project.Id}/reviews/rev-1",
            $"/projects/{project.Id}/reviews/rev-1/checklist",
            $"/projects/{project.Id}/reviews/missing"
        ];

        foreach (var route in routes)
        {
            // Act: render the page and collect its interactive elements (a hidden field is not interactive)
            var document = await ParseAsync(client, route);
            var elements = document.QuerySelectorAll("a, button, input:not([type='hidden']), select, textarea");

            // Assert: the page has interactive elements and each has a kebab-case test id
            Assert.NotEmpty(elements);
            foreach (var element in elements)
            {
                var testId = element.GetAttribute("data-testid");
                Assert.True(
                    testId is not null && KebabCase().IsMatch(testId),
                    $"A <{element.LocalName}> on '{route}' has no kebab-case data-testid (found '{testId}').");
            }
        }
    }

    /// <summary>
    ///     Requests a page and parses the returned document.
    /// </summary>
    /// <param name="client">The in-memory client.</param>
    /// <param name="route">The route to request.</param>
    /// <returns>The parsed document.</returns>
    private static async Task<AngleSharp.Html.Dom.IHtmlDocument> ParseAsync(HttpClient client, string route)
    {
        var response = await client.GetAsync(route, TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return await new HtmlParser().ParseDocumentAsync(html, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Matches lowercase words, digits and dots joined by single hyphens.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"^[a-z0-9]+(?:[-.][a-z0-9]+)*$")]
    private static partial Regex KebabCase();
}
