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
using Microsoft.Extensions.DependencyInjection;
using static DemaConsulting.ReviewCenter.Tests.Api.ApiTestSupport;

namespace DemaConsulting.ReviewCenter.Tests.Api;

/// <summary>
///     Subsystem tests for the Api subsystem: whole journeys through the real request pipeline with the real
///     services, a real database, and no mocked unit.
/// </summary>
public class ApiSubsystemTests
{
    /// <summary>
    ///     Test the token journey end to end: a user creates a token, a script uses it, and after the user revokes
    ///     it the same request is refused.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiSubsystem_TokenJourney_CreateUseRevoke_WorksEndToEnd()
    {
        // Arrange: a user with a new token
        await using var app = await StartAsync();
        var created = await CreateTokenAsync(app);
        using var client = ClientFor(app, created.Value);

        // Act: use the token, revoke it, and use it again
        var before = await client.GetAsync("/api/projects", TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<PersonalAccessTokenService>()
            .RevokeAsync(TestActors.Ann, created.Token.Id, TestContext.Current.CancellationToken);
        var after = await client.GetAsync("/api/projects", TestContext.Current.CancellationToken);

        // Assert: accepted while valid, refused once revoked
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    /// <summary>
    ///     Test the review journey end to end: a script creates a draft review from files, reads it back, finds that
    ///     the attestation of a draft is refused, and downloads the attestation of a complete review.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiSubsystem_ReviewJourney_CreateDraftReadAndDownloadAttestation_WorksEndToEnd()
    {
        // Arrange: a project, a token, and a second project with a complete review
        await using var app = await StartAsync();
        var completeProjectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        var token = await CreateTokenAsync(app);
        var project = await app.Services.GetRequiredService<ProjectService>().CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        using var client = ClientFor(app, token.Value);
        var body = new
        {
            id = "REV-9",
            title = "Journey",
            files = new[] { new { path = "a.txt", contentBase64 = Convert.ToBase64String("one\n"u8.ToArray()) } }
        };

        // Act: create the draft, read it back, ask for its attestation, and download the complete review's
        var create = await client.PostAsync($"/api/projects/{project.Id}/reviews", Json(body), TestContext.Current.CancellationToken);
        var read = await client.GetAsync($"/api/projects/{project.Id}/reviews/REV-9", TestContext.Current.CancellationToken);
        using var readJson = await JsonAsync(read);
        var draftPdf = await client.GetAsync($"/api/projects/{project.Id}/reviews/REV-9/attestation.pdf", TestContext.Current.CancellationToken);
        var completePdf = await client.GetAsync($"/api/projects/{completeProjectId}/reviews/REV-1/attestation.pdf", TestContext.Current.CancellationToken);

        // Assert: a flagged draft, no attestation for it, and a PDF for the complete review
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(readJson.RootElement.GetProperty("createdViaApi").GetBoolean());
        Assert.False(readJson.RootElement.GetProperty("approved").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, draftPdf.StatusCode);
        Assert.Equal(HttpStatusCode.OK, completePdf.StatusCode);
        Assert.Equal("application/pdf", completePdf.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    ///     Test the evidence journey end to end, as ReviewMark runs it: with HTTP Basic credentials that carry a token
    ///     as the password, a script reads the evidence index of a project, follows the link of its entry to the
    ///     attestation, and once the token is revoked the same request is refused.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiSubsystem_EvidenceJourney_BasicCredentials_ReadIndexAndFollowLink_WorksEndToEnd()
    {
        // Arrange: a complete review with a review-set id, and a token
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: false);
        var store = app.Services.GetRequiredService<IReviewStore>();
        await EvidenceTestSupport.AddReviewAsync(store, TimeProvider.System, projectId, "REV-2", "Core-Logic", "one\n"u8.ToArray());
        var annId = (await app.Services.GetRequiredService<UserService>().GetAsync("ann", TestContext.Current.CancellationToken))!.Id;
        var signing = app.Services.GetRequiredService<SigningService>();
        var slotId = await EvidenceTestSupport.AddSlotAsync(signing, projectId, "REV-2", annId);
        await EvidenceTestSupport.AnswerAllPassAsync(app.Services.GetRequiredService<ChecklistService>(), projectId, "REV-2");
        await EvidenceTestSupport.SignAsync(signing, projectId, "REV-2", slotId, EvidenceTestSupport.Ann, TestBrowser.Password);
        var created = await CreateTokenAsync(app);
        using var client = ClientFor(app, created.Value, basic: true);

        // Act: read the index, follow the link of its entry, revoke the token and read the index again
        var index = await client.GetAsync($"/api/projects/{projectId}/evidence/index.json", TestContext.Current.CancellationToken);
        using var indexJson = await JsonAsync(index);
        var file = Assert.Single(indexJson.RootElement.GetProperty("reviews").EnumerateArray()).GetProperty("file").GetString()!;
        var pdf = await client.GetAsync(new Uri(file).PathAndQuery, TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<PersonalAccessTokenService>()
            .RevokeAsync(TestActors.Ann, created.Token.Id, TestContext.Current.CancellationToken);
        var after = await client.GetAsync($"/api/projects/{projectId}/evidence/index.json", TestContext.Current.CancellationToken);

        // Assert: the index lists the review, the link is a PDF, and the revoked token is refused
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    /// <summary>
    ///     Test that an approved (locked) review stays readable through the API with its attestation and evidence
    ///     index, that a change of it is refused by the services and is answered as a 409 problem, and that after an
    ///     administrator reopens it the API reports it as not approved.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiSubsystem_LockedReview_StaysReadableAndMutationIsProblem409()
    {
        // Arrange: an approved review and a token
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        var token = await CreateTokenAsync(app);
        using var client = ClientFor(app, token.Value);
        var route = $"/api/projects/{projectId}/reviews/REV-1";

        // Act: read the locked review, its attestation and the evidence index, and try to change it
        var read = await client.GetAsync(route, TestContext.Current.CancellationToken);
        using var readJson = await JsonAsync(read);
        var pdf = await client.GetAsync($"{route}/attestation.pdf", TestContext.Current.CancellationToken);
        var index = await client.GetAsync($"/api/projects/{projectId}/evidence/index.json", TestContext.Current.CancellationToken);
        var refusal = await Assert.ThrowsAsync<DomainRuleException>(() => app.Services.GetRequiredService<ThreadService>().StartThreadAsync(
            projectId, "REV-1", new ThreadAnchor("a.txt", 1, 1), TestActors.Ann, "Late remark", TestContext.Current.CancellationToken));
        var problem = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(ApiProblems.ToResult(refusal));

        // Assert: readable, and the refused change maps to 409
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(readJson.RootElement.GetProperty("approved").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Equal(DomainRule.ReviewLocked, refusal.Rule);
        Assert.Equal(409, problem.StatusCode);

        // Act: an administrator reopens the review
        await app.Services.GetRequiredService<SigningService>().ReopenReviewAsync(
            TestActors.Ann, projectId, "REV-1", "Found a gap", TestContext.Current.CancellationToken);
        var reread = await client.GetAsync(route, TestContext.Current.CancellationToken);
        using var rereadJson = await JsonAsync(reread);

        // Assert: no longer approved
        Assert.False(rereadJson.RootElement.GetProperty("approved").GetBoolean());
    }

    /// <summary>
    ///     Test that a token never signs and never opens the web pages: the API has no signing route, the signing
    ///     service refuses an actor that came through a token, and the account page treats the token as signed out.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiSubsystem_TokenPrincipal_SigningAndWebPages_AreNotReachable()
    {
        // Arrange: a draft review, a token, and an actor that came through that token
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: false);
        var token = await CreateTokenAsync(app);
        using var client = ClientFor(app, token.Value);
        var viaToken = new Actor("ann", "Ann") { ApiTokenId = token.Token.DisplayId };

        // Act: look for a signing route, sign through the service, and open a web page with the token
        var sign = await client.PostAsync($"/api/projects/{projectId}/reviews/REV-1/sign", Json(new { }), TestContext.Current.CancellationToken);
        var refusal = await Assert.ThrowsAsync<DomainRuleException>(() => app.Services.GetRequiredService<SigningService>().SignAsync(
            viaToken, projectId, "REV-1", 1, "digest", TestBrowser.Password, TestContext.Current.CancellationToken));
        var page = await client.GetAsync("/account", TestContext.Current.CancellationToken);

        // Assert: no route, a refusal, and the login page
        Assert.Equal(HttpStatusCode.NotFound, sign.StatusCode);
        Assert.Equal(DomainRule.ActorNotAllowed, refusal.Rule);
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.StartsWith("/login", page.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }
}
