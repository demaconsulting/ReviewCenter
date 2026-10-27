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
using Microsoft.Extensions.DependencyInjection;
using static DemaConsulting.ReviewCenter.Tests.Api.ApiTestSupport;

namespace DemaConsulting.ReviewCenter.Tests.Api;

/// <summary>
///     Tests for the personal access token authentication of the API: accepted and refused tokens, the generic
///     answer, the throttle, the audit trail, the last-used time, and the separation from the cookie sign-in.
/// </summary>
public class ApiTokenAuthenticationTests
{
    /// <summary>
    ///     Test that a valid token authenticates the owner, and that using it records the last-used time.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiTokenAuthentication_Request_ValidToken_AuthenticatesAndRecordsLastUsed()
    {
        // Arrange: an application with a token that was never used
        await using var app = await StartAsync();
        var created = await CreateTokenAsync(app);
        using var client = ClientFor(app, created.Value);
        var service = app.Services.GetRequiredService<PersonalAccessTokenService>();
        Assert.Null(Assert.Single(await service.ListAsync(TestActors.Ann, TestContext.Current.CancellationToken)).LastUsed);

        // Act: call the API
        var response = await client.GetAsync("/api/projects", TestContext.Current.CancellationToken);

        // Assert: accepted, and the use is recorded
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(Assert.Single(await service.ListAsync(TestActors.Ann, TestContext.Current.CancellationToken)).LastUsed);
    }

    /// <summary>
    ///     Test that a request without a usable token, or with another kind of credential, gets 401 with the bearer
    ///     and Basic challenge and a problem document.
    /// </summary>
    /// <param name="authorization">The <c>Authorization</c> header value; <c>null</c> for none.</param>
    [Theory(Timeout = 60000)]
    [InlineData(null)]
    [InlineData("Basic YW5uOnNlY3JldA==")]
    [InlineData("Basic !!not-base64!!")]
    [InlineData("Basic YW5u")]
    [InlineData("Basic YW5uOg==")]
    [InlineData("Digest abc")]
    [InlineData("Bearer")]
    public async Task ApiTokenAuthentication_Request_NoUsableToken_Answers401WithChallenge(string? authorization)
    {
        // Arrange: an application and a request with the given header
        await using var app = await StartAsync();
        using var client = ClientFor(app, null);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/projects");
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        // Act: send it
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert: 401, a challenge for both schemes, a problem document
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.Select(h => h.Scheme));
        var basic = Assert.Single(response.Headers.WwwAuthenticate, h => h.Scheme == "Basic");
        Assert.Contains("realm=\"ReviewCenter\"", basic.Parameter, StringComparison.Ordinal);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    ///     Test that HTTP Basic credentials authenticate the owner when the password is a token, whatever the user
    ///     name is (ReviewMark sends Basic only), and that the use is recorded.
    /// </summary>
    /// <param name="username">The user name to send.</param>
    [Theory(Timeout = 60000)]
    [InlineData("reviewmark")]
    [InlineData("")]
    public async Task ApiTokenAuthentication_Request_BasicWithTokenAsPassword_AuthenticatesAsOwner(string username)
    {
        // Arrange: an application with a token that was never used
        await using var app = await StartAsync();
        var created = await CreateTokenAsync(app);
        using var client = ClientFor(app, null);
        client.DefaultRequestHeaders.Authorization = BasicHeader(username, created.Value);
        var service = app.Services.GetRequiredService<PersonalAccessTokenService>();

        // Act: call the API
        var response = await client.GetAsync("/api/projects", TestContext.Current.CancellationToken);

        // Assert: accepted as the owner of the token, and the use is recorded
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(Assert.Single(await service.ListAsync(TestActors.Ann, TestContext.Current.CancellationToken)).LastUsed);
    }

    /// <summary>
    ///     Test that a wrong Basic password gets exactly the answer of a wrong bearer token, is recorded in the audit
    ///     trail without its value, and is throttled like any other refused token.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiTokenAuthentication_Request_BasicWithWrongPassword_GetsTheSameGeneric401AndIsThrottledAndAudited()
    {
        // Arrange: a limit of three attempts, a good token, and a wrong token
        await using var app = await StartAsync(new Dictionary<string, string?>
        {
            ["Identity:Throttle:MaxFailuresPerName"] = "3",
            ["Identity:Throttle:MaxFailuresPerAddress"] = "1000"
        });
        var good = await CreateTokenAsync(app, "ann");
        var wrong = "rcpat_" + new string('A', 43);
        using var bearer = ClientFor(app, wrong);
        using var basic = ClientFor(app, wrong, basic: true);

        // Act: one refused attempt with each scheme
        var bearerAnswer = await bearer.GetAsync("/api/projects", TestContext.Current.CancellationToken);
        var basicAnswer = await basic.GetAsync("/api/projects", TestContext.Current.CancellationToken);

        // Assert: the same 401, the same challenge and the same body
        Assert.Equal(HttpStatusCode.Unauthorized, basicAnswer.StatusCode);
        Assert.Equal(bearerAnswer.Headers.WwwAuthenticate.ToString(), basicAnswer.Headers.WwwAuthenticate.ToString());
        Assert.Equal(
            await bearerAnswer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await basicAnswer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Act: one more refused attempt, then the good token with Basic
        await basic.GetAsync("/api/projects", TestContext.Current.CancellationToken);
        using var goodBasic = ClientFor(app, good.Value, basic: true);
        var throttled = await goodBasic.GetAsync("/api/projects", TestContext.Current.CancellationToken);

        // Assert: throttled as a problem document even for the good token, recorded without the value
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        var entries = await AuditAsync(app);
        Assert.Contains(entries, e => e.Action == "token.use-failed");
        Assert.Contains(entries, e => e.Action == "token.use-throttled");
        Assert.All(entries, e => Assert.DoesNotContain(wrong, e.Detail, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that every kind of refused token (unknown, malformed, revoked, disabled owner) gets the same answer,
    ///     which does not say why the token was refused.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiTokenAuthentication_Request_RefusedTokens_AllGetTheSameGenericAnswer()
    {
        // Arrange: a revoked token, a token of a disabled owner, and a good token as the control
        await using var app = await StartAsync(new Dictionary<string, string?>
        {
            ["Identity:Throttle:MaxFailuresPerName"] = "100",
            ["Identity:Throttle:MaxFailuresPerAddress"] = "100"
        });
        var revoked = await CreateTokenAsync(app, "ann");
        var disabled = await CreateTokenAsync(app, "bob");
        var good = await CreateTokenAsync(app, "cyd");
        await app.Services.GetRequiredService<PersonalAccessTokenService>()
            .RevokeAsync(TestActors.Ann, revoked.Token.Id, TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<UserService>().SetEnabledAsync("bob", false, TestContext.Current.CancellationToken);
        var presented = new[]
        {
            revoked.Value,
            disabled.Value,
            "rcpat_" + new string('A', 43),
            "not-a-token",
            good.Value + "x"
        };

        // Act: present each of them
        var bodies = new List<string>();
        foreach (var value in presented)
        {
            using var client = ClientFor(app, value);
            var response = await client.GetAsync("/api/projects", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            bodies.Add(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        // Assert: one and the same answer, and the control token still works
        Assert.Single(bodies.Distinct());
        using var control = ClientFor(app, good.Value);
        Assert.Equal(HttpStatusCode.OK, (await control.GetAsync("/api/projects", TestContext.Current.CancellationToken)).StatusCode);
    }

    /// <summary>
    ///     Test that a token stops working when it expires.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiTokenAuthentication_Request_ExpiredToken_Answers401()
    {
        // Arrange: an application whose clock a test moves, and a 30-day token
        var clock = new MovableTimeProvider(DateTimeOffset.UtcNow);
        await using var app = await StartAsync(configureServices: services => services.AddSingleton<TimeProvider>(clock));
        var created = await CreateTokenAsync(app, days: 30);
        using var client = ClientFor(app, created.Value);
        var before = await client.GetAsync("/api/projects", TestContext.Current.CancellationToken);

        // Act: the 30 days pass
        clock.Advance(TimeSpan.FromDays(30));
        var after = await client.GetAsync("/api/projects", TestContext.Current.CancellationToken);

        // Assert: accepted before, refused after
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    /// <summary>
    ///     Test that the session cookie of a signed-in person does not authenticate the API.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiTokenAuthentication_Request_SessionCookie_IsNotAccepted()
    {
        // Arrange: a browser that is signed in
        await using var app = await StartAsync();
        using var browser = await TestBrowser.SignedInAsync(app, "ann");
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/account", TestContext.Current.CancellationToken)).StatusCode);

        // Act: call the API with the cookie only, and with the cookie and Basic credentials that are not a token
        var response = await browser.Client.GetAsync("/api/projects", TestContext.Current.CancellationToken);
        using var withBasic = new HttpRequestMessage(HttpMethod.Get, "/api/projects");
        withBasic.Headers.Authorization = BasicHeader("ann", TestBrowser.Password);
        var basicResponse = await browser.Client.SendAsync(withBasic, TestContext.Current.CancellationToken);

        // Assert: refused like a request without credentials
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.Select(h => h.Scheme));
        Assert.Equal(HttpStatusCode.Unauthorized, basicResponse.StatusCode);
    }

    /// <summary>
    ///     Test that a token does not sign anybody in to the web pages: the pages, the attestation page route and
    ///     the sign-out endpoint all treat the request as signed out.
    /// </summary>
    /// <param name="route">The page route to request.</param>
    /// <param name="basic">Whether the token is sent as the password of HTTP Basic credentials.</param>
    [Theory(Timeout = 60000)]
    [InlineData("/account", false)]
    [InlineData("/projects/1/reviews/REV-1/attestation.pdf", false)]
    [InlineData("/account", true)]
    [InlineData("/projects/1/reviews/REV-1/attestation.pdf", true)]
    public async Task ApiTokenAuthentication_Request_TokenOnWebPage_IsNotSignedIn(string route, bool basic)
    {
        // Arrange: an application with a token and a complete review
        await using var app = await StartAsync();
        await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        var created = await CreateTokenAsync(app, "ann");
        using var client = ClientFor(app, created.Value, basic);

        // Act: request the page with the token
        var response = await client.GetAsync(route, TestContext.Current.CancellationToken);

        // Assert: sent to the login page, as for any signed-out request
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a token does not change any answer of the sign-in and sign-out endpoints and of the web socket
    ///     negotiation of the pages: they answer the same with and without a token, and record nothing for the owner.
    /// </summary>
    /// <param name="route">The address to post to.</param>
    /// <param name="basic">Whether the token is sent as the password of HTTP Basic credentials.</param>
    [Theory(Timeout = 60000)]
    [InlineData("/_blazor/negotiate?negotiateVersion=1", false)]
    [InlineData("/auth/logout", false)]
    [InlineData("/auth/login", false)]
    [InlineData("/_blazor/negotiate?negotiateVersion=1", true)]
    [InlineData("/auth/logout", true)]
    [InlineData("/auth/login", true)]
    public async Task ApiTokenAuthentication_Request_TokenOnAuthAndBlazorRoutes_IsNotSignedIn(string route, bool basic)
    {
        // Arrange: an application with a token, the audit trail so far, and a client with and one without the token
        await using var app = await StartAsync();
        var created = await CreateTokenAsync(app, "ann");
        var before = (await AuditAsync(app)).Count;
        using var withToken = ClientFor(app, created.Value, basic);
        using var withoutToken = ClientFor(app, null);

        // Act: post to the route with and without the token
        var answerWith = await withToken.PostAsync(route, new FormUrlEncodedContent([]), TestContext.Current.CancellationToken);
        var answerWithout = await withoutToken.PostAsync(route, new FormUrlEncodedContent([]), TestContext.Current.CancellationToken);

        // Assert: the token changes nothing about the answer
        Assert.Equal(answerWithout.StatusCode, answerWith.StatusCode);
        Assert.Equal(answerWithout.Headers.Location?.OriginalString, answerWith.Headers.Location?.OriginalString);
        Assert.Empty(answerWith.Headers.WwwAuthenticate);

        // Assert: nothing was recorded as done by the owner of the token or by the token
        var entries = await AuditAsync(app);
        Assert.Equal(before, entries.Count);
        Assert.DoesNotContain(entries, e => e.Action.StartsWith("token.use", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that no outcome of token authentication (accepted, refused as unknown, refused as revoked) writes the
    ///     token, its secret part, or any text with the token prefix to the application log.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiTokenAuthentication_Request_AnyOutcome_NeverLogsTheToken()
    {
        // Arrange: an application that keeps its log, one token that is used and one that is revoked
        var logs = new CapturingLoggerProvider();
        await using var app = await StartAsync(logs: logs);
        var used = await CreateTokenAsync(app, "ann");
        var revoked = await CreateTokenAsync(app, "ann");
        var unknown = "rcpat_" + new string('B', 43);
        await app.Services.GetRequiredService<PersonalAccessTokenService>()
            .RevokeAsync(TestActors.Ann, revoked.Token.Id, TestContext.Current.CancellationToken);

        // Act: present each of them as a bearer token, one is accepted and two are refused; then as Basic passwords
        var statuses = new List<HttpStatusCode>();
        foreach (var basic in new[] { false, true })
        {
            foreach (var value in new[] { used.Value, unknown, revoked.Value })
            {
                using var client = ClientFor(app, value, basic);
                statuses.Add((await client.GetAsync("/api/projects", TestContext.Current.CancellationToken)).StatusCode);
            }
        }

        // Assert: the outcomes happened with both schemes
        Assert.Equal(
            [
                HttpStatusCode.OK, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
                HttpStatusCode.OK, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized
            ],
            statuses);

        // Assert: the log is not empty (so the check is not vacuous) and nothing in it has a token, the prefix, or
        // the Base64 text of the Basic credentials
        var entries = logs.Entries;
        Assert.NotEmpty(entries);
        var forbidden = new[] { used.Value, revoked.Value, unknown }
            .SelectMany(v => new[] { v, v["rcpat_".Length..], BasicHeader("reviewmark", v).Parameter! })
            .Append("rcpat_")
            .ToList();
        foreach (var entry in entries)
        {
            var texts = new[] { entry.Message }.Concat(entry.Properties.Select(p => $"{p.Key}={p.Value}"));
            foreach (var text in texts)
            {
                Assert.All(forbidden, f => Assert.DoesNotContain(f, text, StringComparison.Ordinal));
            }
        }
    }

    /// <summary>
    ///     Test that too many refused tokens from one address are throttled with 429, even for a good token, and
    ///     that the throttle does not stop people signing in with a password.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiTokenAuthentication_Request_TooManyRefusals_AnswersTooManyRequests()
    {
        // Arrange: a limit of three attempts, a good token, and a person who can sign in
        await using var app = await StartAsync(new Dictionary<string, string?>
        {
            ["Identity:Throttle:MaxFailuresPerName"] = "3",
            ["Identity:Throttle:MaxFailuresPerAddress"] = "1000"
        });
        var good = await CreateTokenAsync(app, "ann");
        using var bad = ClientFor(app, "rcpat_" + new string('A', 43));
        using var goodClient = ClientFor(app, good.Value);
        var statuses = new List<HttpStatusCode>();

        // Act: three refused attempts, then the good token
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await bad.GetAsync("/api/projects", TestContext.Current.CancellationToken)).StatusCode);
        }

        var throttled = await goodClient.GetAsync("/api/projects", TestContext.Current.CancellationToken);

        // Assert: three 401, then 429 as a problem document even for the good token
        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized], statuses);
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.Equal("application/problem+json", throttled.Content.Headers.ContentType?.MediaType);

        // Assert: signing in with a password is not affected
        using var browser = await TestBrowser.SignedInAsync(app, "ann");
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/account", TestContext.Current.CancellationToken)).StatusCode);
    }

    /// <summary>
    ///     Test that a refused token is recorded in the audit trail with the reason and, for a stored token, its
    ///     display id, that repeats are recorded once, and that the token value is never recorded.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiTokenAuthentication_Request_RefusedToken_IsAuditedOnceWithoutTheValue()
    {
        // Arrange: a revoked token
        await using var app = await StartAsync();
        var created = await CreateTokenAsync(app, "ann");
        await app.Services.GetRequiredService<PersonalAccessTokenService>()
            .RevokeAsync(TestActors.Ann, created.Token.Id, TestContext.Current.CancellationToken);
        using var client = ClientFor(app, created.Value);

        // Act: present it three times
        for (var i = 0; i < 3; i++)
        {
            await client.GetAsync("/api/projects", TestContext.Current.CancellationToken);
        }

        // Assert: one failure entry that names the reason and the token's display id, and no entry has the value
        var entries = await AuditAsync(app);
        var failure = Assert.Single(entries, e => e.Action == "token.use-failed");
        Assert.Contains("revoked", failure.Detail, StringComparison.Ordinal);
        Assert.Contains(created.Token.DisplayId, failure.Detail, StringComparison.Ordinal);
        Assert.All(entries, e => Assert.DoesNotContain(created.Value, e.Detail, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that the person behind a token is the one the token was made for, with their current role, so the
    ///     things the token creates carry their name.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiTokenAuthentication_Request_ValidToken_ActsAsTheOwner()
    {
        // Arrange: a token of ann and a project
        await using var app = await StartAsync();
        var created = await CreateTokenAsync(app, "ann");
        await app.Services.GetRequiredService<ProjectService>().CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        using var client = ClientFor(app, created.Value);
        var body = new
        {
            id = "REV-1",
            title = "First",
            files = new[] { new { path = "a.txt", contentBase64 = Convert.ToBase64String("one\n"u8.ToArray()) } }
        };

        // Act: create a review with the token
        var response = await client.PostAsync("/api/projects/1/reviews", ApiTestSupport.Json(body), TestContext.Current.CancellationToken);

        // Assert: the review and its audit entry name ann, and say the token was used
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = Assert.Single(await AuditAsync(app), e => e.Action == "review.created");
        Assert.Equal("Ann (ann)", entry.ActorName);
        Assert.Contains($"created via API token '{created.Token.DisplayId}'", entry.Detail, StringComparison.Ordinal);
    }
}
