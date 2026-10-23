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

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     The browser operations the Puppet commands need, abstracted so they can be tested without a browser.
/// </summary>
/// <remarks>
///     Every method that waits takes the timeout in milliseconds and reports an expired wait or a
///     missing element by throwing <see cref="PuppetException"/> with a message fit for the user.
///     The production implementation drives Chromium through Playwright; tests use a fake.
/// </remarks>
internal interface IBrowserPage
{
    /// <summary>
    ///     Navigates to a URL and waits until the page is usable (for ReviewCenter pages: interactive).
    /// </summary>
    /// <param name="url">The absolute URL.</param>
    /// <param name="timeoutMs">The maximum time to wait.</param>
    /// <returns>A task that completes when the page is ready.</returns>
    Task GotoAsync(string url, int timeoutMs);

    /// <summary>
    ///     Clicks the element matching a selector.
    /// </summary>
    /// <param name="selector">The Playwright selector.</param>
    /// <param name="timeoutMs">The maximum time to wait for the element and the click.</param>
    /// <returns>A task that completes when the click was performed.</returns>
    Task ClickAsync(string selector, int timeoutMs);

    /// <summary>
    ///     Replaces the value of the input matching a selector.
    /// </summary>
    /// <param name="selector">The Playwright selector.</param>
    /// <param name="text">The text to enter.</param>
    /// <param name="timeoutMs">The maximum time to wait for the element and the edit.</param>
    /// <returns>A task that completes when the value was entered.</returns>
    Task FillAsync(string selector, string text, int timeoutMs);

    /// <summary>
    ///     Reads the visible text of the element matching a selector.
    /// </summary>
    /// <param name="selector">The Playwright selector.</param>
    /// <param name="timeoutMs">The maximum time to wait for the element.</param>
    /// <returns>The element's text.</returns>
    Task<string> TextAsync(string selector, int timeoutMs);

    /// <summary>
    ///     Captures a PNG screenshot.
    /// </summary>
    /// <param name="fullPage">Whether to capture the whole scrollable page instead of the viewport.</param>
    /// <param name="timeoutMs">The maximum time to wait for the screenshot.</param>
    /// <returns>The PNG bytes.</returns>
    Task<byte[]> ScreenshotAsync(bool fullPage, int timeoutMs);

    /// <summary>
    ///     Produces a textual snapshot of the page: its URL, title, accessibility tree, and test ids.
    /// </summary>
    /// <param name="timeoutMs">The maximum time to wait.</param>
    /// <returns>The snapshot text.</returns>
    Task<string> SnapshotAsync(int timeoutMs);
}

/// <summary>
///     Executes browser commands against a page and turns the outcome into a control response.
/// </summary>
/// <remarks>
///     This is the only place that knows what each verb means; the daemon just hands it requests.
///     Keeping it free of sockets and processes lets every verb and every failure mode be tested with a
///     fake page. Commands are executed one at a time by the caller. The class holds no mutable state.
/// </remarks>
internal sealed class CommandExecutor
{
    /// <summary>
    ///     The browser page commands run against.
    /// </summary>
    private readonly IBrowserPage _page;

    /// <summary>
    ///     The server's base URL (no trailing slash); relative <c>goto</c> paths are resolved against it.
    /// </summary>
    private readonly string _baseUrl;

    /// <summary>
    ///     The directory used when a screenshot request names no output path.
    /// </summary>
    private readonly string _screenshotDirectory;

    /// <summary>
    ///     The session id, used to name default screenshots.
    /// </summary>
    private readonly string _sessionId;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandExecutor"/> class.
    /// </summary>
    /// <param name="page">The page to drive.</param>
    /// <param name="baseUrl">The server base URL.</param>
    /// <param name="sessionId">The session id.</param>
    /// <param name="screenshotDirectory">The default screenshot directory.</param>
    public CommandExecutor(IBrowserPage page, string baseUrl, string sessionId, string screenshotDirectory)
    {
        _page = page;
        _baseUrl = baseUrl.TrimEnd('/');
        _sessionId = sessionId;
        _screenshotDirectory = screenshotDirectory;
    }

    /// <summary>
    ///     Executes one command.
    /// </summary>
    /// <param name="request">The request; its token has already been verified.</param>
    /// <returns>
    ///     A successful response carrying the command output, or a failed response (exit code 1)
    ///     whose message explains the problem. Never throws for anticipated failures.
    /// </returns>
    public async Task<ControlResponse> ExecuteAsync(ControlRequest request)
    {
        try
        {
            var output = request.Command switch
            {
                "goto" => await GotoAsync(request),
                "click" => await ClickAsync(request),
                "fill" => await FillAsync(request),
                "text" => await _page.TextAsync(RequireSelector(request), request.TimeoutMs),
                "screenshot" => await ScreenshotAsync(request),
                "snapshot" => await _page.SnapshotAsync(request.TimeoutMs),
                _ => throw new PuppetException($"Unknown command '{request.Command}'.")
            };

            return new ControlResponse(true, 0, output);
        }
        catch (PuppetException ex)
        {
            return new ControlResponse(false, 1, ex.Message);
        }
    }

    /// <summary>
    ///     Executes <c>goto</c>.
    /// </summary>
    /// <param name="request">The request; <c>Text</c> holds the path or URL.</param>
    /// <returns>A confirmation message.</returns>
    /// <exception cref="PuppetException">Thrown when the target is missing or is outside the session's server.</exception>
    private async Task<string> GotoAsync(ControlRequest request)
    {
        var target = request.Text;
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new PuppetException("goto requires a path such as '/'.");
        }

        // Paths are resolved against the session's server; absolute URLs must stay on that server so the
        // tool cannot be turned into a general-purpose browser pointed at arbitrary sites
        var url = target.StartsWith('/') ? _baseUrl + target : target;
        if (!url.StartsWith(_baseUrl + "/", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(url, _baseUrl, StringComparison.OrdinalIgnoreCase))
        {
            throw new PuppetException($"goto only navigates within the session's server ({_baseUrl}); '{target}' is outside it.");
        }

        await _page.GotoAsync(url, request.TimeoutMs);
        return $"Navigated to {url}";
    }

    /// <summary>
    ///     Executes <c>click</c>.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>A confirmation message.</returns>
    private async Task<string> ClickAsync(ControlRequest request)
    {
        var selector = RequireSelector(request);
        await _page.ClickAsync(selector, request.TimeoutMs);
        return $"Clicked {request.Selector}";
    }

    /// <summary>
    ///     Executes <c>fill</c>.
    /// </summary>
    /// <param name="request">The request; <c>Text</c> holds the value to enter.</param>
    /// <returns>A confirmation message.</returns>
    private async Task<string> FillAsync(ControlRequest request)
    {
        var selector = RequireSelector(request);
        await _page.FillAsync(selector, request.Text ?? string.Empty, request.TimeoutMs);
        return $"Filled {request.Selector}";
    }

    /// <summary>
    ///     Executes <c>screenshot</c>: captures the page and writes the PNG to disk.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The absolute path of the saved file.</returns>
    /// <exception cref="PuppetException">Thrown when the file cannot be written.</exception>
    private async Task<string> ScreenshotAsync(ControlRequest request)
    {
        var path = request.OutputPath ?? Path.Combine(
            _screenshotDirectory,
            $"{_sessionId}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
        path = Path.GetFullPath(path);

        var bytes = await _page.ScreenshotAsync(request.FullPage, request.TimeoutMs);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PuppetException($"Cannot write screenshot to '{path}': {ex.Message}", ex);
        }

        return path;
    }

    /// <summary>
    ///     Resolves the selector of a request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The Playwright selector.</returns>
    /// <exception cref="PuppetException">Thrown when the request has no selector.</exception>
    private static string RequireSelector(ControlRequest request)
    {
        return string.IsNullOrWhiteSpace(request.Selector)
            ? throw new PuppetException($"{request.Command} requires a selector.")
            : SelectorResolver.Resolve(request.Selector);
    }
}
