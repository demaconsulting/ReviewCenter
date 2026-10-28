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
    ///     Gets or sets the navigation manager, supplied by dependency injection; used to confine an account with a
    ///     temporary password to the pages it may use.
    /// </summary>
    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    /// <summary>
    ///     Gets the signed-in user, or <c>null</c> when nobody is signed in.
    /// </summary>
    private Actor? Actor { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the signed-in user is an administrator.
    /// </summary>
    private bool IsAdmin { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the signed-in user still has to change a temporary password; such a user
    ///     sees no navigation tabs and is sent to the account page from every other page.
    /// </summary>
    private bool MustChangePassword { get; set; }

    /// <summary>
    ///     Gets the first segments of the pages a user with a temporary password may open: the page that changes the
    ///     password, the sign-in page, and the static explanations.
    /// </summary>
    private static readonly string[] PagesOpenDuringPasswordChange = ["account", "login", "help", "about"];

    /// <summary>
    ///     Gets the version shown in the footer: the informational version up to the <c>+</c>, then <c>+</c> and the
    ///     first 7 characters of the commit that follows it, when there is one.
    /// </summary>
    private static string ShortVersion
    {
        get
        {
            var full = Program.Version;
            var plus = full.IndexOf('+', StringComparison.Ordinal);
            if (plus < 0)
            {
                return full;
            }

            var sha = full[(plus + 1)..];
            return sha.Length == 0 ? full[..plus] : $"{full[..plus]}+{sha[..Math.Min(7, sha.Length)]}";
        }
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var state = await AuthenticationState.GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated == true)
        {
            Actor = await AuthenticationState.GetActorAsync();
            IsAdmin = state.User.IsInRole(SignIn.AdminRole);
            MustChangePassword = SignIn.IsPasswordChangeRequired(state.User);

            // One redirect confines the user to the page that replaces the temporary password; the claim is
            // enough, so no service is called per navigation (the data is refused by the access rules anyway)
            if (MustChangePassword && !IsOpenDuringPasswordChange(Navigation.ToBaseRelativePath(Navigation.Uri)))
            {
                Navigation.NavigateTo(SignIn.AccountPath, forceLoad: true);
            }
        }
    }

    /// <summary>
    ///     Decides whether a page may be opened while the password is still temporary.
    /// </summary>
    /// <param name="relativePath">The path relative to the base address, possibly with a query or fragment.</param>
    /// <returns><c>true</c> when its first segment is one of <see cref="PagesOpenDuringPasswordChange"/>.</returns>
    private static bool IsOpenDuringPasswordChange(string relativePath)
    {
        var end = relativePath.IndexOfAny(['/', '?', '#']);
        var first = end < 0 ? relativePath : relativePath[..end];
        return PagesOpenDuringPasswordChange.Contains(first, StringComparer.OrdinalIgnoreCase);
    }
}
