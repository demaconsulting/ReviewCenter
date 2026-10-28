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

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     End-to-end test: start the real server, start Puppet, drive the real pages (sign in, open Projects through
///     the header, create a project, switch the theme), and stop Puppet.
/// </summary>
/// <remarks>
///     This test is requirement-linked evidence for the Puppet system and daemon requirements, and for the live
///     (interactive) connection of the web user interface. Locally it is
///     skipped with a clear reason when Chromium is not installed. CI installs Chromium and sets
///     <c>REVIEWCENTER_REQUIRE_CHROMIUM=true</c>, which turns a missing or broken browser into a failure so
///     the evidence can never be skipped silently.
/// </remarks>
[Collection("Sequential")]
public class PuppetEndToEndTests
{
    /// <summary>
    ///     The environment variable that turns a missing Chromium from a skip into a failure.
    /// </summary>
    private const string RequireChromiumVariable = "REVIEWCENTER_REQUIRE_CHROMIUM";

    /// <summary>
    ///     The user name of the administrator that the test server is given.
    /// </summary>
    private const string UserName = "puppet";

    /// <summary>
    ///     The password of the administrator that the test server is given.
    /// </summary>
    private const string UserPassword = "correct horse battery";

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
        var controlFile = Path.Combine(directory.FullName, "control.json");
        var shot = Path.Combine(directory.FullName, "home.png");
        using var server = await TestServer.StartAsync(cancellation, seedUser: true);

        async Task<(int Code, string Output, string Error)> Run(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            return (await PuppetProgram.RunAsync(args, output, error, controlFile, cancellation), output.ToString(), error.ToString());
        }

        try
        {
            // Act and assert: start; a missing browser is a skip locally and a failure where Chromium is required
            var start = await StartPuppetAsync(Run, server.Url);
            Assert.True(start.Code == 0, start.Error);
            Assert.Equal(1, (await Run("start")).Code);

            // Act and assert: the page shows the sign-in link of the header (nobody is signed in)
            var go = await Run("goto", "/");
            Assert.Equal(0, go.Code);
            Assert.StartsWith("HTTP 200", go.Output, StringComparison.Ordinal);
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
            Assert.Equal(0, (await Run("fill", "login-username-input", UserName)).Code);
            Assert.Equal(0, (await Run("fill", "login-password-input", UserPassword)).Code);
            Assert.Equal(0, (await Run("click", "login-button")).Code);
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

            // Act and assert: choosing the dark theme on the account page applies at once and is remembered
            Assert.Equal(0, (await Run("goto", "/account")).Code);
            Assert.Equal(0, (await Run("click", "theme-dark")).Code);
            Assert.Equal(0, (await Run("wait", "css=html[data-theme=dark]")).Code);
            Assert.StartsWith("dark", (await Run("eval", "localStorage.getItem('reviewcenter-theme')")).Output.Trim('"'), StringComparison.Ordinal);

            // Act and assert: a missing element fails fast with a one-line message and exit code 1
            var missing = await Run("click", "no-such-element");
            Assert.Equal(1, missing.Code);
            Assert.Contains("no-such-element", missing.Error, StringComparison.Ordinal);

            // Act and assert: the screenshot is a PNG
            Assert.Equal(0, (await Run("screenshot", shot)).Code);
            var png = await File.ReadAllBytesAsync(shot, cancellation);
            Assert.True(png.Length > 1000);
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
            Assert.Equal(0, (await Run("viewport", "800x600")).Code);
            Assert.Equal(0, (await Run("theme", "dark")).Code);
            Assert.StartsWith("true", (await Run("eval", "matchMedia('(prefers-color-scheme: dark)').matches")).Output, StringComparison.Ordinal);
            Assert.Equal(0, (await Run("console")).Code);

            // Act and assert: stop succeeds and removes the control file; a second stop finds nothing running
            Assert.Equal(0, (await Run("stop")).Code);
            Assert.False(File.Exists(controlFile));
            Assert.Equal(2, (await Run("stop")).Code);
        }
        finally
        {
            // A failing test must not leave a browser behind; this is a no-op after a successful stop
            await Run("stop");
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    ///     Test that a hover-revealed control of a real review diff is reached in a real browser: the <c>+</c> of a
    ///     diff line is hidden until the mouse is on its row, hovering the row reveals it, and clicking it opens the
    ///     comment composer.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task PuppetEndToEnd_ReviewDiff_HoverRevealsLineCommentAndClickOpensComposer_WorksAgainstRealServer()
    {
        // Arrange: a local git repository with a base tag and one changed line; the test needs git, as CI has it
        var cancellation = TestContext.Current.CancellationToken;
        if (!await IsGitAvailableAsync(cancellation))
        {
            Assert.Skip("git is not available.");
        }

        var directory = Directory.CreateTempSubdirectory("puppet-diff-tests-");
        var controlFile = Path.Combine(directory.FullName, "control.json");
        var repository = Path.Combine(directory.FullName, "repo");
        var gitCache = Path.Combine(directory.FullName, "git-cache");
        Directory.CreateDirectory(repository);
        await RunGitAsync(repository, cancellation, "init", "-b", "main");
        Directory.CreateDirectory(Path.Combine(repository, "src"));
        var sample = Path.Combine(repository, "src", "sample.txt");
        await File.WriteAllTextAsync(sample, "alpha\nbeta\ngamma\n", cancellation);
        await RunGitAsync(repository, cancellation, "add", "-A");
        await RunGitAsync(repository, cancellation, "commit", "-m", "base");
        await RunGitAsync(repository, cancellation, "tag", "base");
        await File.WriteAllTextAsync(sample, "alpha\nBETA\ngamma\n", cancellation);
        await RunGitAsync(repository, cancellation, "commit", "-a", "-m", "change");

        var server = await TestServer.StartAsync(cancellation, seedUser: true, gitCachePath: gitCache);

        async Task<(int Code, string Output, string Error)> Run(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            return (await PuppetProgram.RunAsync(args, output, error, controlFile, cancellation), output.ToString(), error.ToString());
        }

        // The computed opacity of the + of the added line (the 0 to 1 transition takes a moment, so poll briefly)
        async Task<bool> OpacityBecomesAsync(string expected)
        {
            const string opacity = "getComputedStyle(document.querySelector('[data-testid=diff-comment-2]')).opacity";
            for (var attempt = 0; attempt < 30; attempt++)
            {
                if ((await Run("eval", opacity)).Output.Split('\n')[0].Trim().Trim('"') == expected)
                {
                    return true;
                }

                await Task.Delay(100, cancellation);
            }

            return false;
        }

        try
        {
            // Act: start, sign in, register the repository, create a project and a review of the change
            var start = await StartPuppetAsync(Run, server.Url);
            Assert.True(start.Code == 0, start.Error);
            Assert.Equal(0, (await Run("goto", "/login")).Code);
            Assert.Equal(0, (await Run("fill", "login-username-input", UserName)).Code);
            Assert.Equal(0, (await Run("fill", "login-password-input", UserPassword)).Code);
            Assert.Equal(0, (await Run("click", "login-button")).Code);
            Assert.Equal(0, (await Run("wait", "current-user")).Code);

            Assert.Equal(0, (await Run("goto", "/projects/new")).Code);
            Assert.Equal(0, (await Run("wait", "css=[data-testid=new-project-page][data-interactive=true]")).Code);
            Assert.Equal(0, (await Run("fill", "project-name-input", "Puppet Project")).Code);
            Assert.Equal(0, (await Run("click", "create-project-button")).Code);
            Assert.Equal(0, (await Run("wait", "project-name")).Code);

            Assert.Equal(0, (await Run("goto", "/projects/1/repositories/new")).Code);
            Assert.Equal(0, (await Run("wait", "css=[data-testid=new-repository-page][data-interactive=true]")).Code);
            Assert.Equal(0, (await Run("fill", "new-repository-name-input", "puppet-repository")).Code);
            Assert.Equal(0, (await Run("fill", "new-repository-location-input", repository)).Code);
            Assert.Equal(0, (await Run("click", "create-repository-button")).Code);
            Assert.Equal(0, (await Run("wait", "repository-row-1")).Code);

            Assert.Equal(0, (await Run("goto", "/projects/1/reviews/new")).Code);
            Assert.Equal(0, (await Run("wait", "css=[data-testid=new-review-page][data-interactive=true]")).Code);
            Assert.Equal(0, (await Run("fill", "review-id-input", "diff-review")).Code);
            Assert.Equal(0, (await Run("fill", "review-title-input", "Diff review")).Code);
            Assert.Equal(0, (await Run("check", "source-repository-radio")).Code);
            Assert.Equal(0, (await Run("select", "repository-select", "1")).Code);
            Assert.Equal(0, (await Run("fill", "head-revision-input", "main")).Code);
            Assert.Equal(0, (await Run("fill", "baseline-revision-input", "base")).Code);
            Assert.Equal(0, (await Run("click", "create-review-button")).Code);
            Assert.Equal(0, (await Run("wait", "css=[data-testid=review-page][data-interactive=true]", "--timeout", "30000")).Code);

            // Act and assert: the changed file shows its diff, and the + of the added line is hidden at first
            Assert.Equal(0, (await Run("click", "file-select-0")).Code);
            Assert.Equal(0, (await Run("click", "view-diff-button")).Code);
            Assert.Equal(0, (await Run("wait", "diff-view")).Code);
            Assert.Equal(0, (await Run("wait", "diff-comment-2")).Code);
            Assert.Equal(0, (await Run("mouse-move", "0", "0")).Code);
            Assert.True(await OpacityBecomesAsync("0"), "The + of a diff line must be hidden until its row is pointed at.");

            // Act and assert: pointing at the row reveals the +, and the hover persists until the next command
            Assert.Equal(0, (await Run("hover", "diff-row-2")).Code);
            Assert.True(await OpacityBecomesAsync("1"), "Hovering a diff row must reveal its + control.");

            // Act and assert: clicking the revealed + opens the comment composer under the line
            Assert.Equal(0, (await Run("click", "diff-comment-2")).Code);
            Assert.Equal(0, (await Run("wait", "css=[data-testid=diff-composer] [data-testid=new-thread-form]")).Code);
        }
        finally
        {
            // A failing test must not leave a browser behind; this is a no-op after a successful stop
            await Run("stop");
            server.Dispose();
            DeleteDirectory(directory);
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
                string.Equals(Environment.GetEnvironmentVariable(RequireChromiumVariable), "true", StringComparison.OrdinalIgnoreCase),
                $"{RequireChromiumVariable} is true but Chromium is unavailable: {start.Error}");
            Assert.Skip(start.Error);
        }

        return start;
    }

    /// <summary>
    ///     Tells whether the <c>git</c> command line is available.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><c>true</c> when <c>git --version</c> succeeds.</returns>
    private static async Task<bool> IsGitAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunGitAsync(Path.GetTempPath(), cancellationToken, "--version");
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Runs one git command with a fixed identity and without the user's or the system's configuration.
    /// </summary>
    /// <param name="workingDirectory">The directory to run in.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <returns>A task that completes when git succeeded.</returns>
    private static async Task RunGitAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(Path.GetTempPath(), "reviewcenter-no-such-gitconfig");
        info.Environment["GIT_AUTHOR_NAME"] = "Puppet Test";
        info.Environment["GIT_AUTHOR_EMAIL"] = "puppet@example.com";
        info.Environment["GIT_COMMITTER_NAME"] = "Puppet Test";
        info.Environment["GIT_COMMITTER_EMAIL"] = "puppet@example.com";
        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        _ = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {await error}");
        }
    }

    /// <summary>
    ///     Removes a temporary folder, including read-only git objects; errors are ignored (best effort).
    /// </summary>
    /// <param name="directory">The folder to remove.</param>
    private static void DeleteDirectory(DirectoryInfo directory)
    {
        try
        {
            foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
            }

            directory.Delete(recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignored: the folder is under the temp directory
        }
    }

    /// <summary>
    ///     The ReviewCenter server running as a separate process on a free loopback port.
    /// </summary>
    private sealed class TestServer : IDisposable
    {
        private readonly Process _process;

        private readonly string _databaseFile;

        private TestServer(Process process, string url, string databaseFile)
        {
            _process = process;
            _databaseFile = databaseFile;
            Url = url;
        }

        /// <summary>
        ///     Gets the base URL of the server, without a trailing slash.
        /// </summary>
        public string Url { get; }

        /// <summary>
        ///     Starts the server and waits until <c>/health</c> answers <c>Healthy</c>.
        /// </summary>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <param name="seedUser">Whether to create an administrator (<c>UserName</c>) before the server starts.</param>
        /// <param name="gitCachePath">The folder for the server's clones of repositories, or <c>null</c> for the default.</param>
        /// <returns>The running server; disposing it kills the process tree.</returns>
        public static async Task<TestServer> StartAsync(CancellationToken cancellationToken, bool seedUser = false, string? gitCachePath = null)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            listener.Stop();

            // The server gets a private database file so the run never touches the real application data folder
            var databaseFile = Path.Combine(Path.GetTempPath(), "ReviewCenterPuppet-" + Guid.NewGuid().ToString("N") + ".db");
            var dll = typeof(PuppetEndToEndTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "ServerDllPath").Value!;
            var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(dll)!
            };
            info.Environment["Database__ConnectionString"] = $"Data Source={databaseFile};Pooling=False";
            if (gitCachePath is not null)
            {
                info.Environment["Git__CachePath"] = gitCachePath;
            }

            // A signed-in flow needs a user: the command line creates the first administrator, as an operator would
            if (seedUser)
            {
                var add = new ProcessStartInfo(info.FileName)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = info.WorkingDirectory
                };
                add.Environment["Database__ConnectionString"] = info.Environment["Database__ConnectionString"];
                add.Environment["REVIEWCENTER_PASSWORD"] = UserPassword;
                foreach (var argument in new[] { dll, "user", "add", "--username", UserName, "--display-name", "Puppet Admin", "--system-admin" })
                {
                    add.ArgumentList.Add(argument);
                }

                using var adding = Process.Start(add)!;
                var addError = adding.StandardError.ReadToEndAsync(cancellationToken);
                _ = await adding.StandardOutput.ReadToEndAsync(cancellationToken);
                await adding.WaitForExitAsync(cancellationToken);
                if (adding.ExitCode != 0)
                {
                    throw new InvalidOperationException("Creating the test user failed: " + await addError);
                }
            }

            info.ArgumentList.Add(dll);
            info.ArgumentList.Add("serve");
            info.ArgumentList.Add("--urls");
            info.ArgumentList.Add(url);
            var process = Process.Start(info)!;
            var server = new TestServer(process, url, databaseFile);

            // Drain the output so the server never blocks on a full pipe
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            for (var attempt = 0; attempt < 300 && !process.HasExited; attempt++)
            {
                try
                {
                    if (await http.GetStringAsync($"{url}/health", cancellationToken) == "Healthy")
                    {
                        return server;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    // Not accepting connections yet
                }

                await Task.Delay(100, cancellationToken);
            }

            server.Dispose();
            throw new InvalidOperationException($"The ReviewCenter server at {url} did not become healthy.");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit();
            _process.Dispose();

            // Best-effort removal of the private database file
            try
            {
                File.Delete(_databaseFile);
            }
            catch (IOException)
            {
                // Ignored: the file is under the temp directory
            }
        }
    }
}
