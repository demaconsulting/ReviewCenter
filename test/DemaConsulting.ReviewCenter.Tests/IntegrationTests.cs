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

using DemaConsulting.ReviewCenter.Utilities;

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     System-level integration tests that run the ReviewCenter application via dotnet.
/// </summary>
[Collection("Sequential")]
public class IntegrationTests
{
    private readonly string _dllPath;

    /// <summary>
    ///     Initialize test by locating the ReviewCenter DLL.
    /// </summary>
    public IntegrationTests()
    {
        // The DLL should be in the same directory as the test assembly
        // because the test project references the main project
        var baseDir = AppContext.BaseDirectory;
        _dllPath = PathHelpers.SafePathCombine(baseDir, "DemaConsulting.ReviewCenter.dll");

        Assert.True(File.Exists(_dllPath), $"Could not find ReviewCenter DLL at {_dllPath}");
    }

    /// <summary>
    ///     Test that version flag outputs version information.
    /// </summary>
    [Fact]
    public void ReviewCenter_VersionFlag_Provided_OutputsVersion()
    {
        // Arrange: (none — constructor initializes _dllPath)

        // Act: run the tool with version flag
        var exitCode = Runner.Run(
            out var output,
            "dotnet",
            _dllPath,
            "--version");

        // Assert: version string is printed; no banner or errors
        Assert.Equal(0, exitCode);
        Assert.Matches(@"\d+\.\d+\.\d+", output);
        Assert.DoesNotContain("Error", output);
        Assert.DoesNotContain("Copyright", output);
    }

    /// <summary>
    ///     Test that help flag outputs usage information.
    /// </summary>
    [Fact]
    public void ReviewCenter_HelpFlag_Provided_OutputsUsageInformation()
    {
        // Arrange: (none — constructor initializes _dllPath)

        // Act: run the tool with help flag
        var exitCode = Runner.Run(
            out var output,
            "dotnet",
            _dllPath,
            "--help");

        // Assert: usage text contains required sections and key flags
        Assert.Equal(0, exitCode);
        Assert.Contains("Usage:", output);
        Assert.Contains("Options:", output);
        Assert.Contains("--version", output);
        Assert.Contains("--help", output);
    }

    /// <summary>
    ///     Test that no arguments displays the tool banner and runs default logic.
    /// </summary>
    [Fact]
    public void ReviewCenter_NoArguments_Invoked_DisplaysBanner()
    {
        // Arrange: (none — constructor initializes _dllPath)

        // Act: run the tool with no arguments
        var exitCode = Runner.Run(
            out var output,
            "dotnet",
            _dllPath);

        // Assert: banner is displayed with tool name and copyright; exit code is success
        Assert.Equal(0, exitCode);
        Assert.Contains("ReviewCenter version", output);
        Assert.Contains("Copyright", output);
    }

    /// <summary>
    ///     Test that validate flag runs self-validation and outputs summary.
    /// </summary>
    [Fact]
    public void ReviewCenter_ValidateFlag_Provided_RunsValidation()
    {
        // Arrange: (none — constructor initializes _dllPath)

        // Act: run the tool with validate flag
        var exitCode = Runner.Run(
            out var output,
            "dotnet",
            _dllPath,
            "--validate");

        // Assert: validation summary is present; exit code is success
        Assert.Equal(0, exitCode);
        Assert.Contains("Total Tests:", output);
        Assert.Contains("Passed:", output);
    }

    /// <summary>
    ///     Test that validate with --results flag generates a TRX file.
    /// </summary>
    [Fact]
    public void ReviewCenter_ValidateWithTrxResults_Requested_GeneratesTrxFile()
    {
        // Arrange: temporary TRX results file path
        var resultsFile = Path.Combine(Path.GetTempPath(), $"integration_test_{Guid.NewGuid()}.trx");

        try
        {
            // Act: run validation with TRX results output
            var exitCode = Runner.Run(
                out var _,
                "dotnet",
                _dllPath,
                "--validate",
                "--results",
                resultsFile);

            // Assert: results file is created with valid TRX structure
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(resultsFile), "Results file was not created");

            var trxContent = File.ReadAllText(resultsFile);
            Assert.Contains("<TestRun", trxContent);
            Assert.Contains("</TestRun>", trxContent);
        }
        finally
        {
            if (File.Exists(resultsFile))
            {
                File.Delete(resultsFile);
            }
        }
    }

    /// <summary>
    ///     Test that validate with --result (legacy alias) flag generates a results file.
    /// </summary>
    [Fact]
    public void ReviewCenter_ResultAlias_LegacyFlag_WritesResultsFile()
    {
        // Arrange: temporary TRX results file path; use legacy --result alias
        var resultsFile = Path.Combine(Path.GetTempPath(), $"integration_test_{Guid.NewGuid()}.trx");

        try
        {
            // Act: run validation with legacy --result alias
            var exitCode = Runner.Run(
                out var _,
                "dotnet",
                _dllPath,
                "--validate",
                "--result",
                resultsFile);

            // Assert: results file is created; exit code is success
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(resultsFile), "Results file was not created for legacy --result alias");
        }
        finally
        {
            if (File.Exists(resultsFile))
            {
                File.Delete(resultsFile);
            }
        }
    }

    /// <summary>
    ///     Test that validate with an unsupported results extension returns a non-zero exit code.
    /// </summary>
    [Fact]
    public void ReviewCenter_ValidateWithBadExtension_ExtensionInvalid_ReturnsNonZero()
    {
        // Arrange: unsupported results file extension triggers WriteError → ExitCode 1
        var resultsFile = Path.Combine(Path.GetTempPath(), $"integration_test_{Guid.NewGuid()}.bad");

        try
        {
            // Act: run validation with unsupported extension
            var exitCode = Runner.Run(
                out var _,
                "dotnet",
                _dllPath,
                "--validate",
                "--results",
                resultsFile);

            // Assert: non-zero exit code indicates the error path was triggered
            Assert.NotEqual(0, exitCode);
            Assert.False(File.Exists(resultsFile), "No file should be created for unsupported extension");
        }
        finally
        {
            if (File.Exists(resultsFile))
            {
                File.Delete(resultsFile);
            }
        }
    }

    /// <summary>
    ///     Test that silent flag suppresses output.
    /// </summary>
    [Fact]
    public void ReviewCenter_SilentFlag_Provided_SuppressesOutput()
    {
        // Arrange: (none — constructor initializes _dllPath)

        // Act: run the tool with --version and --silent to produce deterministic silent output
        var exitCode = Runner.Run(
            out var output,
            "dotnet",
            _dllPath,
            "--version",
            "--silent");

        // Assert: no console output in silent mode
        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(output), $"Expected no output in silent mode but got: {output}");
    }

    /// <summary>
    ///     Test that log flag writes output to a file.
    /// </summary>
    [Fact]
    public void ReviewCenter_LogFlag_Provided_WritesOutputToFile()
    {
        // Arrange: temporary log file path
        var logFile = Path.GetTempFileName();

        try
        {
            // Act: run the tool with log flag
            var exitCode = Runner.Run(
                out var output,
                "dotnet",
                _dllPath,
                "--log",
                logFile);

            // Assert: log file is created and contains tool output; console output matches
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(logFile), "Log file was not created");

            var logContent = File.ReadAllText(logFile);
            Assert.Contains("ReviewCenter version", logContent);
            Assert.Contains("ReviewCenter version", output);
        }
        finally
        {
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }
        }
    }

    /// <summary>
    ///     Test that validate with --results flag generates a JUnit XML file.
    /// </summary>
    [Fact]
    public void ReviewCenter_ValidateWithXmlResults_Requested_GeneratesJUnitFile()
    {
        // Arrange: temporary XML results file path
        var resultsFile = Path.Combine(Path.GetTempPath(), $"integration_test_{Guid.NewGuid()}.xml");

        try
        {
            // Act: run validation with JUnit XML results output
            var exitCode = Runner.Run(
                out var _,
                "dotnet",
                _dllPath,
                "--validate",
                "--results",
                resultsFile);

            // Assert: results file is created with valid JUnit structure
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(resultsFile), "Results file was not created");

            var xmlContent = File.ReadAllText(resultsFile);
            Assert.Contains("<testsuites", xmlContent);
        }
        finally
        {
            if (File.Exists(resultsFile))
            {
                File.Delete(resultsFile);
            }
        }
    }

    /// <summary>
    ///     Test that an unknown argument causes an error message and non-zero exit code.
    /// </summary>
    [Fact]
    public void ReviewCenter_UnknownArgument_Provided_ReturnsError()
    {
        // Arrange: (none — constructor initializes _dllPath)

        // Act: run the tool with an unknown argument
        var exitCode = Runner.Run(
            out var output,
            "dotnet",
            _dllPath,
            "--unknown");

        // Assert: non-zero exit code and error message naming the unrecognized flag
        Assert.NotEqual(0, exitCode);
        Assert.Contains("Error", output);
        Assert.Contains("--unknown", output);
    }

    /// <summary>
    ///     Test that validate with depth flag outputs headings at the specified depth.
    /// </summary>
    [Fact]
    public void ReviewCenter_ValidateWithDepth_DepthThree_OutputsCorrectHeadingLevel()
    {
        // Arrange: (none — constructor initializes _dllPath)

        // Act: run validation with heading depth 3
        var exitCode = Runner.Run(
            out var output,
            "dotnet",
            _dllPath,
            "--validate",
            "--depth",
            "3");

        // Assert: output contains level-3 markdown headings
        Assert.Equal(0, exitCode);
        Assert.Contains("###", output);
    }

    /// <summary>
    ///     Test that the serve verb starts a server whose health endpoint answers "Healthy".
    /// </summary>
    [Fact]
    public async Task ReviewCenter_ServeVerb_Started_HealthEndpointReturnsHealthy()
    {
        // Arrange: start the server on a free port
        using var server = ServerProcess.Start();

        // Act: wait for the server and request the health endpoint
        var healthy = await server.WaitUntilHealthyAsync(TimeSpan.FromSeconds(30));
        using var client = new HttpClient();
        var response = healthy ? await client.GetAsync($"{server.BaseUrl}/health", TestContext.Current.CancellationToken) : null;

        // Assert: 200 with the plain-text body
        Assert.True(healthy, "Server did not become healthy in time");
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the serve verb listens on the URL given with --urls.
    /// </summary>
    [Fact]
    public async Task ReviewCenter_ServeWithUrls_PortSpecified_ListensOnRequestedPort()
    {
        // Arrange: start the server; ServerProcess passes --urls with a free port
        using var server = ServerProcess.Start();

        // Act: wait until the requested address answers
        var healthy = await server.WaitUntilHealthyAsync(TimeSpan.FromSeconds(30));

        // Assert: the server answers on exactly the address that was requested
        Assert.True(healthy, $"Server did not answer on {server.BaseUrl}");
    }

    /// <summary>
    ///     Test that --urls without the serve verb is rejected with a non-zero exit code.
    /// </summary>
    [Fact]
    public void ReviewCenter_UrlsWithoutServe_Provided_ReturnsError()
    {
        // Arrange: (none — constructor initializes _dllPath)

        // Act: run the tool with --urls but no serve verb
        var exitCode = Runner.Run(
            out var output,
            "dotnet",
            _dllPath,
            "--urls",
            "http://127.0.0.1:6001");

        // Assert: error names the verb that is required
        Assert.NotEqual(0, exitCode);
        Assert.Contains("serve", output);
    }

    /// <summary>
    ///     Test that the home page served over HTTP contains the title and the interactive controls.
    /// </summary>
    [Fact]
    public async Task ReviewCenter_ServeVerb_Started_HomePageContainsTitleAndControls()
    {
        // Arrange: start the server and wait for it
        using var server = ServerProcess.Start();
        var healthy = await server.WaitUntilHealthyAsync(TimeSpan.FromSeconds(30));
        Assert.True(healthy, "Server did not become healthy in time");

        // Act: request the home page and the Blazor client script
        using var client = new HttpClient();
        var html = await client.GetStringAsync($"{server.BaseUrl}/", TestContext.Current.CancellationToken);
        var script = await client.GetByteArrayAsync($"{server.BaseUrl}/_framework/blazor.web.js", TestContext.Current.CancellationToken);

        // Assert: title, test ids, and a non-empty client script (needed for interactivity)
        Assert.Contains("<title>ReviewCenter</title>", html);
        Assert.Contains("data-testid=\"hello-button\"", html);
        Assert.Contains("data-testid=\"counter-button\"", html);
        Assert.Contains("data-testid=\"counter-value\"", html);
        Assert.NotEmpty(script);
    }

    /// <summary>
    ///     Test that the running server sends an anonymous request for the projects page to the login page, and
    ///     that the database file named by the configuration is created at start-up.
    /// </summary>
    [Fact]
    public async Task ReviewCenter_ServeVerb_Started_RedirectsAnonymousProjectsRequestToLoginAndCreatesDatabase()
    {
        // Arrange: start the server; the test process points it at a temporary database
        using var server = ServerProcess.Start();
        var healthy = await server.WaitUntilHealthyAsync(TimeSpan.FromSeconds(30));
        Assert.True(healthy, "Server did not become healthy in time");

        // Act: request the projects page without signing in, and the login page
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler);
        var projects = await client.GetAsync($"{server.BaseUrl}/projects", TestContext.Current.CancellationToken);
        var login = await client.GetStringAsync($"{server.BaseUrl}/login", TestContext.Current.CancellationToken);

        // Assert: the projects page redirects to the login page, which serves the form, and the database file exists
        Assert.Equal(System.Net.HttpStatusCode.Redirect, projects.StatusCode);
        Assert.Contains("/login?ReturnUrl=", projects.Headers.Location?.ToString(), StringComparison.Ordinal);
        Assert.Contains("data-testid=\"login-form\"", login);
        var connectionString = Environment.GetEnvironmentVariable("Database__ConnectionString");
        Assert.NotNull(connectionString);
        var file = connectionString.Split(';')[0].Replace("Data Source=", string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(file), $"Database file '{file}' was not created");
    }

    /// <summary>
    ///     Test that the user add verb creates a user with the password from the environment variable, never
    ///     prints the password, and refuses a second user with the same name.
    /// </summary>
    [Fact]
    public void ReviewCenter_UserAddVerb_PasswordFromEnvironment_CreatesUserThenRejectsDuplicate()
    {
        // Arrange: a new database and the password in the environment, not on the command line
        const string password = "correct horse battery";
        var environment = new Dictionary<string, string>
        {
            [TestDatabase.ConnectionStringVariable] = TestDatabase.NewConnectionString(),
            ["REVIEWCENTER_PASSWORD"] = password
        };

        // Act: add the same user twice
        var first = Runner.RunWithEnvironment(
            out var firstOutput, environment, "dotnet", _dllPath, "user", "add", "--username", "root", "--display-name", "Root", "--role", "Admin");
        var second = Runner.RunWithEnvironment(
            out var secondOutput, environment, "dotnet", _dllPath, "user", "add", "--username", "root", "--display-name", "Root", "--role", "Admin");

        // Assert: the first succeeds, the second fails, and the password is never printed
        Assert.Equal(0, first);
        Assert.Contains("Created user 'root' with the role Admin.", firstOutput);
        Assert.Equal(1, second);
        Assert.Contains("already exists", secondOutput);
        Assert.DoesNotContain(password, firstOutput + secondOutput);
    }

    /// <summary>
    ///     Test that the user add verb without any password source fails with a clear message instead of hanging.
    /// </summary>
    [Fact]
    public void ReviewCenter_UserAddVerb_NoPassword_ReturnsError()
    {
        // Arrange: no password in the environment, and standard input that is closed
        var environment = new Dictionary<string, string>
        {
            [TestDatabase.ConnectionStringVariable] = TestDatabase.NewConnectionString(),
            ["REVIEWCENTER_PASSWORD"] = string.Empty
        };

        // Act: add a user
        var exitCode = Runner.RunWithEnvironment(
            out var output, environment, "dotnet", _dllPath, "user", "add", "--username", "root", "--display-name", "Root");

        // Assert: a clear error and a non-zero exit code
        Assert.Equal(1, exitCode);
        Assert.Contains("No password was provided.", output);
    }

    /// <summary>
    ///     Test that the serve verb writes a structured start-up log entry that names the listen URL.
    /// </summary>
    [Fact]
    public async Task ReviewCenter_ServeVerb_Started_LogsStartupEntryWithListenUrl()
    {
        // Arrange: start the server on a free port and wait until it answers
        using var server = ServerProcess.Start();
        var healthy = await server.WaitUntilHealthyAsync(TimeSpan.FromSeconds(30));
        Assert.True(healthy, "Server did not become healthy in time");

        // Act: give the console logger a moment to flush the start-up entry
        var expected = $"listening on {server.BaseUrl}";
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !server.Output.Contains(expected, StringComparison.Ordinal))
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        // Assert: the entry was written and names the address the server listens on
        Assert.Contains("ReviewCenter server started", server.Output, StringComparison.Ordinal);
        Assert.Contains(expected, server.Output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a running server stops by itself, with exit code zero, when it is asked to terminate the way a
    ///     process supervisor or container runtime does. Not run on Windows, which cannot signal a child process.
    /// </summary>
    [Fact]
    public async Task ReviewCenter_ServeVerb_TerminationRequested_StopsGracefullyWithExitCodeZero()
    {
        // Arrange: a running server (a termination signal cannot be sent to a child process on Windows)
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A graceful stop signal cannot be sent to a child process on Windows.");
        using var server = ServerProcess.Start();
        var healthy = await server.WaitUntilHealthyAsync(TimeSpan.FromSeconds(30));
        Assert.True(healthy, "Server did not become healthy in time");

        // Act: ask the server to terminate and wait for it to exit by itself
        server.RequestGracefulStop();
        var exitCode = server.WaitForExit(TimeSpan.FromSeconds(30));

        // Assert: it stopped within the time limit with a successful exit code
        Assert.NotNull(exitCode);
        Assert.Equal(0, exitCode);
    }

    /// <summary>
    ///     Test that the serve verb starting on a port already in use fails with a non-zero exit code.
    /// </summary>
    [Fact]
    public void ReviewCenter_ServeVerb_PortInUse_ReturnsError()
    {
        // Arrange: occupy a port
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

            // Act: try to serve on the occupied port
            var exitCode = Runner.Run(
                out var output,
                "dotnet",
                _dllPath,
                "serve",
                "--urls",
                $"http://127.0.0.1:{port}");

            // Assert: a clear error and a non-zero exit code, not an unhandled exception
            Assert.NotEqual(0, exitCode);
            Assert.Contains("Failed to start the server", output);
        }
        finally
        {
            listener.Stop();
        }
    }
}

