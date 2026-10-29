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
///     The persistence contract for time-based one-time password enrollments and their recovery codes, implemented
///     by the storage layer.
/// </summary>
/// <remarks>
///     Every method that decides who wins a race (<see cref="TryAdvanceStepAsync"/>,
///     <see cref="TryConsumeRecoveryCodeAsync"/>, <see cref="ConfirmAsync"/>) is a single conditional change whose
///     result says whether this caller made it, so two concurrent callers can never both succeed. A change that is
///     audited is stored in one transaction together with its audit entry. Implementations must return new objects
///     on every read and be safe for concurrent use.
/// </remarks>
public interface ITotpStore
{
    /// <summary>
    ///     Reads the enrollment of a user, pending or confirmed.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The enrollment, or <c>null</c> when the user has none.</returns>
    Task<UserTotp?> GetAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the users that have a confirmed enrollment.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The identities of the users.</returns>
    Task<IReadOnlySet<int>> ListActiveUserIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stores a pending enrollment, replacing an earlier pending one.
    /// </summary>
    /// <param name="pending">The pending enrollment; <see cref="UserTotp.ConfirmedAt"/> must be <c>null</c>. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>false</c> when the user already has a confirmed enrollment (nothing is changed); otherwise <c>true</c>.</returns>
    Task<bool> SavePendingAsync(UserTotp pending, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Confirms a pending enrollment, stores its recovery codes and the audit entry, all in one transaction.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="acceptedStep">The time step of the code that proved possession; becomes the last accepted step.</param>
    /// <param name="now">The moment of the confirmation.</param>
    /// <param name="recoveryHashes">The hashes of the new recovery codes. Must not be null.</param>
    /// <param name="audit">The audit draft saved with the change. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> for exactly one caller; <c>false</c> when the enrollment is not pending (nothing is changed).</returns>
    Task<bool> ConfirmAsync(
        int userId,
        long acceptedStep,
        DateTimeOffset now,
        IReadOnlyList<string> recoveryHashes,
        AuditDraft audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Records the time step of an accepted code, only when it is greater than the last accepted one.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="step">The time step of the code.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> for exactly one caller per step; <c>false</c> when the step is not greater than the last
    ///     accepted one (the code was used already, or is older than a code that was) or the user has no confirmed
    ///     enrollment.
    /// </returns>
    Task<bool> TryAdvanceStepAsync(int userId, long step, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Spends a recovery code and stores the audit entry in the same transaction.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="codeHash">The hash of the typed code. Must not be null.</param>
    /// <param name="now">The moment of use.</param>
    /// <param name="audit">The audit draft saved when the code was spent. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> for exactly one caller; <c>false</c> when the code is unknown or already used (nothing is changed).</returns>
    Task<bool> TryConsumeRecoveryCodeAsync(
        int userId,
        string codeHash,
        DateTimeOffset now,
        AuditDraft audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Replaces all recovery codes of a confirmed enrollment and stores the audit entry in the same transaction.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="hashes">The hashes of the new codes. Must not be null.</param>
    /// <param name="now">The moment the codes are generated.</param>
    /// <param name="audit">The audit draft. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>false</c> when the user has no confirmed enrollment (nothing is changed); otherwise <c>true</c>.</returns>
    Task<bool> ReplaceRecoveryCodesAsync(
        int userId,
        IReadOnlyList<string> hashes,
        DateTimeOffset now,
        AuditDraft audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Counts the recovery codes that are not used yet.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of unused codes.</returns>
    Task<int> CountUnusedRecoveryCodesAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Removes the enrollment of a user with its recovery codes and stores the audit entry in the same transaction.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="audit">The audit draft. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>false</c> when the user has no enrollment (nothing is changed, no entry is stored); otherwise <c>true</c>.</returns>
    Task<bool> RemoveAsync(int userId, AuditDraft audit, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Removes a pending enrollment (the user gave up); a confirmed enrollment is left alone.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when a pending enrollment was removed.</returns>
    Task<bool> RemovePendingAsync(int userId, CancellationToken cancellationToken = default);
}
