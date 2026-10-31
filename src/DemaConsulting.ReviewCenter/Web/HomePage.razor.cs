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
///     Code-behind for the home page (<c>HomePage.razor</c>): reads how many reviews need the signed-in user's action
///     for the inbox card.
/// </summary>
/// <remarks>
///     The page stays static server-side rendered: the count is read once while the page is rendered, through
///     <see cref="InboxService"/>, which gives zero for an anonymous visitor and for a user without access. Nothing
///     is written. The page itself needs no sign-in.
/// </remarks>
public partial class HomePage : ComponentBase
{
    /// <summary>
    ///     Gets or sets the service that counts the reviews that need action, supplied by dependency injection.
    /// </summary>
    [Inject]
    private InboxService Inbox { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets the number of reviews that need the user's action.
    /// </summary>
    private int ActionCount { get; set; }

    /// <summary>
    ///     Gets the sentence of the inbox card.
    /// </summary>
    private string InboxSummary
    {
        get
        {
            if (ActionCount == 0)
            {
                return "Nothing needs your action.";
            }

            return ActionCount == 1 ? "1 needs your action." : $"{ActionCount} need your action.";
        }
    }

    /// <summary>
    ///     Reads the count when the page is created.
    /// </summary>
    /// <returns>A task that completes when the count is read.</returns>
    protected override async Task OnInitializedAsync()
    {
        ActionCount = await Inbox.CountAsync(await AuthenticationState.GetActorOrNullAsync());
    }
}
