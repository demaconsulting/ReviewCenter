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

using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Messaging;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace DemaConsulting.ReviewCenter.SelfTest.Checks;

/// <summary>
///     Checks that a notification mail is composed without any ballot content and delivered as one <c>.eml</c> file.
/// </summary>
/// <remarks>
///     <para>
///     The check turns two fixture inbox rows into a mail with the production <see cref="NotificationComposer"/>
///     and hands it to the production <see cref="FileNotificationSender"/>, which writes it into a temporary
///     folder. It then reads the file back and verifies that exactly one <c>.eml</c> file and no temporary file
///     exist, that the mail names the project and links to the review, that the subject names no review, and that
///     none of the text of the fixture rows (the title, the detail text and the outcome word) is in the mail.
///     </para>
///     <para>
///     The check is hermetic: the settings are given in memory, nothing is read from the configuration, no
///     database, repository, host or network is used, and the temporary folder is removed at the end. The report
///     holds no address and no path. The class is stateless and thread-safe.
///     </para>
/// </remarks>
internal static class NotificationMailCheck
{
    /// <summary>
    ///     The name of the check in the report and the results file.
    /// </summary>
    public const string Name = "ReviewCenter_NotificationMail";

    /// <summary>
    ///     The name of the project of the fixture rows.
    /// </summary>
    private const string ProjectName = "Selftest Project";

    /// <summary>
    ///     The identifier of the review of the fixture rows.
    /// </summary>
    private const string ReviewId = "SELFTEST-REVIEW";

    /// <summary>
    ///     The title of the review of the fixture rows; it must not reach the mail.
    /// </summary>
    private const string ReviewTitle = "MARKER-TITLE-TEXT";

    /// <summary>
    ///     The detail text of the fixture rows; it must never reach a mail.
    /// </summary>
    private const string DetailText = "MARKER-DETAIL-TEXT";

    /// <summary>
    ///     The address the server's links start with in the fixture; the loopback address is never reachable from
    ///     outside.
    /// </summary>
    private static readonly string BaseUrl = $"https://{System.Net.IPAddress.Loopback}";

    /// <summary>
    ///     Runs the check.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A short description of what was verified; never an address or a path.</returns>
    /// <exception cref="ValidationFailedException">
    ///     Thrown when the mail is not composed as expected, when not exactly one file is written, or when the mail
    ///     holds text it must not hold.
    /// </exception>
    public static async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var folder = new TemporaryDirectory();
        var options = new NotificationOptions
        {
            Enabled = true,
            Sender = NotificationSenderKind.File,
            FromAddress = "reviewcenter@example.com",
            BaseUrl = BaseUrl,
            IncludeTitles = false,
            File = new FileNotificationOptions { Directory = folder.DirectoryPath }
        };

        // Compose: a row to answer and a concluded review with an outcome, each with text that must stay out of a mail
        var candidates = new[]
            {
                new InboxItem(
                    InboxSection.ToAnswer, 7, ProjectName, ReviewId, ReviewTitle, InboxService.NotStartedChip,
                    true, null, false, DetailText),
                new InboxItem(
                    InboxSection.Updates, 7, ProjectName, ReviewId, ReviewTitle, InboxService.ApprovedChip,
                    true, null, false, DetailText)
            }
            .Select(NotificationComposer.Classify)
            .OfType<NotificationCandidate>()
            .ToList();
        ValidationFailedException.ThrowUnless(candidates.Count == 2, "A fixture row was not turned into a candidate.");
        var composer = new NotificationComposer(options.FromName, options.BaseUrl, options.IncludeTitles);
        var composition = composer.Compose(candidates, 0, NotificationMode.Every);
        var message = new NotificationMessage("user@example.com", composition.Subject, composition.Text, composition.Html);

        // Deliver with the production file sender
        await new FileNotificationSender(options, NullLogger<FileNotificationSender>.Instance)
            .SendAsync(message, cancellationToken);
        var files = Directory.GetFiles(folder.DirectoryPath, "*.eml");
        ValidationFailedException.ThrowUnless(files.Length == 1, "Exactly one .eml file was not written.");
        ValidationFailedException.ThrowUnless(
            Directory.GetFiles(folder.DirectoryPath, "*.tmp").Length == 0,
            "A temporary file was left behind.");

        // Read the mail back as a mail client would
        MimeMessage mime;
        await using (var stream = File.OpenRead(files[0]))
        {
            mime = await MimeMessage.LoadAsync(stream, cancellationToken);
        }

        var text = mime.TextBody ?? string.Empty;
        var html = mime.HtmlBody ?? string.Empty;
        ValidationFailedException.ThrowUnless(
            text.Length > 0 && html.Length > 0,
            "The mail does not hold both a text and an HTML form.");

        // The mail names the project and links to the review
        var link = $"{BaseUrl}/projects/7/reviews/{ReviewId}";
        ValidationFailedException.ThrowUnless(
            text.Contains(ProjectName, StringComparison.Ordinal) && html.Contains(ProjectName, StringComparison.Ordinal),
            "The mail does not name the project.");
        ValidationFailedException.ThrowUnless(
            text.Contains(link, StringComparison.Ordinal) && html.Contains(link, StringComparison.Ordinal),
            "The mail does not link to the review.");

        // The subject is fixed text and names no review
        var subject = mime.Subject ?? string.Empty;
        ValidationFailedException.ThrowUnless(
            !subject.Contains(ReviewId, StringComparison.Ordinal)
            && !subject.Contains(ProjectName, StringComparison.Ordinal),
            "The subject names a review or a project.");

        // None of the text of the fixture rows, and no outcome word, is in the mail
        foreach (var forbidden in new[] { ReviewTitle, DetailText, "approved", "rejected" })
        {
            ValidationFailedException.ThrowUnless(
                !text.Contains(forbidden, StringComparison.OrdinalIgnoreCase)
                && !html.Contains(forbidden, StringComparison.OrdinalIgnoreCase)
                && !subject.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                "The mail holds text of a row or an outcome word.");
        }

        return "2 items composed; 1 .eml file written; project and link present; no ballot content; " +
               "subject names no review";
    }
}
