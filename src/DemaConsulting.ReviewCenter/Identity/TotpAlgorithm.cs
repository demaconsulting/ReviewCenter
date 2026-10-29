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
using OtpNet;

namespace DemaConsulting.ReviewCenter.Identity;

/// <summary>
///     The time-based one-time password algorithm of RFC 6238 (HMAC-SHA-1, six digits, 30 second steps), implemented
///     with the Otp.NET library.
/// </summary>
/// <remarks>
///     The library does the HMAC and the Base32 work; this class fixes the parameters every authenticator app
///     supports, accepts only well-formed six digit codes, and double-checks that the step the library reports lies
///     within one step of the current one, so replay protection always records a real time step. The tests and
///     the self-validation check compare it against the RFC 6238 Appendix B vectors. The class is stateless and
///     thread-safe.
/// </remarks>
internal sealed class TotpAlgorithm : ITotpAlgorithm
{
    /// <summary>
    ///     The length of the step in seconds.
    /// </summary>
    internal const int StepSeconds = 30;

    /// <summary>
    ///     The number of digits of a code that a person types.
    /// </summary>
    internal const int Digits = 6;

    /// <summary>
    ///     The size of a secret in bytes (160 bits, the size of the SHA-1 output as RFC 4226 recommends).
    /// </summary>
    private const int SecretBytes = 20;

    /// <summary>
    ///     The number of steps accepted before and after the current one.
    /// </summary>
    private const int WindowSteps = 1;

    /// <inheritdoc />
    public byte[] GenerateSecret()
    {
        return KeyGeneration.GenerateRandomKey(SecretBytes);
    }

    /// <inheritdoc />
    public string ToBase32(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return Base32Encoding.ToString(secret).TrimEnd('=');
    }

    /// <inheritdoc />
    public byte[]? TryFromBase32(string? text)
    {
        var cleaned = (text ?? string.Empty)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .TrimEnd('=')
            .ToUpperInvariant();
        if (cleaned.Length == 0 || !cleaned.All(symbol => symbol is (>= 'A' and <= 'Z') or (>= '2' and <= '7')))
        {
            return null;
        }

        return Base32Encoding.ToBytes(cleaned);
    }

    /// <inheritdoc />
    public string BuildUri(byte[] secret, string issuer, string accountName)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(accountName);
        return new OtpUri(OtpType.Totp, secret, accountName, issuer, OtpHashMode.Sha1, Digits, StepSeconds).ToString();
    }

    /// <inheritdoc />
    public string ComputeCode(byte[] secret, DateTimeOffset time, int digits = Digits)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentOutOfRangeException.ThrowIfLessThan(digits, 6);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, 8);
        return new Totp(secret, StepSeconds, OtpHashMode.Sha1, digits).ComputeTotp(time.UtcDateTime);
    }

    /// <inheritdoc />
    public bool TryVerify(byte[] secret, string? code, DateTimeOffset now, out long matchedStep)
    {
        ArgumentNullException.ThrowIfNull(secret);
        matchedStep = 0;
        if (code is null || code.Length != Digits || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        var totp = new Totp(secret, StepSeconds, OtpHashMode.Sha1, Digits);
        if (!totp.VerifyTotp(now.UtcDateTime, code, out var step, new VerificationWindow(WindowSteps, WindowSteps)))
        {
            return false;
        }

        // Replay protection trusts the step: refuse anything that is not within the window around now
        var current = StepOf(now);
        if (step < current - WindowSteps || step > current + WindowSteps)
        {
            return false;
        }

        matchedStep = step;
        return true;
    }

    /// <inheritdoc />
    public long StepOf(DateTimeOffset time)
    {
        return (long)Math.Floor(time.ToUnixTimeSeconds() / (double)StepSeconds);
    }
}
