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

using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     System-level integration tests that run the ReviewCenter application via dotnet.
/// </summary>
[Collection("Sequential")]
public partial class IntegrationTests
{
    private readonly string _dllPath;

    /// <summary>
    ///     Matches the start of a JUnit test case element.
    /// </summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("<testcase ")]
    private static partial Regex TestCaseElement();

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
        // Arrange: the shared real process run (see RealValidationRun.Process)

        // Act: read the outcome of the process run
        var run = RealValidationRun.Process;

        // Assert: validation summary is present for all nine checks; exit code is success
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Total Tests: 9", run.Output);
        Assert.Contains("Passed: 9", run.Output);
        Assert.Contains("Failed: 0", run.Output);
    }

    /// <summary>
    ///     Test that validate with --result (legacy alias) flag generates a results file.
    /// </summary>
    [Fact]
    public void ReviewCenter_ResultAlias_LegacyFlag_WritesResultsFile()
    {
        // Arrange: the shared real process run, which used the legacy --result alias

        // Act: read the outcome of the process run
        var run = RealValidationRun.Process;

        // Assert: results file is created; exit code is success
        Assert.Equal(0, run.ExitCode);
        Assert.False(string.IsNullOrEmpty(run.ResultsContent), "Results file was not created for legacy --result alias");
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
        // Arrange: the shared real process run, which asked for a JUnit XML results file

        // Act: read the outcome of the process run
        var run = RealValidationRun.Process;

        // Assert: results file is created with valid JUnit structure and one test case per check
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("<testsuites", run.ResultsContent);
        Assert.Equal(9, TestCaseElement().Count(run.ResultsContent));
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
        // Arrange: the shared real process run, which asked for heading depth 3

        // Act: read the outcome of the process run
        var run = RealValidationRun.Process;

        // Assert: output contains level-3 markdown headings
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("### DEMA Consulting", run.Output);
    }

    /// <summary>
    ///     Test the whole integrity journey on the running host with a real SQLite database: an author creates a
    ///     review, a separate reviewer signs it so it is approved and locked, a change is refused, an administrator
    ///     reopens it with a reason, the old signature no longer approves, a new signature approves it again, and the
    ///     attestation PDF then shows the reopen history.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task IntegrationTests_ApprovalLockReopenResign_EndToEnd()
    {
        // Arrange: a host, two administrators (ann authors and reopens, cyd reviews), and a draft review of ann
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await Api.ApiTestSupport.StartAsync();
        await Api.ApiTestSupport.EnsureUserAsync(app, "ann");
        await Api.ApiTestSupport.EnsureUserAsync(app, "bob");
        await Api.ApiTestSupport.EnsureUserAsync(app, "cyd");
        var users = app.Services.GetRequiredService<UserService>();
        var cyd = (await users.GetAsync("cyd", cancellationToken))!;
        var project = await app.Services.GetRequiredService<ProjectService>().CreateAsync("Apollo", null, cancellationToken);
        var store = app.Services.GetRequiredService<IReviewStore>();
        await store.AddMembershipAsync(cyd.Id, project.Id, cancellationToken);
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id, "rev-1", "First review", [new("a.txt", Core.TestContent.A)], Core.TestActors.Bob, cancellationToken);
        var checklists = app.Services.GetRequiredService<ChecklistService>();
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await checklists.SetAnswerAsync(
                project.Id, "rev-1", item.Id, ChecklistOutcome.Pass, null, Core.TestActors.Ann, cancellationToken);
        }

        var signing = app.Services.GetRequiredService<SigningService>();
        var slot = await signing.AddSlotAsync(Core.TestActors.Ann, project.Id, "rev-1", cyd.Id, null, cancellationToken);
        var reviewer = new Actor("cyd", "Cyd");

        // Act: the separate reviewer signs; then a change is tried, and the attestation is read while locked
        var first = (await signing.GetApprovalAsync(project.Id, "rev-1", cancellationToken))!;
        await signing.SignAsync(reviewer, project.Id, "rev-1", slot.Id, first.Digest, Tests.TestBrowser.Password, cancellationToken);
        var locked = (await signing.GetApprovalAsync(project.Id, "rev-1", cancellationToken))!;
        var refusal = await Assert.ThrowsAsync<DomainRuleException>(() => app.Services.GetRequiredService<ThreadService>().StartThreadAsync(
            project.Id, "rev-1", new ThreadAnchor("a.txt", 1, 1), reviewer, "Late remark", cancellationToken));
        using var client = Api.ApiTestSupport.ClientFor(app, (await Api.ApiTestSupport.CreateTokenAsync(app)).Value);
        var attestationRoute = $"/api/projects/{project.Id}/reviews/rev-1/attestation.pdf";
        var lockedPdf = await client.GetAsync(attestationRoute, cancellationToken);

        // Assert: approved and locked, the change refused, the attestation still available without a reopen history
        Assert.True(first.Slots.All(s => s.SignedAt is null));
        Assert.True(locked.IsApproved);
        Assert.Equal(first.Digest, locked.Digest);
        Assert.Equal(DomainRule.ReviewLocked, refusal.Rule);
        Assert.Equal(System.Net.HttpStatusCode.OK, lockedPdf.StatusCode);
        Assert.DoesNotContain("Reopen history", string.Join("\n", new Evidence.PdfInspector(await lockedPdf.Content.ReadAsByteArrayAsync(cancellationToken)).TextLines), StringComparison.Ordinal);

        // Act: an administrator reopens it with a reason, then the reviewer signs the new digest
        await signing.ReopenReviewAsync(Core.TestActors.Ann, project.Id, "rev-1", "Missed a case.", cancellationToken);
        var reopened = (await signing.GetApprovalAsync(project.Id, "rev-1", cancellationToken))!;
        await signing.SignAsync(reviewer, project.Id, "rev-1", slot.Id, reopened.Digest, Tests.TestBrowser.Password, cancellationToken);
        var approvedAgain = (await signing.GetApprovalAsync(project.Id, "rev-1", cancellationToken))!;
        var finalPdf = await client.GetAsync(attestationRoute, cancellationToken);

        // Assert: reopened review was unsigned for the new digest, approved again by a fresh signature, with the history in the attestation
        Assert.False(reopened.IsApproved);
        Assert.NotEqual(locked.Digest, reopened.Digest);
        Assert.True(approvedAgain.IsApproved);
        Assert.Equal(reopened.Digest, approvedAgain.Digest);
        var reopen = Assert.Single(approvedAgain.Reopens!);
        Assert.Equal("Missed a case.", reopen.Reason);
        Assert.Equal(locked.Digest, reopen.PreviousDigest);
        Assert.Equal(System.Net.HttpStatusCode.OK, finalPdf.StatusCode);
        var text = string.Join("\n", new Evidence.PdfInspector(await finalPdf.Content.ReadAsByteArrayAsync(cancellationToken)).TextLines);
        Assert.Contains("Reopen history", text, StringComparison.Ordinal);
        Assert.Contains("Missed a case.", text, StringComparison.Ordinal);
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
    ///     Test that the home page served over HTTP contains the title and the tagline.
    /// </summary>
    [Fact]
    public async Task ReviewCenter_ServeVerb_Started_HomePageContainsTitleAndTagline()
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
        Assert.Contains("Web-based code review for regulated projects.", html);
        Assert.NotEmpty(script);
    }

    /// <summary>
    ///     Test that the running server serves the built-in Help and the About page to a visitor who is not signed in.
    /// </summary>
    [Fact]
    public async Task ReviewCenter_ServeVerb_Started_ServesHelpAndAboutPagesWithoutSignIn()
    {
        // Arrange: start the server and wait for it
        using var server = ServerProcess.Start();
        var healthy = await server.WaitUntilHealthyAsync(TimeSpan.FromSeconds(30));
        Assert.True(healthy, "Server did not become healthy in time");

        // Act: request the help index, a help topic, and the about page
        using var client = new HttpClient();
        var index = await client.GetStringAsync($"{server.BaseUrl}/help", TestContext.Current.CancellationToken);
        var topic = await client.GetStringAsync($"{server.BaseUrl}/help/getting-started", TestContext.Current.CancellationToken);
        var about = await client.GetStringAsync($"{server.BaseUrl}/about", TestContext.Current.CancellationToken);

        // Assert: each page is served with its own marker
        Assert.Contains("data-testid=\"help-page\"", index);
        Assert.Contains("data-testid=\"help-topic-page\"", topic);
        Assert.Contains("data-testid=\"about-page\"", about);
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
            out var firstOutput, environment, "dotnet", _dllPath, "user", "add", "--username", "root", "--display-name", "Root", "--system-admin");
        var second = Runner.RunWithEnvironment(
            out var secondOutput, environment, "dotnet", _dllPath, "user", "add", "--username", "root", "--display-name", "Root", "--system-admin");

        // Assert: the first succeeds, the second fails, and the password is never printed
        Assert.Equal(0, first);
        Assert.Contains("Created user 'root' (system administrator).", firstOutput);
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
    ///     Test that the published application records its history in a tamper-evident audit trail: the audit verify
    ///     verb reports an intact chain with exit code 0, and after an entry in the middle of the history is edited
    ///     in the database file behind the application's back, it reports the first broken entry with exit code 1.
    /// </summary>
    [Fact]
    public void ReviewCenter_AuditVerifyVerb_HistoryEditedInDatabase_ReportsFirstBrokenEntryWithExitCodeOne()
    {
        // Arrange: a new database whose history holds three audited events (three users added by the application)
        var connectionString = TestDatabase.NewConnectionString();
        var environment = new Dictionary<string, string>
        {
            [TestDatabase.ConnectionStringVariable] = connectionString,
            ["REVIEWCENTER_PASSWORD"] = "correct horse battery"
        };
        foreach (var name in new[] { "ann", "bob", "carl" })
        {
            Assert.Equal(
                0,
                Runner.RunWithEnvironment(
                    out _, environment, "dotnet", _dllPath, "user", "add", "--username", name, "--display-name", name));
        }

        // Act: verify the untouched history, edit the second entry in the file, and verify again
        var intactCode = Runner.RunWithEnvironment(out var intactOutput, environment, "dotnet", _dllPath, "audit", "verify");
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE AuditEntries SET Detail = 'forged' WHERE Sequence = 2";
            command.ExecuteNonQuery();
        }

        var brokenCode = Runner.RunWithEnvironment(out var brokenOutput, environment, "dotnet", _dllPath, "audit", "verify");

        // Assert: intact before the edit, and the edited entry is named afterwards
        Assert.Equal(0, intactCode);
        Assert.Contains("Audit chain intact", intactOutput);
        Assert.Equal(1, brokenCode);
        Assert.Contains("Audit chain BROKEN at entry 2", brokenOutput);
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
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
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

