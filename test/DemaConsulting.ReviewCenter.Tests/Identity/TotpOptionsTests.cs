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
using DemaConsulting.ReviewCenter.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Identity;

/// <summary>
///     Tests of the <c>Identity:Totp</c> settings: the validator, the defaults and the start-up check.
/// </summary>
public class TotpOptionsTests
{
    /// <summary>
    ///     Test that the defaults are on and the product name as issuer.
    /// </summary>
    [Fact]
    public void TotpOptions_Defaults_AreEnabledWithTheProductIssuer()
    {
        // Arrange and Act: a fresh settings object
        var options = new TotpOptions();

        // Assert: the documented defaults
        Assert.True(options.Enabled);
        Assert.Equal("ReviewCenter", options.Issuer);
        Assert.Equal("ReviewCenter", TotpSettings.DefaultIssuer);
    }

    /// <summary>
    ///     Test that valid issuers pass the validator.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    [Theory]
    [InlineData("ReviewCenter")]
    [InlineData("Acme Review Center")]
    [InlineData("A")]
    public void TotpOptionsValidator_Validate_ValidIssuer_Succeeds(string issuer)
    {
        // Arrange: the validator and options with the issuer
        var validator = new TotpOptionsValidator();

        // Act: validate
        var result = validator.Validate(null, new TotpOptions { Issuer = issuer });

        // Assert: accepted
        Assert.True(result.Succeeded);
    }

    /// <summary>
    ///     Test that invalid issuers are refused with a message that names the setting and does not echo the value.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a:b")]
    [InlineData("bad\nissuer")]
    public void TotpOptionsValidator_Validate_InvalidIssuer_FailsAndNamesTheSetting(string issuer)
    {
        // Arrange: the validator
        var validator = new TotpOptionsValidator();

        // Act: validate
        var result = validator.Validate(null, new TotpOptions { Issuer = issuer });

        // Assert: refused, with the setting name and no value
        Assert.True(result.Failed);
        Assert.Contains("Identity:Totp:Issuer", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("bad", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a too long issuer is refused.
    /// </summary>
    [Fact]
    public void TotpOptionsValidator_Validate_TooLongIssuer_Fails()
    {
        // Arrange: an issuer one character over the limit
        var validator = new TotpOptionsValidator();

        // Act: validate
        var result = validator.Validate(null, new TotpOptions { Issuer = new string('x', TotpOptions.MaxIssuerLength + 1) });

        // Assert: refused
        Assert.True(result.Failed);
    }

    /// <summary>
    ///     Test that an invalid setting stops the start-up with a clear message.
    /// </summary>
    /// <param name="name">The setting name below <c>Identity:Totp</c>.</param>
    /// <param name="value">The invalid value.</param>
    [Theory]
    [InlineData("Enabled", "maybe")]
    [InlineData("Issuer", "")]
    [InlineData("Issuer", "a:b")]
    public void ServerHost_CreateApplication_InvalidTotpSettings_ThrowsClearError(string name, string value)
    {
        // Arrange: configuration with one invalid second factor value
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
                    [$"Identity:Totp:{name}"] = value
                });
            }
        };

        // Act: create the application
        var exception = Assert.Throws<InvalidOperationException>(() => ServerHost.CreateApplication(context, options));

        // Assert: the message is the two-factor settings error and names the setting
        Assert.Contains("Invalid two-factor sign-in settings", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"Identity:Totp:{name}", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the default, a switched-off and a customized configuration all build the application and give the
    ///     settings the service uses.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_ValidTotpSettings_ProvideTheSettings()
    {
        // Arrange: three configurations
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

        // Act: build each
        using var defaults = ServerHost.CreateApplication(context, Options([]));
        using var off = ServerHost.CreateApplication(
            context, Options(new() { ["Identity:Totp:Enabled"] = "false", ["Identity:Totp:Issuer"] = "  Acme  " }));

        // Assert: the defaults are on with the product issuer; the other is off with the trimmed issuer
        var defaultSettings = defaults.Services.GetRequiredService<TotpSettings>();
        Assert.True(defaultSettings.Enabled);
        Assert.Equal("ReviewCenter", defaultSettings.Issuer);
        var offSettings = off.Services.GetRequiredService<TotpSettings>();
        Assert.False(offSettings.Enabled);
        Assert.Equal("Acme", offSettings.Issuer);
    }
}
