using System.Net;
using DemaConsulting.ReviewCenter.Api;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Tests.Evidence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static DemaConsulting.ReviewCenter.Tests.Api.ApiTestSupport;

namespace DemaConsulting.ReviewCenter.Tests.Api;

/// <summary>
///     Tests, over HTTP, of the anonymous access that a project can switch on for its evidence index and its
///     attestation PDFs: who is served with each switch on and off, that the two routes are independent, that no other
///     route of the API opens up, and that nothing about a project leaks to a caller without a token.
/// </summary>
public class AnonymousEvidenceAccessTests
{
    /// <summary>
    ///     The address (without the project number) of the evidence index.
    /// </summary>
    private const string IndexRoute = "/api/projects/{p}/evidence/index.json";

    /// <summary>
    ///     The address (without the project number) of the attestation PDF of the arranged review.
    /// </summary>
    private const string PdfRoute = "/api/projects/{p}/reviews/REV-1/attestation.pdf";

    /// <summary>
    ///     The number of a project that does not exist.
    /// </summary>
    private const int MissingProject = 9999;

    /// <summary>
    ///     Test that a project with both switches off (the default) refuses an anonymous caller on both routes with the
    ///     same generic 401 as for a project that does not exist, so that nothing tells whether the project exists.
    /// </summary>
    /// <param name="route">The route.</param>
    [Theory(Timeout = 120000)]
    [InlineData(IndexRoute)]
    [InlineData(PdfRoute)]
    public async Task AnonymousEvidenceAccess_SwitchOff_Anonymous_AnswersLikeAMissingProject(string route)
    {
        // Arrange: a project with a complete review and the default settings
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        using var client = ClientFor(app, null);

        // Act
        var existing = await GetAsync(client, route, projectId);
        var missing = await GetAsync(client, route, MissingProject);
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();

        // Assert: 401 for both, with identical bodies and the challenge header
        Assert.Equal(HttpStatusCode.Unauthorized, existing.Status);
        Assert.Equal(missing.Status, existing.Status);
        Assert.Equal(missing.Body, existing.Body);
        Assert.Equal(missing.Challenge, existing.Challenge);
        Assert.Contains("Bearer", existing.Challenge, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test the matrix of who is served by one route with its own switch on and off: an anonymous caller only when
    ///     the switch is on, a member always, a non-member never (404), and a disabled user or a wrong token never (401),
    ///     also when the switch is on.
    /// </summary>
    /// <param name="route">The route.</param>
    [Theory(Timeout = 120000)]
    [InlineData(IndexRoute)]
    [InlineData(PdfRoute)]
    public async Task AnonymousEvidenceAccess_Matrix_ServesOnlyWhoMay(string route)
    {
        // Arrange: a complete review, a member, a non-member, a disabled member and a wrong token
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        await EnsureUserAsync(app, "out", UserRole.Reviewer);
        await EnsureUserAsync(app, "dis", UserRole.Reviewer);
        var disabledUser = (await app.Services.GetRequiredService<UserService>()
            .GetAsync("dis", TestContext.Current.CancellationToken))!;
        await app.Services.GetRequiredService<IReviewStore>()
            .AddMembershipAsync(disabledUser.Id, projectId, TestContext.Current.CancellationToken);
        var member = await CreateTokenAsync(app, "bob");
        var outsider = await CreateTokenAsync(app, "out");
        var disabled = await CreateTokenAsync(app, "dis");
        await app.Services.GetRequiredService<UserService>()
            .SetEnabledAsync("dis", false, TestContext.Current.CancellationToken);
        var isIndex = route == IndexRoute;

        foreach (var on in new[] { false, true })
        {
            // Arrange: set this route's own switch
            await SetAsync(app, projectId, s => isIndex ? s with { AnonymousIndex = on } : s with { AnonymousAttestations = on });

            // Act and assert, caller by caller
            using var anonymous = ClientFor(app, null);
            using var memberClient = ClientFor(app, member.Value);
            using var basicMember = ClientFor(app, member.Value, basic: true);
            using var outsiderClient = ClientFor(app, outsider.Value);
            using var disabledClient = ClientFor(app, disabled.Value);
            using var wrongClient = ClientFor(app, "rcpat_" + new string('A', 43));

            var anonymousAnswer = await GetAsync(anonymous, route, projectId);
            Assert.Equal(on ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, anonymousAnswer.Status);
            Assert.Equal(HttpStatusCode.OK, (await GetAsync(memberClient, route, projectId)).Status);
            Assert.Equal(HttpStatusCode.OK, (await GetAsync(basicMember, route, projectId)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(outsiderClient, route, projectId)).Status);
            Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(disabledClient, route, projectId)).Status);
            Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(wrongClient, route, projectId)).Status);

            // Assert: a served answer is never cached
            if (on)
            {
                Assert.Equal("no-store", anonymousAnswer.CacheControl);
            }
        }
    }

    /// <summary>
    ///     Test that the two switches are independent: with only the index open the PDF is still refused to an
    ///     anonymous caller, and the other way round.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AnonymousEvidenceAccess_OneSwitchOn_DoesNotOpenTheOtherRoute()
    {
        // Arrange
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        using var client = ClientFor(app, null);

        // Act and assert: only the index
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        await SetAsync(app, projectId, s => s with { AnonymousIndex = true });
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, IndexRoute, projectId)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, PdfRoute, projectId)).Status);

        // Act and assert: only the PDF
        await SetAsync(app, projectId, s => s with { AnonymousIndex = false, AnonymousAttestations = true });
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, IndexRoute, projectId)).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, PdfRoute, projectId)).Status);
    }

    /// <summary>
    ///     Test that a switch of one project does not open another project.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AnonymousEvidenceAccess_SwitchOn_OpensOnlyItsOwnProject()
    {
        // Arrange: two projects, the first open
        await using var app = await StartAsync();
        var first = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        var second = (await app.Services.GetRequiredService<ProjectService>()
            .CreateAsync("Gemini", null, TestContext.Current.CancellationToken)).Id;
        await SetAsync(app, first, s => s with { AnonymousIndex = true });
        using var client = ClientFor(app, null);

        // Act and assert
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, IndexRoute, first)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, IndexRoute, second)).Status);
    }

    /// <summary>
    ///     Test that the anonymous index lists a complete review of a review set with its attestation address, and that
    ///     an anonymous download of the PDF is a signed PDF that is audited as an anonymous request.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AnonymousEvidenceAccess_BothOn_ServesIndexEntryAndSignedPdfAndAuditsAnonymous()
    {
        // Arrange
        await using var app = await StartAsync();
        var projectId = await ApiEndpointsTests.ArrangeSignedReviewWithSetIdAsync(app);
        await SetAsync(app, projectId, s => s with { AnonymousIndex = true, AnonymousAttestations = true });
        using var client = ClientFor(app, null);

        // Act
        var index = await client.GetAsync($"/api/projects/{projectId}/evidence/index.json", TestContext.Current.CancellationToken);
        using var json = await JsonAsync(index);
        var file = Assert.Single(json.RootElement.GetProperty("reviews").EnumerateArray()).GetProperty("file").GetString()!;
        var pdf = await client.GetAsync(new Uri(file), TestContext.Current.CancellationToken);
        var bytes = await pdf.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.True(pdf.Headers.CacheControl?.NoStore);
        Assert.NotNull(new PdfInspector(bytes).VerifySignature());
        var entry = Assert.Single(
            await AuditAsync(app), e => e.Action == AttestationService.AuditAction);
        Assert.Equal("anonymous", entry.ActorName);
        Assert.Equal(projectId, entry.ProjectId);
    }

    /// <summary>
    ///     Test that an anonymous caller cannot tell a review that is not complete from one that does not exist: both
    ///     are answered with the same 404 and the same body, nothing is recorded, while a member still gets the 409
    ///     for the incomplete review and the 404 for the unknown one.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AnonymousEvidenceAccess_AttestationPdf_IncompleteReview_AnswersLikeMissingReview()
    {
        // Arrange: a review nobody signed in a project that opens the PDFs, and a member
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: false);
        await SetAsync(app, projectId, s => s with { AnonymousAttestations = true });
        var member = await CreateTokenAsync(app, "bob");
        using var anonymous = ClientFor(app, null);
        using var memberClient = ClientFor(app, member.Value);
        const string incompleteRoute = "/api/projects/{p}/reviews/REV-1/attestation.pdf";
        const string missingRoute = "/api/projects/{p}/reviews/NOPE/attestation.pdf";

        // Act
        var incomplete = await GetAsync(anonymous, incompleteRoute, projectId);
        var missing = await GetAsync(anonymous, missingRoute, projectId);
        var memberIncomplete = await GetAsync(memberClient, incompleteRoute, projectId);
        var memberMissing = await GetAsync(memberClient, missingRoute, projectId);
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();

        // Assert: the same 404, body and headers for the anonymous caller; nothing recorded
        Assert.Equal(HttpStatusCode.NotFound, incomplete.Status);
        Assert.Equal(missing.Status, incomplete.Status);
        Assert.Equal(missing.Body, incomplete.Body);
        Assert.Equal("no-store", incomplete.CacheControl);
        Assert.Equal(missing.CacheControl, incomplete.CacheControl);
        Assert.DoesNotContain(await AuditAsync(app), e => e.Action == AttestationService.AuditAction);

        // Assert: a member is still told that the review is not complete
        Assert.Equal(HttpStatusCode.Conflict, memberIncomplete.Status);
        Assert.Equal(HttpStatusCode.NotFound, memberMissing.Status);
    }

    /// <summary>
    ///     Test that with both switches on an anonymous caller is still refused by every other route of the API, and
    ///     that an unknown address is answered like for everyone.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="route">The address.</param>
    /// <param name="expected">The expected status.</param>
    [Theory(Timeout = 120000)]
    [InlineData("GET", "/api/projects", 401)]
    [InlineData("GET", "/api/projects/{p}/reviews", 401)]
    [InlineData("GET", "/api/projects/{p}/reviews/REV-1", 401)]
    [InlineData("POST", "/api/projects/{p}/reviews", 401)]
    [InlineData("GET", "/api/projects/{p}/reviews/REV-1/attestation.pdf/extra", 404)]
    [InlineData("GET", "/api/projects/{p}/evidence", 404)]
    [InlineData("POST", "/api/projects/{p}/evidence/index.json", 404)]
    [InlineData("POST", "/api/projects/{p}/reviews/REV-1/attestation.pdf", 404)]
    public async Task AnonymousEvidenceAccess_BothOn_OtherRoutes_StayClosed(string method, string route, int expected)
    {
        // Arrange
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        await SetAsync(app, projectId, s => s with { AnonymousIndex = true, AnonymousAttestations = true });
        using var client = ClientFor(app, null);
        using var request = new HttpRequestMessage(new HttpMethod(method), route.Replace("{p}", projectId.ToString()));
        if (method == "POST")
        {
            request.Content = Json(new { id = "X-1", title = "T", files = Array.Empty<object>() });
        }

        // Act
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, (int)response.StatusCode);
    }

    /// <summary>
    ///     Test that the page route of the attestation (cookie, not token) is not opened to anonymous callers by the
    ///     switch: it still sends a caller who is not signed in to the sign-in page.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AnonymousEvidenceAccess_BothOn_AttestationPageRoute_StillNeedsSignIn()
    {
        // Arrange
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        await SetAsync(app, projectId, s => s with { AnonymousIndex = true, AnonymousAttestations = true });
        using var client = ClientFor(app, null);

        // Act
        var response = await client.GetAsync(
            $"/projects/{projectId}/reviews/REV-1/attestation.pdf", TestContext.Current.CancellationToken);

        // Assert: not served; sent to the sign-in page
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("login", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a request is refused as soon as a switch is turned off, without a restart.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AnonymousEvidenceAccess_SwitchTurnedOff_RefusesAtOnce()
    {
        // Arrange: an open index that was served
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        await SetAsync(app, projectId, s => s with { AnonymousIndex = true });
        using var client = ClientFor(app, null);
        var before = await GetAsync(client, IndexRoute, projectId);

        // Act
        await SetAsync(app, projectId, s => s with { AnonymousIndex = false });
        var after = await GetAsync(client, IndexRoute, projectId);
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();

        // Assert
        Assert.Equal(HttpStatusCode.OK, before.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, after.Status);
    }

    /// <summary>
    ///     Test that the anonymous callers of the two routes are limited per address with a 429 and
    ///     <c>Retry-After</c>, and that a caller with a token is not limited.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AnonymousEvidenceAccess_TooManyAnonymousRequests_Answers429WithRetryAfter()
    {
        // Arrange
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        await SetAsync(app, projectId, s => s with { AnonymousIndex = true });
        var token = await CreateTokenAsync(app, "bob");
        using var anonymous = ClientFor(app, null);
        using var member = ClientFor(app, token.Value);

        // Act: one more request than the limit allows, then a token request
        HttpResponseMessage? last = null;
        for (var i = 0; i <= AnonymousRateLimit.Limit; i++)
        {
            last?.Dispose();
            last = await anonymous.GetAsync(
                $"/api/projects/{projectId}/evidence/index.json", TestContext.Current.CancellationToken);
        }

        var withToken = await member.GetAsync(
            $"/api/projects/{projectId}/evidence/index.json", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(last);
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        Assert.NotNull(last.Headers.RetryAfter?.Delta);
        Assert.True(last.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
        last.Dispose();
    }

    /// <summary>
    ///     Test that the limiter counts per address and starts a new window after the old one. Its bound on memory is
    ///     tested in <see cref="AnonymousRateLimitTests"/>.
    /// </summary>
    [Fact]
    public void AnonymousRateLimit_TryAcquire_CountsPerAddressAndWindow()
    {
        // Arrange
        var clock = new MovableTimeProvider(new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var limiter = new AnonymousRateLimit(clock);

        // Act: use up the budget of one address
        for (var i = 0; i < AnonymousRateLimit.Limit; i++)
        {
            Assert.True(limiter.TryAcquire("10.0.0.1", out _));
        }

        var refused = limiter.TryAcquire("10.0.0.1", out var retryAfter);
        var other = limiter.TryAcquire("10.0.0.2", out _);
        var unknownFirst = limiter.TryAcquire(null, out _);
        clock.Advance(AnonymousRateLimit.Window + TimeSpan.FromSeconds(1));
        var afterWindow = limiter.TryAcquire("10.0.0.1", out _);

        // Assert
        Assert.False(refused);
        Assert.True(retryAfter > TimeSpan.Zero && retryAfter <= AnonymousRateLimit.Window);
        Assert.True(other);
        Assert.True(unknownFirst);
        Assert.True(afterWindow);
    }

    /// <summary>
    ///     Test that exactly two endpoints of the API can be reached without a token, that they are the evidence index
    ///     and the attestation PDF, and that every other endpoint of the API requires the token policy.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AnonymousEvidenceAccess_Endpoints_OnlyTheTwoRoutesUseTheOpenPolicies()
    {
        // Arrange
        await using var app = await StartAsync();
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api", StringComparison.Ordinal))
            .ToList();

        // Act
        var found = endpoints
            .Select(e => (
                Policies: e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList(),
                Anonymous: e.Metadata.GetMetadata<IAllowAnonymous>() is not null,
                Route: e.RoutePattern.RawText!))
            .ToList();

        // Assert: the open policies are used by exactly the two routes
        var open = found
            .Where(v => v.Policies.Contains(ApiTokenAuthentication.EvidenceIndexPolicyName)
                        || v.Policies.Contains(ApiTokenAuthentication.AttestationPolicyName))
            .Select(v => v.Route)
            .Order()
            .ToList();
        Assert.Equal(
            [
                "/api/projects/{projectId:int}/evidence/index.json",
                "/api/projects/{projectId:int}/reviews/{reviewId}/attestation.pdf"
            ],
            open);

        // Assert: everything else needs the token policy, except the document and the catch-all 404
        foreach (var value in found.Where(p => !open.Contains(p.Route)))
        {
            Assert.True(
                value.Anonymous || value.Policies.Contains(ApiTokenAuthentication.PolicyName),
                $"{value.Route} is neither behind the Api policy nor deliberately open.");
        }

        Assert.All(
            found.Where(v => v.Anonymous).Select(v => v.Route).Distinct(),
            route => Assert.True(
                route is "/api/{**rest}" || route.StartsWith("/api/openapi", StringComparison.Ordinal),
                $"{route} is open to anonymous callers."));
    }

    /// <summary>
    ///     Changes the evidence settings of a project as a system administrator.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="projectId">The project number.</param>
    /// <param name="change">Computes the new settings from the current ones.</param>
    /// <returns>A task that completes when the settings are stored.</returns>
    private static async Task SetAsync(
        WebApplication app,
        int projectId,
        Func<EvidenceSettings, EvidenceSettings> change)
    {
        var service = app.Services.GetRequiredService<EvidenceSettingsService>();
        var current = await service.GetAsync(TestActors.Ann, projectId, TestContext.Current.CancellationToken);
        await service.SetAsync(TestActors.Ann, projectId, current, change(current), TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Sends a GET request to a route.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="route">The route; <c>{p}</c> stands for the project number.</param>
    /// <param name="projectId">The project number.</param>
    /// <returns>The status, the body, the challenge header and the cache header of the answer.</returns>
    private static async Task<(HttpStatusCode Status, byte[] Body, string Challenge, string? CacheControl)> GetAsync(
        HttpClient client,
        string route,
        int projectId)
    {
        using var response = await client.GetAsync(
            route.Replace("{p}", projectId.ToString()), TestContext.Current.CancellationToken);
        return (
            response.StatusCode,
            await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken),
            string.Join(" | ", response.Headers.WwwAuthenticate.Select(h => h.ToString())),
            response.Headers.CacheControl?.ToString());
    }
}
