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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="ReviewService.CreateFromRepositoryAsync"/> using in-memory fakes: what is frozen,
///     the empty-selection refusal, the limits, the fingerprint, the audit detail and the record digest binding.
/// </summary>
public class ReviewServiceRepositoryTests
{
    /// <summary>
    ///     A commit identifier used as the baseline.
    /// </summary>
    private const string BaseSha = "1111111111111111111111111111111111111111";

    /// <summary>
    ///     A commit identifier used as the head.
    /// </summary>
    private const string HeadSha = "2222222222222222222222222222222222222222";

    /// <summary>
    ///     The instant reported by the clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The acting user.
    /// </summary>
    private static readonly Actor _actor = new("adm", "Adm");

    /// <summary>
    ///     The fixture: the services over fakes, one enabled repository and the project.
    /// </summary>
    /// <param name="Store">The fake review store.</param>
    /// <param name="Service">The review service under test.</param>
    /// <param name="Repositories">The repository registry service.</param>
    /// <param name="Git">The scripted git provider.</param>
    /// <param name="ProjectId">The project the reviews go into.</param>
    /// <param name="RepositoryId">The enabled repository.</param>
    private sealed record Fixture(
        FakeReviewStore Store,
        ReviewService Service,
        RepositoryService Repositories,
        FakeGitRepositoryProvider Git,
        int ProjectId,
        int RepositoryId);

    /// <summary>
    ///     Creates the fixture, with the revisions <c>main</c> and <c>v1</c> known and a scripted change set:
    ///     <c>src/new.txt</c> added, <c>src/mod.txt</c> modified, <c>docs/gone.md</c> deleted.
    /// </summary>
    /// <returns>The fixture.</returns>
    private static async Task<Fixture> CreateAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FixedTimeProvider(_now);
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), clock);
        await users.CreateAsync("adm", "Adm", UserRole.Admin, "correct horse battery", cancellationToken);
        var repositories = new RepositoryService(new FakeRepositoryStore(), new FakeCredentialProtector(), users, clock);
        var repository = await repositories.CreateAsync(
            "main-repo", "https://example.com/org/repo.git", null, null, _actor, cancellationToken);
        var git = new FakeGitRepositoryProvider();
        git.Revisions["main"] = HeadSha;
        git.Revisions["v1"] = BaseSha;
        git.Script(
            BaseSha,
            HeadSha,
            [
                ("docs/gone.md", ChangeKind.Deleted, "old doc\n", null),
                ("src/mod.txt", ChangeKind.Modified, "world\n", "hello\n"),
                ("src/new.txt", ChangeKind.Added, null, "world\n")
            ]);
        var store = new FakeReviewStore();
        var project = await new ProjectService(store, clock).CreateAsync("Apollo", null, cancellationToken);
        return new Fixture(
            store,
            new ReviewService(store, clock, repositories, git),
            repositories,
            git,
            project.Id,
            repository.Id);
    }

    /// <summary>
    ///     Creates a review from the scripted repository.
    /// </summary>
    /// <param name="f">The fixture.</param>
    /// <param name="id">The review id.</param>
    /// <param name="baseline">The baseline revision, or <c>null</c>.</param>
    /// <param name="selection">The selection text.</param>
    /// <param name="repositoryId">The repository, or <c>null</c> for the fixture's.</param>
    /// <returns>The result.</returns>
    private static Task<RepositoryReviewResult> CreateReviewAsync(
        Fixture f,
        string id = "rev-1",
        string? baseline = "v1",
        string selection = "**/*",
        int? repositoryId = null)
    {
        return f.Service.CreateFromRepositoryAsync(
            f.ProjectId,
            id,
            "Title",
            repositoryId ?? f.RepositoryId,
            "main",
            baseline,
            selection,
            _actor,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Test that a review from a repository freezes every change with its kind and both contents, plus the
    ///     repository, both commits and the selection.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_Changes_FreezesFilesAndSource()
    {
        // Arrange: the fixture with an added, a modified and a deleted file
        var f = await CreateAsync();

        // Act: create the review with the default selection
        var result = await CreateReviewAsync(f);

        // Assert: the source is recorded on the review
        var review = f.Store.Reviews.Single();
        Assert.Same(review, result.Review);
        Assert.Equal(f.RepositoryId, review.RepositoryId);
        Assert.Equal("main-repo", review.RepositoryName);
        Assert.Equal(BaseSha, review.BaselineSha);
        Assert.Equal(HeadSha, review.HeadSha);
        Assert.Equal("**/*", review.Selection);
        Assert.True(review.IsFromRepository);
        Assert.Equal(ReviewState.Draft, review.State);

        // Assert: the files are ordered by path and carry kind, head values and baseline values
        Assert.Equal(["docs/gone.md", "src/mod.txt", "src/new.txt"], review.Files.Select(file => file.Path));
        var deleted = review.Files[0];
        Assert.Equal(ChangeKind.Deleted, deleted.ChangeKind);
        Assert.Empty(deleted.Content);
        Assert.Equal(0, deleted.Size);
        Assert.Equal(string.Empty, deleted.Sha256);
        Assert.Equal("old doc\n", Encoding.UTF8.GetString(deleted.BaselineContent!));
        Assert.Equal(8, deleted.BaselineSize);
        var modified = review.Files[1];
        Assert.Equal(ChangeKind.Modified, modified.ChangeKind);
        Assert.Equal("hello\n", Encoding.UTF8.GetString(modified.Content));
        Assert.Equal(TestContent.HashA, modified.Sha256);
        Assert.Equal("world\n", Encoding.UTF8.GetString(modified.BaselineContent!));
        Assert.Equal(TestContent.HashB, modified.BaselineSha256);
        var added = review.Files[2];
        Assert.Equal(ChangeKind.Added, added.ChangeKind);
        Assert.Equal(TestContent.HashB, added.Sha256);
        Assert.Null(added.BaselineContent);
        Assert.Null(added.BaselineSha256);
    }

    /// <summary>
    ///     Test that the fingerprint covers only the files that exist at the head and equals the content
    ///     fingerprint of those files.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_Changes_FingerprintCoversHeadFilesOnly()
    {
        // Arrange: the fixture
        var f = await CreateAsync();

        // Act: create the review
        var result = await CreateReviewAsync(f);

        // Assert: the fingerprint is that of {hello, world}, in either order, and ignores the deleted file
        Assert.Equal(Fingerprint.Compute([TestContent.HashA, TestContent.HashB]), result.Review.ContentFingerprint);
        Assert.Equal(Fingerprint.Compute([TestContent.HashB, TestContent.HashA]), result.Review.ContentFingerprint);
    }

    /// <summary>
    ///     Test that the selection chooses the files: patterns include, a leading <c>!</c> excludes.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_Selection_ChoosesFiles()
    {
        // Arrange: the fixture
        var f = await CreateAsync();

        // Act: select everything under src except one file, with blank lines around the patterns
        var result = await CreateReviewAsync(f, selection: "\nsrc/**\n!src/mod.txt\n");

        // Assert: only the added file is reviewed, and the normalized selection text is stored
        Assert.Equal(["src/new.txt"], result.Review.Files.Select(file => file.Path));
        Assert.Equal("src/**\n!src/mod.txt", result.Review.Selection);
    }

    /// <summary>
    ///     Test that a deleted file is matched by its path, so a selection can ask for it.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_SelectionOfDeletedFile_ReviewsIt()
    {
        // Arrange: the fixture
        var f = await CreateAsync();

        // Act: select only the documentation
        var result = await CreateReviewAsync(f, selection: "docs/**");

        // Assert: the deleted file is the only file
        var file = Assert.Single(result.Review.Files);
        Assert.Equal(ChangeKind.Deleted, file.ChangeKind);
        Assert.Equal(Fingerprint.Compute(Array.Empty<string>()), result.Review.ContentFingerprint);
    }

    /// <summary>
    ///     Test that a selection that matches no changed file refuses the review with the documented message and
    ///     stores nothing.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_NoMatchingChange_RefusesWithNothingToReview()
    {
        // Arrange: the fixture
        var f = await CreateAsync();

        // Act: select files that did not change
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => CreateReviewAsync(f, selection: "other/**"));

        // Assert: the rule, the exact message with both revisions as typed, and nothing stored or audited
        Assert.Equal(DomainRule.NothingToReview, ex.Rule);
        Assert.Equal("Nothing to review: no files matching the selection changed between v1 and main", ex.Message);
        Assert.Empty(f.Store.Reviews);
        Assert.Empty(CreatedAudits(f));
        Assert.Equal(0, f.Git.BlobReads);
    }

    /// <summary>
    ///     Test that the refusal names the beginning of the history when there is no baseline.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_NoBaselineNoMatch_NamesBeginningOfHistory()
    {
        // Arrange: the fixture and a head with one file
        var f = await CreateAsync();
        f.Git.Script(null, HeadSha, [("a.txt", ChangeKind.Added, null, "hello\n")]);

        // Act: select a file that does not exist
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => CreateReviewAsync(f, baseline: null, selection: "b.txt"));

        // Assert: the message
        Assert.Equal(
            "Nothing to review: no files matching the selection changed between the beginning of the history and main",
            ex.Message);
    }

    /// <summary>
    ///     Test that a blank baseline means no baseline: every file of the head is an addition.
    /// </summary>
    /// <param name="baseline">The blank baseline text.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReviewService_CreateFromRepositoryAsync_BlankBaseline_ReviewsAllHeadFilesAsAdded(string? baseline)
    {
        // Arrange: the fixture with a full head listing
        var f = await CreateAsync();
        f.Git.Script(null, HeadSha, [("a.txt", ChangeKind.Added, null, "hello\n"), ("b.txt", ChangeKind.Added, null, "world\n")]);

        // Act: create without a baseline
        var result = await CreateReviewAsync(f, baseline: baseline);

        // Assert: no baseline commit, all added
        Assert.Null(result.Review.BaselineSha);
        Assert.All(result.Review.Files, file => Assert.Equal(ChangeKind.Added, file.ChangeKind));
        Assert.Equal(2, result.Review.Files.Count);
    }

    /// <summary>
    ///     Test that symbolic links and submodules are reported to the creator and counted in the audit detail,
    ///     but only when they match the selection.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_SkippedEntries_ReturnedAndCounted()
    {
        // Arrange: a change set with two skipped entries, one outside the selection
        var f = await CreateAsync();
        f.Git.Script(
            BaseSha,
            HeadSha,
            [("src/a.txt", ChangeKind.Added, null, "hello\n")],
            new SkippedEntry("src/link", "Symbolic links are not reviewed."),
            new SkippedEntry("vendor/sub", "Submodules are not reviewed."));

        // Act: create with a selection that covers only src
        var result = await CreateReviewAsync(f, selection: "src/**");

        // Assert: only the matching skipped entry is returned, and the audit counts it
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal("src/link", skipped.Path);
        Assert.Contains("1 skipped", CreatedAudits(f).Single().Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the audit entry names the repository, both commits, the counts and the fingerprint.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_Changes_AuditDetailDescribesSource()
    {
        // Arrange: the fixture
        var f = await CreateAsync();

        // Act: create the review
        var result = await CreateReviewAsync(f);

        // Assert: one review.created entry with the documented content
        var audit = Assert.Single(CreatedAudits(f));
        Assert.Equal("review.created", audit.Action);
        Assert.Equal("adm", audit.ActorUsername);
        Assert.Equal(f.ProjectId, audit.ProjectId);
        Assert.Equal("rev-1", audit.ReviewId);
        Assert.Equal(
            $"review 'Title' from repository 'main-repo', baseline {BaseSha}, head {HeadSha}, 3 files: " +
            $"1 added, 1 modified, 1 deleted, 0 skipped, fingerprint {result.Review.ContentFingerprint}",
            audit.Detail);
    }

    /// <summary>
    ///     Test that reading the review and its content later never opens the repository, and that a change in the
    ///     repository afterwards does not change the review, its content or its record digest.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_RepositoryChangesLater_ReviewIsUnaffected()
    {
        // Arrange: a created review and its digest
        var f = await CreateAsync();
        await CreateReviewAsync(f);
        var cancellationToken = TestContext.Current.CancellationToken;
        var digestBefore = RecordDigest.Compute((await f.Store.GetRecordAsync(f.ProjectId, "rev-1", cancellationToken))!);
        Assert.Single(f.Git.Opened);

        // Act: the repository moves on (new head, new blob content, new changes), then the review is read
        f.Git.Revisions["main"] = "3333333333333333333333333333333333333333";
        foreach (var key in f.Git.Blobs.Keys.ToList())
        {
            f.Git.Blobs[key] = Encoding.UTF8.GetBytes("changed later");
        }

        f.Git.Changes.Clear();
        var review = await f.Service.GetAsync(f.ProjectId, "rev-1", cancellationToken);
        var head = await f.Service.GetFileContentAsync(f.ProjectId, "rev-1", "src/mod.txt", cancellationToken);
        var baseline = await f.Service.GetBaselineContentAsync(f.ProjectId, "rev-1", "src/mod.txt", cancellationToken);
        var digestAfter = RecordDigest.Compute((await f.Store.GetRecordAsync(f.ProjectId, "rev-1", cancellationToken))!);

        // Assert: the frozen copies are still what was fetched, the digest is the same and git was not asked again
        Assert.Equal(HeadSha, review!.HeadSha);
        Assert.Equal("hello\n", Encoding.UTF8.GetString(head!));
        Assert.Equal("world\n", Encoding.UTF8.GetString(baseline!));
        Assert.Equal(digestBefore, digestAfter);
        Assert.Single(f.Git.Opened);
    }

    /// <summary>
    ///     Test that a file over the per-file limit on either side is refused from its size, before any content is
    ///     read, and nothing is stored.
    /// </summary>
    /// <param name="side">Which side of the change is too large.</param>
    [Theory]
    [InlineData("head")]
    [InlineData("baseline")]
    public async Task ReviewService_CreateFromRepositoryAsync_FileTooLarge_RefusedBeforeReading(string side)
    {
        // Arrange: a modified file with one oversized side
        var f = await CreateAsync();
        var big = new string('x', (int)ReviewLimits.MaxFileSizeBytes + 1);
        f.Git.Script(
            BaseSha,
            HeadSha,
            [("big.txt", ChangeKind.Modified, side == "baseline" ? big : "a", side == "head" ? big : "b")]);

        // Act: create the review
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => CreateReviewAsync(f));

        // Assert: the rule names the file, nothing was read or stored
        Assert.Equal(DomainRule.FileTooLarge, ex.Rule);
        Assert.Contains("big.txt", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, f.Git.BlobReads);
        Assert.Empty(f.Store.Reviews);
    }

    /// <summary>
    ///     Test that the total size of both sides of all changes is limited, from sizes alone.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_TotalTooLarge_RefusedBeforeReading()
    {
        // Arrange: six files whose two sides together exceed the total limit while each stays under the file limit
        var f = await CreateAsync();
        var piece = new string('x', (int)ReviewLimits.MaxFileSizeBytes);
        var changes = Enumerable.Range(0, 6)
            .Select(i => ($"f{i}.txt", ChangeKind.Modified, (string?)piece, (string?)piece))
            .ToList();
        f.Git.Script(BaseSha, HeadSha, changes);

        // Act: create the review
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => CreateReviewAsync(f));

        // Assert: the total limit applies, nothing was read or stored
        Assert.Equal(DomainRule.TotalSizeExceeded, ex.Rule);
        Assert.Equal(0, f.Git.BlobReads);
        Assert.Empty(f.Store.Reviews);
    }

    /// <summary>
    ///     Test that too many changed files are refused.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_TooManyFiles_Refused()
    {
        // Arrange: one more file than allowed
        var f = await CreateAsync();
        var changes = Enumerable.Range(0, ReviewLimits.MaxFileCount + 1)
            .Select(i => ($"f{i:D5}.txt", ChangeKind.Added, (string?)null, (string?)"a"))
            .ToList();
        f.Git.Script(BaseSha, HeadSha, changes);

        // Act: create the review
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => CreateReviewAsync(f));

        // Assert: the rule, and no content was read
        Assert.Equal(DomainRule.TooManyFiles, ex.Rule);
        Assert.Equal(0, f.Git.BlobReads);
    }

    /// <summary>
    ///     Test that a disabled or unknown repository, a bad revision and an unusable selection are refused and
    ///     store nothing.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_BadRequests_AreRefusedWithTheirRule()
    {
        // Arrange: the fixture and a disabled repository
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var off = await f.Repositories.CreateAsync("off", "https://example.com/off.git", null, null, _actor, cancellationToken);
        await f.Repositories.SetEnabledAsync(off.Id, false, _actor, cancellationToken);

        // Act, Assert: each request fails with its own rule
        Assert.Equal(
            DomainRule.RepositoryDisabled,
            (await Assert.ThrowsAsync<DomainRuleException>(() => CreateReviewAsync(f, repositoryId: off.Id))).Rule);
        Assert.Equal(
            DomainRule.RepositoryNotFound,
            (await Assert.ThrowsAsync<DomainRuleException>(() => CreateReviewAsync(f, repositoryId: 999))).Rule);
        Assert.Equal(
            DomainRule.RevisionNotFound,
            (await Assert.ThrowsAsync<DomainRuleException>(() => CreateReviewAsync(f, baseline: "nope"))).Rule);
        Assert.Equal(
            DomainRule.RevisionInvalid,
            (await Assert.ThrowsAsync<DomainRuleException>(() => CreateReviewAsync(f, baseline: "--upload-pack=x"))).Rule);
        Assert.Equal(
            DomainRule.SelectionInvalid,
            (await Assert.ThrowsAsync<DomainRuleException>(() => CreateReviewAsync(f, selection: "  "))).Rule);
        Assert.Equal(
            DomainRule.ReviewIdInvalid,
            (await Assert.ThrowsAsync<DomainRuleException>(() => CreateReviewAsync(f, id: "bad id"))).Rule);
        Assert.Empty(f.Store.Reviews);
        Assert.Empty(CreatedAudits(f));
    }

    /// <summary>
    ///     Test that a missing head revision is refused, and that a git failure is passed on as its own rule.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_GitFailure_IsPassedOn()
    {
        // Arrange: a provider that cannot reach the repository
        var f = await CreateAsync();
        f.Git.OpenFailure = new DomainRuleException(DomainRule.GitAccessFailed, "git could not read the repository");

        // Act: create the review
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => CreateReviewAsync(f));

        // Assert: the rule is kept, nothing stored
        Assert.Equal(DomainRule.GitAccessFailed, ex.Rule);
        Assert.Empty(f.Store.Reviews);

        // Act, Assert: a blank head revision is refused before git is touched
        f.Git.OpenFailure = null;
        var opened = f.Git.Opened.Count;
        var blank = await Assert.ThrowsAsync<DomainRuleException>(() => f.Service.CreateFromRepositoryAsync(
            f.ProjectId, "rev-2", "Title", f.RepositoryId, " ", null, "**/*", _actor, TestContext.Current.CancellationToken));
        Assert.Equal(DomainRule.RevisionInvalid, blank.Rule);
        Assert.Equal(opened, f.Git.Opened.Count);
    }

    /// <summary>
    ///     Test that a duplicate review id and an unknown project are refused before the repository is opened.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_DuplicateIdOrUnknownProject_RefusedBeforeOpening()
    {
        // Arrange: a created review
        var f = await CreateAsync();
        await CreateReviewAsync(f);
        var opened = f.Git.Opened.Count;

        // Act, Assert: the same id again, and an unknown project
        var duplicate = await Assert.ThrowsAsync<DomainRuleException>(() => CreateReviewAsync(f));
        Assert.Equal(DomainRule.ReviewIdDuplicate, duplicate.Rule);
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(() => f.Service.CreateFromRepositoryAsync(
            f.ProjectId + 50, "rev-9", "Title", f.RepositoryId, "main", "v1", "**/*", _actor, TestContext.Current.CancellationToken));
        Assert.Equal(DomainRule.ProjectNotFound, unknown.Rule);
        Assert.Equal(opened, f.Git.Opened.Count);
    }

    /// <summary>
    ///     Test that a service created without repository access refuses to create from a repository.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_WithoutRepositories_Throws()
    {
        // Arrange: a service with only the store and the clock
        var service = new ReviewService(new FakeReviewStore(), new FixedTimeProvider(_now));

        // Act, Assert: the call is refused as a programming error
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateFromRepositoryAsync(
            1, "rev-1", "T", 1, "main", null, "**/*", _actor, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the record digest changes when the selection, the head commit or the repository changes, even
    ///     when the files are the same, so a signature binds to the source.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateFromRepositoryAsync_SameFilesOtherSource_ChangesDigest()
    {
        // Arrange: a review with the default selection and its digest
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateReviewAsync(f, "rev-a");
        var reference = await DigestAsync(f, "rev-a");

        // Act: the same files with another selection text, another head commit and another repository
        await CreateReviewAsync(f, "rev-b", selection: "**");
        f.Git.Revisions["other"] = "4444444444444444444444444444444444444444";
        f.Git.Script(BaseSha, "4444444444444444444444444444444444444444", [
            ("docs/gone.md", ChangeKind.Deleted, "old doc\n", null),
            ("src/mod.txt", ChangeKind.Modified, "world\n", "hello\n"),
            ("src/new.txt", ChangeKind.Added, null, "world\n")
        ]);
        await f.Service.CreateFromRepositoryAsync(
            f.ProjectId, "rev-c", "Title", f.RepositoryId, "other", "v1", "**/*", _actor, cancellationToken);
        var second = await f.Repositories.CreateAsync("second", "https://example.com/second.git", null, null, _actor, cancellationToken);
        await CreateReviewAsync(f, "rev-d", repositoryId: second.Id);

        // Assert: every digest is different from the reference and from each other
        var digests = new[] { reference, await DigestAsync(f, "rev-b"), await DigestAsync(f, "rev-c"), await DigestAsync(f, "rev-d") };
        Assert.Equal(4, digests.Distinct().Count());
    }

    /// <summary>
    ///     Gets the <c>review.created</c> audit entries recorded so far.
    /// </summary>
    /// <param name="f">The fixture.</param>
    /// <returns>The entries.</returns>
    private static List<AuditDraft> CreatedAudits(Fixture f)
    {
        return f.Store.Audits.Where(audit => audit.Action == "review.created").ToList();
    }

    /// <summary>
    ///     Computes the record digest of a stored review.
    /// </summary>
    /// <param name="f">The fixture.</param>
    /// <param name="reviewId">The review id.</param>
    /// <returns>The digest.</returns>
    private static async Task<string> DigestAsync(Fixture f, string reviewId)
    {
        var record = await f.Store.GetRecordAsync(f.ProjectId, reviewId, TestContext.Current.CancellationToken);
        return RecordDigest.Compute(record!);
    }
}
