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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemaConsulting.ReviewCenter.Tests.Messaging;

/// <summary>
///     Subsystem tests for the Messaging subsystem: the settings of the <c>Notifications</c> section choosing the
///     sender, and the sender delivering a message, working together through the service registration.
/// </summary>
public sealed class MessagingSubsystemTests : IDisposable
{
    /// <summary>
    ///     The folder the file sender writes into; deleted with the test.
    /// </summary>
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"rc-messaging-{Guid.NewGuid():N}");

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    /// <summary>
    ///     Test that with notifications off the registered sender is not configured and refuses to send.
    /// </summary>
    [Fact]
    public async Task MessagingSubsystem_NotificationsOff_SenderIsNotConfiguredAndSendFails()
    {
        // Arrange: the default settings, which are off
        using var provider = Build(new Dictionary<string, string?>());
        var sender = provider.GetRequiredService<INotificationSender>();
        var settings = provider.GetRequiredService<NotificationSettings>();

        // Act: send
        var exception = await Assert.ThrowsAsync<NotificationSendException>(() => sender.SendAsync(
            new NotificationMessage("ann@example.com", "S", "t", "<p>t</p>"),
            TestContext.Current.CancellationToken));

        // Assert: nothing is configured, nothing can be sent
        Assert.False(sender.IsConfigured);
        Assert.False(settings.Enabled);
        Assert.NotNull(exception);
    }

    /// <summary>
    ///     Test that the settings of the file transport choose the file sender, which delivers a message.
    /// </summary>
    [Fact]
    public async Task MessagingSubsystem_FileTransport_SettingsChooseFileSenderThatDeliversMessage()
    {
        // Arrange: settings for the file transport
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Notifications:Enabled"] = "true",
            ["Notifications:Sender"] = "File",
            ["Notifications:FromAddress"] = "reviews@example.com",
            ["Notifications:BaseUrl"] = "https://reviewcenter.example.com/",
            ["Notifications:File:Directory"] = _folder
        });
        var sender = provider.GetRequiredService<INotificationSender>();

        // Act: send a message
        await sender.SendAsync(
            new NotificationMessage("ann@example.com", "Hello", "text", "<p>text</p>"),
            TestContext.Current.CancellationToken);

        // Assert: the file sender, usable settings and one file in the folder
        Assert.IsType<FileNotificationSender>(sender);
        Assert.True(provider.GetRequiredService<NotificationSettings>().Enabled);
        Assert.Single(Directory.GetFiles(_folder, "*.eml"));
    }

    /// <summary>
    ///     Test that the settings of the SMTP transport choose the SMTP sender.
    /// </summary>
    [Fact]
    public void MessagingSubsystem_SmtpTransport_SettingsChooseSmtpSender()
    {
        // Arrange: settings for the SMTP transport
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Notifications:Enabled"] = "true",
            ["Notifications:Sender"] = "Smtp",
            ["Notifications:FromAddress"] = "reviews@example.com",
            ["Notifications:BaseUrl"] = "https://reviewcenter.example.com/",
            ["Notifications:Smtp:Host"] = "mail.example.com"
        });

        // Act: resolve the sender
        var sender = provider.GetRequiredService<INotificationSender>();

        // Assert: the SMTP sender
        Assert.IsType<SmtpNotificationSender>(sender);
        Assert.True(sender.IsConfigured);
    }

    /// <summary>
    ///     Test that invalid settings are reported when the settings are first used, naming the setting.
    /// </summary>
    [Fact]
    public void MessagingSubsystem_InvalidSettings_AreRefusedNamingTheSetting()
    {
        // Arrange: enabled SMTP settings without a host
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Notifications:Enabled"] = "true",
            ["Notifications:Sender"] = "Smtp",
            ["Notifications:FromAddress"] = "reviews@example.com",
            ["Notifications:BaseUrl"] = "https://reviewcenter.example.com/"
        });

        // Act: use the settings
        var exception = Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<INotificationSender>());

        // Assert: the setting is named
        Assert.Contains("Notifications:Smtp:Host", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Builds a service provider with the notification registration over the given settings.
    /// </summary>
    /// <param name="settings">The configuration values.</param>
    /// <returns>The service provider.</returns>
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddNotifications(configuration);
        return services.BuildServiceProvider();
    }
}
