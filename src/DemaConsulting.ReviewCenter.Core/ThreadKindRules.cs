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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     The ways a person can be related to a thread or to one of its comments, which decide what they may do with
///     the report content of the thread.
/// </summary>
/// <remarks>
///     A person can hold several relations at once, so the values are flags. They are facts about the person
///     (looked up by <see cref="ThreadService"/>), never granted by the caller.
/// </remarks>
[Flags]
internal enum ThreadActorRole
{
    /// <summary>
    ///     No relation to the thread beyond being a contributor of the project.
    /// </summary>
    None = 0,

    /// <summary>
    ///     The person wrote the starting comment of the thread.
    /// </summary>
    Starter = 1,

    /// <summary>
    ///     The person is the primary or the alternate reviewer of any reviewer slot of the review.
    /// </summary>
    SlotOwner = 2,

    /// <summary>
    ///     The person leads the review: its creator who can still contribute, or an administrator of the project.
    /// </summary>
    Lead = 4,

    /// <summary>
    ///     The person wrote the comment in question.
    /// </summary>
    CommentAuthor = 8
}

/// <summary>
///     The pure rules that decide what is a Finding or a Question and who may change that.
/// </summary>
/// <remarks>
///     Kept apart from <see cref="ThreadService"/> so the permission table and the derivation of the kind can be
///     read and tested without a store: a thread is a Finding exactly when its starting comment is included in the
///     report; promoting or demoting it flips that flag. The class is stateless and thread-safe.
/// </remarks>
internal static class ThreadKindRules
{
    /// <summary>
    ///     Derives the kind of a thread from its starting comment.
    /// </summary>
    /// <param name="thread">The thread. Must not be null.</param>
    /// <returns><see cref="ThreadKind.Finding"/> when the starting comment is included in the report, otherwise
    /// <see cref="ThreadKind.Question"/>.</returns>
    public static ThreadKind KindOf(ReviewThread thread)
    {
        return thread.Kind;
    }

    /// <summary>
    ///     Tells whether a person may turn a Question into a Finding.
    /// </summary>
    /// <param name="roles">The relations of the person to the thread and the review.</param>
    /// <returns><c>true</c> for the person who started the thread, a reviewer of any slot, or the lead.</returns>
    public static bool CanPromote(ThreadActorRole roles)
    {
        return (roles & (ThreadActorRole.Starter | ThreadActorRole.SlotOwner | ThreadActorRole.Lead)) != 0;
    }

    /// <summary>
    ///     Tells whether a person may turn a Finding back into a Question.
    /// </summary>
    /// <param name="roles">The relations of the person to the thread and the review.</param>
    /// <returns><c>true</c> for the person who started the thread or the lead; a reviewer who did not start it may
    /// promote a thread but not take it out of the report again.</returns>
    public static bool CanDemote(ThreadActorRole roles)
    {
        return (roles & (ThreadActorRole.Starter | ThreadActorRole.Lead)) != 0;
    }

    /// <summary>
    ///     Tells whether a person may include a reply in the report or take it out again.
    /// </summary>
    /// <param name="roles">The relations of the person to the thread, the comment and the review.</param>
    /// <returns><c>true</c> for the author of the reply or the lead.</returns>
    public static bool CanToggleReply(ThreadActorRole roles)
    {
        return (roles & (ThreadActorRole.CommentAuthor | ThreadActorRole.Lead)) != 0;
    }

    /// <summary>
    ///     Tells whether the report flag of a comment may be toggled on its own.
    /// </summary>
    /// <param name="thread">The thread the comment belongs to. Must not be null.</param>
    /// <param name="comment">The comment. Must not be null.</param>
    /// <returns><c>false</c> for the starting comment (its flag is the kind of the thread, changed by promoting or
    /// demoting) and for a resolution note (it follows the thread); <c>true</c> for an ordinary reply.</returns>
    public static bool IsToggleable(ReviewThread thread, ReviewComment comment)
    {
        var isStarting = thread.Comments.Count > 0 && thread.Comments[0].Id == comment.Id;
        return !isStarting && comment.Kind == ReviewCommentKind.Comment;
    }

    /// <summary>
    ///     Words a kind for audit details and messages.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The lowercase word for the kind.</returns>
    public static string KindText(ThreadKind kind)
    {
        return kind == ThreadKind.Finding ? "finding" : "question";
    }
}
