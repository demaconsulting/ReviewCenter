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

using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="ProjectPage"/> component, rendered in memory with bUnit.
/// </summary>
public class ProjectPageTests : PageTestBase
{
    /// <summary>
    ///     Renders the page for a project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ProjectPage> RenderPage(int projectId)
    {
        return Render<ProjectPage>(parameters => parameters.Add(page => page.ProjectId, projectId));
    }

    /// <summary>
    ///     Selects files in the file input the way a browser would.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="files">The files to select as name and bytes pairs.</param>
    private static void SelectFiles(IRenderedComponent<ProjectPage> cut, params (string Name, byte[] Content)[] files)
    {
        cut.FindComponent<InputFile>().UploadFiles(
            files.Select(file => InputFileContent.CreateFromBinary(file.Content, file.Name)).ToArray());
    }

    /// <summary>
    ///     Test that an unknown project shows a not-found message and no form.
    /// </summary>
    [Fact]
    public void ProjectPage_Render_UnknownProject_ShowsNotFound()
    {
        // Arrange: no projects exist

        // Act: render the page for project 99
        var cut = RenderPage(99);

        // Assert: not-found is shown and there is no review form
        Assert.Contains("99", cut.Find("[data-testid='project-not-found']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='new-review-form']"));
    }

    /// <summary>
    ///     Test that a project without reviews shows its name and an empty message.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_ProjectWithoutReviews_ShowsNameAndEmptyMessage()
    {
        // Arrange: a project with a description and no reviews
        var project = await Projects.CreateAsync("Apollo", "Moon landing", TestContext.Current.CancellationToken);

        // Act: render the page
        var cut = RenderPage(project.Id);

        // Assert: name, description and the empty message
        Assert.Equal("Apollo", cut.Find("[data-testid='project-name']").TextContent);
        Assert.Equal("Moon landing", cut.Find("[data-testid='project-description']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='reviews-empty']"));
    }

    /// <summary>
    ///     Test that existing reviews are listed with id link, title and state.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_ProjectWithReview_ListsReviewWithLinkTitleAndState()
    {
        // Arrange: a project with one review
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await Reviews.CreateDraftAsync(project.Id, "REV-1", "First review", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);

        // Act: render the page
        var cut = RenderPage(project.Id);

        // Assert: the review row shows a link to the review, its title and its state
        var link = cut.Find("[data-testid='review-link-REV-1']");
        Assert.Equal("REV-1", link.TextContent);
        Assert.Equal($"/projects/{project.Id}/reviews/REV-1", link.GetAttribute("href"));
        Assert.Equal("First review", cut.Find("[data-testid='review-title-REV-1']").TextContent);
        Assert.Equal("Draft", cut.Find("[data-testid='review-state-REV-1']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='reviews-empty']"));
    }

    /// <summary>
    ///     Test that choosing files lists them with their names and sizes.
    /// </summary>
    [Fact]
    public async Task ProjectPage_FileInput_FilesSelected_ListsNamesAndSizes()
    {
        // Arrange: a project and its rendered page
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var cut = RenderPage(project.Id);

        // Act: select two files
        SelectFiles(cut, ("a.txt", TestContent.A), ("c.bin", TestContent.C));

        // Assert: both are listed with name and size
        await cut.WaitForAssertionAsync(() => Assert.Equal("a.txt", cut.Find("[data-testid='selected-file-path-0']").TextContent));
        Assert.Equal("c.bin", cut.Find("[data-testid='selected-file-path-1']").TextContent);
        Assert.Equal($"{TestContent.A.Length} bytes", cut.Find("[data-testid='selected-file-size-0']").TextContent);
        Assert.Equal($"{TestContent.C.Length} bytes", cut.Find("[data-testid='selected-file-size-1']").TextContent);
    }

    /// <summary>
    ///     Test that submitting a valid form stores the review with verbatim bytes and opens the review page.
    /// </summary>
    [Fact]
    public async Task ProjectPage_CreateForm_ValidInput_CreatesReviewAndNavigatesToIt()
    {
        // Arrange: a project, the form filled in, and binary and text files selected
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var cut = RenderPage(project.Id);
        await cut.Find("[data-testid='review-id-input']").InputAsync("REV-1");
        await cut.Find("[data-testid='review-title-input']").InputAsync("First review");
        SelectFiles(cut, ("a.txt", TestContent.A), ("c.bin", TestContent.C));
        await cut.WaitForAssertionAsync(() => Assert.NotEmpty(cut.FindAll("[data-testid='selected-file-0']")));

        // Act: submit the form
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the review exists with the exact bytes and the browser was sent to it
        var review = await Reviews.GetAsync(project.Id, "REV-1", TestContext.Current.CancellationToken);
        Assert.Equal("First review", review?.Title);
        Assert.Equal(Fingerprint.Compute(new[] { TestContent.A, TestContent.C }), review?.ContentFingerprint);
        Assert.Equal(TestContent.C, await Reviews.GetFileContentAsync(project.Id, "REV-1", "c.bin", TestContext.Current.CancellationToken));
        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith($"/projects/{project.Id}/reviews/REV-1", navigation.Uri, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that submitting without files shows the empty-file-set error and creates nothing.
    /// </summary>
    [Fact]
    public async Task ProjectPage_CreateForm_NoFiles_ShowsErrorAndCreatesNothing()
    {
        // Arrange: a project with id and title entered but no files chosen
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var cut = RenderPage(project.Id);
        await cut.Find("[data-testid='review-id-input']").InputAsync("REV-1");
        await cut.Find("[data-testid='review-title-input']").InputAsync("First review");

        // Act: submit the form
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the error mentions files and nothing was stored
        Assert.Contains("file", cut.Find("[data-testid='review-error']").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await Reviews.ListAsync(project.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a duplicate review id is shown as an error naming the id, keeping the form values.
    /// </summary>
    [Fact]
    public async Task ProjectPage_CreateForm_DuplicateReviewId_ShowsErrorNamingTheId()
    {
        // Arrange: a project that already has REV-1, and a form that tries to create it again
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await Reviews.CreateDraftAsync(project.Id, "REV-1", "First", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        var cut = RenderPage(project.Id);
        await cut.Find("[data-testid='review-id-input']").InputAsync("REV-1");
        await cut.Find("[data-testid='review-title-input']").InputAsync("Again");
        SelectFiles(cut, ("b.txt", TestContent.B));
        await cut.WaitForAssertionAsync(() => Assert.NotEmpty(cut.FindAll("[data-testid='selected-file-0']")));

        // Act: submit the form
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the error names the id, only one review exists, and the form kept the typed id
        Assert.Contains("REV-1", cut.Find("[data-testid='review-error']").TextContent, StringComparison.Ordinal);
        Assert.Single(await Reviews.ListAsync(project.Id, TestContext.Current.CancellationToken));
        Assert.Equal("REV-1", cut.Find("[data-testid='review-id-input']").GetAttribute("value"));
    }

    /// <summary>
    ///     Test that two selected files with the same name are reported as a duplicate path on submit.
    /// </summary>
    [Fact]
    public async Task ProjectPage_CreateForm_DuplicateFileNames_ShowsDuplicatePathError()
    {
        // Arrange: a project and two selected files with the same name
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var cut = RenderPage(project.Id);
        await cut.Find("[data-testid='review-id-input']").InputAsync("REV-1");
        await cut.Find("[data-testid='review-title-input']").InputAsync("First review");
        SelectFiles(cut, ("same.txt", TestContent.A), ("same.txt", TestContent.B));
        await cut.WaitForAssertionAsync(() => Assert.NotEmpty(cut.FindAll("[data-testid='selected-file-1']")));

        // Act: submit the form
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the error names the path and no review was created
        Assert.Contains("same.txt", cut.Find("[data-testid='review-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(await Reviews.ListAsync(project.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a selected file over the size limit is rejected with a clear error and not kept, and that
    ///     submitting afterwards keeps that reason visible instead of replacing it.
    /// </summary>
    [Fact]
    public async Task ProjectPage_FileInput_FileTooLarge_ShowsErrorAndKeepsNothing()
    {
        // Arrange: a project and a file one byte over the limit
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var cut = RenderPage(project.Id);
        await cut.Find("[data-testid='review-id-input']").InputAsync("REV-1");
        await cut.Find("[data-testid='review-title-input']").InputAsync("First review");

        // Act: select the oversized file, then submit the form
        SelectFiles(cut, ("big.bin", new byte[ReviewLimits.MaxFileSizeBytes + 1]));
        await cut.WaitForAssertionAsync(() => Assert.Contains("big.bin", cut.Find("[data-testid='review-error']").TextContent, StringComparison.Ordinal));
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the error still names the file, the selection list is empty and nothing was stored
        Assert.Contains("big.bin", cut.Find("[data-testid='review-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='selected-files-list']"));
        Assert.Empty(await Reviews.ListAsync(project.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a selection of more files than a review may have is rejected as a whole with a clear
    ///     error, and that submitting afterwards keeps that reason visible and stores nothing.
    /// </summary>
    [Fact]
    public async Task ProjectPage_FileInput_TooManyFiles_ShowsErrorAndKeepsNothing()
    {
        // Arrange: a project and one more tiny file than the limit allows
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var cut = RenderPage(project.Id);
        await cut.Find("[data-testid='review-id-input']").InputAsync("REV-1");
        await cut.Find("[data-testid='review-title-input']").InputAsync("First review");
        var files = Enumerable.Range(0, ReviewLimits.MaxFileCount + 1)
            .Select(i => ($"file-{i}.txt", TestContent.A))
            .ToArray();

        // Act: select the files, then submit the form
        SelectFiles(cut, files);
        var limit = ReviewLimits.MaxFileCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await cut.WaitForAssertionAsync(() => Assert.Contains(limit, cut.Find("[data-testid='review-error']").TextContent, StringComparison.Ordinal));
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the error still names the limit, the selection list is empty and nothing was stored
        Assert.Contains(limit, cut.Find("[data-testid='review-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='selected-files-list']"));
        Assert.Empty(await Reviews.ListAsync(project.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a selection whose files are each within the file limit but together exceed the total limit
    ///     is rejected as a whole with a clear error, and that submitting afterwards stores nothing.
    /// </summary>
    [Fact]
    public async Task ProjectPage_FileInput_TotalSizeTooLarge_ShowsErrorAndKeepsNothing()
    {
        // Arrange: a project and enough files at the per-file limit (sharing one buffer) to pass the total limit
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var cut = RenderPage(project.Id);
        await cut.Find("[data-testid='review-id-input']").InputAsync("REV-1");
        await cut.Find("[data-testid='review-title-input']").InputAsync("First review");
        var content = new byte[ReviewLimits.MaxFileSizeBytes];
        var count = (int)(ReviewLimits.MaxTotalSizeBytes / ReviewLimits.MaxFileSizeBytes) + 1;
        var files = Enumerable.Range(0, count)
            .Select(i => ($"part-{i}.bin", content))
            .ToArray();

        // Act: select the files, then submit the form
        SelectFiles(cut, files);
        await cut.WaitForAssertionAsync(() => Assert.Contains("together", cut.Find("[data-testid='review-error']").TextContent, StringComparison.Ordinal));
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the error still names the total limit, the selection list is empty and nothing was stored
        Assert.Contains("together", cut.Find("[data-testid='review-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='selected-files-list']"));
        Assert.Empty(await Reviews.ListAsync(project.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a client that declares small files but sends more bytes than the total limit allows is
    ///     rejected based on the bytes actually received.
    /// </summary>
    [Fact]
    public async Task ProjectPage_FileInput_DeclaredSizesUnderstated_RejectsOnReceivedBytes()
    {
        // Arrange: files that declare one byte each but stream the per-file maximum, enough to pass the total limit
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var cut = RenderPage(project.Id);
        var count = (int)(ReviewLimits.MaxTotalSizeBytes / ReviewLimits.MaxFileSizeBytes) + 1;
        var content = new byte[ReviewLimits.MaxFileSizeBytes];
        IReadOnlyList<IBrowserFile> files = Enumerable.Range(0, count)
            .Select(i => (IBrowserFile)new UnderstatedBrowserFile($"part-{i}.bin", content))
            .ToArray();

        // Act: deliver the selection to the file input's change callback
        await cut.InvokeAsync(() => cut.FindComponent<InputFile>().Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs(files)));

        // Assert: the total-limit error is shown and no file is kept
        Assert.Contains("together", cut.Find("[data-testid='review-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='selected-files-list']"));
    }

    /// <summary>
    ///     A browser file that reports a size of one byte but streams the given content.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <param name="content">The bytes actually streamed.</param>
    private sealed class UnderstatedBrowserFile(string name, byte[] content) : IBrowserFile
    {
        /// <inheritdoc />
        public string Name { get; } = name;

        /// <inheritdoc />
        public DateTimeOffset LastModified { get; } = DateTimeOffset.UnixEpoch;

        /// <inheritdoc />
        public long Size => 1;

        /// <inheritdoc />
        public string ContentType => "application/octet-stream";

        /// <inheritdoc />
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            return new MemoryStream(content, writable: false);
        }
    }

    /// <summary>
    ///     Test that a new selection replaces the previous one.
    /// </summary>
    [Fact]
    public async Task ProjectPage_FileInput_SecondSelection_ReplacesFirst()
    {
        // Arrange: a project with a first selection
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var cut = RenderPage(project.Id);
        SelectFiles(cut, ("first.txt", TestContent.A));
        await cut.WaitForAssertionAsync(() => Assert.Equal("first.txt", cut.Find("[data-testid='selected-file-path-0']").TextContent));

        // Act: select another file
        SelectFiles(cut, ("second.txt", TestContent.B));

        // Assert: only the second selection is listed
        await cut.WaitForAssertionAsync(() => Assert.Equal("second.txt", cut.Find("[data-testid='selected-file-path-0']").TextContent));
        Assert.Empty(cut.FindAll("[data-testid='selected-file-1']"));
    }

    /// <summary>
    ///     Test that navigating to another project clears the form and the selection of the previous one.
    /// </summary>
    [Fact]
    public async Task ProjectPage_ParametersChange_OtherProject_ClearsFormAndSelection()
    {
        // Arrange: a page for the first project with a filled form, a selected file and an error
        var first = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var second = await Projects.CreateAsync("Gemini", null, TestContext.Current.CancellationToken);
        var cut = RenderPage(first.Id);
        await cut.Find("[data-testid='review-id-input']").InputAsync("REV-1");
        await cut.Find("[data-testid='review-title-input']").InputAsync("First review");
        SelectFiles(cut, ("a.txt", TestContent.A));
        await cut.WaitForAssertionAsync(() => Assert.NotEmpty(cut.FindAll("[data-testid='selected-file-0']")));

        // Act: the router reuses the component for the other project
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(ProjectPage.ProjectId)] = second.Id })));

        // Assert: the new project is shown with an empty form and no selected files
        await cut.WaitForAssertionAsync(() => Assert.Equal("Gemini", cut.Find("[data-testid='project-name']").TextContent));
        Assert.Equal(string.Empty, cut.Find("[data-testid='review-id-input']").GetAttribute("value") ?? string.Empty);
        Assert.Equal(string.Empty, cut.Find("[data-testid='review-title-input']").GetAttribute("value") ?? string.Empty);
        Assert.Empty(cut.FindAll("[data-testid='selected-file-0']"));
    }

    /// <summary>
    ///     Test that the interactive marker is set once the component has rendered on a live circuit.
    /// </summary>
    [Fact]
    public void ProjectPage_AfterRender_Interactive_SetsMarker()
    {
        // Arrange: no setup required

        // Act: render the page
        var cut = RenderPage(1);

        // Assert: the root element reports interactivity
        Assert.Equal("true", cut.Find("[data-testid='project-page']").GetAttribute("data-interactive"));
    }
}
