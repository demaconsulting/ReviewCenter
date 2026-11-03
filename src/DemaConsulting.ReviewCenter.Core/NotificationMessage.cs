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
///     One e-mail the server wants to send: a single recipient, a subject and the text and HTML forms of the body.
/// </summary>
/// <remarks>
///     The message is validated when it is built, so that no sender ever has to defend itself against a value that
///     could add a header line or a second recipient: the recipient must be exactly one acceptable address (see
///     <see cref="NotificationAddress"/>) and the subject must be a single line without a control character. The
///     exception messages name the field and never repeat the value. The bodies may hold line breaks but no NUL
///     character. The type is immutable and thread-safe.
/// </remarks>
public sealed class NotificationMessage
{
    /// <summary>
    ///     The longest accepted subject in characters.
    /// </summary>
    public const int MaxSubjectLength = 200;

    /// <summary>
    ///     The longest accepted body (text or HTML) in characters.
    /// </summary>
    public const int MaxBodyLength = 100_000;

    /// <summary>
    ///     Initializes a new instance of the <see cref="NotificationMessage"/> class.
    /// </summary>
    /// <param name="toAddress">The recipient; one acceptable address. It is normalized to lower case.</param>
    /// <param name="subject">The subject; one line of 1 to <see cref="MaxSubjectLength"/> characters without a control character.</param>
    /// <param name="textBody">The plain text body; not empty.</param>
    /// <param name="htmlBody">The HTML body; not empty. The caller encodes every value it puts into it.</param>
    /// <exception cref="ArgumentException">
    ///     Thrown when a value breaks a rule above; the message names the parameter and never holds the value.
    /// </exception>
    public NotificationMessage(string toAddress, string subject, string textBody, string htmlBody)
    {
        // The recipient must be exactly one plain address; this also rejects a line break or a second recipient
        if (!NotificationAddress.TryNormalize(toAddress, out var normalized))
        {
            throw new ArgumentException("The recipient must be exactly one valid e-mail address.", nameof(toAddress));
        }

        // The subject becomes a header: a line break or another control character could start a new header line
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > MaxSubjectLength || subject.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"The subject must be one line of 1 to {MaxSubjectLength} characters without control characters.",
                nameof(subject));
        }

        CheckBody(textBody, nameof(textBody));
        CheckBody(htmlBody, nameof(htmlBody));
        ToAddress = normalized;
        Subject = subject;
        TextBody = textBody;
        HtmlBody = htmlBody;
    }

    /// <summary>
    ///     Gets the recipient: one normalized (lower case) address.
    /// </summary>
    public string ToAddress { get; }

    /// <summary>
    ///     Gets the subject: one line without a control character.
    /// </summary>
    public string Subject { get; }

    /// <summary>
    ///     Gets the plain text body.
    /// </summary>
    public string TextBody { get; }

    /// <summary>
    ///     Gets the HTML body.
    /// </summary>
    public string HtmlBody { get; }

    /// <summary>
    ///     Checks one body.
    /// </summary>
    /// <param name="body">The body.</param>
    /// <param name="parameterName">The name of the parameter, for the message.</param>
    /// <exception cref="ArgumentException">Thrown when the body is empty, too long or holds a NUL character.</exception>
    private static void CheckBody(string body, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > MaxBodyLength || body.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The body must be 1 to {MaxBodyLength} characters without a NUL character.",
                parameterName);
        }
    }
}
