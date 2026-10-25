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
        Assert.Contains("Page not found.", cut.Markup);
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

        // Assert: the creation form is shown and there is no access denied message
        Assert.NotNull(cut.Find("[data-testid='create-user-button']"));
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
