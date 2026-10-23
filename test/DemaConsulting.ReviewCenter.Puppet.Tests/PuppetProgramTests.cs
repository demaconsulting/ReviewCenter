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
///     Unit tests for the command-line handling of <see cref="PuppetProgram"/>, using fake collaborators.
/// </summary>
public class PuppetProgramTests
{
    /// <summary>
    ///     Test that --help prints the usage and succeeds.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_Help_PrintsUsageAndExitsZero()
    {
        // Arrange: collaborators that must not be used
        var output = new StringWriter();

        // Act: ask for help
        var exitCode = await PuppetProgram.RunAsync(["--help"], output, new StringWriter(), NoServices(), TestContext.Current.CancellationToken);

        // Assert: usage is printed
        Assert.Equal(0, exitCode);
        Assert.Contains("Usage: puppet run", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that bad arguments are rejected with exit code 2 and a message.
    /// </summary>
    /// <param name="commandLine">The arguments, separated by spaces.</param>
    /// <param name="expectedMessage">A fragment of the expected message.</param>
    [Theory]
    [InlineData("", "Expected the verb 'run'")]
    [InlineData("start steps.json", "Expected the verb 'run'")]
    [InlineData("run", "steps file is missing")]
    [InlineData("run steps.json --bogus", "Unexpected argument '--bogus'")]
    [InlineData("run steps.json --timeout 0", "--timeout must be")]
    [InlineData("run steps.json --out", "needs a value")]
    [InlineData("run steps.json --url http://x --server-dll y.dll", "cannot be used together")]
    [InlineData("run does-not-exist.json", "Cannot read the steps file")]
    public async Task PuppetProgram_Run_BadArguments_ExitsTwoWithMessage(string commandLine, string expectedMessage)
    {
        // Arrange: a bad command line and collaborators that must not be used
        var error = new StringWriter();
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Act: run it
        var exitCode = await PuppetProgram.RunAsync(args, new StringWriter(), error, NoServices(), TestContext.Current.CancellationToken);

        // Assert: exit code 2 and a message that names the problem
        Assert.Equal(2, exitCode);
        Assert.Contains(expectedMessage, error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that --url drives the given server without starting one.
    /// </summary>
    [Fact]
    public async Task PuppetProgram_Run_UrlOption_UsesGivenServerWithoutStartingOne()
    {
        // Arrange: a steps file, a fake browser, and a server starter that fails the test if called
        using var directory = new TempDirectory();
        var steps = directory.WriteFile("steps.json", """[{ "kind": "goto", "url": "/about" }]""");
        var page = new FakeBrowserPage();
        var services = new PuppetServices(
            (_, _) => throw new InvalidOperationException("The server must not be started."),
            () => Task.FromResult<IBrowserPage>(page));

        // Act: run against an existing server
        var exitCode = await PuppetProgram.RunAsync(
            ["run", steps, "--url", "http://localhost:5000/", "--out", directory.Path],
            new StringWriter(),
            new StringWriter(),
            services,
            TestContext.Current.CancellationToken);

        // Assert: the page was driven at the given URL
        Assert.Equal(0, exitCode);
        Assert.Equal(["goto http://localhost:5000/about"], page.Calls);
    }

    /// <summary>
    ///     Creates services that fail the test if they are used.
    /// </summary>
    /// <returns>The services.</returns>
    private static PuppetServices NoServices()
    {
        return new PuppetServices(
            (_, _) => throw new InvalidOperationException("The server must not be started."),
            () => throw new InvalidOperationException("The browser must not be launched."));
    }
}
