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
    ///     Provides every domain rule that the Core services report, so that a rule added later without a scenario
    ///     fails the test instead of going unnoticed. The rules that only the git content source reports are
    ///     excluded here; the content sources subsystem and git tests cover them.
    /// </summary>
    public static TheoryData<DomainRule> AllRules => new(Enum.GetValues<DomainRule>().Where(rule => !GitSourceRules.Contains(rule)));

    /// <summary>
    ///     The rules reported only by the git content source (a revision that does not exist, git missing, a failed
    ///     or timed out git call); they are covered by the tests of the ContentSources subsystem.
    /// </summary>
    private static readonly DomainRule[] GitSourceRules =
    [
        DomainRule.RevisionNotFound,
        DomainRule.GitUnavailable,
        DomainRule.GitAccessFailed,
        DomainRule.GitTimeout
    ];

    /// <summary>
    ///     The rules that the repository registry and the selection and revision checks report.
    /// </summary>
    private static readonly DomainRule[] RepositoryRules =
    [
        DomainRule.RepositoryNameInvalid,
        DomainRule.RepositoryNameTaken,
        DomainRule.RepositoryLocationInvalid,
        DomainRule.RepositoryCredentialInvalid,
        DomainRule.RepositoryNotFound,
        DomainRule.RepositoryDisabled,
        DomainRule.CredentialsUnreadable,
        DomainRule.RevisionInvalid,
        DomainRule.SelectionInvalid,
        DomainRule.NothingToReview,
        DomainRule.ReviewSetConfigMissing,
        DomainRule.ReviewSetConfigInvalid
    ];

    /// <summary>
    ///     The rules that the personal access token service reports.
    /// </summary>
    private static readonly DomainRule[] TokenRules =
    [
        DomainRule.TokenNameInvalid,
        DomainRule.TokenExpiryInvalid,
        DomainRule.TokenLimitReached,
        DomainRule.TokenNotFound
    ];

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
        var root = await users.CreateAsync("root", "Root", UserRole.Admin, ValidPassword, TestContext.Current.CancellationToken);
        var project = await projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await reviews.CreateDraftAsync(
            project.Id, "rev-1", "First", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);

        // Act: break the rule
        var signing = new SigningService(store, users, new FixedTimeProvider(_now));
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => ViolateByGroupAsync(rule, signing, checklists, store, root, projects, reviews, threads, users, project.Id));

        // Assert: the rule is named, and only the first valid project, review and user exist, with no thread; the
        // only answers are those the re-authentication scenario needs to get as far as the password
        Assert.Equal(rule, exception.Rule);
        Assert.Single(await projects.ListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await users.ListAsync(TestContext.Current.CancellationToken));
        Assert.Single(store.Reviews);
        Assert.Empty(store.Threads);
        var overall = (await checklists.GetAsync(project.Id, "rev-1", TestContext.Current.CancellationToken))!.Overall;
        Assert.Equal(rule == DomainRule.ReauthenticationFailed ? overall.Total : 0, overall.Answered);
    }

    /// <summary>
    ///     Makes a request that breaks the rule through the service group that reports it.
    /// </summary>
    /// <param name="rule">The rule to break.</param>
    /// <param name="signing">The signing service.</param>
    /// <param name="checklists">The checklist service.</param>
    /// <param name="store">The store behind the services.</param>
    /// <param name="root">The user <c>root</c>.</param>
    /// <param name="projects">The project service.</param>
    /// <param name="reviews">The review service.</param>
    /// <param name="threads">The thread service.</param>
    /// <param name="users">The user service.</param>
    /// <param name="projectId">The identity of the existing project.</param>
    /// <returns>A task that fails with the rule violation.</returns>
    private static Task ViolateByGroupAsync(
        DomainRule rule,
        SigningService signing,
        ChecklistService checklists,
        FakeReviewStore store,
        User root,
        ProjectService projects,
        ReviewService reviews,
        ThreadService threads,
        UserService users,
        int projectId)
    {
        if (SigningRules.Contains(rule))
        {
            return ViolateSigningAsync(rule, signing, checklists, store, root, projectId);
        }

        if (RepositoryRules.Contains(rule))
        {
            return ViolateRepositoryAsync(rule, users);
        }

        if (TokenRules.Contains(rule))
        {
            return ViolateTokenAsync(rule);
        }

        return ViolateAsync(rule, projects, reviews, threads, checklists, users, projectId);
    }

    /// <summary>
    ///     Makes a request that breaks one rule of the personal access token service.
    /// </summary>
    /// <param name="rule">The rule to break; one of <see cref="TokenRules"/>.</param>
    /// <returns>A task that fails with the rule violation.</returns>
    private static async Task ViolateTokenAsync(DomainRule rule)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FixedTimeProvider(_now);
        var userStore = new FakeUserStore();
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        await users.CreateAsync("tok", "Tok", UserRole.Reviewer, ValidPassword, cancellationToken);
        var service = new PersonalAccessTokenService(new FakePersonalAccessTokenStore(userStore), userStore, clock);
        var actor = new Actor("tok", "Tok");
        switch (rule)
        {
            case DomainRule.TokenNameInvalid:
                await service.CreateAsync(actor, " ", 30, cancellationToken);
                return;
            case DomainRule.TokenExpiryInvalid:
                await service.CreateAsync(actor, "name", 7, cancellationToken);
                return;
            case DomainRule.TokenLimitReached:
                for (var i = 0; i <= ReviewLimits.MaxActiveTokensPerUser; i++)
                {
                    await service.CreateAsync(actor, $"token {i}", 30, cancellationToken);
                }

                return;
            default:
                await service.RevokeAsync(actor, 999, cancellationToken);
                return;
        }
    }

    /// <summary>
    ///     Makes a request that breaks one rule of the repository registry or of the selection and revision checks.
    /// </summary>
    /// <param name="rule">The rule to break; one of <see cref="RepositoryRules"/>.</param>
    /// <param name="users">The user service, which holds the administrator <c>root</c>.</param>
    /// <returns>A task that fails with the rule violation.</returns>
    private static async Task ViolateRepositoryAsync(DomainRule rule, UserService users)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var protector = new FakeCredentialProtector();
        var service = new RepositoryService(new FakeRepositoryStore(), protector, users, new FixedTimeProvider(_now));
        var actor = new Actor("root", "Root");
        const string location = "https://example.com/org/repo.git";
        switch (rule)
        {
            case DomainRule.RepositoryNameInvalid:
                await service.CreateAsync(" ", location, null, null, actor, cancellationToken);
                return;
            case DomainRule.RepositoryNameTaken:
                await service.CreateAsync("dup", location, null, null, actor, cancellationToken);
                await service.CreateAsync("DUP", location, null, null, actor, cancellationToken);
                return;
            case DomainRule.RepositoryLocationInvalid:
                await service.CreateAsync("a", "ftp://example.com/r.git", null, null, actor, cancellationToken);
                return;
            case DomainRule.RepositoryCredentialInvalid:
                await service.CreateAsync("a", "file:///srv/r.git", "svc", "tok", actor, cancellationToken);
                return;
            case DomainRule.RepositoryNotFound:
                await service.SetEnabledAsync(99, false, actor, cancellationToken);
                return;
            case DomainRule.RepositoryDisabled:
                var disabled = await service.CreateAsync("off", location, null, null, actor, cancellationToken);
                await service.SetEnabledAsync(disabled.Id, false, actor, cancellationToken);
                await service.GetAccessAsync(disabled.Id, cancellationToken);
                return;
            case DomainRule.CredentialsUnreadable:
                var secret = await service.CreateAsync("tok", location, "svc", "tkn", actor, cancellationToken);
                protector.Unreadable = true;
                await service.GetAccessAsync(secret.Id, cancellationToken);
                return;
            case DomainRule.RevisionInvalid:
                RevisionValidator.Validate("--upload-pack=x");
                return;
            case DomainRule.NothingToReview:
                // A repository whose only change is outside the selection
                var clock = new FixedTimeProvider(_now);
                var reviewStore = new FakeReviewStore();
                var project = await new ProjectService(reviewStore, clock).CreateAsync("P", null, cancellationToken);
                var repository = await service.CreateAsync("repo", location, null, null, actor, cancellationToken);
                var git = new FakeGitRepositoryProvider();
                git.Revisions["main"] = "h1";
                git.Script(null, "h1", [("docs/a.md", ChangeKind.Added, null, "x")]);
                await new ReviewService(reviewStore, clock, service, git).CreateFromRepositoryAsync(
                    project.Id, "rev-1", "T", repository.Id, "main", null, "src/**", actor, cancellationToken);
                return;
            case DomainRule.ReviewSetConfigMissing:
            case DomainRule.ReviewSetConfigInvalid:
                // A plan for a head that has no configuration, or one the parser rejects
                var planClock = new FixedTimeProvider(_now);
                var planStore = new FakeReviewStore();
                var planProject = await new ProjectService(planStore, planClock).CreateAsync("P", null, cancellationToken);
                var planRepository = await service.CreateAsync("repo", location, null, null, actor, cancellationToken);
                var planGit = new FakeGitRepositoryProvider();
                var headSha = new string('1', 40);
                planGit.Revisions["main"] = headSha;
                var hasConfig = rule == DomainRule.ReviewSetConfigInvalid;
                planGit.Script(
                    null,
                    headSha,
                    hasConfig ? [(".reviewmark.yaml", ChangeKind.Added, null, "reviews: [")] : []);
                var parser = new FakeReviewSetParser
                {
                    Failure = new DomainRuleException(DomainRule.ReviewSetConfigInvalid, "The file is not valid YAML.")
                };
                var planner = new ReviewPlanner(
                    planStore,
                    new ReviewService(planStore, planClock, service, planGit),
                    service,
                    planGit,
                    new SigningService(planStore, users, planClock),
                    parser);
                await planner.PlanAsync(planProject.Id, planRepository.Id, "main", BaselineMode.None, null, cancellationToken);
                return;
            default:
                SelectionMatcher.Parse(string.Empty);
                return;
        }
    }

    /// <summary>
    ///     The rules that the signing service reports; <see cref="ViolateSigningAsync"/> breaks these.
    /// </summary>
    private static readonly DomainRule[] SigningRules =
    [
        DomainRule.ReviewNotFound,
        DomainRule.SlotNotFound,
        DomainRule.SlotUserInvalid,
        DomainRule.SlotSigned,
        DomainRule.ActorNotAllowed,
        DomainRule.SigningNotAllowed,
        DomainRule.ReauthenticationFailed,
        DomainRule.RecordChanged
    ];

    /// <summary>
    ///     Makes a request that breaks one rule of the signing service, against a store that holds the review
    ///     <c>rev-1</c> and the user <c>root</c>.
    /// </summary>
    /// <param name="rule">The rule to break; one of <see cref="SigningRules"/>.</param>
    /// <param name="signing">The signing service.</param>
    /// <param name="checklists">The checklist service.</param>
    /// <param name="store">The store behind the services.</param>
    /// <param name="root">The user <c>root</c>.</param>
    /// <param name="projectId">The identity of the existing project.</param>
    /// <returns>A task that fails with the rule violation.</returns>
    private static async Task ViolateSigningAsync(
        DomainRule rule,
        SigningService signing,
        ChecklistService checklists,
        FakeReviewStore store,
        User root,
        int projectId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = new Actor("root", "Root");
        switch (rule)
        {
            case DomainRule.ReviewNotFound:
                await signing.AddSlotAsync(actor, projectId, "no-such-review", root.Id, null, cancellationToken);
                return;
            case DomainRule.SlotNotFound:
                await signing.RemoveSlotAsync(actor, projectId, "rev-1", 99, cancellationToken);
                return;
            case DomainRule.SlotUserInvalid:
                await signing.AddSlotAsync(actor, projectId, "rev-1", root.Id, root.Id, cancellationToken);
                return;
            case DomainRule.ActorNotAllowed:
                await signing.AddSlotAsync(new Actor("ghost", "Ghost"), projectId, "rev-1", root.Id, null, cancellationToken);
                return;
        }

        // The rest need a slot of root and the digest of the record as it is
        var slot = await signing.AddSlotAsync(actor, projectId, "rev-1", root.Id, null, cancellationToken);
        var digest = (await signing.GetApprovalAsync(projectId, "rev-1", cancellationToken))!.Digest;
        switch (rule)
        {
            case DomainRule.RecordChanged:
                await signing.SignAsync(actor, projectId, "rev-1", slot.Id, new string('0', 64), ValidPassword, cancellationToken);
                return;
            case DomainRule.SigningNotAllowed:
                // The checklist is unanswered
                await signing.SignAsync(actor, projectId, "rev-1", slot.Id, digest, ValidPassword, cancellationToken);
                return;
            case DomainRule.SlotSigned:
                // A signature stored directly (the store does not judge blockers), then the slot is removed
                await store.AddSignatureAsync(
                    new ReviewSignature
                    {
                        ProjectId = projectId,
                        ReviewId = "rev-1",
                        SlotId = slot.Id,
                        UserId = root.Id,
                        DisplayName = "Root",
                        Role = SlotRole.Primary,
                        SignedAt = _now,
                        RecordDigest = digest,
                        Meaning = SigningService.MeaningText
                    },
                    cancellationToken);
                await signing.RemoveSlotAsync(actor, projectId, "rev-1", slot.Id, cancellationToken);
                return;
            default:
                // Re-authentication: answer every check so that only the password stands in the way
                foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
                {
                    await checklists.SetAnswerAsync(projectId, "rev-1", item.Id, ChecklistOutcome.Pass, null, actor, cancellationToken);
                }

                digest = (await signing.GetApprovalAsync(projectId, "rev-1", cancellationToken))!.Digest;
                await signing.SignAsync(actor, projectId, "rev-1", slot.Id, digest, "wrong password here", cancellationToken);
                return;
        }
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

    /// <summary>
    ///     Test the access token workflow: a user creates a token and sees it listed without its value, the value
    ///     authenticates the owner, a token of an actor that came through a token is refused, and a revoked token
    ///     no longer authenticates.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_AccessTokens_CreateAuthenticateAndRevoke_FollowTheOwner()
    {
        // Arrange: the token service over fake storage with a user
        var clock = new MovableTimeProvider(_now);
        var userStore = new FakeUserStore();
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        var tokens = new PersonalAccessTokenService(new FakePersonalAccessTokenStore(userStore), userStore, clock);
        var cancellationToken = TestContext.Current.CancellationToken;
        await users.CreateAsync("ann", "Ann", UserRole.Reviewer, ValidPassword, cancellationToken);

        // Act: create a token, authenticate with it and with a wrong value, list, try as a token actor, and revoke
        var created = await tokens.CreateAsync(TestActors.Ann, "build server", 30, cancellationToken);
        var accepted = await tokens.AuthenticateAsync(created.Value, cancellationToken);
        var wrong = await tokens.AuthenticateAsync(created.Value + "x", cancellationToken);
        var listed = await tokens.ListAsync(TestActors.Ann, cancellationToken);
        var viaToken = await Assert.ThrowsAsync<DomainRuleException>(
            () => tokens.CreateAsync(new Actor("ann", "Ann") { ApiTokenId = created.Token.DisplayId }, "another", 30, cancellationToken));
        await tokens.RevokeAsync(TestActors.Ann, created.Token.Id, cancellationToken);
        var revoked = await tokens.AuthenticateAsync(created.Value, cancellationToken);

        // Assert: accepted for the owner, refused when wrong, listed, refused for a token actor, refused once revoked
        Assert.True(accepted.Succeeded);
        Assert.Equal("ann", accepted.User?.Username);
        Assert.False(wrong.Succeeded);
        Assert.Equal("build server", Assert.Single(listed).Name);
        Assert.Equal(DomainRule.ActorNotAllowed, viaToken.Rule);
        Assert.False(revoked.Succeeded);
    }

    /// <summary>
    ///     Test that an actor that came through a token can create a review that is flagged as created through the
    ///     API, but cannot name reviewers or sign, even with the right password and the right digest.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_TokenActor_CreatesFlaggedReviewButCannotSign()
    {
        // Arrange: the services over one store, a user with a slot on a review, and an actor that came through a token
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var projects = new ProjectService(store, new FixedTimeProvider(_now));
        var reviews = new ReviewService(store, new FixedTimeProvider(_now));
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), new FixedTimeProvider(_now));
        var signing = new SigningService(store, users, new FixedTimeProvider(_now));
        var ann = await users.CreateAsync("ann", "Ann", UserRole.Reviewer, ValidPassword, cancellationToken);
        var project = await projects.CreateAsync("Apollo", null, cancellationToken);
        var viaToken = new Actor("ann", "Ann") { ApiTokenId = "K7M2QX9P" };

        // Act: create a review as the token actor, name a reviewer as the person, then try to name and sign as the token actor
        var review = await reviews.CreateDraftAsync(project.Id, "rev-9", "Via API", [new("a.txt", TestContent.A)], viaToken, cancellationToken);
        var slot = await signing.AddSlotAsync(new Actor("ann", "Ann"), project.Id, "rev-9", ann.Id, null, cancellationToken);
        var approval = await signing.GetApprovalAsync(project.Id, "rev-9", cancellationToken);
        var naming = await Assert.ThrowsAsync<DomainRuleException>(
            () => signing.AddSlotAsync(viaToken, project.Id, "rev-9", ann.Id, null, cancellationToken));
        var signed = await Assert.ThrowsAsync<DomainRuleException>(() => signing.SignAsync(
            viaToken, project.Id, "rev-9", slot.Id, approval!.Digest, ValidPassword, cancellationToken));

        // Assert: the review is flagged, naming and signing are refused for the token actor, and nothing is signed
        Assert.True(review.CreatedViaApi);
        Assert.Equal(DomainRule.ActorNotAllowed, naming.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, signed.Rule);
        Assert.Equal(SlotSignatureState.NotSigned, (await signing.GetApprovalAsync(project.Id, "rev-9", cancellationToken))!.Slots.Single().State);
    }

    /// <summary>
    ///     Test the signing workflow: a reviewer slot is added, the review is blocked until the checklist is answered,
    ///     a wrong password is refused, a reviewer signs, a change to the record makes the signature out of date, and
    ///     the alternate signs the new record.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_Signatures_SignChangeOutOfDateAndResign_FollowsTheRecord()
    {
        // Arrange: the services over one store with two users, a review, and one slot of ann and bob
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var projects = new ProjectService(store, new FixedTimeProvider(_now));
        var reviews = new ReviewService(store, new FixedTimeProvider(_now));
        var threads = new ThreadService(store, new FixedTimeProvider(_now));
        var checklists = new ChecklistService(store, new FixedTimeProvider(_now));
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), new FixedTimeProvider(_now));
        var signing = new SigningService(store, users, new FixedTimeProvider(_now));
        var ann = await users.CreateAsync("ann", "Ann", UserRole.Reviewer, ValidPassword, cancellationToken);
        var bob = await users.CreateAsync("bob", "Bob", UserRole.Reviewer, ValidPassword, cancellationToken);
        var project = await projects.CreateAsync("Apollo", null, cancellationToken);
        await reviews.CreateDraftAsync(project.Id, "rev-1", "First", [new("a.txt", TestContent.A)], cancellationToken);
        var annActor = new Actor("ann", "Ann");
        var bobActor = new Actor("bob", "Bob");
        var slot = await signing.AddSlotAsync(annActor, project.Id, "rev-1", ann.Id, bob.Id, cancellationToken);

        // Act: read the blocked status, answer every check, then sign with a wrong and with the right password
        var blocked = await signing.GetApprovalAsync(project.Id, "rev-1", cancellationToken);
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await checklists.SetAnswerAsync(project.Id, "rev-1", item.Id, ChecklistOutcome.Pass, null, annActor, cancellationToken);
        }

        var ready = await signing.GetApprovalAsync(project.Id, "rev-1", cancellationToken);
        var wrong = await Assert.ThrowsAsync<DomainRuleException>(() => signing.SignAsync(
            annActor, project.Id, "rev-1", slot.Id, ready!.Digest, "wrong password here", cancellationToken));
        await signing.SignAsync(annActor, project.Id, "rev-1", slot.Id, ready!.Digest, ValidPassword, cancellationToken);
        var approved = await signing.GetApprovalAsync(project.Id, "rev-1", cancellationToken);

        // Act: a comment is posted and resolved, so the record changes; the alternate then signs the new record
        var thread = await threads.StartThreadAsync(
            project.Id, "rev-1", "a.txt", null, null, bobActor, "One more thing", cancellationToken);
        await threads.ResolveAsync(project.Id, "rev-1", thread.Id, cancellationToken);
        var outOfDate = await signing.GetApprovalAsync(project.Id, "rev-1", cancellationToken);
        await signing.SignAsync(bobActor, project.Id, "rev-1", slot.Id, outOfDate!.Digest, ValidPassword, cancellationToken);
        var resigned = await signing.GetApprovalAsync(project.Id, "rev-1", cancellationToken);

        // Assert: blocked until answered, wrong password refused, approved after signing, out of date after the
        // change, and approved again by the alternate with both signatures kept
        Assert.False(blocked!.IsApproved);
        Assert.NotEmpty(blocked.Blockers);
        Assert.Empty(ready.Blockers);
        Assert.Equal(DomainRule.ReauthenticationFailed, wrong.Rule);
        Assert.True(approved!.IsApproved);
        Assert.False(outOfDate.IsApproved);
        Assert.Equal(SlotSignatureState.OutOfDate, outOfDate.Slots.Single().State);
        Assert.NotEqual(ready.Digest, outOfDate.Digest);
        Assert.True(resigned!.IsApproved);
        Assert.Equal("Bob", resigned.Slots.Single().SignerName);
        Assert.Equal(2, (await store.ListSignaturesAsync(project.Id, "rev-1", cancellationToken)).Count);
    }
}
