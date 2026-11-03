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

using System.Net;
using System.Net.Sockets;
using System.Text;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Messaging;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace DemaConsulting.ReviewCenter.Tests.Messaging;

/// <summary>
///     Unit tests for <see cref="SmtpNotificationSender"/> against an in-process fake SMTP server on the loopback
///     interface; no mail leaves the machine.
/// </summary>
public class SmtpNotificationSenderTests
{
    /// <summary>
    ///     Test that the sender reports that it is configured.
    /// </summary>
    [Fact]
    public void SmtpNotificationSender_IsConfigured_IsTrue()
    {
        // Arrange and Act: a sender
        var sender = new SmtpNotificationSender(Options(1), NullLogger<SmtpNotificationSender>.Instance);

        // Assert: configured
        Assert.True(sender.IsConfigured);
    }

    /// <summary>
    ///     Test that a message is delivered with the envelope, the headers and both body parts.
    /// </summary>
    [Fact]
    public async Task SmtpNotificationSender_Send_DeliversMessageToServer()
    {
        // Arrange: a server and a sender that points at it
        using var server = new FakeSmtpServer();
        var sender = new SmtpNotificationSender(Options(server.Port), NullLogger<SmtpNotificationSender>.Instance);
        var message = new NotificationMessage("ann@example.com", "Hello Ann", "plain text body", "<p>html body</p>");

        // Act: send
        await sender.SendAsync(message, TestContext.Current.CancellationToken);

        // Assert: the envelope, the headers and both parts arrived
        Assert.Equal(["reviews@example.com"], server.Senders);
        Assert.Equal(["ann@example.com"], server.Recipients);
        var received = Parse(Assert.Single(server.Messages));
        Assert.Equal("Hello Ann", received.Subject);
        Assert.Equal("ann@example.com", received.To.Mailboxes.Single().Address);
        Assert.Equal("Review Center", received.From.Mailboxes.Single().Name);
        Assert.Contains("plain text body", received.TextBody, StringComparison.Ordinal);
        Assert.Contains("<p>html body</p>", received.HtmlBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a sender name and a subject with special characters stay in their own header.
    /// </summary>
    [Fact]
    public async Task SmtpNotificationSender_Send_SpecialCharacters_DoNotLeaveTheirHeader()
    {
        // Arrange: a sender name with a comma, quotes and angle brackets and a subject with non-ASCII text
        using var server = new FakeSmtpServer();
        var options = Options(server.Port);
        options.FromName = "Review, \"Center\" <x@evil.example>";
        var sender = new SmtpNotificationSender(options, NullLogger<SmtpNotificationSender>.Instance);
        var message = new NotificationMessage("ann@example.com", "Gr\u00fc\u00dfe: Bcc: eve@example.org", "t", "<p>t</p>");

        // Act: send
        await sender.SendAsync(message, TestContext.Current.CancellationToken);

        // Assert: one sender, one recipient, no extra header and the texts unchanged
        var received = Parse(Assert.Single(server.Messages));
        Assert.Equal("reviews@example.com", received.From.Mailboxes.Single().Address);
        Assert.Equal(options.FromName, received.From.Mailboxes.Single().Name);
        Assert.Equal(message.Subject, received.Subject);
        Assert.Empty(received.Bcc);
        Assert.Empty(received.Cc);
        Assert.DoesNotContain(received.Headers, header => header.Field.Equals("Bcc", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["ann@example.com"], server.Recipients);
    }

    /// <summary>
    ///     Test that the user name and the password are sent when a user name is set.
    /// </summary>
    [Fact]
    public async Task SmtpNotificationSender_Send_WithCredentials_Authenticates()
    {
        // Arrange: a server that wants a password
        using var server = new FakeSmtpServer { ExpectedPassword = "pa55" };
        var options = Options(server.Port);
        options.Smtp.Username = "mailer";
        options.Smtp.Password = "pa55";
        var sender = new SmtpNotificationSender(options, NullLogger<SmtpNotificationSender>.Instance);

        // Act: send
        await sender.SendAsync(Message(), TestContext.Current.CancellationToken);

        // Assert: signed in and delivered
        Assert.Equal(["mailer:pa55"], server.Credentials);
        Assert.Single(server.Messages);
    }

    /// <summary>
    ///     Test that a wrong password fails the send with a message that holds neither the password nor the server
    ///     text.
    /// </summary>
    [Fact]
    public async Task SmtpNotificationSender_Send_WrongPassword_ThrowsWithoutSecret()
    {
        // Arrange: a server that wants another password
        using var server = new FakeSmtpServer { ExpectedPassword = "right" };
        var options = Options(server.Port);
        options.Smtp.Username = "mailer";
        options.Smtp.Password = "wrong-secret";
        var sender = new SmtpNotificationSender(options, NullLogger<SmtpNotificationSender>.Instance);

        // Act: send
        var exception = await Assert.ThrowsAsync<NotificationSendException>(
            () => sender.SendAsync(Message(), TestContext.Current.CancellationToken));

        // Assert: nothing delivered and no secret in the message
        Assert.Empty(server.Messages);
        Assert.DoesNotContain("wrong-secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Rejected", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a recipient the server refuses fails the send without echoing the address.
    /// </summary>
    [Fact]
    public async Task SmtpNotificationSender_Send_RecipientRefused_ThrowsWithoutAddress()
    {
        // Arrange: a server that refuses every recipient
        using var server = new FakeSmtpServer { RejectRecipients = true };
        var sender = new SmtpNotificationSender(Options(server.Port), NullLogger<SmtpNotificationSender>.Instance);

        // Act: send
        var exception = await Assert.ThrowsAsync<NotificationSendException>(
            () => sender.SendAsync(Message(), TestContext.Current.CancellationToken));

        // Assert: nothing delivered and no address or server text in the message
        Assert.Empty(server.Messages);
        Assert.DoesNotContain("ann@example.com", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("No such user", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the operator log of a failed send holds neither the recipient nor the password and no
    ///     exception object.
    /// </summary>
    [Fact]
    public async Task SmtpNotificationSender_Send_Failure_LogsNoRecipientOrPassword()
    {
        // Arrange: a server that refuses every recipient, a login that works, and a logger that records
        using var server = new FakeSmtpServer { RejectRecipients = true, ExpectedPassword = "pa55-secret" };
        var options = Options(server.Port);
        options.Smtp.Username = "mailer";
        options.Smtp.Password = "pa55-secret";
        var logger = new RecordingLogger();
        var sender = new SmtpNotificationSender(options, logger);

        // Act: send
        await Assert.ThrowsAsync<NotificationSendException>(
            () => sender.SendAsync(Message(), TestContext.Current.CancellationToken));

        // Assert: something was logged, without the recipient, the password, the server text or an exception
        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain("ann@example.com", entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("pa55-secret", entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("No such user", entry.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a server that can not be reached fails the send.
    /// </summary>
    [Fact]
    public async Task SmtpNotificationSender_Send_ServerUnreachable_Throws()
    {
        // Arrange: a port nobody listens on
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
        }

        var sender = new SmtpNotificationSender(Options(port), NullLogger<SmtpNotificationSender>.Instance);

        // Act and Assert: the failure is the domain exception
        await Assert.ThrowsAsync<NotificationSendException>(
            () => sender.SendAsync(Message(), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a canceled token ends the send with a cancellation, not with a send failure.
    /// </summary>
    [Fact]
    public async Task SmtpNotificationSender_Send_Canceled_ThrowsOperationCanceled()
    {
        // Arrange: a server and a token that is already canceled
        using var server = new FakeSmtpServer();
        var sender = new SmtpNotificationSender(Options(server.Port), NullLogger<SmtpNotificationSender>.Instance);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        // Act and Assert: cancellation passes through
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.SendAsync(Message(), canceled.Token));
        Assert.Empty(server.Messages);
    }

    /// <summary>
    ///     Test that a missing message is a programming error.
    /// </summary>
    [Fact]
    public async Task SmtpNotificationSender_Send_NullMessage_ThrowsArgumentNull()
    {
        // Arrange: a sender
        var sender = new SmtpNotificationSender(Options(1), NullLogger<SmtpNotificationSender>.Instance);

        // Act and Assert: refused
        await Assert.ThrowsAsync<ArgumentNullException>(() => sender.SendAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that each TLS mode maps to the matching MailKit option and an undefined mode is refused.
    /// </summary>
    [Fact]
    public void SmtpNotificationSender_MapTls_MapsEveryMode()
    {
        // Arrange, Act and Assert: the three modes and an undefined one
        Assert.Equal(SecureSocketOptions.None, SmtpNotificationSender.MapTls(SmtpTlsMode.None));
        Assert.Equal(SecureSocketOptions.StartTls, SmtpNotificationSender.MapTls(SmtpTlsMode.StartTls));
        Assert.Equal(SecureSocketOptions.SslOnConnect, SmtpNotificationSender.MapTls(SmtpTlsMode.Implicit));
        Assert.Throws<ArgumentOutOfRangeException>(() => SmtpNotificationSender.MapTls((SmtpTlsMode)77));
    }

    /// <summary>
    ///     Parses the data a fake server recorded.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns>The message.</returns>
    private static MimeMessage Parse(string data)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        return MimeMessage.Load(stream);
    }

    /// <summary>
    ///     Creates a simple message.
    /// </summary>
    /// <returns>The message.</returns>
    private static NotificationMessage Message()
    {
        return new NotificationMessage("ann@example.com", "Subject", "text", "<p>html</p>");
    }

    /// <summary>
    ///     Creates settings for an unencrypted conversation with the fake server.
    /// </summary>
    /// <param name="port">The port of the fake server.</param>
    /// <returns>The settings.</returns>
    private static NotificationOptions Options(int port)
    {
        return new NotificationOptions
        {
            Enabled = true,
            Sender = NotificationSenderKind.Smtp,
            FromAddress = "reviews@example.com",
            FromName = "Review Center",
            BaseUrl = "https://reviews.example.com",
            Smtp = new SmtpNotificationOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Tls = SmtpTlsMode.None,
                TimeoutSeconds = 10
            }
        };
    }

    /// <summary>
    ///     A logger that keeps every entry for inspection.
    /// </summary>
    private sealed class RecordingLogger : ILogger<SmtpNotificationSender>
    {
        /// <summary>
        ///     Gets the entries written so far.
        /// </summary>
        public List<(string Text, Exception? Exception)> Entries { get; } = [];

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((formatter(state, exception), exception));
    }
}
