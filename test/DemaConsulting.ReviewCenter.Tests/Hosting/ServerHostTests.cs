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

using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.ContentSources;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.Identity;
using DemaConsulting.ReviewCenter.SelfTest;
using DemaConsulting.ReviewCenter.SelfTest.Checks;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
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
    ///     Test that a database configured through the <c>Database</c> section is created when the application is
    ///     built: the file and its folder appear.
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
            // Act: start the pipeline against that database
            await using var app = await TestWebApp.StartAsync(logs, $"Data Source={file};Pooling=False", ["serve", "--silent"]);

            // Assert: the database file exists
            Assert.True(File.Exists(file));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that the project service of the host works against the configured database: a project that is
    ///     created through it is stored and read back.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_DatabaseConfigured_ProjectServiceStoresProjects()
    {
        // Arrange: start the pipeline against a throwaway database
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, TestDatabase.NewConnectionString(), ["serve", "--silent"]);

        // Act: create a project through the registered service and read it back
        var project = await TestWebApp.CreateProjectAsync(app, "Apollo");
        var found = await app.Services.GetRequiredService<ProjectService>()
            .GetAsync(TestActors.Ann, project.Id, TestContext.Current.CancellationToken);

        // Assert: the project was stored in the database
        Assert.True(project.Id > 0);
        Assert.Equal("Apollo", found?.Name);
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
            var project = await TestWebApp.CreateProjectAsync(first, "Apollo");
            projectId = project.Id;
        }

        // Act: start a second instance on the same database
        await using var second = await TestWebApp.StartAsync(logs, connectionString, ["serve", "--silent"]);
        var found = await second.Services.GetRequiredService<ProjectService>()
            .GetAsync(TestActors.Ann, projectId, TestContext.Current.CancellationToken);

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
    ///     Test that a sign-in throttle limit that is not positive stops the build with an error naming the setting.
    /// </summary>
    /// <param name="name">The setting under <c>Identity:Throttle</c>.</param>
    /// <param name="value">The invalid value.</param>
    [Theory]
    [InlineData("MaxFailuresPerName", "0")]
    [InlineData("MaxFailuresPerName", "-1")]
    [InlineData("MaxFailuresPerAddress", "0")]
    public void ServerHost_CreateApplication_InvalidThrottleSettings_ThrowsClearError(string name, string value)
    {
        // Arrange and Act: configuration with one limit that is not positive
        var exception = CreateWithInvalidThrottle(new Dictionary<string, string?> { [$"Identity:Throttle:{name}"] = value });

        // Assert: the message is the sign-in settings error and names the setting
        Assert.Contains("Invalid sign-in settings", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"Identity:Throttle:{name}", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a sign-in throttle window outside 1 to 86400 seconds stops the build with an error naming the
    ///     setting.
    /// </summary>
    /// <param name="value">The invalid number of seconds.</param>
    [Theory]
    [InlineData("0")]
    [InlineData("86401")]
    public void ServerHost_CreateApplication_ThrottleWindowOutsideRange_ThrowsClearError(string value)
    {
        // Arrange and Act: configuration with a window outside the allowed range
        var exception = CreateWithInvalidThrottle(new Dictionary<string, string?> { ["Identity:Throttle:WindowSeconds"] = value });

        // Assert: the message is the sign-in settings error and names the setting
        Assert.Contains("Invalid sign-in settings", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Identity:Throttle:WindowSeconds", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a per-address throttle limit below the per-name limit stops the build with an error naming the
    ///     setting.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_ThrottleAddressLimitBelowNameLimit_ThrowsClearError()
    {
        // Arrange and Act: configuration whose address limit is below the name limit
        var exception = CreateWithInvalidThrottle(new Dictionary<string, string?>
        {
            ["Identity:Throttle:MaxFailuresPerAddress"] = "4",
            ["Identity:Throttle:MaxFailuresPerName"] = "5"
        });

        // Assert: the message is the sign-in settings error and names the setting
        Assert.Contains("Invalid sign-in settings", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Identity:Throttle:MaxFailuresPerAddress", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Creates the application with throttle settings that are invalid and returns the error that stops the build.
    /// </summary>
    /// <param name="throttle">The invalid settings under <c>Identity:Throttle</c>, with their full names.</param>
    /// <returns>The error that stopped the build.</returns>
    private static InvalidOperationException CreateWithInvalidThrottle(Dictionary<string, string?> throttle)
    {
        using var context = Context.Create(["serve", "--silent"]);
        var options = new ServerHostOptions
        {
            MapStaticAssets = false,
            ConfigureBuilder = builder =>
            {
                builder.WebHost.UseTestServer();
                throttle["Database:ConnectionString"] = TestDatabase.NewConnectionString();
                builder.Configuration.AddInMemoryCollection(throttle);
            }
        };

        return Assert.Throws<InvalidOperationException>(() => ServerHost.CreateApplication(context, options));
    }

    /// <summary>
    ///     Test that the default and a customized set of throttle settings are accepted.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_ValidThrottleSettings_BuildsTheApplication()
    {
        // Arrange: the defaults, and values that are explicit but valid
        using var context = Context.Create(["serve", "--silent"]);
        ServerHostOptions Options(Dictionary<string, string?> extra) => new()
        {
            MapStaticAssets = false,
            ConfigureBuilder = builder =>
            {
                builder.WebHost.UseTestServer();
                extra["Database:ConnectionString"] = TestDatabase.NewConnectionString();
                builder.Configuration.AddInMemoryCollection(extra);
            }
        };

        // Act / Assert: both build
        using var defaults = ServerHost.CreateApplication(context, Options([]));
        using var custom = ServerHost.CreateApplication(context, Options(new Dictionary<string, string?>
        {
            ["Identity:Throttle:MaxFailuresPerName"] = "3",
            ["Identity:Throttle:MaxFailuresPerAddress"] = "3",
            ["Identity:Throttle:WindowSeconds"] = "86400"
        }));
        Assert.NotNull(defaults);
        Assert.NotNull(custom);
    }

    /// <summary>
    ///     Test that explicit, valid throttle settings (the largest window included) build the application.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_ExplicitValidThrottleSettings_BuildsTheApplication()
    {
        // Arrange: explicit values, with the largest window
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
                    ["Identity:Throttle:MaxFailuresPerName"] = "3",
                    ["Identity:Throttle:MaxFailuresPerAddress"] = "3",
                    ["Identity:Throttle:WindowSeconds"] = "86400"
                });
            }
        };

        // Act
        using var application = ServerHost.CreateApplication(context, options);

        // Assert: the application was built
        Assert.NotNull(application);
    }

    /// <summary>
    ///     Test that invalid settings of sign-in with an OpenID Connect provider stop the build with an error that
    ///     names the setting and never contains the secret.
    /// </summary>
    /// <param name="name">The setting under <c>Identity:Oidc</c>.</param>
    /// <param name="value">The invalid value.</param>
    [Theory]
    [InlineData("Authority", "")]
    [InlineData("Authority", "http://idp.example.com/")]
    [InlineData("Authority", "not an address")]
    [InlineData("Authority", "https://idp.example.com/?x=1")]
    [InlineData("ClientId", "")]
    [InlineData("ClientId", "has space")]
    [InlineData("Scopes", "profile email")]
    [InlineData("DisplayName", "12345678901234567890123456789012345678901234567890X")]
    [InlineData("UsernameClaim", "has space")]
    [InlineData("ReauthMaxAgeSeconds", "5")]
    [InlineData("ReauthMaxAgeSeconds", "901")]
    [InlineData("ClientSecret", "top-secret-bell-\u0007-character")]
    [InlineData("ClientSecret", "")]
    [InlineData("ClientSecret", "   ")]
    public void ServerHost_CreateApplication_InvalidOidcSettings_ThrowsClearErrorWithoutSecret(string name, string value)
    {
        // Arrange: valid enabled settings with one invalid value, and a secret that must stay out of the message
        const string secret = "top-secret-value";
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
                    ["Identity:Oidc:Enabled"] = "true",
                    ["Identity:Oidc:Authority"] = "https://idp.example.com/",
                    ["Identity:Oidc:ClientId"] = "reviewcenter",
                    ["Identity:Oidc:ClientSecret"] = secret,
                    [$"Identity:Oidc:{name}"] = value
                });
            }
        };

        // Act: create the application
        var exception = Assert.Throws<InvalidOperationException>(() => ServerHost.CreateApplication(context, options));

        // Assert: the message is the sign-in settings error, names the setting and has no value
        Assert.Contains("Invalid OpenID Connect sign-in settings", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"Identity:Oidc:{name}", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the provider settings are not checked while sign-in with a provider is off: a garbage
    ///     authority does not stop the build.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_OidcOff_BuildsTheApplication()
    {
        // Arrange: sign-in with a provider is off, and the unused authority is garbage
        using var context = Context.Create(["serve", "--silent"]);
        var options = OidcHostOptions(new Dictionary<string, string?>
        {
            ["Identity:Oidc:Authority"] = "garbage"
        });

        // Act: create the application
        using var application = ServerHost.CreateApplication(context, options);

        // Assert: the application was built
        Assert.NotNull(application);
    }

    /// <summary>
    ///     Test that valid provider settings with an https authority build the application.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_OidcOffOrValid_BuildsTheApplication()
    {
        // Arrange: valid enabled settings with an https authority
        using var context = Context.Create(["serve", "--silent"]);
        var options = OidcHostOptions(new Dictionary<string, string?>
        {
            ["Identity:Oidc:Enabled"] = "true",
            ["Identity:Oidc:Authority"] = "https://idp.example.com/realm",
            ["Identity:Oidc:ClientId"] = "reviewcenter",
            ["Identity:Oidc:ClientSecret"] = "top-secret-value",
            ["Identity:Oidc:ReauthMaxAgeSeconds"] = "900"
        });

        // Act: create the application
        using var application = ServerHost.CreateApplication(context, options);

        // Assert: the application was built
        Assert.NotNull(application);
    }

    /// <summary>
    ///     Gets host options for the OpenID Connect tests: an in-memory server, a throwaway database and the
    ///     given settings.
    /// </summary>
    /// <param name="settings">The OpenID Connect settings to add.</param>
    /// <returns>The host options.</returns>
    private static ServerHostOptions OidcHostOptions(Dictionary<string, string?> settings) => new()
    {
        MapStaticAssets = false,
        ConfigureBuilder = builder =>
        {
            builder.WebHost.UseTestServer();
            settings["Database:ConnectionString"] = TestDatabase.NewConnectionString();
            builder.Configuration.AddInMemoryCollection(settings);
        }
    };

    /// <summary>
    ///     Test that valid settings with http on a loopback host build the application.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_OidcLoopbackHttpAuthority_BuildsTheApplication()
    {
        // Arrange: valid enabled settings whose authority is http on a loopback host
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
                    ["Identity:Oidc:Enabled"] = "true",
                    ["Identity:Oidc:Authority"] = "http://localhost:8080/realm",
                    ["Identity:Oidc:ClientId"] = "reviewcenter",
                    ["Identity:Oidc:ClientSecret"] = "top-secret-value"
                });
            }
        };

        // Act
        using var application = ServerHost.CreateApplication(context, options);

        // Assert: the application was built
        Assert.NotNull(application);
    }

    /// <summary>
    ///     Test that invalid e-mail notification settings stop the build with an error that names the setting and
    ///     never contains the password.
    /// </summary>
    /// <param name="name">The setting under <c>Notifications</c>.</param>
    /// <param name="value">The invalid value.</param>
    [Theory]
    [InlineData("FromAddress", "not an address")]
    [InlineData("BaseUrl", "")]
    [InlineData("Smtp:Host", "")]
    [InlineData("Smtp:Port", "70000")]
    [InlineData("DigestHour", "24")]
    [InlineData("TimeZone", "No/Such_Zone")]
    public void ServerHost_CreateApplication_InvalidNotificationSettings_ThrowsClearErrorWithoutSecret(
        string name,
        string value)
    {
        // Arrange: valid enabled SMTP settings with one invalid value, and a password that must stay out
        using var context = Context.Create(["serve", "--silent"]);
        var settings = ValidNotificationSettings();
        settings[$"Notifications:{name}"] = value;
        var options = new ServerHostOptions
        {
            MapStaticAssets = false,
            ConfigureBuilder = builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(settings);
            }
        };

        // Act: create the application
        var exception = Assert.Throws<InvalidOperationException>(() => ServerHost.CreateApplication(context, options));

        // Assert: the message is the notification settings error, names the setting and has no password
        Assert.Contains("Invalid notification settings", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"Notifications:{name}", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the notification settings are not checked while notifications are off: a garbage sender address
    ///     does not stop the build.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_NotificationsOff_DoesNotCheckTheSettings()
    {
        // Arrange: notifications are not enabled, and an unused setting is garbage
        using var context = Context.Create(["serve", "--silent"]);
        var options = NotificationHostOptions(
            new Dictionary<string, string?> { ["Notifications:FromAddress"] = "garbage" },
            new CapturingLoggerProvider());

        // Act: create the application
        using var application = ServerHost.CreateApplication(context, options);

        // Assert: the application was built
        Assert.NotNull(application);
    }

    /// <summary>
    ///     Test that valid notification settings build the application while notifications are on.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_NotificationsOnAndValid_BuildsTheApplication()
    {
        // Arrange: valid enabled SMTP settings
        using var context = Context.Create(["serve", "--silent"]);
        var options = NotificationHostOptions(ValidNotificationSettings(), new CapturingLoggerProvider());

        // Act: create the application
        using var application = ServerHost.CreateApplication(context, options);

        // Assert: the application was built
        Assert.NotNull(application);
    }

    /// <summary>
    ///     Test that an inline SMTP password makes the start log a warning that names the password file setting.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_NotificationsOffOrValid_BuildsAndWarnsAboutInlinePassword()
    {
        // Arrange: valid enabled SMTP settings with an inline password
        using var context = Context.Create(["serve", "--silent"]);
        var logs = new CapturingLoggerProvider();

        // Act: create the application
        using var application = ServerHost.CreateApplication(context, NotificationHostOptions(ValidNotificationSettings(), logs));

        // Assert: exactly one warning about the notification settings, which names the file setting to use instead
        var warning = Assert.Single(logs.Entries, IsNotificationWarning);
        Assert.Contains("Notifications:Smtp:PasswordFile", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the warning about an inline SMTP password does not contain the password.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_NotificationsInlinePassword_WarningHidesThePassword()
    {
        // Arrange: valid enabled SMTP settings with an inline password
        using var context = Context.Create(["serve", "--silent"]);
        var logs = new CapturingLoggerProvider();

        // Act: create the application
        using var application = ServerHost.CreateApplication(context, NotificationHostOptions(ValidNotificationSettings(), logs));

        // Assert: the warning does not show the password
        var warning = Assert.Single(logs.Entries, IsNotificationWarning);
        Assert.DoesNotContain("top-secret", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Gets host options for the notification tests: an in-memory server, a throwaway database, the given
    ///     settings and the given logger provider.
    /// </summary>
    /// <param name="settings">The notification settings to add.</param>
    /// <param name="logs">The logger provider that captures the log entries.</param>
    /// <returns>The host options.</returns>
    private static ServerHostOptions NotificationHostOptions(Dictionary<string, string?> settings, CapturingLoggerProvider logs) => new()
    {
        MapStaticAssets = false,
        ConfigureBuilder = builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Logging.AddProvider(logs);
            settings["Database:ConnectionString"] = TestDatabase.NewConnectionString();
            builder.Configuration.AddInMemoryCollection(settings);
        }
    };

    /// <summary>
    ///     Gets whether a log entry is the warning about the notification settings.
    /// </summary>
    /// <param name="entry">The log entry.</param>
    /// <returns>True when the entry is that warning.</returns>
    private static bool IsNotificationWarning(CapturedLogEntry entry) =>
        entry.Level == LogLevel.Warning && entry.Message.Contains("Notification settings", StringComparison.Ordinal);

    /// <summary>
    ///     Test that a database created before the feature stops the start while notifications are on.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_DatabaseWithoutNotificationTables_FailsOnlyWhenNotificationsAreOn()
    {
        // Arrange: a database file that already has a table, so the server does not create the model
        using var folder = new TemporaryDirectory();
        var connectionString = CreateOldDatabase(folder);
        using var context = Context.Create(["serve", "--silent"]);

        // Act: notifications on
        var exception = Assert.Throws<InvalidOperationException>(
            () => ServerHost.CreateApplication(context, OldDatabaseOptions(connectionString, ValidNotificationSettings())));

        // Assert: the message tells to recreate the database
        Assert.Contains("no notification tables", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a database created before the feature does not stop the start while notifications are off.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_DatabaseWithoutNotificationTables_StartsWhenNotificationsAreOff()
    {
        // Arrange: a database file that already has a table, so the server does not create the model
        using var folder = new TemporaryDirectory();
        var connectionString = CreateOldDatabase(folder);
        using var context = Context.Create(["serve", "--silent"]);

        // Act: notifications off
        using var application = ServerHost.CreateApplication(context, OldDatabaseOptions(connectionString, []));

        // Assert: the application was built
        Assert.NotNull(application);
    }

    /// <summary>
    ///     Creates a database file with one unrelated table, which is how a database from before the feature looks.
    /// </summary>
    /// <param name="folder">The folder that holds the file.</param>
    /// <returns>The connection string of the database.</returns>
    private static string CreateOldDatabase(TemporaryDirectory folder)
    {
        var connectionString = $"Data Source={folder.GetFilePath("old.db")};Pooling=False";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Old (Id INTEGER)";
        command.ExecuteNonQuery();
        return connectionString;
    }

    /// <summary>
    ///     Gets host options that point the server at an existing database.
    /// </summary>
    /// <param name="connectionString">The connection string of the database.</param>
    /// <param name="settings">The notification settings to add.</param>
    /// <returns>The host options.</returns>
    private static ServerHostOptions OldDatabaseOptions(string connectionString, Dictionary<string, string?> settings) => new()
    {
        MapStaticAssets = false,
        ConfigureBuilder = builder =>
        {
            builder.WebHost.UseTestServer();
            settings["Database:ConnectionString"] = connectionString;
            builder.Configuration.AddInMemoryCollection(settings);
        }
    };

    /// <summary>
    ///     Gets valid enabled SMTP notification settings with an inline password.
    /// </summary>
    /// <returns>A new, modifiable dictionary of settings.</returns>
    private static Dictionary<string, string?> ValidNotificationSettings() => new()
    {
        ["Notifications:Enabled"] = "true",
        ["Notifications:Sender"] = "Smtp",
        ["Notifications:FromAddress"] = "reviewcenter@example.com",
        ["Notifications:BaseUrl"] = "https://reviewcenter.example.com/",
        ["Notifications:Smtp:Host"] = "mail.example.com",
        ["Notifications:Smtp:Port"] = "587",
        ["Notifications:Smtp:Tls"] = "StartTls",
        ["Notifications:Smtp:Username"] = "rc-mailer",
        ["Notifications:Smtp:Password"] = "top-secret-value"
    };

    /// <summary>
    ///     Test that a client secret file that is missing, or that is set together with an inline client secret, stops
    ///     the build with an error that names the settings and never contains the secret.
    /// </summary>
    /// <param name="conflict">Whether both settings are set (otherwise the file does not exist).</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ServerHost_CreateApplication_ClientSecretFileProblem_ThrowsClearErrorWithoutSecret(bool conflict)
    {
        // Arrange: valid enabled settings, a secret file path, and a secret that must stay out of the message
        const string secret = "top-secret-value";
        using var folder = new TemporaryDirectory();
        var path = folder.GetFilePath("client-secret.txt");
        if (conflict)
        {
            File.WriteAllText(path, "secret-in-the-file");
        }

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
                    ["Identity:Oidc:Enabled"] = "true",
                    ["Identity:Oidc:Authority"] = "https://idp.example.com/",
                    ["Identity:Oidc:ClientId"] = "reviewcenter",
                    ["Identity:Oidc:ClientSecret"] = conflict ? secret : string.Empty,
                    ["Identity:Oidc:ClientSecretFile"] = path
                });
            }
        };

        // Act: create the application
        var exception = Assert.Throws<InvalidOperationException>(() => ServerHost.CreateApplication(context, options));

        // Assert: the sign-in settings error names the file setting and holds no secret
        Assert.Contains("Invalid OpenID Connect sign-in settings", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Identity:Oidc:ClientSecretFile", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-in-the-file", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a client secret file that is set together with an inline client secret stops the build with an
    ///     error that names the file setting and never contains either secret.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_ClientSecretFileWithInlineSecret_ThrowsClearErrorWithoutSecret()
    {
        // Arrange: valid enabled settings, an existing secret file, and an inline secret as well
        using var folder = new TemporaryDirectory();
        var path = folder.GetFilePath("client-secret.txt");
        File.WriteAllText(path, "secret-in-the-file");
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
                    ["Identity:Oidc:Enabled"] = "true",
                    ["Identity:Oidc:Authority"] = "https://idp.example.com/",
                    ["Identity:Oidc:ClientId"] = "reviewcenter",
                    ["Identity:Oidc:ClientSecret"] = "top-secret-value",
                    ["Identity:Oidc:ClientSecretFile"] = path
                });
            }
        };

        // Act: create the application
        var exception = Assert.Throws<InvalidOperationException>(() => ServerHost.CreateApplication(context, options));

        // Assert: the sign-in settings error names the file setting and holds neither secret
        Assert.Contains("Invalid OpenID Connect sign-in settings", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Identity:Oidc:ClientSecretFile", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-in-the-file", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an attestation provider that does not exist stops the build with an error naming the setting.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_UnknownAttestationProvider_ThrowsClearError()
    {
        // Arrange: configuration that selects a provider the product does not have
        using var context = Context.Create(["serve", "--silent"]);
        var options = AttestationHostOptions(("Attestation:Provider", "Pfx"));

        // Act: create the application
        var exception = Assert.Throws<InvalidOperationException>(() => ServerHost.CreateApplication(context, options));

        // Assert: the message names the setting
        Assert.Contains("The attestation signing key is not usable", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Attestation:Provider", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the certificate store provider without a thumbprint stops the build with an error naming the setting.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_CertificateStoreWithoutThumbprint_ThrowsClearError()
    {
        // Arrange: configuration that selects the certificate store and no certificate
        using var context = Context.Create(["serve", "--silent"]);
        var options = AttestationHostOptions(("Attestation:Provider", "CertificateStore"));

        // Act: create the application
        var exception = Assert.Throws<InvalidOperationException>(() => ServerHost.CreateApplication(context, options));

        // Assert: the message names the setting
        Assert.Contains("Attestation:CertificateThumbprint", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a certificate that is not in the store stops the build with an error naming the thumbprint.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_CertificateNotInStore_ThrowsClearError()
    {
        // Arrange: configuration that names a certificate that does not exist
        var thumbprint = new string('B', 40);
        using var context = Context.Create(["serve", "--silent"]);
        var options = AttestationHostOptions(
            ("Attestation:Provider", "CertificateStore"),
            ("Attestation:CertificateThumbprint", thumbprint));

        // Act: create the application
        var exception = Assert.Throws<InvalidOperationException>(() => ServerHost.CreateApplication(context, options));

        // Assert: the thumbprint is named
        Assert.Contains(thumbprint, exception.Message, StringComparison.Ordinal);
        Assert.Contains("was not found", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a trusted key that is required stops the build while the key is the self-signed default.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_RequireTrustedKeyWithSelfSignedProvider_ThrowsClearError()
    {
        // Arrange: configuration that requires a trusted key and keeps the self-signed provider
        using var context = Context.Create(["serve", "--silent"]);
        var options = AttestationHostOptions(("Attestation:RequireTrustedKey", "true"));

        // Act: create the application
        var exception = Assert.Throws<InvalidOperationException>(() => ServerHost.CreateApplication(context, options));

        // Assert: the message names the setting and the reason
        Assert.Contains("Attestation:RequireTrustedKey", exception.Message, StringComparison.Ordinal);
        Assert.Contains("self-signed", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a folder for the self-signed key that cannot be used stops the build with an error naming the setting.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_SelfSignedFolderIsAFile_ThrowsClearError()
    {
        // Arrange: a file where the folder of the key should be
        var file = Path.Combine(Path.GetTempPath(), "ReviewCenterTests-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "not a folder");
        using var context = Context.Create(["serve", "--silent"]);
        var options = AttestationHostOptions(("Attestation:SelfSignedFolder", file));

        try
        {
            // Act: create the application
            var exception = Assert.Throws<InvalidOperationException>(() => ServerHost.CreateApplication(context, options));

            // Assert: the message names the setting
            Assert.Contains("Attestation:SelfSignedFolder", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    ///     Test that without any attestation setting the server creates its self-signed key beside the data
    ///     protection keys.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_NoAttestationSettings_CreatesSelfSignedKeyBesideTheDataProtectionKeys()
    {
        // Arrange: a database in a folder of its own, so that the key folder is known
        var folder = Path.Combine(Path.GetTempPath(), "ReviewCenterTests-" + Guid.NewGuid().ToString("N"));
        var connectionString = $"Data Source={Path.Combine(folder, "rc.db")};Pooling=False";
        var expected = Path.Combine(folder, "keys", "attestation-selfsigned", SelfSignedKeyProvider.FileName);
        var logs = new CapturingLoggerProvider();

        try
        {
            // Act: start once on that folder
            await using var app = await TestWebApp.StartAsync(logs, connectionString, ["serve", "--silent"]);

            // Assert: the key file is where it should be
            Assert.True(File.Exists(expected));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that the self-signed key is the same after a restart on the same folder.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_NoAttestationSettings_SelfSignedKeyPersistsAcrossRestart()
    {
        // Arrange: a database in a folder of its own, so that the key folder is known
        var folder = Path.Combine(Path.GetTempPath(), "ReviewCenterTests-" + Guid.NewGuid().ToString("N"));
        var connectionString = $"Data Source={Path.Combine(folder, "rc.db")};Pooling=False";
        var logs = new CapturingLoggerProvider();

        try
        {
            // Act: start twice on the same folder
            string first;
            await using (var app = await TestWebApp.StartAsync(logs, connectionString, ["serve", "--silent"]))
            {
                first = app.Services.GetRequiredService<IAttestationKeyProvider>().GetSigningKey().Info.Thumbprint;
            }

            await using var again = await TestWebApp.StartAsync(logs, connectionString, ["serve", "--silent"]);
            var second = again.Services.GetRequiredService<IAttestationKeyProvider>().GetSigningKey().Info.Thumbprint;

            // Assert: the key is the same after the restart
            Assert.Equal(first, second);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that the folder of the self-signed key can be chosen with the settings.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_SelfSignedFolderConfigured_KeepsTheKeyThere()
    {
        // Arrange: a folder chosen for the key
        var folder = Path.Combine(Path.GetTempPath(), "ReviewCenterTests-" + Guid.NewGuid().ToString("N"));
        using var context = Context.Create(["serve", "--silent"]);
        var options = AttestationHostOptions(("Attestation:SelfSignedFolder", folder));

        try
        {
            // Act: create the application, which loads the key
            await using var app = ServerHost.CreateApplication(context, options);

            // Assert: the key is in that folder
            Assert.True(File.Exists(Path.Combine(folder, SelfSignedKeyProvider.FileName)));
            Assert.True(app.Services.GetRequiredService<IAttestationKeyProvider>().Describe().IsAvailable);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a git timeout outside 1 to 3600 seconds stops the build with an error naming the setting.
    /// </summary>
    /// <param name="seconds">The configured timeout.</param>
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("3601")]
    public void ServerHost_CreateApplication_GitTimeoutOutOfRange_ThrowsClearError(string seconds)
    {
        // Arrange: configuration with an out-of-range git timeout
        using var context = Context.Create(["serve", "--silent"]);
        var options = GitTimeoutOptions(seconds);

        // Act: create the application
        var exception = Assert.Throws<InvalidOperationException>(() => ServerHost.CreateApplication(context, options));

        // Assert: the message names the setting
        Assert.Contains("Git:TimeoutSeconds", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the smallest and largest accepted git timeouts let the application build.
    /// </summary>
    /// <param name="seconds">The configured timeout.</param>
    [Theory]
    [InlineData("1")]
    [InlineData("3600")]
    public void ServerHost_CreateApplication_GitTimeoutAtLimits_Starts(string seconds)
    {
        // Arrange: configuration with a git timeout on a limit
        using var context = Context.Create(["serve", "--silent"]);
        var options = GitTimeoutOptions(seconds);

        // Act: create the application
        using var app = ServerHost.CreateApplication(context, options);

        // Assert: the configured value is in effect
        var git = app.Services.GetRequiredService<GitOptions>();
        Assert.Equal(int.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture), git.TimeoutSeconds);
    }

    /// <summary>
    ///     Builds host options for an in-memory server with a git timeout.
    /// </summary>
    /// <param name="seconds">The configured timeout, as text.</param>
    /// <returns>The host options.</returns>
    private static ServerHostOptions GitTimeoutOptions(string seconds)
    {
        return new ServerHostOptions
        {
            MapStaticAssets = false,
            ConfigureBuilder = builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = TestDatabase.NewConnectionString(),
                    ["Git:TimeoutSeconds"] = seconds
                });
            }
        };
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
    ///     Test that the host registers the review service next to the other domain services and connects it to the
    ///     configured database.
    /// </summary>
    [Fact]
    public async Task ServerHost_AddServices_Configured_RegistersReviewService()
    {
        // Arrange: start the real pipeline on an in-memory server and store a project in its database
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        var project = await TestWebApp.CreateProjectAsync(app, "Apollo");

        // Act: resolve the service and list the reviews of the project
        var reviews = await app.Services.GetRequiredService<ReviewService>()
            .ListAsync(project.Id, TestContext.Current.CancellationToken);

        // Assert: the service answers from the database with no reviews for the new project
        Assert.Empty(reviews);
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
    ///     Test that the host registers the inbox store and the inbox service as singletons.
    /// </summary>
    [Fact]
    public async Task ServerHost_AddServices_Configured_RegistersInboxService()
    {
        // Arrange: start the real pipeline on an in-memory server
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");

        // Act: resolve both
        var inbox = app.Services.GetRequiredService<InboxService>();
        var store = app.Services.GetRequiredService<IInboxStore>();

        // Assert: singletons, and the service answers for a user who is not known
        Assert.Same(inbox, app.Services.GetRequiredService<InboxService>());
        Assert.Same(store, app.Services.GetRequiredService<IInboxStore>());
        Assert.Equal(0, await inbox.CountAsync(new Actor("nobody", "Nobody"), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the host registers the project access rules as one shared instance.
    /// </summary>
    [Fact]
    public async Task ServerHost_AddServices_Configured_RegistersProjectAccessAsSingleton()
    {
        // Arrange: start the real pipeline on an in-memory server
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");

        // Act: resolve the rules twice
        var first = app.Services.GetRequiredService<IProjectAccess>();
        var second = app.Services.GetRequiredService<IProjectAccess>();

        // Assert: one shared instance
        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    /// <summary>
    ///     Test that the host registers the audit appender, the audit store and the audit service as one shared
    ///     instance each.
    /// </summary>
    [Fact]
    public async Task ServerHost_AddServices_Configured_RegistersAuditServicesAsSingletons()
    {
        // Arrange: start the real pipeline on an in-memory server
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");

        // Act: resolve each of the three twice
        var appender = app.Services.GetRequiredService<AuditAppender>();
        var store = app.Services.GetRequiredService<IAuditStore>();
        var audit = app.Services.GetRequiredService<AuditService>();

        // Assert: every resolution returns the same instance
        Assert.Same(appender, app.Services.GetRequiredService<AuditAppender>());
        Assert.Same(store, app.Services.GetRequiredService<IAuditStore>());
        Assert.Same(audit, app.Services.GetRequiredService<AuditService>());
    }

    /// <summary>
    ///     Test that an audit entry that cannot be stored makes the host log a warning about the entry.
    /// </summary>
    [Fact]
    public async Task ServerHost_AddServices_AuditTrailUnwritable_LogsWarning()
    {
        // Arrange: an application whose audit store refuses every appended entry
        var logs = new CapturingLoggerProvider();
        await using var app = await StartWithFailingAuditStoreAsync(logs);

        // Act: record an entry through the registered audit service
        await app.Services.GetRequiredService<AuditService>()
            .TryRecordAsync(AuditDraft.By(new Actor("ann", "Ann"), "test.action", "detail"), TestContext.Current.CancellationToken);

        // Assert: the host logged a warning about the entry
        Assert.Contains(
            logs.Entries,
            e => e.Category == typeof(ServerHost).FullName
                 && e.Level == LogLevel.Warning
                 && e.Message.Contains("An audit entry could not be recorded", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that an audit entry that cannot be stored does not fail the caller: nothing is thrown and the result is
    ///     false.
    /// </summary>
    [Fact]
    public async Task ServerHost_AddServices_AuditTrailUnwritable_ReturnsFalseWithoutThrowing()
    {
        // Arrange: an application whose audit store refuses every appended entry
        await using var app = await StartWithFailingAuditStoreAsync(new CapturingLoggerProvider());

        // Act: record an entry through the registered audit service
        var recorded = await app.Services.GetRequiredService<AuditService>()
            .TryRecordAsync(AuditDraft.By(new Actor("ann", "Ann"), "test.action", "detail"), TestContext.Current.CancellationToken);

        // Assert: nothing was thrown, and the result is false
        Assert.False(recorded);
    }

    /// <summary>
    ///     Starts an application whose audit store refuses every appended entry.
    /// </summary>
    /// <param name="logs">Receives the log entries.</param>
    /// <returns>The started application; the caller disposes it.</returns>
    private static Task<WebApplication> StartWithFailingAuditStoreAsync(CapturingLoggerProvider logs)
    {
        return TestWebApp.StartAsync(
            logs,
            null,
            ["serve", "--silent"],
            null,
            services =>
            {
                services.AddSingleton<AuditStore>();
                services.AddSingleton<IAuditStore>(provider =>
                {
                    var proxy = DispatchProxy.Create<IAuditStore, FailingAppendProxy>();
                    ((FailingAppendProxy)(object)proxy).Inner = provider.GetRequiredService<AuditStore>();
                    return proxy;
                });
            });
    }

    /// <summary>
    ///     Test that registering and resolving the services creates no key file and no key folder: the key ring is
    ///     touched only when the application starts or a secret is protected.
    /// </summary>
    [Fact]
    public void ServerHost_AddServices_ServicesOnly_CreatesNoKeyFile()
    {
        // Arrange: configuration that points the key ring at a folder that does not exist
        using var folder = new TemporaryDirectory();
        var keys = Path.Combine(folder.DirectoryPath, "keys");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = TestDatabase.NewConnectionString(),
                ["DataProtection:KeysPath"] = keys,
                ["DataProtection:Protection"] = "PermissionsOnly"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        // Act: register the services and resolve two of them, without the key ring
        ServerHost.AddServices(services, configuration);
        using var provider = services.BuildServiceProvider();
        var users = provider.GetRequiredService<UserService>();
        var audit = provider.GetRequiredService<AuditService>();

        // Assert: the services exist, and neither the folder nor a key was created
        Assert.NotNull(users);
        Assert.NotNull(audit);
        Assert.False(Directory.Exists(keys), "registering the services must not create the key ring");
    }

    /// <summary>
    ///     Test that the host registers the repository service as one shared instance.
    /// </summary>
    [Fact]
    public async Task ServerHost_AddServices_Configured_RegistersRepositoryServiceAsSingleton()
    {
        // Arrange: start the real pipeline on a throwaway database
        var (started, _) = await StartWithDefaultFoldersAsync();
        await using var app = started;

        // Act: resolve the service twice
        var service = app.Services.GetRequiredService<RepositoryService>();

        // Assert: one shared instance
        Assert.Same(service, app.Services.GetRequiredService<RepositoryService>());
    }

    /// <summary>
    ///     Test that the host registers the git content source for the repository service.
    /// </summary>
    [Fact]
    public async Task ServerHost_AddServices_Configured_RegistersGitSource()
    {
        // Arrange: start the real pipeline on a throwaway database
        var (started, _) = await StartWithDefaultFoldersAsync();
        await using var app = started;

        // Act: resolve the git content source
        var provider = app.Services.GetRequiredService<IGitRepositoryProvider>();

        // Assert: it is registered
        Assert.NotNull(provider);
    }

    /// <summary>
    ///     Test that without <c>Git:CachePath</c> the git cache folder is <c>git-cache</c> beside the database.
    /// </summary>
    [Fact]
    public async Task ServerHost_AddServices_Configured_RegistersRepositoryServicesWithDefaultFolders()
    {
        // Arrange: start the real pipeline on a throwaway database
        var (started, folder) = await StartWithDefaultFoldersAsync();
        await using var app = started;

        // Act: read the git options that the host registered
        var cachePath = app.Services.GetRequiredService<GitOptions>().CachePath;

        // Assert: the cache folder is beside the database
        Assert.Equal(Path.Combine(folder, "git-cache"), cachePath);
    }

    /// <summary>
    ///     Test that without <c>DataProtection:KeysPath</c> the key ring is persisted in a <c>keys</c> folder beside
    ///     the database.
    /// </summary>
    [Fact]
    public async Task ServerHost_AddServices_Configured_PlacesTheKeyRingBesideTheDatabase()
    {
        // Arrange: start the real pipeline on a throwaway database
        var (started, folder) = await StartWithDefaultFoldersAsync();
        await using var app = started;

        // Act: protect a value, which makes the key ring write its key
        app.Services.GetRequiredService<ICredentialProtector>().Protect("tkn-Host");

        // Assert: the key files are in the keys folder beside the database
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(folder, "keys"), "key-*.xml"));
    }

    /// <summary>
    ///     Test that the credential protector of the host encrypts a repository token and decrypts it again.
    /// </summary>
    [Fact]
    public async Task ServerHost_AddServices_Configured_CredentialProtectorRoundTripsAToken()
    {
        // Arrange: start the real pipeline on a throwaway database
        var (started, _) = await StartWithDefaultFoldersAsync();
        await using var app = started;
        var protector = app.Services.GetRequiredService<ICredentialProtector>();

        // Act: protect a token and unprotect the result
        var cipher = protector.Protect("tkn-Host");

        // Assert: the encrypted text does not contain the token, and decrypts back to it
        Assert.DoesNotContain("tkn-Host", cipher, StringComparison.Ordinal);
        Assert.Equal("tkn-Host", protector.Unprotect(cipher));
    }

    /// <summary>
    ///     Starts the real pipeline on a throwaway database, with no setting for the git cache or the key ring.
    /// </summary>
    /// <returns>The application and the folder that holds the database.</returns>
    private static async Task<(WebApplication App, string Folder)> StartWithDefaultFoldersAsync()
    {
        var connectionString = TestDatabase.NewConnectionString();
        var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), connectionString, ["serve", "--silent"]);
        return (app, DatabaseSetup.ResolveDataDirectory(new DatabaseOptions { ConnectionString = connectionString }));
    }

    /// <summary>
    ///     Test that the host registers the review planner as one shared instance.
    /// </summary>
    [Fact]
    public async Task ServerHost_AddServices_Configured_RegistersReviewPlannerAndParser()
    {
        // Arrange: start the real pipeline on a throwaway database
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, TestDatabase.NewConnectionString(), ["serve", "--silent"]);

        // Act: resolve the planner
        var planner = app.Services.GetRequiredService<ReviewPlanner>();

        // Assert: it exists and is a singleton
        Assert.Same(planner, app.Services.GetRequiredService<ReviewPlanner>());
    }

    /// <summary>
    ///     Test that the host registers the parser of the review-set file as one shared instance.
    /// </summary>
    [Fact]
    public async Task ServerHost_AddServices_Configured_RegistersReviewSetParserAsSingleton()
    {
        // Arrange: start the real pipeline on a throwaway database
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, TestDatabase.NewConnectionString(), ["serve", "--silent"]);

        // Act: resolve the parser
        var parser = app.Services.GetRequiredService<IReviewSetParser>();

        // Assert: it exists and is a singleton
        Assert.Same(parser, app.Services.GetRequiredService<IReviewSetParser>());
    }

    /// <summary>
    ///     Test that the host registers the personal access token service as one shared instance.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_ApiRegistration_RegistersTokenServiceAsSingleton()
    {
        // Arrange: start the real pipeline on an in-memory server
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");

        // Act: resolve the token service
        var service = app.Services.GetRequiredService<PersonalAccessTokenService>();

        // Assert: one shared instance
        Assert.Same(service, app.Services.GetRequiredService<PersonalAccessTokenService>());
    }

    /// <summary>
    ///     Test that the API routes are mapped under <c>/api</c>: a protected route is found, and answers the caller
    ///     instead of falling through to a web page.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_ApiRoutes_AreMapped()
    {
        // Arrange: start the real pipeline on an in-memory server
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request a route of the API without a token
        var response = await client.GetAsync("/api/projects", TestContext.Current.CancellationToken);

        // Assert: the route exists (any answer but 404) and its content is not a web page
        Assert.NotEqual(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    ///     Test that a request without a token to a protected API route is answered with status 401.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_ApiRoutes_WithoutTokenAreUnauthorized()
    {
        // Arrange: start the real pipeline on an in-memory server
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request a protected route without a token
        var response = await client.GetAsync("/api/projects", TestContext.Current.CancellationToken);

        // Assert: 401
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    ///     Test that the OpenAPI document is served at <c>/api/openapi.json</c> without a token.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_OpenApiDocument_IsServedWithoutToken()
    {
        // Arrange: start the real pipeline on an in-memory server
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request the document without a token
        var response = await client.GetAsync("/api/openapi.json", TestContext.Current.CancellationToken);

        // Assert: 200
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    ///     Test that an address under <c>/api</c> that does not exist is answered with a JSON 404 and does not fall
    ///     through to the web pages.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_UnknownApiAddress_ReturnsJsonProblem404()
    {
        // Arrange: start the real pipeline on an in-memory server
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request an address that does not exist
        var response = await client.GetAsync("/api/nothing", TestContext.Current.CancellationToken);

        // Assert: 404 with a JSON problem document
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
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
    ///     Test that the key ring folder is <c>keys</c> beside the database when no folder is configured or the
    ///     configured one is blank.
    /// </summary>
    [Fact]
    public void ServerHost_ResolveKeysPath_ConfiguredOrDefault_ChoosesTheFolder()
    {
        // Arrange: a database
        var folder = Path.Combine(Path.GetTempPath(), "rc-keys-test");
        var database = new DatabaseOptions { ConnectionString = Path.Combine(folder, "x.db") };

        // Act: resolve with no configured folder and with a blank one
        var fallback = ServerHost.ResolveKeysPath(new KeyRingSettings(), database);
        var blank = ServerHost.ResolveKeysPath(new KeyRingSettings { KeysPath = "  " }, database);

        // Assert: keys beside the database in both cases
        Assert.Equal(Path.Combine(folder, "keys"), fallback);
        Assert.Equal(Path.Combine(folder, "keys"), blank);
    }

    /// <summary>
    ///     Test that the key ring folder is the configured one when <c>DataProtection:KeysPath</c> is set.
    /// </summary>
    [Fact]
    public void ServerHost_ResolveKeysPath_Configured_ChoosesTheConfiguredFolder()
    {
        // Arrange: a database and a configured folder
        var folder = Path.Combine(Path.GetTempPath(), "rc-keys-test");
        var database = new DatabaseOptions { ConnectionString = Path.Combine(folder, "x.db") };

        // Act: resolve with a configured folder
        var configured = ServerHost.ResolveKeysPath(new KeyRingSettings { KeysPath = Path.Combine(folder, "custom") }, database);

        // Assert: the configured folder wins
        Assert.Equal(Path.Combine(folder, "custom"), configured);
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
        var exitCode = await ServerHost.RunAsync(context, UnsupportedProviderOptions(), TestContext.Current.CancellationToken);

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
            var exitCode = await ServerHost.RunAsync(context, options, TestContext.Current.CancellationToken);

            // Assert: exit code 1 rather than an unhandled exception
            Assert.Equal(1, exitCode);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that an anonymous visitor who asks for a protected page is sent to the login page, which remembers
    ///     the page that was asked for.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_AnonymousVisitor_ProtectedPageRedirectsToLogin()
    {
        // Arrange: the real pipeline and a visitor who is not signed in
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        using var browser = new TestBrowser(app);

        // Act: request a protected page
        var response = await browser.Client.GetAsync("/projects", TestContext.Current.CancellationToken);

        // Assert: redirected to the login page with the requested page as the return address
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString();
        Assert.Contains("/login?ReturnUrl=", location, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("/projects"), location, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a visitor who is signed in is served a page that needs a signed-in user.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_SignedInUser_ProtectedPageIsServed()
    {
        // Arrange: the real pipeline and a visitor who has signed in
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        using var browser = await TestBrowser.SignedInAsync(app, "ann");

        // Act: request a protected page
        var response = await browser.Client.GetAsync("/projects", TestContext.Current.CancellationToken);

        // Assert: the page is served, not redirected to the login page
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    /// <summary>
    ///     Test that the health route is answered to a visitor who is not signed in, without a redirect to the
    ///     login page.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_HealthRoute_NotSignedIn_IsNotRedirectedToLogin()
    {
        // Arrange: the real pipeline and a visitor who is not signed in
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        using var browser = new TestBrowser(app);

        // Act: request the health route
        var response = await browser.Client.GetAsync(ServerHost.HealthPath, TestContext.Current.CancellationToken);

        // Assert: answered directly, not redirected to the login page
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    /// <summary>
    ///     Test that the home page and the login page are open to an anonymous visitor.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_AnonymousVisitor_HomeAndLoginPagesAreServed()
    {
        // Arrange: the real pipeline and a visitor who is not signed in
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        using var browser = new TestBrowser(app);

        // Act: request both pages
        var home = await browser.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var login = await browser.Client.GetAsync("/login", TestContext.Current.CancellationToken);

        // Assert: both are served
        Assert.Equal(System.Net.HttpStatusCode.OK, home.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, login.StatusCode);
    }

    /// <summary>
    ///     Test that the git timeout is the default of 120 seconds when no setting is given.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_NoGitSettings_UsesDefaultTimeout()
    {
        // Arrange: the real pipeline with no git settings
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");

        // Act: read the git options in effect
        var git = app.Services.GetRequiredService<GitOptions>();

        // Assert: the default timeout
        Assert.Equal(GitOptions.DefaultTimeoutSeconds, git.TimeoutSeconds);
        Assert.Equal(120, git.TimeoutSeconds);
    }

    /// <summary>
    ///     Test that the text of a start-up failure reaches the context, which writes it to the log file.
    /// </summary>
    [Fact]
    public async Task ServerHost_RunAsync_UnsupportedProvider_ReportsTheError()
    {
        // Arrange: a log file and configuration that selects an unsupported provider
        using var folder = new TemporaryDirectory();
        var logFile = Path.Combine(folder.DirectoryPath, "run.log");
        int exitCode;
        using (var context = Context.Create(["serve", "--silent", "--log", logFile]))
        {
            // Act: run the server
            exitCode = await ServerHost.RunAsync(context, UnsupportedProviderOptions(), TestContext.Current.CancellationToken);
        }

        // Assert: exit code 1, and the error text names the failure and the provider
        Assert.Equal(1, exitCode);
        var text = await File.ReadAllTextAsync(logFile, TestContext.Current.CancellationToken);
        Assert.Contains("Failed to prepare the database", text, StringComparison.Ordinal);
        Assert.Contains("Oracle", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the endpoint that receives the one-time code is mapped while the second factor is on.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_TotpOn_CodeEndpointIsMapped()
    {
        // Arrange: the real pipeline with the second factor on (the default)
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");

        // Act: look for the endpoint among the mapped routes
        var mapped = IsMapped(app, TotpSignIn.CodeEndpoint);

        // Assert: it is mapped
        Assert.True(mapped);
    }

    /// <summary>
    ///     Test that the endpoint that receives the one-time code is not mapped while the second factor is off.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_TotpOff_CodeEndpointIsNotMapped()
    {
        // Arrange: the real pipeline with the second factor switched off
        var settings = new Dictionary<string, string?> { ["Identity:Totp:Enabled"] = "false" };
        await using var app = await TestWebApp.StartAsync(
            new CapturingLoggerProvider(),
            null,
            ["serve", "--silent"],
            settings);

        // Act: look for the endpoint among the mapped routes
        var mapped = IsMapped(app, TotpSignIn.CodeEndpoint);

        // Assert: it is not mapped
        Assert.False(mapped);
    }

    /// <summary>
    ///     Tells whether a route pattern is mapped in an application.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="pattern">The route pattern, for example <c>/auth/login-totp</c>.</param>
    /// <returns><c>true</c> when an endpoint with that pattern is mapped.</returns>
    private static bool IsMapped(WebApplication app, string pattern)
    {
        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Any(endpoint => string.Equals(endpoint.RoutePattern.RawText, pattern, StringComparison.Ordinal));
    }

    /// <summary>
    ///     A proxy of the audit store that refuses every appended entry and passes everything else on.
    /// </summary>
    private class FailingAppendProxy : DispatchProxy
    {
        /// <summary>
        ///     Gets or sets the store that receives all other calls.
        /// </summary>
        public IAuditStore Inner { get; set; } = null!;

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IAuditStore.AppendAsync))
            {
                return Task.FromException<AuditEntry>(new InvalidOperationException("The audit trail is unavailable."));
            }

            try
            {
                return targetMethod.Invoke(Inner, args);
            }
            catch (TargetInvocationException exception)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException!).Throw();
                throw;
            }
        }
    }

    /// <summary>
    ///     Creates host options with attestation settings and a throwaway database.
    /// </summary>
    /// <param name="settings">The settings as configuration keys and values.</param>
    /// <returns>The options.</returns>
    private static ServerHostOptions AttestationHostOptions(params (string Key, string Value)[] settings)
    {
        return new ServerHostOptions
        {
            MapStaticAssets = false,
            ConfigureBuilder = builder =>
            {
                builder.WebHost.UseTestServer();
                var values = new Dictionary<string, string?> { ["Database:ConnectionString"] = TestDatabase.NewConnectionString() };
                foreach (var (key, value) in settings)
                {
                    values[key] = value;
                }

                builder.Configuration.AddInMemoryCollection(values);
            }
        };
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

/// <summary>
///     Tests of the fully isolated host (<see cref="ServerHostOptions.IsolatedSettings"/>): it must ignore every
///     ambient configuration source. They change process environment variables, so they run exclusively.
/// </summary>
[Collection("Sequential")]
public class ServerHostIsolationTests
{
    /// <summary>
    ///     Sets environment variables and restores the previous values when disposed.
    /// </summary>
    private sealed class PoisonedEnvironment : IDisposable
    {
        /// <summary>
        ///     The values the variables had before.
        /// </summary>
        private readonly Dictionary<string, string?> _previous = [];

        /// <summary>
        ///     Initializes a new instance of the <see cref="PoisonedEnvironment"/> class and sets the variables.
        /// </summary>
        /// <param name="poisonedPort">The port the poisoned listen addresses name.</param>
        /// <param name="sentinelFolder">A folder that must stay empty; the poisoned paths point into it.</param>
        public PoisonedEnvironment(int poisonedPort, string sentinelFolder)
        {
            Set("ASPNETCORE_URLS", $"http://127.0.0.1:{poisonedPort}");
            Set("ASPNETCORE_HTTP_PORTS", poisonedPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Set("ASPNETCORE_HTTPS_PORTS", null);
            Set("ASPNETCORE_Kestrel__Endpoints__Default__Url", $"http://127.0.0.1:{poisonedPort}");
            Set("Kestrel__Endpoints__Default__Url", $"http://127.0.0.1:{poisonedPort}");
            Set("DOTNET_URLS", $"http://127.0.0.1:{poisonedPort}");
            Set("ASPNETCORE_ENVIRONMENT", "Development");
            Set("DOTNET_ENVIRONMENT", "Development");
            Set("Database__Provider", "Oracle");
            Set("DataProtection__KeysPath", Path.Combine(sentinelFolder, "keys"));
            Set("Git__CachePath", Path.Combine(sentinelFolder, "git"));
            Set(TestDatabase.ConnectionStringVariable, $"Data Source={Path.Combine(sentinelFolder, "ambient.db")};Pooling=False");
        }

        /// <summary>
        ///     Sets one variable and remembers its previous value.
        /// </summary>
        /// <param name="name">The variable name.</param>
        /// <param name="value">The new value, or <c>null</c> to remove the variable.</param>
        private void Set(string name, string? value)
        {
            _previous.TryAdd(name, Environment.GetEnvironmentVariable(name));
            Environment.SetEnvironmentVariable(name, value);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (var (name, value) in _previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    /// <summary>
    ///     Finds a loopback port that is free right now.
    /// </summary>
    /// <returns>The port number.</returns>
    private static int FindFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    ///     Creates the options of the isolated host over a temporary folder.
    /// </summary>
    /// <param name="folder">The temporary folder.</param>
    /// <returns>The options.</returns>
    private static ServerHostOptions IsolatedOptions(TemporaryDirectory folder)
    {
        return new ServerHostOptions { IsolatedSettings = SelfTestServices.CreateSettings(folder) };
    }

    /// <summary>
    ///     Starts the isolated host in a poisoned process environment, lets the caller inspect it, and stops it.
    /// </summary>
    /// <param name="inspect">
    ///     The assertions, given the started host, the poisoned port, the temporary folder of the host and the
    ///     sentinel folder that must stay empty.
    /// </param>
    /// <returns>A task that completes when the host has been stopped.</returns>
    private static async Task RunPoisonedIsolatedHostAsync(
        Action<WebApplication, int, TemporaryDirectory, TemporaryDirectory> inspect)
    {
        // Arrange: a poisoned process environment and a sentinel folder that must stay untouched
        using var folder = new TemporaryDirectory();
        using var sentinel = new TemporaryDirectory();
        var poisonedPort = FindFreePort();
        using var poison = new PoisonedEnvironment(poisonedPort, sentinel.DirectoryPath);
        using var context = Context.Create(["serve", "--silent", "--urls", "http://127.0.0.1:0"]);

        // Act: create and start the isolated host
        var app = ServerHost.CreateApplication(context, IsolatedOptions(folder));
        try
        {
            await app.StartAsync(TestContext.Current.CancellationToken);
            inspect(app, poisonedPort, folder, sentinel);
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    /// <summary>
    ///     Test that the isolated host listens only on the address of the context and not on the ambient listen
    ///     address of a poisoned environment.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_IsolatedSettings_IgnoresPoisonedEnvironment()
    {
        // Arrange, Act: start the isolated host in a poisoned environment and check where it listens
        await RunPoisonedIsolatedHostAsync((app, poisonedPort, _, _) =>
        {
            // Assert: one loopback address that is not the poisoned one
            var address = HostStartCheck.ExpectOneLoopbackAddress(app.Urls);
            Assert.NotEqual(poisonedPort, address.Port);
        });
    }

    /// <summary>
    ///     Test that the isolated host runs in the Production environment although the ambient environment variables
    ///     say Development.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_IsolatedSettings_RunsInProductionEnvironment()
    {
        // Arrange, Act: start the isolated host in a poisoned environment
        await RunPoisonedIsolatedHostAsync((app, _, _, _) =>
        {
            // Assert: the environment is Production
            Assert.Equal("Production", app.Environment.EnvironmentName);
        });
    }

    /// <summary>
    ///     Test that the isolated host keeps its database in its temporary folder and leaves the places that the
    ///     ambient settings name (database, key ring and git cache) untouched.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_IsolatedSettings_LeavesAmbientDataPlacesUntouched()
    {
        // Arrange, Act: start the isolated host in a poisoned environment whose paths point into a sentinel folder
        await RunPoisonedIsolatedHostAsync((_, _, folder, sentinel) =>
        {
            // Assert: the database is in the temporary folder, and nothing was written to the sentinel folder
            Assert.True(File.Exists(Path.Combine(folder.DirectoryPath, "selftest.db")));
            Assert.Empty(Directory.GetFileSystemEntries(sentinel.DirectoryPath));
        });
    }

    /// <summary>
    ///     Test that the poison used above is effective: the normal host, which reads the environment, listens on the
    ///     poisoned address. Without this the test above could pass because the poison does nothing.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_WithoutIsolation_AmbientEndpointWins()
    {
        // Arrange: the same poisoned environment, and a normal host that only gets a temporary database
        using var folder = new TemporaryDirectory();
        using var sentinel = new TemporaryDirectory();
        var poisonedPort = FindFreePort();
        using var poison = new PoisonedEnvironment(poisonedPort, sentinel.DirectoryPath);
        using var context = Context.Create(["serve", "--silent", "--urls", "http://127.0.0.1:0"]);
        var settings = SelfTestServices.CreateSettings(folder);
        var options = new ServerHostOptions
        {
            ConfigureBuilder = builder => builder.Configuration.AddInMemoryCollection(settings)
        };

        // Act: create and start the normal host
        var app = ServerHost.CreateApplication(context, options);
        try
        {
            await app.StartAsync(TestContext.Current.CancellationToken);

            // Assert: the ambient endpoint decided where it listens
            Assert.Contains(app.Urls, url => url.EndsWith($":{poisonedPort}", StringComparison.Ordinal));
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    /// <summary>
    ///     Test that routes with constraints work on the isolated host, which needs the full routing registration:
    ///     a non-numeric project id is "not found" and a numeric one reaches the API, which asks for a token.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_IsolatedSettings_ConstrainedRoutesWork()
    {
        // Arrange: start the isolated host
        using var folder = new TemporaryDirectory();
        using var context = Context.Create(["serve", "--silent", "--urls", "http://127.0.0.1:0"]);
        var app = ServerHost.CreateApplication(context, IsolatedOptions(folder));
        try
        {
            await app.StartAsync(TestContext.Current.CancellationToken);
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) };

            // Act: request an API route with a constraint, with a failing and a matching value
            var notNumeric = await client.GetAsync("/api/projects/abc/reviews", TestContext.Current.CancellationToken);
            var numeric = await client.GetAsync("/api/projects/1/reviews", TestContext.Current.CancellationToken);

            // Assert: the constraint is applied (404) and the matching route is reached (401: no token)
            Assert.Equal(System.Net.HttpStatusCode.NotFound, notNumeric.StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, numeric.StatusCode);
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    /// <summary>
    ///     Test that a configuration that makes the host listen on more than one address is refused by the host check
    ///     instead of being tested through its first address.
    /// </summary>
    [Fact]
    public void HostStartCheck_ExpectOneLoopbackAddress_MoreThanOneAddress_ThrowsValidationFailed()
    {
        // Act: two loopback addresses
        var exception = Record.Exception(
            () => HostStartCheck.ExpectOneLoopbackAddress(["http://127.0.0.1:5001", "http://127.0.0.1:5002"]));

        // Assert: refused with a message that says how many addresses there were
        var failed = Assert.IsType<ValidationFailedException>(exception);
        Assert.Contains("2 addresses", failed.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an address that is not loopback, or has no real port, is refused by the host check.
    /// </summary>
    [Theory]
    [InlineData("http://0.0.0.0:5001")]
    [InlineData("http://192.0.2.1:5001")]
    [InlineData("http://localhost:5001")]
    [InlineData("http://127.0.0.1:0")]
    public void HostStartCheck_ExpectOneLoopbackAddress_NotLoopbackOrNoPort_ThrowsValidationFailed(string url)
    {
        // Act: check one unacceptable address
        var exception = Record.Exception(() => HostStartCheck.ExpectOneLoopbackAddress([url]));

        // Assert: refused
        Assert.IsType<ValidationFailedException>(exception);
    }

    /// <summary>
    ///     Test that no address at all is refused, and that exactly one loopback address is accepted.
    /// </summary>
    [Fact]
    public void HostStartCheck_ExpectOneLoopbackAddress_NoAddressOrOneLoopback_RefusesOrAccepts()
    {
        // Act: check an empty list and a good address
        var none = Record.Exception(() => HostStartCheck.ExpectOneLoopbackAddress([]));
        var address = HostStartCheck.ExpectOneLoopbackAddress(["http://[::1]:5001"]);

        // Assert: the empty list is refused and the IPv6 loopback address with its port is returned
        Assert.IsType<ValidationFailedException>(none);
        Assert.Equal(5001, address.Port);
    }
}

/// <summary>
///     Tests of the protection of the key ring at the level of the host: the settings, the check at start-up, and a
///     restart over a protected ring. The hosts are fully isolated (<see cref="ServerHostOptions.IsolatedSettings"/>),
///     because the key ring settings are read when the services are registered; they are never started, and they
///     never use the key store of the operating system.
/// </summary>
public class ServerHostKeyRingTests
{
    /// <summary>
    ///     A password that must never appear in a message.
    /// </summary>
    private const string Password = "Pa55-word-for-host-tests";

    /// <summary>
    ///     Makes the isolated settings of a host over a temporary folder, with changes.
    /// </summary>
    /// <param name="folder">The temporary folder.</param>
    /// <param name="changes">The settings that are added or replaced.</param>
    /// <returns>The settings.</returns>
    private static Dictionary<string, string?> Settings(TemporaryDirectory folder, params (string Key, string? Value)[] changes)
    {
        var settings = SelfTestServices.CreateSettings(folder);
        foreach (var (key, value) in changes)
        {
            settings[key] = value;
        }

        return settings;
    }

    /// <summary>
    ///     Builds the application, which checks the key ring; it is not started.
    /// </summary>
    /// <param name="settings">The isolated settings.</param>
    /// <param name="logs">Receives the log entries.</param>
    /// <returns>The application; the caller disposes it.</returns>
    private static WebApplication Create(Dictionary<string, string?> settings, CapturingLoggerProvider logs)
    {
        using var context = Context.Create(["serve", "--silent"]);
        return ServerHost.CreateApplication(context, new ServerHostOptions
        {
            IsolatedSettings = settings,
            ConfigureBuilder = builder => builder.Logging.AddProvider(logs)
        });
    }

    /// <summary>
    ///     Gets the key files of the key ring of the settings.
    /// </summary>
    /// <param name="folder">The temporary folder.</param>
    /// <returns>The paths of the key files.</returns>
    private static string[] KeyFiles(TemporaryDirectory folder)
    {
        var keys = Path.Combine(folder.DirectoryPath, "keys");
        return Directory.Exists(keys) ? Directory.GetFiles(keys, "key-*.xml") : [];
    }

    /// <summary>
    ///     Test that a token encrypted before a restart is readable after it when the key ring is protected with a
    ///     certificate.
    /// </summary>
    [Fact]
    public async Task ServerHost_Restart_CertificateKeyRing_ProtectedValueStaysReadable()
    {
        // Arrange: a certificate, and a log to capture
        using var folder = new TemporaryDirectory();
        var logs = new CapturingLoggerProvider();

        // Act: encrypt a token, restart over the same ring and read the token
        var plain = await ProtectAndRestartOverCertificateRingAsync(folder, logs);

        // Assert: readable
        Assert.Equal("tkn-HostCertificate", plain);
    }

    /// <summary>
    ///     Test that the key files of a key ring that is protected with a certificate are encrypted at rest.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_CertificateKeyRing_KeyFilesAreEncrypted()
    {
        // Arrange: a certificate, and a log to capture
        using var folder = new TemporaryDirectory();
        var logs = new CapturingLoggerProvider();

        // Act: start with a ring protected by the certificate, which writes the key file
        await ProtectAndRestartOverCertificateRingAsync(folder, logs);

        // Assert: the key file holds an encrypted secret and no master key in clear text
        var text = await File.ReadAllTextAsync(Assert.Single(KeyFiles(folder)), TestContext.Current.CancellationToken);
        Assert.Contains("<encryptedSecret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<masterKey", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that protecting the key ring with a certificate logs the protection but never the password.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_CertificateKeyRing_DoesNotLogThePassword()
    {
        // Arrange: a certificate, and a log to capture
        using var folder = new TemporaryDirectory();
        var logs = new CapturingLoggerProvider();

        // Act: start twice with a ring protected by the certificate
        await ProtectAndRestartOverCertificateRingAsync(folder, logs);

        // Assert: the protection was logged at information level, and no entry holds the password
        var entries = logs.Entries.Where(entry => entry.Category == typeof(KeyRingProtection).FullName).ToList();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.Equal(LogLevel.Information, entry.Level));
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(Password, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Encrypts a token with a key ring that is protected with a certificate, restarts over the same ring while
    ///     requiring encrypted keys, and reads the token again.
    /// </summary>
    /// <param name="folder">The temporary folder of the hosts.</param>
    /// <param name="logs">Receives the log entries of both hosts.</param>
    /// <returns>The token as the second host reads it.</returns>
    private static async Task<string> ProtectAndRestartOverCertificateRingAsync(TemporaryDirectory folder, CapturingLoggerProvider logs)
    {
        var certificate = KeyRingTestCertificates.WritePfx(folder, "ring", Password);
        string cipher;
        await using (var first = Create(Settings(
            folder,
            ("DataProtection:CertificatePath", certificate),
            ("DataProtection:CertificatePassword", Password),
            ("DataProtection:Protection", "Auto")), logs))
        {
            cipher = first.Services.GetRequiredService<ICredentialProtector>().Protect("tkn-HostCertificate");
        }

        await using var second = Create(Settings(
            folder,
            ("DataProtection:CertificatePath", certificate),
            ("DataProtection:CertificatePassword", Password),
            ("DataProtection:Protection", "Auto"),
            ("DataProtection:RequireEncryptedKeys", "true")), logs);
        return second.Services.GetRequiredService<ICredentialProtector>().Unprotect(cipher);
    }

    /// <summary>
    ///     Test that the File attestation provider loads a protected PFX at start, logs the warning that the
    ///     certificate ends soon, and never logs the password.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_FileAttestationKey_StartsAndWarnsAboutExpiry()
    {
        // Arrange: a protected PFX that ends in 30 days
        using var folder = new TemporaryDirectory();
        var attestation = KeyRingTestCertificates.WritePfx(folder, "attest", Password);
        var logs = new CapturingLoggerProvider();

        // Act: build the application with the File provider
        await using var app = Create(
            Settings(
                folder,
                ("Attestation:Provider", "File"),
                ("Attestation:CertificatePath", attestation),
                ("Attestation:CertificatePassword", Password)),
            logs);
        var status = app.Services.GetRequiredService<IAttestationKeyProvider>().Describe();

        // Assert: the key is available, the expiry warning is logged and shown, and the password is not logged
        Assert.True(status.IsAvailable);
        Assert.Equal("File", status.Provider);
        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains(" expires on ", StringComparison.Ordinal));
        Assert.Contains(status.Warnings, warning => warning.Contains(" expires on ", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(Password, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that an attestation key file without a password is refused when the settings of the key ring require
    ///     encrypted keys.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_UnprotectedFileAttestationKey_RefusedWithRequireEncryptedKeys()
    {
        // Arrange: a ring protected with a certificate, and an attestation PFX without a password
        using var folder = new TemporaryDirectory();
        var common = UnprotectedAttestationKeySettings(folder);

        // Act: create the application with the requirement
        var exception = Assert.Throws<InvalidOperationException>(() => Create(
            Settings(folder, [.. common, ("DataProtection:RequireEncryptedKeys", "true")]),
            new CapturingLoggerProvider()));

        // Assert: refused, naming the setting and saying that the key is not protected by a password
        Assert.Contains("DataProtection:RequireEncryptedKeys", exception.Message, StringComparison.Ordinal);
        Assert.Contains("is not protected by a password", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an attestation key file without a password does not stop the start when the key ring settings
    ///     do not require encrypted keys.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_UnprotectedFileAttestationKey_StartsWithoutRequireEncryptedKeys()
    {
        // Arrange: a ring protected with a certificate, and an attestation PFX without a password
        using var folder = new TemporaryDirectory();
        var common = UnprotectedAttestationKeySettings(folder);

        // Act: create the application without the requirement
        await using var app = Create(Settings(folder, common), new CapturingLoggerProvider());

        // Assert: the application started with a usable attestation key
        Assert.True(app.Services.GetRequiredService<IAttestationKeyProvider>().Describe().IsAvailable);
    }

    /// <summary>
    ///     Test that an attestation key file without a password makes the start log a warning that the key is not
    ///     protected by a password.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_UnprotectedFileAttestationKey_WarnsWithoutRequireEncryptedKeys()
    {
        // Arrange: a ring protected with a certificate, and an attestation PFX without a password
        using var folder = new TemporaryDirectory();
        var common = UnprotectedAttestationKeySettings(folder);
        var logs = new CapturingLoggerProvider();

        // Act: create the application without the requirement
        await using var app = Create(Settings(folder, common), logs);

        // Assert: the start logged the warning
        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains("is not protected by a password", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Gets the settings of a ring that is protected with a certificate and a File attestation key without a
    ///     password.
    /// </summary>
    /// <param name="folder">The temporary folder that receives the certificate files.</param>
    /// <returns>The settings.</returns>
    private static (string, string?)[] UnprotectedAttestationKeySettings(TemporaryDirectory folder)
    {
        var ring = KeyRingTestCertificates.WritePfx(folder, "ring", Password);
        var attestation = KeyRingTestCertificates.WritePfx(folder, "attest", null);
        return
        [
            ("DataProtection:CertificatePath", ring),
            ("DataProtection:CertificatePassword", Password),
            ("DataProtection:Protection", "Auto"),
            ("Attestation:Provider", "File"),
            ("Attestation:CertificatePath", attestation)
        ];
    }

    /// <summary>
    ///     Test that settings that cannot work stop the start with an error that names the setting at fault.
    /// </summary>
    /// <param name="key">The setting to change.</param>
    /// <param name="value">Its value.</param>
    /// <param name="expected">What the message holds.</param>
    [Theory]
    [InlineData("DataProtection:Protection", "Magic", "DataProtection:Protection 'Magic' is not supported. Use Auto, Dpapi, Certificate or PermissionsOnly.")]
    [InlineData("DataProtection:Protection", "Certificate", "DataProtection:CertificatePath")]
    [InlineData("DataProtection:DpapiScope", "Everyone", "DataProtection:DpapiScope 'Everyone' is not supported.")]
    [InlineData("DataProtection:RequireEncryptedKeys", "true", "The key ring is not encrypted at rest and DataProtection:RequireEncryptedKeys is true.")]
    public void ServerHost_CreateApplication_InvalidKeyRingSettings_ThrowsClearError(string key, string value, string expected)
    {
        // Arrange: settings with one wrong value; the rest are those of the isolated host
        using var folder = new TemporaryDirectory();
        var settings = Settings(folder, (key, value));

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => Create(settings, new CapturingLoggerProvider()));

        // Assert: the message names the setting, and nothing was written to the key ring
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        Assert.Empty(KeyFiles(folder));
    }

    /// <summary>
    ///     Test that a certificate file that is missing stops the start with an error that names the setting and the
    ///     path.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_MissingCertificate_ThrowsClearError()
    {
        // Arrange: a path of a certificate file that does not exist
        using var folder = new TemporaryDirectory();
        var missing = folder.GetFilePath("missing.pfx");

        // Act: create the application
        var exception = Assert.Throws<InvalidOperationException>(() => Create(
            Settings(folder, ("DataProtection:Protection", "Auto"), ("DataProtection:CertificatePath", missing), ("DataProtection:CertificatePassword", Password)),
            new CapturingLoggerProvider()));

        // Assert: the message names the setting and the path
        Assert.Contains($"DataProtection:CertificatePath '{missing}' could not be loaded", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a certificate file that cannot be opened with the configured password stops the start with an
    ///     error that names both possible causes and never shows the password.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_WrongPasswordCertificate_ThrowsClearErrorWithoutPassword()
    {
        // Arrange: a certificate file and a password that is not its password
        using var folder = new TemporaryDirectory();
        var present = KeyRingTestCertificates.WritePfx(folder, "ring", Password);

        // Act: create the application
        var exception = Assert.Throws<InvalidOperationException>(() => Create(
            Settings(folder, ("DataProtection:CertificatePath", present), ("DataProtection:CertificatePassword", "other-password"), ("DataProtection:Protection", "Certificate")),
            new CapturingLoggerProvider()));

        // Assert: the message names both causes (CertificateWrongPassword) and shows neither password (CertificateNoSecret)
        Assert.Contains("wrong password or not a certificate with a private key", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("other-password", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a start with another certificate than the one that protects the ring is refused and creates no
    ///     second key, so that the stored tokens are not lost silently.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_WrongCertificateForTheRing_ThrowsClearErrorWithoutSecondKey()
    {
        // Arrange: a ring protected with one certificate
        using var folder = new TemporaryDirectory();
        var right = KeyRingTestCertificates.WritePfx(folder, "right", Password);
        var wrong = KeyRingTestCertificates.WritePfx(folder, "wrong", Password);
        var logs = new CapturingLoggerProvider();
        await using (var first = Create(Settings(
            folder,
            ("DataProtection:Protection", "Auto"),
            ("DataProtection:CertificatePath", right),
            ("DataProtection:CertificatePassword", Password)), logs))
        {
            Assert.Single(KeyFiles(folder));
        }

        // Act: start with the other certificate
        var exception = Assert.Throws<InvalidOperationException>(() => Create(
            Settings(folder, ("DataProtection:Protection", "Auto"), ("DataProtection:CertificatePath", wrong), ("DataProtection:CertificatePassword", Password)),
            logs));

        // Assert: refused, naming the settings, and still one key file
        Assert.Contains("DataProtection:CertificatePath", exception.Message, StringComparison.Ordinal);
        Assert.Contains("DataProtection:PreviousCertificatePaths", exception.Message, StringComparison.Ordinal);
        Assert.Single(KeyFiles(folder));
    }

    /// <summary>
    ///     Test that a ring protected only by permissions is reported with a warning at every start.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_PermissionsOnly_WarnsAtEveryStart()
    {
        // Arrange: two starts over the same folder
        using var folder = new TemporaryDirectory();
        var logs = new CapturingLoggerProvider();

        // Act
        await using (var first = Create(Settings(folder), logs))
        {
            Assert.Single(KeyFiles(folder));
        }

        await using (var second = Create(Settings(folder), logs))
        {
            Assert.Single(KeyFiles(folder));
        }

        // Assert: one warning per start, naming the settings that fix it
        var warnings = logs.Entries.Where(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("not encrypted at rest", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, warning =>
        {
            Assert.Contains("DataProtection:CertificatePath", warning.Message, StringComparison.Ordinal);
            Assert.Contains("DataProtection:RequireEncryptedKeys", warning.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>
    ///     Test that a ring with an unencrypted key file stops the start when encryption is required, so that a
    ///     required protection is never claimed while a clear key is on the disk.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_RequireEncryptedKeysWithPlaintextRing_ThrowsClearError()
    {
        // Arrange: a ring that an earlier start without a certificate left unencrypted
        using var folder = new TemporaryDirectory();
        var certificate = KeyRingTestCertificates.WritePfx(folder, "ring", Password);
        await using (var first = Create(Settings(folder), new CapturingLoggerProvider()))
        {
            var plaintext = await File.ReadAllTextAsync(Assert.Single(KeyFiles(folder)), TestContext.Current.CancellationToken);
            Assert.Contains("<masterKey", plaintext, StringComparison.Ordinal);
        }

        // Act: start with a certificate and the requirement
        var exception = Assert.Throws<InvalidOperationException>(() => Create(
            Settings(
                folder,
                ("DataProtection:Protection", "Certificate"),
                ("DataProtection:CertificatePath", certificate),
                ("DataProtection:CertificatePassword", Password),
                ("DataProtection:RequireEncryptedKeys", "true")),
            new CapturingLoggerProvider()));

        // Assert
        Assert.Contains("DataProtection:RequireEncryptedKeys", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Path.GetFileName(KeyFiles(folder).Single()), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Stores a protected repository token and a protected second-factor secret of a running host in its database.
    /// </summary>
    /// <param name="settings">The isolated settings of the host.</param>
    /// <param name="app">The host whose ring protects the values.</param>
    /// <param name="repositoryId">The id of the repository row.</param>
    /// <param name="userId">The id of the user row.</param>
    private static void SeedSecrets(Dictionary<string, string?> settings, WebApplication app, int repositoryId, int userId)
    {
        var connectionString = settings["Database:ConnectionString"]!;
        SecretSeed.AddRepositoryToken(
            connectionString, repositoryId, app.Services.GetRequiredService<ICredentialProtector>().Protect("tkn-lost-ring"));
        SecretSeed.AddTotpSecret(
            connectionString,
            userId,
            app.Services.GetRequiredService<ITotpSecretProtector>().Protect(userId, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]));
    }

    /// <summary>
    ///     Test that a fresh installation (a new database and an empty ring) starts and creates its ring, without a
    ///     warning or an error about the secrets.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_FreshInstallation_StartsAndCreatesRing()
    {
        // Arrange: nothing yet
        using var folder = new TemporaryDirectory();
        var logs = new CapturingLoggerProvider();

        // Act
        await using var app = Create(Settings(folder), logs);

        // Assert: one key, and nothing about lost or unreadable secrets
        Assert.Single(KeyFiles(folder));
        Assert.DoesNotContain(logs.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("AllowNewKeyRing", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a database with protected secrets and a key folder without keys stops the start with the
    ///     documented message.
    /// </summary>
    /// <param name="removeFolder"><c>true</c> to remove the key folder, <c>false</c> to leave it empty.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerHost_CreateApplication_LostRing_RefusesToStartAndCreatesNoKey(bool removeFolder)
    {
        // Arrange, Act: lose the ring of a database with two secrets and start again
        using var folder = new TemporaryDirectory();
        var exception = await StartWithLostRingAsync(folder, removeFolder);

        // Assert: the message of the guard
        var keys = Path.Combine(folder.DirectoryPath, "keys");
        Assert.Equal(
            $"Key ring '{keys}' is empty but the database holds 2 protected secrets. Restore the keys folder that belongs " +
            "to this database, or set DataProtection:AllowNewKeyRing=true for one start to accept that they become unreadable.",
            exception.Message);
    }

    /// <summary>
    ///     Test that the start that is refused because the key ring is lost creates no key file.
    /// </summary>
    /// <param name="removeFolder"><c>true</c> to remove the key folder, <c>false</c> to leave it empty.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerHost_CreateApplication_LostRing_CreatesNoKey(bool removeFolder)
    {
        // Arrange, Act: lose the ring of a database with two secrets and start again
        using var folder = new TemporaryDirectory();
        await StartWithLostRingAsync(folder, removeFolder);

        // Assert: no key was created by the refused start
        Assert.Empty(KeyFiles(folder));
    }

    /// <summary>
    ///     Test that the error that stops the start because the key ring is lost does not show a stored secret.
    /// </summary>
    /// <param name="removeFolder"><c>true</c> to remove the key folder, <c>false</c> to leave it empty.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerHost_CreateApplication_LostRing_ErrorShowsNoSecret(bool removeFolder)
    {
        // Arrange, Act: lose the ring of a database with two secrets and start again
        using var folder = new TemporaryDirectory();
        var exception = await StartWithLostRingAsync(folder, removeFolder);

        // Assert: the message does not hold the stored token
        Assert.DoesNotContain("tkn-lost-ring", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Creates a database with two secrets that a key ring protects, loses the ring, and starts again.
    /// </summary>
    /// <param name="folder">The temporary folder of the host.</param>
    /// <param name="removeFolder"><c>true</c> to remove the key folder, <c>false</c> to leave it empty.</param>
    /// <returns>The error that stops the start.</returns>
    private static async Task<InvalidOperationException> StartWithLostRingAsync(TemporaryDirectory folder, bool removeFolder)
    {
        var settings = Settings(folder);
        var keys = Path.Combine(folder.DirectoryPath, "keys");
        await using (var first = Create(settings, new CapturingLoggerProvider()))
        {
            SeedSecrets(settings, first, 3, 4);
        }

        foreach (var file in KeyFiles(folder))
        {
            File.Delete(file);
        }

        if (removeFolder)
        {
            Directory.Delete(keys, recursive: true);
        }

        return Assert.Throws<InvalidOperationException>(() => Create(settings, new CapturingLoggerProvider()));
    }

    /// <summary>
    ///     Test that <c>DataProtection:AllowNewKeyRing</c> lets the start go on after the ring was lost, with a warning
    ///     that names the number of secrets, and that the new ring is created.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_LostRingWithAllowNewKeyRing_StartsWithWarning()
    {
        // Arrange: the ring of a database with two secrets is lost
        using var folder = new TemporaryDirectory();
        var settings = Settings(folder);
        await using (var first = Create(settings, new CapturingLoggerProvider()))
        {
            SeedSecrets(settings, first, 3, 4);
        }

        Directory.Delete(Path.Combine(folder.DirectoryPath, "keys"), recursive: true);

        // The attestation key lives beside the ring by default, so it is lost with it; these settings keep it apart
        Directory.Delete(Path.Combine(folder.DirectoryPath, "attestation"), recursive: true);
        var logs = new CapturingLoggerProvider();

        // Act: start with the escape hatch
        await using var app = Create(Settings(folder, ("DataProtection:AllowNewKeyRing", "true")), logs);

        // Assert: a new key, and a warning with the count that does not reveal a secret
        Assert.Single(KeyFiles(folder));
        var warning = Assert.Single(
            logs.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("AllowNewKeyRing", StringComparison.Ordinal));
        Assert.Contains("2 protected secrets", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tkn-lost-ring", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a self-signed attestation key that the new key ring cannot decrypt stops the start with an error
    ///     that names the protected file and tells the operator to delete it, and leaves the file in place.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_AttestationKeyOfLostRing_ThrowsNamingTheFile()
    {
        // Arrange: a start that creates the protected attestation key, then the ring is lost but the key file is not
        using var folder = new TemporaryDirectory();
        var settings = Settings(folder);
        await using (var first = Create(settings, new CapturingLoggerProvider()))
        {
            Assert.True(File.Exists(Path.Combine(folder.DirectoryPath, "attestation", SelfSignedKeyProvider.FileName)));
        }

        var keyFile = Path.Combine(folder.DirectoryPath, "attestation", SelfSignedKeyProvider.FileName);
        Directory.Delete(Path.Combine(folder.DirectoryPath, "keys"), recursive: true);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => Create(settings, new CapturingLoggerProvider()));

        // Assert: the file is named, the remedy is given, and the file is untouched
        Assert.Contains(SelfSignedKeyProvider.FileName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("delete the file", exception.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(keyFile));
    }

    /// <summary>
    ///     Test that <c>DataProtection:AllowNewKeyRing</c> does not help for the protected attestation key: it accepts
    ///     that stored secrets become unreadable, but the server never deletes or replaces a signing key, so the start
    ///     still stops with an error that names the file, and the file is left untouched.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_AttestationKeyOfLostRingWithAllowNewKeyRing_StillThrowsNamingTheFile()
    {
        // Arrange: a start that creates the secrets and the protected attestation key, then the ring is lost
        using var folder = new TemporaryDirectory();
        var settings = Settings(folder);
        await using (var first = Create(settings, new CapturingLoggerProvider()))
        {
            SeedSecrets(settings, first, 3, 4);
        }

        var keyFile = Path.Combine(folder.DirectoryPath, "attestation", SelfSignedKeyProvider.FileName);
        Assert.True(File.Exists(keyFile));
        var before = await File.ReadAllBytesAsync(keyFile, TestContext.Current.CancellationToken);
        Directory.Delete(Path.Combine(folder.DirectoryPath, "keys"), recursive: true);

        // Act: start with the escape hatch for the lost ring
        var exception = Assert.Throws<InvalidOperationException>(
            () => Create(Settings(folder, ("DataProtection:AllowNewKeyRing", "true")), new CapturingLoggerProvider()));

        // Assert: the file is named with the remedy, and it was neither deleted nor replaced
        Assert.Contains(SelfSignedKeyProvider.FileName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("delete the file", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllBytesAsync(keyFile, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that an empty ring next to a database that cannot be read stops the start without creating a key, even
    ///     with <c>DataProtection:AllowNewKeyRing</c>, and that the message holds neither the exception text nor the
    ///     location of the database.
    /// </summary>
    /// <param name="allowNewKeyRing"><c>true</c> to set <c>DataProtection:AllowNewKeyRing</c>.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServerHost_CreateApplication_EmptyRingAndUnreadableDatabase_RefusesToStartAndCreatesNoKey(bool allowNewKeyRing)
    {
        // Arrange: a database that opens but cannot be scanned, and a ring without keys
        using var folder = new TemporaryDirectory();
        var settings = Settings(folder, ("DataProtection:AllowNewKeyRing", allowNewKeyRing ? "true" : "false"));
        var databaseFile = new SqliteConnectionStringBuilder(settings["Database:ConnectionString"]).DataSource;
        CreateBrokenViewDatabase(databaseFile);
        var keys = Path.Combine(folder.DirectoryPath, "keys");

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => Create(settings, new CapturingLoggerProvider()));

        // Assert: the sanitized message of the guard, and no key was created by the refused start
        Assert.Equal(
            $"Key ring '{keys}' is empty and the database (Database:ConnectionString) could not be read to check whether " +
            "it holds protected secrets, so no key was created. Fix the access to the database and start again; run " +
            "'reviewcenter --validate --check-config' for the details.",
            exception.Message);
        Assert.DoesNotContain(databaseFile, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Missing", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(exception.InnerException);
        Assert.Empty(KeyFiles(folder));
    }

    /// <summary>
    ///     Creates a database file that opens and holds a table (so the schema is not created again) but whose
    ///     <c>Repositories</c> view reads a table that does not exist, so the secret scan fails with a database error
    ///     after the start-up prepared the database.
    /// </summary>
    /// <param name="databaseFile">The path of the database file to create.</param>
    private static void CreateBrokenViewDatabase(string databaseFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databaseFile)!);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databaseFile }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE \"Placeholder\" (\"Id\" INTEGER); " +
                "CREATE VIEW \"Repositories\" AS SELECT \"Id\" AS \"Id\", \"Id\" AS \"EncryptedToken\" FROM \"Missing\";";
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    ///     Test that a ring with keys next to a database that cannot be read still starts, with a warning, as the
    ///     database is reported by the start-up that opens it.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_RingWithKeysAndUnreadableDatabase_StartsWithWarning()
    {
        // Arrange: a ring with a key, and a database that opens but cannot be scanned
        using var folder = new TemporaryDirectory();
        var settings = Settings(folder);
        await using (var first = Create(settings, new CapturingLoggerProvider()))
        {
            Assert.Single(KeyFiles(folder));
        }

        var databaseFile = new SqliteConnectionStringBuilder(settings["Database:ConnectionString"]).DataSource;
        SqliteConnection.ClearAllPools();
        File.Delete(databaseFile);
        CreateBrokenViewDatabase(databaseFile);
        var logs = new CapturingLoggerProvider();

        // Act
        await using var app = Create(settings, logs);

        // Assert: started on the same key, with the warning that the database could not be read
        Assert.Single(KeyFiles(folder));
        Assert.Single(
            logs.Entries,
            entry => entry.Level == LogLevel.Warning &&
                     entry.Message.Contains("database could not be read", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a restart over the same ring is not refused, logs nothing about the secrets, and still reads
    ///     them.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_RestartWithSameRing_StartsWithoutComplaint()
    {
        // Arrange: a database with secrets of the ring
        using var folder = new TemporaryDirectory();
        var settings = Settings(folder);
        await using (var first = Create(settings, new CapturingLoggerProvider()))
        {
            SeedSecrets(settings, first, 3, 4);
        }

        var logs = new CapturingLoggerProvider();

        // Act: restart with the same folders
        await using var second = Create(settings, logs);

        // Assert: still one key, no error and no mention of the escape hatch
        Assert.Single(KeyFiles(folder));
        Assert.DoesNotContain(logs.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("AllowNewKeyRing", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that secrets of another ring (the ring has keys, but not these) are logged as an error with the counts
    ///     and the remedy, and do not stop the start.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_SomeSecretsUnreadable_LogsErrorAndStarts()
    {
        // Arrange: a database with one readable and two unreadable secrets (protected by the ring of another installation)
        using var folder = new TemporaryDirectory();
        using var other = new TemporaryDirectory();
        var settings = Settings(folder);
        await using (var first = Create(settings, new CapturingLoggerProvider()))
        {
            SeedSecrets(settings, first, 3, 4);
        }

        var connectionString = settings["Database:ConnectionString"]!;
        await using (var foreign = Create(Settings(other), new CapturingLoggerProvider()))
        {
            SecretSeed.AddRepositoryToken(
                connectionString, 7, foreign.Services.GetRequiredService<ICredentialProtector>().Protect("tkn-foreign"));
            SecretSeed.AddTotpSecret(
                connectionString,
                9,
                foreign.Services.GetRequiredService<ITotpSecretProtector>().Protect(9, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]));
        }

        var logs = new CapturingLoggerProvider();

        // Act
        await using var app = Create(settings, logs);

        // Assert: the start went on, and the error names the counts per kind, the ids and the remedy
        var error = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("cannot decrypt 2 of 4 protected secrets", error.Message, StringComparison.Ordinal);
        Assert.Contains("1 repository token(s) (repository ids 7)", error.Message, StringComparison.Ordinal);
        Assert.Contains("1 second-factor secret(s) (user ids 9)", error.Message, StringComparison.Ordinal);
        Assert.Contains("administrator resets the two-factor secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tkn-foreign", error.Message, StringComparison.Ordinal);
        Assert.Single(KeyFiles(folder));
    }

    /// <summary>
    ///     Test that the escape hatch is off by default.
    /// </summary>
    [Fact]
    public void KeyRingSettings_AllowNewKeyRing_DefaultsToFalse()
    {
        // Arrange / Act
        var settings = new KeyRingSettings();

        // Assert
        Assert.False(settings.AllowNewKeyRing);
    }

    /// <summary>
    ///     Test that DPAPI off Windows stops the start with an error that names the setting.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_DpapiOnUnix_ThrowsClearError()
    {
        // Arrange: DPAPI requested on a machine that does not have it
        Assert.SkipWhen(OperatingSystem.IsWindows(), "DPAPI is available on Windows.");
        using var folder = new TemporaryDirectory();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => Create(
            Settings(folder, ("DataProtection:Protection", "Dpapi")),
            new CapturingLoggerProvider()));

        // Assert
        Assert.Contains("DataProtection:Protection Dpapi is only available on Windows.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that on Unix the key folder is private to the owner (mode 0700) when the key ring is protected by
    ///     permissions only.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task ServerHost_CreateApplication_PermissionsOnlyOnUnix_RestrictsTheFolder()
    {
        // Arrange: a key folder that exists with loose permissions
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file permissions are only enforced on Linux and macOS.");
        using var folder = new TemporaryDirectory();
        var keys = CreateKeyFolderWithLoosePermissions(folder);

        // Act: create the application
        await using var app = Create(Settings(folder), new CapturingLoggerProvider());

        // Assert: 0700 for the folder
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(keys));
    }

    /// <summary>
    ///     Test that on Unix every key file is readable and writable by the owner only (mode 0600) when the key ring
    ///     is protected by permissions only.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task ServerHost_CreateApplication_PermissionsOnlyOnUnix_RestrictsTheKeyFiles()
    {
        // Arrange: a key folder that exists with loose permissions
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file permissions are only enforced on Linux and macOS.");
        using var folder = new TemporaryDirectory();
        CreateKeyFolderWithLoosePermissions(folder);

        // Act: create the application, which writes the key file
        await using var app = Create(Settings(folder), new CapturingLoggerProvider());

        // Assert: 0600 for every key file
        var files = KeyFiles(folder);
        Assert.NotEmpty(files);
        Assert.All(files, file => Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file)));
    }

    /// <summary>
    ///     Creates the key folder of the settings with permissions that other accounts may use.
    /// </summary>
    /// <param name="folder">The temporary folder.</param>
    /// <returns>The path of the key folder.</returns>
    [UnsupportedOSPlatform("windows")]
    private static string CreateKeyFolderWithLoosePermissions(TemporaryDirectory folder)
    {
        var keys = Path.Combine(folder.DirectoryPath, "keys");
        Directory.CreateDirectory(keys);
        File.SetUnixFileMode(keys, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
        return keys;
    }
}
