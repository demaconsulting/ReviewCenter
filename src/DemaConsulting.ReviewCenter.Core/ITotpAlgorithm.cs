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
///     The time-based one-time password algorithm (RFC 6238, HMAC-SHA-1, 30 second steps), so Core can use it
///     without a reference to the library that implements it.
/// </summary>
/// <remarks>
///     SHA-1, six digits and 30 seconds are what every authenticator app supports, which is why they are fixed. The
///     implementation lives in the web project (see <c>TotpAlgorithm</c>); tests and the self-validation check it
///     against the published RFC 6238 vectors. Implementations must be thread-safe.
/// </remarks>
public interface ITotpAlgorithm
{
    /// <summary>
    ///     Creates a new random secret.
    /// </summary>
    /// <returns>20 random bytes (160 bits); the caller owns the array and should clear it after use.</returns>
    byte[] GenerateSecret();

    /// <summary>
    ///     Writes a secret as Base32 text, the form authenticator apps accept.
    /// </summary>
    /// <param name="secret">The secret. Must not be null.</param>
    /// <returns>Upper-case Base32 text without padding.</returns>
    string ToBase32(byte[] secret);

    /// <summary>
    ///     Reads a secret written as Base32 text.
    /// </summary>
    /// <param name="text">The text; spaces and hyphens are ignored and the case does not matter. May be null.</param>
    /// <returns>The secret, or <c>null</c> when the text is not valid Base32.</returns>
    byte[]? TryFromBase32(string? text);

    /// <summary>
    ///     Builds the <c>otpauth://</c> address that authenticator apps read from a QR code or accept as text.
    /// </summary>
    /// <param name="secret">The secret. Must not be null.</param>
    /// <param name="issuer">The name of the service shown in the app. Must not be null.</param>
    /// <param name="accountName">The name of the account shown in the app. Must not be null.</param>
    /// <returns>The address; it contains the secret and must be treated like the secret.</returns>
    string BuildUri(byte[] secret, string issuer, string accountName);

    /// <summary>
    ///     Computes the code of a moment.
    /// </summary>
    /// <param name="secret">The secret. Must not be null.</param>
    /// <param name="time">The moment.</param>
    /// <param name="digits">The number of digits, 6 to 8.</param>
    /// <returns>The code with leading zeros.</returns>
    string ComputeCode(byte[] secret, DateTimeOffset time, int digits = 6);

    /// <summary>
    ///     Checks a six digit code against the secret for the moment, allowing one step before and one step after.
    /// </summary>
    /// <param name="secret">The secret. Must not be null.</param>
    /// <param name="code">The typed code; anything but exactly six digits is refused. May be null.</param>
    /// <param name="now">The current moment.</param>
    /// <param name="matchedStep">The time step the code belongs to; 0 when the code is refused.</param>
    /// <returns><c>true</c> when the code is right for one of the three steps.</returns>
    /// <remarks>The matched step is what replay protection records; it is the step of the code, not of <paramref name="now"/>.</remarks>
    bool TryVerify(byte[] secret, string? code, DateTimeOffset now, out long matchedStep);

    /// <summary>
    ///     Gets the time step of a moment.
    /// </summary>
    /// <param name="time">The moment.</param>
    /// <returns>The number of whole 30 second steps since 1970-01-01 UTC.</returns>
    long StepOf(DateTimeOffset time);
}
