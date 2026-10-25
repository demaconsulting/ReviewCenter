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

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the audit page (<c>AuditPage.razor</c>): the whole audit trail for administrators, and the
///     button that verifies its hash chain.
/// </summary>
/// <remarks>
///     An attribute on the page limits entry to administrators. An open page re-checks its user only every
///     <see cref="AuthOptions.RevalidationSeconds"/>, so the page also asks the user store, when it loads and
///     when the verification is started, whether the signed-in user is still an enabled administrator, and refuses
///     with an error otherwise (defense in depth). The verification is a click on a live circuit, not a form post,
///     so the framework's circuit protection applies instead of an antiforgery token. The page talks to the domain
///     only through <see cref="AuditService"/> and <see cref="UserService"/>, and changes nothing. The position in the
///     trail is carried in the address (<c>?before=</c> for an older page, <c>?after=</c> for a newer one). Each
///     browser connection (circuit) gets its own page instance, so the state here is per-user and needs no
///     synchronization.
/// </remarks>
public partial class AuditPage : ComponentBase
{
    /// <summary>
    ///     The message shown when the signed-in user is not (or no longer) an enabled administrator.
    /// </summary>
    private const string AdminRequired = "Administrator rights are required.";

    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     Gets or sets the sequence to start before (the "Older" position), from the <c>before</c> query value.
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "before")]
    public long? Before { get; set; }

    /// <summary>
    ///     Gets or sets the sequence to start after (the "Newer" position), from the <c>after</c> query value.
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "after")]
    public long? After { get; set; }

    /// <summary>
    ///     Gets or sets the service that reads and verifies the audit trail, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuditService AuditService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that reads users, supplied by dependency injection.
    /// </summary>
    [Inject]
    private UserService UserService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets a value indicating whether the lookup has finished.
    /// </summary>
    private bool Loaded { get; set; }

    /// <summary>
    ///     Gets the page of entries shown, or <c>null</c> when it is not loaded (or the user is refused).
    /// </summary>
    private AuditEntryPage? Page { get; set; }

    /// <summary>
    ///     Gets the result of the last verification, or <c>null</c> when none was run.
    /// </summary>
    private AuditVerificationResult? Result { get; set; }

    /// <summary>
    ///     Gets the message of the last refusal, or <c>null</c> when there is none.
    /// </summary>
    private string? ErrorMessage { get; set; }

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Loads the page of entries whenever the address changes, for an administrator only.
    /// </summary>
    /// <returns>A task that completes when the page is loaded or refused.</returns>
    protected override async Task OnParametersSetAsync()
    {
        // Clear the previous page before the first await so a stale page is never shown under a new address
        Loaded = false;
        Page = null;
        ErrorMessage = null;

        if (await RequireAdministratorAsync())
        {
            Page = await AuditService.ListAsync(null, null, Before, After);
        }

        Loaded = true;
    }

    /// <summary>
    ///     Marks the page interactive after it has rendered on a live circuit.
    /// </summary>
    /// <param name="firstRender"><c>true</c> for the first render of this component instance.</param>
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
    ///     Verifies the whole chain and shows the result.
    /// </summary>
    /// <returns>A task that completes when the verification has finished or was refused.</returns>
    /// <remarks>
    ///     Refused, without reading the trail, unless the signed-in user is still an enabled administrator.
    /// </remarks>
    private async Task VerifyAsync()
    {
        Result = null;
        ErrorMessage = null;
        if (!await RequireAdministratorAsync())
        {
            return;
        }

        Result = await AuditService.VerifyAsync();
    }

    /// <summary>
    ///     Checks that the signed-in user is still an enabled administrator in the user store.
    /// </summary>
    /// <returns><c>true</c> when so; otherwise <c>false</c>, with <see cref="ErrorMessage"/> set.</returns>
    private async Task<bool> RequireAdministratorAsync()
    {
        // An ended sign-in state is shown as a message, not raised as a fault
        Actor actor;
        try
        {
            actor = await AuthenticationState.GetActorAsync();
        }
        catch (DomainRuleException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }

        var current = await UserService.GetAsync(actor.Username);
        if (current is not { Enabled: true, Role: UserRole.Admin })
        {
            ErrorMessage = AdminRequired;
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Formats the time of an entry in UTC.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The time as <c>yyyy-MM-dd HH:mm:ss</c> in UTC.</returns>
    private static string FormatTime(AuditEntry entry)
    {
        return entry.TimestampUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Describes the review an entry belongs to.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The project and review ids, or the project id alone, or a dash for an entry without either.</returns>
    private static string ReviewLabel(AuditEntry entry)
    {
        if (entry.ProjectId is null)
        {
            return "-";
        }

        return entry.ReviewId is null ? $"project {entry.ProjectId}" : $"{entry.ProjectId}/{entry.ReviewId}";
    }

    /// <summary>
    ///     Builds the address of another page of the trail.
    /// </summary>
    /// <param name="key">The query key: <c>before</c> or <c>after</c>.</param>
    /// <param name="sequence">The sequence to carry; <c>null</c> gives the first page.</param>
    /// <returns>The address, relative to the site root.</returns>
    private static string PageLink(string key, long? sequence)
    {
        return sequence is { } value ? $"/audit?{key}={value.ToString(CultureInfo.InvariantCulture)}" : "/audit";
    }
}
