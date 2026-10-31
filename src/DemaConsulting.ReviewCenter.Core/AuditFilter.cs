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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     What to look for when reading the audit trail: every set part must match, and a part that is <c>null</c>
///     places no limit.
/// </summary>
/// <remarks>
///     One record carries every question the audit pages ask, so the service, the store contract and the pages
///     share one definition of a filter. The scoping rule is part of the meaning: a filter with a
///     <see cref="ProjectId"/> only ever matches entries of that project and never an entry that names no project
///     (a global event such as a sign-in); entries without a project are reachable only with no project set, which
///     the whole-trail page, open to system administrators alone, does. <see cref="AuditService"/> validates and
///     normalizes a filter before the store sees it (see <see cref="Validated"/>), so the store can trust it. The
///     record is immutable and thread-safe.
/// </remarks>
/// <param name="ProjectId">Only entries of this project, or <c>null</c> for entries of any project or none.</param>
/// <param name="ReviewId">
///     Only entries of the review with this identifier (inside <paramref name="ProjectId"/> when that is set, in any
///     project otherwise), or <c>null</c>.
/// </param>
/// <param name="ActorUserId">Only entries by the user with this identity, or <c>null</c>.</param>
/// <param name="ActorText">
///     Only entries whose stored actor text contains this text, without regard to case, or <c>null</c>. The text is
///     matched literally: <c>%</c>, <c>_</c> and <c>\</c> have no special meaning.
/// </param>
/// <param name="Action">Only entries with exactly this action code, or <c>null</c>.</param>
/// <param name="ActionPrefix">
///     Only entries whose action code starts with this text (for example <c>review.</c> for every review action), or
///     <c>null</c>. Matched literally, like <paramref name="ActorText"/>.
/// </param>
/// <param name="From">Only entries recorded on or after the start of this UTC day, or <c>null</c>.</param>
/// <param name="To">Only entries recorded up to the end of this UTC day, inclusive, or <c>null</c>.</param>
public sealed record AuditFilter(
    int? ProjectId = null,
    string? ReviewId = null,
    int? ActorUserId = null,
    string? ActorText = null,
    string? Action = null,
    string? ActionPrefix = null,
    DateOnly? From = null,
    DateOnly? To = null)
{
    /// <summary>
    ///     Gets the filter that matches every entry.
    /// </summary>
    public static AuditFilter None { get; } = new();

    /// <summary>
    ///     Gets the first instant that matches <see cref="From"/>: the start of that UTC day; <c>null</c> for no limit.
    /// </summary>
    public DateTimeOffset? FromUtc => From is { } day ? StartOf(day) : null;

    /// <summary>
    ///     Gets the first instant that is too late for <see cref="To"/>: the start of the next UTC day; <c>null</c> for
    ///     no limit.
    /// </summary>
    /// <remarks>
    ///     The upper bound is exclusive on purpose: every instant of the last day (up to the last tick,
    ///     23:59:59.9999999) is before it, while no tick of the next day is, so a day needs no end-of-day constant.
    ///     The last representable day has no next day and so no upper bound.
    /// </remarks>
    public DateTimeOffset? ToExclusiveUtc => To is { } day && day != DateOnly.MaxValue ? StartOf(day.AddDays(1)) : null;

    /// <summary>
    ///     Checks the filter and returns it in the form the store expects.
    /// </summary>
    /// <returns>
    ///     A filter whose texts are trimmed and whose empty texts are <c>null</c>.
    /// </returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.AuditFilterInvalid"/> when <see cref="From"/> is after <see cref="To"/>,
    ///     or a text is longer than the stored field it is matched against (a longer text could never match, and an
    ///     unbounded text would be a way to make the store work for nothing).
    /// </exception>
    public AuditFilter Validated()
    {
        // Texts are compared as typed by a person: surrounding white space is never meant, and "empty" means "any"
        var reviewId = Clean(ReviewId);
        var actorText = Clean(ActorText);
        var action = Clean(Action);
        var actionPrefix = Clean(ActionPrefix);

        // Bound every text by the width of the stored field it is compared with
        Require(reviewId is null || reviewId.Length <= ReviewLimits.MaxReviewIdLength, "The review id is too long.");
        Require(actorText is null || actorText.Length <= AuditText.MaxActorNameLength, "The actor text is too long.");
        Require(action is null || action.Length <= AuditText.MaxActionLength, "The action is too long.");
        Require(
            actionPrefix is null || actionPrefix.Length <= AuditText.MaxActionLength,
            "The action prefix is too long.");

        // A reversed range can never match anything, so it is a mistake to report rather than an empty result
        Require(From is null || To is null || From <= To, "The 'from' date is after the 'to' date.");

        return this with { ReviewId = reviewId, ActorText = actorText, Action = action, ActionPrefix = actionPrefix };
    }

    /// <summary>
    ///     Describes the filter in one short, human-readable line, for the audit entry of an export.
    /// </summary>
    /// <returns>
    ///     The set parts separated by commas (for example <c>from 2026-01-01, project 5, actor contains "bob"</c>), or
    ///     <c>no filter</c> when nothing is set. The parts are ordered from the short and most telling to the long
    ///     free text: the date range, the project, the review, the actor id, the action and the action group, and the
    ///     actor text last. Every text is at most as long as the stored field it is matched with, but the whole line
    ///     can still be longer than a stored audit detail; a cut by the store then removes only the end of the
    ///     actor text or the actions, never the date range, the project or the review.
    /// </returns>
    public string Describe()
    {
        var parts = new List<string>();
        if (From is { } from)
        {
            parts.Add($"from {from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }

        if (To is { } to)
        {
            parts.Add($"to {to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }

        if (ProjectId is { } project)
        {
            parts.Add($"project {project.ToString(CultureInfo.InvariantCulture)}");
        }

        if (ReviewId is { } review)
        {
            parts.Add($"review \"{review}\"");
        }

        if (ActorUserId is { } actorUserId)
        {
            parts.Add($"actor id {actorUserId.ToString(CultureInfo.InvariantCulture)}");
        }

        if (Action is { } action)
        {
            parts.Add($"action \"{action}\"");
        }

        if (ActionPrefix is { } prefix)
        {
            parts.Add($"action group \"{prefix}\"");
        }

        if (ActorText is { } actorText)
        {
            parts.Add($"actor contains \"{actorText}\"");
        }

        return parts.Count == 0 ? "no filter" : string.Join(", ", parts);
    }

    /// <summary>
    ///     Gets the start of a UTC day.
    /// </summary>
    /// <param name="day">The day.</param>
    /// <returns>Midnight at the start of the day, in UTC.</returns>
    private static DateTimeOffset StartOf(DateOnly day)
    {
        return new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    /// <summary>
    ///     Trims a text and turns an empty one into <c>null</c>.
    /// </summary>
    /// <param name="text">The text, or <c>null</c>.</param>
    /// <returns>The trimmed text, or <c>null</c> when nothing is left.</returns>
    private static string? Clean(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    ///     Raises the invalid-filter rule when a condition does not hold.
    /// </summary>
    /// <param name="condition">The condition that must hold.</param>
    /// <param name="message">The message of the violation.</param>
    /// <exception cref="DomainRuleException">Thrown when <paramref name="condition"/> is <c>false</c>.</exception>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(DomainRule.AuditFilterInvalid, message);
        }
    }
}
