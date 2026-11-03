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
///     In-memory <see cref="INotificationStore"/> that keeps the contract of the real one: the limits and the
///     pending state are decided together, and every decision with one winner is made atomically.
/// </summary>
/// <remarks>
///     One lock serializes every access, which is what the serialized transaction and the conditional updates of the
///     database give. The limits are decided by <see cref="ConfirmationRequest.Decide"/>, the same code the real
///     store uses. Like the real store it returns copies. A test can read the stored rows, the counted mails and
///     the audit drafts the changes carried. Thread-safe.
/// </remarks>
internal sealed class FakeNotificationStore : INotificationStore
{
    /// <summary>
    ///     Serializes every access.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     The preference rows by user.
    /// </summary>
    private readonly Dictionary<int, NotificationPreference> _rows = [];

    /// <summary>
    ///     The counted mails.
    /// </summary>
    private readonly List<NotificationSendLogEntry> _log = [];

    /// <summary>
    ///     The next identity of a counted mail.
    /// </summary>
    private int _nextLogId = 1;

    /// <summary>
    ///     Gets the audit drafts received so far, in the order the changes were made.
    /// </summary>
    public List<AuditDraft> Audits { get; } = [];

    /// <summary>
    ///     Gets the number of calls the store received, for tests that show that a feature touches no table.
    /// </summary>
    public int Calls { get; private set; }

    /// <summary>
    ///     Gets a snapshot of the counted mails.
    /// </summary>
    public IReadOnlyList<NotificationSendLogEntry> Log
    {
        get
        {
            lock (_lock)
            {
                return _log.ToList();
            }
        }
    }

    /// <summary>
    ///     Counts a mail as if it had been sent, for tests of the limits.
    /// </summary>
    /// <param name="userId">The user the mail was for.</param>
    /// <param name="address">The address the mail went to.</param>
    /// <param name="sentUtc">When the mail was sent.</param>
    public void AddLog(int userId, string address, DateTimeOffset sentUtc)
    {
        lock (_lock)
        {
            _log.Add(new NotificationSendLogEntry
            {
                Id = _nextLogId++,
                UserId = userId,
                ToAddress = address,
                Kind = NotificationSendLogEntry.KindConfirmation,
                SentUtc = sentUtc
            });
        }
    }

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<NotificationPreference?> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Calls++;
            return Task.FromResult(_rows.TryGetValue(userId, out var row) ? Copy(row) : null);
        }
    }

    /// <inheritdoc />
    public Task<(ConfirmationReservation Outcome, int LogId)> ReserveConfirmationAsync(
        ConfirmationRequest request,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _rows.TryGetValue(request.UserId, out var existing);
            var userCount = _log.Count(e => e.UserId == request.UserId && e.SentUtc >= request.Now.AddHours(-24));
            var addressCount = _log.Count(e => e.ToAddress == request.Address && e.SentUtc >= request.Now.AddHours(-24));
            var serverCount = _log.Count(e => e.SentUtc >= request.Now.AddHours(-1));
            var outcome = request.Decide(existing?.PendingSentUtc, userCount, addressCount, serverCount);
            if (outcome != ConfirmationReservation.Reserved)
            {
                return Task.FromResult((outcome, 0));
            }

            var logId = _nextLogId++;
            _log.Add(new NotificationSendLogEntry
            {
                Id = logId,
                UserId = request.UserId,
                ToAddress = request.Address,
                Kind = NotificationSendLogEntry.KindConfirmation,
                SentUtc = request.Now
            });
            _rows[request.UserId] = new NotificationPreference
            {
                UserId = request.UserId,
                Email = existing?.Email,
                PendingEmail = request.Address,
                PendingCodeHash = request.CodeHash,
                PendingCodeSalt = request.CodeSalt,
                PendingCodeExpiresUtc = request.ExpiresUtc,
                PendingAttempts = 0,
                PendingSentUtc = request.Now,
                Updated = request.Now
            };
            Audits.Add(audit);
            return Task.FromResult((outcome, logId));
        }
    }

    /// <inheritdoc />
    public Task AbortConfirmationAsync(
        int userId,
        int logId,
        string codeHash,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _log.RemoveAll(e => e.Id == logId && e.UserId == userId);
            if (_rows.TryGetValue(userId, out var row) && row.PendingCodeHash == codeHash)
            {
                _rows[userId] = new NotificationPreference { UserId = userId, Email = row.Email, Updated = row.Updated };
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> TryCountAttemptAsync(
        int userId,
        string codeHash,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_rows.TryGetValue(userId, out var row) || row.PendingCodeHash != codeHash || row.PendingAttempts >= maxAttempts)
            {
                return Task.FromResult(false);
            }

            var counted = Copy(row);
            _rows[userId] = new NotificationPreference
            {
                UserId = counted.UserId,
                Email = counted.Email,
                PendingEmail = counted.PendingEmail,
                PendingCodeHash = counted.PendingCodeHash,
                PendingCodeSalt = counted.PendingCodeSalt,
                PendingCodeExpiresUtc = counted.PendingCodeExpiresUtc,
                PendingAttempts = counted.PendingAttempts + 1,
                PendingSentUtc = counted.PendingSentUtc,
                Updated = counted.Updated
            };
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(
        int userId,
        string codeHash,
        DateTimeOffset now,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_rows.TryGetValue(userId, out var row) || row.PendingCodeHash != codeHash || row.PendingEmail is null)
            {
                return Task.FromResult(false);
            }

            _rows[userId] = new NotificationPreference { UserId = userId, Email = row.PendingEmail, Updated = now };
            Audits.Add(audit);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(int userId, AuditDraft audit, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_rows.Remove(userId))
            {
                return Task.FromResult(false);
            }

            Audits.Add(audit);
            return Task.FromResult(true);
        }
    }

    /// <summary>
    ///     Copies a row, as the real store returns copies.
    /// </summary>
    /// <param name="row">The stored row.</param>
    /// <returns>The copy.</returns>
    private static NotificationPreference Copy(NotificationPreference row)
    {
        return new NotificationPreference
        {
            UserId = row.UserId,
            Email = row.Email,
            PendingEmail = row.PendingEmail,
            PendingCodeHash = row.PendingCodeHash,
            PendingCodeSalt = row.PendingCodeSalt,
            PendingCodeExpiresUtc = row.PendingCodeExpiresUtc,
            PendingAttempts = row.PendingAttempts,
            PendingSentUtc = row.PendingSentUtc,
            Updated = row.Updated
        };
    }
}
