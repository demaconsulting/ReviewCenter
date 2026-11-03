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

using DemaConsulting.ReviewCenter.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Tests.Messaging;

/// <summary>
///     Unit tests for <see cref="NotificationOptions"/> and <see cref="NotificationOptionsValidator"/>: the
///     defaults, the binding, the start-up checks and the warnings. No message may contain a configured value.
/// </summary>
public class NotificationOptionsTests
{
    /// <summary>
    ///     A password the tests use; no message may contain it.
    /// </summary>
    private const string SecretPassword = "s3cr3t-Smtp-Pa55word";

    /// <summary>
    ///     Test that notifications are off by default and that the other defaults are the documented ones.
    /// </summary>
    [Fact]
    public void NotificationOptions_Defaults_AreOffAndDocumented()
    {
        // Arrange and Act: the defaults
        var options = new NotificationOptions();

        // Assert: off, no transport, and the documented schedule and limits
        Assert.False(options.Enabled);
        Assert.Equal(NotificationSenderKind.None, options.Sender);
        Assert.Equal(8, options.DigestHour);
        Assert.Equal("UTC", options.TimeZone);
        Assert.Equal(300, options.BatchWindowSeconds);
        Assert.Equal(60, options.PollSeconds);
        Assert.True(options.IncludeTitles);
        Assert.Equal(587, options.Smtp.Port);
        Assert.Equal(SmtpTlsMode.StartTls, options.Smtp.Tls);
    }

    /// <summary>
    ///     Test that the settings bind from configuration keys such as environment variables provide.
    /// </summary>
    [Fact]
    public void NotificationOptions_Bind_ReadsAllSections()
    {
        // Arrange: configuration values for both transports
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Enabled"] = "true",
                ["Notifications:Sender"] = "Smtp",
                ["Notifications:Smtp:Host"] = "mail.example.com",
                ["Notifications:Smtp:Port"] = "465",
                ["Notifications:Smtp:Tls"] = "Implicit",
                ["Notifications:File:Directory"] = "mails",
                ["Notifications:DigestHour"] = "6"
            })
            .Build();

        // Act: bind
        var options = configuration.GetSection(NotificationOptions.SectionName).Get<NotificationOptions>()!;

        // Assert: the values arrived
        Assert.True(options.Enabled);
        Assert.Equal(NotificationSenderKind.Smtp, options.Sender);
        Assert.Equal("mail.example.com", options.Smtp.Host);
        Assert.Equal(465, options.Smtp.Port);
        Assert.Equal(SmtpTlsMode.Implicit, options.Smtp.Tls);
        Assert.Equal("mails", options.File.Directory);
        Assert.Equal(6, options.DigestHour);
    }

    /// <summary>
    ///     Test that nothing is checked while notifications are off.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_Disabled_AcceptsAnything()
    {
        // Arrange: off, with nonsense around it
        var options = new NotificationOptions { Enabled = false, Sender = (NotificationSenderKind)99, DigestHour = 99 };

        // Act and Assert: accepted
        Assert.True(Validate(options).Succeeded);
    }

    /// <summary>
    ///     Test that complete SMTP settings are accepted.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_CompleteSmtpSettings_AreAccepted()
    {
        // Arrange and Act: complete settings
        var result = Validate(SmtpOptions());

        // Assert: accepted
        Assert.True(result.Succeeded, result.FailureMessage);
    }

    /// <summary>
    ///     Test that complete file settings are accepted.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_CompleteFileSettings_AreAccepted()
    {
        // Arrange and Act: complete settings
        var result = Validate(FileOptions());

        // Assert: accepted
        Assert.True(result.Succeeded, result.FailureMessage);
    }

    /// <summary>
    ///     Test that enabling without a transport is refused.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_NoSender_IsRejected()
    {
        // Arrange: enabled but no transport
        var options = FileOptions();
        options.Sender = NotificationSenderKind.None;

        // Act and Assert: the message names the setting
        AssertRejected(options, "Notifications:Sender");
    }

    /// <summary>
    ///     Test that a missing sender address is refused.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_MissingFromAddress_IsRejected()
    {
        // Arrange: no sender address
        var options = FileOptions();
        options.FromAddress = string.Empty;

        // Act and Assert: refused
        AssertRejected(options, "Notifications:FromAddress");
    }

    /// <summary>
    ///     Test that a sender address with a header injection is refused.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_FromAddressWithLineBreak_IsRejected()
    {
        // Arrange: an address that tries to add a header line
        var options = FileOptions();
        options.FromAddress = "reviews@example.com\r\nBcc: eve@example.org";

        // Act and Assert: refused
        AssertRejected(options, "Notifications:FromAddress");
    }

    /// <summary>
    ///     Test that a sender name with a control character is refused.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_FromNameWithLineBreak_IsRejected()
    {
        // Arrange: a name with a line break
        var options = FileOptions();
        options.FromName = "Review\r\nCenter";

        // Act and Assert: refused
        AssertRejected(options, "Notifications:FromName");
    }

    /// <summary>
    ///     Test that a missing base address is refused.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_MissingBaseUrl_IsRejected()
    {
        // Arrange: no base address
        var options = FileOptions();
        options.BaseUrl = string.Empty;

        // Act and Assert: refused
        AssertRejected(options, "Notifications:BaseUrl");
    }

    /// <summary>
    ///     Test that a base address that is not absolute is refused.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_RelativeBaseUrl_IsRejected()
    {
        // Arrange: a relative address
        var options = FileOptions();
        options.BaseUrl = "/reviews";

        // Act and Assert: refused
        AssertRejected(options, "Notifications:BaseUrl");
    }

    /// <summary>
    ///     Test that a base address with a query, a fragment or user information is refused.
    /// </summary>
    [Theory]
    [InlineData("https://reviews.example.com/?x=1")]
    [InlineData("https://user:pw@reviews.example.com/")]
    [InlineData("https://reviews.example.com/#top")]
    public void NotificationOptionsValidator_BaseUrlWithExtras_IsRejected(string baseUrl)
    {
        // Arrange: an address with an extra part
        var options = FileOptions();
        options.BaseUrl = baseUrl;

        // Act and Assert: refused, and the value is not echoed
        AssertRejected(options, "Notifications:BaseUrl");
    }

    /// <summary>
    ///     Test that an unencrypted base address is refused unless it is on this machine.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_HttpBaseUrlForAnotherHost_IsRejected()
    {
        // Arrange: http to another machine
        var options = FileOptions();
        options.BaseUrl = "http://reviews.example.com";

        // Act and Assert: refused
        AssertRejected(options, "Notifications:BaseUrl");
    }

    /// <summary>
    ///     Test that an unencrypted base address is accepted for the local machine.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_HttpBaseUrlForLoopback_IsAccepted()
    {
        // Arrange: http to localhost
        var options = FileOptions();
        options.BaseUrl = "http://localhost:5000";

        // Act and Assert: accepted
        Assert.True(Validate(options).Succeeded);
    }

    /// <summary>
    ///     Test that an SMTP transport without a host is refused.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_SmtpWithoutHost_IsRejected()
    {
        // Arrange: no host
        var options = SmtpOptions();
        options.Smtp.Host = string.Empty;

        // Act and Assert: refused
        AssertRejected(options, "Notifications:Smtp:Host");
    }

    /// <summary>
    ///     Test that a host with a space is refused.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_SmtpHostWithSpace_IsRejected()
    {
        // Arrange: a host with a space
        var options = SmtpOptions();
        options.Smtp.Host = "mail example.com";

        // Act and Assert: refused
        AssertRejected(options, "Notifications:Smtp:Host");
    }

    /// <summary>
    ///     Test that a port outside the valid range is refused.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void NotificationOptionsValidator_SmtpPortOutOfRange_IsRejected(int port)
    {
        // Arrange: a bad port
        var options = SmtpOptions();
        options.Smtp.Port = port;

        // Act and Assert: refused
        AssertRejected(options, "Notifications:Smtp:Port");
    }

    /// <summary>
    ///     Test that an unknown TLS mode is refused.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_UnknownTlsMode_IsRejected()
    {
        // Arrange: a mode that does not exist
        var options = SmtpOptions();
        options.Smtp.Tls = (SmtpTlsMode)42;

        // Act and Assert: refused
        AssertRejected(options, "Notifications:Smtp:Tls");
    }

    /// <summary>
    ///     Test that a time-out outside the valid range is refused.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(121)]
    public void NotificationOptionsValidator_SmtpTimeoutOutOfRange_IsRejected(int seconds)
    {
        // Arrange: a bad time-out
        var options = SmtpOptions();
        options.Smtp.TimeoutSeconds = seconds;

        // Act and Assert: refused
        AssertRejected(options, "Notifications:Smtp:TimeoutSeconds");
    }

    /// <summary>
    ///     Test that a user name without a password is refused.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_UsernameWithoutPassword_IsRejected()
    {
        // Arrange: half a login
        var options = SmtpOptions();
        options.Smtp.Username = "mailer";

        // Act and Assert: refused
        AssertRejected(options, "Notifications:Smtp:Password");
    }

    /// <summary>
    ///     Test that a password without a user name is refused.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_PasswordWithoutUsername_IsRejected()
    {
        // Arrange: half a login
        var options = SmtpOptions();
        options.Smtp.Password = SecretPassword;

        // Act and Assert: refused, and the password is not echoed
        AssertRejected(options, "Notifications:Smtp:Username");
    }

    /// <summary>
    ///     Test that an inline password together with a password file is refused.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_PasswordAndPasswordFile_IsRejected()
    {
        // Arrange: both ways to give the password
        var options = SmtpOptions();
        options.Smtp.Username = "mailer";
        options.Smtp.Password = SecretPassword;
        options.Smtp.PasswordFile = "password.txt";

        // Act and Assert: refused, and the password is not echoed
        AssertRejected(options, "Notifications:Smtp:Password");
    }

    /// <summary>
    ///     Test that a password file that does not exist is refused.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_MissingPasswordFile_IsRejected()
    {
        // Arrange: a file that is not there
        var options = SmtpOptions();
        options.Smtp.Username = "mailer";
        options.Smtp.PasswordFile = Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.txt");

        // Act and Assert: refused
        AssertRejected(options, "Notifications:Smtp:PasswordFile");
    }

    /// <summary>
    ///     Test that a password from a file is accepted and resolved with trailing line breaks removed.
    /// </summary>
    [Fact]
    public async Task NotificationOptions_ResolvePassword_FromFile_ReturnsTrimmedPassword()
    {
        // Arrange: a password file with a trailing line break
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "smtp-password.txt");
        await File.WriteAllTextAsync(path, SecretPassword + "\n", TestContext.Current.CancellationToken);
        var options = SmtpOptions();
        options.Smtp.Username = "mailer";
        options.Smtp.PasswordFile = path;

        // Act: validate and resolve
        var result = Validate(options);
        var password = options.ResolvePassword();

        // Assert: accepted, and the password is what the file holds
        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Equal(SecretPassword, password);
    }

    /// <summary>
    ///     Test that a password that is too long or holds a control character is refused without echoing it.
    /// </summary>
    /// <param name="fromFile">Whether the password comes from the password file.</param>
    /// <param name="tooLong">Whether the password is too long; otherwise it holds a control character.</param>
    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task NotificationOptions_ResolvePassword_TooLongOrControlCharacter_Throws(bool fromFile, bool tooLong)
    {
        // Arrange: a bad password, inline or in a file
        using var folder = new TempFolder();
        var bad = tooLong
            ? new string('x', NotificationOptions.MaxPasswordLength + 1)
            : "pass\u0007word";
        var options = SmtpOptions();
        options.Smtp.Username = "mailer";
        var setting = "Notifications:Smtp:Password";
        if (fromFile)
        {
            var path = Path.Combine(folder.Path, "smtp-password.txt");
            await File.WriteAllTextAsync(path, bad, TestContext.Current.CancellationToken);
            options.Smtp.PasswordFile = path;
            setting += "File";
        }
        else
        {
            options.Smtp.Password = bad;
        }

        // Act: resolve the password
        var exception = Assert.Throws<InvalidOperationException>(() => options.ResolvePassword());

        // Assert: the message names the setting and never holds the password
        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(bad, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that credentials over an unencrypted connection to another machine are refused.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_CredentialsWithoutTlsToRemoteHost_AreRejected()
    {
        // Arrange: a login without TLS to another machine
        var options = SmtpOptions();
        options.Smtp.Username = "mailer";
        options.Smtp.Password = SecretPassword;
        options.Smtp.Tls = SmtpTlsMode.None;

        // Act and Assert: refused
        AssertRejected(options, "Notifications:Smtp:Tls");
    }

    /// <summary>
    ///     Test that credentials over an unencrypted connection to this machine are accepted.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_CredentialsWithoutTlsToLoopback_AreAccepted()
    {
        // Arrange: a login without TLS to this machine
        var options = SmtpOptions();
        options.Smtp.Host = "127.0.0.1";
        options.Smtp.Username = "mailer";
        options.Smtp.Password = SecretPassword;
        options.Smtp.Tls = SmtpTlsMode.None;

        // Act and Assert: accepted
        Assert.True(Validate(options).Succeeded);
    }

    /// <summary>
    ///     Test that a file transport without a folder is refused.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_FileWithoutDirectory_IsRejected()
    {
        // Arrange: no folder
        var options = FileOptions();
        options.File.Directory = string.Empty;

        // Act and Assert: refused
        AssertRejected(options, "Notifications:File:Directory");
    }

    /// <summary>
    ///     Test that a digest hour outside 0 to 23 is refused.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void NotificationOptionsValidator_DigestHourOutOfRange_IsRejected(int hour)
    {
        // Arrange: a bad hour
        var options = FileOptions();
        options.DigestHour = hour;

        // Act and Assert: refused
        AssertRejected(options, "Notifications:DigestHour");
    }

    /// <summary>
    ///     Test that an unknown time zone is refused.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_UnknownTimeZone_IsRejected()
    {
        // Arrange: a zone that does not exist
        var options = FileOptions();
        options.TimeZone = "Mars/Olympus_Mons";

        // Act and Assert: refused
        AssertRejected(options, "Notifications:TimeZone");
    }

    /// <summary>
    ///     Test that a batch window outside its bounds is refused.
    /// </summary>
    [Theory]
    [InlineData(29)]
    [InlineData(3601)]
    public void NotificationOptionsValidator_BatchWindowOutOfRange_IsRejected(int seconds)
    {
        // Arrange: a bad window
        var options = FileOptions();
        options.BatchWindowSeconds = seconds;

        // Act and Assert: refused
        AssertRejected(options, "Notifications:BatchWindowSeconds");
    }

    /// <summary>
    ///     Test that a poll interval outside its bounds is refused.
    /// </summary>
    [Theory]
    [InlineData(9)]
    [InlineData(3601)]
    public void NotificationOptionsValidator_PollSecondsOutOfRange_IsRejected(int seconds)
    {
        // Arrange: a bad interval
        var options = FileOptions();
        options.PollSeconds = seconds;

        // Act and Assert: refused
        AssertRejected(options, "Notifications:PollSeconds");
    }

    /// <summary>
    ///     Test that a mail limit outside its bounds is refused.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(10_001)]
    public void NotificationOptionsValidator_MaxMailsPerHourOutOfRange_IsRejected(int limit)
    {
        // Arrange: a bad limit
        var options = FileOptions();
        options.MaxMailsPerHour = limit;

        // Act and Assert: refused
        AssertRejected(options, "Notifications:MaxMailsPerHour");
    }

    /// <summary>
    ///     Test that an inline password is reported as a warning that does not hold the password.
    /// </summary>
    [Fact]
    public void NotificationOptions_GetWarnings_InlinePassword_WarnsWithoutValue()
    {
        // Arrange: an inline password
        var options = SmtpOptions();
        options.Smtp.Username = "mailer";
        options.Smtp.Password = SecretPassword;

        // Act: collect the warnings
        var warnings = options.GetWarnings();

        // Assert: one warning that names the setting and advises the file
        var warning = Assert.Single(warnings);
        Assert.Contains("Notifications:Smtp:Password", warning, StringComparison.Ordinal);
        Assert.Contains("PasswordFile", warning, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretPassword, warning, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that no TLS to another machine is reported as a warning.
    /// </summary>
    [Fact]
    public void NotificationOptions_GetWarnings_NoTlsToRemoteHost_Warns()
    {
        // Arrange: no TLS and no login to another machine
        var options = SmtpOptions();
        options.Smtp.Tls = SmtpTlsMode.None;

        // Act: collect the warnings
        var warnings = options.GetWarnings();

        // Assert: the warning names the setting
        var warning = Assert.Single(warnings);
        Assert.Contains("Notifications:Smtp:Tls", warning, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that good settings and a switched-off feature give no warning.
    /// </summary>
    [Fact]
    public void NotificationOptions_GetWarnings_GoodOrDisabled_IsEmpty()
    {
        // Arrange: good settings, and the same inline password but switched off
        var good = SmtpOptions();
        var off = SmtpOptions();
        off.Smtp.Password = SecretPassword;
        off.Enabled = false;

        // Act and Assert: no warnings
        Assert.Empty(good.GetWarnings());
        Assert.Empty(off.GetWarnings());
    }

    /// <summary>
    ///     Test that no rejection message contains the configured password, even when other settings are wrong.
    /// </summary>
    [Fact]
    public void NotificationOptionsValidator_Messages_NeverContainThePassword()
    {
        // Arrange: a password together with a wrong port
        var options = SmtpOptions();
        options.Smtp.Username = "mailer";
        options.Smtp.Password = SecretPassword;
        options.Smtp.Port = 0;

        // Act: validate
        var result = Validate(options);

        // Assert: refused for the port, without the password
        Assert.True(result.Failed);
        Assert.DoesNotContain(SecretPassword, result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Validates the settings the way the options system does.
    /// </summary>
    /// <param name="options">The settings.</param>
    /// <returns>The result.</returns>
    private static ValidateOptionsResult Validate(NotificationOptions options)
    {
        return new NotificationOptionsValidator().Validate(null, options);
    }

    /// <summary>
    ///     Asserts that the settings are refused and that the message names a setting.
    /// </summary>
    /// <param name="options">The settings.</param>
    /// <param name="setting">The full name of the setting the message must name.</param>
    private static void AssertRejected(NotificationOptions options, string setting)
    {
        var result = Validate(options);
        Assert.True(result.Failed);
        Assert.Contains(setting, result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretPassword, result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Creates complete enabled settings for the SMTP transport.
    /// </summary>
    /// <returns>The settings.</returns>
    private static NotificationOptions SmtpOptions()
    {
        return new NotificationOptions
        {
            Enabled = true,
            Sender = NotificationSenderKind.Smtp,
            FromAddress = "reviews@example.com",
            BaseUrl = "https://reviews.example.com",
            Smtp = new SmtpNotificationOptions { Host = "mail.example.com" }
        };
    }

    /// <summary>
    ///     Creates complete enabled settings for the file transport.
    /// </summary>
    /// <returns>The settings.</returns>
    private static NotificationOptions FileOptions()
    {
        return new NotificationOptions
        {
            Enabled = true,
            Sender = NotificationSenderKind.File,
            FromAddress = "reviews@example.com",
            BaseUrl = "https://reviews.example.com",
            File = new FileNotificationOptions { Directory = "mails" }
        };
    }

    /// <summary>
    ///     A temporary folder that is deleted with the object.
    /// </summary>
    private sealed class TempFolder : IDisposable
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="TempFolder"/> class and creates the folder.
        /// </summary>
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rc-notify-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        /// <summary>
        ///     Gets the path of the folder.
        /// </summary>
        public string Path { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
