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
///     Launches headless Chromium through Playwright and drives its single page.
/// </summary>
/// <remarks>
///     Playwright downloads its browsers separately from the NuGet package. When Chromium has not been
///     installed yet, launching fails with a cryptic message, so that case is detected and replaced
///     with the exact command that fixes it.
/// </remarks>
internal static class PlaywrightBrowserLauncher
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
    ///     Launches headless Chromium with one blank page.
    /// </summary>
    /// <returns>The page; disposing it closes the browser.</returns>
    /// <exception cref="PuppetException">Thrown when the browser cannot be launched.</exception>
    public static async Task<IBrowserPage> LaunchAsync()
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
            return new PlaywrightPage(playwright, browser, await context.NewPageAsync());
        }
        catch (PlaywrightException ex)
        {
            if (browser is not null)
            {
                await browser.DisposeAsync();
            }

            playwright?.Dispose();
            throw new PuppetException(DescribeLaunchFailure(ex), ex);
        }
    }

    /// <summary>
    ///     Describes a failed browser launch for the user.
    /// </summary>
    /// <param name="exception">The launch failure.</param>
    /// <returns>
    ///     The exact install command when Chromium is not installed; otherwise the first line of the failure.
    /// </returns>
    internal static string DescribeLaunchFailure(PlaywrightException exception)
    {
        // Playwright reports a missing browser binary with this wording
        if (exception.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("playwright install", StringComparison.OrdinalIgnoreCase))
        {
            var script = Path.Combine(AppContext.BaseDirectory, "playwright.ps1");
            return $"Chromium is not installed for Playwright. Install it once with: pwsh \"{script}\" install chromium";
        }

        return $"Could not launch Chromium: {FirstLine(exception.Message)}";
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
}

/// <summary>
///     <see cref="IBrowserPage"/> implemented on a Playwright page.
/// </summary>
/// <remarks>
///     Translates Playwright's exceptions into <see cref="PuppetException"/> messages that name the
///     selector and the timeout, so a failed step tells the user what to fix. Disposing it closes the
///     browser and releases Playwright.
/// </remarks>
/// <param name="playwright">The Playwright instance; disposed with the page.</param>
/// <param name="browser">The browser; closed with the page.</param>
/// <param name="page">The Playwright page.</param>
internal sealed class PlaywrightPage(IPlaywright playwright, IBrowser browser, IPage page) : IBrowserPage
{
    /// <summary>
    ///     The attribute ReviewCenter pages use to report that their live connection is established.
    /// </summary>
    private const string InteractiveAttribute = "data-interactive";

    /// <summary>
    ///     How long to let the page settle after a click or fill. A Blazor Server page re-renders over its
    ///     live connection after the event returns, so a text step that follows immediately could read the
    ///     old value; a short pause is the simplest remedy.
    /// </summary>
    private const float SettleMs = 300;

    /// <inheritdoc />
    public Task GotoAsync(string url, int timeoutMs)
    {
        return GuardAsync($"loading {url}", timeoutMs, async () =>
        {
            await page.GotoAsync(url, new PageGotoOptions { Timeout = timeoutMs });

            // A Blazor Server page is static HTML until its circuit connects; clicking before then does
            // nothing, so wait for the page's own marker instead of guessing with a sleep
            if (await page.Locator($"[{InteractiveAttribute}]").CountAsync() > 0)
            {
                await page.Locator($"[{InteractiveAttribute}='true']").First
                    .WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs });
            }
        });
    }

    /// <inheritdoc />
    public Task ClickAsync(string selector, int timeoutMs)
    {
        // Playwright waits for the element to exist and be clickable, up to the timeout
        return GuardAsync($"clicking {selector}", timeoutMs, async () =>
        {
            await page.Locator(selector).ClickAsync(new LocatorClickOptions { Timeout = timeoutMs });
            await page.WaitForTimeoutAsync(SettleMs);
        });
    }

    /// <inheritdoc />
    public Task FillAsync(string selector, string text, int timeoutMs)
    {
        return GuardAsync($"filling {selector}", timeoutMs, async () =>
        {
            await page.Locator(selector).FillAsync(text, new LocatorFillOptions { Timeout = timeoutMs });
            await page.WaitForTimeoutAsync(SettleMs);
        });
    }

    /// <inheritdoc />
    public async Task<string> TextAsync(string selector, int timeoutMs)
    {
        var text = string.Empty;
        await GuardAsync($"reading {selector}", timeoutMs, async () =>
            text = await page.Locator(selector).InnerTextAsync(new LocatorInnerTextOptions { Timeout = timeoutMs }));
        return text;
    }

    /// <inheritdoc />
    public async Task<byte[]> ScreenshotAsync(bool fullPage, int timeoutMs)
    {
        var bytes = Array.Empty<byte>();
        await GuardAsync("taking a screenshot", timeoutMs, async () =>
            bytes = await page.ScreenshotAsync(new PageScreenshotOptions { FullPage = fullPage, Type = ScreenshotType.Png, Timeout = timeoutMs }));
        return bytes;
    }

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

    /// <summary>
    ///     Runs a Playwright operation and converts its failures to user messages.
    /// </summary>
    /// <param name="what">What the operation does, for messages (for example "clicking [data-testid=\"x\"]").</param>
    /// <param name="timeoutMs">The timeout, for messages.</param>
    /// <param name="operation">The operation to run.</param>
    /// <returns>A task that completes when the operation has finished.</returns>
    /// <exception cref="PuppetException">Thrown when the operation times out or fails.</exception>
    private static async Task GuardAsync(string what, int timeoutMs, Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (TimeoutException)
        {
            throw new PuppetException($"Timed out after {timeoutMs / 1000.0:0.#} s {what}.");
        }
        catch (PlaywrightException ex)
        {
            throw new PuppetException($"Problem {what}: {PlaywrightBrowserLauncher.FirstLine(ex.Message)}", ex);
        }
    }
}
