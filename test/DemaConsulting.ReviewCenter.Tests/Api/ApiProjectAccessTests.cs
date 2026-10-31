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
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using static DemaConsulting.ReviewCenter.Tests.Api.ApiTestSupport;

namespace DemaConsulting.ReviewCenter.Tests.Api;

/// <summary>
///     Tests, over HTTP and independent of any page, that project-based access control holds on every route that
///     belongs to a project: a user who is no member of the project (with a cookie or a personal access token) gets
///     the answer for a project that does not exist, and a member and a system administrator are served.
/// </summary>
public class ApiProjectAccessTests
{
    /// <summary>
    ///     The review that exists in the arranged project.
    /// </summary>
    private const string ReviewId = "REV-1";

    /// <summary>
    ///     The number of a project that does not exist.
    /// </summary>
    private const int MissingProject = 9999;

    /// <summary>
    ///     Test that every project-scoped API route answers a non-member exactly like a missing project, for a
    ///     bearer token and for the Basic credentials ReviewMark uses.
    /// </summary>
    /// <param name="basic">Whether the token is sent as the password of Basic credentials.</param>
    [Theory(Timeout = 120000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApiProjectAccess_EveryProjectRoute_NonMemberToken_AnswersLikeAMissingProject(bool basic)
    {
        // Arrange: a project with a review, and a user who is no member of it, with a token
        await using var app = await StartAsync();
        var projectId = await ArrangeAsync(app);
        await EnsureUserAsync(app, "out", UserRole.Reviewer);
        var outsider = await CreateTokenAsync(app, "out");
        using var client = ClientFor(app, outsider.Value, basic);

        foreach (var route in Routes())
        {
            // Act: request the route for the hidden project and for a project that does not exist
            var hidden = await SendAsync(client, route, projectId);
            var missing = await SendAsync(client, route, MissingProject);
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();

            // Assert: 404, and the same body apart from the number of the project
            Assert.True(
                hidden.Status == HttpStatusCode.NotFound,
                $"{route.Method} {route.Path} answered {(int)hidden.Status} for a project the user is no member of.");
            Assert.Equal(missing.Status, hidden.Status);
            Assert.Equal(missing.ContentType, hidden.ContentType);
            Assert.Equal(Normalize(missing.Body, MissingProject), Normalize(hidden.Body, projectId));
        }
    }

    /// <summary>
    ///     Test that a member and a system administrator who is no member are served by every project-scoped API
    ///     route, so the refusal above is about membership and not about the route.
    /// </summary>
    /// <param name="username">The user who calls: the member or the system administrator.</param>
    [Theory(Timeout = 120000)]
    [InlineData("mem")]
    [InlineData("ann")]
    public async Task ApiProjectAccess_EveryProjectRoute_MemberAndSystemAdmin_AreNotRefused(string username)
    {
        // Arrange: a project with a review, a member and a system administrator, each with a token
        await using var app = await StartAsync();
        var projectId = await ArrangeAsync(app);
        var token = await CreateTokenAsync(app, username);
        using var client = ClientFor(app, token.Value);

        foreach (var route in Routes())
        {
            // Act
            var response = await SendAsync(client, route, projectId);
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();

            // Assert: whatever the route answers, it is neither a not-found nor a refusal
            Assert.True(
                response.Status is not HttpStatusCode.NotFound and not HttpStatusCode.Forbidden and not HttpStatusCode.Unauthorized,
                $"{route.Method} {route.Path} answered {(int)response.Status} for {username}.");
        }
    }

    /// <summary>
    ///     Test that the project list holds only the projects the owner of the token may see.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiProjectAccess_ListProjects_ShowsOnlyMemberProjectsExceptToSystemAdmins()
    {
        // Arrange: two projects, a member of the first, an outsider and a system administrator
        await using var app = await StartAsync();
        var first = await ArrangeAsync(app);
        await EnsureUserAsync(app, "out", UserRole.Reviewer);
        var second = (await app.Services.GetRequiredService<ProjectService>()
            .CreateAsync("Gemini", null, TestContext.Current.CancellationToken)).Id;
        var members = await IdsAsync(app, await CreateTokenAsync(app, "mem"));
        var outsiders = await IdsAsync(app, await CreateTokenAsync(app, "out"));
        var admins = await IdsAsync(app, await CreateTokenAsync(app, "ann"));

        // Assert
        Assert.Equal([first], members);
        Assert.Empty(outsiders);
        Assert.Equal([first, second], admins.Order());
    }

    /// <summary>
    ///     Test that the attestation web route, used with a browser session, answers a non-member like a missing
    ///     project and does not record an attestation.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiProjectAccess_AttestationPage_NonMemberSession_Answers404()
    {
        // Arrange: a project with a review, and a signed-in non-member
        await using var app = await StartAsync();
        var projectId = await ArrangeAsync(app);
        await EnsureUserAsync(app, "out", UserRole.Reviewer);
        using var outsider = await TestBrowser.SignedInAsync(app, "out", UserRole.Reviewer);
        var route = $"/projects/{projectId}/reviews/{ReviewId}/attestation.pdf";
        var missingRoute = $"/projects/{MissingProject}/reviews/{ReviewId}/attestation.pdf";

        // Act
        var hidden = await outsider.Client.GetAsync(route, TestContext.Current.CancellationToken);
        var missing = await outsider.Client.GetAsync(missingRoute, TestContext.Current.CancellationToken);

        // Assert: the same answer for both, which is 404
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(missing.StatusCode, hidden.StatusCode);
        Assert.Equal(
            await missing.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await hidden.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a token follows the memberships of its owner at once: after the owner is removed from the
    ///     project the same token gets 404.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiProjectAccess_Token_OwnerRemovedFromProject_Gets404AtOnce()
    {
        // Arrange: a member with a token who is served
        await using var app = await StartAsync();
        var projectId = await ArrangeAsync(app);
        var token = await CreateTokenAsync(app, "mem");
        using var client = ClientFor(app, token.Value);
        var before = await client.GetAsync($"/api/projects/{projectId}/reviews", TestContext.Current.CancellationToken);

        // Act: an administrator removes the member
        await app.Services.GetRequiredService<ProjectService>().RemoveMemberAsync(
            AuditTestExtensions.DefaultActor, projectId, "mem", TestContext.Current.CancellationToken);
        var after = await client.GetAsync($"/api/projects/{projectId}/reviews", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    /// <summary>
    ///     Replaces the number of the project in a body, as a whole number only, so that two answers can be compared.
    /// </summary>
    /// <param name="body">The body.</param>
    /// <param name="projectId">The number of the project.</param>
    /// <returns>The body with the number replaced.</returns>
    private static string Normalize(string body, int projectId)
    {
        return System.Text.RegularExpressions.Regex.Replace(body, $@"(?<![\d.])\b{projectId}\b(?![\d.])", "#");
    }

    /// <summary>
    ///     Lists the routes of the API that belong to a project.
    /// </summary>
    /// <returns>The method and path template of each route; <c>{p}</c> stands for the project number.</returns>
    private static (HttpMethod Method, string Path)[] Routes()
    {
        return
        [
            (HttpMethod.Get, "/api/projects/{p}/reviews"),
            (HttpMethod.Get, $"/api/projects/{{p}}/reviews/{ReviewId}"),
            (HttpMethod.Post, "/api/projects/{p}/reviews"),
            (HttpMethod.Get, $"/api/projects/{{p}}/reviews/{ReviewId}/attestation.pdf"),
            (HttpMethod.Get, "/api/projects/{p}/evidence/index.json"),
            (HttpMethod.Get, "/api/projects/{p}/checklist")
        ];
    }

    /// <summary>
    ///     Sends a request to a route of the API.
    /// </summary>
    /// <param name="client">The client with the credentials.</param>
    /// <param name="route">The route.</param>
    /// <param name="projectId">The project number to put in the path.</param>
    /// <returns>The status, content type and body of the answer.</returns>
    private static async Task<(HttpStatusCode Status, string? ContentType, string Body)> SendAsync(
        HttpClient client,
        (HttpMethod Method, string Path) route,
        int projectId)
    {
        using var request = new HttpRequestMessage(route.Method, route.Path.Replace("{p}", projectId.ToString()));
        if (route.Method == HttpMethod.Post)
        {
            request.Content = Json(new
            {
                id = "REV-NEW",
                title = "New",
                files = new[] { new { path = "a.txt", contentBase64 = Convert.ToBase64String("one\n"u8.ToArray()) } }
            });
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return (
            response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Reads the numbers of the projects the project list returns for a token.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="token">The token.</param>
    /// <returns>The project numbers.</returns>
    private static async Task<List<int>> IdsAsync(WebApplication app, CreatedPersonalAccessToken token)
    {
        using var client = ClientFor(app, token.Value);
        var response = await client.GetAsync("/api/projects", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await JsonAsync(response);
        return json.RootElement.EnumerateArray().Select(p => p.GetProperty("id").GetInt32()).ToList();
    }

    /// <summary>
    ///     Creates the system administrator <c>ann</c>, the member <c>mem</c>, a project that <c>mem</c> belongs to
    ///     and a review in it.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The number of the project.</returns>
    private static async Task<int> ArrangeAsync(WebApplication app)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await EnsureUserAsync(app, "ann");
        await EnsureUserAsync(app, "mem", UserRole.Reviewer);
        var project = await app.Services.GetRequiredService<ProjectService>().CreateAsync("Apollo", null, cancellationToken);
        var mem = (await app.Services.GetRequiredService<UserService>().GetAsync("mem", cancellationToken))!;
        await app.Services.GetRequiredService<IReviewStore>().AddMembershipAsync(mem.Id, project.Id, cancellationToken);
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id, ReviewId, "First", [new("a.txt", TestContent.A)], TestActors.Ann, cancellationToken);
        return project.Id;
    }
}
