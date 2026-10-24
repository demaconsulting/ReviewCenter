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

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     Unit tests for the launch-failure message and URL resolution of <see cref="PlaywrightBrowser"/>.
/// </summary>
public class PlaywrightBrowserTests
{
    /// <summary>
    ///     Test that a missing Chromium is explained with the install command and other failures with their first line.
    /// </summary>
    [Fact]
    public void PlaywrightBrowser_DescribeLaunchFailure_ChromiumMissingOrOtherFailure_NamesFixOrFirstLine()
    {
        // Arrange: Playwright's wording for a missing browser, and an unrelated failure with several lines
        var missing = new PlaywrightException("BrowserType.LaunchAsync: Executable doesn't exist at C:\\x\\chrome.exe\nrun playwright install");
        var other = new PlaywrightException("Target closed\nmore details");

        // Act: describe both
        var missingText = PlaywrightBrowser.DescribeLaunchFailure(missing);
        var otherText = PlaywrightBrowser.DescribeLaunchFailure(other);

        // Assert: the first names the install script and command, the second only its first line
        Assert.Contains("playwright.ps1", missingText, StringComparison.Ordinal);
        Assert.Contains("install chromium", missingText, StringComparison.Ordinal);
        Assert.Equal("Could not launch Chromium: Target closed", otherText);
    }

    /// <summary>
    ///     Test that a path is resolved against the base URL, an absolute URL is kept, and a path without a base fails.
    /// </summary>
    [Fact]
    public void PlaywrightBrowser_ResolveUrl_PathAndAbsoluteAndMissingBase_ResolvesOrThrows()
    {
        // Arrange: a base URL
        const string baseUrl = "http://127.0.0.1:5199";

        // Act and assert: a path is joined, an absolute URL is untouched, a path without a base is refused
        Assert.Equal("http://127.0.0.1:5199/about", PlaywrightBrowser.ResolveUrl("/about", baseUrl));
        Assert.Equal("http://127.0.0.1:5199/about", PlaywrightBrowser.ResolveUrl("about", baseUrl + "/"));
        Assert.Equal("https://example.com/x", PlaywrightBrowser.ResolveUrl("https://example.com/x", baseUrl));
        Assert.Throws<PuppetException>(() => PlaywrightBrowser.ResolveUrl("/about", null));
    }
}
