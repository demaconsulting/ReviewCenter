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
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using DemaConsulting.ReviewCenter.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     A minimal browser for the in-memory application: it keeps cookies between requests, does not follow
///     redirects (so tests see them), and signs in through the real login endpoint.
/// </summary>
internal sealed class TestBrowser : IDisposable
{
    /// <summary>
    ///     A password that satisfies the password policy, used for the users the tests create.
    /// </summary>
    public const string Password = "correct horse battery";

    /// <summary>
    ///     The handler that keeps the cookies.
    /// </summary>
    private readonly CookieHandler _handler;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TestBrowser"/> class for an application.
    /// </summary>
    /// <param name="app">The started in-memory application.</param>
    /// <param name="https"><c>true</c> to address the application over https.</param>
    public TestBrowser(WebApplication app, bool https = false)
    {
        _handler = new CookieHandler(app.GetTestServer().CreateHandler());
        Client = new HttpClient(_handler) { BaseAddress = new Uri(https ? "https://localhost" : "http://localhost") };
    }

    /// <summary>
    ///     Gets the client; it sends the cookies it has received.
    /// </summary>
    public HttpClient Client { get; }

    /// <summary>
    ///     Gets every <c>Set-Cookie</c> header value the application has sent, in order.
    /// </summary>
    public IReadOnlyList<string> SetCookieHeaders => _handler.SetCookieHeaders;

    /// <summary>
    ///     Creates a user (unless it exists) and returns a browser that is signed in as that user.
    /// </summary>
    /// <param name="app">The started in-memory application.</param>
    /// <param name="username">The user name.</param>
    /// <param name="role">The role of the user, if it has to be created.</param>
    /// <returns>The signed-in browser; the caller disposes it.</returns>
    public static async Task<TestBrowser> SignedInAsync(WebApplication app, string username, UserRole role = UserRole.Admin)
    {
        var users = app.Services.GetRequiredService<UserService>();
        if (await users.GetAsync(username, TestContext.Current.CancellationToken) is null)
        {
            await users.CreateAsync(
                username, char.ToUpperInvariant(username[0]) + username[1..], role, Password, TestContext.Current.CancellationToken);
        }

        var browser = new TestBrowser(app);
        var response = await browser.LoginAsync(username, Password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return browser;
    }

    /// <summary>
    ///     Requests a page and parses the returned document.
    /// </summary>
    /// <param name="route">The route to request.</param>
    /// <param name="expected">The status code the page must answer with.</param>
    /// <returns>The parsed document.</returns>
    public async Task<IHtmlDocument> GetPageAsync(string route, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await Client.GetAsync(route, TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return await new HtmlParser().ParseDocumentAsync(html, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Reads an antiforgery token from a page; the token belongs to the cookies the browser holds now.
    /// </summary>
    /// <param name="route">The page that holds a form with a token.</param>
    /// <returns>The token.</returns>
    public async Task<string> GetTokenAsync(string route = "/login")
    {
        var page = await GetPageAsync(route);
        return page.QuerySelector("input[name='__RequestVerificationToken']")?.GetAttribute("value")
               ?? throw new InvalidOperationException($"The page '{route}' has no antiforgery token.");
    }

    /// <summary>
    ///     Posts a form.
    /// </summary>
    /// <param name="path">The endpoint to post to.</param>
    /// <param name="fields">The form fields.</param>
    /// <param name="withToken"><c>true</c> to add a valid antiforgery token.</param>
    /// <returns>The response; redirects are not followed.</returns>
    public async Task<HttpResponseMessage> PostFormAsync(
        string path,
        IEnumerable<KeyValuePair<string, string>> fields,
        bool withToken = true)
    {
        var all = fields.ToList();
        if (withToken)
        {
            all.Add(new("__RequestVerificationToken", await GetTokenAsync()));
        }

        return await Client.PostAsync(path, new FormUrlEncodedContent(all), TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Posts the login form.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="password">The password.</param>
    /// <param name="returnUrl">The return address, if any.</param>
    /// <returns>The response; redirects are not followed.</returns>
    public Task<HttpResponseMessage> LoginAsync(string username, string password, string? returnUrl = null)
    {
        List<KeyValuePair<string, string>> fields = [new("username", username), new("password", password)];
        if (returnUrl is not null)
        {
            fields.Add(new("returnUrl", returnUrl));
        }

        return PostFormAsync("/auth/login", fields);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Client.Dispose();
    }

    /// <summary>
    ///     Keeps cookies between requests like a browser does.
    /// </summary>
    /// <param name="inner">The handler that reaches the in-memory server.</param>
    private sealed class CookieHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        /// <summary>
        ///     The cookies received so far.
        /// </summary>
        private readonly CookieContainer _cookies = new();

        /// <summary>
        ///     The raw <c>Set-Cookie</c> header values received so far.
        /// </summary>
        private readonly List<string> _setCookieHeaders = [];

        /// <summary>
        ///     Gets every raw <c>Set-Cookie</c> header value received so far.
        /// </summary>
        public IReadOnlyList<string> SetCookieHeaders => _setCookieHeaders;

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;

            // Send the cookies the way a browser would
            var cookieHeader = _cookies.GetCookieHeader(uri);
            if (cookieHeader.Length > 0)
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            }

            var response = await base.SendAsync(request, cancellationToken);

            // Remember what the application sets (a past expiry removes a cookie)
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var value in values)
                {
                    _setCookieHeaders.Add(value);
                    _cookies.SetCookies(uri, value);
                }
            }

            return response;
        }
    }
}
