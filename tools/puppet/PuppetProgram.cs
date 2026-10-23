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

using System.Reflection;

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     The collaborators the Puppet commands use; replaced by fakes in tests.
/// </summary>
/// <param name="Store">The session file store.</param>
/// <param name="DaemonLauncher">Starts the detached session daemon.</param>
/// <param name="ServerRunner">Starts the ReviewCenter server (used inside the daemon).</param>
/// <param name="BrowserLauncher">Launches the browser (used inside the daemon).</param>
/// <param name="StartTimeout">
///     How long <c>start</c> waits for the session to become ready; <c>null</c> selects
///     <see cref="PuppetProgram.DefaultStartTimeout"/>. Tests shorten it.
/// </param>
/// <param name="StopTimeout">
///     How long <c>start</c> and <c>stop</c> wait for a daemon to finish its teardown before it is killed;
///     <c>null</c> selects <see cref="PuppetProgram.DefaultStopTimeout"/>. Tests shorten it.
/// </param>
internal sealed record PuppetServices(
    SessionStore Store,
    IDaemonLauncher DaemonLauncher,
    IServerRunner ServerRunner,
    IBrowserLauncher BrowserLauncher,
    TimeSpan? StartTimeout = null,
    TimeSpan? StopTimeout = null)
{
    /// <summary>
    ///     Gets the production collaborators.
    /// </summary>
    /// <returns>Services backed by the real file system, processes, and Playwright.</returns>
    public static PuppetServices CreateDefault()
    {
        return new PuppetServices(
            new SessionStore(),
            new DaemonLauncher(),
            new ServerProcessRunner(),
            new PlaywrightBrowserLauncher());
    }
}

/// <summary>
///     Entry point and command dispatcher of the Puppet tool.
/// </summary>
/// <remarks>
///     Puppet lets a developer (or a coding agent) look at and drive the ReviewCenter web UI from
///     the command line: <c>start</c> a session, then <c>goto</c>, <c>click</c>, <c>fill</c>, <c>text</c>,
///     <c>screenshot</c>, and <c>snapshot</c> against it, then <c>stop</c>. Each command is a separate short
///     process that talks to a long-lived daemon holding the server and browser (see
///     <see cref="DaemonHost"/>). Exit codes are stable so scripts can react to them.
/// </remarks>
internal static class PuppetProgram
{
    /// <summary>
    ///     Exit code: the command succeeded.
    /// </summary>
    public const int ExitOk = 0;

    /// <summary>
    ///     Exit code: bad arguments, or the command failed (missing element, timeout, ...).
    /// </summary>
    public const int ExitFailed = 1;

    /// <summary>
    ///     Exit code: the named session does not exist.
    /// </summary>
    public const int ExitUnknownSession = 2;

    /// <summary>
    ///     Exit code: the session existed but its daemon had died; it was cleaned up.
    /// </summary>
    public const int ExitStaleSession = 3;

    /// <summary>
    ///     Exit code: the session could not be started (server, browser, or Chromium missing).
    /// </summary>
    public const int ExitStartFailed = 4;

    /// <summary>
    ///     The environment variable that overrides the server assembly path.
    /// </summary>
    public const string ServerEnvironmentVariable = "REVIEWCENTER_SERVER_DLL";

    /// <summary>
    ///     The number of characters in a generated session id (the start of a GUID in "N" format); short
    ///     enough to type, long enough that two sessions practically never collide.
    /// </summary>
    private const int SessionIdLength = 8;

    /// <summary>
    ///     The default for how long <c>start</c> waits for the daemon to report that the session is ready;
    ///     covers starting the server (up to its own health timeout) plus launching Chromium.
    /// </summary>
    internal static readonly TimeSpan DefaultStartTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    ///     The default for how long <c>start</c> and <c>stop</c> wait for the daemon to finish its teardown before it is killed.
    /// </summary>
    internal static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How often <c>start</c> re-reads the session file while waiting for the daemon; short enough to
    ///     feel immediate, long enough not to spin.
    /// </summary>
    private static readonly TimeSpan StartPollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     How often <c>stop</c> checks whether the daemon has exited.
    /// </summary>
    private static readonly TimeSpan StopPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    ///     Extra time the client allows a control exchange beyond the command's own timeout, covering the
    ///     connection, the request, and the response transfer (for example a large screenshot message).
    /// </summary>
    private static readonly TimeSpan ControlExchangeGrace = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     The process entry point.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        return await RunAsync(args, Console.Out, Console.Error, PuppetServices.CreateDefault(), CancellationToken.None);
    }

    /// <summary>
    ///     Runs one Puppet invocation.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="output">Receives normal output.</param>
    /// <param name="error">Receives error messages.</param>
    /// <param name="services">The collaborators to use.</param>
    /// <param name="cancellationToken">Cancels long waits.</param>
    /// <returns>The exit code (see the <c>Exit*</c> constants).</returns>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        PuppetServices services,
        CancellationToken cancellationToken)
    {
        if (args.Length > 0 && args[0] is "--help" or "-h" or "-?")
        {
            PrintUsage(output);
            return ExitOk;
        }

        CommandLine command;
        try
        {
            command = CommandLine.Parse(args);
        }
        catch (ArgumentException ex)
        {
            WriteLine(error, ex.Message);
            return ExitFailed;
        }

        try
        {
            return command.Verb switch
            {
                CommandLine.StartVerb => await StartAsync(command, output, error, services, cancellationToken),
                CommandLine.DaemonVerb => await RunDaemonAsync(command, error, services, cancellationToken),
                "list" => await ListAsync(output, services),
                _ => await RunClientCommandAsync(command, output, error, services, cancellationToken)
            };
        }
        catch (PuppetException ex)
        {
            WriteLine(error, ex.Message);
            return ExitFailed;
        }
    }

    /// <summary>
    ///     Writes one line of text.
    /// </summary>
    /// <remarks>
    ///     The console writers are synchronous in practice; one helper keeps every call site free of
    ///     the sync-versus-async writer debate.
    /// </remarks>
    /// <param name="writer">Where to write.</param>
    /// <param name="message">The text of the line.</param>
    private static void WriteLine(TextWriter writer, string message)
    {
        writer.WriteLine(message);
    }

    /// <summary>
    ///     Prints the usage text.
    /// </summary>
    /// <param name="output">Where to print.</param>
    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine("Puppet - drive the ReviewCenter web UI in a headless browser");
        output.WriteLine();
        output.WriteLine("Usage: puppet <command> [arguments] [options]");
        output.WriteLine();
        output.WriteLine("Commands:");
        output.WriteLine("  start [--session <id>]      Start the server and browser; prints the session id");
        output.WriteLine("  goto <path>                 Navigate (for example: goto /)");
        output.WriteLine("  click <selector>            Click an element");
        output.WriteLine("  fill <selector> <text>      Type text into an input");
        output.WriteLine("  text <selector>             Print an element's text");
        output.WriteLine("  screenshot [--out <file>] [--full-page]   Save a PNG and print its path");
        output.WriteLine("  snapshot                    Print an accessibility snapshot of the page");
        output.WriteLine("  stop                        Stop the session (server and browser)");
        output.WriteLine("  list                        List sessions; stale ones are cleaned up");
        output.WriteLine();
        output.WriteLine("Selectors: a plain name such as hello-button means data-testid=\"hello-button\";");
        output.WriteLine("anything else (for example '#id', 'text=Hello') is passed to Playwright unchanged.");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine("  --session <id>              Session to use (default: $REVIEWCENTER_PUPPET_SESSION or the only session)");
        output.WriteLine("  --timeout <seconds>         Per-command timeout, 1-600 (default: 10)");
        output.WriteLine("  --server <dll>              (start) ReviewCenter server assembly to run");
        output.WriteLine();
        output.WriteLine("Exit codes: 0 ok, 1 failed/bad arguments, 2 unknown session, 3 stale session, 4 start failed");
        output.WriteLine("First-time setup: pwsh <output folder>/playwright.ps1 install chromium");
    }

    /// <summary>
    ///     Finds the server assembly to run.
    /// </summary>
    /// <param name="explicitPath">The <c>--server</c> value, or <c>null</c>.</param>
    /// <returns>The path of an existing server assembly.</returns>
    /// <exception cref="PuppetException">Thrown when no server assembly can be found.</exception>
    internal static string ResolveServerPath(string? explicitPath)
    {
        // Precedence: explicit option, then environment, then the path recorded at build time
        var path = explicitPath
                   ?? Environment.GetEnvironmentVariable(ServerEnvironmentVariable)
                   ?? typeof(PuppetProgram).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                       .FirstOrDefault(a => a.Key == "ServerDllPath")?.Value;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new PuppetException(
                $"The ReviewCenter server assembly was not found ({path ?? "no path configured"}). " +
                "Build the solution first, or pass --server <path to DemaConsulting.ReviewCenter.dll>.");
        }

        return Path.GetFullPath(path);
    }

    /// <summary>
    ///     Executes <c>start</c>: launches the daemon and waits until the session is ready.
    /// </summary>
    /// <param name="command">The parsed command.</param>
    /// <param name="output">Normal output.</param>
    /// <param name="error">Error output.</param>
    /// <param name="services">The collaborators.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The exit code.</returns>
    private static async Task<int> StartAsync(
        CommandLine command,
        TextWriter output,
        TextWriter error,
        PuppetServices services,
        CancellationToken cancellationToken)
    {
        var sessionId = command.SessionId ?? Guid.NewGuid().ToString("N")[..SessionIdLength];
        string serverPath;
        try
        {
            serverPath = ResolveServerPath(command.ServerPath);
        }
        catch (PuppetException ex)
        {
            WriteLine(error, ex.Message);
            return ExitStartFailed;
        }

        // Take the start lock first: of several simultaneous 'start' commands for one id, only one gets past here
        if (!services.Store.TryReserve(sessionId))
        {
            WriteLine(error, $"Session '{sessionId}' is already being started by another 'start' command.");
            return ExitFailed;
        }

        try
        {
            // Look under the lock: another start may have finished since the lock was last free. Refuse to
            // clobber a running session; quietly replace the leftovers of a dead one
            var existing = services.Store.Lookup(sessionId, out _, startLockHeld: true);
            if (existing == SessionLookup.Live)
            {
                WriteLine(error, $"Session '{sessionId}' is already running. Use 'stop --session {sessionId}' first.");
                return ExitFailed;
            }

            if (existing == SessionLookup.Stale)
            {
                WriteLine(output, $"Cleaned up a stale session file for '{sessionId}'.");
            }

            // A failure to launch leaves nothing behind: no session file exists yet and the lock is released below
            var daemon = services.DaemonLauncher.Launch(sessionId, serverPath);
            return await WaitUntilReadyAsync(sessionId, daemon, output, error, services, cancellationToken);
        }
        finally
        {
            services.Store.Release(sessionId);
        }
    }

    /// <summary>
    ///     Waits for a launched daemon to publish a ready session, cleaning up everything on any other outcome.
    /// </summary>
    /// <remarks>
    ///     The launched daemon's identity is known from the moment it is created, so a timeout, a
    ///     cancellation, or a daemon that died can stop the daemon (and any server or browser it
    ///     already recorded) even though no session file may have been written yet.
    /// </remarks>
    /// <param name="sessionId">The session id.</param>
    /// <param name="daemon">The identity of the launched daemon.</param>
    /// <param name="output">Normal output.</param>
    /// <param name="error">Error output.</param>
    /// <param name="services">The collaborators.</param>
    /// <param name="cancellationToken">Cancels the wait; the daemon is stopped before the cancellation propagates.</param>
    /// <returns>The exit code.</returns>
    private static async Task<int> WaitUntilReadyAsync(
        string sessionId,
        ProcessIdentity daemon,
        TextWriter output,
        TextWriter error,
        PuppetServices services,
        CancellationToken cancellationToken)
    {
        var timeout = services.StartTimeout ?? DefaultStartTimeout;
        var deadline = DateTime.UtcNow + timeout;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = services.Store.TryRead(sessionId);
                if (info is { Status: SessionStatus.Ready })
                {
                    WriteLine(output, $"Session: {sessionId}");
                    WriteLine(output, $"Server: {info.ServerUrl}");
                    return ExitOk;
                }

                if (info is { Status: SessionStatus.Failed })
                {
                    // The daemon publishes Failed before it tears the browser and server down: let it finish
                    // (or stop it), then remove the session while this start still holds the start lock
                    await WaitForDaemonExitAsync(daemon, services, cancellationToken);
                    AbortStart(sessionId, daemon, services.Store);
                    WriteLine(error, $"Could not start the session: {info.Error}");
                    return ExitStartFailed;
                }

                if (!services.Store.IsAlive(daemon))
                {
                    // One last look: the daemon may have published its result just before exiting
                    info = services.Store.TryRead(sessionId);
                    AbortStart(sessionId, daemon, services.Store);
                    WriteLine(
                        error,
                        $"The session daemon exited unexpectedly during start-up{(info?.Error is null ? "." : ": " + info.Error)}");
                    return ExitStartFailed;
                }

                await Task.Delay(StartPollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Do not leave a half-started daemon behind when the caller gives up
            AbortStart(sessionId, daemon, services.Store);
            throw;
        }

        // Do not leave a half-started daemon behind
        AbortStart(sessionId, daemon, services.Store);
        WriteLine(error, $"The session did not become ready within {timeout.TotalSeconds:0} seconds.");
        return ExitStartFailed;
    }

    /// <summary>
    ///     Abandons a start: stops the launched daemon and whatever it recorded, and removes the session file.
    /// </summary>
    /// <remarks>
    ///     Processes are always identified by id and start time, never by name. The launched daemon is
    ///     known even when it never wrote a session file; when it did, the file also lists the server and
    ///     browser it started.
    /// </remarks>
    /// <param name="sessionId">The session id.</param>
    /// <param name="daemon">The identity of the launched daemon.</param>
    /// <param name="store">The session store.</param>
    private static void AbortStart(string sessionId, ProcessIdentity daemon, SessionStore store)
    {
        var recorded = store.TryRead(sessionId);
        if (recorded is not null)
        {
            store.Terminate(recorded);
        }

        store.Terminate(daemon);
        store.Delete(sessionId);
    }


    /// <summary>
    ///     Runs the hidden <c>__daemon</c> verb: hosts the session until it is stopped.
    /// </summary>
    /// <param name="command">The parsed command; carries the session id and server path.</param>
    /// <param name="error">Error output (normally unobserved: the daemon has no console).</param>
    /// <param name="services">The collaborators.</param>
    /// <param name="cancellationToken">Stops the daemon.</param>
    /// <returns>The daemon exit code.</returns>
    private static async Task<int> RunDaemonAsync(
        CommandLine command,
        TextWriter error,
        PuppetServices services,
        CancellationToken cancellationToken)
    {
        if (command.SessionId is null || command.ServerPath is null)
        {
            WriteLine(error, "__daemon requires --session and --server.");
            return ExitFailed;
        }

        var host = new DaemonHost(services.Store, services.ServerRunner, services.BrowserLauncher);
        return await host.RunAsync(command.SessionId, command.ServerPath, cancellationToken);
    }

    /// <summary>
    ///     Executes <c>list</c>.
    /// </summary>
    /// <param name="output">Normal output.</param>
    /// <param name="services">The collaborators.</param>
    /// <returns>The exit code (always success).</returns>
    private static async Task<int> ListAsync(TextWriter output, PuppetServices services)
    {
        var printed = 0;
        foreach (var id in services.Store.ListIds())
        {
            var lookup = services.Store.Lookup(id, out var info);
            switch (lookup)
            {
                case SessionLookup.Live:
                    WriteLine(output, $"{id}  {info!.Status.ToString().ToLowerInvariant()}  {info.ServerUrl}  daemon pid {info.DaemonPid}");
                    printed++;
                    break;
                case SessionLookup.Stale:
                    WriteLine(output, $"{id}  stale (daemon not running; cleaned up)");
                    printed++;
                    break;
                case SessionLookup.Unknown:
                    // The file vanished between listing and lookup; nothing to report
                    break;
            }
        }

        if (printed == 0)
        {
            WriteLine(output, "No active sessions.");
        }

        return ExitOk;
    }

    /// <summary>
    ///     Executes a command that is forwarded to the session daemon (everything except start/list).
    /// </summary>
    /// <param name="command">The parsed command.</param>
    /// <param name="output">Normal output.</param>
    /// <param name="error">Error output.</param>
    /// <param name="services">The collaborators.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    /// <returns>The exit code.</returns>
    private static async Task<int> RunClientCommandAsync(
        CommandLine command,
        TextWriter output,
        TextWriter error,
        PuppetServices services,
        CancellationToken cancellationToken)
    {
        var (sessionId, problem, problemCode) = ResolveSession(command, services.Store);
        if (sessionId is null)
        {
            WriteLine(error, problem);
            return problemCode;
        }

        var lookup = services.Store.Lookup(sessionId, out var info);
        if (lookup == SessionLookup.Unknown)
        {
            WriteLine(error, $"Unknown session '{sessionId}'. Run 'list' to see sessions or 'start' to create one.");
            return ExitUnknownSession;
        }

        if (lookup == SessionLookup.Stale)
        {
            WriteLine(
                error,
                $"Session '{sessionId}' is stale: its daemon is no longer running. The leftovers were cleaned up; run 'start --session {sessionId}' to begin again.");
            return ExitStaleSession;
        }

        if (info!.Status != SessionStatus.Ready)
        {
            WriteLine(error, $"Session '{sessionId}' is not ready (status: {info.Status}).");
            return ExitFailed;
        }

        var request = BuildRequest(command, info);
        ControlResponse response;
        try
        {
            response = await ControlClient.SendAsync(
                info.ControlPort, request, TimeSpan.FromMilliseconds(command.TimeoutMilliseconds) + ControlExchangeGrace, cancellationToken);
        }
        catch (PuppetException) when (command.Verb == "stop")
        {
            // A daemon that cannot be asked to stop is stopped by force
            ForceStop(info, services.Store);
            WriteLine(output, $"Session '{sessionId}' stopped (forced).");
            return ExitOk;
        }

        if (command.Verb == "stop" && response.Ok)
        {
            await StopSessionAsync(info, services, cancellationToken);
            WriteLine(output, $"Session '{sessionId}' stopped.");
            return ExitOk;
        }

        if (response.Ok)
        {
            WriteLine(output, response.Message);
            return ExitOk;
        }

        WriteLine(error, response.Message);
        return response.ExitCode == 0 ? ExitFailed : response.ExitCode;
    }

    /// <summary>
    ///     Chooses the session a command applies to.
    /// </summary>
    /// <param name="command">The parsed command.</param>
    /// <param name="store">The session store.</param>
    /// <returns>
    ///     The session id; or, when none can be chosen, <c>null</c> with an explanation and exit code.
    /// </returns>
    private static (string? SessionId, string Problem, int ExitCode) ResolveSession(CommandLine command, SessionStore store)
    {
        var chosen = command.SessionId ?? Environment.GetEnvironmentVariable(CommandLine.SessionEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            return CommandLine.IsValidSessionId(chosen)
                ? (chosen, string.Empty, ExitOk)
                : (null, $"'{chosen}' is not a valid session id.", ExitFailed);
        }

        // No explicit choice: use the only live session, if there is exactly one
        var live = store.ListIds().Where(id => store.Lookup(id, out _) == SessionLookup.Live).ToList();
        return live.Count switch
        {
            1 => (live[0], string.Empty, ExitOk),
            0 => (null, "No session is running. Start one with 'start'.", ExitUnknownSession),
            _ => (null, $"Several sessions are running ({string.Join(", ", live)}); choose one with --session.", ExitFailed)
        };
    }

    /// <summary>
    ///     Builds the control request for a command.
    /// </summary>
    /// <param name="command">The parsed command.</param>
    /// <param name="info">The live session.</param>
    /// <returns>The request to send to the daemon.</returns>
    private static ControlRequest BuildRequest(CommandLine command, SessionInfo info)
    {
        var args = command.Positionals;
        return new ControlRequest(
            info.ControlToken,
            command.Verb,
            Selector: command.Verb is "click" or "fill" or "text" ? args[0] : null,
            Text: command.Verb switch { "goto" => args[0], "fill" => args[1], _ => null },
            // The daemon's working directory differs from the caller's, so relative paths are made absolute here
            OutputPath: command.OutputPath is null ? null : Path.GetFullPath(command.OutputPath),
            FullPage: command.FullPage,
            TimeoutMs: command.TimeoutMilliseconds);
    }

    /// <summary>
    ///     Waits for the daemon to finish its teardown after a stop request, then releases everything the
    ///     session recorded and removes its file, unless a newer session has replaced it.
    /// </summary>
    /// <param name="info">The session being stopped.</param>
    /// <param name="services">The collaborators.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the session has been released.</returns>
    private static async Task StopSessionAsync(SessionInfo info, PuppetServices services, CancellationToken cancellationToken)
    {
        await WaitUntilGoneAsync(() => services.Store.IsDaemonAlive(info), services, cancellationToken);

        // A daemon that ignored the request for too long is killed here; one whose teardown failed may have
        // left a server or browser behind, which its session file still records
        ForceStop(info, services.Store);
    }

    /// <summary>
    ///     Waits for a launched daemon to exit, up to the stop timeout.
    /// </summary>
    /// <param name="daemon">The identity of the daemon.</param>
    /// <param name="services">The collaborators.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the daemon is gone or the wait timed out.</returns>
    private static Task WaitForDaemonExitAsync(ProcessIdentity daemon, PuppetServices services, CancellationToken cancellationToken)
    {
        return WaitUntilGoneAsync(() => services.Store.IsAlive(daemon), services, cancellationToken);
    }

    /// <summary>
    ///     Polls until a process is gone or the stop timeout has passed.
    /// </summary>
    /// <param name="isAlive">Tells whether the process is still running.</param>
    /// <param name="services">The collaborators (carry the timeout).</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the process is gone or the timeout passed.</returns>
    private static async Task WaitUntilGoneAsync(Func<bool> isAlive, PuppetServices services, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + (services.StopTimeout ?? DefaultStopTimeout);
        while (DateTime.UtcNow < deadline && isAlive())
        {
            await Task.Delay(StopPollInterval, cancellationToken);
        }
    }

    /// <summary>
    ///     Kills the daemon, server, and browser of a session and deletes its file, unless a newer session has taken its place.
    /// </summary>
    /// <remarks>
    ///     The session file is re-read first: the daemon may have recorded more processes, or marked the
    ///     session failed, since <paramref name="info"/> was read. The file is then deleted only if it
    ///     still describes the same daemon, so a <c>start</c> that published a replacement in the
    ///     meantime keeps its session.
    /// </remarks>
    /// <param name="info">The session.</param>
    /// <param name="store">The session store.</param>
    private static void ForceStop(SessionInfo info, SessionStore store)
    {
        var current = store.TryRead(info.SessionId);
        store.Terminate(current is not null && SessionStore.IsSameSession(current, info) ? current : info);
        store.DeleteIfSame(info.SessionId, info);
    }
}
