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
///     Encrypts and decrypts the tokens of the repository registry, so they are never stored as plain text.
/// </summary>
/// <remarks>
///     The contract lets Core use encryption without a reference to any cryptography framework; the content
///     sources subsystem implements it with ASP.NET Core Data Protection. Implementations must be thread-safe.
/// </remarks>
public interface ICredentialProtector
{
    /// <summary>
    ///     Encrypts a token.
    /// </summary>
    /// <param name="plainText">The token. Must not be null.</param>
    /// <returns>The encrypted text, safe to store.</returns>
    string Protect(string plainText);

    /// <summary>
    ///     Decrypts a stored token.
    /// </summary>
    /// <param name="protectedText">The encrypted text from <see cref="Protect"/>. Must not be null.</param>
    /// <returns>The token.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.CredentialsUnreadable"/> when the text cannot be decrypted, for example
    ///     because the key ring was lost; the message never contains the text.
    /// </exception>
    string Unprotect(string protectedText);
}
