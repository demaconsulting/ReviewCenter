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
///     Validates and normalizes the e-mail address a person enters for notifications.
/// </summary>
/// <remarks>
///     The rules are deliberately stricter than the full e-mail address grammar, because the address is typed by a
///     user and ends up in the header of a message the server sends: exactly one plain address (no display name, no
///     list of addresses, no comment, no quoted local part), ASCII only, a dotted domain, and no control character
///     of any kind, so that a value can never smuggle a second header line or a second recipient into a message.
///     The address is normalized to lower case, which is how every mail system treats it in practice and which
///     makes the comparison of two addresses (for the sending limits) a plain ordinal comparison. The class is
///     pure and framework-free so that Core does not depend on a mail library; it is stateless and thread-safe.
/// </remarks>
public static class NotificationAddress
{
    /// <summary>
    ///     The longest accepted address in characters (the limit of RFC 5321 for a forward path).
    /// </summary>
    public const int MaxLength = 254;

    /// <summary>
    ///     The longest accepted local part (before the <c>@</c>) in characters.
    /// </summary>
    private const int MaxLocalPartLength = 64;

    /// <summary>
    ///     The longest accepted label of the domain (between two dots) in characters.
    /// </summary>
    private const int MaxLabelLength = 63;

    /// <summary>
    ///     The punctuation characters, besides letters and digits, that the local part may hold.
    /// </summary>
    private const string LocalPartPunctuation = "!#$%&'*+/=?^_`{|}~.-";

    /// <summary>
    ///     Normalizes an address and tells whether it is acceptable.
    /// </summary>
    /// <param name="text">The text the person entered; may be <c>null</c>. Surrounding white space is ignored.</param>
    /// <param name="address">The normalized (trimmed, lower case) address when the result is <c>true</c>; empty otherwise.</param>
    /// <returns><c>true</c> when the text is exactly one acceptable address.</returns>
    public static bool TryNormalize(string? text, out string address)
    {
        address = string.Empty;
        if (text is null)
        {
            return false;
        }

        // Trim first: a pasted address often carries a trailing space or newline, which is not part of the address.
        // Any control character that remains inside is refused below, so a line break can not survive
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxLength)
        {
            return false;
        }

        // Exactly one at sign separates the local part from the domain
        var at = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (at < 1 || at != trimmed.LastIndexOf('@') || at == trimmed.Length - 1)
        {
            return false;
        }

        var localPart = trimmed[..at];
        var domain = trimmed[(at + 1)..];
        if (!IsValidLocalPart(localPart) || !IsValidDomain(domain))
        {
            return false;
        }

        address = trimmed.ToLowerInvariant();
        return true;
    }

    /// <summary>
    ///     Tells whether a text is an acceptable address.
    /// </summary>
    /// <param name="text">The text to test; may be <c>null</c>.</param>
    /// <returns><c>true</c> when <see cref="TryNormalize"/> accepts the text.</returns>
    public static bool IsValid(string? text)
    {
        return TryNormalize(text, out _);
    }

    /// <summary>
    ///     Tells whether a text is an acceptable local part.
    /// </summary>
    /// <param name="localPart">The part before the <c>@</c>.</param>
    /// <returns><c>true</c> when it is dot-separated atoms of ASCII letters, digits and the usual punctuation.</returns>
    private static bool IsValidLocalPart(string localPart)
    {
        // No leading or trailing dot and no empty atom (two dots in a row)
        if (localPart.Length > MaxLocalPartLength
            || localPart[0] == '.'
            || localPart[^1] == '.'
            || localPart.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return localPart.All(c => char.IsAsciiLetterOrDigit(c) || LocalPartPunctuation.Contains(c, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Tells whether a text is an acceptable domain name.
    /// </summary>
    /// <param name="domain">The part after the <c>@</c>.</param>
    /// <returns><c>true</c> when it has at least two labels of ASCII letters, digits and inner hyphens.</returns>
    private static bool IsValidDomain(string domain)
    {
        // A dotted name: an address on a bare host name (user@localhost) is not accepted for notifications
        var labels = domain.Split('.');
        return labels.Length >= 2 && labels.All(IsValidLabel);
    }

    /// <summary>
    ///     Tells whether a text is an acceptable label of a domain name.
    /// </summary>
    /// <param name="label">The text between two dots.</param>
    /// <returns><c>true</c> when it is 1 to 63 ASCII letters, digits and hyphens that neither start nor end with a hyphen.</returns>
    private static bool IsValidLabel(string label)
    {
        return label.Length is > 0 and <= MaxLabelLength
               && label[0] != '-'
               && label[^1] != '-'
               && label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }
}
