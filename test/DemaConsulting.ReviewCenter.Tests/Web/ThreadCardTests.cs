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

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="ThreadCard"/> component: the kind badge, the chat bubble and the "In report" mark of
///     each comment, and the buttons that the <see cref="ThreadActions"/> allow. Rendered in memory with bUnit.
/// </summary>
public class ThreadCardTests : BunitContext
{
    /// <summary>
    ///     The identity of the thread of every test.
    /// </summary>
    private const int ThreadId = 5;

    /// <summary>
    ///     The time of every comment.
    /// </summary>
    private static readonly DateTimeOffset _time = new(2025, 3, 4, 5, 6, 0, TimeSpan.Zero);

    /// <summary>
    ///     Builds a thread with a starting comment and one reply.
    /// </summary>
    /// <param name="finding"><c>true</c> when the starting comment is in the report.</param>
    /// <param name="state">The state of the thread.</param>
    /// <returns>The thread; the starting comment has the identity 11 and the reply 12.</returns>
    private static ReviewThread BuildThread(bool finding, ReviewThreadState state = ReviewThreadState.Open)
    {
        return new ReviewThread
        {
            Id = ThreadId,
            ProjectId = 1,
            ReviewId = "REV-1",
            FilePath = "a.txt",
            State = state,
            Created = _time,
            Comments =
            [
                new ReviewComment { Id = 11, ThreadId = ThreadId, Author = "Bob", AuthorUsername = "bob", Text = "Is this right?", Created = _time, IncludeInReport = finding },
                new ReviewComment { Id = 12, ThreadId = ThreadId, Author = "Cyd", AuthorUsername = "cyd", Text = "I think so", Created = _time }
            ]
        };
    }

    /// <summary>
    ///     Renders the card.
    /// </summary>
    /// <param name="thread">The thread to show.</param>
    /// <param name="actions">What the person may do.</param>
    /// <param name="calls">Receives the name of every callback that ran.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ThreadCard> RenderCard(ReviewThread thread, ThreadActions actions, List<string> calls)
    {
        return Render<ThreadCard>(parameters => parameters
            .Add(card => card.Thread, thread)
            .Add(card => card.Actions, actions)
            .Add(card => card.AnchorText, "a.txt")
            .Add(card => card.CountText, "2 comments")
            .Add(card => card.CanReply, actions.CanResolve)
            .Add(card => card.OnPromote, () => calls.Add("promote"))
            .Add(card => card.OnDemote, () => calls.Add("demote"))
            .Add(card => card.OnReopen, () => calls.Add("reopen"))
            .Add(card => card.OnResolve, (string? note) => calls.Add($"resolve:{note}"))
            .Add(card => card.OnToggleReport, (ReviewComment comment) => calls.Add($"toggle:{comment.Id}")));
    }

    /// <summary>
    ///     Test that a Question shows the neutral kind and chat bubbles without an "In report" mark.
    /// </summary>
    [Fact]
    public void ThreadCard_Question_ShowsQuestionBadgeAndChatBubbles()
    {
        // Arrange and Act
        var cut = RenderCard(BuildThread(false), ThreadActions.None, []);

        // Assert
        Assert.Equal("Question", cut.Find("[data-testid='thread-kind-5-label']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='thread-kind-5-in-report']"));
        Assert.Equal("false", cut.Find("[data-testid='comment-5-1']").GetAttribute("data-in-report"));
        Assert.Empty(cut.FindAll("[data-testid^='comment-in-report-5-']"));
        Assert.Contains("chat", cut.Find("[data-testid='comment-5-2']").ClassList);
    }

    /// <summary>
    ///     Test that a Finding shows the kind with the "In report" badge and marks the starting comment, while a reply
    ///     stays chat.
    /// </summary>
    [Fact]
    public void ThreadCard_Finding_ShowsFindingBadgeAndInReportMarkOnStartingComment()
    {
        // Arrange and Act
        var cut = RenderCard(BuildThread(true), ThreadActions.None, []);

        // Assert
        Assert.Equal("Finding", cut.Find("[data-testid='thread-kind-5-label']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='thread-kind-5-in-report']"));
        Assert.Equal("true", cut.Find("[data-testid='comment-5-1']").GetAttribute("data-in-report"));
        Assert.Equal("In report", cut.Find("[data-testid='comment-in-report-5-1']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='comment-in-report-5-2']"));
    }

    /// <summary>
    ///     Test that Promote shows only when the actions allow it (a slot owner, the starter or the lead) and is absent
    ///     for a person who may do nothing (an auditor), and that it asks the parent when clicked.
    /// </summary>
    [Fact]
    public async Task ThreadCard_PromoteVisibleForSlotOwner_HiddenForAuditor()
    {
        // Arrange: a slot owner may promote
        var calls = new List<string>();
        var owner = RenderCard(BuildThread(false), new ThreadActions(ThreadId, true, false, true, []), calls);

        // Act
        var button = owner.Find("[data-testid='thread-promote-5']");
        await button.ClickAsync();

        // Assert: the label says what it does, and the parent was asked
        Assert.Equal("Make this a Finding", button.TextContent);
        Assert.Equal("Add to report", button.GetAttribute("title"));
        Assert.Equal(["promote"], calls);
        Assert.Empty(owner.FindAll("[data-testid='thread-demote-5']"));

        // Arrange and Act: an auditor gets no actions
        var auditor = RenderCard(BuildThread(false), ThreadActions.None, []);

        // Assert
        Assert.Empty(auditor.FindAll("[data-testid='thread-promote-5']"));
        Assert.Empty(auditor.FindAll("[data-testid='thread-demote-5']"));
        Assert.Empty(auditor.FindAll("[data-testid='thread-toggle-5']"));
    }

    /// <summary>
    ///     Test that Demote shows only when the actions allow it, which is the starter and the lead, and not for any
    ///     other person, who may still resolve.
    /// </summary>
    [Fact]
    public async Task ThreadCard_DemoteHiddenForNonStarterNonLead()
    {
        // Arrange: the starter or the lead may demote
        var calls = new List<string>();
        var starter = RenderCard(BuildThread(true), new ThreadActions(ThreadId, false, true, true, []), calls);

        // Act
        var button = starter.Find("[data-testid='thread-demote-5']");
        await button.ClickAsync();

        // Assert
        Assert.Equal("Make this a Question", button.TextContent);
        Assert.Equal(["demote"], calls);
        Assert.Empty(starter.FindAll("[data-testid='thread-promote-5']"));

        // Arrange and Act: anybody else cannot demote
        var other = RenderCard(BuildThread(true), new ThreadActions(ThreadId, false, false, true, []), []);

        // Assert: Resolve stays, Demote and Promote are absent
        Assert.Empty(other.FindAll("[data-testid='thread-demote-5']"));
        Assert.Empty(other.FindAll("[data-testid='thread-promote-5']"));
        Assert.NotEmpty(other.FindAll("[data-testid='thread-toggle-5']"));
    }

    /// <summary>
    ///     Test that "Add to report" shows only on the replies the actions list, and says "Remove from report" for a
    ///     reply that is in the report.
    /// </summary>
    [Fact]
    public async Task ThreadCard_ReplyReportToggle_OnlyAuthorOrLead()
    {
        // Arrange: the person may toggle the reply (12) but the starting comment is never listed
        var calls = new List<string>();
        var cut = RenderCard(BuildThread(false), new ThreadActions(ThreadId, true, false, true, [12]), calls);

        // Act
        var toggle = cut.Find("[data-testid='comment-report-5-2']");
        await toggle.ClickAsync();

        // Assert
        Assert.Equal("Add to report", toggle.TextContent);
        Assert.Equal(["toggle:12"], calls);
        Assert.Empty(cut.FindAll("[data-testid='comment-report-5-1']"));

        // Arrange and Act: nothing listed, nothing shown
        var none = RenderCard(BuildThread(false), new ThreadActions(ThreadId, true, false, true, []), []);

        // Assert
        Assert.Empty(none.FindAll("[data-testid^='comment-report-']"));

        // Arrange and Act: a reply already in the report offers to take it out
        var thread = BuildThread(true);
        thread.Comments[1] = new ReviewComment
        {
            Id = 12,
            ThreadId = ThreadId,
            Author = "Cyd",
            AuthorUsername = "cyd",
            Text = "I think so",
            Created = _time,
            IncludeInReport = true
        };
        var included = RenderCard(thread, new ThreadActions(ThreadId, false, true, true, [12]), []);

        // Assert
        Assert.Equal("Remove from report", included.Find("[data-testid='comment-report-5-2']").TextContent);
    }

    /// <summary>
    ///     Test that resolving a Finding asks for the note first, hands the note over and shows Reopen for a resolved
    ///     thread, with the resolution note marked.
    /// </summary>
    [Fact]
    public async Task ThreadCard_ResolveFinding_AsksForNoteThenReopens()
    {
        // Arrange
        var calls = new List<string>();
        var cut = RenderCard(BuildThread(true), new ThreadActions(ThreadId, false, true, true, []), calls);

        // Act: resolve asks for the note, an empty note is refused
        await cut.Find("[data-testid='thread-toggle-5']").ClickAsync();
        await cut.Find("[data-testid='resolve-note-confirm-5']").ClickAsync();
        Assert.Empty(calls);
        await cut.Find("[data-testid='resolve-note-text-5']").InputAsync("fixed in abc123");
        await cut.Find("[data-testid='resolve-note-confirm-5']").ClickAsync();

        // Assert: the dialog stays open, with the note in it, until the thread is resolved
        Assert.Equal(["resolve:fixed in abc123"], calls);
        Assert.NotEmpty(cut.FindAll("[data-testid='resolve-note-5']"));

        // Arrange and Act: a resolved thread with a note offers Reopen and marks the note
        var resolved = BuildThread(true, ReviewThreadState.Resolved);
        resolved.Comments.Add(new ReviewComment
        {
            Id = 13,
            ThreadId = ThreadId,
            Author = "Bob",
            AuthorUsername = "bob",
            Text = "fixed in abc123",
            Created = _time,
            Kind = ReviewCommentKind.Resolution
        });
        var reopened = RenderCard(resolved, new ThreadActions(ThreadId, false, true, true, []), calls);
        await reopened.Find("[data-testid='thread-toggle-5']").ClickAsync();

        // Assert
        Assert.Equal("Reopen", reopened.Find("[data-testid='thread-toggle-5']").TextContent);
        Assert.Equal("Resolution note", reopened.Find("[data-testid='comment-note-5-3']").TextContent);
        Assert.Equal("reopen", calls[^1]);
    }
}
