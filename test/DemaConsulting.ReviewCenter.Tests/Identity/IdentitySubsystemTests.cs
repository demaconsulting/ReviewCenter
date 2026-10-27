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
using System.Security.Claims;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Identity;

/// <summary>
///     Subsystem tests for the Identity subsystem: the password hasher and the cookie sign-in working together
///     with the user service of the Core subsystem inside the real application pipeline.
/// </summary>
public class IdentitySubsystemTests
{
    /// <summary>
    ///     Test the whole sign-in journey: a protected page is refused, the login form signs the user in and
    ///     returns to the page, the page shows who is signed in, and logging out closes the door again. The
    ///     stored password is a hash, not the password.
    /// </summary>
    [Fact]
    public async Task IdentitySubsystem_SignInJourney_LoginUseAndLogout_WorksEndToEnd()
    {
        // Arrange: an application with one user created through the Core service
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        var users = app.Services.GetRequiredService<UserService>();
        await users.CreateAsync("ann", "Ann Example", UserRole.Reviewer, TestBrowser.Password, TestContext.Current.CancellationToken);
        using var browser = new TestBrowser(app);

        // Act: visit a protected page, log in with the form, visit it again, then log out
        var refused = await browser.Client.GetAsync("/projects", TestContext.Current.CancellationToken);
        var login = await browser.LoginAsync("ann", TestBrowser.Password, "/projects");
        var page = await browser.GetPageAsync("/projects");
        var logout = await browser.PostFormAsync("/auth/logout", []);
        var closed = await browser.Client.GetAsync("/projects", TestContext.Current.CancellationToken);

        // Assert: each step did what a user expects
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        Assert.Equal("/projects", login.Headers.Location?.OriginalString);
        Assert.Equal("Ann Example", page.QuerySelector("[data-testid='current-user']")?.TextContent);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, closed.StatusCode);
        var stored = await users.GetAsync("ann", TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.DoesNotContain(TestBrowser.Password, stored.PasswordHash, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the sign-in state of an open page (a circuit) is checked against the real user store: the
    ///     provider that a circuit resolves is ours (not the framework default), keeps a valid user, and ends
    ///     the session of a user who was disabled after signing in.
    /// </summary>
    [Fact]
    public async Task IdentitySubsystem_Revalidation_UserDisabledAfterSignIn_OpenSessionIsNoLongerValid()
    {
        // Arrange: the real pipeline with one user, and the provider of a new scope, as a circuit gets it
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        var users = app.Services.GetRequiredService<UserService>();
        await users.CreateAsync("ann", "Ann Example", UserRole.Reviewer, TestBrowser.Password, TestContext.Current.CancellationToken);
        using var scope = app.Services.CreateScope();
        var resolved = scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
        var provider = Assert.IsType<RevalidatingAuthenticationStateProvider>(resolved);
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "ann"),
                new Claim(SignIn.DisplayNameClaimType, "Ann Example"),
                new Claim(ClaimTypes.Role, nameof(UserRole.Reviewer))
            ],
            "Test");
        provider.SetAuthenticationState(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity))));

        // Act: check while the user is valid, disable the user through the real service, and check again
        var state = await provider.GetAuthenticationStateAsync();
        var before = await provider.IsCurrentAsync(state, TestContext.Current.CancellationToken);
        await users.SetEnabledAsync("ann", false, TestContext.Current.CancellationToken);
        var after = await provider.IsCurrentAsync(state, TestContext.Current.CancellationToken);

        // Assert: valid before, no longer valid after
        Assert.True(before);
        Assert.False(after);
    }

    /// <summary>
    ///     Test that the throttle, the user service and the audit trail work together: repeated wrong passwords
    ///     for one name are refused with the throttle answer, the right password is then refused too, and the
    ///     trail has one failure entry and one throttled entry without the passwords.
    /// </summary>
    [Fact]
    public async Task IdentitySubsystem_Throttle_RepeatedFailures_AreRefusedAndAuditedOncePerWindow()
    {
        // Arrange: an application with a limit of three attempts per name and one user
        await using var app = await TestWebApp.StartAsync(
            new CapturingLoggerProvider(),
            null,
            ["serve", "--silent"],
            new Dictionary<string, string?>
            {
                ["Identity:Throttle:MaxFailuresPerName"] = "3",
                ["Identity:Throttle:MaxFailuresPerAddress"] = "50"
            });
        var users = app.Services.GetRequiredService<UserService>();
        await users.CreateAsync("ann", "Ann Example", UserRole.Reviewer, TestBrowser.Password, TestContext.Current.CancellationToken);
        using var browser = new TestBrowser(app);

        // Act: three wrong attempts, then two more with the right password
        var wrong = new List<string?>();
        for (var i = 0; i < 3; i++)
        {
            wrong.Add((await browser.LoginAsync("ann", "wrong-attempt-" + i)).Headers.Location?.OriginalString);
        }

        var refused = await browser.LoginAsync("ann", TestBrowser.Password);
        await browser.LoginAsync("ann", TestBrowser.Password);
        var page = await browser.GetPageAsync("/login?error=throttled");

        // Assert: generic answers first, then the throttle answer; the page says so; the trail has one entry each
        Assert.All(wrong, location => Assert.Equal("/login?error=1", location));
        Assert.Equal("/login?error=throttled", refused.Headers.Location?.OriginalString);
        Assert.NotNull(page.QuerySelector("[data-testid='login-throttled']"));
        var audit = app.Services.GetRequiredService<AuditService>();
        var entries = (await audit.ListAsync(null, null, null, null, TestContext.Current.CancellationToken)).Entries;
        Assert.Equal(3, entries.Count(e => e.Action == "auth.sign-in-failed"));
        Assert.Single(entries, e => e.Action == "auth.sign-in-throttled");
        Assert.DoesNotContain(entries, e => e.Detail.Contains("wrong-attempt", StringComparison.Ordinal));
    }
}
