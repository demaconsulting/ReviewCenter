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
using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="ReviewPage"/> component, rendered in memory with bUnit.
/// </summary>
public class ReviewPageTests : PageTestBase
{
    /// <summary>
    ///     Creates a project with one review holding the given files.
    /// </summary>
    /// <param name="files">The files of the review, in the order they should be listed.</param>
    /// <returns>The project id.</returns>
    private async Task<int> CreateReviewAsync(params UploadedFile[] files)
    {
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await Reviews.CreateDraftAsync(project.Id, "REV-1", "First review", files, TestContext.Current.CancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Renders the page for a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review id.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ReviewPage> RenderPage(int projectId, string reviewId = "REV-1")
    {
        return Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, reviewId));
    }

    /// <summary>
    ///     Test that an unknown review shows a not-found message and no details.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_UnknownReview_ShowsNotFound()
    {
        // Arrange: a project without that review
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);

        // Act: render the page for a review that does not exist
        var cut = RenderPage(project.Id, "NOPE");

        // Assert: not-found names the review and no details are shown
        Assert.Contains("NOPE", cut.Find("[data-testid='review-not-found']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='review-fingerprint']"));
    }

    /// <summary>
    ///     Test that the review's id, title, state and fingerprint are shown.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ExistingReview_ShowsIdTitleStateAndFingerprint()
    {
        // Arrange: a review of three files with a known fingerprint
        var projectId = await CreateReviewAsync(
            new UploadedFile("a.txt", TestContent.A),
            new UploadedFile("b.txt", TestContent.B),
            new UploadedFile("c.bin", TestContent.C));

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: every header value is shown
        Assert.Equal("First review", cut.Find("[data-testid='review-title']").TextContent);
        Assert.Equal("REV-1", cut.Find("[data-testid='review-id']").TextContent);
        Assert.Equal("Draft", cut.Find("[data-testid='review-state']").TextContent);
        Assert.Equal(TestContent.FingerprintABC, cut.Find("[data-testid='review-fingerprint']").TextContent);
    }

    /// <summary>
    ///     Test that every file is listed with its path, size and SHA-256 hash.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ExistingReview_ListsFilesWithPathSizeAndHash()
    {
        // Arrange: a review of two files
        var projectId = await CreateReviewAsync(
            new UploadedFile("a.txt", TestContent.A),
            new UploadedFile("c.bin", TestContent.C));

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: path, size and hash of each file
        Assert.Equal("a.txt", cut.Find("[data-testid='file-path-0']").TextContent);
        Assert.Equal(TestContent.A.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), cut.Find("[data-testid='file-size-0']").TextContent);
        Assert.Equal(TestContent.HashA, cut.Find("[data-testid='file-hash-0']").TextContent);
        Assert.Equal("c.bin", cut.Find("[data-testid='file-path-1']").TextContent);
        Assert.Equal(TestContent.HashC, cut.Find("[data-testid='file-hash-1']").TextContent);
    }

    /// <summary>
    ///     Test that the breadcrumb names the owning project and the selected file's row is highlighted.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_FileSelected_ShowsProjectNameAndHighlightsRow()
    {
        // Arrange: a review of two files in the project named "Apollo"
        var projectId = await CreateReviewAsync(
            new UploadedFile("a.txt", TestContent.A),
            new UploadedFile("b.txt", TestContent.B));
        var cut = RenderPage(projectId);

        // Act: select the second file
        await cut.Find("[data-testid='file-select-1']").ClickAsync();

        // Assert: the breadcrumb shows the project name and only the selected row is highlighted
        Assert.Equal("Apollo", cut.Find("[data-testid='project-link']").TextContent);
        Assert.DoesNotContain("selected", cut.Find("[data-testid='file-row-0']").ClassList);
        Assert.Contains("selected", cut.Find("[data-testid='file-row-1']").ClassList);
    }

    /// <summary>
    ///     Test that no file content is shown until a file is selected.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_NoFileSelected_ShowsNoContent()
    {
        // Arrange: a review with one file
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: neither content nor binary notice is shown
        Assert.Empty(cut.FindAll("[data-testid='file-content']"));
        Assert.Empty(cut.FindAll("[data-testid='file-binary-notice']"));
    }

    /// <summary>
    ///     Test that selecting a text file shows its raw text in a preformatted block.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_TextFile_ShowsRawTextInPre()
    {
        // Arrange: a review with a text file that has markup-like characters
        var text = "line one\n  <b>indented & raw</b>\nline three\n";
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", Encoding.UTF8.GetBytes(text)));
        var cut = RenderPage(projectId);

        // Act: select the file
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: the text is shown escaped but otherwise unchanged, inside a pre element, with the path
        var pre = cut.Find("[data-testid='file-content']");
        Assert.Equal("pre", pre.TagName.ToLowerInvariant());
        Assert.Equal(text, pre.TextContent);
        Assert.Equal("a.txt", cut.Find("[data-testid='selected-file-path']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='file-binary-notice']"));
    }

    /// <summary>
    ///     Test that selecting a binary file shows the binary notice instead of content.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_BinaryFile_ShowsBinaryNoticeAndNoContent()
    {
        // Arrange: a review with a binary file
        var projectId = await CreateReviewAsync(new UploadedFile("c.bin", TestContent.C));
        var cut = RenderPage(projectId);

        // Act: select the file
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: the notice is shown, the content is not, and the path is shown
        Assert.Contains("binary", cut.Find("[data-testid='file-binary-notice']").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("[data-testid='file-content']"));
        Assert.Equal("c.bin", cut.Find("[data-testid='selected-file-path']").TextContent);
    }

    /// <summary>
    ///     Test that text which is not valid UTF-8 (without a NUL byte) is treated as binary.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_InvalidUtf8_ShowsBinaryNotice()
    {
        // Arrange: a file with a lone continuation byte
        var projectId = await CreateReviewAsync(new UploadedFile("latin1.txt", [0x63, 0x61, 0x66, 0xe9, 0x0a]));
        var cut = RenderPage(projectId);

        // Act: select the file
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: shown as binary
        Assert.NotEmpty(cut.FindAll("[data-testid='file-binary-notice']"));
    }

    /// <summary>
    ///     Test that an empty file shows an empty content block rather than the binary notice.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_EmptyFile_ShowsEmptyContent()
    {
        // Arrange: a review with an empty file
        var projectId = await CreateReviewAsync(new UploadedFile("empty.txt", []));
        var cut = RenderPage(projectId);

        // Act: select the file
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: empty text in a pre, no binary notice
        Assert.Equal(string.Empty, cut.Find("[data-testid='file-content']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='file-binary-notice']"));
    }

    /// <summary>
    ///     Test that selecting another file replaces the shown content.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_SecondFile_ReplacesContent()
    {
        // Arrange: a review with two text files, the first one shown
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A), new UploadedFile("b.txt", TestContent.B));
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Act: select the second file
        await cut.Find("[data-testid='file-select-1']").ClickAsync();

        // Assert: the second file is shown
        Assert.Equal("world\n", cut.Find("[data-testid='file-content']").TextContent);
        Assert.Equal("b.txt", cut.Find("[data-testid='selected-file-path']").TextContent);
    }

    /// <summary>
    ///     Test that navigating to another review replaces the shown review and clears the selected file.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ParametersChange_OtherReview_ShowsNewReviewAndClearsSelection()
    {
        // Arrange: a page showing REV-1 with its file selected, and a second review in the project
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        await Reviews.CreateDraftAsync(projectId, "REV-2", "Second review", [new("b.txt", TestContent.B)], TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Act: the router reuses the component for the other review
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(ReviewPage.ProjectId)] = projectId, [nameof(ReviewPage.ReviewId)] = "REV-2" })));

        // Assert: the second review is shown and no file content from the first one remains
        await cut.WaitForAssertionAsync(() => Assert.Equal("Second review", cut.Find("[data-testid='review-title']").TextContent));
        Assert.Equal("b.txt", cut.Find("[data-testid='file-path-0']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='file-content']"));
    }

    /// <summary>
    ///     Test that every file button carries an accessible name that identifies its file.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ExistingReview_FileButtonsNameTheirFile()
    {
        // Arrange: a review of two files
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A), new UploadedFile("b.txt", TestContent.B));

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: each button's accessible name includes its file path
        Assert.Equal("View a.txt", cut.Find("[data-testid='file-select-0']").GetAttribute("aria-label"));
        Assert.Equal("View b.txt", cut.Find("[data-testid='file-select-1']").GetAttribute("aria-label"));
    }

    /// <summary>
    ///     Test that the interactive marker is set once the component has rendered on a live circuit.
    /// </summary>
    [Fact]
    public void ReviewPage_AfterRender_Interactive_SetsMarker()
    {
        // Arrange: no setup required

        // Act: render the page
        var cut = RenderPage(1);

        // Assert: the root element reports interactivity
        Assert.Equal("true", cut.Find("[data-testid='review-page']").GetAttribute("data-interactive"));
    }
}
