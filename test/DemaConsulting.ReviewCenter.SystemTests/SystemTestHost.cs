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
using System.Text.Json;
using DemaConsulting.ReviewCenter.Puppeting;

namespace DemaConsulting.ReviewCenter.SystemTests;

/// <summary>
///     The test host of the system tests: the real ReviewCenter server as a separate process, git, temporary folders,
///     and the in-process browser with the helpers the tests use to read and wait for its pages.
/// </summary>
/// <remarks>
///     A browser that cannot be started because Chromium is missing skips the test locally. CI installs Chromium and
///     sets <c>REVIEWCENTER_REQUIRE_CHROMIUM=true</c>, which turns a missing or broken browser into a failure, so the
///     evidence can never be skipped silently.
/// </remarks>
internal static class SystemTestHost
{
    /// <summary>
    ///     The environment variable that turns a missing Chromium from a skip into a failure.
    /// </summary>
    internal const string RequireChromiumVariable = "REVIEWCENTER_REQUIRE_CHROMIUM";

    /// <summary>
    ///     The user name of the administrator that the test server is given.
    /// </summary>
    internal const string UserName = "puppet";

    /// <summary>
    ///     The password of the administrator that the test server is given.
    /// </summary>
    internal const string UserPassword = "correct horse battery";

    /// <summary>
    ///     Starts a browser for a server; a missing Chromium is a skip locally and a failure where Chromium is required.
    /// </summary>
    /// <param name="url">The base URL of the server.</param>
    /// <returns>The browser; disposing it closes Chromium.</returns>
    internal static async Task<PuppetBrowser> StartBrowserAsync(string url)
    {
        try
        {
            return await PuppetBrowser.StartAsync(url, "1280x800");
        }
        catch (PuppetingException ex) when (ex.Message.Contains("Chromium", StringComparison.Ordinal))
        {
            Assert.False(
                string.Equals(Environment.GetEnvironmentVariable(RequireChromiumVariable), "true", StringComparison.OrdinalIgnoreCase),
                $"{RequireChromiumVariable} is true but Chromium is unavailable: {ex.Message}");
            Assert.Skip(ex.Message);
            throw;
        }
    }

    /// <summary>
    ///     Evaluates a JavaScript expression in the page and returns its value as text.
    /// </summary>
    /// <param name="browser">The browser.</param>
    /// <param name="expression">The expression.</param>
    /// <returns>The value; a string is returned as it is, anything else as its JSON text.</returns>
    internal static async Task<string> ValueOfAsync(this PuppetBrowser browser, string expression)
    {
        var json = await browser.EvalAsync(expression);
        return json.StartsWith('"') ? JsonSerializer.Deserialize<string>(json) ?? string.Empty : json;
    }

    /// <summary>
    ///     Clicks an element without requiring the click to report success.
    /// </summary>
    /// <param name="browser">The browser.</param>
    /// <param name="selector">The selector; a plain name is a test identifier.</param>
    /// <returns>A task that completes when the click was tried.</returns>
    internal static async Task TryClickAsync(this PuppetBrowser browser, string selector)
    {
        try
        {
            await browser.ClickAsync(selector);
        }
        catch (PuppetingException)
        {
            // The caller waits for the result of the click instead
        }
    }

    /// <summary>
    ///     Waits until a condition holds, for at most 20 seconds.
    /// </summary>
    /// <param name="browser">The browser the condition reads (unused; it keeps the calls readable).</param>
    /// <param name="condition">The condition.</param>
    /// <param name="what">What is waited for, for the message of the failure.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    internal static async Task WaitUntilAsync(this PuppetBrowser browser, Func<Task<bool>> condition, string what)
    {
        _ = browser;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Timed out waiting for {what}.");
    }

    /// <summary>
    ///     Opens a page and waits until its live connection works.
    /// </summary>
    /// <param name="browser">The browser.</param>
    /// <param name="path">The address, relative to the server.</param>
    /// <param name="pageTestId">The test identifier of the page.</param>
    /// <returns>A task that completes when the page is interactive.</returns>
    internal static async Task OpenPageAsync(this PuppetBrowser browser, string path, string pageTestId)
    {
        await browser.GotoAsync(path);
        await browser.WaitForAsync($"css=[data-testid={pageTestId}][data-interactive=true]", 30000);
    }

    /// <summary>
    ///     Signs in at the login page.
    /// </summary>
    /// <param name="browser">The browser.</param>
    /// <param name="userName">The user name; every test user has the same password.</param>
    /// <returns>A task that completes when the user is signed in.</returns>
    internal static async Task SignInAsync(this PuppetBrowser browser, string userName)
    {
        await browser.GotoAsync("/login");
        await browser.FillAsync("login-username-input", userName);
        await browser.FillAsync("login-password-input", UserPassword);

        // The form posts to the server, which hashes the password; on a busy machine that can outlast the click,
        // so do not require the click to report success and wait for the signed-in header instead
        await browser.TryClickAsync("login-button");
        await browser.WaitForAsync("current-user", 60000);
    }
    /// <summary>
    ///     Tells whether the <c>git</c> command line is available.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><c>true</c> when <c>git --version</c> succeeds.</returns>
    internal static async Task<bool> IsGitAvailableAsync(CancellationToken cancellationToken)
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
    internal static async Task RunGitAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
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
    ///     A temporary folder that is removed, best effort, when disposed.
    /// </summary>
    /// <param name="directory">The folder to remove.</param>
    internal sealed class TemporaryFolder(DirectoryInfo directory) : IDisposable
    {
        /// <summary>
        ///     Removes the folder, including read-only git objects; errors are ignored (best effort).
        /// </summary>
        public void Dispose()
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
    }

    /// <summary>
    ///     The ReviewCenter server running as a separate process on a free loopback port.
    /// </summary>
    internal sealed class TestServer : IDisposable
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
        /// <param name="extraUsers">Ordinary users to create besides the administrator (with the same password), or <c>null</c> for none.</param>
        /// <param name="notificationFolder">
        ///     The folder that receives the e-mail the server writes as <c>.eml</c> files (the file sender of the
        ///     e-mail notifications), or <c>null</c> to leave e-mail notifications switched off.
        /// </param>
        /// <returns>The running server; disposing it kills the process tree.</returns>
        public static async Task<TestServer> StartAsync(
            CancellationToken cancellationToken,
            bool seedUser = false,
            string? gitCachePath = null,
            IReadOnlyList<(string Name, string DisplayName)>? extraUsers = null,
            string? notificationFolder = null)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            listener.Stop();

            // The server gets a private database file so the run never touches the real application data folder
            var databaseFile = Path.Combine(Path.GetTempPath(), "ReviewCenterSystemTests-" + Guid.NewGuid().ToString("N") + ".db");
            var dll = typeof(SystemTestHost).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "ServerDllPath").Value!;
            var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(dll)!
            };
            info.Environment["Database__ConnectionString"] = $"Data Source={databaseFile};Pooling=False";

            // The key ring lives beside the temporary database, protected by its permissions only: the test never
            // uses the key store of the operating system
            info.Environment["DataProtection__Protection"] = "PermissionsOnly";
            if (gitCachePath is not null)
            {
                info.Environment["Git__CachePath"] = gitCachePath;
            }

            // E-mail notifications are off unless a folder is given; then the server writes every mail as a file into
            // it, and its links start with the loopback address of this server
            if (notificationFolder is not null)
            {
                info.Environment["Notifications__Enabled"] = "true";
                info.Environment["Notifications__Sender"] = "File";
                info.Environment["Notifications__File__Directory"] = notificationFolder;
                info.Environment["Notifications__FromAddress"] = "reviewcenter@example.com";
                info.Environment["Notifications__BaseUrl"] = url;
            }

            // A signed-in flow needs a user: the command line creates the first administrator, as an operator would,
            // and then the ordinary users the test asked for (all with the same password)
            if (seedUser)
            {
                var users = new List<(string Name, string DisplayName, bool Admin)> { (UserName, "Puppet Admin", true) };
                users.AddRange((extraUsers ?? []).Select(user => (user.Name, user.DisplayName, false)));
                foreach (var (name, displayName, admin) in users)
                {
                    var add = new ProcessStartInfo(info.FileName)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = info.WorkingDirectory
                    };
                    add.Environment["Database__ConnectionString"] = info.Environment["Database__ConnectionString"];
                    add.Environment["REVIEWCENTER_PASSWORD"] = UserPassword;
                    foreach (var argument in new[] { dll, "user", "add", "--username", name, "--display-name", displayName })
                    {
                        add.ArgumentList.Add(argument);
                    }

                    if (admin)
                    {
                        add.ArgumentList.Add("--system-admin");
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
            }

            info.ArgumentList.Add(dll);
            info.ArgumentList.Add("serve");
            info.ArgumentList.Add("--urls");
            info.ArgumentList.Add(url);
            var process = Process.Start(info)!;
            var server = new TestServer(process, url, databaseFile);
            var started = false;
            try
            {
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
                            started = true;
                            return server;
                        }
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                    {
                        // Not accepting connections yet
                    }

                    await Task.Delay(100, cancellationToken);
                }

                throw new InvalidOperationException($"The ReviewCenter server at {url} did not become healthy.");
            }
            finally
            {
                if (!started)
                {
                    server.Dispose();
                }
            }
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
