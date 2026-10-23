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
using System.Text.Json;

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     A failure the user should be told about as a plain message, without a stack trace.
/// </summary>
/// <remarks>
///     Used for every anticipated operational problem: a missing element, a timeout, an unreachable
///     daemon, a start-up failure. Unexpected programming errors use other exception types so they
///     are not hidden behind a friendly message.
/// </remarks>
public sealed class PuppetException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PuppetException"/> class.
    /// </summary>
    /// <param name="message">The message shown to the user.</param>
    public PuppetException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="PuppetException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">The message shown to the user.</param>
    /// <param name="innerException">The underlying cause.</param>
    public PuppetException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    ///     Describes an unexpected exception as a one-line message for the user and the session file.
    /// </summary>
    /// <param name="exception">The unexpected exception.</param>
    /// <returns>
    ///     Text of the form <c>Unexpected error (TypeName): first line of the message</c>, so the cause
    ///     is visible without a stack trace and without hiding that it was not an anticipated failure.
    /// </returns>
    internal static string DescribeUnexpected(Exception exception)
    {
        var message = exception.Message;
        var index = message.IndexOfAny(['\r', '\n']);
        var firstLine = index < 0 ? message : message[..index];
        return $"Unexpected error ({exception.GetType().Name}): {firstLine}";
    }
}

/// <summary>
///     One command sent from a client invocation to the session daemon.
/// </summary>
/// <param name="Token">The session's shared secret; the daemon rejects requests without it.</param>
/// <param name="Command">The verb (goto, click, fill, text, screenshot, snapshot, stop).</param>
/// <param name="Selector">The selector text for element commands; otherwise <c>null</c>.</param>
/// <param name="Text">The text to type for <c>fill</c>, or the path/URL for <c>goto</c>; otherwise <c>null</c>.</param>
/// <param name="OutputPath">The absolute screenshot path, or <c>null</c> for the default location.</param>
/// <param name="FullPage">Whether a screenshot captures the whole scrollable page.</param>
/// <param name="TimeoutMs">The per-command timeout in milliseconds.</param>
internal sealed record ControlRequest(
    string Token,
    string Command,
    string? Selector,
    string? Text,
    string? OutputPath,
    bool FullPage,
    int TimeoutMs);

/// <summary>
///     The daemon's answer to one <see cref="ControlRequest"/>.
/// </summary>
/// <param name="Ok">Whether the command succeeded.</param>
/// <param name="ExitCode">The process exit code the client should return (0 when successful).</param>
/// <param name="Message">The text to show the user: command output on success, the reason on failure.</param>
internal sealed record ControlResponse(bool Ok, int ExitCode, string Message);

/// <summary>
///     The wire format shared by the control client and server: one JSON document per line.
/// </summary>
/// <remarks>
///     Newline-delimited JSON is trivial to implement on both ends, easy to debug with any TCP tool,
///     and needs no framing beyond the line break because JSON strings escape embedded newlines.
/// </remarks>
internal static class ControlProtocol
{
    /// <summary>
    ///     The longest line either side accepts, which bounds memory use on a malformed peer.
    /// </summary>
    public const int MaxLineLength = 8 * 1024 * 1024;

    /// <summary>
    ///     Serializes a message to a single line of JSON (without the line break).
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="message">The message.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize<T>(T message)
    {
        return JsonSerializer.Serialize(message);
    }

    /// <summary>
    ///     Deserializes a message from a line of JSON.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="line">The JSON text.</param>
    /// <returns>The message.</returns>
    /// <exception cref="PuppetException">Thrown when the line is not a valid message.</exception>
    public static T Deserialize<T>(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(line) ?? throw new PuppetException("Empty control message.");
        }
        catch (JsonException ex)
        {
            throw new PuppetException("Malformed control message.", ex);
        }
    }

    /// <summary>
    ///     Parses the answer a client received, accepting only exactly the shape the daemon sends.
    /// </summary>
    /// <remarks>
    ///     The client connects to a loopback port that the session file names. That port can be taken by an
    ///     unrelated process once the daemon is gone, and such a process may answer with anything. Like the
    ///     server readiness probe, which accepts only the exact health answer, the client therefore accepts
    ///     only a JSON object with exactly a boolean <c>Ok</c>, an integer <c>ExitCode</c>, and a string
    ///     <c>Message</c>. A lenient parse would turn an unrelated answer (for example <c>{}</c>) into a
    ///     "failed command" with no message. There are no redirects to follow on this channel: it is a raw
    ///     line-delimited TCP exchange, not HTTP.
    /// </remarks>
    /// <param name="line">The received line.</param>
    /// <param name="port">The port that answered, for the message.</param>
    /// <returns>The response.</returns>
    /// <exception cref="PuppetException">Thrown when the line is not exactly a control response.</exception>
    public static ControlResponse ParseResponse(string line, int port)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(["ExitCode", "Message", "Ok"], StringComparer.Ordinal) &&
                root.GetProperty("Ok").ValueKind is JsonValueKind.True or JsonValueKind.False &&
                root.GetProperty("ExitCode").ValueKind == JsonValueKind.Number &&
                root.GetProperty("ExitCode").TryGetInt32(out var exitCode) &&
                root.GetProperty("Message").ValueKind == JsonValueKind.String)
            {
                return new ControlResponse(root.GetProperty("Ok").GetBoolean(), exitCode, root.GetProperty("Message").GetString()!);
            }
        }
        catch (JsonException)
        {
            // Not JSON at all: rejected below like any other unexpected answer
        }

        throw new PuppetException(
            $"The service on port {port} did not answer like a Puppet session daemon; the session may be gone and its port reused.");
    }

    /// <summary>
    ///     Reads one line from a stream, enforcing <see cref="MaxLineLength"/>.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The line, or <c>null</c> when the peer closed the connection before sending one.</returns>
    /// <exception cref="PuppetException">Thrown when the line is longer than the limit.</exception>
    public static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[1];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken) == 1)
        {
            if (buffer[0] == '\n')
            {
                return builder.ToString();
            }

            builder.Append(buffer[0]);
            if (builder.Length > MaxLineLength)
            {
                throw new PuppetException("Control message too long.");
            }
        }

        // The stream ended: a partial line without a terminator is incomplete and discarded
        return null;
    }
}

/// <summary>
///     Sends one command to a session daemon and returns its answer.
/// </summary>
/// <remarks>
///     A new short-lived connection is used per command, which keeps the protocol stateless: there
///     is no connection to keep alive between the separate processes a script starts.
/// </remarks>
internal static class ControlClient
{
    /// <summary>
    ///     Sends a request to the daemon listening on a loopback port.
    /// </summary>
    /// <param name="port">The daemon's control port.</param>
    /// <param name="request">The request to send.</param>
    /// <param name="timeout">How long to wait for the answer overall.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    /// <returns>The daemon's response.</returns>
    /// <exception cref="PuppetException">
    ///     Thrown when the daemon cannot be reached, closes the connection without answering, or
    ///     does not answer within the timeout.
    /// </exception>
    public static async Task<ControlResponse> SendAsync(
        int port,
        ControlRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, timeoutSource.Token);
            var stream = client.GetStream();

            // UTF-8 without a byte-order mark so the server's reader sees clean JSON
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            await writer.WriteAsync((ControlProtocol.Serialize(request) + "\n").AsMemory(), timeoutSource.Token);
            await writer.FlushAsync(timeoutSource.Token);

            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var line = await ControlProtocol.ReadLineAsync(reader, timeoutSource.Token);
            return line is null
                ? throw new PuppetException("The session daemon closed the connection without answering.")
                : ControlProtocol.ParseResponse(line, port);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PuppetException($"The session daemon did not answer within {timeout.TotalSeconds:0} seconds.");
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            throw new PuppetException($"Cannot reach the session daemon on port {port}: {ex.Message}", ex);
        }
    }
}
