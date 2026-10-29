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
///     Unit tests for the argument parsing and output formatting of <see cref="PuppetProgram"/>.
/// </summary>
public class PuppetProgramTests
{
    /// <summary>
    ///     Test that valid command lines become the expected requests.
    /// </summary>
    [Fact]
    public void PuppetProgram_ParseCommand_ValidCommands_BuildRequests()
    {
        // Arrange and act: parse a representative set of command lines
        var go = PuppetProgram.ParseCommand(["goto", "/about"]);
        var press = PuppetProgram.ParseCommand(["press", "Enter", "testid:name"]);
        var pressKey = PuppetProgram.ParseCommand(["press", "Escape"]);
        var wait = PuppetProgram.ParseCommand(["wait", "text=Saved", "--timeout", "1500"]);
        var shot = PuppetProgram.ParseCommand(["screenshot", "shot.png", "--full", "--selector", "projects-link"]);
        var theme = PuppetProgram.ParseCommand(["theme", "dark"]);
        var start = PuppetProgram.ParseCommand(["start", "--url", "http://127.0.0.1:5199/", "--viewport", "390x800", "--headed"]);

        // Assert: arguments and options are split, files are absolute, and the start URL loses its trailing slash
        Assert.Equal("goto", go.Command);
        Assert.Equal(["/about"], go.Args);
        Assert.Equal(["Enter", "testid:name"], press.Args);
        Assert.Equal(["Escape"], pressKey.Args);
        Assert.Equal(("wait", "1500"), (wait.Command, wait.Options["timeout"]));
        Assert.Equal(Path.GetFullPath("shot.png"), shot.Args[0]);
        Assert.Equal(("true", "projects-link"), (shot.Options["full"], shot.Options["selector"]));
        Assert.Equal(["dark"], theme.Args);
        Assert.Equal("http://127.0.0.1:5199", start.Options["url"]);
        Assert.Equal(("390x800", "true"), (start.Options["viewport"], start.Options["headed"]));
    }

    /// <summary>
    ///     Test that bad command lines exit with 2 and a usage message, and that help succeeds.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task PuppetProgram_Run_BadArguments_ExitsTwoWithUsage()
    {
        // Arrange: bad command lines with the message each must produce
        var cases = new (string CommandLine, string Message)[]
        {
            ("", "Usage: puppet"),
            ("bogus", "Unknown command 'bogus'"),
            ("click", "Usage: puppet click <selector>"),
            ("click a b", "Usage: puppet click <selector>"),
            ("click a --bogus", "Unknown option '--bogus'"),
            ("wait a --timeout", "needs a value"),
            ("wait a --timeout 0", "--timeout must be"),
            ("viewport wide", "viewport must look like"),
            ("theme blue", "theme must be light or dark"),
            ("upload a missing-file.txt", "does not exist"),
            ("start --url nope", "--url must be an absolute"),
        };

        foreach (var (commandLine, message) in cases)
        {
            using var error = new StringWriter();
            using var unusedOutput = new StringWriter();

            // Act: run it (nothing is started because parsing fails first)
            var exitCode = await PuppetProgram.RunAsync(
                commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                unusedOutput,
                error,
                NoControlFile(),
                TestContext.Current.CancellationToken);

            // Assert: exit code 2 and a message that names the problem
            Assert.Equal(2, exitCode);
            Assert.Contains(message, error.ToString(), StringComparison.Ordinal);
        }

        // Assert: help prints the usage and succeeds
        using var output = new StringWriter();
        using var helpError = new StringWriter();
        Assert.Equal(0, await PuppetProgram.RunAsync(["--help"], output, helpError, NoControlFile(), TestContext.Current.CancellationToken));
        Assert.Contains("puppet screenshot", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a command without a running Puppet exits with 2 and says how to start it.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task PuppetProgram_Run_NotRunning_ExitsTwoWithStartHint()
    {
        // Arrange: no control file at all, then a stale one whose port nobody listens on
        var controlFile = NoControlFile();
        using var absent = new StringWriter();
        using var stale = new StringWriter();
        using var unused = new StringWriter();

        // Act: send a command in both situations
        var absentCode = await PuppetProgram.RunAsync(["snapshot"], unused, absent, controlFile, TestContext.Current.CancellationToken);
        ControlChannel.Write(controlFile, new ControlInfo(1, null, null));
        var staleCode = await PuppetProgram.RunAsync(["snapshot"], unused, stale, controlFile, TestContext.Current.CancellationToken);
        File.Delete(controlFile);

        // Assert: both exit with 2 and point at 'puppet start'
        Assert.Equal(2, absentCode);
        Assert.Equal(2, staleCode);
        Assert.Contains("puppet start", absent.ToString(), StringComparison.Ordinal);
        Assert.Contains("puppet start", stale.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a response prints its result, the URL and title line, and the new errors, with the right exit code.
    /// </summary>
    [Fact]
    public void PuppetProgram_Format_ResultUrlTitleAndErrors_PrintsFooter()
    {
        // Arrange: a successful multi-line result with one new error, and a failure
        var ok = new PuppetResponse(true, "line 1\nline 2", "http://x/", "Title", ["console.error: boom"]);
        var failed = new PuppetResponse(false, "click timed out", "http://x/", "Title", []);
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var unused = new StringWriter();

        // Act: format both
        var okCode = PuppetProgram.Format(ok, output, error);
        var failedCode = PuppetProgram.Format(failed, unused, error);

        // Assert: the result, the footer, and the event line are printed; a failure goes to stderr with exit code 1
        Assert.Equal(0, okCode);
        Assert.Equal($"line 1\nline 2\n@ http://x/  \"Title\"\n! console.error: boom\n".ReplaceLineEndings(), output.ToString().ReplaceLineEndings());
        Assert.Equal(1, failedCode);
        Assert.Equal("click timed out", error.ToString().Trim());
    }

    /// <summary>
    ///     Gets a control file path that does not exist.
    /// </summary>
    /// <returns>A unique path in the temp folder.</returns>
    private static string NoControlFile()
    {
        return Path.Combine(Path.GetTempPath(), $"puppet-tests-{Guid.NewGuid():N}.json");
    }
}
