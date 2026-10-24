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
using System.Text.Json;

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     One command from the client to the daemon.
/// </summary>
/// <param name="Command">The command name, for example <c>click</c>.</param>
/// <param name="Args">The positional arguments (file paths already absolute).</param>
/// <param name="Options">The <c>--name value</c> options; a flag has the value <c>true</c>.</param>
internal sealed record PuppetRequest(string Command, string[] Args, Dictionary<string, string> Options);

/// <summary>
///     The daemon's answer to one command.
/// </summary>
/// <param name="Ok">Whether the command succeeded.</param>
/// <param name="Output">The result, or the one-line error message when <paramref name="Ok"/> is false.</param>
/// <param name="Url">The current page URL (empty when unknown).</param>
/// <param name="Title">The current page title (empty when unknown).</param>
/// <param name="Events">Console errors, page errors, and failed requests since the previous command.</param>
internal sealed record PuppetResponse(bool Ok, string Output, string Url, string Title, string[] Events);

/// <summary>
///     The content of the control file: where the daemon listens, or why it could not start.
/// </summary>
/// <param name="Port">The loopback port, or <c>null</c> when start-up failed.</param>
/// <param name="BaseUrl">The base URL for relative <c>goto</c> paths, or <c>null</c>.</param>
/// <param name="Error">The start-up failure message, or <c>null</c>.</param>
internal sealed record ControlInfo(int? Port, string? BaseUrl, string? Error);

/// <summary>
///     The control file and the loopback TCP protocol between the Puppet client and its browser daemon.
/// </summary>
/// <remarks>
///     One connection carries one command: the client sends one JSON line and the daemon answers with one
///     JSON line. There is no authentication; the daemon listens on 127.0.0.1 only.
/// </remarks>
internal static class ControlChannel
{
    /// <summary>
    ///     The JSON settings shared by both directions and the control file.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    ///     Gets the well-known control file path, shared by every Puppet command of this user.
    /// </summary>
    public static string DefaultControlFile => Path.Combine(Path.GetTempPath(), "reviewcenter-puppet.json");

    /// <summary>
    ///     Reads the control file.
    /// </summary>
    /// <param name="file">The control file path.</param>
    /// <returns>The content, or <c>null</c> when the file is absent or unreadable.</returns>
    public static ControlInfo? Read(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<ControlInfo>(File.ReadAllText(file), Json);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Absent, half written, or garbage: all mean "no usable control file"
            return null;
        }
    }

    /// <summary>
    ///     Writes the control file.
    /// </summary>
    /// <param name="file">The control file path.</param>
    /// <param name="info">The content.</param>
    public static void Write(string file, ControlInfo info)
    {
        File.WriteAllText(file, JsonSerializer.Serialize(info, Json));
    }

    /// <summary>
    ///     Sends one command to the daemon and waits for its answer.
    /// </summary>
    /// <param name="port">The daemon's loopback port.</param>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    /// <returns>The daemon's response.</returns>
    /// <exception cref="IOException">Thrown when the daemon closes the connection without answering.</exception>
    public static async Task<PuppetResponse> SendAsync(int port, PuppetRequest request, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream);
        await using var writer = new StreamWriter(stream) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, Json).AsMemory(), cancellationToken);
        var line = await reader.ReadLineAsync(cancellationToken) ?? throw new IOException("The Puppet daemon closed the connection.");
        return JsonSerializer.Deserialize<PuppetResponse>(line, Json) ?? throw new IOException("The Puppet daemon sent an empty answer.");
    }

    /// <summary>
    ///     Serves commands one at a time until cancelled.
    /// </summary>
    /// <param name="listener">The started listener.</param>
    /// <param name="handler">Produces the answer for a command.</param>
    /// <param name="cancellationToken">Ends the loop; checked after each answer is written.</param>
    /// <returns>A task that completes when the loop has ended.</returns>
    public static async Task ServeAsync(
        TcpListener listener,
        Func<PuppetRequest, Task<PuppetResponse>> handler,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                try
                {
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream);
                    await using var writer = new StreamWriter(stream) { AutoFlush = true };
                    var request = JsonSerializer.Deserialize<PuppetRequest>(await reader.ReadLineAsync(CancellationToken.None) ?? "null", Json);
                    if (request is not null)
                    {
                        await writer.WriteLineAsync(JsonSerializer.Serialize(await handler(request), Json).AsMemory(), CancellationToken.None);
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    // A client that went away or sent garbage must not stop the daemon
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped while waiting for the next connection
        }
    }

    /// <summary>
    ///     Checks whether a Puppet daemon answers on the port.
    /// </summary>
    /// <param name="port">The port from a control file.</param>
    /// <returns><c>true</c> when the daemon answered a ping within two seconds; <c>false</c> for a stale file.</returns>
    public static async Task<bool> IsAnsweringAsync(int port)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var response = await SendAsync(port, new PuppetRequest("ping", [], []), timeout.Token);
            return response.Output == "pong";
        }
        catch (Exception ex) when (ex is SocketException or IOException or JsonException or OperationCanceledException)
        {
            return false;
        }
    }
}
