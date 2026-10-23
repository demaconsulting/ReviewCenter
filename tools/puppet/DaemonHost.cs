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

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     A ReviewCenter server process started on behalf of a session.
/// </summary>
internal interface IRunningServer : IAsyncDisposable
{
    /// <summary>
    ///     Gets the base URL the server answers on (no trailing slash).
    /// </summary>
    string Url { get; }

    /// <summary>
    ///     Gets the process id of the server.
    /// </summary>
    int ProcessId { get; }

    /// <summary>
    ///     Gets the start time of the server process, in UTC.
    /// </summary>
    DateTime StartTimeUtc { get; }
}

/// <summary>
///     Starts ReviewCenter servers.
/// </summary>
internal interface IServerRunner
{
    /// <summary>
    ///     Starts a server on a free loopback port and waits until it is healthy.
    /// </summary>
    /// <param name="serverPath">The path of the ReviewCenter server assembly.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>The running server.</returns>
    /// <exception cref="PuppetException">Thrown when the server cannot be started or does not become healthy.</exception>
    Task<IRunningServer> StartAsync(string serverPath, CancellationToken cancellationToken);
}

/// <summary>
///     A launched browser and the page the session drives.
/// </summary>
internal interface IBrowserSession : IAsyncDisposable
{
    /// <summary>
    ///     Gets the page commands are executed against.
    /// </summary>
    IBrowserPage Page { get; }

    /// <summary>
    ///     Gets the process id of the Chromium browser process; 0 when it cannot be determined.
    /// </summary>
    int ProcessId { get; }

    /// <summary>
    ///     Gets the start time of the browser process, in UTC; meaningful only when <see cref="ProcessId"/> is not 0.
    /// </summary>
    DateTime StartTimeUtc { get; }
}

/// <summary>
///     Launches browsers.
/// </summary>
internal interface IBrowserLauncher
{
    /// <summary>
    ///     Launches a headless browser with one blank page.
    /// </summary>
    /// <returns>The browser session.</returns>
    /// <exception cref="PuppetException">Thrown when the browser cannot be launched (for example Chromium is not installed).</exception>
    Task<IBrowserSession> LaunchAsync();
}

/// <summary>
///     The long-lived process behind one session: owns the server, the browser, and the control channel.
/// </summary>
/// <remarks>
///     Browser state (the loaded page, a half-filled form, a signed-in user) must survive between
///     the separate command invocations a script makes, so something has to stay alive between them.
///     That something is this host. It publishes how to reach it in the session file, executes
///     commands one at a time, and tears everything down on <c>stop</c>, on an idle timeout, or when
///     start-up fails, so no server or browser is ever left behind. Collaborators are injected so the
///     whole lifecycle can be tested with fakes. Not thread-safe; one instance runs one session once.
/// </remarks>
internal sealed class DaemonHost
{
    /// <summary>
    ///     How long a session may sit without any command before it shuts itself down.
    /// </summary>
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    ///     The session file store.
    /// </summary>
    private readonly SessionStore _store;

    /// <summary>
    ///     Starts the server.
    /// </summary>
    private readonly IServerRunner _serverRunner;

    /// <summary>
    ///     Launches the browser.
    /// </summary>
    private readonly IBrowserLauncher _browserLauncher;

    /// <summary>
    ///     The idle shutdown delay.
    /// </summary>
    private readonly TimeSpan _idleTimeout;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DaemonHost"/> class.
    /// </summary>
    /// <param name="store">The session file store.</param>
    /// <param name="serverRunner">Starts the ReviewCenter server.</param>
    /// <param name="browserLauncher">Launches the browser.</param>
    /// <param name="idleTimeout">The idle shutdown delay; <c>null</c> selects <see cref="DefaultIdleTimeout"/>.</param>
    public DaemonHost(
        SessionStore store,
        IServerRunner serverRunner,
        IBrowserLauncher browserLauncher,
        TimeSpan? idleTimeout = null)
    {
        _store = store;
        _serverRunner = serverRunner;
        _browserLauncher = browserLauncher;
        _idleTimeout = idleTimeout ?? DefaultIdleTimeout;
    }

    /// <summary>
    ///     Runs a session from start-up until it is stopped.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="serverPath">The path of the server assembly to run.</param>
    /// <param name="cancellationToken">Stops the session from outside (used by tests).</param>
    /// <returns>
    ///     0 after a normal stop or idle shutdown; 4 when start-up failed or an unexpected failure ended
    ///     the session, in which case the session file is left behind with status
    ///     <see cref="SessionStatus.Failed"/> and the reason so the waiting <c>start</c> command (or the
    ///     next lookup, which finds the daemon gone and cleans up) can report it. The browser and the
    ///     server are released on every path.
    /// </returns>
    public async Task<int> RunAsync(string sessionId, string serverPath, CancellationToken cancellationToken)
    {
        using var stopSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = ControlServer.CreateToken();
        CommandExecutor? executor = null;

        using var self = Process.GetCurrentProcess();
        var daemonStart = self.StartTime.ToUniversalTime();

        using var control = new ControlServer(token, (request, _) => HandleAsync(request, executor, stopSource));
        var info = new SessionInfo(
            sessionId, SessionStatus.Starting, control.Port, token, string.Empty, self.Id, daemonStart, 0,
            default, 0, default, null);
        _store.Write(info);

        IRunningServer? server = null;
        IBrowserSession? browser = null;
        var exitCode = 0;
        try
        {
            // Start the server first: the browser has nothing to look at until it answers
            server = await _serverRunner.StartAsync(serverPath, cancellationToken);
            info = info with { ServerUrl = server.Url, ServerPid = server.ProcessId, ServerStartTimeUtc = server.StartTimeUtc };
            _store.Write(info);

            browser = await _browserLauncher.LaunchAsync();

            // Record the browser process so a later cleanup can stop it even if this daemon dies abruptly
            info = info with { BrowserPid = browser.ProcessId, BrowserStartTimeUtc = browser.StartTimeUtc };
            _store.Write(info);
            executor = new CommandExecutor(browser.Page, server.Url, sessionId, _store.Directory);

            // Publish readiness only when every command can actually be served
            info = info with { Status = SessionStatus.Ready };
            _store.Write(info);

            await control.RunAsync(_idleTimeout, stopSource.Token);
        }
        catch (PuppetException ex)
        {
            _store.Write(info with { Status = SessionStatus.Failed, Error = ex.Message });
            exitCode = 4;
        }
        catch (OperationCanceledException)
        {
            // Cancelled from outside while starting: behave like a stop
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Anything unanticipated is still a failed session, never a silent crash
            _store.Write(info with { Status = SessionStatus.Failed, Error = PuppetException.DescribeUnexpected(ex) });
            exitCode = 4;
        }
        finally
        {
            // Runs on every path so the browser and the server are never left behind
            try
            {
                await TearDownAsync(browser, server);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A failed disposal must not replace the exit code of the real failure. When it is the
                // only failure, keep the session file (marked failed, with the server's pid) so the
                // next lookup finds the daemon gone and cleans up whatever is still running
                if (exitCode == 0)
                {
                    var reason = ex is PuppetException ? ex.Message : PuppetException.DescribeUnexpected(ex);
                    _store.Write(info with { Status = SessionStatus.Failed, Error = reason });
                    exitCode = 4;
                }
            }
        }

        if (exitCode == 0)
        {
            _store.Delete(sessionId);
        }

        return exitCode;
    }

    /// <summary>
    ///     Handles one authenticated request: <c>stop</c> ends the session, everything else goes to the executor.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="executor">The command executor, or <c>null</c> while the session is still starting.</param>
    /// <param name="stopSource">Cancelled to end the session.</param>
    /// <returns>The response.</returns>
    private static Task<ControlResponse> HandleAsync(
        ControlRequest request,
        CommandExecutor? executor,
        CancellationTokenSource stopSource)
    {
        if (request.Command == "stop")
        {
            stopSource.Cancel();
            return Task.FromResult(new ControlResponse(true, 0, "Session stopping"));
        }

        return executor is null
            ? Task.FromResult(new ControlResponse(false, 1, "The session is still starting; try again shortly."))
            : executor.ExecuteAsync(request);
    }

    /// <summary>
    ///     Stops the browser and the server, in that order, ignoring ones that never started.
    /// </summary>
    /// <param name="browser">The browser, or <c>null</c>.</param>
    /// <param name="server">The server, or <c>null</c>.</param>
    /// <returns>A task that completes when both are disposed.</returns>
    private static async Task TearDownAsync(IBrowserSession? browser, IRunningServer? server)
    {
        // The browser goes first because it depends on the server; the finally block makes sure a
        // failure to close the browser cannot leave the server running
        try
        {
            if (browser is not null)
            {
                await browser.DisposeAsync();
            }
        }
        finally
        {
            if (server is not null)
            {
                await server.DisposeAsync();
            }
        }
    }
}
