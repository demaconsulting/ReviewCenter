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
using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="TotpLoginPage"/> component (the second step of signing in) and the notice
///     the <see cref="LoginPage"/> shows when that step expired, rendered in memory with bUnit.
/// </summary>
public class TotpLoginPageTests : PageTestBase
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Test that the page with a pending sign-in shows the code form that posts to the endpoint, with the
    ///     antiforgery field, a link back to sign in, no user name, and no error.
    /// </summary>
    [Fact]
    public async Task TotpLoginPage_Render_PendingSignIn_ShowsCodeFormAndNoUserName()
    {
        // Arrange: nobody is signed in, and the password step of "ann" was accepted
        SignOut();
        var context = await PendingSignInAsync();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/login/totp");

        // Act: render the page
        var cut = Render<TotpLoginPage>(parameters => parameters.AddCascadingValue(context));

        // Assert: the form, its code field, the link back, and nothing that names the user or shows an error
        var form = cut.Find("[data-testid='totp-login-form']");
        Assert.Equal("post", form.GetAttribute("method"));
        Assert.Equal("/auth/login-totp", form.GetAttribute("action"));
        Assert.Equal("code", cut.Find("[data-testid='totp-code-input']").GetAttribute("name"));
        Assert.Equal("one-time-code", cut.Find("[data-testid='totp-code-input']").GetAttribute("autocomplete"));
        Assert.Equal("/login", cut.Find("[data-testid='totp-login-cancel']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid='totp-login-error']"));
        Assert.Empty(cut.FindAll("[data-testid='totp-login-throttled']"));
        Assert.DoesNotContain("ann", cut.Find("main").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("/login/totp", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a failed attempt shows the same generic message whatever the value of the flag, and a
    ///     throttled one shows the throttle message instead.
    /// </summary>
    /// <param name="query">The query string of the address.</param>
    /// <param name="expectedTestId">The test id of the message that must be shown.</param>
    /// <param name="absentTestId">The test id of the message that must not be shown.</param>
    [Theory]
    [InlineData("?error=1", "totp-login-error", "totp-login-throttled")]
    [InlineData("?error=anything", "totp-login-error", "totp-login-throttled")]
    [InlineData("?error=throttled", "totp-login-throttled", "totp-login-error")]
    public async Task TotpLoginPage_Render_ErrorFlag_ShowsMatchingMessage(string query, string expectedTestId, string absentTestId)
    {
        // Arrange: a pending sign-in and an address with the flag
        SignOut();
        var context = await PendingSignInAsync();
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/login/totp{query}");

        // Act: render the page
        var cut = Render<TotpLoginPage>(parameters => parameters.AddCascadingValue(context));

        // Assert: only the matching message is shown, and it does not say why the code failed
        Assert.NotNull(cut.Find($"[data-testid='{expectedTestId}']"));
        Assert.Empty(cut.FindAll($"[data-testid='{absentTestId}']"));
        Assert.DoesNotContain("recovery code was", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that the page sends the user back to the sign-in page, with the expired flag, when there is no
    ///     cookie or the cookie names no pending sign-in.
    /// </summary>
    /// <param name="cookie">The value of the cookie; <c>null</c> for none.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("not-a-real-challenge")]
    public void TotpLoginPage_Render_NoValidChallenge_RedirectsToLoginWithExpiredFlag(string? cookie)
    {
        // Arrange: nobody is signed in, and the request carries no valid challenge
        SignOut();
        var context = ContextFrom(cookie);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/login/totp");

        // Act: render the page
        Render<TotpLoginPage>(parameters => parameters.AddCascadingValue(context));

        // Assert: the browser is sent to the sign-in page with the expired flag
        Assert.EndsWith("/login?error=totp-expired", navigation.Uri, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a pending sign-in is not accepted from another address.
    /// </summary>
    [Fact]
    public async Task TotpLoginPage_Render_ChallengeFromOtherAddress_RedirectsToLogin()
    {
        // Arrange: the password step was accepted from one address and the page is requested from another
        SignOut();
        var context = await PendingSignInAsync();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/login/totp");

        // Act: render the page
        Render<TotpLoginPage>(parameters => parameters.AddCascadingValue(context));

        // Assert: the browser is sent to the sign-in page with the expired flag
        Assert.EndsWith("/login?error=totp-expired", navigation.Uri, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the sign-in page shows a notice when the second step expired, and not the invalid password
    ///     message.
    /// </summary>
    [Fact]
    public void LoginPage_Render_TotpExpired_ShowsNoticeAndNoPasswordError()
    {
        // Arrange: nobody is signed in and the address carries the expired flag
        SignOut();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/login?error=totp-expired");

        // Act: render the page
        var cut = Render<LoginPage>();

        // Assert: the notice asks to sign in again, and the failed-password message is not shown
        Assert.Contains("Sign in again", cut.Find("[data-testid='login-totp-expired']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='login-error']"));
        Assert.Empty(cut.FindAll("[data-testid='login-throttled']"));
    }

    /// <summary>
    ///     Test that the sign-in page shows no notice without the flag, and the password message for a failure.
    /// </summary>
    [Fact]
    public void LoginPage_Render_PasswordFailure_ShowsNoExpiredNotice()
    {
        // Arrange: nobody is signed in and the address carries the generic failure flag
        SignOut();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/login?error=1");

        // Act: render the page
        var cut = Render<LoginPage>();

        // Assert: the password message is shown and the expired notice is not
        Assert.NotNull(cut.Find("[data-testid='login-error']"));
        Assert.Empty(cut.FindAll("[data-testid='login-totp-expired']"));
    }

    /// <summary>
    ///     Creates a user, starts a pending sign-in for it, and returns the request that carries its cookie.
    /// </summary>
    /// <returns>A request from a fixed address that carries the cookie of the pending sign-in.</returns>
    private async Task<DefaultHttpContext> PendingSignInAsync()
    {
        var user = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var address = SignIn.ClientAddress(ContextFrom(null));
        var token = Totp.BeginSignIn(user, address, "/projects");
        return ContextFrom(token);
    }

    /// <summary>
    ///     Creates a request from a fixed address.
    /// </summary>
    /// <param name="cookie">The value of the challenge cookie; <c>null</c> for none.</param>
    /// <returns>The request.</returns>
    private static DefaultHttpContext ContextFrom(string? cookie)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.7");
        if (cookie is not null)
        {
            context.Request.Headers.Cookie = $"{TotpSignIn.CookieName}={cookie}";
        }

        return context;
    }
}
