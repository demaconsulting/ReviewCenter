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

namespace DemaConsulting.ReviewCenter.Identity;

/// <summary>
///     The settings of the optional second factor (time-based one-time passwords), read from the
///     <c>Identity:Totp</c> section.
/// </summary>
/// <remarks>
///     The second factor is on by default: nothing changes for an account until its owner sets it up. Switching it
///     off hides the setup, removes the second step from sign-in and the code from signing, and lets enrolled
///     accounts sign in with the password alone again (the enrollments stay and apply again when it is switched
///     back on). The values come from <c>appsettings.json</c> or from environment variables such as
///     <c>Identity__Totp__Enabled</c> and are checked at start-up by <see cref="TotpOptionsValidator"/>. The type is
///     a plain settings holder that is not thread-safe while it is being bound.
/// </remarks>
internal sealed class TotpOptions
{
    /// <summary>
    ///     The name of the configuration section that holds <see cref="TotpOptions"/>.
    /// </summary>
    public const string SectionName = "Identity:Totp";

    /// <summary>
    ///     The longest accepted <see cref="Issuer"/>.
    /// </summary>
    public const int MaxIssuerLength = 64;

    /// <summary>
    ///     Gets or sets a value indicating whether the second factor is available; on by default.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Gets or sets the service name that authenticator apps show next to the account.
    /// </summary>
    public string Issuer { get; set; } = TotpSettings.DefaultIssuer;
}

/// <summary>
///     Checks the <c>Identity:Totp</c> settings when they are first used, at start-up.
/// </summary>
/// <remarks>
///     The issuer is checked even while the second factor is off, so a mistake does not wait for the switch to be
///     turned on. The messages name the setting and the rule and never contain a value. The class holds no state and
///     is thread-safe.
/// </remarks>
internal sealed class TotpOptionsValidator : IValidateOptions<TotpOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TotpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var issuer = options.Issuer?.Trim() ?? string.Empty;
        if (issuer.Length == 0 || issuer.Length > TotpOptions.MaxIssuerLength)
        {
            return ValidateOptionsResult.Fail(
                $"{TotpOptions.SectionName}:Issuer must be between 1 and {TotpOptions.MaxIssuerLength} characters.");
        }

        // A colon separates the issuer from the account in the label of an authenticator entry
        return issuer.Contains(':', StringComparison.Ordinal) || issuer.Any(char.IsControl)
            ? ValidateOptionsResult.Fail(
                $"{TotpOptions.SectionName}:Issuer must not contain a colon or a control character.")
            : ValidateOptionsResult.Success;
    }
}
