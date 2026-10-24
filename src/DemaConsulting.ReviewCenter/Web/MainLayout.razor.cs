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
using Microsoft.AspNetCore.Components.Authorization;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the page frame (<c>MainLayout.razor</c>): works out who is signed in for the header.
/// </summary>
/// <remarks>
///     The layout is rendered statically for every request, so the user is read once from the authentication
///     state of that request. Nothing here changes state; signing out is a form post handled by an endpoint.
///     A layout instance serves one render, so it needs no synchronization.
/// </remarks>
public partial class MainLayout : LayoutComponentBase
{
    /// <summary>
    ///     Gets or sets the authentication state of the current request, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets the signed-in user, or <c>null</c> when nobody is signed in.
    /// </summary>
    private Actor? Actor { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the signed-in user is an administrator.
    /// </summary>
    private bool IsAdmin { get; set; }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var state = await AuthenticationState.GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated == true)
        {
            Actor = await AuthenticationState.GetActorAsync();
            IsAdmin = state.User.IsInRole(SignIn.AdminRole);
        }
    }
}
