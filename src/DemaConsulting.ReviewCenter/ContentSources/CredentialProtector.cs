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
using DemaConsulting.ReviewCenter.Core;
using Microsoft.AspNetCore.DataProtection;

namespace DemaConsulting.ReviewCenter.ContentSources;

/// <summary>
///     Encrypts repository tokens with ASP.NET Core Data Protection, using a purpose string that belongs to this
///     feature only.
/// </summary>
/// <remarks>
///     The protector is thread-safe because the underlying data protector is. Data Protection payloads are
///     authenticated, so a changed or foreign payload is reported as unreadable rather than decrypted wrongly.
/// </remarks>
internal sealed class CredentialProtector : ICredentialProtector
{
    /// <summary>
    ///     The purpose string that isolates repository tokens from every other use of the key ring.
    /// </summary>
    public const string Purpose = "DemaConsulting.ReviewCenter.RepositoryCredentials.v1";

    /// <summary>
    ///     The underlying data protector.
    /// </summary>
    private readonly IDataProtector _protector;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CredentialProtector"/> class.
    /// </summary>
    /// <param name="provider">The Data Protection provider. Must not be null.</param>
    public CredentialProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    /// <inheritdoc />
    public string Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        return _protector.Protect(plainText);
    }

    /// <inheritdoc />
    public string Unprotect(string protectedText)
    {
        ArgumentNullException.ThrowIfNull(protectedText);
        try
        {
            return _protector.Unprotect(protectedText);
        }
        catch (CryptographicException)
        {
            // The inner exception is dropped on purpose; it describes the payload and is of no use to the caller
            throw new DomainRuleException(
                DomainRule.CredentialsUnreadable,
                "A stored repository token could not be decrypted, probably because the key ring changed. Enter the token again.");
        }
    }
}
