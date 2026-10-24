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

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the sign-in page (<c>LoginPage.razor</c>): reads the return address and the failure
///     flag from the query string.
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
    private bool Failed => !string.IsNullOrEmpty(Error);
}
