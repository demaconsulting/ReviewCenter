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
///     One inbox item a user was already told about, in the state the user was told about it.
/// </summary>
/// <remarks>
///     The row is the dedupe memory of the notification dispatcher: an item (user, project, review, inbox
///     section and kind of reason) is mailed only when it has no row or when its state is new compared with
///     <see cref="State"/> (see <see cref="NotificationComposer.IsNew"/>). The state
///     is a short token such as <c>to-answer</c> or <c>replies:3</c>; it never holds an outcome word, a comment
///     or any other content. The type is not thread-safe.
/// </remarks>
public sealed class NotifiedItem
{
    /// <summary>
    ///     Gets the identity of the row, assigned by the database.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    ///     Gets the user who was told.
    /// </summary>
    public int UserId { get; init; }

    /// <summary>
    ///     Gets the project of the review.
    /// </summary>
    public int ProjectId { get; init; }

    /// <summary>
    ///     Gets the identifier of the review.
    /// </summary>
    public string ReviewId { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the inbox section the item was shown in.
    /// </summary>
    public InboxSection Section { get; init; }

    /// <summary>
    ///     Gets the kind of the reason: the state token without a count (see <see cref="NotifiedKey.Kind"/>), so the
    ///     same review can hold a row for each reason that the inbox shows in one section.
    /// </summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the state token the user was told about.
    /// </summary>
    public string State { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the moment the user was told, or the moment the item was recorded without a mail (UTC).
    /// </summary>
    public DateTimeOffset NotifiedUtc { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the server gave up mailing <see cref="State"/> (the sending failed until the
    ///     retries were used up), so the user was not told. A row like that only keeps the server from mailing the same
    ///     state again; a later new state of the item is mailed as usual and records the row with <c>false</c>.
    /// </summary>
    public bool GaveUp { get; init; }
}
