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

using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Web;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the conclusion card of the <see cref="ReviewPage"/> component: the outcome badge, the conclusion
///     text, the findings in the report with their resolution notes, the signers, the empty state and the hidden state
///     of a masked viewer.
/// </summary>
public class ReviewPageConclusionCardTests : PageTestBase
{
    /// <summary>
    ///     The password every user of the tests has.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The conclusion text the tests write.
    /// </summary>
    private const string ConclusionText = "The first check failed; the change is not ready.";

    /// <summary>
    ///     Creates the users ann, bob and cyd, a project of which they are members, and the review <c>REV-1</c>.
    /// </summary>
    /// <returns>The project identity and the identities of the users by user name.</returns>
    private async Task<(int ProjectId, Dictionary<string, int> Ids)> CreateReviewAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ids = new Dictionary<string, int>();
        foreach (var (username, name) in new[] { ("ann", "Ann"), ("bob", "Bob"), ("cyd", "Cyd") })
        {
            ids[username] = (await Users.CreateAsync(username, name, UserRole.Reviewer, Password, cancellationToken)).Id;
        }

        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        foreach (var id in ids.Values)
        {
            await Store.AddMembershipAsync(id, project.Id, cancellationToken);
        }

        await Reviews.CreateDraftAsync(project.Id, "REV-1", "First review", [new("a.txt", TestContent.A)], cancellationToken);
        return (project.Id, ids);
    }

    /// <summary>
    ///     Lets a slot owner answer every check and hand in the ballot.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="owner">The slot owner.</param>
    /// <param name="firstFails"><c>true</c> to answer the first check Fail; every other check is Pass.</param>
    /// <returns>A task that completes when the ballot is handed in.</returns>
    private async Task SubmitBallotAsync(int projectId, Actor owner, bool firstFails)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = true;
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            var fail = first && firstFails;
            await Checklists.SetAnswerAsync(
                projectId, "REV-1", item.Id, fail ? ChecklistOutcome.Fail : ChecklistOutcome.Pass, fail ? "Wrong." : null, owner, cancellationToken);
            first = false;
        }

        await Checklists.SubmitBallotAsync(projectId, "REV-1", owner, cancellationToken);
    }

    /// <summary>
    ///     Signs a slot as ann with the current record.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <returns>A task that completes when the slot is signed.</returns>
    private async Task SignAsync(int projectId, int slotId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var approval = await Signing.GetApprovalAsync(projectId, "REV-1", cancellationToken);
        await Signing.SignAsync(TestActors.Ann, projectId, "REV-1", slotId, approval!.Digest, Password, cancellationToken);
    }

    /// <summary>
    ///     Starts a thread about the whole file <c>a.txt</c> as ann.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="text">The text of the starting comment.</param>
    /// <param name="finding"><c>true</c> to start a Finding.</param>
    /// <returns>The thread.</returns>
    private Task<ReviewThread> StartAsync(int projectId, string text, bool finding)
    {
        return Threads.StartThreadAsync(
            projectId, "REV-1", new ThreadAnchor("a.txt"), TestActors.Ann, text, TestContext.Current.CancellationToken,
            includeInReport: finding);
    }

    /// <summary>
    ///     Renders the page for <c>REV-1</c>.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ReviewPage> RenderPage(int projectId)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, "REV-1"));
    }

    /// <summary>
    ///     Test that a rejected, signed review shows the Rejected outcome as an icon and a word, the conclusion text,
    ///     the findings in the report with their resolution notes (and chat threads not at all), and the signer with
    ///     the time.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ConclusionCard_Rejected_ShowsOutcomeTextFindingsAndSigners()
    {
        // Arrange: a rejected single-slot review with a resolved Finding, a resolved Question and the text, signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var (projectId, ids) = await CreateReviewAsync();
        var slot = await Signing.AddSlotAsync(TestActors.Ann, projectId, "REV-1", ids["ann"], ids["bob"], cancellationToken);
        await SubmitBallotAsync(projectId, TestActors.Ann, firstFails: true);
        var finding = await StartAsync(projectId, "Wrong result", true);
        var question = await StartAsync(projectId, "Why this way?", false);
        await Threads.ResolveAsync(projectId, "REV-1", finding.Id, TestActors.Ann, cancellationToken, null, "fixed in abc123");
        await Threads.ResolveAsync(projectId, "REV-1", question.Id, TestActors.Ann, cancellationToken);
        await Checklists.SetReviewConclusionTextAsync(projectId, "REV-1", ConclusionText, TestActors.Ann, cancellationToken);
        await SignAsync(projectId, slot.Id);

        // Act
        var cut = RenderPage(projectId);

        // Assert: the outcome is an icon that assistive technology skips and the word
        var outcome = cut.Find("[data-testid='review-conclusion-outcome']");
        Assert.Contains("rejected", outcome.ClassList);
        Assert.Equal("Rejected", outcome.TextContent.Trim());
        Assert.Equal("true", outcome.QuerySelector("svg")!.GetAttribute("aria-hidden"));

        // Assert: the text, only the Finding with its note, and the signer
        Assert.Equal(ConclusionText, cut.Find("[data-testid='review-conclusion-text']").TextContent);
        Assert.Equal("Findings in the report", cut.Find("[data-testid='review-conclusion-findings-heading']").TextContent);
        Assert.Equal("Wrong result", cut.Find($"[data-testid='review-conclusion-finding-text-{finding.Id}']").TextContent);
        Assert.Equal("Resolved", cut.Find($"[data-testid='review-conclusion-finding-state-{finding.Id}']").TextContent);
        Assert.Equal("fixed in abc123", cut.Find($"[data-testid='review-conclusion-finding-note-{finding.Id}']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='review-conclusion-finding-{question.Id}']"));
        Assert.Contains("Signed by Ann on", cut.Find("[data-testid='review-conclusion-signers']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an approved review shows the Approved outcome, and no list of findings when none is in the report.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ConclusionCard_Approved_ShowsApprovedOutcomeWithoutFindings()
    {
        // Arrange: an approved review with a resolved Question only
        var cancellationToken = TestContext.Current.CancellationToken;
        var (projectId, ids) = await CreateReviewAsync();
        var slot = await Signing.AddSlotAsync(TestActors.Ann, projectId, "REV-1", ids["ann"], ids["bob"], cancellationToken);
        await SubmitBallotAsync(projectId, TestActors.Ann, firstFails: false);
        var question = await StartAsync(projectId, "Why this way?", false);
        await Threads.ResolveAsync(projectId, "REV-1", question.Id, TestActors.Ann, cancellationToken);
        await SignAsync(projectId, slot.Id);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        var outcome = cut.Find("[data-testid='review-conclusion-outcome']");
        Assert.Contains("approved", outcome.ClassList);
        Assert.Equal("Approved", outcome.TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid='review-conclusion-findings']"));
        Assert.Empty(cut.FindAll("[data-testid='review-conclusion-findings-heading']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='review-conclusion-signers']"));
    }

    /// <summary>
    ///     Test that a review in progress lists its Findings, open ones as Open, shows no outcome and no signers, and
    ///     that a viewer who may not write the conclusion sees the empty state.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ConclusionCard_InProgress_ShowsEmptyStateAndOpenFindings()
    {
        // Arrange: an auditor looks at a review with an open Finding and no conclusion written
        var cancellationToken = TestContext.Current.CancellationToken;
        var (projectId, ids) = await CreateReviewAsync();
        await Signing.AddSlotAsync(TestActors.Ann, projectId, "REV-1", ids["ann"], ids["bob"], cancellationToken);
        var finding = await StartAsync(projectId, "Wrong result", true);
        var auditor = await Users.CreateAsync("aud", "Aud", UserRole.Reviewer, Password, cancellationToken);
        await Store.AddMembershipAsync(auditor.Id, projectId, ProjectRole.Auditor, cancellationToken);
        SignInAs("aud", "Aud", UserRole.Reviewer);

        // Act
        var cut = RenderPage(projectId);

        // Assert: the empty state, the open Finding, no outcome, no signers
        Assert.Equal("No conclusion written.", cut.Find("[data-testid='review-conclusion-empty']").TextContent);
        Assert.Equal("Open", cut.Find($"[data-testid='review-conclusion-finding-state-{finding.Id}']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='review-conclusion-finding-note-{finding.Id}']"));
        Assert.Empty(cut.FindAll("[data-testid='review-conclusion-outcome']"));
        Assert.Empty(cut.FindAll("[data-testid='review-conclusion-signers']"));
        Assert.Empty(cut.FindAll("[data-testid='review-conclusion-form']"));
    }

    /// <summary>
    ///     Test that a masked viewer, a reviewer who has not handed in a ballot while another has, sees the hidden
    ///     notice and none of the card: no findings, no outcome, no signers.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ConclusionCard_MaskedViewer_ShowsNoticeOnly()
    {
        // Arrange: ann handed in, bob did not; a Finding is in the report
        var cancellationToken = TestContext.Current.CancellationToken;
        var (projectId, ids) = await CreateReviewAsync();
        await Signing.AddSlotAsync(TestActors.Ann, projectId, "REV-1", ids["ann"], null, cancellationToken);
        await Signing.AddSlotAsync(TestActors.Ann, projectId, "REV-1", ids["bob"], null, cancellationToken);
        await SubmitBallotAsync(projectId, TestActors.Ann, firstFails: true);
        await StartAsync(projectId, "Wrong result", true);
        SignInAs("bob", "Bob", false);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='review-conclusion-hidden']"));
        Assert.Empty(cut.FindAll("[data-testid='review-conclusion-findings']"));
        Assert.Empty(cut.FindAll("[data-testid='review-conclusion-outcome']"));
        Assert.Empty(cut.FindAll("[data-testid='review-conclusion-signers']"));
        Assert.Empty(cut.FindAll("[data-testid='review-conclusion-empty']"));
    }
}
