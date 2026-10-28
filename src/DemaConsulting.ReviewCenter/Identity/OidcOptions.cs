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
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Identity;

/// <summary>
///     The settings of sign-in with an OpenID Connect provider, read from the <c>Identity:Oidc</c> section.
/// </summary>
/// <remarks>
///     Sign-in with a provider is off unless <see cref="Enabled"/> is set; while it is off nothing else is read,
///     checked or registered. The settings are bound by the options system, so they can come from
///     <c>appsettings.json</c> or from environment variables such as <c>Identity__Oidc__Authority</c>; the client
///     secret belongs in an environment variable or a secret store, not in a file under source control. Every
///     value is bounded and checked at start-up by <see cref="OidcOptionsValidator"/>. The type is a plain
///     settings holder that is not thread-safe while it is being bound, and it prints nothing: the secret never
///     reaches a log, a message or a page.
/// </remarks>
internal sealed class OidcOptions
{
    /// <summary>
    ///     The name of the configuration section that holds <see cref="OidcOptions"/>.
    /// </summary>
    public const string SectionName = "Identity:Oidc";

    /// <summary>
    ///     The longest accepted <see cref="Authority"/>.
    /// </summary>
    public const int MaxAuthorityLength = 512;

    /// <summary>
    ///     The longest accepted <see cref="ClientId"/>.
    /// </summary>
    public const int MaxClientIdLength = 256;

    /// <summary>
    ///     The longest accepted <see cref="ClientSecret"/>.
    /// </summary>
    public const int MaxClientSecretLength = 512;

    /// <summary>
    ///     The longest accepted <see cref="Scopes"/> text.
    /// </summary>
    public const int MaxScopesLength = 512;

    /// <summary>
    ///     The most scopes accepted in <see cref="Scopes"/>.
    /// </summary>
    public const int MaxScopeCount = 20;

    /// <summary>
    ///     The longest accepted single scope.
    /// </summary>
    public const int MaxScopeLength = 128;

    /// <summary>
    ///     The longest accepted <see cref="DisplayName"/>.
    /// </summary>
    public const int MaxDisplayNameLength = 50;

    /// <summary>
    ///     The longest accepted <see cref="UsernameClaim"/>.
    /// </summary>
    public const int MaxUsernameClaimLength = 64;

    /// <summary>
    ///     The smallest accepted <see cref="ReauthMaxAgeSeconds"/>.
    /// </summary>
    public const int MinReauthMaxAgeSeconds = 10;

    /// <summary>
    ///     The largest accepted <see cref="ReauthMaxAgeSeconds"/> (15 minutes).
    /// </summary>
    public const int MaxReauthMaxAgeSeconds = 900;

    /// <summary>
    ///     Gets or sets a value indicating whether people can sign in with the provider; off by default.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     Gets or sets the address of the provider (its issuer): an absolute <c>https</c> address, or <c>http</c>
    ///     for a loopback host such as <c>localhost</c> in development; without query or fragment.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the identifier under which this server is registered at the provider.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the secret of this server at the provider. It is required while sign-in with a provider is
    ///     enabled: the server is a confidential client that proves itself at the token endpoint, so a public client
    ///     that relies on PKCE alone is not supported. Supply it through an environment variable
    ///     (<c>Identity__Oidc__ClientSecret</c>) or a secret store rather than a file that is checked in.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the scopes to request, separated by spaces; must contain <c>openid</c>.
    /// </summary>
    public string Scopes { get; set; } = "openid profile email";

    /// <summary>
    ///     Gets or sets the name of the provider as people know it; shown on the button "Sign in with ...".
    /// </summary>
    public string DisplayName { get; set; } = "OpenID Connect";

    /// <summary>
    ///     Gets or sets the claim that suggests the user name of a new account; when it is missing the part of the
    ///     e-mail address before the <c>@</c> is used, then the subject.
    /// </summary>
    public string UsernameClaim { get; set; } = "preferred_username";

    /// <summary>
    ///     Gets or sets a value indicating whether the provider must have verified the e-mail address (the
    ///     <c>email_verified</c> claim must be <c>true</c>) before the sign-in is accepted.
    /// </summary>
    public bool RequireEmailVerified { get; set; }

    /// <summary>
    ///     Gets or sets how old, in seconds, the moment of the last sign-in at the provider (<c>auth_time</c>) may be
    ///     when a signature is confirmed; from <see cref="MinReauthMaxAgeSeconds"/> to
    ///     <see cref="MaxReauthMaxAgeSeconds"/>.
    /// </summary>
    public int ReauthMaxAgeSeconds { get; set; } = 120;

    /// <summary>
    ///     Tells whether the provider is reached over a plain loopback address, where http is accepted.
    /// </summary>
    /// <returns><c>true</c> when <see cref="Authority"/> is an <c>http</c> address of a loopback host.</returns>
    public bool IsLoopbackHttp()
    {
        return Uri.TryCreate(Authority, UriKind.Absolute, out var uri)
               && uri.Scheme == Uri.UriSchemeHttp
               && IsLoopback(uri);
    }

    /// <summary>
    ///     Tells whether the host of an address is the local machine.
    /// </summary>
    /// <param name="uri">The address.</param>
    /// <returns><c>true</c> for <c>localhost</c> and for a loopback IP address.</returns>
    internal static bool IsLoopback(Uri uri)
    {
        return uri.IsLoopback
               || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
    }
}

/// <summary>
///     Checks the <c>Identity:Oidc</c> settings when they are first used, at start-up.
/// </summary>
/// <remarks>
///     Nothing is checked while <see cref="OidcOptions.Enabled"/> is off. The messages name the setting and the rule
///     and never contain a value, so the secret cannot reach a log through them. Only the first problem is
///     reported. The class holds no state and is thread-safe.
/// </remarks>
internal sealed class OidcOptionsValidator : IValidateOptions<OidcOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OidcOptions options)
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
    private static string? Check(OidcOptions options)
    {
        const string section = OidcOptions.SectionName;
        return CheckAuthority(options.Authority)
               ?? CheckText(
                   options.ClientId,
                   $"{section}:ClientId",
                   OidcOptions.MaxClientIdLength,
                   required: true,
                   allowSpace: false)
               ?? CheckText(
                   options.ClientSecret,
                   $"{section}:ClientSecret",
                   OidcOptions.MaxClientSecretLength,
                   required: true,
                   allowSpace: true)
               ?? CheckScopes(options.Scopes)
               ?? CheckText(
                   options.DisplayName,
                   $"{section}:DisplayName",
                   OidcOptions.MaxDisplayNameLength,
                   required: true,
                   allowSpace: true)
               ?? CheckText(
                   options.UsernameClaim,
                   $"{section}:UsernameClaim",
                   OidcOptions.MaxUsernameClaimLength,
                   required: true,
                   allowSpace: false)
               ?? CheckAge(options.ReauthMaxAgeSeconds);
    }

    /// <summary>
    ///     Checks the address of the provider.
    /// </summary>
    /// <param name="authority">The address.</param>
    /// <returns>The message of the problem; <c>null</c> when the address is usable.</returns>
    private static string? CheckAuthority(string? authority)
    {
        const string key = $"{OidcOptions.SectionName}:Authority";
        if (string.IsNullOrWhiteSpace(authority))
        {
            return $"{key} is required when sign-in with OpenID Connect is enabled.";
        }

        if (authority.Length > OidcOptions.MaxAuthorityLength
            || !Uri.TryCreate(authority, UriKind.Absolute, out var uri)
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.UserInfo.Length > 0)
        {
            return $"{key} must be an absolute address of at most {OidcOptions.MaxAuthorityLength} characters " +
                   "without user, query or fragment.";
        }

        var secure = uri.Scheme == Uri.UriSchemeHttps;
        if (!secure && !(uri.Scheme == Uri.UriSchemeHttp && OidcOptions.IsLoopback(uri)))
        {
            return $"{key} must use https (http is accepted only for localhost or another loopback address).";
        }

        return null;
    }

    /// <summary>
    ///     Checks a text setting.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="key">The full name of the setting, for the message.</param>
    /// <param name="maxLength">The longest accepted value.</param>
    /// <param name="required">Whether the value may not be empty.</param>
    /// <param name="allowSpace">Whether spaces are accepted inside the value.</param>
    /// <returns>The message of the problem; <c>null</c> when the value is usable.</returns>
    private static string? CheckText(string? value, string key, int maxLength, bool required, bool allowSpace)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return required ? $"{key} is required when sign-in with OpenID Connect is enabled." : null;
        }

        if (value.Length > maxLength
            || value.Any(c => char.IsControl(c) || (!allowSpace && char.IsWhiteSpace(c))))
        {
            return $"{key} must be at most {maxLength} characters without control characters" +
                   (allowSpace ? "." : " or spaces.");
        }

        return null;
    }

    /// <summary>
    ///     Checks the list of scopes.
    /// </summary>
    /// <param name="scopes">The scopes, separated by spaces.</param>
    /// <returns>The message of the problem; <c>null</c> when the list is usable.</returns>
    private static string? CheckScopes(string? scopes)
    {
        const string key = $"{OidcOptions.SectionName}:Scopes";
        var list = (scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!list.Contains("openid", StringComparer.Ordinal))
        {
            return $"{key} must contain the scope 'openid'.";
        }

        // Printable ASCII only: the scopes are sent in the address of the authorization request
        if (scopes!.Length > OidcOptions.MaxScopesLength
            || list.Length > OidcOptions.MaxScopeCount
            || list.Any(scope => scope.Length > OidcOptions.MaxScopeLength || scope.Any(c => c < '!' || c > '~')))
        {
            return $"{key} must hold at most {OidcOptions.MaxScopeCount} scopes of printable characters, each at " +
                   $"most {OidcOptions.MaxScopeLength} characters, {OidcOptions.MaxScopesLength} in total.";
        }

        return null;
    }

    /// <summary>
    ///     Checks the age allowed for a confirmation of identity.
    /// </summary>
    /// <param name="seconds">The age in seconds.</param>
    /// <returns>The message of the problem; <c>null</c> when the age is usable.</returns>
    private static string? CheckAge(int seconds)
    {
        return seconds is >= OidcOptions.MinReauthMaxAgeSeconds and <= OidcOptions.MaxReauthMaxAgeSeconds
            ? null
            : $"{OidcOptions.SectionName}:ReauthMaxAgeSeconds must be between {OidcOptions.MinReauthMaxAgeSeconds} " +
              $"and {OidcOptions.MaxReauthMaxAgeSeconds}.";
    }
}
