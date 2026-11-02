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
///     The groups of the inbox, in the order in which the page shows them.
/// </summary>
/// <remarks>
///     <see cref="ToAnswer"/>, <see cref="Disputed"/>, <see cref="ToSign"/>, <see cref="RepliesWaiting"/> and
///     <see cref="FailedChecks"/> need the user's action and are counted; <see cref="WaitingForOthers"/> and
///     <see cref="Updates"/> only inform. The numeric values are not stored anywhere; the order is the display order.
/// </remarks>
public enum InboxSection
{
    /// <summary>
    ///     The user holds a reviewer slot and has not handed in the ballot of the checklist yet (none or a draft).
    /// </summary>
    ToAnswer,

    /// <summary>
    ///     The user has handed in a ballot, may see the other ballots, and some of the checks or sections they answered
    ///     differently are still disputed. Never listed for a user who may not see the other ballots yet.
    /// </summary>
    Disputed,

    /// <summary>
    ///     The user is a reviewer of a slot that has no current signature, has handed in the ballot, and nothing blocks
    ///     the signature (shown as "Ready to sign").
    /// </summary>
    ToSign,

    /// <summary>
    ///     Someone answered in a thread the user took part in or started, or on the user's own review.
    /// </summary>
    RepliesWaiting,

    /// <summary>
    ///     The user's own review has checks answered Fail.
    /// </summary>
    FailedChecks,

    /// <summary>
    ///     The user holds a reviewer slot, has done their part (the ballot is handed in) and cannot sign yet, or has
    ///     signed; the review waits for other people or for open items. For information, not counted as work.
    /// </summary>
    WaitingForOthers,

    /// <summary>
    ///     A review of the user was approved or reopened by someone else since the user last looked; this section
    ///     is for information and is not counted as work.
    /// </summary>
    Updates
}

/// <summary>
///     Tells which inbox sections need the action of the user.
/// </summary>
/// <remarks>
///     One place decides it, so the number in the navigation badge and the sections of the page cannot differ.
/// </remarks>
internal static class InboxSections
{
    /// <summary>
    ///     Tells whether a section is work for the user.
    /// </summary>
    /// <param name="section">The section.</param>
    /// <returns><c>false</c> for <see cref="InboxSection.WaitingForOthers"/> and <see cref="InboxSection.Updates"/>.</returns>
    public static bool IsAction(InboxSection section)
    {
        return section is not (InboxSection.WaitingForOthers or InboxSection.Updates);
    }
}

/// <summary>
///     One row of the inbox: a review, why it is listed and whether it is new to the user.
/// </summary>
/// <remarks>
///     A review can appear in several sections, once in each. The record is an immutable value holder and is safe to
///     share between threads.
/// </remarks>
/// <param name="Section">The group the row belongs to.</param>
/// <param name="ProjectId">The identity of the owning project.</param>
/// <param name="ProjectName">The name of the owning project.</param>
/// <param name="ReviewId">The review identifier.</param>
/// <param name="Title">The review title.</param>
/// <param name="Chip">The short reason the review is listed, for example <c>Not signed</c> or <c>2 waiting</c>.</param>
/// <param name="IsUnread">Whether the reason is new to the user: it happened after the user last saw the review.</param>
/// <param name="DueDate">The due date of the review, or <c>null</c> when it has none.</param>
/// <param name="IsOverdue">Whether the due date is past and the review is not approved.</param>
/// <param name="Detail">An optional sentence with more about the reason, or <c>null</c>.</param>
public sealed record InboxItem(
    InboxSection Section,
    int ProjectId,
    string ProjectName,
    string ReviewId,
    string Title,
    string Chip,
    bool IsUnread,
    DateOnly? DueDate,
    bool IsOverdue,
    string? Detail);

/// <summary>
///     The inbox of one user: the rows and the number of reviews that need the user's action.
/// </summary>
/// <remarks>
///     The record is an immutable value holder and is safe to share between threads.
/// </remarks>
/// <param name="Items">The rows ordered by section, then by project name and review identifier.</param>
/// <param name="ActionCount">
///     The number of distinct reviews with a row in <see cref="InboxSection.ToAnswer"/>,
///     <see cref="InboxSection.Disputed"/>, <see cref="InboxSection.ToSign"/>, <see cref="InboxSection.RepliesWaiting"/>
///     or <see cref="InboxSection.FailedChecks"/>; a review in several of them counts once, and
///     <see cref="InboxSection.WaitingForOthers"/> and <see cref="InboxSection.Updates"/> never count.
/// </param>
public sealed record InboxView(IReadOnlyList<InboxItem> Items, int ActionCount)
{
    /// <summary>
    ///     Gets the empty inbox.
    /// </summary>
    public static InboxView Empty { get; } = new([], 0);

    /// <summary>
    ///     Gets the number of rows that only inform.
    /// </summary>
    public int UpdateCount => Items.Count(item => item.Section == InboxSection.Updates);
}
