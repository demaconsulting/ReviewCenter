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
///     Unit tests for the "Where are we?" status panel at the top of the review page and the plain-language wording
///     that replaced the "out of date" wording. Rendered in memory with bUnit.
/// </summary>
public class ReviewStatusPanelTests : BallotPageTestBase
{
    /// <summary>
    ///     Renders the review page of <c>REV-1</c>.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The rendered page.</returns>
    private IRenderedComponent<ReviewPage> RenderReview(int projectId)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, ReviewKey));
    }

    /// <summary>
    ///     Gets the identities of the slots of <c>REV-1</c> in order.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The slot identities.</returns>
    private async Task<int[]> SlotIdsAsync(int projectId)
    {
        var approval = await Signing.GetApprovalAsync(projectId, ReviewKey, TestContext.Current.CancellationToken);
        return [.. approval!.Slots.Select(slot => slot.SlotId)];
    }

    /// <summary>
    ///     Creates a review with two reviewers, of whom bob handed in a ballot and signed it, then the review changed:
    ///     the signature is stored with the digest of an earlier report and a day before the entries of the change.
    ///     The second reviewer has not signed, so the review is neither approved nor locked.
    /// </summary>
    /// <returns>The project identity and the slot identity.</returns>
    private async Task<(int ProjectId, int SlotId)> ArrangeSignedThenChangedAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = await CreateReviewAsync("bob", "dee");
        await AnswerAsync(projectId, "bob");
        var slotId = (await SlotIdsAsync(projectId))[0];
        var bob = await UserStore.GetByUsernameAsync("bob", cancellationToken);
        var approval = await Signing.GetApprovalAsync(projectId, ReviewKey, cancellationToken);
        await Store.AddSignatureAsync(
            new ReviewSignature
            {
                ProjectId = projectId,
                ReviewId = ReviewKey,
                SlotId = slotId,
                UserId = bob!.Id,
                DisplayName = "Bob",
                Role = SlotRole.Primary,
                SignedAt = new DateTimeOffset(2025, 3, 3, 5, 6, 7, TimeSpan.Zero),
                RecordDigest = approval!.Digest,
                Meaning = "Reviewed and approved"
            },
            cancellationToken);

        // A finding that goes in the report changes the report, so the signature no longer matches
        await Threads.StartThreadAsync(
            projectId,
            ReviewKey,
            new ThreadAnchor("a.txt"),
            TestActors.Ann,
            "Fix this",
            cancellationToken,
            acknowledgedSlotIds: [slotId],
            includeInReport: true);
        await AuditStore.AppendAsync(
            AuditDraft.By(new Actor("cyd", "Cyd"), "review.conclusion-changed", "length 0 -> 12", projectId, ReviewKey),
            cancellationToken);
        return (projectId, slotId);
    }

    /// <summary>
    ///     Test that the panel shows every reviewer with the state of the ballot (Not started, Draft, Handed in) and
    ///     of the signature, and marks the viewer.
    /// </summary>
    [Fact]
    public async Task ReviewStatusPanel_ShowsReviewerBallotAndSignedStates()
    {
        // Arrange: ann handed in, bob has a draft, cyd has not started; the viewer is ann
        var projectId = await CreateReviewAsync("ann", "bob", "cyd");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", submit: false);
        var slots = await SlotIdsAsync(projectId);

        // Act
        var cut = RenderReview(projectId);

        // Assert
        Assert.NotNull(cut.Find("[data-testid='status-panel']"));
        Assert.Equal("Handed in", cut.Find($"[data-testid='status-ballot-{slots[0]}']").TextContent);
        Assert.Equal("Draft", cut.Find($"[data-testid='status-ballot-{slots[1]}']").TextContent);
        Assert.Equal("Not started", cut.Find($"[data-testid='status-ballot-{slots[2]}']").TextContent);
        Assert.All(slots, slot => Assert.Equal("Not signed", cut.Find($"[data-testid='status-signature-{slot}']").TextContent));
        Assert.Contains("(you)", cut.Find($"[data-testid='status-reviewer-name-{slots[0]}']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a disputed check is listed with a Discuss button, that the button opens the form and starts a
    ///     discussion about the check, and that the button is gone once the discussion exists.
    /// </summary>
    [Fact]
    public async Task ReviewStatusPanel_DisputedCheck_DiscussStartsThread()
    {
        // Arrange: ann and bob handed in ballots that differ on the first check; the viewer is ann
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);
        var cut = RenderReview(projectId);
        Assert.NotNull(cut.Find($"[data-testid='status-disputed-{FirstItemTestId}']"));

        // Act
        await cut.Find($"[data-testid='status-discuss-{FirstItemTestId}']").ClickAsync();
        await cut.Find($"[data-testid='discuss-text-{FirstItemTestId}']").InputAsync("Why Fail?");
        await cut.Find($"[data-testid='discuss-start-{FirstItemTestId}']").ClickAsync();

        // Assert: the discussion is about the check, and nothing offers to start another one
        var thread = Assert.Single(await Threads.ListAsync(projectId, ReviewKey, TestContext.Current.CancellationToken));
        Assert.Equal(FirstItemId, thread.CheckItemId);
        Assert.Equal("Why Fail?", thread.Comments.Single().Text);
        Assert.Empty(cut.FindAll($"[data-testid='status-discuss-{FirstItemTestId}']"));
        Assert.Empty(cut.FindAll($"[data-testid='discuss-form-{FirstItemTestId}']"));
        Assert.NotNull(cut.Find("[data-testid='check-discussions']"));
    }

    /// <summary>
    ///     Test that the panel counts the open questions and findings, each with its icon and word.
    /// </summary>
    [Fact]
    public async Task ReviewStatusPanel_ShowsQuestionAndFindingCounts()
    {
        // Arrange: two open questions and one open finding about a file
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = await CreateReviewAsync("ann");
        await Threads.StartThreadAsync(projectId, ReviewKey, new ThreadAnchor("a.txt"), TestActors.Ann, "One?", cancellationToken);
        await Threads.StartThreadAsync(projectId, ReviewKey, new ThreadAnchor("a.txt", 1, 1), TestActors.Ann, "Two?", cancellationToken);
        await Threads.StartThreadAsync(
            projectId, ReviewKey, new ThreadAnchor("a.txt"), TestActors.Ann, "Fix this", cancellationToken, includeInReport: true);

        // Act
        var cut = RenderReview(projectId);

        // Assert: the counts, and the kind badges that show an icon and a word
        Assert.Equal("2 open", cut.Find("[data-testid='status-open-questions-text']").TextContent);
        Assert.Equal("1 open", cut.Find("[data-testid='status-open-findings-text']").TextContent);
        Assert.Equal("Question", cut.Find("[data-testid='status-questions-kind-label']").TextContent);
        Assert.Equal("Finding", cut.Find("[data-testid='status-findings-kind-label']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='status-questions-kind'] svg"));
    }

    /// <summary>
    ///     Test that exactly one next-action element is shown, whatever the viewer can do: a link to the checklist for
    ///     a reviewer who has not answered, a link to the reviewers card when nobody is asked yet.
    /// </summary>
    [Fact]
    public async Task ReviewStatusPanel_ShowsExactlyOneNextActionButton()
    {
        // Arrange: a review without reviewers, seen by the administrator ann
        var emptyProject = await CreateReviewAsync();

        // Act
        var empty = RenderReview(emptyProject);

        // Assert: one action, leading to the reviewers card
        Assert.Single(empty.FindAll("[data-testid='next-action']"));
        var partLink = empty.Find("[data-testid='next-action']").GetAttribute("href")!;
        Assert.EndsWith("#reviewers-card", partLink, StringComparison.Ordinal);
        Assert.False(partLink.StartsWith('#'), "A bare fragment would resolve against the site root and leave the page.");

        // Arrange: a review where bob has not answered, seen by bob
        var projectId = await CreateReviewAsync("bob");
        SignInAs("bob", "Bob", false);

        // Act
        var reviewer = RenderReview(projectId);

        // Assert: one action, leading to the checklist page
        Assert.Single(reviewer.FindAll("[data-testid='next-action']"));
        Assert.Equal(
            $"/projects/{projectId}/reviews/{ReviewKey}/checklist",
            reviewer.Find("[data-testid='next-action']").GetAttribute("href"));
        Assert.Equal("Answer the checklist", reviewer.Find("[data-testid='next-action']").TextContent);
    }

    /// <summary>
    ///     Test that a viewer who has not handed in a ballot is told that the differences and the talk about checks
    ///     are hidden until they do, and sees no disputed check, no Discuss button and no check discussion.
    /// </summary>
    [Fact]
    public async Task ReviewStatusPanel_MaskedViewer_HidesDisputes()
    {
        // Arrange: ann and cyd differ, bob keeps a draft and is the viewer; the administrator started a discussion
        var projectId = await CreateReviewAsync("ann", "cyd", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "cyd", ChecklistOutcome.Fail);
        await AnswerAsync(projectId, "bob", submit: false);
        await Threads.StartThreadAsync(
            projectId, ReviewKey, ThreadAnchor.ForCheck(FirstItemId), TestActors.Ann, "Why Fail?", TestContext.Current.CancellationToken);
        SignInAs("bob", "Bob", false);

        // Act
        var cut = RenderReview(projectId);

        // Assert
        Assert.Contains(
            "until you submit your ballot", cut.Find("[data-testid='status-hidden']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='status-disputed']"));
        Assert.Empty(cut.FindAll("[data-testid^='status-discuss-']"));
        Assert.Empty(cut.FindAll("[data-testid='check-discussions']"));
        Assert.DoesNotContain("Why Fail?", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("0 open", cut.Find("[data-testid='status-open-questions-text']").TextContent);
    }

    /// <summary>
    ///     Test that a reviewer who signed before the review changed is shown as such, with an expandable list of what
    ///     changed since, in plain words.
    /// </summary>
    [Fact]
    public async Task ReviewPage_SignedThenChanged_ExplainsWhatChanged()
    {
        // Arrange: bob signed, then the conclusion changed; the viewer is the administrator ann
        var (projectId, slotId) = await ArrangeSignedThenChangedAsync();

        // Act
        var cut = RenderReview(projectId);

        // Assert: the state, the heading of the list and the change with who did it
        Assert.Equal(
            "Signed, then the review changed", cut.Find($"[data-testid='status-signature-{slotId}']").TextContent);
        Assert.Equal(
            "What changed since Bob signed", cut.Find($"[data-testid='status-changes-summary-{slotId}']").TextContent);
        var change = cut.Find($"[data-testid='status-change-{slotId}-0']").TextContent;
        Assert.Contains("Cyd (cyd): Changed the review conclusion", change, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the review page of a review that changed after signing never says "out of date" in words the
    ///     reader sees.
    /// </summary>
    [Fact]
    public async Task ReviewPage_NoJargonOutOfDateText()
    {
        // Arrange
        var (projectId, _) = await ArrangeSignedThenChangedAsync();

        // Act
        var cut = RenderReview(projectId);

        // Assert: no visible text uses the old wording
        var text = cut.Find("[data-testid='review-page']").TextContent;
        Assert.DoesNotContain("out of date", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("out-of-date", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outdated", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Signed, then the review changed", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the signature warning tells in plain words that signed reviewers need to look again and sign
    ///     again because the report changes, and names them.
    /// </summary>
    [Fact]
    public void SignatureWarning_UsesPlainLanguage()
    {
        // Arrange
        var slots = new[] { new AffectedSignature(1, 1, "Ann", 1), new AffectedSignature(2, 2, "Bob", 2) };

        // Act
        var cut = Render<SignatureWarning>(parameters => parameters.Add(warning => warning.Slots, slots));

        // Assert
        var text = cut.Find("[data-testid='signature-warning']").TextContent;
        Assert.Contains(
            "Signed reviewers will need to look again and sign again, because the report changes:",
            text,
            StringComparison.Ordinal);
        Assert.Contains("Ann", cut.Find("[data-testid='signature-warning-slots']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("out of date", text, StringComparison.OrdinalIgnoreCase);
    }
}
