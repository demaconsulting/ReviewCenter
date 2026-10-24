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

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     Drives one persistent headless Chromium and exposes it as commands.
/// </summary>
/// <remarks>
///     The Playwright objects live in one place: this class. Every command is a one-to-three line wrapper
///     in <see cref="ExecuteAsync"/>. Playwright keeps the mouse position on the page, so a hover stays in
///     effect until the next mouse command. There are no fixed sleeps: after a navigation the page's own
///     <c>data-interactive</c> marker is awaited, and the caller uses <c>wait</c> after async updates.
/// </remarks>
internal sealed class PlaywrightBrowser : IAsyncDisposable
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

    private PlaywrightBrowser(IPlaywright playwright, IBrowser browser, string? baseUrl, ViewportSize viewport)
    {
        _playwright = playwright;
        _browser = browser;
        _baseUrl = baseUrl;
        _viewport = viewport;
    }

    /// <summary>
    ///     Launches Chromium with one blank page.
    /// </summary>
    /// <param name="baseUrl">The base URL for relative <c>goto</c> paths, or <c>null</c>.</param>
    /// <param name="viewport">The viewport as <c>WxH</c>.</param>
    /// <param name="headed">Whether to show the browser window.</param>
    /// <returns>The browser; disposing it closes Chromium.</returns>
    /// <exception cref="PuppetException">Thrown when the browser cannot be launched.</exception>
    public static async Task<PlaywrightBrowser> LaunchAsync(string? baseUrl, string viewport, bool headed)
    {
        var size = ParseViewport(viewport);
        var playwright = await Playwright.CreateAsync();
        IBrowser? browser = null;
        try
        {
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = !headed });
            var result = new PlaywrightBrowser(playwright, browser, baseUrl, size);
            await result.NewPageAsync();
            return result;
        }
        catch (PlaywrightException ex)
        {
            // A browser that launched but got no page must not be left running
            await (browser?.CloseAsync() ?? Task.CompletedTask);
            playwright.Dispose();
            throw new PuppetException(DescribeLaunchFailure(ex));
        }
    }

    /// <summary>
    ///     Parses a viewport such as <c>1280x800</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The size.</returns>
    /// <exception cref="PuppetException">Thrown when the text is not <c>WxH</c> with positive whole numbers.</exception>
    public static ViewportSize ParseViewport(string text)
    {
        var parts = text.Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height) && width > 0 && height > 0)
        {
            return new ViewportSize { Width = width, Height = height };
        }

        throw new PuppetException($"The viewport must look like 1280x800, not '{text}'.");
    }

    /// <summary>
    ///     Resolves the argument of <c>goto</c> to an absolute URL.
    /// </summary>
    /// <param name="target">An absolute http(s) URL, or a path relative to the base URL.</param>
    /// <param name="baseUrl">The base URL, or <c>null</c> when none was given.</param>
    /// <returns>The absolute URL.</returns>
    /// <exception cref="PuppetException">Thrown for a relative path without a base URL.</exception>
    public static string ResolveUrl(string target, string? baseUrl)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            return target;
        }

        return baseUrl is null
            ? throw new PuppetException($"'{target}' is not an absolute URL; start Puppet with --url <base-url> to use paths.")
            : $"{baseUrl.TrimEnd('/')}/{target.TrimStart('/')}";
    }

    /// <summary>
    ///     Describes a failed browser launch for the user.
    /// </summary>
    /// <param name="exception">The launch failure.</param>
    /// <returns>
    ///     The exact install command when Chromium is not installed; otherwise the first line of the failure.
    /// </returns>
    public static string DescribeLaunchFailure(PlaywrightException exception)
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
    public static string FirstLine(string message)
    {
        var index = message.IndexOfAny(['\r', '\n']);
        return index < 0 ? message : message[..index];
    }

    /// <summary>
    ///     Runs one command on the page.
    /// </summary>
    /// <param name="request">The command; the client has already checked its argument count.</param>
    /// <returns>The result text.</returns>
    /// <exception cref="PuppetException">Thrown when the command fails or times out.</exception>
    public async Task<string> ExecuteAsync(PuppetRequest request)
    {
        var a = request.Args;
        var timeout = request.Options.TryGetValue("timeout", out var t) ? Number(t) : ActionTimeoutMs;
        var navigation = request.Command is "goto" or "reload" or "back";
        try
        {
            switch (request.Command)
            {
                case "goto": return await StatusAsync(await _page.GotoAsync(ResolveUrl(a[0], _baseUrl)));
                case "reload": return await StatusAsync(await _page.ReloadAsync());
                case "back": await _page.GoBackAsync(); await WaitInteractiveAsync(); return "ok";
                case "click": await Find(a[0]).ClickAsync(); return "ok";
                case "dblclick": await Find(a[0]).DblClickAsync(); return "ok";
                case "hover": await Find(a[0]).HoverAsync(); return "ok";
                case "fill": await Find(a[0]).FillAsync(a[1]); return "ok";
                case "press": await (a.Length > 1 ? Find(a[1]).PressAsync(a[0]) : _page.Keyboard.PressAsync(a[0])); return "ok";
                case "type": await _page.Keyboard.TypeAsync(a[0]); return "ok";
                case "select": await Find(a[0]).SelectOptionAsync(a[1]); return "ok";
                case "check": await Find(a[0]).CheckAsync(); return "ok";
                case "uncheck": await Find(a[0]).UncheckAsync(); return "ok";
                case "upload": await Find(a[0]).SetInputFilesAsync(a[1]); return "ok";
                case "mouse-move": await _page.Mouse.MoveAsync(Number(a[0]), Number(a[1])); return "ok";
                case "mouse-click": await _page.Mouse.ClickAsync(Number(a[0]), Number(a[1])); return "ok";
                case "wait": await Find(a[0]).First.WaitForAsync(new LocatorWaitForOptions { Timeout = timeout }); return "ok";
                case "text": return await TextAsync(a.FirstOrDefault());
                case "snapshot": return await _page.EvaluateAsync<string>(SnapshotScript);
                case "screenshot": return await ScreenshotAsync(a[0], request.Options);
                case "viewport":
                    _viewport = ParseViewport(a[0]);
                    await _page.SetViewportSizeAsync(_viewport.Width, _viewport.Height);
                    return "ok";
                case "theme":
                    _scheme = a[0] == "dark" ? ColorScheme.Dark : ColorScheme.Light;
                    await _page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = _scheme });
                    return "ok";
                case "eval": return await _page.EvaluateAsync<string?>("e => JSON.stringify(window.eval(e))", a[0]) ?? "null";
                case "console": return ConsoleText();
                case "reset": await _context.CloseAsync(); await NewPageAsync(); return "ok";
                default: throw new PuppetException($"Unknown command '{request.Command}'.");
            }
        }
        catch (TimeoutException)
        {
            throw new PuppetException(navigation
                ? $"navigation failed: timed out loading {a.FirstOrDefault() ?? "the page"}"
                : $"{request.Command} timed out after {timeout:0}ms (selector not found or not actionable): {string.Join(' ', a)}");
        }
        catch (PlaywrightException ex)
        {
            throw new PuppetException($"{(navigation ? "navigation" : request.Command)} failed: {FirstLine(ex.Message)}");
        }
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

    private static float Number(string text)
    {
        return float.Parse(text, CultureInfo.InvariantCulture);
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

    private async Task<string> TextAsync(string? selector)
    {
        var text = selector is null ? await _page.InnerTextAsync("body") : await Find(selector).InnerTextAsync();
        return text.Length > MaxText ? $"{text[..MaxText]}\n... truncated at {MaxText} characters" : text;
    }

    /// <summary>
    ///     Saves a screenshot of the page or of one element; it never moves the mouse.
    /// </summary>
    /// <param name="file">The absolute PNG path.</param>
    /// <param name="options">Optional <c>full</c> and <c>selector</c>.</param>
    /// <returns>The path and size.</returns>
    private async Task<string> ScreenshotAsync(string file, Dictionary<string, string> options)
    {
        if (options.TryGetValue("selector", out var selector))
        {
            await Find(selector).ScreenshotAsync(new LocatorScreenshotOptions { Path = file, Scale = ScreenshotScale.Css });
        }
        else
        {
            await _page.ScreenshotAsync(new PageScreenshotOptions { Path = file, FullPage = options.ContainsKey("full"), Scale = ScreenshotScale.Css });
        }

        return $"{file} ({new FileInfo(file).Length} bytes)";
    }

    /// <summary>
    ///     Lists the last 50 buffered events and marks everything as reported.
    /// </summary>
    /// <returns>The events, one per line.</returns>
    private string ConsoleText()
    {
        _reported = _events.Count;
        return _events.Count == 0 ? "(no console errors, page errors, or failed requests)" : string.Join('\n', _events.TakeLast(50));
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
