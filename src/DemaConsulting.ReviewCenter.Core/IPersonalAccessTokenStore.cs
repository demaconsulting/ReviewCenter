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
///     The persistence contract for personal access tokens, implemented by the storage layer.
/// </summary>
/// <remarks>
///     Tokens have their own store so the user store stays about accounts. The store only persists and finds; every
///     rule (limits, expiry, who may revoke) lives in <see cref="PersonalAccessTokenService"/>. Implementations must
///     return new <see cref="PersonalAccessToken"/> objects on every read.
/// </remarks>
public interface IPersonalAccessTokenStore
{
    /// <summary>
    ///     Adds a token.
    /// </summary>
    /// <param name="token">The token to add; <see cref="PersonalAccessToken.Id"/> is ignored. Must not be null.</param>
    /// <param name="audit">
    ///     Builds the audit draft from the stored token; saved in the same transaction as the token. Must not be null.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored token with its assigned identity.</returns>
    Task<PersonalAccessToken> AddAsync(
        PersonalAccessToken token,
        Func<PersonalAccessToken, AuditDraft> audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the tokens of a user, including revoked and expired ones.
    /// </summary>
    /// <param name="userId">The identity of the owner.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The tokens, newest first; empty when there are none.</returns>
    Task<IReadOnlyList<PersonalAccessToken>> ListForUserAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Finds a token by the hash of its value.
    /// </summary>
    /// <param name="tokenHash">The lowercase hexadecimal SHA-256 hash of the presented token value.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The token with <see cref="PersonalAccessToken.User"/> loaded, or <c>null</c> when none has the hash.</returns>
    Task<PersonalAccessToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Revokes a token of a user.
    /// </summary>
    /// <param name="tokenId">The identity of the token.</param>
    /// <param name="userId">The identity of the owner; a token of another user is not touched.</param>
    /// <param name="when">The moment of the revocation.</param>
    /// <param name="audit">
    ///     Builds the audit draft from the revoked token; saved in the same transaction. Must not be null.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The revoked token, or <c>null</c> when the user has no such token or it was already revoked.</returns>
    Task<PersonalAccessToken?> RevokeAsync(
        int tokenId,
        int userId,
        DateTimeOffset when,
        Func<PersonalAccessToken, AuditDraft> audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Records that a token was used, but only if its last-used moment is still the one the caller saw; changes only
    ///     <see cref="PersonalAccessToken.LastUsed"/> and is not audited.
    /// </summary>
    /// <param name="tokenId">The identity of the token.</param>
    /// <param name="expectedLastUsed">
    ///     The last-used moment the caller read (<c>null</c> for a token never used). The moment is stored only when
    ///     the stored value is still equal to this one, as one atomic update, so of several concurrent callers that
    ///     read the same value exactly one writes.
    /// </param>
    /// <param name="when">The moment of the use.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the moment was stored; <c>false</c> when another caller changed it first.</returns>
    Task<bool> TouchAsync(
        int tokenId,
        DateTimeOffset? expectedLastUsed,
        DateTimeOffset when,
        CancellationToken cancellationToken = default);
}
