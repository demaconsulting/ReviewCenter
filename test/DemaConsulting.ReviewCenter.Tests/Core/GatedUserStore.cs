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
///     An <see cref="IUserStore"/> that forwards to another store, but holds back the lookup of the names that start
///     with a given prefix until a test opens the gate (or makes the lookup fail), so a test can keep many attempts
///     inside the lookup at the same time.
/// </summary>
/// <param name="inner">The store that does the work.</param>
/// <param name="gatedPrefix">The names that start with this text are held back; every other name passes.</param>
internal sealed class GatedUserStore(IUserStore inner, string gatedPrefix) : IUserStore
{
    /// <summary>
    ///     Completed to let the held lookups go on.
    /// </summary>
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     Guards <see cref="_entered"/> and <see cref="_waiters"/>.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     The callers of <see cref="WhenEnteredAsync"/> that wait for a number of lookups.
    /// </summary>
    private readonly List<(int Count, TaskCompletionSource Source)> _waiters = [];

    /// <summary>
    ///     How many lookups of a gated name have started.
    /// </summary>
    private int _entered;

    /// <summary>
    ///     Gets how many lookups of a gated name have started.
    /// </summary>
    public int Entered
    {
        get
        {
            lock (_lock)
            {
                return _entered;
            }
        }
    }

    /// <summary>
    ///     Gets or sets the exception thrown by a gated lookup once the gate is open; <c>null</c> for none.
    /// </summary>
    public Exception? LookupFailure { get; set; }

    /// <summary>
    ///     Lets the held lookups (and every later one) go on.
    /// </summary>
    public void Open() => _gate.TrySetResult();

    /// <summary>
    ///     Returns a task that completes as soon as a number of lookups of gated names have started, so a test waits
    ///     for the attempts to be inside the lookup without polling or sleeping.
    /// </summary>
    /// <param name="count">The number of started lookups to wait for.</param>
    /// <returns>A task that completes when at least that many lookups have started.</returns>
    public Task WhenEnteredAsync(int count)
    {
        lock (_lock)
        {
            if (_entered >= count)
            {
                return Task.CompletedTask;
            }

            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((count, source));
            return source.Task;
        }
    }

    /// <inheritdoc/>
    public Task<User> AddAsync(User user, Func<User, AuditDraft> audit, CancellationToken cancellationToken = default)
    {
        return inner.AddAsync(user, audit, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<User> AddToProjectAsync(
        User user,
        int projectId,
        ProjectRole role,
        Func<User, AuditDraft> audit,
        CancellationToken cancellationToken = default)
    {
        return inner.AddToProjectAsync(user, projectId, role, audit, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        if (username.StartsWith(gatedPrefix, StringComparison.Ordinal))
        {
            lock (_lock)
            {
                _entered++;
                foreach (var waiter in _waiters.Where(w => w.Count <= _entered).ToList())
                {
                    waiter.Source.TrySetResult();
                    _waiters.Remove(waiter);
                }
            }

            await _gate.Task.WaitAsync(cancellationToken);
            if (LookupFailure is { } failure)
            {
                throw failure;
            }
        }

        return await inner.GetByUsernameAsync(username, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
    {
        return inner.ListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task UpdateAsync(User user, AuditDraft? audit, CancellationToken cancellationToken = default)
    {
        return inner.UpdateAsync(user, audit, cancellationToken);
    }
}
