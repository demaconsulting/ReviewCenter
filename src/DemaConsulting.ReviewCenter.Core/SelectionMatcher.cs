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
///     One parsed file selection pattern: whether it includes or excludes, and its path segments.
/// </summary>
/// <remarks>
///     Instances are created only by <see cref="SelectionMatcher.Parse"/>, which validates the text. The type is
///     immutable and thread-safe.
/// </remarks>
public sealed class SelectionPattern
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SelectionPattern"/> class.
    /// </summary>
    /// <param name="text">The normalized pattern text, without the exclusion mark.</param>
    /// <param name="exclude">Whether a match removes the path from the selection.</param>
    /// <param name="segments">The segments of the pattern, split at <c>/</c>.</param>
    internal SelectionPattern(string text, bool exclude, IReadOnlyList<string> segments)
    {
        Text = text;
        Exclude = exclude;
        Segments = segments;
    }

    /// <summary>
    ///     Gets the normalized pattern text, without the exclusion mark.
    /// </summary>
    public string Text { get; }

    /// <summary>
    ///     Gets a value indicating whether a match removes the path from the selection.
    /// </summary>
    public bool Exclude { get; }

    /// <summary>
    ///     Gets the segments of the pattern; a segment of exactly <c>**</c> matches any number of path segments.
    /// </summary>
    public IReadOnlyList<string> Segments { get; }
}

/// <summary>
///     Parses and applies the file selection patterns an author gives when a review is created from a repository.
/// </summary>
/// <remarks>
///     <para>
///         The syntax is deliberately small. A pattern is one line; blank lines are skipped and each line is
///         trimmed. A leading <c>!</c> makes the pattern an exclusion. The patterns are applied in order: a path is
///         selected when the last pattern that matches it is an inclusion. <c>*</c> matches any characters within one
///         path segment (never <c>/</c>), <c>?</c> matches exactly one character, and a segment of exactly <c>**</c>
///         matches zero or more segments (a trailing <c>**</c> needs at least one). A trailing <c>/</c> means
///         everything below that folder, a name without wildcards is an exact path, and a leading <c>./</c> or
///         <c>/</c> is dropped. Matching is ordinal and case-sensitive. <c>[</c>, <c>]</c>, <c>{</c> and <c>}</c> are
///         ordinary characters.
///     </para>
///     <para>
///         Backslashes, <c>..</c> and <c>.</c> segments, empty segments, control characters and over-long or
///         too many patterns are rejected, so a pattern can never reach outside the repository or hide text.
///         The class is stateless and thread-safe.
///     </para>
/// </remarks>
public static class SelectionMatcher
{
    /// <summary>
    ///     The maximum number of patterns in one selection.
    /// </summary>
    public const int MaxPatterns = 100;

    /// <summary>
    ///     The maximum length of one pattern line, in characters.
    /// </summary>
    public const int MaxPatternLength = 400;

    /// <summary>
    ///     The selection that matches every changed file; it is the default when a client sends no selection.
    /// </summary>
    public const string AllFiles = "**/*";

    /// <summary>
    ///     The segment text that matches any number of path segments.
    /// </summary>
    private const string GlobStar = "**";

    /// <summary>
    ///     Parses and validates the text of a selection.
    /// </summary>
    /// <param name="text">The patterns, one per line. Must not be null.</param>
    /// <returns>The validated patterns in their original order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.SelectionInvalid"/> when a rule is broken.</exception>
    public static IReadOnlyList<SelectionPattern> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var patterns = new List<SelectionPattern>();
        foreach (var line in text.Split('\n').Select(rawLine => rawLine.Trim()))
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (patterns.Count == MaxPatterns)
            {
                throw Invalid($"A selection may have at most {MaxPatterns} patterns.");
            }

            patterns.Add(ParseLine(line));
        }

        if (patterns.Count == 0)
        {
            throw Invalid("Enter at least one file pattern.");
        }

        return patterns;
    }

    /// <summary>
    ///     Decides whether a path is selected by a list of patterns.
    /// </summary>
    /// <param name="path">The repository path, using <c>/</c> as the separator. Must not be null.</param>
    /// <param name="patterns">The patterns from <see cref="Parse"/>. Must not be null.</param>
    /// <returns><c>true</c> when the last matching pattern is an inclusion; <c>false</c> when none matches or an exclusion matches last.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static bool IsMatch(string path, IReadOnlyList<SelectionPattern> patterns)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(patterns);

        var parts = path.Split('/');
        var selected = false;
        foreach (var pattern in patterns.Where(p => MatchSegments(p.Segments, parts)))
        {
            selected = !pattern.Exclude;
        }

        return selected;
    }

    /// <summary>
    ///     Writes parsed patterns back as the normalized selection text a review stores.
    /// </summary>
    /// <remarks>
    ///     One shared writer keeps the text a review stores and the text the planner compares it with identical.
    /// </remarks>
    /// <param name="patterns">The patterns from <see cref="Parse"/>. Must not be null.</param>
    /// <returns>The patterns in their original order, one per line (<c>\n</c>), exclusions with a leading <c>!</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="patterns"/> is null.</exception>
    public static string Normalize(IReadOnlyList<SelectionPattern> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        return string.Join('\n', patterns.Select(p => (p.Exclude ? "!" : string.Empty) + p.Text));
    }

    /// <summary>
    ///     Parses one trimmed, non-empty line.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>The pattern.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.SelectionInvalid"/> when the line is not valid.</exception>
    private static SelectionPattern ParseLine(string line)
    {
        if (line.Length > MaxPatternLength)
        {
            throw Invalid($"A pattern may have at most {MaxPatternLength} characters.");
        }

        if (line.Any(char.IsControl))
        {
            throw Invalid("A pattern must not contain control characters.");
        }

        var exclude = line.StartsWith('!');
        var body = exclude ? line[1..].Trim() : line;
        if (body.Contains('\\', StringComparison.Ordinal))
        {
            throw Invalid("Use / as the path separator in a pattern, not \\.");
        }

        while (body.StartsWith("./", StringComparison.Ordinal) || body.StartsWith('/'))
        {
            body = body.StartsWith('/') ? body[1..] : body[2..];
        }

        if (body.Length == 0)
        {
            throw Invalid(exclude ? "A ! must be followed by a pattern." : "A pattern must name a file or folder.");
        }

        var segments = body.Split('/').ToList();
        if (segments[^1].Length == 0)
        {
            segments[^1] = GlobStar;
        }

        ValidateSegments(segments, body);
        return new SelectionPattern(body, exclude, segments);
    }

    /// <summary>
    ///     Refuses a pattern that has an empty, <c>.</c> or <c>..</c> path segment.
    /// </summary>
    /// <param name="segments">The segments of the pattern.</param>
    /// <param name="body">The pattern without its leading marks, for the message.</param>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.SelectionInvalid"/> when a segment is not valid.</exception>
    private static void ValidateSegments(IReadOnlyList<string> segments, string body)
    {
        if (segments.Any(segment => segment.Length == 0 || segment == "." || segment == ".."))
        {
            throw Invalid($"The pattern '{body}' has an empty, '.' or '..' path segment.");
        }
    }

    /// <summary>
    ///     Matches the segments of a pattern against the segments of a path.
    /// </summary>
    /// <param name="pattern">The pattern segments.</param>
    /// <param name="parts">The path segments.</param>
    /// <returns><c>true</c> when the whole path matches.</returns>
    private static bool MatchSegments(IReadOnlyList<string> pattern, string[] parts)
    {
        // Memo of the answer for (pattern index, path index): 0 unknown, 1 true, 2 false. It keeps patterns with
        // many ** segments from taking exponential time.
        var memo = new byte[(pattern.Count + 1) * (parts.Length + 1)];
        return Match(pattern, 0, parts, 0, memo);
    }

    /// <summary>
    ///     Matches the rest of a pattern against the rest of a path.
    /// </summary>
    /// <param name="pattern">The pattern segments.</param>
    /// <param name="pi">The index of the next pattern segment.</param>
    /// <param name="parts">The path segments.</param>
    /// <param name="si">The index of the next path segment.</param>
    /// <param name="memo">The memo of earlier answers.</param>
    /// <returns><c>true</c> when the rest matches.</returns>
    private static bool Match(IReadOnlyList<string> pattern, int pi, string[] parts, int si, byte[] memo)
    {
        var key = (pi * (parts.Length + 1)) + si;
        if (memo[key] != 0)
        {
            return memo[key] == 1;
        }

        bool result;
        if (pi == pattern.Count)
        {
            result = si == parts.Length;
        }
        else if (pattern[pi] == GlobStar)
        {
            // A trailing ** must take at least one segment, so "folder/**" does not select a file called "folder"
            var minimum = pi == pattern.Count - 1 ? 1 : 0;
            result = false;
            for (var take = minimum; si + take <= parts.Length && !result; take++)
            {
                result = Match(pattern, pi + 1, parts, si + take, memo);
            }
        }
        else
        {
            result = si < parts.Length
                && MatchSegment(pattern[pi], parts[si])
                && Match(pattern, pi + 1, parts, si + 1, memo);
        }

        memo[key] = result ? (byte)1 : (byte)2;
        return result;
    }

    /// <summary>
    ///     Matches one path segment against one pattern segment with <c>*</c> and <c>?</c>.
    /// </summary>
    /// <param name="pattern">The pattern segment.</param>
    /// <param name="text">The path segment.</param>
    /// <returns><c>true</c> when the whole segment matches.</returns>
    private static bool MatchSegment(string pattern, string text)
    {
        // Iterative wildcard matching: remember the last * and, on a mismatch, let it take one more character
        int p = 0, t = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == text[t]))
            {
                p++;
                t++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    /// <summary>
    ///     Creates the exception for a selection rule violation.
    /// </summary>
    /// <param name="message">The operator-readable explanation.</param>
    /// <returns>The exception to throw.</returns>
    private static DomainRuleException Invalid(string message) =>
        new(DomainRule.SelectionInvalid, message);
}
