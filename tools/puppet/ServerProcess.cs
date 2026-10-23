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

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     A started ReviewCenter server.
/// </summary>
/// <remarks>
///     Disposing it stops the server process and its children.
/// </remarks>
internal interface IRunningServer : IAsyncDisposable
{
    /// <summary>
    ///     Gets the base URL of the server, without a trailing slash.
    /// </summary>
    string Url { get; }
}

/// <summary>
///     Starts the ReviewCenter server as a child process on a free loopback port and waits for it to be healthy.
/// </summary>
/// <remarks>
///     The server runs as a separate process so the Puppet exercises exactly what ships: the real
///     executable and its real <c>/health</c> endpoint. Stopping is best effort: the process tree is
///     killed once and the exit is awaited for a bounded time.
/// </remarks>
internal sealed class ServerProcess : IRunningServer
{
    /// <summary>
    ///     How long the server may take to become healthy.
    /// </summary>
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How long to wait for the process to exit after it was killed.
    /// </summary>
    private static readonly TimeSpan KillWaitTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How many trailing output lines are kept for the failure message.
    /// </summary>
    private const int KeptOutputLines = 20;

    /// <summary>
    ///     The exact body the ReviewCenter health endpoint answers with. The Puppet does not reference the
    ///     server assembly, so the value is repeated here.
    /// </summary>
    private const string HealthyText = "Healthy";

    /// <summary>
    ///     The server process.
    /// </summary>
    private readonly Process _process;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ServerProcess"/> class.
    /// </summary>
    /// <param name="process">The started process; ownership transfers to this instance.</param>
    /// <param name="url">The server base URL.</param>
    private ServerProcess(Process process, string url)
    {
        _process = process;
        Url = url;
    }

    /// <inheritdoc />
    public string Url { get; }

    /// <summary>
    ///     Starts the server and waits until <c>/health</c> answers.
    /// </summary>
    /// <param name="serverPath">The path of the ReviewCenter server assembly.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The running server; disposing it stops the process tree.</returns>
    /// <exception cref="PuppetException">Thrown when the server cannot be started or is not healthy in time.</exception>
    public static async Task<IRunningServer> StartAsync(string serverPath, CancellationToken cancellationToken)
    {
        var url = $"http://127.0.0.1:{GetFreePort()}";

        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(serverPath))!
        };
        startInfo.ArgumentList.Add(serverPath);
        startInfo.ArgumentList.Add("serve");
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(url);

        var output = new ConcurrentQueue<string>();
        var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => Remember(output, e.Data);
        process.ErrorDataReceived += (_, e) => Remember(output, e.Data);

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            process.Dispose();
            throw new PuppetException($"Could not start the ReviewCenter server ('dotnet' not found?): {ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var server = new ServerProcess(process, url);

        try
        {
            await WaitUntilHealthyAsync(process, url, output, cancellationToken);
            return server;
        }
        catch
        {
            // Never leave a half-started server behind; a failed stop must not hide the start-up error
            try
            {
                await server.DisposeAsync();
            }
            catch (TimeoutException)
            {
                // Best effort
            }

            throw;
        }
    }

    /// <summary>
    ///     Stops the server process tree.
    /// </summary>
    /// <returns>A task that completes when the process has exited.</returns>
    /// <exception cref="TimeoutException">Thrown when the process is still running after the wait.</exception>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync(CancellationToken.None).WaitAsync(KillWaitTimeout);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited on its own in the meantime
        }
        finally
        {
            _process.Dispose();
        }
    }

    /// <summary>
    ///     Asks the operating system for a free loopback port.
    /// </summary>
    /// <returns>A port that was free at the time of the call.</returns>
    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    ///     Keeps a bounded tail of the server's output for diagnostics.
    /// </summary>
    /// <param name="output">The queue holding the tail.</param>
    /// <param name="line">The new line, or <c>null</c> at end of stream.</param>
    private static void Remember(ConcurrentQueue<string> output, string? line)
    {
        if (line is null)
        {
            return;
        }

        output.Enqueue(line);
        while (output.Count > KeptOutputLines && output.TryDequeue(out _))
        {
            // Drop the oldest lines
        }
    }

    /// <summary>
    ///     Polls <c>/health</c> until it answers <c>200 Healthy</c>, the process exits, or the time runs out.
    /// </summary>
    /// <param name="process">The server process.</param>
    /// <param name="url">The server base URL.</param>
    /// <param name="output">The captured output tail.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the server is healthy.</returns>
    /// <exception cref="PuppetException">Thrown when the server exits or does not become healthy in time.</exception>
    private static async Task WaitUntilHealthyAsync(
        Process process,
        string url,
        ConcurrentQueue<string> output,
        CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + HealthTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new PuppetException(
                    $"The ReviewCenter server exited with code {process.ExitCode} during start-up.{Environment.NewLine}" +
                    string.Join(Environment.NewLine, output));
            }

            if (await IsHealthyAsync(http, url, cancellationToken))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        throw new PuppetException(
            $"The ReviewCenter server at {url} did not become healthy within {HealthTimeout.TotalSeconds:0} seconds.{Environment.NewLine}" +
            string.Join(Environment.NewLine, output));
    }

    /// <summary>
    ///     Requests <c>/health</c> once.
    /// </summary>
    /// <param name="http">The client to use.</param>
    /// <param name="url">The server base URL.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>true</c> when the server answered status 200 with the body <c>Healthy</c>.</returns>
    private static async Task<bool> IsHealthyAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync($"{url}/health", cancellationToken);
            return response.StatusCode == HttpStatusCode.OK &&
                   await response.Content.ReadAsStringAsync(cancellationToken) == HealthyText;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Not accepting connections yet (or a request that hung); try again shortly
            return false;
        }
    }
}
