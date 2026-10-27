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
///     A personal access token: a revocable, expiring credential that lets a user call the API without a password.
/// </summary>
/// <remarks>
///     Instances are created by <see cref="PersonalAccessTokenService"/>, which enforces the rules, and are populated
///     by the storage layer. Only the SHA-256 hash of the token value is stored (<see cref="TokenHash"/>), never the
///     value, so a copy of the database cannot be used to call the API. <see cref="DisplayId"/> is a random,
///     non-secret short identifier that has no relation to the secret; it is what lists and audit entries show. The
///     type is not thread-safe.
/// </remarks>
public sealed class PersonalAccessToken
{
    /// <summary>
    ///     Gets the identity assigned by the store when the token is added.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    ///     Gets the identity of the user the token acts as.
    /// </summary>
    public int UserId { get; init; }

    /// <summary>
    ///     Gets or sets the owner; loaded only by lookups that need it (the authentication of a request), otherwise <c>null</c>.
    /// </summary>
    public User? User { get; set; }

    /// <summary>
    ///     Gets the name the owner gave the token to remember what it is used for (1 to
    ///     <see cref="ReviewLimits.MaxTokenNameLength"/> characters).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the random, non-secret short identifier shown in lists and recorded in the audit trail.
    /// </summary>
    public string DisplayId { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the lowercase hexadecimal SHA-256 hash of the token value; the only trace of the value that is kept.
    /// </summary>
    public string TokenHash { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the moment the token was created (UTC).
    /// </summary>
    public DateTimeOffset Created { get; init; }

    /// <summary>
    ///     Gets the moment the token stops working (UTC); every token expires.
    /// </summary>
    public DateTimeOffset Expires { get; init; }

    /// <summary>
    ///     Gets or sets the moment the token was last used, rounded to <see cref="ReviewLimits.TokenLastUsedGranularity"/>
    ///     by how often it is written, or <c>null</c> when it was never used.
    /// </summary>
    public DateTimeOffset? LastUsed { get; set; }

    /// <summary>
    ///     Gets or sets the moment the token was revoked, or <c>null</c> when it was not.
    /// </summary>
    public DateTimeOffset? Revoked { get; set; }

    /// <summary>
    ///     Decides whether the token can be used at a moment.
    /// </summary>
    /// <param name="now">The moment to check.</param>
    /// <returns><c>true</c> when the token is not revoked and has not expired.</returns>
    public bool IsActive(DateTimeOffset now)
    {
        return Revoked is null && Expires > now;
    }
}
