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
using DemaConsulting.ReviewCenter.Tests.Evidence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using static DemaConsulting.ReviewCenter.Tests.Api.ApiTestSupport;

namespace DemaConsulting.ReviewCenter.Tests.Api;

/// <summary>
///     Tests of the API for the read-only role: a token of an auditor of a project reads what any member reads and
///     is refused every write with 403, while a user who is no member of the project gets the 404 of a missing project.
/// </summary>
public class ApiAuditorTests
{
    /// <summary>
    ///     Arranges a project with one review, the auditor <c>aud</c> of the project and the outsider <c>out</c>.
    /// </summary>
    /// <param name="app">The started application.</param>
    /// <returns>The project number.</returns>
    private static async Task<int> ArrangeAsync(WebApplication app)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: false);
        await EnsureUserAsync(app, "aud", UserRole.Reviewer);
        await EnsureUserAsync(app, "out", UserRole.Reviewer);
        var auditor = (await app.Services.GetRequiredService<UserService>().GetAsync("aud", cancellationToken))!;
        await app.Services.GetRequiredService<IReviewStore>().AddMembershipAsync(
            auditor.Id, projectId, ProjectRole.Auditor, cancellationToken);
        return projectId;
    }

    /// <summary>
    ///     Test that the token of an auditor reads the project, its review list with the progress, a review and the
    ///     checklist of the project, like the token of any member.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiAuditor_Reads_Token_ServeTheProject()
    {
        // Arrange: an auditor with a token
        await using var app = await StartAsync();
        var projectId = await ArrangeAsync(app);
        var token = await CreateTokenAsync(app, "aud");
        using var client = ClientFor(app, token.Value);

        // Act
        var projects = await client.GetAsync("/api/projects", TestContext.Current.CancellationToken);
        var reviews = await client.GetAsync($"/api/projects/{projectId}/reviews", TestContext.Current.CancellationToken);
        var review = await client.GetAsync($"/api/projects/{projectId}/reviews/REV-1", TestContext.Current.CancellationToken);
        var checklist = await client.GetAsync($"/api/projects/{projectId}/checklist", TestContext.Current.CancellationToken);
        using var projectList = await JsonAsync(projects);
        using var reviewList = await JsonAsync(reviews);

        // Assert
        Assert.Equal(HttpStatusCode.OK, projects.StatusCode);
        Assert.Equal(projectId, Assert.Single(projectList.RootElement.EnumerateArray()).GetProperty("id").GetInt32());
        Assert.Equal(HttpStatusCode.OK, reviews.StatusCode);
        Assert.Equal("REV-1", Assert.Single(reviewList.RootElement.EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Equal(HttpStatusCode.OK, checklist.StatusCode);
    }

    /// <summary>
    ///     Test that the token of an auditor cannot create a review (403 as a problem document, nothing stored), and
    ///     that the token of a user who is no member gets the 404 of a project that does not exist.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiAuditor_CreateReview_AuditorIs403AndNonMemberIs404()
    {
        // Arrange
        await using var app = await StartAsync();
        var projectId = await ArrangeAsync(app);
        var auditorToken = await CreateTokenAsync(app, "aud");
        var outsiderToken = await CreateTokenAsync(app, "out");
        using var auditor = ClientFor(app, auditorToken.Value);
        using var outsider = ClientFor(app, outsiderToken.Value);
        var body = new
        {
            id = "REV-9",
            title = "Nope",
            files = new[] { new { path = "a.txt", contentBase64 = Convert.ToBase64String("one\n"u8.ToArray()) } }
        };
        var auditBefore = (await AuditAsync(app)).Count;

        // Act
        var forbidden = await auditor.PostAsync(
            $"/api/projects/{projectId}/reviews", Json(body), TestContext.Current.CancellationToken);
        var hidden = await outsider.PostAsync(
            $"/api/projects/{projectId}/reviews", Json(body), TestContext.Current.CancellationToken);
        var hiddenRead = await outsider.GetAsync(
            $"/api/projects/{projectId}/reviews", TestContext.Current.CancellationToken);

        // Assert: refused, as problem documents, and nothing was stored or audited
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("application/problem+json", forbidden.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hiddenRead.StatusCode);
        var stored = await app.Services.GetRequiredService<ReviewService>().ListAsync(projectId, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(stored, r => r.Id == "REV-9");
        Assert.Equal(auditBefore, (await AuditAsync(app)).Count);
    }

    /// <summary>
    ///     Test that the rule of a member who still has reviewer work answers as a conflict (409).
    /// </summary>
    [Fact]
    public void ApiAuditor_MemberHasOpenReviewWork_Is409()
    {
        // Arrange: nothing to prepare

        // Act
        var status = ApiProblems.StatusOf(DomainRule.MemberHasOpenReviewWork);

        // Assert
        Assert.Equal(409, status);
    }
}
