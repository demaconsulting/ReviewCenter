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
using DemaConsulting.ReviewCenter.ContentSources;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.Storage;
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
    ///     Test that a session length that is not positive stops the build with an error naming the setting.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_NonPositiveSessionHours_ThrowsClearError()
    {
        // Arrange: configuration with a session length of zero hours
        using var context = Context.Create(["serve", "--silent"]);
        var options = new ServerHostOptions
        {
            MapStaticAssets = false,
            ConfigureBuilder = builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = TestDatabase.NewConnectionString(),
                    ["Auth:SessionHours"] = "0"
                });
            }
        };

        // Act: create the application
        var exception = Assert.Throws<InvalidOperationException>(() => ServerHost.CreateApplication(context, options));

        // Assert: the message names the setting
        Assert.Contains("Auth:SessionHours", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a re-check interval below the minimum stops the build with an error naming the setting.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_RevalidationSecondsBelowMinimum_ThrowsClearError()
    {
        // Arrange: configuration with a re-check interval of 4 seconds
        using var context = Context.Create(["serve", "--silent"]);
        var options = new ServerHostOptions
        {
            MapStaticAssets = false,
            ConfigureBuilder = builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = TestDatabase.NewConnectionString(),
                    ["Auth:RevalidationSeconds"] = "4"
                });
            }
        };

        // Act: create the application
        var exception = Assert.Throws<InvalidOperationException>(() => ServerHost.CreateApplication(context, options));

        // Assert: the message names the setting
        Assert.Contains("Auth:RevalidationSeconds", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the host registers the user service next to the other domain services.
    /// </summary>
    [Fact]
    public async Task ServerHost_AddServices_Configured_RegistersUserService()
    {
        // Arrange: start the real pipeline on an in-memory server
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");

        // Act: create a user through the registered service
        var user = await app.Services.GetRequiredService<UserService>()
            .CreateAsync("ann", "Ann", UserRole.Reviewer, "correct horse battery", TestContext.Current.CancellationToken);

        // Assert: the user was stored
        Assert.True(user.Id > 0);
    }

    /// <summary>
    ///     Test that the host registers the signing service next to the other domain services.
    /// </summary>
    [Fact]
    public async Task ServerHost_AddServices_Configured_RegistersSigningService()
    {
        // Arrange: start the real pipeline on an in-memory server
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");

        // Act: resolve the service
        var signing = app.Services.GetRequiredService<SigningService>();

        // Assert: it is a singleton and answers for a review that does not exist
        Assert.Same(signing, app.Services.GetRequiredService<SigningService>());
        Assert.Null(await signing.GetApprovalAsync(1, "nope", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the host registers the repository registry and the git source, with the key ring and the git
    ///     cache folder placed beside the database by default.
    /// </summary>
    [Fact]
    public async Task ServerHost_AddServices_Configured_RegistersRepositoryServicesWithDefaultFolders()
    {
        // Arrange: start the real pipeline on a throwaway database
        var connectionString = TestDatabase.NewConnectionString();
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, connectionString, ["serve", "--silent"]);
        var database = new DatabaseOptions { ConnectionString = connectionString };
        var folder = DatabaseSetup.ResolveDataDirectory(database);

        // Act: resolve the services and protect a value
        var service = app.Services.GetRequiredService<RepositoryService>();
        var provider = app.Services.GetRequiredService<IGitRepositoryProvider>();
        var protector = app.Services.GetRequiredService<ICredentialProtector>();
        var cipher = protector.Protect("tkn-Host");

        // Assert: registered as singletons, folders beside the database, the key ring persisted and the token encrypted
        Assert.Same(service, app.Services.GetRequiredService<RepositoryService>());
        Assert.NotNull(provider);
        Assert.Equal(Path.Combine(folder, "git-cache"), app.Services.GetRequiredService<GitOptions>().CachePath);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(folder, "keys"), "key-*.xml"));
        Assert.DoesNotContain("tkn-Host", cipher, StringComparison.Ordinal);
        Assert.Equal("tkn-Host", protector.Unprotect(cipher));
    }

    /// <summary>
    ///     Test that a token encrypted before a restart is readable after it, because the key ring is persisted.
    /// </summary>
    [Fact]
    public async Task ServerHost_Restart_KeyRingPersisted_TokenStaysReadable()
    {
        // Arrange: a database shared by two instances, and a token encrypted by the first
        var connectionString = TestDatabase.NewConnectionString();
        var logs = new CapturingLoggerProvider();
        string cipher;
        await using (var first = await TestWebApp.StartAsync(logs, connectionString, ["serve", "--silent"]))
        {
            cipher = first.Services.GetRequiredService<ICredentialProtector>().Protect("tkn-Restart");
        }

        // Act: start a second instance and read the token
        await using var second = await TestWebApp.StartAsync(logs, connectionString, ["serve", "--silent"]);
        var plain = second.Services.GetRequiredService<ICredentialProtector>().Unprotect(cipher);

        // Assert: readable
        Assert.Equal("tkn-Restart", plain);
    }

    /// <summary>
    ///     Test that a key ring folder that cannot be written stops the start with an error naming the setting,
    ///     instead of silently using keys that would be lost at the next restart.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_KeyRingFolderNotWritable_ThrowsClearError()
    {
        // Arrange: a key ring "folder" that is an existing file
        var file = Path.Combine(Path.GetTempPath(), "ReviewCenterTests-" + Guid.NewGuid().ToString("N") + ".keys");
        File.WriteAllText(file, "not a folder");
        using var context = Context.Create(["serve", "--silent"]);
        var options = new ServerHostOptions
        {
            MapStaticAssets = false,
            ConfigureBuilder = builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = TestDatabase.NewConnectionString(),
                    ["DataProtection:KeysPath"] = file
                });
            }
        };

        try
        {
            // Act: create the application
            var exception = Assert.Throws<InvalidOperationException>(() => ServerHost.CreateApplication(context, options));

            // Assert: the message names the setting and the folder
            Assert.Contains("DataProtection:KeysPath", exception.Message, StringComparison.Ordinal);
            Assert.Contains(file, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    ///     Test that the key ring folder is the configured one, or <c>keys</c> beside the database.
    /// </summary>
    [Fact]
    public void ServerHost_ResolveKeysPath_ConfiguredOrDefault_ChoosesTheFolder()
    {
        // Arrange: a database and a configured folder
        var folder = Path.Combine(Path.GetTempPath(), "rc-keys-test");
        var database = new DatabaseOptions { ConnectionString = Path.Combine(folder, "x.db") };

        // Act: resolve with and without a configured folder
        var configured = ServerHost.ResolveKeysPath(new DataProtectionSettings { KeysPath = Path.Combine(folder, "custom") }, database);
        var fallback = ServerHost.ResolveKeysPath(new DataProtectionSettings(), database);
        var blank = ServerHost.ResolveKeysPath(new DataProtectionSettings { KeysPath = "  " }, database);

        // Assert: configured wins; otherwise keys beside the database
        Assert.Equal(Path.Combine(folder, "custom"), configured);
        Assert.Equal(Path.Combine(folder, "keys"), fallback);
        Assert.Equal(Path.Combine(folder, "keys"), blank);
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
