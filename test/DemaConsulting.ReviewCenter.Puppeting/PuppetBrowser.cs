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

using System.Globalization;
using Microsoft.Playwright;

namespace DemaConsulting.ReviewCenter.Puppeting;

/// <summary>
///     Drives one headless Chromium page, as an object in the calling process.
/// </summary>
/// <remarks>
///     The Playwright objects live in one place: this class. Every operation is a short method; selectors are
///     resolved by <see cref="SelectorResolver"/>, so a plain name means a <c>data-testid</c>. Playwright keeps the
///     mouse position on the page, so a hover stays in effect until the next mouse operation. There are no fixed
///     sleeps: after a navigation the page's own <c>data-interactive</c> marker is awaited, and the caller uses
///     <see cref="WaitForAsync"/> after asynchronous updates. Every anticipated failure (a missing element, a timeout,
///     a browser that cannot start) is a <see cref="PuppetingException"/> with a one-line message.
/// </remarks>
public sealed class PuppetBrowser : IAsyncDisposable
{
    /// <summary>
    ///     The default viewport, as <c>WxH</c>.
    /// </summary>
    public const string DefaultViewport = "1280x800";

    /// <summary>
    ///     The default timeout of an action in milliseconds; short so a bad selector fails fast.
    /// </summary>
    private const float ActionTimeoutMs = 5000;

    /// <summary>
    ///     The most buffered console/page/request events; the oldest are dropped.
    /// </summary>
    private const int MaxEvents = 200;

    /// <summary>
    ///     The longest <c>text</c> result in characters.
    /// </summary>
    private const int MaxText = 8000;

    /// <summary>
    ///     Lists the visible interactive or test-id elements, at most 200, one per line.
    /// </summary>
    private const string SnapshotScript = """
        () => {
          const lines = [];
          const selector = 'a,button,input,select,textarea,summary,h1,h2,h3,[role],[data-testid],[tabindex]';
          for (const e of document.querySelectorAll(selector)) {
            const box = e.getBoundingClientRect();
            if (box.width === 0 || box.height === 0 || getComputedStyle(e).visibility === 'hidden') continue;
            const name = (e.getAttribute('aria-label') || e.innerText || e.value || e.placeholder || '')
              .trim().replace(/\s+/g, ' ').slice(0, 60);
            const flags = (e.disabled ? ' disabled' : '') + (e.checked ? ' checked' : '');
            lines.push(e.tagName.toLowerCase() + (e.getAttribute('role') ? ' role=' + e.getAttribute('role') : '') +
              ' "' + name + '"' + (e.dataset.testid ? ' testid=' + e.dataset.testid : '') + flags);
          }
          if (lines.length > 200) { const more = lines.length - 200; lines.length = 200; lines.push('... ' + more + ' more not shown'); }
          return lines.join('\n') || '(no visible elements)';
        }
        """;

    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly string? _baseUrl;
    private readonly List<string> _events = [];

    /// <summary>
    ///     How many buffered events were already reported to the caller.
    /// </summary>
    private int _reported;

    // Remembered so that reset keeps the viewport and the color scheme
    private ViewportSize _viewport;
    private ColorScheme _scheme = ColorScheme.Light;
    private IBrowserContext _context = null!;
    private IPage _page = null!;

    private PuppetBrowser(IPlaywright playwright, IBrowser browser, string? baseUrl, ViewportSize viewport)
    {
        _playwright = playwright;
        _browser = browser;
        _baseUrl = baseUrl;
        _viewport = viewport;
    }

    /// <summary>
    ///     Launches Chromium with one blank page.
    /// </summary>
    /// <param name="baseUrl">The base URL for relative <see cref="GotoAsync"/> paths, or <c>null</c>.</param>
    /// <param name="viewport">The viewport as <c>WxH</c>.</param>
    /// <param name="headed">Whether to show the browser window.</param>
    /// <returns>The browser; disposing it closes Chromium.</returns>
    /// <exception cref="PuppetingException">Thrown when the viewport is not valid or the browser cannot be launched.</exception>
    public static async Task<PuppetBrowser> StartAsync(string? baseUrl, string viewport = DefaultViewport, bool headed = false)
    {
        var (width, height) = ParseViewport(viewport);
        var playwright = await Playwright.CreateAsync();
        IBrowser? browser = null;
        try
        {
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = !headed });
            var result = new PuppetBrowser(playwright, browser, baseUrl?.TrimEnd('/'), new ViewportSize { Width = width, Height = height });
            await result.NewPageAsync();
            return result;
        }
        catch (PlaywrightException ex)
        {
            // A browser that launched but got no page must not be left running
            await (browser?.CloseAsync() ?? Task.CompletedTask);
            playwright.Dispose();
            throw new PuppetingException(DescribeLaunchFailure(ex));
        }
    }

    /// <summary>
    ///     Parses a viewport such as <c>1280x800</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The width and height.</returns>
    /// <exception cref="PuppetingException">Thrown when the text is not <c>WxH</c> with positive whole numbers.</exception>
    public static (int Width, int Height) ParseViewport(string text)
    {
        var parts = text.Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height) && width > 0 && height > 0)
        {
            return (width, height);
        }

        throw new PuppetingException($"The viewport must look like 1280x800, not '{text}'.");
    }

    /// <summary>
    ///     Resolves the target of <see cref="GotoAsync"/> to an absolute URL.
    /// </summary>
    /// <param name="target">An absolute http(s) URL, or a path relative to the base URL.</param>
    /// <param name="baseUrl">The base URL, or <c>null</c> when none was given.</param>
    /// <returns>The absolute URL.</returns>
    /// <exception cref="PuppetingException">Thrown for a relative path without a base URL.</exception>
    internal static string ResolveUrl(string target, string? baseUrl)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            return target;
        }

        return baseUrl is null
            ? throw new PuppetingException($"'{target}' is not an absolute URL; start Puppet with --url <base-url> to use paths.")
            : $"{baseUrl.TrimEnd('/')}/{target.TrimStart('/')}";
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

    /// <summary>
    ///     Opens a URL, or a path relative to the base URL, and waits until the page is interactive.
    /// </summary>
    /// <param name="target">An absolute http(s) URL, or a path relative to the base URL.</param>
    /// <returns><c>HTTP</c> and the status code of the response, or <c>ok</c> when there was none.</returns>
    public Task<string> GotoAsync(string target)
    {
        return NavigateAsync(target, async () => await StatusAsync(await _page.GotoAsync(ResolveUrl(target, _baseUrl))));
    }

    /// <summary>
    ///     Reloads the page and waits until it is interactive.
    /// </summary>
    /// <returns><c>HTTP</c> and the status code of the response, or <c>ok</c> when there was none.</returns>
    public Task<string> ReloadAsync()
    {
        return NavigateAsync(null, async () => await StatusAsync(await _page.ReloadAsync()));
    }

    /// <summary>
    ///     Goes back one page in the history and waits until the page is interactive.
    /// </summary>
    /// <returns>A task that completes when the page is interactive.</returns>
    public Task BackAsync()
    {
        return NavigateAsync(null, async () =>
        {
            await _page.GoBackAsync();
            await WaitInteractiveAsync();
            return "ok";
        });
    }

    /// <summary>
    ///     Clicks an element.
    /// </summary>
    /// <param name="selector">The selector; a plain name is a test identifier.</param>
    /// <returns>A task that completes when the element was clicked.</returns>
    public Task ClickAsync(string selector)
    {
        return ActAsync("click", [selector], () => Find(selector).ClickAsync());
    }

    /// <summary>
    ///     Double-clicks an element.
    /// </summary>
    /// <param name="selector">The selector; a plain name is a test identifier.</param>
    /// <returns>A task that completes when the element was double-clicked.</returns>
    public Task DoubleClickAsync(string selector)
    {
        return ActAsync("dblclick", [selector], () => Find(selector).DblClickAsync());
    }

    /// <summary>
    ///     Moves the mouse over an element; the hover stays until the next mouse operation.
    /// </summary>
    /// <param name="selector">The selector; a plain name is a test identifier.</param>
    /// <returns>A task that completes when the mouse is over the element.</returns>
    public Task HoverAsync(string selector)
    {
        return ActAsync("hover", [selector], () => Find(selector).HoverAsync());
    }

    /// <summary>
    ///     Replaces the value of a form field.
    /// </summary>
    /// <param name="selector">The selector; a plain name is a test identifier.</param>
    /// <param name="text">The new value.</param>
    /// <returns>A task that completes when the field holds the value.</returns>
    public Task FillAsync(string selector, string text)
    {
        return ActAsync("fill", [selector, text], () => Find(selector).FillAsync(text));
    }

    /// <summary>
    ///     Presses a key, on an element or on the page.
    /// </summary>
    /// <param name="key">The key, such as <c>Enter</c> or <c>Control+Enter</c>.</param>
    /// <param name="selector">The selector of the element to press it on, or <c>null</c> for the page.</param>
    /// <returns>A task that completes when the key was pressed.</returns>
    public Task PressAsync(string key, string? selector = null)
    {
        return ActAsync(
            "press",
            selector is null ? [key] : [key, selector],
            () => selector is null ? _page.Keyboard.PressAsync(key) : Find(selector).PressAsync(key));
    }

    /// <summary>
    ///     Types text with the keyboard into the focused element.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A task that completes when the text was typed.</returns>
    public Task TypeAsync(string text)
    {
        return ActAsync("type", [text], () => _page.Keyboard.TypeAsync(text));
    }

    /// <summary>
    ///     Selects an option of a select element.
    /// </summary>
    /// <param name="selector">The selector; a plain name is a test identifier.</param>
    /// <param name="value">The value or label of the option.</param>
    /// <returns>A task that completes when the option is selected.</returns>
    public Task SelectAsync(string selector, string value)
    {
        return ActAsync("select", [selector, value], () => Find(selector).SelectOptionAsync(value));
    }

    /// <summary>
    ///     Checks a check box or radio button.
    /// </summary>
    /// <param name="selector">The selector; a plain name is a test identifier.</param>
    /// <returns>A task that completes when the element is checked.</returns>
    public Task CheckAsync(string selector)
    {
        return ActAsync("check", [selector], () => Find(selector).CheckAsync());
    }

    /// <summary>
    ///     Unchecks a check box.
    /// </summary>
    /// <param name="selector">The selector; a plain name is a test identifier.</param>
    /// <returns>A task that completes when the element is unchecked.</returns>
    public Task UncheckAsync(string selector)
    {
        return ActAsync("uncheck", [selector], () => Find(selector).UncheckAsync());
    }

    /// <summary>
    ///     Sets the file of a file input.
    /// </summary>
    /// <param name="selector">The selector; a plain name is a test identifier.</param>
    /// <param name="file">The path of the file.</param>
    /// <returns>A task that completes when the file is set.</returns>
    public Task UploadAsync(string selector, string file)
    {
        return ActAsync("upload", [selector, file], () => Find(selector).SetInputFilesAsync(file));
    }

    /// <summary>
    ///     Moves the mouse to a point of the page.
    /// </summary>
    /// <param name="x">The horizontal position in CSS pixels.</param>
    /// <param name="y">The vertical position in CSS pixels.</param>
    /// <returns>A task that completes when the mouse has moved.</returns>
    public Task MouseMoveAsync(float x, float y)
    {
        return ActAsync("mouse-move", [Text(x), Text(y)], () => _page.Mouse.MoveAsync(x, y));
    }

    /// <summary>
    ///     Clicks a point of the page.
    /// </summary>
    /// <param name="x">The horizontal position in CSS pixels.</param>
    /// <param name="y">The vertical position in CSS pixels.</param>
    /// <returns>A task that completes when the point was clicked.</returns>
    public Task MouseClickAsync(float x, float y)
    {
        return ActAsync("mouse-click", [Text(x), Text(y)], () => _page.Mouse.ClickAsync(x, y));
    }

    /// <summary>
    ///     Waits until an element is on the page.
    /// </summary>
    /// <param name="selector">The selector; a plain name is a test identifier.</param>
    /// <param name="timeoutMs">The longest wait in milliseconds, or <c>null</c> for the default action timeout.</param>
    /// <returns>A task that completes when the element is there.</returns>
    public Task WaitForAsync(string selector, float? timeoutMs = null)
    {
        var timeout = timeoutMs ?? ActionTimeoutMs;
        return ActAsync("wait", [selector], () => Find(selector).First.WaitForAsync(new LocatorWaitForOptions { Timeout = timeout }), timeout);
    }

    /// <summary>
    ///     Reads the visible text of an element or of the whole page.
    /// </summary>
    /// <param name="selector">The selector, or <c>null</c> for the whole page.</param>
    /// <returns>The text, truncated after 8000 characters.</returns>
    public Task<string> TextAsync(string? selector = null)
    {
        return RunAsync("text", selector is null ? [] : [selector], ActionTimeoutMs, async () =>
        {
            var text = selector is null ? await _page.InnerTextAsync("body") : await Find(selector).InnerTextAsync();
            return text.Length > MaxText ? $"{text[..MaxText]}\n... truncated at {MaxText} characters" : text;
        });
    }

    /// <summary>
    ///     Lists the visible interactive or test-id elements, at most 200, one per line.
    /// </summary>
    /// <returns>The list.</returns>
    public Task<string> SnapshotAsync()
    {
        return RunAsync("snapshot", [], ActionTimeoutMs, () => _page.EvaluateAsync<string>(SnapshotScript));
    }

    /// <summary>
    ///     Saves a screenshot of the page or of one element; it never moves the mouse.
    /// </summary>
    /// <param name="file">The PNG path.</param>
    /// <param name="fullPage">Whether to capture the whole scrollable page instead of the viewport.</param>
    /// <param name="selector">The selector of one element to capture, or <c>null</c> for the page.</param>
    /// <returns>The path and size.</returns>
    public Task<string> ScreenshotAsync(string file, bool fullPage = false, string? selector = null)
    {
        return RunAsync("screenshot", [file], ActionTimeoutMs, async () =>
        {
            if (selector is not null)
            {
                await Find(selector).ScreenshotAsync(new LocatorScreenshotOptions { Path = file, Scale = ScreenshotScale.Css });
            }
            else
            {
                await _page.ScreenshotAsync(new PageScreenshotOptions { Path = file, FullPage = fullPage, Scale = ScreenshotScale.Css });
            }

            return $"{file} ({new FileInfo(file).Length} bytes)";
        });
    }

    /// <summary>
    ///     Changes the viewport; it is kept by <see cref="ResetAsync"/>.
    /// </summary>
    /// <param name="viewport">The viewport as <c>WxH</c>.</param>
    /// <returns>A task that completes when the viewport has changed.</returns>
    public Task SetViewportAsync(string viewport)
    {
        var (width, height) = ParseViewport(viewport);
        return ActAsync("viewport", [viewport], async () =>
        {
            _viewport = new ViewportSize { Width = width, Height = height };
            await _page.SetViewportSizeAsync(width, height);
        });
    }

    /// <summary>
    ///     Emulates the light or the dark color scheme of the operating system; it is kept by <see cref="ResetAsync"/>.
    /// </summary>
    /// <param name="dark">Whether to emulate the dark color scheme.</param>
    /// <returns>A task that completes when the color scheme applies.</returns>
    public Task SetColorSchemeAsync(bool dark)
    {
        return ActAsync("theme", [dark ? "dark" : "light"], async () =>
        {
            _scheme = dark ? ColorScheme.Dark : ColorScheme.Light;
            await _page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = _scheme });
        });
    }

    /// <summary>
    ///     Evaluates a JavaScript expression in the page.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The JSON text of the value; <c>null</c> for undefined.</returns>
    public Task<string> EvalAsync(string expression)
    {
        return RunAsync("eval", [expression], ActionTimeoutMs, async () =>
            await _page.EvaluateAsync<string?>("e => JSON.stringify(window.eval(e))", expression) ?? "null");
    }

    /// <summary>
    ///     Gets the text content of the element with a test identifier.
    /// </summary>
    /// <param name="testId">The test identifier.</param>
    /// <returns>The text content, or an empty text when the page has no such element.</returns>
    public Task<string> TextOfAsync(string testId)
    {
        return QueryAsync<string>("t => document.querySelector(`[data-testid=\"${t}\"]`)?.textContent ?? ''", testId);
    }

    /// <summary>
    ///     Tells whether the page has an element with a test identifier.
    /// </summary>
    /// <param name="testId">The test identifier.</param>
    /// <returns><c>true</c> when the page has the element.</returns>
    public Task<bool> ExistsAsync(string testId)
    {
        return QueryAsync<bool>("t => !!document.querySelector(`[data-testid=\"${t}\"]`)", testId);
    }

    /// <summary>
    ///     Tells whether the page has an element with a test identifier and it is disabled.
    /// </summary>
    /// <param name="testId">The test identifier.</param>
    /// <returns><c>true</c> when the page has the element and it is disabled.</returns>
    public Task<bool> IsDisabledAsync(string testId)
    {
        return QueryAsync<bool>("t => document.querySelector(`[data-testid=\"${t}\"]`)?.disabled === true", testId);
    }

    /// <summary>
    ///     Counts the elements whose test identifier starts with a prefix.
    /// </summary>
    /// <param name="prefix">The start of the test identifier.</param>
    /// <returns>The number of elements.</returns>
    public Task<int> CountAsync(string prefix)
    {
        return QueryAsync<int>("p => document.querySelectorAll(`[data-testid^=\"${p}\"]`).length", prefix);
    }

    /// <summary>
    ///     Lists the last 50 recorded console errors, page errors, and failed requests, and marks everything as reported.
    /// </summary>
    /// <returns>The events, one per line.</returns>
    public string ConsoleText()
    {
        _reported = _events.Count;
        return _events.Count == 0 ? "(no console errors, page errors, or failed requests)" : string.Join('\n', _events.TakeLast(50));
    }

    /// <summary>
    ///     Replaces the page by a new one in a new context (no cookies or storage), keeping the viewport and color scheme.
    /// </summary>
    /// <returns>A task that completes when the new page exists.</returns>
    public Task ResetAsync()
    {
        return ActAsync("reset", [], async () =>
        {
            await _context.CloseAsync();
            await NewPageAsync();
        });
    }

    /// <summary>
    ///     Gets the current URL and title; empty strings when the page cannot be queried (for example mid-navigation).
    /// </summary>
    /// <returns>The URL and title.</returns>
    public async Task<(string Url, string Title)> UrlAndTitleAsync()
    {
        try
        {
            return (_page.Url, await _page.TitleAsync());
        }
        catch (PlaywrightException)
        {
            return (string.Empty, string.Empty);
        }
    }

    /// <summary>
    ///     Gets the events recorded since the previous call and marks them as reported.
    /// </summary>
    /// <returns>The new events.</returns>
    public string[] TakeNewEvents()
    {
        var events = _events.Skip(_reported).ToArray();
        _reported = _events.Count;
        return events;
    }

    /// <summary>
    ///     Closes the browser and releases Playwright.
    /// </summary>
    /// <returns>A task that completes when everything is released.</returns>
    public async ValueTask DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
    }

    private static string Text(float value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private ILocator Find(string selector)
    {
        return _page.Locator(SelectorResolver.Resolve(selector));
    }

    /// <summary>
    ///     Waits for a Blazor Server page's live connection, so a click is not lost on static HTML.
    /// </summary>
    /// <returns>A task that completes when the page is interactive (or has no such marker).</returns>
    private async Task WaitInteractiveAsync()
    {
        if (await _page.Locator("[data-interactive]").CountAsync() > 0)
        {
            await _page.Locator("[data-interactive='true']").First.WaitForAsync();
        }
    }

    private async Task<string> StatusAsync(IResponse? response)
    {
        await WaitInteractiveAsync();
        return response is null ? "ok" : $"HTTP {response.Status}";
    }

    /// <summary>
    ///     Runs a navigation and reports a timeout or browser failure as a navigation failure.
    /// </summary>
    /// <param name="target">What is loaded, for the message, or <c>null</c> for the current page.</param>
    /// <param name="navigation">The navigation.</param>
    /// <returns>The result of the navigation.</returns>
    /// <exception cref="PuppetingException">Thrown when the navigation fails or times out.</exception>
    private static async Task<string> NavigateAsync(string? target, Func<Task<string>> navigation)
    {
        try
        {
            return await navigation();
        }
        catch (TimeoutException)
        {
            throw new PuppetingException($"navigation failed: timed out loading {target ?? "the page"}");
        }
        catch (PlaywrightException ex)
        {
            throw new PuppetingException($"navigation failed: {FirstLine(ex.Message)}");
        }
    }

    /// <summary>
    ///     Runs an operation without a result; see <see cref="RunAsync{T}"/>.
    /// </summary>
    /// <param name="operation">The name of the operation, for the message.</param>
    /// <param name="args">The arguments of the operation, for the message.</param>
    /// <param name="action">The operation.</param>
    /// <param name="timeoutMs">The timeout of the operation, for the message.</param>
    /// <returns>A task that completes when the operation has finished.</returns>
    private static Task ActAsync(string operation, string[] args, Func<Task> action, float timeoutMs = ActionTimeoutMs)
    {
        return RunAsync(operation, args, timeoutMs, async () =>
        {
            await action();
            return true;
        });
    }

    /// <summary>
    ///     Runs an operation on the page and reports a timeout or browser failure as a one-line message.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="operation">The name of the operation, for the message.</param>
    /// <param name="args">The arguments of the operation, for the message.</param>
    /// <param name="timeoutMs">The timeout of the operation, for the message.</param>
    /// <param name="action">The operation.</param>
    /// <returns>The result of the operation.</returns>
    /// <exception cref="PuppetingException">Thrown when the operation fails or times out.</exception>
    private static async Task<T> RunAsync<T>(string operation, string[] args, float timeoutMs, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (TimeoutException)
        {
            throw new PuppetingException(
                $"{operation} timed out after {timeoutMs:0}ms (selector not found or not actionable): {string.Join(' ', args)}");
        }
        catch (PlaywrightException ex)
        {
            throw new PuppetingException($"{operation} failed: {FirstLine(ex.Message)}");
        }
    }

    /// <summary>
    ///     Evaluates a JavaScript function of one text argument in the page.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="function">The function.</param>
    /// <param name="argument">The argument.</param>
    /// <returns>The result.</returns>
    private Task<T> QueryAsync<T>(string function, string argument)
    {
        return RunAsync("eval", [argument], ActionTimeoutMs, () => _page.EvaluateAsync<T>(function, argument));
    }
    private void Record(string text)
    {
        if (_events.Count >= MaxEvents)
        {
            _events.RemoveAt(0);
            _reported = Math.Max(0, _reported - 1);
        }

        _events.Add(text);
    }

    /// <summary>
    ///     Creates a context and page with the remembered viewport and color scheme, and starts recording events.
    /// </summary>
    /// <returns>A task that completes when the page exists.</returns>
    private async Task NewPageAsync()
    {
        _events.Clear();
        _reported = 0;
        _context = await _browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = _viewport, ColorScheme = _scheme });
        _context.SetDefaultTimeout(ActionTimeoutMs);
        _context.SetDefaultNavigationTimeout(30000);
        _page = await _context.NewPageAsync();
        _page.Console += (_, m) => { if (m.Type == "error") { Record($"console.error: {m.Text}"); } };
        _page.PageError += (_, m) => Record($"pageerror: {FirstLine(m)}");
        _page.RequestFailed += (_, r) => Record($"requestfailed: {r.Method} {r.Url} {r.Failure}");
    }
}
