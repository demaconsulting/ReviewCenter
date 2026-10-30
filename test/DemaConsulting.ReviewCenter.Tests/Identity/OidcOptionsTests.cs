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
using DemaConsulting.ReviewCenter.Utilities;

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
        // Arrange: garbage in every setting, and no secret
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
    [InlineData("ClientSecret", "")]
    [InlineData("ClientSecret", "   ")]
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
    ///     Test that enabled settings without a client secret (never set, empty or only white space) are refused at
    ///     start-up: the server is a confidential client, so a public client with PKCE alone is not supported.
    /// </summary>
    /// <param name="secret">The secret; <c>null</c> to leave the setting at its default.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t \t")]
    public void OidcOptionsValidator_Validate_MissingClientSecret_FailsNamingTheSetting(string? secret)
    {
        // Arrange: valid enabled settings, with the secret removed
        var options = Valid();
        options.ClientSecret = secret ?? new OidcOptions().ClientSecret;

        // Act
        var result = Validate(options);

        // Assert: it fails, names the setting and says it is required
        Assert.True(result.Failed);
        Assert.Contains("Identity:Oidc:ClientSecret", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("required", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a secret that is too long or holds a control character is refused without the secret, or any part
    ///     of it, appearing in the message.
    /// </summary>
    /// <param name="tooLong">Whether the secret is too long (otherwise it holds a control character).</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OidcOptionsValidator_Validate_InvalidClientSecret_FailsWithoutEchoingTheSecret(bool tooLong)
    {
        // Arrange: a secret that starts with a recognizable text
        var options = Valid();
        options.ClientSecret = tooLong
            ? Secret + new string('x', OidcOptions.MaxClientSecretLength)
            : Secret + "\u0007";

        // Act
        var result = Validate(options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("Identity:Oidc:ClientSecret", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("xxxx", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a client secret in a file is accepted: it is trimmed, it replaces the inline secret, and the
    ///     inline setting may stay empty.
    /// </summary>
    [Fact]
    public void OidcOptionsValidator_Validate_ClientSecretFile_SucceedsAndResolvesTheTrimmedSecret()
    {
        // Arrange: valid settings with the secret in a file (with the newline an editor adds) and none inline
        using var directory = new TemporaryDirectory();
        var path = directory.GetFilePath("client-secret.txt");
        File.WriteAllText(path, Secret + "\n");
        var options = Valid();
        options.ClientSecret = string.Empty;
        options.ClientSecretFile = path;

        // Act
        var result = Validate(options);
        var resolved = options.ResolveClientSecret();

        // Assert
        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Equal(Secret, resolved);
    }

    /// <summary>
    ///     Test that a secret both inline and in a file is refused, naming both settings and no secret.
    /// </summary>
    [Fact]
    public void OidcOptionsValidator_Validate_ClientSecretAndFileBothSet_FailsNamingBothSettings()
    {
        // Arrange: a file that exists, and the inline secret of the valid settings
        using var directory = new TemporaryDirectory();
        var path = directory.GetFilePath("client-secret.txt");
        File.WriteAllText(path, "secret-in-the-file");
        var options = Valid();
        options.ClientSecretFile = path;

        // Act
        var result = Validate(options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("Identity:Oidc:ClientSecret ", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Identity:Oidc:ClientSecretFile", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-in-the-file", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a secret file that is missing or empty, or a secret in it that is out of bounds, is refused
    ///     naming the file setting, without the secret in the message.
    /// </summary>
    /// <param name="problem">The kind of problem: <c>missing</c>, <c>empty</c>, <c>oversize</c>, <c>tooLong</c>
    ///     or <c>control</c>.</param>
    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("oversize")]
    [InlineData("tooLong")]
    [InlineData("control")]
    public void OidcOptionsValidator_Validate_ClientSecretFileProblem_FailsNamingTheFileSettingWithoutTheSecret(
        string problem)
    {
        // Arrange: valid settings whose secret comes from a file with the given problem
        using var directory = new TemporaryDirectory();
        var path = directory.GetFilePath("client-secret.txt");
        switch (problem)
        {
            case "empty":
                File.WriteAllText(path, "\n");
                break;
            case "oversize":
                File.WriteAllText(path, Secret + new string('x', SecretSource.MaxFileBytes));
                break;
            case "tooLong":
                File.WriteAllText(path, Secret + new string('x', OidcOptions.MaxClientSecretLength));
                break;
            case "control":
                File.WriteAllText(path, Secret + "\u0007");
                break;
        }

        var options = Valid();
        options.ClientSecret = string.Empty;
        options.ClientSecretFile = path;

        // Act
        var result = Validate(options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("Identity:Oidc:ClientSecretFile", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("xxxx", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the resolver the provider registration uses applies the same bounds as the start-up check: a
    ///     file that passes the check and then changes to an out-of-bounds secret is refused on the second read,
    ///     naming the file setting and not holding the secret.
    /// </summary>
    /// <param name="change">The change made after the check: <c>tooLong</c> or <c>control</c>.</param>
    [Theory]
    [InlineData("tooLong")]
    [InlineData("control")]
    public void OidcOptions_ResolveClientSecret_FileChangedAfterValidation_RefusesOutOfBoundsSecret(string change)
    {
        // Arrange: settings that validate, with the secret in a file
        using var directory = new TemporaryDirectory();
        var path = directory.GetFilePath("client-secret.txt");
        File.WriteAllText(path, Secret);
        var options = Valid();
        options.ClientSecret = string.Empty;
        options.ClientSecretFile = path;
        Assert.True(Validate(options).Succeeded);

        // Act: the file changes, then the secret is read again as the provider registration does
        var changed = change == "tooLong"
            ? Secret + new string('x', OidcOptions.MaxClientSecretLength)
            : Secret + "\u0007";
        File.WriteAllText(path, changed);
        var ex = Assert.Throws<InvalidOperationException>(() => options.ResolveClientSecret());

        // Assert
        Assert.Contains("Identity:Oidc:ClientSecretFile", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, ex.Message, StringComparison.Ordinal);
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
