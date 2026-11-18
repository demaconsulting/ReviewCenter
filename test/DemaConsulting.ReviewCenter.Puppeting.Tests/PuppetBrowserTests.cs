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

namespace DemaConsulting.ReviewCenter.Puppeting.Tests;

/// <summary>
///     Unit tests for the launch-failure message, URL resolution, timeout, and waits of <see cref="PuppetBrowser"/>.
/// </summary>
public class PuppetBrowserTests
{
    /// <summary>
    ///     Test that a missing Chromium is explained with the install command and other failures with their first line.
    /// </summary>
    [Fact]
    public void PuppetBrowser_DescribeLaunchFailure_ChromiumMissingOrOtherFailure_NamesFixOrFirstLine()
    {
        // Arrange: Playwright's wording for a missing browser, and an unrelated failure with several lines
        var missing = new PlaywrightException("BrowserType.LaunchAsync: Executable doesn't exist at C:\\x\\chrome.exe\nrun playwright install");
        var other = new PlaywrightException("Target closed\nmore details");

        // Act: describe both
        var missingText = PuppetBrowser.DescribeLaunchFailure(missing);
        var otherText = PuppetBrowser.DescribeLaunchFailure(other);

        // Assert: the first names the install script and command, the second only its first line
        Assert.Contains("playwright.ps1", missingText, StringComparison.Ordinal);
        Assert.Contains("install chromium", missingText, StringComparison.Ordinal);
        Assert.Equal("Could not launch Chromium: Target closed", otherText);
    }

    /// <summary>
    ///     Test that a path is resolved against the base URL, an absolute URL is kept, and a path without a base fails.
    /// </summary>
    [Fact]
    public void PuppetBrowser_ResolveUrl_PathAndAbsoluteAndMissingBase_ResolvesOrThrows()
    {
        // Arrange: a base URL
        const string baseUrl = "http://127.0.0.1:5199";

        // Act and assert: a path is joined, an absolute URL is untouched, a path without a base is refused
        Assert.Equal("http://127.0.0.1:5199/about", PuppetBrowser.ResolveUrl("/about", baseUrl));
        Assert.Equal("http://127.0.0.1:5199/about", PuppetBrowser.ResolveUrl("about", baseUrl + "/"));
        Assert.Equal("https://example.com/x", PuppetBrowser.ResolveUrl("https://example.com/x", baseUrl));
        Assert.Throws<PuppetingException>(() => PuppetBrowser.ResolveUrl("/about", null));
    }

    /// <summary>
    ///     Test that a wait for an element that is already on a page whose live connection is not up yet also waits
    ///     for the page's interactive marker, because the live connection renders that element again.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task PuppetBrowser_WaitForAsync_ElementOnPageNotYetInteractive_WaitsForTheMarker()
    {
        // Arrange: a page with the element and a marker that becomes true a moment later
        await using var browser = await StartOrSkipAsync(PuppetBrowser.DefaultTimeoutMs);
        await browser.EvalAsync(
            "document.body.innerHTML = '<main data-interactive=\"false\"><p data-testid=\"item\">x</p></main>';" +
            "setTimeout(() => document.querySelector('main').setAttribute('data-interactive', 'true'), 500)");

        // Act
        await browser.WaitForAsync("item");

        // Assert: the wait returned only once the page was interactive
        Assert.Equal("\"true\"", await browser.EvalAsync("document.querySelector('main').getAttribute('data-interactive')"));
    }

    /// <summary>
    ///     Test that the timeout given at the start applies to a wait for an element that never appears.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task PuppetBrowser_WaitForAsync_StartTimeoutAndMissingElement_FailsAfterThatTimeout()
    {
        // Arrange: a browser with a short timeout
        await using var browser = await StartOrSkipAsync(300);

        // Act
        var failure = await Assert.ThrowsAsync<PuppetingException>(() => browser.WaitForAsync("missing"));

        // Assert: the message names the timeout of the start and the selector
        Assert.Equal("wait timed out after 300ms (selector not found or not actionable): missing", failure.Message);
    }

    /// <summary>
    ///     Test that a timeout that is not a positive number is refused before a browser is launched.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task PuppetBrowser_StartAsync_TimeoutNotPositive_Throws()
    {
        // Act and assert: zero and a negative timeout are refused with a message that names the timeout
        var zero = await Assert.ThrowsAsync<PuppetingException>(() => PuppetBrowser.StartAsync(null, timeoutMs: 0));
        Assert.Contains("timeout", zero.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<PuppetingException>(() => PuppetBrowser.StartAsync(null, timeoutMs: -1));
    }

    /// <summary>
    ///     Starts a browser without a base URL; a missing Chromium is a skip locally and a failure where it is required.
    /// </summary>
    /// <param name="timeoutMs">The timeout of the browser.</param>
    /// <returns>The browser.</returns>
    private static async Task<PuppetBrowser> StartOrSkipAsync(float timeoutMs)
    {
        try
        {
            return await PuppetBrowser.StartAsync(null, timeoutMs: timeoutMs);
        }
        catch (PuppetingException ex) when (ex.Message.Contains("Chromium", StringComparison.Ordinal))
        {
            Assert.False(
                string.Equals(Environment.GetEnvironmentVariable("REVIEWCENTER_REQUIRE_CHROMIUM"), "true", StringComparison.OrdinalIgnoreCase),
                $"REVIEWCENTER_REQUIRE_CHROMIUM is true but Chromium is unavailable: {ex.Message}");
            Assert.Skip(ex.Message);
            throw;
        }
    }
}
