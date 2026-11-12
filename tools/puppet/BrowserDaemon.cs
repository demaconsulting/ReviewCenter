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
using System.Net;
using System.Net.Sockets;
using DemaConsulting.ReviewCenter.Puppeting;

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     The long-running process behind <c>puppet start</c>: one <see cref="PuppetBrowser"/>, served over loopback TCP.
/// </summary>
/// <remarks>
///     The daemon never writes to the console, because it runs detached. It publishes the control file only
///     once the browser is up and the listener is bound; when it cannot start, the control file holds the error
///     instead. Cleanup is best effort.
/// </remarks>
internal static class BrowserDaemon
{
    /// <summary>
    ///     Runs the daemon until a <c>stop</c> command arrives.
    /// </summary>
    /// <param name="controlFile">The control file to publish.</param>
    /// <param name="baseUrl">The base URL for relative <c>goto</c> paths, or <c>null</c>.</param>
    /// <param name="viewport">The viewport as <c>WxH</c>.</param>
    /// <param name="headed">Whether to show the browser window.</param>
    /// <returns>The process exit code: 0 after a stop, 1 when the daemon could not start.</returns>
    public static async Task<int> RunAsync(string controlFile, string? baseUrl, string viewport, bool headed)
    {
        using var stopped = new CancellationTokenSource();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        PuppetBrowser? browser = null;
        var published = false;
        try
        {
            browser = await PuppetBrowser.StartAsync(baseUrl, viewport, headed);
            listener.Start();
            ControlChannel.Write(controlFile, new ControlInfo(((IPEndPoint)listener.LocalEndpoint).Port, baseUrl, null));
            published = true;
            await ControlChannel.ServeAsync(
                listener,
                async request =>
                {
                    if (request.Command != "stop")
                    {
                        return request.Command == "ping" ? new PuppetResponse(true, "pong", string.Empty, string.Empty, []) : await DispatchAsync(browser, request);
                    }

                    // Remove the file before answering, so a caller that sees the answer sees Puppet as stopped
                    File.Delete(controlFile);
                    await stopped.CancelAsync();
                    var (url, title) = await browser.UrlAndTitleAsync();
                    return new PuppetResponse(true, "stopped", url, title, browser.TakeNewEvents());
                },
                stopped.Token);
            return 0;
        }
        catch (PuppetingException ex) when (!published)
        {
            ControlChannel.Write(controlFile, new ControlInfo(null, null, ex.Message));
            return 1;
        }
        finally
        {
            listener.Stop();
            if (browser is not null)
            {
                await browser.DisposeAsync();
            }
        }
    }

    /// <summary>
    ///     Runs one browser command and attaches the page URL, title, and new events to the answer.
    /// </summary>
    /// <param name="browser">The browser.</param>
    /// <param name="request">The command.</param>
    /// <returns>The response; a failed command is an answer with <c>Ok</c> false, never an exception.</returns>
    private static async Task<PuppetResponse> DispatchAsync(PuppetBrowser browser, PuppetRequest request)
    {
        var ok = true;
        string output;
        try
        {
            output = await ExecuteAsync(browser, request);
        }
        catch (Exception ex)
        {
            // Any failure of one command is reported to the caller; the daemon keeps running
            ok = false;
            output = ex is PuppetingException or ArgumentException ? ex.Message : $"internal error: {ex.GetType().Name}: {FirstLine(ex.Message)}";
        }

        var (url, title) = await browser.UrlAndTitleAsync();
        return new PuppetResponse(ok, output, url, title, browser.TakeNewEvents());
    }

    /// <summary>
    ///     Maps one command of the control channel to the method of the browser that performs it.
    /// </summary>
    /// <param name="browser">The browser.</param>
    /// <param name="request">The command; the client has already checked its argument count and values.</param>
    /// <returns>The result text.</returns>
    /// <exception cref="PuppetingException">Thrown when the command fails or times out.</exception>
    internal static async Task<string> ExecuteAsync(PuppetBrowser browser, PuppetRequest request)
    {
        const string ok = "ok";
        var a = request.Args;
        switch (request.Command)
        {
            case "goto": return await browser.GotoAsync(a[0]);
            case "reload": return await browser.ReloadAsync();
            case "back": await browser.BackAsync(); return ok;
            case "click": await browser.ClickAsync(a[0]); return ok;
            case "dblclick": await browser.DoubleClickAsync(a[0]); return ok;
            case "hover": await browser.HoverAsync(a[0]); return ok;
            case "fill": await browser.FillAsync(a[0], a[1]); return ok;
            case "press": await browser.PressAsync(a[0], a.Length > 1 ? a[1] : null); return ok;
            case "type": await browser.TypeAsync(a[0]); return ok;
            case "select": await browser.SelectAsync(a[0], a[1]); return ok;
            case "check": await browser.CheckAsync(a[0]); return ok;
            case "uncheck": await browser.UncheckAsync(a[0]); return ok;
            case "upload": await browser.UploadAsync(a[0], a[1]); return ok;
            case "mouse-move": await browser.MouseMoveAsync(Number(a[0]), Number(a[1])); return ok;
            case "mouse-click": await browser.MouseClickAsync(Number(a[0]), Number(a[1])); return ok;
            case "wait":
                await browser.WaitForAsync(a[0], request.Options.TryGetValue("timeout", out var timeout) ? Number(timeout) : null);
                return ok;
            case "text": return await browser.TextAsync(a.FirstOrDefault());
            case "snapshot": return await browser.SnapshotAsync();
            case "screenshot":
                return await browser.ScreenshotAsync(a[0], request.Options.ContainsKey("full"), request.Options.GetValueOrDefault("selector"));
            case "viewport": await browser.SetViewportAsync(a[0]); return ok;
            case "theme": await browser.SetColorSchemeAsync(a[0] == "dark"); return ok;
            case "eval": return await browser.EvalAsync(a[0]);
            case "console": return browser.ConsoleText();
            case "reset": await browser.ResetAsync(); return ok;
            default: throw new PuppetingException($"Unknown command '{request.Command}'.");
        }
    }

    private static float Number(string text)
    {
        return float.Parse(text, CultureInfo.InvariantCulture);
    }

    private static string FirstLine(string message)
    {
        var index = message.IndexOfAny(['\r', '\n']);
        return index < 0 ? message : message[..index];
    }
}
