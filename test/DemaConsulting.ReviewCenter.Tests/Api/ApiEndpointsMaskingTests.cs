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
using System.Text.Json;
using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Tests.Evidence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using static DemaConsulting.ReviewCenter.Tests.Api.ApiTestSupport;

namespace DemaConsulting.ReviewCenter.Tests.Api;

/// <summary>
///     Tests that the review resources of the API hide what the review page hides: a token never learns the failed
///     checks, the dispute, the blocker wording, the check threads or the conclusion text of a review with several
///     slots that is not concluded, and shows the outcome only once the review is concluded.
/// </summary>
public class ApiEndpointsMaskingTests
{
    /// <summary>
    ///     The name of the lead of the review in the arranged world.
    /// </summary>
    private const string Lead = "lead";

    /// <summary>
    ///     The identifier of the arranged review.
    /// </summary>
    private const string ReviewId = "REV-M";

    /// <summary>
    ///     The conclusion text of the arranged review, when it has one.
    /// </summary>
    private const string ConclusionText = "Reviewed against the design; no open concerns.";

    /// <summary>
    ///     Test that the token of a project member who holds no slot sees nothing of a review with two slots that is not
    ///     concluded: no failed count, no answered count, no check threads, no blocker or next-step wording about a
    ///     dispute or a failed check, and no conclusion text, while the review is marked as masked.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiEndpoints_GetReview_TokenNonSubmitterMultiSlot_HidesFailedDisputeBlockersAndCheckThreads()
    {
        // Arrange: ann hands in Pass, bob hands in Fail on the first check; a check thread, a finding and a question
        await using var app = await StartAsync();
        var projectId = await ArrangeAsync(app, bobSubmitted: true, conclusionText: ConclusionText);
        await StartThreadsAsync(app, projectId);
        var token = await CreateTokenAsync(app, "cyd");
        using var client = ClientFor(app, token.Value);

        // Act
        var response = await client.GetAsync($"/api/projects/{projectId}/reviews/{ReviewId}", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        // Assert: masked, and nothing of the standing is there
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(root.GetProperty("masked").GetBoolean());
        var counts = root.GetProperty("counts");
        Assert.Equal(0, counts.GetProperty("checklistFailed").GetInt32());
        Assert.Equal(0, counts.GetProperty("checklistAnswered").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("outcome").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("conclusionText").ValueKind);
        Assert.DoesNotContain("no open concerns", body, StringComparison.Ordinal);

        // Assert: the check thread is not counted, the file threads are (a finding and a question)
        Assert.Equal(2, counts.GetProperty("openThreads").GetInt32());
        Assert.Equal(1, counts.GetProperty("openFindings").GetInt32());
        Assert.Equal(1, counts.GetProperty("openQuestions").GetInt32());

        // Assert: no wording about a dispute or a failed check
        AssertNoVerdictWording(root);
    }

    /// <summary>
    ///     Test that the token of the lead of the review, and the token of a reviewer who has not handed in the ballot,
    ///     are masked too: a token is never the lead, an administrator or an auditor, and a draft ballot shows nothing.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiEndpoints_GetReview_TokenOfLeadOrDraftOwner_StillMasked()
    {
        // Arrange: ann has handed in, bob has not (his draft answers Fail)
        await using var app = await StartAsync();
        var projectId = await ArrangeAsync(app, bobSubmitted: false);
        await StartThreadsAsync(app, projectId);

        foreach (var user in new[] { Lead, "bob" })
        {
            var token = await CreateTokenAsync(app, user);
            using var client = ClientFor(app, token.Value);

            // Act
            var response = await client.GetAsync($"/api/projects/{projectId}/reviews/{ReviewId}", TestContext.Current.CancellationToken);
            using var json = await JsonAsync(response);
            var root = json.RootElement;

            // Assert: masked with no standing
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(root.GetProperty("masked").GetBoolean(), user);
            Assert.Equal(0, root.GetProperty("counts").GetProperty("checklistFailed").GetInt32());
            Assert.Equal(2, root.GetProperty("counts").GetProperty("openThreads").GetInt32());
            AssertNoVerdictWording(root);
        }
    }

    /// <summary>
    ///     Test that the token of a reviewer who has handed in the ballot sees the consolidated standing, the check
    ///     threads and their kinds, as its owner does on the review page, and still no outcome while the review is not
    ///     concluded.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiEndpoints_GetReview_TokenOfSubmittedSlotOwner_SeesConsolidatedCounts()
    {
        // Arrange: both ballots are in, and a check thread, a finding and a question exist
        await using var app = await StartAsync();
        var projectId = await ArrangeAsync(app, bobSubmitted: true, conclusionText: ConclusionText);
        await StartThreadsAsync(app, projectId);
        var token = await CreateTokenAsync(app, "ann");
        using var client = ClientFor(app, token.Value);

        // Act
        var response = await client.GetAsync($"/api/projects/{projectId}/reviews/{ReviewId}", TestContext.Current.CancellationToken);
        using var json = await JsonAsync(response);
        var root = json.RootElement;

        // Assert: not masked; the counts and the check thread are there; the review is not concluded
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(root.GetProperty("masked").GetBoolean());
        var counts = root.GetProperty("counts");
        Assert.True(counts.GetProperty("checklistAnswered").GetInt32() > 0);
        Assert.Equal(3, counts.GetProperty("openThreads").GetInt32());
        Assert.Equal(2, counts.GetProperty("openQuestions").GetInt32());
        Assert.Equal(1, counts.GetProperty("openFindings").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("outcome").ValueKind);
        Assert.Equal(ConclusionText, root.GetProperty("conclusionText").GetString());
    }

    /// <summary>
    ///     Test that a concluded review shows its outcome (approved or rejected), the conclusion text and the counts to
    ///     the token of a member who holds no slot, since a conclusion is public to everyone who may see the project.
    /// </summary>
    /// <param name="failFirst">Whether both reviewers answer Fail on the first check, which rejects the review.</param>
    /// <param name="outcome">The expected outcome.</param>
    [Theory(Timeout = 120000)]
    [InlineData(false, "Approved")]
    [InlineData(true, "Rejected")]
    public async Task ApiEndpoints_GetReview_Concluded_ShowsOutcomeConclusionTextAndCounts(bool failFirst, string outcome)
    {
        // Arrange: both ballots are in, the text is written and both reviewers sign
        await using var app = await StartAsync();
        var first = failFirst ? ChecklistOutcome.Fail : ChecklistOutcome.Pass;
        var projectId = await ArrangeAsync(app, bobSubmitted: true, conclusionText: ConclusionText, annFirst: first, bobFirst: first);
        await StartThreadsAsync(app, projectId);
        await SignBothAsync(app, projectId);
        var token = await CreateTokenAsync(app, "cyd");
        using var client = ClientFor(app, token.Value);

        // Act
        var response = await client.GetAsync($"/api/projects/{projectId}/reviews/{ReviewId}", TestContext.Current.CancellationToken);
        using var json = await JsonAsync(response);
        var root = json.RootElement;

        // Assert: nothing is hidden any more
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(root.GetProperty("masked").GetBoolean());
        Assert.Equal(outcome, root.GetProperty("outcome").GetString());
        Assert.Equal(ConclusionText, root.GetProperty("conclusionText").GetString());
        var counts = root.GetProperty("counts");
        Assert.Equal(counts.GetProperty("checklistItems").GetInt32(), counts.GetProperty("checklistAnswered").GetInt32());
        Assert.Equal(failFirst ? 1 : 0, counts.GetProperty("checklistFailed").GetInt32());
        Assert.Equal(0, counts.GetProperty("openThreads").GetInt32());
        Assert.Equal(3, counts.GetProperty("resolvedThreads").GetInt32());
        Assert.Equal(1, counts.GetProperty("resolvedFindings").GetInt32());
        Assert.Equal(2, counts.GetProperty("resolvedQuestions").GetInt32());
    }

    /// <summary>
    ///     Test that a review that is not concluded has no outcome, even for the token of a reviewer who sees the
    ///     standing, and that a review with a single slot is not masked.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiEndpoints_GetReview_SingleSlot_UnmaskedAndNotConcludedHasNoOutcome()
    {
        // Arrange: a review with one slot whose reviewer answered everything
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: false);
        var token = await CreateTokenAsync(app, "bob");
        using var client = ClientFor(app, token.Value);

        // Act
        var response = await client.GetAsync($"/api/projects/{projectId}/reviews/REV-1", TestContext.Current.CancellationToken);
        using var json = await JsonAsync(response);
        var root = json.RootElement;

        // Assert: the standing is there and there is no outcome
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(root.GetProperty("masked").GetBoolean());
        var counts = root.GetProperty("counts");
        Assert.Equal(counts.GetProperty("checklistItems").GetInt32(), counts.GetProperty("checklistAnswered").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("outcome").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("conclusionText").ValueKind);
    }

    /// <summary>
    ///     Test that the counts of open and resolved threads are split by kind: a thread that is in the report is a
    ///     finding, one that is not is a question.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiEndpoints_GetReview_ThreadKindCounts_FindingsAndQuestions()
    {
        // Arrange: a single-slot review with an open and a resolved finding and an open question
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await StartAsync();
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: false);
        var threads = app.Services.GetRequiredService<ThreadService>();
        var ann = new Actor("ann", "Ann");
        var anchor = new ThreadAnchor("a.txt");
        await threads.StartThreadAsync(projectId, "REV-1", anchor, ann, "Finding", cancellationToken, includeInReport: true);
        var done = await threads.StartThreadAsync(projectId, "REV-1", anchor, ann, "Done", cancellationToken, includeInReport: true);
        await threads.ResolveAsync(projectId, "REV-1", done.Id, ann, cancellationToken, resolutionNote: "Done");
        await threads.StartThreadAsync(projectId, "REV-1", anchor, ann, "Question", cancellationToken);
        var token = await CreateTokenAsync(app, "bob");
        using var client = ClientFor(app, token.Value);

        // Act
        var response = await client.GetAsync($"/api/projects/{projectId}/reviews/REV-1", cancellationToken);
        using var json = await JsonAsync(response);
        var counts = json.RootElement.GetProperty("counts");

        // Assert: the totals and the kinds agree
        Assert.Equal(2, counts.GetProperty("openThreads").GetInt32());
        Assert.Equal(1, counts.GetProperty("resolvedThreads").GetInt32());
        Assert.Equal(1, counts.GetProperty("openFindings").GetInt32());
        Assert.Equal(1, counts.GetProperty("openQuestions").GetInt32());
        Assert.Equal(1, counts.GetProperty("resolvedFindings").GetInt32());
        Assert.Equal(0, counts.GetProperty("resolvedQuestions").GetInt32());
    }

    /// <summary>
    ///     Test that the answer to the creation of a review is reduced like the details of the review: the token of a
    ///     member creates a review, which has no outcome, no conclusion text and no failed checks.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiEndpoints_CreateReview_ResponseIsMaskedLikeTheDetails()
    {
        // Arrange: a project with a member who creates a review through a token
        await using var app = await StartAsync();
        var projectId = await ArrangeAsync(app, bobSubmitted: true);
        var token = await CreateTokenAsync(app, "cyd");
        using var client = ClientFor(app, token.Value);
        var body = new
        {
            id = "REV-C",
            title = "Created",
            files = new[] { new { path = "a.txt", contentBase64 = Convert.ToBase64String("one\n"u8.ToArray()) } }
        };

        // Act
        var response = await client.PostAsync($"/api/projects/{projectId}/reviews", Json(body), TestContext.Current.CancellationToken);
        using var json = await JsonAsync(response);
        var review = json.RootElement.GetProperty("review");

        // Assert: created, with no standing
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, review.GetProperty("outcome").ValueKind);
        Assert.Equal(JsonValueKind.Null, review.GetProperty("conclusionText").ValueKind);
        Assert.Equal(0, review.GetProperty("counts").GetProperty("checklistFailed").GetInt32());
        AssertNoVerdictWording(review);
    }

    /// <summary>
    ///     Test that the review list shows the outcome of a review only when it is concluded, and that the progress of
    ///     a review the token may not see the standing of reveals no verdict.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiEndpoints_ListReviews_TokenActor_OutcomeShownOnlyWhenConcluded()
    {
        // Arrange: a review with both ballots in, which agree
        await using var app = await StartAsync();
        var projectId = await ArrangeAsync(app, bobSubmitted: true, bobFirst: ChecklistOutcome.Pass);
        var token = await CreateTokenAsync(app, "cyd");
        using var client = ClientFor(app, token.Value);

        // Act: the list before and after the conclusion
        var cancellationToken = TestContext.Current.CancellationToken;
        var before = await ReadFirstSummaryAsync(client, projectId, cancellationToken);
        await SignBothAsync(app, projectId);
        var after = await ReadFirstSummaryAsync(client, projectId, cancellationToken);

        // Assert
        Assert.Equal(JsonValueKind.Null, before.GetProperty("outcome").ValueKind);
        Assert.Equal("Approved", after.GetProperty("outcome").GetString());
    }

    /// <summary>
    ///     Test that the review details have no field of the ballots of a single reviewer, no per-reviewer answer and no
    ///     dispute: the names of the fields are exactly the documented ones (an allow-list), so a new field cannot
    ///     leak without a deliberate change of this test and of the API description.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiEndpoints_ReviewDetail_NoPerSlotBallotOrDisputeFields()
    {
        // Arrange: a review with a dispute, read through a token of a reviewer who sees the standing
        await using var app = await StartAsync();
        var projectId = await ArrangeAsync(app, bobSubmitted: true);
        var token = await CreateTokenAsync(app, "ann");
        using var client = ClientFor(app, token.Value);

        // Act
        var response = await client.GetAsync($"/api/projects/{projectId}/reviews/{ReviewId}", TestContext.Current.CancellationToken);
        using var json = await JsonAsync(response);
        var root = json.RootElement;

        // Assert: the fields of the review, of its counts and of its slots are the documented ones
        string[] review =
        [
            "id", "title", "state", "created", "contentFingerprint", "createdBy", "createdViaApi", "isFromRepository",
            "repositoryName", "baselineSha", "headSha", "selection", "reviewSetId", "dueDate", "overdue", "approved",
            "approvalDigest", "slots", "counts", "blockers", "nextStep", "followsReviewId", "carriedAnswerCount",
            "carriedThreadCount", "outcome", "conclusionText", "masked"
        ];
        string[] counts =
        [
            "files", "openThreads", "resolvedThreads", "checklistItems", "checklistAnswered", "checklistFailed",
            "openFindings",
            "openQuestions", "resolvedFindings", "resolvedQuestions"
        ];
        string[] slot = ["id", "number", "primary", "alternate", "state", "signedBy", "signedAt"];
        Assert.Equal(review.Order(), root.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(counts.Order(), root.GetProperty("counts").EnumerateObject().Select(p => p.Name).Order());
        Assert.All(
            root.GetProperty("slots").EnumerateArray(),
            s => Assert.Equal(slot.Order(), s.EnumerateObject().Select(p => p.Name).Order()));
    }

    /// <summary>
    ///     Test that the review resources are unchanged for callers that were refused before: no token is a 401 and a
    ///     token of a user who is no member of the project is a 404.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ApiEndpoints_GetReview_AnonymousOrNonMember_Unchanged()
    {
        // Arrange: a project and a user outside it
        await using var app = await StartAsync();
        var projectId = await ArrangeAsync(app, bobSubmitted: true);
        await EnsureUserAsync(app, "out", UserRole.Reviewer);
        var token = await CreateTokenAsync(app, "out");
        using var anonymous = ClientFor(app, null);
        using var outsider = ClientFor(app, token.Value);

        // Act
        var none = await anonymous.GetAsync($"/api/projects/{projectId}/reviews/{ReviewId}", TestContext.Current.CancellationToken);
        var hidden = await outsider.GetAsync($"/api/projects/{projectId}/reviews/{ReviewId}", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    /// <summary>
    ///     Test that the endpoint code reads the approval, the check threads and the consolidated checklist only for
    ///     the actor, never through a viewer-less overload (an allow-list scan of the source): every call of those
    ///     reads passes the actor as its first argument, and the removed viewer-less checklist read does not come back.
    /// </summary>
    [Fact]
    public void ApiEndpoints_Source_UsesNoViewerLessReadsForReviewDetail()
    {
        // Arrange: the source of the API and the pattern of a call of a read that decides what a viewer may see
        var root = FindRepositoryRoot();
        var calls = new Regex(
            @"\b(?<name>GetApprovalAsync|GetApprovalsAsync|GetConsolidatedAsync|GetForSystemAsync|Threads\.ListAsync|threads\.ListAsync)\(\s*(?<first>[^,)\s]+)",
            RegexOptions.CultureInvariant);

        // Act: find every call in the files of the API
        var found = Directory.EnumerateFiles(Path.Combine(root, "src", "DemaConsulting.ReviewCenter", "Api"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => calls.Matches(File.ReadAllText(file)).Select(m => (File: Path.GetFileName(file), Name: m.Groups["name"].Value, First: m.Groups["first"].Value)))
            .ToList();

        // Assert: the reads are there, each for the actor; the removed system read is nowhere in the source
        Assert.True(found.Count >= 3, "The scan found too few calls; the pattern is out of date.");
        Assert.All(found, call => Assert.True(call.First == "actor", $"{call.File}: {call.Name} is called without the actor."));
        var sources = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        Assert.DoesNotContain(sources, file => File.ReadAllText(file).Contains("GetForSystemAsync", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Asserts that the blockers and the next step of a review mention no dispute, failure or rejection.
    /// </summary>
    /// <param name="review">The review details.</param>
    private static void AssertNoVerdictWording(JsonElement review)
    {
        var words = new[] { "dispute", "fail", "reject" };
        var text = string.Join(
            ' ',
            review.GetProperty("blockers").EnumerateArray().Select(b => b.GetString())
                .Append(review.GetProperty("nextStep").GetString()));
        foreach (var word in words)
        {
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    ///     Reads the first summary of the review list of a project.
    /// </summary>
    /// <param name="client">The client of a token.</param>
    /// <param name="projectId">The project.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The summary, as a detached element.</returns>
    private static async Task<JsonElement> ReadFirstSummaryAsync(HttpClient client, int projectId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync($"/api/projects/{projectId}/reviews", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await JsonAsync(response);
        return json.RootElement.EnumerateArray().Single(r => r.GetProperty("id").GetString() == ReviewId).Clone();
    }

    /// <summary>
    ///     Arranges a project with a review that has two slots (ann and bob), led by a member who holds no slot, and a
    ///     further member (cyd) who holds none either. All users are ordinary members: none is a system administrator.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="bobSubmitted">Whether bob hands in his ballot, which answers Fail on the first check.</param>
    /// <param name="conclusionText">The conclusion text the lead writes, or <c>null</c> for none.</param>
    /// <param name="annFirst">The answer of ann to the first check.</param>
    /// <param name="bobFirst">The answer of bob to the first check.</param>
    /// <returns>The identifier of the project.</returns>
    private static async Task<int> ArrangeAsync(
        WebApplication app,
        bool bobSubmitted,
        string? conclusionText = null,
        ChecklistOutcome annFirst = ChecklistOutcome.Pass,
        ChecklistOutcome bobFirst = ChecklistOutcome.Fail)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var users = app.Services.GetRequiredService<UserService>();
        var ids = new Dictionary<string, int>();
        await users.CreateAsync("root", "root", UserRole.Admin, TestBrowser.Password, cancellationToken);
        foreach (var name in new[] { Lead, "ann", "bob", "cyd" })
        {
            ids[name] = (await users.CreateAsync(name, name, UserRole.Reviewer, TestBrowser.Password, cancellationToken)).Id;
        }

        var project = await app.Services.GetRequiredService<ProjectService>().CreateAsync(
            "Apollo", null, new Actor("root", "root"), cancellationToken);
        var store = app.Services.GetRequiredService<IReviewStore>();
        foreach (var id in ids.Values)
        {
            await store.AddMembershipAsync(id, project.Id, cancellationToken);
        }

        var lead = new Actor(Lead, Lead);
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id, ReviewId, "Masked", [new UploadedFile("a.txt", "one\ntwo\n"u8.ToArray())], lead, cancellationToken);
        var signing = app.Services.GetRequiredService<SigningService>();
        await signing.AddSlotAsync(lead, project.Id, ReviewId, ids["ann"], null, cancellationToken);
        await signing.AddSlotAsync(lead, project.Id, ReviewId, ids["bob"], null, cancellationToken);
        var checklists = app.Services.GetRequiredService<ChecklistService>();
        await AnswerAsync(checklists, project.Id, "ann", annFirst);
        await checklists.SubmitBallotAsync(project.Id, ReviewId, new Actor("ann", "ann"), cancellationToken);
        await AnswerAsync(checklists, project.Id, "bob", bobFirst);
        if (bobSubmitted)
        {
            await checklists.SubmitBallotAsync(project.Id, ReviewId, new Actor("bob", "bob"), cancellationToken);
        }

        if (conclusionText is not null)
        {
            await checklists.SetReviewConclusionTextAsync(project.Id, ReviewId, conclusionText, lead, cancellationToken);
        }

        return project.Id;
    }

    /// <summary>
    ///     Lets a reviewer answer every check, the first one with the given outcome and the others Pass.
    /// </summary>
    /// <param name="checklists">The checklist service.</param>
    /// <param name="projectId">The project.</param>
    /// <param name="username">The reviewer.</param>
    /// <param name="first">The answer to the first check.</param>
    /// <returns>A task that completes when the answers are stored.</returns>
    private static async Task AnswerAsync(ChecklistService checklists, int projectId, string username, ChecklistOutcome first)
    {
        var actor = new Actor(username, username);
        var firstId = DefaultChecklist.Current.Sections[0].Items[0].Id;
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            var outcome = item.Id == firstId ? first : ChecklistOutcome.Pass;
            var justification = outcome == ChecklistOutcome.Pass ? null : "A reason";
            await checklists.SetAnswerAsync(
                projectId, ReviewId, item.Id, outcome, justification, actor, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Starts the threads of the arranged review: a check thread, a finding and a question about the file.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="projectId">The project.</param>
    /// <returns>A task that completes when the threads exist.</returns>
    private static async Task StartThreadsAsync(WebApplication app, int projectId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var threads = app.Services.GetRequiredService<ThreadService>();
        var ann = new Actor("ann", "ann");
        var firstId = DefaultChecklist.Current.Sections[0].Items[0].Id;
        await threads.StartThreadAsync(projectId, ReviewId, ThreadAnchor.ForCheck(firstId), ann, "Why Fail?", cancellationToken);
        await threads.StartThreadAsync(projectId, ReviewId, new ThreadAnchor("a.txt"), ann, "A finding", cancellationToken, includeInReport: true);
        await threads.StartThreadAsync(projectId, ReviewId, new ThreadAnchor("a.txt"), ann, "A question", cancellationToken);
    }

    /// <summary>
    ///     Signs the arranged review with both reviewers, which concludes it.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="projectId">The project.</param>
    /// <returns>A task that completes when both have signed.</returns>
    private static async Task SignBothAsync(WebApplication app, int projectId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var signing = app.Services.GetRequiredService<SigningService>();

        // A review is signed when nothing is open; ann has handed in her ballot, so she sees every thread
        var threads = app.Services.GetRequiredService<ThreadService>();
        var ann = new Actor("ann", "ann");
        foreach (var thread in (await threads.ListAsync(ann, projectId, ReviewId, cancellationToken)).Where(t => t.State == ReviewThreadState.Open))
        {
            await threads.ResolveAsync(projectId, ReviewId, thread.Id, ann, cancellationToken, resolutionNote: "Done");
        }

        var approval = (await signing.GetApprovalAsync(projectId, ReviewId, cancellationToken))!;
        foreach (var slot in approval.Slots)
        {
            var user = slot.Primary.Username;
            await signing.SignAsync(
                new Actor(user, user), projectId, ReviewId, slot.SlotId, approval.Digest, TestBrowser.Password, cancellationToken);
        }
    }

    /// <summary>
    ///     Finds the root of the repository by its solution file.
    /// </summary>
    /// <returns>The folder of the repository.</returns>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DemaConsulting.ReviewCenter.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
