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

using DemaConsulting.ReviewCenter.Identity;

namespace DemaConsulting.ReviewCenter.Tests.Identity;

/// <summary>
///     Unit tests for <see cref="OidcOptions"/> and <see cref="OidcOptionsValidator"/>: the bounds that stop a start-up
///     with invalid settings of sign-in with an OpenID Connect provider.
/// </summary>
public class OidcOptionsTests
{
    /// <summary>
    ///     A secret that must never appear in a message.
    /// </summary>
    private const string Secret = "s3cr3t-value";

    /// <summary>
    ///     Creates settings that are enabled and valid.
    /// </summary>
    /// <returns>The settings.</returns>
    private static OidcOptions Valid()
    {
        return new OidcOptions
        {
            Enabled = true,
            Authority = "https://idp.example.com/realm",
            ClientId = "reviewcenter",
            ClientSecret = Secret
        };
    }

    /// <summary>
    ///     Validates settings the way the options system does.
    /// </summary>
    /// <param name="options">The settings.</param>
    /// <returns>The result of the validation.</returns>
    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(OidcOptions options)
    {
        return new OidcOptionsValidator().Validate(null, options);
    }

    /// <summary>
    ///     Test that valid settings, the defaults of the optional ones, and settings at their limits are accepted.
    /// </summary>
    [Fact]
    public void OidcOptionsValidator_Validate_ValidSettings_Succeeds()
    {
        // Arrange: valid settings, and settings at every limit
        var atLimits = Valid();
        atLimits.ClientId = new string('c', OidcOptions.MaxClientIdLength);
        atLimits.ClientSecret = new string('s', OidcOptions.MaxClientSecretLength);
        atLimits.DisplayName = new string('d', OidcOptions.MaxDisplayNameLength);
        atLimits.UsernameClaim = new string('u', OidcOptions.MaxUsernameClaimLength);
        atLimits.Authority = "https://idp.example.com/" + new string('a', OidcOptions.MaxAuthorityLength - 24);
        atLimits.Scopes = "openid " + string.Join(' ', Enumerable.Repeat("scope", OidcOptions.MaxScopeCount - 1));
        atLimits.ReauthMaxAgeSeconds = OidcOptions.MaxReauthMaxAgeSeconds;

        // Act
        var plain = Validate(Valid());
        var limits = Validate(atLimits);

        // Assert
        Assert.True(plain.Succeeded);
        Assert.True(limits.Succeeded, limits.FailureMessage);
    }

    /// <summary>
    ///     Test that nothing is checked while sign-in with a provider is off.
    /// </summary>
    [Fact]
    public void OidcOptionsValidator_Validate_Disabled_SucceedsWhateverTheValues()
    {
        // Arrange: garbage in every setting
        var options = new OidcOptions { Authority = "garbage", ClientId = "a b", Scopes = "x", ReauthMaxAgeSeconds = 0 };

        // Act
        var result = Validate(options);

        // Assert
        Assert.True(result.Succeeded);
    }

    /// <summary>
    ///     Test that each invalid setting fails with a message that names it and holds no value.
    /// </summary>
    /// <param name="setting">The setting that is invalid.</param>
    /// <param name="value">The invalid value.</param>
    [Theory]
    [InlineData("Authority", "")]
    [InlineData("Authority", "   ")]
    [InlineData("Authority", "idp.example.com")]
    [InlineData("Authority", "ftp://idp.example.com/")]
    [InlineData("Authority", "http://idp.example.com/")]
    [InlineData("Authority", "https://idp.example.com/?a=b")]
    [InlineData("Authority", "https://idp.example.com/#frag")]
    [InlineData("Authority", "https://user:pw@idp.example.com/")]
    [InlineData("ClientId", "")]
    [InlineData("ClientId", "two words")]
    [InlineData("ClientId", "bell\u0007")]
    [InlineData("ClientSecret", "line\nbreak")]
    [InlineData("Scopes", "")]
    [InlineData("Scopes", "profile email")]
    [InlineData("Scopes", "openid caf\u00e9")]
    [InlineData("DisplayName", "")]
    [InlineData("DisplayName", "tab\there")]
    [InlineData("UsernameClaim", "")]
    [InlineData("UsernameClaim", "two words")]
    public void OidcOptionsValidator_Validate_InvalidSetting_FailsNamingTheSettingWithoutValues(
        string setting,
        string value)
    {
        // Arrange: valid settings with one invalid value
        var options = Valid();
        typeof(OidcOptions).GetProperty(setting)!.SetValue(options, value);

        // Act
        var result = Validate(options);

        // Assert: it fails, names the setting, and neither the value nor the secret is in the message
        Assert.True(result.Failed);
        Assert.Contains($"Identity:Oidc:{setting}", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.FailureMessage, StringComparison.Ordinal);
        if (value.Trim().Length > 3)
        {
            Assert.DoesNotContain(value, result.FailureMessage, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that a value one past each limit is refused.
    /// </summary>
    /// <param name="setting">The setting under test.</param>
    [Theory]
    [InlineData("ClientId")]
    [InlineData("ClientSecret")]
    [InlineData("DisplayName")]
    [InlineData("UsernameClaim")]
    [InlineData("Authority")]
    [InlineData("Scopes")]
    public void OidcOptionsValidator_Validate_ValuePastTheLimit_Fails(string setting)
    {
        // Arrange: a value one character longer than the limit
        var limit = setting switch
        {
            "ClientId" => OidcOptions.MaxClientIdLength,
            "ClientSecret" => OidcOptions.MaxClientSecretLength,
            "DisplayName" => OidcOptions.MaxDisplayNameLength,
            "UsernameClaim" => OidcOptions.MaxUsernameClaimLength,
            "Authority" => OidcOptions.MaxAuthorityLength,
            _ => OidcOptions.MaxScopesLength
        };
        var value = setting switch
        {
            "Authority" => "https://idp.example.com/" + new string('a', limit - 23),
            "Scopes" => "openid " + new string('s', limit - 6),
            _ => new string('x', limit + 1)
        };
        var options = Valid();
        typeof(OidcOptions).GetProperty(setting)!.SetValue(options, value);

        // Act
        var result = Validate(options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains($"Identity:Oidc:{setting}", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that too many scopes, and a scope that is too long, are refused.
    /// </summary>
    /// <param name="count">The number of scopes besides <c>openid</c>.</param>
    /// <param name="length">The length of each of them.</param>
    [Theory]
    [InlineData(20, 1)]
    [InlineData(1, OidcOptions.MaxScopeLength + 1)]
    public void OidcOptionsValidator_Validate_TooManyOrTooLongScopes_Fails(int count, int length)
    {
        // Arrange
        var options = Valid();
        options.Scopes = "openid " + string.Join(' ', Enumerable.Repeat(new string('s', length), count));

        // Act
        var result = Validate(options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("Identity:Oidc:Scopes", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the age for a confirmation of identity must be within its bounds.
    /// </summary>
    /// <param name="seconds">The age in seconds.</param>
    /// <param name="accepted">Whether the age is accepted.</param>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(120, true)]
    [InlineData(900, true)]
    [InlineData(901, false)]
    public void OidcOptionsValidator_Validate_ReauthMaxAgeSeconds_MustBeWithinBounds(int seconds, bool accepted)
    {
        // Arrange
        var options = Valid();
        options.ReauthMaxAgeSeconds = seconds;

        // Act
        var result = Validate(options);

        // Assert
        Assert.Equal(accepted, result.Succeeded);
        if (!accepted)
        {
            Assert.Contains("Identity:Oidc:ReauthMaxAgeSeconds", result.FailureMessage, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that http is accepted for the loopback hosts only.
    /// </summary>
    /// <param name="authority">The address.</param>
    /// <param name="accepted">Whether the address is accepted.</param>
    [Theory]
    [InlineData("http://localhost:8080/realm", true)]
    [InlineData("http://127.0.0.1:5000/", true)]
    [InlineData("http://[::1]:5000/", true)]
    [InlineData("https://idp.example.com/", true)]
    [InlineData("http://idp.example.com/", false)]
    [InlineData("http://192.168.1.10/", false)]
    public void OidcOptionsValidator_Validate_Scheme_HttpOnlyForLoopback(string authority, bool accepted)
    {
        // Arrange
        var options = Valid();
        options.Authority = authority;

        // Act
        var result = Validate(options);

        // Assert
        Assert.Equal(accepted, result.Succeeded);
        Assert.Equal(authority.StartsWith("http:", StringComparison.Ordinal) && accepted, options.IsLoopbackHttp());
    }

    /// <summary>
    ///     Test that the validator refuses a missing settings object.
    /// </summary>
    [Fact]
    public void OidcOptionsValidator_Validate_Null_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<ArgumentNullException>(() => new OidcOptionsValidator().Validate(null, null!));
    }
}
