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
using AspNetPasswordHasher = Microsoft.AspNetCore.Identity.PasswordHasher<string>;
using PasswordVerificationResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;

namespace DemaConsulting.ReviewCenter.Identity;

/// <summary>
///     The <see cref="IPasswordHasher"/> implementation backed by the password hasher of the ASP.NET Core framework.
/// </summary>
/// <remarks>
///     The framework hasher uses PBKDF2 with a random salt per hash and an iteration count that is stored
///     inside the hash, so the work factor can rise later without breaking stored hashes. A hash that the
///     framework would like to upgrade (<c>SuccessRehashNeeded</c>) is accepted as a success and is not
///     re-hashed. The adapter is stateless and safe for concurrent use.
/// </remarks>
internal sealed class PasswordHasher : IPasswordHasher
{
    /// <summary>
    ///     The framework hasher; its user argument is unused, so a plain string stands in for the user type.
    /// </summary>
    private readonly AspNetPasswordHasher _hasher = new();

    /// <inheritdoc />
    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return _hasher.HashPassword(string.Empty, password);
    }

    /// <inheritdoc />
    public bool Verify(string hash, string password)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(password);
        return _hasher.VerifyHashedPassword(string.Empty, hash, password) != PasswordVerificationResult.Failed;
    }
}
