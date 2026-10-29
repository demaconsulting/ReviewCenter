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

using System.Text;
using DemaConsulting.ReviewCenter.Identity;

namespace DemaConsulting.ReviewCenter.SelfTest.Checks;

/// <summary>
///     Checks the one-time password algorithm of the second factor against the published RFC 6238 test vectors.
/// </summary>
/// <remarks>
///     The check calls the production <see cref="TotpAlgorithm"/> with the secret and the times of RFC 6238
///     Appendix B (HMAC-SHA-1, eight digits), so an installation whose library or runtime computes other codes than
///     every authenticator app is reported instead of locking people out. It also checks the window of one step,
///     that two steps are refused, and the Base32 and <c>otpauth://</c> forms. It needs no files and takes
///     milliseconds. The class is stateless and thread-safe.
/// </remarks>
internal static class TotpCheck
{
    /// <summary>
    ///     The name of the check in the report and the results file.
    /// </summary>
    public const string Name = "ReviewCenter_Totp";

    /// <summary>
    ///     The shared secret of the RFC 6238 test vectors: the ASCII text <c>12345678901234567890</c>.
    /// </summary>
    private const string VectorSecret = "12345678901234567890";

    /// <summary>
    ///     The published SHA-1 test vectors of RFC 6238 Appendix B: Unix time and the eight digit code.
    /// </summary>
    private static readonly (long Time, string Code)[] Vectors =
    [
        (59, "94287082"),
        (1111111109, "07081804"),
        (1111111111, "14050471"),
        (1234567890, "89005924"),
        (2000000000, "69279037"),
        (20000000000, "65353130")
    ];

    /// <summary>
    ///     Runs the check.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A short description of what was verified.</returns>
    /// <exception cref="ValidationFailedException">Thrown when a code differs from the published vector, or the window or the forms are wrong.</exception>
    public static Task<string> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var algorithm = new TotpAlgorithm();
        var secret = Encoding.ASCII.GetBytes(VectorSecret);

        // The published vectors, with eight digits
        foreach (var (time, code) in Vectors)
        {
            var computed = algorithm.ComputeCode(secret, DateTimeOffset.FromUnixTimeSeconds(time), 8);
            ValidationFailedException.ThrowUnless(
                computed == code,
                $"The code for Unix time {time} differs from the RFC 6238 test vector.");
        }

        // Six digits are the last six of the eight digit vector; the step that is returned is the time step of the
        // moment, and the window of one step before and after is accepted but two steps are not
        var now = DateTimeOffset.FromUnixTimeSeconds(1111111109);
        var six = algorithm.ComputeCode(secret, now);
        ValidationFailedException.ThrowUnless(six == "081804", "The six digit code differs from the RFC 6238 test vector.");
        ValidationFailedException.ThrowUnless(
            algorithm.TryVerify(secret, six, now, out var step) && step == 1111111109 / 30,
            "The current code was refused or its time step is wrong.");
        foreach (var offset in new[] { -30, 30 })
        {
            ValidationFailedException.ThrowUnless(
                algorithm.TryVerify(secret, six, now.AddSeconds(offset), out var neighbor) && neighbor == step,
                "A code of the neighboring time step was refused.");
        }

        foreach (var offset in new[] { -60, 60 })
        {
            ValidationFailedException.ThrowUnless(
                !algorithm.TryVerify(secret, six, now.AddSeconds(offset), out _),
                "A code two time steps away was accepted.");
        }

        ValidationFailedException.ThrowUnless(
            !algorithm.TryVerify(secret, "000000", now, out _) && !algorithm.TryVerify(secret, "08180", now, out _),
            "A wrong or malformed code was accepted.");

        // The forms an authenticator app reads
        var base32 = algorithm.ToBase32(secret);
        ValidationFailedException.ThrowUnless(
            base32 == "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"
            && algorithm.TryFromBase32(base32)?.SequenceEqual(secret) == true,
            "The Base32 form of the secret is wrong.");
        ValidationFailedException.ThrowUnless(
            algorithm.BuildUri(secret, "ReviewCenter", "ada").StartsWith("otpauth://totp/", StringComparison.Ordinal),
            "The otpauth address is wrong.");

        return Task.FromResult("RFC 6238 SHA-1 vectors, window +/-1, replay step returned");
    }
}
