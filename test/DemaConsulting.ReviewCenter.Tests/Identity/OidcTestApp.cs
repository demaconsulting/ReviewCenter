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
using DemaConsulting.ReviewCenter.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Identity;

/// <summary>
///     Starts the real application with sign-in through the fake identity provider switched on, and walks a
///     <see cref="TestBrowser"/> through the redirects between the application and the provider.
/// </summary>
internal static class OidcTestApp
{
    /// <summary>
    ///     The display name of the provider in the settings of the test application.
    /// </summary>
    public const string DisplayName = "Test IdP";

    /// <summary>
    ///     The pages and endpoints a flow passes through; a redirect to any other place ends the flow.
    /// </summary>
    private static readonly string[] FlowPaths = ["/signin-oidc", "/auth/oidc/complete"];

    /// <summary>
    ///     Starts the application against a database of its own and the fake provider.
    /// </summary>
    /// <param name="idp">The provider the application trusts.</param>
    /// <param name="logs">
    ///     Receives the log of the application; <c>null</c> to capture into a throw-away provider.
    /// </param>
    /// <param name="settings">Settings that replace or add to the defaults of the test; <c>null</c> for none.</param>
    /// <returns>The started application; the caller disposes it.</returns>
    public static async Task<WebApplication> StartAsync(
        FakeIdentityProvider idp,
        CapturingLoggerProvider? logs = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var all = new Dictionary<string, string?>
        {
            ["Identity:Oidc:Enabled"] = "true",
            ["Identity:Oidc:Authority"] = idp.BaseUrl,
            ["Identity:Oidc:ClientId"] = "reviewcenter-test",
            ["Identity:Oidc:ClientSecret"] = "test-client-secret-value",
            ["Identity:Oidc:DisplayName"] = DisplayName
        };
        if (settings is not null)
        {
            foreach (var pair in settings)
            {
                all[pair.Key] = pair.Value;
            }
        }

        return await TestWebApp.StartAsync(
            logs ?? new CapturingLoggerProvider(),
            connectionString: null,
            ["serve", "--silent"],
            all);
    }

    /// <summary>
    ///     Presses the button "Sign in with ..." and follows the browser through the provider back to the application.
    /// </summary>
    /// <param name="browser">The browser; it keeps the cookies of the application.</param>
    /// <param name="idp">The provider.</param>
    /// <param name="returnUrl">The address to come back to; <c>null</c> for none.</param>
    /// <returns>The first redirect that leaves the sign-in flow, or the first answer that is no redirect.</returns>
    public static async Task<HttpResponseMessage> SignInAsync(
        TestBrowser browser,
        FakeIdentityProvider idp,
        string? returnUrl = null)
    {
        List<KeyValuePair<string, string>> fields = [];
        if (returnUrl is not null)
        {
            fields.Add(new("returnUrl", returnUrl));
        }

        var start = await browser.PostFormAsync("/auth/oidc/login", fields);
        return await FollowAsync(browser, idp, start);
    }

    /// <summary>
    ///     Presses the button "Confirm identity" for a slot and follows the browser through the provider back.
    /// </summary>
    /// <param name="browser">The signed-in browser.</param>
    /// <param name="idp">The provider.</param>
    /// <param name="projectId">The project of the review.</param>
    /// <param name="reviewId">The review.</param>
    /// <param name="slotId">The slot.</param>
    /// <param name="returnUrl">The address to come back to.</param>
    /// <returns>The first redirect that leaves the flow, or the first answer that is no redirect.</returns>
    public static async Task<HttpResponseMessage> ReauthAsync(
        TestBrowser browser,
        FakeIdentityProvider idp,
        int projectId,
        string reviewId,
        int slotId,
        string returnUrl)
    {
        var start = await StartReauthAsync(browser, projectId, reviewId, slotId, returnUrl);
        return await FollowAsync(browser, idp, start);
    }

    /// <summary>
    ///     Presses the button "Confirm identity" for a slot, without following the answer.
    /// </summary>
    /// <param name="browser">The signed-in browser.</param>
    /// <param name="projectId">The project of the review.</param>
    /// <param name="reviewId">The review.</param>
    /// <param name="slotId">The slot.</param>
    /// <param name="returnUrl">The address to come back to.</param>
    /// <returns>The answer of the application.</returns>
    public static Task<HttpResponseMessage> StartReauthAsync(
        TestBrowser browser,
        int projectId,
        string reviewId,
        int slotId,
        string returnUrl)
    {
        return browser.PostFormAsync(
            "/auth/oidc/reauth",
            [
                new("projectId", projectId.ToString()),
                new("reviewId", reviewId),
                new("slotId", slotId.ToString()),
                new("returnUrl", returnUrl)
            ]);
    }

    /// <summary>
    ///     Follows redirects like a browser, sending each request to the application or to the provider, until the
    ///     flow leaves the callback and the completion endpoints.
    /// </summary>
    /// <param name="browser">The browser of the application.</param>
    /// <param name="idp">The provider.</param>
    /// <param name="response">The answer to start from.</param>
    /// <returns>The first redirect that leaves the flow, or the first answer that is no redirect.</returns>
    public static async Task<HttpResponseMessage> FollowAsync(
        TestBrowser browser,
        FakeIdentityProvider idp,
        HttpResponseMessage response)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var hop = 0; hop < 10; hop++)
        {
            var location = response.Headers.Location;
            if (response.StatusCode != HttpStatusCode.Redirect || location is null)
            {
                return response;
            }

            if (location.IsAbsoluteUri && location.Authority == new Uri(idp.BaseUrl).Authority)
            {
                response = await idp.Client.GetAsync(location, cancellationToken);
                continue;
            }

            var path = location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0];
            if (!FlowPaths.Contains(path, StringComparer.Ordinal))
            {
                return response;
            }

            response = await browser.Client.GetAsync(location, cancellationToken);
        }

        throw new InvalidOperationException("The sign-in flow did not end after ten redirects.");
    }

    /// <summary>
    ///     Gets the <c>Set-Cookie</c> values that set the session cookie.
    /// </summary>
    /// <param name="browser">The browser.</param>
    /// <returns>The values; empty when no session cookie was issued.</returns>
    public static List<string> SessionCookies(TestBrowser browser)
    {
        return browser.SetCookieHeaders
            .Where(header => header.StartsWith(SignIn.CookieName + "=", StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    ///     Reads every audit entry of an action.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="action">The action code; <c>null</c> for the entries of every action.</param>
    /// <returns>The entries, newest first.</returns>
    public static async Task<IReadOnlyList<AuditEntry>> AuditAsync(WebApplication app, string? action)
    {
        var audit = app.Services.GetRequiredService<AuditService>();
        var result = new List<AuditEntry>();
        long? before = null;
        while (true)
        {
            var page = await audit.ListAsync(null, null, before, null, TestContext.Current.CancellationToken);
            result.AddRange(page.Entries.Where(entry => action is null || entry.Action == action));
            if (!page.HasOlder)
            {
                return result;
            }

            before = page.OlderBefore;
        }
    }

    /// <summary>
    ///     Checks that none of the values a faulty identity token carried reached the log or the audit trail.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="logs">The captured log of the application.</param>
    /// <returns>A task that completes when everything is checked.</returns>
    public static async Task AssertNoTokenValuesRecordedAsync(WebApplication app, CapturingLoggerProvider logs)
    {
        string[] values =
        [
            FakeIdentityProvider.ForgedIssuer,
            FakeIdentityProvider.ForgedAudience,
            FakeIdentityProvider.ForgedNonce,
            "subject-1",
            "olive@example.com"
        ];
        foreach (var value in values)
        {
            // The framework's own handler (an OTS item, whose log the unit does not control) names the issuer and
            // audience of a token it refused in its log. Both values are chosen by the sender of the token and
            // are no secret. The exemption is limited to exactly those two values and that one log category: every
            // other value (nonce, subject, e-mail address), and everything the application itself logs or audits,
            // must stay out. The requirement of the unit is worded for what the unit itself logs and records
            var framework = value is FakeIdentityProvider.ForgedIssuer or FakeIdentityProvider.ForgedAudience;
            var own = logs.Entries.Where(entry => !framework || !entry.Category.StartsWith(
                "Microsoft.AspNetCore.Authentication.OpenIdConnect", StringComparison.Ordinal)).ToList();
            Assert.DoesNotContain(own, entry => entry.Message.Contains(value, StringComparison.Ordinal));
            Assert.DoesNotContain(
                own,
                entry => entry.Properties.Values.Any(
                    property => property?.ToString()?.Contains(value, StringComparison.Ordinal) == true));
            Assert.DoesNotContain(
                await AuditAsync(app, null),
                entry => entry.Detail.Contains(value, StringComparison.Ordinal));
        }
    }

    /// <summary>
    ///     Finds a user by name or fails the test.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="username">The user name.</param>
    /// <returns>The user.</returns>
    public static async Task<User> UserAsync(WebApplication app, string username)
    {
        var users = app.Services.GetRequiredService<UserService>();
        return await users.GetAsync(username, TestContext.Current.CancellationToken)
               ?? throw new InvalidOperationException($"The user '{username}' does not exist.");
    }
}
