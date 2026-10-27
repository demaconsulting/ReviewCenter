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

using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     In-memory <see cref="IPersonalAccessTokenStore"/> used to test <see cref="PersonalAccessTokenService"/>
///     without a database.
/// </summary>
/// <remarks>
///     Like the real store it returns new objects on every read, so changing a returned token changes nothing until
///     the store is asked to. Owners are read from the <see cref="FakeUserStore"/> the service also uses. Not
///     thread-safe; each test owns its instance.
/// </remarks>
/// <param name="users">The user store that holds the owners of the tokens.</param>
internal sealed class FakePersonalAccessTokenStore(FakeUserStore users) : IPersonalAccessTokenStore
{
    /// <summary>
    ///     The stored tokens.
    /// </summary>
    private readonly List<PersonalAccessToken> _tokens = [];

    /// <summary>
    ///     Gets the audit drafts received so far, in the order the changes were made.
    /// </summary>
    public List<AuditDraft> Audits { get; } = [];

    /// <summary>
    ///     Gets the number of times a use was recorded.
    /// </summary>
    public int TouchCount { get; private set; }

    /// <summary>
    ///     Gets or sets a replacement for the lookup by hash, so a test can make the store return the wrong row.
    /// </summary>
    public Func<string, PersonalAccessToken?>? FindOverride { get; set; }

    /// <summary>
    ///     Gets a copy of every stored token, as the store holds them.
    /// </summary>
    public IReadOnlyList<PersonalAccessToken> Rows => _tokens.Select(t => Copy(t, t.Id)).ToList();

    /// <inheritdoc />
    public Task<PersonalAccessToken> AddAsync(
        PersonalAccessToken token,
        Func<PersonalAccessToken, AuditDraft> audit,
        CancellationToken cancellationToken = default)
    {
        _tokens.Add(Copy(token, _tokens.Count + 1));
        var stored = Copy(_tokens[^1], _tokens[^1].Id);
        Audits.Add(audit(stored));
        return Task.FromResult(stored);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PersonalAccessToken>> ListForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PersonalAccessToken> list = _tokens
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.Created)
            .ThenByDescending(t => t.Id)
            .Select(t => Copy(t, t.Id))
            .ToList();
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public async Task<PersonalAccessToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        if (FindOverride is not null)
        {
            return FindOverride(tokenHash);
        }

        var token = _tokens.FirstOrDefault(t => t.TokenHash == tokenHash);
        if (token is null)
        {
            return null;
        }

        var copy = Copy(token, token.Id);
        copy.User = (await users.ListAsync(cancellationToken)).FirstOrDefault(u => u.Id == token.UserId);
        return copy;
    }

    /// <inheritdoc />
    public Task<PersonalAccessToken?> RevokeAsync(
        int tokenId,
        int userId,
        DateTimeOffset when,
        Func<PersonalAccessToken, AuditDraft> audit,
        CancellationToken cancellationToken = default)
    {
        var token = _tokens.FirstOrDefault(t => t.Id == tokenId && t.UserId == userId && t.Revoked is null);
        if (token is null)
        {
            return Task.FromResult<PersonalAccessToken?>(null);
        }

        token.Revoked = when;
        var copy = Copy(token, token.Id);
        Audits.Add(audit(copy));
        return Task.FromResult<PersonalAccessToken?>(copy);
    }

    /// <inheritdoc />
    public Task TouchAsync(int tokenId, DateTimeOffset when, CancellationToken cancellationToken = default)
    {
        TouchCount++;
        _tokens.First(t => t.Id == tokenId).LastUsed = when;
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Copies a token and gives the copy an identity.
    /// </summary>
    /// <param name="token">The token to copy.</param>
    /// <param name="id">The identity of the copy.</param>
    /// <returns>The copy, without an owner object.</returns>
    private static PersonalAccessToken Copy(PersonalAccessToken token, int id)
    {
        return new PersonalAccessToken
        {
            Id = id,
            UserId = token.UserId,
            Name = token.Name,
            DisplayId = token.DisplayId,
            TokenHash = token.TokenHash,
            Created = token.Created,
            Expires = token.Expires,
            LastUsed = token.LastUsed,
            Revoked = token.Revoked
        };
    }
}
