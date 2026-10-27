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
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the App unit: the HTML shell (<c>App.razor</c>) and the router (<c>Routes.razor</c>).
/// </summary>
public class AppTests : PageTestBase
{
    /// <summary>
    ///     Test that the HTML shell declares a base href, the head outlet output, and the Blazor client script.
    /// </summary>
    [Fact]
    public async Task App_Render_ServedAtRoot_ContainsShellElements()
    {
        // Arrange: start the real pipeline in memory
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request the root document
        var html = await client.GetStringAsync("/", Xunit.TestContext.Current.CancellationToken);

        // Assert: shell elements are present
        Assert.Contains("<base href=\"/\">", html);
        Assert.Contains("<title>ReviewCenter</title>", html);
        Assert.Contains("_framework/blazor.web.js", html);
    }

    /// <summary>
    ///     Test that the router renders the home page for the root URL.
    /// </summary>
    [Fact]
    public void Routes_Render_RootUrl_RendersHomePage()
    {
        // Arrange: navigate to the root URL
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/");

        // Act: render the router
        var cut = Render<Routes>();

        // Assert: the home page is shown
        Assert.NotEmpty(cut.FindAll("[data-testid='home-page']"));
    }

    /// <summary>
    ///     Test that a request to the running server for an address that no page handles is answered with status 404
    ///     and the framed not-found page, with the header brand link and the footer.
    /// </summary>
    [Fact]
    public async Task App_Request_UnknownPath_Returns404WithFramedNotFoundPage()
    {
        // Arrange: start the real pipeline in memory
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request an address that no page handles
        var response = await client.GetAsync("/no-such-page?x=1", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: 404 with the not-found page inside the page frame
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("data-testid=\"not-found\"", html, StringComparison.Ordinal);
        Assert.Contains("Page not found", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"home-link\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"footer\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an unknown address under the reserved <c>/api</c> prefix keeps a plain 404 without the page frame.
    /// </summary>
    [Fact]
    public async Task App_Request_UnknownApiPath_Returns404WithoutPageFrame()
    {
        // Arrange: start the real pipeline in memory
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request an unknown machine-client address
        var response = await client.GetAsync("/api/anything", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: 404 and no HTML page
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("data-testid=\"footer\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Page not found", body, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an unknown address that ends in a file extension keeps a plain 404 without the page frame.
    /// </summary>
    [Fact]
    public async Task App_Request_UnknownFilePath_Returns404WithoutPageFrame()
    {
        // Arrange: start the real pipeline in memory
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request a missing style sheet
        var response = await client.GetAsync("/missing.css", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: 404 and no HTML page
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Page not found", body, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a non-page request (a POST) to an unknown address keeps a plain 404 without the page frame.
    /// </summary>
    [Fact]
    public async Task App_Request_PostToUnknownPath_Returns404WithoutPageFrame()
    {
        // Arrange: start the real pipeline in memory
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: post to an unknown address
        var response = await client.PostAsync("/no-such-page", new StringContent(string.Empty), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: the 404 is not turned into the not-found page
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Page not found", body, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the router shows a not-found message for an unknown URL.
    /// </summary>
    [Fact]
    public void Routes_Render_UnknownUrl_ShowsNotFoundMessage()
    {
        // Arrange: navigate to a URL no page handles
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/does-not-exist");

        // Act: render the router
        var cut = Render<Routes>();

        // Assert: the not-found message is shown
        Assert.Contains("Page not found", cut.Find("[data-testid='not-found'] h1").TextContent, StringComparison.Ordinal);
        Assert.Equal("/projects", cut.Find("[data-testid='not-found-projects-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that the not-found page of an unknown URL is shown inside the page frame, so it has the header
    ///     with the product link and the footer like every other page.
    /// </summary>
    [Fact]
    public void Routes_Render_UnknownUrl_ShowsNotFoundInsidePageFrameWithHeaderAndFooter()
    {
        // Arrange: nobody is signed in and the address matches no page
        SignOut();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/does-not-exist");

        // Act: render the router
        var cut = Render<Routes>();

        // Assert: the not-found page, the header brand link, and the footer are all present
        Assert.NotEmpty(cut.FindAll("[data-testid='not-found']"));
        Assert.Equal("/", Assert.Single(cut.FindAll("[data-testid='home-link']")).GetAttribute("href"));
        Assert.Contains("DEMA Consulting", cut.Find("[data-testid='footer']").TextContent, StringComparison.Ordinal);
        Assert.Equal("/about", cut.Find("[data-testid='about-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that the header shows the display name of the signed-in user with the account and logout
    ///     controls, and the Users link only for an administrator.
    /// </summary>
    [Fact]
    public void MainLayout_Render_SignedInUser_ShowsNameAccountAndLogout()
    {
        // Arrange: a reviewer is signed in
        SignInAs("bea", "Bea");

        // Act: render the layout, then again as an administrator
        var reviewer = Render<MainLayout>();
        SignInAs("root", "Root", UserRole.Admin);
        var admin = Render<MainLayout>();

        // Assert: the name, the account link and the logout button are shown, but the Users link only to the administrator
        Assert.Equal("Bea", reviewer.Find("[data-testid='current-user']").TextContent);
        Assert.Equal("/projects", reviewer.Find("[data-testid='projects-link']").GetAttribute("href"));
        Assert.NotNull(reviewer.Find("[data-testid='account-link']"));
        Assert.NotNull(reviewer.Find("[data-testid='logout-button']"));
        Assert.Empty(reviewer.FindAll("[data-testid='users-link']"));
        Assert.Empty(reviewer.FindAll("[data-testid='audit-link']"));
        Assert.Empty(reviewer.FindAll("[data-testid='repositories-link']"));
        Assert.Empty(reviewer.FindAll("[data-testid='signin-link']"));
        Assert.NotNull(admin.Find("[data-testid='users-link']"));
        Assert.Equal("/repositories", admin.Find("[data-testid='repositories-link']").GetAttribute("href"));
        Assert.Equal("/audit", admin.Find("[data-testid='audit-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that every page has a footer with the version and a link to the About page, even when nobody is
    ///     signed in, and that the header has exactly one link to the home page.
    /// </summary>
    [Fact]
    public void MainLayout_Render_NobodySignedIn_ShowsFooterWithVersionAndAboutLink()
    {
        // Arrange: nobody is signed in
        SignOut();

        // Act: render the layout
        var layout = Render<MainLayout>();

        // Assert: the footer names the product, the version and the copyright and links to /about
        Assert.Contains("ReviewCenter", layout.Find("[data-testid='footer']").TextContent);
        Assert.Contains("DEMA Consulting", layout.Find("[data-testid='footer']").TextContent);
        Assert.False(string.IsNullOrWhiteSpace(layout.Find("[data-testid='version-text']").TextContent));
        Assert.Contains(Program.Version.Split('+')[0], layout.Find("[data-testid='version-text']").TextContent, StringComparison.Ordinal);
        Assert.True(layout.Find("[data-testid='version-text']").TextContent.Length <= Program.Version.Length);
        Assert.Equal("/about", layout.Find("[data-testid='about-link']").GetAttribute("href"));
        Assert.Equal("/", Assert.Single(layout.FindAll("[data-testid='home-link']")).GetAttribute("href"));
    }

    /// <summary>
    ///     Test that the document head applies the stored theme choice before the style sheet is requested, so
    ///     the page never flashes the wrong theme.
    /// </summary>
    [Fact]
    public async Task App_Render_ServedAtRoot_AppliesStoredThemeBeforeStyleSheet()
    {
        // Arrange: start the real pipeline in memory
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request the root document
        var html = await client.GetStringAsync("/", Xunit.TestContext.Current.CancellationToken);

        // Assert: the inline script that reads the stored choice comes before the style sheet link
        var script = html.IndexOf("reviewcenter-theme", StringComparison.Ordinal);
        var styleSheet = html.IndexOf("rel=\"stylesheet\"", StringComparison.Ordinal);
        Assert.True(script >= 0, "The theme script is missing.");
        Assert.True(styleSheet > script, "The theme script must come before the style sheet.");
        Assert.Contains("data-theme", html, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the document defines the browser helper behind the "Copy link" button: the clipboard API with
    ///     a hidden-textarea fallback for pages that are not a secure context.
    /// </summary>
    [Fact]
    public async Task App_Render_ServedAtRoot_DefinesCopyTextHelper()
    {
        // Arrange: start the real pipeline in memory
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request the root document
        var html = await client.GetStringAsync("/", Xunit.TestContext.Current.CancellationToken);

        // Assert: the helper, the clipboard API and the fallback are all there
        Assert.Contains("copyText", html, StringComparison.Ordinal);
        Assert.Contains("navigator.clipboard", html, StringComparison.Ordinal);
        Assert.Contains("createElement('textarea')", html, StringComparison.Ordinal);
        Assert.Contains("execCommand('copy')", html, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the address of the new-review form is handled by the form, not by a review called "new".
    /// </summary>
    [Fact]
    public async Task Routes_Render_NewReviewRoute_ShowsFormNotReview()
    {
        // Arrange: a signed-in user, a project, and the router at the new-review address
        SignInAs("ann", "Ann");
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/projects/{project.Id}/reviews/new");

        // Act: render the router
        var cut = Render<Routes>();

        // Assert: the creation form is shown and no review page is
        Assert.NotEmpty(cut.FindAll("[data-testid='new-review-form']"));
        Assert.Empty(cut.FindAll("[data-testid='review-page']"));
    }

    /// <summary>
    ///     Test that the header offers a sign-in link, and no account controls, when nobody is signed in.
    /// </summary>
    [Fact]
    public void MainLayout_Render_NobodySignedIn_ShowsSignInLink()
    {
        // Arrange: nobody is signed in
        SignOut();

        // Act: render the layout
        var cut = Render<MainLayout>();

        // Assert: only the sign-in link is shown
        Assert.Empty(cut.FindAll("[data-testid='projects-link']"));
        Assert.Equal("/login", cut.Find("[data-testid='signin-link']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid='current-user']"));
        Assert.Empty(cut.FindAll("[data-testid='logout-button']"));
    }

    /// <summary>
    ///     Test that a reviewer who navigates to the user administration route inside the circuit is refused
    ///     with the access denied message and is not shown the user list.
    /// </summary>
    [Fact]
    public async Task Routes_Render_ReviewerOpensUsersRoute_ShowsAccessDeniedNotUserList()
    {
        // Arrange: a reviewer is signed in, one user exists, and the router is at /users
        SignInAs("ann", "Ann");
        await Users.CreateAsync("root", "Root User", UserRole.Admin, "correct horse battery", TestContext.Current.CancellationToken);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/users");

        // Act: render the router
        var cut = Render<Routes>();

        // Assert: access is denied and neither the user list nor the creation form is rendered
        Assert.Contains("Access denied", cut.Find("[data-testid='access-denied']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='create-user-button']"));
        Assert.DoesNotContain("Root User", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an administrator opening the user administration route is shown the page.
    /// </summary>
    [Fact]
    public void Routes_Render_AdminOpensUsersRoute_ShowsUsersPage()
    {
        // Arrange: an administrator is signed in and the router is at /users
        SignInAs("root", "Root", UserRole.Admin);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/users");

        // Act: render the router
        var cut = Render<Routes>();

        // Assert: the user list page is shown (with its New user button) and there is no access denied message
        Assert.NotNull(cut.Find("[data-testid='new-user-button']"));
        Assert.Empty(cut.FindAll("[data-testid='access-denied']"));
    }

    /// <summary>
    ///     Test that a signed-out user who navigates to a protected route is not shown the page and is pointed to
    ///     the login page, which returns to the requested page.
    /// </summary>
    [Fact]
    public void Routes_Render_AnonymousOpensProjectsRoute_ShowsLoginLinkNotPage()
    {
        // Arrange: nobody is signed in and the router is at /projects
        SignOut();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/projects");

        // Act: render the router
        var cut = Render<Routes>();

        // Assert: the projects page is not rendered; a link to the login page with the return address is
        Assert.Empty(cut.FindAll("[data-testid='projects-page']"));
        Assert.Equal(
            "/login?ReturnUrl=" + Uri.EscapeDataString("/projects"),
            cut.Find("[data-testid='access-denied-login-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that the session gate shows its content, and nothing of its own, while the user is signed in.
    /// </summary>
    [Fact]
    public void SessionGate_Render_SignedInUser_ShowsContent()
    {
        // Arrange: a signed-in user (the default)

        // Act: render the gate around some content
        var cut = Render<SessionGate>(parameters => parameters.AddChildContent("<p data-testid=\"inner\">Inside</p>"));

        // Assert: the content is shown and the session-ended message is not
        Assert.Equal("Inside", cut.Find("[data-testid='inner']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='session-ended']"));
    }

    /// <summary>
    ///     Test that the session gate shows the session-ended message with a login link that returns to the page,
    ///     and none of its content, when nobody is signed in.
    /// </summary>
    [Fact]
    public void SessionGate_Render_Anonymous_ShowsSessionEndedWithLoginLinkAndNoContent()
    {
        // Arrange: nobody is signed in and the page is at /projects/1
        SignOut();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/projects/1");

        // Act: render the gate around some content
        var cut = Render<SessionGate>(parameters => parameters.AddChildContent("<p data-testid=\"inner\">Inside</p>"));

        // Assert: the message names what happened, the link returns to this page, and the content is gone
        Assert.Contains("Session ended", cut.Find("[data-testid='session-ended'] h1").TextContent, StringComparison.Ordinal);
        Assert.Equal(
            "/login?ReturnUrl=" + Uri.EscapeDataString("/projects/1"),
            cut.Find("[data-testid='session-ended-login-link']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid='inner']"));
    }

    /// <summary>
    ///     Test that an open gate replaces its content by the session-ended message when the sign-in state of the
    ///     page becomes anonymous.
    /// </summary>
    [Fact]
    public void SessionGate_Render_StateBecomesAnonymous_ReplacesContentWithMessage()
    {
        // Arrange: a signed-in user and a gate that shows its content
        var cut = Render<SessionGate>(parameters => parameters.AddChildContent("<p data-testid=\"inner\">Inside</p>"));
        Assert.NotEmpty(cut.FindAll("[data-testid='inner']"));

        // Act: the sign-in state ends
        SignOut();

        // Assert: the content is replaced by the message
        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("[data-testid='session-ended']"));
            Assert.Empty(cut.FindAll("[data-testid='inner']"));
        });
    }

    /// <summary>
    ///     Test that every protected page is wrapped in the session gate, so that none is left showing data or
    ///     actions to somebody who is no longer signed in.
    /// </summary>
    /// <param name="page">The page to render.</param>
    /// <param name="rootTestId">The test id of the root element of the page.</param>
    [Theory]
    [InlineData("ProjectsPage", "projects-page")]
    [InlineData("ProjectPage", "project-page")]
    [InlineData("PlanPage", "plan-page")]
    [InlineData("ReviewPage", "review-page")]
    [InlineData("ChecklistPage", "checklist-page")]
    [InlineData("AccountPage", "account-page")]
    [InlineData("UsersPage", "users-page")]
    public void SessionGate_Render_EveryProtectedPageSignedOut_ShowsSessionEndedAndNoPage(string page, string rootTestId)
    {
        // Arrange: nobody is signed in
        SignOut();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var noParameters = new Dictionary<string, object>();
        var projectParameters = new Dictionary<string, object> { ["ProjectId"] = 1 };
        var reviewParameters = new Dictionary<string, object> { ["ProjectId"] = 1, ["ReviewId"] = "REV-1" };
        var (type, parameters) = page switch
        {
            "ProjectsPage" => (typeof(ProjectsPage), noParameters),
            "ProjectPage" => (typeof(ProjectPage), projectParameters),
            "PlanPage" => (typeof(PlanPage), projectParameters),
            "ReviewPage" => (typeof(ReviewPage), reviewParameters),
            "ChecklistPage" => (typeof(ChecklistPage), reviewParameters),
            "AccountPage" => (typeof(AccountPage), noParameters),
            _ => (typeof(UsersPage), noParameters)
        };

        // Act: render the page
        var cut = Render<DynamicComponent>(p => p.Add(c => c.Type, type).Add(c => c.Parameters, parameters));

        // Assert: the session-ended message is shown instead of the page
        Assert.NotEmpty(cut.FindAll("[data-testid='session-ended']"));
        Assert.Empty(cut.FindAll($"[data-testid='{rootTestId}']"));
    }
}
