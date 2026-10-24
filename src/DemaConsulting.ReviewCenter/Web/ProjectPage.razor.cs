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
using Microsoft.AspNetCore.Components.Forms;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the project page (<c>ProjectPage.razor</c>): shows one project, lists its reviews and
///     creates a new review from uploaded files.
/// </summary>
/// <remarks>
///     The page talks to the domain only through <see cref="ProjectService"/> and <see cref="ReviewService"/>,
///     which own the rules; a rule violation is shown next to the form instead of being thrown. The upload
///     limits are enforced here as well, before the bytes are read, because a browser file can be far larger
///     than the domain accepts. Browsers give a file input only the file name, so the path of an uploaded
///     file is its name and two files with the same name are rejected as duplicates by the service. Each
///     browser connection (circuit) gets its own page instance, so the state here is per-user and needs no
///     synchronization. There is no authentication yet: every visitor is the same anonymous operator.
/// </remarks>
public partial class ProjectPage : ComponentBase
{
    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     The project identity whose data is currently loaded, or <c>null</c> before the first load.
    /// </summary>
    private int? _loadedProjectId;

    /// <summary>
    ///     Gets or sets the identity of the project to show, from the page address.
    /// </summary>
    [Parameter]
    public int ProjectId { get; set; }

    /// <summary>
    ///     Gets or sets the service that reads projects, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectService ProjectService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that lists and creates reviews, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ReviewService ReviewService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the navigation manager, supplied by dependency injection; used to open the new review.
    /// </summary>
    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    /// <summary>
    ///     Gets a value indicating whether the project lookup has finished.
    /// </summary>
    private bool Loaded { get; set; }

    /// <summary>
    ///     Gets the project shown, or <c>null</c> when it does not exist.
    /// </summary>
    private Project? Project { get; set; }

    /// <summary>
    ///     Gets the reviews of the project.
    /// </summary>
    private IReadOnlyList<Review> Reviews { get; set; } = [];

    /// <summary>
    ///     Gets or sets the review id typed into the form.
    /// </summary>
    private string ReviewId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the review title typed into the form.
    /// </summary>
    private string ReviewTitle { get; set; } = string.Empty;

    /// <summary>
    ///     Gets the files chosen in the file input, with their bytes already read.
    /// </summary>
    private IReadOnlyList<UploadedFile> SelectedFiles { get; set; } = [];

    /// <summary>
    ///     Gets the message of the last rejected selection or submission, or <c>null</c> when there is none.
    /// </summary>
    private string? ErrorMessage { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the last file selection was rejected for breaking an upload limit.
    /// </summary>
    private bool SelectionRejected { get; set; }

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Loads the project and its reviews whenever the project identity changes.
    /// </summary>
    /// <returns>A task that completes when the data is loaded.</returns>
    protected override async Task OnParametersSetAsync()
    {
        // The router reuses this instance when only the project id changes; drop the previous project's
        // data and form state so it can neither be shown nor submitted into the new project
        if (_loadedProjectId != ProjectId)
        {
            _loadedProjectId = ProjectId;
            Loaded = false;
            Project = null;
            Reviews = [];
            ReviewId = string.Empty;
            ReviewTitle = string.Empty;
            SelectedFiles = [];
            ErrorMessage = null;
            SelectionRejected = false;
        }

        Project = await ProjectService.GetAsync(ProjectId);
        Reviews = Project is null ? [] : await ReviewService.ListAsync(ProjectId);
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
    ///     Reads the files chosen in the file input into memory, enforcing the upload limits.
    /// </summary>
    /// <param name="e">The change event that carries the chosen files.</param>
    /// <returns>A task that completes when the files are read or the selection was rejected.</returns>
    /// <remarks>
    ///     A new selection replaces the previous one, like a native file input. A selection that breaks a
    ///     limit is rejected as a whole: nothing is kept and the reason is shown, so a half-read file set can
    ///     never be submitted by accident.
    /// </remarks>
    private async Task OnFilesSelectedAsync(InputFileChangeEventArgs e)
    {
        ErrorMessage = null;
        SelectionRejected = false;
        SelectedFiles = [];

        // Reject a selection with too many files before touching any of them
        if (e.FileCount > ReviewLimits.MaxFileCount)
        {
            ErrorMessage = $"A review can have at most {ReviewLimits.MaxFileCount} files.";
            SelectionRejected = true;
            return;
        }

        // Check the declared sizes first so that nothing too large is ever read
        var browserFiles = e.GetMultipleFiles(ReviewLimits.MaxFileCount);
        long total = 0;
        foreach (var browserFile in browserFiles)
        {
            if (browserFile.Size > ReviewLimits.MaxFileSizeBytes)
            {
                ErrorMessage = $"The file '{browserFile.Name}' is larger than the limit of {ReviewLimits.MaxFileSizeBytes / 1024 / 1024} MiB.";
                SelectionRejected = true;
                return;
            }

            total += browserFile.Size;
        }

        if (total > ReviewLimits.MaxTotalSizeBytes)
        {
            ErrorMessage = $"The files together are larger than the limit of {ReviewLimits.MaxTotalSizeBytes / 1024 / 1024} MiB.";
            SelectionRejected = true;
            return;
        }

        // Read each file into memory, keeping the bytes exactly as sent
        var files = new List<UploadedFile>(browserFiles.Count);
        foreach (var browserFile in browserFiles)
        {
            // The explicit limit is the domain maximum (2 MiB); the analyzer cannot see through the constant
#pragma warning disable S5693 // Content length is limited to ReviewLimits.MaxFileSizeBytes
            await using var stream = browserFile.OpenReadStream(ReviewLimits.MaxFileSizeBytes);
#pragma warning restore S5693
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            files.Add(new UploadedFile(browserFile.Name, buffer.ToArray()));
        }

        SelectedFiles = files;
    }

    /// <summary>
    ///     Creates the review from the form values and opens it.
    /// </summary>
    /// <returns>A task that completes when the submission has been handled.</returns>
    /// <remarks>
    ///     On a rule violation (duplicate id, no files, duplicate paths and so on) the form keeps what was
    ///     entered and the message is shown. On success the browser is taken to the new review.
    /// </remarks>
    private async Task CreateReviewAsync()
    {
        // A rejected selection already explained itself; do not replace that reason with "no files"
        if (SelectionRejected)
        {
            return;
        }

        Review review;
        try
        {
            review = await ReviewService.CreateDraftAsync(ProjectId, ReviewId, ReviewTitle, SelectedFiles);
        }
        catch (DomainRuleException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        // Success: show the review that was just created
        ErrorMessage = null;
        Navigation.NavigateTo($"/projects/{ProjectId}/reviews/{Uri.EscapeDataString(review.Id)}");
    }
}
