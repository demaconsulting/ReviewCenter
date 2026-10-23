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
using System.Reflection;
using Microsoft.Playwright;

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     A browser page that records the calls made to it instead of driving a browser.
/// </summary>
internal sealed class FakeBrowserPage : IBrowserPage
{
    /// <summary>
    ///     Gets the calls received, in order, as readable text such as <c>click [data-testid="x"]</c>.
    /// </summary>
    public List<string> Calls { get; } = [];

    /// <summary>
    ///     Gets or sets the text returned by <see cref="TextAsync"/>.
    /// </summary>
    public string TextResult { get; set; } = "fake text";

    /// <summary>
    ///     Gets or sets an exception thrown by element commands, to simulate a missing element or timeout.
    /// </summary>
    public PuppetException? Failure { get; set; }

    /// <summary>
    ///     Gets or sets an exception thrown by <see cref="ScreenshotAsync"/>, to simulate an unexpected failure such as a crashed browser.
    /// </summary>
    public Exception? UnexpectedFailure { get; set; }

    /// <inheritdoc />
    public Task GotoAsync(string url, int timeoutMs)
    {
        Calls.Add($"goto {url}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClickAsync(string selector, int timeoutMs)
    {
        Calls.Add($"click {selector}");
        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }

    /// <inheritdoc />
    public Task FillAsync(string selector, string text, int timeoutMs)
    {
        Calls.Add($"fill {selector} {text}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> TextAsync(string selector, int timeoutMs)
    {
        Calls.Add($"text {selector}");
        return Task.FromResult(TextResult);
    }

    /// <inheritdoc />
    public Task<byte[]> ScreenshotAsync(bool fullPage)
    {
        Calls.Add($"screenshot fullPage={fullPage}");
        if (UnexpectedFailure is not null)
        {
            return Task.FromException<byte[]>(UnexpectedFailure);
        }

        return Task.FromResult(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
    }

    /// <inheritdoc />
    public Task<string> SnapshotAsync(int timeoutMs)
    {
        Calls.Add("snapshot");
        return Task.FromResult("fake snapshot");
    }
}

/// <summary>
///     A server runner that reports a fixed URL instead of starting a process.
/// </summary>
internal sealed class FakeServerRunner : IServerRunner
{
    /// <summary>
    ///     Gets or sets an exception to throw from <see cref="StartAsync"/>.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether <see cref="StartAsync"/> never completes until it is cancelled,
    ///     to simulate a server that is too slow to become healthy.
    /// </summary>
    public bool BlockUntilCancelled { get; set; }

    /// <summary>
    ///     Gets or sets an exception thrown when a started server is disposed, to simulate a server process that cannot be stopped.
    /// </summary>
    public Exception? DisposeFailure { get; set; }

    /// <summary>
    ///     Gets the number of servers disposed so far.
    /// </summary>
    public int DisposedCount { get; private set; }

    /// <inheritdoc />
    public async Task<IRunningServer> StartAsync(string serverPath, CancellationToken cancellationToken)
    {
        if (BlockUntilCancelled)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        using var self = Process.GetCurrentProcess();
        return new FakeRunningServer(this, self.Id, self.StartTime.ToUniversalTime());
    }

    /// <summary>
    ///     A running server that only counts its disposal.
    /// </summary>
    /// <param name="owner">The runner that created the server.</param>
    /// <param name="processId">The process id to report.</param>
    /// <param name="startTimeUtc">The start time to report.</param>
    private sealed class FakeRunningServer(FakeServerRunner owner, int processId, DateTime startTimeUtc) : IRunningServer
    {
        /// <inheritdoc />
        public string Url => "http://127.0.0.1:5999";

        /// <inheritdoc />
        public int ProcessId => processId;

        /// <inheritdoc />
        public DateTime StartTimeUtc => startTimeUtc;

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            owner.DisposedCount++;
            return owner.DisposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(owner.DisposeFailure);
        }
    }
}

/// <summary>
///     A browser launcher that hands out a <see cref="FakeBrowserPage"/>.
/// </summary>
internal sealed class FakeBrowserLauncher : IBrowserLauncher
{
    /// <summary>
    ///     Gets the page the launched browser exposes.
    /// </summary>
    public FakeBrowserPage Page { get; } = new();

    /// <summary>
    ///     Gets or sets an exception to throw from <see cref="LaunchAsync"/>, such as a missing-Chromium error.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    ///     Gets or sets the browser process id the launched session reports; 0 means unknown.
    /// </summary>
    public int ReportedProcessId { get; set; }

    /// <summary>
    ///     Gets or sets the browser start time the launched session reports.
    /// </summary>
    public DateTime ReportedStartTimeUtc { get; set; }

    /// <summary>
    ///     Gets the number of browsers disposed so far.
    /// </summary>
    public int DisposedCount { get; private set; }

    /// <inheritdoc />
    public Task<IBrowserSession> LaunchAsync()
    {
        return Failure is null
            ? Task.FromResult<IBrowserSession>(new FakeBrowserSession(this))
            : Task.FromException<IBrowserSession>(Failure);
    }

    /// <summary>
    ///     A browser session that only counts its disposal.
    /// </summary>
    /// <param name="owner">The launcher that created the session.</param>
    private sealed class FakeBrowserSession(FakeBrowserLauncher owner) : IBrowserSession
    {
        /// <inheritdoc />
        public IBrowserPage Page => owner.Page;

        /// <inheritdoc />
        public int ProcessId => owner.ReportedProcessId;

        /// <inheritdoc />
        public DateTime StartTimeUtc => owner.ReportedStartTimeUtc;

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            owner.DisposedCount++;
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
///     A Playwright <see cref="IPage"/> whose screenshot always fails, built with a dispatch proxy so the
///     large interface does not have to be implemented by hand.
/// </summary>
public class FailingScreenshotPage : DispatchProxy
{
    /// <summary>
    ///     Gets or sets the exception the screenshot task faults with.
    /// </summary>
    public Exception? ScreenshotFailure { get; set; }

    /// <summary>
    ///     Creates a page whose screenshot faults with the given exception.
    /// </summary>
    /// <param name="failure">The exception the screenshot task faults with.</param>
    /// <returns>The page.</returns>
    public static IPage Create(Exception failure)
    {
        var page = Create<IPage, FailingScreenshotPage>();
        ((FailingScreenshotPage)(object)page).ScreenshotFailure = failure;
        return page;
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IPage.ScreenshotAsync))
        {
            return Task.FromException<byte[]>(ScreenshotFailure ?? new InvalidOperationException("No failure configured."));
        }

        throw new NotSupportedException($"The test page does not support {targetMethod?.Name}.");
    }
}

/// <summary>
///     A daemon launcher that records requests instead of starting a process.
/// </summary>
internal sealed class FakeDaemonLauncher : IDaemonLauncher
{
    /// <summary>
    ///     Gets or sets the action run when <see cref="Launch"/> is called, to simulate the daemon's session-file updates.
    /// </summary>
    public Action<string, string>? OnLaunch { get; set; }

    /// <summary>
    ///     Gets or sets an exception thrown by <see cref="Launch"/>, to simulate a process that cannot be created.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    ///     Gets or sets the process id reported for the "daemon".
    /// </summary>
    public int ReportedPid { get; set; } = Environment.ProcessId;

    /// <summary>
    ///     Gets or sets the start time reported for the "daemon".
    /// </summary>
    public DateTime ReportedStartTimeUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Gets the session ids launched so far.
    /// </summary>
    public List<string> Launched { get; } = [];

    /// <inheritdoc />
    public ProcessIdentity Launch(string sessionId, string serverPath)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Launched.Add(sessionId);
        OnLaunch?.Invoke(sessionId, serverPath);
        return new ProcessIdentity(ReportedPid, ReportedStartTimeUtc);
    }
}

/// <summary>
///     A temporary directory that is deleted when disposed.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TempDirectory"/> class and creates the directory.
    /// </summary>
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rc-puppet-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>
    ///     Gets the full path of the directory.
    /// </summary>
    public string Path { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp folder must not fail a test
        }
    }
}
