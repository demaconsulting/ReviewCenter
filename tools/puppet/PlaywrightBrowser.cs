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

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     Launches headless Chromium through Playwright.
/// </summary>
/// <remarks>
///     Playwright downloads its browsers separately from the NuGet package. When Chromium has not been
///     installed yet, launching fails with a cryptic message, so that case is detected and replaced
///     with the exact command that fixes it.
/// </remarks>
internal sealed class PlaywrightBrowserLauncher : IBrowserLauncher
{
    /// <summary>
    ///     The width of the browser viewport in pixels; fixed so screenshots are reproducible.
    /// </summary>
    private const int ViewportWidth = 1280;

    /// <summary>
    ///     The height of the browser viewport in pixels.
    /// </summary>
    private const int ViewportHeight = 720;

    /// <summary>
    ///     Builds the message telling the user how to install Chromium.
    /// </summary>
    /// <returns>The message, naming the <c>playwright.ps1</c> script next to this executable.</returns>
    public static string BuildMissingBrowserMessage()
    {
        var script = Path.Combine(AppContext.BaseDirectory, "playwright.ps1");
        return $"Chromium is not installed for Playwright. Install it once with: pwsh \"{script}\" install chromium";
    }

    /// <summary>
    ///     Launches headless Chromium with one blank page.
    /// </summary>
    /// <returns>The browser session; disposing it closes the browser.</returns>
    /// <exception cref="PuppetException">Thrown when the browser cannot be launched.</exception>
    public async Task<IBrowserSession> LaunchAsync()
    {
        IPlaywright? playwright = null;
        IBrowser? browser = null;
        try
        {
            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = ViewportWidth, Height = ViewportHeight }
            });
            var page = await context.NewPageAsync();
            var (browserPid, browserStart) = await FindBrowserProcessAsync(browser);
            return new PlaywrightBrowserSession(playwright, browser, new PlaywrightPage(page), browserPid, browserStart);
        }
        catch (PlaywrightException ex)
        {
            if (browser is not null)
            {
                await browser.DisposeAsync();
            }

            playwright?.Dispose();

            // Playwright reports a missing browser binary with this wording
            var missing = ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) ||
                          ex.Message.Contains("playwright install", StringComparison.OrdinalIgnoreCase);
            throw new PuppetException(
                missing ? BuildMissingBrowserMessage() : $"Could not launch Chromium: {FirstLine(ex.Message)}",
                ex);
        }
    }

    /// <summary>
    ///     Finds the operating system process of the Chromium browser Playwright just launched.
    /// </summary>
    /// <remarks>
    ///     Playwright exposes no process id for a browser. Chromium reports its own through the
    ///     DevTools <c>SystemInfo.getProcessInfo</c> command, which is asked for here so the session
    ///     can record exactly the process it owns (and later terminate it by id and start time, never by
    ///     name). Failure to find it is not fatal: the session then simply has no browser process
    ///     recorded and relies on the daemon's process tree for cleanup.
    /// </remarks>
    /// <param name="browser">The launched browser.</param>
    /// <returns>The process id and start time; <c>(0, default)</c> when they cannot be determined.</returns>
    private static async Task<(int ProcessId, DateTime StartTimeUtc)> FindBrowserProcessAsync(IBrowser browser)
    {
        try
        {
            var session = await browser.NewBrowserCDPSessionAsync();
            return await QueryBrowserProcessAsync(session);
        }
        catch (PlaywrightException)
        {
            // Not Chromium or the browser went away: carry on without a recorded pid
            return (0, default);
        }
    }

    /// <summary>
    ///     Asks an open DevTools session for the browser's process, resolves its start time, and detaches
    ///     the session.
    /// </summary>
    /// <remarks>
    ///     The session serves this one query, so it is always detached afterwards (also when the query
    ///     fails); a browser that has already closed makes the detach fail, which is tolerated.
    /// </remarks>
    /// <param name="session">The DevTools session to query; detached before this method returns.</param>
    /// <returns>The process id and start time; <c>(0, default)</c> when they cannot be determined.</returns>
    internal static async Task<(int ProcessId, DateTime StartTimeUtc)> QueryBrowserProcessAsync(ICDPSession session)
    {
        try
        {
            var reply = await session.SendAsync("SystemInfo.getProcessInfo");
            if (reply is { } json && json.TryGetProperty("processInfo", out var processes))
            {
                foreach (var process in processes.EnumerateArray())
                {
                    // The entry of type "browser" is the main Chromium process
                    if (process.TryGetProperty("type", out var type) && type.GetString() == "browser" &&
                        process.TryGetProperty("id", out var id) && id.TryGetInt32(out var pid))
                    {
                        using var handle = System.Diagnostics.Process.GetProcessById(pid);
                        return (pid, handle.StartTime.ToUniversalTime());
                    }
                }
            }
        }
        catch (Exception ex) when (ex is PlaywrightException or ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            // Not Chromium, the command is unavailable, or the process vanished: carry on without a recorded pid
        }
        finally
        {
            try
            {
                await session.DetachAsync();
            }
            catch (Exception ex) when (ex is PlaywrightException or ObjectDisposedException)
            {
                // The browser already closed, which releases the session anyway
            }
        }

        return (0, default);
    }

    /// <summary>
    ///     Gets the first line of a possibly multi-line message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The text up to the first line break.</returns>
    internal static string FirstLine(string message)
    {
        var index = message.IndexOfAny(['\r', '\n']);
        return index < 0 ? message : message[..index];
    }

    /// <summary>
    ///     A launched Playwright instance, browser, and page.
    /// </summary>
    /// <param name="playwright">The Playwright instance; disposed with the session.</param>
    /// <param name="browser">The browser; closed with the session.</param>
    /// <param name="page">The page commands run against.</param>
    /// <param name="processId">The Chromium process id, or 0 when unknown.</param>
    /// <param name="startTimeUtc">The Chromium process start time, in UTC.</param>
    private sealed class PlaywrightBrowserSession(
        IPlaywright playwright,
        IBrowser browser,
        IBrowserPage page,
        int processId,
        DateTime startTimeUtc) : IBrowserSession
    {
        /// <inheritdoc />
        public IBrowserPage Page { get; } = page;

        /// <inheritdoc />
        public int ProcessId { get; } = processId;

        /// <inheritdoc />
        public DateTime StartTimeUtc { get; } = startTimeUtc;

        /// <summary>
        ///     Closes the browser and releases Playwright.
        /// </summary>
        /// <returns>A task that completes when everything is released.</returns>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await browser.CloseAsync();
            }
            catch (PlaywrightException)
            {
                // The browser already went away; releasing Playwright below finishes the cleanup
            }

            playwright.Dispose();
        }
    }
}

/// <summary>
///     <see cref="IBrowserPage"/> implemented on a Playwright page.
/// </summary>
/// <remarks>
///     Translates Playwright's exceptions into <see cref="PuppetException"/> messages that name the
///     selector and the timeout, so a failed command tells the user what to fix.
/// </remarks>
/// <param name="page">The Playwright page.</param>
internal sealed class PlaywrightPage(IPage page) : IBrowserPage
{
    /// <summary>
    ///     The attribute ReviewCenter pages use to report that their live connection is established.
    /// </summary>
    private const string InteractiveAttribute = "data-interactive";

    /// <summary>
    ///     Navigates and waits for the page to become interactive when it advertises that state.
    /// </summary>
    /// <param name="url">The absolute URL.</param>
    /// <param name="timeoutMs">The maximum time to wait.</param>
    /// <returns>A task that completes when the page is ready.</returns>
    /// <exception cref="PuppetException">Thrown on navigation failure or timeout.</exception>
    public async Task GotoAsync(string url, int timeoutMs)
    {
        try
        {
            await page.GotoAsync(url, new PageGotoOptions { Timeout = timeoutMs });

            // A Blazor Server page is static HTML until its circuit connects; clicking before then does
            // nothing, so wait for the page's own marker instead of guessing with a sleep
            if (await page.Locator($"[{InteractiveAttribute}]").CountAsync() > 0)
            {
                await page.Locator($"[{InteractiveAttribute}='true']").First
                    .WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs });
            }
        }
        catch (TimeoutException)
        {
            throw new PuppetException($"Timed out after {Seconds(timeoutMs)} s waiting for {url} to load and become interactive.");
        }
        catch (PlaywrightException ex)
        {
            throw new PuppetException($"Could not load {url}: {PlaywrightBrowserLauncher.FirstLine(ex.Message)}", ex);
        }
    }

    /// <summary>
    ///     Waits for an element and clicks it.
    /// </summary>
    /// <param name="selector">The Playwright selector.</param>
    /// <param name="timeoutMs">The maximum time to wait.</param>
    /// <returns>A task that completes when the click was performed.</returns>
    /// <exception cref="PuppetException">Thrown when the element is missing or cannot be clicked in time.</exception>
    public async Task ClickAsync(string selector, int timeoutMs)
    {
        var locator = await WaitForElementAsync(selector, timeoutMs);
        await RunAsync(selector, timeoutMs, "click", () => locator.ClickAsync(new LocatorClickOptions { Timeout = timeoutMs }));
    }

    /// <summary>
    ///     Waits for an input and replaces its value.
    /// </summary>
    /// <param name="selector">The Playwright selector.</param>
    /// <param name="text">The text to enter.</param>
    /// <param name="timeoutMs">The maximum time to wait.</param>
    /// <returns>A task that completes when the value was entered.</returns>
    /// <exception cref="PuppetException">Thrown when the element is missing or cannot be edited in time.</exception>
    public async Task FillAsync(string selector, string text, int timeoutMs)
    {
        var locator = await WaitForElementAsync(selector, timeoutMs);
        await RunAsync(selector, timeoutMs, "fill", () => locator.FillAsync(text, new LocatorFillOptions { Timeout = timeoutMs }));
    }

    /// <summary>
    ///     Waits for an element and reads its visible text.
    /// </summary>
    /// <param name="selector">The Playwright selector.</param>
    /// <param name="timeoutMs">The maximum time to wait.</param>
    /// <returns>The element's inner text.</returns>
    /// <exception cref="PuppetException">Thrown when the element is missing in time.</exception>
    public async Task<string> TextAsync(string selector, int timeoutMs)
    {
        var locator = await WaitForElementAsync(selector, timeoutMs);
        var text = string.Empty;
        await RunAsync(selector, timeoutMs, "read text", async () =>
            text = await locator.InnerTextAsync(new LocatorInnerTextOptions { Timeout = timeoutMs }));
        return text;
    }

    /// <summary>
    ///     Captures a PNG screenshot.
    /// </summary>
    /// <param name="fullPage">Whether to capture the whole scrollable page.</param>
    /// <param name="timeoutMs">The maximum time to wait for the screenshot.</param>
    /// <returns>The PNG bytes.</returns>
    /// <exception cref="PuppetException">Thrown when the screenshot cannot be taken in time (for example the browser went away).</exception>
    public async Task<byte[]> ScreenshotAsync(bool fullPage, int timeoutMs)
    {
        try
        {
            return await page.ScreenshotAsync(new PageScreenshotOptions { FullPage = fullPage, Type = ScreenshotType.Png, Timeout = timeoutMs });
        }
        catch (TimeoutException)
        {
            throw new PuppetException($"Timed out after {Seconds(timeoutMs)} s taking a screenshot.");
        }
        catch (PlaywrightException ex)
        {
            throw new PuppetException($"Could not take a screenshot: {PlaywrightBrowserLauncher.FirstLine(ex.Message)}", ex);
        }
    }

    /// <summary>
    ///     Produces a textual snapshot: URL, title, accessibility tree, and the test ids present.
    /// </summary>
    /// <param name="timeoutMs">The maximum time to wait for the accessibility tree.</param>
    /// <returns>The snapshot text.</returns>
    /// <exception cref="PuppetException">Thrown when the snapshot cannot be taken in time.</exception>
    public async Task<string> SnapshotAsync(int timeoutMs)
    {
        try
        {
            var tree = await page.Locator("body").AriaSnapshotAsync(new LocatorAriaSnapshotOptions { Timeout = timeoutMs });
            var ids = await page.EvaluateAsync<string[]>(
                "() => Array.from(document.querySelectorAll('[data-testid]')).map(e => e.getAttribute('data-testid'))");
            var title = await page.TitleAsync();
            return $"URL: {page.Url}{Environment.NewLine}Title: {title}{Environment.NewLine}{Environment.NewLine}" +
                   $"Accessibility tree:{Environment.NewLine}{tree}{Environment.NewLine}{Environment.NewLine}" +
                   $"Test ids: {string.Join(", ", ids)}";
        }
        catch (TimeoutException)
        {
            throw new PuppetException($"Timed out after {Seconds(timeoutMs)} s taking a snapshot.");
        }
        catch (PlaywrightException ex)
        {
            throw new PuppetException($"Could not take a snapshot: {PlaywrightBrowserLauncher.FirstLine(ex.Message)}", ex);
        }
    }

    /// <summary>
    ///     Waits for exactly one element to be attached to the page.
    /// </summary>
    /// <param name="selector">The Playwright selector.</param>
    /// <param name="timeoutMs">The maximum time to wait.</param>
    /// <returns>The locator for the element.</returns>
    /// <exception cref="PuppetException">Thrown when no element appears in time.</exception>
    private async Task<ILocator> WaitForElementAsync(string selector, int timeoutMs)
    {
        var locator = page.Locator(selector);
        try
        {
            await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = timeoutMs });
            return locator;
        }
        catch (TimeoutException)
        {
            throw new PuppetException($"No element matching {selector} appeared within {Seconds(timeoutMs)} s.");
        }
        catch (PlaywrightException ex)
        {
            throw new PuppetException($"Problem with selector {selector}: {PlaywrightBrowserLauncher.FirstLine(ex.Message)}", ex);
        }
    }

    /// <summary>
    ///     Runs an element action and converts Playwright failures to user messages.
    /// </summary>
    /// <param name="selector">The selector, for messages.</param>
    /// <param name="timeoutMs">The timeout, for messages.</param>
    /// <param name="action">The action's name, for messages.</param>
    /// <param name="operation">The operation to run.</param>
    /// <returns>A task that completes when the operation does.</returns>
    /// <exception cref="PuppetException">Thrown when the operation times out or fails.</exception>
    private static async Task RunAsync(string selector, int timeoutMs, string action, Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (TimeoutException)
        {
            throw new PuppetException($"Timed out after {Seconds(timeoutMs)} s trying to {action} {selector} (is it visible and enabled?).");
        }
        catch (PlaywrightException ex)
        {
            throw new PuppetException($"Could not {action} {selector}: {PlaywrightBrowserLauncher.FirstLine(ex.Message)}", ex);
        }
    }

    /// <summary>
    ///     Converts milliseconds to whole seconds for messages.
    /// </summary>
    /// <param name="timeoutMs">The timeout in milliseconds.</param>
    /// <returns>The timeout in seconds.</returns>
    private static int Seconds(int timeoutMs)
    {
        return timeoutMs / 1000;
    }
}
