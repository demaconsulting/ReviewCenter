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

using System.Globalization;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Caching.Memory;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the page frame (<c>MainLayout.razor</c>): works out who is signed in for the header and the
///     number of reviews that need the user's action for the Inbox tab.
/// </summary>
/// <remarks>
///     The layout is rendered statically for every request, so the user is read once from the authentication
///     state of that request. Nothing here changes state; signing out is a form post handled by an endpoint.
///     A user with a temporary password is confined to a few pages: the check runs when the layout starts and again
///     on every navigation, because an interactive page keeps the layout alive while the user clicks on. A layout
///     instance serves one render, so it needs no synchronization.
///     The count on the Inbox tab comes from <see cref="InboxService.CountAsync"/>, which costs one snapshot per
///     review the user is involved in, so it is kept per user in the shared <see cref="IMemoryCache"/> for
///     <see cref="CountLifetime"/>: it is read when the layout starts and again on each navigation (no timer), which
///     is normally one count per user in that time however often the user clicks (tabs that read at the same moment
///     may each read once). The age of an entry is checked
///     against the injected <see cref="TimeProvider"/> so a test controls it. The count is only a hint, so a failure
///     to read it hides the badge and never breaks the page, and nothing is asked for anybody who is signed out or
///     still has to change a temporary password. The cache lives in this layer because the Core project does not
///     reference the caching package and the cache is a presentation concern.
/// </remarks>
public partial class MainLayout : LayoutComponentBase, IDisposable
{
    /// <summary>
    ///     How long a read count is reused before it is read again.
    /// </summary>
    internal static readonly TimeSpan CountLifetime = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     The highest number the badge shows exactly; a larger count is shown as this number and a plus sign.
    /// </summary>
    private const int MaxShownCount = 99;

    /// <summary>
    ///     The start of the cache key of a user's count; the user name follows.
    /// </summary>
    private const string CountKeyPrefix = "nav-inbox-count:";

    /// <summary>
    ///     Whether <see cref="OnLocationChanged"/> is subscribed to the navigation manager.
    /// </summary>
    private bool _subscribed;

    /// <summary>
    ///     Whether the layout was disposed, so a count that finishes late changes nothing.
    /// </summary>
    private bool _disposed;

    /// <summary>
    ///     Gets or sets the authentication state of the current request, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the navigation manager, supplied by dependency injection; used to confine an account with a
    ///     temporary password to the pages it may use and to refresh the count on navigation.
    /// </summary>
    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that counts the reviews that need action, supplied by dependency injection.
    /// </summary>
    [Inject]
    private InboxService Inbox { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the cache that holds the count of each user for <see cref="CountLifetime"/>, supplied by
    ///     dependency injection.
    /// </summary>
    [Inject]
    private IMemoryCache Cache { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the clock that ages the cached counts, supplied by dependency injection.
    /// </summary>
    [Inject]
    private TimeProvider Time { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the logger for a count that could not be read, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ILogger<MainLayout> Logger { get; set; } = null!;

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
    ///     Gets the number of reviews that need the user's action, or zero when it is not known.
    /// </summary>
    private int ActionCount { get; set; }

    /// <summary>
    ///     Gets the text of the badge: the count, or <c>99+</c> for a larger one.
    /// </summary>
    private string ActionCountText =>
        ActionCount > MaxShownCount ? $"{MaxShownCount}+" : ActionCount.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    ///     Gets the accessible text of the badge, which always carries the exact count.
    /// </summary>
    private string ActionCountLabel => ActionCount == 1
        ? "1 review needs your action"
        : string.Create(CultureInfo.InvariantCulture, $"{ActionCount} reviews need your action");

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
            if (MustChangePassword)
            {
                RedirectUnlessOpenDuringPasswordChange(Navigation.Uri);
            }
            else
            {
                await RefreshActionCountAsync();
            }

            // The layout lives on while the user navigates inside the circuit, so every later navigation is
            // checked (or the count refreshed) too; the handler is removed again in Dispose. A layout that was
            // disposed during the count read must not subscribe
            if (!_disposed)
            {
                Navigation.LocationChanged += OnLocationChanged;
                _subscribed = true;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Stops watching the navigation, so a layout that is gone is not kept alive and does not redirect.
    /// </summary>
    /// <param name="disposing"><c>true</c> when called from <see cref="Dispose()"/> rather than a destructor.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            if (_subscribed)
            {
                Navigation.LocationChanged -= OnLocationChanged;
                _subscribed = false;
            }
        }
    }

    /// <summary>
    ///     Checks the page the user navigated to while the password is still temporary, or refreshes the count of the
    ///     Inbox tab.
    /// </summary>
    /// <param name="sender">The navigation manager.</param>
    /// <param name="e">The new location.</param>
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        // The layout may be disposed while the circuit still raises a navigation; it then does nothing
        if (_disposed)
        {
            return;
        }

        if (MustChangePassword)
        {
            RedirectUnlessOpenDuringPasswordChange(e.Location);
            return;
        }

        // The cache keeps this cheap; the refresh never throws, so nothing needs to observe the task
        _ = InvokeAsync(async () =>
        {
            // The callback may run after the layout was disposed: it then reads nothing and renders nothing
            if (_disposed)
            {
                return;
            }

            await RefreshActionCountAsync();
            if (!_disposed)
            {
                StateHasChanged();
            }
        });
    }

    /// <summary>
    ///     Reads the number of reviews that need the user's action, from the cache when it is younger than
    ///     <see cref="CountLifetime"/>. A failure leaves the count at zero, which hides the badge.
    /// </summary>
    /// <returns>A task that completes when the count is set; it never faults.</returns>
    private async Task RefreshActionCountAsync()
    {
        if (Actor is null || MustChangePassword)
        {
            return;
        }

        try
        {
            var key = CountKeyPrefix + Actor.Username.ToUpperInvariant();
            if (Cache.TryGetValue(key, out CachedCount? hit) && hit is not null && IsFresh(hit, Time.GetUtcNow()))
            {
                ActionCount = hit.Count;
                return;
            }

            var count = await Inbox.CountAsync(Actor);
            ActionCount = count;

            // The entry is stamped when the number became available, not when the read started, so a slow read does
            // not shorten the time the number is reused. The cache evicts by its own clock; the stamp is what the
            // freshness check above uses
            Cache.Set(key, new CachedCount(count, Time.GetUtcNow()), CountLifetime);
        }
        catch (Exception ex)
        {
            // The badge is a convenience: hide it rather than break the page; the operator log carries the user name
            // and the exception, and the page shows neither
            ActionCount = 0;
            Logger.LogWarning(ex, "The inbox count of {Username} could not be read; the badge is hidden", Actor.Username);
        }
    }

    /// <summary>
    ///     Decides whether a cached count may still be shown.
    /// </summary>
    /// <param name="entry">The cached count.</param>
    /// <param name="now">The current time.</param>
    /// <returns><c>true</c> when the count is younger than <see cref="CountLifetime"/> and not from the future.</returns>
    private static bool IsFresh(CachedCount entry, DateTimeOffset now)
    {
        var age = now - entry.ReadAt;
        return age >= TimeSpan.Zero && age < CountLifetime;
    }

    /// <summary>
    ///     A count as it was read, with the time it was read.
    /// </summary>
    /// <param name="Count">The number of reviews that need the user's action.</param>
    /// <param name="ReadAt">When the count was read.</param>
    private sealed record CachedCount(int Count, DateTimeOffset ReadAt);

    /// <summary>
    ///     Sends the user to the account page unless the address is one that stays open during a password change.
    /// </summary>
    /// <param name="uri">The absolute address the user is on or is going to.</param>
    private void RedirectUnlessOpenDuringPasswordChange(string uri)
    {
        if (MustChangePassword && !IsOpenDuringPasswordChange(Navigation.ToBaseRelativePath(uri)))
        {
            Navigation.NavigateTo(SignIn.AccountPath, forceLoad: true);
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
