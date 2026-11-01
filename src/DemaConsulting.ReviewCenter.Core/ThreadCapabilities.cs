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
///     What a person may do about the discussion threads of one review, so a page offers only what would be accepted.
/// </summary>
/// <remarks>
///     The facts come from <see cref="ThreadService.GetCapabilitiesAsync"/>; the rules behind them are the ones the
///     start methods enforce, so a page that follows the capabilities never offers an action that is then refused.
///     The record is an immutable value holder.
/// </remarks>
/// <param name="CanStartThread">
///     <c>true</c> when the person may start a thread about a file: they can contribute to the project and the review
///     is not concluded.
/// </param>
/// <param name="CanStartCheckThread">
///     <c>true</c> when the person may start a discussion about a check: the same as
///     <paramref name="CanStartThread"/> and <see cref="CheckThreadRules.CanStart"/> allows them.
/// </param>
public sealed record ThreadCapabilities(bool CanStartThread, bool CanStartCheckThread)
{
    /// <summary>
    ///     Gets the capabilities of a person who may start nothing.
    /// </summary>
    public static ThreadCapabilities None { get; } = new(false, false);

    /// <summary>
    ///     Gets what the person may do with each thread they may see; empty when they may do nothing, so a thread that
    ///     is not listed offers no action. A thread about a check is listed only for a viewer who may see it.
    /// </summary>
    public IReadOnlyList<ThreadActions> Threads { get; init; } = [];

    /// <summary>
    ///     Compares two capability sets by value, including the per-thread actions.
    /// </summary>
    /// <param name="other">The other capabilities.</param>
    /// <returns><c>true</c> when every flag and every per-thread action is equal.</returns>
    public bool Equals(ThreadCapabilities? other)
    {
        return other is not null
            && CanStartThread == other.CanStartThread
            && CanStartCheckThread == other.CanStartCheckThread
            && Threads.SequenceEqual(other.Threads);
    }

    /// <summary>
    ///     Gets a hash code that agrees with <see cref="Equals(ThreadCapabilities?)"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(CanStartThread, CanStartCheckThread, Threads.Count);
    }

    /// <summary>
    ///     Finds the actions the person has on a thread.
    /// </summary>
    /// <param name="threadId">The identity of the thread.</param>
    /// <returns>The actions; <see cref="ThreadActions.None"/> for a thread that is not listed.</returns>
    public ThreadActions ForThread(int threadId)
    {
        return Threads.FirstOrDefault(actions => actions.ThreadId == threadId) ?? ThreadActions.None;
    }

    /// <summary>
    ///     Tells whether the person may turn a Question into a Finding.
    /// </summary>
    /// <param name="threadId">The identity of the thread.</param>
    /// <returns><c>true</c> when the thread is an open or resolved Question the person may promote.</returns>
    public bool CanPromote(int threadId)
    {
        return ForThread(threadId).CanPromote;
    }

    /// <summary>
    ///     Tells whether the person may turn a Finding back into a Question.
    /// </summary>
    /// <param name="threadId">The identity of the thread.</param>
    /// <returns><c>true</c> when the thread is a Finding the person may demote.</returns>
    public bool CanDemote(int threadId)
    {
        return ForThread(threadId).CanDemote;
    }

    /// <summary>
    ///     Tells whether the person may resolve the thread or open it again.
    /// </summary>
    /// <param name="threadId">The identity of the thread.</param>
    /// <returns><c>true</c> when the person may change the state of the thread.</returns>
    public bool CanResolve(int threadId)
    {
        return ForThread(threadId).CanResolve;
    }

    /// <summary>
    ///     Tells whether the person may include a reply in the report or take it out again.
    /// </summary>
    /// <param name="threadId">The identity of the thread.</param>
    /// <param name="commentId">The identity of the reply.</param>
    /// <returns><c>true</c> for a reply of the thread the person wrote or, as the lead, may decide on.</returns>
    public bool CanToggleReport(int threadId, int commentId)
    {
        return ForThread(threadId).ToggleCommentIds.Contains(commentId);
    }
}

/// <summary>
///     What a person may do with one thread, so a page offers only what <see cref="ThreadService"/> would accept.
/// </summary>
/// <remarks>
///     The rules are the ones the promote, demote, resolve and report-toggle methods of the service enforce
///     (<see cref="ThreadKindRules"/>). An immutable value holder.
/// </remarks>
/// <param name="ThreadId">The identity of the thread.</param>
/// <param name="CanPromote">
///     <c>true</c> for a Question that its starter, a reviewer of the review or the lead may make a Finding; <c>false</c>
///     for a resolved Question without a resolution note (it must be reopened and resolved again with a note first).
/// </param>
/// <param name="CanDemote">
///     <c>true</c> for a Finding that its starter or the lead may make a Question again.
/// </param>
/// <param name="CanResolve">
///     <c>true</c> when the person may resolve the thread or open it again; any contributor may, while the review is
///     not concluded.
/// </param>
/// <param name="ToggleCommentIds">
///     The identities of the replies the person may include in the report or take out of it: the replies they wrote and,
///     for the lead, all replies. The starting comment and resolution notes are never listed.
/// </param>
public sealed record ThreadActions(
    int ThreadId,
    bool CanPromote,
    bool CanDemote,
    bool CanResolve,
    IReadOnlyList<int> ToggleCommentIds)
{
    /// <summary>
    ///     Gets the actions of a person who may do nothing with a thread.
    /// </summary>
    public static ThreadActions None { get; } = new(0, false, false, false, []);

    /// <summary>
    ///     Compares two action sets by value, including the replies that may be toggled.
    /// </summary>
    /// <param name="other">The other actions.</param>
    /// <returns><c>true</c> when every flag and every reply identity is equal.</returns>
    public bool Equals(ThreadActions? other)
    {
        return other is not null
            && ThreadId == other.ThreadId
            && CanPromote == other.CanPromote
            && CanDemote == other.CanDemote
            && CanResolve == other.CanResolve
            && ToggleCommentIds.SequenceEqual(other.ToggleCommentIds);
    }

    /// <summary>
    ///     Gets a hash code that agrees with <see cref="Equals(ThreadActions?)"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(ThreadId, CanPromote, CanDemote, CanResolve, ToggleCommentIds.Count);
    }
}
