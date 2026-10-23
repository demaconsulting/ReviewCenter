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

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     Unit tests for the <see cref="PuppetProgram"/> command dispatcher, using fakes for every collaborator.
/// </summary>
public class PuppetProgramTests
{
    /// <summary>
    ///     The result of one program invocation.
    /// </summary>
    /// <param name="ExitCode">The exit code.</param>
    /// <param name="Output">What was written to standard output.</param>
    /// <param name="Error">What was written to standard error.</param>
    private sealed record RunResult(int ExitCode, string Output, string Error);

    /// <summary>
    ///     Runs the program with the given arguments and services.
    /// </summary>
    /// <param name="services">The collaborators.</param>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The exit code and captured text.</returns>
    private static async Task<RunResult> RunAsync(PuppetServices services, params string[] args)
    {
        return await RunAsync(services, Xunit.TestContext.Current.CancellationToken, args);
    }

    /// <summary>
    ///     Runs the program with the given arguments, services, and cancellation token.
    /// </summary>
    /// <param name="services">The collaborators.</param>
    /// <param name="cancellationToken">Cancels long waits.</param>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The exit code and captured text.</returns>
    private static async Task<RunResult> RunAsync(PuppetServices services, CancellationToken cancellationToken, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await PuppetProgram.RunAsync(args, output, error, services, cancellationToken);
        return new RunResult(exitCode, output.ToString(), error.ToString());
    }

    /// <summary>
    ///     Creates services around a store in a temp folder where no process counts as alive and killing is a no-op.
    /// </summary>
    /// <param name="temp">The temp folder.</param>
    /// <param name="launcher">The daemon launcher to use.</param>
    /// <param name="alive">Decides whether a process counts as alive.</param>
    /// <param name="kill">Records process kills; <c>null</c> ignores them.</param>
    /// <param name="startTimeout">How long <c>start</c> waits for readiness; <c>null</c> keeps the default.</param>
    /// <returns>The services.</returns>
    private static PuppetServices CreateServices(
        TempDirectory temp,
        FakeDaemonLauncher? launcher = null,
        Func<bool>? alive = null,
        Action<int, DateTime>? kill = null,
        TimeSpan? startTimeout = null)
    {
        var store = new SessionStore(temp.Path, (_, _) => alive?.Invoke() ?? false, kill ?? ((_, _) => { }));
        return new PuppetServices(
            store, launcher ?? new FakeDaemonLauncher(), new FakeServerRunner(), new FakeBrowserLauncher(), startTimeout);
    }

    /// <summary>
    ///     Creates a ready session record.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <param name="status">The status to record.</param>
    /// <param name="error">An error message, if any.</param>
    /// <returns>The record.</returns>
    private static SessionInfo CreateInfo(string id, SessionStatus status = SessionStatus.Ready, string? error = null)
    {
        return new SessionInfo(
            id, status, 40000, "token", "http://127.0.0.1:5999", Environment.ProcessId,
            DateTime.UtcNow, 0, default, 0, default, error);
    }

    /// <summary>
    ///     Test that --help prints the commands and exits 0.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_Help_ListsCommandsAndExitsZero()
    {
        // Arrange: services over an empty store
        using var temp = new TempDirectory();

        // Act: ask for help
        var result = await RunAsync(CreateServices(temp), "--help");

        // Assert: exit 0 and the main commands plus the Chromium setup hint are listed
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("start", result.Output, StringComparison.Ordinal);
        Assert.Contains("screenshot", result.Output, StringComparison.Ordinal);
        Assert.Contains("playwright.ps1 install chromium", result.Output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a bad command line exits 1 with an explanation on the error stream.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_BadArguments_ExitsOneWithMessage()
    {
        // Arrange: services over an empty store
        using var temp = new TempDirectory();

        // Act: run an unknown command
        var result = await RunAsync(CreateServices(temp), "dance");

        // Assert: exit 1 and the message names the command
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("dance", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a command for a session that does not exist exits 2.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_UnknownSession_ExitsTwo()
    {
        // Arrange: an empty store
        using var temp = new TempDirectory();

        // Act: click in a session that was never started
        var result = await RunAsync(CreateServices(temp), "click", "hello-button", "--session", "ghost");

        // Assert: exit 2 and the message names the session
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Unknown session 'ghost'", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a command for a session whose daemon died exits 3 and removes the leftover file.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_StaleSession_ExitsThreeAndCleansUp()
    {
        // Arrange: a stored session whose daemon is not running
        using var temp = new TempDirectory();
        var services = CreateServices(temp, alive: () => false);
        services.Store.Write(CreateInfo("old"));

        // Act: click in it
        var result = await RunAsync(services, "click", "hello-button", "--session", "old");

        // Assert: exit 3, explained, and the file is gone
        Assert.Equal(3, result.ExitCode);
        Assert.Contains("stale", result.Error, StringComparison.Ordinal);
        Assert.Null(services.Store.TryRead("old"));
    }

    /// <summary>
    ///     Test that with no session named and none running the command exits 2.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_NoSessionRunning_ExitsTwo()
    {
        // Arrange: an empty store and no session environment variable
        using var temp = new TempDirectory();
        Environment.SetEnvironmentVariable(CommandLine.SessionEnvironmentVariable, null);

        // Act: run snapshot without naming a session
        var result = await RunAsync(CreateServices(temp), "snapshot");

        // Assert: exit 2 with a hint to start one
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("start", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that list with no sessions says so.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_ListWithNoSessions_ReportsNone()
    {
        // Arrange: an empty store
        using var temp = new TempDirectory();

        // Act: list
        var result = await RunAsync(CreateServices(temp), "list");

        // Assert: exit 0 and "No active sessions."
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No active sessions.", result.Output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that list shows live sessions and cleans up stale ones.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_ListWithStaleSession_ReportsAndCleansUp()
    {
        // Arrange: one session whose daemon is not running
        using var temp = new TempDirectory();
        var services = CreateServices(temp, alive: () => false);
        services.Store.Write(CreateInfo("dead"));

        // Act: list
        var result = await RunAsync(services, "list");

        // Assert: reported as stale and removed; a second list is empty
        Assert.Contains("dead  stale", result.Output, StringComparison.Ordinal);
        Assert.Null(services.Store.TryRead("dead"));
        var second = await RunAsync(services, "list");
        Assert.Contains("No active sessions.", second.Output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that start prints the session id and server URL once the daemon reports ready.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_StartDaemonReady_PrintsSessionAndUrl()
    {
        // Arrange: a "daemon" that immediately publishes a ready session, and an existing file as server path
        using var temp = new TempDirectory();
        var server = Path.Combine(temp.Path, "server.dll");
        await File.WriteAllTextAsync(server, "x", Xunit.TestContext.Current.CancellationToken);
        var launcher = new FakeDaemonLauncher();
        var services = CreateServices(temp, launcher, () => true);
        launcher.OnLaunch = (id, _) => services.Store.Write(CreateInfo(id));

        // Act: start a named session
        var result = await RunAsync(services, "start", "--session", "demo", "--server", server);

        // Assert: exit 0 with session and URL printed
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Session: demo", result.Output, StringComparison.Ordinal);
        Assert.Contains("Server: http://127.0.0.1:5999", result.Output, StringComparison.Ordinal);
        Assert.Equal(["demo"], launcher.Launched);
    }

    /// <summary>
    ///     Test that start without --session generates an id.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_StartWithoutSession_GeneratesEightCharacterId()
    {
        // Arrange: a daemon that publishes ready
        using var temp = new TempDirectory();
        var server = Path.Combine(temp.Path, "server.dll");
        await File.WriteAllTextAsync(server, "x", Xunit.TestContext.Current.CancellationToken);
        var launcher = new FakeDaemonLauncher();
        var services = CreateServices(temp, launcher, () => true);
        launcher.OnLaunch = (id, _) => services.Store.Write(CreateInfo(id));

        // Act: start without naming a session
        var result = await RunAsync(services, "start", "--server", server);

        // Assert: an eight character id was generated and printed
        Assert.Equal(0, result.ExitCode);
        Assert.Single(launcher.Launched);
        Assert.Equal(8, launcher.Launched[0].Length);
        Assert.Contains($"Session: {launcher.Launched[0]}", result.Output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a failed start (for example Chromium missing) exits 4, shows the reason, and leaves no file.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_StartDaemonFailed_ExitsFourWithReason()
    {
        // Arrange: a daemon that reports failure
        using var temp = new TempDirectory();
        var server = Path.Combine(temp.Path, "server.dll");
        await File.WriteAllTextAsync(server, "x", Xunit.TestContext.Current.CancellationToken);
        var launcher = new FakeDaemonLauncher();
        var services = CreateServices(temp, launcher, () => true);
        launcher.OnLaunch = (id, _) => services.Store.Write(CreateInfo(id, SessionStatus.Failed, "Chromium is not installed."));

        // Act: start
        var result = await RunAsync(services, "start", "--session", "demo", "--server", server);

        // Assert: exit 4 with the reason, and the failed file was removed
        Assert.Equal(4, result.ExitCode);
        Assert.Contains("Chromium is not installed.", result.Error, StringComparison.Ordinal);
        Assert.Null(services.Store.TryRead("demo"));
    }

    /// <summary>
    ///     Test that a daemon that dies during start-up is detected rather than waited for.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_StartDaemonDiesEarly_ExitsFour()
    {
        // Arrange: a "daemon" whose process id does not exist
        using var temp = new TempDirectory();
        var server = Path.Combine(temp.Path, "server.dll");
        await File.WriteAllTextAsync(server, "x", Xunit.TestContext.Current.CancellationToken);
        var launcher = new FakeDaemonLauncher { ReportedPid = int.MaxValue };
        var services = CreateServices(temp, launcher);

        // Act: start
        var result = await RunAsync(services, "start", "--session", "demo", "--server", server);

        // Assert: exit 4 with an explanation, and the claim was removed
        Assert.Equal(4, result.ExitCode);
        Assert.Contains("exited unexpectedly", result.Error, StringComparison.Ordinal);
        Assert.Null(services.Store.TryRead("demo"));
    }

    /// <summary>
    ///     Test that start refuses to replace a running session with the same id.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_StartExistingLiveSession_ExitsOne()
    {
        // Arrange: a live session
        using var temp = new TempDirectory();
        var server = Path.Combine(temp.Path, "server.dll");
        await File.WriteAllTextAsync(server, "x", Xunit.TestContext.Current.CancellationToken);
        var launcher = new FakeDaemonLauncher();
        var services = CreateServices(temp, launcher, () => true);
        services.Store.Write(CreateInfo("busy"));

        // Act: start the same id
        var result = await RunAsync(services, "start", "--session", "busy", "--server", server);

        // Assert: refused and nothing was launched
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("already running", result.Error, StringComparison.Ordinal);
        Assert.Empty(launcher.Launched);
    }

    /// <summary>
    ///     Test that a second start for an id whose start-up is still in progress is refused with a clear error
    ///     while the first one carries on, so only one daemon is ever launched.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_StartWhileAnotherStartInProgress_FailsAndLaunchesOnlyOnce()
    {
        // Arrange: a first start whose daemon launch is held up until the test releases it
        using var temp = new TempDirectory();
        var server = Path.Combine(temp.Path, "server.dll");
        await File.WriteAllTextAsync(server, "x", Xunit.TestContext.Current.CancellationToken);
        var launcher = new FakeDaemonLauncher();
        var services = CreateServices(temp, launcher, () => true);
        using var launching = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        launcher.OnLaunch = (id, _) =>
        {
            launching.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            services.Store.Write(CreateInfo(id));
        };
        var first = Task.Run(
            () => RunAsync(services, "start", "--session", "same", "--server", server),
            Xunit.TestContext.Current.CancellationToken);
        Assert.True(launching.Wait(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken));

        // Act: a second start for the same id while the first is still starting, then let the first finish
        var second = await RunAsync(services, "start", "--session", "same", "--server", server);
        release.Set();
        var firstResult = await first;

        // Assert: the second failed with a clear error, the first succeeded, and only one daemon was launched
        Assert.Equal(1, second.ExitCode);
        Assert.Contains("already being started", second.Error, StringComparison.Ordinal);
        Assert.Equal(0, firstResult.ExitCode);
        Assert.Equal(["same"], launcher.Launched);
    }

    /// <summary>
    ///     Test that many simultaneous starts of one id launch exactly one daemon and the rest fail with exit code 1.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_ConcurrentStartsOfSameSession_ExactlyOneSucceeds()
    {
        // Arrange: a daemon that publishes a ready session as soon as it is launched
        const int starters = 8;
        using var temp = new TempDirectory();
        var server = Path.Combine(temp.Path, "server.dll");
        await File.WriteAllTextAsync(server, "x", Xunit.TestContext.Current.CancellationToken);
        var launcher = new FakeDaemonLauncher();
        var services = CreateServices(temp, launcher, () => true);
        launcher.OnLaunch = (id, _) => services.Store.Write(CreateInfo(id));

        // Act: start the same id from several threads at once
        var results = await Task.WhenAll(
            Enumerable.Range(0, starters).Select(_ => Task.Run(
                () => RunAsync(services, "start", "--session", "race", "--server", server),
                Xunit.TestContext.Current.CancellationToken)));

        // Assert: one start won and launched; every other start failed with exit code 1 and an explanation
        Assert.Single(launcher.Launched);
        Assert.Equal(1, results.Count(r => r.ExitCode == 0));
        Assert.All(
            results.Where(r => r.ExitCode != 0),
            r =>
            {
                Assert.Equal(1, r.ExitCode);
                Assert.Contains("already", r.Error, StringComparison.Ordinal);
            });
    }

    /// <summary>
    ///     Test that a daemon that cannot be launched gives the session id back and reports the failure.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_StartLaunchFails_ReleasesTheSessionAndReportsError()
    {
        // Arrange: a launcher that cannot create the process
        using var temp = new TempDirectory();
        var server = Path.Combine(temp.Path, "server.dll");
        await File.WriteAllTextAsync(server, "x", Xunit.TestContext.Current.CancellationToken);
        var launcher = new FakeDaemonLauncher { Failure = new PuppetException("Could not start the session daemon: access denied") };
        var services = CreateServices(temp, launcher, () => true);

        // Act: start
        var result = await RunAsync(services, "start", "--session", "demo", "--server", server);

        // Assert: exit 1 with the reason, and no claim is left blocking the id
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("access denied", result.Error, StringComparison.Ordinal);
        Assert.Null(services.Store.TryRead("demo"));
        Assert.True(services.Store.TryReserve("demo"));
    }

    /// <summary>
    ///     Test that a start-up timeout terminates the launched daemon by pid and start time even though it never
    ///     wrote a session file, and leaves no session state behind.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_StartTimesOutWithNoSessionFile_TerminatesLaunchedDaemon()
    {
        // Arrange: a daemon that stays alive but never publishes anything, and a short start timeout
        using var temp = new TempDirectory();
        var server = Path.Combine(temp.Path, "server.dll");
        await File.WriteAllTextAsync(server, "x", Xunit.TestContext.Current.CancellationToken);
        var daemonStart = new DateTime(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var launcher = new FakeDaemonLauncher { ReportedPid = 4242, ReportedStartTimeUtc = daemonStart };
        var killed = new List<(int Pid, DateTime Start)>();
        var services = CreateServices(
            temp, launcher, () => true, (pid, start) => killed.Add((pid, start)), TimeSpan.FromMilliseconds(300));

        // Act: start
        var result = await RunAsync(services, "start", "--session", "slow", "--server", server);

        // Assert: exit 4 with a timeout message, only the launched daemon was killed (verified by its start time),
        // and the claim was removed
        Assert.Equal(4, result.ExitCode);
        Assert.Contains("did not become ready", result.Error, StringComparison.Ordinal);
        Assert.Equal([(4242, daemonStart)], killed);
        Assert.Null(services.Store.TryRead("slow"));
    }

    /// <summary>
    ///     Test that a start-up timeout also terminates the server and the browser the daemon had already recorded.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_StartTimesOutWithRecordedProcesses_TerminatesBrowserServerAndDaemon()
    {
        // Arrange: a daemon that recorded a server and a browser but never became ready
        using var temp = new TempDirectory();
        var server = Path.Combine(temp.Path, "server.dll");
        await File.WriteAllTextAsync(server, "x", Xunit.TestContext.Current.CancellationToken);
        var daemonStart = new DateTime(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var serverStart = daemonStart.AddSeconds(5);
        var browserStart = daemonStart.AddSeconds(10);
        var launcher = new FakeDaemonLauncher { ReportedPid = 4242, ReportedStartTimeUtc = daemonStart };
        var killed = new List<(int Pid, DateTime Start)>();
        var services = CreateServices(
            temp, launcher, () => true, (pid, start) => killed.Add((pid, start)), TimeSpan.FromMilliseconds(300));
        launcher.OnLaunch = (id, _) => services.Store.Write(new SessionInfo(
            id, SessionStatus.Starting, 40000, "token", "http://127.0.0.1:5999", 4242, daemonStart, 5151, serverStart,
            6161, browserStart, null));

        // Act: start
        var result = await RunAsync(services, "start", "--session", "slow", "--server", server);

        // Assert: exit 4, browser, server, and daemon all terminated by their identities, and the file removed
        Assert.Equal(4, result.ExitCode);
        Assert.Contains((6161, browserStart), killed);
        Assert.Contains((5151, serverStart), killed);
        Assert.Contains((4242, daemonStart), killed);
        Assert.Null(services.Store.TryRead("slow"));
    }

    /// <summary>
    ///     Test that cancelling a start terminates the launched daemon and removes the session state before the
    ///     cancellation propagates.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_StartCancelled_TerminatesLaunchedDaemonAndCleansUp()
    {
        // Arrange: a daemon that never becomes ready, and a caller that gives up shortly after
        using var temp = new TempDirectory();
        var server = Path.Combine(temp.Path, "server.dll");
        await File.WriteAllTextAsync(server, "x", Xunit.TestContext.Current.CancellationToken);
        var daemonStart = new DateTime(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var launcher = new FakeDaemonLauncher { ReportedPid = 4242, ReportedStartTimeUtc = daemonStart };
        var killed = new List<(int Pid, DateTime Start)>();
        var services = CreateServices(temp, launcher, () => true, (pid, start) => killed.Add((pid, start)));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Xunit.TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        // Act: start until it is cancelled
        var exception = await Record.ExceptionAsync(
            () => RunAsync(services, cancellation.Token, "start", "--session", "slow", "--server", server));

        // Assert: the cancellation propagated, the launched daemon was killed by identity, and nothing is left
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal([(4242, daemonStart)], killed);
        Assert.Null(services.Store.TryRead("slow"));
    }

    /// <summary>
    ///     Test that start reports a clear error when the server assembly does not exist.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_StartMissingServerAssembly_ExitsFourWithHint()
    {
        // Arrange: a path that does not exist
        using var temp = new TempDirectory();

        // Act: start with it
        var result = await RunAsync(CreateServices(temp), "start", "--server", Path.Combine(temp.Path, "nope.dll"));

        // Assert: exit 4 and a hint to build
        Assert.Equal(4, result.ExitCode);
        Assert.Contains("not found", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that commands travel through the real control channel to a daemon, a missing element gives exit 1,
    ///     and stop ends the daemon and removes the session.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_ClientCommandsAgainstDaemon_ClickMissingElementAndStop()
    {
        // Arrange: a daemon running in the background over fakes; liveness follows the daemon task
        using var temp = new TempDirectory();
        Task<int>? daemon = null;
        var store = new SessionStore(temp.Path, (_, _) => daemon is { IsCompleted: false }, (_, _) => { });
        var browser = new FakeBrowserLauncher();
        var services = new PuppetServices(store, new FakeDaemonLauncher(), new FakeServerRunner(), browser);
        daemon = new DaemonHost(store, services.ServerRunner, browser).RunAsync("demo", "server.dll", Xunit.TestContext.Current.CancellationToken);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (store.TryRead("demo")?.Status != SessionStatus.Ready && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, Xunit.TestContext.Current.CancellationToken);
        }

        // Act: click, read text, fail a click, then stop
        var click = await RunAsync(services, "click", "hello-button", "--session", "demo");
        var text = await RunAsync(services, "text", "hello-message", "--session", "demo");
        browser.Page.Failure = new PuppetException("No element matching [data-testid=\"nope\"] appeared within 10 s.");
        var missing = await RunAsync(services, "click", "nope", "--session", "demo");
        var stop = await RunAsync(services, "stop", "--session", "demo");
        var daemonExit = await daemon;

        // Assert: each outcome and exit code, and the daemon is gone with its file
        Assert.Equal(0, click.ExitCode);
        Assert.Contains("Clicked hello-button", click.Output, StringComparison.Ordinal);
        Assert.Equal(0, text.ExitCode);
        Assert.Contains("fake text", text.Output, StringComparison.Ordinal);
        Assert.Equal(1, missing.ExitCode);
        Assert.Contains("No element matching", missing.Error, StringComparison.Ordinal);
        Assert.Equal(0, stop.ExitCode);
        Assert.Equal(0, daemonExit);
        Assert.Null(store.TryRead("demo"));
    }
}
