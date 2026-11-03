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

namespace DemaConsulting.ReviewCenter.Tests.Messaging;

/// <summary>
///     A minimal SMTP server that listens on the loopback interface, for tests of the SMTP sender.
/// </summary>
/// <remarks>
///     The server speaks just enough of the protocol (greeting, the client greeting, <c>AUTH PLAIN</c>, <c>MAIL</c>,
///     <c>RCPT</c>, <c>DATA</c>, <c>QUIT</c>) for one client at a time, never encrypts and never relays: every
///     accepted message is only recorded. The recorded lists are guarded by a lock and safe to read while the
///     server runs. The class must be disposed, which stops the listener.
/// </remarks>
internal sealed class FakeSmtpServer : IDisposable
{
    /// <summary>
    ///     Guards the recorded lists.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     The listener on the loopback interface.
    /// </summary>
    private readonly TcpListener _listener;

    /// <summary>
    ///     Stops the accept loop.
    /// </summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>
    ///     The accept loop.
    /// </summary>
    private readonly Task _loop;

    /// <summary>
    ///     The messages received, as the text between <c>DATA</c> and the closing dot.
    /// </summary>
    private readonly List<string> _messages = [];

    /// <summary>
    ///     The envelope senders received.
    /// </summary>
    private readonly List<string> _senders = [];

    /// <summary>
    ///     The envelope recipients received.
    /// </summary>
    private readonly List<string> _recipients = [];

    /// <summary>
    ///     The credentials received with <c>AUTH PLAIN</c>, as <c>user:password</c>.
    /// </summary>
    private readonly List<string> _credentials = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeSmtpServer"/> class and starts to listen.
    /// </summary>
    public FakeSmtpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(AcceptLoopAsync, CancellationToken.None);
    }

    /// <summary>
    ///     Gets the port the server listens on.
    /// </summary>
    public int Port { get; }

    /// <summary>
    ///     Gets or sets a value indicating whether the server refuses every recipient with a permanent error.
    /// </summary>
    public bool RejectRecipients { get; set; }

    /// <summary>
    ///     Gets or sets the password the server accepts; <c>null</c> accepts any.
    /// </summary>
    public string? ExpectedPassword { get; set; }

    /// <summary>
    ///     Gets the messages received so far.
    /// </summary>
    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
            {
                return [.. _messages];
            }
        }
    }

    /// <summary>
    ///     Gets the envelope senders received so far.
    /// </summary>
    public IReadOnlyList<string> Senders
    {
        get
        {
            lock (_gate)
            {
                return [.. _senders];
            }
        }
    }

    /// <summary>
    ///     Gets the envelope recipients received so far.
    /// </summary>
    public IReadOnlyList<string> Recipients
    {
        get
        {
            lock (_gate)
            {
                return [.. _recipients];
            }
        }
    }

    /// <summary>
    ///     Gets the credentials received so far, as <c>user:password</c>.
    /// </summary>
    public IReadOnlyList<string> Credentials
    {
        get
        {
            lock (_gate)
            {
                return [.. _credentials];
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        catch (AggregateException)
        {
            // The loop ends with an exception when the listener stops; nothing to report
        }

        _stop.Dispose();
    }

    /// <summary>
    ///     Accepts clients one after the other until the server is disposed.
    /// </summary>
    /// <returns>A task that completes when the listener has stopped.</returns>
    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await ServeAsync(client);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            catch (IOException)
            {
                // A client that hangs up in the middle of a conversation does not stop the server
            }
        }
    }

    /// <summary>
    ///     Serves one client until it quits or hangs up.
    /// </summary>
    /// <param name="client">The connected client.</param>
    /// <returns>A task that completes when the conversation ends.</returns>
    private async Task ServeAsync(TcpClient client)
    {
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, new ASCIIEncoding()) { NewLine = "\r\n", AutoFlush = true };
        await ReplyAsync(writer, "220 localhost fake SMTP ready");
        while (await reader.ReadLineAsync(_stop.Token) is { } line)
        {
            var command = line.ToUpperInvariant();
            // cspell:ignore EHLO HELO
            if (command.StartsWith("EHLO", StringComparison.Ordinal) || command.StartsWith("HELO", StringComparison.Ordinal))
            {
                await ReplyAsync(writer, "250-localhost");
                await ReplyAsync(writer, "250 AUTH PLAIN");
            }
            else if (command.StartsWith("AUTH PLAIN", StringComparison.Ordinal))
            {
                await AuthenticateAsync(writer, line);
            }
            else if (command.StartsWith("MAIL FROM:", StringComparison.Ordinal))
            {
                Record(_senders, Between(line));
                await ReplyAsync(writer, "250 OK");
            }
            else if (command.StartsWith("RCPT TO:", StringComparison.Ordinal))
            {
                Record(_recipients, Between(line));
                await ReplyAsync(writer, RejectRecipients ? "550 5.1.1 No such user" : "250 OK");
            }
            else if (command == "DATA")
            {
                await ReplyAsync(writer, "354 End data with <CR><LF>.<CR><LF>");
                Record(_messages, await ReadDataAsync(reader));
                await ReplyAsync(writer, "250 OK queued");
            }
            else if (command == "QUIT")
            {
                await ReplyAsync(writer, "221 Bye");
                return;
            }
            else
            {
                await ReplyAsync(writer, "250 OK");
            }
        }
    }

    /// <summary>
    ///     Writes one reply line to the client.
    /// </summary>
    /// <param name="writer">The writer to the client.</param>
    /// <param name="text">The reply without the line break.</param>
    /// <returns>A task that completes when the line is written.</returns>
    private Task ReplyAsync(StreamWriter writer, string text)
    {
        return writer.WriteLineAsync(text.AsMemory(), _stop.Token);
    }

    /// <summary>
    ///     Answers an <c>AUTH PLAIN</c> command that carries its initial response.
    /// </summary>
    /// <param name="writer">The writer to the client.</param>
    /// <param name="line">The command line.</param>
    /// <returns>A task that completes when the answer is written.</returns>
    private async Task AuthenticateAsync(StreamWriter writer, string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var decoded = parts.Length > 2 ? Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])) : string.Empty;
        var fields = decoded.Split('\0');
        var user = fields.Length > 1 ? fields[1] : string.Empty;
        var password = fields.Length > 2 ? fields[2] : string.Empty;
        Record(_credentials, $"{user}:{password}");
        var accepted = ExpectedPassword is null || ExpectedPassword == password;
        await ReplyAsync(writer, accepted ? "235 2.7.0 Authentication successful" : "535 5.7.8 Rejected");
    }

    /// <summary>
    ///     Reads the data of a message up to the closing dot line.
    /// </summary>
    /// <param name="reader">The reader from the client.</param>
    /// <returns>The data with the dot-stuffing removed and lines joined by line breaks.</returns>
    private async Task<string> ReadDataAsync(StreamReader reader)
    {
        var builder = new StringBuilder();
        while (await reader.ReadLineAsync(_stop.Token) is { } line && line != ".")
        {
            builder.Append(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line).Append("\r\n");
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Takes the text between the angle brackets of a command line.
    /// </summary>
    /// <param name="line">The command line.</param>
    /// <returns>The text between <c>&lt;</c> and <c>&gt;</c>; the whole line when there are none.</returns>
    private static string Between(string line)
    {
        var start = line.IndexOf('<', StringComparison.Ordinal);
        var end = line.IndexOf('>', StringComparison.Ordinal);
        return start >= 0 && end > start ? line[(start + 1)..end] : line;
    }

    /// <summary>
    ///     Adds a value to one of the recorded lists under the lock.
    /// </summary>
    /// <param name="list">The list.</param>
    /// <param name="value">The value.</param>
    private void Record(List<string> list, string value)
    {
        lock (_gate)
        {
            list.Add(value);
        }
    }
}
