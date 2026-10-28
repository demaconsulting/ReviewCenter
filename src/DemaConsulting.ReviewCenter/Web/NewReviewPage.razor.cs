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
using Microsoft.AspNetCore.Components.Forms;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the new review page (<c>NewReviewPage.razor</c>): creates a review in a project from uploaded
///     files or from a git repository.
/// </summary>
/// <remarks>
///     The page talks to the domain only through <see cref="ProjectService"/>, <see cref="ReviewService"/> and
///     <see cref="RepositoryService"/>, which own the rules; a rule violation is shown next to the form instead of
///     being thrown. The upload limits are enforced here as well, before the bytes are read, because a browser file
///     can be far larger than the domain accepts. Browsers give a file input only the file name, so the path of an
///     uploaded file is its name and two files with the same name are rejected as duplicates by the service. Each
///     browser connection (circuit) gets its own page instance, so the state here is per-user and needs no
///     synchronization. The page requires a signed-in user.
/// </remarks>
public partial class NewReviewPage : ComponentBase
{
    /// <summary>
    ///     The pattern the selection starts with: every changed file.
    /// </summary>
    private const string DefaultSelection = "**/*";

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
    ///     Gets or sets the identity of the project the review is created in, from the page address.
    /// </summary>
    [Parameter]
    public int ProjectId { get; set; }

    /// <summary>
    ///     Gets or sets the service that reads projects, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectService ProjectService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that creates reviews, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ReviewService ReviewService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the repository registry, supplied by dependency injection; used only to list the enabled repositories.
    /// </summary>
    [Inject]
    private RepositoryService RepositoryService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the project access rules that every action re-checks, supplied by dependency injection.
    /// </summary>
    [Inject]
    private IProjectAccess Access { get; set; } = null!;

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
    ///     Gets the project the review is created in, or <c>null</c> when it does not exist.
    /// </summary>
    private Project? Project { get; set; }

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
    ///     Gets the enabled repositories a review can be created from (name only; never location or credentials).
    /// </summary>
    private IReadOnlyList<RepositorySummary> Repositories { get; set; } = [];

    /// <summary>
    ///     Gets a value indicating whether the new review reads its files from a repository instead of an upload.
    /// </summary>
    private bool FromRepository { get; set; }

    /// <summary>
    ///     Gets or sets the identity of the chosen repository as the text of the select element; empty for none.
    /// </summary>
    private string RepositoryIdText { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the head revision typed into the form.
    /// </summary>
    private string HeadRevision { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the optional baseline revision typed into the form.
    /// </summary>
    private string BaselineRevision { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the file selection typed into the form, one pattern per line.
    /// </summary>
    private string Selection { get; set; } = DefaultSelection;

    /// <summary>
    ///     Gets a value indicating whether a submission is running.
    /// </summary>
    private bool Busy { get; set; }

    /// <summary>
    ///     Gets the review that was just created while the skipped entries are being shown, or <c>null</c>.
    /// </summary>
    private Review? CreatedReview { get; set; }

    /// <summary>
    ///     Gets the entries that were left out of the review that was just created.
    /// </summary>
    private IReadOnlyList<SkippedEntry> SkippedEntries { get; set; } = [];

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
    ///     Loads the project and the repositories whenever the project identity changes.
    /// </summary>
    /// <returns>A task that completes when the data is loaded.</returns>
    protected override async Task OnParametersSetAsync()
    {
        // The router reuses this instance when only the project id changes; drop the previous project's
        // form state so it can neither be shown nor submitted into the new project
        if (_loadedProjectId != ProjectId)
        {
            _loadedProjectId = ProjectId;
            Loaded = false;
            Project = null;
            ReviewId = string.Empty;
            ReviewTitle = string.Empty;
            SelectedFiles = [];
            ErrorMessage = null;
            SelectionRejected = false;
            FromRepository = false;
            RepositoryIdText = string.Empty;
            HeadRevision = string.Empty;
            BaselineRevision = string.Empty;
            Selection = DefaultSelection;
            CreatedReview = null;
            SkippedEntries = [];
        }

        var actor = await AuthenticationState.GetActorOrNullAsync();
        Project = await ProjectService.GetAsync(actor, ProjectId);
        Repositories = actor is not null && Project is not null
            ? await RepositoryService.ListEnabledAsync(actor, ProjectId)
            : [];
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

        // Read each file into memory, keeping the bytes exactly as sent. The declared sizes come from the
        // client, so the bytes actually received are counted too, and reading stops at the total limit
        var files = new List<UploadedFile>(browserFiles.Count);
        long received = 0;
        foreach (var browserFile in browserFiles)
        {
            // The explicit limit is the domain maximum (2 MiB); the analyzer cannot see through the constant
#pragma warning disable S5693 // Content length is limited to ReviewLimits.MaxFileSizeBytes
            await using var stream = browserFile.OpenReadStream(ReviewLimits.MaxFileSizeBytes);
#pragma warning restore S5693
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            received += buffer.Length;
            if (received > ReviewLimits.MaxTotalSizeBytes)
            {
                ErrorMessage = $"The files together are larger than the limit of {ReviewLimits.MaxTotalSizeBytes / 1024 / 1024} MiB.";
                SelectionRejected = true;
                return;
            }

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
    ///     entered and the message is shown. On success the browser is taken to the new review. The submission
    ///     is refused, with a message, when the sign-in state of the page has already ended.
    /// </remarks>
    private async Task CreateReviewAsync()
    {
        if (!await GuardAsync())
        {
            return;
        }

        // A rejected selection already explained itself; do not replace that reason with "no files"
        if (SelectionRejected && !FromRepository)
        {
            return;
        }

        // A second submit while the first still runs (a fetch can take a while) would only repeat the work
        if (Busy)
        {
            return;
        }

        Busy = true;
        CreatedReview = null;
        try
        {
            // The address .../reviews/new is the form itself, so a review with that id could never be opened
            if (string.Equals(ReviewId.Trim(), "new", StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = "The review id 'new' is reserved. Choose another id.";
                return;
            }

            Review review;
            IReadOnlyList<SkippedEntry> skipped = [];
            try
            {
                // The actor is audited; asking for it also refuses an ended sign-in state
                var actor = await AuthenticationState.GetActorAsync();
                if (FromRepository)
                {
                    if (!int.TryParse(RepositoryIdText, out var repositoryId))
                    {
                        ErrorMessage = "Choose a repository.";
                        return;
                    }

                    var result = await ReviewService.CreateFromRepositoryAsync(
                        ProjectId,
                        ReviewId,
                        ReviewTitle,
                        repositoryId,
                        HeadRevision,
                        BaselineRevision,
                        Selection,
                        actor);
                    review = result.Review;
                    skipped = result.Skipped;
                }
                else
                {
                    review = await ReviewService.CreateDraftAsync(ProjectId, ReviewId, ReviewTitle, SelectedFiles, actor);
                }
            }
            catch (DomainRuleException ex)
            {
                ErrorMessage = ex.Message;
                return;
            }

            // Success: show the review that was just created. Entries that were left out (symbolic links, submodules)
            // are shown first, because they are not stored with the review and this is the only time to tell the creator
            ErrorMessage = null;
            if (skipped.Count > 0)
            {
                CreatedReview = review;
                SkippedEntries = skipped;
                return;
            }

            Navigation.NavigateTo($"/projects/{ProjectId}/reviews/{Uri.EscapeDataString(review.Id)}");
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>
    ///     Re-checks that the signed-in user may still view the project, at the start of an action on the open page.
    /// </summary>
    /// <returns>
    ///     <c>true</c> when the action may go on; <c>false</c> when the access is gone, in which case the page has
    ///     already dropped the project and its repositories and shows the not-found state, and the action must stop.
    /// </returns>
    private Task<bool> GuardAsync()
    {
        return Access.GuardViewAsync(AuthenticationState, ProjectId, ShowNoAccess);
    }

    /// <summary>
    ///     Runs an action that only changes what the page shows of the loaded repositories, after the access re-check.
    /// </summary>
    /// <param name="action">The action to run while the user may still view the project.</param>
    /// <returns>A task that completes when the action has run or the page has dropped its data.</returns>
    private async Task GuardedAsync(Action action)
    {
        if (await GuardAsync())
        {
            action();
        }
    }

    /// <summary>
    ///     Drops the project, its repositories and the form, and shows the same "does not exist" state as for a
    ///     non-member.
    /// </summary>
    private void ShowNoAccess()
    {
        Project = null;
        Repositories = [];
        ReviewId = string.Empty;
        ReviewTitle = string.Empty;
        SelectedFiles = [];
        ErrorMessage = null;
        SelectionRejected = false;
        FromRepository = false;
        RepositoryIdText = string.Empty;
        HeadRevision = string.Empty;
        BaselineRevision = string.Empty;
        Selection = DefaultSelection;
        CreatedReview = null;
        SkippedEntries = [];
        Loaded = true;
    }

    /// <summary>
    ///     Chooses where the files of the new review come from.
    /// </summary>
    /// <param name="fromRepository"><c>true</c> to read them from a repository; <c>false</c> to upload them.</param>
    private void ChooseSource(bool fromRepository)
    {
        FromRepository = fromRepository && Repositories.Count > 0;
        ErrorMessage = null;
    }
}
