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
///     Unit tests of the checklist that <see cref="ReviewService"/> freezes into a new review: the definition of the
///     project at both ways of creating a review, the default for a project without one, the refusal of a definition
///     that cannot be used, and the immunity of existing reviews to a later change of the project's checklist.
/// </summary>
public class ReviewServiceChecklistTests
{
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
    /// <param name="Reviews">The review service under test.</param>
    /// <param name="Checklists">The service that reads the checklist of a review.</param>
    /// <param name="ProjectChecklists">The service that changes the checklist of the project.</param>
    /// <param name="ProjectId">The project the reviews go into.</param>
    /// <param name="RepositoryId">The enabled repository.</param>
    private sealed record Fixture(
        FakeReviewStore Store,
        ReviewService Reviews,
        ChecklistService Checklists,
        ProjectChecklistService ProjectChecklists,
        int ProjectId,
        int RepositoryId);

    /// <summary>
    ///     Builds the JSON text of a definition with one section and one check.
    /// </summary>
    /// <param name="version">The version label.</param>
    /// <param name="text">The wording of the check.</param>
    /// <returns>The JSON text.</returns>
    private static string Custom(string version, string text = "Project wording")
    {
        return new ChecklistDefinition(
            version,
            [new ChecklistSection("proj", "Project", string.Empty, [new ChecklistItem("proj-01", text)])]).ToJson();
    }

    /// <summary>
    ///     Creates the fixture, with a repository whose head <c>main</c> has one added file.
    /// </summary>
    /// <returns>The fixture.</returns>
    private static async Task<Fixture> CreateAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var clock = new FixedTimeProvider(_now);
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), clock);
        await users.CreateAsync("adm", "Adm", UserRole.Admin, "correct horse battery", cancellationToken);
        var repositories = TestServices.Repositories(store, clock);
        var project = await TestServices.Projects(store, clock).CreateAsync("Apollo", null, cancellationToken);
        var repository = await repositories.CreateAsync(
            project.Id, "main-repo", "https://example.com/org/repo.git", null, null, _actor, cancellationToken);
        var git = new FakeGitRepositoryProvider();
        git.Revisions["main"] = HeadSha;
        git.Script(null, HeadSha, [("src/new.txt", ChangeKind.Added, null, "world\n")]);
        return new Fixture(
            store,
            TestServices.Reviews(store, clock, repositories, git),
            TestServices.Checklists(store, clock),
            TestServices.ProjectChecklists(store),
            project.Id,
            repository.Id);
    }

    /// <summary>
    ///     Sets the checklist of the project.
    /// </summary>
    /// <param name="f">The fixture.</param>
    /// <param name="currentJson">The stored text the change starts from; <c>null</c> for the default.</param>
    /// <param name="json">The new text.</param>
    /// <returns>A task that completes when the checklist is stored.</returns>
    private static async Task SetProjectChecklistAsync(Fixture f, string? currentJson, string json)
    {
        Assert.True(await f.ProjectChecklists.SetAsync(
            _actor, f.ProjectId, ProjectChecklistService.HashOf(currentJson), json, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Creates an upload review, which is one way of creating a review.
    /// </summary>
    /// <param name="f">The fixture.</param>
    /// <param name="id">The review id.</param>
    /// <returns>A task that completes when the review is created.</returns>
    private static Task UploadAsync(Fixture f, string id)
    {
        return f.Reviews.CreateDraftAsync(
            f.ProjectId, id, "Upload", [new UploadedFile("a.txt", TestContent.A)], _actor, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Creates a review from the repository, which is the other way of creating a review (and the way the
    ///     review planner uses).
    /// </summary>
    /// <param name="f">The fixture.</param>
    /// <param name="id">The review id.</param>
    /// <returns>A task that completes when the review is created.</returns>
    private static Task FromRepositoryAsync(Fixture f, string id)
    {
        return f.Reviews.CreateFromRepositoryAsync(
            f.ProjectId,
            id,
            "Repository",
            new RepositorySource(f.RepositoryId, "main", null, "**/*"),
            _actor,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Gets the stored review.
    /// </summary>
    /// <param name="f">The fixture.</param>
    /// <param name="id">The review id.</param>
    /// <returns>The review.</returns>
    private static Review ReviewOf(Fixture f, string id) => f.Store.Reviews.Single(r => r.Id == id);

    /// <summary>
    ///     Test that a review created from an upload and a review created from a repository each freeze the
    ///     definition of the project, in its normal form, with its version label.
    /// </summary>
    [Fact]
    public async Task ReviewService_Create_ProjectChecklist_IsFrozenAtBothCreatePaths()
    {
        // Arrange: the project has a checklist of its own
        var f = await CreateAsync();
        await SetProjectChecklistAsync(f, null, Custom("proj-1"));

        // Act
        await UploadAsync(f, "up-1");
        await FromRepositoryAsync(f, "repo-1");

        // Assert: both reviews carry the project's definition, and the service reads it back
        foreach (var id in new[] { "up-1", "repo-1" })
        {
            var checklist = ReviewOf(f, id).Checklist!;
            Assert.Equal("proj-1", checklist.Version);
            Assert.Equal(Custom("proj-1"), checklist.Definition);
            var state = (await f.Checklists.GetAsync(f.ProjectId, id, TestContext.Current.CancellationToken))!;
            Assert.Equal("Project wording", state.Definition.Sections[0].Items[0].Text);
        }
    }

    /// <summary>
    ///     Test that a project without a checklist of its own, and a project that went back to the default, give
    ///     the built-in default to a new review at both create paths.
    /// </summary>
    [Fact]
    public async Task ReviewService_Create_NoProjectChecklist_FreezesTheDefaultAtBothCreatePaths()
    {
        // Arrange: one project that never had a checklist, and one that was reset
        var f = await CreateAsync();
        await UploadAsync(f, "up-1");
        await FromRepositoryAsync(f, "repo-1");
        await SetProjectChecklistAsync(f, null, Custom("proj-1"));
        Assert.True(await f.ProjectChecklists.ResetAsync(
            _actor, f.ProjectId, ProjectChecklistService.HashOf(Custom("proj-1")), TestContext.Current.CancellationToken));

        // Act
        await UploadAsync(f, "up-2");
        await FromRepositoryAsync(f, "repo-2");

        // Assert
        foreach (var id in new[] { "up-1", "repo-1", "up-2", "repo-2" })
        {
            var checklist = ReviewOf(f, id).Checklist!;
            Assert.Equal(DefaultChecklist.Version, checklist.Version);
            Assert.Equal(DefaultChecklist.Current.ToJson(), checklist.Definition);
        }
    }

    /// <summary>
    ///     Test that a stored definition that cannot be used is never replaced by the default: the review is refused
    ///     with the invalid-definition rule at both create paths, and nothing is stored.
    /// </summary>
    /// <param name="fromRepository">Whether the review is created from the repository, or from an upload.</param>
    /// <param name="stored">The stored text.</param>
    [Theory]
    [InlineData(false, "{ not json")]
    [InlineData(true, "{ not json")]
    [InlineData(false, "{\"version\": \"x\", \"sections\": []}")]
    [InlineData(true, "{\"version\": \"x\", \"sections\": []}")]
    public async Task ReviewService_Create_UnusableProjectChecklist_FailsClosed(bool fromRepository, string stored)
    {
        // Arrange: a definition that was stored by some other means (an older version, a manual edit)
        var f = await CreateAsync();
        Assert.True(await f.Store.UpdateChecklistDefinitionAsync(
            f.ProjectId,
            ProjectChecklistService.HashOf(null),
            stored,
            AuditDraft.By(_actor, ProjectChecklistService.ChangedAction, "x", f.ProjectId),
            TestContext.Current.CancellationToken));

        // Act
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => fromRepository ? FromRepositoryAsync(f, "rev-1") : UploadAsync(f, "rev-1"));

        // Assert
        Assert.Equal(DomainRule.ChecklistDefinitionInvalid, exception.Rule);
        Assert.Contains("project " + f.ProjectId, exception.Message, StringComparison.Ordinal);
        Assert.Empty(f.Store.Reviews);
        Assert.DoesNotContain(f.Store.Audits, a => a.Action == "review.created");
    }

    /// <summary>
    ///     Test that changing the checklist of the project after a review was created changes neither the wording
    ///     the review shows nor its record digest, for both create paths, and that a review created afterwards
    ///     gets the new definition.
    /// </summary>
    [Fact]
    public async Task ReviewService_ProjectChecklistChangedLater_ExistingReviewsAreUnaffected()
    {
        // Arrange: a review of each kind under the first definition
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        await SetProjectChecklistAsync(f, null, Custom("proj-1"));
        await UploadAsync(f, "up-1");
        await FromRepositoryAsync(f, "repo-1");
        var digests = new Dictionary<string, string>
        {
            ["up-1"] = RecordDigest.Compute((await f.Store.GetRecordAsync(f.ProjectId, "up-1", cancellationToken))!),
            ["repo-1"] = RecordDigest.Compute((await f.Store.GetRecordAsync(f.ProjectId, "repo-1", cancellationToken))!)
        };

        // Act: the project changes its checklist, then goes back to the default, and a new review is created
        await SetProjectChecklistAsync(f, Custom("proj-1"), Custom("proj-2", "Changed wording"));
        Assert.True(await f.ProjectChecklists.ResetAsync(
            _actor, f.ProjectId, ProjectChecklistService.HashOf(Custom("proj-2", "Changed wording")), cancellationToken));
        await SetProjectChecklistAsync(f, null, Custom("proj-3", "Newest wording"));
        await UploadAsync(f, "up-2");

        // Assert: the existing reviews keep wording and digest; the new one has the newest definition
        foreach (var id in new[] { "up-1", "repo-1" })
        {
            var state = (await f.Checklists.GetAsync(f.ProjectId, id, cancellationToken))!;
            Assert.Equal("proj-1", state.Definition.Version);
            Assert.Equal("Project wording", state.Definition.Sections[0].Items[0].Text);
            Assert.Equal(digests[id], RecordDigest.Compute((await f.Store.GetRecordAsync(f.ProjectId, id, cancellationToken))!));
        }

        Assert.Equal("proj-3", ReviewOf(f, "up-2").Checklist!.Version);
    }

    /// <summary>
    ///     Test that a review that was approved and is therefore locked is not affected by a change of the
    ///     project's checklist: its record digest and its answers stay as they were and it stays locked.
    /// </summary>
    [Fact]
    public async Task ReviewService_ProjectChecklistChangedLater_ApprovedReviewIsUnaffected()
    {
        // Arrange: an approved review made under the default checklist
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FixedTimeProvider(_now);
        await UploadAsync(f, "rev-1");
        await AuditTestExtensions.ApproveReviewAsync(f.Store, clock, f.ProjectId, "rev-1", "approver", cancellationToken);
        var digestBefore = RecordDigest.Compute((await f.Store.GetRecordAsync(f.ProjectId, "rev-1", cancellationToken))!);

        // Act: the project replaces its checklist
        await SetProjectChecklistAsync(f, null, Custom("proj-1"));

        // Assert: digest, wording and answers are unchanged, and the review is still locked
        Assert.Equal(digestBefore, RecordDigest.Compute((await f.Store.GetRecordAsync(f.ProjectId, "rev-1", cancellationToken))!));
        var state = (await f.Checklists.GetAsync(f.ProjectId, "rev-1", cancellationToken))!;
        Assert.Equal(DefaultChecklist.Version, state.Definition.Version);
        Assert.Equal(state.Overall.Total, state.Overall.Answered);
        var first = DefaultChecklist.Current.Sections[0].Items[0];
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Checklists.SetAnswerAsync(
                f.ProjectId, "rev-1", first.Id, ChecklistOutcome.Fail, "Wrong.", TestActors.Ann, cancellationToken));
        Assert.Equal(DomainRule.ReviewLocked, exception.Rule);
    }
}
