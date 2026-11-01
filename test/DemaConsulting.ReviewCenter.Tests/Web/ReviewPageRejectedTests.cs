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
///     Unit tests for what <see cref="ReviewPage"/> shows of a rejected review: the conclusion card where the
///     reviewers write the reasons, the outcome and meaning above the sign form, the Rejected badge, the lock and the
///     missing attestation.
/// </summary>
public class ReviewPageRejectedTests : PageTestBase
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
    ///     Creates the project and the review <c>REV-1</c>, seats ann (primary) with bob and, optionally, cyd in a second
    ///     slot, and lets every slot owner hand in a ballot that answers the first check Fail and the others Pass.
    /// </summary>
    /// <param name="withText"><c>true</c> to write the conclusion text as ann.</param>
    /// <param name="secondSlot"><c>true</c> to seat cyd in a second slot whose ballot also rejects.</param>
    /// <returns>The project identity and the identity of the first slot.</returns>
    private async Task<(int ProjectId, int SlotId)> CreateRejectedAsync(bool withText, bool secondSlot = false)
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
        var slot = await Signing.AddSlotAsync(TestActors.Ann, project.Id, "REV-1", ids["ann"], ids["bob"], cancellationToken);
        await RejectAsync(project.Id, TestActors.Ann);
        if (secondSlot)
        {
            await Signing.AddSlotAsync(TestActors.Ann, project.Id, "REV-1", ids["cyd"], null, cancellationToken);
            await RejectAsync(project.Id, new Actor("cyd", "Cyd"));
        }

        if (withText)
        {
            await Checklists.SetReviewConclusionTextAsync(project.Id, "REV-1", ConclusionText, TestActors.Ann, cancellationToken);
        }

        return (project.Id, slot.Id);
    }

    /// <summary>
    ///     Lets a slot owner answer the first check Fail and every other check Pass in the own ballot and hand it in.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="owner">The slot owner.</param>
    /// <returns>A task that completes when the ballot is handed in.</returns>
    private async Task RejectAsync(int projectId, Actor owner)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = true;
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await Checklists.SetAnswerAsync(
                projectId,
                "REV-1",
                item.Id,
                first ? ChecklistOutcome.Fail : ChecklistOutcome.Pass,
                first ? "Wrong." : null,
                owner,
                cancellationToken);
            first = false;
        }

        await Checklists.SubmitBallotAsync(projectId, "REV-1", owner, cancellationToken);
    }

    /// <summary>
    ///     Signs a slot of <c>REV-1</c> as ann with the current record.
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
    ///     Test that a rejection without a conclusion text lists the conclusion as the blocker, offers no sign form and
    ///     shows the conclusion editor to the slot owner.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_RejectedOutcomeBlankConclusion_ShowsBlockerAndNoSignForm()
    {
        // Arrange: the ballots agree on a Fail and no conclusion text is written
        var (projectId, slotId) = await CreateRejectedAsync(withText: false);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Contains(SigningService.ConclusionRequiredText, cut.Find($"[data-testid='sign-blockers-{slotId}']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll($"[data-testid='sign-form-{slotId}']"));
        Assert.NotNull(cut.Find("[data-testid='review-conclusion-form']"));
        Assert.Equal("In review", cut.Find("[data-testid='review-approval']").TextContent);
    }

    /// <summary>
    ///     Test that the slot owner saves the conclusion text through the card, which then stores it, removes the
    ///     blocker and offers the sign form for the rejection.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ConclusionCard_SlotOwnerSavesText_ShowsInCard()
    {
        // Arrange
        var (projectId, slotId) = await CreateRejectedAsync(withText: false);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='review-conclusion-input']").InputAsync(ConclusionText);

        // Act
        await cut.Find("[data-testid='review-conclusion-form']").SubmitAsync();

        // Assert: stored, shown in the editor, no blocker, and the sign form is for the rejection
        var review = await Store.GetReviewAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        Assert.Equal(ConclusionText, review!.ReviewConclusionText);
        Assert.Equal(ConclusionText, cut.Find("[data-testid='review-conclusion-input']").GetAttribute("value"));
        Assert.Empty(cut.FindAll($"[data-testid='sign-blockers-{slotId}']"));
        Assert.NotNull(cut.Find($"[data-testid='sign-form-{slotId}']"));
        Assert.Empty(cut.FindAll("[data-testid='review-conclusion-error']"));
    }

    /// <summary>
    ///     Test that a change of the conclusion text after a signature asks for confirmation naming the slot, and that
    ///     confirming stores it.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ConclusionCard_ChangeAfterSignature_AsksForConfirmation()
    {
        // Arrange: two slots, the conclusion text written, the first slot signed
        var (projectId, slotId) = await CreateRejectedAsync(withText: true, secondSlot: true);
        await SignAsync(projectId, slotId);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='review-conclusion-input']").InputAsync("Changed reasons.");

        // Act
        await cut.Find("[data-testid='review-conclusion-form']").SubmitAsync();

        // Assert: asked first, nothing stored yet
        Assert.Equal("Slot 1 (Ann)", cut.Find("[data-testid='signature-warning-slots']").TextContent);
        var unchanged = await Store.GetReviewAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        Assert.Equal(ConclusionText, unchanged!.ReviewConclusionText);

        // Act: confirm
        await cut.Find("[data-testid='signature-warning-confirm']").ClickAsync();

        // Assert: stored, and the signature is out of date
        var changed = await Store.GetReviewAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        Assert.Equal("Changed reasons.", changed!.ReviewConclusionText);
        Assert.Empty(cut.FindAll("[data-testid='signature-warning']"));
    }

    /// <summary>
    ///     Test that a member who owns no slot sees the conclusion text without an editor.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ConclusionCard_ReadOnlyUser_SeesTextWithoutEditor()
    {
        // Arrange: a conclusion text, viewed by cyd, who is a member but holds no slot
        var (projectId, _) = await CreateRejectedAsync(withText: true);
        SignInAs("cyd", "Cyd", isSystemAdmin: false);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Equal(ConclusionText, cut.Find("[data-testid='review-conclusion-text']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='review-conclusion-form']"));
        Assert.Empty(cut.FindAll("[data-testid='review-conclusion-input']"));
    }

    /// <summary>
    ///     Test that the sign form of a rejection says what is signed, that signing it with the password concludes the
    ///     review as rejected, and that the review is then locked without an attestation.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Sign_RejectedOutcome_ShowsRejectMeaningAndConcludes()
    {
        // Arrange: a rejection with its conclusion text
        var (projectId, slotId) = await CreateRejectedAsync(withText: true);
        var cut = RenderPage(projectId);

        // Assert: the form names the outcome and the meaning of the rejection
        Assert.Equal("You are signing: REJECTED", cut.Find($"[data-testid='sign-outcome-{slotId}']").TextContent);
        Assert.Equal(SigningService.MeaningRejectedText, cut.Find($"[data-testid='sign-meaning-{slotId}']").TextContent);
        await cut.Find($"[data-testid='sign-password-{slotId}']").InputAsync(Password);

        // Act
        await cut.Find($"[data-testid='sign-form-{slotId}']").SubmitAsync();

        // Assert: signed as a rejection and concluded
        var signature = Assert.Single(await Store.ListSignaturesAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Equal(ReviewOutcome.Rejected, signature.Outcome);
        Assert.Equal("Rejected", cut.Find("[data-testid='review-approval']").TextContent);
        Assert.Equal("Rejected. Every reviewer slot has a current signature.", cut.Find("[data-testid='next-step']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='review-locked-banner']"));
        Assert.Empty(cut.FindAll("[data-testid='attestation-download-button']"));
    }

    /// <summary>
    ///     Test that a review concluded as rejected shows the Rejected badge, the Locked badge and banner and the note
    ///     that there is no attestation of a rejection, and offers no attestation download.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_RejectedConcluded_ShowsRejectedBadgeLockedBannerAndNoAttestation()
    {
        // Arrange: a concluded, rejected review
        var (projectId, slotId) = await CreateRejectedAsync(withText: true);
        await SignAsync(projectId, slotId);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Equal("Rejected", cut.Find("[data-testid='review-approval']").TextContent);
        Assert.Contains("rejected", cut.Find("[data-testid='review-approval']").ClassName, StringComparison.Ordinal);
        Assert.Equal("Locked", cut.Find("[data-testid='review-locked']").TextContent);
        Assert.Contains("concluded and locked", cut.Find("[data-testid='review-locked-banner']").TextContent, StringComparison.Ordinal);
        Assert.Contains("rejected", cut.Find("[data-testid='attestation-rejected-note']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='attestation']"));
        Assert.Empty(cut.FindAll("[data-testid='attestation-download-button']"));
    }

    /// <summary>
    ///     Test that the conclusion card of a concluded review is read-only: it shows the text, even to the slot owner
    ///     who wrote it, without the form.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ConcludedReview_ConclusionCardIsReadOnly()
    {
        // Arrange: a concluded, rejected review, viewed by ann
        var (projectId, slotId) = await CreateRejectedAsync(withText: true);
        await SignAsync(projectId, slotId);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Equal(ConclusionText, cut.Find("[data-testid='review-conclusion-text']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='review-conclusion-form']"));
        Assert.Empty(cut.FindAll("[data-testid='review-conclusion-save']"));
    }
}
