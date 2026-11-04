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
///     The identity of a notified item of one user: the project, the review and the inbox section.
/// </summary>
/// <param name="ProjectId">The project of the review.</param>
/// <param name="ReviewId">The identifier of the review.</param>
/// <param name="Section">The inbox section the item is shown in.</param>
public sealed record NotifiedKey(int ProjectId, string ReviewId, InboxSection Section);

/// <summary>
///     A notified item with the state token the user was told about.
/// </summary>
/// <param name="Key">The identity of the item.</param>
/// <param name="State">The state token, for example <c>to-answer</c>.</param>
public sealed record NotifiedEntry(NotifiedKey Key, string State);

/// <summary>
///     The send-log row that is written together with the notified items of a mail.
/// </summary>
/// <param name="Kind">The kind of the mail, for example <see cref="NotificationSendLogEntry.KindUpdate"/>.</param>
/// <param name="Address">The address the mail was sent to.</param>
/// <param name="SentUtc">When the mail was sent (UTC).</param>
public sealed record NotificationLogRecord(string Kind, string Address, DateTimeOffset SentUtc);

/// <summary>
///     Everything the dispatcher changes for one user after a pass, applied in one transaction.
/// </summary>
/// <remarks>
///     Notified items, the dispatch state and the send-log row are saved together or not at all, so a crash can
///     not leave a mail recorded without its items or the items without the mail. The record is immutable; the
///     lists must not be changed after construction.
/// </remarks>
/// <param name="NowUtc">The time of the pass (UTC); stored as the notified time of the upserted items.</param>
/// <param name="Upserts">The items to insert or whose state to update.</param>
/// <param name="RemoveKeys">The items to forget, because they left the inbox of the user.</param>
/// <param name="ReplaceAll">
///     When not <c>null</c>, the complete new set of notified items of the user; every other item is deleted (the
///     baseline). <paramref name="Upserts"/> and <paramref name="RemoveKeys"/> are ignored in that case.
/// </param>
/// <param name="State">The new dispatch state of the user.</param>
/// <param name="Log">The send-log row to write; <c>null</c> when no mail was sent.</param>
public sealed record NotificationCommit(
    DateTimeOffset NowUtc,
    IReadOnlyList<NotifiedEntry> Upserts,
    IReadOnlyList<NotifiedKey> RemoveKeys,
    IReadOnlyList<NotifiedEntry>? ReplaceAll,
    NotificationDispatchState State,
    NotificationLogRecord? Log);
