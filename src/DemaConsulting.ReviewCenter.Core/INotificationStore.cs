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
///     The outcome of asking the store to reserve a confirmation mail.
/// </summary>
public enum ConfirmationReservation
{
    /// <summary>The mail may be sent: the pending address and the code hash are stored and the mail is counted.</summary>
    Reserved,

    /// <summary>The last confirmation mail for this user was sent too recently; nothing is stored.</summary>
    TooSoon,

    /// <summary>The user already received the most confirmation mails allowed per day; nothing is stored.</summary>
    UserLimitReached,

    /// <summary>The address already received the most confirmation mails allowed per day; nothing is stored.</summary>
    AddressLimitReached,

    /// <summary>The server already sent the most mails allowed per hour; nothing is stored.</summary>
    GlobalLimitReached
}

/// <summary>
///     What the store needs to reserve a confirmation mail: the new pending state and the limits to apply.
/// </summary>
/// <remarks>
///     The limits are applied by the store inside the transaction that stores the pending state, so two requests
///     that race can not both slip under a limit. The decision itself is <see cref="Decide"/>, shared by every
///     store so that they can not disagree. The record is immutable and thread-safe.
/// </remarks>
/// <param name="UserId">The user who asks for the confirmation.</param>
/// <param name="Address">The normalized address to confirm.</param>
/// <param name="CodeHash">The salted hash of the code that is mailed.</param>
/// <param name="CodeSalt">The salt of <paramref name="CodeHash"/>.</param>
/// <param name="Now">The current time (UTC).</param>
/// <param name="ExpiresUtc">The moment the code stops being accepted (UTC).</param>
/// <param name="Cooldown">The time after a mail during which the user may not ask for another.</param>
/// <param name="MaxPerUserPerDay">The most confirmation mails per user in 24 hours.</param>
/// <param name="MaxPerAddressPerDay">The most confirmation mails per target address in 24 hours, over all users.</param>
/// <param name="MaxPerHour">The most mails of any kind the server sends in one hour.</param>
public sealed record ConfirmationRequest(
    int UserId,
    string Address,
    string CodeHash,
    string CodeSalt,
    DateTimeOffset Now,
    DateTimeOffset ExpiresUtc,
    TimeSpan Cooldown,
    int MaxPerUserPerDay,
    int MaxPerAddressPerDay,
    int MaxPerHour)
{
    /// <summary>
    ///     Decides whether a confirmation mail may be sent.
    /// </summary>
    /// <param name="lastSentUtc">When the last confirmation mail for the user was sent; <c>null</c> when none was.</param>
    /// <param name="userCountLastDay">How many confirmation mails the user received in the last 24 hours.</param>
    /// <param name="addressCountLastDay">How many confirmation mails the address received in the last 24 hours.</param>
    /// <param name="serverCountLastHour">How many mails the server sent in the last hour.</param>
    /// <returns>The outcome; the first limit that is reached wins, in the order too soon, user, address, server.</returns>
    public ConfirmationReservation Decide(
        DateTimeOffset? lastSentUtc,
        int userCountLastDay,
        int addressCountLastDay,
        int serverCountLastHour)
    {
        if (lastSentUtc is { } last && Now - last < Cooldown)
        {
            return ConfirmationReservation.TooSoon;
        }

        if (userCountLastDay >= MaxPerUserPerDay)
        {
            return ConfirmationReservation.UserLimitReached;
        }

        if (addressCountLastDay >= MaxPerAddressPerDay)
        {
            return ConfirmationReservation.AddressLimitReached;
        }

        return serverCountLastHour >= MaxPerHour
            ? ConfirmationReservation.GlobalLimitReached
            : ConfirmationReservation.Reserved;
    }
}

/// <summary>
///     The persistence port of e-mail notifications: the address of a user with its confirmation, and the log of
///     mails that the sending limits count.
/// </summary>
/// <remarks>
///     Every change that must have exactly one winner is atomic in the implementation (a conditional update whose
///     affected-row count is the answer), and a change that is audited is saved in the same transaction as its audit
///     entry, so a change and its entry exist together or not at all. Methods return copies, never tracked
///     entities. Implementations are thread-safe.
/// </remarks>
public interface INotificationStore
{
    /// <summary>
    ///     Tells whether the tables of e-mail notifications exist in the database.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>false</c> when the database was created before e-mail notifications existed and must be recreated.</returns>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the preference row of a user.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The row; <c>null</c> when the user never entered an address.</returns>
    Task<NotificationPreference?> GetAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Applies the sending limits and, when the mail may be sent, stores the pending address with the hash of its
    ///     code and counts the mail, all in one transaction together with the audit entry.
    /// </summary>
    /// <param name="request">The new pending state and the limits. Must not be null.</param>
    /// <param name="audit">The audit entry saved with the change. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The outcome and, for <see cref="ConfirmationReservation.Reserved"/>, the identity of the counted mail
    ///     (for <see cref="AbortConfirmationAsync"/>); zero otherwise.
    /// </returns>
    Task<(ConfirmationReservation Outcome, int LogId)> ReserveConfirmationAsync(
        ConfirmationRequest request,
        AuditDraft audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Takes back a reservation whose mail could not be sent: the mail is no longer counted and the pending
    ///     state of that mail is cleared.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="logId">The identity returned by <see cref="ReserveConfirmationAsync"/>.</param>
    /// <param name="codeHash">The hash that was stored; a newer pending state with another hash is left alone.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the reservation is taken back.</returns>
    Task AbortConfirmationAsync(int userId, int logId, string codeHash, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Counts one wrong-or-right attempt to type the code, only while the code is the given one and fewer than
    ///     the allowed number of attempts were made.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="codeHash">The hash of the code the attempt is for.</param>
    /// <param name="maxAttempts">The number of attempts allowed.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the attempt was counted; <c>false</c> when no such code waits or no attempt is left.</returns>
    Task<bool> TryCountAttemptAsync(
        int userId,
        string codeHash,
        int maxAttempts,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Makes the pending address the confirmed address, only while the pending code is the given one and has not
    ///     expired at the given time, together with the audit entry.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="codeHash">The hash of the code that was typed correctly.</param>
    /// <param name="now">The current time (UTC); a code that expires at or before it is not confirmed.</param>
    /// <param name="audit">The audit entry saved with the change. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the address was confirmed by this call; <c>false</c> when the pending state changed or the
    ///     code has expired.
    /// </returns>
    Task<bool> ConfirmAsync(
        int userId,
        string codeHash,
        DateTimeOffset now,
        AuditDraft audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Removes the confirmed and the pending address of a user, together with the audit entry.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="audit">The audit entry saved with the change. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when a row was removed; <c>false</c> when the user had no address.</returns>
    Task<bool> RemoveAsync(int userId, AuditDraft audit, CancellationToken cancellationToken = default);
}
