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
}
