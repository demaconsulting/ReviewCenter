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
using DemaConsulting.ReviewCenter.Tests.Evidence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static DemaConsulting.ReviewCenter.Tests.Api.ApiTestSupport;

namespace DemaConsulting.ReviewCenter.Tests.Api;

/// <summary>
///     Tests for the endpoints of the API: what they return, how they create reviews, how they refuse bad requests,
///     and that nothing under <c>/api</c> can sign.
/// </summary>
public class ApiEndpointsTests
{
    /// <summary>
    ///     A well-formed commit id for the repository tests.
    /// </summary>
    private const string HeadSha = "1111111111111111111111111111111111111111";

    /// <summary>
    ///     Another well-formed commit id, for the baseline.
    /// </summary>
    private const string BaseSha = "2222222222222222222222222222222222222222";

    /// <summary>
    ///     Test that the project list returns every project with its details in camel-case JSON.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiEndpoints_ListProjects_Token_ReturnsProjects()
    {
        // Arrange: two projects and a token
        await using var app = await StartAsync();
        var token = await CreateTokenAsync(app);
        var projects = app.Services.GetRequiredService<ProjectService>();
        await projects.CreateAsync("Apollo", "Moon", TestContext.Current.CancellationToken);
        await projects.CreateAsync("Gemini", null, TestContext.Current.CancellationToken);
        using var client = ClientFor(app, token.Value);

        // Act: list them
        var response = await client.GetAsync("/api/projects", TestContext.Current.CancellationToken);
        using var json = await JsonAsync(response);

        // Assert: both are there, with camel-case names
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var names = json.RootElement.EnumerateArray().Select(p => p.GetProperty("name").GetString()).Order().ToList();
        Assert.Equal(["Apollo", "Gemini"], names);
        var apollo = json.RootElement.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Apollo");
        Assert.Equal("Moon", apollo.GetProperty("description").GetString());
        Assert.True(apollo.GetProperty("id").GetInt32() > 0);
        Assert.True(apollo.TryGetProperty("created", out _));
    }

    /// <summary>
    ///     Test that the review list of a project returns the summaries, and that an unknown project is a 404 problem.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiEndpoints_ListReviews_Project_ReturnsSummariesOrNotFound()
    {
        // Arrange: a project with one review
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: false);
        var token = await CreateTokenAsync(app);
        using var client = ClientFor(app, token.Value);

        // Act: list the reviews of the project and of a project that does not exist
        var found = await client.GetAsync($"/api/projects/{projectId}/reviews", TestContext.Current.CancellationToken);
        var missing = await client.GetAsync("/api/projects/999/reviews", TestContext.Current.CancellationToken);
        using var json = await JsonAsync(found);

        // Assert: the summary, and a problem for the other
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var review = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal("REV-1", review.GetProperty("id").GetString());
        Assert.Equal("First review", review.GetProperty("title").GetString());
        Assert.False(review.GetProperty("createdViaApi").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    ///     Test that the review details of a complete review show its slots, approval, counts and next step.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiEndpoints_GetReview_CompleteReview_ReturnsDetails()
    {
        // Arrange: a complete review (every slot signed)
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        var token = await CreateTokenAsync(app);
        using var client = ClientFor(app, token.Value);

        // Act: get the review, and one that does not exist
        var response = await client.GetAsync($"/api/projects/{projectId}/reviews/REV-1", TestContext.Current.CancellationToken);
        var missing = await client.GetAsync($"/api/projects/{projectId}/reviews/NOPE", TestContext.Current.CancellationToken);
        using var json = await JsonAsync(response);
        var root = json.RootElement;

        // Assert: the details of the review
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("REV-1", root.GetProperty("id").GetString());
        Assert.True(root.GetProperty("approved").GetBoolean());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("approvalDigest").GetString()));
        Assert.Equal(1, root.GetProperty("counts").GetProperty("files").GetInt32());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("nextStep").GetString()));
        var slot = Assert.Single(root.GetProperty("slots").EnumerateArray());
        Assert.Equal("ann", slot.GetProperty("primary").GetString());
        Assert.Equal("bob", slot.GetProperty("alternate").GetString());
        Assert.Equal("Ann", slot.GetProperty("signedBy").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    /// <summary>
    ///     Test that every endpoint of the API refuses a request without a token with 401.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="route">The address.</param>
    [Theory(Timeout = 60000)]
    [InlineData("GET", "/api/projects")]
    [InlineData("GET", "/api/projects/1/reviews")]
    [InlineData("GET", "/api/projects/1/reviews/REV-1")]
    [InlineData("POST", "/api/projects/1/reviews")]
    [InlineData("GET", "/api/projects/1/reviews/REV-1/attestation.pdf")]
    public async Task ApiEndpoints_Request_NoToken_Answers401(string method, string route)
    {
        // Arrange: an application with a project and a review
        await using var app = await StartAsync();
        await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        using var client = ClientFor(app, null);
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "POST")
        {
            request.Content = Json(new { id = "X-1", title = "T", files = Array.Empty<object>() });
        }

        // Act: send it without a token
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert: refused
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    ///     Test that a review can be created from uploaded files, that the answer carries the details and the
    ///     address of the review, and that the review and the audit entry say it was created through a token.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiEndpoints_CreateReview_Upload_CreatesDraftFlaggedAsApi()
    {
        // Arrange: a project and a token
        await using var app = await StartAsync();
        var token = await CreateTokenAsync(app);
        var project = await app.Services.GetRequiredService<ProjectService>().CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        using var client = ClientFor(app, token.Value);
        var body = new
        {
            id = "REV-7",
            title = "Uploaded",
            files = new[]
            {
                new { path = "a.txt", contentBase64 = Convert.ToBase64String("one\n"u8.ToArray()) },
                new { path = "dir/b.txt", contentBase64 = Convert.ToBase64String("two\n"u8.ToArray()) }
            }
        };

        // Act: create the review
        var response = await client.PostAsync($"/api/projects/{project.Id}/reviews", Json(body), TestContext.Current.CancellationToken);
        using var json = await JsonAsync(response);

        // Assert: 201 with the address, the details, and the API flag
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/projects/{project.Id}/reviews/REV-7", response.Headers.Location?.OriginalString);
        var review = json.RootElement.GetProperty("review");
        Assert.Equal("REV-7", review.GetProperty("id").GetString());
        Assert.Equal("ann", review.GetProperty("createdBy").GetString());
        Assert.True(review.GetProperty("createdViaApi").GetBoolean());
        Assert.Equal(2, review.GetProperty("counts").GetProperty("files").GetInt32());
        Assert.Empty(json.RootElement.GetProperty("skipped").EnumerateArray());

        // Assert: the review can be read back with the flag, and the audit entry names the token
        var read = await client.GetAsync(response.Headers.Location, TestContext.Current.CancellationToken);
        using var readJson = await JsonAsync(read);
        Assert.True(readJson.RootElement.GetProperty("createdViaApi").GetBoolean());
        var entry = Assert.Single(await AuditAsync(app), e => e.Action == "review.created");
        Assert.Contains($"created via API token '{token.Token.DisplayId}'", entry.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a review can be created from a registered repository, and that skipped files are reported.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiEndpoints_CreateReview_Repository_CreatesReviewFromChanges()
    {
        // Arrange: a registered repository with scripted changes
        var git = new FakeGitRepositoryProvider();
        git.Revisions["main"] = HeadSha;
        git.Revisions["v1"] = BaseSha;
        git.Script(
            BaseSha,
            HeadSha,
            [
                ("src/mod.txt", ChangeKind.Modified, "world\n", "hello\n"),
                ("src/new.txt", ChangeKind.Added, null, "world\n")
            ]);
        await using var app = await StartAsync(configureServices: services => services.AddSingleton<IGitRepositoryProvider>(git));
        var token = await CreateTokenAsync(app);
        await EnsureUserAsync(app, "adm", UserRole.Admin);
        var repository = await app.Services.GetRequiredService<RepositoryService>().CreateAsync(
            "main-repo", "https://example.com/org/repo.git", null, null, new Actor("adm", "Adm"), TestContext.Current.CancellationToken);
        var project = await app.Services.GetRequiredService<ProjectService>().CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        using var client = ClientFor(app, token.Value);
        var body = new
        {
            id = "REV-R",
            title = "From repository",
            repository = new { repositoryId = repository.Id, headRevision = "main", baselineRevision = "v1", selection = "**/*" }
        };

        // Act: create the review
        var response = await client.PostAsync($"/api/projects/{project.Id}/reviews", Json(body), TestContext.Current.CancellationToken);
        using var json = await JsonAsync(response);

        // Assert: created from the repository, flagged, with both commits
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var review = json.RootElement.GetProperty("review");
        Assert.True(review.GetProperty("isFromRepository").GetBoolean());
        Assert.True(review.GetProperty("createdViaApi").GetBoolean());
        Assert.Equal("main-repo", review.GetProperty("repositoryName").GetString());
        Assert.Equal(HeadSha, review.GetProperty("headSha").GetString());
        Assert.Equal(BaseSha, review.GetProperty("baselineSha").GetString());
        Assert.Equal(2, review.GetProperty("counts").GetProperty("files").GetInt32());
        var entry = Assert.Single(await AuditAsync(app), e => e.Action == "review.created");
        Assert.Contains($"created via API token '{token.Token.DisplayId}'", entry.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a request body that is not well formed is refused with 400 and the name of the field, and
    ///     that nothing is created.
    /// </summary>
    /// <param name="json">The request body.</param>
    /// <param name="field">The field the problem names.</param>
    [Theory(Timeout = 60000)]
    [InlineData("""{"id":"X-1","title":"T"}""", "source")]
    [InlineData("""{"id":"X-1","title":"T","files":[],"repository":{"repositoryId":1,"headRevision":"main"}}""", "source")]
    [InlineData("""{"id":"X-1","title":"T","files":[{"path":"a.txt","contentBase64":"not base64!"}]}""", "files[0].contentBase64")]
    [InlineData("", "body")]
    public async Task ApiEndpoints_CreateReview_BadRequestBody_Answers400NamingTheField(string json, string field)
    {
        // Arrange: a project and a token
        await using var app = await StartAsync();
        var token = await CreateTokenAsync(app);
        var project = await app.Services.GetRequiredService<ProjectService>().CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        using var client = ClientFor(app, token.Value);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act: send the body
        var response = await client.PostAsync($"/api/projects/{project.Id}/reviews", content, TestContext.Current.CancellationToken);
        using var problem = await JsonAsync(response);

        // Assert: 400 with the field named, and no review
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _));
        Assert.Empty(await app.Services.GetRequiredService<ReviewService>().ListAsync(project.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the rules of the services answer with their status: 400 for an invalid review id or title,
    ///     409 for a duplicate id and 404 for an unknown project.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiEndpoints_CreateReview_ServiceRules_AnswerTheirStatus()
    {
        // Arrange: a project with one review and a token
        await using var app = await StartAsync();
        var token = await CreateTokenAsync(app);
        var project = await app.Services.GetRequiredService<ProjectService>().CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        using var client = ClientFor(app, token.Value);
        var files = new[] { new { path = "a.txt", contentBase64 = Convert.ToBase64String("one\n"u8.ToArray()) } };
        var first = await client.PostAsync(
            $"/api/projects/{project.Id}/reviews", Json(new { id = "REV-1", title = "T", files }), TestContext.Current.CancellationToken);

        // Act: a duplicate, an invalid id, an empty title and an unknown project
        var duplicate = await client.PostAsync(
            $"/api/projects/{project.Id}/reviews", Json(new { id = "REV-1", title = "T", files }), TestContext.Current.CancellationToken);
        var badId = await client.PostAsync(
            $"/api/projects/{project.Id}/reviews", Json(new { id = "bad id!", title = "T", files }), TestContext.Current.CancellationToken);
        var noTitle = await client.PostAsync(
            $"/api/projects/{project.Id}/reviews", Json(new { id = "REV-2", title = " ", files }), TestContext.Current.CancellationToken);
        var noProject = await client.PostAsync(
            "/api/projects/999/reviews", Json(new { id = "REV-3", title = "T", files }), TestContext.Current.CancellationToken);

        // Assert: the statuses, each as a problem document
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badId.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noTitle.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noProject.StatusCode);
        Assert.All(
            new[] { duplicate, badId, noTitle, noProject },
            r => Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType));
        using var problem = await JsonAsync(duplicate);
        Assert.Equal(409, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.RootElement.GetProperty("detail").GetString()));
    }

    /// <summary>
    ///     Test that the attestation of a complete review downloads as a PDF with the token, that an incomplete
    ///     review is a 409 problem, and that an unknown review is a 404 problem.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiEndpoints_GetAttestation_Token_ReturnsPdfOrProblem()
    {
        // Arrange: one complete review and a token
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        var token = await CreateTokenAsync(app);
        using var client = ClientFor(app, token.Value);

        // Act: download it, and ask for a review that does not exist
        var response = await client.GetAsync($"/api/projects/{projectId}/reviews/REV-1/attestation.pdf", TestContext.Current.CancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var missing = await client.GetAsync($"/api/projects/{projectId}/reviews/NOPE/attestation.pdf", TestContext.Current.CancellationToken);

        // Assert: a PDF, and a problem for the other
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    ///     Test that the attestation of a review that is not complete answers 409 with a problem document.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiEndpoints_GetAttestation_ReviewNotSigned_Answers409Problem()
    {
        // Arrange: a review nobody signed
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: false);
        var token = await CreateTokenAsync(app);
        using var client = ClientFor(app, token.Value);

        // Act: ask for the attestation
        var response = await client.GetAsync($"/api/projects/{projectId}/reviews/REV-1/attestation.pdf", TestContext.Current.CancellationToken);

        // Assert: conflict as a problem document
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    ///     Test that an address under <c>/api</c> that does not exist answers a JSON 404 for any method, with or
    ///     without a token, and never the web application.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="withToken">Whether to send a token.</param>
    [Theory(Timeout = 60000)]
    [InlineData("GET", true)]
    [InlineData("GET", false)]
    [InlineData("POST", true)]
    [InlineData("DELETE", false)]
    public async Task ApiEndpoints_Request_UnknownRoute_AnswersJsonNotFound(string method, bool withToken)
    {
        // Arrange: an application and a client with or without a token
        await using var app = await StartAsync();
        var token = await CreateTokenAsync(app);
        using var client = ClientFor(app, withToken ? token.Value : null);

        // Act: request an address that does not exist
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/api/nothing/here"), TestContext.Current.CancellationToken);

        // Assert: a JSON 404
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    ///     Test that no route under <c>/api</c> can sign or change reviewer slots, and that asking for such a route
    ///     with a token is a 404.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiEndpoints_Routes_NoSigningOrSlotRoutes()
    {
        // Arrange: an application with a review and a token
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: false);
        var token = await CreateTokenAsync(app);
        using var client = ClientFor(app, token.Value);
        var patterns = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .Where(p => p.StartsWith("/api", StringComparison.Ordinal))
            .ToList();

        // Act: ask for the routes the web pages use to change slots and sign
        var slot = await client.PostAsync($"/api/projects/{projectId}/reviews/REV-1/slots", Json(new { primary = 1 }), TestContext.Current.CancellationToken);
        var sign = await client.PostAsync($"/api/projects/{projectId}/reviews/REV-1/sign", Json(new { }), TestContext.Current.CancellationToken);

        // Assert: nothing is routed for them, and the answers are 404
        Assert.NotEmpty(patterns);
        Assert.DoesNotContain(patterns, p => p.Contains("slot", StringComparison.OrdinalIgnoreCase)
            || p.Contains("sign", StringComparison.OrdinalIgnoreCase)
            || p.Contains("approve", StringComparison.OrdinalIgnoreCase)
            || p.Contains("approval", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.NotFound, slot.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sign.StatusCode);
    }
}
