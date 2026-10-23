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
///     attribute, so the common case should be as short as possible: a plain name such as
///     <c>hello-button</c> means "the element whose test id is hello-button". Anything that
///     contains selector syntax (CSS, text, or role selectors) is passed through unchanged for the
///     occasional case where a test id does not exist. The class is stateless and thread-safe.
/// </remarks>
internal static class SelectorResolver
{
    /// <summary>
    ///     Resolves the selector text to a Playwright selector.
    /// </summary>
    /// <param name="selector">The text the user supplied. Must not be null or blank.</param>
    /// <returns>
    ///     A <c>[data-testid="name"]</c> attribute selector when <paramref name="selector"/> is a
    ///     plain name (letters, digits, hyphen, underscore); otherwise the text unchanged.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="selector"/> is null or blank.</exception>
    public static string Resolve(string selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);

        // A name made only of identifier characters cannot be CSS/text/role syntax, so it is a test id
        return IsPlainName(selector) ? $"[data-testid=\"{selector}\"]" : selector;
    }

    /// <summary>
    ///     Checks whether the text consists only of identifier characters.
    /// </summary>
    /// <param name="selector">The text to check.</param>
    /// <returns><c>true</c> when every character is a letter, digit, hyphen, or underscore.</returns>
    private static bool IsPlainName(string selector)
    {
        return selector.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
    }
}
