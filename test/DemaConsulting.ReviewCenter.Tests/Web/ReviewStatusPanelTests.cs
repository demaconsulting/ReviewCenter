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
    ///     The password every test user signs with.
    /// </summary>
    private const string SigningPassword = "correct horse battery";

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
    ///     Creates a review in which ann and bob handed in ballots that differ on the first check, and bob had signed
    ///     the report as it was. The signature is stored directly: a disagreement blocks signing, and a signature
    ///     given before the disagreement would already be stale and give no warning.
    /// </summary>
    /// <returns>The project identity and the slot identity of bob.</returns>
    private async Task<(int ProjectId, int SlotId)> ArrangeSignedThenDisputedAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);
        var slotId = (await SlotIdsAsync(projectId))[1];
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
        return (projectId, slotId);
    }

    /// <summary>
    ///     Opens the Discuss form of the first disputed check in the status panel, types a text and chooses the Finding
    ///     kind, so that starting the discussion changes the report, and starts it.
    /// </summary>
    /// <param name="cut">The rendered review page.</param>
    /// <returns>A task that completes when the discussion was asked to start.</returns>
    private static async Task StartFindingDiscussionAsync(IRenderedComponent<ReviewPage> cut)
    {
        await cut.Find($"[data-testid='status-discuss-{FirstItemTestId}']").ClickAsync();
        await cut.Find($"[data-testid='discuss-text-{FirstItemTestId}']").InputAsync("This check needs a fix");
        await cut.Find($"[data-testid='discuss-kind-{FirstItemTestId}-finding']")
            .ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "finding" });
        await cut.Find($"[data-testid='discuss-start-{FirstItemTestId}']").ClickAsync();
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
        Assert.Equal("Where are we?", cut.Find("[data-testid='status-panel'] h2").TextContent);
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
    ///     Test that a Discuss from the status panel that adds a finding to the report, after a reviewer signed, shows
    ///     the signature warning in the form, stores nothing until it is confirmed, and then stores the discussion and
    ///     makes the signature stale.
    /// </summary>
    [Fact]
    public async Task ReviewStatusPanel_DisputedCheck_DiscussAsFindingAfterSignature_WarnsThenStoresOnConfirm()
    {
        // Arrange: bob signed the report as it was, and the viewer ann sees the dispute
        var cancellationToken = TestContext.Current.CancellationToken;
        var (projectId, slotId) = await ArrangeSignedThenDisputedAsync();
        var cut = RenderReview(projectId);

        // Act: start a discussion that goes in the report
        await StartFindingDiscussionAsync(cut);

        // Assert: the warning names bob, nothing is stored, and the form stays open with the typed text
        Assert.Contains("Bob", cut.Find("[data-testid='signature-warning-slots']").TextContent, StringComparison.Ordinal);
        Assert.Empty(await Threads.ListAsync(projectId, ReviewKey, cancellationToken));
        Assert.Equal("This check needs a fix", cut.Find($"[data-testid='discuss-text-{FirstItemTestId}']").GetAttribute("value"));

        // Act: confirm
        await cut.Find("[data-testid='signature-warning-confirm']").ClickAsync();

        // Assert: one finding about the check, no warning and no form, and bob's signature is stale
        var thread = Assert.Single(await Threads.ListAsync(projectId, ReviewKey, cancellationToken));
        Assert.Equal(FirstItemId, thread.CheckItemId);
        Assert.Equal(ThreadKind.Finding, thread.Kind);
        Assert.Empty(cut.FindAll("[data-testid='signature-warning']"));
        Assert.Empty(cut.FindAll($"[data-testid='discuss-form-{FirstItemTestId}']"));
        Assert.Equal(
            "Signed, then the review changed", cut.Find($"[data-testid='status-signature-{slotId}']").TextContent);
    }

    /// <summary>
    ///     Test that canceling the signature warning of a Discuss from the status panel stores nothing, removes the
    ///     warning and keeps the form with the typed text.
    /// </summary>
    [Fact]
    public async Task ReviewStatusPanel_DisputedCheck_DiscussWarningCancel_StoresNothing()
    {
        // Arrange: bob signed the report as it was, and the viewer ann started a finding discussion
        var (projectId, _) = await ArrangeSignedThenDisputedAsync();
        var cut = RenderReview(projectId);
        await StartFindingDiscussionAsync(cut);
        Assert.NotNull(cut.Find("[data-testid='signature-warning']"));

        // Act
        await cut.Find("[data-testid='signature-warning-cancel']").ClickAsync();

        // Assert: nothing is stored, the warning is gone and the form still holds the text
        Assert.Empty(await Threads.ListAsync(projectId, ReviewKey, TestContext.Current.CancellationToken));
        Assert.Empty(cut.FindAll("[data-testid='signature-warning']"));
        Assert.Equal("This check needs a fix", cut.Find($"[data-testid='discuss-text-{FirstItemTestId}']").GetAttribute("value"));
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
    ///     Test that a disputed check lists who answered what, and the justification of the other reviewer, with the
    ///     text encoded as plain text.
    /// </summary>
    [Fact]
    public async Task ReviewStatusPanel_DisputedCheck_ShowsAnswersAndOtherReviewersJustification()
    {
        // Arrange: ann passes; bob fails with a reason that looks like markup; the viewer is ann
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail, submit: false);
        var bob = new Actor("bob", "Bob");
        await Checklists.SetAnswerAsync(
            projectId, ReviewKey, FirstItemId, ChecklistOutcome.Fail, "<b>Too slow</b>", bob, cancellationToken);
        await Checklists.SubmitBallotAsync(projectId, ReviewKey, bob, cancellationToken);

        // Act
        var cut = RenderReview(projectId);

        // Assert: both answers, the viewer marked, and the other reviewer's reason as text (never the viewer's own)
        Assert.Equal(
            "Ann (you): Pass / Bob: Fail", cut.Find($"[data-testid='status-disputed-answers-{FirstItemTestId}']").TextContent);
        var why = cut.Find($"[data-testid='status-disputed-why-{FirstItemTestId}-2']");
        Assert.Contains("Bob: ", why.TextContent, StringComparison.Ordinal);
        Assert.Contains("<b>Too slow</b>", why.TextContent, StringComparison.Ordinal);
        Assert.Empty(why.QuerySelectorAll("b"));
        Assert.Empty(cut.FindAll($"[data-testid='status-disputed-why-{FirstItemTestId}-1']"));
    }

    /// <summary>
    ///     Test that a viewer who has not handed in sees no answer, no reason, no outcome note and no caution, and that
    ///     a signature note is only for the owner of the signature.
    /// </summary>
    [Fact]
    public async Task ReviewStatusPanel_MaskedViewer_NoAnswersNoOutcomeNoCaution()
    {
        // Arrange: ann and cyd both failed the first check and handed in; bob keeps a draft and is the viewer
        var projectId = await CreateReviewAsync("ann", "cyd", "bob");
        await AnswerAsync(projectId, "ann", ChecklistOutcome.Fail);
        await AnswerAsync(projectId, "cyd", ChecklistOutcome.Fail);
        await AnswerAsync(projectId, "bob", submit: false);
        SignInAs("bob", "Bob", false);

        // Act
        var cut = RenderReview(projectId);

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='status-outcome']"));
        Assert.Empty(cut.FindAll("[data-testid='status-caution']"));
        Assert.Empty(cut.FindAll("[data-testid='status-signature-lost']"));
        Assert.Empty(cut.FindAll("[data-testid^='status-disputed']"));
        Assert.DoesNotContain("A reason", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Rejected", cut.Find("[data-testid='status-panel']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("failed", cut.Find("[data-testid='status-panel']").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("check failed", cut.Find("[data-testid='checklist-progress-summary']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a viewer who handed in, and may see the answers, is told plainly that a check both reviewers failed
    ///     makes the review Rejected, and that the Checklist card names the failed check.
    /// </summary>
    [Fact]
    public async Task ReviewStatusPanel_BothFailed_NamesTheOutcome()
    {
        // Arrange: ann and bob both failed the first check; the viewer is ann
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann", ChecklistOutcome.Fail);
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);

        // Act
        var cut = RenderReview(projectId);

        // Assert
        var note = cut.Find("[data-testid='status-outcome']").TextContent;
        Assert.Contains(FirstItemId, note, StringComparison.Ordinal);
        Assert.Contains("Rejected", note, StringComparison.Ordinal);
        Assert.Equal("Checklist complete, 1 check failed", cut.Find("[data-testid='checklist-progress-summary']").TextContent);
    }

    /// <summary>
    ///     Test that a reviewer who could sign early is cautioned by name about the reviewer who has not handed in, and
    ///     that the reviewer who has not handed in sees no such caution.
    /// </summary>
    [Fact]
    public async Task ReviewStatusPanel_EarlySigning_ShowsCautionNamingTheMissingReviewer()
    {
        // Arrange: ann handed in, bob did not
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", submit: false);

        // Act
        var forAnn = RenderReview(projectId);
        SignInAs("bob", "Bob", false);
        var forBob = RenderReview(projectId);

        // Assert
        var caution = forAnn.Find("[data-testid='status-caution']").TextContent;
        Assert.Contains("Bob", caution, StringComparison.Ordinal);
        Assert.Contains("sign again", caution, StringComparison.Ordinal);
        Assert.Empty(forBob.FindAll("[data-testid='status-caution']"));
    }

    /// <summary>
    ///     Test that the person who started a discussion about a check waits for the answer, without a button, and that
    ///     the other reviewer is still asked to answer.
    /// </summary>
    [Fact]
    public async Task ReviewStatusPanel_OwnQuestion_AskerWaitsAndOtherPartyAnswers()
    {
        // Arrange: a dispute, and ann (the viewer) started the discussion about it
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);
        await Threads.StartThreadAsync(
            projectId, ReviewKey, ThreadAnchor.ForCheck(FirstItemId), TestActors.Ann, "Why Fail?", TestContext.Current.CancellationToken);

        // Act
        var asker = RenderReview(projectId);
        SignInAs("bob", "Bob", false);
        var other = RenderReview(projectId);

        // Assert: a sentence for the asker, a link for the other party
        var waiting = asker.Find("[data-testid='next-action']");
        Assert.Equal("Waiting for Bob to answer your question.", waiting.TextContent);
        Assert.Null(waiting.GetAttribute("href"));
        Assert.Empty(asker.FindAll("a[data-testid='next-action']"));
        Assert.Equal("Answer the open questions", other.Find("a[data-testid='next-action']").TextContent);
    }

    /// <summary>
    ///     Test that a reviewer whose signature no longer counts is told so plainly in the panel, in the words of the
    ///     inbox, and that somebody else is not.
    /// </summary>
    [Fact]
    public async Task ReviewStatusPanel_SignatureLost_StatedToTheSigner()
    {
        // Arrange: bob signed and then the review changed
        var (projectId, _) = await ArrangeSignedThenChangedAsync();
        SignInAs("bob", "Bob", false);

        // Act
        var forBob = RenderReview(projectId);
        SignInAs("dee", "Dee", false);
        var forDee = RenderReview(projectId);

        // Assert
        Assert.Equal(
            "The review changed, your signature no longer counts.",
            forBob.Find("[data-testid='status-signature-lost']").TextContent);
        Assert.Empty(forDee.FindAll("[data-testid='status-signature-lost']"));
    }

    /// <summary>
    ///     Test that after a reviewer signed, the panel and the next step of the review page both say whom the review
    ///     waits for.
    /// </summary>
    [Fact]
    public async Task ReviewStatusPanel_AfterSigning_PanelAndNextStepAgree()
    {
        // Arrange: ann and bob agree, and ann signs; the viewer is ann
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob");
        var slots = await SlotIdsAsync(projectId);
        var approval = await Signing.GetApprovalAsync(projectId, ReviewKey, TestContext.Current.CancellationToken);
        await Signing.SignAsync(
            TestActors.Ann, projectId, ReviewKey, slots[0], approval!.Digest, SigningPassword, TestContext.Current.CancellationToken);

        // Act
        var cut = RenderReview(projectId);

        // Assert
        Assert.Equal("You have signed. Waiting for Bob to sign.", cut.Find("[data-testid='next-action']").TextContent);
        Assert.Equal("Signed", cut.Find($"[data-testid='status-signature-{slots[0]}']").TextContent);
        Assert.Equal("Not signed", cut.Find($"[data-testid='status-signature-{slots[1]}']").TextContent);
        var nextStep = cut.Find("[data-testid='next-step']").TextContent;
        Assert.Contains("Waiting for the signature of Bob", nextStep, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a viewer who has not handed in is told once that the answers of the others stay hidden: the panel
    ///     says so, and the next step of the review says to hand in without repeating it.
    /// </summary>
    [Fact]
    public async Task ReviewStatusPanel_MaskedViewer_HideNoticeAppearsOnce()
    {
        // Arrange: ann handed in, bob keeps a draft and is the viewer
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", submit: false);
        SignInAs("bob", "Bob", false);

        // Act
        var cut = RenderReview(projectId);

        // Assert
        var nextStep = cut.Find("[data-testid='next-step']").TextContent;
        Assert.StartsWith("Hand in your ballot", nextStep, StringComparison.Ordinal);
        Assert.DoesNotContain("stay hidden", nextStep, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("[data-testid='status-hidden']"));
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
        Assert.Contains("2025-03-04 05:06 UTC", change, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the note above the list of changes says when the reviewer signed and that the reviewer needs to
    ///     look again and sign again.
    /// </summary>
    [Fact]
    public async Task ReviewStatusPanel_SignedThenChanged_NoteSaysToLookAgainAndSignAgain()
    {
        // Arrange: bob signed on 2025-03-03, then the conclusion changed
        var (projectId, slotId) = await ArrangeSignedThenChangedAsync();

        // Act
        var cut = RenderReview(projectId);

        // Assert: the note names the signer, the time of the signature and the way forward
        var note = cut.Find($"[data-testid='status-changes-note-{slotId}']").TextContent;
        Assert.Contains("Signed 2025-03-03 05:06 UTC", note, StringComparison.Ordinal);
        Assert.Contains("Bob needs to look", note, StringComparison.Ordinal);
        Assert.Contains("again and sign again", note, StringComparison.Ordinal);
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
