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

using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for the pure permission and kind rules of <see cref="ThreadKindRules"/>.
/// </summary>
public class ThreadKindRulesTests
{
    /// <summary>
    ///     The instant used for every time of the arranged data.
    /// </summary>
    private static readonly DateTimeOffset _time = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Builds a comment.
    /// </summary>
    /// <param name="id">The identity of the comment.</param>
    /// <param name="included">Whether the comment is included in the report.</param>
    /// <param name="kind">The kind of the comment.</param>
    /// <returns>The comment.</returns>
    private static ReviewComment Comment(int id, bool included, ReviewCommentKind kind = ReviewCommentKind.Comment)
    {
        return new ReviewComment
        {
            Id = id,
            ThreadId = 1,
            Author = "Ann",
            AuthorUsername = "ann",
            Text = "text",
            Created = _time,
            IncludeInReport = included,
            Kind = kind
        };
    }

    /// <summary>
    ///     Builds a thread with the given comments.
    /// </summary>
    /// <param name="comments">The comments in posting order.</param>
    /// <returns>The thread.</returns>
    private static ReviewThread Thread(params ReviewComment[] comments)
    {
        return new ReviewThread
        {
            Id = 1,
            ProjectId = 1,
            ReviewId = "rev-1",
            FilePath = "a.txt",
            State = ReviewThreadState.Open,
            Created = _time,
            Comments = [.. comments]
        };
    }

    /// <summary>
    ///     Test that a thread whose starting comment is included in the report is a Finding, whatever its replies say,
    ///     and that a thread without comments is a Question.
    /// </summary>
    [Fact]
    public void ThreadKindRules_KindOf_StartingCommentIncluded_IsFinding()
    {
        // Arrange: a thread with an included starting comment and an excluded reply, and a thread without comments
        var finding = Thread(Comment(1, true), Comment(2, false));
        var empty = Thread();

        // Act, Assert
        Assert.Equal(ThreadKind.Finding, ThreadKindRules.KindOf(finding));
        Assert.Equal(ThreadKind.Question, ThreadKindRules.KindOf(empty));
    }

    /// <summary>
    ///     Test that a thread whose only included comment is a reply is still a Question.
    /// </summary>
    [Fact]
    public void ThreadKindRules_KindOf_OnlyReplyIncluded_IsQuestion()
    {
        // Arrange: the starting comment is chat, the reply is included
        var thread = Thread(Comment(1, false), Comment(2, true));

        // Act, Assert
        Assert.Equal(ThreadKind.Question, ThreadKindRules.KindOf(thread));
    }

    /// <summary>
    ///     Test that the person who started a thread, a reviewer of a slot and the lead may promote, and that nobody
    ///     else may.
    /// </summary>
    [Fact]
    public void ThreadKindRules_CanPromote_Roles_StarterSlotOwnerLeadOnly()
    {
        // Arrange, Act, Assert
        Assert.True(ThreadKindRules.CanPromote(ThreadActorRole.Starter));
        Assert.True(ThreadKindRules.CanPromote(ThreadActorRole.SlotOwner));
        Assert.True(ThreadKindRules.CanPromote(ThreadActorRole.Lead));
        Assert.True(ThreadKindRules.CanPromote(ThreadActorRole.SlotOwner | ThreadActorRole.CommentAuthor));
        Assert.False(ThreadKindRules.CanPromote(ThreadActorRole.None));
        Assert.False(ThreadKindRules.CanPromote(ThreadActorRole.CommentAuthor));
    }

    /// <summary>
    ///     Test that only the person who started a thread and the lead may demote, so a reviewer who did not start it
    ///     cannot take it out of the report.
    /// </summary>
    [Fact]
    public void ThreadKindRules_CanDemote_Roles_StarterAndLeadOnly()
    {
        // Arrange, Act, Assert
        Assert.True(ThreadKindRules.CanDemote(ThreadActorRole.Starter));
        Assert.True(ThreadKindRules.CanDemote(ThreadActorRole.Lead));
        Assert.False(ThreadKindRules.CanDemote(ThreadActorRole.SlotOwner));
        Assert.False(ThreadKindRules.CanDemote(ThreadActorRole.None));
        Assert.False(ThreadKindRules.CanDemote(ThreadActorRole.CommentAuthor));
    }

    /// <summary>
    ///     Test that only the author of a reply and the lead may toggle the reply.
    /// </summary>
    [Fact]
    public void ThreadKindRules_CanToggleReply_Roles_AuthorAndLeadOnly()
    {
        // Arrange, Act, Assert
        Assert.True(ThreadKindRules.CanToggleReply(ThreadActorRole.CommentAuthor));
        Assert.True(ThreadKindRules.CanToggleReply(ThreadActorRole.Lead));
        Assert.False(ThreadKindRules.CanToggleReply(ThreadActorRole.Starter));
        Assert.False(ThreadKindRules.CanToggleReply(ThreadActorRole.SlotOwner));
        Assert.False(ThreadKindRules.CanToggleReply(ThreadActorRole.None));
    }

    /// <summary>
    ///     Test that the starting comment and a resolution note cannot be toggled on their own, and that a reply can.
    /// </summary>
    [Fact]
    public void ThreadKindRules_IsToggleable_StartingAndResolutionComments_AreNot()
    {
        // Arrange: a starting comment, a reply and a resolution note
        var starting = Comment(1, true);
        var reply = Comment(2, false);
        var resolution = Comment(3, true, ReviewCommentKind.Resolution);
        var thread = Thread(starting, reply, resolution);

        // Act, Assert
        Assert.False(ThreadKindRules.IsToggleable(thread, starting));
        Assert.True(ThreadKindRules.IsToggleable(thread, reply));
        Assert.False(ThreadKindRules.IsToggleable(thread, resolution));
        Assert.Equal("finding", ThreadKindRules.KindText(ThreadKind.Finding));
        Assert.Equal("question", ThreadKindRules.KindText(ThreadKind.Question));
    }
}
