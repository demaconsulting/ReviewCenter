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

using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.Messaging;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.ReviewCenter.Tests.Hosting;

/// <summary>
///     Subsystem tests for the Hosting subsystem: the server host and the endpoints it exposes.
/// </summary>
public class HostingSubsystemTests
{
    /// <summary>
    ///     Test that a health probe against the running host succeeds.
    /// </summary>
    [Fact]
    public async Task HostingSubsystem_HealthEndpoint_Requested_ReturnsHealthy()
    {
        // Arrange: start the host
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");

        // Act: probe health the way an operator or the Puppet tool does
        using var client = app.GetTestClient();
        var response = await client.GetAsync(ServerHost.HealthPath, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: success status and the agreed body
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(ServerHost.HealthyText, body);
    }

    /// <summary>
    ///     Test that logging configured by the host emits a structured start-up entry with a named property.
    /// </summary>
    [Fact]
    public async Task HostingSubsystem_Startup_LoggingConfigured_EmitsStructuredEntry()
    {
        // Arrange: start the host
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");

        // Assert: the application's own entry has a named property
        var started = Assert.Single(logs.Entries, e => e.Category == typeof(ServerHost).FullName);
        Assert.True(started.Properties.ContainsKey("Urls"), "Start-up entry should carry a Urls property");
    }

    /// <summary>
    ///     Test that the log entries of the web framework flow to the same sinks as the entries of the server.
    /// </summary>
    [Fact]
    public async Task HostingSubsystem_Startup_LoggingConfigured_FrameworkEntriesReachTheSameSinks()
    {
        // Arrange: start the host and make one request so the framework logs too
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: make one request
        await client.GetAsync(ServerHost.HealthPath, TestContext.Current.CancellationToken);

        // Assert: framework logging reached the same sink
        Assert.Contains(logs.Entries, e => e.Category.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                                           && e.Level == LogLevel.Information);
    }

    /// <summary>
    ///     Test that the host serves the home route and the health route from one pipeline.
    /// </summary>
    [Fact]
    public async Task HostingSubsystem_Routes_BothRegistered_HomeAndHealthAnswer()
    {
        // Arrange: start the host
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request both routes
        var home = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var health = await client.GetAsync(ServerHost.HealthPath, TestContext.Current.CancellationToken);

        // Assert: both answer 200
        Assert.Equal(System.Net.HttpStatusCode.OK, home.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
    }

    /// <summary>
    ///     Test that the host creates the configured database at start-up, serves pages from it, and that the
    ///     data is still served after the host is stopped and started again on the same database.
    /// </summary>
    [Fact]
    public async Task HostingSubsystem_Startup_DatabaseConfigured_ServesStoredProjectsAfterRestart()
    {
        // Arrange: a new database file, and a project stored through the first host
        var connectionString = TestDatabase.NewConnectionString();
        var logs = new CapturingLoggerProvider();
        await using (var first = await TestWebApp.StartAsync(logs, connectionString, ["serve", "--silent"]))
        {
            await TestWebApp.CreateProjectAsync(first, "Apollo", "Moon landing");
        }

        // Act: start a second host on the same database, sign in as a new user and request the projects page
        await using var second = await TestWebApp.StartAsync(logs, connectionString, ["serve", "--silent"]);
        using var browser = await TestBrowser.SignedInAsync(second, "ann");
        var html = await browser.Client.GetStringAsync("/projects", TestContext.Current.CancellationToken);

        // Assert: the stored project is listed
        Assert.Contains("Apollo", html, StringComparison.Ordinal);
        Assert.Contains("Moon landing", html, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the host creates the configured database file when the server starts.
    /// </summary>
    [Fact]
    public async Task HostingSubsystem_Startup_DatabaseConfigured_CreatesTheDatabaseFile()
    {
        // Arrange: a connection string for a database file that does not exist yet
        var connectionString = TestDatabase.NewConnectionString();
        var file = connectionString.Split(';')[0].Replace("Data Source=", string.Empty, StringComparison.Ordinal);
        Assert.False(File.Exists(file), "The database file should not exist before the host starts");
        var logs = new CapturingLoggerProvider();

        // Act: start the host on that database
        await using var app = await TestWebApp.StartAsync(logs, connectionString, ["serve", "--silent"]);

        // Assert: the file now exists
        Assert.True(File.Exists(file), $"Database file '{file}' was not created");
    }

    /// <summary>
    ///     Test that the project, review, user, signing, and inbox services are available from a started host
    ///     that is configured with a database.
    /// </summary>
    [Fact]
    public async Task HostingSubsystem_Startup_DatabaseConfigured_DomainServicesResolve()
    {
        // Arrange: a started host on a new database
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");

        // Act: resolve each domain service from the host
        var projects = app.Services.GetService<ProjectService>();
        var reviews = app.Services.GetService<ReviewService>();
        var users = app.Services.GetService<UserService>();
        var signing = app.Services.GetService<SigningService>();
        var inbox = app.Services.GetService<InboxService>();

        // Assert: every service is available
        Assert.NotNull(projects);
        Assert.NotNull(reviews);
        Assert.NotNull(users);
        Assert.NotNull(signing);
        Assert.NotNull(inbox);
    }

    /// <summary>
    ///     Test that a visitor who is not signed in is sent to the login page when asking for a page that
    ///     needs a signed-in user.
    /// </summary>
    [Fact]
    public async Task HostingSubsystem_Pages_AnonymousVisitor_ProtectedPageRedirectsToLogin()
    {
        // Arrange: a started host and a client that has no sign-in and does not follow redirects
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request a protected page without signing in
        var projects = await client.GetAsync("/projects", TestContext.Current.CancellationToken);

        // Assert: the protected page redirects to the login page
        Assert.Equal(System.Net.HttpStatusCode.Redirect, projects.StatusCode);
        Assert.Contains("/login", projects.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the login page is served to a visitor who is not signed in.
    /// </summary>
    [Fact]
    public async Task HostingSubsystem_Pages_AnonymousVisitor_LoginPageIsServed()
    {
        // Arrange: a started host and a client that has no sign-in
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request the login page without signing in
        var login = await client.GetAsync("/login", TestContext.Current.CancellationToken);

        // Assert: the login page answers 200
        Assert.Equal(System.Net.HttpStatusCode.OK, login.StatusCode);
    }

    /// <summary>
    ///     Test that a database setting the host cannot honor ends the run with exit code 1, instead of an
    ///     unhandled exception.
    /// </summary>
    [Fact]
    public async Task HostingSubsystem_Startup_UnsupportedDatabaseProvider_FailsWithExitCodeOne()
    {
        // Arrange: configuration that selects a provider that is not implemented
        using var context = Context.Create(["serve", "--silent"]);

        // Act: run the server
        var exitCode = await ServerHost.RunAsync(context, UnsupportedProviderOptions(), TestContext.Current.CancellationToken);

        // Assert: the run failed with exit code 1 and the context holds the failure
        Assert.Equal(1, exitCode);
        Assert.Equal(1, context.ExitCode);
    }

    /// <summary>
    ///     Test that a database setting the host cannot honor is reported with an error text that names the
    ///     problem on the context.
    /// </summary>
    [Fact]
    public async Task HostingSubsystem_Startup_UnsupportedDatabaseProvider_ReportsAnErrorNamingTheProvider()
    {
        // Arrange: a log file for the context and configuration that selects a provider that is not implemented
        using var folder = new TemporaryDirectory();
        var logFile = Path.Combine(folder.DirectoryPath, "run.log");

        // Act: run the server
        using (var context = Context.Create(["serve", "--silent", "--log", logFile]))
        {
            await ServerHost.RunAsync(context, UnsupportedProviderOptions(), TestContext.Current.CancellationToken);
        }

        // Assert: the error text names the provider
        var text = await File.ReadAllTextAsync(logFile, TestContext.Current.CancellationToken);
        Assert.Contains("Oracle", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the running web host has the notification worker when notifications are enabled, and that the
    ///     worker is running then.
    /// </summary>
    [Fact]
    public async Task HostingSubsystem_NotificationWorker_OnlyEnabledWebHost()
    {
        // Arrange: the settings that turn notifications on with the file sender
        var folder = Path.Combine(Path.GetTempPath(), $"rc-hosting-{Guid.NewGuid():N}");
        var settings = new Dictionary<string, string?>
        {
            ["Notifications:Enabled"] = "true",
            ["Notifications:Sender"] = "File",
            ["Notifications:FromAddress"] = "reviewcenter@example.com",
            ["Notifications:BaseUrl"] = "https://reviewcenter.example.com/",
            ["Notifications:File:Directory"] = folder
        };

        // Act: start a host with the settings
        await using var enabled = await TestWebApp.StartAsync(new CapturingLoggerProvider(), null, ["serve", "--silent"], settings);

        // Assert: it has the worker, and the worker was started by the host
        var worker = Assert.Single(enabled.Services.GetServices<IHostedService>().OfType<NotificationWorker>());
        Assert.NotNull(worker.ExecuteTask);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    ///     Test that the running web host has no notification worker when notifications are not enabled.
    /// </summary>
    [Fact]
    public async Task HostingSubsystem_NotificationWorker_NotEnabled_WebHostHasNoWorker()
    {
        // Arrange and act: start a host without notification settings
        await using var off = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");

        // Assert: it has no worker
        Assert.Empty(off.Services.GetServices<IHostedService>().OfType<NotificationWorker>());
    }

    /// <summary>
    ///     Build the host options that select a database provider that is not implemented.
    /// </summary>
    /// <returns>The options.</returns>
    private static ServerHostOptions UnsupportedProviderOptions()
    {
        return new ServerHostOptions
        {
            MapStaticAssets = false,
            ConfigureBuilder = builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "Oracle",
                    ["Database:ConnectionString"] = TestDatabase.NewConnectionString()
                });
            }
        };
    }
}
