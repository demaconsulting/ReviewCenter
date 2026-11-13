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
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.Messaging;
using DemaConsulting.ReviewCenter.SelfTest.Checks;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DemaConsulting.ReviewCenter.Tests.Hosting;

/// <summary>
///     Unit tests for the registration of the notification worker by the <see cref="ServerHost"/> composition
///     root: only in the web host, and only when notifications are enabled with a transport.
/// </summary>
public class ServerHostNotificationWorkerTests
{
    /// <summary>
    ///     Test that the web host has the notification worker when notifications are enabled with the file sender.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_NotificationsFile_HasWorker()
    {
        // Arrange
        using var folder = new TemporaryDirectory();
        using var context = Context.Create(["serve", "--silent"]);

        // Act
        using var app = ServerHost.CreateApplication(context, Options(FileSettings(folder)));

        // Assert
        Assert.Single(app.Services.GetServices<IHostedService>().OfType<NotificationWorker>());
    }

    /// <summary>
    ///     Test that the web host has the notification worker when notifications are enabled with SMTP.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_NotificationsSmtp_HasWorker()
    {
        // Arrange
        using var context = Context.Create(["serve", "--silent"]);
        var settings = new Dictionary<string, string?>
        {
            ["Notifications:Enabled"] = "true",
            ["Notifications:Sender"] = "Smtp",
            ["Notifications:FromAddress"] = "reviewcenter@example.com",
            ["Notifications:BaseUrl"] = "https://reviewcenter.example.com/",
            ["Notifications:Smtp:Host"] = "mail.example.com",
            ["Notifications:Smtp:Port"] = "587",
            ["Notifications:Smtp:Tls"] = "StartTls"
        };

        // Act
        using var app = ServerHost.CreateApplication(context, Options(settings));

        // Assert
        Assert.Single(app.Services.GetServices<IHostedService>().OfType<NotificationWorker>());
    }

    /// <summary>
    ///     Test that the web host has no notification worker while notifications are off.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_NotificationsOff_NoWorker()
    {
        // Arrange: notifications off, with settings that would otherwise be usable
        using var folder = new TemporaryDirectory();
        using var context = Context.Create(["serve", "--silent"]);
        var settings = FileSettings(folder);
        settings["Notifications:Enabled"] = "false";

        // Act
        using var off = ServerHost.CreateApplication(context, Options(settings));
        using var unset = ServerHost.CreateApplication(context, Options([]));

        // Assert
        Assert.Empty(off.Services.GetServices<IHostedService>().OfType<NotificationWorker>());
        Assert.Empty(unset.Services.GetServices<IHostedService>().OfType<NotificationWorker>());
    }

    /// <summary>
    ///     Test that enabled notifications without a transport are refused at start-up, so no worker exists.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_EnabledSenderNone_RefusedBeforeAnyWorker()
    {
        // Arrange: enabled, but no sender chosen
        using var context = Context.Create(["serve", "--silent"]);
        var settings = new Dictionary<string, string?>
        {
            ["Notifications:Enabled"] = "true",
            ["Notifications:FromAddress"] = "reviewcenter@example.com",
            ["Notifications:BaseUrl"] = "https://reviewcenter.example.com/"
        };

        // Act and Assert: the application is not built
        var exception = Assert.Throws<InvalidOperationException>(
            () => ServerHost.CreateApplication(context, Options(settings)));
        Assert.Contains("Notifications:Sender", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the shared service registration of the commands adds no hosted service, even when
    ///     notifications are enabled: a command-line run never mails.
    /// </summary>
    [Fact]
    public void ServerHost_AddServices_NotificationsEnabled_NoHostedService()
    {
        // Arrange: enabled file sender
        using var folder = new TemporaryDirectory();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(FileSettings(folder)).Build();
        var services = new ServiceCollection();

        // Act
        ServerHost.AddServices(services, configuration);

        // Assert: the only hosted service of the framework (data protection) is there, none made by a factory
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService)
                          && (descriptor.ImplementationFactory is not null || descriptor.ImplementationType == typeof(NotificationWorker)));
    }

    /// <summary>
    ///     Test that the host of the self-test start check has no notification worker.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_SelfTestSettings_NoWorker()
    {
        // Arrange: the isolated settings of the start check
        using var folder = new TemporaryDirectory();
        using var context = Context.Create(["serve", "--silent", "--urls", "http://127.0.0.1:0"]);

        // Act
        using var app = ServerHost.CreateApplication(
            context,
            new ServerHostOptions { IsolatedSettings = SelfTestServices.CreateSettings(folder) });

        // Assert
        Assert.Empty(app.Services.GetServices<IHostedService>().OfType<NotificationWorker>());
    }

    /// <summary>
    ///     Test that the worker runs while the host runs and has stopped when the host has stopped.
    /// </summary>
    [Fact]
    public async Task ServerHost_RunAsync_Stop_WorkerStopped()
    {
        // Arrange: a started in-memory host with notifications on
        using var folder = new TemporaryDirectory();
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(
            logs,
            connectionString: null,
            ["serve", "--silent"],
            FileSettings(folder));
        var worker = Assert.Single(app.Services.GetServices<IHostedService>().OfType<NotificationWorker>());
        Assert.False(worker.ExecuteTask!.IsCompleted);

        // Act
        await app.StopAsync(TestContext.Current.CancellationToken);

        // Assert: stopped without a fault. A stop that comes before the thread pool has started the worker leaves
        // BackgroundService's Task.Run task Canceled rather than RanToCompletion; both are a clean stop.
        Assert.True(worker.ExecuteTask.IsCompleted);
        Assert.False(worker.ExecuteTask.IsFaulted);
    }

    /// <summary>
    ///     Builds the options of a host over an in-memory server with a throwaway database.
    /// </summary>
    /// <param name="settings">The settings to add.</param>
    /// <returns>The options.</returns>
    private static ServerHostOptions Options(Dictionary<string, string?> settings)
    {
        return new ServerHostOptions
        {
            MapStaticAssets = false,
            ConfigureBuilder = builder =>
            {
                builder.WebHost.UseTestServer();
                settings["Database:ConnectionString"] = TestDatabase.NewConnectionString();
                builder.Configuration.AddInMemoryCollection(settings);
            }
        };
    }

    /// <summary>
    ///     Gets settings that enable notifications with the file sender.
    /// </summary>
    /// <param name="folder">The folder the file sender writes to.</param>
    /// <returns>A new, modifiable dictionary of settings.</returns>
    private static Dictionary<string, string?> FileSettings(TemporaryDirectory folder)
    {
        return new Dictionary<string, string?>
        {
            ["Notifications:Enabled"] = "true",
            ["Notifications:Sender"] = "File",
            ["Notifications:FromAddress"] = "reviewcenter@example.com",
            ["Notifications:BaseUrl"] = "https://reviewcenter.example.com/",
            ["Notifications:File:Directory"] = folder.GetFilePath("mails")
        };
    }
}
