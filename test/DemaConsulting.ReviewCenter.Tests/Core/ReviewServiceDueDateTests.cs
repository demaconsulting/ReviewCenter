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
///     Unit tests of the optional due date of a review in <see cref="ReviewService"/>: the rule at creation, the
///     change by a project administrator or the creator, the refusals, the overdue rule and the fact that the date
///     is not part of the record digest. The tests use the real project access rules over in-memory stores.
/// </summary>
public class ReviewServiceDueDateTests
{
    /// <summary>
    ///     The password every test user has.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The instant at which the clock stands: 4 March 2025, so today (UTC) is that day.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Today as a UTC calendar day.
    /// </summary>
    private static readonly DateOnly _today = new(2025, 3, 4);

    /// <summary>
    ///     The system administrator.
    /// </summary>
    private static readonly Actor _root = new("root", "Root");

    /// <summary>
    ///     A project administrator of the project.
    /// </summary>
    private static readonly Actor _projectAdmin = new("padm", "Padm");

    /// <summary>
    ///     The member who creates the review.
    /// </summary>
    private static readonly Actor _creator = new("creator", "Creator");

    /// <summary>
    ///     Another member, who neither administers the project nor created the review.
    /// </summary>
    private static readonly Actor _member = new("member", "Member");

    /// <summary>
    ///     The reviewer of the first slot.
    /// </summary>
    private static readonly Actor _reviewerOne = new("reviewer1", "Reviewer One");

    /// <summary>
    ///     The reviewer of the second slot.
    /// </summary>
    private static readonly Actor _reviewerTwo = new("reviewer2", "Reviewer Two");

    /// <summary>
    ///     The arranged world.
    /// </summary>
    /// <param name="Store">The review store.</param>
    /// <param name="Reviews">The review service over the real access rules.</param>
    /// <param name="Signing">The signing service over the same rules.</param>
    /// <param name="Checklists">The checklist service.</param>
    /// <param name="ProjectId">The project.</param>
    /// <param name="Ids">The identities of the users, by user name.</param>
    private sealed record World(
        FakeReviewStore Store,
        ReviewService Reviews,
        SigningService Signing,
        ChecklistService Checklists,
        int ProjectId,
        Dictionary<string, int> Ids);

    /// <summary>
    ///     Arranges the users, the project and the services; no review exists yet.
    /// </summary>
    /// <returns>The world.</returns>
    private static async Task<World> WorldAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FixedTimeProvider(_now);
        var store = new FakeReviewStore();
        var userStore = new FakeUserStore(store);
        var ids = new Dictionary<string, int>();
        foreach (var (name, admin) in new[]
                 {
                     ("root", true), ("padm", false), ("creator", false), ("member", false),
                     ("reviewer1", false), ("reviewer2", false)
                 })
        {
            var stored = await userStore.AddAsync(
                new User
                {
                    Username = name,
                    DisplayName = name,
                    IsSystemAdmin = admin,
                    PasswordHash = "hash:" + Password,
                    Created = _now
                },
                created => AuditDraft.By(Actor.CommandLine, "user.created", created.Username),
                cancellationToken);
            ids[name] = stored.Id;
        }

        var access = new ProjectAccess(userStore, store);
        var projects = new ProjectService(store, userStore, access, new FakePasswordHasher(), clock);
        var project = await projects.CreateAsync("Own", null, _root, cancellationToken);
        await store.AddMembershipAsync(ids["padm"], project.Id, ProjectRole.ProjectAdmin, cancellationToken);
        foreach (var name in new[] { "creator", "member", "reviewer1", "reviewer2" })
        {
            await store.AddMembershipAsync(ids[name], project.Id, ProjectRole.User, cancellationToken);
        }

        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        return new World(
            store,
            new ReviewService(store, access, clock),
            new SigningService(store, users, access, clock),
            new ChecklistService(store, access, userStore, clock),
            project.Id,
            ids);
    }

    /// <summary>
    ///     Creates the review <c>rev-1</c> as the creator, with an optional due date.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="dueDate">The due date, or <c>null</c>.</param>
    /// <returns>The created review.</returns>
    private static Task<Review> CreateReviewAsync(World world, DateOnly? dueDate = null)
    {
        return world.Reviews.CreateDraftAsync(
            world.ProjectId,
            "rev-1",
            "First",
            [new UploadedFile("a.txt", TestContent.A)],
            _creator,
            TestContext.Current.CancellationToken,
            dueDate: dueDate);
    }

    /// <summary>
    ///     Answers the checklist of <c>rev-1</c>, adds two slots and signs the first one, so that the review is
    ///     one signature short of approval.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <returns>The identities of the two slots.</returns>
    private static async Task<(int First, int Second)> SignFirstSlotAsync(World world)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await world.Signing.AddSlotAsync(
            _projectAdmin, world.ProjectId, "rev-1", world.Ids["reviewer1"], null, cancellationToken);
        var second = await world.Signing.AddSlotAsync(
            _projectAdmin, world.ProjectId, "rev-1", world.Ids["reviewer2"], null, cancellationToken);

        // Each reviewer answers the checklist in the own ballot and hands it in
        foreach (var reviewer in new[] { _reviewerOne, _reviewerTwo })
        {
            await AuditTestExtensions.AnswerAllAsync(world.Checklists, world.ProjectId, "rev-1", reviewer, cancellationToken);
            await world.Checklists.SubmitBallotAsync(world.ProjectId, "rev-1", reviewer, cancellationToken);
        }
        var digest = (await world.Signing.GetApprovalAsync(world.ProjectId, "rev-1", cancellationToken))!.Digest;
        await world.Signing.SignAsync(_reviewerOne, world.ProjectId, "rev-1", first.Id, digest, Password, cancellationToken);
        return (first.Id, second.Id);
    }

    /// <summary>
    ///     Test that a due date from today up to two years ahead is stored with a new review and named in the
    ///     audit detail of the creation.
    /// </summary>
    /// <param name="offsetDays">Days from today; the last case is exactly two years.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(730)]
    public async Task ReviewService_CreateDraftAsync_DueDateInRange_StoresDateAndAuditsIt(int offsetDays)
    {
        // Arrange: a project; the date is today plus the offset (two years ahead for 730 days of the 2025 calendar)
        var world = await WorldAsync();
        var due = offsetDays == 730 ? _today.AddYears(2) : _today.AddDays(offsetDays);

        // Act: create the review with that due date
        var review = await CreateReviewAsync(world, due);

        // Assert: stored with the review, and the creation audit entry names the date
        Assert.Equal(due, review.DueDate);
        Assert.Equal(due, (await world.Reviews.GetAsync(world.ProjectId, "rev-1", TestContext.Current.CancellationToken))!.DueDate);
        var audit = Assert.Single(world.Store.Audits, a => a.Action == "review.created");
        Assert.Contains($"due {due:yyyy-MM-dd}", audit.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a review without a due date has none and its creation audit detail does not mention one.
    /// </summary>
    [Fact]
    public async Task ReviewService_CreateDraftAsync_NoDueDate_HasNoDateAndNoAuditText()
    {
        // Arrange and act: create a review without a due date
        var world = await WorldAsync();
        var review = await CreateReviewAsync(world);

        // Assert: no date, no text
        Assert.Null(review.DueDate);
        var audit = Assert.Single(world.Store.Audits, a => a.Action == "review.created");
        Assert.DoesNotContain("due ", audit.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a due date in the past or more than two years ahead is refused at creation and nothing is stored.
    /// </summary>
    /// <param name="tooFarAhead"><c>true</c> to test the day after the two-year limit, <c>false</c> for yesterday.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewService_CreateDraftAsync_DueDateOutOfRange_IsRefusedAndStoresNothing(bool tooFarAhead)
    {
        // Arrange: a project; yesterday, or the day after the limit
        var world = await WorldAsync();
        var due = tooFarAhead ? _today.AddYears(2).AddDays(1) : _today.AddDays(-1);

        // Act: create the review with that date
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => CreateReviewAsync(world, due));

        // Assert: refused with the due date rule, and neither a review nor an audit entry exists
        Assert.Equal(DomainRule.DueDateInvalid, ex.Rule);
        Assert.Empty(world.Store.Reviews);
        Assert.DoesNotContain(world.Store.Audits, a => a.Action == "review.created");
    }

    /// <summary>
    ///     Test that a project administrator can set and change the due date, a past date is allowed on change, and
    ///     each change is audited with the old and the new value.
    /// </summary>
    [Fact]
    public async Task ReviewService_SetDueDateAsync_ProjectAdmin_ChangesAndAuditsOldAndNew()
    {
        // Arrange: a review without a date
        var world = await WorldAsync();
        await CreateReviewAsync(world);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: set a date, then change it to a date in the past
        var set = await world.Reviews.SetDueDateAsync(world.ProjectId, "rev-1", new DateOnly(2025, 4, 1), _projectAdmin, cancellationToken);
        var changed = await world.Reviews.SetDueDateAsync(world.ProjectId, "rev-1", new DateOnly(2025, 3, 1), _projectAdmin, cancellationToken);

        // Assert: both changed the value, and the audit trail names who, the old and the new value
        Assert.True(set);
        Assert.True(changed);
        Assert.Equal(new DateOnly(2025, 3, 1), (await world.Reviews.GetAsync(world.ProjectId, "rev-1", cancellationToken))!.DueDate);
        var audits = world.Store.Audits.Where(a => a.Action == "review.due-date-changed").ToList();
        Assert.Equal(2, audits.Count);
        Assert.Equal("none -> 2025-04-01", audits[0].Detail);
        Assert.Equal("2025-04-01 -> 2025-03-01", audits[1].Detail);
        Assert.Equal("padm", audits[0].ActorUsername);
    }

    /// <summary>
    ///     Test that the creator of the review and a system administrator can change the due date.
    /// </summary>
    [Fact]
    public async Task ReviewService_SetDueDateAsync_CreatorAndSystemAdmin_AreAllowed()
    {
        // Arrange: a review created by the creator
        var world = await WorldAsync();
        await CreateReviewAsync(world);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: the creator sets a date, the system administrator changes it
        var byCreator = await world.Reviews.SetDueDateAsync(world.ProjectId, "rev-1", new DateOnly(2025, 5, 1), _creator, cancellationToken);
        var byRoot = await world.Reviews.SetDueDateAsync(world.ProjectId, "rev-1", new DateOnly(2025, 6, 1), _root, cancellationToken);

        // Assert: both are allowed
        Assert.True(byCreator);
        Assert.True(byRoot);
        Assert.Equal(new DateOnly(2025, 6, 1), (await world.Reviews.GetAsync(world.ProjectId, "rev-1", cancellationToken))!.DueDate);
    }

    /// <summary>
    ///     Test that a member who neither administers the project nor created the review is refused, and so is a
    ///     person who may not see the project; nothing changes and nothing is audited.
    /// </summary>
    [Fact]
    public async Task ReviewService_SetDueDateAsync_OtherMemberOrOutsider_IsRefusedAndChangesNothing()
    {
        // Arrange: a review with a date
        var world = await WorldAsync();
        await CreateReviewAsync(world, _today.AddDays(10));
        var cancellationToken = TestContext.Current.CancellationToken;
        var auditsBefore = world.Store.Audits.Count;

        // Act: another member, and a signed-in user who is no member at all, try to change it
        var member = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Reviews.SetDueDateAsync(world.ProjectId, "rev-1", null, _member, cancellationToken));
        var outsider = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Reviews.SetDueDateAsync(world.ProjectId, "rev-1", null, new Actor("stranger", "Stranger"), cancellationToken));

        // Assert: the member is not allowed, the stranger does not even see the project, and nothing changed
        Assert.Equal(DomainRule.ActorNotAllowed, member.Rule);
        Assert.Equal(DomainRule.ProjectNotFound, outsider.Rule);
        Assert.Equal(_today.AddDays(10), (await world.Reviews.GetAsync(world.ProjectId, "rev-1", cancellationToken))!.DueDate);
        Assert.Equal(auditsBefore, world.Store.Audits.Count);
        Assert.False(await world.Reviews.CanSetDueDateAsync(_member, (await world.Reviews.GetAsync(world.ProjectId, "rev-1", cancellationToken))!, cancellationToken));
    }

    /// <summary>
    ///     Test that a creator who has since become a read-only member (Auditor) can no longer change the due date.
    /// </summary>
    [Fact]
    public async Task ReviewService_SetDueDateAsync_CreatorNowReadOnly_IsRefused()
    {
        // Arrange: a review with a date, whose creator is then made a read-only member
        var world = await WorldAsync();
        await CreateReviewAsync(world, _today.AddDays(10));
        var cancellationToken = TestContext.Current.CancellationToken;
        await world.Store.SetMembershipRoleAsync(
            world.ProjectId, world.Ids["creator"], ProjectRole.Auditor, AuditTestExtensions.Draft(), false, cancellationToken);
        var review = (await world.Reviews.GetAsync(world.ProjectId, "rev-1", cancellationToken))!;

        // Act
        var refused = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Reviews.SetDueDateAsync(world.ProjectId, "rev-1", null, _creator, cancellationToken));

        // Assert: refused, the date is unchanged and the page would not offer the control
        Assert.Equal(DomainRule.ActorNotAllowed, refused.Rule);
        Assert.Equal(_today.AddDays(10), review.DueDate);
        Assert.False(await world.Reviews.CanSetDueDateAsync(_creator, review, cancellationToken));
    }

    /// <summary>
    ///     Test that an actor that came through an API token cannot change the due date, even the administrator or
    ///     creator behind the token.
    /// </summary>
    [Fact]
    public async Task ReviewService_SetDueDateAsync_ApiToken_IsRefused()
    {
        // Arrange: a review, and the creator and the administrator arriving through a token
        var world = await WorldAsync();
        await CreateReviewAsync(world);
        var cancellationToken = TestContext.Current.CancellationToken;
        var creatorViaToken = _creator with { ApiTokenId = "K7M2QX9P" };
        var adminViaToken = _projectAdmin with { ApiTokenId = "K7M2QX9P" };

        // Act: try to set a date with each
        var byCreator = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Reviews.SetDueDateAsync(world.ProjectId, "rev-1", _today.AddDays(3), creatorViaToken, cancellationToken));
        var byAdmin = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Reviews.SetDueDateAsync(world.ProjectId, "rev-1", _today.AddDays(3), adminViaToken, cancellationToken));

        // Assert: both are refused and nothing is stored
        Assert.Equal(DomainRule.ActorNotAllowed, byCreator.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, byAdmin.Rule);
        Assert.Null((await world.Reviews.GetAsync(world.ProjectId, "rev-1", cancellationToken))!.DueDate);
        Assert.DoesNotContain(world.Store.Audits, a => a.Action == "review.due-date-changed");
    }

    /// <summary>
    ///     Test that the due date can be cleared, that the audit text says so, and that setting the value it already
    ///     has changes and records nothing.
    /// </summary>
    [Fact]
    public async Task ReviewService_SetDueDateAsync_ClearAndUnchanged_AuditsOnlyTheChange()
    {
        // Arrange: a review with a date
        var world = await WorldAsync();
        await CreateReviewAsync(world, _today.AddDays(5));
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: set the same date, clear it, clear it again
        var same = await world.Reviews.SetDueDateAsync(world.ProjectId, "rev-1", _today.AddDays(5), _creator, cancellationToken);
        var cleared = await world.Reviews.SetDueDateAsync(world.ProjectId, "rev-1", null, _creator, cancellationToken);
        var again = await world.Reviews.SetDueDateAsync(world.ProjectId, "rev-1", null, _creator, cancellationToken);

        // Assert: only the clearing changed and was audited
        Assert.False(same);
        Assert.True(cleared);
        Assert.False(again);
        Assert.Null((await world.Reviews.GetAsync(world.ProjectId, "rev-1", cancellationToken))!.DueDate);
        var audit = Assert.Single(world.Store.Audits, a => a.Action == "review.due-date-changed");
        Assert.Equal($"{_today.AddDays(5):yyyy-MM-dd} -> none", audit.Detail);
    }

    /// <summary>
    ///     Test that a due date more than two years ahead is refused on change too.
    /// </summary>
    [Fact]
    public async Task ReviewService_SetDueDateAsync_TooFarAhead_IsRefused()
    {
        // Arrange: a review
        var world = await WorldAsync();
        await CreateReviewAsync(world);

        // Act: set a date beyond the limit
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Reviews.SetDueDateAsync(
            world.ProjectId, "rev-1", _today.AddYears(2).AddDays(1), _projectAdmin, TestContext.Current.CancellationToken));

        // Assert: refused with the due date rule
        Assert.Equal(DomainRule.DueDateInvalid, ex.Rule);
    }

    /// <summary>
    ///     Test that a review that does not exist is reported as such.
    /// </summary>
    [Fact]
    public async Task ReviewService_SetDueDateAsync_MissingReview_IsNotFound()
    {
        // Arrange: a project with no review
        var world = await WorldAsync();

        // Act: change the date of a review that does not exist
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Reviews.SetDueDateAsync(
            world.ProjectId, "nope", _today, _projectAdmin, TestContext.Current.CancellationToken));

        // Assert: not found
        Assert.Equal(DomainRule.ReviewNotFound, ex.Rule);
    }

    /// <summary>
    ///     Test that an approved review refuses a change of the due date with the locked rule and keeps its date, and
    ///     that it is never overdue.
    /// </summary>
    [Fact]
    public async Task ReviewService_SetDueDateAsync_ApprovedReview_IsLockedAndNeverOverdue()
    {
        // Arrange: a review whose date has passed, signed by both reviewers
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateReviewAsync(world, _today);
        var (_, second) = await SignFirstSlotAsync(world);
        var digest = (await world.Signing.GetApprovalAsync(world.ProjectId, "rev-1", cancellationToken))!.Digest;
        await world.Signing.SignAsync(_reviewerTwo, world.ProjectId, "rev-1", second, digest, Password, cancellationToken);
        Assert.True((await world.Signing.GetApprovalAsync(world.ProjectId, "rev-1", cancellationToken))!.IsApproved);

        // Act: try to change the date of the approved review
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Reviews.SetDueDateAsync(world.ProjectId, "rev-1", _today.AddDays(9), _projectAdmin, cancellationToken));

        // Assert: locked, the date is kept, nothing audited, and an approved review is not overdue even tomorrow
        Assert.Equal(DomainRule.ReviewLocked, ex.Rule);
        var review = (await world.Reviews.GetAsync(world.ProjectId, "rev-1", cancellationToken))!;
        Assert.Equal(_today, review.DueDate);
        Assert.DoesNotContain(world.Store.Audits, a => a.Action == "review.due-date-changed");
        Assert.False(new ReviewService(world.Store, new AllowAllProjectAccess(world.Store), new FixedTimeProvider(_now.AddDays(3)))
            .IsOverdue(review, isApproved: true));
    }

    /// <summary>
    ///     Test that someone else changing the date at the same moment makes the change fail on the stale value with
    ///     the record-changed rule, and the other change stays.
    /// </summary>
    [Fact]
    public async Task ReviewService_SetDueDateAsync_ChangedByOtherMeanwhile_FailsAsRecordChanged()
    {
        // Arrange: a review without a date; just before the store compares, another change lands
        var world = await WorldAsync();
        await CreateReviewAsync(world);
        var cancellationToken = TestContext.Current.CancellationToken;
        world.Store.BeforeDueDateUpdate = () =>
        {
            world.Store.BeforeDueDateUpdate = null;
            world.Store.UpdateDueDateAsync(
                world.ProjectId,
                "rev-1",
                null,
                _today.AddDays(20),
                AuditDraft.By(_root, ReviewService.DueDateChangedAction, "none -> other", world.ProjectId, "rev-1"),
                cancellationToken).GetAwaiter().GetResult();
        };

        // Act: change the date from the value that was read
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Reviews.SetDueDateAsync(world.ProjectId, "rev-1", _today.AddDays(1), _projectAdmin, cancellationToken));

        // Assert: refused as changed by someone else, and the other change is what is stored
        Assert.Equal(DomainRule.RecordChanged, ex.Rule);
        Assert.Equal(_today.AddDays(20), (await world.Reviews.GetAsync(world.ProjectId, "rev-1", cancellationToken))!.DueDate);
    }

    /// <summary>
    ///     Test that the due date is not part of the record digest: changing it neither changes the digest nor
    ///     invalidates an existing signature.
    /// </summary>
    [Fact]
    public async Task ReviewService_SetDueDateAsync_AfterSignature_KeepsDigestAndSignature()
    {
        // Arrange: a review with one of its two slots signed
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateReviewAsync(world);
        var (first, _) = await SignFirstSlotAsync(world);
        var before = (await world.Signing.GetApprovalAsync(world.ProjectId, "rev-1", cancellationToken))!;

        // Act: set the due date, then clear it
        await world.Reviews.SetDueDateAsync(world.ProjectId, "rev-1", _today.AddDays(30), _projectAdmin, cancellationToken);
        var duringSet = (await world.Signing.GetApprovalAsync(world.ProjectId, "rev-1", cancellationToken))!;
        await world.Reviews.SetDueDateAsync(world.ProjectId, "rev-1", null, _projectAdmin, cancellationToken);
        var afterClear = (await world.Signing.GetApprovalAsync(world.ProjectId, "rev-1", cancellationToken))!;

        // Assert: same digest, and the first slot is still signed
        Assert.Equal(before.Digest, duringSet.Digest);
        Assert.Equal(before.Digest, afterClear.Digest);
        Assert.Equal(SlotSignatureState.Signed, duringSet.Slots.Single(s => s.SlotId == first).State);
        Assert.Equal(SlotSignatureState.Signed, afterClear.Slots.Single(s => s.SlotId == first).State);
    }

    /// <summary>
    ///     Test the overdue rule: only a date before today (UTC) of a review that is not approved is overdue.
    /// </summary>
    [Fact]
    public async Task ReviewService_IsOverdue_PastDateAndNotApproved_IsTheOnlyOverdueCase()
    {
        // Arrange: a service whose clock stands on 4 March 2025 (UTC), and four reviews
        var world = await WorldAsync();
        var past = new Review { ProjectId = world.ProjectId, Id = "a", DueDate = _today.AddDays(-1) };
        var todayDue = new Review { ProjectId = world.ProjectId, Id = "b", DueDate = _today };
        var future = new Review { ProjectId = world.ProjectId, Id = "c", DueDate = _today.AddDays(1) };
        var none = new Review { ProjectId = world.ProjectId, Id = "d" };

        // Act and assert: only the past date of an unapproved review is overdue
        Assert.True(world.Reviews.IsOverdue(past, isApproved: false));
        Assert.False(world.Reviews.IsOverdue(past, isApproved: true));
        Assert.False(world.Reviews.IsOverdue(todayDue, isApproved: false));
        Assert.False(world.Reviews.IsOverdue(future, isApproved: false));
        Assert.False(world.Reviews.IsOverdue(none, isApproved: false));
        Assert.Equal(_today, world.Reviews.TodayUtc);
    }

    /// <summary>
    ///     Test that the text of a due date is read in the ISO form only, and that no text means no date.
    /// </summary>
    [Fact]
    public void ReviewService_ParseDueDate_IsoTextOnly_ReadsDateOrRefuses()
    {
        // Act and assert: nothing, a date and white space
        Assert.Null(ReviewService.ParseDueDate(null));
        Assert.Null(ReviewService.ParseDueDate("  "));
        Assert.Equal(new DateOnly(2025, 12, 31), ReviewService.ParseDueDate(" 2025-12-31 "));

        // Act and assert: other shapes are refused with the due date rule
        foreach (var bad in new[] { "31/12/2025", "2025-13-01", "tomorrow", "2025-12-31T10:00:00" })
        {
            var ex = Assert.Throws<DomainRuleException>(() => ReviewService.ParseDueDate(bad));
            Assert.Equal(DomainRule.DueDateInvalid, ex.Rule);
        }
    }
}
