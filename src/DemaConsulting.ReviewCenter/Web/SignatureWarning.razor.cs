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
using Microsoft.AspNetCore.Components;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the signature warning (<c>SignatureWarning.razor</c>): the inline banner that names the
///     signatures a change would make out of date and asks the person to confirm or cancel the change.
/// </summary>
/// <remarks>
///     The component only shows what its parent gives it; the parent decides what Confirm and Cancel do, so it holds
///     no state of its own and needs no synchronization. It is a helper of the review page and the checklist page.
/// </remarks>
public partial class SignatureWarning : ComponentBase
{
    /// <summary>
    ///     Gets or sets the signatures the change would make out of date; the parent supplies them.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public IReadOnlyList<AffectedSignature> Slots { get; set; } = [];

    /// <summary>
    ///     Gets or sets what happens when the person confirms the change.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public EventCallback OnConfirm { get; set; }

    /// <summary>
    ///     Gets or sets what happens when the person cancels the change.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public EventCallback OnCancel { get; set; }

    /// <summary>
    ///     Gets the signatures as text, for example <c>Slot 1 (Riley Reviewer), Slot 3 (Sam Signer)</c>.
    /// </summary>
    private string SlotsText => string.Join(", ", Slots.Select(slot => $"Slot {slot.SlotNumber} ({slot.SignerDisplayName})"));
}

/// <summary>
///     A change that waits for the person to confirm that it makes signatures out of date.
/// </summary>
/// <param name="Key">Which control the change belongs to; the banner is shown only there.</param>
/// <param name="Slots">Every signature the person is asked to confirm, merged over all refusals so far.</param>
/// <param name="Retry">Repeats the change, given the signatures that are confirmed.</param>
internal sealed record PendingSignatureWarning(
    string Key,
    IReadOnlyList<AffectedSignature> Slots,
    Func<IReadOnlyList<AffectedSignature>, Task> Retry)
{
    /// <summary>
    ///     Creates the warning for a change that was refused because it would make signatures out of date.
    /// </summary>
    /// <param name="key">Which control the change belongs to.</param>
    /// <param name="confirmed">The signatures the person has already confirmed; <c>null</c> for none.</param>
    /// <param name="refusal">The refusal; it lists the signatures that were not yet confirmed.</param>
    /// <param name="retry">Repeats the change, given the signatures that are confirmed.</param>
    /// <returns>A warning that lists the confirmed signatures and the newly named ones, each once.</returns>
    /// <remarks>
    ///     The service names only the signatures that were not acknowledged. After a second refusal the warning shows
    ///     both groups, so a confirmation always covers exactly what the person was shown.
    /// </remarks>
    public static PendingSignatureWarning Create(
        string key,
        IReadOnlyList<AffectedSignature>? confirmed,
        DomainRuleException refusal,
        Func<IReadOnlyList<AffectedSignature>, Task> retry)
    {
        var slots = (confirmed ?? [])
            .Concat(refusal.AffectedSlots)
            .GroupBy(slot => slot.SlotId)
            .Select(group => group.First())
            .OrderBy(slot => slot.SlotNumber)
            .ToList();
        return new PendingSignatureWarning(key, slots, retry);
    }
}
