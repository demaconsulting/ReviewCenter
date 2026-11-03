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

using System.Globalization;
using DemaConsulting.ReviewCenter.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.SelfTest.Checks;

/// <summary>
///     The opt-in check (<c>--validate --check-config</c>) that the e-mail notification settings the server is
///     configured with are usable.
/// </summary>
/// <remarks>
///     <para>
///     It loads the configuration the way the server does and runs the same checks as the start-up
///     (<see cref="NotificationOptionsValidator"/>), and it resolves the SMTP password the way the sender does, so a
///     missing or unreadable password file is found before the server is started. It reports the transport, for the
///     SMTP transport the server, port and connection protection, and where the password comes from.
///     Warnings (a password written directly into the configuration, an unencrypted connection to another machine)
///     are appended to the report.
///     </para>
///     <para>
///     The check never connects to a server and never sends a mail: it is a check of the settings, not of the
///     network. It prints no password, no address and no path. While notifications are off it is skipped. The class
///     is stateless and thread-safe.
///     </para>
/// </remarks>
internal static class ConfiguredNotificationsCheck
{
    /// <summary>
    ///     The name of the check in the report and the results file.
    /// </summary>
    public const string Name = "ReviewCenter_ConfiguredNotifications";

    /// <summary>
    ///     Runs the check.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The transport, the server and the password source, with any warnings; never a secret or a path.</returns>
    /// <exception cref="ValidationSkippedException">Thrown when e-mail notifications are not enabled.</exception>
    /// <exception cref="ValidationFailedException">Thrown when the settings are not usable.</exception>
    public static Task<string> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The same configuration sources as the server: appsettings.json next to the program and the environment
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = typeof(ConfiguredNotificationsCheck).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });
        var options = new NotificationOptions();
        try
        {
            builder.Configuration.GetSection(NotificationOptions.SectionName).Bind(options);
        }
        catch (InvalidOperationException)
        {
            // A value that cannot be converted; the exception text may hold the value, so it is not shown
            throw new ValidationFailedException(
                "A value of the Notifications settings has the wrong type (for example a number or Sender is not valid).");
        }

        if (!options.Enabled)
        {
            throw new ValidationSkippedException("E-mail notifications are not enabled (Notifications:Enabled).");
        }

        // The checks of the start-up; their messages name the setting and never a value
        var result = new NotificationOptionsValidator().Validate(null, options);
        ValidationFailedException.ThrowUnless(
            result.Succeeded,
            result.FailureMessage ?? "The Notifications settings are not usable.");

        // Describe what the settings say, without a secret, a path or an address
        var report = options.Sender == NotificationSenderKind.Smtp
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Sender SMTP, server {options.Smtp.Host.Trim()}:{options.Smtp.Port}, TLS {options.Smtp.Tls}, " +
                $"password {DescribePassword(options)}.")
            : "Sender File, mails are written as .eml files into the configured folder.";
        var warnings = options.GetWarnings().Select(warning => $" Warning: {warning}");
        return Task.FromResult(report + string.Concat(warnings));
    }

    /// <summary>
    ///     Tells where the SMTP password comes from, without showing it.
    /// </summary>
    /// <param name="options">The settings.</param>
    /// <returns>A short description.</returns>
    private static string DescribePassword(NotificationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Smtp.PasswordFile))
        {
            return "from a file";
        }

        return string.IsNullOrWhiteSpace(options.Smtp.Password) ? "not set" : "inline in the configuration";
    }
}
