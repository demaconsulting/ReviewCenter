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
///     The time-based one-time password enrollment of a local user: the encrypted secret and the replay state.
/// </summary>
/// <remarks>
///     The state lives in its own table, never in columns of <see cref="User"/>: the user store rewrites every column
///     of a user from a copy read earlier, and a stale copy must never be able to move <see cref="LastAcceptedStep"/>
///     backwards (that would re-open a used code). Only <see cref="TotpService"/> changes the row, through
///     <see cref="ITotpStore"/>, which makes every change atomic. The secret is stored encrypted
///     (<see cref="ITotpSecretProtector"/>) and is never logged, audited or shown again after enrollment. The type
///     is not thread-safe.
/// </remarks>
public sealed class UserTotp
{
    /// <summary>
    ///     Gets the identity of the user the enrollment belongs to; there is at most one enrollment per user.
    /// </summary>
    public int UserId { get; init; }

    /// <summary>
    ///     Gets the secret as encrypted by <see cref="ITotpSecretProtector"/>; never the secret itself.
    /// </summary>
    public string ProtectedSecret { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the moment the enrollment was started (UTC); a pending enrollment expires a short time after it.
    /// </summary>
    public DateTimeOffset Created { get; init; }

    /// <summary>
    ///     Gets the moment the user proved possession of the secret with a code (UTC), or <c>null</c> while the
    ///     enrollment is only pending; a pending enrollment is ignored by sign-in.
    /// </summary>
    public DateTimeOffset? ConfirmedAt { get; init; }

    /// <summary>
    ///     Gets the time step of the last code that was accepted; a code is accepted only when its time step is
    ///     greater, so a code can never be used twice and an older code can never be used after a newer one.
    /// </summary>
    public long LastAcceptedStep { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the enrollment is confirmed and therefore required at sign-in.
    /// </summary>
    public bool IsActive => ConfirmedAt is not null;
}
