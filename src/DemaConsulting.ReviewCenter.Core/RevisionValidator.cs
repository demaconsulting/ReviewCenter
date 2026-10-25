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
///     Validates the revision (branch, tag or commit name) an author enters, before it is given to git.
/// </summary>
/// <remarks>
///     <para>
///         The rule is a strict allow-list, not a copy of git's own naming rules: 1 to
///         <see cref="MaxLength"/> characters, starting with a letter or digit, then letters, digits and
///         <c>. _ / + @ # -</c>. Names that git would also accept but that are risky or ambiguous are refused:
///         <c>..</c>, <c>@{</c>, <c>//</c>, a trailing <c>/</c> or <c>.</c>, and a <c>.lock</c> suffix. Because a
///         revision cannot start with <c>-</c> and cannot hold spaces or control characters, it can never be read as
///         a git option (such as <c>--upload-pack=...</c>) or split into several arguments. The ancestry forms
///         (<c>~</c>, <c>^</c>, <c>:</c>) are not allowed either.
///     </para>
///     <para>
///         The check is a defense in depth: the git layer also passes the revision after <c>--end-of-options</c>
///         and never through a shell. The class is stateless and thread-safe.
///     </para>
/// </remarks>
public static class RevisionValidator
{
    /// <summary>
    ///     The maximum length of a revision, in characters.
    /// </summary>
    public const int MaxLength = 200;

    /// <summary>
    ///     Checks a revision and returns it unchanged when it is acceptable.
    /// </summary>
    /// <param name="revision">The revision to check; leading and trailing white space is not removed.</param>
    /// <returns>The revision, exactly as given.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.RevisionInvalid"/> when the revision is not acceptable.</exception>
    public static string Validate(string? revision)
    {
        if (!IsValid(revision))
        {
            throw new DomainRuleException(
                DomainRule.RevisionInvalid,
                $"A revision must be 1 to {MaxLength} characters: a branch, tag or commit name made of letters, digits and . _ / + @ # -, starting with a letter or digit.");
        }

        return revision!;
    }

    /// <summary>
    ///     Decides whether a revision is acceptable.
    /// </summary>
    /// <param name="revision">The revision to check.</param>
    /// <returns><c>true</c> when the revision meets every rule.</returns>
    public static bool IsValid(string? revision)
    {
        if (string.IsNullOrEmpty(revision) || revision.Length > MaxLength)
        {
            return false;
        }

        if (!char.IsAsciiLetterOrDigit(revision[0])
            || revision.Any(c => !char.IsAsciiLetterOrDigit(c) && "._/+@#-".IndexOf(c, StringComparison.Ordinal) < 0))
        {
            return false;
        }
        return !revision.Contains("..", StringComparison.Ordinal)
            && !revision.Contains("@{", StringComparison.Ordinal)
            && !revision.Contains("//", StringComparison.Ordinal)
            && !revision.EndsWith('/')
            && !revision.EndsWith('.')
            && !revision.EndsWith(".lock", StringComparison.Ordinal);
    }
}
