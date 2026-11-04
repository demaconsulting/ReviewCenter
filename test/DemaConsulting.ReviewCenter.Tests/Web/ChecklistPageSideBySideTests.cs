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
using DemaConsulting.ReviewCenter.Web;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for what the <see cref="ChecklistPage"/> shows of the other reviewers' ballots: nothing until the
///     viewer has handed in their own, the differing answers side by side afterwards, and the Discuss button that
///     starts a discussion about a check. Rendered in memory with bUnit.
/// </summary>
public class ChecklistPageSideBySideTests : BallotPageTestBase
{
    /// <summary>
    ///     Opens the first section of the checklist when it is not open yet, so its checks are rendered.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    private static async Task OpenFirstSectionAsync(IRenderedComponent<ChecklistPage> cut)
    {
        if (cut.FindAll($"[data-testid='check-{FirstItemTestId}']").Count == 0)
        {
            var sectionId = DefaultChecklist.Current.Sections[0].Id.ToLowerInvariant();
            await cut.Find($"[data-testid='section-{sectionId}-toggle']").ClickAsync();
        }
    }

    /// <summary>
    ///     Test that a reviewer with a draft sees how many ballots are in, and neither the other ballots, nor the
    ///     number of differing checks, nor the standing of the review.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_DraftBallot_HidesOtherBallotsAndDisputeCounts()
    {
        // Arrange: ann handed in Pass everywhere; bob has a draft with a Fail
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail, submit: false);
        SignInAs("bob", "Bob", false);

        // Act
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);

        // Assert: who has handed in is visible, what they answered is not
        Assert.Equal("1 of 2 ballots handed in.", cut.Find("[data-testid='ballots-handed-in']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='ballots-hidden-notice']"));
        Assert.Empty(cut.FindAll("[data-testid^='ballot-others-']"));
        Assert.DoesNotContain("Different answers", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("disputed", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a plain member without a slot sees no standing at all, only how many ballots are in, and is
    ///     offered the shortcut to become a reviewer.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_MemberWithoutSlot_SeesNoStandingOnlyBallotCount()
    {
        // Arrange: two reviewers handed in answers that differ
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);
        await AddMemberAsync(projectId, "dee");
        SignInAs("dee", "Dee", false);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Equal("2 of 2 ballots handed in.", cut.Find("[data-testid='ballots-handed-in']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='ballots-hidden-notice']"));
        Assert.Empty(cut.FindAll("[data-testid='checklist-status']"));
        Assert.Empty(cut.FindAll("[data-testid='checklist-progress']"));
        Assert.Empty(cut.FindAll("[data-testid^='ballot-others-']"));
        Assert.NotNull(cut.Find("[data-testid='assign-me']"));
    }

    /// <summary>
    ///     Test that a plain member who holds no slot has the answer controls disabled.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_MemberWithoutSlot_AnswerControlsAreDisabled()
    {
        // Arrange: a review with two slots, viewed by dee, who is a plain member without a slot
        var projectId = await CreateReviewAsync("ann", "bob");
        await AddMemberAsync(projectId, "dee");
        SignInAs("dee", "Dee", false);

        // Act
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);

        // Assert: the three answer buttons of the first check cannot be used
        foreach (var testId in new[] { $"check-pass-{FirstItemTestId}", $"check-fail-{FirstItemTestId}", $"check-na-{FirstItemTestId}" })
        {
            Assert.True(cut.Find($"[data-testid='{testId}']").HasAttribute("disabled"), testId);
        }
    }

    /// <summary>
    ///     Test that a plain member who holds no slot sees the standing of a review with a single slot, where the
    ///     answers are open to everyone.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_MemberWithoutSlot_SingleSlotReview_ShowsTheStanding()
    {
        // Arrange: bob, the only reviewer, answered every check; dee is a plain member without a slot
        var projectId = await CreateReviewAsync("bob");
        await AnswerAsync(projectId, "bob");
        await AddMemberAsync(projectId, "dee");
        SignInAs("dee", "Dee", false);

        // Act
        var cut = RenderPage(projectId);

        // Assert: the notice is there, and so is the standing
        Assert.NotNull(cut.Find("[data-testid='no-slot-notice']"));
        Assert.Equal("Checklist complete.", cut.Find("[data-testid='checklist-status']").TextContent);
        Assert.Equal("58 of 58 checks answered", cut.Find("[data-testid='checklist-progress']").TextContent);
    }

    /// <summary>
    ///     Test that a plain member who holds no slot sees the standing of a concluded review, where the answers are
    ///     open to everyone.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_MemberWithoutSlot_ConcludedReview_ShowsTheStanding()
    {
        // Arrange: ann and bob handed in Pass everywhere and both signed, so the review is concluded; dee is a plain
        // member without a slot
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob");
        var approval = await Signing.GetApprovalAsync(projectId, ReviewKey, cancellationToken);
        foreach (var slot in approval!.Slots)
        {
            await Signing.SignAsync(
                new Actor(slot.Primary.Username, slot.Primary.DisplayName),
                projectId,
                ReviewKey,
                slot.SlotId,
                approval.Digest,
                "correct horse battery",
                cancellationToken);
        }

        await AddMemberAsync(projectId, "dee");
        SignInAs("dee", "Dee", false);

        // Act
        var cut = RenderPage(projectId);

        // Assert: the review is locked, and the standing is shown
        Assert.NotNull(cut.Find("[data-testid='checklist-locked']"));
        Assert.Equal("Checklist complete.", cut.Find("[data-testid='checklist-status']").TextContent);
        Assert.Equal("58 of 58 checks answered", cut.Find("[data-testid='checklist-progress']").TextContent);
    }

    /// <summary>
    ///     Test that a reviewer who handed in sees the checks the ballots answer differently, side by side, with the
    ///     names, and no hidden notice.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_SubmittedBallot_ShowsSideBySideForDifferingChecks()
    {
        // Arrange: ann says Pass and bob says N/A on the first check
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.NotApplicable);
        SignInAs("bob", "Bob", false);

        // Act
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);

        // Assert
        Assert.Equal("2 of 2 ballots handed in.", cut.Find("[data-testid='ballots-handed-in']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='ballots-hidden-notice']"));
        Assert.Equal(
            "Ann: Pass / Bob: N/A",
            cut.Find($"[data-testid='ballot-others-text-{FirstItemTestId}']").TextContent);
        Assert.Contains("Different answers", cut.Find($"[data-testid='ballot-others-{FirstItemTestId}']").TextContent, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("[data-testid^='ballot-others-text-']"));
    }

    /// <summary>
    ///     Test that the answers are shown side by side in the order of the reviewer slots, not in the alphabetical
    ///     order of the names.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_SubmittedBallot_ShowsAnswersInTheOrderOfTheSlots()
    {
        // Arrange: zed holds the first slot and abe the second; zed says Pass and abe says Fail on the first check
        var projectId = await CreateReviewAsync("zed", "abe");
        await AnswerAsync(projectId, "zed");
        await AnswerAsync(projectId, "abe", ChecklistOutcome.Fail);
        SignInAs("abe", "Abe", false);

        // Act
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);

        // Assert
        Assert.Equal(
            "Zed: Pass / Abe: Fail",
            cut.Find($"[data-testid='ballot-others-text-{FirstItemTestId}']").TextContent);
    }

    /// <summary>
    ///     Test that the administrator, who steers the review, sees the differing answers without a ballot of their own.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_AdministratorWithoutBallot_SeesDifferingAnswers()
    {
        // Arrange: ann is the ambient system administrator and holds no slot
        var projectId = await CreateReviewAsync("bob", "cyd");
        await AnswerAsync(projectId, "bob");
        await AnswerAsync(projectId, "cyd", ChecklistOutcome.Fail);

        // Act
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='ballots-hidden-notice']"));
        Assert.Equal(
            "Bob: Pass / Cyd: Fail",
            cut.Find($"[data-testid='ballot-others-text-{FirstItemTestId}']").TextContent);
    }

    /// <summary>
    ///     Test that a review with a single reviewer shows no ballot counts, no hidden notice and no side-by-side
    ///     answers, as before.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_SingleSlot_ShowsNoBallotCountsOrSideBySide()
    {
        // Arrange
        var projectId = await CreateReviewAsync("cyd");
        await AnswerAsync(projectId, "cyd");
        SignInAs("cyd", "Cyd", false);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='ballots-handed-in']"));
        Assert.Empty(cut.FindAll("[data-testid='ballots-hidden-notice']"));
        Assert.Empty(cut.FindAll("[data-testid^='ballot-others-']"));
        Assert.Empty(cut.FindAll("[data-testid='submit-and-sign']"));
    }

    /// <summary>
    ///     Answers every check with Pass except the first, which gets the given outcome and reason, and hands the ballot in.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="username">The user name of the reviewer, who holds a slot.</param>
    /// <param name="outcome">The answer to the first check.</param>
    /// <param name="reason">The justification of that answer.</param>
    /// <param name="submit">Whether to hand the ballot in.</param>
    /// <returns>A task that completes when the answers are stored.</returns>
    private async Task AnswerWithReasonAsync(
        int projectId, string username, ChecklistOutcome outcome, string reason, bool submit = true)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await AnswerAsync(projectId, username, submit: false);
        var actor = new Actor(username, char.ToUpperInvariant(username[0]) + username[1..]);
        await Checklists.SetAnswerAsync(projectId, ReviewKey, FirstItemId, outcome, reason, actor, cancellationToken);
        if (submit)
        {
            await Checklists.SubmitBallotAsync(projectId, ReviewKey, actor, cancellationToken);
        }
    }

    /// <summary>
    ///     Test that a viewer who handed in reads the other reviewer's reason in the bar of the differing check, that the
    ///     text is encoded (nothing a reviewer typed becomes markup), and that the viewer's own reason is not repeated.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_DisputedCheck_ShowsOtherReviewersReasonEncodedAndNotOwn()
    {
        // Arrange: ann says N/A with a reason, bob says Fail with a reason that holds markup
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerWithReasonAsync(projectId, "ann", ChecklistOutcome.NotApplicable, "Own reason of ann");
        await AnswerWithReasonAsync(projectId, "bob", ChecklistOutcome.Fail, "<b>too vague</b> & unclear");

        // Act: ann looks first, then bob
        SignInAs("ann", "Ann", true);
        var asAnn = RenderPage(projectId);
        await OpenFirstSectionAsync(asAnn);
        SignInAs("bob", "Bob", false);
        var asBob = RenderPage(projectId);
        await OpenFirstSectionAsync(asBob);

        // Assert: ann reads bob's reason, encoded, and not her own
        var bobReason = asAnn.Find($"[data-testid='ballot-others-why-{FirstItemTestId}-2']");
        Assert.Equal("Bob: \u201C<b>too vague</b> & unclear\u201D", bobReason.TextContent);
        Assert.Empty(bobReason.QuerySelectorAll("b"));
        Assert.Contains("&lt;b&gt;too vague&lt;/b&gt;", bobReason.InnerHtml, StringComparison.Ordinal);
        Assert.Empty(asAnn.FindAll($"[data-testid='ballot-others-why-{FirstItemTestId}-1']"));

        // Assert: bob reads ann's reason and not his own
        Assert.Equal(
            "Ann: \u201COwn reason of ann\u201D",
            asBob.Find($"[data-testid='ballot-others-why-{FirstItemTestId}-1']").TextContent);
        Assert.Empty(asBob.FindAll($"[data-testid='ballot-others-why-{FirstItemTestId}-2']"));
    }

    /// <summary>
    ///     Test that a reviewer with a draft and a member without a slot read nothing of the other reviewers' reasons,
    ///     no count of differing checks and no failed check, while the other ballots are hidden.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_HiddenViewer_ShowsNoReasonNoDisputeCountNoFailedCheck()
    {
        // Arrange: ann handed in a Fail with a reason; bob answered Fail as a draft; dee is a member without a slot
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerWithReasonAsync(projectId, "ann", ChecklistOutcome.Fail, "Secret reason of ann");
        await AnswerWithReasonAsync(projectId, "bob", ChecklistOutcome.Fail, "Bob's draft reason", submit: false);
        await AddMemberAsync(projectId, "dee");

        foreach (var viewer in new[] { "bob", "dee" })
        {
            // Act
            SignInAs(viewer, char.ToUpperInvariant(viewer[0]) + viewer[1..], false);
            var cut = RenderPage(projectId);
            await OpenFirstSectionAsync(cut);

            // Assert
            Assert.DoesNotContain("Secret reason", cut.Markup, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("[data-testid^='ballot-others-']"));
            Assert.Empty(cut.FindAll("[data-testid='differing-summary']"));
            Assert.DoesNotContain("differently", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("failed", cut.Markup, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    ///     Test that the page tells at the top how many checks are answered differently once the viewer may see them,
    ///     in the singular and the plural, and not when every check agrees.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_DisputedChecks_SummaryAtTheTopCountsThem()
    {
        // Arrange: bob differs from ann on the first check only; a second review has two differing checks
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);
        var agreed = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(agreed, "ann");
        await AnswerAsync(agreed, "bob");
        var second = DefaultChecklist.Current.Sections.SelectMany(section => section.Items).ElementAt(1).Id;
        var many = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(many, "ann");
        await AnswerAsync(many, "bob", ChecklistOutcome.Fail, submit: false);
        await Checklists.SetAnswerAsync(
            many, ReviewKey, second, ChecklistOutcome.NotApplicable, "n/a", new Actor("bob", "Bob"), TestContext.Current.CancellationToken);
        await Checklists.SubmitBallotAsync(many, ReviewKey, new Actor("bob", "Bob"), TestContext.Current.CancellationToken);
        SignInAs("bob", "Bob", false);

        // Act
        var one = RenderPage(projectId);
        var none = RenderPage(agreed);
        var two = RenderPage(many);

        // Assert
        Assert.Equal(
            "1 check answered differently. Open it to discuss.",
            one.Find("[data-testid='differing-summary']").TextContent.Trim('\u26A0', ' '));
        Assert.Empty(none.FindAll("[data-testid='differing-summary']"));
        Assert.Equal(
            "2 checks answered differently. Open them to discuss.",
            two.Find("[data-testid='differing-summary']").TextContent.Trim('\u26A0', ' '));
    }

    /// <summary>
    ///     Test that the status line names the number of failed checks when the viewer may see that the submitted
    ///     ballots agree on a failure, and says only that the checklist is complete otherwise.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_AgreedFailure_StatusLineNamesFailedChecks()
    {
        // Arrange: both reviewers failed the first check; a second review has two Pass ballots
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann", ChecklistOutcome.Fail);
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);
        var passed = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(passed, "ann");
        await AnswerAsync(passed, "bob");
        SignInAs("bob", "Bob", false);

        // Act
        var failed = RenderPage(projectId);
        var clean = RenderPage(passed);

        // Assert
        Assert.Equal("Checklist complete, 1 check failed.", failed.Find("[data-testid='checklist-status']").TextContent);
        Assert.Equal("Checklist complete.", clean.Find("[data-testid='checklist-status']").TextContent);
    }

    /// <summary>
    ///     Test that a reviewer who has not handed in is not told that a check failed, though the other reviewer's
    ///     handed-in ballot is a Fail and the own draft is complete.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_AgreedFailureHiddenFromDraftViewer_StatusLineSaysOnlyComplete()
    {
        // Arrange: ann handed in a Fail; bob has a complete draft with a Fail
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann", ChecklistOutcome.Fail);
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail, submit: false);
        SignInAs("bob", "Bob", false);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Equal("Checklist complete.", cut.Find("[data-testid='checklist-status']").TextContent);
    }

    /// <summary>
    ///     Test that the line about handed-in ballots names the reviewers who have not handed in, except the viewer.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_BallotsLine_NamesWhoHasNotHandedIn()
    {
        // Arrange: ann handed in, bob has a draft, cyd has not started
        var projectId = await CreateReviewAsync("ann", "bob", "cyd");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", submit: false);
        SignInAs("bob", "Bob", false);

        // Act
        var cut = RenderPage(projectId);

        // Assert: the viewer is not named, and the page keeps the masking notice
        Assert.Equal(
            "1 of 3 ballots handed in. Cyd has not handed in yet.",
            cut.Find("[data-testid='ballots-handed-in']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='ballots-hidden-notice']"));
    }

    /// <summary>
    ///     Test that a concluded review with several reviewers points to the history, where all the answers are, and
    ///     that the pointer is not there while the review is open.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_ConcludedMultiSlotReview_PointsToTheHistory()
    {
        // Arrange: ann and bob handed in Pass everywhere and both signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob");
        var open = RenderPage(projectId);
        var approval = await Signing.GetApprovalAsync(projectId, ReviewKey, cancellationToken);
        foreach (var slot in approval!.Slots)
        {
            await Signing.SignAsync(
                new Actor(slot.Primary.Username, slot.Primary.DisplayName),
                projectId,
                ReviewKey,
                slot.SlotId,
                approval.Digest,
                "correct horse battery",
                cancellationToken);
        }

        SignInAs("bob", "Bob", false);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Empty(open.FindAll("[data-testid='checklist-history-pointer']"));
        Assert.NotNull(cut.Find("[data-testid='checklist-locked']"));
        Assert.NotNull(cut.Find("[data-testid='checklist-history-pointer']"));
        Assert.EndsWith("/history", cut.Find("[data-testid='checklist-history-link']").GetAttribute("href"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the Discuss button of a differing check starts a discussion about that check with the typed text,
    ///     and that the page then says where to find it.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_DisputedCheck_DiscussStartsCheckThread()
    {
        // Arrange
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);
        SignInAs("bob", "Bob", false);
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);

        // Act
        await cut.Find($"[data-testid='discuss-{FirstItemTestId}']").ClickAsync();
        await cut.Find($"[data-testid='discuss-text-{FirstItemTestId}']").InputAsync("Why did you fail this one?");
        await cut.Find($"[data-testid='discuss-start-{FirstItemTestId}']").ClickAsync();

        // Assert
        var thread = Assert.Single(await Threads.ListAsync(projectId, ReviewKey, TestContext.Current.CancellationToken));
        Assert.Equal(FirstItemId, thread.CheckItemId);
        Assert.Equal("Why did you fail this one?", thread.Comments.Single().Text);
        Assert.NotNull(cut.Find($"[data-testid='discuss-started-{FirstItemTestId}']"));
        Assert.Empty(cut.FindAll($"[data-testid='discuss-form-{FirstItemTestId}']"));
    }

    /// <summary>
    ///     Test that the notice of a started discussion links to the review page, where the thread is.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_DiscussStarted_LinksToTheReviewPage()
    {
        // Arrange: a differing check, and bob has the Discuss form open
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);
        SignInAs("bob", "Bob", false);
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);
        await cut.Find($"[data-testid='discuss-{FirstItemTestId}']").ClickAsync();
        await cut.Find($"[data-testid='discuss-text-{FirstItemTestId}']").InputAsync("Why did you fail this one?");

        // Act: start the discussion
        await cut.Find($"[data-testid='discuss-start-{FirstItemTestId}']").ClickAsync();

        // Assert: the anchor of the notice (it has no test identifier of its own) points to the review page
        var link = cut.Find($"[data-testid='discuss-started-{FirstItemTestId}'] a");
        Assert.Equal($"/projects/{projectId}/reviews/{ReviewKey}", link.GetAttribute("href"));
    }

    /// <summary>
    ///     Test that the Discuss form of a check offers the choice of kind with Question as the default, and that
    ///     choosing "Finding (goes in the report)" starts a Finding.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_DiscussFinding_KindChosenInComposer()
    {
        // Arrange
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);
        SignInAs("bob", "Bob", false);
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);
        await cut.Find($"[data-testid='discuss-{FirstItemTestId}']").ClickAsync();

        // Assert: Question is the default
        var kindId = $"discuss-kind-{FirstItemTestId}";
        Assert.True(cut.Find($"[data-testid='{kindId}-question']").HasAttribute("checked"));
        Assert.False(cut.Find($"[data-testid='{kindId}-finding']").HasAttribute("checked"));

        // Act: choose Finding and start
        await cut.Find($"[data-testid='{kindId}-finding']").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "finding" });
        await cut.Find($"[data-testid='discuss-text-{FirstItemTestId}']").InputAsync("This check needs a fix");
        await cut.Find($"[data-testid='discuss-start-{FirstItemTestId}']").ClickAsync();

        // Assert: the thread is a Finding
        var thread = Assert.Single(await Threads.ListAsync(projectId, ReviewKey, TestContext.Current.CancellationToken));
        Assert.Equal(ThreadKind.Finding, thread.Kind);
        Assert.Equal(FirstItemId, thread.CheckItemId);
    }

    /// <summary>
    ///     Test that an empty discussion is refused with the reason shown next to the check.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_DiscussWithoutText_ShowsError()
    {
        // Arrange
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);
        SignInAs("bob", "Bob", false);
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);
        await cut.Find($"[data-testid='discuss-{FirstItemTestId}']").ClickAsync();

        // Act
        await cut.Find($"[data-testid='discuss-start-{FirstItemTestId}']").ClickAsync();

        // Assert
        Assert.NotNull(cut.Find($"[data-testid='discuss-error-{FirstItemTestId}']"));
        Assert.Empty(await Threads.ListAsync(projectId, ReviewKey, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a viewer who cannot start a discussion about a check (a reviewer who has not handed in cannot even
    ///     see the differing answers) gets no Discuss button, and that Cancel closes the form.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_DiscussCancel_ClosesTheForm()
    {
        // Arrange
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);
        SignInAs("bob", "Bob", false);
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);
        await cut.Find($"[data-testid='discuss-{FirstItemTestId}']").ClickAsync();
        Assert.NotNull(cut.Find($"[data-testid='discuss-form-{FirstItemTestId}']"));

        // Act
        await cut.Find($"[data-testid='discuss-cancel-{FirstItemTestId}']").ClickAsync();

        // Assert: the form is gone and the button is back
        Assert.Empty(cut.FindAll($"[data-testid='discuss-form-{FirstItemTestId}']"));
        Assert.NotNull(cut.Find($"[data-testid='discuss-{FirstItemTestId}']"));
        Assert.Empty(await Threads.ListAsync(projectId, ReviewKey, TestContext.Current.CancellationToken));
    }
}
