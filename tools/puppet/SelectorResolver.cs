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

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     Turns the selector text a user types into the selector Playwright evaluates.
/// </summary>
/// <remarks>
///     The ReviewCenter UI marks every element a driver needs with a stable <c>data-testid</c>
///     attribute, so the common case should be as short as possible: <c>testid:hello-button</c> or just
///     <c>hello-button</c> means "the element whose test id is hello-button". Anything else
///     (<c>css=...</c>, <c>text=...</c>, <c>role=...</c>, <c>#id</c>) is passed through unchanged for the
///     occasional case where a test id does not exist. The class is stateless and thread-safe.
/// </remarks>
internal static class SelectorResolver
{
    /// <summary>
    ///     The explicit test-id prefix.
    /// </summary>
    private const string TestIdPrefix = "testid:";

    /// <summary>
    ///     Resolves the selector text to a Playwright selector.
    /// </summary>
    /// <param name="selector">The text the user supplied. Must not be null or blank.</param>
    /// <returns>
    ///     A <c>[data-testid="name"]</c> attribute selector when <paramref name="selector"/> has the
    ///     <c>testid:</c> prefix or is a plain name (letters, digits, hyphen, underscore); otherwise the text unchanged.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="selector"/> is null or blank, or has the <c>testid:</c> prefix without a name.</exception>
    public static string Resolve(string selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);

        // A name made only of identifier characters cannot be CSS/text/role syntax, so it is a test id
        if (selector.StartsWith(TestIdPrefix, StringComparison.Ordinal))
        {
            var name = selector[TestIdPrefix.Length..];
            return string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A testid: selector needs a name, like testid:hello-button.", nameof(selector)) : TestId(name);
        }

        return selector.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_') ? TestId(selector) : selector;
    }

    /// <summary>
    ///     Builds the attribute selector for a test id, escaping backslashes and quotes.
    /// </summary>
    /// <param name="name">The test id.</param>
    /// <returns>The selector.</returns>
    private static string TestId(string name)
    {
        return $"[data-testid=\"{name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"]";
    }
}
