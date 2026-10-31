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

using System.Net;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Identity;

/// <summary>
///     Unit tests for <see cref="SignIn"/>: the login and logout endpoints, the session cookie, the return-URL
///     check and the access rules, exercised over the in-memory application.
/// </summary>
public class SignInTests
{
    /// <summary>
    ///     Test that a successful login redirects to the requested place and sets a session cookie that script
    ///     cannot read and that is not sent along with cross-site requests.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_ValidCredentials_SetsSafeSessionCookieAndRedirects()
    {
        // Arrange: an application with a user
        await using var app = await StartAsync();
        await CreateUserAsync(app, "ann");
        using var browser = new TestBrowser(app);
        var users = app.Services.GetRequiredService<UserService>();
        var failed = await browser.LoginAsync("ann", "not the password at all", "/projects");
        var beforeLogin = (await users.GetAsync("ann", TestContext.Current.CancellationToken))!.LastSignInUtc;

        // Act: log in with a return URL
        var response = await browser.LoginAsync("ann", TestBrowser.Password, "/projects");

        // Assert: the failed attempt stored no sign-in time, the successful login stored one
        Assert.NotEqual("/projects", failed.Headers.Location?.OriginalString);
        Assert.Null(beforeLogin);
        Assert.NotNull((await users.GetAsync("ann", TestContext.Current.CancellationToken))!.LastSignInUtc);

        // Assert: redirected to the return URL, with a session cookie that is HttpOnly, SameSite=Lax and has no expiry
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/projects", response.Headers.Location?.OriginalString);
        var cookie = Assert.Single(AuthCookies(browser));
        Assert.Contains("HttpOnly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that the session cookie is marked Secure when the request came over https.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_OverHttps_CookieIsSecure()
    {
        // Arrange: an application with a user, and a browser that uses https
        await using var app = await StartAsync();
        await CreateUserAsync(app, "ann");
        using var browser = new TestBrowser(app, https: true);

        // Act: log in
        await browser.LoginAsync("ann", TestBrowser.Password);

        // Assert: the session cookie is Secure
        var cookie = Assert.Single(AuthCookies(browser));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that an unknown user, a wrong password, a disabled account and a locked account all get the same
    ///     answer and no session cookie, so the response does not reveal which accounts exist.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_EveryKindOfFailure_GivesTheSameAnswerAndNoCookie()
    {
        // Arrange: an application with a normal, a disabled and a soon-to-be-locked user
        await using var app = await StartAsync();
        await CreateUserAsync(app, "ann");
        await CreateUserAsync(app, "dis");
        await CreateUserAsync(app, "lock");
        var users = app.Services.GetRequiredService<UserService>();
        await users.SetEnabledAsync("dis", false, TestContext.Current.CancellationToken);
        for (var i = 0; i < 5; i++)
        {
            await users.AuthenticateAsync("lock", "wrong password here", TestContext.Current.CancellationToken);
        }

        // Act: fail in the four ways
        var attempts = new[]
        {
            ("nobody", TestBrowser.Password),
            ("ann", "wrong password here"),
            ("dis", TestBrowser.Password),
            ("lock", TestBrowser.Password)
        };
        var locations = new List<string?>();
        foreach (var (username, password) in attempts)
        {
            using var browser = new TestBrowser(app);
            var response = await browser.LoginAsync(username, password);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Empty(AuthCookies(browser));
            locations.Add(response.Headers.Location?.OriginalString);
        }

        // Assert: one and the same redirect back to the login page
        Assert.All(locations, location => Assert.Equal("/login?error=1", location));
    }

    /// <summary>
    ///     Test that a client over the per-name limit gets the same "too many attempts" answer for a known and for an
    ///     unknown user name, so the answer does not reveal which accounts exist.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_TooManyAttempts_RedirectsToThrottledAnswerIdenticalForKnownAndUnknownNames()
    {
        // Arrange: a limit of two attempts per name, and one known user
        await using var app = await StartAsync(perName: 2);
        await CreateUserAsync(app, "ann");
        using var browser = new TestBrowser(app);
        var locations = new Dictionary<string, List<string?>> { ["ann"] = [], ["nobody"] = [] };

        // Act: three wrong attempts for the known and for the unknown name
        foreach (var name in locations.Keys)
        {
            for (var i = 0; i < 3; i++)
            {
                var response = await browser.LoginAsync(name, "wrong password here", "/account");
                locations[name].Add(response.Headers.Location?.OriginalString);
            }
        }

        // Assert: both names get the generic answer twice, then the same throttled answer
        var generic = $"/login?error=1&ReturnUrl={Uri.EscapeDataString("/account")}";
        var expected = new[] { generic, generic, $"/login?error=throttled&ReturnUrl={Uri.EscapeDataString("/account")}" };
        Assert.Equal(expected, locations["ann"]);
        Assert.Equal(expected, locations["nobody"]);
    }

    /// <summary>
    ///     Test that a throttled client is refused even with the right password and gets no session cookie.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_Throttled_RefusesEvenTheRightPasswordAndSetsNoCookie()
    {
        // Arrange: a limit of two attempts per name, both used up
        await using var app = await StartAsync(perName: 2);
        await CreateUserAsync(app, "ann");
        using var browser = new TestBrowser(app);
        await browser.LoginAsync("ann", "wrong password here");
        await browser.LoginAsync("ann", "wrong password here");

        // Act: the right password
        var response = await browser.LoginAsync("ann", TestBrowser.Password);

        // Assert: refused as throttled, nobody is signed in
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?error=throttled", response.Headers.Location?.OriginalString);
        Assert.Empty(AuthCookies(browser));
    }

    /// <summary>
    ///     Test that a successful sign-in gives the attempts of the name back.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_SuccessAfterFailures_ResetsTheNameCounter()
    {
        // Arrange: a limit of three attempts per name; two wrong attempts, then the right password
        await using var app = await StartAsync(perName: 3);
        await CreateUserAsync(app, "ann");
        using var browser = new TestBrowser(app);
        await browser.LoginAsync("ann", "wrong password here");
        await browser.LoginAsync("ann", "wrong password here");
        var success = await browser.LoginAsync("ann", TestBrowser.Password);

        // Act: three more wrong attempts, then one more
        var locations = new List<string?>();
        for (var i = 0; i < 4; i++)
        {
            locations.Add((await browser.LoginAsync("ann", "wrong password here")).Headers.Location?.OriginalString);
        }

        // Assert: the success was not throttled, and the count started again after it
        Assert.Equal("/", success.Headers.Location?.OriginalString);
        Assert.Equal(["/login?error=1", "/login?error=1", "/login?error=1", "/login?error=throttled"], locations);
    }

    /// <summary>
    ///     Test that a failed login keeps a safe return URL for the next attempt.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_WrongPasswordWithReturnUrl_KeepsReturnUrlOnLoginPage()
    {
        // Arrange: an application with a user
        await using var app = await StartAsync();
        await CreateUserAsync(app, "ann");
        using var browser = new TestBrowser(app);

        // Act: fail to log in with a return URL
        var response = await browser.LoginAsync("ann", "wrong password here", "/projects/1");

        // Assert: the login page is asked for again with the return URL
        Assert.Equal(
            $"/login?error=1&ReturnUrl={Uri.EscapeDataString("/projects/1")}",
            response.Headers.Location?.OriginalString);
    }

    /// <summary>
    ///     Test that only a site-relative path is accepted as the place to go after login.
    /// </summary>
    /// <param name="returnUrl">The requested return URL.</param>
    /// <param name="expected">The URL that is used.</param>
    [Theory]
    [InlineData("/projects/1", "/projects/1")]
    [InlineData("/projects?x=1", "/projects?x=1")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example/", "/")]
    [InlineData("javascript:alert(1)", "/")]
    [InlineData("projects", "/")]
    [InlineData("/projects\r\nX: y", "/")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    public void SignIn_SafeReturnUrl_AnyInput_KeepsOnlyLocalPaths(string? returnUrl, string expected)
    {
        // Arrange: (none)

        // Act: reduce the URL
        var result = SignIn.SafeReturnUrl(returnUrl);

        // Assert: local paths are kept, everything else becomes the home page
        Assert.Equal(expected, result);
    }

    /// <summary>
    ///     Test that a login with a hostile return URL goes to the home page.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_HostileReturnUrl_RedirectsToHomePage()
    {
        // Arrange: an application with a user
        await using var app = await StartAsync();
        await CreateUserAsync(app, "ann");
        using var browser = new TestBrowser(app);

        // Act: log in with a return URL that points to another site
        var response = await browser.LoginAsync("ann", TestBrowser.Password, "https://evil.example/");

        // Assert: redirected to the home page of this site
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    /// <summary>
    ///     Test that a login or logout post without the antiforgery token is refused.
    /// </summary>
    [Fact]
    public async Task SignIn_PostWithoutAntiforgeryToken_IsRefusedAndSignsNobodyIn()
    {
        // Arrange: an application with a user
        await using var app = await StartAsync();
        await CreateUserAsync(app, "ann");
        using var browser = new TestBrowser(app);

        // Act: post the right credentials without a token, and a logout without a token
        var login = await browser.PostFormAsync(
            SignIn.LoginEndpoint,
            [new("username", "ann"), new("password", TestBrowser.Password)],
            withToken: false);
        var logout = await browser.PostFormAsync(SignIn.LogoutEndpoint, [], withToken: false);

        // Assert: both are refused and no session cookie was issued
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.Empty(AuthCookies(browser));
    }

    /// <summary>
    ///     Test that the protected pages send an anonymous visitor to the login page, and that the public pages
    ///     are open.
    /// </summary>
    /// <param name="route">The route to request.</param>
    [Theory]
    [InlineData("/projects")]
    [InlineData("/projects/1")]
    [InlineData("/projects/1/reviews/1")]
    [InlineData("/projects/1/reviews/1/checklist")]
    [InlineData("/account")]
    [InlineData("/users")]
    [InlineData("/projects/1/repositories")]
    public async Task SignIn_ProtectedPage_Anonymous_RedirectsToLogin(string route)
    {
        // Arrange: an application and a visitor who is not signed in
        await using var app = await StartAsync();
        using var browser = new TestBrowser(app);

        // Act: request the page
        var response = await browser.Client.GetAsync(route, TestContext.Current.CancellationToken);

        // Assert: redirected to the login page, which remembers where the visitor wanted to go
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString();
        Assert.Contains("/login?ReturnUrl=", location, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString(route), location, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the home page, the login page and the health probe stay open to anonymous visitors.
    /// </summary>
    /// <param name="route">The route to request.</param>
    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData(ServerHost.HealthPath)]
    public async Task SignIn_PublicPage_Anonymous_IsServed(string route)
    {
        // Arrange: an application and a visitor who is not signed in
        await using var app = await StartAsync();
        using var browser = new TestBrowser(app);

        // Act: request the page
        var response = await browser.Client.GetAsync(route, TestContext.Current.CancellationToken);

        // Assert: served
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    ///     Test that the user administration page is for administrators only.
    /// </summary>
    [Fact]
    public async Task SignIn_UsersPage_ReviewerAndAdmin_OnlyAdminIsAllowed()
    {
        // Arrange: an application with a reviewer and an administrator, each signed in
        await using var app = await StartAsync();
        using var reviewer = await TestBrowser.SignedInAsync(app, "rev", UserRole.Reviewer);
        using var admin = await TestBrowser.SignedInAsync(app, "adm", UserRole.Admin);

        // Act: both request the page
        var forReviewer = await reviewer.Client.GetAsync("/users", TestContext.Current.CancellationToken);
        var forAdmin = await admin.Client.GetAsync("/users", TestContext.Current.CancellationToken);

        // Assert: the reviewer is forbidden with an explanation, the administrator is served
        Assert.Equal(HttpStatusCode.Forbidden, forReviewer.StatusCode);
        Assert.Contains("Access denied", await forReviewer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, forAdmin.StatusCode);
    }

    /// <summary>
    ///     Test that a project's repository page is for its administrators: a system administrator is served and a
    ///     user who is no member of the project cannot even tell that the project exists.
    /// </summary>
    [Fact]
    public async Task SignIn_RepositoriesPage_NonMemberAndSystemAdmin_OnlyAdminIsServed()
    {
        // Arrange: an application with a project, a user outside it and a system administrator, each signed in
        await using var app = await StartAsync();
        using var reviewer = await TestBrowser.SignedInAsync(app, "rev", UserRole.Reviewer);
        using var admin = await TestBrowser.SignedInAsync(app, "adm", UserRole.Admin);
        var project = await TestWebApp.CreateProjectAsync(app, "Apollo");
        var route = $"/projects/{project.Id}/repositories";

        // Act: both request the page
        var forReviewer = await reviewer.Client.GetAsync(route, TestContext.Current.CancellationToken);
        var forAdmin = await admin.Client.GetAsync(route, TestContext.Current.CancellationToken);

        // Assert: the non-member gets the answer for a missing project, the administrator is served
        Assert.Contains("does not exist", await forReviewer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.DoesNotContain("does not exist", await forAdmin.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, forAdmin.StatusCode);
    }

    /// <summary>
    ///     Test that logging out ends the session.
    /// </summary>
    [Fact]
    public async Task SignIn_Logout_SignedInUser_EndsTheSession()
    {
        // Arrange: a signed-in user who can see the projects page
        await using var app = await StartAsync();
        using var browser = await TestBrowser.SignedInAsync(app, "ann");
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/projects", TestContext.Current.CancellationToken)).StatusCode);

        // Act: log out and request the page again
        var logout = await browser.PostFormAsync(SignIn.LogoutEndpoint, []);
        var after = await browser.Client.GetAsync("/projects", TestContext.Current.CancellationToken);

        // Assert: redirected home, and the page is protected again
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/", logout.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
    }

    /// <summary>
    ///     Test that a session cookie stops working when the account is disabled.
    /// </summary>
    [Fact]
    public async Task SignIn_Session_UserDisabledAfterLogin_IsRejected()
    {
        // Arrange: a signed-in user
        await using var app = await StartAsync();
        using var browser = await TestBrowser.SignedInAsync(app, "ann", UserRole.Reviewer);
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/projects", TestContext.Current.CancellationToken)).StatusCode);

        // Act: disable the account, then use the old cookie
        await app.Services.GetRequiredService<UserService>().SetEnabledAsync("ann", false, TestContext.Current.CancellationToken);
        var response = await browser.Client.GetAsync("/projects", TestContext.Current.CancellationToken);

        // Assert: the visitor has to sign in again
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    /// <summary>
    ///     Test that a session cookie stops working when the role of the account changes, so the user signs in
    ///     again and gets the new role.
    /// </summary>
    [Fact]
    public async Task SignIn_Session_RoleChangedAfterLogin_IsRejected()
    {
        // Arrange: a signed-in administrator, and a second administrator so the first may be demoted
        await using var app = await StartAsync();
        using var browser = await TestBrowser.SignedInAsync(app, "adm", UserRole.Admin);
        await CreateUserAsync(app, "other", UserRole.Admin);

        // Act: demote the first one, then use the old cookie
        await app.Services.GetRequiredService<UserService>().SetRoleAsync("adm", UserRole.Reviewer, TestContext.Current.CancellationToken);
        var response = await browser.Client.GetAsync("/users", TestContext.Current.CancellationToken);

        // Assert: the old session does not carry the old role along
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    /// <summary>
    ///     Test that the actor is built from the claims of the signed-in user, and that nobody signed in is a
    ///     rule violation the page can show.
    /// </summary>
    [Fact]
    public async Task SignIn_GetActorAsync_SignedInAndAnonymous_ReturnsActorOrThrows()
    {
        // Arrange: a provider with a signed-in user and one with nobody
        var identity = new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "ann"),
                new System.Security.Claims.Claim(SignIn.DisplayNameClaimType, "Ann")
            ],
            "test");
        var signedIn = new FixedAuthenticationStateProvider(new System.Security.Claims.ClaimsPrincipal(identity));
        var anonymous = new FixedAuthenticationStateProvider(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()));

        // Act: get the actors
        var actor = await signedIn.GetActorAsync();
        var exception = await Record.ExceptionAsync(() => anonymous.GetActorAsync());

        // Assert: the actor carries the user name and display name; anonymous is refused with a rule violation
        // (not a fault) so that a page shows the message instead of failing
        Assert.Equal(new Actor("ann", "Ann"), actor);
        var violation = Assert.IsType<DomainRuleException>(exception);
        Assert.Equal(DomainRule.ActorNotAllowed, violation.Rule);
        Assert.Contains("no longer signed in", violation.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the shared rule accepts a principal only when the stored user is enabled and the system
    ///     administrator claim and display name are unchanged.
    /// </summary>
    [Theory]
    [InlineData(true, false, "Ann", false, "Ann", true)]
    [InlineData(true, true, "Ann", true, "Ann", true)]
    [InlineData(false, false, "Ann", false, "Ann", false)]
    [InlineData(true, true, "Ann", false, "Ann", false)]
    [InlineData(true, false, "Ann", true, "Ann", false)]
    [InlineData(true, false, "Ann B.", false, "Ann", false)]
    public void SignIn_IsCurrent_StoredUserVersusPrincipal_FollowsTheRule(
        bool enabled, bool storedAdmin, string storedName, bool claimAdmin, string claimName, bool expected)
    {
        // Arrange: a stored user and a principal issued with the given claims
        var user = new User { Id = 1, Username = "ann", DisplayName = storedName, IsSystemAdmin = storedAdmin, Enabled = enabled };
        var principal = PrincipalWith(claimAdmin, claimName);

        // Act: apply the rule
        var current = SignIn.IsCurrent(principal, user);

        // Assert: only an enabled, unchanged user is current
        Assert.Equal(expected, current);
    }

    /// <summary>
    ///     Test that the shared rule refuses a missing principal, a missing user, and a principal without the
    ///     display name claim.
    /// </summary>
    [Fact]
    public void SignIn_IsCurrent_MissingPrincipalUserOrClaims_ReturnsFalse()
    {
        // Arrange: a valid stored user, and a principal that lacks the display name
        var user = new User { Id = 1, Username = "ann", DisplayName = "Ann", Enabled = true };
        var noName = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, SignIn.AdminRole)], "test"));

        // Act and assert: each is refused
        Assert.False(SignIn.IsCurrent(null, user));
        Assert.False(SignIn.IsCurrent(PrincipalWith(false, "Ann"), null));
        Assert.False(SignIn.IsCurrent(noName, user));
    }

    /// <summary>
    ///     Test that a flagged user (temporary password) is signed in without the system administrator role, is sent
    ///     to the account page whatever the return URL says, and that the session stops working once the flag
    ///     changes after the session was issued.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_TemporaryPassword_GoesToAccountPageWithoutAdminRoleAndStaleWhenFlagChanges()
    {
        // Arrange: a system administrator who has to change the password
        await using var app = await StartAsync();
        await CreateUserAsync(app, "ann", UserRole.Admin);
        var cancellationToken = TestContext.Current.CancellationToken;
        var userStore = app.Services.GetRequiredService<IUserStore>();
        var ann = (await userStore.GetByUsernameAsync("ann", cancellationToken))!;
        ann.MustChangePassword = true;
        await userStore.UpdateAsync(ann, null, cancellationToken);
        using var browser = new TestBrowser(app);

        // Act: log in with a return URL, then ask for a page that needs the administrator role
        var login = await browser.LoginAsync("ann", TestBrowser.Password, "/projects");
        var cookies = AuthCookies(browser);
        var users = await browser.Client.GetAsync("/users", cancellationToken);
        ann = (await userStore.GetByUsernameAsync("ann", cancellationToken))!;
        ann.MustChangePassword = false;
        await userStore.UpdateAsync(ann, null, cancellationToken);
        var stale = await browser.Client.GetAsync("/projects", cancellationToken);

        // Assert: the account page is the destination, the role is not granted, the old session is stale
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(SignIn.AccountPath, login.Headers.Location?.OriginalString);
        Assert.Single(cookies);
        Assert.NotEqual(HttpStatusCode.OK, users.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, stale.StatusCode);
    }

    /// <summary>
    ///     Test that the shared rule ties the temporary-password claim to the stored flag in both directions, and
    ///     that a flagged system administrator is current only without the administrator claim.
    /// </summary>
    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    public void SignIn_IsCurrent_TemporaryPasswordFlag_MustMatchTheClaim(
        bool flagged, bool storedAdmin, bool claimFlag, bool expected)
    {
        // Arrange: a stored user, and a principal that carries the flag claim and never the admin claim when flagged
        var user = new User
        {
            Id = 1,
            Username = "ann",
            DisplayName = "Ann",
            Enabled = true,
            IsSystemAdmin = storedAdmin,
            MustChangePassword = flagged
        };
        List<System.Security.Claims.Claim> claims =
        [
            new(System.Security.Claims.ClaimTypes.Name, "ann"),
            new(SignIn.DisplayNameClaimType, "Ann")
        ];
        if (claimFlag)
        {
            claims.Add(new(SignIn.MustChangePasswordClaimType, "true"));
        }

        if (storedAdmin && !flagged)
        {
            claims.Add(new(System.Security.Claims.ClaimTypes.Role, SignIn.AdminRole));
        }

        var principal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(claims, "test"));

        // Act
        var current = SignIn.IsCurrent(principal, user);

        // Assert
        Assert.Equal(expected, current);
        Assert.Equal(claimFlag, SignIn.IsPasswordChangeRequired(principal));
    }

    /// <summary>
    ///     Test that the check interval of open pages defaults to 30 seconds and that its minimum is 5.
    /// </summary>
    [Fact]
    public void AuthOptions_RevalidationSeconds_Default_IsThirtyWithMinimumFive()
    {
        // Arrange: default settings
        var options = new AuthOptions();

        // Act and assert: the default and the minimum
        Assert.Equal(30, options.RevalidationSeconds);
        Assert.Equal(5, AuthOptions.MinimumRevalidationSeconds);
    }

    /// <summary>
    ///     Test that the registration refuses null arguments.
    /// </summary>
    [Fact]
    public void SignIn_AddSignIn_NullArguments_Throw()
    {
        // Arrange: (none)

        // Act and assert: null arguments are refused
        Assert.Throws<ArgumentNullException>(() => SignIn.AddSignIn(null!, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));
        Assert.Throws<ArgumentNullException>(() => SignIn.AddSignIn(new ServiceCollection(), null!));
    }

    /// <summary>
    ///     Test that the login page shows the failure message only after a failed attempt, and carries only a
    ///     safe return URL in its form.
    /// </summary>
    [Fact]
    public async Task SignIn_LoginPage_ErrorAndReturnUrl_AreShownSafely()
    {
        // Arrange: an application and a visitor who is not signed in
        await using var app = await StartAsync();
        using var browser = new TestBrowser(app);

        // Act: request the login page plainly, after a failure with a local return URL, and with a hostile one
        var plain = await browser.GetPageAsync("/login");
        var failed = await browser.GetPageAsync($"/login?error=1&ReturnUrl={Uri.EscapeDataString("/projects/1")}");
        var hostile = await browser.GetPageAsync($"/login?ReturnUrl={Uri.EscapeDataString("https://evil.example/")}");

        // Assert: the controls exist, the error shows only after a failure, and only the local URL is carried on
        Assert.NotNull(plain.QuerySelector("[data-testid='login-username-input']"));
        Assert.NotNull(plain.QuerySelector("[data-testid='login-password-input']"));
        Assert.NotNull(plain.QuerySelector("[data-testid='login-button']"));
        Assert.Null(plain.QuerySelector("[data-testid='login-error']"));
        Assert.Equal("Invalid user name or password.", failed.QuerySelector("[data-testid='login-error']")?.TextContent);
        Assert.Equal("/projects/1", failed.QuerySelector("input[name='returnUrl']")?.GetAttribute("value"));
        Assert.Equal("/", hostile.QuerySelector("input[name='returnUrl']")?.GetAttribute("value"));
    }

    /// <summary>
    ///     Test that the login page shows the "too many attempts" message, instead of the generic one, only for the
    ///     throttled error.
    /// </summary>
    [Fact]
    public async Task SignIn_LoginPage_ThrottledError_ShowsTooManyAttemptsMessage()
    {
        // Arrange: an application and a visitor who is not signed in
        await using var app = await StartAsync();
        using var browser = new TestBrowser(app);

        // Act: request the login page after a throttled attempt
        var throttled = await browser.GetPageAsync("/login?error=throttled");

        // Assert: the throttle message replaces the generic one
        Assert.Equal("Too many sign-in attempts. Try again in a few minutes.", throttled.QuerySelector("[data-testid='login-throttled']")?.TextContent);
        Assert.Null(throttled.QuerySelector("[data-testid='login-error']"));
    }

    /// <summary>
    ///     Builds a principal with the claims the application issues, for the given administrator flag and display
    ///     name.
    /// </summary>
    /// <param name="admin">Whether the principal carries the system administrator claim.</param>
    /// <param name="displayName">The display name claim value.</param>
    /// <returns>The principal.</returns>
    private static System.Security.Claims.ClaimsPrincipal PrincipalWith(bool admin, string displayName)
    {
        List<System.Security.Claims.Claim> claims =
        [
            new(System.Security.Claims.ClaimTypes.Name, "ann"),
            new(SignIn.DisplayNameClaimType, displayName)
        ];
        if (admin)
        {
            claims.Add(new(System.Security.Claims.ClaimTypes.Role, SignIn.AdminRole));
        }

        return new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test"));
    }

    /// <summary>
    ///     Starts an in-memory application with its own temporary database.
    /// </summary>
    /// <param name="perName">The throttle limit per name; <c>null</c> for the default.</param>
    /// <returns>The started application; the caller disposes it.</returns>
    private static async Task<WebApplication> StartAsync(int? perName = null)
    {
        var settings = perName is null
            ? null
            : new Dictionary<string, string?>
            {
                ["Identity:Throttle:MaxFailuresPerName"] = perName.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Identity:Throttle:MaxFailuresPerAddress"] = "1000"
            };
        return await TestWebApp.StartAsync(new CapturingLoggerProvider(), null, ["serve", "--silent"], settings);
    }

    /// <summary>
    ///     Creates a user with the standard test password.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="username">The user name.</param>
    /// <param name="role">The role.</param>
    /// <returns>A task that completes when the user exists.</returns>
    private static async Task CreateUserAsync(WebApplication app, string username, UserRole role = UserRole.Reviewer)
    {
        await app.Services.GetRequiredService<UserService>()
            .CreateAsync(username, username, role, TestBrowser.Password, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Gets the <c>Set-Cookie</c> values that set the session cookie.
    /// </summary>
    /// <param name="browser">The browser.</param>
    /// <returns>The values; empty when no session cookie was issued.</returns>
    private static List<string> AuthCookies(TestBrowser browser)
    {
        return browser.SetCookieHeaders
            .Where(h => h.StartsWith(SignIn.CookieName + "=", StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    ///     An authentication state provider that always returns one principal.
    /// </summary>
    /// <param name="principal">The principal to return.</param>
    private sealed class FixedAuthenticationStateProvider(System.Security.Claims.ClaimsPrincipal principal)
        : Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider
    {
        /// <inheritdoc />
        public override Task<Microsoft.AspNetCore.Components.Authorization.AuthenticationState> GetAuthenticationStateAsync()
        {
            return Task.FromResult(new Microsoft.AspNetCore.Components.Authorization.AuthenticationState(principal));
        }
    }
}
