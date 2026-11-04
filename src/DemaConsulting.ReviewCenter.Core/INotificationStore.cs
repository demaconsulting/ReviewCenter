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
///     The outcome of asking the store to reserve the send of a notification mail.
/// </summary>
public enum SendReservation
{
    /// <summary>The mail may be sent: it is counted from now on.</summary>
    Reserved,

    /// <summary>The server already sent the most mails allowed per hour; nothing is stored.</summary>
    ServerHourLimitReached,

    /// <summary>The user already received the most mails allowed per day; nothing is stored.</summary>
    UserDayLimitReached
}

/// <summary>
///     The decision of the sending limits for a notification mail, shared by every store so that they can not
///     disagree.
/// </summary>
/// <remarks>
///     The server limit counts every mail of any kind (confirmation mails included) of the last hour, and the user
///     limit counts every mail of any kind of the user in the last 24 hours. The class is static and thread-safe.
/// </remarks>
public static class SendLimits
{
    /// <summary>
    ///     Decides whether a notification mail may be sent.
    /// </summary>
    /// <param name="serverCountLastHour">How many mails of any kind the server sent in the last hour.</param>
    /// <param name="userCountLastDay">How many mails of any kind the user received in the last 24 hours.</param>
    /// <param name="maxPerHour">The most mails of any kind the server sends in one hour.</param>
    /// <param name="maxPerUserPerDay">The most mails of any kind one user receives in 24 hours.</param>
    /// <returns>The outcome; the server limit is looked at first.</returns>
    public static SendReservation Decide(
        int serverCountLastHour,
        int userCountLastDay,
        int maxPerHour,
        int maxPerUserPerDay)
    {
        if (serverCountLastHour >= maxPerHour)
        {
            return SendReservation.ServerHourLimitReached;
        }

        return userCountLastDay >= maxPerUserPerDay
            ? SendReservation.UserDayLimitReached
            : SendReservation.Reserved;
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
    ///     expired at the given time, together with the audit entry. The generation of the row rises by one.
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
    ///     Removes the confirmed and the pending address of a user, together with the audit entry. The notification
    ///     mode, the dispatch state and the notified items of the user are removed with it, in the same transaction.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="audit">The audit entry saved with the change. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when a row was removed; <c>false</c> when the user had no address.</returns>
    Task<bool> RemoveAsync(int userId, AuditDraft audit, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Sets the notification mode of a user, together with the audit entry, when the user has a row and, for a
    ///     mode other than <see cref="NotificationMode.None"/>, a confirmed address.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="mode">The new mode.</param>
    /// <param name="nowUtc">The current time (UTC).</param>
    /// <param name="audit">The audit entry saved with the change. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the mode changed; <c>false</c> when nothing changed and no audit entry was written.</returns>
    /// <remarks>
    ///     A change clears the batch window and the failure state and raises the generation of the row by one.
    ///     Switching on from <see cref="NotificationMode.None"/> marks the baseline as pending; switching between
    ///     the active modes keeps the baseline as it is, so a baseline that is still pending stays pending and the
    ///     backlog is never mailed; switching to <see cref="NotificationMode.None"/> clears the baseline mark, and
    ///     deletes the notified items and the time of the last digest.
    /// </remarks>
    Task<bool> SetModeAsync(
        int userId,
        NotificationMode mode,
        DateTimeOffset nowUtc,
        AuditDraft audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the users who chose a mode other than <see cref="NotificationMode.None"/> and have a confirmed
    ///     address.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The recipients with their dispatch state, ordered by user identity.</returns>
    Task<IReadOnlyList<NotificationRecipient>> ListRecipientsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the items a user was already told about.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The notified items of the user.</returns>
    Task<IReadOnlyList<NotifiedItem>> GetNotifiedAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Applies the result of a dispatcher pass for one user in one transaction: notified items and dispatch
    ///     state. Send-log rows older than one day are deleted in the same transaction.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="commit">What to change. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when applied; <c>false</c> when nothing was changed because the user has no row, the mode is
    ///     none, the confirmed address is not <see cref="NotificationCommit.Address"/>, or the generation is not
    ///     <see cref="NotificationCommit.Generation"/> any more.
    /// </returns>
    /// <remarks>
    ///     The conditions are checked in the same conditional update that writes the dispatch state, so a commit that
    ///     was prepared before a change of the address or the mode can never be applied.
    /// </remarks>
    Task<bool> ApplyAsync(int userId, NotificationCommit commit, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Applies the sending limits and, when the mail may be sent, counts the mail by writing its send-log row, all
    ///     in one serialized transaction, so that senders that race (another server instance, a confirmation mail)
    ///     can not both slip under a limit. Send-log rows older than one day are deleted in the same transaction.
    /// </summary>
    /// <param name="userId">The user the mail is for.</param>
    /// <param name="kind">The kind of the mail, for example <see cref="NotificationSendLogEntry.KindUpdate"/>.</param>
    /// <param name="address">The address the mail goes to.</param>
    /// <param name="now">The current time (UTC); the time stored as the time of the mail.</param>
    /// <param name="maxPerHour">The most mails of any kind the server sends in one hour.</param>
    /// <param name="maxPerUserPerDay">The most mails of any kind one user receives in 24 hours.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The outcome (decided by <see cref="SendLimits.Decide"/>) and, for <see cref="SendReservation.Reserved"/>,
    ///     the identity of the counted mail (for <see cref="ReleaseSendAsync"/>); zero otherwise.
    /// </returns>
    /// <remarks>
    ///     The mail is counted from the moment it is reserved, so a mail whose sender crashes between the
    ///     reservation and the send stays counted.
    /// </remarks>
    Task<(SendReservation Outcome, int LogId)> ReserveSendAsync(
        int userId,
        string kind,
        string address,
        DateTimeOffset now,
        int maxPerHour,
        int maxPerUserPerDay,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Takes back a reservation whose mail was not sent: the mail is no longer counted.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="logId">The identity returned by <see cref="ReserveSendAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the reservation is taken back; nothing happens when it is gone.</returns>
    Task ReleaseSendAsync(int userId, int logId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Counts the mails of the send log since a moment.
    /// </summary>
    /// <param name="userId">The user whose mails to count; <c>null</c> to count the mails of all users.</param>
    /// <param name="sinceUtc">The start of the period (UTC).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of mails of every kind sent at or after <paramref name="sinceUtc"/>.</returns>
    Task<int> CountSentAsync(int? userId, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);
}
