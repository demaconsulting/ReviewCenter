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
using DemaConsulting.ReviewCenter.Core;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DemaConsulting.ReviewCenter.ContentSources;

/// <summary>
///     Reads the review-sets out of a ReviewMark <c>.reviewmark.yaml</c> with YamlDotNet.
/// </summary>
/// <remarks>
///     <para>
///         ReviewMark itself reads the file with YamlDotNet, so quoting, flow style and block scalars mean
///         the same here as there. Only the top-level <c>reviews</c> list is read, and of each entry only <c>id</c>,
///         <c>title</c> and <c>paths</c>; every other key, at any level, is ignored. The file is deserialized into
///         plain typed objects (no tags and no object graphs), after a size check and a pass that refuses aliases
///         (which could otherwise expand a small file into a huge document), and the counts and lengths are
///         checked afterwards, so a hostile file cannot make the server do unbounded work.
///     </para>
///     <para>
///         A problem is reported as a <see cref="DomainRuleException"/> with
///         <see cref="DomainRule.ReviewSetConfigInvalid"/> and a plain message; the YAML library's own message and
///         stack trace never reach the caller. The class is stateless and thread-safe.
///     </para>
/// </remarks>
internal sealed class ReviewMarkConfigParser : IReviewSetParser
{
    /// <summary>
    ///     The name of the file, for messages.
    /// </summary>
    private const string FileName = ".reviewmark.yaml";

    /// <summary>
    ///     The deserializer; it is stateless once built and reused.
    /// </summary>
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <inheritdoc />
    public IReadOnlyList<ReviewSetDefinition> Parse(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > ReviewLimits.MaxReviewSetFileBytes)
        {
            throw Invalid($"The file {FileName} is larger than {ReviewLimits.MaxReviewSetFileBytes / 1024} KiB.");
        }

        // Read the YAML into plain objects; any failure of the library becomes one plain message
        Document? document;
        try
        {
            var text = Decode(content);
            RejectAliases(text);
            document = Deserializer.Deserialize<Document?>(text);
        }
        catch (YamlException ex)
        {
            var mark = ex.Start;
            throw Invalid($"The file {FileName} is not valid YAML (line {mark.Line}, column {mark.Column}).");
        }

        var entries = document?.Reviews;
        if (entries is null || entries.Count == 0)
        {
            throw Invalid($"The file {FileName} defines no review-sets: it needs a 'reviews' list.");
        }

        if (entries.Count > ReviewLimits.MaxReviewSets)
        {
            throw Invalid($"The file {FileName} defines more than {ReviewLimits.MaxReviewSets} review-sets.");
        }

        // Check every entry; the position (starting at 1) names the entry in messages
        var definitions = new List<ReviewSetDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < entries.Count; index++)
        {
            var definition = ToDefinition(entries[index], index + 1);
            if (!ids.Add(definition.Id))
            {
                throw Invalid($"Review-set {index + 1} repeats the id of an earlier review-set; ids must be unique.");
            }

            definitions.Add(definition);
        }

        return definitions;
    }

    /// <summary>
    ///     Checks one entry and turns it into a definition.
    /// </summary>
    /// <param name="entry">The entry as read; may be null for an empty list item.</param>
    /// <param name="position">The position of the entry, starting at 1.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ReviewSetConfigInvalid"/> when the entry is not usable.</exception>
    private static ReviewSetDefinition ToDefinition(Entry? entry, int position)
    {
        var id = entry?.Id?.Trim();
        var title = entry?.Title?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            throw Invalid($"Review-set {position} has no id.");
        }

        if (string.IsNullOrEmpty(title))
        {
            throw Invalid($"Review-set {position} has no title.");
        }

        if (id.Length > ReviewLimits.MaxReviewSetIdLength)
        {
            throw Invalid($"The id of review-set {position} is longer than {ReviewLimits.MaxReviewSetIdLength} characters.");
        }

        if (title.Length > ReviewLimits.MaxReviewSetTitleLength)
        {
            throw Invalid($"The title of review-set {position} is longer than {ReviewLimits.MaxReviewSetTitleLength} characters.");
        }

        if (id.Any(char.IsControl))
        {
            throw Invalid($"The id of review-set {position} contains control characters.");
        }

        if (title.Any(char.IsControl))
        {
            throw Invalid($"The title of review-set {position} contains control characters.");
        }

        // The paths join into one selection text with line breaks, so a path must not hold control characters
        var paths = entry!.Paths ?? [];
        if (paths.Count > SelectionMatcher.MaxPatterns)
        {
            throw Invalid($"Review-set {position} has more than {SelectionMatcher.MaxPatterns} paths.");
        }

        var cleaned = new List<string>();
        foreach (var path in paths)
        {
            var trimmed = path?.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.Any(char.IsControl) || trimmed.Length > SelectionMatcher.MaxPatternLength)
            {
                throw Invalid(
                    $"A path of review-set {position} is empty, longer than {SelectionMatcher.MaxPatternLength} characters, or contains control characters.");
            }

            cleaned.Add(trimmed);
        }

        return new ReviewSetDefinition(id, title, cleaned);
    }

    /// <summary>
    ///     Refuses a file that uses YAML aliases.
    /// </summary>
    /// <remarks>
    ///     An alias is expanded again at every use, so a small file can describe an enormous document (the "billion
    ///     laughs" attack) before any count can be checked. The review-sets of a real file need no aliases, so the
    ///     events of the file are read first, without expanding anything, and a file that has an alias is refused.
    /// </remarks>
    /// <param name="text">The YAML text.</param>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ReviewSetConfigInvalid"/> when the file has an alias.</exception>
    /// <exception cref="YamlException">Thrown when the text is not valid YAML.</exception>
    private static void RejectAliases(string text)
    {
        var parser = new Parser(new StringReader(text));
        while (parser.MoveNext())
        {
            if (parser.Current is AnchorAlias alias)
            {
                throw Invalid($"The file {FileName} uses a YAML alias (line {alias.Start.Line}), which is not supported.");
            }
        }
    }

    /// <summary>
    ///     Decodes the bytes as UTF-8, dropping a byte order mark.
    /// </summary>
    /// <param name="content">The raw bytes.</param>
    /// <returns>The text; bytes that are not valid UTF-8 become replacement characters.</returns>
    private static string Decode(byte[] content)
    {
        var skip = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(content, skip, content.Length - skip);
    }

    /// <summary>
    ///     Builds the exception for an unusable file.
    /// </summary>
    /// <param name="message">The plain message.</param>
    /// <returns>The exception to throw.</returns>
    private static DomainRuleException Invalid(string message) => new(DomainRule.ReviewSetConfigInvalid, message);

    /// <summary>
    ///     The part of the file that is read: the top-level <c>reviews</c> list.
    /// </summary>
    public sealed class Document
    {
        /// <summary>
        ///     Gets or sets the review-sets.
        /// </summary>
        public List<Entry?>? Reviews { get; set; }
    }

    /// <summary>
    ///     One entry of the <c>reviews</c> list.
    /// </summary>
    public sealed class Entry
    {
        /// <summary>
        ///     Gets or sets the identifier of the set.
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        ///     Gets or sets the title of the set.
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        ///     Gets or sets the ordered include and exclude patterns.
        /// </summary>
        public List<string?>? Paths { get; set; }
    }
}
