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

using System.Globalization;
using System.Security.Cryptography;
using DemaConsulting.ReviewCenter.Core;
using Microsoft.AspNetCore.DataProtection;

namespace DemaConsulting.ReviewCenter.Identity;

/// <summary>
///     Encrypts the secrets of the second factor enrollments with ASP.NET Core Data Protection, using a purpose
///     string that belongs to this feature only and a sub-purpose per user.
/// </summary>
/// <remarks>
///     The sub-purpose binds a ciphertext to its user: copying the stored value into the row of another user makes it
///     unreadable. Data Protection payloads are authenticated, so a changed or foreign payload is reported as
///     unreadable rather than decrypted wrongly. The class is thread-safe because the underlying protector is.
/// </remarks>
internal sealed class TotpSecretProtector : ITotpSecretProtector
{
    /// <summary>
    ///     The purpose string that isolates second factor secrets from every other use of the key ring.
    /// </summary>
    public const string Purpose = "DemaConsulting.ReviewCenter.TotpSecrets.v1";

    /// <summary>
    ///     The protector of this feature; a protector per user is derived from it.
    /// </summary>
    private readonly IDataProtector _protector;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TotpSecretProtector"/> class.
    /// </summary>
    /// <param name="provider">The Data Protection provider. Must not be null.</param>
    public TotpSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    /// <inheritdoc />
    public string Protect(int userId, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return Convert.ToBase64String(ForUser(userId).Protect(secret));
    }

    /// <inheritdoc />
    public byte[] Unprotect(int userId, string protectedSecret)
    {
        ArgumentNullException.ThrowIfNull(protectedSecret);
        try
        {
            return ForUser(userId).Unprotect(Convert.FromBase64String(protectedSecret));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            // The inner exception is dropped on purpose; it describes the payload and is of no use to the caller
            throw new DomainRuleException(
                DomainRule.TotpSecretUnreadable,
                "The stored second factor secret could not be decrypted, probably because the key ring changed. An administrator must reset the second factor.");
        }
    }

    /// <summary>
    ///     Gets the protector of one user.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <returns>The protector whose output only the same user's protector can read.</returns>
    private IDataProtector ForUser(int userId)
    {
        return _protector.CreateProtector(userId.ToString(CultureInfo.InvariantCulture));
    }
}
