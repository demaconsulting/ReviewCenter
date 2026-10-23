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

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     Unit tests for the <see cref="CommandExecutor"/>, using a fake browser page.
/// </summary>
public class CommandExecutorTests
{
    /// <summary>
    ///     The server URL the executor under test belongs to.
    /// </summary>
    private const string BaseUrl = "http://127.0.0.1:5999";

    /// <summary>
    ///     Creates a request for tests.
    /// </summary>
    /// <param name="command">The command name.</param>
    /// <param name="selector">The selector, if any.</param>
    /// <param name="text">The text or path argument, if any.</param>
    /// <param name="outputPath">The screenshot path, if any.</param>
    /// <returns>A control request.</returns>
    private static ControlRequest CreateRequest(string command, string? selector = null, string? text = null, string? outputPath = null)
    {
        return new ControlRequest("t", command, selector, text, outputPath, false, 5000);
    }

    /// <summary>
    ///     Test that goto of a rooted path navigates within the session's server.
    /// </summary>
    [Fact]
    public async Task CommandExecutor_Execute_GotoRootedPath_NavigatesWithinServer()
    {
        // Arrange: an executor over a fake page
        var page = new FakeBrowserPage();
        var executor = new CommandExecutor(page, BaseUrl + "/", "s", Path.GetTempPath());

        // Act: goto /
        var response = await executor.ExecuteAsync(CreateRequest("goto", text: "/"));

        // Assert: the page was sent to the server root
        Assert.True(response.Ok);
        Assert.Equal([$"goto {BaseUrl}/"], page.Calls);
    }

    /// <summary>
    ///     Test that goto to another origin is refused so the tool cannot be used to browse elsewhere.
    /// </summary>
    [Fact]
    public async Task CommandExecutor_Execute_GotoForeignUrl_IsRefused()
    {
        // Arrange: an executor over a fake page
        var page = new FakeBrowserPage();
        var executor = new CommandExecutor(page, BaseUrl, "s", Path.GetTempPath());

        // Act: goto an external site
        var response = await executor.ExecuteAsync(CreateRequest("goto", text: "https://example.com/"));

        // Assert: refused, and no navigation happened
        Assert.False(response.Ok);
        Assert.Contains("outside", response.Message, StringComparison.Ordinal);
        Assert.Empty(page.Calls);
    }

    /// <summary>
    ///     Test that a plain selector reaches the page as a data-testid selector.
    /// </summary>
    [Fact]
    public async Task CommandExecutor_Execute_ClickPlainName_UsesDataTestIdSelector()
    {
        // Arrange: an executor over a fake page
        var page = new FakeBrowserPage();
        var executor = new CommandExecutor(page, BaseUrl, "s", Path.GetTempPath());

        // Act: click hello-button
        var response = await executor.ExecuteAsync(CreateRequest("click", "hello-button"));

        // Assert: the page was asked to click the data-testid
        Assert.True(response.Ok);
        Assert.Equal(["click [data-testid=\"hello-button\"]"], page.Calls);
    }

    /// <summary>
    ///     Test that text returns the element text as the response message.
    /// </summary>
    [Fact]
    public async Task CommandExecutor_Execute_Text_ReturnsElementText()
    {
        // Arrange: a page that reports some text
        var page = new FakeBrowserPage { TextResult = "Hello from ReviewCenter!" };
        var executor = new CommandExecutor(page, BaseUrl, "s", Path.GetTempPath());

        // Act: read the text
        var response = await executor.ExecuteAsync(CreateRequest("text", "hello-message"));

        // Assert: the message is the text
        Assert.Equal("Hello from ReviewCenter!", response.Message);
    }

    /// <summary>
    ///     Test that fill sends the text to the page.
    /// </summary>
    [Fact]
    public async Task CommandExecutor_Execute_Fill_SendsTextToPage()
    {
        // Arrange: an executor over a fake page
        var page = new FakeBrowserPage();
        var executor = new CommandExecutor(page, BaseUrl, "s", Path.GetTempPath());

        // Act: fill an input
        var response = await executor.ExecuteAsync(CreateRequest("fill", "#name", "Ada"));

        // Assert: the page received the text
        Assert.True(response.Ok);
        Assert.Equal(["fill #name Ada"], page.Calls);
    }

    /// <summary>
    ///     Test that a page failure such as a missing element becomes a failed response with the page's message.
    /// </summary>
    [Fact]
    public async Task CommandExecutor_Execute_MissingElement_ReturnsFailureMessage()
    {
        // Arrange: a page whose click fails
        var page = new FakeBrowserPage { Failure = new PuppetException("No element matching [data-testid=\"x\"] appeared within 10 s.") };
        var executor = new CommandExecutor(page, BaseUrl, "s", Path.GetTempPath());

        // Act: click it
        var response = await executor.ExecuteAsync(CreateRequest("click", "x"));

        // Assert: failed with a nonzero exit code and the message
        Assert.False(response.Ok);
        Assert.Equal(1, response.ExitCode);
        Assert.Contains("No element", response.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that screenshot with an explicit path writes the file and returns its absolute path.
    /// </summary>
    [Fact]
    public async Task CommandExecutor_Execute_ScreenshotWithOut_WritesFileAndReturnsPath()
    {
        // Arrange: an executor and a target path in a temp folder
        using var temp = new TempDirectory();
        var page = new FakeBrowserPage();
        var executor = new CommandExecutor(page, BaseUrl, "s", temp.Path);
        var target = Path.Combine(temp.Path, "sub", "shot.png");

        // Act: take a screenshot
        var response = await executor.ExecuteAsync(CreateRequest("screenshot", outputPath: target));

        // Assert: the file exists and the path is reported
        Assert.True(response.Ok);
        Assert.Equal(target, response.Message);
        Assert.True(File.Exists(target));
    }

    /// <summary>
    ///     Test that screenshot without a path saves into the session folder with the session id in the name.
    /// </summary>
    [Fact]
    public async Task CommandExecutor_Execute_ScreenshotWithoutOut_UsesDefaultLocation()
    {
        // Arrange: an executor whose default folder is a temp folder
        using var temp = new TempDirectory();
        var executor = new CommandExecutor(new FakeBrowserPage(), BaseUrl, "demo", temp.Path);

        // Act: take a screenshot
        var response = await executor.ExecuteAsync(CreateRequest("screenshot"));

        // Assert: saved in the folder as demo-<timestamp>.png
        Assert.True(response.Ok);
        Assert.Equal(temp.Path, Path.GetDirectoryName(response.Message));
        Assert.StartsWith("demo-", Path.GetFileName(response.Message), StringComparison.Ordinal);
        Assert.EndsWith(".png", response.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(response.Message));
    }

    /// <summary>
    ///     Test that screenshot receives the request's per-command timeout, so it cannot outlive the client exchange.
    /// </summary>
    [Fact]
    public async Task CommandExecutor_Execute_Screenshot_PassesRequestTimeoutToPage()
    {
        // Arrange: an executor over a fake page and a request with a distinctive timeout
        using var temp = new TempDirectory();
        var page = new FakeBrowserPage();
        var executor = new CommandExecutor(page, BaseUrl, "s", temp.Path);
        var request = new ControlRequest("t", "screenshot", null, null, null, false, 1234);

        // Act: take a screenshot
        var response = await executor.ExecuteAsync(request);

        // Assert: the page was given the request's timeout
        Assert.True(response.Ok);
        Assert.Equal([1234], page.ScreenshotTimeoutsMs);
    }

    /// <summary>
    ///     Test that snapshot returns the page's snapshot text.
    /// </summary>
    [Fact]
    public async Task CommandExecutor_Execute_Snapshot_ReturnsSnapshotText()
    {
        // Arrange: an executor over a fake page
        var executor = new CommandExecutor(new FakeBrowserPage(), BaseUrl, "s", Path.GetTempPath());

        // Act: snapshot
        var response = await executor.ExecuteAsync(CreateRequest("snapshot"));

        // Assert: the snapshot text is returned
        Assert.Equal("fake snapshot", response.Message);
    }

    /// <summary>
    ///     Test that an unknown command or a missing selector fails cleanly.
    /// </summary>
    [Fact]
    public async Task CommandExecutor_Execute_UnknownCommandOrMissingSelector_Fails()
    {
        // Arrange: an executor over a fake page
        var executor = new CommandExecutor(new FakeBrowserPage(), BaseUrl, "s", Path.GetTempPath());

        // Act: an unknown command and a click without a selector
        var unknown = await executor.ExecuteAsync(CreateRequest("dance"));
        var missing = await executor.ExecuteAsync(CreateRequest("click"));

        // Assert: both fail with a message
        Assert.False(unknown.Ok);
        Assert.False(missing.Ok);
        Assert.Contains("selector", missing.Message, StringComparison.Ordinal);
    }
}
