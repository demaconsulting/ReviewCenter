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
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     The filter of an audit page as the address carries it: parsed from the query string, never failing, and
///     able to write itself back into a query string for the paging links.
/// </summary>
/// <remarks>
///     Every value comes from the address and is therefore hostile until proven otherwise. Parsing is lenient and
///     total: a value that is not usable falls back to "no limit" (or, for the page size, to the default) instead
///     of raising, so a hand-edited address never breaks the page. Texts are trimmed and cut to the width of the
///     stored field they are matched with, dates must be <c>yyyy-MM-dd</c> (invariant culture), the action must be
///     one of <see cref="AuditActions"/> (an exact action, or a group written with its trailing dot), and the page
///     size must be one the pages offer. A <c>from</c> date after the <c>to</c> date can never match, so both dates
///     are dropped and <see cref="Message"/> says so. The values are rendered back into the form by the Razor
///     encoder, and written into links with <see cref="Uri.EscapeDataString(string)"/>. The state is immutable
///     once parsed and thread-safe.
/// </remarks>
public sealed class AuditFilterState
{
    /// <summary>
    ///     The format of a date in the address and in the date inputs.
    /// </summary>
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    ///     The longest raw query value that is looked at; longer values cannot be valid for any field.
    /// </summary>
    private const int MaxRawLength = 200;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditFilterState"/> class that matches everything.
    /// </summary>
    private AuditFilterState()
    {
    }

    /// <summary>
    ///     Gets the text to look for in the actor, or an empty string for any actor.
    /// </summary>
    public string Actor { get; private init; } = string.Empty;

    /// <summary>
    ///     Gets the chosen action: an empty string for any action, a group with its trailing dot (for example
    ///     <c>review.</c>), or one exact action code.
    /// </summary>
    public string Action { get; private init; } = string.Empty;

    /// <summary>
    ///     Gets the review identifier to look for, or an empty string for any review.
    /// </summary>
    public string Review { get; private init; } = string.Empty;

    /// <summary>
    ///     Gets the first UTC day to show, or <c>null</c> for no limit.
    /// </summary>
    public DateOnly? From { get; private init; }

    /// <summary>
    ///     Gets the last UTC day to show, or <c>null</c> for no limit.
    /// </summary>
    public DateOnly? To { get; private init; }

    /// <summary>
    ///     Gets the project to show on the whole-trail page, or <c>null</c> for every project.
    /// </summary>
    public int? ProjectId { get; private init; }

    /// <summary>
    ///     Gets the number of entries on a page; always one of <see cref="AuditService.PageSizes"/>.
    /// </summary>
    public int PageSize { get; private init; } = AuditService.DefaultPageSize;

    /// <summary>
    ///     Gets a sentence about a value that was dropped, or <c>null</c> when every value was usable.
    /// </summary>
    public string? Message { get; private init; }

    /// <summary>
    ///     Gets a value indicating whether anything differs from the unfiltered, default-size view.
    /// </summary>
    public bool IsFiltered =>
        Actor.Length > 0 || Action.Length > 0 || Review.Length > 0 || From is not null || To is not null ||
        ProjectId is not null || PageSize != AuditService.DefaultPageSize;

    /// <summary>
    ///     Gets a value indicating whether any entry-limiting value is set (the page size alone limits nothing).
    /// </summary>
    public bool Narrows =>
        Actor.Length > 0 || Action.Length > 0 || Review.Length > 0 || From is not null || To is not null ||
        ProjectId is not null;

    /// <summary>
    ///     Gets the date in the form of the date inputs, or an empty string for no date.
    /// </summary>
    /// <param name="day">The day, or <c>null</c>.</param>
    /// <returns>The day as <c>yyyy-MM-dd</c>, or an empty string.</returns>
    public static string FormatDate(DateOnly? day)
    {
        return day?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>
    ///     Reads the filter from the raw query values of a page.
    /// </summary>
    /// <param name="actor">The <c>actor</c> value, or <c>null</c>.</param>
    /// <param name="action">The <c>action</c> value, or <c>null</c>.</param>
    /// <param name="review">The <c>review</c> value, or <c>null</c>.</param>
    /// <param name="from">The <c>from</c> value, or <c>null</c>.</param>
    /// <param name="to">The <c>to</c> value, or <c>null</c>.</param>
    /// <param name="size">The <c>size</c> value, or <c>null</c>.</param>
    /// <param name="project">The <c>project</c> value, or <c>null</c>; only the whole-trail page uses it.</param>
    /// <returns>The state; never <c>null</c>, and every value in it is safe to use.</returns>
    public static AuditFilterState FromQuery(
        string? actor,
        string? action,
        string? review,
        string? from,
        string? to,
        string? size,
        string? project)
    {
        var fromDay = ParseDate(from);
        var toDay = ParseDate(to);
        string? message = null;
        if (fromDay is not null && toDay is not null && fromDay > toDay)
        {
            fromDay = null;
            toDay = null;
            message = "The 'from' date is after the 'to' date, so both dates were ignored.";
        }

        return new AuditFilterState
        {
            Actor = Text(actor, AuditText.MaxActorNameLength),
            Action = ParseAction(action),
            Review = Text(review, ReviewLimits.MaxReviewIdLength),
            From = fromDay,
            To = toDay,
            ProjectId = int.TryParse(Raw(project), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
                ? id
                : null,
            PageSize = int.TryParse(Raw(size), NumberStyles.None, CultureInfo.InvariantCulture, out var requested) &&
                       AuditService.PageSizes.Contains(requested)
                ? requested
                : AuditService.DefaultPageSize,
            Message = message,
        };
    }

    /// <summary>
    ///     Builds the filter the service reads with.
    /// </summary>
    /// <param name="projectId">
    ///     The project to limit to; a project page passes its own project so the page can never show another one,
    ///     the whole-trail page passes <c>null</c> and uses <see cref="ProjectId"/> instead.
    /// </param>
    /// <returns>The filter; it is valid by construction.</returns>
    public AuditFilter ToFilter(int? projectId)
    {
        var isGroup = Action.EndsWith('.');
        return new AuditFilter(
            ProjectId: projectId ?? ProjectId,
            ReviewId: Review.Length > 0 ? Review : null,
            ActorText: Actor.Length > 0 ? Actor : null,
            Action: Action.Length > 0 && !isGroup ? Action : null,
            ActionPrefix: isGroup ? Action : null,
            From: From,
            To: To);
    }

    /// <summary>
    ///     Writes the state and a paging position into a query string.
    /// </summary>
    /// <param name="before">The "Older" position, or <c>null</c>.</param>
    /// <param name="after">The "Newer" position, or <c>null</c>.</param>
    /// <returns>An empty string, or <c>?</c> followed by the <c>name=value</c> pairs that differ from the defaults.</returns>
    public string ToQuery(long? before = null, long? after = null)
    {
        var parts = new List<string>();
        Add(parts, "project", ProjectId?.ToString(CultureInfo.InvariantCulture));
        Add(parts, "actor", Actor);
        Add(parts, "action", Action);
        Add(parts, "review", Review);
        Add(parts, "from", From is null ? null : FormatDate(From));
        Add(parts, "to", To is null ? null : FormatDate(To));
        Add(
            parts,
            "size",
            PageSize == AuditService.DefaultPageSize ? null : PageSize.ToString(CultureInfo.InvariantCulture));
        Add(parts, "before", before?.ToString(CultureInfo.InvariantCulture));
        Add(parts, "after", after?.ToString(CultureInfo.InvariantCulture));
        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

    /// <summary>
    ///     Writes the filter into the query string of an export link: the format and the entry-limiting values.
    /// </summary>
    /// <param name="format">The export format, <c>csv</c> or <c>json</c>.</param>
    /// <param name="includeProject">
    ///     <c>true</c> to carry the project (the whole-trail page); a project page's export is already limited to
    ///     its own project by the address.
    /// </param>
    /// <returns>
    ///     <c>?format=</c> and the format, followed by the <c>name=value</c> pairs of the values that are set. The page
    ///     size and the paging positions are never carried: an export covers everything that matches the filter.
    /// </returns>
    public string ToExportQuery(string format, bool includeProject)
    {
        var parts = new List<string>();
        Add(parts, "format", format);
        Add(parts, "project", includeProject ? ProjectId?.ToString(CultureInfo.InvariantCulture) : null);
        Add(parts, "actor", Actor);
        Add(parts, "action", Action);
        Add(parts, "review", Review);
        Add(parts, "from", From is null ? null : FormatDate(From));
        Add(parts, "to", To is null ? null : FormatDate(To));
        return "?" + string.Join('&', parts);
    }

    /// <summary>
    ///     Adds one <c>name=value</c> pair when the value is not empty.
    /// </summary>
    /// <param name="parts">The pairs so far.</param>
    /// <param name="name">The query name.</param>
    /// <param name="value">The value, or <c>null</c> or empty to add nothing.</param>
    private static void Add(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            parts.Add(name + "=" + Uri.EscapeDataString(value));
        }
    }

    /// <summary>
    ///     Gets a raw value only when it is short enough to be valid for any field.
    /// </summary>
    /// <param name="value">The raw value, or <c>null</c>.</param>
    /// <returns>The trimmed value, or an empty string.</returns>
    private static string Raw(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length > MaxRawLength ? string.Empty : trimmed;
    }

    /// <summary>
    ///     Trims a text and cuts it to the width of the field it is matched with.
    /// </summary>
    /// <param name="value">The raw value, or <c>null</c>.</param>
    /// <param name="maxLength">The width of the stored field.</param>
    /// <returns>The text, never longer than <paramref name="maxLength"/>.</returns>
    private static string Text(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length > maxLength ? trimmed[..maxLength].TrimEnd() : trimmed;
    }

    /// <summary>
    ///     Reads a date written as <c>yyyy-MM-dd</c>.
    /// </summary>
    /// <param name="value">The raw value, or <c>null</c>.</param>
    /// <returns>The day, or <c>null</c> when the value is empty or not a date.</returns>
    private static DateOnly? ParseDate(string? value)
    {
        return DateOnly.TryParseExact(Raw(value), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;
    }

    /// <summary>
    ///     Reads the action choice: only a known action or a known group (with its trailing dot) is accepted.
    /// </summary>
    /// <param name="value">The raw value, or <c>null</c>.</param>
    /// <returns>The action or group, or an empty string for any action.</returns>
    private static string ParseAction(string? value)
    {
        var action = Raw(value);
        return AuditActions.IsKnownAction(action) || AuditActions.IsKnownPrefix(action) ? action : string.Empty;
    }
}
