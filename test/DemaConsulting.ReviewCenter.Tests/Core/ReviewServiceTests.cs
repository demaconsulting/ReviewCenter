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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="ReviewService"/> using an in-memory store.
/// </summary>
public class ReviewServiceTests
{
    /// <summary>
    ///     The instant reported by the service's clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Creates a store with one project and a review service over it.
    /// </summary>
    /// <returns>The store, the service and the id of the project.</returns>
    private static async Task<(FakeReviewStore Store, ReviewService Service, int ProjectId)> CreateWithProjectAsync()
    {
        var store = new FakeReviewStore();
        var project = await TestServices.Projects(store, new FixedTimeProvider(_now))
            .CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        return (store, TestServices.Reviews(store, new FixedTimeProvider(_now)), project.Id);
    }

    /// <summary>
    ///     Builds a one-file upload.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="content">The file bytes.</param>
    /// <returns>A list with the single file.</returns>
    private static List<UploadedFile> One(string path, byte[] content) => [new UploadedFile(path, content)];

    /// <summary>
    ///     Test that a draft created by an actor that came through an API token is flagged and the audit detail
    ///     names the non-secret token id.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_ApiActor_FlagsReviewAndAuditText()
    {
        // Arrange: a project and an actor that authenticated with a token
        var (store, service, projectId) = await CreateWithProjectAsync();
        var viaApi = new Actor("ann", "Ann") { ApiTokenId = "K7M2QX9P" };

        // Act: create the draft as that actor
        var review = await service.CreateDraftAsync(
            projectId, "REV-1", "First", One("a.txt", TestContent.A), viaApi, TestContext.Current.CancellationToken);

        // Assert: the review is flagged, and the audit detail ends with the token id
        Assert.Equal("ann", review.CreatedBy);
        Assert.True(review.CreatedViaApi);
        var audit = Assert.Single(store.Audits, a => a.Action == "review.created");
        Assert.EndsWith(", created via API token 'K7M2QX9P'", audit.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a draft created from the web pages records the creator but is not flagged as created through
    ///     the API, and the audit detail does not mention a token.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_UiActor_RecordsCreatorWithoutApiFlag()
    {
        // Arrange: a project
        var (store, service, projectId) = await CreateWithProjectAsync();

        // Act: create the draft as a person who signed in with a password
        var review = await service.CreateDraftAsync(projectId, "REV-1", "First", One("a.txt", TestContent.A), TestContext.Current.CancellationToken);

        // Assert: creator recorded, no API flag, no token text
        Assert.Equal("ann", review.CreatedBy);
        Assert.False(review.CreatedViaApi);
        var audit = Assert.Single(store.Audits, a => a.Action == "review.created");
        Assert.DoesNotContain("API token", audit.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a valid draft is stored with its state, time, trimmed title and a known fingerprint.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_ValidInput_StoresDraftWithFingerprint()
    {
        // Arrange: a project and three files in non-sorted order
        var (store, service, projectId) = await CreateWithProjectAsync();
        List<UploadedFile> files =
        [
            new("src/c.bin", TestContent.C),
            new("a.txt", TestContent.A),
            new("b.txt", TestContent.B)
        ];

        // Act: create the draft with a title that has surrounding whitespace
        var review = await service.CreateDraftAsync(projectId, "REV-1", "  First review  ", files, TestContext.Current.CancellationToken);

        // Assert: draft state, clock time, trimmed title and the known fingerprint
        Assert.Equal(ReviewState.Draft, review.State);
        Assert.Equal(_now, review.Created);
        Assert.Equal("First review", review.Title);
        Assert.Equal(TestContent.FingerprintABC, review.ContentFingerprint);
        Assert.Single(store.Reviews);

        // Assert: every file carries the size and hash of its own content
        var stored = store.Reviews.Single().Files.ToDictionary(file => file.Path);
        Assert.Equal(TestContent.A.Length, stored["a.txt"].Size);
        Assert.Equal(TestContent.HashA, stored["a.txt"].Sha256);
        Assert.Equal(TestContent.B.Length, stored["b.txt"].Size);
        Assert.Equal(TestContent.HashB, stored["b.txt"].Sha256);
        Assert.Equal(TestContent.C.Length, stored["src/c.bin"].Size);
        Assert.Equal(TestContent.HashC, stored["src/c.bin"].Sha256);
    }

    /// <summary>
    ///     Test that a new review carries the default checklist as a snapshot with its version.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_ValidInput_AttachesDefaultChecklistSnapshot()
    {
        // Arrange: a project
        var (store, service, projectId) = await CreateWithProjectAsync();

        // Act: create a draft
        await service.CreateDraftAsync(projectId, "REV-1", "First", One("a.txt", TestContent.A), TestContext.Current.CancellationToken);

        // Assert: the stored review has the default checklist, parsed back to the same definition
        var checklist = store.Reviews.Single().Checklist;
        Assert.NotNull(checklist);
        Assert.Equal(DefaultChecklist.Version, checklist.Version);
        Assert.Equal(58, checklist.Parse().Sections.Sum(section => section.Items.Count));
    }

    /// <summary>
    ///     Test that each file keeps its bytes verbatim and carries its size and hash.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_BinaryAndCrLfContent_KeepsBytesVerbatim()
    {
        // Arrange: a project and a binary file with CR LF and an empty file
        var (store, service, projectId) = await CreateWithProjectAsync();
        List<UploadedFile> files = [new("c.bin", TestContent.C), new("empty.txt", [])];

        // Act: create the draft
        await service.CreateDraftAsync(projectId, "REV-1", "Title", files, TestContext.Current.CancellationToken);

        // Assert: bytes, sizes and hashes of every file are exactly those of the input
        var stored = store.Reviews.Single().Files.ToDictionary(file => file.Path);
        Assert.Equal(TestContent.C, stored["c.bin"].Content);
        Assert.Equal(TestContent.C.Length, stored["c.bin"].Size);
        Assert.Equal(TestContent.HashC, stored["c.bin"].Sha256);
        Assert.Empty(stored["empty.txt"].Content);
        Assert.Equal(0, stored["empty.txt"].Size);
        Assert.Equal(TestContent.HashEmpty, stored["empty.txt"].Sha256);
    }

    /// <summary>
    ///     Test that the review fingerprint ignores file paths and file order.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_SameBytesDifferentPathsAndOrder_GivesSameFingerprint()
    {
        // Arrange: a project and the same three contents under other paths and in another order
        var (_, service, projectId) = await CreateWithProjectAsync();
        List<UploadedFile> files =
        [
            new("zzz/one", TestContent.B),
            new("other/two", TestContent.C),
            new("three", TestContent.A)
        ];

        // Act: create the draft
        var review = await service.CreateDraftAsync(projectId, "REV-1", "Title", files, TestContext.Current.CancellationToken);

        // Assert: identical to the fingerprint of the same contents elsewhere
        Assert.Equal(TestContent.FingerprintABC, review.ContentFingerprint);
    }

    /// <summary>
    ///     Test that an empty file set is rejected and nothing is stored.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_NoFiles_ThrowsNoFiles()
    {
        // Arrange: a project
        var (store, service, projectId) = await CreateWithProjectAsync();

        // Act: create a review without files
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateDraftAsync(projectId, "REV-1", "Title", [], TestContext.Current.CancellationToken));

        // Assert: the rule is reported and nothing is stored
        Assert.Equal(DomainRule.NoFiles, exception.Rule);
        Assert.Empty(store.Reviews);
    }

    /// <summary>
    ///     Test that two files with the same path are rejected.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_DuplicatePaths_ThrowsDuplicatePath()
    {
        // Arrange: a project and two files with the same path
        var (_, service, projectId) = await CreateWithProjectAsync();
        List<UploadedFile> files = [new("a.txt", TestContent.A), new("a.txt", TestContent.B)];

        // Act: create the review
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateDraftAsync(projectId, "REV-1", "Title", files, TestContext.Current.CancellationToken));

        // Assert: the rule names the offending path
        Assert.Equal(DomainRule.DuplicatePath, exception.Rule);
        Assert.Contains("a.txt", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that paths differing only in case are distinct, since file systems differ.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_PathsDifferingInCase_AreDistinct()
    {
        // Arrange: a project and two paths that differ only in case
        var (_, service, projectId) = await CreateWithProjectAsync();
        List<UploadedFile> files = [new("a.txt", TestContent.A), new("A.txt", TestContent.B)];

        // Act: create the review
        var review = await service.CreateDraftAsync(projectId, "REV-1", "Title", files, TestContext.Current.CancellationToken);

        // Assert: both files are kept
        Assert.Equal(2, review.Files.Count);
    }

    /// <summary>
    ///     Test that a blank path or one longer than the limit is rejected.
    /// </summary>
    /// <param name="path">The invalid path.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReviewService_CreateDraftAsync_BlankPath_ThrowsPathInvalid(string path)
    {
        // Arrange: a project
        var (_, service, projectId) = await CreateWithProjectAsync();

        // Act: create a review whose only file has the blank path
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateDraftAsync(projectId, "REV-1", "Title", One(path, TestContent.A), TestContext.Current.CancellationToken));

        // Assert: the path rule is reported
        Assert.Equal(DomainRule.PathInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that a path over the length limit is rejected.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_PathTooLong_ThrowsPathInvalid()
    {
        // Arrange: a project
        var (_, service, projectId) = await CreateWithProjectAsync();
        var longPath = new string('p', ReviewLimits.MaxPathLength + 1);

        // Act: create a review with the over-long path
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateDraftAsync(projectId, "REV-1", "Title", One(longPath, TestContent.A), TestContext.Current.CancellationToken));

        // Assert: the path rule is reported
        Assert.Equal(DomainRule.PathInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that a duplicate id in the same project is rejected with a message naming the id.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_DuplicateIdInSameProject_ThrowsReviewIdDuplicate()
    {
        // Arrange: a project that already has review REV-1
        var (store, service, projectId) = await CreateWithProjectAsync();
        await service.CreateDraftAsync(projectId, "REV-1", "First", One("a.txt", TestContent.A), TestContext.Current.CancellationToken);

        // Act: create another review with the same id
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateDraftAsync(projectId, "REV-1", "Second", One("b.txt", TestContent.B), TestContext.Current.CancellationToken));

        // Assert: rejected, naming the id, and only the first review is stored
        Assert.Equal(DomainRule.ReviewIdDuplicate, exception.Rule);
        Assert.Contains("REV-1", exception.Message, StringComparison.Ordinal);
        Assert.Single(store.Reviews);
    }

    /// <summary>
    ///     Test that the same review id may be used in two different projects.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_SameIdInOtherProject_IsAllowed()
    {
        // Arrange: two projects, the first already having REV-1
        var (store, service, firstProjectId) = await CreateWithProjectAsync();
        var second = await TestServices.Projects(store, new FixedTimeProvider(_now))
            .CreateAsync("Gemini", null, TestContext.Current.CancellationToken);
        await service.CreateDraftAsync(firstProjectId, "REV-1", "First", One("a.txt", TestContent.A), TestContext.Current.CancellationToken);

        // Act: create REV-1 in the second project
        var review = await service.CreateDraftAsync(second.Id, "REV-1", "Other", One("a.txt", TestContent.A), TestContext.Current.CancellationToken);

        // Assert: both exist
        Assert.Equal(second.Id, review.ProjectId);
        Assert.Equal(2, store.Reviews.Count);
    }

    /// <summary>
    ///     Test that ids which are blank, too long, or contain disallowed characters are rejected.
    /// </summary>
    /// <param name="id">The invalid id.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("-leading-dash")]
    [InlineData(".leading-dot")]
    [InlineData("slash/inside")]
    [InlineData("ümlaut")]
    public async Task ReviewService_CreateDraftAsync_InvalidId_ThrowsReviewIdInvalid(string? id)
    {
        // Arrange: a project
        var (_, service, projectId) = await CreateWithProjectAsync();

        // Act: create a review with the invalid id
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateDraftAsync(projectId, id, "Title", One("a.txt", TestContent.A), TestContext.Current.CancellationToken));

        // Assert: the id rule is reported
        Assert.Equal(DomainRule.ReviewIdInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that an id over the length limit is rejected while one exactly at the limit is accepted.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_IdLength_LimitIsInclusive()
    {
        // Arrange: a project
        var (_, service, projectId) = await CreateWithProjectAsync();

        // Act: create reviews with an id at the limit and one over it
        var atLimit = await service.CreateDraftAsync(
            projectId, new string('i', ReviewLimits.MaxReviewIdLength), "Title", One("a.txt", TestContent.A), TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateDraftAsync(
                projectId, new string('j', ReviewLimits.MaxReviewIdLength + 1), "Title", One("a.txt", TestContent.A), TestContext.Current.CancellationToken));

        // Assert: only the longer one is rejected
        Assert.Equal(ReviewLimits.MaxReviewIdLength, atLimit.Id.Length);
        Assert.Equal(DomainRule.ReviewIdInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that a blank title or one over the length limit is rejected.
    /// </summary>
    /// <param name="title">The invalid title.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReviewService_CreateDraftAsync_BlankTitle_ThrowsReviewTitleInvalid(string? title)
    {
        // Arrange: a project
        var (_, service, projectId) = await CreateWithProjectAsync();

        // Act: create a review with the blank title
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateDraftAsync(projectId, "REV-1", title, One("a.txt", TestContent.A), TestContext.Current.CancellationToken));

        // Assert: the title rule is reported
        Assert.Equal(DomainRule.ReviewTitleInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that a title over the length limit is rejected.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_TitleTooLong_ThrowsReviewTitleInvalid()
    {
        // Arrange: a project
        var (_, service, projectId) = await CreateWithProjectAsync();

        // Act: create a review with an over-long title
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateDraftAsync(
                projectId, "REV-1", new string('t', ReviewLimits.MaxReviewTitleLength + 1), One("a.txt", TestContent.A), TestContext.Current.CancellationToken));

        // Assert: the title rule is reported
        Assert.Equal(DomainRule.ReviewTitleInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that a review for a project that does not exist is rejected.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_UnknownProject_ThrowsProjectNotFound()
    {
        // Arrange: a service over a store with no projects
        var service = TestServices.Reviews(new FakeReviewStore(), new FixedTimeProvider(_now));

        // Act: create a review in project 42
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateDraftAsync(42, "REV-1", "Title", One("a.txt", TestContent.A), TestContext.Current.CancellationToken));

        // Assert: the project rule is reported
        Assert.Equal(DomainRule.ProjectNotFound, exception.Rule);
    }

    /// <summary>
    ///     Test that a file one byte over the per-file limit is rejected and one exactly at the limit is accepted.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_FileSize_LimitIsInclusive()
    {
        // Arrange: a project
        var (_, service, projectId) = await CreateWithProjectAsync();

        // Act: create reviews with a file at the limit and one byte over
        var atLimit = await service.CreateDraftAsync(
            projectId, "REV-1", "Title", One("big.bin", new byte[ReviewLimits.MaxFileSizeBytes]), TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateDraftAsync(
                projectId, "REV-2", "Title", One("big.bin", new byte[ReviewLimits.MaxFileSizeBytes + 1]), TestContext.Current.CancellationToken));

        // Assert: only the bigger one is rejected
        Assert.Equal(ReviewLimits.MaxFileSizeBytes, atLimit.Files.Single().Size);
        Assert.Equal(DomainRule.FileTooLarge, exception.Rule);
    }

    /// <summary>
    ///     Test that more files than the limit are rejected.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_TooManyFiles_ThrowsTooManyFiles()
    {
        // Arrange: a project and one more file than allowed
        var (_, service, projectId) = await CreateWithProjectAsync();
        var files = Enumerable.Range(0, ReviewLimits.MaxFileCount + 1)
            .Select(i => new UploadedFile($"f{i}.txt", [0x41]))
            .ToList();

        // Act: create the review
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateDraftAsync(projectId, "REV-1", "Title", files, TestContext.Current.CancellationToken));

        // Assert: the file count rule is reported
        Assert.Equal(DomainRule.TooManyFiles, exception.Rule);
    }

    /// <summary>
    ///     Test that files that are each within limits but together exceed the total limit are rejected.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_TotalTooLarge_ThrowsTotalSizeExceeded()
    {
        // Arrange: a project and eleven files of the per-file maximum, which together exceed the total limit
        var (_, service, projectId) = await CreateWithProjectAsync();
        var files = Enumerable.Range(0, 11)
            .Select(i => new UploadedFile($"f{i}.bin", new byte[ReviewLimits.MaxFileSizeBytes]))
            .ToList();

        // Act: create the review
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateDraftAsync(projectId, "REV-1", "Title", files, TestContext.Current.CancellationToken));

        // Assert: the total size rule is reported
        Assert.Equal(DomainRule.TotalSizeExceeded, exception.Rule);
    }

    /// <summary>
    ///     Test that a created review can be read back, listed, and its file content fetched verbatim.
    /// </summary>
    [Fact]
    public async Task ReviewService_GetAsync_CreatedReview_ReturnsReviewListAndContent()
    {
        // Arrange: a project with one review of two files
        var (_, service, projectId) = await CreateWithProjectAsync();
        await service.CreateDraftAsync(
            projectId, "REV-1", "Title", [new("a.txt", TestContent.A), new("c.bin", TestContent.C)], TestContext.Current.CancellationToken);

        // Act: read the review, the list and one file's content
        var review = await service.GetAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        var list = await service.ListAsync(projectId, TestContext.Current.CancellationToken);
        var content = await service.GetFileContentAsync(projectId, "REV-1", "c.bin", TestContext.Current.CancellationToken);
        var missing = await service.GetAsync(projectId, "NOPE", TestContext.Current.CancellationToken);

        // Assert: found, listed, content intact, unknown id gives null
        Assert.Equal(2, review?.Files.Count);
        Assert.Single(list);
        Assert.Equal(TestContent.C, content);
        Assert.Null(missing);
    }

    /// <summary>
    ///     Test that the content of a file that does not exist, or of a review that does not exist, is null.
    /// </summary>
    [Fact]
    public async Task ReviewService_GetFileContentAsync_MissingFileOrReview_ReturnsNull()
    {
        // Arrange: a project with one review of one file
        var (_, service, projectId) = await CreateWithProjectAsync();
        await service.CreateDraftAsync(projectId, "REV-1", "Title", One("a.txt", TestContent.A), TestContext.Current.CancellationToken);

        // Act: read a missing file of the review, and a file of a missing review
        var missingFile = await service.GetFileContentAsync(projectId, "REV-1", "missing.txt", TestContext.Current.CancellationToken);
        var missingReview = await service.GetFileContentAsync(projectId, "NOPE", "a.txt", TestContext.Current.CancellationToken);

        // Assert: nothing is returned for either
        Assert.Null(missingFile);
        Assert.Null(missingReview);
    }
}
