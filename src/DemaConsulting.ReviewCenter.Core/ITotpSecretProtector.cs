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
///     Encrypts and decrypts the secrets of the time-based one-time password enrollments, so they are never stored
///     as plain text.
/// </summary>
/// <remarks>
///     The contract lets Core use encryption without a reference to any cryptography framework; the identity
///     subsystem implements it with ASP.NET Core Data Protection. The ciphertext is bound to the user, so a copied
///     value cannot be moved to the row of another user. Implementations must be thread-safe.
/// </remarks>
public interface ITotpSecretProtector
{
    /// <summary>
    ///     Encrypts the secret of a user.
    /// </summary>
    /// <param name="userId">The identity of the user the secret belongs to.</param>
    /// <param name="secret">The secret. Must not be null.</param>
    /// <returns>The encrypted text, safe to store.</returns>
    string Protect(int userId, byte[] secret);

    /// <summary>
    ///     Decrypts the stored secret of a user.
    /// </summary>
    /// <param name="userId">The identity of the user the secret belongs to.</param>
    /// <param name="protectedSecret">The encrypted text from <see cref="Protect"/>. Must not be null.</param>
    /// <returns>The secret; the caller should clear the array after use.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.TotpSecretUnreadable"/> when the text cannot be decrypted for this user,
    ///     for example because the key ring was lost; the message never contains the text.
    /// </exception>
    byte[] Unprotect(int userId, string protectedSecret);
}
