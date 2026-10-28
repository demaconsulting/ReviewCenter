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
using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Core;
using Microsoft.AspNetCore.Components;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the audit entry table (<c>AuditEntryTable.razor</c>): shows one page of audit entries and
///     the paging buttons; shared by the whole-trail page and the project audit page.
/// </summary>
/// <remarks>
///     The component only formats what its parent gives it. The parent decides who may see the entries and clears
///     them when the right is lost, so the component holds no state of its own and needs no synchronization.
/// </remarks>
public partial class AuditEntryTable : ComponentBase
{
    /// <summary>
    ///     Gets or sets the page of entries to show; the parent supplies it.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public AuditEntryPage Page { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the address of the parent page without a query, which the paging buttons extend with the
    ///     position, for example <c>/audit</c>.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public string BasePath { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the sentence shown when the page has no entries.
    /// </summary>
    [Parameter]
    public string EmptyText { get; set; } = "Nothing has been recorded yet.";

    /// <summary>
    ///     Formats the time of an entry in UTC.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The time as <c>yyyy-MM-dd HH:mm:ss</c> in UTC.</returns>
    private static string FormatTime(AuditEntry entry)
    {
        return entry.TimestampUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Describes the review an entry belongs to.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The project and review ids, or the project id alone, or a dash for an entry without either.</returns>
    private static string ReviewLabel(AuditEntry entry)
    {
        if (entry.ProjectId is null)
        {
            return "-";
        }

        return entry.ReviewId is null ? $"project {entry.ProjectId}" : $"{entry.ProjectId}/{entry.ReviewId}";
    }

    /// <summary>
    ///     Renders an entry detail as plain text, with long hexadecimal values shortened (full value in a tooltip).
    /// </summary>
    /// <param name="detail">The detail text of an entry.</param>
    /// <returns>The content to render; all text is encoded by the renderer.</returns>
    private static RenderFragment DetailContent(string detail)
    {
        return builder =>
        {
            foreach (var (text, isHash) in SplitDetail(detail))
            {
                if (isHash)
                {
                    builder.OpenElement(0, "span");
                    builder.AddAttribute(1, "class", "audit-hash");
                    builder.AddAttribute(2, "title", text);
                    builder.AddContent(3, ShortHash(text));
                    builder.CloseElement();
                }
                else
                {
                    builder.AddContent(4, text);
                }
            }
        };
    }

    /// <summary>
    ///     Splits an entry detail into plain text parts and long hexadecimal values (hashes, fingerprints), so the
    ///     page can show the values shortened with the full value kept in a tooltip.
    /// </summary>
    /// <param name="detail">The detail text of an entry.</param>
    /// <returns>The parts in order; the concatenated text of all parts is the original detail.</returns>
    private static IEnumerable<(string Text, bool IsHash)> SplitDetail(string detail)
    {
        var position = 0;
        foreach (var match in LongHexRegex().Matches(detail).Cast<Match>())
        {
            if (match.Index > position)
            {
                yield return (detail[position..match.Index], false);
            }

            yield return (detail.Substring(match.Index, match.Length), true);
            position = match.Index + match.Length;
        }

        if (position < detail.Length)
        {
            yield return (detail[position..], false);
        }
    }

    /// <summary>
    ///     Shortens a long hexadecimal value for display.
    /// </summary>
    /// <param name="value">The full value.</param>
    /// <returns>The first 12 characters followed by an ellipsis.</returns>
    private static string ShortHash(string value)
    {
        return value.Length <= 12 ? value : value[..12] + "…";
    }

    /// <summary>
    ///     Matches a hexadecimal value long enough to be a hash or fingerprint (at least 24 characters).
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex("(?<![0-9A-Za-z])[0-9a-fA-F]{24,}(?![0-9A-Za-z])")]
    private static partial Regex LongHexRegex();

    /// <summary>
    ///     Builds the address of another page of the trail.
    /// </summary>
    /// <param name="key">The query key: <c>before</c> or <c>after</c>.</param>
    /// <param name="sequence">The sequence to carry; <c>null</c> gives the first page.</param>
    /// <returns>The address, relative to the site root.</returns>
    private string PageLink(string key, long? sequence)
    {
        return sequence is { } value ? $"{BasePath}?{key}={value.ToString(CultureInfo.InvariantCulture)}" : BasePath;
    }
}
