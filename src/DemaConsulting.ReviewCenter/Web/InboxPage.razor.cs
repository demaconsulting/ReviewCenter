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
///     Code-behind for the inbox page (<c>InboxPage.razor</c>): lists the work waiting for the signed-in user.
/// </summary>
/// <remarks>
///     The page talks to the domain only through <see cref="InboxService"/>, which returns only reviews of projects
///     the user can see, so a user without access gets an empty inbox. Each browser connection (circuit) gets its
///     own page instance, so the state here is per-user and needs no synchronization. The page requires a signed-in
///     user.
/// </remarks>
public partial class InboxPage : ComponentBase
{
    /// <summary>
    ///     The groups in the order in which they are shown.
    /// </summary>
    private static readonly InboxSection[] AllSections =
    [
        InboxSection.ToSign,
        InboxSection.RepliesWaiting,
        InboxSection.FailedChecks,
        InboxSection.Updates
    ];

    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     Gets or sets the service that builds the inbox, supplied by dependency injection.
    /// </summary>
    [Inject]
    private InboxService Inbox { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets the rows of the inbox.
    /// </summary>
    private List<InboxItem> Items { get; set; } = [];

    /// <summary>
    ///     Gets the number of reviews that need the user's action.
    /// </summary>
    private int ActionCount { get; set; }

    /// <summary>
    ///     Gets the groups in display order.
    /// </summary>
    private static IReadOnlyList<InboxSection> Sections => AllSections;

    /// <summary>
    ///     Gets the sentence above the list that says how much is waiting.
    /// </summary>
    private string Summary
    {
        get
        {
            var updates = Items.Count(item => item.Section == InboxSection.Updates);
            var action = ActionCount == 1 ? "1 needs your action" : $"{ActionCount} need your action";
            var update = updates == 1 ? "1 update" : $"{updates} updates";
            return $"{action}, {update}";
        }
    }

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Gets the key of a group, used in test identifiers.
    /// </summary>
    /// <param name="section">The group.</param>
    /// <returns>A lowercase kebab-case key.</returns>
    private static string SectionKey(InboxSection section)
    {
        return section switch
        {
            InboxSection.ToSign => "to-sign",
            InboxSection.RepliesWaiting => "replies-waiting",
            InboxSection.FailedChecks => "failed-checks",
            _ => "updates"
        };
    }

    /// <summary>
    ///     Gets the heading of a group.
    /// </summary>
    /// <param name="section">The group.</param>
    /// <returns>The heading text.</returns>
    private static string SectionTitle(InboxSection section)
    {
        return section switch
        {
            InboxSection.ToSign => "To sign",
            InboxSection.RepliesWaiting => "Replies waiting",
            InboxSection.FailedChecks => "Failed checks",
            _ => "Updates"
        };
    }

    /// <summary>
    ///     Gets the style class of the chip of a row.
    /// </summary>
    /// <param name="item">The row.</param>
    /// <returns>A class name; failed checks and out-of-date signatures use the warning style, approvals the success style.</returns>
    private static string? ChipClass(InboxItem item)
    {
        return item.Section switch
        {
            InboxSection.ToSign or InboxSection.RepliesWaiting => "pending",
            InboxSection.FailedChecks => "failed",
            _ => item.Chip == InboxService.ApprovedChip ? "approved" : "pending"
        };
    }

    /// <summary>
    ///     Loads the inbox when the page is created.
    /// </summary>
    /// <returns>A task that completes when the inbox is loaded.</returns>
    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    /// <summary>
    ///     Marks the page interactive after it has rendered on a live circuit.
    /// </summary>
    /// <param name="firstRender"><c>true</c> for the first render of this component instance.</param>
    /// <remarks>
    ///     <c>OnAfterRender</c> is never invoked during static prerendering, so reaching it proves that
    ///     the circuit is connected. A second render makes the new marker value reach the browser.
    /// </remarks>
    protected override void OnAfterRender(bool firstRender)
    {
        // Only the first render transitions the marker; later renders must not loop
        if (firstRender)
        {
            _interactive = true;
            StateHasChanged();
        }
    }

    /// <summary>
    ///     Marks every listed review as seen and shows the inbox again.
    /// </summary>
    /// <returns>A task that completes when the inbox is reloaded.</returns>
    private async Task MarkAllReadAsync()
    {
        var actor = await AuthenticationState.GetActorOrNullAsync();
        if (actor is not null)
        {
            await Inbox.MarkAllReadAsync(actor);
        }

        await LoadAsync();
    }

    /// <summary>
    ///     Reads the inbox of the signed-in user.
    /// </summary>
    /// <returns>A task that completes when the rows are loaded.</returns>
    private async Task LoadAsync()
    {
        var view = await Inbox.GetInboxAsync(await AuthenticationState.GetActorOrNullAsync());
        Items = [.. view.Items];
        ActionCount = view.ActionCount;
    }
}
