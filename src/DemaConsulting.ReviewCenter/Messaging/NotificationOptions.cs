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

using System.Net;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Messaging;

/// <summary>
///     The kind of transport that delivers e-mail.
/// </summary>
internal enum NotificationSenderKind
{
    /// <summary>No transport: nothing is sent.</summary>
    None,

    /// <summary>A mail server reached over SMTP.</summary>
    Smtp,

    /// <summary>One file per message in a folder, for demonstrations, development and tests.</summary>
    File
}

/// <summary>
///     How the connection to the SMTP server is protected.
/// </summary>
internal enum SmtpTlsMode
{
    /// <summary>The conversation is not encrypted; accepted without a warning only for a server on this machine.</summary>
    None,

    /// <summary>The conversation starts in clear text and is switched to TLS with a command of the protocol.</summary>
    StartTls,

    /// <summary>The conversation is TLS from the first byte.</summary>
    Implicit
}

/// <summary>
///     The settings of the SMTP server, read from the <c>Notifications:Smtp</c> section.
/// </summary>
/// <remarks>
///     The password is given inline (<see cref="Password"/>) or through a file (<see cref="PasswordFile"/>), never
///     both. The type is a plain settings holder that is not thread-safe while it is being bound and it prints
///     nothing: the password never reaches a log, a message or a page.
/// </remarks>
internal sealed class SmtpNotificationOptions
{
    /// <summary>
    ///     Gets or sets the name or address of the SMTP server.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the port of the SMTP server; 587 is the port of the submission service.
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    ///     Gets or sets how the connection is protected.
    /// </summary>
    public SmtpTlsMode Tls { get; set; } = SmtpTlsMode.StartTls;

    /// <summary>
    ///     Gets or sets the user name for the SMTP server; empty when the server needs no sign-in.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the password inline. Prefer <see cref="PasswordFile"/>; setting both is refused.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the path of a file that holds the password instead of <see cref="Password"/>; see
    ///     <see cref="SecretSource"/>.
    /// </summary>
    public string PasswordFile { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets how long, in seconds, the server may take to answer before the send fails.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
///     The settings of the file transport, read from the <c>Notifications:File</c> section.
/// </summary>
/// <remarks>The type is a plain settings holder that is not thread-safe while it is being bound.</remarks>
internal sealed class FileNotificationOptions
{
    /// <summary>
    ///     Gets or sets the folder that receives one <c>.eml</c> file per message; it is created when needed.
    /// </summary>
    public string Directory { get; set; } = string.Empty;
}

/// <summary>
///     The settings of e-mail notifications, read from the <c>Notifications</c> section.
/// </summary>
/// <remarks>
///     E-mail notifications are off unless <see cref="Enabled"/> is set; while they are off nothing else is read,
///     checked or registered. The settings are bound by the options system, so they can come from
///     <c>appsettings.json</c> or from environment variables such as <c>Notifications__Smtp__Host</c>. Every value
///     is bounded and checked at start-up by <see cref="NotificationOptionsValidator"/>. The settings of the
///     schedule (<see cref="DigestHour"/>, <see cref="TimeZone"/>, <see cref="BatchWindowSeconds"/>,
///     <see cref="PollSeconds"/>, <see cref="IncludeTitles"/> and <see cref="BaseUrl"/>) are validated
///     here and drive the notification worker (see <see cref="NotificationWorker"/>). The type is a plain settings holder
///     that is not thread-safe while it is being bound, and it prints nothing: the password never reaches a log, a
///     message or a page.
/// </remarks>
internal sealed class NotificationOptions
{
    /// <summary>
    ///     The name of the configuration section that holds <see cref="NotificationOptions"/>.
    /// </summary>
    public const string SectionName = "Notifications";

    /// <summary>
    ///     The longest accepted <see cref="FromName"/>.
    /// </summary>
    public const int MaxFromNameLength = 80;

    /// <summary>
    ///     The longest accepted <see cref="BaseUrl"/>.
    /// </summary>
    public const int MaxBaseUrlLength = 512;

    /// <summary>
    ///     The longest accepted SMTP host, user name and file path.
    /// </summary>
    public const int MaxTextLength = 512;

    /// <summary>
    ///     The longest accepted password.
    /// </summary>
    public const int MaxPasswordLength = 512;

    /// <summary>
    ///     Gets or sets a value indicating whether e-mail notifications are switched on.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     Gets or sets the transport; required when <see cref="Enabled"/> is set.
    /// </summary>
    public NotificationSenderKind Sender { get; set; } = NotificationSenderKind.None;

    /// <summary>
    ///     Gets or sets the address of the installation that links in mails start with, for example
    ///     <c>https://reviews.example.com</c>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the address mails come from.
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the name of the installation as it appears in mails and in the sender name.
    /// </summary>
    public string FromName { get; set; } = NotificationSettings.DefaultInstanceName;

    /// <summary>
    ///     Gets or sets the settings of the SMTP transport.
    /// </summary>
    public SmtpNotificationOptions Smtp { get; set; } = new();

    /// <summary>
    ///     Gets or sets the settings of the file transport.
    /// </summary>
    public FileNotificationOptions File { get; set; } = new();

    /// <summary>
    ///     Gets or sets the local hour (0 to 23) at which the daily summary is sent.
    /// </summary>
    public int DigestHour { get; set; } = 8;

    /// <summary>
    ///     Gets or sets the identifier of the time zone of <see cref="DigestHour"/>, for example <c>UTC</c> or
    ///     <c>Europe/Berlin</c>; it must be known to the operating system.
    /// </summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>
    ///     Gets or sets how many seconds changes are collected before one mail is sent.
    /// </summary>
    public int BatchWindowSeconds { get; set; } = 300;

    /// <summary>
    ///     Gets or sets how often, in seconds, the server looks for changes to mail.
    /// </summary>
    public int PollSeconds { get; set; } = 60;

    /// <summary>
    ///     Gets or sets a value indicating whether the titles of reviews may appear in mails.
    /// </summary>
    public bool IncludeTitles { get; set; } = true;

    /// <summary>
    ///     Gets or sets the most mails the server sends in one hour, over all users.
    /// </summary>
    public int MaxMailsPerHour { get; set; } = NotificationSettings.DefaultMaxMailsPerHour;

    /// <summary>
    ///     Resolves the SMTP password from <see cref="SmtpNotificationOptions.Password"/> or from the file named by
    ///     <see cref="SmtpNotificationOptions.PasswordFile"/>.
    /// </summary>
    /// <returns>The password; <c>null</c> when neither setting is set.</returns>
    /// <remarks>
    ///     The file is read at each call, so a rotated password is picked up without a restart. The length and
    ///     control-character bounds are checked here, so a file that changes after the start-up check can not slip
    ///     past them. The password is never logged or put in a message.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when both settings are set, when the file can not be used (see <see cref="SecretSource"/>), or
    ///     when the password is longer than <see cref="MaxPasswordLength"/> characters or holds a control
    ///     character; the message names the setting and never holds the password.
    /// </exception>
    internal string? ResolvePassword()
    {
        var useFile = !string.IsNullOrWhiteSpace(Smtp.PasswordFile);
        var password = SecretSource.Resolve(
            Smtp.Password,
            $"{SectionName}:Smtp:{nameof(SmtpNotificationOptions.Password)}",
            Smtp.PasswordFile,
            $"{SectionName}:Smtp:{nameof(SmtpNotificationOptions.PasswordFile)}");
        if (password is not null && (password.Length > MaxPasswordLength || password.Any(char.IsControl)))
        {
            var setting = useFile ? nameof(SmtpNotificationOptions.PasswordFile) : nameof(SmtpNotificationOptions.Password);
            throw new InvalidOperationException(
                $"{SectionName}:Smtp:{setting} must be at most {MaxPasswordLength} characters without control characters.");
        }

        return password;
    }

    /// <summary>
    ///     Lists the settings that work but that an operator should reconsider.
    /// </summary>
    /// <returns>
    ///     One sentence per warning; empty while notifications are off or nothing is doubtful. No sentence holds a
    ///     value.
    /// </returns>
    public IReadOnlyList<string> GetWarnings()
    {
        var warnings = new List<string>();
        if (!Enabled || Sender != NotificationSenderKind.Smtp)
        {
            return warnings;
        }

        // A password that is written into a configuration file or the environment is easier to leak than a file
        // that only the server can read
        if (!string.IsNullOrWhiteSpace(Smtp.Password))
        {
            warnings.Add(
                $"{SectionName}:Smtp:Password holds the password directly; prefer {SectionName}:Smtp:PasswordFile, " +
                "a file that only the server can read.");
        }

        // Mail and credentials would cross the network in clear text
        if (Smtp.Tls == SmtpTlsMode.None && !IsLoopbackHost(Smtp.Host))
        {
            warnings.Add(
                $"{SectionName}:Smtp:Tls is None for a server that is not on this machine; " +
                "the mail travels unencrypted.");
        }

        return warnings;
    }

    /// <summary>
    ///     Tells whether a host name or address is the local machine.
    /// </summary>
    /// <param name="host">The host name or IP address.</param>
    /// <returns><c>true</c> for <c>localhost</c> and for a loopback IP address.</returns>
    internal static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var text = host.Trim().Trim('[', ']');
        return text.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || (IPAddress.TryParse(text, out var address) && IPAddress.IsLoopback(address));
    }
}

/// <summary>
///     Checks the <c>Notifications</c> settings when they are first used, at start-up.
/// </summary>
/// <remarks>
///     Nothing is checked while <see cref="NotificationOptions.Enabled"/> is off. The messages name the setting and
///     the rule and never contain a value, so the password cannot reach a log through them. Only the first problem
///     is reported. The class holds no state and is thread-safe.
/// </remarks>
internal sealed class NotificationOptionsValidator : IValidateOptions<NotificationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, NotificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problem = options.Enabled ? Check(options) : null;
        return problem is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problem);
    }

    /// <summary>
    ///     Finds the first problem of enabled settings.
    /// </summary>
    /// <param name="options">The settings.</param>
    /// <returns>The message of the first problem; <c>null</c> when the settings are usable.</returns>
    private static string? Check(NotificationOptions options)
    {
        const string section = NotificationOptions.SectionName;
        return CheckSender(options)
               ?? CheckFromAddress(options.FromAddress)
               ?? CheckText(options.FromName, $"{section}:FromName", NotificationOptions.MaxFromNameLength)
               ?? CheckBaseUrl(options.BaseUrl)
               ?? (options.Sender == NotificationSenderKind.Smtp ? CheckSmtp(options) : CheckFile(options.File))
               ?? CheckRange(options.DigestHour, $"{section}:DigestHour", 0, 23)
               ?? CheckTimeZone(options.TimeZone)
               ?? CheckRange(options.BatchWindowSeconds, $"{section}:BatchWindowSeconds", 30, 3600)
               ?? CheckRange(options.PollSeconds, $"{section}:PollSeconds", 10, 3600)
               ?? CheckRange(options.MaxMailsPerHour, $"{section}:MaxMailsPerHour", 1, 10_000);
    }

    /// <summary>
    ///     Checks the kind of transport.
    /// </summary>
    /// <param name="options">The settings.</param>
    /// <returns>The message of the problem; <c>null</c> when the kind is usable.</returns>
    private static string? CheckSender(NotificationOptions options)
    {
        const string key = $"{NotificationOptions.SectionName}:Sender";
        return options.Sender is NotificationSenderKind.Smtp or NotificationSenderKind.File
            ? null
            : $"{key} must be Smtp or File when e-mail notifications are enabled.";
    }

    /// <summary>
    ///     Checks the sender address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>The message of the problem; <c>null</c> when the address is usable.</returns>
    private static string? CheckFromAddress(string? address)
    {
        const string key = $"{NotificationOptions.SectionName}:FromAddress";
        if (string.IsNullOrWhiteSpace(address))
        {
            return $"{key} is required when e-mail notifications are enabled.";
        }

        return NotificationAddress.IsValid(address) ? null : $"{key} must be exactly one valid e-mail address.";
    }

    /// <summary>
    ///     Checks the address of the installation.
    /// </summary>
    /// <param name="baseUrl">The address.</param>
    /// <returns>The message of the problem; <c>null</c> when the address is usable.</returns>
    private static string? CheckBaseUrl(string? baseUrl)
    {
        const string key = $"{NotificationOptions.SectionName}:BaseUrl";
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return $"{key} is required when e-mail notifications are enabled.";
        }

        if (baseUrl.Length > NotificationOptions.MaxBaseUrlLength
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.UserInfo.Length > 0)
        {
            return $"{key} must be an absolute address of at most {NotificationOptions.MaxBaseUrlLength} " +
                   "characters without user, query or fragment.";
        }

        // Links in mails must not lead through an unencrypted connection, except to this machine
        var secure = uri.Scheme == Uri.UriSchemeHttps;
        var local = uri.Scheme == Uri.UriSchemeHttp && NotificationOptions.IsLoopbackHost(uri.Host);
        return secure || local
            ? null
            : $"{key} must use https (http is accepted only for localhost or another loopback address).";
    }

    /// <summary>
    ///     Checks the settings of the SMTP transport.
    /// </summary>
    /// <param name="options">The settings.</param>
    /// <returns>The message of the first problem; <c>null</c> when the settings are usable.</returns>
    private static string? CheckSmtp(NotificationOptions options)
    {
        const string section = $"{NotificationOptions.SectionName}:Smtp";
        var smtp = options.Smtp;
        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            return $"{section}:Host is required when the SMTP transport is used.";
        }

        if (smtp.Host.Length > NotificationOptions.MaxTextLength
            || smtp.Host.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            return $"{section}:Host must be a host name or address without spaces or control characters.";
        }

        if (smtp.Port is < 1 or > 65535)
        {
            return $"{section}:Port must be between 1 and 65535.";
        }

        if (!Enum.IsDefined(smtp.Tls))
        {
            return $"{section}:Tls must be None, StartTls or Implicit.";
        }

        if (smtp.TimeoutSeconds is < 5 or > 120)
        {
            return $"{section}:TimeoutSeconds must be between 5 and 120 seconds.";
        }

        return CheckCredentials(options);
    }

    /// <summary>
    ///     Checks the user name and the password of the SMTP transport.
    /// </summary>
    /// <param name="options">The settings.</param>
    /// <returns>The message of the first problem; <c>null</c> when the credentials are usable.</returns>
    /// <remarks>
    ///     Both settings at once, a file that is missing, unreadable, empty or too large, and a password that is
    ///     too long or has a control character are all reported by
    ///     <see cref="NotificationOptions.ResolvePassword"/>, the one resolver the sender also uses. No message
    ///     holds the password.
    /// </remarks>
    private static string? CheckCredentials(NotificationOptions options)
    {
        const string section = $"{NotificationOptions.SectionName}:Smtp";
        var smtp = options.Smtp;
        var userProblem = CheckText(smtp.Username, $"{section}:Username", NotificationOptions.MaxTextLength);
        if (userProblem is not null)
        {
            return userProblem;
        }

        string? password;
        try
        {
            password = options.ResolvePassword();
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }

        // A user name needs a password and a password needs a user name: half a login can never work
        var hasUser = !string.IsNullOrWhiteSpace(smtp.Username);
        if (hasUser && password is null)
        {
            return $"{section}:Password or {section}:PasswordFile is required when {section}:Username is set.";
        }

        if (!hasUser && password is not null)
        {
            return $"{section}:Username is required when a password is set.";
        }

        // Credentials must never cross the network in clear text
        return hasUser && smtp.Tls == SmtpTlsMode.None && !NotificationOptions.IsLoopbackHost(smtp.Host)
            ? $"{section}:Tls must be StartTls or Implicit when {section}:Username is set for another machine."
            : null;
    }

    /// <summary>
    ///     Checks the settings of the file transport.
    /// </summary>
    /// <param name="file">The settings.</param>
    /// <returns>The message of the problem; <c>null</c> when the settings are usable.</returns>
    private static string? CheckFile(FileNotificationOptions file)
    {
        const string key = $"{NotificationOptions.SectionName}:File:Directory";
        return string.IsNullOrWhiteSpace(file.Directory)
            ? $"{key} is required when the file transport is used."
            : CheckText(file.Directory, key, NotificationOptions.MaxTextLength);
    }

    /// <summary>
    ///     Checks a text setting for its length and for control characters; an empty text passes.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="key">The full name of the setting.</param>
    /// <param name="maxLength">The longest accepted length.</param>
    /// <returns>The message of the problem; <c>null</c> when the value is usable.</returns>
    private static string? CheckText(string? value, string key, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length > maxLength || value.Any(char.IsControl)
            ? $"{key} must be at most {maxLength} characters without control characters."
            : null;
    }

    /// <summary>
    ///     Checks a number setting.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="key">The full name of the setting.</param>
    /// <param name="minimum">The smallest accepted value.</param>
    /// <param name="maximum">The largest accepted value.</param>
    /// <returns>The message of the problem; <c>null</c> when the value is in range.</returns>
    private static string? CheckRange(int value, string key, int minimum, int maximum)
    {
        return value < minimum || value > maximum ? $"{key} must be between {minimum} and {maximum}." : null;
    }

    /// <summary>
    ///     Checks the time zone.
    /// </summary>
    /// <param name="timeZone">The identifier.</param>
    /// <returns>The message of the problem; <c>null</c> when the operating system knows the zone.</returns>
    private static string? CheckTimeZone(string? timeZone)
    {
        const string key = $"{NotificationOptions.SectionName}:TimeZone";
        if (string.IsNullOrWhiteSpace(timeZone))
        {
            return $"{key} is required when e-mail notifications are enabled.";
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZone.Trim());
            return null;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return $"{key} is not a time zone that this system knows.";
        }
    }
}
