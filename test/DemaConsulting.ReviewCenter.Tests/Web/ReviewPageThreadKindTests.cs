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
using Microsoft.AspNetCore.Components;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the two kinds of thread on the <see cref="ReviewPage"/> component: the thread index with its
///     kind filters and counts, the summary strip, the choice of kind when a thread is started, promote and demote with
///     the signature warning and the audit trail, and the absence of every action once the review is concluded.
/// </summary>
public class ReviewPageThreadKindTests : PageTestBase
{
    /// <summary>
    ///     The password every user of these tests has.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The anchor of every thread: the whole file <c>a.txt</c>.
    /// </summary>
    private static readonly ThreadAnchor _anchor = new("a.txt");

    /// <summary>
    ///     The reviewer of the second slot.
    /// </summary>
    private static readonly Actor _cyd = new("cyd", "Cyd");

    /// <summary>
    ///     Creates the users ann (a system administrator, so the lead; also the ambient user of the page), bob and cyd,
    ///     each with the password.
    /// </summary>
    /// <returns>The identities of the users by user name.</returns>
    private async Task<Dictionary<string, int>> CreateUsersAsync()
    {
        var ids = new Dictionary<string, int>();
        foreach (var (username, name) in new[] { ("ann", "Ann"), ("bob", "Bob"), ("cyd", "Cyd") })
        {
            ids[username] = (await Users.CreateAsync(username, name, username == "ann", Password, TestContext.Current.CancellationToken)).Id;
        }

        return ids;
    }

    /// <summary>
    ///     Creates a project with the review <c>REV-1</c> of the file <c>a.txt</c>; every user is a member.
    /// </summary>
    /// <returns>The project identity.</returns>
    private async Task<int> CreateReviewAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        foreach (var user in await UserStore.ListAsync(cancellationToken))
        {
            await Store.AddMembershipAsync(user.Id, project.Id, cancellationToken);
        }

        await Reviews.CreateDraftAsync(project.Id, "REV-1", "First review", [new UploadedFile("a.txt", TestContent.A)], cancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Creates a review that can be signed: ann (primary) and bob (alternate) hold slot 1 and cyd slot 2, and ann
    ///     handed in a ballot with Pass everywhere.
    /// </summary>
    /// <returns>The project identity and the identity of the first slot.</returns>
    private async Task<(int ProjectId, int SlotId)> CreateSignableAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ids = await CreateUsersAsync();
        var projectId = await CreateReviewAsync();
        var slot = await Signing.AddSlotAsync(TestActors.Ann, projectId, "REV-1", ids["ann"], ids["bob"], cancellationToken);
        await Signing.AddSlotAsync(TestActors.Ann, projectId, "REV-1", ids["cyd"], null, cancellationToken);
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await Checklists.SetAnswerAsync(projectId, "REV-1", item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, cancellationToken);
        }

        await Checklists.SubmitBallotAsync(projectId, "REV-1", TestActors.Ann, cancellationToken);
        return (projectId, slot.Id);
    }

    /// <summary>
    ///     Signs a slot as ann with the current record.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <returns>A task that completes when the slot is signed.</returns>
    private async Task SignAsAnnAsync(int projectId, int slotId)
    {
        var approval = await Signing.GetApprovalAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        await Signing.SignAsync(
            TestActors.Ann, projectId, "REV-1", slotId, approval!.Digest, Password, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Renders the page for the review <c>REV-1</c> and chooses the file <c>a.txt</c>.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The rendered component.</returns>
    private async Task<IRenderedComponent<ReviewPage>> RenderWithFileAsync(int projectId)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, "REV-1"));
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        return cut;
    }

    /// <summary>
    ///     Starts a thread of the given kind on <c>a.txt</c> as bob.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="text">The text of the starting comment.</param>
    /// <param name="finding"><c>true</c> to start a Finding.</param>
    /// <returns>The thread.</returns>
    private Task<ReviewThread> StartAsync(int projectId, string text, bool finding)
    {
        return Threads.StartThreadAsync(
            projectId, "REV-1", _anchor, TestActors.Bob, text, TestContext.Current.CancellationToken, includeInReport: finding);
    }

    /// <summary>
    ///     Resolves a thread as ann with a note.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="threadId">The thread identity.</param>
    /// <param name="note">The resolution note.</param>
    /// <returns>A task that completes when the thread is resolved.</returns>
    private Task ResolveAsync(int projectId, int threadId, string note)
    {
        return Threads.ResolveAsync(
            projectId, "REV-1", threadId, TestActors.Ann, TestContext.Current.CancellationToken, null, note);
    }

    /// <summary>
    ///     Test that the thread index filters by kind and by state, and counts each filter: the kind counts follow the
    ///     state filter and the state counts follow the kind filter.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ThreadIndex_FiltersByKindWithCounts()
    {
        // Arrange: an open Question, an open Finding and a resolved Finding
        await CreateUsersAsync();
        var projectId = await CreateReviewAsync();
        var question = await StartAsync(projectId, "Why this way?", false);
        var openFinding = await StartAsync(projectId, "Wrong result", true);
        var resolvedFinding = await StartAsync(projectId, "Typo", true);
        await ResolveAsync(projectId, resolvedFinding.Id, "fixed in abc123");

        // Act: render the page and show every state
        var cut = await RenderWithFileAsync(projectId);
        await cut.Find("[data-testid='index-filter-all']").ClickAsync();

        // Assert: all three rows, each with its kind, and the counts of the kinds
        Assert.Equal(3, cut.FindAll("[data-testid^='index-row-']").Count);
        Assert.Equal("Question", cut.Find($"[data-testid='index-kind-{question.Id}-label']").TextContent);
        Assert.Equal("Finding", cut.Find($"[data-testid='index-kind-{openFinding.Id}-label']").TextContent);
        Assert.Equal("3", cut.Find("[data-testid='threads-kind-all-count']").TextContent);
        Assert.Equal("1", cut.Find("[data-testid='threads-questions-count']").TextContent);
        Assert.Equal("2", cut.Find("[data-testid='threads-findings-count']").TextContent);

        // Act: only the Findings
        await cut.Find("[data-testid='index-kind-finding']").ClickAsync();

        // Assert
        Assert.Empty(cut.FindAll($"[data-testid='index-row-{question.Id}']"));
        Assert.NotEmpty(cut.FindAll($"[data-testid='index-row-{openFinding.Id}']"));
        Assert.NotEmpty(cut.FindAll($"[data-testid='index-row-{resolvedFinding.Id}']"));
        Assert.Equal("2", cut.Find("[data-testid='threads-all-count']").TextContent);

        // Act: only the open Findings
        await cut.Find("[data-testid='index-filter-open']").ClickAsync();

        // Assert
        Assert.Empty(cut.FindAll($"[data-testid='index-row-{resolvedFinding.Id}']"));
        Assert.NotEmpty(cut.FindAll($"[data-testid='index-row-{openFinding.Id}']"));
        Assert.Equal("1", cut.Find("[data-testid='threads-findings-count']").TextContent);
        Assert.Equal("1", cut.Find("[data-testid='threads-questions-count']").TextContent);

        // Act: only the Questions, resolved
        await cut.Find("[data-testid='index-kind-question']").ClickAsync();
        await cut.Find("[data-testid='index-filter-resolved']").ClickAsync();

        // Assert: no resolved Question, so the list is empty
        Assert.Empty(cut.FindAll("[data-testid^='index-row-']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='thread-index-empty']"));
    }

    /// <summary>
    ///     Test that the summary strip names the open questions and the open findings separately, and the resolved
    ///     threads.
    /// </summary>
    [Fact]
    public async Task ReviewPage_SummaryStrip_ShowsQuestionAndFindingCounts()
    {
        // Arrange: two open Questions, one open Finding and one resolved Finding
        await CreateUsersAsync();
        var projectId = await CreateReviewAsync();
        await StartAsync(projectId, "One", false);
        await StartAsync(projectId, "Two", false);
        await StartAsync(projectId, "Three", true);
        var resolved = await StartAsync(projectId, "Four", true);
        await ResolveAsync(projectId, resolved.Id, "not an issue");

        // Act
        var cut = await RenderWithFileAsync(projectId);

        // Assert
        Assert.Equal("2 open questions", cut.Find("[data-testid='summary-open-questions']").TextContent);
        Assert.Equal("1 open finding", cut.Find("[data-testid='summary-open-findings']").TextContent);
        Assert.Equal("1 resolved", cut.Find("[data-testid='summary-resolved']").TextContent);
    }

    /// <summary>
    ///     Test that a thread started on the page with the Finding choice is a Finding, with the default being a
    ///     Question, and that this works on a review with a single reviewer, where the kind is not a ballot feature.
    /// </summary>
    [Fact]
    public async Task ReviewPage_SingleSlot_KindChoiceStillAvailable()
    {
        // Arrange: a review with a single slot
        var cancellationToken = TestContext.Current.CancellationToken;
        var ids = await CreateUsersAsync();
        var projectId = await CreateReviewAsync();
        await Signing.AddSlotAsync(TestActors.Ann, projectId, "REV-1", ids["ann"], null, cancellationToken);
        var cut = await RenderWithFileAsync(projectId);

        // Assert: the choice is offered and Question is the default
        Assert.True(cut.Find("[data-testid='thread-kind-choice-question']").HasAttribute("checked"));
        Assert.NotEmpty(cut.FindAll("[data-testid='thread-kind-choice-finding']"));

        // Act: start a Question without touching the choice, then a Finding
        await cut.Find("[data-testid='thread-text-input']").InputAsync("A question");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();
        await cut.Find("[data-testid='thread-kind-choice-finding']").ChangeAsync(new ChangeEventArgs { Value = "finding" });
        await cut.Find("[data-testid='thread-text-input']").InputAsync("A finding");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: one thread of each kind, and the choice is back to Question
        var stored = await Threads.ListAsync(projectId, "REV-1", cancellationToken);
        Assert.Equal(2, stored.Count);
        Assert.Equal(ThreadKind.Question, stored.Single(t => t.Comments[0].Text == "A question").Kind);
        Assert.Equal(ThreadKind.Finding, stored.Single(t => t.Comments[0].Text == "A finding").Kind);
        Assert.True(cut.Find("[data-testid='thread-kind-choice-question']").HasAttribute("checked"));
    }

    /// <summary>
    ///     Test that a thread about a check, shown in the card of check discussions, has its kind badge and the
    ///     Promote button for the lead.
    /// </summary>
    [Fact]
    public async Task ReviewPage_CheckThread_ShowsKindBadgeAndPromote()
    {
        // Arrange: a Question about the first check
        await CreateUsersAsync();
        var projectId = await CreateReviewAsync();
        var thread = await Threads.StartThreadAsync(
            projectId, "REV-1", ThreadAnchor.ForCheck(DefaultChecklist.Current.Sections[0].Items[0].Id), TestActors.Bob,
            "Is this clear?", TestContext.Current.CancellationToken);

        // Act
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, "REV-1"));
        var card = cut.Find("[data-testid='check-discussions']");
        await cut.Find($"[data-testid='thread-promote-{thread.Id}']").ClickAsync();

        // Assert: the card showed a Question, and promoting made it a Finding
        Assert.NotNull(card.QuerySelector($"[data-testid='thread-kind-{thread.Id}']"));
        var stored = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Equal(ThreadKind.Finding, stored.Kind);
        Assert.Equal("Finding", cut.Find($"[data-testid='thread-kind-{thread.Id}-label']").TextContent);
    }

    /// <summary>
    ///     Test that promoting a Question after a slot signed asks for confirmation naming the signature, stores
    ///     nothing on Cancel, and on Confirm makes the Finding, makes the signature out of date and audits the change.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Promote_AfterSignature_ShowsSignatureImpactAndAudits()
    {
        // Arrange: a resolved Question with a note, then slot 1 signs
        var (projectId, slotId) = await CreateSignableAsync();
        var thread = await StartAsync(projectId, "Why this way?", false);
        await ResolveAsync(projectId, thread.Id, "answered in chat");
        await SignAsAnnAsync(projectId, slotId);
        var cut = await RenderWithFileAsync(projectId);
        await cut.Find($"[data-testid='thread-expand-{thread.Id}']").ClickAsync();

        // Act: promote
        await cut.Find($"[data-testid='thread-promote-{thread.Id}']").ClickAsync();

        // Assert: the warning names the signature and nothing is stored yet
        Assert.Contains("Ann", cut.Find("[data-testid='signature-warning-slots']").TextContent, StringComparison.Ordinal);
        var stored = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Equal(ThreadKind.Question, stored.Kind);

        // Act: cancel, then promote again and confirm
        await cut.Find("[data-testid='signature-warning-cancel']").ClickAsync();
        Assert.Empty(cut.FindAll("[data-testid='signature-warning']"));
        await cut.Find($"[data-testid='thread-promote-{thread.Id}']").ClickAsync();
        await cut.Find("[data-testid='signature-warning-confirm']").ClickAsync();

        // Assert: a Finding, the signature is out of date, and the change is audited
        stored = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Equal(ThreadKind.Finding, stored.Kind);
        var approval = await Signing.GetApprovalAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        Assert.Equal(SlotSignatureState.OutOfDate, approval!.Slots.Single(slot => slot.SlotId == slotId).State);
        Assert.Contains(Store.Audits, audit => audit.Action == "thread.kind-changed");
        Assert.Equal("Finding", cut.Find($"[data-testid='thread-kind-{thread.Id}-label']").TextContent);
    }

    /// <summary>
    ///     Test that demoting a Finding after a slot signed names the signer who will have to sign again, and that
    ///     confirming removes the thread from the report.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Demote_AfterSignature_NamesAffectedSigners()
    {
        // Arrange: a resolved Finding, then slot 1 signs
        var (projectId, slotId) = await CreateSignableAsync();
        var thread = await StartAsync(projectId, "Wrong result", true);
        await ResolveAsync(projectId, thread.Id, "fixed in abc123");
        await SignAsAnnAsync(projectId, slotId);
        var cut = await RenderWithFileAsync(projectId);
        await cut.Find($"[data-testid='thread-expand-{thread.Id}']").ClickAsync();

        // Act: demote
        await cut.Find($"[data-testid='thread-demote-{thread.Id}']").ClickAsync();

        // Assert: the warning names the signer and nothing is stored yet
        Assert.Contains("Ann", cut.Find("[data-testid='signature-warning-slots']").TextContent, StringComparison.Ordinal);
        Assert.Equal(ThreadKind.Finding, Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken)).Kind);

        // Act: confirm
        await cut.Find("[data-testid='signature-warning-confirm']").ClickAsync();

        // Assert: a Question again, and audited
        Assert.Equal(ThreadKind.Question, Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken)).Kind);
        Assert.Contains(Store.Audits, audit => audit.Action == "thread.kind-changed");
    }

    /// <summary>
    ///     Test that a resolve note typed for a Finding survives the signature warning: the dialog stays open with the
    ///     note when the person cancels the warning, and confirming stores that note.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ResolveFinding_AfterSignature_CancelKeepsTheTypedNote()
    {
        // Arrange: a Finding that was resolved, signed by slot 1 and then opened again (reopening a Finding changes
        // nothing the signature covers)
        var cancellationToken = TestContext.Current.CancellationToken;
        var (projectId, slotId) = await CreateSignableAsync();
        var thread = await StartAsync(projectId, "Wrong result", true);
        await ResolveAsync(projectId, thread.Id, "first try");
        await SignAsAnnAsync(projectId, slotId);
        await Threads.ReopenAsync(projectId, "REV-1", thread.Id, TestActors.Ann, cancellationToken);
        var cut = await RenderWithFileAsync(projectId);

        // Act: resolve with a note, which changes the record
        await cut.Find($"[data-testid='thread-toggle-{thread.Id}']").ClickAsync();
        await cut.Find($"[data-testid='resolve-note-text-{thread.Id}']").InputAsync("fixed in abc123");
        await cut.Find($"[data-testid='resolve-note-confirm-{thread.Id}']").ClickAsync();

        // Assert: the warning names the signer, nothing is stored, and the dialog is still open
        Assert.Contains("Ann", cut.Find("[data-testid='signature-warning-slots']").TextContent, StringComparison.Ordinal);
        Assert.Equal(ReviewThreadState.Open, Assert.Single(await Threads.ListAsync(projectId, "REV-1", cancellationToken)).State);
        Assert.NotEmpty(cut.FindAll($"[data-testid='resolve-note-{thread.Id}']"));

        // Act: cancel the warning, then resolve again without typing the note again
        await cut.Find("[data-testid='signature-warning-cancel']").ClickAsync();
        Assert.NotEmpty(cut.FindAll($"[data-testid='resolve-note-{thread.Id}']"));
        await cut.Find($"[data-testid='resolve-note-confirm-{thread.Id}']").ClickAsync();
        await cut.Find("[data-testid='signature-warning-confirm']").ClickAsync();

        // Assert: resolved with the note that was typed first, and the dialog is gone
        var stored = Assert.Single(await Threads.ListAsync(projectId, "REV-1", cancellationToken));
        Assert.Equal(ReviewThreadState.Resolved, stored.State);
        Assert.Equal("fixed in abc123", stored.Comments.Last(c => c.Kind == ReviewCommentKind.Resolution).Text);
        Assert.Empty(cut.FindAll($"[data-testid='resolve-note-{thread.Id}']"));
    }

    /// <summary>
    ///     Test that adding a reply to the report and taking it out again works from the thread card, for a reply the
    ///     person may toggle (the lead may toggle any reply).
    /// </summary>
    [Fact]
    public async Task ReviewPage_ReplyReportToggle_AddsAndRemovesReplyFromReport()
    {
        // Arrange: a Finding with a reply by bob; ann is the lead
        await CreateUsersAsync();
        var projectId = await CreateReviewAsync();
        var thread = await StartAsync(projectId, "Wrong result", true);
        var reply = await Threads.ReplyAsync(projectId, "REV-1", thread.Id, TestActors.Bob, "Also here", TestContext.Current.CancellationToken);
        var cut = await RenderWithFileAsync(projectId);

        // Act: add the reply to the report
        await cut.Find($"[data-testid='comment-report-{thread.Id}-2']").ClickAsync();

        // Assert
        Assert.True(Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken)).Comments[1].IncludeInReport);
        Assert.Equal("In report", cut.Find($"[data-testid='comment-in-report-{thread.Id}-2']").TextContent);
        Assert.Equal("Remove from report", cut.Find($"[data-testid='comment-report-{thread.Id}-2']").TextContent);

        // Act: take it out again
        await cut.Find($"[data-testid='comment-report-{thread.Id}-2']").ClickAsync();

        // Assert
        Assert.False(Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken)).Comments[1].IncludeInReport);
        Assert.Equal(reply.Id, Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken)).Comments[1].Id);
    }

    /// <summary>
    ///     Test that a concluded review offers no action on its threads: no Promote, Demote, Resolve or Reopen, no
    ///     report toggle and no reply box.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Concluded_ThreadActionsAbsent()
    {
        // Arrange: a signable review with one resolved Finding, signed by both slots
        var (projectId, slotId) = await CreateSignableAsync();
        var thread = await StartAsync(projectId, "Wrong result", true);
        await Threads.ReplyAsync(projectId, "REV-1", thread.Id, TestActors.Bob, "A reply", TestContext.Current.CancellationToken);
        await ResolveAsync(projectId, thread.Id, "fixed in abc123");
        await SignAsAnnAsync(projectId, slotId);
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await Checklists.SetAnswerAsync(
                projectId, "REV-1", item.Id, ChecklistOutcome.Pass, null, _cyd, TestContext.Current.CancellationToken);
        }

        await Checklists.SubmitBallotAsync(projectId, "REV-1", _cyd, TestContext.Current.CancellationToken);
        var approval = await Signing.GetApprovalAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        var second = approval!.Slots.Single(slot => slot.SlotId != slotId);
        await Signing.SignAsync(
            _cyd, projectId, "REV-1", second.SlotId, approval.Digest, Password, TestContext.Current.CancellationToken);

        // Act
        var cut = await RenderWithFileAsync(projectId);
        await cut.Find($"[data-testid='thread-expand-{thread.Id}']").ClickAsync();

        // Assert: the review is concluded and the card has no action
        Assert.Equal("Approved", cut.Find("[data-testid='review-approval']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='thread-promote-{thread.Id}']"));
        Assert.Empty(cut.FindAll($"[data-testid='thread-demote-{thread.Id}']"));
        Assert.Empty(cut.FindAll($"[data-testid='thread-toggle-{thread.Id}']"));
        Assert.Empty(cut.FindAll("[data-testid^='comment-report-']"));
        Assert.Empty(cut.FindAll($"[data-testid='reply-box-{thread.Id}']"));
    }

    /// <summary>
    ///     Test that an auditor, who may only read, sees the kind of every thread but no action on it.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Auditor_SeesKindsButNoThreadActions()
    {
        // Arrange: a Question and a Finding, and an auditor who opens the page
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateUsersAsync();
        var projectId = await CreateReviewAsync();
        var question = await StartAsync(projectId, "Why this way?", false);
        var finding = await StartAsync(projectId, "Wrong result", true);
        var auditor = await Users.CreateAsync("aud", "Aud", UserRole.Reviewer, Password, cancellationToken);
        await Store.AddMembershipAsync(auditor.Id, projectId, ProjectRole.Auditor, cancellationToken);
        SignInAs("aud", "Aud", UserRole.Reviewer);

        // Act
        var cut = await RenderWithFileAsync(projectId);

        // Assert: the kinds show, no action does
        Assert.Equal("Question", cut.Find($"[data-testid='thread-kind-{question.Id}-label']").TextContent);
        Assert.Equal("Finding", cut.Find($"[data-testid='thread-kind-{finding.Id}-label']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid^='thread-promote-']"));
        Assert.Empty(cut.FindAll("[data-testid^='thread-demote-']"));
        Assert.Empty(cut.FindAll("[data-testid^='thread-toggle-']"));
        Assert.Empty(cut.FindAll("[data-testid^='comment-report-']"));
        Assert.Empty(cut.FindAll("[data-testid='new-thread-form']"));
    }

    /// <summary>
    ///     Test that no kind marker rests on color alone: every badge and marker on the page has a visible word or an
    ///     accessible name, and its icon is hidden from assistive technology.
    /// </summary>
    [Fact]
    public async Task ReviewPage_KindMarkers_EveryMarkerHasTextLabel()
    {
        // Arrange: a Question and a Finding on a line of the file, and one about the whole file
        await CreateUsersAsync();
        var projectId = await CreateReviewAsync();
        await StartAsync(projectId, "Why this way?", false);
        await StartAsync(projectId, "Wrong result", true);
        await Threads.StartThreadAsync(
            projectId, "REV-1", new ThreadAnchor("a.txt", 1, 1, ThreadSide.Head), TestActors.Bob, "On the line",
            TestContext.Current.CancellationToken, includeInReport: true);

        // Act
        var cut = await RenderWithFileAsync(projectId);

        // Assert: badges have a visible label, markers have an accessible name, every icon is hidden
        var badges = cut.FindAll(".kind-badge");
        Assert.NotEmpty(badges);
        Assert.All(badges, badge => Assert.Contains(badge.TextContent.Trim(), new[] { "Question", "Finding" }));
        var markers = cut.FindAll(".kind-marker");
        Assert.NotEmpty(markers);
        Assert.All(markers, marker => Assert.False(string.IsNullOrWhiteSpace(marker.GetAttribute("aria-label"))));
        Assert.All(cut.FindAll(".kind-badge svg, .kind-marker svg"), icon => Assert.Equal("true", icon.GetAttribute("aria-hidden")));
    }
}
