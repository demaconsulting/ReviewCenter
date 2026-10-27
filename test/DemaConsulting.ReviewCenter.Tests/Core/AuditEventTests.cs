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
///     Tests of the audit drafts the services hand to the stores: the action code and the detail format of
///     every audited event, and that no secret and no comment text is ever part of a draft.
/// </summary>
public class AuditEventTests
{
    /// <summary>
    ///     The instant reported by the clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     A password that satisfies the password policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The comment text used to check that comment text is never recorded.
    /// </summary>
    private const string SecretComment = "The vault password is hunter2-hunter2";

    /// <summary>
    ///     Everything the tests need: the services over the in-memory stores.
    /// </summary>
    /// <param name="Reviews">The review store, which keeps the drafts it was given.</param>
    /// <param name="Users">The user store, which keeps the drafts it was given.</param>
    /// <param name="Trail">The audit store that receives the best-effort events.</param>
    /// <param name="Clock">The movable clock.</param>
    /// <param name="UserService">The user service.</param>
    /// <param name="Projects">The project service.</param>
    /// <param name="ReviewService">The review service.</param>
    /// <param name="Threads">The thread service.</param>
    /// <param name="Checklists">The checklist service.</param>
    /// <param name="Signing">The signing service.</param>
    /// <param name="ProjectId">The id of the project <c>Apollo</c>.</param>
    /// <param name="UserIds">The ids of the users ann and bob.</param>
    private sealed record Fixture(
        FakeReviewStore Reviews,
        FakeUserStore Users,
        FakeAuditStore Trail,
        MovableTimeProvider Clock,
        UserService UserService,
        ProjectService Projects,
        ReviewService ReviewService,
        ThreadService Threads,
        ChecklistService Checklists,
        SigningService Signing,
        int ProjectId,
        IReadOnlyDictionary<string, int> UserIds);

    /// <summary>
    ///     Creates the services, the users ann and bob, the project <c>Apollo</c> and the review <c>rev-1</c>.
    /// </summary>
    /// <returns>The fixture.</returns>
    private static async Task<Fixture> CreateAsync()
    {
        var reviews = new FakeReviewStore();
        var users = new FakeUserStore();
        var clock = new MovableTimeProvider(_now);
        var trail = new FakeAuditStore(clock);
        var userService = new UserService(users, new FakePasswordHasher(), clock, new AuditService(trail, null, clock));
        var ids = new Dictionary<string, int>();
        foreach (var (username, name) in new[] { ("ann", "Ann"), ("bob", "Bob") })
        {
            ids[username] = (await userService.CreateAsync(username, name, UserRole.Reviewer, Password, Actor.CommandLine, TestContext.Current.CancellationToken)).Id;
        }

        var projects = new ProjectService(reviews, clock);
        var project = await projects.CreateAsync("Apollo", "Moon", TestActors.Ann, TestContext.Current.CancellationToken);
        var reviewService = new ReviewService(reviews, clock);
        await reviewService.CreateDraftAsync(
            project.Id,
            "rev-1",
            "First",
            [new("a.txt", Encoding.UTF8.GetBytes("one\ntwo\nthree\n"))],
            TestActors.Ann,
            TestContext.Current.CancellationToken);
        return new Fixture(
            reviews,
            users,
            trail,
            clock,
            userService,
            projects,
            reviewService,
            new ThreadService(reviews, clock),
            new ChecklistService(reviews, clock),
            new SigningService(reviews, userService, clock),
            project.Id,
            ids);
    }

    /// <summary>
    ///     Test that creating a project records the action, the project and the actor.
    /// </summary>
    [Fact]
    public async Task ProjectService_CreateAsync_Project_RecordsProjectCreated()
    {
        // Arrange / Act
        var fixture = await CreateAsync();

        // Assert
        var draft = fixture.Reviews.Audits[0];
        Assert.Equal("project.created", draft.Action);
        Assert.Equal("project 'Apollo'", draft.Detail);
        Assert.Equal(fixture.ProjectId, draft.ProjectId);
        Assert.Null(draft.ReviewId);
        Assert.Equal("Ann (ann)", draft.ActorName);
    }

    /// <summary>
    ///     Test that creating a review records the fingerprint and the file count.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_Review_RecordsReviewCreatedWithFingerprint()
    {
        // Arrange / Act
        var fixture = await CreateAsync();

        // Assert
        var draft = fixture.Reviews.Audits[1];
        var review = await fixture.ReviewService.GetAsync(fixture.ProjectId, "rev-1", TestContext.Current.CancellationToken);
        Assert.Equal("review.created", draft.Action);
        Assert.Equal($"review 'First', fingerprint {review!.ContentFingerprint}, 1 file", draft.Detail);
        Assert.Equal(64, review.ContentFingerprint.Length);
        Assert.Equal((fixture.ProjectId, "rev-1"), (draft.ProjectId, draft.ReviewId));
    }

    /// <summary>
    ///     Test that a review of several files says so in the plural.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_SeveralFiles_RecordsPluralCount()
    {
        // Arrange
        var fixture = await CreateAsync();

        // Act
        await fixture.ReviewService.CreateDraftAsync(
            fixture.ProjectId,
            "rev-2",
            "Second",
            [new("a.txt", TestContent.A), new("b.txt", TestContent.B)],
            TestActors.Bob,
            TestContext.Current.CancellationToken);

        // Assert
        var draft = fixture.Reviews.Audits[^1];
        Assert.EndsWith(", 2 files", draft.Detail, StringComparison.Ordinal);
        Assert.Equal("Bob (bob)", draft.ActorName);
    }

    /// <summary>
    ///     Test that thread events record ids and anchors, a short digest of the text, and never the text.
    /// </summary>
    [Fact]
    public async Task ThreadService_ThreadEvents_RecordIdsAnchorsAndDigestButNotText()
    {
        // Arrange
        var fixture = await CreateAsync();

        // Act
        var thread = await fixture.Threads.StartThreadAsync(fixture.ProjectId, "rev-1", "a.txt", 2, 3, TestActors.Ann, SecretComment, TestContext.Current.CancellationToken);
        var whole = await fixture.Threads.StartThreadAsync(fixture.ProjectId, "rev-1", "a.txt", null, null, TestActors.Ann, "whole", TestContext.Current.CancellationToken);
        var single = await fixture.Threads.StartThreadAsync(fixture.ProjectId, "rev-1", "a.txt", 1, 1, TestActors.Ann, "single", TestContext.Current.CancellationToken);
        var reply = await fixture.Threads.ReplyAsync(fixture.ProjectId, "rev-1", thread.Id, TestActors.Bob, SecretComment, TestContext.Current.CancellationToken);
        await fixture.Threads.ResolveAsync(fixture.ProjectId, "rev-1", thread.Id, TestActors.Bob, TestContext.Current.CancellationToken);
        await fixture.Threads.ReopenAsync(fixture.ProjectId, "rev-1", thread.Id, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert
        var drafts = fixture.Reviews.Audits.Skip(2).ToList();
        var digest = AuditText.ShortHash(SecretComment);
        Assert.Equal(
            ["thread.started", "thread.started", "thread.started", "thread.replied", "thread.resolved", "thread.reopened"],
            drafts.Select(d => d.Action));
        Assert.Equal($"thread {thread.Id} on a.txt lines 2-3; comment {thread.Comments[0].Id}, text sha256 {digest}", drafts[0].Detail);
        Assert.Equal($"thread {whole.Id} on a.txt (whole file); comment {whole.Comments[0].Id}, text sha256 {AuditText.ShortHash("whole")}", drafts[1].Detail);
        Assert.Equal($"thread {single.Id} on a.txt line 1; comment {single.Comments[0].Id}, text sha256 {AuditText.ShortHash("single")}", drafts[2].Detail);
        Assert.Equal($"thread {thread.Id}, comment {reply.Id}, text sha256 {digest}", drafts[3].Detail);
        Assert.Equal($"thread {thread.Id}", drafts[4].Detail);
        Assert.Equal($"thread {thread.Id}", drafts[5].Detail);
        Assert.Equal(["Ann (ann)", "Ann (ann)", "Ann (ann)", "Bob (bob)", "Bob (bob)", "Ann (ann)"], drafts.Select(d => d.ActorName));
        Assert.All(drafts, d => Assert.Equal((fixture.ProjectId, "rev-1"), (d.ProjectId, d.ReviewId)));
        Assert.All(drafts, d => Assert.DoesNotContain("hunter2", d.Detail, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that resolving a thread that does not exist records nothing.
    /// </summary>
    [Fact]
    public async Task ThreadService_ResolveAsync_MissingThread_RecordsNothing()
    {
        // Arrange
        var fixture = await CreateAsync();
        var before = fixture.Reviews.Audits.Count;

        // Act
        await Assert.ThrowsAsync<DomainRuleException>(
            () => fixture.Threads.ResolveAsync(fixture.ProjectId, "rev-1", 99, TestActors.Ann, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(before, fixture.Reviews.Audits.Count);
    }

    /// <summary>
    ///     Test that checklist events record the item and outcome, and a digest of the justification, not the text.
    /// </summary>
    [Fact]
    public async Task ChecklistService_ChecklistEvents_RecordOutcomeAndDigestButNotJustification()
    {
        // Arrange
        var fixture = await CreateAsync();
        var firstItem = DefaultChecklist.Current.Sections[0].Items[0].Id;
        var sectionId = DefaultChecklist.Current.Sections[1].Id;
        const string Justification = "Confidential reason hunter2";

        // Act
        await fixture.Checklists.SetAnswerAsync(fixture.ProjectId, "rev-1", firstItem, ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        await fixture.Checklists.SetAnswerAsync(fixture.ProjectId, "rev-1", firstItem, ChecklistOutcome.Fail, Justification, TestActors.Bob, TestContext.Current.CancellationToken);
        await fixture.Checklists.SetSectionApplicabilityAsync(fixture.ProjectId, "rev-1", sectionId, false, "Not relevant", TestActors.Ann, TestContext.Current.CancellationToken);
        await fixture.Checklists.SetSectionApplicabilityAsync(fixture.ProjectId, "rev-1", sectionId, true, null, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert
        var drafts = fixture.Reviews.Audits.Skip(2).ToList();
        Assert.Equal(["checklist.answered", "checklist.answered", "checklist.applicability-changed", "checklist.applicability-changed"], drafts.Select(d => d.Action));
        Assert.Equal($"item {firstItem}: Pass", drafts[0].Detail);
        Assert.Equal($"item {firstItem}: Fail; justification sha256 {AuditText.ShortHash(Justification)}", drafts[1].Detail);
        Assert.Equal($"section {sectionId}: not applicable, reason sha256 {AuditText.ShortHash("Not relevant")}", drafts[2].Detail);
        Assert.Equal($"section {sectionId}: applicable", drafts[3].Detail);
        Assert.All(drafts, d => Assert.DoesNotContain("hunter2", d.Detail, StringComparison.Ordinal));
        Assert.All(drafts, d => Assert.Equal((fixture.ProjectId, "rev-1"), (d.ProjectId, d.ReviewId)));
    }

    /// <summary>
    ///     Test that slot changes and the signature are recorded, the signature with the record digest.
    /// </summary>
    [Fact]
    public async Task SigningService_SlotAndSignatureEvents_AreRecordedWithMembersAndDigest()
    {
        // Arrange
        var fixture = await CreateAsync();
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(s => s.Items))
        {
            await fixture.Checklists.SetAnswerAsync(fixture.ProjectId, "rev-1", item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        }

        var before = fixture.Reviews.Audits.Count;

        // Act
        var slot = await fixture.Signing.AddSlotAsync(TestActors.Ann, fixture.ProjectId, "rev-1", fixture.UserIds["ann"], fixture.UserIds["bob"], TestContext.Current.CancellationToken);
        await fixture.Signing.UpdateSlotAsync(TestActors.Bob, fixture.ProjectId, "rev-1", slot.Id, fixture.UserIds["bob"], null, TestContext.Current.CancellationToken);
        await fixture.Signing.UpdateSlotAsync(TestActors.Bob, fixture.ProjectId, "rev-1", slot.Id, fixture.UserIds["ann"], fixture.UserIds["bob"], TestContext.Current.CancellationToken);
        var digest = (await fixture.Signing.GetApprovalAsync(fixture.ProjectId, "rev-1", TestContext.Current.CancellationToken))!.Digest;
        await fixture.Signing.SignAsync(TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken);
        var second = await fixture.Signing.AddSlotAsync(TestActors.Ann, fixture.ProjectId, "rev-1", 0 + fixture.UserIds["bob"] + 100, null, TestContext.Current.CancellationToken).ContinueWith(
            t => t.IsFaulted, TaskScheduler.Default);

        // Assert: the failed add of an unknown user records nothing
        Assert.True(second);
        var drafts = fixture.Reviews.Audits.Skip(before).ToList();
        Assert.Equal(["slot.added", "slot.updated", "slot.updated", "signature.added"], drafts.Select(d => d.Action));
        Assert.Equal($"slot {slot.Id}: primary ann, alternate bob", drafts[0].Detail);
        Assert.Equal($"slot {slot.Id}: primary bob, alternate none", drafts[1].Detail);
        Assert.Equal($"slot {slot.Id}: primary ann, alternate bob", drafts[2].Detail);
        Assert.Equal($"slot {slot.Id}, role Primary, record digest {digest}", drafts[3].Detail);
        Assert.Equal("Ann (ann)", drafts[3].ActorName);
        Assert.DoesNotContain(drafts, d => d.Detail.Contains(Password, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that removing a slot records who was in it.
    /// </summary>
    [Fact]
    public async Task SigningService_RemoveSlotAsync_RecordsSlotRemovedWithItsMembers()
    {
        // Arrange
        var fixture = await CreateAsync();
        var slot = await fixture.Signing.AddSlotAsync(TestActors.Ann, fixture.ProjectId, "rev-1", fixture.UserIds["ann"], null, TestContext.Current.CancellationToken);

        // Act
        await fixture.Signing.RemoveSlotAsync(TestActors.Bob, fixture.ProjectId, "rev-1", slot.Id, TestContext.Current.CancellationToken);

        // Assert
        var draft = fixture.Reviews.Audits[^1];
        Assert.Equal("slot.removed", draft.Action);
        Assert.Equal($"slot {slot.Id}: primary ann, alternate none", draft.Detail);
        Assert.Equal("Bob (bob)", draft.ActorName);
    }

    /// <summary>
    ///     Test that every user administration event records the action, the target and the acting administrator.
    /// </summary>
    [Fact]
    public async Task UserService_AdministrationEvents_AreRecordedWithoutPasswords()
    {
        // Arrange
        var fixture = await CreateAsync();
        var admin = new Actor("root", "Root");
        await fixture.UserService.CreateAsync("dana", "Dana", UserRole.Admin, Password, admin, TestContext.Current.CancellationToken);
        fixture.Users.Audits.Clear();

        // Act
        await fixture.UserService.CreateAsync("carl", "Carl", UserRole.Reviewer, Password, admin, TestContext.Current.CancellationToken);
        await fixture.UserService.SetRoleAsync("carl", UserRole.Admin, admin, TestContext.Current.CancellationToken);
        await fixture.UserService.SetEnabledAsync("carl", false, admin, TestContext.Current.CancellationToken);
        await fixture.UserService.SetEnabledAsync("carl", true, admin, TestContext.Current.CancellationToken);
        await fixture.UserService.SetPasswordAsync("carl", "a brand new password", admin, TestContext.Current.CancellationToken);
        await fixture.UserService.ChangeOwnPasswordAsync("carl", "a brand new password", "yet another password", TestContext.Current.CancellationToken);

        // Assert
        var drafts = fixture.Users.Audits.ToList();
        Assert.Equal(
            ["user.created", "user.role-changed", "user.disabled", "user.enabled", "user.password-set-by-admin", "user.password-changed"],
            drafts.Select(d => d.Action));
        Assert.Equal("user 'carl', role Reviewer", drafts[0].Detail);
        Assert.Equal("user 'carl': Reviewer -> Admin", drafts[1].Detail);
        Assert.Equal("user 'carl'", drafts[2].Detail);
        Assert.Equal("user 'carl'", drafts[3].Detail);
        Assert.Equal("user 'carl'", drafts[4].Detail);
        Assert.Equal("user 'carl'", drafts[5].Detail);
        Assert.Equal("Root (root)", drafts[0].ActorName);
        Assert.Equal("Carl (carl)", drafts[5].ActorName);
        Assert.All(drafts, d => Assert.DoesNotContain("password ", d.Detail.Replace("password-", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(drafts, d => d.Detail.Contains("brand new", StringComparison.Ordinal) || d.Detail.Contains("hash:", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a user created from the command line is recorded as the command line.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_CommandLineActor_IsRecordedAsCommandLine()
    {
        // Arrange / Act
        var fixture = await CreateAsync();

        // Assert
        Assert.Equal("command line", fixture.Users.Audits[0].ActorName);
        Assert.Equal("user.created", fixture.Users.Audits[0].Action);
    }

    /// <summary>
    ///     Test that a failure counter update is not an audit event of its own.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_WrongPassword_CounterUpdateCarriesNoDraft()
    {
        // Arrange
        var fixture = await CreateAsync();
        var before = fixture.Users.Audits.Count;

        // Act
        await fixture.UserService.AuthenticateAsync("ann", "wrong password!", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(before, fixture.Users.Audits.Count);
    }

    /// <summary>
    ///     Test that an unknown user is recorded with the typed name, made one line and cut to the user name
    ///     length limit, and never with the password.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_UnknownUser_RecordsCleanedCappedTypedNameWithoutPassword()
    {
        // Arrange
        var fixture = await CreateAsync();
        var typed = "mallory\r\nfake line\u0007" + new string('x', 200);

        // Act
        var user = await fixture.UserService.AuthenticateAsync(typed, "typed-secret-password", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(user);
        var entry = Assert.Single(fixture.Trail.Entries);
        Assert.Equal("auth.sign-in-failed", entry.Action);
        Assert.Equal("unknown user", entry.ActorName);
        Assert.Null(entry.ActorUserId);
        Assert.StartsWith("unknown user, name 'mallory  fake line ", entry.Detail, StringComparison.Ordinal);
        Assert.EndsWith("...'", entry.Detail, StringComparison.Ordinal);
        Assert.Equal("unknown user, name '".Length + ReviewLimits.MaxUsernameLength + 1, entry.Detail.Length);
        Assert.DoesNotContain('\n', entry.Detail);
        Assert.DoesNotContain("typed-secret", entry.ActorName + entry.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that repeated attempts with the same unknown name are recorded once per window, then once more with the count.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_UnknownUserRepeated_RecordsOneEntryPerWindow()
    {
        // Arrange
        var fixture = await CreateAsync();
        for (var i = 0; i < 4; i++)
        {
            await fixture.UserService.AuthenticateAsync(i == 3 ? "MALLORY" : "mallory", "pw-" + i, TestContext.Current.CancellationToken);
        }

        // Act
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.UserService.AuthenticateAsync("mallory", "again", TestContext.Current.CancellationToken);
        await fixture.UserService.AuthenticateAsync("other", "again", TestContext.Current.CancellationToken);

        // Assert: name case does not matter for the window; another name has its own
        Assert.Equal(
            [
                "unknown user, name 'mallory'",
                "unknown user, name 'mallory'; 3 more in the minute after the previous entry",
                "unknown user, name 'other'"
            ],
            fixture.Trail.Entries.Select(e => e.Detail).ToArray());
    }

    /// <summary>
    ///     Test that attempts on a locked account are recorded once per window and account, even with the right password.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_LockedAccount_RecordsSignInLockedOncePerWindow()
    {
        // Arrange
        var fixture = await CreateAsync();
        for (var i = 0; i < 5; i++)
        {
            await fixture.UserService.AuthenticateAsync("ann", "wrong-" + i, TestContext.Current.CancellationToken);
        }

        // Act
        var right = await fixture.UserService.AuthenticateAsync("ann", Password, TestContext.Current.CancellationToken);
        await fixture.UserService.AuthenticateAsync("ann", "wrong again", TestContext.Current.CancellationToken);
        await fixture.UserService.AuthenticateAsync("bob", "wrong-bob", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(right);
        var locked = Assert.Single(fixture.Trail.Entries, e => e.Action == "auth.sign-in-locked");
        Assert.Equal("Ann (ann)", locked.ActorName);
        Assert.Equal("account locked", locked.Detail);
        Assert.DoesNotContain(fixture.Trail.Entries, e => (e.Detail + e.ActorName).Contains("wrong", StringComparison.Ordinal) && e.Action != "auth.sign-in-failed");
        Assert.Equal(1, fixture.Trail.Entries.Count(e => e.Action == "auth.sign-in-failed" && e.ActorName == "Bob (bob)"));
    }

    /// <summary>
    ///     Test that a wrong password and a refusal on a locked account during re-authentication are recorded as
    ///     a failed signature re-authentication, not as a failed sign-in.
    /// </summary>
    [Fact]
    public async Task UserService_ReauthenticateAsync_WrongPasswordAndLockedAccount_RecordSignatureReauthFailed()
    {
        // Arrange
        var fixture = await CreateAsync();

        // Act
        var wrong = await fixture.UserService.ReauthenticateAsync("ann", "typed-wrong-password", TestContext.Current.CancellationToken);
        for (var i = 0; i < 4; i++)
        {
            await fixture.UserService.ReauthenticateAsync("ann", "wrong-" + i, TestContext.Current.CancellationToken);
        }

        var locked = await fixture.UserService.ReauthenticateAsync("ann", Password, TestContext.Current.CancellationToken);
        var unknown = await fixture.UserService.ReauthenticateAsync("zed", "typed-wrong-password", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(wrong);
        Assert.Null(locked);
        Assert.Null(unknown);
        var entries = fixture.Trail.Entries;
        Assert.DoesNotContain(entries, e => e.Action is "auth.sign-in-failed" or "auth.sign-in-locked");
        Assert.Equal(5, entries.Count(e => e.Action == "signature.reauth-failed" && e.Detail == "wrong password"));
        Assert.Single(entries, e => e.Action == "signature.reauth-failed" && e.Detail == "account locked" && e.ActorName == "Ann (ann)");
        Assert.Single(entries, e => e.Action == "signature.reauth-failed" && e.Detail == "unknown user, name 'zed'");
        Assert.Single(entries, e => e.Action == "auth.locked-out");
        Assert.DoesNotContain(entries, e => (e.Detail + e.ActorName).Contains("typed-wrong", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a signature attempt with a wrong password and then on the locked account is recorded without the password.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_WrongPasswordThenLocked_RecordsReauthFailedEntriesWithoutPassword()
    {
        // Arrange: a review that may be signed, and four wrong passwords already given
        var fixture = await CreateAsync();
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(s => s.Items))
        {
            await fixture.Checklists.SetAnswerAsync(fixture.ProjectId, "rev-1", item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        }

        var slot = await fixture.Signing.AddSlotAsync(TestActors.Ann, fixture.ProjectId, "rev-1", fixture.UserIds["ann"], null, TestContext.Current.CancellationToken);
        var digest = (await fixture.Signing.GetApprovalAsync(fixture.ProjectId, "rev-1", TestContext.Current.CancellationToken))!.Digest;
        for (var i = 0; i < 4; i++)
        {
            await fixture.UserService.ReauthenticateAsync("ann", "wrong-" + i, TestContext.Current.CancellationToken);
        }

        // Act: the fifth wrong password locks the account, the next attempt meets the lock
        var firstFailure = await Record.ExceptionAsync(() => fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, "typed-wrong-password", TestContext.Current.CancellationToken));
        var secondFailure = await Record.ExceptionAsync(() => fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken));

        // Assert: both refused as a failed re-authentication, and recorded without any password
        Assert.Equal(DomainRule.ReauthenticationFailed, Assert.IsType<DomainRuleException>(firstFailure).Rule);
        Assert.Equal(DomainRule.ReauthenticationFailed, Assert.IsType<DomainRuleException>(secondFailure).Rule);
        var entries = fixture.Trail.Entries;
        Assert.Equal(5, entries.Count(e => e.Action == "signature.reauth-failed" && e.Detail == "wrong password"));
        Assert.Single(entries, e => e.Action == "signature.reauth-failed" && e.Detail == "account locked");
        Assert.DoesNotContain(entries, e => (e.Detail + e.ActorName).Contains("typed-wrong", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a wrong password for an existing user records that user and never the password.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_WrongPassword_RecordsExistingUserWithoutPassword()
    {
        // Arrange
        var fixture = await CreateAsync();

        // Act
        await fixture.UserService.AuthenticateAsync("ANN", "typed-wrong-password", TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(fixture.Trail.Entries);
        Assert.Equal("auth.sign-in-failed", entry.Action);
        Assert.Equal("Ann (ann)", entry.ActorName);
        Assert.Equal("wrong password", entry.Detail);
        Assert.DoesNotContain("typed-wrong-password", entry.ActorName + entry.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a disabled account with the right password is recorded as such.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_DisabledAccount_RecordsAccountDisabled()
    {
        // Arrange
        var fixture = await CreateAsync();
        await fixture.UserService.SetEnabledAsync("bob", false, Actor.CommandLine, TestContext.Current.CancellationToken);

        // Act
        await fixture.UserService.AuthenticateAsync("bob", Password, TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(fixture.Trail.Entries);
        Assert.Equal("auth.sign-in-failed", entry.Action);
        Assert.Equal("account disabled", entry.Detail);
        Assert.DoesNotContain(Password, entry.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that repeated sign-ins on a disabled account are recorded once per window and account, with the count carried to the next window.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_DisabledAccountRepeated_RecordsOneEntryPerWindow()
    {
        // Arrange
        var fixture = await CreateAsync();
        await fixture.UserService.SetEnabledAsync("bob", false, Actor.CommandLine, TestContext.Current.CancellationToken);
        await fixture.UserService.SetEnabledAsync("ann", false, Actor.CommandLine, TestContext.Current.CancellationToken);

        // Act: four attempts on bob (one in upper case) and one on ann, then one more on bob after the window
        foreach (var name in new[] { "bob", "BOB", "bob", "bob", "ann" })
        {
            await fixture.UserService.AuthenticateAsync(name, Password, TestContext.Current.CancellationToken);
        }

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.UserService.AuthenticateAsync("bob", Password, TestContext.Current.CancellationToken);

        // Assert
        Assert.All(fixture.Trail.Entries, e => Assert.Equal("auth.sign-in-failed", e.Action));
        Assert.Equal(
            [
                "Bob (bob): account disabled",
                "Ann (ann): account disabled",
                "Bob (bob): account disabled; 3 more in the minute after the previous entry"
            ],
            fixture.Trail.Entries.Select(e => $"{e.ActorName}: {e.Detail}").ToArray());
    }

    /// <summary>
    ///     Test that repeated re-authentications on a disabled account are recorded once per window as a failed signature re-authentication.
    /// </summary>
    [Fact]
    public async Task UserService_ReauthenticateAsync_DisabledAccountRepeated_RecordsOneSignatureReauthFailedPerWindow()
    {
        // Arrange
        var fixture = await CreateAsync();
        await fixture.UserService.SetEnabledAsync("bob", false, Actor.CommandLine, TestContext.Current.CancellationToken);

        // Act
        for (var i = 0; i < 4; i++)
        {
            await fixture.UserService.ReauthenticateAsync("bob", Password, TestContext.Current.CancellationToken);
        }

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.UserService.ReauthenticateAsync("bob", Password, TestContext.Current.CancellationToken);

        // Assert
        Assert.All(fixture.Trail.Entries, e => Assert.Equal("signature.reauth-failed", e.Action));
        Assert.Equal(
            ["account disabled", "account disabled; 3 more in the minute after the previous entry"],
            fixture.Trail.Entries.Select(e => e.Detail).ToArray());
    }

    /// <summary>
    ///     Test that a disabled reason and a locked reason of one account in one window are both recorded, because the reason is part of the key.
    /// </summary>
    [Fact]
    public async Task UserService_ReauthenticateAsync_DisabledThenLockedInOneMinute_RecordsBothReasons()
    {
        // Arrange: bob is disabled and a re-authentication with the right password is refused
        var fixture = await CreateAsync();
        await fixture.UserService.SetEnabledAsync("bob", false, Actor.CommandLine, TestContext.Current.CancellationToken);
        await fixture.UserService.ReauthenticateAsync("bob", Password, TestContext.Current.CancellationToken);

        // Act: five wrong passwords lock the account, then the right password meets the lock
        for (var i = 0; i < 5; i++)
        {
            await fixture.UserService.ReauthenticateAsync("bob", "wrong-" + i, TestContext.Current.CancellationToken);
        }

        await fixture.UserService.ReauthenticateAsync("bob", Password, TestContext.Current.CancellationToken);

        // Assert
        var entries = fixture.Trail.Entries.Where(e => e.Action == "signature.reauth-failed").ToArray();
        Assert.Single(entries, e => e.Detail == "account disabled");
        Assert.Single(entries, e => e.Detail == "account locked");
        Assert.Equal(5, entries.Count(e => e.Detail == "wrong password"));
    }

    /// <summary>
    ///     Test that the fifth failure records the lockout once.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_FifthFailure_RecordsLockoutOnce()
    {
        // Arrange
        var fixture = await CreateAsync();

        // Act
        for (var i = 0; i < 5; i++)
        {
            await fixture.UserService.AuthenticateAsync("ann", "wrong-" + i, TestContext.Current.CancellationToken);
        }

        // Assert
        var entries = fixture.Trail.Entries;
        Assert.Equal(5, entries.Count(e => e.Action == "auth.sign-in-failed"));
        var lockout = Assert.Single(entries, e => e.Action == "auth.locked-out");
        Assert.Equal("Ann (ann)", lockout.ActorName);
        Assert.Equal("locked for 5 minutes after 5 failed attempts", lockout.Detail);
        Assert.Equal(6, entries.Count);
        Assert.DoesNotContain(entries, e => (e.Detail + e.ActorName).Contains("wrong-", StringComparison.Ordinal));
        Assert.True(AuditChain.Verify(entries).IsIntact);
    }

    /// <summary>
    ///     Test that a failing audit store never changes the outcome of a sign-in check or lets the failure out.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_AuditStoreFails_StillRefusesAndDoesNotThrow()
    {
        // Arrange
        var fixture = await CreateAsync();
        fixture.Trail.FailNextAppend = new InvalidOperationException("disk full");

        // Act
        var wrong = await fixture.UserService.AuthenticateAsync("ann", "not the password", TestContext.Current.CancellationToken);
        var right = await fixture.UserService.AuthenticateAsync("ann", Password, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(wrong);
        Assert.NotNull(right);
        Assert.Empty(fixture.Trail.Entries);
    }

    /// <summary>
    ///     Test that a user service without an audit service works as before.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_NoAuditService_Works()
    {
        // Arrange
        var service = new UserService(new FakeUserStore(), new FakePasswordHasher(), new FixedTimeProvider(_now));
        await service.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, Actor.CommandLine, TestContext.Current.CancellationToken);

        // Act
        var wrong = await service.AuthenticateAsync("ann", "nope nope nope", TestContext.Current.CancellationToken);
        var unknown = await service.AuthenticateAsync("zed", "nope nope nope", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(wrong);
        Assert.Null(unknown);
    }
}
