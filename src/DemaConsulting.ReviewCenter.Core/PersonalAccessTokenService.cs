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

using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     A newly created personal access token together with its value, which exists only in this result.
/// </summary>
/// <remarks>
///     The value is the only copy: it is neither stored nor recorded, so the caller must show it to the owner at once
///     and then drop it. The record is immutable.
/// </remarks>
/// <param name="Token">The stored token (without its value).</param>
/// <param name="Value">The token value the owner presents to the API; never stored, logged or audited.</param>
public sealed record CreatedPersonalAccessToken(PersonalAccessToken Token, string Value);

/// <summary>
///     Why a presented token was not accepted; for the audit trail only, never shown to the caller of the API.
/// </summary>
public enum TokenFailure
{
    /// <summary>The token was accepted.</summary>
    None,

    /// <summary>The presented text does not have the shape of a token.</summary>
    Malformed,

    /// <summary>No stored token matches.</summary>
    Unknown,

    /// <summary>The token has expired.</summary>
    Expired,

    /// <summary>The token was revoked.</summary>
    Revoked,

    /// <summary>The owner is disabled.</summary>
    OwnerDisabled,

    /// <summary>The owner is locked out of signing in.</summary>
    OwnerLockedOut
}

/// <summary>
///     The outcome of checking a presented token.
/// </summary>
/// <remarks>The record is immutable.</remarks>
/// <param name="Failure">Why the token was refused, or <see cref="TokenFailure.None"/> when it was accepted.</param>
/// <param name="Token">The matching token when one was found (also for some refusals, so the audit can name it); otherwise <c>null</c>.</param>
/// <param name="User">The owner with the current role and display name when the token was accepted; otherwise <c>null</c>.</param>
public sealed record TokenValidation(TokenFailure Failure, PersonalAccessToken? Token, User? User)
{
    /// <summary>
    ///     Gets a value indicating whether the token was accepted.
    /// </summary>
    public bool Succeeded => Failure == TokenFailure.None;
}

/// <summary>
///     Creates, lists, revokes and checks personal access tokens, enforcing the token rules.
/// </summary>
/// <remarks>
///     A token value is <c>rcpat_</c> followed by the base64url text of 32 random bytes; it is a secret and
///     nothing else (the short <see cref="PersonalAccessToken.DisplayId"/> is drawn separately, so showing it
///     reveals nothing of the secret). Only the SHA-256 hash of the value is stored. A presented token is checked by
///     hashing it, finding the row by the hash and then verifying the stored hash with a fixed-time comparison, so
///     the check neither trusts the lookup nor leaks how much of a hash matched. Every token expires (30, 90 or 365
///     days), at most <see cref="ReviewLimits.MaxActiveTokensPerUser"/> are active per user, and a token always acts
///     with the current role of its owner. The operations that create or revoke run one at a time (a semaphore), which
///     makes the limit race-free; the service must therefore be a single instance (a singleton). Token values are
///     never logged or put into messages or the audit trail.
/// </remarks>
public sealed class PersonalAccessTokenService
{
    /// <summary>
    ///     The text every token value starts with, so a leaked value is recognizable (for example by secret scanners).
    /// </summary>
    public const string TokenPrefix = "rcpat_";

    /// <summary>
    ///     The number of random bytes in the secret part of a token (256 bits).
    /// </summary>
    private const int SecretByteCount = 32;

    /// <summary>
    ///     The length of the base64url text of <see cref="SecretByteCount"/> bytes, without padding.
    /// </summary>
    private const int SecretTextLength = 43;

    /// <summary>
    ///     The number of characters of <see cref="PersonalAccessToken.DisplayId"/>.
    /// </summary>
    private const int DisplayIdLength = 8;

    /// <summary>
    ///     The characters of a display id: digits and capital letters without the easily confused I, L, O and U.
    /// </summary>
    private const string DisplayIdAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    ///     The store that persists tokens.
    /// </summary>
    private readonly IPersonalAccessTokenStore _store;

    /// <summary>
    ///     The store that reads the owners of tokens.
    /// </summary>
    private readonly IUserStore _users;

    /// <summary>
    ///     The clock that supplies creation, expiry and use times.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Serializes the operations that create or revoke tokens.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    ///     Initializes a new instance of the <see cref="PersonalAccessTokenService"/> class.
    /// </summary>
    /// <param name="store">The store that persists tokens. Must not be null.</param>
    /// <param name="users">The store that reads the owners of tokens. Must not be null.</param>
    /// <param name="timeProvider">The clock; injected so tests control time. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public PersonalAccessTokenService(IPersonalAccessTokenStore store, IUserStore users, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _users = users;
        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     Creates a token for the user who is signed in.
    /// </summary>
    /// <param name="actor">The signed-in user; the token acts as this user. Must not be null.</param>
    /// <param name="name">
    ///     What the token is for; leading and trailing white space is removed. Must not be empty, must not contain
    ///     control characters and must not exceed <see cref="ReviewLimits.MaxTokenNameLength"/> characters.
    /// </param>
    /// <param name="expiryDays">The lifetime; one of <see cref="ReviewLimits.TokenExpiryChoicesInDays"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored token and its value; the value cannot be read again.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown when a rule is violated: <see cref="DomainRule.TokenNameInvalid"/>,
    ///     <see cref="DomainRule.TokenExpiryInvalid"/>, <see cref="DomainRule.TokenLimitReached"/>, or
    ///     <see cref="DomainRule.ActorNotAllowed"/> when the actor is an API token, or the account is gone or
    ///     disabled; nothing is stored then.
    /// </exception>
    /// <remarks>
    ///     A token cannot create tokens: that would let a leaked token renew itself.
    /// </remarks>
    public async Task<CreatedPersonalAccessToken> CreateAsync(
        Actor actor,
        string? name,
        int expiryDays,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.ViaApiToken)
        {
            throw new DomainRuleException(DomainRule.ActorNotAllowed, "An API token cannot create tokens.");
        }

        // Validate everything that needs no I/O first so a bad request fails fast and cheaply
        var trimmedName = ValidateName(name);
        if (!ReviewLimits.TokenExpiryChoicesInDays.Contains(expiryDays))
        {
            throw new DomainRuleException(
                DomainRule.TokenExpiryInvalid,
                $"A token expires after {string.Join(", ", ReviewLimits.TokenExpiryChoicesInDays)} days; {expiryDays} is not one of them.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var user = await LoadOwnerAsync(actor, cancellationToken);
            var now = _timeProvider.GetUtcNow();

            // Only active tokens count, so revoking or waiting for expiry makes room again
            var existing = await _store.ListForUserAsync(user.Id, cancellationToken);
            if (existing.Count(token => token.IsActive(now)) >= ReviewLimits.MaxActiveTokensPerUser)
            {
                throw new DomainRuleException(
                    DomainRule.TokenLimitReached,
                    $"You can have at most {ReviewLimits.MaxActiveTokensPerUser} active tokens; revoke one first.");
            }

            // The secret is random and shows nothing about the display id, which is drawn on its own
            var value = TokenPrefix + Base64Url(RandomNumberGenerator.GetBytes(SecretByteCount));
            var token = new PersonalAccessToken
            {
                UserId = user.Id,
                Name = trimmedName,
                DisplayId = new string(RandomNumberGenerator.GetItems<char>(DisplayIdAlphabet, DisplayIdLength)),
                TokenHash = Hash(value),
                Created = now,
                Expires = now.AddDays(expiryDays)
            };

            // The audit entry never carries the value or its hash
            var stored = await _store.AddAsync(
                token,
                added => AuditDraft.By(
                    actor,
                    "token.created",
                    $"token '{AuditText.Clean(added.Name, ReviewLimits.MaxTokenNameLength)}' ({added.DisplayId}), expires {added.Expires:yyyy-MM-dd'T'HH:mm:ss'Z'}"),
                cancellationToken);
            return new CreatedPersonalAccessToken(stored, value);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Lists the tokens of the signed-in user.
    /// </summary>
    /// <param name="actor">The signed-in user. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The tokens, newest first, including revoked and expired ones; never a token value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ActorNotAllowed"/> when the account is gone.</exception>
    public async Task<IReadOnlyList<PersonalAccessToken>> ListAsync(Actor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var user = await LoadOwnerAsync(actor, cancellationToken);
        return await _store.ListForUserAsync(user.Id, cancellationToken);
    }

    /// <summary>
    ///     Revokes a token of the signed-in user.
    /// </summary>
    /// <param name="actor">The signed-in user, who must own the token. Must not be null.</param>
    /// <param name="tokenId">The identity of the token.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the token is revoked.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.TokenNotFound"/> when the user has no such token or it is already
    ///     revoked (a token of another user is reported the same way), or <see cref="DomainRule.ActorNotAllowed"/>
    ///     when the actor is an API token or the account is gone.
    /// </exception>
    public async Task RevokeAsync(Actor actor, int tokenId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.ViaApiToken)
        {
            throw new DomainRuleException(DomainRule.ActorNotAllowed, "An API token cannot revoke tokens.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var user = await LoadOwnerAsync(actor, cancellationToken);
            var revoked = await _store.RevokeAsync(
                tokenId,
                user.Id,
                _timeProvider.GetUtcNow(),
                token => AuditDraft.By(
                    actor,
                    "token.revoked",
                    $"token '{AuditText.Clean(token.Name, ReviewLimits.MaxTokenNameLength)}' ({token.DisplayId})"),
                cancellationToken);
            if (revoked is null)
            {
                throw new DomainRuleException(DomainRule.TokenNotFound, "The token does not exist or is already revoked.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Checks a presented token value.
    /// </summary>
    /// <param name="presented">The value from the request; may be null, empty or hostile.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     A validation that succeeds when the token exists, is not revoked or expired and its owner is enabled and not
    ///     locked out; the accepted validation carries the owner as currently stored, so a changed role applies at
    ///     once. Every refusal carries the reason for the audit trail; callers must not reveal it.
    /// </returns>
    /// <remarks>
    ///     The row is found by the hash of the value and the stored hash is verified again with
    ///     <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>, so a store
    ///     that returns the wrong row cannot authenticate anybody. A successful check records the use at most once
    ///     per <see cref="ReviewLimits.TokenLastUsedGranularity"/>, which keeps busy automation from writing on every
    ///     request.
    /// </remarks>
    public async Task<TokenValidation> AuthenticateAsync(string? presented, CancellationToken cancellationToken = default)
    {
        // A value of the wrong shape is refused without touching the store
        if (!HasTokenShape(presented))
        {
            return new TokenValidation(TokenFailure.Malformed, null, null);
        }

        // Find by the hash, then verify it again in constant time
        var presentedHash = Hash(presented!);
        var token = await _store.FindByHashAsync(presentedHash, cancellationToken);
        if (token?.User is null
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(presentedHash),
                Encoding.ASCII.GetBytes(token.TokenHash)))
        {
            return new TokenValidation(TokenFailure.Unknown, null, null);
        }

        // The token and its owner must both be usable right now
        var now = _timeProvider.GetUtcNow();
        if (token.Revoked is not null)
        {
            return new TokenValidation(TokenFailure.Revoked, token, null);
        }

        if (token.Expires <= now)
        {
            return new TokenValidation(TokenFailure.Expired, token, null);
        }

        if (!token.User.Enabled)
        {
            return new TokenValidation(TokenFailure.OwnerDisabled, token, null);
        }

        if (token.User.LockoutUntil is { } lockoutUntil && lockoutUntil > now)
        {
            return new TokenValidation(TokenFailure.OwnerLockedOut, token, null);
        }

        // Record the use, but not more often than the granularity. The store applies the change only if the last-used
        // moment is still the one read above, so a burst of requests writes once
        if (token.LastUsed is not { } lastUsed || now - lastUsed >= ReviewLimits.TokenLastUsedGranularity)
        {
            await _store.TouchAsync(token.Id, token.LastUsed, now, cancellationToken);
        }

        return new TokenValidation(TokenFailure.None, token, token.User);
    }

    /// <summary>
    ///     Computes the lowercase hexadecimal SHA-256 hash that is stored for a token value.
    /// </summary>
    /// <param name="value">The token value. Must not be null.</param>
    /// <returns>The hash over the UTF-8 bytes of the value.</returns>
    private static string Hash(string value)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>
    ///     Writes bytes as base64url text without padding.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The text, using only letters, digits, <c>-</c> and <c>_</c>.</returns>
    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    ///     Decides whether a text has the shape of a token value: the prefix and a base64url secret of the right length.
    /// </summary>
    /// <param name="text">The text; may be null.</param>
    /// <returns><c>true</c> when the shape is right.</returns>
    private static bool HasTokenShape(string? text)
    {
        if (text is null
            || text.Length != TokenPrefix.Length + SecretTextLength
            || !text.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return text.AsSpan(TokenPrefix.Length).IndexOfAnyExcept(Base64UrlCharacters) < 0;
    }

    /// <summary>
    ///     The characters a base64url secret can contain.
    /// </summary>
    private static readonly System.Buffers.SearchValues<char> Base64UrlCharacters =
        System.Buffers.SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    /// <summary>
    ///     Validates and trims a token name.
    /// </summary>
    /// <param name="name">The name; may be null.</param>
    /// <returns>The trimmed name.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.TokenNameInvalid"/> when the name is empty, too long or has control characters.</exception>
    private static string ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > ReviewLimits.MaxTokenNameLength || trimmed.Any(char.IsControl))
        {
            throw new DomainRuleException(
                DomainRule.TokenNameInvalid,
                $"The token name must be 1 to {ReviewLimits.MaxTokenNameLength} characters without control characters.");
        }

        return trimmed;
    }

    /// <summary>
    ///     Reads the account of the signed-in user for an operation on the own tokens.
    /// </summary>
    /// <param name="actor">The signed-in user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The enabled user.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ActorNotAllowed"/> when the account is gone or disabled.</exception>
    private async Task<User> LoadOwnerAsync(Actor actor, CancellationToken cancellationToken)
    {
        var user = await _users.GetByUsernameAsync(actor.Username, cancellationToken);
        if (user is null || !user.Enabled)
        {
            throw new DomainRuleException(DomainRule.ActorNotAllowed, "Your account is not available, so nothing was saved.");
        }

        return user;
    }
}
