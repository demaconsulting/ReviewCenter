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
///     Code-behind for the "Submit and sign" form (<c>SignConfirm.razor</c>): the password and optional second-factor
///     code that confirm a signature, and the primary button that hands the ballot in and signs.
/// </summary>
/// <remarks>
///     The component holds the typed credentials only until the form is sent: they are passed to
///     <see cref="OnSubmitAndSign"/> once, then cleared, and the fields are rebuilt empty, whatever the outcome. It
///     makes no decision; the parent hands the ballot in, signs and shows the result. A person who signs in with an
///     identity provider gets no fields (see <see cref="NeedsPassword"/>) and is passed an empty
///     <see cref="SignerCredentials"/>. Each browser connection gets its own instance, so the state needs no
///     synchronization.
/// </remarks>
public partial class SignConfirm : ComponentBase
{
    /// <summary>
    ///     The password typed so far; cleared when the form is sent.
    /// </summary>
    private string _password = string.Empty;

    /// <summary>
    ///     The second-factor code typed so far; cleared when the form is sent.
    /// </summary>
    private string _code = string.Empty;

    /// <summary>
    ///     How often the fields were emptied; part of the field keys so that the browser fields are rebuilt empty.
    /// </summary>
    private int _resets;

    /// <summary>
    ///     Gets or sets a value indicating whether the person confirms with a password; <c>false</c> for a person who
    ///     signs in with an identity provider.
    /// </summary>
    [Parameter]
    public bool NeedsPassword { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether the person has a second factor, so a current code is asked for too.
    /// </summary>
    [Parameter]
    public bool NeedsCode { get; set; }

    /// <summary>
    ///     Gets or sets what happens when the form is sent; it receives the typed credentials.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public EventCallback<SignerCredentials> OnSubmitAndSign { get; set; }

    /// <summary>
    ///     Hands the typed credentials to the parent and empties the fields.
    /// </summary>
    /// <returns>A task that completes when the parent has handled the form.</returns>
    private async Task SubmitAsync()
    {
        // Take the values out first so that nothing typed stays in the component, whatever the parent does
        var credentials = new SignerCredentials(
            NeedsPassword ? _password : null,
            NeedsPassword && NeedsCode ? _code : null);
        _password = string.Empty;
        _code = string.Empty;
        _resets++;
        await OnSubmitAndSign.InvokeAsync(credentials);
    }
}
