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
///     One single-use recovery code of a user with a time-based one-time password enrollment.
/// </summary>
/// <remarks>
///     Only a hash of the code is stored (<see cref="CodeHash"/>), so a copy of the database does not reveal the
///     codes. A code is spent by setting <see cref="UsedAt"/> with a conditional update that succeeds for exactly
///     one caller. The type is not thread-safe.
/// </remarks>
public sealed class TotpRecoveryCode
{
    /// <summary>
    ///     Gets the identity assigned by the store.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    ///     Gets the identity of the user the code belongs to.
    /// </summary>
    public int UserId { get; init; }

    /// <summary>
    ///     Gets the lowercase hexadecimal SHA-256 hash of the code, salted with the user; never the code itself.
    /// </summary>
    public string CodeHash { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the moment the code was generated (UTC).
    /// </summary>
    public DateTimeOffset Created { get; init; }

    /// <summary>
    ///     Gets the moment the code was used (UTC), or <c>null</c> while it is unused.
    /// </summary>
    public DateTimeOffset? UsedAt { get; init; }
}
