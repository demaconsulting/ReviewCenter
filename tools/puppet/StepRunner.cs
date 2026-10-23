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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     The browser operations the steps need, abstracted so step execution can be tested without a browser.
/// </summary>
/// <remarks>
///     Every method that waits takes the timeout in milliseconds and reports an expired wait or a
///     missing element by throwing <see cref="PuppetException"/> with a message fit for the user.
///     Disposing the page closes the browser. The production implementation drives Chromium through
///     Playwright; tests use a fake.
/// </remarks>
internal interface IBrowserPage : IAsyncDisposable
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
}

/// <summary>
///     The outcome of one executed step, printed as one JSON line.
/// </summary>
/// <param name="Step">The 1-based step number.</param>
/// <param name="Kind">The step kind.</param>
/// <param name="Ok">Whether the step succeeded.</param>
/// <param name="Text">The element text (text steps only).</param>
/// <param name="File">The absolute screenshot path (screenshot steps only).</param>
/// <param name="Error">The failure message (failed steps only).</param>
/// <param name="FailureScreenshot">The absolute path of the screenshot taken after a failure, when it could be taken.</param>
internal sealed record StepResult(
    int Step,
    string Kind,
    bool Ok,
    string? Text = null,
    string? File = null,
    string? Error = null,
    string? FailureScreenshot = null)
{
    /// <summary>
    ///     The JSON settings for result lines: camelCase names, absent values omitted, quotes not escaped
    ///     (the output is read by people and by AI agents, not embedded in HTML).
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    ///     Formats the result as one compact JSON object.
    /// </summary>
    /// <returns>The JSON text without line breaks.</returns>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }
}

/// <summary>
///     Executes the steps of a steps file against a browser page, in order, stopping at the first failure.
/// </summary>
/// <remarks>
///     Stateless. Selectors are resolved with <see cref="SelectorResolver"/>; <c>goto</c> paths are joined
///     to the server base URL. When a step fails, a screenshot of the page is taken if that is cheap
///     (best effort; it never hides the real error).
/// </remarks>
internal static class StepRunner
{
    /// <summary>
    ///     Runs the steps.
    /// </summary>
    /// <param name="steps">The validated steps.</param>
    /// <param name="page">The page to drive.</param>
    /// <param name="baseUrl">The server base URL; <c>goto</c> paths are appended to it.</param>
    /// <param name="outDirectory">The folder that receives screenshots; created when missing.</param>
    /// <param name="timeoutMs">The wait limit of every step, in milliseconds.</param>
    /// <param name="report">Called with the result of each executed step as soon as it is known.</param>
    /// <param name="cancellationToken">Cancels the run between steps.</param>
    /// <returns><c>true</c> when every step succeeded; <c>false</c> when a step failed (later steps are not run).</returns>
    public static async Task<bool> RunAsync(
        IReadOnlyList<Step> steps,
        IBrowserPage page,
        string baseUrl,
        string outDirectory,
        int timeoutMs,
        Action<StepResult> report,
        CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                report(await ExecuteAsync(step, page, baseUrl, outDirectory, timeoutMs));
            }
            catch (PuppetException ex)
            {
                var failureShot = await TryFailureScreenshotAsync(step, page, outDirectory, timeoutMs);
                report(new StepResult(step.Number, step.Kind, false, Error: ex.Message, FailureScreenshot: failureShot));
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Executes one step.
    /// </summary>
    /// <param name="step">The step.</param>
    /// <param name="page">The page to drive.</param>
    /// <param name="baseUrl">The server base URL.</param>
    /// <param name="outDirectory">The screenshot folder.</param>
    /// <param name="timeoutMs">The wait limit in milliseconds.</param>
    /// <returns>The successful result.</returns>
    /// <exception cref="PuppetException">Thrown when the step fails.</exception>
    private static async Task<StepResult> ExecuteAsync(Step step, IBrowserPage page, string baseUrl, string outDirectory, int timeoutMs)
    {
        switch (step.Kind)
        {
            case "goto":
                await page.GotoAsync(baseUrl.TrimEnd('/') + step.Url, timeoutMs);
                return new StepResult(step.Number, step.Kind, true);

            case "click":
                await page.ClickAsync(SelectorResolver.Resolve(step.Selector!), timeoutMs);
                return new StepResult(step.Number, step.Kind, true);

            case "fill":
                await page.FillAsync(SelectorResolver.Resolve(step.Selector!), step.Value!, timeoutMs);
                return new StepResult(step.Number, step.Kind, true);

            case "text":
                var text = await page.TextAsync(SelectorResolver.Resolve(step.Selector!), timeoutMs);
                return new StepResult(step.Number, step.Kind, true, Text: text);

            default:
                var path = await SaveScreenshotAsync(page, outDirectory, step.File!, step.FullPage, timeoutMs);
                return new StepResult(step.Number, step.Kind, true, File: path);
        }
    }

    /// <summary>
    ///     Takes a screenshot and writes it to the output folder.
    /// </summary>
    /// <param name="page">The page to capture.</param>
    /// <param name="outDirectory">The screenshot folder.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="fullPage">Whether to capture the whole page.</param>
    /// <param name="timeoutMs">The wait limit in milliseconds.</param>
    /// <returns>The absolute path of the PNG file.</returns>
    /// <exception cref="PuppetException">Thrown when the screenshot cannot be taken or written.</exception>
    private static async Task<string> SaveScreenshotAsync(IBrowserPage page, string outDirectory, string fileName, bool fullPage, int timeoutMs)
    {
        var bytes = await page.ScreenshotAsync(fullPage, timeoutMs);
        var path = Path.Combine(outDirectory, fileName);
        try
        {
            Directory.CreateDirectory(outDirectory);
            await File.WriteAllBytesAsync(path, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PuppetException($"Could not write the screenshot '{path}': {ex.Message}", ex);
        }

        return path;
    }

    /// <summary>
    ///     Tries to capture the page after a failed step.
    /// </summary>
    /// <param name="step">The failed step.</param>
    /// <param name="page">The page to capture.</param>
    /// <param name="outDirectory">The screenshot folder.</param>
    /// <param name="timeoutMs">The wait limit in milliseconds.</param>
    /// <returns>The screenshot path, or <c>null</c> when it could not be taken.</returns>
    private static async Task<string?> TryFailureScreenshotAsync(Step step, IBrowserPage page, string outDirectory, int timeoutMs)
    {
        try
        {
            return await SaveScreenshotAsync(page, outDirectory, $"failure-step-{step.Number}.png", false, timeoutMs);
        }
        catch (PuppetException)
        {
            // The original failure is the useful one; a missing failure screenshot is not worth reporting
            return null;
        }
    }
}
