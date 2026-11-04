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

using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Messaging;
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemaConsulting.ReviewCenter.Tests.Messaging;

/// <summary>
///     Unit tests for the registration of the notification worker: it is registered only when notifications are
///     enabled with the SMTP or the file sender, and the settings reach the worker, the dispatcher, the schedule and
///     the composer.
/// </summary>
/// <remarks>
///     The worker is started on a clock that only moves when a test moves it, so the poll interval, the batch
///     window and the digest hour are observed through the mails the real wiring sends.
/// </remarks>
public sealed class NotificationWorkerRegistrationTests
{
    /// <summary>
    ///     The cancellation token of the running test.
    /// </summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Test that the worker is registered when notifications are enabled with the SMTP sender.
    /// </summary>
    [Fact]
    public async Task NotificationRegistration_AddNotificationWorker_EnabledSmtp_Registers()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        var settings = Settings("Smtp");
        settings["Notifications:Smtp:Host"] = "mail.example.com";

        // Act
        using var provider = Build(world, new StepClock(), settings);

        // Assert
        Assert.Single(provider.GetServices<IHostedService>().OfType<NotificationWorker>());
    }

    /// <summary>
    ///     Test that the worker is registered when notifications are enabled with the file sender.
    /// </summary>
    [Fact]
    public async Task NotificationRegistration_AddNotificationWorker_EnabledFile_Registers()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();

        // Act
        using var provider = Build(world, new StepClock(), Settings("File"));

        // Assert
        Assert.Single(provider.GetServices<IHostedService>().OfType<NotificationWorker>());
    }

    /// <summary>
    ///     Test that no worker is registered while notifications are not enabled.
    /// </summary>
    [Fact]
    public async Task NotificationRegistration_AddNotificationWorker_Off_NotRegistered()
    {
        // Arrange: a sender is chosen but notifications are not enabled
        var world = await NotificationDispatcherWorld.CreateAsync();
        var settings = Settings("File");
        settings["Notifications:Enabled"] = "false";

        // Act
        using var provider = Build(world, new StepClock(), settings);

        // Assert
        Assert.Empty(provider.GetServices<IHostedService>());
    }

    /// <summary>
    ///     Test that no worker is registered when notifications have no section at all.
    /// </summary>
    [Fact]
    public async Task NotificationRegistration_AddNotificationWorker_NoSection_NotRegistered()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();

        // Act
        using var provider = Build(world, new StepClock(), []);

        // Assert
        Assert.Empty(provider.GetServices<IHostedService>());
    }

    /// <summary>
    ///     Test that no worker is registered when the sender is None.
    /// </summary>
    [Fact]
    public async Task NotificationRegistration_AddNotificationWorker_EnabledSenderNone_NotRegistered()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();

        // Act
        using var provider = Build(world, new StepClock(), Settings("None"));

        // Assert
        Assert.Empty(provider.GetServices<IHostedService>());
    }

    /// <summary>
    ///     Test that the worker passes at the configured poll interval and not before.
    /// </summary>
    [Fact]
    public async Task NotificationRegistration_AddNotificationWorker_PollSeconds_Honored()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        var clock = new StepClock();
        var settings = Settings("File");
        settings["Notifications:PollSeconds"] = "45";
        using var provider = Build(world, clock, settings);
        var worker = provider.GetServices<IHostedService>().Single();
        await worker.StartAsync(Ct);
        await clock.WaitForTimersAsync(1);
        Assert.Equal(1, world.Store.Calls);

        // Act: just before the interval, then at it
        await clock.AdvanceAsync(TimeSpan.FromSeconds(44), expectPass: false);
        var before = world.Store.Calls;
        await clock.AdvanceAsync(TimeSpan.FromSeconds(1), expectPass: true);
        var after = world.Store.Calls;

        // Assert
        Assert.Equal(1, before);
        Assert.Equal(2, after);
        await worker.StopAsync(Ct);
    }

    /// <summary>
    ///     Test that the dispatcher waits the configured batch window before it sends.
    /// </summary>
    [Fact]
    public async Task NotificationRegistration_AddNotificationWorker_BatchWindow_Honored()
    {
        // Arrange: a window of 30 seconds, passes every 10 seconds, ann has something new
        var world = await NotificationDispatcherWorld.CreateAsync();
        var clock = new StepClock();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        var settings = Settings("File");
        settings["Notifications:PollSeconds"] = "10";
        settings["Notifications:BatchWindowSeconds"] = "30";
        using var provider = Build(world, clock, settings);
        var worker = provider.GetServices<IHostedService>().Single();

        // Act: the window starts with the first pass
        await worker.StartAsync(Ct);
        await clock.WaitForTimersAsync(1);
        await clock.AdvanceAsync(TimeSpan.FromSeconds(10), expectPass: true);
        await clock.AdvanceAsync(TimeSpan.FromSeconds(10), expectPass: true);
        var early = world.Sender.Sent.Count;
        await clock.AdvanceAsync(TimeSpan.FromSeconds(10), expectPass: true);

        // Assert: nothing before 30 seconds, one mail at 30 seconds (the default window is 300)
        Assert.Equal(0, early);
        Assert.Single(world.Sender.Sent);
        await worker.StopAsync(Ct);
    }

    /// <summary>
    ///     Test that the daily summary follows the configured time zone and hour.
    /// </summary>
    [Fact]
    public async Task NotificationRegistration_AddNotificationWorker_ZoneAndHour_Honored()
    {
        // Arrange: the summary at 06:00 in Tokyo (UTC+9); ann's baseline is taken at 14:06 Tokyo on March 4th
        var world = await NotificationDispatcherWorld.CreateAsync();
        var clock = new StepClock();
        world.Enroll("ann", NotificationMode.Daily, baselinePending: true);
        var settings = Settings("File");
        settings["Notifications:TimeZone"] = "Asia/Tokyo";
        settings["Notifications:DigestHour"] = "6";
        using var provider = Build(world, clock, settings);
        var worker = provider.GetServices<IHostedService>().Single();
        await worker.StartAsync(Ct);
        await clock.WaitForTimersAsync(1);
        await world.SeatAsync("r1", "ann");

        // Act: 05:59 Tokyo the next morning, then 06:00
        await clock.AdvanceAsync(TimeSpan.FromHours(15) + TimeSpan.FromMinutes(53), expectPass: true);
        var early = world.Sender.Sent.Count;
        await clock.AdvanceAsync(TimeSpan.FromMinutes(1), expectPass: true);

        // Assert: nothing before the hour of the zone, one summary at it
        Assert.Equal(0, early);
        var message = Assert.Single(world.Sender.Sent);
        Assert.Contains("daily summary", message.Subject, StringComparison.Ordinal);
        await worker.StopAsync(Ct);
    }

    /// <summary>
    ///     Test that titles are left out of the mail when they are not to be included.
    /// </summary>
    [Fact]
    public async Task NotificationRegistration_AddNotificationWorker_IncludeTitles_Honored()
    {
        // Arrange: titles off
        var world = await NotificationDispatcherWorld.CreateAsync();
        var clock = new StepClock();
        await world.SeatAsync("r1", "ann", "Secret launch plan");
        world.Enroll("ann", NotificationMode.Every);
        var settings = Settings("File");
        settings["Notifications:IncludeTitles"] = "false";
        settings["Notifications:PollSeconds"] = "60";
        settings["Notifications:BatchWindowSeconds"] = "60";
        using var provider = Build(world, clock, settings);
        var worker = provider.GetServices<IHostedService>().Single();
        await worker.StartAsync(Ct);
        await clock.WaitForTimersAsync(1);

        // Act
        await clock.AdvanceAsync(TimeSpan.FromSeconds(60), expectPass: true);

        // Assert
        var message = Assert.Single(world.Sender.Sent);
        Assert.DoesNotContain("Secret launch plan", message.TextBody + message.HtmlBody + message.Subject, StringComparison.Ordinal);
        await worker.StopAsync(Ct);
    }

    /// <summary>
    ///     Test that titles are part of the mail by default.
    /// </summary>
    [Fact]
    public async Task NotificationRegistration_AddNotificationWorker_IncludeTitlesDefault_TitleShown()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        var clock = new StepClock();
        await world.SeatAsync("r1", "ann", "Secret launch plan");
        world.Enroll("ann", NotificationMode.Every);
        var settings = Settings("File");
        settings["Notifications:PollSeconds"] = "60";
        settings["Notifications:BatchWindowSeconds"] = "60";
        using var provider = Build(world, clock, settings);
        var worker = provider.GetServices<IHostedService>().Single();
        await worker.StartAsync(Ct);
        await clock.WaitForTimersAsync(1);

        // Act
        await clock.AdvanceAsync(TimeSpan.FromSeconds(60), expectPass: true);

        // Assert
        var message = Assert.Single(world.Sender.Sent);
        Assert.Contains("Secret launch plan", message.TextBody, StringComparison.Ordinal);
        await worker.StopAsync(Ct);
    }

    /// <summary>
    ///     Test that a missing argument is refused.
    /// </summary>
    [Fact]
    public void NotificationRegistration_AddNotificationWorker_NullArgument_Throws()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        // Act and Assert
        Assert.Throws<ArgumentNullException>(() => NotificationRegistration.AddNotificationWorker(null!, configuration));
        Assert.Throws<ArgumentNullException>(() => services.AddNotificationWorker(null!));
    }

    /// <summary>
    ///     Gets the settings of enabled notifications with a sender.
    /// </summary>
    /// <param name="sender">The name of the sender.</param>
    /// <returns>The configuration values.</returns>
    private static Dictionary<string, string?> Settings(string sender)
    {
        return new Dictionary<string, string?>
        {
            ["Notifications:Enabled"] = "true",
            ["Notifications:Sender"] = sender,
            ["Notifications:FromAddress"] = "reviews@example.com",
            ["Notifications:BaseUrl"] = "https://reviewcenter.example.com/",
            ["Notifications:File:Directory"] = Path.Combine(Path.GetTempPath(), "rc-registration-unused")
        };
    }

    /// <summary>
    ///     Builds a service provider with the registrations and the fakes the dispatcher needs.
    /// </summary>
    /// <param name="world">The world that holds the fakes.</param>
    /// <param name="clock">The clock of the worker and the dispatcher.</param>
    /// <param name="settings">The configuration values.</param>
    /// <returns>The service provider.</returns>
    private static ServiceProvider Build(NotificationDispatcherWorld world, StepClock clock, Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddNotifications(configuration);
        services.AddNotificationWorker(configuration);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<INotificationStore>(world.Store);
        services.AddSingleton<IAuditStore>(world.AuditStore);
        services.AddSingleton(world.Inbox.Inbox);
        services.AddSingleton<INotificationSender>(world.Sender);
        return services.BuildServiceProvider();
    }
}
