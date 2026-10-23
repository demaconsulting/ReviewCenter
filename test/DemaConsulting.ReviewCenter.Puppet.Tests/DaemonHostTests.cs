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

using Microsoft.Playwright;

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     Unit tests for the <see cref="DaemonHost"/>, using a fake server and a fake browser.
/// </summary>
public class DaemonHostTests
{
    /// <summary>
    ///     Waits until the session file reports the wanted status.
    /// </summary>
    /// <param name="store">The session store.</param>
    /// <param name="id">The session id.</param>
    /// <param name="status">The status to wait for.</param>
    /// <returns>The session record once it has the status.</returns>
    private static async Task<SessionInfo> WaitForStatusAsync(SessionStore store, string id, SessionStatus status)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var info = store.TryRead(id);
            if (info?.Status == status)
            {
                return info;
            }

            await Task.Delay(25, Xunit.TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Session '{id}' never reached {status}.");
    }

    /// <summary>
    ///     Test that a started daemon publishes a ready session, executes a command, and stops cleanly on request.
    /// </summary>
    [Fact]
    public async Task DaemonHost_Run_FakeBrowserClickCommand_ReturnsOk()
    {
        // Arrange: a daemon over fakes, running in the background
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);
        var server = new FakeServerRunner();
        var browser = new FakeBrowserLauncher();
        var host = new DaemonHost(store, server, browser);
        var running = host.RunAsync("demo", "server.dll", Xunit.TestContext.Current.CancellationToken);
        var info = await WaitForStatusAsync(store, "demo", SessionStatus.Ready);

        // Act: click through the control channel, then ask it to stop
        var click = await ControlClient.SendAsync(
            info.ControlPort,
            new ControlRequest(info.ControlToken, "click", "hello-button", null, null, false, 5000),
            TimeSpan.FromSeconds(10),
            Xunit.TestContext.Current.CancellationToken);
        var stop = await ControlClient.SendAsync(
            info.ControlPort,
            new ControlRequest(info.ControlToken, "stop", null, null, null, false, 5000),
            TimeSpan.FromSeconds(10),
            Xunit.TestContext.Current.CancellationToken);
        var exitCode = await running;

        // Assert: the click reached the page, the daemon exited 0, tore down both resources and removed its file
        Assert.True(click.Ok);
        Assert.True(stop.Ok);
        Assert.Equal(0, exitCode);
        Assert.Equal(["click [data-testid=\"hello-button\"]"], browser.Page.Calls);
        Assert.Equal(1, browser.DisposedCount);
        Assert.Equal(1, server.DisposedCount);
        Assert.Null(store.TryRead("demo"));
    }

    /// <summary>
    ///     Test that a ready session records the server URL and the daemon's own process.
    /// </summary>
    [Fact]
    public async Task DaemonHost_Run_Ready_RecordsServerAndDaemonDetails()
    {
        // Arrange: a running daemon over fakes
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Xunit.TestContext.Current.CancellationToken);
        var running = new DaemonHost(store, new FakeServerRunner(), new FakeBrowserLauncher())
            .RunAsync("demo", "server.dll", cancellation.Token);

        // Act: wait for the ready record
        var info = await WaitForStatusAsync(store, "demo", SessionStatus.Ready);

        // Assert: details are recorded
        Assert.Equal("http://127.0.0.1:5999", info.ServerUrl);
        Assert.Equal(Environment.ProcessId, info.DaemonPid);
        Assert.False(string.IsNullOrEmpty(info.ControlToken));
        Assert.True(info.ControlPort > 0);

        // Cleanup: cancel to stop the daemon
        await cancellation.CancelAsync();
        Assert.Equal(0, await running);
    }

    /// <summary>
    ///     Test that a missing Chromium (browser launch failure) produces a failed session with the message, exit code 4,
    ///     and the server torn down.
    /// </summary>
    [Fact]
    public async Task DaemonHost_Run_BrowserLaunchFails_WritesFailedSessionAndReturnsFour()
    {
        // Arrange: a browser launcher that fails
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);
        var server = new FakeServerRunner();
        var browser = new FakeBrowserLauncher { Failure = new PuppetException("Chromium is not installed.") };
        var host = new DaemonHost(store, server, browser);

        // Act: run the daemon
        var exitCode = await host.RunAsync("demo", "server.dll", Xunit.TestContext.Current.CancellationToken);

        // Assert: exit 4, failed status with the message, server stopped
        var info = store.TryRead("demo");
        Assert.Equal(4, exitCode);
        Assert.Equal(SessionStatus.Failed, info!.Status);
        Assert.Contains("Chromium is not installed", info.Error, StringComparison.Ordinal);
        Assert.Equal(1, server.DisposedCount);
    }

    /// <summary>
    ///     Test that a server start failure produces a failed session and exit code 4.
    /// </summary>
    [Fact]
    public async Task DaemonHost_Run_ServerStartFails_WritesFailedSessionAndReturnsFour()
    {
        // Arrange: a server runner that fails
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);
        var server = new FakeServerRunner { Failure = new PuppetException("Server did not become healthy.") };
        var host = new DaemonHost(store, server, new FakeBrowserLauncher());

        // Act: run the daemon
        var exitCode = await host.RunAsync("demo", "server.dll", Xunit.TestContext.Current.CancellationToken);

        // Assert: exit 4 with the server's message
        Assert.Equal(4, exitCode);
        Assert.Contains("healthy", store.TryRead("demo")!.Error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the browser process reported by the browser session is recorded in the session file, so cleanup
    ///     can terminate it if the daemon dies.
    /// </summary>
    [Fact]
    public async Task DaemonHost_Run_Ready_RecordsBrowserProcessIdentity()
    {
        // Arrange: a browser that reports a process id and start time
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);
        var browserStart = new DateTime(2025, 3, 1, 12, 0, 10, DateTimeKind.Utc);
        var browser = new FakeBrowserLauncher { ReportedProcessId = 6161, ReportedStartTimeUtc = browserStart };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Xunit.TestContext.Current.CancellationToken);
        var running = new DaemonHost(store, new FakeServerRunner(), browser).RunAsync("demo", "server.dll", cancellation.Token);

        // Act: wait for the ready record
        var info = await WaitForStatusAsync(store, "demo", SessionStatus.Ready);

        // Assert: the browser identity and the server identity are both recorded
        Assert.Equal(6161, info.BrowserPid);
        Assert.Equal(browserStart, info.BrowserStartTimeUtc);
        Assert.True(info.ServerPid > 0);

        // Cleanup: cancel to stop the daemon
        await cancellation.CancelAsync();
        Assert.Equal(0, await running);
    }

    /// <summary>
    ///     Test that a server that cannot be stopped at shutdown is surfaced as a failed session and exit code 4
    ///     rather than reported as a clean shutdown.
    /// </summary>
    [Fact]
    public async Task DaemonHost_Run_ServerCannotBeStopped_ReportsFailureAndReturnsFour()
    {
        // Arrange: a server whose disposal fails, and a daemon that is told to stop straight away
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);
        var server = new FakeServerRunner { DisposeFailure = new PuppetException("The server process 1234 could not be stopped.") };
        var browser = new FakeBrowserLauncher();
        var running = new DaemonHost(store, server, browser).RunAsync("demo", "server.dll", Xunit.TestContext.Current.CancellationToken);
        var info = await WaitForStatusAsync(store, "demo", SessionStatus.Ready);

        // Act: stop the daemon through the control channel
        await ControlClient.SendAsync(
            info.ControlPort,
            new ControlRequest(info.ControlToken, "stop", null, null, null, false, 5000),
            TimeSpan.FromSeconds(10),
            Xunit.TestContext.Current.CancellationToken);
        var exitCode = await running;

        // Assert: exit 4, the failure is recorded in a kept session file, and the browser was still released
        var failed = store.TryRead("demo");
        Assert.Equal(4, exitCode);
        Assert.Equal(SessionStatus.Failed, failed!.Status);
        Assert.Contains("could not be stopped", failed.Error, StringComparison.Ordinal);
        Assert.Equal(1, browser.DisposedCount);
    }

    /// <summary>
    ///     Test that a server that exits after the first kill is stopped with a single kill.
    /// </summary>
    [Fact]
    public async Task DaemonHost_ServerStop_ExitsAfterFirstKill_KillsOnce()
    {
        // Arrange: a process that exits as soon as it is killed
        var kills = 0;

        // Act: stop it
        await ServerProcessRunner.StopAsync("server 1", () => kills++, _ => Task.FromResult(true));

        // Assert: one kill was enough
        Assert.Equal(1, kills);
    }

    /// <summary>
    ///     Test that a server that survives the first kill is killed again, and stops without error once it exits.
    /// </summary>
    [Fact]
    public async Task DaemonHost_ServerStop_ExitsAfterEscalation_KillsTwice()
    {
        // Arrange: a process that only exits after the second kill
        var kills = 0;

        // Act: stop it
        await ServerProcessRunner.StopAsync("server 1", () => kills++, _ => Task.FromResult(kills >= 2));

        // Assert: it was escalated exactly once
        Assert.Equal(2, kills);
    }

    /// <summary>
    ///     Test that a server that never exits is reported as a failure naming the process.
    /// </summary>
    [Fact]
    public async Task DaemonHost_ServerStop_NeverExits_ThrowsPuppetException()
    {
        // Arrange: a process that ignores every kill
        var kills = 0;

        // Act: try to stop it
        var exception = await Record.ExceptionAsync(
            () => ServerProcessRunner.StopAsync("server 1", () => kills++, _ => Task.FromResult(false)));

        // Assert: the failure is surfaced after the escalation, and says which process is still running
        Assert.IsType<PuppetException>(exception);
        Assert.Contains("server 1", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, kills);
    }

    /// <summary>
    ///     Answers a single HTTP request on a loopback port with a canned response, for the health probe tests.
    /// </summary>
    /// <param name="status">The status line text, such as <c>200 OK</c>.</param>
    /// <param name="body">The response body.</param>
    /// <param name="cancellationToken">Cancels the wait for the request.</param>
    /// <returns>The base URL the probe should use, and the task that completes once the request was answered.</returns>
    private static (string Url, Task Served) ServeOnce(string status, string body, CancellationToken cancellationToken)
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var url = $"http://127.0.0.1:{((System.Net.IPEndPoint)listener.LocalEndpoint).Port}";
        var served = Task.Run(
            async () =>
            {
                try
                {
                    using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream);
                    while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 })
                    {
                        // Skip the request line and headers
                    }

                    var length = System.Text.Encoding.UTF8.GetByteCount(body);
                    var response = $"HTTP/1.1 {status}\r\nContent-Type: text/plain\r\nContent-Length: {length}\r\nConnection: close\r\n\r\n{body}";
                    await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(response), cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                finally
                {
                    listener.Stop();
                }
            },
            cancellationToken);
        return (url, served);
    }

    /// <summary>
    ///     Test that only the exact ReviewCenter health answer (status 200, body <c>Healthy</c>) counts as healthy.
    /// </summary>
    [Fact]
    public async Task DaemonHost_HealthProbe_ExactHealthyAnswer_IsHealthy()
    {
        // Arrange: a listener that answers like the ReviewCenter server
        using var http = new HttpClient();
        var (url, served) = ServeOnce("200 OK", ServerProcessRunner.HealthyText, Xunit.TestContext.Current.CancellationToken);

        // Act: probe it
        var probe = await ServerProcessRunner.ProbeHealthAsync(http, url, Xunit.TestContext.Current.CancellationToken);
        await served;

        // Assert: healthy
        Assert.Equal(HealthState.Healthy, probe.State);
    }

    /// <summary>
    ///     Test that a success status with the wrong body, or with no body, is a mismatch (some other service on the
    ///     port) rather than a healthy ReviewCenter server.
    /// </summary>
    [Theory]
    [InlineData("200 OK", "Unhealthy")]
    [InlineData("200 OK", "")]
    [InlineData("204 No Content", "")]
    public async Task DaemonHost_HealthProbe_SuccessWithoutExactBody_IsMismatchNamingTheAnswer(string status, string body)
    {
        // Arrange: a listener that answers /health with a success status but not the health text
        using var http = new HttpClient();
        var (url, served) = ServeOnce(status, body, Xunit.TestContext.Current.CancellationToken);

        // Act: probe it
        var probe = await ServerProcessRunner.ProbeHealthAsync(http, url, Xunit.TestContext.Current.CancellationToken);
        await served;

        // Assert: a mismatch whose detail names the status and the expected text
        Assert.Equal(HealthState.Mismatch, probe.State);
        Assert.Contains(status[..3], probe.Detail, StringComparison.Ordinal);
        Assert.Contains(ServerProcessRunner.HealthyText, probe.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an unsuccessful status, or no listener at all, means the server is not ready yet.
    /// </summary>
    [Fact]
    public async Task DaemonHost_HealthProbe_ErrorStatusOrNoListener_IsNotReady()
    {
        // Arrange: a listener that answers 503, and a port nothing listens on
        using var http = new HttpClient();
        var (url, served) = ServeOnce("503 Service Unavailable", "starting", Xunit.TestContext.Current.CancellationToken);
        var closed = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        closed.Start();
        var closedUrl = $"http://127.0.0.1:{((System.Net.IPEndPoint)closed.LocalEndpoint).Port}";
        closed.Stop();

        // Act: probe both
        var unavailable = await ServerProcessRunner.ProbeHealthAsync(http, url, Xunit.TestContext.Current.CancellationToken);
        await served;
        var refused = await ServerProcessRunner.ProbeHealthAsync(http, closedUrl, Xunit.TestContext.Current.CancellationToken);

        // Assert: both are "not ready yet", so the caller keeps polling
        Assert.Equal(HealthState.NotReady, unavailable.State);
        Assert.Equal(HealthState.NotReady, refused.State);
    }

    /// <summary>
    ///     Test that a daemon with no commands shuts itself down after the idle timeout and cleans up.
    /// </summary>
    [Fact]
    public async Task DaemonHost_Run_Idle_ShutsDownAndRemovesSession()
    {
        // Arrange: a daemon with a very short idle timeout
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);
        var server = new FakeServerRunner();
        var browser = new FakeBrowserLauncher();
        var host = new DaemonHost(store, server, browser, TimeSpan.FromMilliseconds(200));

        // Act: run with nobody calling
        var exitCode = await host.RunAsync("demo", "server.dll", Xunit.TestContext.Current.CancellationToken);

        // Assert: clean exit, resources released, file removed
        Assert.Equal(0, exitCode);
        Assert.Equal(1, server.DisposedCount);
        Assert.Equal(1, browser.DisposedCount);
        Assert.Null(store.TryRead("demo"));
    }

    /// <summary>
    ///     Test that a clean shutdown leaves a replacement session alone: when another daemon published a record
    ///     for the same id while this one was shutting down, the replacement must survive.
    /// </summary>
    [Fact]
    public async Task DaemonHost_Run_ReplacementPublishedDuringShutdown_IsNotDeleted()
    {
        // Arrange: a server whose shutdown is the moment another daemon takes over the session id
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);
        var replacement = new SessionInfo(
            "demo", SessionStatus.Ready, 4321, "replacement-token", "http://127.0.0.1:5998", 999999,
            DateTime.UtcNow, 0, default, 0, default, null);
        var server = new FakeServerRunner { OnDispose = () => store.Write(replacement) };
        var browser = new FakeBrowserLauncher();
        var host = new DaemonHost(store, server, browser, TimeSpan.FromMilliseconds(200));

        // Act: run until the idle shutdown
        var exitCode = await host.RunAsync("demo", "server.dll", Xunit.TestContext.Current.CancellationToken);

        // Assert: a clean exit that kept the replacement record untouched
        Assert.Equal(0, exitCode);
        var remaining = store.TryRead("demo");
        Assert.NotNull(remaining);
        Assert.Equal("replacement-token", remaining.ControlToken);
        Assert.Equal(999999, remaining.DaemonPid);
    }

    /// <summary>
    ///     Test that a clean shutdown does not delete the record while a <c>start</c> holds the start lock for the id;
    ///     that start (or the next lookup) cleans it up instead.
    /// </summary>
    [Fact]
    public async Task DaemonHost_Run_StartLockHeldDuringShutdown_LeavesRecordForTheStart()
    {
        // Arrange: a start command that holds the lock for the id when the daemon shuts down
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);
        var server = new FakeServerRunner { OnDispose = () => Assert.True(store.TryReserve("demo")) };
        var browser = new FakeBrowserLauncher();
        var host = new DaemonHost(store, server, browser, TimeSpan.FromMilliseconds(200));

        // Act: run until the idle shutdown
        var exitCode = await host.RunAsync("demo", "server.dll", Xunit.TestContext.Current.CancellationToken);

        // Assert: still a clean exit, resources released, and the record was left alone
        Assert.Equal(0, exitCode);
        Assert.Equal(1, browser.DisposedCount);
        Assert.NotNull(store.TryRead("demo"));
        store.Release("demo");
    }

    /// <summary>
    ///     Test that a command failing unexpectedly (a crashed browser during a screenshot) is answered with a failed    ///     response, that the session keeps serving, and that a later stop still releases everything.
    /// </summary>
    [Fact]
    public async Task DaemonHost_Run_ScreenshotThrowsUnexpected_ReturnsFailureAndKeepsServing()
    {
        // Arrange: a running daemon whose page fails unexpectedly when taking a screenshot
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);
        var server = new FakeServerRunner();
        var browser = new FakeBrowserLauncher();
        browser.Page.UnexpectedFailure = new InvalidOperationException("browser exploded\nsecond line");
        var running = new DaemonHost(store, server, browser).RunAsync("demo", "server.dll", Xunit.TestContext.Current.CancellationToken);
        var info = await WaitForStatusAsync(store, "demo", SessionStatus.Ready);

        // Act: the failing screenshot, then a click, then stop
        var screenshot = await ControlClient.SendAsync(
            info.ControlPort,
            new ControlRequest(info.ControlToken, "screenshot", null, null, null, false, 5000),
            TimeSpan.FromSeconds(10),
            Xunit.TestContext.Current.CancellationToken);
        var click = await ControlClient.SendAsync(
            info.ControlPort,
            new ControlRequest(info.ControlToken, "click", "hello-button", null, null, false, 5000),
            TimeSpan.FromSeconds(10),
            Xunit.TestContext.Current.CancellationToken);
        var stop = await ControlClient.SendAsync(
            info.ControlPort,
            new ControlRequest(info.ControlToken, "stop", null, null, null, false, 5000),
            TimeSpan.FromSeconds(10),
            Xunit.TestContext.Current.CancellationToken);
        var exitCode = await running;

        // Assert: the screenshot failed with a named problem, the daemon kept serving, and stopped cleanly
        Assert.False(screenshot.Ok);
        Assert.Equal(1, screenshot.ExitCode);
        Assert.Contains("InvalidOperationException", screenshot.Message, StringComparison.Ordinal);
        Assert.Contains("browser exploded", screenshot.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("second line", screenshot.Message, StringComparison.Ordinal);
        Assert.True(click.Ok);
        Assert.True(stop.Ok);
        Assert.Equal(0, exitCode);
        Assert.Equal(1, browser.DisposedCount);
        Assert.Equal(1, server.DisposedCount);
    }

    /// <summary>
    ///     Test that an unexpected exception while starting the browser produces a failed session, exit code 4,
    ///     and the server torn down.
    /// </summary>
    [Fact]
    public async Task DaemonHost_Run_BrowserLaunchThrowsUnexpected_WritesFailedSessionAndReturnsFour()
    {
        // Arrange: a browser launcher that throws a non-Puppet exception
        using var temp = new TempDirectory();
        var store = new SessionStore(temp.Path);
        var server = new FakeServerRunner();
        var browser = new FakeBrowserLauncher { Failure = new InvalidOperationException("driver crashed") };
        var host = new DaemonHost(store, server, browser);

        // Act: run the daemon
        var exitCode = await host.RunAsync("demo", "server.dll", Xunit.TestContext.Current.CancellationToken);

        // Assert: exit 4, failed status naming the problem, server stopped
        var info = store.TryRead("demo");
        Assert.Equal(4, exitCode);
        Assert.Equal(SessionStatus.Failed, info!.Status);
        Assert.Contains("Unexpected error", info.Error, StringComparison.Ordinal);
        Assert.Contains("driver crashed", info.Error, StringComparison.Ordinal);
        Assert.Equal(1, server.DisposedCount);
    }

    /// <summary>
    ///     Test that the Playwright page adapter turns a failed screenshot into a <see cref="PuppetException"/> that
    ///     names the problem on one line.
    /// </summary>
    [Fact]
    public async Task DaemonHost_PlaywrightPage_ScreenshotFails_ThrowsPuppetException()
    {
        // Arrange: a Playwright page whose screenshot fails the way a closed browser does
        var page = new PlaywrightPage(FailingScreenshotPage.Create(new PlaywrightException("Target closed\nlong call log")));

        // Act: take a screenshot
        var exception = await Assert.ThrowsAsync<PuppetException>(() => page.ScreenshotAsync(false, 5000));

        // Assert: the message names the screenshot and the first line only
        Assert.Contains("screenshot", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Target closed", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("call log", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the Playwright page adapter applies the per-command timeout to the screenshot so a hung
    ///     capture cannot block the daemon's control loop, and reports an expired timeout as a
    ///     <see cref="PuppetException"/>.
    /// </summary>
    [Fact]
    public async Task DaemonHost_PlaywrightPage_ScreenshotTimesOut_AppliesTimeoutAndThrowsPuppetException()
    {
        // Arrange: a Playwright page whose screenshot times out
        var playwrightPage = FailingScreenshotPage.Create(new TimeoutException("Timeout 2000ms exceeded."));
        var page = new PlaywrightPage(playwrightPage);

        // Act: take a screenshot with a 2 s timeout
        var exception = await Assert.ThrowsAsync<PuppetException>(() => page.ScreenshotAsync(true, 2000));

        // Assert: the timeout was handed to Playwright, and the failure names the timeout in seconds
        var options = FailingScreenshotPage.From(playwrightPage).RequestedOptions;
        Assert.NotNull(options);
        Assert.Equal(2000, options.Timeout);
        Assert.True(options.FullPage);
        Assert.Contains("Timed out after 2 s", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the browser process query returns the browser's identity and detaches the protocol session.
    /// </summary>
    [Fact]
    public async Task DaemonHost_PlaywrightBrowser_ProcessQuery_DetachesProtocolSession()
    {
        // Arrange: a protocol session that answers with the test process as the browser
        var pid = Environment.ProcessId;
        var reply = System.Text.Json.JsonDocument.Parse($$"""{"processInfo":[{"type":"gpu","id":1},{"type":"browser","id":{{pid}}}]}""")
            .RootElement.Clone();
        var session = new FakeCdpSession { Reply = reply };

        // Act: query the browser process
        var (processId, startTime) = await PlaywrightBrowserLauncher.QueryBrowserProcessAsync(session);

        // Assert: the identity was found, and the session was detached once
        Assert.Equal(pid, processId);
        Assert.NotEqual(default, startTime);
        Assert.Equal(1, session.DetachCount);
    }

    /// <summary>
    ///     Test that a failing query still detaches the session, and that a detach failing because the browser has
    ///     already closed is tolerated.
    /// </summary>
    [Fact]
    public async Task DaemonHost_PlaywrightBrowser_DetachFailsBecauseBrowserClosed_IsTolerated()
    {
        // Arrange: a protocol session whose query and detach both fail because the browser closed
        var session = new FakeCdpSession
        {
            SendFailure = new PlaywrightException("Target closed"),
            DetachFailure = new PlaywrightException("Target closed")
        };

        // Act: query the browser process
        var (processId, _) = await PlaywrightBrowserLauncher.QueryBrowserProcessAsync(session);

        // Assert: no identity and no exception, and the detach was attempted
        Assert.Equal(0, processId);
        Assert.Equal(1, session.DetachCount);
    }

    /// <summary>
    ///     A DevTools protocol session that records how often it was detached.
    /// </summary>
    private sealed class FakeCdpSession : ICDPSession
    {
        /// <inheritdoc />
        public event EventHandler<ICDPSession>? Close;


        /// <summary>
        ///     Gets or sets the reply to <see cref="SendAsync"/>.
        /// </summary>
        public System.Text.Json.JsonElement? Reply { get; set; }

        /// <summary>
        ///     Gets or sets an exception thrown by <see cref="SendAsync"/>.
        /// </summary>
        public Exception? SendFailure { get; set; }

        /// <summary>
        ///     Gets or sets an exception thrown by <see cref="DetachAsync"/>.
        /// </summary>
        public Exception? DetachFailure { get; set; }

        /// <summary>
        ///     Gets how many times the session was detached.
        /// </summary>
        public int DetachCount { get; private set; }

        /// <inheritdoc />
        public Task DetachAsync()
        {
            DetachCount++;
            Close?.Invoke(this, this);
            return DetachFailure is null ? Task.CompletedTask : Task.FromException(DetachFailure);
        }

        /// <inheritdoc />
        public Task<System.Text.Json.JsonElement?> SendAsync(string method, Dictionary<string, object>? args = null)
        {
            return SendFailure is null ? Task.FromResult(Reply) : Task.FromException<System.Text.Json.JsonElement?>(SendFailure);
        }

        /// <inheritdoc />
        public ICDPSessionEvent Event(string eventName)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
