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
using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     The daemon side of the control channel: accepts commands on a loopback port and dispatches them.
/// </summary>
/// <remarks>
///     The channel only ever listens on the loopback interface and requires a random per-session
///     token on every request, so another local user or a web page cannot drive the browser. Requests
///     are handled one at a time because the session owns a single browser page; this also gives
///     scripts the ordering they expect. Not thread-safe: <see cref="RunAsync"/> must be called once.
/// </remarks>
internal sealed class ControlServer : IDisposable
{
    /// <summary>
    ///     How long a connected client may take to send its request line before the connection is dropped.
    /// </summary>
    private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     The expected token, as bytes for constant-time comparison.
    /// </summary>
    private readonly byte[] _tokenBytes;

    /// <summary>
    ///     Executes one authenticated request.
    /// </summary>
    private readonly Func<ControlRequest, CancellationToken, Task<ControlResponse>> _handler;

    /// <summary>
    ///     The loopback listener.
    /// </summary>
    private readonly TcpListener _listener;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ControlServer"/> class and starts listening.
    /// </summary>
    /// <param name="token">The shared secret clients must present.</param>
    /// <param name="handler">Executes an authenticated request and produces the response.</param>
    public ControlServer(string token, Func<ControlRequest, CancellationToken, Task<ControlResponse>> handler)
    {
        _tokenBytes = Encoding.UTF8.GetBytes(token);
        _handler = handler;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    /// <summary>
    ///     Gets the loopback port the server listens on.
    /// </summary>
    public int Port { get; }

    /// <summary>
    ///     Generates a new random session token.
    /// </summary>
    /// <returns>A 256-bit random value as hexadecimal text.</returns>
    public static string CreateToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    /// <summary>
    ///     Accepts and serves requests until cancelled or the idle timeout elapses.
    /// </summary>
    /// <param name="idleTimeout">How long to wait for a connection before giving up.</param>
    /// <param name="cancellationToken">Stops the loop (for example after a <c>stop</c> command).</param>
    /// <returns><c>true</c> when the loop ended because nothing connected within the idle timeout.</returns>
    public async Task<bool> RunAsync(TimeSpan idleTimeout, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var acceptSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            acceptSource.CancelAfter(idleTimeout);

            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(acceptSource.Token);
            }
            catch (OperationCanceledException)
            {
                // Either a stop request (token cancelled) or the idle timeout (only the linked source)
                return !cancellationToken.IsCancellationRequested;
            }

            using (client)
            {
                await ServeClientAsync(client, cancellationToken);
            }
        }

        return false;
    }

    /// <summary>
    ///     Stops listening.
    /// </summary>
    public void Dispose()
    {
        _listener.Stop();
    }

    /// <summary>
    ///     Reads one request from a connected client, executes it, and writes the response.
    /// </summary>
    /// <remarks>
    ///     Anticipated failures and unexpected exceptions alike are turned into a failed response, so
    ///     one bad request never ends the serving loop; only <see cref="OutOfMemoryException"/> is left alone.
    /// </remarks>
    /// <param name="client">The connected client.</param>
    /// <param name="cancellationToken">Cancels command execution.</param>
    private async Task ServeClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        ControlResponse response;
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        try
        {
            using var readSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readSource.CancelAfter(RequestReadTimeout);
            var line = await ControlProtocol.ReadLineAsync(reader, readSource.Token);
            if (line is null)
            {
                return;
            }

            response = await ExecuteAsync(line, cancellationToken);
        }
        catch (PuppetException ex)
        {
            response = new ControlResponse(false, 1, ex.Message);
        }
        catch (OperationCanceledException)
        {
            response = new ControlResponse(false, 1, "The request was cancelled or took too long to arrive.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An unanticipated failure (a browser that died, a reset connection) must not kill the
            // daemon: answer this one request with the problem and keep serving the session
            response = new ControlResponse(false, 1, PuppetException.DescribeUnexpected(ex));
        }

        try
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            await writer.WriteAsync((ControlProtocol.Serialize(response) + "\n").AsMemory(), CancellationToken.None);
            await writer.FlushAsync(CancellationToken.None);
        }
        catch (IOException)
        {
            // The client went away before reading the answer; nothing useful can be done about it
        }
    }

    /// <summary>
    ///     Authenticates and executes a request line.
    /// </summary>
    /// <param name="line">The JSON request.</param>
    /// <param name="cancellationToken">Cancels command execution.</param>
    /// <returns>The response.</returns>
    private async Task<ControlResponse> ExecuteAsync(string line, CancellationToken cancellationToken)
    {
        var request = ControlProtocol.Deserialize<ControlRequest>(line);

        // Constant-time comparison so response timing cannot leak the token
        var presented = Encoding.UTF8.GetBytes(request.Token ?? string.Empty);
        if (!CryptographicOperations.FixedTimeEquals(presented, _tokenBytes))
        {
            return new ControlResponse(false, 1, "Rejected: invalid session token.");
        }

        return await _handler(request, cancellationToken);
    }
}
