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

using System.Text;
using DemaConsulting.ReviewCenter.Core;
using Microsoft.AspNetCore.Components;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the review page (<c>ReviewPage.razor</c>): the read-only view of one review and of the
///     content of its files.
/// </summary>
/// <remarks>
///     The page reads through <see cref="ReviewService"/> only. File bytes are loaded one file at a time when
///     the operator selects it, never with the file list. A file is shown as text only when it is valid UTF-8
///     without NUL bytes; anything else is reported as a binary file rather than rendered as garbage. The text
///     is shown exactly as stored (no line-ending conversion). Each browser connection (circuit) gets its own
///     page instance, so the state here is per-user and needs no synchronization.
/// </remarks>
public partial class ReviewPage : ComponentBase
{
    /// <summary>
    ///     A strict UTF-8 decoder that throws on invalid bytes, used to tell text from binary content.
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     Gets or sets the identity of the owning project, from the page address.
    /// </summary>
    [Parameter]
    public int ProjectId { get; set; }

    /// <summary>
    ///     Gets or sets the review identifier, from the page address.
    /// </summary>
    [Parameter]
    public string ReviewId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the service that reads reviews, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ReviewService ReviewService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that reads projects, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectService ProjectService { get; set; } = null!;

    /// <summary>
    ///     Gets the name of the owning project shown in the breadcrumb, or <c>null</c> when it is unknown.
    /// </summary>
    private string? ProjectName { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the review lookup has finished.
    /// </summary>
    private bool Loaded { get; set; }

    /// <summary>
    ///     Gets the review shown, or <c>null</c> when it does not exist.
    /// </summary>
    private Review? Review { get; set; }

    /// <summary>
    ///     Gets the path of the file whose content is shown, or <c>null</c> when no file is selected.
    /// </summary>
    private string? SelectedPath { get; set; }

    /// <summary>
    ///     Gets the text of the selected file, or <c>null</c> when no file is selected or it is binary.
    /// </summary>
    private string? SelectedText { get; set; }

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Loads the review whenever the address parameters change, and clears the file selection.
    /// </summary>
    /// <returns>A task that completes when the review is loaded.</returns>
    protected override async Task OnParametersSetAsync()
    {
        Review = await ReviewService.GetAsync(ProjectId, ReviewId);
        ProjectName = (await ProjectService.GetAsync(ProjectId))?.Name;
        SelectedPath = null;
        SelectedText = null;
        Loaded = true;
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
    ///     Loads and shows the content of one file of the review.
    /// </summary>
    /// <param name="index">The zero-based position of the file in the displayed list.</param>
    /// <returns>A task that completes when the content is loaded.</returns>
    /// <remarks>
    ///     An index outside the list is ignored. A file that cannot be found in the store (removed since the
    ///     page was loaded) is shown as empty text rather than failing the circuit.
    /// </remarks>
    private async Task SelectFileAsync(int index)
    {
        if (Review is null || index < 0 || index >= Review.Files.Count)
        {
            return;
        }

        // Load the bytes of just this file
        var path = Review.Files[index].Path;
        var content = await ReviewService.GetFileContentAsync(ProjectId, ReviewId, path) ?? [];

        // Show text only when it is clean UTF-8; otherwise tell the operator it is binary
        SelectedPath = path;
        SelectedText = TryDecodeText(content);
    }

    /// <summary>
    ///     Decodes file bytes as text when they are text.
    /// </summary>
    /// <param name="content">The exact file bytes.</param>
    /// <returns>The text, or <c>null</c> when the bytes contain a NUL byte or are not valid UTF-8.</returns>
    private static string? TryDecodeText(byte[] content)
    {
        // A NUL byte is never part of a text file, even when the rest happens to be valid UTF-8
        if (Array.IndexOf(content, (byte)0) >= 0)
        {
            return null;
        }

        try
        {
            return StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
