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
    ///     Renders the project page for a project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ProjectPage> RenderProjectPage(int projectId)
    {
        return Render<ProjectPage>(parameters => parameters.Add(page => page.ProjectId, projectId));
    }

    /// <summary>
    ///     Renders the new review page (the form that creates a review) for a project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<NewReviewPage> RenderPage(int projectId)
    {
        return Render<NewReviewPage>(parameters => parameters.Add(page => page.ProjectId, projectId));
    }

    /// <summary>
    ///     Selects files in the file input the way a browser would.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="files">The files to select as name and bytes pairs.</param>
    private static void SelectFiles(IRenderedComponent<NewReviewPage> cut, params (string Name, byte[] Content)[] files)
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
        var cut = RenderProjectPage(99);

        // Assert: not-found is shown and there is no review form
        Assert.Contains("99", cut.Find("[data-testid='project-not-found']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='new-review-form']"));
    }

    /// <summary>
    ///     Test that the project page offers planning only when an enabled repository exists, and says so otherwise.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_PlanLink_ShownOnlyWithEnabledRepository()
    {
        // Arrange: a project, first without any repository
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);

        // Act: render the page without a repository
        var without = RenderProjectPage(project.Id);

        // Assert: no link, but a hint that explains why
        Assert.Empty(without.FindAll("[data-testid='plan-reviews-link']"));
        Assert.NotEmpty(without.FindAll("[data-testid='plan-reviews-unavailable']"));

        // Arrange: an administrator registers a repository
        var admin = new Actor("adm", "Adm");
        await Users.CreateAsync("adm", "Adm", UserRole.Admin, "correct horse battery", TestContext.Current.CancellationToken);
        await Repositories.CreateAsync(
            "main-repo", "https://example.com/org/repo.git", null, null, admin, TestContext.Current.CancellationToken);

        // Act: render the page again
        var with = RenderProjectPage(project.Id);

        // Assert: the link goes to the plan page of the project and the hint is gone
        var link = with.Find("[data-testid='plan-reviews-link']");
        Assert.Equal($"/projects/{project.Id}/plan", link.GetAttribute("href"));
        Assert.Equal("Plan reviews", link.TextContent);
        Assert.Empty(with.FindAll("[data-testid='plan-reviews-unavailable']"));
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
        var cut = RenderProjectPage(project.Id);

        // Assert: name, description, the empty message and the New review button leading to the form page
        Assert.Equal("Apollo", cut.Find("[data-testid='project-name']").TextContent);
        Assert.Equal("Moon landing", cut.Find("[data-testid='project-description']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='reviews-empty']"));
        Assert.Equal($"/projects/{project.Id}/reviews/new", cut.Find("[data-testid='new-review-button']").GetAttribute("href"));
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
        var cut = RenderProjectPage(project.Id);

        // Assert: the review row shows a link to the review, its title and its state
        var link = cut.Find("[data-testid='review-link-REV-1']");
        Assert.Equal("REV-1", link.TextContent);
        Assert.Equal($"/projects/{project.Id}/reviews/REV-1", link.GetAttribute("href"));
        Assert.Equal("First review", cut.Find("[data-testid='review-title-REV-1']").TextContent);
        Assert.Equal("Draft", cut.Find("[data-testid='review-state-REV-1']").TextContent);
        Assert.Equal("In review", cut.Find("[data-testid='review-approval-REV-1']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='reviews-empty']"));
    }

    /// <summary>
    ///     Test that a review whose every slot has a current signature is listed as Approved, and one without
    ///     slots or with an unsigned slot as In review.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_SignedAndUnsignedReviews_ShowApprovedOrInReview()
    {
        // Arrange: two reviews, one fully signed by its only slot, one without slots
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, "correct horse battery", TestContext.Current.CancellationToken);
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await Store.AddMembershipAsync(ann.Id, project.Id, TestContext.Current.CancellationToken);
        await Reviews.CreateDraftAsync(project.Id, "REV-1", "Signed", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        await Reviews.CreateDraftAsync(project.Id, "REV-2", "Open", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await Checklists.SetAnswerAsync(
                project.Id, "REV-1", item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        }

        var slot = await Signing.AddSlotAsync(TestActors.Ann, project.Id, "REV-1", ann.Id, null, TestContext.Current.CancellationToken);
        var approval = await Signing.GetApprovalAsync(project.Id, "REV-1", TestContext.Current.CancellationToken);
        await Signing.SignAsync(
            TestActors.Ann, project.Id, "REV-1", slot.Id, approval!.Digest, "correct horse battery", TestContext.Current.CancellationToken);

        // Act: render the page
        var cut = RenderProjectPage(project.Id);

        // Assert: the badges follow the approval
        Assert.Equal("Approved", cut.Find("[data-testid='review-approval-REV-1']").TextContent);
        Assert.Equal("In review", cut.Find("[data-testid='review-approval-REV-2']").TextContent);
    }

    /// <summary>
    ///     Test that choosing files lists them with their names and sizes.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_FileInput_FilesSelected_ListsNamesAndSizes()
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
    public async Task NewReviewPage_CreateForm_ValidInput_CreatesReviewAndNavigatesToIt()
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
    ///     Test that a review submitted after the session ended, but before the page learned of it, stores nothing
    ///     and leaves the not-found page (the access check finds no user).
    /// </summary>
    [Fact]
    public async Task NewReviewPage_CreateForm_SessionEndedBeforePageKnows_ShowsNotFoundAndCreatesNothing()
    {
        // Arrange: a filled-in form, and then the session ends without the page being told
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var cut = RenderPage(project.Id);
        await cut.Find("[data-testid='review-id-input']").InputAsync("REV-1");
        await cut.Find("[data-testid='review-title-input']").InputAsync("First review");
        SelectFiles(cut, ("a.txt", TestContent.A));
        await cut.WaitForAssertionAsync(() => Assert.NotEmpty(cut.FindAll("[data-testid='selected-file-0']")));
        SignOutQuietly();

        // Act: submit the form
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the page shows the not-found state, and no review exists
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='new-review-form']"));
        Assert.Empty(await Reviews.ListAsync(project.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a member who loses the membership while the new-review form is open gets the not-found page
    ///     when submitting, and that nothing is created.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_CreateForm_MembershipRevoked_ShowsNotFoundAndCreatesNothing()
    {
        // Arrange: a member has a filled-in form open
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var member = await Users.CreateAsync("mem", "Mem", UserRole.Reviewer, "correct horse battery", TestContext.Current.CancellationToken);
        await Store.AddMembershipAsync(member.Id, project.Id, TestContext.Current.CancellationToken);
        SignInAs("mem", "Mem", UserRole.Reviewer);
        var cut = RenderPage(project.Id);
        await cut.Find("[data-testid='review-id-input']").InputAsync("REV-1");
        await cut.Find("[data-testid='review-title-input']").InputAsync("First review");
        SelectFiles(cut, ("a.txt", TestContent.A));
        await cut.WaitForAssertionAsync(() => Assert.NotEmpty(cut.FindAll("[data-testid='selected-file-0']")));
        await RevokeMembershipAsync(project.Id, "mem");

        // Act: submit the form
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the not-found state, nothing of the project, and no review
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='new-review-form']"));
        Assert.DoesNotContain("Apollo", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(await Reviews.ListAsync(project.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that submitting without files shows the empty-file-set error and creates nothing.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_CreateForm_NoFiles_ShowsErrorAndCreatesNothing()
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
    public async Task NewReviewPage_CreateForm_DuplicateReviewId_ShowsErrorNamingTheId()
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
    ///     Test that the review id "new" is refused, because the address of the form would hide that review.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_CreateForm_ReservedReviewId_ShowsErrorAndCreatesNothing()
    {
        // Arrange: a project and a filled form whose review id is the reserved word, in any letter case
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var cut = RenderPage(project.Id);
        await cut.Find("[data-testid='review-id-input']").InputAsync("New");
        await cut.Find("[data-testid='review-title-input']").InputAsync("Reserved");
        SelectFiles(cut, ("a.txt", TestContent.A));
        await cut.WaitForAssertionAsync(() => Assert.NotEmpty(cut.FindAll("[data-testid='selected-file-0']")));

        // Act: submit the form
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the error says the id is reserved and no review was created
        Assert.Contains("reserved", cut.Find("[data-testid='review-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(await Reviews.ListAsync(project.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that two selected files with the same name are reported as a duplicate path on submit.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_CreateForm_DuplicateFileNames_ShowsDuplicatePathError()
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
    public async Task NewReviewPage_FileInput_FileTooLarge_ShowsErrorAndKeepsNothing()
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
    public async Task NewReviewPage_FileInput_TooManyFiles_ShowsErrorAndKeepsNothing()
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
    public async Task NewReviewPage_FileInput_TotalSizeTooLarge_ShowsErrorAndKeepsNothing()
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
    public async Task NewReviewPage_FileInput_DeclaredSizesUnderstated_RejectsOnReceivedBytes()
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
    public async Task NewReviewPage_FileInput_SecondSelection_ReplacesFirst()
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
    public async Task NewReviewPage_ParametersChange_OtherProject_ClearsFormAndSelection()
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
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(NewReviewPage.ProjectId)] = second.Id })));

        // Assert: the new project is shown with an empty form and no selected files
        await cut.WaitForAssertionAsync(() => Assert.Equal("Gemini", cut.Find("[data-testid='project-link']").TextContent));
        Assert.Equal(string.Empty, cut.Find("[data-testid='review-id-input']").GetAttribute("value") ?? string.Empty);
        Assert.Equal(string.Empty, cut.Find("[data-testid='review-title-input']").GetAttribute("value") ?? string.Empty);
        Assert.Empty(cut.FindAll("[data-testid='selected-file-0']"));
    }

    /// <summary>
    ///     Creates two system administrators (<c>root</c> and <c>boss</c>), the project Apollo with a review and a
    ///     member, and an enabled user who is no member yet, and signs in as <c>root</c>.
    /// </summary>
    /// <param name="rootIsMember">Whether <c>root</c> also is a plain member of the project.</param>
    /// <returns>The project identity; the member is <c>mbr</c> and the candidate is <c>zed-candidate</c>.</returns>
    private async Task<int> ArrangeSystemAdminAsync(bool rootIsMember)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string password = "correct horse battery";
        var root = await Users.CreateAsync("root", "Root", UserRole.Admin, password, cancellationToken);
        await Users.CreateAsync("boss", "Boss", UserRole.Admin, password, cancellationToken);
        var member = await Users.CreateAsync("mbr", "Zed Member", UserRole.Reviewer, password, cancellationToken);
        await Users.CreateAsync("zed-candidate", "Zed Candidate", UserRole.Reviewer, password, cancellationToken);
        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        await Store.AddMembershipAsync(member.Id, project.Id, cancellationToken);
        if (rootIsMember)
        {
            await Store.AddMembershipAsync(root.Id, project.Id, cancellationToken);
        }

        await Reviews.CreateDraftAsync(project.Id, "REV-1", "First", [new("a.txt", TestContent.A)], cancellationToken);
        SignInAs("root", "Root", UserRole.Admin);
        return project.Id;
    }

    /// <summary>
    ///     Test that a system administrator who was demoted, but still is a member, loses the members section and
    ///     the repository link when the next member action runs, while the reviews of the project stay.
    /// </summary>
    [Fact]
    public async Task ProjectPage_MemberChange_SystemAdminDemotedButStillMember_DropsMembersSectionKeepsReviews()
    {
        // Arrange: a system administrator with the page open who also is a plain member, then demoted
        var projectId = await ArrangeSystemAdminAsync(rootIsMember: true);
        var cut = RenderProjectPage(projectId);
        Assert.NotEmpty(cut.FindAll("[data-testid='member-row-mbr']"));
        await Users.SetRoleAsync("root", UserRole.Reviewer, TestContext.Current.CancellationToken);

        // Act: remove a member from the page that is still open
        await cut.Find("[data-testid='member-remove-mbr']").ClickAsync();

        // Assert: no members section and nothing in it, no error, the reviews stay, and the membership is stored
        Assert.Empty(cut.FindAll("[data-testid='members-list']"));
        Assert.Empty(cut.FindAll("[data-testid^='member-row-']"));
        Assert.Empty(cut.FindAll("[data-testid='members-empty']"));
        Assert.Empty(cut.FindAll("[data-testid='add-member-button']"));
        Assert.Empty(cut.FindAll("[data-testid='member-error']"));
        Assert.Empty(cut.FindAll("[data-testid='member-status']"));
        Assert.Empty(cut.FindAll("[data-testid='repositories-link']"));
        Assert.DoesNotContain("Zed Member", cut.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll("[data-testid='review-link-REV-1']"));
        var member = await UserStore.GetByUsernameAsync("mbr", TestContext.Current.CancellationToken);
        Assert.NotNull(await Store.GetMembershipAsync(member!.Id, projectId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a system administrator who was demoted and is no member gets the not-found state, with no
    ///     reviews and no members, when the next member action runs.
    /// </summary>
    [Fact]
    public async Task ProjectPage_MemberChange_SystemAdminDemotedAndNotMember_ShowsNotFoundAndNoData()
    {
        // Arrange: a system administrator with the page open who is no member, then demoted
        var projectId = await ArrangeSystemAdminAsync(rootIsMember: false);
        var cut = RenderProjectPage(projectId);
        await Users.SetRoleAsync("root", UserRole.Reviewer, TestContext.Current.CancellationToken);

        // Act: remove a member from the page that is still open
        await cut.Find("[data-testid='member-remove-mbr']").ClickAsync();

        // Assert: the state of a non-member, with nothing of the project, and the membership is stored
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='reviews-list']"));
        Assert.Empty(cut.FindAll("[data-testid^='review-row-']"));
        Assert.Empty(cut.FindAll("[data-testid='members-list']"));
        Assert.Empty(cut.FindAll("[data-testid='project-name']"));
        Assert.DoesNotContain("Zed Member", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("REV-1", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Apollo", cut.Markup, StringComparison.Ordinal);
        var member = await UserStore.GetByUsernameAsync("mbr", TestContext.Current.CancellationToken);
        Assert.NotNull(await Store.GetMembershipAsync(member!.Id, projectId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that changing a role after the system administrator was demoted does not change the role and drops the
    ///     members section.
    /// </summary>
    [Fact]
    public async Task ProjectPage_ToggleRole_SystemAdminDemoted_ChangesNothingAndDropsMembers()
    {
        // Arrange: a system administrator with the page open who also is a plain member, then demoted
        var projectId = await ArrangeSystemAdminAsync(rootIsMember: true);
        var cut = RenderProjectPage(projectId);
        await Users.SetRoleAsync("root", UserRole.Reviewer, TestContext.Current.CancellationToken);

        // Act: make the member a project admin
        await cut.Find("[data-testid='member-toggle-role-mbr']").ClickAsync();

        // Assert: no member section, no candidates in the markup, and the role is unchanged
        Assert.Empty(cut.FindAll("[data-testid='add-member-button']"));
        Assert.Empty(cut.FindAll("[data-testid='members-list']"));
        Assert.DoesNotContain("zed-candidate", cut.Markup, StringComparison.Ordinal);
        var member = await UserStore.GetByUsernameAsync("mbr", TestContext.Current.CancellationToken);
        Assert.Equal(
            ProjectRole.User,
            (await Store.GetMembershipAsync(member!.Id, projectId, TestContext.Current.CancellationToken))!.Role);
    }

    /// <summary>
    ///     Test that a member action sent after the session ended, but before the page learned of it, shows the
    ///     not-found state and changes nothing.
    /// </summary>
    [Fact]
    public async Task ProjectPage_MemberChange_SessionEndedBeforePageKnows_ShowsNotFoundAndChangesNothing()
    {
        // Arrange: a system administrator with the page open, then the session ends without the page being told
        var projectId = await ArrangeSystemAdminAsync(rootIsMember: false);
        var cut = RenderProjectPage(projectId);
        SignOutQuietly();

        // Act: remove a member
        await cut.Find("[data-testid='member-remove-mbr']").ClickAsync();

        // Assert: the state of a non-member, with no members on the page, and the membership is stored
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='members-list']"));
        Assert.Empty(cut.FindAll("[data-testid='member-error']"));
        Assert.DoesNotContain("Zed Member", cut.Markup, StringComparison.Ordinal);
        var member = await UserStore.GetByUsernameAsync("mbr", TestContext.Current.CancellationToken);
        Assert.NotNull(await Store.GetMembershipAsync(member!.Id, projectId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Creates a project Apollo with a review, a project administrator <c>pam</c> (signed in), another
    ///     project administrator <c>pat</c> when asked, a plain member <c>mbr</c> and a user who is no member.
    /// </summary>
    /// <param name="secondAdmin">Whether a second project administrator <c>pat</c> exists.</param>
    /// <returns>The project identity.</returns>
    private async Task<int> ArrangeProjectAdminAsync(bool secondAdmin)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string password = "correct horse battery";
        var pam = await Users.CreateAsync("pam", "Pam Admin", UserRole.Reviewer, password, cancellationToken);
        var member = await Users.CreateAsync("mbr", "Zed Member", UserRole.Reviewer, password, cancellationToken);
        await Users.CreateAsync("outsider", "Olive Outsider", UserRole.Reviewer, password, cancellationToken);
        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        await Store.AddMembershipAsync(pam.Id, project.Id, cancellationToken);
        await Store.AddMembershipAsync(member.Id, project.Id, cancellationToken);
        await ChangeMemberRoleAsync(project.Id, "pam", ProjectRole.ProjectAdmin);
        if (secondAdmin)
        {
            var pat = await Users.CreateAsync("pat", "Pat Admin", UserRole.Reviewer, password, cancellationToken);
            await Store.AddMembershipAsync(pat.Id, project.Id, cancellationToken);
            await ChangeMemberRoleAsync(project.Id, "pat", ProjectRole.ProjectAdmin);
        }

        await Reviews.CreateDraftAsync(project.Id, "REV-1", "First", [new("a.txt", TestContent.A)], cancellationToken);
        SignInAs("pam", "Pam Admin", isSystemAdmin: false);
        return project.Id;
    }

    /// <summary>
    ///     Test that a project administrator sees the members with role and remove buttons, the add-member button
    ///     and the audit trail button, and no list of other users.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_ProjectAdmin_ShowsMembersAndButtons()
    {
        // Arrange: a project administrator who is signed in
        var projectId = await ArrangeProjectAdminAsync(secondAdmin: false);

        // Act: render the page
        var cut = RenderProjectPage(projectId);

        // Assert: members, buttons and the audit link are there; nothing offers other users
        Assert.Equal("Project admin", cut.Find("[data-testid='member-role-pam']").TextContent);
        Assert.Equal("Member", cut.Find("[data-testid='member-role-mbr']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='member-toggle-role-mbr']"));
        Assert.NotNull(cut.Find("[data-testid='member-remove-mbr']"));
        Assert.Equal(
            $"/projects/{projectId}/members/new",
            cut.Find("[data-testid='add-member-button']").GetAttribute("href"));
        Assert.Equal(
            $"/projects/{projectId}/audit",
            cut.Find("[data-testid='project-audit-link']").GetAttribute("href"));
        Assert.Equal(
            $"/projects/{projectId}/evidence-settings",
            cut.Find("[data-testid='evidence-settings-link']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("select"));
        Assert.DoesNotContain("outsider", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a plain member sees the reviews but no members section, no add button and no audit button.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_PlainMember_ShowsNoMembersAndNoAuditLink()
    {
        // Arrange: a plain member who is signed in
        var projectId = await ArrangeProjectAdminAsync(secondAdmin: false);
        SignInAs("mbr", "Zed Member", isSystemAdmin: false);

        // Act: render the page
        var cut = RenderProjectPage(projectId);

        // Assert: the review is listed, the administration is not
        Assert.NotEmpty(cut.FindAll("[data-testid='review-link-REV-1']"));
        Assert.Empty(cut.FindAll("[data-testid='members-list']"));
        Assert.Empty(cut.FindAll("[data-testid='add-member-button']"));
        Assert.Empty(cut.FindAll("[data-testid='project-audit-link']"));
        Assert.Empty(cut.FindAll("[data-testid='evidence-settings-link']"));
        Assert.DoesNotContain("Pam Admin", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a project administrator can make a member a project admin and remove a member.
    /// </summary>
    [Fact]
    public async Task ProjectPage_MemberChange_ProjectAdmin_ChangesRoleAndRemoves()
    {
        // Arrange: a project administrator with a plain member
        var projectId = await ArrangeProjectAdminAsync(secondAdmin: false);
        var cut = RenderProjectPage(projectId);

        // Act: promote the member, then remove the member
        await cut.Find("[data-testid='member-toggle-role-mbr']").ClickAsync();
        var promoted = cut.Find("[data-testid='member-role-mbr']").TextContent;
        await cut.Find("[data-testid='member-remove-mbr']").ClickAsync();

        // Assert: the role showed as project admin, then the row is gone
        Assert.Equal("Project admin", promoted);
        Assert.Empty(cut.FindAll("[data-testid='member-row-mbr']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='member-row-pam']"));
    }

    /// <summary>
    ///     Test that the last project administrator cannot remove or demote themselves: the page explains why
    ///     and nothing changes.
    /// </summary>
    /// <param name="removeInsteadOfDemote">Whether to remove (true) or to demote (false).</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProjectPage_MemberChange_LastProjectAdminSelf_ShowsExplanationAndChangesNothing(
        bool removeInsteadOfDemote)
    {
        // Arrange: the only project administrator has the page open
        var projectId = await ArrangeProjectAdminAsync(secondAdmin: false);
        var cut = RenderProjectPage(projectId);

        // Act: try to remove or demote themselves
        await cut.Find($"[data-testid='member-{(removeInsteadOfDemote ? "remove" : "toggle-role")}-pam']").ClickAsync();

        // Assert: the explanation is shown, the row and the role stay
        Assert.Contains(ProjectService.LastAdminMessage, cut.Find("[data-testid='member-error']").TextContent,
            StringComparison.Ordinal);
        Assert.Equal("Project admin", cut.Find("[data-testid='member-role-pam']").TextContent);
        var pam = await UserStore.GetByUsernameAsync("pam", TestContext.Current.CancellationToken);
        Assert.Equal(
            ProjectRole.ProjectAdmin,
            (await Store.GetMembershipAsync(pam!.Id, projectId, TestContext.Current.CancellationToken))!.Role);
    }

    /// <summary>
    ///     Test that a project administrator sees the review rules with the default (independent reviewers
    ///     required), switches the rule off and on again, and that each change is stored and audited.
    /// </summary>
    [Fact]
    public async Task ProjectPage_ReviewRules_AdminSavesToggle()
    {
        // Arrange: a project administrator with the page open
        var projectId = await ArrangeProjectAdminAsync(secondAdmin: false);
        var cut = RenderProjectPage(projectId);
        Assert.True(cut.Find("[data-testid='independent-reviewers-switch']").HasAttribute("checked"));

        // Act: switch off and save
        await cut.Find("[data-testid='independent-reviewers-switch']").ChangeAsync(new ChangeEventArgs { Value = false });
        await cut.Find("[data-testid='independent-reviewers-save']").ClickAsync();

        // Assert: stored, reported, and audited
        Assert.Equal("Independent reviewers are no longer required.", cut.Find("[data-testid='review-rules-status']").TextContent);
        var stored = await Projects.GetAsync(TestActors.Ann, projectId, TestContext.Current.CancellationToken);
        Assert.False(stored!.RequireIndependentReviewers);
        Assert.Contains(
            Store.Audits,
            e => e.Action == ProjectService.IndependentReviewersChangedAction
                && e.ProjectId == projectId
                && e.Detail.Contains("on->off", StringComparison.Ordinal));

        // Act: switch on again and save
        await cut.Find("[data-testid='independent-reviewers-switch']").ChangeAsync(new ChangeEventArgs { Value = true });
        await cut.Find("[data-testid='independent-reviewers-save']").ClickAsync();

        // Assert: required again
        Assert.Equal("Independent reviewers are now required.", cut.Find("[data-testid='review-rules-status']").TextContent);
        Assert.True((await Projects.GetAsync(TestActors.Ann, projectId, TestContext.Current.CancellationToken))!.RequireIndependentReviewers);
    }

    /// <summary>
    ///     Test that a plain member does not see the review rules card.
    /// </summary>
    [Fact]
    public async Task ProjectPage_ReviewRules_NonAdmin_HidesCard()
    {
        // Arrange: a plain member who is signed in
        var projectId = await ArrangeProjectAdminAsync(secondAdmin: false);
        SignInAs("mbr", "Zed Member", isSystemAdmin: false);

        // Act: render the page
        var cut = RenderProjectPage(projectId);

        // Assert: no rules card, switch or save button
        Assert.Empty(cut.FindAll("[data-testid='review-rules-card']"));
        Assert.Empty(cut.FindAll("[data-testid='independent-reviewers-switch']"));
        Assert.Empty(cut.FindAll("[data-testid='independent-reviewers-save']"));
    }

    /// <summary>
    ///     Test that with a second project administrator the first one may remove themselves.
    /// </summary>
    [Fact]
    public async Task ProjectPage_MemberChange_SecondAdminExists_AdminMayRemoveSelf()
    {
        // Arrange: two project administrators, the first has the page open
        var projectId = await ArrangeProjectAdminAsync(secondAdmin: true);
        var cut = RenderProjectPage(projectId);

        // Act: remove themselves
        await cut.Find("[data-testid='member-remove-pam']").ClickAsync();

        // Assert: the membership is gone, so the page no longer shows the project
        var pam = await UserStore.GetByUsernameAsync("pam", TestContext.Current.CancellationToken);
        Assert.Null(await Store.GetMembershipAsync(pam!.Id, projectId, TestContext.Current.CancellationToken));
        Assert.Empty(cut.FindAll("[data-testid='members-list']"));
        Assert.DoesNotContain("Zed Member", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a project administrator who lost the membership while the page was open gets the not-found
    ///     state, with no data, from every handler, and changes nothing.
    /// </summary>
    /// <param name="action">The testid of the button that is used.</param>
    [Theory]
    [InlineData("member-remove-mbr")]
    [InlineData("member-toggle-role-mbr")]
    public async Task ProjectPage_MemberChange_ProjectAdminMembershipRevoked_ShowsNotFoundAndChangesNothing(
        string action)
    {
        // Arrange: a project administrator with the page open, then removed from the project by another
        var projectId = await ArrangeProjectAdminAsync(secondAdmin: true);
        var cut = RenderProjectPage(projectId);
        await RevokeMembershipAsync(projectId, "pam");

        // Act: use the button
        await cut.Find($"[data-testid='{action}']").ClickAsync();

        // Assert: nothing of the project is shown, and the member is unchanged
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='members-list']"));
        Assert.Empty(cut.FindAll("[data-testid='reviews-list']"));
        Assert.Empty(cut.FindAll("[data-testid='member-error']"));
        Assert.DoesNotContain("Zed Member", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("REV-1", cut.Markup, StringComparison.Ordinal);
        var member = await UserStore.GetByUsernameAsync("mbr", TestContext.Current.CancellationToken);
        var stored = await Store.GetMembershipAsync(member!.Id, projectId, TestContext.Current.CancellationToken);
        Assert.Equal(ProjectRole.User, stored!.Role);
    }

    /// <summary>
    ///     Test that a project administrator who was demoted to a plain member while the page was open loses the
    ///     members section and the audit button but still sees the reviews, and changes nothing.
    /// </summary>
    [Fact]
    public async Task ProjectPage_MemberChange_ProjectAdminDemoted_DropsMembersKeepsReviews()
    {
        // Arrange: a project administrator with the page open, then demoted by another
        var projectId = await ArrangeProjectAdminAsync(secondAdmin: true);
        var cut = RenderProjectPage(projectId);
        await ChangeMemberRoleAsync(projectId, "pam", ProjectRole.User);

        // Act: remove the member
        await cut.Find("[data-testid='member-remove-mbr']").ClickAsync();

        // Assert: no members section and no audit button, the review stays, and the member is still there
        Assert.Empty(cut.FindAll("[data-testid='members-list']"));
        Assert.Empty(cut.FindAll("[data-testid='add-member-button']"));
        Assert.Empty(cut.FindAll("[data-testid='project-audit-link']"));
        Assert.DoesNotContain("Zed Member", cut.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll("[data-testid='review-link-REV-1']"));
        var member = await UserStore.GetByUsernameAsync("mbr", TestContext.Current.CancellationToken);
        Assert.NotNull(await Store.GetMembershipAsync(member!.Id, projectId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the interactive marker is set once the component has rendered on a live circuit.
    /// </summary>
    [Fact]
    public void ProjectPage_AfterRender_Interactive_SetsMarker()
    {
        // Arrange: no setup required

        // Act: render the page
        var cut = RenderProjectPage(1);

        // Assert: the root element reports interactivity
        Assert.Equal("true", cut.Find("[data-testid='project-page']").GetAttribute("data-interactive"));
    }
}
