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
///     Subsystem tests for the Core subsystem: the project and review services working together with the
///     fingerprint calculation over a store.
/// </summary>
public class CoreSubsystemTests
{
    /// <summary>
    ///     The instant reported by the services' clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     A password that satisfies the password policy.
    /// </summary>
    private const string ValidPassword = "correct horse battery";

    /// <summary>
    ///     Test the complete workflow: a project and a review of three files are created through the services,
    ///     and are then listed and read back with the known fingerprint and the original bytes.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_ProjectAndReviewWorkflow_ValidInput_StoresFingerprintsAndReadsBack()
    {
        // Arrange: both services over one store
        var store = new FakeReviewStore();
        var projects = new ProjectService(store, new FixedTimeProvider(_now));
        var reviews = new ReviewService(store, new FixedTimeProvider(_now));

        // Act: create a project, create a review of three files in it, then read everything back
        var project = await projects.CreateAsync("Apollo", "Moon landing", TestContext.Current.CancellationToken);
        await reviews.CreateDraftAsync(
            project.Id,
            "rev-1",
            "First review",
            [new("a.txt", TestContent.A), new("b.txt", TestContent.B), new("c.bin", TestContent.C)],
            TestContext.Current.CancellationToken);
        var listedProjects = await projects.ListAsync(TestContext.Current.CancellationToken);
        var listedReviews = await reviews.ListAsync(project.Id, TestContext.Current.CancellationToken);
        var review = await reviews.GetAsync(project.Id, "rev-1", TestContext.Current.CancellationToken);
        var content = await reviews.GetFileContentAsync(project.Id, "rev-1", "c.bin", TestContext.Current.CancellationToken);

        // Assert: the project and review are listed, the fingerprint is the known answer, and the bytes are intact
        Assert.Equal("Apollo", Assert.Single(listedProjects).Name);
        Assert.Equal("rev-1", Assert.Single(listedReviews).Id);
        Assert.NotNull(review);
        Assert.Equal(ReviewState.Draft, review.State);
        Assert.Equal(TestContent.FingerprintABC, review.ContentFingerprint);
        Assert.Equal(3, review.Files.Count);
        Assert.Equal(TestContent.C, content);

        // Assert: every file carries its own size and SHA-256 hash
        var files = review.Files.ToDictionary(file => file.Path);
        Assert.Equal(TestContent.A.Length, files["a.txt"].Size);
        Assert.Equal(TestContent.HashA, files["a.txt"].Sha256);
        Assert.Equal(TestContent.B.Length, files["b.txt"].Size);
        Assert.Equal(TestContent.HashB, files["b.txt"].Sha256);
        Assert.Equal(TestContent.C.Length, files["c.bin"].Size);
        Assert.Equal(TestContent.HashC, files["c.bin"].Sha256);
    }

    /// <summary>
    ///     Test that the same file contents give the same review fingerprint when they are submitted in another
    ///     order and under other file names.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_Fingerprint_SameContentOtherNamesAndOrder_GivesSameFingerprint()
    {
        // Arrange: both services over one store, with a project
        var store = new FakeReviewStore();
        var projects = new ProjectService(store, new FixedTimeProvider(_now));
        var reviews = new ReviewService(store, new FixedTimeProvider(_now));
        var project = await projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);

        // Act: create two reviews with the same three contents, the second in reverse order under other names
        var first = await reviews.CreateDraftAsync(
            project.Id,
            "rev-1",
            "First",
            [new("a.txt", TestContent.A), new("b.txt", TestContent.B), new("c.bin", TestContent.C)],
            TestContext.Current.CancellationToken);
        var second = await reviews.CreateDraftAsync(
            project.Id,
            "rev-2",
            "Second",
            [new("z/three", TestContent.C), new("y/two", TestContent.B), new("x/one", TestContent.A)],
            TestContext.Current.CancellationToken);

        // Assert: both reviews have the known fingerprint of the contents
        Assert.Equal(TestContent.FingerprintABC, first.ContentFingerprint);
        Assert.Equal(first.ContentFingerprint, second.ContentFingerprint);
    }

    /// <summary>
    ///     Provides every domain rule, so that a rule added later without a scenario fails the test instead of
    ///     going unnoticed.
    /// </summary>
    public static TheoryData<DomainRule> AllRules => new(Enum.GetValues<DomainRule>());

    /// <summary>
    ///     Test that each domain rule is reported through the services as a domain rule exception that names
    ///     the rule, and that nothing is stored for the violation.
    /// </summary>
    /// <param name="rule">The rule to violate.</param>
    [Theory]
    [MemberData(nameof(AllRules))]
    public async Task CoreSubsystem_RuleViolation_IsReportedAsDomainRuleAndStoresNothing(DomainRule rule)
    {
        // Arrange: both services over one store, with one valid project and review
        var store = new FakeReviewStore();
        var projects = new ProjectService(store, new FixedTimeProvider(_now));
        var reviews = new ReviewService(store, new FixedTimeProvider(_now));
        var threads = new ThreadService(store, new FixedTimeProvider(_now));
        var checklists = new ChecklistService(store, new FixedTimeProvider(_now));
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), new FixedTimeProvider(_now));
        await users.CreateAsync("root", "Root", UserRole.Admin, ValidPassword, TestContext.Current.CancellationToken);
        var project = await projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await reviews.CreateDraftAsync(
            project.Id, "rev-1", "First", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);

        // Act: break the rule
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => ViolateAsync(rule, projects, reviews, threads, checklists, users, project.Id));

        // Assert: the rule is named, and only the first valid project, review and user exist, with no thread or answer
        Assert.Equal(rule, exception.Rule);
        Assert.Single(await projects.ListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await users.ListAsync(TestContext.Current.CancellationToken));
        Assert.Single(store.Reviews);
        Assert.Empty(store.Threads);
        Assert.Equal(
            0,
            (await checklists.GetAsync(project.Id, "rev-1", TestContext.Current.CancellationToken))!.Overall.Answered);
    }

    /// <summary>
    ///     Makes a request that breaks one rule, against a store that holds the project and the review
    ///     <c>rev-1</c> with the file <c>a.txt</c>.
    /// </summary>
    /// <param name="rule">The rule to break.</param>
    /// <param name="projects">The project service.</param>
    /// <param name="reviews">The review service.</param>
    /// <param name="threads">The thread service.</param>
    /// <param name="checklists">The checklist service.</param>
    /// <param name="users">The user service, holding one administrator named <c>root</c>.</param>
    /// <param name="projectId">The identity of the existing project.</param>
    /// <returns>The task of the request, which fails with the rule violation.</returns>
    private static Task ViolateAsync(
        DomainRule rule,
        ProjectService projects,
        ReviewService reviews,
        ThreadService threads,
        ChecklistService checklists,
        UserService users,
        int projectId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        List<UploadedFile> oneFile = [new("a.txt", TestContent.A)];

        // One shared array keeps the over-limit cases cheap: only the lengths matter to the rules
        var maxSizeContent = new byte[ReviewLimits.MaxFileSizeBytes];
        return rule switch
        {
            DomainRule.ProjectNameInvalid => projects.CreateAsync("  ", null, cancellationToken),
            DomainRule.ProjectDescriptionTooLong => projects.CreateAsync(
                "Gemini", new string('d', ReviewLimits.MaxProjectDescriptionLength + 1), cancellationToken),
            DomainRule.ProjectNotFound => reviews.CreateDraftAsync(projectId + 100, "rev-2", "T", oneFile, cancellationToken),
            DomainRule.ReviewIdInvalid => reviews.CreateDraftAsync(projectId, "bad id", "T", oneFile, cancellationToken),
            DomainRule.ReviewIdDuplicate => reviews.CreateDraftAsync(projectId, "rev-1", "T", oneFile, cancellationToken),
            DomainRule.ReviewTitleInvalid => reviews.CreateDraftAsync(projectId, "rev-2", " ", oneFile, cancellationToken),
            DomainRule.NoFiles => reviews.CreateDraftAsync(projectId, "rev-2", "T", [], cancellationToken),
            DomainRule.DuplicatePath => reviews.CreateDraftAsync(
                projectId, "rev-2", "T", [new("a.txt", TestContent.A), new("a.txt", TestContent.B)], cancellationToken),
            DomainRule.PathInvalid => reviews.CreateDraftAsync(
                projectId, "rev-2", "T", [new(" ", TestContent.A)], cancellationToken),
            DomainRule.FileTooLarge => reviews.CreateDraftAsync(
                projectId, "rev-2", "T", [new("big.bin", new byte[ReviewLimits.MaxFileSizeBytes + 1])], cancellationToken),
            DomainRule.TooManyFiles => reviews.CreateDraftAsync(
                projectId,
                "rev-2",
                "T",
                Enumerable.Range(0, ReviewLimits.MaxFileCount + 1).Select(i => new UploadedFile($"f{i}.txt", [0x41])).ToList(),
                cancellationToken),
            DomainRule.TotalSizeExceeded => reviews.CreateDraftAsync(
                projectId,
                "rev-2",
                "T",
                Enumerable.Range(0, (int)(ReviewLimits.MaxTotalSizeBytes / ReviewLimits.MaxFileSizeBytes) + 1)
                    .Select(i => new UploadedFile($"f{i}.bin", maxSizeContent))
                    .ToList(),
                cancellationToken),
            DomainRule.CommentTextInvalid => threads.StartThreadAsync(
                projectId, "rev-1", "a.txt", null, null, TestActors.Ann, " ", cancellationToken),
            DomainRule.ThreadAnchorInvalid => threads.StartThreadAsync(
                projectId, "rev-1", "a.txt", 1, 99, TestActors.Ann, "Text", cancellationToken),
            DomainRule.ThreadNotFound => threads.ResolveAsync(projectId, "rev-1", 99, cancellationToken),
            DomainRule.ChecklistNotFound => checklists.SetAnswerAsync(
                projectId, "no-such-review", "REQ-01", ChecklistOutcome.Pass, null, TestActors.Ann, cancellationToken),
            DomainRule.ChecklistItemNotFound => checklists.SetAnswerAsync(
                projectId, "rev-1", "NOPE-01", ChecklistOutcome.Pass, null, TestActors.Ann, cancellationToken),
            DomainRule.ChecklistJustificationInvalid => checklists.SetAnswerAsync(
                projectId, "rev-1", "REQ-01", ChecklistOutcome.Fail, " ", TestActors.Ann, cancellationToken),
            DomainRule.UserNameInvalid => users.CreateAsync("a b", "New", UserRole.Reviewer, ValidPassword, cancellationToken),
            DomainRule.UserNameTaken => users.CreateAsync("ROOT", "New", UserRole.Reviewer, ValidPassword, cancellationToken),
            DomainRule.DisplayNameInvalid => users.CreateAsync("new.user", " ", UserRole.Reviewer, ValidPassword, cancellationToken),
            DomainRule.PasswordInvalid => users.CreateAsync("new.user", "New", UserRole.Reviewer, "short", cancellationToken),
            DomainRule.CurrentPasswordIncorrect => users.ChangeOwnPasswordAsync(
                "root", "wrong password!", ValidPassword, cancellationToken),
            DomainRule.UserNotFound => users.SetEnabledAsync("nobody", false, cancellationToken),
            DomainRule.LastAdmin => users.SetEnabledAsync("root", false, cancellationToken),
            _ => throw new NotSupportedException($"No violation is defined for the rule '{rule}'.")
        };
    }

    /// <summary>
    ///     Test the thread workflow: a thread is started on a line range, answered, resolved and reopened, and
    ///     reads back with its comments in posting order and the time of the clock.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_ThreadWorkflow_StartReplyResolveReopen_ReadsBackInOrder()
    {
        // Arrange: all three services over one store, with a project and a review of one two-line file
        var store = new FakeReviewStore();
        var projects = new ProjectService(store, new FixedTimeProvider(_now));
        var reviews = new ReviewService(store, new FixedTimeProvider(_now));
        var threads = new ThreadService(store, new FixedTimeProvider(_now));
        var project = await projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await reviews.CreateDraftAsync(
            project.Id,
            "rev-1",
            "First",
            [new("ab.txt", "a\nb\n"u8.ToArray())],
            TestContext.Current.CancellationToken);

        // Act: start a thread, reply twice, resolve it and read it back, then reopen it and read it again
        var started = await threads.StartThreadAsync(
            project.Id, "rev-1", "ab.txt", 1, 2, TestActors.Ann, "First", TestContext.Current.CancellationToken);
        await threads.ReplyAsync(project.Id, "rev-1", started.Id, TestActors.Bob, "Second", TestContext.Current.CancellationToken);
        await threads.ReplyAsync(project.Id, "rev-1", started.Id, TestActors.Ann, "Third", TestContext.Current.CancellationToken);
        await threads.ResolveAsync(project.Id, "rev-1", started.Id, TestContext.Current.CancellationToken);
        var resolved = Assert.Single(await threads.ListAsync(project.Id, "rev-1", TestContext.Current.CancellationToken));
        await threads.ReopenAsync(project.Id, "rev-1", started.Id, TestContext.Current.CancellationToken);
        var reopened = Assert.Single(await threads.ListAsync(project.Id, "rev-1", TestContext.Current.CancellationToken));

        // Assert: anchor, states and the comments in posting order with the clock time
        Assert.Equal(1, resolved.StartLine);
        Assert.Equal(2, resolved.EndLine);
        Assert.Equal(ReviewThreadState.Resolved, resolved.State);
        Assert.Equal(ReviewThreadState.Open, reopened.State);
        Assert.Equal(_now, reopened.Created);
        Assert.Equal(["Ann", "Bob", "Ann"], reopened.Comments.Select(c => c.Author));
        Assert.Equal(["First", "Second", "Third"], reopened.Comments.Select(c => c.Text));
        Assert.All(reopened.Comments, comment => Assert.Equal(_now, comment.Created));
    }

    /// <summary>
    ///     Test the checklist workflow: a review gets the default checklist when it is created, a failed check is
    ///     answered with its justification, a section is marked as not applicable, and the progress and
    ///     completeness follow.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_Checklist_FrozenAtCreationAnswerAndProgress()
    {
        // Arrange: the services over one store, with a project and a review
        var store = new FakeReviewStore();
        var projects = new ProjectService(store, new FixedTimeProvider(_now));
        var reviews = new ReviewService(store, new FixedTimeProvider(_now));
        var checklists = new ChecklistService(store, new FixedTimeProvider(_now));
        var project = await projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await reviews.CreateDraftAsync(
            project.Id, "rev-1", "First", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);

        // Act: read the new checklist, answer one check as failed, mark one section as not applicable, read again
        var initial = await checklists.GetAsync(project.Id, "rev-1", TestContext.Current.CancellationToken);
        await checklists.SetAnswerAsync(
            project.Id, "rev-1", "REQ-01", ChecklistOutcome.Fail, " Two requirements share an id ", TestActors.Ann,
            TestContext.Current.CancellationToken);
        await checklists.SetSectionApplicabilityAsync(
            project.Id, "rev-1", "DES", false, "No design files", TestActors.Ann, TestContext.Current.CancellationToken);
        var after = await checklists.GetAsync(project.Id, "rev-1", TestContext.Current.CancellationToken);

        // Assert: the review started with the whole default checklist
        Assert.NotNull(initial);
        Assert.Equal(DefaultChecklist.Version, initial.Definition.Version);
        Assert.Equal(new ChecklistProgress(0, 58), initial.Overall);

        // Assert: the failed answer counts as answered, the not-applicable section leaves the total, and the
        // checklist is not complete yet
        Assert.NotNull(after);
        Assert.Equal("Two requirements share an id", after.AnswerFor("REQ-01")!.Justification);
        Assert.Equal(new ChecklistProgress(1, 53), after.Overall);
        Assert.False(after.IsComplete);
    }

    /// <summary>
    ///     Test the users workflow: a user is created, signs in, is locked out after repeated failures, changes the
    ///     password, and the last enabled administrator is protected.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_UsersWorkflow_CreateSignInLockoutAndLastAdmin()
    {
        // Arrange: the user service over fake storage and a clock the test can move
        var clock = new MovableTimeProvider(_now);
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), clock);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: create an administrator and a reviewer, sign in, fail five times, then wait out the lockout
        await users.CreateAsync("root", "Root", UserRole.Admin, ValidPassword, cancellationToken);
        await users.CreateAsync("Ann", "Ann", UserRole.Reviewer, ValidPassword, cancellationToken);
        var signedIn = await users.AuthenticateAsync("ANN", ValidPassword, cancellationToken);
        for (var i = 0; i < 5; i++)
        {
            await users.AuthenticateAsync("ann", "wrong password!", cancellationToken);
        }

        var locked = await users.AuthenticateAsync("ann", ValidPassword, cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(5));
        var unlocked = await users.AuthenticateAsync("ann", ValidPassword, cancellationToken);
        await users.ChangeOwnPasswordAsync("ann", ValidPassword, "another long password", cancellationToken);
        var changed = await users.AuthenticateAsync("ann", "another long password", cancellationToken);
        var lastAdmin = await Assert.ThrowsAsync<DomainRuleException>(
            () => users.SetRoleAsync("root", UserRole.Reviewer, cancellationToken));

        // Assert: sign-in is case-insensitive, the lockout refuses even the right password until it ends, the new
        // password works, and the last administrator cannot be demoted
        Assert.Equal("Ann", signedIn?.Username);
        Assert.Null(locked);
        Assert.NotNull(unlocked);
        Assert.NotNull(changed);
        Assert.Equal(DomainRule.LastAdmin, lastAdmin.Rule);
    }
}
