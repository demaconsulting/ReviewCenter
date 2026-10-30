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
///     Unit tests for <see cref="ReviewPlanner"/> over in-memory fakes: how each review-set is classified, how
///     baselines are chosen and fall back, and how the picked reviews are created.
/// </summary>
public class ReviewPlannerTests
{
    /// <summary>
    ///     A password that satisfies the password policy; every test user has it.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The oldest commit of the scripted history.
    /// </summary>
    private static readonly string H0 = new('a', 40);

    /// <summary>
    ///     The middle commit of the scripted history; the revision <c>v1</c>.
    /// </summary>
    private static readonly string H1 = new('b', 40);

    /// <summary>
    ///     The newest commit of the scripted history; the revision <c>main</c>.
    /// </summary>
    private static readonly string H2 = new('c', 40);

    /// <summary>
    ///     The instant at which each test's clock starts.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The acting user.
    /// </summary>
    private static readonly Actor _actor = new("adm", "Adm");

    /// <summary>
    ///     The text of the scripted configuration file; the fake parser ignores it.
    /// </summary>
    private const string ConfigText = "reviews: []\n";

    /// <summary>
    ///     The fixture: the services over fakes, one enabled repository and the project.
    /// </summary>
    /// <param name="Store">The fake review store.</param>
    /// <param name="Clock">The movable clock.</param>
    /// <param name="Reviews">The review service.</param>
    /// <param name="Signing">The signing service.</param>
    /// <param name="Checklists">The checklist service.</param>
    /// <param name="Repositories">The repository registry service.</param>
    /// <param name="Git">The scripted git provider.</param>
    /// <param name="Parser">The scripted parser.</param>
    /// <param name="Planner">The planner under test.</param>
    /// <param name="ProjectId">The project the reviews go into.</param>
    /// <param name="RepositoryId">The enabled repository.</param>
    /// <param name="AnnId">The identity of the user <c>ann</c>.</param>
    private sealed record Fixture(
        FakeReviewStore Store,
        MovableTimeProvider Clock,
        ReviewService Reviews,
        SigningService Signing,
        ChecklistService Checklists,
        RepositoryService Repositories,
        FakeGitRepositoryProvider Git,
        FakeReviewSetParser Parser,
        ReviewPlanner Planner,
        int ProjectId,
        int RepositoryId,
        int AnnId);

    /// <summary>
    ///     Creates the fixture. The history is <c>H0</c> (<c>v0</c>), <c>H1</c> (<c>v1</c>) and <c>H2</c>
    ///     (<c>main</c>); the sets are <c>core</c> (<c>src/**</c>), <c>docs</c> (<c>docs/**</c>) and
    ///     <c>empty</c> (<c>nothing/**</c>). No commit is an ancestor of another until a test says so.
    /// </summary>
    /// <returns>The fixture.</returns>
    private static async Task<Fixture> CreateAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new MovableTimeProvider(_now);
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), clock);
        await users.CreateAsync("adm", "Adm", UserRole.Admin, Password, cancellationToken);
        var ann = await users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, cancellationToken);
        var store = new FakeReviewStore();
        var project = await TestServices.Projects(store, clock).CreateAsync("Apollo", null, cancellationToken);
        await store.AddMembershipAsync(ann.Id, project.Id, cancellationToken);
        var repositories = TestServices.Repositories(store, clock);
        var repository = await repositories.CreateAsync(
            project.Id, "main-repo", "https://example.com/org/repo.git", null, null, _actor, cancellationToken);
        var git = new FakeGitRepositoryProvider();
        git.Revisions["main"] = H2;
        git.Revisions["v1"] = H1;
        git.Revisions["v0"] = H0;
        git.Script(
            null,
            H0,
            [
                (".reviewmark.yaml", ChangeKind.Added, null, ConfigText),
                ("docs/a.md", ChangeKind.Added, null, "doc\n"),
                ("src/a.cs", ChangeKind.Added, null, "one\n")
            ]);
        git.Script(
            null,
            H1,
            [
                (".reviewmark.yaml", ChangeKind.Added, null, ConfigText),
                ("docs/a.md", ChangeKind.Added, null, "doc\n"),
                ("src/a.cs", ChangeKind.Added, null, "two\n")
            ]);
        git.Script(
            null,
            H2,
            [
                (".reviewmark.yaml", ChangeKind.Added, null, ConfigText),
                ("docs/a.md", ChangeKind.Added, null, "doc\n"),
                ("src/a.cs", ChangeKind.Added, null, "three\n"),
                ("src/b.cs", ChangeKind.Added, null, "new\n")
            ]);
        git.Script(
            H1,
            H2,
            [
                ("src/a.cs", ChangeKind.Modified, "two\n", "three\n"),
                ("src/b.cs", ChangeKind.Added, null, "new\n")
            ]);
        git.Script(
            H0,
            H2,
            [
                ("src/a.cs", ChangeKind.Modified, "one\n", "three\n"),
                ("src/b.cs", ChangeKind.Added, null, "new\n")
            ]);
        var parser = new FakeReviewSetParser
        {
            Sets =
            [
                new("core", "Core", ["src/**"]),
                new("docs", "Documentation", ["docs/**"]),
                new("empty", "Empty", ["nothing/**"])
            ]
        };
        var reviews = TestServices.Reviews(store, clock, repositories, git);
        var signing = TestServices.Signing(store, users, clock);
        return new Fixture(
            store,
            clock,
            reviews,
            signing,
            TestServices.Checklists(store, clock),
            repositories,
            git,
            parser,
            TestServices.Planner(store, reviews, repositories, git, signing, parser),
            project.Id,
            repository.Id,
            ann.Id);
    }

    /// <summary>
    ///     Plans the head <c>main</c>.
    /// </summary>
    /// <param name="f">The fixture.</param>
    /// <param name="mode">The baseline mode.</param>
    /// <param name="baseline">The explicit baseline revision, for explicit mode.</param>
    /// <returns>The plan.</returns>
    private static Task<ReviewPlan> PlanAsync(Fixture f, BaselineMode mode = BaselineMode.None, string? baseline = null)
    {
        return f.Planner.PlanAsync(
            _actor, f.ProjectId, f.RepositoryId, "main", mode, baseline, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Makes a create request from a row, as a page does.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The request.</returns>
    private static PlanCreateRequest RequestFor(PlanRow row) => new(row.SetId, row.Title, row.Selection, row.BaselineSha);

    /// <summary>
    ///     Files the reviews of the given sets at a head, the way a user would: plan with no baseline and create
    ///     the rows of the sets. <c>main</c> is moved back to <c>H2</c> afterwards.
    /// </summary>
    /// <param name="f">The fixture.</param>
    /// <param name="head">The head commit to plan at.</param>
    /// <param name="setIds">The sets to create reviews for.</param>
    /// <returns>The results of the creation.</returns>
    private static async Task<IReadOnlyList<PlanCreateResult>> FileAsync(Fixture f, string head, params string[] setIds)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        MoveMain(f, head);
        var plan = await PlanAsync(f);
        var requests = plan.Rows.Where(row => setIds.Contains(row.SetId)).Select(RequestFor).ToList();
        var results = await f.Planner.CreateAsync(f.ProjectId, f.RepositoryId, plan.HeadSha, requests, _actor, cancellationToken);
        MoveMain(f, H2);
        return results;
    }

    /// <summary>
    ///     Points the revision <c>main</c> at a commit.
    /// </summary>
    /// <param name="f">The fixture.</param>
    /// <param name="sha">The commit.</param>
    private static void MoveMain(Fixture f, string sha) => f.Git.Revisions["main"] = sha;

    /// <summary>
    ///     Signs a review: answers the whole checklist, adds a slot for ann and lets ann sign it.
    /// </summary>
    /// <param name="f">The fixture.</param>
    /// <param name="reviewId">The review to sign.</param>
    /// <returns>A task that completes when the review is signed.</returns>
    private static async Task SignAsync(Fixture f, string reviewId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await f.Checklists.SetAnswerAsync(
                f.ProjectId, reviewId, item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, cancellationToken);
        }

        var slot = await f.Signing.AddSlotAsync(TestActors.Ann, f.ProjectId, reviewId, f.AnnId, null, cancellationToken);
        var digest = (await f.Signing.GetApprovalAsync(f.ProjectId, reviewId, cancellationToken))!.Digest;
        await f.Signing.SignAsync(TestActors.Ann, f.ProjectId, reviewId, slot.Id, digest, Password, cancellationToken);
    }

    /// <summary>
    ///     Test that a plan without a baseline classifies each set, counts files by kind and uses one session.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_NoBaseline_ClassifiesSetsWithOneSession()
    {
        // Arrange: the fixture, where core has two added files, docs one and empty none
        var f = await CreateAsync();

        // Act: plan the head with no baseline
        var plan = await PlanAsync(f);

        // Assert: the head, one row per set in the order of the file, the counts and a single listing
        Assert.Equal(H2, plan.HeadSha);
        Assert.Equal("main", plan.HeadRevision);
        Assert.Equal("main-repo", plan.RepositoryName);
        Assert.Equal(["core", "docs", "empty"], plan.Rows.Select(row => row.SetId));
        var core = plan.Rows[0];
        Assert.Equal(PlanRowStatus.WillCreate, core.Status);
        Assert.Equal((2, 0, 0), (core.Added, core.Modified, core.Deleted));
        Assert.Equal(2, core.ChangedFiles);
        Assert.Equal("src/**", core.Selection);
        Assert.Null(core.BaselineSha);
        Assert.Equal("Full review: all selected files", core.BaselineNote);
        Assert.Equal((1, 0, 0), (plan.Rows[1].Added, plan.Rows[1].Modified, plan.Rows[1].Deleted));
        Assert.Equal(PlanRowStatus.NothingToReview, plan.Rows[2].Status);
        Assert.Equal("No file matches the paths", plan.Rows[2].BaselineNote);
        Assert.Equal((2, 0, 1, 0), (plan.WillCreateCount, plan.AlreadyFiledCount, plan.NothingCount, plan.CannotPlanCount));
        Assert.Single(f.Git.Opened);
        Assert.Equal(1, f.Git.ListCalls);
        Assert.Equal(ConfigText, Encoding.UTF8.GetString(f.Parser.LastContent!));
    }

    /// <summary>
    ///     Test that an explicit baseline is resolved once, used for every set and listed once.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_ExplicitBaseline_UsesItForEverySet()
    {
        // Arrange: the fixture, where only core changed between v1 and main
        var f = await CreateAsync();

        // Act: plan since v1
        var plan = await PlanAsync(f, BaselineMode.Explicit, "v1");

        // Assert: the baseline is on the plan and the rows, and core shows a modified and an added file
        Assert.Equal(BaselineMode.Explicit, plan.Mode);
        Assert.Equal("v1", plan.ExplicitBaselineRevision);
        Assert.Equal(H1, plan.ExplicitBaselineSha);
        var core = plan.Rows[0];
        Assert.Equal(PlanRowStatus.WillCreate, core.Status);
        Assert.Equal(H1, core.BaselineSha);
        Assert.Equal((1, 1, 0), (core.Added, core.Modified, core.Deleted));
        Assert.Equal("Changes since v1 (bbbbbbb)", core.BaselineNote);
        Assert.Equal(PlanRowStatus.NothingToReview, plan.Rows[1].Status);
        Assert.Equal(2, f.Git.ListCalls);
        Assert.Single(f.Git.Opened);
    }

    /// <summary>
    ///     Test that an explicit baseline that does not exist fails the plan.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_UnknownExplicitBaseline_ThrowsRevisionNotFound()
    {
        // Arrange: the fixture
        var f = await CreateAsync();

        // Act: plan since a revision that does not exist
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => PlanAsync(f, BaselineMode.Explicit, "nope"));

        // Assert: the rule
        Assert.Equal(DomainRule.RevisionNotFound, exception.Rule);
    }

    /// <summary>
    ///     Test that a review filed from a plan freezes the checklist of the project, which is the same path as a
    ///     review created from a repository, and that a project without a checklist of its own gets the default.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_CreateAsync_ProjectChecklist_IsFrozenIntoTheReview()
    {
        // Arrange: a project with its own checklist
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var checklists = TestServices.ProjectChecklists(f.Store);
        var custom = new ChecklistDefinition(
            "plan-1", [new ChecklistSection("p", "Plan", string.Empty, [new ChecklistItem("p-01", "Planned check")])]);
        Assert.True(await checklists.SetAsync(
            _actor, f.ProjectId, ProjectChecklistService.HashOf(null), custom.ToJson(), cancellationToken));

        // Act: file the review of a set from a plan
        await FileAsync(f, H1, "core");

        // Assert: the review has the project's checklist
        var review = f.Store.Reviews.Single();
        Assert.Equal("plan-1", review.Checklist!.Version);
        Assert.Equal(custom.ToJson(), review.Checklist.Definition);
    }

    /// <summary>
    ///     Test that last-reviewed mode makes a full review for a set that has only an unsigned review.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_LastReviewedWithOnlyDraft_FullReview()
    {
        // Arrange: a review of core at H1 that nobody signed
        var f = await CreateAsync();
        await FileAsync(f, H1, "core");
        f.Git.Ancestors.Add((H1, H2));

        // Act: plan with the last reviewed head as baseline
        var plan = await PlanAsync(f, BaselineMode.LastReviewed);

        // Assert: a draft is not a baseline
        Assert.Null(plan.Rows[0].BaselineSha);
        Assert.Equal("Full review: no signed review of this set yet", plan.Rows[0].BaselineNote);
        Assert.Equal(PlanRowStatus.WillCreate, plan.Rows[0].Status);
    }

    /// <summary>
    ///     Test that the head of the last signed review becomes the baseline when it is an ancestor.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_LastReviewedSignedAncestor_UsesItsHeadAsBaseline()
    {
        // Arrange: a signed review of core at H1, which is an ancestor of the head
        var f = await CreateAsync();
        await FileAsync(f, H1, "core");
        await SignAsync(f, "core");
        f.Git.Ancestors.Add((H1, H2));

        // Act: plan with the last reviewed head as baseline
        var plan = await PlanAsync(f, BaselineMode.LastReviewed);

        // Assert: core is a delta since H1; docs has no signed review and gets a full review
        var core = plan.Rows[0];
        Assert.Equal(PlanRowStatus.WillCreate, core.Status);
        Assert.Equal(H1, core.BaselineSha);
        Assert.Equal((1, 1, 0), (core.Added, core.Modified, core.Deleted));
        Assert.Equal("Changes since review 'core' (bbbbbbb)", core.BaselineNote);
        Assert.Null(plan.Rows[1].BaselineSha);
        Assert.Equal("Full review: no signed review of this set yet", plan.Rows[1].BaselineNote);
    }

    /// <summary>
    ///     Test that the review signed last is the baseline, not the one created last.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_LastReviewedTwoSigned_PicksTheOneSignedLast()
    {
        // Arrange: two reviews of core, the older one signed later; both heads are ancestors of the head
        var f = await CreateAsync();
        await FileAsync(f, H0, "core");
        f.Clock.Advance(TimeSpan.FromHours(1));
        await FileAsync(f, H1, "core");
        await SignAsync(f, "core-2");
        f.Clock.Advance(TimeSpan.FromHours(1));
        await SignAsync(f, "core");
        f.Git.Ancestors.Add((H0, H2));
        f.Git.Ancestors.Add((H1, H2));

        // Act: plan with the last reviewed head as baseline
        var plan = await PlanAsync(f, BaselineMode.LastReviewed);

        // Assert: the baseline is the head of the review that was signed last
        Assert.Equal(H0, plan.Rows[0].BaselineSha);
        Assert.Equal("Changes since review 'core' (aaaaaaa)", plan.Rows[0].BaselineNote);
    }

    /// <summary>
    ///     Test that a last signed review that is not an ancestor of the head gives a full review with a reason.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_LastReviewedNotAncestor_FullReviewWithNote()
    {
        // Arrange: a signed review of core at H1, which is not an ancestor of the head
        var f = await CreateAsync();
        await FileAsync(f, H1, "core");
        await SignAsync(f, "core");

        // Act: plan with the last reviewed head as baseline
        var plan = await PlanAsync(f, BaselineMode.LastReviewed);

        // Assert: a full review, and the note says why
        Assert.Null(plan.Rows[0].BaselineSha);
        Assert.Equal(PlanRowStatus.WillCreate, plan.Rows[0].Status);
        Assert.Contains("is not an ancestor of this revision", plan.Rows[0].BaselineNote);
    }

    /// <summary>
    ///     Test that a failing ancestor check does not fail the plan but gives a full review.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_AncestorCheckFails_FullReviewWithNote()
    {
        // Arrange: a signed review of core whose ancestor check fails
        var f = await CreateAsync();
        await FileAsync(f, H1, "core");
        await SignAsync(f, "core");
        f.Git.AncestorFailures.Add((H1, H2));

        // Act: plan with the last reviewed head as baseline
        var plan = await PlanAsync(f, BaselineMode.LastReviewed);

        // Assert: a full review, and the note says the check was not possible
        Assert.Null(plan.Rows[0].BaselineSha);
        Assert.Contains("could not be checked", plan.Rows[0].BaselineNote);
    }

    /// <summary>
    ///     Test that a changed selection forces a full review even when the last signed head is an ancestor.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_SelectionChanged_FullReviewWithNote()
    {
        // Arrange: a signed review of core, then core selects more files
        var f = await CreateAsync();
        await FileAsync(f, H1, "core");
        await SignAsync(f, "core");
        f.Git.Ancestors.Add((H1, H2));
        f.Parser.Sets[0] = new("core", "Core", ["src/**", "docs/**"]);

        // Act: plan with the last reviewed head as baseline
        var plan = await PlanAsync(f, BaselineMode.LastReviewed);

        // Assert: a full review of the new selection
        var core = plan.Rows[0];
        Assert.Null(core.BaselineSha);
        Assert.Equal("src/**\ndocs/**", core.Selection);
        Assert.Contains("file selection changed", core.BaselineNote);
        Assert.Equal((3, 0, 0), (core.Added, core.Modified, core.Deleted));
    }

    /// <summary>
    ///     Test that the change lists are loaded one baseline at a time, once per distinct baseline, and that the rows
    ///     of sets that share a baseline are still classified and kept in the order of the configuration.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_SeveralBaselines_HoldsOneChangeListAtATimeAndKeepsRowOrder()
    {
        // Arrange: core and core2 signed at H0, lib signed at H1, docs never signed; the sets alternate baselines
        var f = await CreateAsync();
        f.Parser.Sets =
        [
            new("core", "Core", ["src/**"]),
            new("lib", "Lib", ["src/**"]),
            new("docs", "Documentation", ["docs/**"]),
            new("core2", "Core Two", ["src/**"])
        ];
        f.Git.Script(H1, H2, [("src/b.cs", ChangeKind.Added, null, "new\n")]);
        await FileAsync(f, H0, "core", "core2");
        await FileAsync(f, H1, "lib");
        await SignAsync(f, "core");
        await SignAsync(f, "core2");
        await SignAsync(f, "lib");
        f.Git.Ancestors.Add((H0, H2));
        f.Git.Ancestors.Add((H1, H2));
        var listsBefore = f.Git.ListCalls;
        f.Git.TrackChangeSets = true;

        // Act: plan with the last reviewed head as baseline
        var plan = await PlanAsync(f, BaselineMode.LastReviewed);

        // Assert: one listing for the head and one per distinct baseline
        Assert.Equal(3, f.Git.ListCalls - listsBefore);

        // Assert: when each list was requested, none of the earlier ones was still held
        Assert.Equal([0, 0, 0], f.Git.AliveAtListCall);

        // Assert: the rows are in the order of the configuration and each used the changes of its own baseline
        Assert.Equal(["core", "lib", "docs", "core2"], plan.Rows.Select(row => row.SetId));
        Assert.Equal([H0, H1, null, H0], plan.Rows.Select(row => row.BaselineSha));
        Assert.Equal(
            [(1, 1, 0), (1, 0, 0), (1, 0, 0), (1, 1, 0)],
            plan.Rows.Select(row => (row.Added, row.Modified, row.Deleted)));
        Assert.All(plan.Rows, row => Assert.Equal(PlanRowStatus.WillCreate, row.Status));
    }

    /// <summary>
    ///     Test that a set whose last signed review has the same head has nothing to review.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_LastReviewedSameHead_NothingToReview()
    {
        // Arrange: a signed review of core at the head itself
        var f = await CreateAsync();
        await FileAsync(f, H2, "core");
        await SignAsync(f, "core");

        // Act: plan with the last reviewed head as baseline
        var plan = await PlanAsync(f, BaselineMode.LastReviewed);

        // Assert: nothing to review, with the reason
        Assert.Equal(PlanRowStatus.NothingToReview, plan.Rows[0].Status);
        Assert.Equal("Unchanged since review 'core'", plan.Rows[0].BaselineNote);
    }

    /// <summary>
    ///     Test that a set with a review of exactly the same range is reported as already filed.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_ReviewExists_AlreadyFiled()
    {
        // Arrange: core already has a review of the full head
        var f = await CreateAsync();
        await FileAsync(f, H2, "core");

        // Act: plan the same head again
        var plan = await PlanAsync(f);

        // Assert: core is filed under its review id, docs is still to create
        Assert.Equal(PlanRowStatus.AlreadyFiled, plan.Rows[0].Status);
        Assert.Equal("core", plan.Rows[0].ExistingReviewId);
        Assert.Equal(PlanRowStatus.WillCreate, plan.Rows[1].Status);
        Assert.Equal(1, plan.AlreadyFiledCount);
    }

    /// <summary>
    ///     Test that a review of the same set and commits whose paths were different does not hide the set: the
    ///     selection is part of what makes a review the same.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_SameCommitsOtherPaths_WillCreate()
    {
        // Arrange: core was reviewed at the head with src/**, then its paths changed in the configuration
        var f = await CreateAsync();
        await FileAsync(f, H2, "core");
        f.Parser.Sets[0] = new("core", "Core", ["src/a.cs"]);

        // Act: plan the same head again
        var plan = await PlanAsync(f);

        // Assert: core needs a new review for its new selection, with no link to the old review
        Assert.Equal(PlanRowStatus.WillCreate, plan.Rows[0].Status);
        Assert.Null(plan.Rows[0].ExistingReviewId);
        Assert.Equal(0, plan.AlreadyFiledCount);

        // Act: create it, and plan again
        var results = await f.Planner.CreateAsync(
            f.ProjectId, f.RepositoryId, plan.HeadSha, [RequestFor(plan.Rows[0])], _actor, TestContext.Current.CancellationToken);
        var again = await PlanAsync(f);

        // Assert: the new review has its own id, and the plan now finds it
        Assert.Equal(PlanCreateOutcome.Created, results[0].Outcome);
        Assert.Equal("core-2", results[0].ReviewId);
        Assert.Equal(PlanRowStatus.AlreadyFiled, again.Rows[0].Status);
        Assert.Equal("core-2", again.Rows[0].ExistingReviewId);
    }

    /// <summary>
    ///     Test that a set that cannot be planned is a row of its own and does not fail the plan.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_UnusableSets_CannotPlanRows()
    {
        // Arrange: a good set, one with an invalid path, one with an id that is too long and one with too many files
        var f = await CreateAsync();
        var files = Enumerable.Range(0, ReviewLimits.MaxFileCount + 1)
            .Select(index => (Path: $"big/f{index}.txt", Kind: ChangeKind.Added, Baseline: default(string?), Head: (string?)"x"))
            .Append((Path: ".reviewmark.yaml", Kind: ChangeKind.Added, Baseline: null, Head: ConfigText))
            .ToList();
        f.Git.Script(null, H2, files);
        f.Parser.Sets =
        [
            new("good", "Good", [".reviewmark.yaml"]),
            new("bad-path", "Bad", ["../x"]),
            new(new string('i', ReviewLimits.MaxReviewSetIdLength + 1), "Long", ["big/**"]),
            new("big", "Big", ["big/**"])
        ];

        // Act: plan the head
        var plan = await PlanAsync(f);

        // Assert: the good set is planned; the other three each say why not
        Assert.Equal(PlanRowStatus.WillCreate, plan.Rows[0].Status);
        Assert.All(plan.Rows.Skip(1), row => Assert.Equal(PlanRowStatus.CannotPlan, row.Status));
        Assert.Contains("segment", plan.Rows[1].Problem);
        Assert.Contains("longer than", plan.Rows[2].Problem);
        Assert.Contains("Too many files", plan.Rows[3].Problem);
        Assert.Equal(3, plan.CannotPlanCount);
    }

    /// <summary>
    ///     Test that a head without a configuration fails the plan with the dedicated rule.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_NoConfiguration_ThrowsConfigMissing()
    {
        // Arrange: a head with only a source file
        var f = await CreateAsync();
        f.Git.Script(null, H2, [("src/a.cs", ChangeKind.Added, null, "x\n")]);

        // Act: plan it
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => PlanAsync(f));

        // Assert: the rule
        Assert.Equal(DomainRule.ReviewSetConfigMissing, exception.Rule);
    }

    /// <summary>
    ///     Test that a configuration larger than the limit is refused before it is read.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_ConfigurationTooLarge_ThrowsConfigInvalidWithoutReading()
    {
        // Arrange: a configuration one byte over the limit
        var f = await CreateAsync();
        f.Git.Script(
            null,
            H2,
            [(".reviewmark.yaml", ChangeKind.Added, null, new string('x', (int)ReviewLimits.MaxReviewSetFileBytes + 1))]);

        // Act: plan it
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => PlanAsync(f));

        // Assert: the rule, and the content was not read
        Assert.Equal(DomainRule.ReviewSetConfigInvalid, exception.Rule);
        Assert.Equal(0, f.Git.BlobReads);
    }

    /// <summary>
    ///     Test that a baseline mode that is not defined is refused before anything is read, instead of being
    ///     treated as the last-signed mode.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_UndefinedBaselineMode_ThrowsArgumentOutOfRangeWithoutReading()
    {
        // Arrange: the fixture
        var f = await CreateAsync();

        // Act: plan with a mode value that no enum member has
        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => PlanAsync(f, (BaselineMode)99));

        // Assert: the parameter is named and no listing was requested
        Assert.Equal("mode", exception.ParamName);
        Assert.Equal(0, f.Git.BlobReads);
    }

    /// <summary>
    ///     Test that a parser failure is passed on unchanged.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_ParserRejectsFile_ThrowsConfigInvalid()
    {
        // Arrange: a parser that rejects the file
        var f = await CreateAsync();
        f.Parser.Failure = new DomainRuleException(DomainRule.ReviewSetConfigInvalid, "The file is not valid YAML.");

        // Act: plan it
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => PlanAsync(f));

        // Assert: the rule and the plain message
        Assert.Equal(DomainRule.ReviewSetConfigInvalid, exception.Rule);
        Assert.Equal("The file is not valid YAML.", exception.Message);
    }

    /// <summary>
    ///     Test that requests that cannot be served fail with the right rule.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_PlanAsync_BadRequests_ThrowTheirRules()
    {
        // Arrange: the fixture and a disabled repository
        var f = await CreateAsync();
        var disabled = await f.Repositories.CreateAsync(
            f.ProjectId, "off", "https://example.com/org/off.git", null, null, _actor, TestContext.Current.CancellationToken);
        await f.Repositories.SetEnabledAsync(disabled.Id, false, _actor, TestContext.Current.CancellationToken);
        var token = TestContext.Current.CancellationToken;

        // Act: plan with an unknown head, an invalid head, an unknown project, an unknown repository and a disabled one
        var unknownHead = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Planner.PlanAsync(_actor, f.ProjectId, f.RepositoryId, "nope", BaselineMode.None, null, token));
        var invalidHead = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Planner.PlanAsync(_actor, f.ProjectId, f.RepositoryId, "--upload-pack=x", BaselineMode.None, null, token));
        var noProject = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Planner.PlanAsync(_actor, 999, f.RepositoryId, "main", BaselineMode.None, null, token));
        var noRepository = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Planner.PlanAsync(_actor, f.ProjectId, 999, "main", BaselineMode.None, null, token));
        var off = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Planner.PlanAsync(_actor, f.ProjectId, disabled.Id, "main", BaselineMode.None, null, token));

        // Assert: each has its own rule
        Assert.Equal(DomainRule.RevisionNotFound, unknownHead.Rule);
        Assert.Equal(DomainRule.RevisionInvalid, invalidHead.Rule);
        Assert.Equal(DomainRule.ProjectNotFound, noProject.Rule);
        Assert.Equal(DomainRule.RepositoryNotFound, noRepository.Rule);
        Assert.Equal(DomainRule.RepositoryDisabled, off.Rule);
    }

    /// <summary>
    ///     Test that creating from a plan makes frozen reviews that carry the set, with the generated id and title.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_CreateAsync_PlanRows_CreatesReviewsWithSetIdAndOneSession()
    {
        // Arrange: a plan with two rows to create
        var f = await CreateAsync();
        var plan = await PlanAsync(f);
        var requests = plan.Rows.Where(row => row.Status == PlanRowStatus.WillCreate).Select(RequestFor).ToList();

        // Act: create them
        var results = await f.Planner.CreateAsync(
            f.ProjectId, f.RepositoryId, plan.HeadSha, requests, _actor, TestContext.Current.CancellationToken);

        // Assert: both created in order, with ids from the set ids
        Assert.Equal(["core", "docs"], results.Select(result => result.ReviewId));
        Assert.All(results, result => Assert.Equal(PlanCreateOutcome.Created, result.Outcome));
        var core = f.Store.Reviews.Single(review => review.Id == "core");
        Assert.Equal("core", core.ReviewSetId);
        Assert.Equal("Core (all files at ccccccc)", core.Title);
        Assert.Equal(H2, core.HeadSha);
        Assert.Null(core.BaselineSha);
        Assert.Equal("src/**", core.Selection);
        Assert.Equal(["src/a.cs", "src/b.cs"], core.Files.Select(file => file.Path));

        // Assert: one session for the plan and one for the whole batch, and the audit entry names the set last
        Assert.Equal(2, f.Git.Opened.Count);
        var audit = f.Store.Audits.Single(entry => entry.ReviewId == "core");
        Assert.Equal("review.created", audit.Action);
        Assert.EndsWith(", set 'core'", audit.Detail);
    }

    /// <summary>
    ///     Test that a delta review gets the range in its title and the baseline commit.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_CreateAsync_DeltaRow_UsesBaselineAndRangeTitle()
    {
        // Arrange: a plan since v1
        var f = await CreateAsync();
        var plan = await PlanAsync(f, BaselineMode.Explicit, "v1");

        // Act: create the core row
        var results = await f.Planner.CreateAsync(
            f.ProjectId, f.RepositoryId, plan.HeadSha, [RequestFor(plan.Rows[0])], _actor, TestContext.Current.CancellationToken);

        // Assert: the review is from H1 to H2, with the range in the title
        var review = f.Store.Reviews.Single();
        Assert.Equal(PlanCreateOutcome.Created, results[0].Outcome);
        Assert.Equal(H1, review.BaselineSha);
        Assert.Equal("Core (bbbbbbb..ccccccc)", review.Title);
        Assert.Equal(["src/a.cs", "src/b.cs"], review.Files.Select(file => file.Path));
    }

    /// <summary>
    ///     Test that one request that cannot be created does not stop the others.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_CreateAsync_OneRequestFails_OthersAreCreated()
    {
        // Arrange: a request whose selection matches nothing, then a good one
        var f = await CreateAsync();
        var plan = await PlanAsync(f);
        PlanCreateRequest[] requests =
        [
            new("empty", "Empty", "nothing/**", null),
            RequestFor(plan.Rows[1])
        ];

        // Act: create both
        var results = await f.Planner.CreateAsync(
            f.ProjectId, f.RepositoryId, plan.HeadSha, requests, _actor, TestContext.Current.CancellationToken);

        // Assert: the first failed with a message and stored nothing; the second was created
        Assert.Equal(PlanCreateOutcome.Failed, results[0].Outcome);
        Assert.Null(results[0].ReviewId);
        Assert.Contains("Nothing to review", results[0].Message);
        Assert.Equal(PlanCreateOutcome.Created, results[1].Outcome);
        Assert.Equal("docs", Assert.Single(f.Store.Reviews).Id);
    }

    /// <summary>
    ///     Test that an unexpected fault (not a domain rule violation) fails only its own row, with a generic
    ///     message that carries none of the exception text, and does not lose the other results.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_CreateAsync_UnexpectedFault_FailsOnlyThatRowWithGenericMessage()
    {
        // Arrange: one row created first, then a fault is armed for the next listing
        var f = await CreateAsync();
        var plan = await PlanAsync(f);
        PlanCreateRequest[] requests = [RequestFor(plan.Rows[0]), RequestFor(plan.Rows[1])];
        var created = await f.Planner.CreateAsync(
            f.ProjectId, f.RepositoryId, plan.HeadSha, [requests[0]], _actor, TestContext.Current.CancellationToken);
        f.Git.OnNextListChanges = () => throw new InvalidOperationException("SECRET-DATABASE-DETAIL at C:\\private\\db.sqlite");

        // Act: create the second row (which hits the fault)
        var results = await f.Planner.CreateAsync(
            f.ProjectId, f.RepositoryId, plan.HeadSha, [requests[1]], _actor, TestContext.Current.CancellationToken);
        var after = await f.Planner.CreateAsync(
            f.ProjectId, f.RepositoryId, plan.HeadSha, [requests[1]], _actor, TestContext.Current.CancellationToken);

        // Assert: the faulty row failed with a safe message and stored nothing; the earlier and later results are intact
        Assert.Equal(PlanCreateOutcome.Created, created[0].Outcome);
        Assert.Equal(PlanCreateOutcome.Failed, results[0].Outcome);
        Assert.Null(results[0].ReviewId);
        Assert.DoesNotContain("SECRET", results[0].Message);
        Assert.DoesNotContain("sqlite", results[0].Message);
        Assert.Contains("unexpected error", results[0].Message);
        Assert.Equal(PlanCreateOutcome.Created, after[0].Outcome);
        Assert.Equal(["core", "docs"], f.Store.Reviews.Select(review => review.Id).Order());
    }

    /// <summary>
    ///     Test that a git access failure of one row is reported with a fixed sentence that carries none of the text
    ///     git printed, and that the other rows are still created.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_CreateAsync_GitAccessFailed_FailsOnlyThatRowWithFixedSentence()
    {
        // Arrange: one row created first, then a git access failure with raw git text is armed for the next listing
        var f = await CreateAsync();
        var plan = await PlanAsync(f);
        PlanCreateRequest[] requests = [RequestFor(plan.Rows[0]), RequestFor(plan.Rows[1])];
        var created = await f.Planner.CreateAsync(
            f.ProjectId, f.RepositoryId, plan.HeadSha, [requests[0]], _actor, TestContext.Current.CancellationToken);
        f.Git.OnNextListChanges = () => throw new DomainRuleException(
            DomainRule.GitAccessFailed,
            "Repository 'x' could not be read: fatal: unable to access 'https://user:SECRET-TOKEN@host.example/r.git/': denied");

        // Act: create the second row (which hits the failure), then once more
        var results = await f.Planner.CreateAsync(
            f.ProjectId, f.RepositoryId, plan.HeadSha, [requests[1]], _actor, TestContext.Current.CancellationToken);
        var after = await f.Planner.CreateAsync(
            f.ProjectId, f.RepositoryId, plan.HeadSha, [requests[1]], _actor, TestContext.Current.CancellationToken);

        // Assert: the row failed with the fixed sentence only; the other results are intact
        Assert.Equal(PlanCreateOutcome.Created, created[0].Outcome);
        Assert.Equal(PlanCreateOutcome.Failed, results[0].Outcome);
        Assert.Null(results[0].ReviewId);
        Assert.Equal(ReviewPlanner.GitAccessFailedMessage, results[0].Message);
        Assert.DoesNotContain("fatal", results[0].Message);
        Assert.DoesNotContain("host.example", results[0].Message);
        Assert.DoesNotContain("SECRET", results[0].Message);
        Assert.Equal(PlanCreateOutcome.Created, after[0].Outcome);
        Assert.Equal(["core", "docs"], f.Store.Reviews.Select(review => review.Id).Order());
    }

    /// <summary>
    ///     Test that a cancellation is not turned into a failed row.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_CreateAsync_Cancellation_Propagates()
    {
        // Arrange: the next listing is cancelled
        var f = await CreateAsync();
        var plan = await PlanAsync(f);
        f.Git.OnNextListChanges = () => throw new OperationCanceledException();

        // Act / Assert: the batch is cancelled, not reported as a failed row
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Planner.CreateAsync(
            f.ProjectId, f.RepositoryId, plan.HeadSha, [RequestFor(plan.Rows[0])], _actor, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a request whose review exists already is reported as already filed and creates nothing.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_CreateAsync_SameRangeTwice_SecondIsAlreadyFiled()
    {
        // Arrange: the core review of the head exists
        var f = await CreateAsync();
        var plan = await PlanAsync(f);
        var request = RequestFor(plan.Rows[0]);
        await f.Planner.CreateAsync(
            f.ProjectId, f.RepositoryId, plan.HeadSha, [request], _actor, TestContext.Current.CancellationToken);

        // Act: create it again, as a second user with a stale plan would
        var results = await f.Planner.CreateAsync(
            f.ProjectId, f.RepositoryId, plan.HeadSha, [request], _actor, TestContext.Current.CancellationToken);

        // Assert: already filed under the first review, and still one review
        Assert.Equal(PlanCreateOutcome.AlreadyFiled, results[0].Outcome);
        Assert.Equal("core", results[0].ReviewId);
        Assert.Single(f.Store.Reviews);
    }

    /// <summary>
    ///     Test that a generated id that is taken gets a numeric suffix.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_CreateAsync_IdTaken_UsesSuffix()
    {
        // Arrange: a review called core already exists
        var f = await CreateAsync();
        await f.Reviews.CreateDraftAsync(
            f.ProjectId, "core", "Manual", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        var plan = await PlanAsync(f);

        // Act: create the core row
        var results = await f.Planner.CreateAsync(
            f.ProjectId, f.RepositoryId, plan.HeadSha, [RequestFor(plan.Rows[0])], _actor, TestContext.Current.CancellationToken);

        // Assert: the new review is core-2
        Assert.Equal("core-2", results[0].ReviewId);
        Assert.Equal(["core", "core-2"], f.Store.Reviews.Select(review => review.Id).Order());
    }

    /// <summary>
    ///     Test that an id taken at the same moment by someone else makes the planner try the next suffix.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_CreateAsync_IdTakenDuringCreate_RetriesWithNextSuffix()
    {
        // Arrange: another review called core appears while the files are listed
        var f = await CreateAsync();
        var plan = await PlanAsync(f);
        f.Git.OnNextListChanges = async () => await f.Reviews.CreateDraftAsync(
            f.ProjectId, "core", "Competing", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);

        // Act: create the core row
        var results = await f.Planner.CreateAsync(
            f.ProjectId, f.RepositoryId, plan.HeadSha, [RequestFor(plan.Rows[0])], _actor, TestContext.Current.CancellationToken);

        // Assert: created as core-2, and the competing review is untouched
        Assert.Equal(PlanCreateOutcome.Created, results[0].Outcome);
        Assert.Equal("core-2", results[0].ReviewId);
        Assert.Equal("Competing", f.Store.Reviews.Single(review => review.Id == "core").Title);
    }

    /// <summary>
    ///     Test that every request is validated again, whatever a page sent.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_CreateAsync_InvalidRequests_FailPerRequest()
    {
        // Arrange: a baseline that is not a commit, a selection that is not valid and a set id that is too long
        var f = await CreateAsync();
        PlanCreateRequest[] requests =
        [
            new("core", "Core", "src/**", "v1"),
            new("core", "Core", "../x", null),
            new(new string('i', ReviewLimits.MaxReviewSetIdLength + 1), "Core", "src/**", null)
        ];

        // Act: create them
        var results = await f.Planner.CreateAsync(
            f.ProjectId, f.RepositoryId, H2, requests, _actor, TestContext.Current.CancellationToken);

        // Assert: all three failed with a message, and nothing was stored
        Assert.All(results, result => Assert.Equal(PlanCreateOutcome.Failed, result.Outcome));
        Assert.All(results, result => Assert.False(string.IsNullOrWhiteSpace(result.Message)));
        Assert.Empty(f.Store.Reviews);
    }

    /// <summary>
    ///     Test that a batch that cannot start fails as a whole.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_CreateAsync_BadBatch_Throws()
    {
        // Arrange: the fixture and too many requests
        var f = await CreateAsync();
        var token = TestContext.Current.CancellationToken;
        var tooMany = Enumerable.Repeat(new PlanCreateRequest("core", "Core", "src/**", null), ReviewLimits.MaxReviewSets + 1).ToList();

        // Act and Assert: a head that is not a commit, too many requests, null arguments and a missing project
        var notCommit = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Planner.CreateAsync(f.ProjectId, f.RepositoryId, "main", [], _actor, token));
        Assert.Equal(DomainRule.RevisionInvalid, notCommit.Rule);
        await Assert.ThrowsAsync<ArgumentException>(
            () => f.Planner.CreateAsync(f.ProjectId, f.RepositoryId, H2, tooMany, _actor, token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => f.Planner.CreateAsync(f.ProjectId, f.RepositoryId, H2, null!, _actor, token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => f.Planner.CreateAsync(f.ProjectId, f.RepositoryId, H2, [], null!, token));
        var noProject = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Planner.CreateAsync(999, f.RepositoryId, H2, [], _actor, token));
        Assert.Equal(DomainRule.ProjectNotFound, noProject.Rule);
        Assert.Empty(f.Git.Opened);
    }

    /// <summary>
    ///     Test that the audit text of a review with the longest title, repository name and set id still fits.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_CreateAsync_LongestInputs_AuditDetailFits()
    {
        // Arrange: a repository with the longest name and a set with the longest id and title
        var f = await CreateAsync();
        var token = TestContext.Current.CancellationToken;
        var repository = await f.Repositories.CreateAsync(
            new string('r', RepositoryService.MaxNameLength), "https://example.com/org/long.git", null, null, _actor, token);
        var setId = new string('s', ReviewLimits.MaxReviewSetIdLength);
        var request = new PlanCreateRequest(setId, new string('t', ReviewLimits.MaxReviewSetTitleLength), "src/**", H1);

        // Act: create the review as a delta
        var results = await f.Planner.CreateAsync(f.ProjectId, repository.Id, H2, [request], _actor, token);

        // Assert: created, and the audit detail is within the limit of an audit entry
        Assert.Equal(PlanCreateOutcome.Created, results[0].Outcome);
        var audit = f.Store.Audits.Last(entry => entry.Action == "review.created");
        Assert.True(audit.Detail.Length <= AuditText.MaxDetailLength, $"The detail has {audit.Detail.Length} characters.");
        Assert.Contains(", set '", audit.Detail);
        Assert.True(f.Store.Reviews.Single().Title.Length <= ReviewLimits.MaxReviewTitleLength);
    }

    /// <summary>
    ///     Test that a review id is made from a set id as documented.
    /// </summary>
    /// <param name="setId">The set id.</param>
    /// <param name="taken">The ids already in use.</param>
    /// <param name="expected">The expected id.</param>
    [Theory]
    [InlineData("core", new string[0], "core")]
    [InlineData("My Set/Id", new string[0], "My-Set-Id")]
    [InlineData("--x--", new string[0], "x")]
    [InlineData("a..b__c", new string[0], "a..b__c")]
    [InlineData("x-.", new string[0], "x")]
    [InlineData("...", new string[0], "review")]
    [InlineData("Ünï", new string[0], "n")]
    [InlineData("core", new[] { "core" }, "core-2")]
    [InlineData("core", new[] { "core", "core-2" }, "core-3")]
    [InlineData("Core", new[] { "core" }, "Core")]
    public void ReviewPlanner_GenerateId_SetId_MakesSafeUniqueId(string setId, string[] taken, string expected)
    {
        // Arrange: the ids in use
        var used = new HashSet<string>(taken, StringComparer.Ordinal);

        // Act: generate the id
        var id = ReviewPlanner.GenerateId(setId, used);

        // Assert: the expected id, which is a valid review id
        Assert.Equal(expected, id);
        ReviewService.ValidateId(id);
    }

    /// <summary>
    ///     Test that a long set id is cut so that a suffix still fits in the id limit.
    /// </summary>
    [Fact]
    public void ReviewPlanner_GenerateId_LongSetId_CutsAndStaysWithinLimit()
    {
        // Arrange: a set id of 120 characters; the full-length form is taken, then also the first suffixed forms
        var setId = new string('a', 120);
        var full = new string('a', ReviewLimits.MaxReviewIdLength);
        var taken = new HashSet<string>(StringComparer.Ordinal) { full };

        // Act: generate the id without and with the full form taken
        var first = ReviewPlanner.GenerateId(setId, new HashSet<string>(StringComparer.Ordinal));
        var second = ReviewPlanner.GenerateId(setId, taken);

        // Assert: cut to the limit, then cut again to make room for the suffix; both valid ids within the limit
        Assert.Equal(full, first);
        Assert.Equal(new string('a', ReviewLimits.MaxReviewIdLength - 2) + "-2", second);
        Assert.Equal(ReviewLimits.MaxReviewIdLength, second.Length);
        ReviewService.ValidateId(second);

        // Act: take many suffixes, so that the suffix gets longer
        for (var number = 2; number <= 1000; number++)
        {
            taken.Add(ReviewPlanner.GenerateId(setId, taken));
        }

        var crowded = ReviewPlanner.GenerateId(setId, taken);

        // Assert: still unique, valid, and within the limit with a five character suffix
        Assert.DoesNotContain(crowded, taken);
        Assert.EndsWith("-1001", crowded);
        Assert.True(crowded.Length <= ReviewLimits.MaxReviewIdLength);
        ReviewService.ValidateId(crowded);
    }

    /// <summary>
    ///     Test that the title of a planned review ends with the range and fits the limit.
    /// </summary>
    [Fact]
    public void ReviewPlanner_GenerateTitle_LongSetTitle_CutsTitleKeepsRange()
    {
        // Arrange: the longest set title
        var title = new string('t', ReviewLimits.MaxReviewSetTitleLength);

        // Act: generate titles for a full and a delta review
        var full = ReviewPlanner.GenerateTitle(title, null, H2);
        var delta = ReviewPlanner.GenerateTitle(title, H1, H2);
        var small = ReviewPlanner.GenerateTitle("Core", H1, H2);

        // Assert: within the limit, ending with the range
        Assert.True(full.Length <= ReviewLimits.MaxReviewTitleLength);
        Assert.EndsWith(" (all files at ccccccc)", full);
        Assert.True(delta.Length <= ReviewLimits.MaxReviewTitleLength);
        Assert.EndsWith(" (bbbbbbb..ccccccc)", delta);
        Assert.Equal("Core (bbbbbbb..ccccccc)", small);
        ReviewService.ValidateTitle(full);
        ReviewService.ValidateTitle(delta);
    }

    /// <summary>
    ///     Test that the constructor refuses missing collaborators.
    /// </summary>
    [Fact]
    public async Task ReviewPlanner_Constructor_NullArgument_Throws()
    {
        // Arrange: the fixture
        var f = await CreateAsync();
        var access = new AllowAllProjectAccess(f.Store);

        // Act and Assert: each missing argument is refused
        Assert.Throws<ArgumentNullException>(() => new ReviewPlanner(null!, f.Reviews, f.Repositories, f.Git, f.Signing, f.Parser, access));
        Assert.Throws<ArgumentNullException>(() => new ReviewPlanner(f.Store, null!, f.Repositories, f.Git, f.Signing, f.Parser, access));
        Assert.Throws<ArgumentNullException>(() => new ReviewPlanner(f.Store, f.Reviews, null!, f.Git, f.Signing, f.Parser, access));
        Assert.Throws<ArgumentNullException>(() => new ReviewPlanner(f.Store, f.Reviews, f.Repositories, null!, f.Signing, f.Parser, access));
        Assert.Throws<ArgumentNullException>(() => new ReviewPlanner(f.Store, f.Reviews, f.Repositories, f.Git, null!, f.Parser, access));
        Assert.Throws<ArgumentNullException>(() => new ReviewPlanner(f.Store, f.Reviews, f.Repositories, f.Git, f.Signing, null!, access));
        Assert.Throws<ArgumentNullException>(() => new ReviewPlanner(f.Store, f.Reviews, f.Repositories, f.Git, f.Signing, f.Parser, null!));
    }
}
