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
        var projects = TestServices.Projects(store, new FixedTimeProvider(_now));
        var reviews = TestServices.Reviews(store, new FixedTimeProvider(_now));

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
        var projects = TestServices.Projects(store, new FixedTimeProvider(_now));
        var reviews = TestServices.Reviews(store, new FixedTimeProvider(_now));
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
    public static TheoryData<DomainRule> AllRules => [.. Enum.GetValues<DomainRule>().Where(rule => !GitSourceRules.Contains(rule))];

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
    ///     The rules that the second factor service reports.
    /// </summary>
    private static readonly DomainRule[] TotpRules =
    [
        DomainRule.TotpNotAvailable,
        DomainRule.TotpAlreadyEnrolled,
        DomainRule.TotpNotEnrolled,
        DomainRule.TotpCodeIncorrect,
        DomainRule.TotpEnrollmentExpired,
        DomainRule.TotpSecretUnreadable
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
        var projects = TestServices.Projects(store, new FixedTimeProvider(_now));
        var reviews = TestServices.Reviews(store, new FixedTimeProvider(_now));
        var threads = TestServices.Threads(store, new FixedTimeProvider(_now));
        var checklists = TestServices.Checklists(store, new FixedTimeProvider(_now));
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), new FixedTimeProvider(_now));
        var root = await users.CreateAsync("root", "Root", UserRole.Admin, ValidPassword, TestContext.Current.CancellationToken);
        var project = await projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await store.AddMembershipAsync(root.Id, project.Id, TestContext.Current.CancellationToken);
        await reviews.CreateDraftAsync(
            project.Id, "rev-1", "First", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);

        // Act: break the rule
        var signing = TestServices.Signing(store, users, new FixedTimeProvider(_now));
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => ViolateByGroupAsync(rule, signing, checklists, store, root, projects, reviews, threads, users, project.Id));

        // Assert: the rule is named, and only the first valid project, review and user exist, with no thread; the
        // only answers are those the re-authentication scenario needs to get as far as the password
        Assert.Equal(rule, exception.Rule);
        Assert.Single(await projects.ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(rule is DomainRule.ReviewerNotIndependent or DomainRule.SlotSigned or DomainRule.SignaturesWouldBecomeOutOfDate ? 2 : 1, (await users.ListAsync(TestContext.Current.CancellationToken)).Count);
        Assert.Single(store.Reviews);
        Assert.Empty(store.Threads);
        var overall = (await checklists.GetAsync(project.Id, "rev-1", TestContext.Current.CancellationToken))!.Overall;
        Assert.Equal(rule is DomainRule.ReauthenticationFailed or DomainRule.ReviewLocked or DomainRule.SlotSigned or DomainRule.SignaturesWouldBecomeOutOfDate ? overall.Total : 0, overall.Answered);
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
            return ViolateSigningAsync(rule, signing, checklists, store, root, users, projectId);
        }

        if (RepositoryRules.Contains(rule))
        {
            return ViolateRepositoryAsync(rule, users);
        }

        if (rule is DomainRule.MembershipExists or DomainRule.MembershipNotFound or DomainRule.LastProjectAdmin
            or DomainRule.TooManyAttempts or DomainRule.PasswordChangeRequired)
        {
            return ViolateMembershipAsync(rule, store, projectId);
        }

        if (TokenRules.Contains(rule))
        {
            return ViolateTokenAsync(rule);
        }

        if (TotpRules.Contains(rule))
        {
            return ViolateTotpAsync(rule);
        }

        return ViolateAsync(rule, projects, reviews, threads, checklists, users, projectId);
    }

    /// <summary>
    ///     Makes a request that breaks one rule of the project membership.
    /// </summary>
    /// <param name="rule">The rule to break; <see cref="DomainRule.MembershipExists"/> or
    ///     <see cref="DomainRule.MembershipNotFound"/>.</param>
    /// <param name="store">The store that holds the project.</param>
    /// <param name="projectId">The identity of the existing project.</param>
    /// <returns>A task that fails with the rule violation.</returns>
    private static async Task ViolateMembershipAsync(DomainRule rule, FakeReviewStore store, int projectId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FixedTimeProvider(_now);
        var userStore = new FakeUserStore(store);
        var userService = new UserService(userStore, new FakePasswordHasher(), clock);

        if (rule is DomainRule.LastProjectAdmin or DomainRule.TooManyAttempts or DomainRule.PasswordChangeRequired)
        {
            await ViolateAdministrationAsync(rule, store, userStore, userService, projectId);
            return;
        }

        // A first user takes the identity the arranged member of the project has, so "mem" is no member
        await userService.CreateAsync("first", "First", UserRole.Reviewer, ValidPassword, cancellationToken);
        await userService.CreateAsync("mem", "Mem", UserRole.Reviewer, ValidPassword, cancellationToken);
        var service = new ProjectService(
            store, userStore, new AllowAllProjectAccess(store), new FakePasswordHasher(), clock);
        var actor = new Actor("root", "Root");
        if (rule == DomainRule.MembershipNotFound)
        {
            await service.RemoveMemberAsync(actor, projectId, "mem", cancellationToken);
            return;
        }

        await service.AddMemberAsync(actor, projectId, "mem", ProjectRole.User, cancellationToken);
        await service.AddMemberAsync(actor, projectId, "mem", ProjectRole.User, cancellationToken);
    }

    /// <summary>
    ///     Makes a request that breaks one of the rules of project administration: the last project administrator, too
    ///     many failed attempts, or an account that still has a temporary password.
    /// </summary>
    /// <param name="rule">The rule to break.</param>
    /// <param name="store">The store that holds the project.</param>
    /// <param name="userStore">The store of the users.</param>
    /// <param name="userService">The user service.</param>
    /// <param name="projectId">The identity of the existing project.</param>
    /// <returns>A task that fails with the rule violation.</returns>
    private static async Task ViolateAdministrationAsync(
        DomainRule rule,
        FakeReviewStore store,
        FakeUserStore userStore,
        UserService userService,
        int projectId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FixedTimeProvider(_now);

        // A first user takes the identity the arranged member of the project has; then comes a project administrator
        // who is no system administrator, so the rules are not waived for them
        await userService.CreateAsync("first", "First", UserRole.Reviewer, ValidPassword, cancellationToken);
        var padm = await userService.CreateAsync("padm", "Padm", UserRole.Reviewer, ValidPassword, cancellationToken);
        await store.AddMembershipAsync(padm.Id, projectId, ProjectRole.ProjectAdmin, cancellationToken);
        var service = new ProjectService(
            store, userStore, new ProjectAccess(userStore, store), new FakePasswordHasher(), clock);
        var actor = new Actor("padm", "Padm");
        switch (rule)
        {
            case DomainRule.LastProjectAdmin:
                await service.RemoveMemberAsync(actor, projectId, "padm", cancellationToken);
                return;
            case DomainRule.TooManyAttempts:
                for (var i = 0; i < ProjectService.MaxFailedAttempts; i++)
                {
                    await Assert.ThrowsAsync<DomainRuleException>(
                        () => service.AddMemberAsync(actor, projectId, "nobody", ProjectRole.User, cancellationToken));
                }

                await service.AddMemberAsync(actor, projectId, "nobody", ProjectRole.User, cancellationToken);
                return;
            default:
                await userStore.AddAsync(
                    new User
                    {
                        Username = "temp",
                        DisplayName = "Temp",
                        Enabled = true,
                        MustChangePassword = true,
                        PasswordHash = "hash",
                        Created = _now
                    },
                    created => AuditDraft.By(Actor.CommandLine, "user.created", created.Username),
                    cancellationToken);
                var tokens = new PersonalAccessTokenService(
                    new FakePersonalAccessTokenStore(userStore), userStore, clock);
                await tokens.ListAsync(new Actor("temp", "Temp"), cancellationToken);
                return;
        }
    }

    /// <summary>
    ///     Makes a request that breaks one rule of the second factor service, in a world of its own.
    /// </summary>
    /// <param name="rule">The rule to break; one of <see cref="TotpRules"/>.</param>
    /// <returns>A task that fails with the rule violation.</returns>
    private static async Task ViolateTotpAsync(DomainRule rule)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = new TotpTestWorld(
            rule == DomainRule.TotpNotAvailable ? new TotpSettings(false, TotpSettings.DefaultIssuer) : null);
        await world.AddUserAsync("tot");
        var actor = new Actor("tot", "Tot");
        switch (rule)
        {
            case DomainRule.TotpNotAvailable:
                await world.Totp.BeginEnrollmentAsync(actor, TotpTestWorld.Password, cancellationToken);
                return;
            case DomainRule.TotpAlreadyEnrolled:
                await world.EnrollAsync("tot");
                await world.Totp.BeginEnrollmentAsync(actor, TotpTestWorld.Password, cancellationToken);
                return;
            case DomainRule.TotpNotEnrolled:
                await world.Totp.DisableAsync(actor, TotpTestWorld.Password, "000000", cancellationToken);
                return;
            case DomainRule.TotpCodeIncorrect:
                {
                    var enrollment = await world.Totp.BeginEnrollmentAsync(actor, TotpTestWorld.Password, cancellationToken);
                    var secret = TotpOracle.FromBase32(enrollment.SecretBase32);
                    var right = world.CodeNow(secret);
                    var wrong = right == "000000" ? "000001" : "000000";
                    await world.Totp.ConfirmEnrollmentAsync(actor, wrong, cancellationToken);
                    return;
                }

            case DomainRule.TotpEnrollmentExpired:
                await world.Totp.BeginEnrollmentAsync(actor, TotpTestWorld.Password, cancellationToken);
                world.Clock.Advance(TimeSpan.FromHours(1));
                await world.Totp.ConfirmEnrollmentAsync(actor, "000000", cancellationToken);
                return;
            default:
                {
                    var (secret, _) = await world.EnrollAsync("tot");
                    world.Protector.Unreadable = true;
                    world.AdvanceSteps(2);
                    await world.Totp.DisableAsync(actor, TotpTestWorld.Password, world.CodeNow(secret), cancellationToken);
                    return;
                }
        }
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
        var clock = new FixedTimeProvider(_now);
        var reviewStore = new FakeReviewStore();
        var project = await TestServices.Projects(reviewStore, clock).CreateAsync("P", null, cancellationToken);
        var service = TestServices.Repositories(reviewStore, clock, null, protector);
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
                var repository = await service.CreateAsync("repo", location, null, null, actor, cancellationToken);
                var git = new FakeGitRepositoryProvider();
                git.Revisions["main"] = "h1";
                git.Script(null, "h1", [("docs/a.md", ChangeKind.Added, null, "x")]);
                await TestServices.Reviews(reviewStore, clock, service, git).CreateFromRepositoryAsync(
                    project.Id, "rev-1", "T", new RepositorySource(repository.Id, "main", null, "src/**"), actor, cancellationToken);
                return;
            case DomainRule.ReviewSetConfigMissing:
            case DomainRule.ReviewSetConfigInvalid:
                // A plan for a head that has no configuration, or one the parser rejects
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
                var planner = TestServices.Planner(
                    reviewStore,
                    TestServices.Reviews(reviewStore, clock, service, planGit),
                    service,
                    planGit,
                    TestServices.Signing(reviewStore, users, clock),
                    parser);
                await planner.PlanAsync(actor, project.Id, planRepository.Id, "main", BaselineMode.None, null, cancellationToken);
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
        DomainRule.RecordChanged,
        DomainRule.ReviewLocked,
        DomainRule.ReviewNotLocked,
        DomainRule.ReopenReasonInvalid,
        DomainRule.ReviewerNotIndependent,
        DomainRule.SignaturesWouldBecomeOutOfDate
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
    /// <param name="users">The user service, to create the author of the review where a rule needs them.</param>
    /// <param name="projectId">The identity of the existing project.</param>
    /// <returns>A task that fails with the rule violation.</returns>
    private static async Task ViolateSigningAsync(
        DomainRule rule,
        SigningService signing,
        ChecklistService checklists,
        FakeReviewStore store,
        User root,
        UserService users,
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
            case DomainRule.ReopenReasonInvalid:
                await signing.ReopenReviewAsync(actor, projectId, "rev-1", "  ", cancellationToken);
                return;
            case DomainRule.ReviewNotLocked:
                await signing.ReopenReviewAsync(actor, projectId, "rev-1", "Not approved yet", cancellationToken);
                return;
            case DomainRule.ReviewerNotIndependent:
                // The review was created by the user "author", who is a member of the project and so a candidate
                var author = await users.CreateAsync("author", "Author", UserRole.Reviewer, ValidPassword, cancellationToken);
                await store.AddMembershipAsync(author.Id, projectId, cancellationToken);
                await signing.AddSlotAsync(actor, projectId, "rev-1", author.Id, null, cancellationToken);
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
                // A second slot nobody signs keeps the review from being approved and so from being locked; then the
                // first slot is signed and removed
                var second = await users.CreateAsync("second", "Second", UserRole.Reviewer, ValidPassword, cancellationToken);
                await store.AddMembershipAsync(second.Id, projectId, cancellationToken);
                await signing.AddSlotAsync(actor, projectId, "rev-1", second.Id, null, cancellationToken);
                await AnswerAllAndSignAsync(signing, checklists, actor, projectId, slot.Id, cancellationToken);
                await signing.RemoveSlotAsync(actor, projectId, "rev-1", slot.Id, cancellationToken);
                return;
            case DomainRule.SignaturesWouldBecomeOutOfDate:
                // A second slot nobody signs keeps the review from being approved and so from being locked; the
                // first slot is signed, and then an answer changes without acknowledging that signature
                var other = await users.CreateAsync("other", "Other", UserRole.Reviewer, ValidPassword, cancellationToken);
                await store.AddMembershipAsync(other.Id, projectId, cancellationToken);
                await signing.AddSlotAsync(actor, projectId, "rev-1", other.Id, null, cancellationToken);
                await AnswerAllAndSignAsync(signing, checklists, actor, projectId, slot.Id, cancellationToken);
                await checklists.SetAnswerAsync(
                    projectId,
                    "rev-1",
                    DefaultChecklist.Current.Sections[0].Items[0].Id,
                    ChecklistOutcome.Fail,
                    "Found a problem.",
                    actor,
                    cancellationToken);
                return;
            case DomainRule.ReviewLocked:
                // Answer every check, sign, and then try to change the approved review
                await AnswerAllAndSignAsync(signing, checklists, actor, projectId, slot.Id, cancellationToken);
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
    ///     Answers every check of <c>rev-1</c> as passed and signs the slot with the resulting record.
    /// </summary>
    /// <param name="signing">The signing service.</param>
    /// <param name="checklists">The checklist service.</param>
    /// <param name="actor">The signer, who holds the slot.</param>
    /// <param name="projectId">The identity of the existing project.</param>
    /// <param name="slotId">The slot to sign.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the slot is signed.</returns>
    private static async Task AnswerAllAndSignAsync(
        SigningService signing,
        ChecklistService checklists,
        Actor actor,
        int projectId,
        int slotId,
        CancellationToken cancellationToken)
    {
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await checklists.SetAnswerAsync(projectId, "rev-1", item.Id, ChecklistOutcome.Pass, null, actor, cancellationToken);
        }

        var digest = (await signing.GetApprovalAsync(projectId, "rev-1", cancellationToken))!.Digest;
        await signing.SignAsync(actor, projectId, "rev-1", slotId, digest, ValidPassword, cancellationToken);
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
                projectId, "rev-1", new ThreadAnchor("a.txt"), TestActors.Ann, " ", cancellationToken),
            DomainRule.ThreadAnchorInvalid => threads.StartThreadAsync(
                projectId, "rev-1", new ThreadAnchor("a.txt", 1, 99), TestActors.Ann, "Text", cancellationToken),
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
            DomainRule.ChecklistDefinitionInvalid => Task.Run(() => ChecklistValidator.ParseOrThrow("{ not json"), cancellationToken),
            DomainRule.DueDateInvalid => reviews.CreateDraftAsync(
                projectId, "rev-2", "T", oneFile, TestActors.Ann, cancellationToken, dueDate: DateOnly.MinValue),
            DomainRule.FollowUpInvalid => reviews.CreateDraftAsync(
                projectId, "rev-2", "T", oneFile, TestActors.Ann, cancellationToken, followsReviewId: "no-such-review"),
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
        var projects = TestServices.Projects(store, new FixedTimeProvider(_now));
        var reviews = TestServices.Reviews(store, new FixedTimeProvider(_now));
        var threads = TestServices.Threads(store, new FixedTimeProvider(_now));
        var project = await projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await reviews.CreateDraftAsync(
            project.Id,
            "rev-1",
            "First",
            [new("ab.txt", "a\nb\n"u8.ToArray())],
            TestContext.Current.CancellationToken);

        // Act: start a thread, reply twice, resolve it and read it back, then reopen it and read it again
        var started = await threads.StartThreadAsync(
            project.Id, "rev-1", new ThreadAnchor("ab.txt", 1, 2), TestActors.Ann, "First", TestContext.Current.CancellationToken);
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
        var projects = TestServices.Projects(store, new FixedTimeProvider(_now));
        var reviews = TestServices.Reviews(store, new FixedTimeProvider(_now));
        var checklists = TestServices.Checklists(store, new FixedTimeProvider(_now));
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
        var projects = TestServices.Projects(store, new FixedTimeProvider(_now));
        var reviews = TestServices.Reviews(store, new FixedTimeProvider(_now));
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), new FixedTimeProvider(_now));
        var signing = TestServices.Signing(store, users, new FixedTimeProvider(_now));
        var ann = await users.CreateAsync("ann", "Ann", UserRole.Reviewer, ValidPassword, cancellationToken);
        var bob = await users.CreateAsync("bob", "Bob", UserRole.Reviewer, ValidPassword, cancellationToken);
        var project = await projects.CreateAsync("Apollo", null, cancellationToken);
        await store.AddMembershipAsync(ann.Id, project.Id, cancellationToken);
        await store.AddMembershipAsync(bob.Id, project.Id, cancellationToken);
        var viaToken = new Actor("ann", "Ann") { ApiTokenId = "K7M2QX9P" };
        var bobViaToken = new Actor("bob", "Bob") { ApiTokenId = "K7M2QX9P" };

        // Act: create a review as the token actor, name a reviewer (not the author) as the person, then try to name
        // and sign as a token actor
        var review = await reviews.CreateDraftAsync(project.Id, "rev-9", "Via API", [new("a.txt", TestContent.A)], viaToken, cancellationToken);
        var slot = await signing.AddSlotAsync(new Actor("ann", "Ann"), project.Id, "rev-9", bob.Id, null, cancellationToken);
        var approval = await signing.GetApprovalAsync(project.Id, "rev-9", cancellationToken);
        var naming = await Assert.ThrowsAsync<DomainRuleException>(
            () => signing.AddSlotAsync(viaToken, project.Id, "rev-9", bob.Id, null, cancellationToken));
        var signed = await Assert.ThrowsAsync<DomainRuleException>(() => signing.SignAsync(
            bobViaToken, project.Id, "rev-9", slot.Id, approval!.Digest, ValidPassword, cancellationToken));

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
        var projects = TestServices.Projects(store, new FixedTimeProvider(_now));
        var reviews = TestServices.Reviews(store, new FixedTimeProvider(_now));
        var checklists = TestServices.Checklists(store, new FixedTimeProvider(_now));
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), new FixedTimeProvider(_now));
        var signing = TestServices.Signing(store, users, new FixedTimeProvider(_now));
        var ann = await users.CreateAsync("ann", "Ann", UserRole.Reviewer, ValidPassword, cancellationToken);
        var bob = await users.CreateAsync("bob", "Bob", UserRole.Reviewer, ValidPassword, cancellationToken);
        var project = await projects.CreateAsync("Apollo", null, cancellationToken);
        await store.AddMembershipAsync(ann.Id, project.Id, cancellationToken);
        await store.AddMembershipAsync(bob.Id, project.Id, cancellationToken);
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

        // Act: an administrator reopens the approved review, so the record changes; the alternate then signs the new record
        await signing.ReopenReviewAsync(annActor, project.Id, "rev-1", "One more thing", cancellationToken);
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

    /// <summary>
    ///     Test that, across the services, a change to a partly signed review is refused until the person acknowledges
    ///     the signature it would make out of date, that the refusal names the slot and the signer and changes nothing,
    ///     and that the acknowledged change then goes through and the signature is out of date.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_Signatures_ChangeAfterSigning_NeedsAcknowledgement()
    {
        // Arrange: two slots, every check answered, and only the first slot signed, so the review is not locked
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var projects = TestServices.Projects(store, new FixedTimeProvider(_now));
        var reviews = TestServices.Reviews(store, new FixedTimeProvider(_now));
        var checklists = TestServices.Checklists(store, new FixedTimeProvider(_now));
        var threads = TestServices.Threads(store, new FixedTimeProvider(_now));
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), new FixedTimeProvider(_now));
        var signing = TestServices.Signing(store, users, new FixedTimeProvider(_now));
        var ann = await users.CreateAsync("ann", "Ann", UserRole.Reviewer, ValidPassword, cancellationToken);
        var bob = await users.CreateAsync("bob", "Bob", UserRole.Reviewer, ValidPassword, cancellationToken);
        var project = await projects.CreateAsync("Apollo", null, cancellationToken);
        await store.AddMembershipAsync(ann.Id, project.Id, cancellationToken);
        await store.AddMembershipAsync(bob.Id, project.Id, cancellationToken);
        await reviews.CreateDraftAsync(project.Id, "rev-1", "First", [new("a.txt", TestContent.A)], cancellationToken);
        var annActor = new Actor("ann", "Ann");
        var first = await signing.AddSlotAsync(annActor, project.Id, "rev-1", ann.Id, null, cancellationToken);
        await signing.AddSlotAsync(annActor, project.Id, "rev-1", bob.Id, null, cancellationToken);
        await AnswerAllAndSignAsync(signing, checklists, annActor, project.Id, first.Id, cancellationToken);
        var itemId = DefaultChecklist.Current.Sections[0].Items[0].Id;

        // Act: change an answer and start a thread without acknowledging
        var answer = await Assert.ThrowsAsync<DomainRuleException>(() => checklists.SetAnswerAsync(
            project.Id, "rev-1", itemId, ChecklistOutcome.Fail, "Found a problem.", annActor, cancellationToken));
        var thread = await Assert.ThrowsAsync<DomainRuleException>(() => threads.StartThreadAsync(
            project.Id, "rev-1", new ThreadAnchor("a.txt"), annActor, "A remark", cancellationToken));
        var untouched = await signing.GetApprovalAsync(project.Id, "rev-1", cancellationToken);

        // Assert: both refused naming slot 1 and its signer, and nothing was written
        Assert.Equal(DomainRule.SignaturesWouldBecomeOutOfDate, answer.Rule);
        Assert.Equal(DomainRule.SignaturesWouldBecomeOutOfDate, thread.Rule);
        var affected = Assert.Single(answer.AffectedSlots);
        Assert.Equal(new AffectedSignature(first.Id, 1, "Ann", ann.Id), affected);
        Assert.Equal(first.Id, Assert.Single(thread.AffectedSlots).SlotId);
        Assert.Contains("slot 1 (Ann)", answer.Message);
        Assert.Empty(store.Threads);
        Assert.Equal(SlotSignatureState.Signed, untouched!.Slots[0].State);

        // Act: acknowledge the named slot and change the answer
        await checklists.SetAnswerAsync(
            project.Id, "rev-1", itemId, ChecklistOutcome.Fail, "Found a problem.", annActor, cancellationToken, [affected.SlotId]);
        var changed = await signing.GetApprovalAsync(project.Id, "rev-1", cancellationToken);

        // Assert: the change went through and the signature is kept but out of date
        Assert.Equal(SlotSignatureState.OutOfDate, changed!.Slots[0].State);
        Assert.False(changed.IsApproved);
        Assert.Single(await store.ListSignaturesAsync(project.Id, "rev-1", cancellationToken));
    }

    /// <summary>
    ///     The services of one project under the real access rules, with a system administrator, the author of the
    ///     review and two more members.
    /// </summary>
    /// <param name="Store">The review store.</param>
    /// <param name="Users">The user service.</param>
    /// <param name="Projects">The project service.</param>
    /// <param name="Threads">The thread service.</param>
    /// <param name="Checklists">The checklist service.</param>
    /// <param name="Signing">The signing service.</param>
    /// <param name="ProjectId">The identity of the project.</param>
    /// <param name="Root">The system administrator.</param>
    /// <param name="Ann">The author of the review <c>rev-1</c>.</param>
    /// <param name="Bob">A reviewer.</param>
    /// <param name="Cyd">Another reviewer.</param>
    /// <param name="Ids">The user identities by user name.</param>
    private sealed record Realm(
        FakeReviewStore Store,
        UserService Users,
        ProjectService Projects,
        ThreadService Threads,
        ChecklistService Checklists,
        SigningService Signing,
        int ProjectId,
        Actor Root,
        Actor Ann,
        Actor Bob,
        Actor Cyd,
        IReadOnlyDictionary<string, int> Ids);

    /// <summary>
    ///     Creates the services over one store under the real access rules, a project, and the review
    ///     <c>rev-1</c> that <c>ann</c> created.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The realm.</returns>
    private static async Task<Realm> CreateRealmAsync(CancellationToken cancellationToken)
    {
        var clock = new FixedTimeProvider(_now);
        var store = new FakeReviewStore();
        var userStore = new FakeUserStore();
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        var access = new ProjectAccess(userStore, store);
        await users.CreateAsync("root", "Root", true, ValidPassword, Actor.CommandLine, cancellationToken);
        var root = new Actor("root", "Root");
        var projects = new ProjectService(store, userStore, access, new FakePasswordHasher(), clock);
        var project = await projects.CreateAsync("Apollo", null, root, cancellationToken);
        var ids = new Dictionary<string, int>();
        foreach (var name in new[] { "ann", "bob", "cyd" })
        {
            var user = await users.CreateAsync(name, char.ToUpperInvariant(name[0]) + name[1..], false, ValidPassword, Actor.CommandLine, cancellationToken);
            await store.AddMembershipAsync(user.Id, project.Id, ProjectRole.User, cancellationToken);
            ids[name] = user.Id;
        }

        var ann = new Actor("ann", "Ann");
        await TestServices.Reviews(store, clock).CreateDraftAsync(
            project.Id, "rev-1", "First", [new("a.txt", TestContent.A)], ann, cancellationToken);
        return new Realm(
            store,
            users,
            projects,
            TestServices.Threads(store, clock),
            TestServices.Checklists(store, clock),
            new SigningService(store, users, access, clock),
            project.Id,
            root,
            ann,
            new Actor("bob", "Bob"),
            new Actor("cyd", "Cyd"),
            ids);
    }

    /// <summary>
    ///     Answers every check of <c>rev-1</c> as Pass, as the given actor.
    /// </summary>
    /// <param name="realm">The realm.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when every check is answered.</returns>
    private static async Task AnswerAllAsync(Realm realm, CancellationToken cancellationToken)
    {
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await realm.Checklists.SetAnswerAsync(
                realm.ProjectId, "rev-1", item.Id, ChecklistOutcome.Pass, null, realm.Bob, cancellationToken);
        }
    }

    /// <summary>
    ///     Gets the approval status of <c>rev-1</c>.
    /// </summary>
    /// <param name="realm">The realm.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The approval status.</returns>
    private static async Task<ReviewApproval> ApprovalOfAsync(Realm realm, CancellationToken cancellationToken)
    {
        return (await realm.Signing.GetApprovalAsync(realm.ProjectId, "rev-1", cancellationToken))!;
    }

    /// <summary>
    ///     Approves <c>rev-1</c>: a resolved thread, every check answered, one slot of <c>bob</c>, signed by bob.
    /// </summary>
    /// <param name="realm">The realm.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The identity of the thread, the identity of the slot and the digest that was signed.</returns>
    private static async Task<(int ThreadId, int SlotId, string Digest)> ApproveAsync(Realm realm, CancellationToken cancellationToken)
    {
        var thread = await realm.Threads.StartThreadAsync(
            realm.ProjectId, "rev-1", new ThreadAnchor("a.txt", 1, 1), realm.Bob, "A remark", cancellationToken);
        await realm.Threads.ResolveAsync(realm.ProjectId, "rev-1", thread.Id, realm.Bob, cancellationToken);
        await AnswerAllAsync(realm, cancellationToken);
        var slot = await realm.Signing.AddSlotAsync(realm.Bob, realm.ProjectId, "rev-1", realm.Ids["bob"], null, cancellationToken);
        var digest = (await ApprovalOfAsync(realm, cancellationToken)).Digest;
        await realm.Signing.SignAsync(realm.Bob, realm.ProjectId, "rev-1", slot.Id, digest, ValidPassword, cancellationToken);
        return (thread.Id, slot.Id, digest);
    }

    /// <summary>
    ///     Test that an approved review is locked: every kind of change (thread, reply, resolve, reopen of a thread,
    ///     answer, applicability, reviewer slots and signing) is refused with the same rule and changes nothing,
    ///     while the approval can still be read.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_ApprovedReview_RefusesEveryMutationWithReviewLocked()
    {
        // Arrange: an approved review with a resolved thread
        var cancellationToken = TestContext.Current.CancellationToken;
        var realm = await CreateRealmAsync(cancellationToken);
        var (threadId, slotId, digest) = await ApproveAsync(realm, cancellationToken);
        var section = DefaultChecklist.Current.Sections[0];
        var pid = realm.ProjectId;
        var mutations = new List<Func<Task>>
        {
            () => realm.Threads.StartThreadAsync(pid, "rev-1", new ThreadAnchor("a.txt", 2, 2), realm.Bob, "New", cancellationToken),
            () => realm.Threads.ReplyAsync(pid, "rev-1", threadId, realm.Bob, "More", cancellationToken),
            () => realm.Threads.ResolveAsync(pid, "rev-1", threadId, realm.Bob, cancellationToken),
            () => realm.Threads.ReopenAsync(pid, "rev-1", threadId, realm.Bob, cancellationToken),
            () => realm.Checklists.SetAnswerAsync(pid, "rev-1", section.Items[0].Id, ChecklistOutcome.Fail, "No.", realm.Bob, cancellationToken),
            () => realm.Checklists.SetSectionApplicabilityAsync(pid, "rev-1", section.Id, false, "Not relevant.", realm.Bob, cancellationToken),
            () => realm.Signing.AddSlotAsync(realm.Bob, pid, "rev-1", realm.Ids["cyd"], null, cancellationToken),
            () => realm.Signing.UpdateSlotAsync(realm.Bob, pid, "rev-1", slotId, realm.Ids["bob"], realm.Ids["cyd"], cancellationToken),
            () => realm.Signing.RemoveSlotAsync(realm.Bob, pid, "rev-1", slotId, cancellationToken),
            () => realm.Signing.SignAsync(realm.Bob, pid, "rev-1", slotId, digest, ValidPassword, cancellationToken)
        };

        // Act: try every change
        var rules = new List<DomainRule>();
        foreach (var mutation in mutations)
        {
            rules.Add((await Assert.ThrowsAsync<DomainRuleException>(mutation)).Rule);
        }

        // Assert: every one is refused as locked, and the approval is exactly as it was
        Assert.All(rules, rule => Assert.Equal(DomainRule.ReviewLocked, rule));
        var approval = await ApprovalOfAsync(realm, cancellationToken);
        Assert.True(approval.IsApproved);
        Assert.Equal(digest, approval.Digest);
        Assert.Single(approval.Slots);
        Assert.Single(await realm.Threads.ListAsync(pid, "rev-1", cancellationToken));
    }

    /// <summary>
    ///     Test that reopening needs an administrator and a reason, is audited, returns the review to In review with
    ///     the old signature kept as history, and that approving again needs a fresh signature for the new digest.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_ReopenReview_RequiresAdminAndReasonAndNeedsFreshSignatures()
    {
        // Arrange: an approved review
        var cancellationToken = TestContext.Current.CancellationToken;
        var realm = await CreateRealmAsync(cancellationToken);
        var (_, slotId, digest) = await ApproveAsync(realm, cancellationToken);
        var pid = realm.ProjectId;

        // Act: a member and an administrator's token try, then the administrator without and with a reason
        var byMember = await Assert.ThrowsAsync<DomainRuleException>(
            () => realm.Signing.ReopenReviewAsync(realm.Ann, pid, "rev-1", "Because.", cancellationToken));
        var byToken = await Assert.ThrowsAsync<DomainRuleException>(() => realm.Signing.ReopenReviewAsync(
            new Actor("root", "Root") { ApiTokenId = "K7M2QX9P" }, pid, "rev-1", "Because.", cancellationToken));
        var noReason = await Assert.ThrowsAsync<DomainRuleException>(
            () => realm.Signing.ReopenReviewAsync(realm.Root, pid, "rev-1", "  ", cancellationToken));
        await realm.Signing.ReopenReviewAsync(realm.Root, pid, "rev-1", "A file was missing", cancellationToken);
        var reopened = await ApprovalOfAsync(realm, cancellationToken);

        // Assert: only the administrator with a reason reopened; In review, history kept, and the reopen is audited
        Assert.Equal(DomainRule.ActorNotAllowed, byMember.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, byToken.Rule);
        Assert.Equal(DomainRule.ReopenReasonInvalid, noReason.Rule);
        Assert.False(reopened.IsApproved);
        Assert.Equal(SlotSignatureState.OutOfDate, reopened.Slots.Single().State);
        Assert.NotEqual(digest, reopened.Digest);
        var reopen = Assert.Single(reopened.Reopens!);
        Assert.Equal(("root", "A file was missing", digest), (reopen.ReopenedBy, reopen.Reason, reopen.PreviousDigest));
        Assert.Single(await realm.Store.ListSignaturesAsync(pid, "rev-1", cancellationToken));
        var audit = Assert.Single(realm.Store.Audits, a => a.Action == "review.reopened");
        Assert.Contains(digest, audit.Detail, StringComparison.Ordinal);
        Assert.Contains("A file was missing", audit.Detail, StringComparison.Ordinal);

        // Act: the review is unlocked again, so a fresh signature for the new digest approves it a second time
        await realm.Signing.SignAsync(realm.Bob, pid, "rev-1", slotId, reopened.Digest, ValidPassword, cancellationToken);
        var again = await ApprovalOfAsync(realm, cancellationToken);

        // Assert: approved again for the new digest, and both signatures are kept
        Assert.True(again.IsApproved);
        Assert.Equal(reopened.Digest, again.Digest);
        Assert.Equal(2, (await realm.Store.ListSignaturesAsync(pid, "rev-1", cancellationToken)).Count);
    }

    /// <summary>
    ///     Test that reopening a review that is not locked is refused with <see cref="DomainRule.ReviewNotLocked"/>
    ///     and records nothing.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_ReopenReview_OfUnlockedReview_ThrowsReviewNotLocked()
    {
        // Arrange: a review that nobody has signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var realm = await CreateRealmAsync(cancellationToken);

        // Act: an administrator reopens it
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => realm.Signing.ReopenReviewAsync(realm.Root, realm.ProjectId, "rev-1", "Why not.", cancellationToken));

        // Assert: refused, and nothing was recorded
        Assert.Equal(DomainRule.ReviewNotLocked, ex.Rule);
        Assert.Empty(await realm.Store.ListReopensAsync(realm.ProjectId, "rev-1", cancellationToken));
        Assert.DoesNotContain(realm.Store.Audits, a => a.Action == "review.reopened");
    }

    /// <summary>
    ///     Test that, while the project requires independent reviewers, the author of a review cannot be a primary
    ///     or alternate reviewer and cannot sign; with the setting off the author can be a reviewer, and switching
    ///     it on again stops that author from signing.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_IndependentReviewers_AuthorCannotBeSlotReviewerOrSign()
    {
        // Arrange: the author ann created rev-1 in a project that requires independent reviewers (the default)
        var cancellationToken = TestContext.Current.CancellationToken;
        var realm = await CreateRealmAsync(cancellationToken);
        var pid = realm.ProjectId;

        // Act: name the author as primary and as alternate
        var primary = await Assert.ThrowsAsync<DomainRuleException>(() => realm.Signing.AddSlotAsync(
            realm.Cyd, pid, "rev-1", realm.Ids["ann"], null, cancellationToken));
        var alternate = await Assert.ThrowsAsync<DomainRuleException>(() => realm.Signing.AddSlotAsync(
            realm.Cyd, pid, "rev-1", realm.Ids["bob"], realm.Ids["ann"], cancellationToken));

        // Assert: refused with the independence rule, and no slot exists
        Assert.Equal(DomainRule.ReviewerNotIndependent, primary.Rule);
        Assert.Equal(DomainRule.ReviewerNotIndependent, alternate.Rule);
        Assert.Empty((await ApprovalOfAsync(realm, cancellationToken)).Slots);

        // Act: an administrator switches the rule off, the author is seated, and the rule is switched on again
        Assert.True(await realm.Projects.SetRequireIndependentReviewersAsync(realm.Root, pid, true, false, cancellationToken));
        var slot = await realm.Signing.AddSlotAsync(realm.Cyd, pid, "rev-1", realm.Ids["ann"], null, cancellationToken);
        await AnswerAllAsync(realm, cancellationToken);
        var digest = (await ApprovalOfAsync(realm, cancellationToken)).Digest;
        Assert.True(await realm.Projects.SetRequireIndependentReviewersAsync(realm.Root, pid, false, true, cancellationToken));
        var signed = await Assert.ThrowsAsync<DomainRuleException>(() => realm.Signing.SignAsync(
            realm.Ann, pid, "rev-1", slot.Id, digest, ValidPassword, cancellationToken));

        // Assert: the author cannot sign, and nothing is signed
        Assert.Equal(DomainRule.ReviewerNotIndependent, signed.Rule);
        Assert.Empty(await realm.Store.ListSignaturesAsync(pid, "rev-1", cancellationToken));
    }

    /// <summary>
    ///     Test that only an administrator (not a member, not a token) changes the independence setting, that a
    ///     stale expectation is refused, and that every change is audited.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_IndependentReviewers_SettingChangeIsAuditedAndAdminOnly()
    {
        // Arrange: a project with the default setting (on)
        var cancellationToken = TestContext.Current.CancellationToken;
        var realm = await CreateRealmAsync(cancellationToken);
        var pid = realm.ProjectId;

        // Act: a member and an administrator's token try, then the administrator switches off, and a stale
        // expectation tries again
        var byMember = await Assert.ThrowsAsync<DomainRuleException>(
            () => realm.Projects.SetRequireIndependentReviewersAsync(realm.Ann, pid, true, false, cancellationToken));
        var byToken = await Assert.ThrowsAsync<DomainRuleException>(() => realm.Projects.SetRequireIndependentReviewersAsync(
            new Actor("root", "Root") { ApiTokenId = "K7M2QX9P" }, pid, true, false, cancellationToken));
        var changed = await realm.Projects.SetRequireIndependentReviewersAsync(realm.Root, pid, true, false, cancellationToken);
        var stale = await Assert.ThrowsAsync<DomainRuleException>(
            () => realm.Projects.SetRequireIndependentReviewersAsync(realm.Root, pid, true, false, cancellationToken));

        // Assert: refused for the member and the token; the administrator's change is stored and audited once
        Assert.Equal(DomainRule.ActorNotAllowed, byMember.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, byToken.Rule);
        Assert.True(changed);
        Assert.Equal(DomainRule.RecordChanged, stale.Rule);
        Assert.False((await realm.Store.GetProjectAsync(pid, cancellationToken))!.RequireIndependentReviewers);
        var audit = Assert.Single(realm.Store.Audits, a => a.Action == ProjectService.IndependentReviewersChangedAction);
        Assert.Equal("root", audit.ActorUsername);
        Assert.Contains("on->off", audit.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that only an administrator (not a member, not a token) changes the two further review rules, that a
    ///     stale expectation is refused, that every change is audited, and that independence is not touched.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_ReviewRuleSwitches_ChangeIsAuditedAdminOnlyAndStale()
    {
        // Arrange: a project with the default settings (all on)
        var cancellationToken = TestContext.Current.CancellationToken;
        var realm = await CreateRealmAsync(cancellationToken);
        var pid = realm.ProjectId;

        // Act: a member and an administrator's token try, then the administrator switches both rules off, and a
        // stale expectation tries again
        var byMember = await Assert.ThrowsAsync<DomainRuleException>(() => realm.Projects.SetReviewRuleAsync(
            realm.Ann, pid, ReviewRule.NonAuthorChecklistAnswers, true, false, cancellationToken));
        var byToken = await Assert.ThrowsAsync<DomainRuleException>(() => realm.Projects.SetReviewRuleAsync(
            new Actor("root", "Root") { ApiTokenId = "K7M2QX9P" }, pid, ReviewRule.NonReopenerSigning, true, false, cancellationToken));
        var authorChanged = await realm.Projects.SetReviewRuleAsync(
            realm.Root, pid, ReviewRule.NonAuthorChecklistAnswers, true, false, cancellationToken);
        var reopenerChanged = await realm.Projects.SetReviewRuleAsync(
            realm.Root, pid, ReviewRule.NonReopenerSigning, true, false, cancellationToken);
        var stale = await Assert.ThrowsAsync<DomainRuleException>(() => realm.Projects.SetReviewRuleAsync(
            realm.Root, pid, ReviewRule.NonAuthorChecklistAnswers, true, false, cancellationToken));

        // Assert: refused for the member and the token; both changes are stored and audited once each
        Assert.Equal(DomainRule.ActorNotAllowed, byMember.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, byToken.Rule);
        Assert.True(authorChanged);
        Assert.True(reopenerChanged);
        Assert.Equal(DomainRule.RecordChanged, stale.Rule);
        var project = (await realm.Store.GetProjectAsync(pid, cancellationToken))!;
        Assert.False(project.RequireNonAuthorChecklistAnswers);
        Assert.False(project.RequireNonReopenerSigning);
        Assert.True(project.RequireIndependentReviewers);
        var audits = realm.Store.Audits.Where(a => a.Action == ProjectService.ReviewRuleChangedAction).ToList();
        Assert.Equal(2, audits.Count);
        Assert.Equal("RequireNonAuthorChecklistAnswers on->off", audits[0].Detail);
        Assert.Equal("RequireNonReopenerSigning on->off", audits[1].Detail);
        Assert.All(audits, a => Assert.Equal("root", a.ActorUsername));
    }

    /// <summary>
    ///     Test that the author of a review created through an API token is the user of the token, so that user
    ///     cannot be a reviewer of it while independence is required.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_ApiTokenReview_AuthorIsTokenUser()
    {
        // Arrange: the realm and a token of ann
        var cancellationToken = TestContext.Current.CancellationToken;
        var realm = await CreateRealmAsync(cancellationToken);
        var viaToken = new Actor("ann", "Ann") { ApiTokenId = "K7M2QX9P" };

        // Act: create a review through the token, then name ann as its reviewer
        var review = await TestServices.Reviews(realm.Store, new FixedTimeProvider(_now)).CreateDraftAsync(
            realm.ProjectId, "rev-9", "Via API", [new("a.txt", TestContent.A)], viaToken, cancellationToken);
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => realm.Signing.AddSlotAsync(
            realm.Cyd, realm.ProjectId, "rev-9", realm.Ids["ann"], null, cancellationToken));

        // Assert: the author is ann (the token's user), flagged as created through the API, and ann is not independent
        Assert.Equal("ann", review.CreatedBy);
        Assert.True(review.CreatedViaApi);
        Assert.Equal(DomainRule.ReviewerNotIndependent, ex.Rule);
    }

    /// <summary>
    ///     Test the inbox workflow end to end over the real services: what waits for the reviewers and the author
    ///     appears as thread, signature and approval happen, and the action count follows the distinct reviews.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_InboxWorkflow_ValidInput_ListsActionItemsAndCounts()
    {
        // Arrange: a review by the author with reviewers ann and bob; ann asks a question and bob answers
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        var annSlot = await world.AddSlotAsync("r1", "ann");
        var bobSlot = await world.AddSlotAsync("r1", "bob");
        var thread = await world.StartThreadAsync("r1", "ann", "Why?");
        await world.ReplyAsync("r1", thread.Id, "bob", "Because");

        // Act and Assert: ann has the review twice (to sign and a reply waiting) but counts it once
        var ann = await world.InboxOfAsync("ann");
        Assert.Equal([InboxSection.ToSign, InboxSection.RepliesWaiting], ann.Items.Select(item => item.Section));
        Assert.Equal(1, ann.ActionCount);
        Assert.Equal(1, await world.Inbox.CountAsync(InboxTestWorld.ActorOf("ann"), cancellationToken));

        // Assert: bob only has to sign, the author waits for the replies
        Assert.Equal([InboxSection.ToSign], (await world.InboxOfAsync("bob")).Items.Select(item => item.Section));
        Assert.Equal([InboxSection.RepliesWaiting], (await world.InboxOfAsync("author")).Items.Select(item => item.Section));

        // Act: resolve the thread, answer every check, and let both reviewers sign
        await world.Threads.ResolveAsync(world.ProjectId, "r1", thread.Id, cancellationToken);
        await world.AnswerAllAsync("r1");
        await world.SignAsync("r1", annSlot.Id, "ann");
        Assert.Equal(0, (await world.InboxOfAsync("ann")).ActionCount);
        await world.SignAsync("r1", bobSlot.Id, "bob");

        // Assert: nothing is left to do; ann and the author are told about the approval, bob signed last himself
        Assert.Equal(0, await world.Inbox.CountAsync(InboxTestWorld.ActorOf("ann"), cancellationToken));
        var annItem = Assert.Single((await world.InboxOfAsync("ann")).Items);
        Assert.Equal(InboxSection.Updates, annItem.Section);
        Assert.Equal(InboxService.ApprovedChip, annItem.Chip);
        Assert.Equal(
            [InboxSection.Updates], (await world.InboxOfAsync("author")).Items.Select(item => item.Section));
        Assert.Empty((await world.InboxOfAsync("bob")).Items);
    }
}

