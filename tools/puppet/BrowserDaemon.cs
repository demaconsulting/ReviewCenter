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

using System.Net;
using System.Net.Sockets;

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     The long-running process behind <c>puppet start</c>: one Chromium, served over loopback TCP.
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
        PlaywrightBrowser? browser = null;
        var published = false;
        try
        {
            browser = await PlaywrightBrowser.LaunchAsync(baseUrl, viewport, headed);
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
        catch (PuppetException ex) when (!published)
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
    private static async Task<PuppetResponse> DispatchAsync(PlaywrightBrowser browser, PuppetRequest request)
    {
        var ok = true;
        string output;
        try
        {
            output = await browser.ExecuteAsync(request);
        }
        catch (Exception ex)
        {
            // Any failure of one command is reported to the caller; the daemon keeps running
            ok = false;
            output = ex is PuppetException or ArgumentException ? ex.Message : $"internal error: {ex.GetType().Name}: {PlaywrightBrowser.FirstLine(ex.Message)}";
        }

        var (url, title) = await browser.UrlAndTitleAsync();
        return new PuppetResponse(ok, output, url, title, browser.TakeNewEvents());
    }
}
