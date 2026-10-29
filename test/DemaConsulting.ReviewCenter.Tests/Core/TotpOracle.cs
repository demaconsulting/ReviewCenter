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
using System.Text;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     An independent reference of the one-time password algorithms, written from RFC 4226 and RFC 6238 with nothing
///     but the HMAC of the runtime, so tests can check the production code (which uses a library) against it.
/// </summary>
/// <remarks>
///     It is deliberately naive: one HMAC-SHA-1 over the eight byte counter, dynamic truncation as in RFC 4226
///     section 5.3, and a time step of 30 seconds counted from the Unix epoch. Its Base32 reader and writer are
///     independent of the library as well. The class is stateless and thread-safe.
/// </remarks>
internal static class TotpOracle
{
    /// <summary>
    ///     The shared secret of the published test vectors: the ASCII text <c>12345678901234567890</c>.
    /// </summary>
    public static byte[] VectorSecret { get; } = Encoding.ASCII.GetBytes("12345678901234567890");

    /// <summary>
    ///     The published SHA-1 vectors of RFC 6238 Appendix B (eight digits): Unix time and code.
    /// </summary>
    public static IReadOnlyList<(long Time, string Code)> Rfc6238Vectors { get; } =
    [
        (59, "94287082"),
        (1111111109, "07081804"),
        (1111111111, "14050471"),
        (1234567890, "89005924"),
        (2000000000, "69279037"),
        (20000000000, "65353130")
    ];

    /// <summary>
    ///     The published HOTP values of RFC 4226 Appendix D (six digits) for the counters 0 to 9.
    /// </summary>
    public static IReadOnlyList<string> Rfc4226Values { get; } =
    [
        "755224", "287082", "359152", "969429", "338314", "254676", "287922", "162583", "399871", "520489"
    ];

    /// <summary>
    ///     Computes an HOTP value (RFC 4226).
    /// </summary>
    /// <param name="key">The shared secret.</param>
    /// <param name="counter">The counter.</param>
    /// <param name="digits">The number of digits.</param>
    /// <returns>The value with leading zeros.</returns>
    public static string Hotp(byte[] key, long counter, int digits)
    {
        var message = new byte[8];
        for (var i = 7; i >= 0; i--)
        {
            message[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        var hash = HMACSHA1.HashData(key, message);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | (hash[offset + 1] << 16)
                     | (hash[offset + 2] << 8)
                     | hash[offset + 3];
        var modulus = 1;
        for (var i = 0; i < digits; i++)
        {
            modulus *= 10;
        }

        return (binary % modulus).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }

    /// <summary>
    ///     Computes the time based value (RFC 6238, HMAC-SHA-1, 30 second step).
    /// </summary>
    /// <param name="key">The shared secret.</param>
    /// <param name="unixSeconds">The Unix time.</param>
    /// <param name="digits">The number of digits.</param>
    /// <returns>The code.</returns>
    public static string Totp(byte[] key, long unixSeconds, int digits = 6)
    {
        return Hotp(key, unixSeconds / 30, digits);
    }

    /// <summary>
    ///     Computes the code of a time step relative to the step of an instant.
    /// </summary>
    /// <param name="key">The shared secret.</param>
    /// <param name="at">The instant.</param>
    /// <param name="stepOffset">The number of steps before (negative) or after (positive) the step of the instant.</param>
    /// <returns>The six digit code.</returns>
    public static string CodeAt(byte[] key, DateTimeOffset at, int stepOffset = 0)
    {
        return Hotp(key, (at.ToUnixTimeSeconds() / 30) + stepOffset, 6);
    }

    /// <summary>
    ///     Writes bytes as RFC 4648 Base32 without padding.
    /// </summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The upper-case text.</returns>
    public static string ToBase32(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var text = new StringBuilder();
        var buffer = 0;
        var bits = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                text.Append(alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            text.Append(alphabet[(buffer << (5 - bits)) & 31]);
        }

        return text.ToString();
    }

    /// <summary>
    ///     Reads RFC 4648 Base32 text.
    /// </summary>
    /// <param name="text">The upper-case text without padding.</param>
    /// <returns>The bytes.</returns>
    public static byte[] FromBase32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var symbol in text)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(symbol, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return bytes.ToArray();
    }
}
