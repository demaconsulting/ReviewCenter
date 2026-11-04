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
///     The identity of a notified item of one user: the project, the review, the inbox section and the kind of the
///     reason.
/// </summary>
/// <remarks>
///     The kind keeps two reasons that the inbox shows in the same section of the same review apart, such as the
///     standing of a reviewer and a question for the user, so that each is told on its own.
/// </remarks>
/// <param name="ProjectId">The project of the review.</param>
/// <param name="ReviewId">The identifier of the review.</param>
/// <param name="Section">The inbox section the item is shown in.</param>
/// <param name="Kind">The kind of the reason: the state token without a count, for example <c>question</c>.</param>
public sealed record NotifiedKey(int ProjectId, string ReviewId, InboxSection Section, string Kind);

/// <summary>
///     A notified item with the state token the user was told about.
/// </summary>
/// <param name="Key">The identity of the item.</param>
/// <param name="State">The state token, for example <c>to-answer</c>.</param>
/// <param name="GaveUp">
///     <c>true</c> when the server gave up mailing this state, so it was not delivered; <c>false</c> (the default)
///     when the user was told, or when the item is only recorded without a mail (the baseline).
/// </param>
public sealed record NotifiedEntry(NotifiedKey Key, string State, bool GaveUp = false);

/// <summary>
///     Everything the dispatcher changes for one user after a pass, applied in one transaction.
/// </summary>
/// <remarks>
///     Notified items and the dispatch state are saved together or not at all. The mail itself was counted before
///     it was sent (see <see cref="INotificationStore.ReserveSendAsync"/>), so the commit carries no send-log row.
///     The record is immutable; the lists must not be changed after construction. The commit is conditional: it
///     names the confirmed address and
///     the generation of the preference the pass was prepared for, and the store applies it only while both still
///     hold, so a result that was prepared before the user changed the address or the mode is discarded and nothing
///     of it is recorded.
/// </remarks>
/// <param name="NowUtc">The time of the pass (UTC); stored as the notified time of the upserted items.</param>
/// <param name="Upserts">The items to insert or whose state to update.</param>
/// <param name="RemoveKeys">The items to forget, because they left the inbox of the user.</param>
/// <param name="ReplaceAll">
///     When not <c>null</c>, the complete new set of notified items of the user; every other item is deleted (the
///     baseline). <paramref name="Upserts"/> and <paramref name="RemoveKeys"/> are ignored in that case.
/// </param>
/// <param name="State">The new dispatch state of the user.</param>
/// <param name="Address">The confirmed address the pass was prepared for.</param>
/// <param name="Generation">
///     The generation of the preference the pass was prepared for (see
///     <see cref="NotificationPreference.Generation"/>).
/// </param>
public sealed record NotificationCommit(
    DateTimeOffset NowUtc,
    IReadOnlyList<NotifiedEntry> Upserts,
    IReadOnlyList<NotifiedKey> RemoveKeys,
    IReadOnlyList<NotifiedEntry>? ReplaceAll,
    NotificationDispatchState State,
    string Address,
    int Generation);
