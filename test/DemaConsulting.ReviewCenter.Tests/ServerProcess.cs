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

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     Helpers for tests that start a real ReviewCenter web server on a loopback port.
/// </summary>
/// <remarks>
///     Tests must not assume a fixed port (parallel runs and other software may hold it), so the
///     helper asks the operating system for a free port and polls the health endpoint until the
///     server answers. Instances are not thread-safe; each test uses its own.
/// </remarks>
internal sealed class ServerProcess : IDisposable
{
    /// <summary>
    ///     The running server process.
    /// </summary>
    private readonly Process _process;

    /// <summary>
    ///     The console output captured so far; guarded by locking on the builder itself.
    /// </summary>
    private readonly System.Text.StringBuilder _output = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="ServerProcess"/> class from a started process.
    /// </summary>
    /// <param name="process">The started server process; ownership transfers to this instance.</param>
    /// <param name="baseUrl">The URL the server listens on.</param>
    private ServerProcess(Process process, string baseUrl)
    {
        _process = process;
        BaseUrl = baseUrl;
    }

    /// <summary>
    ///     Gets the base URL (no trailing slash) the server was asked to listen on.
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>
    ///     Asks the operating system for a currently unused loopback TCP port.
    /// </summary>
    /// <returns>A port number that was free at the time of the call.</returns>
    public static int GetFreePort()
    {
        // Bind to port 0 so the OS picks, read it back, then release it for the server to use
        using var listener = new TcpListener(IPAddress.Loopback, 0);
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
    ///     Starts <c>dotnet DemaConsulting.ReviewCenter.dll serve --urls ...</c> on a free port.
    /// </summary>
    /// <param name="extraArguments">Additional arguments appended after the standard ones.</param>
    /// <returns>The started (but not necessarily ready) server.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the process cannot be started.</exception>
    public static ServerProcess Start(params string[] extraArguments)
    {
        var dllPath = Path.Combine(AppContext.BaseDirectory, "DemaConsulting.ReviewCenter.dll");
        var baseUrl = $"http://127.0.0.1:{GetFreePort()}";

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(dllPath);
        startInfo.ArgumentList.Add("serve");
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(baseUrl);
        foreach (var argument in extraArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start server");

        // Capture the output (and keep draining it) so the server never blocks on a full pipe buffer
        var serverProcess = new ServerProcess(process, baseUrl);
        process.OutputDataReceived += (_, e) => serverProcess.AppendOutput(e.Data);
        process.ErrorDataReceived += (_, e) => serverProcess.AppendOutput(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return serverProcess;
    }

    /// <summary>
    ///     Gets the console output (standard output and standard error) the server has written so far.
    /// </summary>
    public string Output
    {
        get
        {
            lock (_output)
            {
                return _output.ToString();
            }
        }
    }

    /// <summary>
    ///     Asks the server to stop gracefully by sending it the termination signal, the way a process
    ///     supervisor or container runtime does. Only supported on Unix-like systems.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">Thrown on Windows, which has no equivalent signal for a child process.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the signal could not be sent.</exception>
    public void RequestGracefulStop()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("A graceful stop signal cannot be sent to a child process on Windows.");
        }

        var startInfo = new ProcessStartInfo("kill") { UseShellExecute = false, CreateNoWindow = true };
        startInfo.ArgumentList.Add("-TERM");
        startInfo.ArgumentList.Add(_process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var kill = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start kill");
        kill.WaitForExit();
        if (kill.ExitCode != 0)
        {
            throw new InvalidOperationException($"kill exited with code {kill.ExitCode}");
        }
    }

    /// <summary>
    ///     Waits for the server process to exit by itself.
    /// </summary>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <returns>The exit code, or <c>null</c> when the process was still running after the timeout.</returns>
    public int? WaitForExit(TimeSpan timeout)
    {
        return _process.WaitForExit(timeout) ? _process.ExitCode : null;
    }

    /// <summary>
    ///     Appends a line of console output.
    /// </summary>
    /// <param name="line">The line, or <c>null</c> at the end of the stream.</param>
    private void AppendOutput(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_output)
        {
            _output.AppendLine(line);
        }
    }

    /// <summary>
    ///     Polls the health endpoint until it answers 200 with the exact body <c>Healthy</c> or the timeout elapses.
    /// </summary>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <returns><c>true</c> when the server became healthy in time.</returns>
    public async Task<bool> WaitUntilHealthyAsync(TimeSpan timeout)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !_process.HasExited)
        {
            try
            {
                // Same contract as the production probe: status 200 with exactly the body "Healthy"
                using var response = await client.GetAsync($"{BaseUrl}/health");
                if (response.StatusCode == System.Net.HttpStatusCode.OK &&
                    await response.Content.ReadAsStringAsync() == "Healthy")
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet; keep polling
            }
            catch (TaskCanceledException)
            {
                // Request timed out while the server was still starting; keep polling
            }

            await Task.Delay(100);
        }

        return false;
    }

    /// <summary>
    ///     Stops the server process and releases its resources.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited between the check and the kill
        }

        _process.Dispose();
    }
}
