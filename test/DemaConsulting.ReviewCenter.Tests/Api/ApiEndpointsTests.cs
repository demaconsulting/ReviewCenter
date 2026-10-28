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
using DemaConsulting.ReviewCenter.Api;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Tests.Evidence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
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
    ///     Test that the project member rules map to the statuses the API documents: the last project administrator
    ///     is a conflict, too many attempts is 429, and a pending password change is forbidden.
    /// </summary>
    /// <param name="rule">The rule that failed.</param>
    /// <param name="status">The expected HTTP status.</param>
    [Theory]
    [InlineData(DomainRule.LastProjectAdmin, 409)]
    [InlineData(DomainRule.TooManyAttempts, 429)]
    [InlineData(DomainRule.PasswordChangeRequired, 403)]
    public void ApiProblems_StatusOf_MemberRules_MapsToDocumentedStatus(DomainRule rule, int status)
    {
        // Arrange: nothing to prepare

        // Act
        var actual = ApiProblems.StatusOf(rule);

        // Assert
        Assert.Equal(status, actual);
    }

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
    [InlineData("GET", "/api/projects/1/evidence/index.json")]
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
    ///     Test that authentication comes before the body is looked at: a creation request without a token is
    ///     refused with 401 whatever its content type or body, never with 415 or 400.
    /// </summary>
    /// <param name="contentType">The content type of the body.</param>
    /// <param name="body">The body.</param>
    [Theory(Timeout = 60000)]
    [InlineData("text/plain", "not json")]
    [InlineData("application/json", "{ not json")]
    public async Task ApiEndpoints_CreateReview_NoTokenOddBody_Answers401(string contentType, string body)
    {
        // Arrange: an application with a project and a request whose body would otherwise be refused
        await using var app = await StartAsync();
        await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        using var client = ClientFor(app, null);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/projects/1/reviews")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, contentType)
        };

        // Act: send it without a token
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert: refused before the body is read
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
        var project = await app.Services.GetRequiredService<ProjectService>().CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var repository = await app.Services.GetRequiredService<RepositoryService>().CreateAsync(
            project.Id, "main-repo", "https://example.com/org/repo.git", null, null, new Actor("adm", "Adm"), TestContext.Current.CancellationToken);
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
    ///     Test that the evidence index lists the complete review of a plan with its review-set id, the fingerprint
    ///     of the review, the day of completion and an absolute link to the attestation, over bearer and Basic
    ///     credentials, and that the answer is not cached.
    /// </summary>
    /// <param name="basic">Whether the token is sent as the password of HTTP Basic credentials.</param>
    [Theory(Timeout = 60000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApiEndpoints_GetEvidenceIndex_CompleteReviewWithSetId_ReturnsEntryWithAbsoluteFileUrl(bool basic)
    {
        // Arrange: one complete review that has a review-set id, one without, and a token
        await using var app = await StartAsync();
        var projectId = await ArrangeSignedReviewWithSetIdAsync(app);
        var token = await CreateTokenAsync(app);
        using var client = ClientFor(app, token.Value, basic);

        // Act: read the index, and the recorded signature time of the review it stands for
        var response = await client.GetAsync($"/api/projects/{projectId}/evidence/index.json", TestContext.Current.CancellationToken);
        using var json = await JsonAsync(response);
        var approval = await app.Services.GetRequiredService<SigningService>().GetApprovalAsync(projectId, "REV-2", TestContext.Current.CancellationToken);
        var signedDay = approval!.Slots.Max(slot => slot.SignedAt!.Value).UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        // Assert: JSON, not cached, and the root object has the one review that has a set id
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var reviews = json.RootElement.GetProperty("reviews");
        var entry = Assert.Single(reviews.EnumerateArray());
        Assert.Equal("Core-Logic", entry.GetProperty("id").GetString());
        Assert.Matches("^[0-9a-f]{64}$", entry.GetProperty("fingerprint").GetString()!);
        Assert.Equal(signedDay, entry.GetProperty("date").GetString());
        Assert.Equal("pass", entry.GetProperty("result").GetString());
        Assert.Equal($"http://localhost/api/projects/{projectId}/reviews/REV-2/attestation.pdf", entry.GetProperty("file").GetString());
    }

    /// <summary>
    ///     Test that the evidence index builds the attestation address from the scheme, the host and the path base of
    ///     the request, and escapes the review identifier.
    /// </summary>
    /// <param name="scheme">The scheme of the request.</param>
    /// <param name="host">The host header of the request.</param>
    /// <param name="pathBase">The path base of the request.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="expected">The expected address.</param>
    [Theory]
    [InlineData("https", "reviews.example.com:8443", "/center", "REV-2", "https://reviews.example.com:8443/center/api/projects/7/reviews/REV-2/attestation.pdf")]
    [InlineData("http", "localhost", "", "REV-2", "http://localhost/api/projects/7/reviews/REV-2/attestation.pdf")]
    [InlineData("http", "localhost", "", "A B/c?d", "http://localhost/api/projects/7/reviews/A%20B%2Fc%3Fd/attestation.pdf")]
    public void ApiEndpoints_GetEvidenceIndex_SchemeHostPathBaseAndId_BuildsAbsoluteUrl(
        string scheme, string host, string pathBase, string reviewId, string expected)
    {
        // Arrange: a request with the scheme, the host and the path base
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        context.Request.PathBase = pathBase;

        // Act: build the address
        var url = ApiEndpoints.AttestationUrl(context.Request, 7, reviewId);

        // Assert: absolute, from the request, with the identifier escaped
        Assert.Equal(expected, url);
    }

    /// <summary>
    ///     Test that the evidence index builds the attestation address from the host header of the request.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiEndpoints_GetEvidenceIndex_OtherHost_BuildsUrlFromRequestHost()
    {
        // Arrange: one complete review that has a review-set id, a token, and a request for another host
        await using var app = await StartAsync();
        var projectId = await ArrangeSignedReviewWithSetIdAsync(app);
        var token = await CreateTokenAsync(app);
        using var client = ClientFor(app, token.Value);
        client.DefaultRequestHeaders.Host = "reviews.example.com";

        // Act: read the index
        var response = await client.GetAsync($"/api/projects/{projectId}/evidence/index.json", TestContext.Current.CancellationToken);
        using var json = await JsonAsync(response);

        // Assert: the address names the host of the request
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single(json.RootElement.GetProperty("reviews").EnumerateArray());
        Assert.Equal($"http://reviews.example.com/api/projects/{projectId}/reviews/REV-2/attestation.pdf", entry.GetProperty("file").GetString());
    }

    /// <summary>
    ///     Test that the evidence index of a project without a complete review that has a review-set id is an empty
    ///     list, not an error.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiEndpoints_GetEvidenceIndex_NoListableReview_ReturnsEmptyList()
    {
        // Arrange: a complete review without a review-set id, and a token
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        var token = await CreateTokenAsync(app);
        using var client = ClientFor(app, token.Value);

        // Act: read the index
        var response = await client.GetAsync($"/api/projects/{projectId}/evidence/index.json", TestContext.Current.CancellationToken);
        using var json = await JsonAsync(response);

        // Assert: OK with an empty list
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, json.RootElement.GetProperty("reviews").GetArrayLength());
    }

    /// <summary>
    ///     Test that the evidence index of a project that does not exist is a 404 problem document.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiEndpoints_GetEvidenceIndex_UnknownProject_Answers404Problem()
    {
        // Arrange: an application without projects, and a token
        await using var app = await StartAsync();
        var token = await CreateTokenAsync(app);
        using var client = ClientFor(app, token.Value);

        // Act: read the index of a project that does not exist
        var response = await client.GetAsync("/api/projects/99/evidence/index.json", TestContext.Current.CancellationToken);

        // Assert: not found as a problem document that is not cached either
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
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

    /// <summary>
    ///     Test that a file set that is beyond the review limits (the number of files, the size of one file, the total
    ///     size) is refused with 400 and a problem document that names the limit, and that no review is created.
    /// </summary>
    /// <param name="limit">The limit to exceed: <c>count</c>, <c>file</c> or <c>total</c>.</param>
    /// <param name="expectedDetail">Text that the problem detail must contain.</param>
    [Theory(Timeout = 120000)]
    [InlineData("count", "at most 200 files")]
    [InlineData("file", "limit of 2 MiB")]
    [InlineData("total", "limit of 20 MiB")]
    public async Task ApiEndpoints_CreateReview_FileLimits_Answer400Problem(string limit, string expectedDetail)
    {
        // Arrange: a project, a token and a file set that is one step over the limit
        await using var app = await StartAsync();
        var token = await CreateTokenAsync(app);
        var project = await app.Services.GetRequiredService<ProjectService>().CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        using var client = ClientFor(app, token.Value);
        var oneFileLimit = (int)(ReviewLimits.MaxTotalSizeBytes / ReviewLimits.MaxFileSizeBytes);
        var files = limit switch
        {
            "count" => Enumerable.Range(0, ReviewLimits.MaxFileCount + 1).Select(i => UploadOf($"f{i}.txt", 1)).ToList(),
            "file" => [UploadOf("big.bin", ReviewLimits.MaxFileSizeBytes + 1)],

            // The largest files that are allowed one by one, then one byte more than the total allows
            _ => Enumerable.Range(0, oneFileLimit + 1)
                .Select(i => UploadOf($"f{i}.bin", i == oneFileLimit ? 1 : ReviewLimits.MaxFileSizeBytes)).ToList()
        };

        // Act: send the review
        var response = await client.PostAsync(
            $"/api/projects/{project.Id}/reviews", Json(new { id = "REV-L", title = "Limits", files }), TestContext.Current.CancellationToken);
        using var problem = await JsonAsync(response);

        // Assert: 400 as a problem document that names the limit, and nothing is created
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(expectedDetail, problem.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Empty(await app.Services.GetRequiredService<ReviewService>().ListAsync(project.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the endpoint that creates a review carries a request size limit that is derived from the review
    ///     limits: large enough for the Base64 text of the largest upload, and far below an unbounded body.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiEndpoints_CreateReview_Endpoint_HasDerivedBodySizeLimit()
    {
        // Arrange: an application and its endpoints
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        await using var app = await StartAsync();

        // Act: find the endpoint that creates a review and read its size limit
        var endpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Single(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "CreateReview");
        var limit = endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize;

        // Assert: the derived limit, between the Base64 size of the largest upload and twice the largest upload
        Assert.Equal(ApiEndpoints.MaxCreateBodyBytes, limit);
        Assert.True(limit > ReviewLimits.MaxTotalSizeBytes / 3 * 4);
        Assert.True(limit < ReviewLimits.MaxTotalSizeBytes * 2);
    }

    /// <summary>
    ///     Test that a request body larger than the limit of the endpoint is refused with 413 by a real server, and
    ///     that no review is created. The in-memory test server does not enforce the limit, so Kestrel is used.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiEndpoints_CreateReview_BodyOverLimit_Answers413()
    {
        // Arrange: a real server with a project and a token, and a body one byte over the limit of the endpoint
        var (app, address) = await StartKestrelAsync();
        await using (app)
        {
            var token = await CreateTokenAsync(app);
            var project = await app.Services.GetRequiredService<ProjectService>().CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
            using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(100) };
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Value);
            using var content = new ZeroBodyContent(ApiEndpoints.MaxCreateBodyBytes + 1);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{project.Id}/reviews") { Content = content };

            // The server answers before it reads the body, so the client waits for that answer before it sends 30 MB
            request.Headers.ExpectContinue = true;

            // Act: post the body
            var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            // Assert: refused as too large, and nothing is created
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Assert.Empty(await app.Services.GetRequiredService<ReviewService>().ListAsync(project.Id, TestContext.Current.CancellationToken));
        }
    }

    /// <summary>
    ///     Test that a request body that is not JSON of the right shape is refused with 400 and a problem document
    ///     that does not leak details of the server, and that nothing is created.
    /// </summary>
    /// <param name="json">The request body.</param>
    [Theory(Timeout = 60000)]
    [InlineData("""{"id":""")]
    [InlineData("not json")]
    [InlineData("""{"files": 5}""")]
    public async Task ApiEndpoints_CreateReview_MalformedJson_Answers400Problem(string json)
    {
        // Arrange: a project and a token
        await using var app = await StartAsync();
        var token = await CreateTokenAsync(app);
        var project = await app.Services.GetRequiredService<ProjectService>().CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        using var client = ClientFor(app, token.Value);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act: send the body
        var response = await client.PostAsync($"/api/projects/{project.Id}/reviews", content, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: 400 as a problem document, with no exception text or stack trace, and no review
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = System.Text.Json.JsonDocument.Parse(text);
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain("System.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", text, StringComparison.Ordinal);
        Assert.Empty(await app.Services.GetRequiredService<ReviewService>().ListAsync(project.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a request body that does not say it is JSON is refused with 415 and a problem document, with a
    ///     content type of another kind or with none, even when the body is a valid review in JSON, and that nothing is
    ///     created: no review and no audit entry for a review.
    /// </summary>
    /// <param name="contentType">The content type of the body; <c>null</c> for none.</param>
    [Theory(Timeout = 60000)]
    [InlineData(null)]
    [InlineData("text/plain")]
    public async Task ApiEndpoints_CreateReview_NotJsonContentType_Answers415Problem(string? contentType)
    {
        // Arrange: a project and a token
        await using var app = await StartAsync();
        var token = await CreateTokenAsync(app);
        var project = await app.Services.GetRequiredService<ProjectService>().CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        using var client = ClientFor(app, token.Value);
        var auditBefore = (await AuditAsync(app)).Count;

        // A valid review body, so only the missing JSON content type can stop it
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            id = "X-1",
            title = "T",
            files = new[] { UploadOf("a.txt", 3) }
        });
        using var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(json));
        if (contentType is not null)
        {
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        }

        // Act: send the body without the JSON content type
        var response = await client.PostAsync($"/api/projects/{project.Id}/reviews", content, TestContext.Current.CancellationToken);

        // Assert: 415 as a problem document, no review, and no new audit entry
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await app.Services.GetRequiredService<ReviewService>().ListAsync(project.Id, TestContext.Current.CancellationToken));
        Assert.Equal(auditBefore, (await AuditAsync(app)).Count);
    }

    /// <summary>
    ///     Arranges one complete review of a project, signed by its one reviewer, that was planned from the review-set
    ///     <c>Core-Logic</c>, next to a complete review without a review-set id.
    /// </summary>
    /// <param name="app">The running application.</param>
    /// <returns>The project number.</returns>
    private static async Task<int> ArrangeSignedReviewWithSetIdAsync(WebApplication app)
    {
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        var store = app.Services.GetRequiredService<IReviewStore>();
        await EvidenceTestSupport.AddReviewAsync(store, TimeProvider.System, projectId, "REV-2", "Core-Logic", "one\ntwo\n"u8.ToArray());
        await EvidenceTestSupport.AnswerAllPassAsync(app.Services.GetRequiredService<ChecklistService>(), projectId, "REV-2");
        var annId = (await app.Services.GetRequiredService<UserService>().GetAsync("ann", TestContext.Current.CancellationToken))!.Id;
        var signing = app.Services.GetRequiredService<SigningService>();
        var slotId = await EvidenceTestSupport.AddSlotAsync(signing, projectId, "REV-2", annId);
        await EvidenceTestSupport.SignAsync(signing, projectId, "REV-2", slotId, EvidenceTestSupport.Ann, TestBrowser.Password);
        return projectId;
    }

    /// <summary>
    ///     Builds the JSON of one uploaded file of zero bytes.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="size">The number of bytes.</param>
    /// <returns>The file as the API takes it.</returns>
    private static Dictionary<string, string> UploadOf(string path, long size)
    {
        return new Dictionary<string, string> { ["path"] = path, ["contentBase64"] = Convert.ToBase64String(new byte[size]) };
    }

    /// <summary>
    ///     A request body of zero bytes that is written in blocks, so a body of tens of megabytes is never held in memory.
    /// </summary>
    private sealed class ZeroBodyContent : HttpContent
    {
        /// <summary>
        ///     The number of bytes of the body.
        /// </summary>
        private readonly long _length;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ZeroBodyContent"/> class.
        /// </summary>
        /// <param name="length">The number of bytes of the body.</param>
        public ZeroBodyContent(long length)
        {
            _length = length;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }

        /// <inheritdoc />
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var block = new byte[64 * 1024];
            for (var written = 0L; written < _length; written += block.Length)
            {
                await stream.WriteAsync(block.AsMemory(0, (int)Math.Min(block.Length, _length - written)));
            }
        }

        /// <inheritdoc />
        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
    }
}
