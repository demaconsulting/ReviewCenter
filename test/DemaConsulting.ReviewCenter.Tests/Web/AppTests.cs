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
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the App unit: the HTML shell (<c>App.razor</c>) and the router (<c>Routes.razor</c>).
/// </summary>
public class AppTests : InboxWebTestBase
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
        using var content = new StringContent(string.Empty);
        using var response = await client.PostAsync("/no-such-page", content, TestContext.Current.CancellationToken);
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
        SignInAs("bea", "Bea", UserRole.Reviewer);

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
        Assert.Empty(reviewer.FindAll("[data-testid='signin-link']"));
        Assert.NotNull(admin.Find("[data-testid='users-link']"));
        Assert.Empty(admin.FindAll("[data-testid='repositories-link']"));
        Assert.Equal("/audit", admin.Find("[data-testid='audit-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that a user who signed in with a temporary password sees no navigation tabs and is sent to the
    ///     account page from any other page, but may stay on the account, help and about pages.
    /// </summary>
    /// <param name="path">The page the user is on.</param>
    /// <param name="redirected">Whether the user must be sent to the account page.</param>
    [Theory]
    [InlineData("projects", true)]
    [InlineData("reviews/1", true)]
    [InlineData("account", false)]
    [InlineData("help/project-members", false)]
    [InlineData("about", false)]
    public void MainLayout_Render_TemporaryPassword_ConfinesUserToAccountPage(string path, bool redirected)
    {
        // Arrange: a flagged session on the given page
        SignInAsTemporaryPassword("bea", "Bea");
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/" + path);

        // Act: render the layout
        var layout = Render<MainLayout>();

        // Assert: no tabs; the browser is sent to /account only when the page is not one of the open ones
        Assert.Empty(layout.FindAll("[data-testid='projects-link']"));
        Assert.Empty(layout.FindAll("[data-testid='users-link']"));
        Assert.EndsWith(redirected ? "/account" : "/" + path, navigation.Uri, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the confinement also applies to navigation after the layout was rendered, because the layout
    ///     lives on while a user clicks from page to page: a flagged user who goes to another page is sent to the
    ///     account page, one who goes to an open page is not, and a layout that was disposed no longer redirects.
    /// </summary>
    [Fact]
    public void MainLayout_Navigate_TemporaryPassword_RedirectsLaterNavigationAndStopsAfterDispose()
    {
        // Arrange: a flagged session on the help page, which is open
        SignInAsTemporaryPassword("bea", "Bea");
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/help");
        var layout = Render<MainLayout>();
        Assert.NotNull(layout.Markup);

        // Act: navigate to an open page, then to a page that is not open
        navigation.NavigateTo("/account");
        var afterOpen = navigation.Uri;
        navigation.NavigateTo("/help/project-members");
        var afterOpenHelp = navigation.Uri;
        navigation.NavigateTo("/projects");
        var afterClosed = navigation.Uri;

        // Assert: only the closed page is redirected
        Assert.EndsWith("/account", afterOpen, StringComparison.Ordinal);
        Assert.EndsWith("/help/project-members", afterOpenHelp, StringComparison.Ordinal);
        Assert.EndsWith("/account", afterClosed, StringComparison.Ordinal);

        // Act: the layout is disposed and the user navigates again
        layout.Instance.Dispose();
        navigation.NavigateTo("/projects");

        // Assert: the handler was removed, so nothing redirects any more
        Assert.EndsWith("/projects", navigation.Uri, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a user without a temporary password is never redirected by a later navigation.
    /// </summary>
    [Fact]
    public void MainLayout_Navigate_OrdinaryUser_IsNotRedirected()
    {
        // Arrange
        SignInAs("bea", "Bea", UserRole.Reviewer);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/help");
        _ = Render<MainLayout>();

        // Act
        navigation.NavigateTo("/projects");

        // Assert
        Assert.EndsWith("/projects", navigation.Uri, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a user with a single review that needs action sees the Inbox tab with a badge that says so, and
    ///     with two the plural form.
    /// </summary>
    /// <param name="reviews">The number of reviews that need the user's action.</param>
    /// <param name="label">The accessible label the badge must have.</param>
    [Theory]
    [InlineData(1, "1 review needs your action")]
    [InlineData(2, "2 reviews need your action")]
    public async Task MainLayout_Render_UserWithActionItems_ShowsInboxTabWithCount(int reviews, string label)
    {
        // Arrange: ann must sign the given number of reviews
        UseCountingUsers();
        await ArrangeActionItemsAsync(reviews);
        SignInAs("ann", "Ann", false);

        // Act: render the layout
        var layout = Render<MainLayout>();

        // Assert: the Inbox tab links to the inbox and carries the count, with the exact number as accessible text
        Assert.Equal("/inbox", layout.Find("[data-testid='inbox-link']").GetAttribute("href"));
        var badge = layout.Find("[data-testid='nav-inbox-count']");
        Assert.Equal(reviews.ToString(System.Globalization.CultureInfo.InvariantCulture), badge.TextContent);
        Assert.Equal(label, badge.GetAttribute("aria-label"));
        Assert.Equal("inbox-link", badge.ParentElement?.GetAttribute("data-testid"));
    }

    /// <summary>
    ///     Test that the badge is hidden while nothing needs the user's action, but the tab stays.
    /// </summary>
    [Fact]
    public async Task MainLayout_Render_NoActionItems_ShowsInboxTabWithoutBadge()
    {
        // Arrange: ann is a member and nothing waits for her
        UseCountingUsers();
        await ArrangeProjectAsync();
        SignInAs("ann", "Ann", false);

        // Act: render the layout
        var layout = Render<MainLayout>();

        // Assert: the tab is there without a badge
        Assert.Equal("/inbox", layout.Find("[data-testid='inbox-link']").GetAttribute("href"));
        Assert.Empty(layout.FindAll("[data-testid='nav-inbox-count']"));
    }

    /// <summary>
    ///     Test that a count above ninety-nine is shown as 99+ while the accessible label keeps the exact number.
    /// </summary>
    [Fact]
    public async Task MainLayout_Render_ManyActionItems_CapsBadgeAt99Plus()
    {
        // Arrange: ann must sign a hundred reviews
        UseCountingUsers();
        await ArrangeActionItemsAsync(100);
        SignInAs("ann", "Ann", false);

        // Act: render the layout
        var layout = Render<MainLayout>();

        // Assert: the badge is capped, the label is exact
        var badge = layout.Find("[data-testid='nav-inbox-count']");
        Assert.Equal("99+", badge.TextContent);
        Assert.Equal("100 reviews need your action", badge.GetAttribute("aria-label"));
    }

    /// <summary>
    ///     Test that nobody gets an Inbox tab, and the inbox is not even asked for a count, while nobody is signed in
    ///     or the password is still temporary.
    /// </summary>
    [Fact]
    public async Task MainLayout_Render_NobodyOrTemporaryPassword_ShowsNoInboxTabAndReadsNoCount()
    {
        // Arrange: ann has a review that needs her action, but nobody is signed in
        var users = UseCountingUsers();
        await ArrangeActionItemsAsync(1);
        SignOut();

        // Act: render the layout for nobody, then for ann with a temporary password
        var anonymous = Render<MainLayout>();
        SignInAsTemporaryPassword("ann", "Ann");
        var flagged = Render<MainLayout>();

        // Assert: no tab, no badge and no lookup of the inbox in either case
        Assert.Empty(anonymous.FindAll("[data-testid='inbox-link']"));
        Assert.Empty(anonymous.FindAll("[data-testid='nav-inbox-count']"));
        Assert.Empty(flagged.FindAll("[data-testid='inbox-link']"));
        Assert.Empty(flagged.FindAll("[data-testid='nav-inbox-count']"));
        Assert.Equal(0, users.Lookups);
    }

    /// <summary>
    ///     Test that the count is read once per user in thirty seconds however often the layout is rendered, and read
    ///     again once the clock has moved past that, so that the badge may be up to thirty seconds behind.
    /// </summary>
    [Fact]
    public async Task MainLayout_Render_WithinThirtySeconds_ReadsCountOnceThenAgainAfterwards()
    {
        // Arrange: ann must sign one review
        var users = UseCountingUsers();
        await ArrangeActionItemsAsync(1);
        SignInAs("ann", "Ann", false);

        // Act: render twice, add a review, render at 29 seconds, then at 31 seconds
        var first = Render<MainLayout>();
        var second = Render<MainLayout>();
        await CreateInboxReviewAsync("later");
        await AddSlotAsync("later", "ann");
        Time.Advance(TimeSpan.FromSeconds(29));
        var third = Render<MainLayout>();
        var lookupsInside = users.Lookups;
        Time.Advance(TimeSpan.FromSeconds(2));
        var fourth = Render<MainLayout>();

        // Assert: one read for the first three renders, which still show the old number, and a new read afterwards
        Assert.Equal("1", first.Find("[data-testid='nav-inbox-count']").TextContent);
        Assert.Equal("1", second.Find("[data-testid='nav-inbox-count']").TextContent);
        Assert.Equal("1", third.Find("[data-testid='nav-inbox-count']").TextContent);
        Assert.Equal(1, lookupsInside);
        Assert.Equal(2, users.Lookups);
        Assert.Equal("2", fourth.Find("[data-testid='nav-inbox-count']").TextContent);
    }

    /// <summary>
    ///     Test that two users never share a cached count.
    /// </summary>
    [Fact]
    public async Task MainLayout_Render_TwoUsers_KeepsTheirCountsApart()
    {
        // Arrange: ann must sign a review, bob has nothing
        var users = UseCountingUsers();
        await ArrangeActionItemsAsync(1);

        // Act: render for ann, then for bob right after
        SignInAs("ann", "Ann", false);
        var ann = Render<MainLayout>();
        SignInAs("bob", "Bob", false);
        var bob = Render<MainLayout>();

        // Assert: each user got a read of their own
        Assert.Equal("1", ann.Find("[data-testid='nav-inbox-count']").TextContent);
        Assert.Empty(bob.FindAll("[data-testid='nav-inbox-count']"));
        Assert.Equal(2, users.Lookups);
    }

    /// <summary>
    ///     Test that a navigation refreshes the badge once the cached count has expired, that a navigation inside the
    ///     thirty seconds does not read again, and that a layout that was disposed no longer reads.
    /// </summary>
    [Fact]
    public async Task MainLayout_Navigate_ChangedCount_RefreshesBadgeAndStopsAfterDispose()
    {
        // Arrange: ann must sign one review and the layout shows it
        var users = UseCountingUsers();
        await ArrangeActionItemsAsync(1);
        SignInAs("ann", "Ann", false);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/help");
        var layout = Render<MainLayout>();
        Assert.Equal("1", layout.Find("[data-testid='nav-inbox-count']").TextContent);

        // Act: another review needs ann, and she navigates within thirty seconds
        await CreateInboxReviewAsync("later");
        await AddSlotAsync("later", "ann");
        navigation.NavigateTo("/projects");

        // Assert: the cached count is still used
        Assert.Equal(1, users.Lookups);
        Assert.Equal("1", layout.Find("[data-testid='nav-inbox-count']").TextContent);

        // Act: the cache expires and she navigates again
        Time.Advance(TimeSpan.FromSeconds(31));
        navigation.NavigateTo("/inbox");

        // Assert: the badge shows the new number
        await layout.WaitForAssertionAsync(
            () => Assert.Equal("2", layout.Find("[data-testid='nav-inbox-count']").TextContent));
        Assert.Equal(2, users.Lookups);

        // Act: the layout is disposed, the cache expires again and she navigates
        layout.Instance.Dispose();
        Time.Advance(TimeSpan.FromSeconds(31));
        navigation.NavigateTo("/projects");

        // Assert: nothing reads the count any more
        Assert.Equal(2, users.Lookups);
    }

    /// <summary>
    ///     Test that a slow count is reused for thirty seconds from the moment it finished, not from the moment it
    ///     started.
    /// </summary>
    [Fact]
    public async Task MainLayout_Render_SlowCount_IsReusedFromTheEndOfTheRead()
    {
        // Arrange: ann must sign one review and the read takes 20 seconds of the test clock
        var users = UseCountingUsers();
        await ArrangeActionItemsAsync(1);
        SignInAs("ann", "Ann", false);
        users.OnLookup = () => Time.Advance(TimeSpan.FromSeconds(20));
        var first = Render<MainLayout>();
        users.OnLookup = null;

        // Act: 15 seconds after the read finished (35 seconds after it started) the layout is rendered again
        Time.Advance(TimeSpan.FromSeconds(15));
        var second = Render<MainLayout>();

        // Assert: the number of the first read is still reused
        Assert.Equal("1", first.Find("[data-testid='nav-inbox-count']").TextContent);
        Assert.Equal("1", second.Find("[data-testid='nav-inbox-count']").TextContent);
        Assert.Equal(1, users.Lookups);
    }

    /// <summary>
    ///     Test that a navigation that still reaches a layout that was disposed does nothing.
    /// </summary>
    [Fact]
    public async Task MainLayout_Navigate_DisposedLayout_DoesNothing()
    {
        // Arrange: a rendered layout for a user with a review to sign, disposed afterwards, and an expired cache
        var users = UseCountingUsers();
        await ArrangeActionItemsAsync(1);
        SignInAs("ann", "Ann", false);
        var layout = Render<MainLayout>();
        layout.Instance.Dispose();
        Time.Advance(TimeSpan.FromSeconds(31));
        var handler = typeof(MainLayout).GetMethod(
            "OnLocationChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        // Act: the navigation event is raised on the disposed layout anyway
        handler.Invoke(layout.Instance, [null, new LocationChangedEventArgs("http://localhost/projects", false)]);

        // Assert: nothing was read
        Assert.Equal(1, users.Lookups);
    }

    /// <summary>
    ///     Test that a refresh that was queued by a navigation before the layout was disposed, and that runs after the
    ///     disposal, reads nothing.
    /// </summary>
    [Fact]
    public async Task MainLayout_Navigate_RefreshQueuedBeforeDispose_ReadsNothingAfterDispose()
    {
        // Arrange: a rendered layout for a user with a review to sign, and an expired cache
        var users = UseCountingUsers();
        await ArrangeActionItemsAsync(1);
        SignInAs("ann", "Ann", false);
        var layout = Render<MainLayout>();
        Time.Advance(TimeSpan.FromSeconds(31));
        var handler = typeof(MainLayout).GetMethod(
            "OnLocationChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        // Act: the renderer is busy while the navigation arrives, so the refresh is queued; the layout is disposed before
        // the queue runs
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var busy = Task.Run(
            () => layout.InvokeAsync(() =>
            {
                started.Set();
                release.Wait(TestContext.Current.CancellationToken);
            }),
            TestContext.Current.CancellationToken);
        started.Wait(TestContext.Current.CancellationToken);
        handler.Invoke(layout.Instance, [null, new LocationChangedEventArgs("http://localhost/projects", false)]);
        layout.Instance.Dispose();
        release.Set();
        await busy;
        await layout.InvokeAsync(() => { });

        // Assert: the queued refresh did not read the count
        Assert.Equal(1, users.Lookups);
    }

    /// <summary>
    ///     Test that a count that cannot be read hides the badge without breaking the page, is not cached as a
    ///     failure, and shows again once the inbox works.
    /// </summary>
    [Fact]
    public async Task MainLayout_Render_CountFails_HidesBadgeAndStillRendersPage()
    {
        // Arrange: ann must sign a review but reading the inbox fails
        var users = UseCountingUsers();
        await ArrangeActionItemsAsync(1);
        SignInAs("ann", "Ann", false);
        users.Fail = true;

        // Act: render the layout
        var broken = Render<MainLayout>();

        // Assert: the rest of the frame is there and only the badge is missing
        Assert.Equal("Ann", broken.Find("[data-testid='current-user']").TextContent);
        Assert.NotNull(broken.Find("[data-testid='inbox-link']"));
        Assert.NotNull(broken.Find("[data-testid='footer']"));
        Assert.Empty(broken.FindAll("[data-testid='nav-inbox-count']"));

        // Act: the inbox works again within thirty seconds
        users.Fail = false;
        var recovered = Render<MainLayout>();

        // Assert: the failure was not cached, so the badge is back
        Assert.Equal("1", recovered.Find("[data-testid='nav-inbox-count']").TextContent);
    }

    /// <summary>
    ///     Replaces the inbox service by one whose user lookups, the first step of every count, are counted and can be
    ///     made to fail; call it before the first render.
    /// </summary>
    /// <returns>The counting store.</returns>
    private CountingUserStore UseCountingUsers()
    {
        var users = new CountingUserStore(UserStore);
        Services.RemoveAll<InboxService>();
        Services.AddSingleton(new InboxService(InboxStore, Store, Access, users, Time));
        return users;
    }

    /// <summary>
    ///     Creates the project and a number of reviews in which <c>ann</c> has to sign.
    /// </summary>
    /// <param name="reviews">The number of reviews.</param>
    /// <returns>A task that completes when the reviews exist.</returns>
    private async Task ArrangeActionItemsAsync(int reviews)
    {
        await ArrangeProjectAsync();
        for (var i = 0; i < reviews; i++)
        {
            var id = "r" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await CreateInboxReviewAsync(id);
            await AddSlotAsync(id, "ann");
        }
    }

    /// <summary>
    ///     Test that the sign-in page shows the notice to sign in with the new password only after a password
    ///     change.
    /// </summary>
    /// <param name="query">The query string of the address.</param>
    /// <param name="shown">Whether the notice must be shown.</param>
    [Theory]
    [InlineData("?changed=1", true)]
    [InlineData("", false)]
    [InlineData("?changed=0", false)]
    public void LoginPage_Render_ChangedQuery_ShowsNoticeOnlyAfterPasswordChange(string query, bool shown)
    {
        // Arrange: nobody is signed in, and the address carries the query
        SignOut();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/login" + query);

        // Act: render the page
        var cut = Render<LoginPage>();

        // Assert: the notice is there only for changed=1
        Assert.Equal(shown ? 1 : 0, cut.FindAll("[data-testid='login-password-changed']").Count);
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
    ///     Test that the document defines the focus helper and the Ctrl+Enter listener that presses the button a
    ///     comment field names, so a comment can be sent from the keyboard without a server round trip per key.
    /// </summary>
    [Fact]
    public async Task App_Render_ServedAtRoot_DefinesFocusHelperAndCtrlEnterListener()
    {
        // Arrange: start the real pipeline in memory
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request the root document
        var html = await client.GetStringAsync("/", Xunit.TestContext.Current.CancellationToken);

        // Assert: the focus helper, the key listener, and the attribute that names the button are all there
        Assert.Contains("focusTestId", html, StringComparison.Ordinal);
        Assert.Contains("addEventListener('keydown'", html, StringComparison.Ordinal);
        Assert.Contains("data-ctrl-enter-submits", html, StringComparison.Ordinal);
        Assert.Contains("e.ctrlKey || e.metaKey", html, StringComparison.Ordinal);
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
        SignInAs("ann", "Ann", UserRole.Reviewer);
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

    /// <summary>
    ///     A user store that counts the look-ups by user name, which the inbox service makes first on every count, and
    ///     that can be made to fail.
    /// </summary>
    /// <param name="inner">The store the calls are passed on to.</param>
    private sealed class CountingUserStore(IUserStore inner) : IUserStore
    {
        /// <summary>
        ///     Gets the number of look-ups by user name so far.
        /// </summary>
        public int Lookups { get; private set; }

        /// <summary>
        ///     Gets or sets a value indicating whether a look-up by user name throws.
        /// </summary>
        public bool Fail { get; set; }

        /// <summary>
        ///     Gets or sets an action run during every look-up by user name, for example to move the test clock while
        ///     a count is being read.
        /// </summary>
        public Action? OnLookup { get; set; }

        /// <inheritdoc />
        public Task<User> AddAsync(User user, Func<User, AuditDraft> audit, CancellationToken cancellationToken = default)
        {
            return inner.AddAsync(user, audit, cancellationToken);
        }

        /// <inheritdoc />
        public Task<User> AddToProjectAsync(
            User user,
            int projectId,
            ProjectRole role,
            Func<User, AuditDraft> audit,
            CancellationToken cancellationToken = default)
        {
            return inner.AddToProjectAsync(user, projectId, role, audit, cancellationToken);
        }

        /// <inheritdoc />
        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            Lookups++;
            OnLookup?.Invoke();
            if (Fail)
            {
                throw new InvalidOperationException("The user store is not available.");
            }

            return inner.GetByUsernameAsync(username, cancellationToken);
        }

        /// <inheritdoc />
        public Task<User?> GetByExternalAsync(string issuer, string subject, CancellationToken cancellationToken = default)
        {
            return inner.GetByExternalAsync(issuer, subject, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
        {
            return inner.ListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task UpdateAsync(User user, AuditDraft? audit, CancellationToken cancellationToken = default)
        {
            return inner.UpdateAsync(user, audit, cancellationToken);
        }
    }
}
