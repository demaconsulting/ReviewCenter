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
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace DemaConsulting.ReviewCenter.Messaging;

/// <summary>
///     Delivers e-mail through an SMTP server, using MailKit.
/// </summary>
/// <remarks>
///     <para>
///     Each send opens one connection, signs in when a user name is set, sends the message and closes the
///     connection; nothing is kept between sends, so the class holds no mutable state and is thread-safe.
///     </para>
///     <para>
///     The message is built by <see cref="BuildMessage"/> from a <see cref="NotificationMessage"/>, which has
///     already refused line breaks and other control characters in the recipient and the subject, so a header can
///     not be injected. MimeKit encodes the display name and the subject. The body is a plain-text part and an HTML
///     part; the caller supplies the HTML already encoded.
///     </para>
///     <para>
///     The password is read from the settings at each send (see <see cref="NotificationOptions.ResolvePassword"/>)
///     and is never logged; a failure is logged for the operator and surfaces as a
///     <see cref="NotificationSendException"/> whose message holds no server text. The connection is closed and the
///     send ends when the cancellation token is canceled.
///     </para>
/// </remarks>
/// <param name="options">The settings. Must not be null.</param>
/// <param name="logger">The logger. Must not be null.</param>
internal sealed class SmtpNotificationSender(NotificationOptions options, ILogger<SmtpNotificationSender> logger)
    : INotificationSender
{
    /// <summary>
    ///     The settings; checked for null once.
    /// </summary>
    private readonly NotificationOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    /// <exception cref="NotificationSendException">Thrown when the message could not be delivered.</exception>
    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var smtp = _options.Smtp;
        try
        {
            var mime = BuildMessage(_options, message);
            var password = _options.ResolvePassword();
            using var client = new SmtpClient();
            client.Timeout = checked(smtp.TimeoutSeconds * 1000);

            // Connect first: the TLS mode decides whether the conversation is encrypted from the first byte
            await client.ConnectAsync(smtp.Host.Trim(), smtp.Port, MapTls(smtp.Tls), cancellationToken);
            if (!string.IsNullOrWhiteSpace(smtp.Username) && password is not null)
            {
                await client.AuthenticateAsync(smtp.Username, password, cancellationToken);
            }

            _ = await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The operator log gets only the exception type (and the SMTP status), because exception messages can
            // repeat the address; the caller gets a message without server text, address or secret. The exception is
            // deliberately not passed to the logger (S6667), because the logger would write its message.
#pragma warning disable S6667
            logger.LogWarning(
                "Sending an e-mail through SMTP failed ({ExceptionType}, SMTP status {StatusCode}).",
                ex.GetType().Name,
                (ex as SmtpCommandException)?.StatusCode.ToString() ?? "none");
#pragma warning restore S6667
            throw new NotificationSendException("The e-mail could not be sent through the SMTP server.", ex);
        }
    }

    /// <summary>
    ///     Builds the MIME message: sender, recipient, subject and the text and HTML parts.
    /// </summary>
    /// <param name="options">The settings that name the sender. Must not be null.</param>
    /// <param name="message">The message. Must not be null.</param>
    /// <returns>The MIME message.</returns>
    /// <remarks>
    ///     Shared with <see cref="FileNotificationSender"/>, so a file shows what an SMTP server would receive.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    internal static MimeMessage BuildMessage(NotificationOptions options, NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(message);
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName.Trim(), options.FromAddress.Trim()));
        mime.To.Add(MailboxAddress.Parse(message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();
        return mime;
    }

    /// <summary>
    ///     Maps the connection protection of the settings to the one of MailKit.
    /// </summary>
    /// <param name="mode">The setting.</param>
    /// <returns>The MailKit option.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a value that is not defined.</exception>
    internal static SecureSocketOptions MapTls(SmtpTlsMode mode)
    {
        return mode switch
        {
            SmtpTlsMode.None => SecureSocketOptions.None,
            SmtpTlsMode.StartTls => SecureSocketOptions.StartTls,
            SmtpTlsMode.Implicit => SecureSocketOptions.SslOnConnect,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), "Unknown TLS mode.")
        };
    }
}
