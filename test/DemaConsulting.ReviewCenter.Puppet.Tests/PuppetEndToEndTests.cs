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
using System.Reflection;

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     End-to-end test: start the real server, start Puppet, drive the hello page, and stop Puppet.
/// </summary>
/// <remarks>
///     This test is requirement-linked evidence for the Puppet system and daemon requirements. Locally it is
///     skipped with a clear reason when Chromium is not installed. CI installs Chromium and sets
///     <c>REVIEWCENTER_REQUIRE_CHROMIUM=true</c>, which turns a missing or broken browser into a failure so
///     the evidence can never be skipped silently.
/// </remarks>
[Collection("Sequential")]
public class PuppetEndToEndTests
{
    /// <summary>
    ///     The environment variable that turns a missing Chromium from a skip into a failure.
    /// </summary>
    private const string RequireChromiumVariable = "REVIEWCENTER_REQUIRE_CHROMIUM";

    /// <summary>
    ///     Test that Puppet can be started, driven one command at a time, and stopped again.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task PuppetEndToEnd_StartDriveStop_HelloAndCounter_WorksAgainstRealServer()
    {
        // Arrange: the real server, and a private control file and screenshot folder
        var cancellation = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("puppet-tests-");
        var controlFile = Path.Combine(directory.FullName, "control.json");
        var shot = Path.Combine(directory.FullName, "home.png");
        using var server = await TestServer.StartAsync(cancellation);

        async Task<(int Code, string Output, string Error)> Run(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            return (await PuppetProgram.RunAsync(args, output, error, controlFile, cancellation), output.ToString(), error.ToString());
        }

        try
        {
            // Act and assert: start; a missing browser is a skip locally and a failure where Chromium is required
            var start = await Run("start", "--url", server.Url, "--viewport", "1280x800");
            if (start.Code != 0 && start.Error.Contains("Chromium", StringComparison.Ordinal))
            {
                Assert.False(
                    string.Equals(Environment.GetEnvironmentVariable(RequireChromiumVariable), "true", StringComparison.OrdinalIgnoreCase),
                    $"{RequireChromiumVariable} is true but Chromium is unavailable: {start.Error}");
                Assert.Skip(start.Error);
            }

            Assert.True(start.Code == 0, start.Error);
            Assert.Equal(1, (await Run("start")).Code);

            // Act and assert: the page shows the hello button
            var go = await Run("goto", "/");
            Assert.Equal(0, go.Code);
            Assert.StartsWith("HTTP 200", go.Output, StringComparison.Ordinal);
            Assert.Equal("{\"a\":1}", (await Run("eval", "({a: 1})")).Output.Split('\n')[0].Trim());
            var snapshot = await Run("snapshot");
            Assert.Contains("testid=hello-button", snapshot.Output, StringComparison.Ordinal);
            Assert.Contains(server.Url, snapshot.Output, StringComparison.Ordinal);

            // Act and assert: a hover persists across separate commands, including a screenshot, until the mouse moves
            const string isHovered = "document.querySelector('[data-testid=hello-button]').matches(':hover')";
            Assert.Equal(0, (await Run("hover", "hello-button")).Code);
            Assert.Equal(0, (await Run("screenshot", Path.Combine(directory.FullName, "hover.png"))).Code);
            Assert.StartsWith("true", (await Run("eval", isHovered)).Output, StringComparison.Ordinal);
            Assert.Equal(0, (await Run("mouse-move", "0", "0")).Code);
            Assert.StartsWith("false", (await Run("eval", isHovered)).Output, StringComparison.Ordinal);

            // Act and assert: two clicks, then wait for the asynchronous update instead of sleeping
            Assert.Equal(0, (await Run("click", "counter-button")).Code);
            Assert.Equal(0, (await Run("click", "counter-button")).Code);
            Assert.Equal(0, (await Run("wait", "text=Count: 2")).Code);
            Assert.StartsWith("2", (await Run("text", "counter-value")).Output, StringComparison.Ordinal);

            // Act and assert: a missing element fails fast with a one-line message and exit code 1
            var missing = await Run("click", "no-such-element");
            Assert.Equal(1, missing.Code);
            Assert.Contains("no-such-element", missing.Error, StringComparison.Ordinal);

            // Act and assert: the screenshot is a PNG
            Assert.Equal(0, (await Run("screenshot", shot)).Code);
            var png = await File.ReadAllBytesAsync(shot, cancellation);
            Assert.True(png.Length > 1000);
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
            Assert.Equal(0, (await Run("viewport", "800x600")).Code);
            Assert.Equal(0, (await Run("theme", "dark")).Code);
            Assert.StartsWith("true", (await Run("eval", "matchMedia('(prefers-color-scheme: dark)').matches")).Output, StringComparison.Ordinal);
            Assert.Equal(0, (await Run("console")).Code);

            // Act and assert: stop succeeds and removes the control file; a second stop finds nothing running
            Assert.Equal(0, (await Run("stop")).Code);
            Assert.False(File.Exists(controlFile));
            Assert.Equal(2, (await Run("stop")).Code);
        }
        finally
        {
            // A failing test must not leave a browser behind; this is a no-op after a successful stop
            await Run("stop");
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    ///     The ReviewCenter server running as a separate process on a free loopback port.
    /// </summary>
    private sealed class TestServer : IDisposable
    {
        private readonly Process _process;

        private TestServer(Process process, string url)
        {
            _process = process;
            Url = url;
        }

        /// <summary>
        ///     Gets the base URL of the server, without a trailing slash.
        /// </summary>
        public string Url { get; }

        /// <summary>
        ///     Starts the server and waits until <c>/health</c> answers <c>Healthy</c>.
        /// </summary>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>The running server; disposing it kills the process tree.</returns>
        public static async Task<TestServer> StartAsync(CancellationToken cancellationToken)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            listener.Stop();

            var dll = typeof(PuppetEndToEndTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "ServerDllPath").Value!;
            var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(dll)!
            };
            info.ArgumentList.Add(dll);
            info.ArgumentList.Add("serve");
            info.ArgumentList.Add("--urls");
            info.ArgumentList.Add(url);
            var process = Process.Start(info)!;
            var server = new TestServer(process, url);

            // Drain the output so the server never blocks on a full pipe
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            for (var attempt = 0; attempt < 300 && !process.HasExited; attempt++)
            {
                try
                {
                    if (await http.GetStringAsync($"{url}/health", cancellationToken) == "Healthy")
                    {
                        return server;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    // Not accepting connections yet
                }

                await Task.Delay(100, cancellationToken);
            }

            server.Dispose();
            throw new InvalidOperationException($"The ReviewCenter server at {url} did not become healthy.");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _process.Kill(entireProcessTree: true);
            _process.Dispose();
        }
    }
}
