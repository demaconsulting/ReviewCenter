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
using System.Text;

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     Prepares text for the audit trail: keeps it on one line, within its length limit, and valid Unicode;
///     and builds the short digest that stands in for text that must not be stored.
/// </summary>
/// <remarks>
///     The audit trail never stores comment text. Where an entry must still identify a comment, it carries
///     <see cref="ShortHash"/> of the text instead. The class is stateless and thread-safe.
/// </remarks>
public static class AuditText
{
    /// <summary>
    ///     The longest stored action code, in characters.
    /// </summary>
    public const int MaxActionLength = 100;

    /// <summary>
    ///     The longest stored actor text, in characters.
    /// </summary>
    public const int MaxActorNameLength = 200;

    /// <summary>
    ///     The longest stored detail text, in characters.
    /// </summary>
    public const int MaxDetailLength = 500;

    /// <summary>
    ///     The number of hexadecimal characters of <see cref="ShortHash"/>.
    /// </summary>
    private const int ShortHashLength = 8;

    /// <summary>
    ///     The text that marks a shortened value.
    /// </summary>
    private const string Ellipsis = "...";

    /// <summary>
    ///     Makes text safe and bounded for the audit trail.
    /// </summary>
    /// <param name="text">The text; <c>null</c> is treated as empty.</param>
    /// <param name="maxLength">The longest allowed result, in characters; at least 4.</param>
    /// <returns>
    ///     The text on one line: control characters and line or paragraph separators become spaces, an unpaired
    ///     surrogate becomes U+FFFD, leading and trailing white space is removed, and a longer text is cut and
    ///     ends in <c>...</c> so the result never exceeds <paramref name="maxLength"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxLength"/> is below 4.</exception>
    public static string Clean(string? text, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, Ellipsis.Length + 1);

        var builder = new StringBuilder(text?.Length ?? 0);
        var value = text ?? string.Empty;
        var index = 0;
        while (index < value.Length)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
            {
                // A valid pair stays together
                builder.Append(character).Append(value[index + 1]);
                index += 2;
                continue;
            }

            if (char.IsSurrogate(character))
            {
                builder.Append((char)0xFFFD);
            }
            else if (char.IsControl(character)
                     || CharUnicodeInfo.GetUnicodeCategory(character)
                         is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(character);
            }

            index++;
        }

        var cleaned = builder.ToString().Trim();
        if (cleaned.Length <= maxLength)
        {
            return cleaned;
        }

        // Cut so that the ellipsis fits, without splitting a surrogate pair
        var keep = maxLength - Ellipsis.Length;
        if (char.IsHighSurrogate(cleaned[keep - 1]))
        {
            keep--;
        }

        return cleaned[..keep].TrimEnd() + Ellipsis;
    }

    /// <summary>
    ///     Builds the short digest that identifies a text without storing it.
    /// </summary>
    /// <param name="text">The text; leading and trailing white space is ignored; <c>null</c> is treated as empty.</param>
    /// <returns>The first 8 lowercase hexadecimal characters of the SHA-256 of the UTF-8 bytes of the trimmed text.</returns>
    public static string ShortHash(string? text)
    {
        var bytes = Encoding.UTF8.GetBytes((text ?? string.Empty).Trim());
        return Convert.ToHexStringLower(SHA256.HashData(bytes))[..ShortHashLength];
    }
}
