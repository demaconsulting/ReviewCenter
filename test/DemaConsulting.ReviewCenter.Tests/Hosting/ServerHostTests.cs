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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.ReviewCenter.Tests.Hosting;

/// <summary>
///     Unit tests for the <see cref="ServerHost"/> composition root.
/// </summary>
public class ServerHostTests
{
    /// <summary>
    ///     Test that the health route answers 200 with the body "Healthy".
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_HealthRoute_Returns200Healthy()
    {
        // Arrange: start the real pipeline on an in-memory server
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");

        // Act: request the health route
        using var client = app.GetTestClient();
        var response = await client.GetAsync(ServerHost.HealthPath, TestContext.Current.CancellationToken);

        // Assert: 200 and the plain-text body
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that start-up writes one structured entry carrying the listen URL as a property.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_StartupLogging_EmitsStructuredEntry()
    {
        // Arrange: create a capturing logger provider
        var logs = new CapturingLoggerProvider();

        // Act: start the pipeline with an explicit URL
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--urls", "http://127.0.0.1:6123", "--silent");

        // Assert: the entry uses a message template with a named Urls property, not a pre-formatted string
        var entry = Assert.Single(logs.Entries, e => e.Category == typeof(ServerHost).FullName);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("http://127.0.0.1:6123", entry.Properties["Urls"]);
        Assert.Contains("{Urls}", (string)entry.Properties["{OriginalFormat}"]!);
        Assert.Contains("http://127.0.0.1:6123", entry.Message);
    }

    /// <summary>
    ///     Test that silent mode leaves the console logger provider out.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_Silent_OmitsConsoleProvider()
    {
        // Arrange: create a capturing logger provider
        var logs = new CapturingLoggerProvider();

        // Act: start the pipeline silently
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");

        // Assert: no console provider is registered
        var providers = app.Services.GetServices<ILoggerProvider>().Select(p => p.GetType().Name);
        Assert.DoesNotContain("ConsoleLoggerProvider", providers);
    }

    /// <summary>
    ///     Test that without silent mode the console logger provider is registered.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_NotSilent_IncludesConsoleProvider()
    {
        // Arrange: create a capturing logger provider
        var logs = new CapturingLoggerProvider();

        // Act: start the pipeline with console logging on
        await using var app = await TestWebApp.StartAsync(logs, "serve");

        // Assert: the console provider is registered
        var providers = app.Services.GetServices<ILoggerProvider>().Select(p => p.GetType().Name);
        Assert.Contains("ConsoleLoggerProvider", providers);
    }

    /// <summary>
    ///     Test that a null context is rejected.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_NullContext_ThrowsArgumentNullException()
    {
        // Arrange: no setup required; null is the input under test

        // Act: create the application with a null context
        var exception = Record.Exception(() => ServerHost.CreateApplication(null!));

        // Assert: an ArgumentNullException is thrown
        Assert.IsType<ArgumentNullException>(exception);
    }

    /// <summary>
    ///     Test that a database configured through the <c>Database</c> section is created and usable:
    ///     the file and its folder appear, and the domain services work against it.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_DatabaseConfigured_CreatesFileAndServesDomainServices()
    {
        // Arrange: a connection string whose folder does not exist yet
        var folder = Path.Combine(Path.GetTempPath(), "ReviewCenterTests-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(folder, "nested", "configured.db");
        var logs = new CapturingLoggerProvider();

        try
        {
            // Act: start the pipeline against that database and create a project through the registered service
            await using (var app = await TestWebApp.StartAsync(logs, $"Data Source={file};Pooling=False", ["serve", "--silent"]))
            {
                var project = await app.Services.GetRequiredService<ProjectService>()
                    .CreateAsync("Apollo", null, TestContext.Current.CancellationToken);

                // Assert: the database file exists and the project was stored in it
                Assert.True(File.Exists(file));
                Assert.True(project.Id > 0);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that data survives a restart: a project created before the application is stopped is found by
    ///     a new application instance that uses the same database file.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_RestartedOnSameDatabase_KeepsData()
    {
        // Arrange: a database file shared by two application instances
        var connectionString = TestDatabase.NewConnectionString();
        var logs = new CapturingLoggerProvider();
        int projectId;
        await using (var first = await TestWebApp.StartAsync(logs, connectionString, ["serve", "--silent"]))
        {
            var project = await first.Services.GetRequiredService<ProjectService>()
                .CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
            projectId = project.Id;
        }

        // Act: start a second instance on the same database
        await using var second = await TestWebApp.StartAsync(logs, connectionString, ["serve", "--silent"]);
        var found = await second.Services.GetRequiredService<ProjectService>()
            .GetAsync(projectId, TestContext.Current.CancellationToken);

        // Assert: the project is still there
        Assert.Equal("Apollo", found?.Name);
    }

    /// <summary>
    ///     Test that an unsupported database provider stops the build with an error naming the provider.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_UnsupportedProvider_ThrowsClearError()
    {
        // Arrange: configuration that selects a provider the product does not implement
        using var context = Context.Create(["serve", "--silent"]);
        var options = UnsupportedProviderOptions();

        // Act: create the application
        var exception = Assert.Throws<InvalidOperationException>(() => ServerHost.CreateApplication(context, options));

        // Assert: the message says what is wrong
        Assert.Contains("Failed to prepare the database", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Oracle", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a database failure at start-up is reported as an error with exit code 1 instead of an
    ///     unhandled exception. No port is bound, because the failure happens before the server starts.
    /// </summary>
    [Fact]
    public async Task ServerHost_RunAsync_UnsupportedProvider_ReturnsExitCodeOne()
    {
        // Arrange: configuration that selects an unsupported provider
        using var context = Context.Create(["serve", "--silent"]);

        // Act: run the server
        var exitCode = await ServerHost.RunAsync(context, TestContext.Current.CancellationToken, UnsupportedProviderOptions());

        // Assert: exit code 1, and the context recorded the error
        Assert.Equal(1, exitCode);
        Assert.Equal(1, context.ExitCode);
    }

    /// <summary>
    ///     Test that an unusable database location (the file path is an existing folder) is reported as an
    ///     error with exit code 1.
    /// </summary>
    [Fact]
    public async Task ServerHost_RunAsync_UnusableDatabaseLocation_ReturnsExitCodeOne()
    {
        // Arrange: a database path that is an existing directory
        var folder = Path.Combine(Path.GetTempPath(), "ReviewCenterTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var context = Context.Create(["serve", "--silent"]);
        var options = new ServerHostOptions
        {
            MapStaticAssets = false,
            ConfigureBuilder = builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = $"Data Source={folder};Pooling=False"
                });
            }
        };

        try
        {
            // Act: run the server
            var exitCode = await ServerHost.RunAsync(context, TestContext.Current.CancellationToken, options);

            // Assert: exit code 1 rather than an unhandled exception
            Assert.Equal(1, exitCode);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Creates host options that select an unsupported database provider and a throwaway database.
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
