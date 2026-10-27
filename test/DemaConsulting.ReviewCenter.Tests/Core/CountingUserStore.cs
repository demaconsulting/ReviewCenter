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
///     An <see cref="IUserStore"/> that forwards to another store and counts how often the whole user list is read,
///     so a test can prove that a caller reads it once and not once per item.
/// </summary>
/// <param name="inner">The store that does the work.</param>
internal sealed class CountingUserStore(IUserStore inner) : IUserStore
{
    /// <summary>
    ///     Gets how many times <see cref="ListAsync"/> was called.
    /// </summary>
    public int ListCount { get; private set; }

    /// <inheritdoc/>
    public Task<User> AddAsync(User user, Func<User, AuditDraft> audit, CancellationToken cancellationToken = default)
    {
        return inner.AddAsync(user, audit, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        return inner.GetByUsernameAsync(username, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
    {
        ListCount++;
        return inner.ListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task UpdateAsync(User user, AuditDraft? audit, CancellationToken cancellationToken = default)
    {
        return inner.UpdateAsync(user, audit, cancellationToken);
    }
}
