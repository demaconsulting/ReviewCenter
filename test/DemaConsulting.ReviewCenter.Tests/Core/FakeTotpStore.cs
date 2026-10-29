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
///     In-memory <see cref="ITotpStore"/> that keeps the contract of the real one: every decision with one winner
///     (the replay step, a recovery code) is made atomically.
/// </summary>
/// <remarks>
///     One lock serializes every access, which is what the conditional updates of the database give. Like the real
///     store it returns copies. A test can read the stored rows and the audit drafts the changes carried. Thread-safe.
/// </remarks>
internal sealed class FakeTotpStore : ITotpStore
{
    /// <summary>
    ///     Serializes every access.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     The enrollments by user.
    /// </summary>
    private readonly Dictionary<int, UserTotp> _rows = [];

    /// <summary>
    ///     The recovery codes (user, hash) and the moment each was used (<c>null</c> while unused).
    /// </summary>
    private readonly List<(int UserId, string Hash, DateTimeOffset? UsedAt)> _codes = [];

    /// <summary>
    ///     Gets the audit drafts received so far, in the order the changes were made.
    /// </summary>
    public List<AuditDraft> Audits { get; } = [];

    /// <summary>
    ///     Gets the number of successful advances of the replay step.
    /// </summary>
    public int AdvanceCount { get; private set; }

    /// <summary>
    ///     Gets or sets an action that runs once, at the next <see cref="GetAsync"/>, before it answers; lets a test
    ///     change the world while a request is in flight.
    /// </summary>
    public Action? OnNextGet { get; set; }

    /// <summary>
    ///     Gets a snapshot of the stored hashes of a user's recovery codes, used or not.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <returns>The hashes.</returns>
    public IReadOnlyList<string> CodeHashes(int userId)
    {
        lock (_lock)
        {
            return _codes.Where(c => c.UserId == userId).Select(c => c.Hash).ToList();
        }
    }

    /// <inheritdoc />
    public Task<UserTotp?> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        var hook = OnNextGet;
        OnNextGet = null;
        hook?.Invoke();
        lock (_lock)
        {
            return Task.FromResult(_rows.TryGetValue(userId, out var row) ? Copy(row) : null);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlySet<int>> ListActiveUserIdsAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            IReadOnlySet<int> ids = _rows.Values.Where(r => r.IsActive).Select(r => r.UserId).ToHashSet();
            return Task.FromResult(ids);
        }
    }

    /// <inheritdoc />
    public Task<bool> SavePendingAsync(UserTotp pending, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_rows.TryGetValue(pending.UserId, out var existing) && existing.IsActive)
            {
                return Task.FromResult(false);
            }

            _rows[pending.UserId] = Copy(pending);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(
        int userId,
        long acceptedStep,
        DateTimeOffset now,
        IReadOnlyList<string> recoveryHashes,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_rows.TryGetValue(userId, out var row) || row.IsActive)
            {
                return Task.FromResult(false);
            }

            _rows[userId] = new UserTotp
            {
                UserId = userId,
                ProtectedSecret = row.ProtectedSecret,
                Created = row.Created,
                ConfirmedAt = now,
                LastAcceptedStep = acceptedStep
            };
            _codes.AddRange(recoveryHashes.Select(hash => (userId, hash, (DateTimeOffset?)null)));
            Audits.Add(audit);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> TryAdvanceStepAsync(int userId, long step, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_rows.TryGetValue(userId, out var row) || !row.IsActive || row.LastAcceptedStep >= step)
            {
                return Task.FromResult(false);
            }

            _rows[userId] = new UserTotp
            {
                UserId = userId,
                ProtectedSecret = row.ProtectedSecret,
                Created = row.Created,
                ConfirmedAt = row.ConfirmedAt,
                LastAcceptedStep = step
            };
            AdvanceCount++;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> TryConsumeRecoveryCodeAsync(
        int userId,
        string codeHash,
        DateTimeOffset now,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _codes.FindIndex(c => c.UserId == userId && c.Hash == codeHash && c.UsedAt is null);
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            _codes[index] = (userId, codeHash, now);
            Audits.Add(audit);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> ReplaceRecoveryCodesAsync(
        int userId,
        IReadOnlyList<string> hashes,
        DateTimeOffset now,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_rows.TryGetValue(userId, out var row) || !row.IsActive)
            {
                return Task.FromResult(false);
            }

            _codes.RemoveAll(c => c.UserId == userId);
            _codes.AddRange(hashes.Select(hash => (userId, hash, (DateTimeOffset?)null)));
            Audits.Add(audit);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<int> CountUnusedRecoveryCodesAsync(int userId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_codes.Count(c => c.UserId == userId && c.UsedAt is null));
        }
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(int userId, AuditDraft audit, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _codes.RemoveAll(c => c.UserId == userId);
            if (!_rows.Remove(userId))
            {
                return Task.FromResult(false);
            }

            Audits.Add(audit);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> RemovePendingAsync(int userId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_rows.TryGetValue(userId, out var row) && !row.IsActive && _rows.Remove(userId));
        }
    }

    /// <summary>
    ///     Copies an enrollment.
    /// </summary>
    /// <param name="row">The enrollment.</param>
    /// <returns>The copy.</returns>
    private static UserTotp Copy(UserTotp row)
    {
        return new UserTotp
        {
            UserId = row.UserId,
            ProtectedSecret = row.ProtectedSecret,
            Created = row.Created,
            ConfirmedAt = row.ConfirmedAt,
            LastAcceptedStep = row.LastAcceptedStep
        };
    }
}

/// <summary>
///     A trivial <see cref="ITotpSecretProtector"/> for tests: not secure, reversible, and bound to the user like the
///     real one.
/// </summary>
internal sealed class FakeTotpSecretProtector : ITotpSecretProtector
{
    /// <summary>
    ///     Gets or sets a value indicating whether every unprotect fails, as with a lost key ring.
    /// </summary>
    public bool Unreadable { get; set; }

    /// <inheritdoc />
    public string Protect(int userId, byte[] secret)
    {
        return $"fake:{userId}:{Convert.ToBase64String(secret)}";
    }

    /// <inheritdoc />
    public byte[] Unprotect(int userId, string protectedSecret)
    {
        var prefix = $"fake:{userId}:";
        if (Unreadable || !protectedSecret.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new DomainRuleException(DomainRule.TotpSecretUnreadable, "The secret can not be read.");
        }

        return Convert.FromBase64String(protectedSecret[prefix.Length..]);
    }
}
