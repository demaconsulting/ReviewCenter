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
///     How a health request was answered.
/// </summary>
internal enum HealthState
{
    /// <summary>
    ///     The server answered the exact ReviewCenter health response.
    /// </summary>
    Healthy,

    /// <summary>
    ///     Nothing usable answered yet (connection refused, request hung, or a non-success status); try again.
    /// </summary>
    NotReady,

    /// <summary>
    ///     A success response arrived that is not the ReviewCenter health response; the answering service is not the expected server.
    /// </summary>
    Mismatch
}

/// <summary>
///     The classified answer to one health request.
/// </summary>
/// <param name="State">The classification.</param>
/// <param name="Detail">A short explanation for messages; empty when healthy.</param>
internal readonly record struct HealthProbe(HealthState State, string Detail);

/// <summary>
///     Starts the ReviewCenter server as a child process on a free loopback port and waits for it to be healthy.
/// </summary>
/// <remarks>
///     The server runs as a separate process (rather than inside the daemon) so the Puppet exercises
///     exactly what ships: the real executable, its real start-up path, and its real
///     <c>/health</c> endpoint. The random port avoids clashing with anything already listening and
///     lets several sessions run side by side. Stateless; each call starts an independent server.
/// </remarks>
internal sealed class ServerProcessRunner : IServerRunner
{
    /// <summary>
    ///     How long the server may take to become healthy.
    /// </summary>
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     The longest a single health request may take; a server that is up answers in milliseconds,
    ///     so a request that hangs means "not ready yet" and is simply repeated.
    /// </summary>
    private static readonly TimeSpan HealthRequestTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     How often the health endpoint is polled while the server starts.
    /// </summary>
    private static readonly TimeSpan HealthPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    ///     How long to wait for the server process to exit after each attempt to kill it; the operating
    ///     system normally ends a killed process within milliseconds, so exceeding this means it is stuck.
    /// </summary>
    private static readonly TimeSpan KillWaitTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How many trailing output lines are kept for the failure message.
    /// </summary>
    private const int KeptOutputLines = 20;

    /// <summary>
    ///     The exact body the ReviewCenter health endpoint answers with; the server defines the same value
    ///     (the Puppet does not reference the server assembly, so it is repeated here).
    /// </summary>
    internal const string HealthyText = "Healthy";

    /// <summary>
    ///     How many characters of an unexpected health response body appear in the failure message.
    /// </summary>
    private const int MaxShownBodyLength = 80;

    /// <summary>
    ///     Starts the server and waits until <c>/health</c> answers.
    /// </summary>
    /// <param name="serverPath">The path of the ReviewCenter server assembly.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The running server; disposing it stops the process tree.</returns>
    /// <exception cref="PuppetException">Thrown when the server exits early or is not healthy in time.</exception>
    public async Task<IRunningServer> StartAsync(string serverPath, CancellationToken cancellationToken)
    {
        var port = GetFreePort();
        var url = $"http://127.0.0.1:{port}";

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
            if (!process.Start())
            {
                throw new PuppetException("Could not start the ReviewCenter server process.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            process.Dispose();
            throw new PuppetException($"Could not start the ReviewCenter server ('dotnet' not found?): {ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var server = new RunningServer(process, url);

        try
        {
            await WaitUntilHealthyAsync(process, url, output, cancellationToken);
            return server;
        }
        catch (Exception startFailure)
        {
            // Never leave a half-started server behind, and never hide that it could not be removed
            try
            {
                await server.DisposeAsync();
            }
            catch (PuppetException cleanupFailure)
            {
                throw new PuppetException($"{startFailure.Message} {cleanupFailure.Message}", startFailure);
            }

            throw;
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
    ///     Polls <c>/health</c> until it answers success, the process exits, or the time runs out.
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
        using var http = CreateHealthClient();
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

            var probe = await ProbeHealthAsync(http, url, cancellationToken);
            switch (probe.State)
            {
                case HealthState.Healthy:
                    return;
                case HealthState.Mismatch:
                    // Something answered, but not the ReviewCenter health endpoint (for example another process
                    // that took the port between choosing it and the server binding it): fail instead of driving it
                    throw new PuppetException(
                        $"The service answering at {url} is not the ReviewCenter server: {probe.Detail}.{Environment.NewLine}" +
                        string.Join(Environment.NewLine, output));
            }

            await Task.Delay(HealthPollInterval, cancellationToken);
        }

        throw new PuppetException(
            $"The ReviewCenter server at {url} did not become healthy within {HealthTimeout.TotalSeconds:0} seconds.{Environment.NewLine}" +
            string.Join(Environment.NewLine, output));
    }

    /// <summary>
    ///     Creates the client used for readiness probes.
    /// </summary>
    /// <remarks>
    ///     Redirects are not followed: the probe must judge the answer of the process on the chosen
    ///     port itself. Following a redirect would let another process that won the port race send the
    ///     probe to any endpoint that answers <c>200 Healthy</c> (and cause an unintended request).
    /// </remarks>
    /// <returns>A client with a short request timeout and automatic redirects disabled.</returns>
    internal static HttpClient CreateHealthClient()
    {
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = HealthRequestTimeout };
    }

    /// <summary>
    ///     Requests <c>/health</c> once and decides whether the exact ReviewCenter health answer came back.
    /// </summary>
    /// <remarks>
    ///     A success status alone is not proof: the port is chosen before the server binds it, so another
    ///     local process could answer instead, and a misconfigured endpoint could answer with a success
    ///     status and no content. Only status 200 with the body <see cref="HealthyText"/> counts as healthy.
    ///     A non-success status or a refused connection means "not ready yet"; any other answer is a
    ///     mismatch.
    /// </remarks>
    /// <param name="http">The client to use.</param>
    /// <param name="url">The server base URL.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The classified answer.</returns>
    internal static async Task<HealthProbe> ProbeHealthAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync($"{url}/health", cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                // A redirect is never the ReviewCenter health answer: something else owns the port
                return new HealthProbe(
                    HealthState.Mismatch,
                    $"/health answered with the redirect status {(int)response.StatusCode} instead of 200 with '{HealthyText}'");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new HealthProbe(HealthState.NotReady, $"status {(int)response.StatusCode}");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK && body == HealthyText)
            {
                return new HealthProbe(HealthState.Healthy, string.Empty);
            }

            var shown = body.Length > MaxShownBodyLength ? body[..MaxShownBodyLength] + "..." : body;
            return new HealthProbe(
                HealthState.Mismatch,
                $"/health answered status {(int)response.StatusCode} with body '{shown}' instead of 200 with '{HealthyText}'");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Not accepting connections yet (or a request that hung); try again shortly
            return new HealthProbe(HealthState.NotReady, ex.Message);
        }
    }

    /// <summary>
    ///     The handle to a started server process.
    /// </summary>
    private sealed class RunningServer : IRunningServer
    {
        /// <summary>
        ///     The server process.
        /// </summary>
        private readonly Process _process;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RunningServer"/> class.
        /// </summary>
        /// <param name="process">The started process; ownership transfers to this instance.</param>
        /// <param name="url">The server base URL.</param>
        public RunningServer(Process process, string url)
        {
            _process = process;
            Url = url;
            ProcessId = process.Id;
            StartTimeUtc = process.StartTime.ToUniversalTime();
        }

        /// <inheritdoc />
        public string Url { get; }

        /// <inheritdoc />
        public int ProcessId { get; }

        /// <inheritdoc />
        public DateTime StartTimeUtc { get; }

        /// <summary>
        ///     Stops the server process tree and releases the handle.
        /// </summary>
        /// <returns>A task that completes when the process has exited.</returns>
        /// <exception cref="PuppetException">
        ///     Thrown when the process is still running after it was killed and the kill was repeated.
        ///     The handle is released either way, but the failure must not be mistaken for a clean stop.
        /// </exception>
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!HasExited())
                {
                    await StopAsync(
                        $"The ReviewCenter server process {ProcessId}",
                        KillTree,
                        async wait =>
                        {
                            try
                            {
                                await _process.WaitForExitAsync(CancellationToken.None).WaitAsync(wait);
                                return true;
                            }
                            catch (TimeoutException)
                            {
                                return false;
                            }
                        });
                }
            }
            finally
            {
                _process.Dispose();
            }
        }

        /// <summary>
        ///     Checks whether the process has already exited.
        /// </summary>
        /// <returns><c>true</c> when it has exited.</returns>
        private bool HasExited()
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                // No process is associated with the handle any more
                return true;
            }
        }

        /// <summary>
        ///     Asks the operating system to kill the process and its children.
        /// </summary>
        private void KillTree()
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone, or refused; whether it worked is decided by waiting for the exit, not here
            }
        }
    }

    /// <summary>
    ///     Kills a process tree, waits for the exit, and tries again once before reporting failure.
    /// </summary>
    /// <remarks>
    ///     A kill can fail or take effect late (a stuck child, a handle held open by the operating system), and
    ///     reporting a clean stop while the server still holds its port would hand the next session a
    ///     broken environment. The second attempt covers a late-started child; if the process still has
    ///     not exited, the failure is raised so the caller does not claim the cleanup is complete.
    /// </remarks>
    /// <param name="description">Names the process in the failure message.</param>
    /// <param name="killTree">Kills the process and its children; must not throw.</param>
    /// <param name="waitForExit">Waits up to the given time and returns whether the process exited.</param>
    /// <returns>A task that completes when the process has exited.</returns>
    /// <exception cref="PuppetException">Thrown when the process has not exited after two kill attempts.</exception>
    internal static async Task StopAsync(string description, Action killTree, Func<TimeSpan, Task<bool>> waitForExit)
    {
        // First attempt: kill the whole tree and give the operating system time to finish the job
        killTree();
        if (await waitForExit(KillWaitTimeout))
        {
            return;
        }

        // Escalate: repeat the tree kill, which also catches children started in the meantime
        killTree();
        if (await waitForExit(KillWaitTimeout))
        {
            return;
        }

        throw new PuppetException(
            $"{description} did not exit within {KillWaitTimeout.TotalSeconds:0} seconds of being killed twice; it may still be running and must be stopped manually.");
    }
}
