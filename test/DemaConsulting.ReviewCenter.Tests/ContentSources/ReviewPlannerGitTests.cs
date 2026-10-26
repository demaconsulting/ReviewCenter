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

using DemaConsulting.ReviewCenter.ContentSources;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Tests.Storage;

namespace DemaConsulting.ReviewCenter.Tests.ContentSources;

/// <summary>
///     End-to-end tests of planning and creating reviews from a real temporary git repository, a real
///     <c>.reviewmark.yaml</c> and a real database: the plan rows, the ancestor fallback, the already-filed
///     detection, empty review-sets and the last reviewed baseline.
/// </summary>
public class ReviewPlannerGitTests
{
    /// <summary>
    ///     A password that satisfies the password policy; every test user has it.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The review configuration committed in the origin: two sets with files and one with none.
    /// </summary>
    private const string Config = """
        reviews:
          - id: core
            title: Core
            paths:
              - "src/**"
          - id: docs
            title: Docs
            paths: ["docs/**"]
          - id: empty
            title: Empty
            paths:
              - "nothing/**"
        """;

    /// <summary>
    ///     The acting user.
    /// </summary>
    private static readonly Actor _actor = new("adm", "Adm");

    /// <summary>
    ///     A provider that reads the origin folder instead of the registered location, so the test does not
    ///     depend on how a local path is written as a repository location.
    /// </summary>
    /// <param name="inner">The real git source.</param>
    /// <param name="folder">The origin folder.</param>
    private sealed class OriginProvider(IGitRepositoryProvider inner, string folder) : IGitRepositoryProvider
    {
        /// <inheritdoc />
        public Task<IGitRepository> OpenAsync(RepositoryAccess access, CancellationToken cancellationToken = default)
        {
            return inner.OpenAsync(access with { Location = folder }, cancellationToken);
        }
    }

    /// <summary>
    ///     The prepared scenario: the services over a real origin and a real database.
    /// </summary>
    /// <param name="Origin">The working folder of the origin repository.</param>
    /// <param name="Planner">The planner under test.</param>
    /// <param name="Signing">The signing service.</param>
    /// <param name="Checklists">The checklist service.</param>
    /// <param name="Store">The real review store.</param>
    /// <param name="ProjectId">The project the reviews go into.</param>
    /// <param name="RepositoryId">The registered repository.</param>
    /// <param name="AnnId">The identity of the user <c>ann</c>.</param>
    /// <param name="First">The commit tagged <c>v1</c>, where the configuration first exists.</param>
    private sealed record Scenario(
        string Origin,
        ReviewPlanner Planner,
        SigningService Signing,
        ChecklistService Checklists,
        ReviewStore Store,
        int ProjectId,
        int RepositoryId,
        int AnnId,
        string First);

    /// <summary>
    ///     Creates the origin: <c>v0</c> without a configuration, <c>v1</c> with it, and <c>main</c> one commit
    ///     later with <c>src/a.cs</c> modified and <c>src/b.cs</c> added.
    /// </summary>
    /// <param name="root">The temporary folder.</param>
    /// <param name="db">The database.</param>
    /// <returns>The scenario.</returns>
    private static async Task<Scenario> CreateAsync(TempFolder root, TestStore db)
    {
        var origin = Path.Combine(root.Path, "origin");
        Directory.CreateDirectory(origin);
        GitTestSupport.Init(origin);
        GitTestSupport.Write(origin, "README.txt", "readme\n");
        GitTestSupport.Commit(origin, "zero");
        GitTestSupport.Git(origin, "tag", "v0");
        GitTestSupport.Write(origin, ".reviewmark.yaml", Config);
        GitTestSupport.Write(origin, "src/a.cs", "one\n");
        GitTestSupport.Write(origin, "docs/a.md", "doc\n");
        var first = GitTestSupport.Commit(origin, "one");
        GitTestSupport.Git(origin, "tag", "v1");
        GitTestSupport.Write(origin, "src/a.cs", "two\n");
        GitTestSupport.Write(origin, "src/b.cs", "new\n");
        GitTestSupport.Commit(origin, "two");

        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = TimeProvider.System;
        var users = new UserService(db.Users, new FakePasswordHasher(), clock);
        await users.CreateAsync("adm", "Adm", UserRole.Admin, Password, cancellationToken);
        var ann = await users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, cancellationToken);
        var repositories = new RepositoryService(new FakeRepositoryStore(), new FakeCredentialProtector(), users, clock);
        var repository = await repositories.CreateAsync(
            "origin", "https://example.com/origin.git", null, null, _actor, cancellationToken);
        var options = new GitOptions { CachePath = Path.Combine(root.Path, "cache"), TimeoutSeconds = 60 };
        var provider = new OriginProvider(new GitSource(new GitRunner(options), options), origin);
        var project = await new ProjectService(db.Store, clock).CreateAsync("Apollo", null, cancellationToken);
        var reviews = new ReviewService(db.Store, clock, repositories, provider);
        var signing = new SigningService(db.Store, users, clock);
        var planner = new ReviewPlanner(db.Store, reviews, repositories, provider, signing, new ReviewMarkConfigParser());
        return new Scenario(
            origin,
            planner,
            signing,
            new ChecklistService(db.Store, clock),
            db.Store,
            project.Id,
            repository.Id,
            ann.Id,
            first);
    }

    /// <summary>
    ///     Plans a head revision.
    /// </summary>
    /// <param name="s">The scenario.</param>
    /// <param name="head">The head revision.</param>
    /// <param name="mode">The baseline mode.</param>
    /// <param name="baseline">The explicit baseline revision.</param>
    /// <returns>The plan.</returns>
    private static Task<ReviewPlan> PlanAsync(Scenario s, string head, BaselineMode mode, string? baseline = null)
    {
        return s.Planner.PlanAsync(s.ProjectId, s.RepositoryId, head, mode, baseline, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Creates the reviews of the rows of a plan that can be created.
    /// </summary>
    /// <param name="s">The scenario.</param>
    /// <param name="plan">The plan.</param>
    /// <param name="setIds">The sets to create; every row that can be created when omitted.</param>
    /// <returns>The results.</returns>
    private static Task<IReadOnlyList<PlanCreateResult>> CreateRowsAsync(Scenario s, ReviewPlan plan, params string[] setIds)
    {
        var requests = plan.Rows
            .Where(row => row.Status == PlanRowStatus.WillCreate && (setIds.Length == 0 || setIds.Contains(row.SetId)))
            .Select(row => new PlanCreateRequest(row.SetId, row.Title, row.Selection, row.BaselineSha))
            .ToList();
        return s.Planner.CreateAsync(
            s.ProjectId, s.RepositoryId, plan.HeadSha, requests, _actor, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Signs a review: answers the whole checklist, adds a slot for ann and lets ann sign it.
    /// </summary>
    /// <param name="s">The scenario.</param>
    /// <param name="reviewId">The review to sign.</param>
    /// <returns>A task that completes when the review is signed.</returns>
    private static async Task SignAsync(Scenario s, string reviewId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await s.Checklists.SetAnswerAsync(
                s.ProjectId, reviewId, item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, cancellationToken);
        }

        var slot = await s.Signing.AddSlotAsync(TestActors.Ann, s.ProjectId, reviewId, s.AnnId, null, cancellationToken);
        var digest = (await s.Signing.GetApprovalAsync(s.ProjectId, reviewId, cancellationToken))!.Digest;
        await s.Signing.SignAsync(TestActors.Ann, s.ProjectId, reviewId, slot.Id, digest, Password, cancellationToken);
    }

    /// <summary>
    ///     Test that a plan from a real repository classifies the sets for a full review and for an explicit
    ///     baseline, skipping the set that selects no file.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_RealGit_NoBaselineAndExplicitBaseline()
    {
        // Arrange: the origin, the database and the planner
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        using var db = new TestStore();
        var s = await CreateAsync(root, db);

        // Act: plan main with no baseline, then since the tag v1
        var full = await PlanAsync(s, "main", BaselineMode.None);
        var delta = await PlanAsync(s, "main", BaselineMode.Explicit, "v1");

        // Assert: a full review has both source files; the empty set is skipped
        Assert.Equal(["core", "docs", "empty"], full.Rows.Select(row => row.SetId));
        Assert.Equal((2, 0, 0), (full.Rows[0].Added, full.Rows[0].Modified, full.Rows[0].Deleted));
        Assert.Equal((1, 0, 0), (full.Rows[1].Added, full.Rows[1].Modified, full.Rows[1].Deleted));
        Assert.Equal(PlanRowStatus.NothingToReview, full.Rows[2].Status);

        // Assert: since v1, core has one modified and one added file and docs has nothing
        Assert.Equal(s.First, delta.Rows[0].BaselineSha);
        Assert.Equal((1, 1, 0), (delta.Rows[0].Added, delta.Rows[0].Modified, delta.Rows[0].Deleted));
        Assert.Equal(PlanRowStatus.NothingToReview, delta.Rows[1].Status);
        Assert.Equal(PlanRowStatus.NothingToReview, delta.Rows[2].Status);
    }

    /// <summary>
    ///     Test the whole flow against a real repository: file reviews at v1, sign one, plan the last reviewed
    ///     baseline for main, create the delta, and see the rows turn into already filed.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_CreateAsync_RealGit_LastReviewedBaselineThenAlreadyFiled()
    {
        // Arrange: reviews of core and docs at v1, with core signed
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        using var db = new TestStore();
        var s = await CreateAsync(root, db);
        var atV1 = await CreateRowsAsync(s, await PlanAsync(s, "v1", BaselineMode.None));
        await SignAsync(s, "core");

        // Act: plan main from the last reviewed heads, create what is planned, and plan again
        var plan = await PlanAsync(s, "main", BaselineMode.LastReviewed);
        var created = await CreateRowsAsync(s, plan);
        var again = await PlanAsync(s, "main", BaselineMode.LastReviewed);

        // Assert: v1 got its two reviews
        Assert.Equal(["core", "docs"], atV1.Select(result => result.ReviewId));

        // Assert: core is a delta since the signed head; docs has no signed review and is a full review; empty is skipped
        var core = plan.Rows[0];
        Assert.Equal(PlanRowStatus.WillCreate, core.Status);
        Assert.Equal(s.First, core.BaselineSha);
        Assert.Equal((1, 1, 0), (core.Added, core.Modified, core.Deleted));
        Assert.Equal("Changes since review 'core' (" + s.First[..7] + ")", core.BaselineNote);
        Assert.Null(plan.Rows[1].BaselineSha);
        Assert.Equal(PlanRowStatus.NothingToReview, plan.Rows[2].Status);

        // Assert: the reviews were created under new ids, frozen with the baseline and the set
        Assert.Equal(["core-2", "docs-2"], created.Select(result => result.ReviewId));
        var review = (await s.Store.GetReviewAsync(s.ProjectId, "core-2", TestContext.Current.CancellationToken))!;
        Assert.Equal(s.First, review.BaselineSha);
        Assert.Equal("core", review.ReviewSetId);
        Assert.Equal(["src/a.cs", "src/b.cs"], review.Files.Select(file => file.Path));
        var record = (await s.Store.GetRecordAsync(s.ProjectId, "core-2", TestContext.Current.CancellationToken))!;
        Assert.Equal("core", record.Source.ReviewSetId);
        Assert.Equal(64, RecordDigest.Compute(record).Length);

        // Assert: planning again says that both are filed
        Assert.Equal([PlanRowStatus.AlreadyFiled, PlanRowStatus.AlreadyFiled, PlanRowStatus.NothingToReview], again.Rows.Select(row => row.Status));
        Assert.Equal(["core-2", "docs-2"], again.Rows.Take(2).Select(row => row.ExistingReviewId));
    }

    /// <summary>
    ///     Test that a last signed review on another branch, which is not an ancestor of the head, gives a full review.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_RealGit_NotAncestor_FallsBackToFullReview()
    {
        // Arrange: a signed review of core made at the head of a side branch that main does not contain
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        using var db = new TestStore();
        var s = await CreateAsync(root, db);
        GitTestSupport.Git(s.Origin, "checkout", "-b", "side", "v1");
        GitTestSupport.Write(s.Origin, "src/side.cs", "side\n");
        GitTestSupport.Commit(s.Origin, "side");
        GitTestSupport.Git(s.Origin, "checkout", "main");
        await CreateRowsAsync(s, await PlanAsync(s, "side", BaselineMode.None), "core");
        await SignAsync(s, "core");

        // Act: plan main from the last reviewed head
        var plan = await PlanAsync(s, "main", BaselineMode.LastReviewed);

        // Assert: a full review of both files, and the note says why
        var core = plan.Rows[0];
        Assert.Equal(PlanRowStatus.WillCreate, core.Status);
        Assert.Null(core.BaselineSha);
        Assert.Equal((2, 0, 0), (core.Added, core.Modified, core.Deleted));
        Assert.Contains("is not an ancestor of this revision", core.BaselineNote);
    }

    /// <summary>
    ///     Test that a changed selection gives a full review against a real repository, even though the last
    ///     signed head is an ancestor.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_RealGit_SelectionChanged_FallsBackToFullReview()
    {
        // Arrange: a signed review of core at v1, then a configuration on main that selects docs too
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        using var db = new TestStore();
        var s = await CreateAsync(root, db);
        await CreateRowsAsync(s, await PlanAsync(s, "v1", BaselineMode.None), "core");
        await SignAsync(s, "core");
        GitTestSupport.Write(s.Origin, ".reviewmark.yaml", Config.Replace("- \"src/**\"", "- \"src/**\"\n      - \"docs/**\"", StringComparison.Ordinal));
        GitTestSupport.Commit(s.Origin, "wider selection");

        // Act: plan main from the last reviewed head
        var plan = await PlanAsync(s, "main", BaselineMode.LastReviewed);

        // Assert: a full review of the new selection, with the reason
        var core = plan.Rows[0];
        Assert.Null(core.BaselineSha);
        Assert.Equal("src/**\ndocs/**", core.Selection);
        Assert.Contains("file selection changed", core.BaselineNote);
        Assert.Equal((3, 0, 0), (core.Added, core.Modified, core.Deleted));
    }

    /// <summary>
    ///     Test that a head without a configuration and a head that does not exist fail the plan with their rules.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_RealGit_NoConfigurationOrUnknownHead_ThrowTheirRules()
    {
        // Arrange: the origin, where v0 has no .reviewmark.yaml
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        using var db = new TestStore();
        var s = await CreateAsync(root, db);

        // Act: plan v0 and a branch that does not exist
        var missing = await Assert.ThrowsAsync<DomainRuleException>(() => PlanAsync(s, "v0", BaselineMode.None));
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(() => PlanAsync(s, "no-such-branch", BaselineMode.None));

        // Assert: each has its own rule
        Assert.Equal(DomainRule.ReviewSetConfigMissing, missing.Rule);
        Assert.Equal(DomainRule.RevisionNotFound, unknown.Rule);
        Assert.Contains(".reviewmark.yaml", missing.Message);
    }
}
