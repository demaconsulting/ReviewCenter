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
using System.Net;
using System.Text;

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     What a notification may say about one inbox item: where it is, which review it is about and in which state.
/// </summary>
/// <remarks>
///     The type is the masking boundary of notifications. It has no place for the detail text of an inbox row, its
///     due date, its unread flag or an outcome word, so nothing of that can reach a mail. It is built only by
///     <see cref="NotificationComposer.Classify"/>. The record is immutable and thread-safe.
/// </remarks>
/// <param name="Section">The inbox section of the item.</param>
/// <param name="ProjectId">The project of the review.</param>
/// <param name="ProjectName">The name of the project.</param>
/// <param name="ReviewId">The identifier of the review.</param>
/// <param name="Title">The title of the review.</param>
/// <param name="State">The state token, for example <c>to-answer</c> or <c>replies:3</c>.</param>
public sealed record NotificationCandidate(
    InboxSection Section,
    int ProjectId,
    string ProjectName,
    string ReviewId,
    string Title,
    string State)
{
    /// <summary>
    ///     Gets the identity of the item, the key of the dedupe rule.
    /// </summary>
    public NotifiedKey Key => new(ProjectId, ReviewId, Section, NotificationComposer.KindOf(State));
}

/// <summary>
///     The subject and the two bodies of a notification mail.
/// </summary>
/// <param name="Subject">The subject; one line.</param>
/// <param name="Text">The plain text body.</param>
/// <param name="Html">The HTML body.</param>
public sealed record NotificationComposition(string Subject, string Text, string Html);

/// <summary>
///     Turns inbox rows into the candidates of a notification and composes the mail from them.
/// </summary>
/// <remarks>
///     A mail says only that there is news: the kind of item, the project name, the review title (when the
///     operator allows it), the review identifier, a count of waiting replies and a link to the review. It never
///     holds the detail text of an inbox row, a comment, an answer, a verdict or the outcome of a review; the word
///     "approved" or "rejected" is not even stored, a concluded review is just "concluded". The subject is fixed
///     text with the installation name, so no user text can reach a header. Every user string is HTML-encoded in
///     the HTML part and stripped of control characters in the text part. The type holds no state that changes and
///     is thread-safe.
/// </remarks>
public sealed class NotificationComposer
{
    /// <summary>
    ///     The state token of an item the user has to answer (not started or a draft).
    /// </summary>
    public const string StateToAnswer = "to-answer";

    /// <summary>
    ///     The state token of an open question for the user.
    /// </summary>
    public const string StateQuestion = "question";

    /// <summary>
    ///     The state token of disputed answers; it carries no count.
    /// </summary>
    public const string StateDisputed = "disputed";

    /// <summary>
    ///     The state token of a review the user can sign.
    /// </summary>
    public const string StateReady = "ready";

    /// <summary>
    ///     The state token of a review that changed and must be signed again.
    /// </summary>
    public const string StateSignAgain = "sign-again";

    /// <summary>
    ///     The state token of a signature that no longer counts.
    /// </summary>
    public const string StateLost = "lost";

    /// <summary>
    ///     The state token of a concluded review; the outcome is not part of it.
    /// </summary>
    public const string StateConcluded = "concluded";

    /// <summary>
    ///     The state token of a reopened review.
    /// </summary>
    public const string StateReopened = "reopened";

    /// <summary>
    ///     The prefix of the state token of waiting replies; the count follows the colon.
    /// </summary>
    public const string StateRepliesPrefix = "replies:";

    /// <summary>
    ///     The most items listed in one mail; the rest is counted.
    /// </summary>
    public const int MaxListedItems = 50;

    /// <summary>
    ///     The size of a body at which no further item is listed, well below the limit of
    ///     <see cref="NotificationMessage"/>.
    /// </summary>
    private const int BodyBudget = 80_000;

    /// <summary>
    ///     The longest installation name used in the subject.
    /// </summary>
    private const int MaxInstanceNameLength = 100;

    /// <summary>
    ///     The name of the installation, one line.
    /// </summary>
    private readonly string _instance;

    /// <summary>
    ///     The address of the server without a trailing slash.
    /// </summary>
    private readonly string _baseUrl;

    /// <summary>
    ///     Whether the title of a review is part of a mail.
    /// </summary>
    private readonly bool _includeTitles;

    /// <summary>
    ///     Initializes a new instance of the <see cref="NotificationComposer"/> class.
    /// </summary>
    /// <param name="instanceName">The name of the installation, set by the operator; empty for the default name.</param>
    /// <param name="baseUrl">The public address of the server, for example <c>https://reviews.example.com</c>. Must not be empty.</param>
    /// <param name="includeTitles">Whether the title of a review is part of a mail.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="baseUrl"/> is null or empty.</exception>
    public NotificationComposer(string? instanceName, string baseUrl, bool includeTitles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        // The name comes from the operator's settings; control characters become spaces so it stays on one line
        var name = OneLine(instanceName ?? string.Empty);
        if (name.Length > MaxInstanceNameLength)
        {
            name = name[..MaxInstanceNameLength].TrimEnd();
        }

        _instance = name.Length == 0 ? NotificationSettings.DefaultInstanceName : name;
        _baseUrl = baseUrl.Trim().TrimEnd('/');
        _includeTitles = includeTitles;
    }

    /// <summary>
    ///     Turns an inbox row into a candidate, or refuses the row.
    /// </summary>
    /// <param name="item">The inbox row of the recipient. Must not be null.</param>
    /// <returns>
    ///     The candidate; <c>null</c> for a section that is never mailed (failed checks, waiting for others) and for a
    ///     row whose reason is not known, so that a new kind of row is never mailed by accident.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="item"/> is null.</exception>
    public static NotificationCandidate? Classify(InboxItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var state = StateOf(item);
        return state is null
            ? null
            : new NotificationCandidate(item.Section, item.ProjectId, item.ProjectName, item.ReviewId, item.Title, state);
    }

    /// <summary>
    ///     Gets the kind of the reason of a state token: the token without a count.
    /// </summary>
    /// <param name="state">The state token, for example <c>question</c> or <c>replies:3</c>.</param>
    /// <returns>The kind, for example <c>question</c> or <c>replies</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> is null.</exception>
    public static string KindOf(string state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var colon = state.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? state : state[..colon];
    }

    /// <summary>
    ///     Tells whether an item in a state is new for a user who was told about the stored state.
    /// </summary>
    /// <param name="storedState">The state the user was told about; <c>null</c> when the user was told nothing.</param>
    /// <param name="currentState">The state of the item now.</param>
    /// <returns>
    ///     <c>true</c> when nothing is stored, or the state differs; for waiting replies only when the count rose.
    /// </returns>
    public static bool IsNew(string? storedState, string currentState)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        if (storedState is null)
        {
            return true;
        }

        if (TryReadReplies(storedState, out var before) && TryReadReplies(currentState, out var now))
        {
            return now > before;
        }

        return !string.Equals(storedState, currentState, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tells whether the count of waiting replies has fallen below the stored one.
    /// </summary>
    /// <param name="storedState">The state the user was told about; <c>null</c> when the user was told nothing.</param>
    /// <param name="currentState">The state of the item now.</param>
    /// <returns>
    ///     <c>true</c> when both states are waiting replies and the current count is lower than the stored count. Such
    ///     an item is not new, but the stored count must follow it down, so that a later rise is measured from the
    ///     count the item has now.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="currentState"/> is null.</exception>
    public static bool IsLowered(string? storedState, string currentState)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        return storedState is not null
               && TryReadReplies(storedState, out var before)
               && TryReadReplies(currentState, out var now)
               && now < before;
    }

    /// <summary>
    ///     Composes the mail for the new items.
    /// </summary>
    /// <param name="fresh">The new items; not empty. Must not be null.</param>
    /// <param name="outstandingCount">
    ///     How many action items the user was told about earlier and that still wait; shown as a number only, and only
    ///     in a daily summary.
    /// </param>
    /// <param name="mode">The mode of the recipient; <see cref="NotificationMode.Daily"/> composes the summary.</param>
    /// <returns>The subject and the bodies.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fresh"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="fresh"/> is empty.</exception>
    public NotificationComposition Compose(
        IReadOnlyList<NotificationCandidate> fresh,
        int outstandingCount,
        NotificationMode mode)
    {
        ArgumentNullException.ThrowIfNull(fresh);
        if (fresh.Count == 0)
        {
            throw new ArgumentException("A notification needs at least one item.", nameof(fresh));
        }

        var daily = mode == NotificationMode.Daily;
        var subject = daily ? $"{_instance}: your daily summary" : $"{_instance}: there is news for you";
        var text = new StringBuilder();
        var html = new StringBuilder();
        var intro = daily
            ? $"Here is your daily summary of {_instance}. This is what is new for you:"
            : $"There is news for you in {_instance}:";
        text.Append(intro).Append("\n\n");
        html.Append("<p>").Append(WebUtility.HtmlEncode(intro)).Append("</p>\n");

        // Group by section in inbox order; the sort is stable, so the order inside a section is kept
        var ordered = fresh.OrderBy(c => c.Section).ToList();
        var listed = 0;
        InboxSection? current = null;
        var listOpen = false;
        foreach (var item in ordered)
        {
            if (listed >= MaxListedItems || text.Length > BodyBudget || html.Length > BodyBudget)
            {
                break;
            }

            if (current != item.Section)
            {
                if (listOpen)
                {
                    html.Append("</ul>\n");
                }

                current = item.Section;
                var heading = HeadingOf(item.Section);
                text.Append(heading).Append('\n');
                html.Append("<h3>").Append(WebUtility.HtmlEncode(heading)).Append("</h3>\n<ul>\n");
                listOpen = true;
            }

            AppendItem(text, html, item);
            listed++;
        }

        if (listOpen)
        {
            html.Append("</ul>\n");
        }

        text.Append('\n');
        AppendRemainder(text, html, ordered.Count - listed, daily ? outstandingCount : 0);
        AppendFooter(text, html);
        return new NotificationComposition(subject, text.ToString(), html.ToString());
    }

    /// <summary>
    ///     Gets the state token of an inbox row.
    /// </summary>
    /// <param name="item">The row.</param>
    /// <returns>The token; <c>null</c> when the row is not mailed.</returns>
    private static string? StateOf(InboxItem item)
    {
        return item.Section switch
        {
            InboxSection.ToAnswer => item.Chip switch
            {
                InboxService.NotStartedChip or InboxService.DraftChip => StateToAnswer,
                InboxService.QuestionChip => StateQuestion,
                _ => null
            },
            InboxSection.Disputed => StateDisputed,
            InboxSection.ToSign => item.Chip switch
            {
                InboxService.NotSignedChip => StateReady,
                InboxService.SignAgainChip => StateSignAgain,
                _ => null
            },
            InboxSection.SignatureLost => StateLost,
            InboxSection.RepliesWaiting => RepliesStateOf(item.Chip),
            InboxSection.Updates => item.Chip switch
            {
                InboxService.ApprovedChip or InboxService.RejectedChip => StateConcluded,
                InboxService.ReopenedChip => StateReopened,
                _ => null
            },
            _ => null
        };
    }

    /// <summary>
    ///     Gets the state token of a replies row from its chip, for example <c>3 waiting</c>.
    /// </summary>
    /// <param name="chip">The chip.</param>
    /// <returns>The token; <c>null</c> when the chip holds no count.</returns>
    private static string? RepliesStateOf(string chip)
    {
        var space = chip.IndexOf(' ', StringComparison.Ordinal);
        var number = space > 0 ? chip[..space] : chip;
        return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? StateRepliesPrefix + count.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    ///     Reads the count of a replies state token.
    /// </summary>
    /// <param name="state">The token.</param>
    /// <param name="count">The count.</param>
    /// <returns><c>true</c> when the token is a replies token.</returns>
    private static bool TryReadReplies(string state, out int count)
    {
        count = 0;
        return state.StartsWith(StateRepliesPrefix, StringComparison.Ordinal)
               && int.TryParse(
                   state.AsSpan(StateRepliesPrefix.Length),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out count);
    }

    /// <summary>
    ///     Gets the heading of a section in a mail.
    /// </summary>
    /// <param name="section">The section.</param>
    /// <returns>The heading.</returns>
    private static string HeadingOf(InboxSection section)
    {
        return section switch
        {
            InboxSection.ToAnswer => "To answer",
            InboxSection.Disputed => "Answers that differ",
            InboxSection.ToSign => "To sign",
            InboxSection.SignatureLost => "Signature no longer counts",
            InboxSection.RepliesWaiting => "Replies waiting",
            _ => "Updates"
        };
    }

    /// <summary>
    ///     Gets the sentence for the state of an item.
    /// </summary>
    /// <param name="state">The state token.</param>
    /// <returns>The sentence, without a final period.</returns>
    private static string SentenceOf(string state)
    {
        if (TryReadReplies(state, out var count))
        {
            return count == 1 ? "1 reply is waiting" : $"{count.ToString(CultureInfo.InvariantCulture)} replies are waiting";
        }

        return state switch
        {
            StateToAnswer => "your answers are needed",
            StateQuestion => "a question is waiting for you",
            StateDisputed => "answers differ and need to be settled",
            StateReady => "ready for your signature",
            StateSignAgain => "the review changed, please sign again",
            StateLost => "the review changed, your signature no longer counts",
            StateConcluded => "the review was concluded",
            StateReopened => "the review was reopened",
            _ => "there is news"
        };
    }

    /// <summary>
    ///     Adds the lines of one item to both bodies.
    /// </summary>
    /// <param name="text">The text body.</param>
    /// <param name="html">The HTML body.</param>
    /// <param name="item">The item.</param>
    private void AppendItem(StringBuilder text, StringBuilder html, NotificationCandidate item)
    {
        var project = OneLine(item.ProjectName);
        var review = OneLine(item.ReviewId);
        var title = OneLine(item.Title);
        var what = _includeTitles && title.Length > 0 ? $"{project} / {review} \"{title}\"" : $"{project} / {review}";
        var sentence = SentenceOf(item.State);
        var link = LinkTo(item);
        text.Append("- ").Append(what).Append(": ").Append(sentence).Append('\n')
            .Append("  ").Append(link).Append('\n');
        html.Append("<li>").Append(WebUtility.HtmlEncode(what)).Append(": ").Append(WebUtility.HtmlEncode(sentence))
            .Append(" (<a href=\"").Append(WebUtility.HtmlEncode(link)).Append("\">open</a>)</li>\n");
    }

    /// <summary>
    ///     Adds the count of items that are not listed and the count of items that still wait.
    /// </summary>
    /// <param name="text">The text body.</param>
    /// <param name="html">The HTML body.</param>
    /// <param name="more">How many new items were not listed.</param>
    /// <param name="outstanding">How many earlier items still wait; zero for none.</param>
    private static void AppendRemainder(StringBuilder text, StringBuilder html, int more, int outstanding)
    {
        if (more > 0)
        {
            var line = $"... and {more.ToString(CultureInfo.InvariantCulture)} more.";
            text.Append(line).Append("\n\n");
            html.Append("<p>").Append(WebUtility.HtmlEncode(line)).Append("</p>\n");
        }

        if (outstanding > 0)
        {
            var line = outstanding == 1
                ? "1 more item you were told about earlier is still waiting for you."
                : $"{outstanding.ToString(CultureInfo.InvariantCulture)} more items you were told about earlier are still waiting for you.";
            text.Append(line).Append("\n\n");
            html.Append("<p>").Append(WebUtility.HtmlEncode(line)).Append("</p>\n");
        }
    }

    /// <summary>
    ///     Adds the footer: why the mail was sent and where to change it.
    /// </summary>
    /// <param name="text">The text body.</param>
    /// <param name="html">The HTML body.</param>
    private void AppendFooter(StringBuilder text, StringBuilder html)
    {
        var reason = $"You get this message because you chose to be notified by e-mail in {_instance}. " +
                     "Change or stop it on your Account page:";
        var account = _baseUrl + "/account";
        text.Append("--\n").Append(reason).Append('\n').Append(account).Append('\n');
        html.Append("<hr>\n<p>").Append(WebUtility.HtmlEncode(reason)).Append(" <a href=\"")
            .Append(WebUtility.HtmlEncode(account)).Append("\">")
            .Append(WebUtility.HtmlEncode(account)).Append("</a></p>\n");
    }

    /// <summary>
    ///     Builds the link to the review of an item; it needs a sign-in and carries no token.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The absolute link.</returns>
    private string LinkTo(NotificationCandidate item)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_baseUrl}/projects/{item.ProjectId}/reviews/{Uri.EscapeDataString(item.ReviewId)}");
    }

    /// <summary>
    ///     Makes a user string one line: every control character and every line or paragraph separator (U+2028,
    ///     U+2029) becomes a space.
    /// </summary>
    /// <param name="value">The string.</param>
    /// <returns>The single line, trimmed.</returns>
    private static string OneLine(string value)
    {
        var chars = value.Select(c => IsLineBreaking(c) ? ' ' : c).ToArray();
        return new string(chars).Trim();
    }

    /// <summary>
    ///     Tells whether a character can break a line or end a header: a control character, or a line or paragraph
    ///     separator.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><c>true</c> when the character must not stay in a one-line text.</returns>
    private static bool IsLineBreaking(char c)
    {
        return char.IsControl(c) || c is '\u2028' or '\u2029';
    }
}
