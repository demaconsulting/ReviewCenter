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
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the sign-in page (<c>LoginPage.razor</c>): reads the return address and the failure
///     flag (a generic failure, "too many attempts" or a failure at the identity provider) from the query string, and
///     tells whether the button of the identity provider is offered.
/// </summary>
/// <remarks>
///     The page is static, so it holds no state of its own and needs no synchronization. The return address is
///     reduced to a local one here before it is written into the form, and the sign-in endpoint checks it again.
/// </remarks>
public partial class LoginPage : ComponentBase
{
    /// <summary>
    ///     Gets or sets the address the user was heading for, from the <c>ReturnUrl</c> query value.
    /// </summary>
    [SupplyParameterFromQuery(Name = "ReturnUrl")]
    private string? ReturnUrl { get; set; }

    /// <summary>
    ///     Gets or sets the failure flag, from the <c>error</c> query value.
    /// </summary>
    [SupplyParameterFromQuery(Name = "error")]
    private string? Error { get; set; }

    /// <summary>
    ///     Gets the return address to put into the form: the requested one when it is local, otherwise <c>/</c>.
    /// </summary>
    private string SafeReturnUrl => SignIn.SafeReturnUrl(ReturnUrl);

    /// <summary>
    ///     Gets a value indicating whether the previous attempt failed.
    /// </summary>
    private bool Failed => !string.IsNullOrEmpty(Error) && !TotpExpired;

    /// <summary>
    ///     Gets a value indicating whether the second step of a sign-in could not be completed because the first
    ///     step has expired or is unknown, so that the user has to sign in again.
    /// </summary>
    private bool TotpExpired => Error == TotpSignIn.ErrorExpired;

    /// <summary>
    ///     Gets or sets the settings of sign-in with an identity provider, supplied by dependency injection.
    /// </summary>
    [Inject]
    private IOptions<OidcOptions> Oidc { get; set; } = null!;

    /// <summary>
    ///     Gets the settings of the identity provider; the button is shown only when it is enabled.
    /// </summary>
    private OidcOptions Provider => Oidc.Value;

    /// <summary>
    ///     Gets a value indicating whether the previous attempt failed at the identity provider.
    /// </summary>
    private bool ProviderFailed => Provider.Enabled && Error == OidcSignIn.ErrorValue;

    /// <summary>
    ///     Gets a value indicating whether the previous attempt was refused because there were too many.
    /// </summary>
    private bool Throttled => Error == SignIn.ThrottledError;

    /// <summary>
    ///     Gets or sets the flag set after a password change, from the <c>changed</c> query value.
    /// </summary>
    [SupplyParameterFromQuery(Name = "changed")]
    private string? Changed { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the user arrives from a successful password change and has to sign in
    ///     again with the new password.
    /// </summary>
    private bool PasswordChanged => Changed == "1";
}
