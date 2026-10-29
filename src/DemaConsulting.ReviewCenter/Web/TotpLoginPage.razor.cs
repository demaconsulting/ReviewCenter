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
using DemaConsulting.ReviewCenter.Identity;
using Microsoft.AspNetCore.Components;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the second step of sign-in (<c>TotpLoginPage.razor</c>): reads the failure flag from the
///     query string and sends the user back to the sign-in page when there is no valid pending sign-in.
/// </summary>
/// <remarks>
///     The page is static, so it holds no state of its own and needs no synchronization. It only looks at the
///     pending sign-in (it does not use it up); the endpoint that checks the code decides everything else.
/// </remarks>
public partial class TotpLoginPage : ComponentBase
{
    /// <summary>
    ///     Gets or sets the request, supplied by the framework while the page is rendered on the server.
    /// </summary>
    [CascadingParameter]
    private HttpContext? Http { get; set; }

    /// <summary>
    ///     Gets or sets the service of the second factor, supplied by dependency injection.
    /// </summary>
    [Inject]
    private TotpService Totp { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the navigation manager, supplied by dependency injection.
    /// </summary>
    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the failure flag, from the <c>error</c> query value.
    /// </summary>
    [SupplyParameterFromQuery(Name = "error")]
    private string? Error { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the previous attempt failed.
    /// </summary>
    private bool Failed => !string.IsNullOrEmpty(Error) && !Throttled;

    /// <summary>
    ///     Gets a value indicating whether the previous attempt was refused because there were too many.
    /// </summary>
    private bool Throttled => Error == SignIn.ThrottledError;

    /// <summary>
    ///     Sends the user back to the sign-in page when no valid pending sign-in belongs to this request.
    /// </summary>
    protected override void OnInitialized()
    {
        var token = Http?.Request.Cookies[TotpSignIn.CookieName];
        var address = Http is null ? null : SignIn.ClientAddress(Http);
        if (Totp.PeekChallenge(token, address) is null)
        {
            Navigation.NavigateTo($"{SignIn.LoginPath}?error={TotpSignIn.ErrorExpired}");
        }
    }
}
