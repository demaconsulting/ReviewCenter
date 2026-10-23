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
///     System tests that run the whole Puppet pipeline (arguments, steps file, step runner, selector
///     resolver, teardown) through <see cref="PuppetProgram.RunAsync"/> with only the server and the
///     browser replaced by fakes, so they run on any machine without Chromium.
/// </summary>
public class PuppetSystemTests
{
    /// <summary>
    ///     Test that a successful run reports every step and stops the server and browser.
    /// </summary>
    [Fact]
    public async Task ReviewCenterPuppet_Run_AllStepsSucceed_ExitsZeroAndStopsEverything()
    {
        // Arrange: a steps file and fake collaborators
        using var directory = new TempDirectory();
        var steps = directory.WriteFile(
            "steps.json",
            """[{ "kind": "goto", "url": "/" }, { "kind": "click", "selector": "go" }, { "kind": "text", "selector": "answer" }]""");
        var server = new FakeRunningServer();
        var page = new FakeBrowserPage { TextResult = "done" };
        var output = new StringWriter();

        // Act: run it
        var exitCode = await RunAsync(["run", steps, "--out", directory.Path], output, new StringWriter(), server, () => Task.FromResult<IBrowserPage>(page));

        // Assert: exit 0, a result line per step including the text, the page driven at the server URL, and everything stopped
        Assert.Equal(0, exitCode);
        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("""{"step":3,"kind":"text","ok":true,"text":"done"}""", lines[2]);
        Assert.Equal("goto http://fake-server:1234/", page.Calls[0]);
        Assert.Equal(1, server.DisposeCount);
        Assert.Equal(1, page.DisposeCount);
    }

    /// <summary>
    ///     Test that a failed step gives exit code 1, a failure screenshot, no later steps, and still stops everything.
    /// </summary>
    [Fact]
    public async Task ReviewCenterPuppet_Run_StepFails_ExitsOneWithFailureScreenshotAndStopsEverything()
    {
        // Arrange: a click that fails, followed by a step that must not run
        using var directory = new TempDirectory();
        var steps = directory.WriteFile(
            "steps.json",
            """[{ "kind": "goto", "url": "/" }, { "kind": "click", "selector": "missing" }, { "kind": "text", "selector": "later" }]""");
        var server = new FakeRunningServer();
        var page = new FakeBrowserPage { FailClickOn = "missing" };
        var output = new StringWriter();

        // Act: run it
        var exitCode = await RunAsync(["run", steps, "--out", directory.Path], output, new StringWriter(), server, () => Task.FromResult<IBrowserPage>(page));

        // Assert: exit 1, two result lines, the failure screenshot exists, and everything stopped
        Assert.Equal(1, exitCode);
        Assert.Equal(2, output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.True(File.Exists(Path.Combine(directory.Path, "failure-step-2.png")));
        Assert.Equal(1, server.DisposeCount);
        Assert.Equal(1, page.DisposeCount);
    }

    /// <summary>
    ///     Test that a browser that cannot launch gives exit code 2 and still stops the server that was started.
    /// </summary>
    [Fact]
    public async Task ReviewCenterPuppet_Run_BrowserCannotLaunch_ExitsTwoAndStopsServer()
    {
        // Arrange: a browser launch that fails
        using var directory = new TempDirectory();
        var steps = directory.WriteFile("steps.json", """[{ "kind": "goto", "url": "/" }]""");
        var server = new FakeRunningServer();
        var error = new StringWriter();

        // Act: run it
        var exitCode = await RunAsync(
            ["run", steps, "--out", directory.Path],
            new StringWriter(),
            error,
            server,
            () => throw new PuppetException("Chromium is not installed for Playwright."));

        // Assert: exit 2 with the message on the error stream and the server stopped
        Assert.Equal(2, exitCode);
        Assert.Contains("Chromium is not installed", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, server.DisposeCount);
    }

    /// <summary>
    ///     Runs the program with the given fake server and browser launcher.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="output">Receives the result lines.</param>
    /// <param name="error">Receives the error messages.</param>
    /// <param name="server">The server the fake starter returns.</param>
    /// <param name="launchBrowser">The browser launcher.</param>
    /// <returns>The exit code.</returns>
    private static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        FakeRunningServer server,
        Func<Task<IBrowserPage>> launchBrowser)
    {
        // The default server assembly must exist for a run without --url, so point at this test assembly
        string[] withServer = [.. args, "--server-dll", typeof(PuppetSystemTests).Assembly.Location];
        var services = new PuppetServices((_, _) => Task.FromResult<IRunningServer>(server), launchBrowser);
        return PuppetProgram.RunAsync(withServer, output, error, services, TestContext.Current.CancellationToken);
    }
}
