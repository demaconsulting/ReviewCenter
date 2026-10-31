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
    ///     Test that requesting the home route renders the title and the tagline.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_HomeRoute_Requested_RendersTitleAndTagline()
    {
        // Arrange: start the real pipeline in memory
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request the home route
        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: 200, the document title, and the tagline
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>ReviewCenter</title>", html);
        Assert.Contains("Web-based code review for regulated projects.", html);
    }

    /// <summary>
    ///     Test that the first server-rendered response of an interactive page is not yet marked interactive.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_ProjectsRoute_InitialResponse_IsNotYetInteractive()
    {
        // Arrange: start the real pipeline in memory and sign in
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var browser = await TestBrowser.SignedInAsync(app, "ann");

        // Act: request the projects route (no circuit exists for a plain HTTP request)
        var html = await browser.Client.GetStringAsync("/projects", TestContext.Current.CancellationToken);

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
            TestActors.Ann,
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
        Assert.Null(reviewPage.QuerySelector("[data-testid='review-state']"));
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
            project.Id, "rev-1", "First review", [new("a.txt", TestContent.A)], TestActors.Ann, TestContext.Current.CancellationToken);
        var threads = app.Services.GetRequiredService<ThreadService>();
        var bob = await app.Services.GetRequiredService<UserService>()
            .CreateAsync("bob", "Bob", UserRole.Reviewer, TestBrowser.Password, TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<IReviewStore>()
            .AddMembershipAsync(bob.Id, project.Id, TestContext.Current.CancellationToken);
        await threads.StartThreadAsync(project.Id, "rev-1", new ThreadAnchor("a.txt"), TestActors.Ann, "Open", TestContext.Current.CancellationToken);
        var resolved = await threads.StartThreadAsync(project.Id, "rev-1", new ThreadAnchor("a.txt", 1, 1), TestActors.Bob, "Done", TestContext.Current.CancellationToken);
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
    ///     Test that, through the real pipeline, the author of a review sees the notice and disabled answer controls
    ///     on the checklist page, while another user sees neither.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_ChecklistPage_AuthorSeesNoticeAndNonAuthorAnswers()
    {
        // Arrange: start the real pipeline and create a review that ann authored
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var annBrowser = await TestBrowser.SignedInAsync(app, "ann");
        using var bobBrowser = await TestBrowser.SignedInAsync(app, "bob");
        var project = await app.Services.GetRequiredService<ProjectService>()
            .CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id, "rev-1", "First review", [new("a.txt", TestContent.A)], TestActors.Ann, TestContext.Current.CancellationToken);
        var route = $"/projects/{project.Id}/reviews/rev-1/checklist";

        // Act: request the checklist page as the author and as another user
        var asAuthor = await ParseAsync(annBrowser.Client, route);
        var asOther = await ParseAsync(bobBrowser.Client, route);

        // Assert: the author gets the notice and disabled controls; the other user gets neither
        Assert.NotNull(asAuthor.QuerySelector("[data-testid='author-answer-blocked']"));
        Assert.True(asAuthor.QuerySelector("[data-testid='check-pass-req-01']")?.HasAttribute("disabled"));
        Assert.Null(asOther.QuerySelector("[data-testid='author-answer-blocked']"));
        Assert.False(asOther.QuerySelector("[data-testid='check-pass-req-01']")?.HasAttribute("disabled"));
    }

    /// <summary>
    ///     Test that checks answered through the domain service are presented by the checklist page and the
    ///     review page through the real pipeline, and are still there when the pages are requested again.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_ChecklistPage_AnsweredChecksSurviveReload()
    {
        // Arrange: start the real pipeline, create a review (by Bob, because its author may not answer it) and answer two checks
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var browser = await TestBrowser.SignedInAsync(app, "ann");
        using var bobBrowser = await TestBrowser.SignedInAsync(app, "bob");
        var client = browser.Client;
        var project = await app.Services.GetRequiredService<ProjectService>()
            .CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id, "rev-1", "First review", [new("a.txt", TestContent.A)], TestActors.Bob, TestContext.Current.CancellationToken);
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
        Assert.Equal("56 checklist checks unanswered, 1 check answered Fail.", reviewPage.QuerySelector("[data-testid='next-step']")?.TextContent);
    }

    /// <summary>
    ///     Test that slots and signatures created through the domain services are presented by the review page
    ///     and the project page through the real pipeline: Approved after signing, and In review with an
    ///     out-of-date signature once the record changes.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_SignedReview_ApprovalIsPresentedAndGoesOutOfDateWhenRecordChanges()
    {
        // Arrange: start the real pipeline with two users, a review with every check answered, and one slot
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var browser = await TestBrowser.SignedInAsync(app, "ann");
        var client = browser.Client;
        var users = app.Services.GetRequiredService<UserService>();
        var ann = (await users.GetAsync("ann", TestContext.Current.CancellationToken))!;
        var bob = await users.CreateAsync("bob", "Bob", UserRole.Reviewer, TestBrowser.Password, TestContext.Current.CancellationToken);
        var project = await TestWebApp.CreateProjectAsync(app, "Apollo");
        var store = app.Services.GetRequiredService<IReviewStore>();
        await store.AddMembershipAsync(ann.Id, project.Id, TestContext.Current.CancellationToken);
        await store.AddMembershipAsync(bob.Id, project.Id, TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id, "rev-1", "First review", [new("a.txt", TestContent.A)], TestActors.Bob, TestContext.Current.CancellationToken);
        var checklists = app.Services.GetRequiredService<ChecklistService>();
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await checklists.SetAnswerAsync(
                project.Id, "rev-1", item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        }

        var signing = app.Services.GetRequiredService<SigningService>();
        var slot = await signing.AddSlotAsync(
            TestActors.Ann, project.Id, "rev-1", ann.Id, null, TestContext.Current.CancellationToken);
        var route = $"/projects/{project.Id}/reviews/rev-1";

        // Ann is the only reviewer and reopens the review below; the rule that would refuse that is off here
        await app.Services.GetRequiredService<ProjectService>().SetReviewRuleAsync(
            TestActors.Ann, project.Id, ReviewRule.NonReopenerSigning, true, false, TestContext.Current.CancellationToken);

        // Act, Assert: before signing, the page offers the sign form and the review is In review
        var before = await ParseAsync(client, route);
        Assert.Equal("In review", before.QuerySelector("[data-testid='review-approval']")?.TextContent);
        Assert.NotNull(before.QuerySelector($"[data-testid='sign-form-{slot.Id}']"));

        // Act: ann signs with her password, for the digest read from the service
        var approval = (await signing.GetApprovalAsync(project.Id, "rev-1", TestContext.Current.CancellationToken))!;
        await signing.SignAsync(
            TestActors.Ann, project.Id, "rev-1", slot.Id, approval.Digest, TestBrowser.Password, TestContext.Current.CancellationToken);

        // Assert: both pages show the approval and the lock, and the review page the full digest in the details
        var signed = await ParseAsync(client, route);
        var list = await ParseAsync(client, $"/projects/{project.Id}");
        Assert.Equal("Approved", signed.QuerySelector("[data-testid='review-approval']")?.TextContent);
        Assert.NotNull(signed.QuerySelector("[data-testid='review-locked']"));
        Assert.StartsWith("Signed by Ann on ", signed.QuerySelector($"[data-testid='slot-status-{slot.Id}']")?.TextContent, StringComparison.Ordinal);
        Assert.Equal(approval.Digest, signed.QuerySelector("[data-testid='record-digest']")?.GetAttribute("title"));
        Assert.Equal("Approved", list.QuerySelector("[data-testid='review-approval-rev-1']")?.TextContent);

        // Act: an administrator reopens the review, which changes the record
        await signing.ReopenReviewAsync(TestActors.Ann, project.Id, "rev-1", "Late remark", TestContext.Current.CancellationToken);

        // Assert: the signature is out of date and the review is In review again
        var changed = await ParseAsync(client, route);
        var changedList = await ParseAsync(client, $"/projects/{project.Id}");
        Assert.Equal("Signature out of date", changed.QuerySelector($"[data-testid='slot-status-{slot.Id}']")?.TextContent);
        Assert.Equal("In review", changed.QuerySelector("[data-testid='review-approval']")?.TextContent);
        Assert.Equal("In review", changedList.QuerySelector("[data-testid='review-approval-rev-1']")?.TextContent);
    }

    /// <summary>
    ///     Test that an approved review is presented as locked through the real pipeline, with the reopen form for
    ///     an administrator and none for a plain member, and that after an administrator reopens it the page shows
    ///     the review as In review with the reopen history.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_ApprovedReview_ShowsLockedAndAdminCanReopen()
    {
        // Arrange: start the real pipeline with an administrator (ann), a member (cyd) and an approved review
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var admin = await TestBrowser.SignedInAsync(app, "ann");
        using var member = await TestBrowser.SignedInAsync(app, "cyd", UserRole.Reviewer);
        using var bobBrowser = await TestBrowser.SignedInAsync(app, "bob");
        var users = app.Services.GetRequiredService<UserService>();
        var ann = (await users.GetAsync("ann", TestContext.Current.CancellationToken))!;
        var cyd = (await users.GetAsync("cyd", TestContext.Current.CancellationToken))!;
        var project = await TestWebApp.CreateProjectAsync(app, "Apollo");
        var store = app.Services.GetRequiredService<IReviewStore>();
        await store.AddMembershipAsync(ann.Id, project.Id, TestContext.Current.CancellationToken);
        await store.AddMembershipAsync(cyd.Id, project.Id, TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id, "rev-1", "First review", [new("a.txt", TestContent.A)], TestActors.Bob, TestContext.Current.CancellationToken);
        var checklists = app.Services.GetRequiredService<ChecklistService>();
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await checklists.SetAnswerAsync(
                project.Id, "rev-1", item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        }

        var signing = app.Services.GetRequiredService<SigningService>();
        var slot = await signing.AddSlotAsync(
            TestActors.Ann, project.Id, "rev-1", cyd.Id, null, TestContext.Current.CancellationToken);
        var approval = (await signing.GetApprovalAsync(project.Id, "rev-1", TestContext.Current.CancellationToken))!;
        await signing.SignAsync(
            new Actor("cyd", "Cyd"), project.Id, "rev-1", slot.Id, approval.Digest, TestBrowser.Password, TestContext.Current.CancellationToken);
        var route = $"/projects/{project.Id}/reviews/rev-1";

        // Act: read the locked review as the administrator and as the member
        var asAdmin = await ParseAsync(admin.Client, route);
        var asMember = await ParseAsync(member.Client, route);

        // Assert: locked with the controls disabled; the reopen form only for the administrator
        Assert.NotNull(asAdmin.QuerySelector("[data-testid='review-locked']"));
        Assert.NotNull(asAdmin.QuerySelector("[data-testid='review-locked-banner']"));
        Assert.NotNull(asAdmin.QuerySelector("[data-testid='reopen-form']"));
        Assert.True(asAdmin.QuerySelector("[data-testid='new-slot-add-button']")?.HasAttribute("disabled"));
        Assert.NotNull(asMember.QuerySelector("[data-testid='review-locked']"));
        Assert.Null(asMember.QuerySelector("[data-testid='reopen-form']"));

        // Act: the administrator reopens the review with a reason
        await signing.ReopenReviewAsync(TestActors.Ann, project.Id, "rev-1", "A file was missing", TestContext.Current.CancellationToken);
        var reopened = await ParseAsync(admin.Client, route);

        // Assert: In review, no longer locked, and the history shows who, why and the earlier record
        Assert.Equal("In review", reopened.QuerySelector("[data-testid='review-approval']")?.TextContent);
        Assert.Null(reopened.QuerySelector("[data-testid='review-locked']"));
        Assert.Equal("A file was missing", reopened.QuerySelector("[data-testid='reopen-reason-0']")?.TextContent);
        Assert.Equal("Ann", reopened.QuerySelector("[data-testid='reopen-by-0']")?.TextContent);
    }

    /// <summary>
    ///     Test that the project page shows the independent-reviewers setting (on by default) to a project
    ///     administrator only, and shows the stored value after an administrator changes it.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_ProjectAdmin_TogglesIndependentReviewers()
    {
        // Arrange: start the real pipeline with an administrator (ann) and a plain member (cyd) of a project
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var admin = await TestBrowser.SignedInAsync(app, "ann");
        using var member = await TestBrowser.SignedInAsync(app, "cyd", UserRole.Reviewer);
        var users = app.Services.GetRequiredService<UserService>();
        var ann = (await users.GetAsync("ann", TestContext.Current.CancellationToken))!;
        var cyd = (await users.GetAsync("cyd", TestContext.Current.CancellationToken))!;
        var project = await TestWebApp.CreateProjectAsync(app, "Apollo");
        var store = app.Services.GetRequiredService<IReviewStore>();
        await store.AddMembershipAsync(ann.Id, project.Id, TestContext.Current.CancellationToken);
        await store.AddMembershipAsync(cyd.Id, project.Id, TestContext.Current.CancellationToken);
        var route = $"/projects/{project.Id}";

        // Act: read the page as the administrator and as the member
        var before = await ParseAsync(admin.Client, route);
        var asMember = await ParseAsync(member.Client, route);

        // Assert: required by default for the administrator; no setting for the member
        Assert.True(before.QuerySelector("[data-testid='independent-reviewers-switch']")?.HasAttribute("checked"));
        Assert.Null(asMember.QuerySelector("[data-testid='review-rules-card']"));

        // Act: the administrator switches the rule off through the service (the page button does the same call)
        var changed = await app.Services.GetRequiredService<ProjectService>().SetRequireIndependentReviewersAsync(
            TestActors.Ann, project.Id, true, false, TestContext.Current.CancellationToken);
        var after = await ParseAsync(admin.Client, route);

        // Assert: stored, and shown as off
        Assert.True(changed);
        Assert.False(after.QuerySelector("[data-testid='independent-reviewers-switch']")?.HasAttribute("checked"));
    }

    /// <summary>
    ///     Test that through the real pipeline an administrator sees the two stored-only review rule switches on, a
    ///     plain member sees no rules card, and a change made through the service shows as off.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_ProjectAdmin_SavesStoredReviewRuleSwitches()
    {
        // Arrange: the real pipeline with a project, an administrator and a plain member
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var admin = await TestBrowser.SignedInAsync(app, "ann");
        using var member = await TestBrowser.SignedInAsync(app, "cyd", UserRole.Reviewer);
        var users = app.Services.GetRequiredService<UserService>();
        var ann = (await users.GetAsync("ann", TestContext.Current.CancellationToken))!;
        var cyd = (await users.GetAsync("cyd", TestContext.Current.CancellationToken))!;
        var project = await TestWebApp.CreateProjectAsync(app, "Apollo");
        var store = app.Services.GetRequiredService<IReviewStore>();
        await store.AddMembershipAsync(ann.Id, project.Id, TestContext.Current.CancellationToken);
        await store.AddMembershipAsync(cyd.Id, project.Id, TestContext.Current.CancellationToken);
        var route = $"/projects/{project.Id}";

        // Act: read the page as the administrator and as the member
        var before = await ParseAsync(admin.Client, route);
        var asMember = await ParseAsync(member.Client, route);

        // Assert: both switches are on for the administrator; the member sees no card and no switch
        Assert.True(before.QuerySelector("[data-testid='author-checklist-switch']")?.HasAttribute("checked"));
        Assert.True(before.QuerySelector("[data-testid='reopener-signing-switch']")?.HasAttribute("checked"));
        Assert.Null(asMember.QuerySelector("[data-testid='review-rules-card']"));
        Assert.Null(asMember.QuerySelector("[data-testid='author-checklist-switch']"));

        // Act: the administrator switches the author rule off through the service (the page button does the same call)
        var changed = await app.Services.GetRequiredService<ProjectService>().SetReviewRuleAsync(
            TestActors.Ann, project.Id, ReviewRule.NonAuthorChecklistAnswers, true, false, TestContext.Current.CancellationToken);
        var after = await ParseAsync(admin.Client, route);

        // Assert: stored, shown as off, and the other switch is still on
        Assert.True(changed);
        Assert.False(after.QuerySelector("[data-testid='author-checklist-switch']")?.HasAttribute("checked"));
        Assert.True(after.QuerySelector("[data-testid='reopener-signing-switch']")?.HasAttribute("checked"));
    }

    /// <summary>
    ///     Test that the inbox route sends a visitor who is not signed in to the login page, which returns to the
    ///     inbox, and shows nothing of the inbox.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_InboxPage_Unauthenticated_IsChallenged()
    {
        // Arrange: start the real pipeline in memory; nobody is signed in
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var browser = new TestBrowser(app);

        // Act: request the inbox
        var response = await browser.Client.GetAsync("/inbox", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: a redirect to the login page that returns to the inbox, and no inbox in the body
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login?ReturnUrl=", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.Contains(
            Uri.EscapeDataString("/inbox"),
            response.Headers.Location?.OriginalString,
            StringComparison.Ordinal);
        Assert.DoesNotContain("inbox-page", html, StringComparison.Ordinal);
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
        var project = await TestWebApp.CreateProjectAsync(app, "Apollo", "Moon landing");
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id, "rev-1", "First review", [new("a.txt", TestContent.A)], new Actor("root", "root"), TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<RepositoryService>().CreateAsync(
            project.Id, "main-repo", "https://example.com/org/repo.git", null, null, new Actor("root", "root"), TestContext.Current.CancellationToken);
        string[] routes =
        [
            "/",
            "/login",
            "/about",
            "/help",
            "/help/getting-started",
            "/help/inbox",
            "/help/no-such-topic",
            "/account",
            "/inbox",
            "/users",
            "/users/new",
            "/projects",
            "/projects/new",
            $"/projects/{project.Id}",
            $"/projects/{project.Id}/reviews/new",
            $"/projects/{project.Id}/plan",
            $"/projects/{project.Id}/repositories",
            $"/projects/{project.Id}/repositories/new",
            "/projects/9999/plan",
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
    ///     Test that the stylesheet of the app keeps a long line of a diff inside the code area
    ///     and keeps the phone layout narrow, so the page itself never scrolls sideways.
    /// </summary>
    /// <remarks>
    ///     The in-memory browser has no layout engine, so the rendered width at 390 pixels is measured by the
    ///     Puppet walk-through; this test guards the rules that width depends on.
    /// </remarks>
    [Fact]
    public async Task WebSubsystem_Stylesheet_DiffRules_KeepCodeScrollingInsideItself()
    {
        // Arrange and Act: read the stylesheet the shell links to
        var css = await ReadStylesheetAsync();

        // Assert: the stylesheet lays the diff out as two columns per row, wraps long lines inside their cell, keeps the
        // code area from widening the page, keeps inline threads and the collapsed-run button in view, stacks the
        // cells and hides the placeholders on a narrow screen, and gives a phone a narrower gutter
        Assert.Matches(@"\.diff-code\s*\{[^}]*max-width:\s*100%;[^}]*overflow-x:\s*auto;", css);
        Assert.Matches(@"\.diff-pair\s*\{[^}]*display:\s*grid;[^}]*grid-template-columns:\s*\w+\(0,\s*1fr\)\s+\w+\(0,\s*1fr\);", css);
        Assert.Matches(@"\.diff-row\s*\{[^}]*min-width:\s*0;", css);
        Assert.Matches(@"\.diff-row \.line-text\s*\{[^}]*white-space:\s*pre-wrap;[^}]*overflow-wrap:\s*anywhere;", css);
        Assert.Matches(@"\.diff-row\.empty,\s*\.diff-row\.empty \.gutter\s*\{[^}]*background:", css);
        Assert.Matches(@"\.diff-inline\s*\{[^}]*position:\s*sticky;[^}]*left:\s*0;", css);
        Assert.Matches(@"\.diff-gap\s*\{[^}]*position:\s*sticky;[^}]*left:\s*0;", css);
        Assert.Matches(
            @"@media \(max-width: 39\.99rem\)\s*\{\s*\.diff-pair\s*\{[^}]*grid-template-columns:\s*\w+\(0,\s*1fr\);\s*\}\s*\.diff-row\.empty,\s*\.diff-row\.context\.left\s*\{[^}]*display:\s*none;",
            css);
        Assert.Matches(@"@media \(max-width: 29\.99rem\)\s*\{\s*\.diff-row \.line-no\s*\{[^}]*min-width:", css);
    }

    /// <summary>
    ///     Test that the stylesheet reveals the + control of a diff line on row hover and keyboard focus (and always
    ///     on touch screens) without removing it from the layout or the tab order.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_Stylesheet_DiffCommentControl_ShowsOnHoverFocusAndTouch()
    {
        // Arrange and Act: read the stylesheet the shell links to
        var css = await ReadStylesheetAsync();

        // Assert: hidden by opacity by default, revealed on hover, focus within, a selected row and focus-visible
        Assert.Matches(@"\.diff-row \.line-comment\s*\{[^}]*opacity:\s*0;", css);
        Assert.Matches(
            @"\.diff-row:hover \.line-comment,\s*\.diff-row:focus-within \.line-comment,[^{]*\.diff-row \.line-comment:focus-visible\s*\{[^}]*opacity:\s*1;",
            css);

        // Assert: always visible where there is no hover
        Assert.Matches(@"@media \(hover: none\)\s*\{\s*\.diff-row \.line-comment\s*\{[^}]*opacity:\s*1;", css);

        // Assert: never taken out of the layout or the tab order
        Assert.DoesNotMatch(@"\.line-comment[^{]*\{[^}]*display:\s*none", css);
        Assert.DoesNotMatch(@"\.line-comment[^{]*\{[^}]*visibility:\s*hidden", css);
    }

    /// <summary>
    ///     Test that the stylesheet shows the per-thread link as a button-styled link, not as the quiet underlined
    ///     text link it used to be.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_Stylesheet_ThreadLink_IsButtonStyled()
    {
        // Arrange and Act: read the stylesheet the shell links to
        var css = await ReadStylesheetAsync();

        // Assert: no dedicated rule strips the link of its button look, and the thread header sizes the link
        // like the buttons beside it
        Assert.DoesNotMatch(@"a\.thread-link\s*\{", css);
        Assert.Matches(@"\.thread-head button,\s*\.thread-head a\.action\s*\{[^}]*padding:", css);
        Assert.Matches(@"\.thread-head button,\s*\.thread-head a\.action\s*\{[^}]*font-size:", css);
    }

    /// <summary>
    ///     Test that the stylesheet gives the checklist section toggle and the line comment button their own hover
    ///     look instead of the global button hover fill, which left their text close to unreadable on a dark theme.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_Stylesheet_TransparentButtons_OverrideTheGlobalHoverFill()
    {
        // Arrange and Act: read the stylesheet the shell links to
        var css = await ReadStylesheetAsync();

        // Assert: both buttons define a hover rule that fills with the soft accent and never with the hover accent
        foreach (var selector in new[] { @"\.section-toggle", @"\.line-comment" })
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                css, selector + @":hover,\s*" + selector + @":focus-visible\s*\{(?<body>[^}]*)\}");
            Assert.True(match.Success, $"{selector} has no hover rule.");
            Assert.Contains("background: var(--accent-soft)", match.Groups["body"].Value, StringComparison.Ordinal);
            Assert.DoesNotContain("--accent-hover", match.Groups["body"].Value, StringComparison.Ordinal);
        }

        // Assert: the section toggle keeps its text color on hover
        Assert.Matches(@"\.section-toggle:hover,\s*\.section-toggle:focus-visible\s*\{[^}]*color:\s*var\(--text\)", css);
    }

    /// <summary>
    ///     Test that no test id repeats within a page, and that the breadcrumb links of the pages have their own
    ///     ids, distinct from the header tabs.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_AllPages_TestIdsAreUniqueAndBreadcrumbsDistinct()
    {
        // Arrange: start the real pipeline with data, so list rows and forms are all rendered
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var browser = await TestBrowser.SignedInAsync(app, "root", UserRole.Admin);
        var client = browser.Client;
        var project = await TestWebApp.CreateProjectAsync(app, "Apollo", "Moon landing");
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id, "rev-1", "First review", [new("a.txt", TestContent.A)], new Actor("root", "root"), TestContext.Current.CancellationToken);
        var reviews = $"/projects/{project.Id}/reviews/rev-1";
        (string Route, string Breadcrumb, string Header)[] pages =
        [
            ($"/projects/{project.Id}", "breadcrumb-projects-link", "projects-link"),
            ($"/projects/{project.Id}/reviews/new", "breadcrumb-projects-link", "projects-link"),
            ($"/projects/{project.Id}/plan", "breadcrumb-projects-link", "projects-link"),
            (reviews, "breadcrumb-projects-link", "projects-link"),
            ($"{reviews}/checklist", "breadcrumb-projects-link", "projects-link"),
            ($"{reviews}/history", "breadcrumb-projects-link", "projects-link"),
            ($"/projects/{project.Id}/repositories", "breadcrumb-projects-link", "projects-link"),
            ($"/projects/{project.Id}/repositories/new", "breadcrumb-projects-link", "projects-link"),
            ("/users/new", "breadcrumb-users-link", "users-link")
        ];

        foreach (var (route, breadcrumb, header) in pages)
        {
            // Act: render the page
            var document = await ParseAsync(client, route);
            var ids = document.QuerySelectorAll("[data-testid]").Select(e => e.GetAttribute("data-testid")!).ToList();

            // Assert: no id repeats, the breadcrumb link has its own id, and the header tab keeps its id
            var repeated = ids.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(repeated.Count == 0, $"'{route}' repeats the test ids: {string.Join(", ", repeated)}.");
            Assert.NotNull(document.QuerySelector($"[data-testid='{breadcrumb}']"));
            Assert.Single(ids, header);
        }
    }

    /// <summary>
    ///     Reads the stylesheet the shell links to. The in-memory host does not serve static files, so it is read
    ///     from the project that ships it.
    /// </summary>
    /// <returns>The text of <c>app.css</c>.</returns>
    private static async Task<string> ReadStylesheetAsync()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DemaConsulting.ReviewCenter.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return await File.ReadAllTextAsync(
            Path.Combine(directory.FullName, "src", "DemaConsulting.ReviewCenter", "wwwroot", "app.css"),
            TestContext.Current.CancellationToken);
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
