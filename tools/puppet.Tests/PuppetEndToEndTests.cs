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

using DemaConsulting.ReviewCenter.SystemTests;

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     End-to-end test: start the real server, start Puppet, drive the real pages (sign in, open Projects through
///     the header, create a project, switch the theme), and stop Puppet.
/// </summary>
/// <remarks>
///     This test checks the command-line tool itself: the daemon, the control file, the exit codes, and the output.
///     It uses the host of the system tests to start the real server. Locally it is skipped with a clear reason when
///     Chromium is not installed; CI installs Chromium and sets <c>REVIEWCENTER_REQUIRE_CHROMIUM=true</c>, which turns a
///     missing or broken browser into a failure.
/// </remarks>
[Collection("Sequential")]
public class PuppetEndToEndTests
{
    /// <summary>
    ///     Test that Puppet can be started, driven one command at a time, and stopped again, while it signs in,
    ///     follows the header to the projects page, creates a project over the live connection, and switches the theme.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task PuppetEndToEnd_StartDriveStop_SignInProjectAndTheme_WorksAgainstRealServer()
    {
        // Arrange: the real server with one administrator, and a private control file and screenshot folder
        var cancellation = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("puppet-tests-");
        using var cleanup = new SystemTestHost.TemporaryFolder(directory);
        var controlFile = Path.Combine(directory.FullName, "control.json");
        var shot = Path.Combine(directory.FullName, "home.png");
        using var server = await SystemTestHost.TestServer.StartAsync(cancellation, seedUser: true);

        async Task<(int Code, string Output, string Error)> Run(params string[] args)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            return (await PuppetProgram.RunAsync(args, output, error, controlFile, cancellation), output.ToString(), error.ToString());
        }

        try
        {
            // Act and assert: with an empty browser folder Chromium is missing, so the start fails with exit code 1,
            // prints the reason with the install command and leaves no control file behind
            var browsersVariable = "PLAYWRIGHT_BROWSERS_PATH";
            var previousBrowsers = Environment.GetEnvironmentVariable(browsersVariable);
            var emptyBrowsers = Directory.CreateDirectory(Path.Combine(directory.FullName, "no-browsers"));
            Environment.SetEnvironmentVariable(browsersVariable, emptyBrowsers.FullName);
            (int Code, string Output, string Error) badStart;
            try
            {
                badStart = await Run("start", "--url", server.Url, "--viewport", "1280x800");
            }
            finally
            {
                Environment.SetEnvironmentVariable(browsersVariable, previousBrowsers);
            }

            Assert.Equal(1, badStart.Code);
            Assert.Contains("Chromium is not installed for Playwright", badStart.Error, StringComparison.Ordinal);
            Assert.Contains("install chromium", badStart.Error, StringComparison.Ordinal);
            Assert.False(File.Exists(controlFile));

            // Act and assert: start; a missing browser is a skip locally and a failure where Chromium is required
            var start = await StartPuppetAsync(Run, server.Url);
            Assert.True(start.Code == 0, start.Error);
            Assert.Equal(1, (await Run("start")).Code);

            // Act and assert: the page shows the sign-in link of the header (nobody is signed in)
            var go = await Run("goto", "/");
            Assert.Equal(0, go.Code);
            Assert.StartsWith("HTTP 200", go.Output, StringComparison.Ordinal);
            var footer = go.Output.ReplaceLineEndings("\n").Split('\n').Single(line => line.StartsWith("@ ", StringComparison.Ordinal));
            Assert.StartsWith($"@ {server.Url}", footer, StringComparison.Ordinal);
            Assert.EndsWith("\"", footer, StringComparison.Ordinal);
            Assert.Contains("  \"", footer, StringComparison.Ordinal);
            Assert.Equal("{\"a\":1}", (await Run("eval", "({a: 1})")).Output.Split('\n')[0].Trim());
            var snapshot = await Run("snapshot");
            Assert.Contains("testid=signin-link", snapshot.Output, StringComparison.Ordinal);
            Assert.Contains(server.Url, snapshot.Output, StringComparison.Ordinal);

            // Act and assert: a hover persists across separate commands, including a screenshot, until the mouse moves
            const string isHovered = "document.querySelector('[data-testid=signin-link]').matches(':hover')";
            Assert.Equal(0, (await Run("hover", "signin-link")).Code);
            Assert.Equal(0, (await Run("screenshot", Path.Combine(directory.FullName, "hover.png"))).Code);
            Assert.StartsWith("true", (await Run("eval", isHovered)).Output, StringComparison.Ordinal);
            Assert.Equal(0, (await Run("mouse-move", "0", "0")).Code);
            Assert.StartsWith("false", (await Run("eval", isHovered)).Output, StringComparison.Ordinal);

            // Act and assert: a failed sign-in (a plain form post) shows its message; the right password signs in
            Assert.Equal(0, (await Run("goto", "/login")).Code);
            Assert.Equal(0, (await Run("fill", "login-username-input", "nobody")).Code);
            Assert.Equal(0, (await Run("fill", "login-password-input", "wrong-password")).Code);
            Assert.Equal(0, (await Run("click", "login-button")).Code);
            Assert.Equal(0, (await Run("wait", "login-error")).Code);
            Assert.StartsWith("Invalid user name or password.", (await Run("text", "login-error")).Output, StringComparison.Ordinal);
            Assert.Equal(0, (await Run("fill", "login-username-input", SystemTestHost.UserName)).Code);
            Assert.Equal(0, (await Run("fill", "login-password-input", SystemTestHost.UserPassword)).Code);
            Assert.Equal(0, (await Run("press", "Enter", "login-password-input")).Code);
            Assert.Equal(0, (await Run("wait", "current-user")).Code);
            Assert.StartsWith("Puppet Admin", (await Run("text", "current-user")).Output, StringComparison.Ordinal);

            // Act and assert: the header leads to the projects page; its live connection comes up before the next click
            Assert.Equal(0, (await Run("click", "projects-link")).Code);
            Assert.Equal(0, (await Run("wait", "css=[data-testid=projects-page][data-interactive=true]")).Code);
            Assert.Equal(0, (await Run("click", "new-project-button")).Code);
            Assert.Equal(0, (await Run("wait", "css=[data-testid=new-project-page][data-interactive=true]")).Code);

            // Act and assert: a form submitted over the live connection creates the project; wait for the result
            Assert.Equal(0, (await Run("fill", "project-name-input", "Puppet Project")).Code);
            Assert.Equal(0, (await Run("click", "create-project-button")).Code);
            Assert.Equal(0, (await Run("wait", "project-name")).Code);
            Assert.StartsWith("Puppet Project", (await Run("text", "project-name")).Output, StringComparison.Ordinal);
            Assert.Equal(0, (await Run("wait", "text=Puppet Project")).Code);
            Assert.Contains("Puppet Project", (await Run("text")).Output, StringComparison.Ordinal);

            // Act and assert: choosing the dark theme on the account page applies at once and is remembered
            Assert.Equal(0, (await Run("goto", "/account")).Code);
            Assert.Equal(0, (await Run("click", "theme-dark")).Code);
            Assert.Equal(0, (await Run("wait", "css=html[data-theme=dark]")).Code);
            Assert.StartsWith("dark", (await Run("eval", "localStorage.getItem('reviewcenter-theme')")).Output.Trim('"'), StringComparison.Ordinal);

            // Act and assert: a console error is reported once, by the footer of the command that raised it, and
            // not repeated by the commands after that
            var probe = await Run("eval", "console.error('probe')");
            var afterProbe = await Run("eval", "1");
            var laterCommand = await Run("eval", "2");
            const string probeEvent = "! console.error: probe";
            Assert.Single(probe.Output.ReplaceLineEndings("\n").Split('\n'), line => line == probeEvent);
            Assert.DoesNotContain(probeEvent, afterProbe.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(probeEvent, laterCommand.Output, StringComparison.Ordinal);

            // Act and assert: a missing element fails fast with a one-line message and exit code 1
            var missing = await Run("click", "no-such-element");
            Assert.Equal(1, missing.Code);
            Assert.Contains("no-such-element", missing.Error, StringComparison.Ordinal);

            // Act and assert: the screenshot of the page and of one element is a PNG
            Assert.Equal(0, (await Run("screenshot", shot)).Code);
            var png = await File.ReadAllBytesAsync(shot, cancellation);
            Assert.True(png.Length > 1000);
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
            var elementShot = Path.Combine(directory.FullName, "element.png");
            Assert.Equal(0, (await Run("screenshot", elementShot, "--selector", "theme-dark")).Code);
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], (await File.ReadAllBytesAsync(elementShot, cancellation))[..4]);
            Assert.Equal(0, (await Run("viewport", "800x600")).Code);
            Assert.StartsWith("800", (await Run("eval", "innerWidth")).Output, StringComparison.Ordinal);
            Assert.Equal(0, (await Run("theme", "light")).Code);
            Assert.StartsWith("false", (await Run("eval", "matchMedia('(prefers-color-scheme: dark)').matches")).Output, StringComparison.Ordinal);
            Assert.Equal(0, (await Run("theme", "dark")).Code);
            Assert.StartsWith("true", (await Run("eval", "matchMedia('(prefers-color-scheme: dark)').matches")).Output, StringComparison.Ordinal);
            Assert.Equal(0, (await Run("console")).Code);

            // Act and assert: stop succeeds and removes the control file; a second stop finds nothing running, exits
            // with code 2 and points at the start command
            Assert.Equal(0, (await Run("stop")).Code);
            Assert.False(File.Exists(controlFile));
            var secondStop = await Run("stop");
            Assert.Equal(2, secondStop.Code);
            Assert.Contains("puppet start", secondStop.Error, StringComparison.Ordinal);
        }
        finally
        {
            // A failing test must not leave a browser behind; this is a no-op after a successful stop
            await Run("stop");
        }
    }

    /// <summary>
    ///     Starts Puppet; a missing browser is a skip locally and a failure where Chromium is required.
    /// </summary>
    /// <param name="run">Runs one Puppet command.</param>
    /// <param name="url">The base URL of the server.</param>
    /// <returns>The result of the start command (a failure other than a missing browser is returned, not thrown).</returns>
    private static async Task<(int Code, string Output, string Error)> StartPuppetAsync(
        Func<string[], Task<(int Code, string Output, string Error)>> run,
        string url)
    {
        var start = await run(["start", "--url", url, "--viewport", "1280x800"]);
        if (start.Code != 0 && start.Error.Contains("Chromium", StringComparison.Ordinal))
        {
            Assert.False(
                string.Equals(Environment.GetEnvironmentVariable(SystemTestHost.RequireChromiumVariable), "true", StringComparison.OrdinalIgnoreCase),
                $"{SystemTestHost.RequireChromiumVariable} is true but Chromium is unavailable: {start.Error}");
            Assert.Skip(start.Error);
        }

        return start;
    }
}
