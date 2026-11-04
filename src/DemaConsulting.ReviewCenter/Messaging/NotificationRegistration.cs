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
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Messaging;

/// <summary>
///     Registers the settings and the sender of e-mail notifications.
/// </summary>
/// <remarks>The class holds no state and is thread-safe.</remarks>
internal static class NotificationRegistration
{
    /// <summary>
    ///     Registers the settings, the check of the settings and the sender chosen by them.
    /// </summary>
    /// <param name="services">The service collection. Must not be null.</param>
    /// <param name="configuration">The configuration that holds the <c>Notifications</c> section. Must not be null.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    ///     The settings are checked when they are first used, at start-up (see
    ///     <see cref="NotificationOptionsValidator"/>). While notifications are off, or no transport is chosen, the
    ///     sender is <see cref="NoNotificationSender"/>, so nothing of the mail library is used.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static IServiceCollection AddNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<NotificationOptions>().Bind(configuration.GetSection(NotificationOptions.SectionName));
        services.AddSingleton<IValidateOptions<NotificationOptions>, NotificationOptionsValidator>();
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<NotificationOptions>>().Value;
            var name = string.IsNullOrWhiteSpace(options.FromName)
                ? NotificationSettings.DefaultInstanceName
                : options.FromName.Trim();
            var usable = options.Enabled && options.Sender is NotificationSenderKind.Smtp or NotificationSenderKind.File;
            return new NotificationSettings(usable, name, options.MaxMailsPerHour);
        });
        services.AddSingleton<INotificationSender>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<NotificationOptions>>().Value;
            var loggers = provider.GetRequiredService<ILoggerFactory>();
            if (!options.Enabled)
            {
                return new NoNotificationSender();
            }

            return options.Sender switch
            {
                NotificationSenderKind.Smtp =>
                    new SmtpNotificationSender(options, loggers.CreateLogger<SmtpNotificationSender>()),
                NotificationSenderKind.File =>
                    new FileNotificationSender(options, loggers.CreateLogger<FileNotificationSender>()),
                _ => new NoNotificationSender()
            };
        });
        return services;
    }

    /// <summary>
    ///     Registers the background worker that mails the notifications, when notifications are usable.
    /// </summary>
    /// <param name="services">The service collection. Must not be null.</param>
    /// <param name="configuration">The configuration that holds the <c>Notifications</c> section. Must not be null.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    ///     Nothing is registered unless <c>Notifications:Enabled</c> is set and the sender is SMTP or file, so with
    ///     notifications off (or without a transport) no timer runs and no mail is composed. The settings must
    ///     already have been registered with <see cref="AddNotifications"/> and be valid; the worker is only
    ///     meant for the web host, never for a command-line run.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static IServiceCollection AddNotificationWorker(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(NotificationOptions.SectionName).Get<NotificationOptions>();
        if (section is not { Enabled: true } || section.Sender is not (NotificationSenderKind.Smtp or NotificationSenderKind.File))
        {
            return services;
        }

        services.AddHostedService(provider =>
        {
            var options = provider.GetRequiredService<IOptions<NotificationOptions>>().Value;
            var logger = provider.GetRequiredService<ILogger<NotificationWorker>>();
            var settings = provider.GetRequiredService<NotificationSettings>();
            var zone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone.Trim());
            var composer = new NotificationComposer(settings.InstanceName, options.BaseUrl, options.IncludeTitles);
            var schedule = new DigestSchedule(options.DigestHour, zone);
            var dispatcher = new NotificationDispatcher(
                provider.GetRequiredService<INotificationStore>(),
                provider.GetRequiredService<InboxService>(),
                provider.GetRequiredService<IAuditStore>(),
                provider.GetRequiredService<INotificationSender>(),
                composer,
                schedule,
                settings,
                provider.GetRequiredService<TimeProvider>(),
                TimeSpan.FromSeconds(options.BatchWindowSeconds),
                ex => logger.LogError("A notification step failed ({ErrorType}).", ex.GetType().Name));
            return new NotificationWorker(
                dispatcher.RunPassAsync,
                TimeSpan.FromSeconds(options.PollSeconds),
                provider.GetRequiredService<TimeProvider>(),
                logger);
        });
        return services;
    }
}
