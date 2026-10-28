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

using System.Net.Http.Headers;
using System.Text.Json;
using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Api;

/// <summary>
///     Helpers shared by the API tests: starting the application, making users and tokens, and calling the API with a
///     token the way a script would.
/// </summary>
internal static class ApiTestSupport
{
    /// <summary>
    ///     Starts the real pipeline on the in-memory server with a new database.
    /// </summary>
    /// <param name="settings">Extra configuration values; <c>null</c> for none.</param>
    /// <param name="configureServices">Changes to the services; <c>null</c> for none.</param>
    /// <param name="logs">Receives the log entries of the application; <c>null</c> to capture them in a provider nobody reads.</param>
    /// <returns>The started application; the caller disposes it.</returns>
    public static Task<WebApplication> StartAsync(
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null,
        CapturingLoggerProvider? logs = null)
    {
        return TestWebApp.StartAsync(logs ?? new CapturingLoggerProvider(), null, ["serve", "--silent"], settings, configureServices);
    }

    /// <summary>
    ///     Starts the real pipeline on a real Kestrel server that listens on a free loopback port, with a new database.
    ///     The in-memory server does not enforce the request size limit of an endpoint, Kestrel does.
    /// </summary>
    /// <returns>The started application and the address it listens on; the caller disposes the application.</returns>
    public static async Task<(WebApplication App, Uri Address)> StartKestrelAsync()
    {
        using var context = Context.Create(["serve", "--silent"]);
        var app = ServerHost.CreateApplication(context, new ServerHostOptions
        {
            MapStaticAssets = false,
            ConfigureBuilder = builder =>
            {
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = TestDatabase.NewConnectionString()
                });
                FastCommitInterceptor.AddTo(builder.Services);
                TestDatabase.EnsureAttestationKey();
            }
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        return (app, new Uri(app.Urls.First()));
    }

    /// <summary>
    ///     Creates a user unless it exists.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="username">The user name.</param>
    /// <param name="role">
    ///     The role of the user, if it has to be created; the default is a system administrator, like the default actor
    ///     <c>ann</c>, so that a test can create projects and see all of them without being a member.
    /// </param>
    /// <returns>A task that completes when the user exists.</returns>
    public static async Task EnsureUserAsync(WebApplication app, string username, UserRole role = UserRole.Admin)
    {
        var users = app.Services.GetRequiredService<UserService>();
        if (await users.GetAsync(username, TestContext.Current.CancellationToken) is null)
        {
            await users.CreateAsync(
                username, char.ToUpperInvariant(username[0]) + username[1..], role, TestBrowser.Password, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Creates a token for a user (who is created if needed) through the token service.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="username">The owner.</param>
    /// <param name="days">The lifetime in days.</param>
    /// <returns>The token with its one-time value.</returns>
    public static async Task<CreatedPersonalAccessToken> CreateTokenAsync(WebApplication app, string username = "ann", int days = 90)
    {
        await EnsureUserAsync(app, username);
        var users = app.Services.GetRequiredService<UserService>();
        var user = (await users.GetAsync(username, TestContext.Current.CancellationToken))!;
        return await app.Services.GetRequiredService<PersonalAccessTokenService>().CreateAsync(
            new Actor(user.Username, user.DisplayName), "test token", days, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Creates a client that calls the application with a token; it keeps no cookies and follows no redirects.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="token">The token value, or <c>null</c> to send no <c>Authorization</c> header.</param>
    /// <param name="basic">
    ///     <c>true</c> to send the token as the password of HTTP Basic credentials, the way ReviewMark does, instead of
    ///     as a bearer token.
    /// </param>
    /// <returns>The client; the caller disposes it.</returns>
    public static HttpClient ClientFor(WebApplication app, string? token, bool basic = false)
    {
        var client = app.GetTestServer().CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = basic
                ? BasicHeader("reviewmark", token)
                : new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    /// <summary>
    ///     Builds HTTP Basic credentials.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="password">The password.</param>
    /// <returns>The header value.</returns>
    public static AuthenticationHeaderValue BasicHeader(string username, string password)
    {
        return new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}")));
    }

    /// <summary>
    ///     Reads the body of a response as a JSON document.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>The parsed document.</returns>
    public static async Task<JsonDocument> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(text);
    }

    /// <summary>
    ///     Builds a JSON request body.
    /// </summary>
    /// <param name="value">The object to send.</param>
    /// <returns>The content, with the JSON media type.</returns>
    public static StringContent Json(object value)
    {
        return new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");
    }

    /// <summary>
    ///     Reads the audit entries recorded so far, newest first.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The entries of the first page.</returns>
    public static async Task<IReadOnlyList<AuditEntry>> AuditAsync(WebApplication app)
    {
        var page = await app.Services.GetRequiredService<AuditService>().ListAsync(null, null, null, null, TestContext.Current.CancellationToken);
        return page.Entries;
    }
}
