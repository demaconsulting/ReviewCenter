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

using System.Runtime.Versioning;

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     System-level tests of Puppet: the whole command pipeline (command line, session files, control
///     channel, daemon, executor) with only the browser and the server process replaced by fakes, so the
///     tests run on any machine without Chromium.
/// </summary>
public class PuppetSystemTests
{
    /// <summary>
    ///     The result of one program invocation.
    /// </summary>
    /// <param name="ExitCode">The exit code.</param>
    /// <param name="Output">What was written to standard output.</param>
    /// <param name="Error">What was written to standard error.</param>
    private sealed record RunResult(int ExitCode, string Output, string Error);

    /// <summary>
    ///     Runs the program once.
    /// </summary>
    /// <param name="services">The collaborators.</param>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The exit code and captured text.</returns>
    private static async Task<RunResult> RunAsync(PuppetServices services, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await PuppetProgram.RunAsync(args, output, error, services, Xunit.TestContext.Current.CancellationToken);
        return new RunResult(exitCode, output.ToString(), error.ToString());
    }

    /// <summary>
    ///     Test that a session can be started, driven with every command, and stopped, leaving nothing behind.
    /// </summary>
    [Fact]
    public async Task ReviewCenterPuppet_Session_StartDriveStop_RunsEveryCommandAndCleansUp()
    {
        // Arrange: a "launcher" that runs the daemon in this process over fakes; liveness follows the daemon task
        using var temp = new TempDirectory();
        var serverDll = Path.Combine(temp.Path, "server.dll");
        await File.WriteAllTextAsync(serverDll, "x", Xunit.TestContext.Current.CancellationToken);
        Task<int>? daemon = null;
        var store = new SessionStore(temp.Path, (_, _) => daemon is { IsCompleted: false }, (_, _) => { });
        var browser = new FakeBrowserLauncher();
        var server = new FakeServerRunner();
        var launcher = new FakeDaemonLauncher();
        launcher.OnLaunch = (id, path) =>
            daemon = new DaemonHost(store, server, browser).RunAsync(id, path, Xunit.TestContext.Current.CancellationToken);
        var services = new PuppetServices(store, launcher, server, browser);
        var screenshot = Path.Combine(temp.Path, "shot.png");

        // Act: start, then run each command, then stop and list
        var start = await RunAsync(services, "start", "--session", "demo", "--server", serverDll);
        var navigate = await RunAsync(services, "goto", "/", "--session", "demo");
        var click = await RunAsync(services, "click", "hello-button", "--session", "demo");
        var fill = await RunAsync(services, "fill", "#name", "Ada", "--session", "demo");
        var text = await RunAsync(services, "text", "hello-message", "--session", "demo");
        var shot = await RunAsync(services, "screenshot", "--out", screenshot, "--session", "demo");
        var snapshot = await RunAsync(services, "snapshot", "--session", "demo");
        var stop = await RunAsync(services, "stop", "--session", "demo");
        var list = await RunAsync(services, "list");

        // Assert: every command succeeded, the page saw them in order, the screenshot exists, and no session remains
        Assert.Equal(0, start.ExitCode);
        Assert.Equal(0, navigate.ExitCode);
        Assert.Equal(0, click.ExitCode);
        Assert.Equal(0, fill.ExitCode);
        Assert.Equal(0, text.ExitCode);
        Assert.Equal(0, shot.ExitCode);
        Assert.Equal(0, snapshot.ExitCode);
        Assert.Equal(0, stop.ExitCode);
        Assert.Equal(
            [
                "goto http://127.0.0.1:5999/",
                "click [data-testid=\"hello-button\"]",
                "fill #name Ada",
                "text [data-testid=\"hello-message\"]",
                "screenshot fullPage=False",
                "snapshot"
            ],
            browser.Page.Calls);
        Assert.True(File.Exists(screenshot));
        Assert.Contains("No active sessions.", list.Output, StringComparison.Ordinal);
        Assert.Equal(0, await daemon!);
        Assert.Equal(1, server.DisposedCount);
        Assert.Equal(1, browser.DisposedCount);
    }

    /// <summary>
    ///     Test that the different ways a session can be unusable give different, documented exit codes.
    /// </summary>
    [Fact]
    public async Task ReviewCenterPuppet_Errors_UnknownSessionAndMissingElement_ReportDistinctExitCodes()
    {
        // Arrange: a running daemon over fakes whose page fails to find the element
        using var temp = new TempDirectory();
        Task<int>? daemon = null;
        var store = new SessionStore(temp.Path, (_, _) => daemon is { IsCompleted: false }, (_, _) => { });
        var browser = new FakeBrowserLauncher();
        var server = new FakeServerRunner();
        var services = new PuppetServices(store, new FakeDaemonLauncher(), server, browser);
        daemon = new DaemonHost(store, server, browser).RunAsync("live", "server.dll", Xunit.TestContext.Current.CancellationToken);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (store.TryRead("live")?.Status != SessionStatus.Ready && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, Xunit.TestContext.Current.CancellationToken);
        }

        // Act: use an unknown session, then a missing element in the live session, then stop
        var unknown = await RunAsync(services, "text", "x", "--session", "ghost");
        browser.Page.Failure = new PuppetException("No element matching [data-testid=\"x\"] appeared within 10 s.");
        var missing = await RunAsync(services, "click", "x", "--session", "live", "--timeout", "1");
        var stop = await RunAsync(services, "stop", "--session", "live");

        // Assert: unknown is 2, a missing element is 1, and stopping still works
        Assert.Equal(2, unknown.ExitCode);
        Assert.Equal(1, missing.ExitCode);
        Assert.Equal(0, stop.ExitCode);
        Assert.Equal(0, await daemon);
    }

    /// <summary>
    ///     The environment for the start and cleanup scenarios: a real session store in a temporary folder and a
    ///     "launcher" that runs the daemon inside the test process over fakes.
    /// </summary>
    private sealed class DaemonEnvironment : IDisposable
    {
        /// <summary>
        ///     The pid the fake launcher reports for the daemon, distinct from every other recorded pid.
        /// </summary>
        public const int DaemonPid = 4242;

        /// <summary>
        ///     A pid that never counts as alive, for recording a daemon that has died.
        /// </summary>
        public const int DeadPid = 4343;

        /// <summary>
        ///     The temporary folder that holds the session files and the fake server assembly.
        /// </summary>
        private readonly TempDirectory _temp = new();

        /// <summary>
        ///     Cancels the daemon, which is what "terminating" the daemon means in this environment.
        /// </summary>
        private readonly CancellationTokenSource _daemonCancel = new();

        /// <summary>
        ///     The daemon start time the fake launcher reports.
        /// </summary>
        private readonly DateTime _daemonStart = new(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>
        ///     Initializes a new instance of the <see cref="DaemonEnvironment"/> class.
        /// </summary>
        /// <param name="startTimeout">How long <c>start</c> waits for readiness; <c>null</c> keeps the default.</param>
        public DaemonEnvironment(TimeSpan? startTimeout = null)
        {
            ServerDll = System.IO.Path.Combine(_temp.Path, "server.dll");
            File.WriteAllText(ServerDll, "x");
            Store = new SessionStore(
                _temp.Path,
                (pid, _) => pid switch
                {
                    DeadPid => false,

                    // The daemon is alive until its task completes; the daemon records this process's own id
                    DaemonPid => Daemon is not { IsCompleted: true },
                    _ when pid == Environment.ProcessId => Daemon is not { IsCompleted: true },
                    _ => true
                },
                (pid, start) =>
                {
                    Killed.Add((pid, start));
                    if (pid == DaemonPid)
                    {
                        _daemonCancel.Cancel();
                    }
                });
            Launcher = new FakeDaemonLauncher { ReportedPid = DaemonPid, ReportedStartTimeUtc = _daemonStart };
            Launcher.OnLaunch = (id, path) =>
                Daemon = new DaemonHost(Store, Server, Browser).RunAsync(id, path, _daemonCancel.Token);
            Services = new PuppetServices(Store, Launcher, Server, Browser, startTimeout);
        }

        /// <summary>
        ///     Gets the fake server assembly path to pass to <c>start</c>.
        /// </summary>
        public string ServerDll { get; }

        /// <summary>
        ///     Gets the session store.
        /// </summary>
        public SessionStore Store { get; }

        /// <summary>
        ///     Gets the fake server runner.
        /// </summary>
        public FakeServerRunner Server { get; } = new();

        /// <summary>
        ///     Gets the fake browser launcher.
        /// </summary>
        public FakeBrowserLauncher Browser { get; } = new();

        /// <summary>
        ///     Gets the fake daemon launcher.
        /// </summary>
        public FakeDaemonLauncher Launcher { get; }

        /// <summary>
        ///     Gets the services handed to the program.
        /// </summary>
        public PuppetServices Services { get; }

        /// <summary>
        ///     Gets the processes the program asked to terminate, with the start times it verified them by.
        /// </summary>
        public List<(int Pid, DateTime Start)> Killed { get; } = [];

        /// <summary>
        ///     Gets the daemon task once it has been launched.
        /// </summary>
        public Task<int>? Daemon { get; private set; }

        /// <summary>
        ///     Gets the start time the fake launcher reports for the daemon.
        /// </summary>
        public DateTime DaemonStart => _daemonStart;

        /// <inheritdoc />
        public void Dispose()
        {
            _daemonCancel.Cancel();
            _daemonCancel.Dispose();
            _temp.Dispose();
        }
    }

    /// <summary>
    ///     Test that a session whose server cannot start is reported as a start failure and leaves no session behind.
    /// </summary>
    [Fact]
    public async Task ReviewCenterPuppet_Start_ServerCannotStart_ReportsStartFailureAndLeavesNothingBehind()
    {
        // Arrange: a daemon over a server that fails to start
        using var env = new DaemonEnvironment();
        env.Server.Failure = new PuppetException("The server did not become healthy.");

        // Act: start, then list
        var start = await RunAsync(env.Services, "start", "--session", "broken", "--server", env.ServerDll);
        var exit = await env.Daemon!.WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken);
        var list = await RunAsync(env.Services, "list");

        // Assert: exit 4 with the reason, the daemon ended with 4, no session file or reservation remains
        Assert.Equal(4, start.ExitCode);
        Assert.Contains("did not become healthy", start.Error, StringComparison.Ordinal);
        Assert.Equal(4, exit);
        Assert.Empty(env.Store.ListIds());
        Assert.Contains("No active sessions.", list.Output, StringComparison.Ordinal);
        Assert.True(env.Store.TryReserve("broken"));
    }

    /// <summary>
    ///     Test that two simultaneous starts of one session start one session only, and that session still stops cleanly.
    /// </summary>
    [Fact]
    public async Task ReviewCenterPuppet_Start_SimultaneousStartsOfSameSession_StartExactlyOneSession()
    {
        // Arrange: a daemon environment and several starters for the same session id
        const int starters = 4;
        using var env = new DaemonEnvironment();

        // Act: start the same session from several threads at once, then stop it
        var starts = await Task.WhenAll(
            Enumerable.Range(0, starters).Select(_ => Task.Run(
                () => RunAsync(env.Services, "start", "--session", "twin", "--server", env.ServerDll),
                Xunit.TestContext.Current.CancellationToken)));
        var stop = await RunAsync(env.Services, "stop", "--session", "twin");

        // Assert: exactly one start succeeded and launched; the others failed with exit code 1 and an explanation
        Assert.Equal(["twin"], env.Launcher.Launched);
        Assert.Equal(1, starts.Count(s => s.ExitCode == 0));
        Assert.All(
            starts.Where(s => s.ExitCode != 0),
            s =>
            {
                Assert.Equal(1, s.ExitCode);
                Assert.Contains("already", s.Error, StringComparison.Ordinal);
            });
        Assert.Equal(0, stop.ExitCode);
        Assert.Equal(0, await env.Daemon!);
    }

    /// <summary>
    ///     Test that a start that never becomes ready ends the daemon it launched and leaves no session state behind.
    /// </summary>
    [Fact]
    public async Task ReviewCenterPuppet_Start_NeverBecomesReady_TerminatesDaemonAndLeavesNothingBehind()
    {
        // Arrange: a daemon whose server never becomes healthy, and a short start timeout
        using var env = new DaemonEnvironment(TimeSpan.FromMilliseconds(500));
        env.Server.BlockUntilCancelled = true;

        // Act: start, and wait for the daemon to end
        var start = await RunAsync(env.Services, "start", "--session", "slow", "--server", env.ServerDll);
        await env.Daemon!.WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken);

        // Assert: a start failure, the launched daemon was terminated by id and start time, and nothing remains
        Assert.Equal(4, start.ExitCode);
        Assert.Contains("did not become ready", start.Error, StringComparison.Ordinal);
        Assert.Contains((DaemonEnvironment.DaemonPid, env.DaemonStart), env.Killed);
        Assert.Empty(env.Store.ListIds());
        Assert.True(env.Store.TryReserve("slow"));
    }

    /// <summary>
    ///     Test that a stop whose daemon could not stop the server still ends that server (found through the session
    ///     file the daemon leaves behind) and removes the session.
    /// </summary>
    [Fact]
    public async Task ReviewCenterPuppet_Stop_DaemonTeardownFails_TerminatesRecordedServerAndRemovesSession()
    {
        // Arrange: a running session whose server cannot be disposed
        using var env = new DaemonEnvironment();
        var start = await RunAsync(env.Services, "start", "--session", "leaky", "--server", env.ServerDll);
        var recorded = env.Store.TryRead("leaky")!;
        env.Server.DisposeFailure = new PuppetException("The server would not stop.");

        // Act: stop it, then list
        var stop = await RunAsync(env.Services, "stop", "--session", "leaky");
        var list = await RunAsync(env.Services, "list");

        // Assert: the stop succeeded, the daemon reported failure, the leftover server was ended by id and start
        // time, and no session remains
        Assert.Equal(0, start.ExitCode);
        Assert.Equal(0, stop.ExitCode);
        Assert.Equal(4, await env.Daemon!);
        Assert.Contains((recorded.ServerPid, recorded.ServerStartTimeUtc), env.Killed);
        Assert.Empty(env.Store.ListIds());
        Assert.Contains("No active sessions.", list.Output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a start that fails while the daemon's teardown also fails waits for the daemon, ends the server
    ///     the failed teardown left behind, and leaves no session state.
    /// </summary>
    [Fact]
    public async Task ReviewCenterPuppet_Start_FailureWithFailedTeardown_TerminatesLeftoverServerAndLeavesNothingBehind()
    {
        // Arrange: a daemon whose browser cannot launch and whose server cannot be disposed
        using var env = new DaemonEnvironment();
        env.Browser.Failure = new PuppetException("Chromium is not installed.");
        env.Server.DisposeFailure = new PuppetException("The server would not stop.");

        // Act: start, and wait for the daemon to end
        var start = await RunAsync(env.Services, "start", "--session", "broken", "--server", env.ServerDll);
        var exit = await env.Daemon!.WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken);

        // Assert: reported as the original failure, the leftover server was ended by identity, and nothing remains
        Assert.Equal(4, start.ExitCode);
        Assert.Contains("Chromium is not installed.", start.Error, StringComparison.Ordinal);
        Assert.Equal(4, exit);
        Assert.Contains(env.Killed, k => k.Pid == Environment.ProcessId);
        Assert.Empty(env.Store.ListIds());
        Assert.True(env.Store.TryReserve("broken"));
    }

    /// <summary>
    ///     Test that the next command after a daemon died without cleaning up reports a stale session, ends the
    ///     processes it left behind, and removes the session.
    /// </summary>
    [Fact]
    public async Task ReviewCenterPuppet_Session_DaemonDiedWithoutCleanup_ReportsStaleAndEndsLeftoverProcesses()
    {
        // Arrange: a session file whose daemon is gone but whose server and browser are still recorded
        using var env = new DaemonEnvironment();
        var serverStart = env.DaemonStart.AddSeconds(5);
        var browserStart = env.DaemonStart.AddSeconds(8);
        env.Store.Write(new SessionInfo(
            "orphan", SessionStatus.Ready, 40000, "token", "http://127.0.0.1:5999", DaemonEnvironment.DeadPid,
            env.DaemonStart, 5001, serverStart, 5002, browserStart, null));

        // Act: run a command for that session, then list
        var result = await RunAsync(env.Services, "text", "x", "--session", "orphan");
        var list = await RunAsync(env.Services, "list");

        // Assert: exit 3, the leftover browser and server were ended by id and start time, and the session is gone
        Assert.Equal(PuppetProgram.ExitStaleSession, result.ExitCode);
        Assert.Contains((5002, browserStart), env.Killed);
        Assert.Contains((5001, serverStart), env.Killed);
        Assert.Empty(env.Store.ListIds());
        Assert.Contains("No active sessions.", list.Output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a request without the session token is rejected by a running session, that a session id that
    ///     could escape the session folder is refused, and that the session still serves its owner afterwards.
    /// </summary>
    [Fact]
    public async Task ReviewCenterPuppet_Security_WrongTokenAndUnsafeSessionId_AreRejected()
    {
        // Arrange: a running session
        using var env = new DaemonEnvironment();
        var start = await RunAsync(env.Services, "start", "--session", "guarded", "--server", env.ServerDll);
        var port = env.Store.TryRead("guarded")!.ControlPort;

        // Act: send a command with the wrong token straight to the control port, use an unsafe session id,
        // then use the session normally and stop it
        var forged = await ControlClient.SendAsync(
            port,
            new ControlRequest("not-the-token", "text", "hello-message", null, null, false, 5000),
            TimeSpan.FromSeconds(10),
            Xunit.TestContext.Current.CancellationToken);
        var unsafeId = await RunAsync(env.Services, "text", "x", "--session", "..\\..\\evil");
        var owner = await RunAsync(env.Services, "text", "hello-message", "--session", "guarded");
        var stop = await RunAsync(env.Services, "stop", "--session", "guarded");

        // Assert: the forged request was refused and never reached the page, the unsafe id failed, the owner's
        // command worked
        Assert.Equal(0, start.ExitCode);
        Assert.False(forged.Ok);
        Assert.Equal(PuppetProgram.ExitFailed, unsafeId.ExitCode);
        Assert.Equal(0, owner.ExitCode);
        Assert.Equal(["text [data-testid=\"hello-message\"]"], env.Browser.Page.Calls);
        Assert.Equal(0, stop.ExitCode);
    }

    /// <summary>
    ///     Test that on Unix platforms the session folder and the session file that holds the control token are
    ///     readable by their owner only while the session runs.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task ReviewCenterPuppet_Security_UnixPlatform_SessionFileIsOwnerOnly()
    {
        // Arrange: a running session (Windows relies on the per-user temporary folder instead)
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes do not exist on Windows.");
        using var env = new DaemonEnvironment();
        var start = await RunAsync(env.Services, "start", "--session", "private", "--server", env.ServerDll);

        // Act: read the modes of the session folder and the session file
        var folderMode = File.GetUnixFileMode(env.Store.Directory);
        var fileMode = File.GetUnixFileMode(env.Store.GetPath("private"));
        var stop = await RunAsync(env.Services, "stop", "--session", "private");

        // Assert: only the owner has access
        Assert.Equal(0, start.ExitCode);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, folderMode);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, fileMode);
        Assert.Equal(0, stop.ExitCode);
    }
}
