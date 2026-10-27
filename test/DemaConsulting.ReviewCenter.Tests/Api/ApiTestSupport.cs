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
using DemaConsulting.ReviewCenter.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
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
    /// <returns>The started application; the caller disposes it.</returns>
    public static Task<WebApplication> StartAsync(
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        return TestWebApp.StartAsync(new CapturingLoggerProvider(), null, ["serve", "--silent"], settings, configureServices);
    }

    /// <summary>
    ///     Creates a user unless it exists.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="username">The user name.</param>
    /// <param name="role">The role of the user, if it has to be created.</param>
    /// <returns>A task that completes when the user exists.</returns>
    public static async Task EnsureUserAsync(WebApplication app, string username, UserRole role = UserRole.Reviewer)
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
    ///     Creates a client that calls the application with a bearer token; it keeps no cookies and follows no redirects.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="token">The token value, or <c>null</c> to send no <c>Authorization</c> header.</param>
    /// <returns>The client; the caller disposes it.</returns>
    public static HttpClient ClientFor(WebApplication app, string? token)
    {
        var client = app.GetTestServer().CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
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
