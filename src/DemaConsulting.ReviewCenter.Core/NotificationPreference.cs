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
///     The e-mail address of a user for notifications: the confirmed address, and the address that waits for its
///     confirmation code.
/// </summary>
/// <remarks>
///     The state lives in its own table, never in columns of <see cref="User"/>: the user store rewrites every
///     column of a user from a copy read earlier, and a stale copy must never undo a confirmation or reset the
///     number of wrong attempts. Only <see cref="NotificationPreferenceService"/> changes the row, through
///     <see cref="INotificationStore"/>, which makes every change atomic. A new address is stored as
///     <see cref="PendingEmail"/> and becomes <see cref="Email"/> only when the person typed back the code that
///     was mailed to it; the code itself is never stored, only a salted hash of it. The address is personal data
///     of its owner: it is never written to the audit trail and no administrator page shows it. The type is not
///     thread-safe.
/// </remarks>
public sealed class NotificationPreference
{
    /// <summary>
    ///     Gets the identity of the user the row belongs to; there is at most one row per user.
    /// </summary>
    public int UserId { get; init; }

    /// <summary>
    ///     Gets the confirmed address (normalized, lower case), or <c>null</c> while there is none.
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    ///     Gets the address that waits for its confirmation code, or <c>null</c> while none waits.
    /// </summary>
    public string? PendingEmail { get; init; }

    /// <summary>
    ///     Gets the salted hash of the confirmation code that was mailed to <see cref="PendingEmail"/>; <c>null</c>
    ///     while no address waits.
    /// </summary>
    public string? PendingCodeHash { get; init; }

    /// <summary>
    ///     Gets the salt of <see cref="PendingCodeHash"/>; <c>null</c> while no address waits.
    /// </summary>
    public string? PendingCodeSalt { get; init; }

    /// <summary>
    ///     Gets the moment the confirmation code stops being accepted (UTC), or <c>null</c> while no address waits.
    /// </summary>
    public DateTimeOffset? PendingCodeExpiresUtc { get; init; }

    /// <summary>
    ///     Gets the number of wrong attempts to type the code of the waiting address.
    /// </summary>
    public int PendingAttempts { get; init; }

    /// <summary>
    ///     Gets the moment the last confirmation mail was sent for the waiting address (UTC), or <c>null</c> while
    ///     no address waits; a new mail is refused for a short time after it.
    /// </summary>
    public DateTimeOffset? PendingSentUtc { get; init; }

    /// <summary>
    ///     Gets the notification mode the user chose; <see cref="NotificationMode.None"/> until the user chooses one.
    /// </summary>
    public NotificationMode Mode { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the items that exist now must be recorded as already known, without a
    ///     mail, at the next pass of the dispatcher; set when a mode is switched on.
    /// </summary>
    public bool BaselinePending { get; init; }

    /// <summary>
    ///     Gets the moment the batch window of the mode <see cref="NotificationMode.Every"/> started (UTC), or
    ///     <c>null</c> while none runs.
    /// </summary>
    public DateTimeOffset? BatchStartedUtc { get; init; }

    /// <summary>
    ///     Gets the moment the last daily slot was handled (UTC), or <c>null</c> when none was.
    /// </summary>
    public DateTimeOffset? DigestHandledUtc { get; init; }

    /// <summary>
    ///     Gets the number of notification mails in a row that could not be sent.
    /// </summary>
    public int FailureCount { get; init; }

    /// <summary>
    ///     Gets the earliest moment of the next attempt to send after a failure (UTC), or <c>null</c> while none
    ///     waits.
    /// </summary>
    public DateTimeOffset? NextAttemptUtc { get; init; }

    /// <summary>
    ///     Gets the moment the row last changed (UTC).
    /// </summary>
    public DateTimeOffset Updated { get; init; }
}
