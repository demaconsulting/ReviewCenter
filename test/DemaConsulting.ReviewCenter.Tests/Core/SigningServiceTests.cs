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
///     Unit tests for <see cref="SigningService"/> over in-memory stores and the real <see cref="UserService"/>:
///     the slot rules, the signing rules, re-authentication and the out-of-date logic.
/// </summary>
public class SigningServiceTests
{
    /// <summary>
    ///     The instant at which each test's clock starts.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     A password that satisfies the password policy; every test user has it.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     User <c>cyd</c>, the person who changes slots in most tests.
    /// </summary>
    private static readonly Actor Cyd = new("cyd", "Cyd");

    /// <summary>
    ///     Creates the services, four users (ann, bob, cy, dee), a project and the review <c>rev-1</c>.
    /// </summary>
    /// <returns>The prepared fixture.</returns>
    private static async Task<Fixture> CreateAsync()
    {
        var store = new FakeReviewStore();
        var clock = new MovableTimeProvider(_now);
        var userStore = new FakeUserStore();
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        var ids = new Dictionary<string, int>();
        foreach (var (username, name) in new[] { ("ann", "Ann"), ("bob", "Bob"), ("cyd", "Cyd"), ("dee", "Dee") })
        {
            ids[username] = (await users.CreateAsync(username, name, UserRole.Reviewer, Password, TestContext.Current.CancellationToken)).Id;
        }

        var project = await TestServices.Projects(store, clock).CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        foreach (var id in ids.Values)
        {
            await store.AddMembershipAsync(id, project.Id, TestContext.Current.CancellationToken);
        }

        await TestServices.Reviews(store, clock).CreateDraftAsync(
            project.Id, "rev-1", "First", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        return new Fixture(
            store,
            clock,
            users,
            TestServices.Signing(store, users, clock),
            TestServices.Threads(store, clock),
            TestServices.Checklists(store, clock, userStore),
            project.Id,
            ids);
    }

    /// <summary>
    ///     Seats a user in a review (a slot of their own) unless they already hold a slot of it.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="reviewId">The review.</param>
    /// <param name="who">The user to seat.</param>
    /// <returns>A task that completes when the user holds a slot.</returns>
    private static async Task SeatAsync(Fixture fixture, string reviewId, Actor who)
    {
        var userId = fixture.Ids[who.Username];
        var slots = await fixture.Store.ListSlotsAsync(fixture.ProjectId, reviewId, TestContext.Current.CancellationToken);
        if (slots.Any(s => s.PrimaryUserId == userId || s.AlternateUserId == userId))
        {
            return;
        }

        await fixture.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = fixture.ProjectId, ReviewId = reviewId, PrimaryUserId = userId },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Marks a review as answered: the primary reviewer of every slot of it, now or added later through
    ///     <see cref="AddSlotAsync"/>, answers every check as Pass in the own ballot and hands it in.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="reviewId">The review to answer.</param>
    /// <returns>A task that completes when the existing slots have handed their ballots in.</returns>
    private static async Task AnswerAllAsync(Fixture fixture, string reviewId = "rev-1")
    {
        fixture.Answered.Add(reviewId);
        foreach (var slot in await fixture.Store.ListSlotsAsync(fixture.ProjectId, reviewId, TestContext.Current.CancellationToken))
        {
            await AnswerAsPrimaryAsync(fixture, reviewId, slot);
        }
    }

    /// <summary>
    ///     Lets the primary reviewer of a slot answer every check as Pass and hand the ballot in.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="reviewId">The review.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>A task that completes when the ballot is handed in.</returns>
    private static async Task AnswerAsPrimaryAsync(Fixture fixture, string reviewId, ReviewerSlot slot)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerIds = new[] { slot.PrimaryUserId, slot.AlternateUserId }.OfType<int>();
        foreach (var ownerId in ownerIds)
        {
            var username = fixture.Ids.First(pair => pair.Value == ownerId).Key;
            var owner = new Actor(username, username);
            try
            {
                await AuditTestExtensions.AnswerAllAsync(fixture.Checklists, fixture.ProjectId, reviewId, owner, cancellationToken);
                await fixture.Checklists.SubmitBallotAsync(fixture.ProjectId, reviewId, owner, cancellationToken);
                return;
            }
            catch (DomainRuleException ex) when (ex.Rule == DomainRule.AuthorCannotAnswer)
            {
                // The author of the review cannot answer; the alternate of the slot, if any, answers instead
            }
        }
    }

    /// <summary>
    ///     Adds a slot as Cyd; when the review is marked answered by <see cref="AnswerAllAsync"/>, the primary reviewer
    ///     of the new slot answers every check and hands the ballot in.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="reviewId">The review.</param>
    /// <param name="primary">The user name of the primary reviewer.</param>
    /// <param name="alternate">The user name of the alternate reviewer, or <c>null</c> for none.</param>
    /// <returns>The slot.</returns>
    private static async Task<ReviewerSlot> AddSlotAsync(Fixture fixture, string reviewId, string primary, string? alternate = null)
    {
        var slot = await fixture.Signing.AddSlotAsync(
            Cyd,
            fixture.ProjectId,
            reviewId,
            fixture.Ids[primary],
            alternate is null ? null : fixture.Ids[alternate],
            TestContext.Current.CancellationToken);
        if (fixture.Answered.Contains(reviewId))
        {
            await AnswerAsPrimaryAsync(fixture, reviewId, slot);
        }

        return slot;
    }

    /// <summary>
    ///     Creates the review <c>rev-2</c>, authored by <paramref name="author"/>, and marks it as answered (an author
    ///     may not hold a slot of their own review, so the reviewers who are seated later answer it).
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="author">The user who creates the review, and so its author.</param>
    /// <returns>A task that completes when the review is ready to be seated.</returns>
    private static async Task CreateAuthoredReviewAsync(Fixture fixture, Actor author)
    {
        await TestServices.Reviews(fixture.Store, fixture.Clock).CreateDraftAsync(
            fixture.ProjectId,
            "rev-2",
            "Second",
            [new("a.txt", TestContent.A)],
            author,
            TestContext.Current.CancellationToken);
        await AnswerAllAsync(fixture, "rev-2");
    }

    /// <summary>
    ///     Approves <c>rev-1</c>: slot 1 (ann, bob) signed by ann.
    /// </summary>
    /// <returns>The fixture, the signed slot and the digest of the approval.</returns>
    private static async Task<(Fixture Fixture, ReviewerSlot Slot, string Digest)> CreateApprovedAsync()
    {
        var (fixture, slot) = await CreateReadyAsync();
        var digest = (await ApprovalAsync(fixture)).Digest;
        await fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken);
        return (fixture, slot, digest);
    }

    /// <summary>
    ///     Creates a slot with ann as primary and bob as alternate, on a review that can be signed.
    /// </summary>
    /// <returns>The fixture and the slot.</returns>
    private static async Task<(Fixture Fixture, ReviewerSlot Slot)> CreateReadyAsync()
    {
        var fixture = await CreateAsync();
        var slot = await AddSlotAsync(fixture, "rev-1", "ann", "bob");
        await AnswerAllAsync(fixture);
        return (fixture, slot);
    }

    /// <summary>
    ///     Reads the approval status of <c>rev-1</c>.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="reviewId">The review to read.</param>
    /// <returns>The status.</returns>
    private static async Task<ReviewApproval> ApprovalAsync(Fixture fixture, string reviewId = "rev-1")
    {
        return (await fixture.Signing.GetApprovalAsync(fixture.ProjectId, reviewId, TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    ///     Test that an actor that came through an API token cannot sign, even with the right password and digest,
    ///     because a signature must be a person's fresh act.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_ApiTokenActor_IsRefusedBeforeAnyCheck()
    {
        // Arrange: a review that can be signed, and the primary reviewer's identity arriving through a token
        var (fixture, slot) = await CreateReadyAsync();
        var digest = (await ApprovalAsync(fixture)).Digest;
        var viaApi = new Actor("ann", "Ann") { ApiTokenId = "K7M2QX9P" };
        var auditsBefore = fixture.Store.Audits.Count;

        // Act: sign with everything right
        var right = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
            viaApi, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken));

        // Act: sign with everything wrong, more often than the lockout allows, which must give the same answer
        // every time because nothing else is checked
        DomainRuleException? wrong = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            wrong = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
                viaApi, fixture.ProjectId, "rev-999", 12345, "stale", "wrong password here", TestContext.Current.CancellationToken));
        }

        // Assert: refused as not allowed with the API-token message, and the review is still unsigned
        Assert.Equal(DomainRule.ActorNotAllowed, right.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, wrong!.Rule);
        Assert.Equal(right.Message, wrong.Message);
        Assert.Contains("API token", right.Message, StringComparison.Ordinal);
        Assert.All((await ApprovalAsync(fixture)).Slots, s => Assert.Equal(SlotSignatureState.NotSigned, s.State));

        // Assert: nothing was recorded and the password was never counted, so the real person can still sign
        Assert.Equal(auditsBefore, fixture.Store.Audits.Count);
        var signature = await fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken);
        Assert.Equal(fixture.Ids["ann"], signature.UserId);
    }

    /// <summary>
    ///     Test that an actor that came through an API token cannot add, change or remove reviewer slots.
    /// </summary>
    [Fact]
    public async Task SigningService_AddUpdateRemoveSlot_ApiTokenActor_IsRefused()
    {
        // Arrange: a review with one slot, and an actor that came through a token
        var (fixture, slot) = await CreateReadyAsync();
        var viaApi = new Actor("cyd", "Cyd") { ApiTokenId = "K7M2QX9P" };

        // Act: add, update and remove a slot
        var add = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.AddSlotAsync(
            viaApi, fixture.ProjectId, "rev-1", fixture.Ids["dee"], null, TestContext.Current.CancellationToken));
        var update = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.UpdateSlotAsync(
            viaApi, fixture.ProjectId, "rev-1", slot.Id, fixture.Ids["dee"], null, TestContext.Current.CancellationToken));
        var remove = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.RemoveSlotAsync(
            viaApi, fixture.ProjectId, "rev-1", slot.Id, TestContext.Current.CancellationToken));

        // Assert: all refused, and the slot is unchanged
        Assert.Equal(DomainRule.ActorNotAllowed, add.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, update.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, remove.Rule);
        var approval = await ApprovalAsync(fixture);
        var unchanged = Assert.Single(approval.Slots);
        Assert.Equal("Ann", unchanged.Primary.DisplayName);
    }

    /// <summary>
    ///     Test that a slot can be added, is numbered and lists primary and alternate.
    /// </summary>
    [Fact]
    public async Task SigningService_AddSlotAsync_Valid_AddsNumberedUnsignedSlot()
    {
        // Arrange: a review without slots
        var fixture = await CreateAsync();

        // Act: any signed-in user adds two slots
        await fixture.Signing.AddSlotAsync(Cyd, fixture.ProjectId, "rev-1", fixture.Ids["ann"], fixture.Ids["bob"], TestContext.Current.CancellationToken);
        await fixture.Signing.AddSlotAsync(Cyd, fixture.ProjectId, "rev-1", fixture.Ids["cyd"], null, TestContext.Current.CancellationToken);
        var approval = await ApprovalAsync(fixture);

        // Assert: slots are numbered in creation order and not signed
        Assert.Equal([1, 2], approval.Slots.Select(s => s.Number));
        Assert.Equal("Ann", approval.Slots[0].Primary.DisplayName);
        Assert.Equal("Bob", approval.Slots[0].Alternate?.DisplayName);
        Assert.Null(approval.Slots[1].Alternate);
        Assert.All(approval.Slots, s => Assert.Equal(SlotSignatureState.NotSigned, s.State));
        Assert.False(approval.IsApproved);
    }

    /// <summary>
    ///     Test that the same user cannot be primary and alternate of one slot.
    /// </summary>
    [Fact]
    public async Task SigningService_AddSlotAsync_SameUserTwice_ThrowsSlotUserInvalid()
    {
        // Arrange: a review
        var fixture = await CreateAsync();

        // Act: add a slot whose primary and alternate are the same user
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.AddSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", fixture.Ids["ann"], fixture.Ids["ann"], TestContext.Current.CancellationToken));

        // Assert: refused and nothing stored
        Assert.Equal(DomainRule.SlotUserInvalid, exception.Rule);
        Assert.Empty(await fixture.Store.ListSlotsAsync(fixture.ProjectId, "rev-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a user who is already in a slot cannot be put in another slot of the same review.
    /// </summary>
    /// <param name="asAlternate">Whether the repeated user is offered as the alternate rather than the primary.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SigningService_AddSlotAsync_UserInOtherSlot_ThrowsSlotUserInvalid(bool asAlternate)
    {
        // Arrange: a review with a slot of ann and bob
        var (fixture, _) = await CreateReadyAsync();

        // Act: add a slot that repeats bob
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.AddSlotAsync(
            Cyd,
            fixture.ProjectId,
            "rev-1",
            asAlternate ? fixture.Ids["cyd"] : fixture.Ids["bob"],
            asAlternate ? fixture.Ids["bob"] : null,
            TestContext.Current.CancellationToken));

        // Assert: refused
        Assert.Equal(DomainRule.SlotUserInvalid, exception.Rule);
        Assert.Single(await fixture.Store.ListSlotsAsync(fixture.ProjectId, "rev-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that an unknown or disabled user cannot fill a slot.
    /// </summary>
    [Fact]
    public async Task SigningService_AddSlotAsync_UnknownOrDisabledUser_ThrowsSlotUserInvalid()
    {
        // Arrange: a review and a disabled user
        var fixture = await CreateAsync();
        await fixture.Users.SetEnabledAsync("dee", false, TestContext.Current.CancellationToken);

        // Act: offer an unknown primary, and a disabled alternate
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.AddSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", 999, null, TestContext.Current.CancellationToken));
        var disabled = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.AddSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", fixture.Ids["ann"], fixture.Ids["dee"], TestContext.Current.CancellationToken));

        // Assert: both refused
        Assert.Equal(DomainRule.SlotUserInvalid, unknown.Rule);
        Assert.Equal(DomainRule.SlotUserInvalid, disabled.Rule);
    }

    /// <summary>
    ///     Test that a signed-in user whose account is gone or disabled cannot change slots.
    /// </summary>
    [Fact]
    public async Task SigningService_AddSlotAsync_ActorDisabledOrUnknown_ThrowsActorNotAllowed()
    {
        // Arrange: a review and a disabled user
        var fixture = await CreateAsync();
        await fixture.Users.SetEnabledAsync("dee", false, TestContext.Current.CancellationToken);

        // Act: a disabled user and an unknown user try to add a slot
        var disabled = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.AddSlotAsync(
            TestActors.Dee, fixture.ProjectId, "rev-1", fixture.Ids["ann"], null, TestContext.Current.CancellationToken));
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.AddSlotAsync(
            TestActors.Eve, fixture.ProjectId, "rev-1", fixture.Ids["ann"], null, TestContext.Current.CancellationToken));

        // Assert: refused, nothing stored
        Assert.Equal(DomainRule.ActorNotAllowed, disabled.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, unknown.Rule);
        Assert.Empty(await fixture.Store.ListSlotsAsync(fixture.ProjectId, "rev-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a user whose account is gone or disabled cannot change or remove a slot, nor reopen an approved
    ///     review, and that nothing is stored or recorded by the attempt.
    /// </summary>
    [Fact]
    public async Task SigningService_UpdateRemoveAndReopen_ActorDisabledOrUnknown_ThrowsActorNotAllowed()
    {
        // Arrange: an approved review with one signed slot, a disabled user and an unknown user
        var cancellationToken = TestContext.Current.CancellationToken;
        var (fixture, slot, _) = await CreateApprovedAsync();
        await fixture.Users.SetEnabledAsync("dee", false, cancellationToken);
        var auditsBefore = fixture.Store.Audits.Count;
        var failures = new List<DomainRuleException>();

        // Act: each of them tries to change the slot, to remove it and to reopen the review
        foreach (var actor in new[] { TestActors.Dee, TestActors.Eve })
        {
            failures.Add(await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.UpdateSlotAsync(
                actor, fixture.ProjectId, "rev-1", slot.Id, fixture.Ids["cyd"], null, cancellationToken)));
            failures.Add(await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.RemoveSlotAsync(
                actor, fixture.ProjectId, "rev-1", slot.Id, cancellationToken)));
            failures.Add(await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.ReopenReviewAsync(
                actor, fixture.ProjectId, "rev-1", "Because.", cancellationToken)));
        }

        // Assert: every attempt was refused for the actor, and nothing changed or was recorded
        Assert.Equal(6, failures.Count);
        Assert.All(failures, failure => Assert.Equal(DomainRule.ActorNotAllowed, failure.Rule));
        Assert.Equal(auditsBefore, fixture.Store.Audits.Count);
        Assert.True((await ApprovalAsync(fixture)).IsApproved);
        Assert.Empty(await fixture.Store.ListReopensAsync(fixture.ProjectId, "rev-1", cancellationToken));
    }

    /// <summary>
    ///     Test that every slot and signature call for a project that does not exist is refused with
    ///     <see cref="DomainRule.ProjectNotFound"/>, before anything about the review is looked at.
    /// </summary>
    [Fact]
    public async Task SigningService_SlotSignAndReopenCalls_ProjectNotFound_ThrowsProjectNotFound()
    {
        // Arrange: a project identity that does not exist
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await CreateAsync();
        const int Missing = 9999;
        var ann = fixture.Ids["ann"];

        // Act: each call that needs the project
        var failures = new[]
        {
            await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.AddSlotAsync(
                Cyd, Missing, "rev-1", ann, null, cancellationToken)),
            await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.UpdateSlotAsync(
                Cyd, Missing, "rev-1", 1, ann, null, cancellationToken)),
            await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.RemoveSlotAsync(
                Cyd, Missing, "rev-1", 1, cancellationToken)),
            await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
                TestActors.Ann, Missing, "rev-1", 1, "digest", Password, cancellationToken)),
            await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.ReopenReviewAsync(
                Cyd, Missing, "rev-1", "Because.", cancellationToken)),
            await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.RequireConfirmableAsync(
                TestActors.Ann, Missing, "rev-1", 1, cancellationToken))
        };

        // Assert: all refused as a missing project
        Assert.All(failures, failure => Assert.Equal(DomainRule.ProjectNotFound, failure.Rule));
    }

    /// <summary>
    ///     Test that a slot cannot be added to a review that does not exist.
    /// </summary>
    [Fact]
    public async Task SigningService_AddSlotAsync_MissingReview_ThrowsReviewNotFound()
    {
        // Arrange: a project without that review
        var fixture = await CreateAsync();

        // Act: add a slot to a missing review
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.AddSlotAsync(
            Cyd, fixture.ProjectId, "nope", fixture.Ids["ann"], null, TestContext.Current.CancellationToken));

        // Assert: refused
        Assert.Equal(DomainRule.ReviewNotFound, exception.Rule);
    }

    /// <summary>
    ///     Test that an unsigned slot can be changed and removed.
    /// </summary>
    [Fact]
    public async Task SigningService_UpdateAndRemoveSlotAsync_Unsigned_Succeed()
    {
        // Arrange: a slot of ann and bob
        var (fixture, slot) = await CreateReadyAsync();

        // Act: swap the people, then remove the slot
        await fixture.Signing.UpdateSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", slot.Id, fixture.Ids["cyd"], null, TestContext.Current.CancellationToken);
        var changed = (await ApprovalAsync(fixture)).Slots.Single();
        await fixture.Signing.RemoveSlotAsync(Cyd, fixture.ProjectId, "rev-1", slot.Id, TestContext.Current.CancellationToken);

        // Assert: the slot changed, and is gone
        Assert.Equal("Cyd", changed.Primary.DisplayName);
        Assert.Null(changed.Alternate);
        Assert.Empty((await ApprovalAsync(fixture)).Slots);
    }

    /// <summary>
    ///     Test that a slot with a signature, current or out of date, can neither be changed nor removed.
    /// </summary>
    /// <param name="outOfDate">Whether the record changed after signing, so the signature is out of date.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SigningService_UpdateAndRemoveSlotAsync_Signed_ThrowsSlotSigned(bool outOfDate)
    {
        // Arrange: a slot signed by ann next to a second slot nobody has signed, so the review is not approved and
        // so not locked, optionally followed by a change of the record
        var (fixture, slot) = await CreateReadyAsync();
        await AddSlotAsync(fixture, "rev-1", "cyd");
        var digest = (await ApprovalAsync(fixture)).Digest;
        await fixture.Signing.SignAsync(TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken);
        if (outOfDate)
        {
            await fixture.Threads.StartThreadAsync(
                fixture.ProjectId,
                "rev-1",
                new ThreadAnchor("a.txt"),
                TestActors.Bob,
                "A late remark",
                TestContext.Current.CancellationToken,
                await fixture.Store.CurrentlySignedSlotIdsAsync(fixture.ProjectId, "rev-1"));
        }

        // Act: try to change and to remove the slot
        var update = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.UpdateSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", slot.Id, fixture.Ids["cyd"], null, TestContext.Current.CancellationToken));
        var remove = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.RemoveSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", slot.Id, TestContext.Current.CancellationToken));

        // Assert: both refused and the slot is unchanged
        Assert.Equal(DomainRule.SlotSigned, update.Rule);
        Assert.Equal(DomainRule.SlotSigned, remove.Rule);
        Assert.Equal("Ann", (await ApprovalAsync(fixture)).Slots[0].Primary.DisplayName);
    }

    /// <summary>
    ///     Test that changing or removing a slot that does not exist is refused.
    /// </summary>
    [Fact]
    public async Task SigningService_UpdateAndRemoveSlotAsync_UnknownSlot_ThrowsSlotNotFound()
    {
        // Arrange: a review without slots
        var fixture = await CreateAsync();

        // Act: change and remove an unknown slot
        var update = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.UpdateSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", 42, fixture.Ids["cyd"], null, TestContext.Current.CancellationToken));
        var remove = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.RemoveSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", 42, TestContext.Current.CancellationToken));

        // Assert: refused
        Assert.Equal(DomainRule.SlotNotFound, update.Rule);
        Assert.Equal(DomainRule.SlotNotFound, remove.Rule);
    }

    /// <summary>
    ///     Test that a slot can keep its own users when it is changed (they are not "in another slot").
    /// </summary>
    [Fact]
    public async Task SigningService_UpdateSlotAsync_KeepOwnUsers_Succeeds()
    {
        // Arrange: a slot of ann and bob
        var (fixture, slot) = await CreateReadyAsync();

        // Act: swap primary and alternate inside the same slot
        await fixture.Signing.UpdateSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", slot.Id, fixture.Ids["bob"], fixture.Ids["ann"], TestContext.Current.CancellationToken);

        // Assert: swapped
        var status = (await ApprovalAsync(fixture)).Slots.Single();
        Assert.Equal("Bob", status.Primary.DisplayName);
        Assert.Equal("Ann", status.Alternate?.DisplayName);
    }

    /// <summary>
    ///     Test that the primary signs with the password, and that the signature carries everything it should.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_PrimaryWithPassword_StoresSignatureAndApproves()
    {
        // Arrange: a ready review with one slot, and the time moved
        var (fixture, slot) = await CreateReadyAsync();
        var digest = (await ApprovalAsync(fixture)).Digest;
        var signedAt = _now.AddHours(2);
        fixture.Clock.Advance(TimeSpan.FromHours(2));

        // Act: ann signs
        var signature = await fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken);
        var approval = await ApprovalAsync(fixture);

        // Assert: the signature has the slot, user, role, time, digest and meaning; the review is approved
        Assert.Equal(slot.Id, signature.SlotId);
        Assert.Equal(fixture.Ids["ann"], signature.UserId);
        Assert.Equal("Ann", signature.DisplayName);
        Assert.Equal(SlotRole.Primary, signature.Role);
        Assert.Equal(signedAt, signature.SignedAt);
        Assert.Equal(digest, signature.RecordDigest);
        Assert.Equal("I have reviewed this record and approve it.", signature.Meaning);
        Assert.True(approval.IsApproved);
        Assert.Equal(SlotSignatureState.Signed, approval.Slots.Single().State);
        Assert.Equal("Ann", approval.Slots.Single().SignerName);
        Assert.Equal(signedAt, approval.Slots.Single().SignedAt);
        Assert.Equal("Approved. Every reviewer slot has a current signature.", approval.NextStep);
    }

    /// <summary>
    ///     Test that the alternate may sign instead of the primary and is recorded as the alternate.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_Alternate_RecordsAlternateRole()
    {
        // Arrange: a ready review with a slot of ann and bob
        var (fixture, slot) = await CreateReadyAsync();
        var digest = (await ApprovalAsync(fixture)).Digest;

        // Act: bob signs
        var signature = await fixture.Signing.SignAsync(
            TestActors.Bob, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken);

        // Assert: the role is Alternate and the slot is signed
        Assert.Equal(SlotRole.Alternate, signature.Role);
        Assert.Equal("Bob", signature.DisplayName);
        Assert.Equal(SlotSignatureState.Signed, (await ApprovalAsync(fixture)).Slots.Single().State);
    }

    /// <summary>
    ///     Test that a user who is not in the slot cannot sign it, even with the right password.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_NotInSlot_ThrowsActorNotAllowed()
    {
        // Arrange: a ready review with a slot of ann and bob
        var (fixture, slot) = await CreateReadyAsync();
        var digest = (await ApprovalAsync(fixture)).Digest;

        // Act: cy tries to sign
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
            Cyd, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken));

        // Assert: refused, no signature
        Assert.Equal(DomainRule.ActorNotAllowed, exception.Rule);
        Assert.Empty(await fixture.Store.ListSignaturesAsync(fixture.ProjectId, "rev-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a disabled or unknown account cannot sign.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_ActorDisabled_ThrowsActorNotAllowed()
    {
        // Arrange: a ready review whose primary is then disabled
        var (fixture, slot) = await CreateReadyAsync();
        var digest = (await ApprovalAsync(fixture)).Digest;
        await fixture.Users.SetEnabledAsync("ann", false, TestContext.Current.CancellationToken);

        // Act: ann still presents herself
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken));

        // Assert: refused
        Assert.Equal(DomainRule.ActorNotAllowed, exception.Rule);
    }

    /// <summary>
    ///     Test that a wrong or missing password never signs and gives only the generic message, which does not
    ///     contain the password.
    /// </summary>
    /// <param name="password">The password offered.</param>
    [Theory]
    [InlineData("wrong password here")]
    [InlineData("")]
    [InlineData(null)]
    public async Task SigningService_SignAsync_WrongPassword_ThrowsReauthenticationFailed(string? password)
    {
        // Arrange: a ready review
        var (fixture, slot) = await CreateReadyAsync();
        var digest = (await ApprovalAsync(fixture)).Digest;

        // Act: ann signs with a wrong password
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, password, TestContext.Current.CancellationToken));

        // Assert: only the generic text, no signature, the review is not approved
        Assert.Equal(DomainRule.ReauthenticationFailed, exception.Rule);
        Assert.Equal("The password is incorrect.", exception.Message);
        if (!string.IsNullOrEmpty(password))
        {
            Assert.DoesNotContain(password, exception.ToString(), StringComparison.Ordinal);
        }

        Assert.Empty(await fixture.Store.ListSignaturesAsync(fixture.ProjectId, "rev-1", TestContext.Current.CancellationToken));
        Assert.False((await ApprovalAsync(fixture)).IsApproved);
    }

    /// <summary>
    ///     Test that failed re-authentications count towards the same lockout as login, so even the right
    ///     password is then refused.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_RepeatedWrongPassword_LocksOutLikeLogin()
    {
        // Arrange: a ready review
        var (fixture, slot) = await CreateReadyAsync();
        var digest = (await ApprovalAsync(fixture)).Digest;

        // Act: five wrong passwords, then the right one
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
                TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, "wrong password here", TestContext.Current.CancellationToken));
        }

        var locked = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken));

        // Assert: the account is locked for login as well, and nothing was signed
        Assert.Equal(DomainRule.ReauthenticationFailed, locked.Rule);
        Assert.Null(await fixture.Users.AuthenticateAsync("ann", Password, TestContext.Current.CancellationToken));
        Assert.Empty(await fixture.Store.ListSignaturesAsync(fixture.ProjectId, "rev-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that every blocker is reported, signing is refused while any exists, and the refusal is not a
    ///     password attempt.
    /// </summary>
    /// <param name="blocker">The blocker under test: unanswered or thread.</param>
    [Theory]
    [InlineData("unanswered")]
    [InlineData("thread")]
    public async Task SigningService_SignAsync_Blocker_ThrowsSigningNotAllowedAndListsBlocker(string blocker)
    {
        // Arrange: a review with a slot, answered completely except for the "unanswered" case, then one blocker introduced
        var fixture = await CreateAsync();
        var slot = await fixture.Signing.AddSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", fixture.Ids["ann"], fixture.Ids["bob"], TestContext.Current.CancellationToken);
        if (blocker != "unanswered")
        {
            await AnswerAllAsync(fixture);
        }

        if (blocker == "thread")
        {
            await fixture.Threads.StartThreadAsync(
                fixture.ProjectId, "rev-1", new ThreadAnchor("a.txt"), TestActors.Bob, "Look at this", TestContext.Current.CancellationToken);
        }

        var approval = await ApprovalAsync(fixture);

        // Act: ann signs with the right password
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, approval.Digest, Password, TestContext.Current.CancellationToken));

        // Assert: refused with a blocker sentence; not approved, nothing stored
        Assert.Equal(DomainRule.SigningNotAllowed, exception.Rule);
        var expected = blocker == "thread" ? "open" : "unanswered";
        Assert.Contains(approval.Blockers, text => text.Contains(expected, StringComparison.Ordinal));
        Assert.False(approval.IsApproved);
        Assert.Empty(await fixture.Store.ListSignaturesAsync(fixture.ProjectId, "rev-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a section that does not apply neither blocks signing nor needs answers.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_NotApplicableSectionFail_DoesNotBlock()
    {
        // Arrange: a ready review where one section is marked not applicable and one of its checks is Fail
        var (fixture, _) = await CreateReadyAsync();
        var section = DefaultChecklist.Current.Sections[0];
        await fixture.Checklists.SetAnswerAsync(
            fixture.ProjectId, "rev-1", section.Items[0].Id, ChecklistOutcome.Fail, "bad", TestActors.Ann, TestContext.Current.CancellationToken);
        Assert.NotEmpty((await ApprovalAsync(fixture)).Blockers);

        // Act: the section does not apply
        await fixture.Checklists.SetSectionApplicabilityAsync(
            fixture.ProjectId, "rev-1", section.Id, false, "Not relevant", TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert: nothing blocks signing any more
        Assert.Empty((await ApprovalAsync(fixture)).Blockers);
    }

    /// <summary>
    ///     Test that a signing request for a record the signer did not see is refused before the password is used.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_StaleExpectedDigest_ThrowsRecordChanged()
    {
        // Arrange: a ready review; ann sees the digest, then somebody opens a thread and resolves it
        var (fixture, slot) = await CreateReadyAsync();
        var seen = (await ApprovalAsync(fixture)).Digest;
        var thread = await fixture.Threads.StartThreadAsync(
            fixture.ProjectId, "rev-1", new ThreadAnchor("a.txt"), TestActors.Bob, "Remark", TestContext.Current.CancellationToken,
            includeInReport: true);
        await fixture.Threads.ResolveAsync(
            fixture.ProjectId, "rev-1", thread.Id, TestActors.Bob, TestContext.Current.CancellationToken,
            resolutionNote: "Done");

        // Act: ann signs what she saw
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, seen, Password, TestContext.Current.CancellationToken));

        // Assert: refused, nothing signed
        Assert.Equal(DomainRule.RecordChanged, exception.Rule);
        Assert.Empty(await fixture.Store.ListSignaturesAsync(fixture.ProjectId, "rev-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a slot cannot be signed twice for the same record.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_AlreadySignedCurrent_ThrowsSigningNotAllowed()
    {
        // Arrange: a slot signed by ann, next to a second slot nobody has signed, so the review is not locked
        var (fixture, slot) = await CreateReadyAsync();
        await AddSlotAsync(fixture, "rev-1", "cyd");
        var digest = (await ApprovalAsync(fixture)).Digest;
        await fixture.Signing.SignAsync(TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken);

        // Act: bob signs the same slot
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
            TestActors.Bob, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken));

        // Assert: refused, one signature only
        Assert.Equal(DomainRule.SigningNotAllowed, exception.Rule);
        Assert.Single(await fixture.Store.ListSignaturesAsync(fixture.ProjectId, "rev-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the structured blocker counts of the approval status (open threads, unanswered checks and Fail
    ///     answers) equal the numbers in the blocker sentences, for a single read and for the batch read, and are zero
    ///     for an approved review. A Fail answer is counted, but it is information and no blocker sentence.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_BlockerCounts_EqualTheBlockerSentences()
    {
        // Arrange: a review with one Fail answer, the rest unanswered and one open thread; and an approved review
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await CreateAsync();
        var items = DefaultChecklist.Current.Sections.SelectMany(section => section.Items).ToList();
        await SeatAsync(fixture, "rev-1", TestActors.Ann);
        await fixture.Checklists.SetAnswerAsync(
            fixture.ProjectId, "rev-1", items[0].Id, ChecklistOutcome.Fail, "Wrong", TestActors.Ann, cancellationToken);
        await fixture.Threads.StartThreadAsync(
            fixture.ProjectId, "rev-1", new ThreadAnchor("a.txt"), TestActors.Bob, "Look", cancellationToken);
        var (approvedFixture, _, _) = await CreateApprovedAsync();

        // Act: read the status singly and in a batch
        var single = await ApprovalAsync(fixture);
        var batch = (await fixture.Signing.GetApprovalsAsync(fixture.ProjectId, ["rev-1"], cancellationToken))["rev-1"];
        var approved = await ApprovalAsync(approvedFixture);

        // Assert: the counts are those of the sentences, in both reads
        foreach (var approval in new[] { single, batch })
        {
            Assert.Equal(1, approval.OpenThreads);
            Assert.Equal(1, approval.FailedChecks);
            Assert.Equal(items.Count - 1, approval.UnansweredChecks);
            Assert.Contains("1 open question to resolve", approval.Blockers);
            Assert.DoesNotContain("1 check is answered Fail", approval.Blockers);
            Assert.Contains($"{items.Count - 1} checklist checks are unanswered", approval.Blockers);
        }

        Assert.True(approved.IsApproved);
        Assert.Equal((0, 0, 0), (approved.OpenThreads, approved.UnansweredChecks, approved.FailedChecks));
    }

    /// <summary>
    ///     Test that the structured <see cref="ReviewApproval.MissingChecklist"/> flag is true exactly when the "no
    ///     checklist" blocker sentence is present, for a single read and for the batch read.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_MissingChecklist_FlagMatchesTheBlockerSentence()
    {
        // Arrange: a stored review without a checklist next to the review with the default checklist
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await CreateAsync();
        await fixture.Store.AddReviewAsync(
            new Review { ProjectId = fixture.ProjectId, Id = "rev-none", Title = "No checklist", Created = _now },
            cancellationToken);

        // Act: read both singly and in a batch
        var without = await ApprovalAsync(fixture, "rev-none");
        var with = await ApprovalAsync(fixture);
        var batch = await fixture.Signing.GetApprovalsAsync(fixture.ProjectId, ["rev-none", "rev-1"], cancellationToken);

        // Assert: the flag is set exactly with the sentence, in both reads
        Assert.True(without.MissingChecklist);
        Assert.Contains("This review has no checklist.", without.Blockers);
        Assert.False(with.MissingChecklist);
        Assert.DoesNotContain("This review has no checklist.", with.Blockers);
        Assert.True(batch["rev-none"].MissingChecklist);
        Assert.False(batch["rev-1"].MissingChecklist);
        Assert.Equivalent(without, batch["rev-none"]);
    }

    /// <summary>
    ///     Test that a reopening of the review, which changes the record, makes the old signature out of date, which
    ///     can be signed again, and that the history stays.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_RecordChangedAfterSigning_OutOfDateThenResignable()
    {
        // Arrange: a slot signed by ann
        var (fixture, slot) = await CreateReadyAsync();
        var digest = (await ApprovalAsync(fixture)).Digest;
        await fixture.Signing.SignAsync(TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken);

        // Act: an administrator reopens the approved review, so the record differs and the signature is out of date
        await fixture.Signing.ReopenReviewAsync(Cyd, fixture.ProjectId, "rev-1", "One more thing", TestContext.Current.CancellationToken);
        var outOfDate = await ApprovalAsync(fixture);
        await fixture.Signing.SignAsync(TestActors.Bob, fixture.ProjectId, "rev-1", slot.Id, outOfDate.Digest, Password, TestContext.Current.CancellationToken);
        var again = await ApprovalAsync(fixture);

        // Assert: out of date and not approved, then signed again by bob, with both signatures kept
        Assert.Equal(SlotSignatureState.OutOfDate, outOfDate.Slots.Single().State);
        Assert.False(outOfDate.IsApproved);
        Assert.NotEqual(digest, outOfDate.Digest);
        Assert.Equal(SlotSignatureState.Signed, again.Slots.Single().State);
        Assert.Equal("Bob", again.Slots.Single().SignerName);
        Assert.True(again.IsApproved);
        Assert.Equal(2, (await fixture.Store.ListSignaturesAsync(fixture.ProjectId, "rev-1", TestContext.Current.CancellationToken)).Count);
    }

    /// <summary>
    ///     Test that a change of the content of a review that is not approved yet, a checklist answer or a comment,
    ///     makes a current signature out of date and the review not approved, without removing the signature, and
    ///     that the slot can be signed again for the new record.
    /// </summary>
    /// <param name="change">What changes: <c>answer</c> changes a checklist answer, <c>comment</c> adds a comment.</param>
    [Theory]
    [InlineData("answer")]
    [InlineData("comment")]
    public async Task SigningService_GetApprovalAsync_ContentChangedAfterSigning_OutOfDateThenResignable(string change)
    {
        // Arrange: two slots, so that the review is not approved and not locked when ann has signed the first
        var cancellationToken = TestContext.Current.CancellationToken;
        var (fixture, first) = await CreateReadyAsync();
        var second = await AddSlotAsync(fixture, "rev-1", "cyd");
        var signedDigest = (await ApprovalAsync(fixture)).Digest;
        await fixture.Signing.SignAsync(TestActors.Ann, fixture.ProjectId, "rev-1", first.Id, signedDigest, Password, cancellationToken);
        var current = await ApprovalAsync(fixture);

        // Act: the content changes while the signature is current; the signed slots are acknowledged
        var acknowledged = await fixture.Store.CurrentlySignedSlotIdsAsync(fixture.ProjectId, "rev-1");
        ReviewThread? thread = null;
        if (change == "answer")
        {
            // Both reviewers hand in the changed answer, so that the consolidated record (and its digest) changes
            foreach (var reviewer in new[] { TestActors.Bob, Cyd })
            {
                await fixture.Checklists.SetAnswerAsync(
                    fixture.ProjectId,
                    "rev-1",
                    DefaultChecklist.Current.Sections[0].Items[0].Id,
                    ChecklistOutcome.NotApplicable,
                    "Does not apply to this change.",
                    reviewer,
                    cancellationToken,
                    acknowledged);
                await fixture.Checklists.SubmitBallotAsync(fixture.ProjectId, "rev-1", reviewer, cancellationToken, acknowledged);
            }
        }
        else
        {
            thread = await fixture.Threads.StartThreadAsync(
                fixture.ProjectId, "rev-1", new ThreadAnchor("a.txt"), TestActors.Bob, "A late remark", cancellationToken,
                acknowledged, true);
        }

        var outOfDate = await ApprovalAsync(fixture);

        // Assert: the digest moved, the signature is kept but out of date, nothing else is signed, not approved
        Assert.Equal(SlotSignatureState.Signed, current.Slots[0].State);
        Assert.False(current.IsApproved);
        Assert.NotEqual(signedDigest, outOfDate.Digest);
        Assert.Equal(SlotSignatureState.OutOfDate, outOfDate.Slots[0].State);
        Assert.Equal(SlotSignatureState.NotSigned, outOfDate.Slots[1].State);
        Assert.False(outOfDate.IsApproved);
        Assert.Single(await fixture.Store.ListSignaturesAsync(fixture.ProjectId, "rev-1", cancellationToken));

        // Act: a comment opens a thread, which blocks signing until it is resolved; then the alternate signs the new record
        if (thread is not null)
        {
            var blocked = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
                TestActors.Bob, fixture.ProjectId, "rev-1", first.Id, outOfDate.Digest, Password, cancellationToken));
            Assert.Equal(DomainRule.SigningNotAllowed, blocked.Rule);
            await fixture.Threads.ResolveAsync(
                fixture.ProjectId, "rev-1", thread.Id, TestActors.Bob, cancellationToken, resolutionNote: "Done");
        }

        var ready = await ApprovalAsync(fixture);
        await fixture.Signing.SignAsync(
            TestActors.Bob, fixture.ProjectId, "rev-1", first.Id, ready.Digest, Password, cancellationToken);
        var resigned = await ApprovalAsync(fixture);

        // Assert: signed again for the new record, both signatures kept, and still waiting for the second slot
        Assert.Equal(SlotSignatureState.Signed, resigned.Slots[0].State);
        Assert.Equal("Bob", resigned.Slots[0].SignerName);
        Assert.Equal(SlotSignatureState.NotSigned, resigned.Slots[1].State);
        Assert.False(resigned.IsApproved);
        Assert.Equal(2, (await fixture.Store.ListSignaturesAsync(fixture.ProjectId, "rev-1", cancellationToken)).Count);

        // Act: the second slot signs
        await fixture.Signing.SignAsync(
            Cyd, fixture.ProjectId, "rev-1", second.Id, resigned.Digest, Password, cancellationToken);

        // Assert: now the review is approved for the new record
        Assert.True((await ApprovalAsync(fixture)).IsApproved);
    }

    /// <summary>
    ///     Test that the review is approved only when every slot has a current signature.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_TwoSlots_ApprovedOnlyWhenBothSigned()
    {
        // Arrange: two slots (ann + bob, cy alone) on a ready review
        var (fixture, first) = await CreateReadyAsync();
        var second = await AddSlotAsync(fixture, "rev-1", "cyd");
        var digest = (await ApprovalAsync(fixture)).Digest;

        // Act: the first slot signs, then the second
        await fixture.Signing.SignAsync(TestActors.Ann, fixture.ProjectId, "rev-1", first.Id, digest, Password, TestContext.Current.CancellationToken);
        var half = await ApprovalAsync(fixture);
        await fixture.Signing.SignAsync(Cyd, fixture.ProjectId, "rev-1", second.Id, digest, Password, TestContext.Current.CancellationToken);
        var full = await ApprovalAsync(fixture);

        // Assert: In review after one signature, Approved after both
        Assert.False(half.IsApproved);
        Assert.Equal("Ready for signatures.", half.NextStep);
        Assert.True(full.IsApproved);
    }

    /// <summary>
    ///     Test that a review without slots is never approved and the next step asks for a slot.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_NoSlots_NotApprovedAndAsksForSlot()
    {
        // Arrange: a review without slots; nobody can answer, so the checks stay unanswered and are the first blocker
        var fixture = await CreateAsync();

        // Act: read the status
        var approval = await ApprovalAsync(fixture);

        // Assert: not approved, with no slot to sign; the next step names the unanswered checks that nobody can answer
        Assert.False(approval.IsApproved);
        Assert.Empty(approval.Slots);
        Assert.Contains("checklist checks unanswered", approval.NextStep, StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{64}$", approval.Digest);
    }

    /// <summary>
    ///     Test that the status of a review that does not exist is <c>null</c>.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_MissingReview_ReturnsNull()
    {
        // Arrange: a project without that review
        var fixture = await CreateAsync();

        // Act, Assert: no status
        Assert.Null(await fixture.Signing.GetApprovalAsync(fixture.ProjectId, "nope", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the batch read gives for each review the same status as a single read, skips a review that does
    ///     not exist, and loads the users once.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalsAsync_SeveralReviews_EqualsSingleReadsAndLoadsUsersOnce()
    {
        // Arrange: a project with one ready review over a user store that counts list reads
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var clock = new MovableTimeProvider(_now);
        var userStore = new CountingUserStore(new FakeUserStore());
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        var ann = await users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, cancellationToken);
        var project = await TestServices.Projects(store, clock).CreateAsync("Apollo", null, cancellationToken);
        await store.AddMembershipAsync(ann.Id, project.Id, cancellationToken);
        var reviews = TestServices.Reviews(store, clock);
        await reviews.CreateDraftAsync(project.Id, "rev-1", "First", [new("a.txt", TestContent.A)], cancellationToken);
        await reviews.CreateDraftAsync(project.Id, "rev-2", "Second", [new("b.txt", TestContent.A)], cancellationToken);
        var signing = TestServices.Signing(store, users, clock);
        await signing.AddSlotAsync(TestActors.Ann, project.Id, "rev-1", ann.Id, null, cancellationToken);
        var single = await signing.GetApprovalAsync(project.Id, "rev-1", cancellationToken);
        var before = userStore.ListCount;

        // Act: read the two reviews and a missing one in one batch
        var batch = await signing.GetApprovalsAsync(project.Id, ["rev-1", "rev-2", "nope"], cancellationToken);

        // Assert: the existing reviews are there, the status equals the single read, the users were read once
        Assert.Equal(["rev-1", "rev-2"], batch.Keys.Order().ToArray());
        Assert.Equivalent(single, batch["rev-1"]);
        Assert.Equal(1, userStore.ListCount - before);
    }

    /// <summary>
    ///     Test that the batch read of no reviews gives an empty result without reading the user list.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalsAsync_NoReviews_ReturnsEmptyWithoutReadingUsers()
    {
        // Arrange: a user store that counts list reads
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var clock = new MovableTimeProvider(_now);
        var userStore = new CountingUserStore(new FakeUserStore());
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        var project = await TestServices.Projects(store, clock).CreateAsync("Apollo", null, cancellationToken);
        var signing = TestServices.Signing(store, users, clock);

        // Act: read an empty sequence of reviews
        var batch = await signing.GetApprovalsAsync(project.Id, [], cancellationToken);

        // Assert: nothing returned and the user list was never read
        Assert.Empty(batch);
        Assert.Equal(0, userStore.ListCount);
    }

    /// <summary>
    ///     Test that the batch read takes the snapshots of all reviews with one store batch read and not one snapshot
    ///     read per review, that a duplicated identifier is read once, and that every status, including the reopen
    ///     history of a reopened review, equals the single read of the same review.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalsAsync_SeveralReviews_UsesOneStoreBatchAndNoSingleSnapshots()
    {
        // Arrange: a reopened review and a second review of the same project
        var cancellationToken = TestContext.Current.CancellationToken;
        var (fixture, _, _) = await CreateApprovedAsync();
        await fixture.Signing.ReopenReviewAsync(Cyd, fixture.ProjectId, "rev-1", "Because.", cancellationToken);
        await CreateAuthoredReviewAsync(fixture, TestActors.Ann);
        var singleReadsBefore = fixture.Store.SnapshotReads;
        var batchReadsBefore = fixture.Store.SnapshotBatchReads;

        // Act
        var batch = await fixture.Signing.GetApprovalsAsync(fixture.ProjectId, ["rev-1", "rev-2", "rev-1", "nope"], cancellationToken);
        var singleReadsDuring = fixture.Store.SnapshotReads - singleReadsBefore;
        var batchReadsDuring = fixture.Store.SnapshotBatchReads - batchReadsBefore;

        // Assert: one batch read, no single snapshot read, and every status equals the single read
        Assert.Equal(1, batchReadsDuring);
        Assert.Equal(0, singleReadsDuring);
        Assert.Equal(["rev-1", "rev-2"], batch.Keys.Order().ToArray());
        Assert.Equal("Because.", Assert.Single(batch["rev-1"].Reopens!).Reason);
        Assert.Null(batch["rev-2"].Reopens);
        Assert.Equivalent(await ApprovalAsync(fixture, "rev-1"), batch["rev-1"]);
        Assert.Equivalent(await ApprovalAsync(fixture, "rev-2"), batch["rev-2"]);
    }

    /// <summary>
    ///     Test that only enabled members of the project are offered for a slot, and without anything secret.
    /// </summary>
    [Fact]
    public async Task SigningService_ListCandidatesAsync_ReturnsEnabledMembersOnly()
    {
        // Arrange: one member disabled and one user who is not a member
        var fixture = await CreateAsync();
        await fixture.Users.SetEnabledAsync("dee", false, TestContext.Current.CancellationToken);
        await fixture.Users.CreateAsync("eve", "Eve", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);

        // Act: list the candidates
        var candidates = await fixture.Signing.ListCandidatesAsync(Cyd, fixture.ProjectId, TestContext.Current.CancellationToken);

        // Assert: the other three members, not eve
        Assert.Equal(["ann", "bob", "cyd"], candidates.Select(c => c.Username).Order());
    }

    /// <summary>
    ///     Test that a slot can only be given to enabled members of the project: a user who is not a member, even a
    ///     system administrator, is refused as primary and as alternate, and nothing is stored.
    /// </summary>
    [Fact]
    public async Task SigningService_AddSlotAsync_NonMember_ThrowsSlotUserInvalid()
    {
        // Arrange: a review and two users that are not members of its project
        var fixture = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var eve = await fixture.Users.CreateAsync("eve", "Eve", UserRole.Admin, Password, cancellationToken);

        // Act: give the slot to the outsider as primary, then as alternate
        var primary = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.AddSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", eve.Id, null, cancellationToken));
        var alternate = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.AddSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", fixture.Ids["ann"], eve.Id, cancellationToken));

        // Assert: both are refused with the slot rule and no slot exists
        Assert.Equal(DomainRule.SlotUserInvalid, primary.Rule);
        Assert.Equal(DomainRule.SlotUserInvalid, alternate.Rule);
        Assert.Empty(await fixture.Store.ListSlotsAsync(fixture.ProjectId, "rev-1", cancellationToken));
    }

    /// <summary>
    ///     Test that a member who was removed from the project can no longer sign, even though the slot is still
    ///     assigned to them: signing needs a membership, which a system administrator's rights do not replace.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_NotAMember_IsRefused()
    {
        // Arrange: a review whose primary reviewer is then removed from the project
        var (fixture, slot) = await CreateReadyAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var digest = (await ApprovalAsync(fixture)).Digest;
        await fixture.Store.RemoveMembershipAsync(
            fixture.ProjectId, fixture.Ids["ann"], AuditTestExtensions.Draft(), false, cancellationToken);

        // Act: the removed member tries to sign
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, Password, cancellationToken));

        // Assert: refused and nothing is signed
        Assert.Equal(DomainRule.ActorNotAllowed, exception.Rule);
        Assert.Empty(await fixture.Store.ListSignaturesAsync(fixture.ProjectId, "rev-1", cancellationToken));
    }

    /// <summary>
    ///     Test that the approval status marks an assignee who is no longer a member of the project, for the primary
    ///     and for the alternate, in the single read and in the batch read, and keeps the slot assigned.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_AssigneeRemovedFromProject_IsMarkedNotAMember()
    {
        // Arrange: a slot with primary ann and alternate bob; the control read shows both as members
        var (fixture, _) = await CreateReadyAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var control = (await ApprovalAsync(fixture)).Slots.Single();
        await fixture.Store.RemoveMembershipAsync(
            fixture.ProjectId, fixture.Ids["ann"], AuditTestExtensions.Draft(), false, cancellationToken);
        await fixture.Store.RemoveMembershipAsync(
            fixture.ProjectId, fixture.Ids["bob"], AuditTestExtensions.Draft(), false, cancellationToken);

        // Act
        var single = (await ApprovalAsync(fixture)).Slots.Single();
        var batchResult = await fixture.Signing.GetApprovalsAsync(fixture.ProjectId, ["rev-1"], cancellationToken);
        var batch = batchResult["rev-1"].Slots.Single();

        // Assert
        Assert.True(control.Primary.IsMember);
        Assert.True(control.Alternate!.IsMember);
        Assert.Equal("ann", single.Primary.Username);
        Assert.False(single.Primary.IsMember);
        Assert.False(single.Alternate!.IsMember);
        Assert.False(batch.Primary.IsMember);
        Assert.False(batch.Alternate!.IsMember);
    }

    /// <summary>
    ///     Test that the author of a review, and nobody else, is refused as primary or as alternate reviewer of a
    ///     new slot while independent reviewers are required (the default), and that no slot is stored.
    /// </summary>
    /// <param name="asPrimary"><c>true</c> to name the author as primary reviewer, <c>false</c> as alternate.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SigningService_AddSlotAsync_AuthorAsPrimaryOrAlternate_ThrowsReviewerNotIndependent(bool asPrimary)
    {
        // Arrange: a review that ann created
        var fixture = await CreateAsync();
        await CreateAuthoredReviewAsync(fixture, TestActors.Ann);
        var primary = asPrimary ? fixture.Ids["ann"] : fixture.Ids["bob"];
        int? alternate = asPrimary ? null : fixture.Ids["ann"];

        // Act: seat the author
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.AddSlotAsync(
            Cyd, fixture.ProjectId, "rev-2", primary, alternate, TestContext.Current.CancellationToken));
        var control = await AddSlotAsync(fixture, "rev-2", "bob", "cyd");

        // Assert: refused with the documented reason; a separate reviewer is accepted and is the only slot
        Assert.Equal(DomainRule.ReviewerNotIndependent, ex.Rule);
        Assert.Contains("Ann created this review", ex.Message);
        Assert.Single((await ApprovalAsync(fixture, "rev-2")).Slots);
        Assert.Equal(fixture.Ids["bob"], control.PrimaryUserId);
    }

    /// <summary>
    ///     Test that an existing slot cannot be changed to seat the author, as primary or as alternate.
    /// </summary>
    /// <param name="asPrimary"><c>true</c> to name the author as primary reviewer, <c>false</c> as alternate.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SigningService_UpdateSlotAsync_AuthorAsPrimaryOrAlternate_ThrowsReviewerNotIndependent(bool asPrimary)
    {
        // Arrange: a review that ann created, with a slot for bob
        var fixture = await CreateAsync();
        await CreateAuthoredReviewAsync(fixture, TestActors.Ann);
        var slot = await AddSlotAsync(fixture, "rev-2", "bob");
        var primary = asPrimary ? fixture.Ids["ann"] : fixture.Ids["bob"];
        int? alternate = asPrimary ? null : fixture.Ids["ann"];

        // Act: change the slot to seat the author
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.UpdateSlotAsync(
            Cyd, fixture.ProjectId, "rev-2", slot.Id, primary, alternate, TestContext.Current.CancellationToken));

        // Assert: refused, and the slot still has bob only
        Assert.Equal(DomainRule.ReviewerNotIndependent, ex.Rule);
        var stored = (await ApprovalAsync(fixture, "rev-2")).Slots.Single();
        Assert.Equal("bob", stored.Primary.Username);
        Assert.Null(stored.Alternate);
    }

    /// <summary>
    ///     Test that with the independence rule switched off the author can be seated and can sign, as before.
    /// </summary>
    [Fact]
    public async Task SigningService_AddSlotAsync_AuthorAndIndependenceOff_Succeeds()
    {
        // Arrange: a review that ann created, in a project that does not require independent reviewers
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await CreateAsync();
        await CreateAuthoredReviewAsync(fixture, TestActors.Ann);
        await TestServices.Projects(fixture.Store, fixture.Clock)
            .SetRequireIndependentReviewersAsync(Cyd, fixture.ProjectId, true, false, cancellationToken);

        // Act: seat the author and let the author sign
        var slot = await AddSlotAsync(fixture, "rev-2", "ann", "dee");
        var digest = (await ApprovalAsync(fixture, "rev-2")).Digest;
        await fixture.Signing.SignAsync(TestActors.Ann, fixture.ProjectId, "rev-2", slot.Id, digest, Password, cancellationToken);

        // Assert: the author's signature stands and approves the review
        var approval = await ApprovalAsync(fixture, "rev-2");
        Assert.Equal(SlotSignatureState.Signed, approval.Slots.Single().State);
        Assert.True(approval.IsApproved);
    }

    /// <summary>
    ///     Test that an author who was seated while the rule was off is refused at signing time once the rule is on
    ///     again, and that a separate reviewer can sign.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_AuthorInPreexistingSlot_ThrowsReviewerNotIndependent()
    {
        // Arrange: the author sits in a slot (rule off), a separate reviewer in another, and the rule is on again
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await CreateAsync();
        await CreateAuthoredReviewAsync(fixture, TestActors.Ann);
        var projects = TestServices.Projects(fixture.Store, fixture.Clock);
        await projects.SetRequireIndependentReviewersAsync(Cyd, fixture.ProjectId, true, false, cancellationToken);
        var own = await AddSlotAsync(fixture, "rev-2", "ann", "dee");
        var other = await AddSlotAsync(fixture, "rev-2", "bob");
        await projects.SetRequireIndependentReviewersAsync(Cyd, fixture.ProjectId, false, true, cancellationToken);
        var digest = (await ApprovalAsync(fixture, "rev-2")).Digest;

        // Act: the author signs, then the separate reviewer signs
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-2", own.Id, digest, Password, cancellationToken));
        await fixture.Signing.SignAsync(TestActors.Bob, fixture.ProjectId, "rev-2", other.Id, digest, Password, cancellationToken);

        // Assert: the author's attempt is refused and stored nothing; the separate reviewer's signature stands
        Assert.Equal(DomainRule.ReviewerNotIndependent, ex.Rule);
        var slots = (await ApprovalAsync(fixture, "rev-2")).Slots;
        Assert.Equal(SlotSignatureState.NotSigned, slots[0].State);
        Assert.Equal(SlotSignatureState.Signed, slots[1].State);
    }

    /// <summary>
    ///     Test that every slot change is refused with <see cref="DomainRule.ReviewLocked"/> while the review is
    ///     approved, and that nothing changes.
    /// </summary>
    [Fact]
    public async Task SigningService_AddUpdateRemoveSlotAsync_ApprovedReview_ThrowsReviewLocked()
    {
        // Arrange: an approved review
        var cancellationToken = TestContext.Current.CancellationToken;
        var (fixture, slot, digest) = await CreateApprovedAsync();

        // Act: add, update and remove a slot
        var add = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.AddSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", fixture.Ids["dee"], null, cancellationToken));
        var update = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.UpdateSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", slot.Id, fixture.Ids["bob"], null, cancellationToken));
        var remove = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.RemoveSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", slot.Id, cancellationToken));

        // Assert: all refused as locked, and the approval is untouched
        Assert.Equal(DomainRule.ReviewLocked, add.Rule);
        Assert.Equal(DomainRule.ReviewLocked, update.Rule);
        Assert.Equal(DomainRule.ReviewLocked, remove.Rule);
        var approval = await ApprovalAsync(fixture);
        Assert.True(approval.IsApproved);
        Assert.Equal(digest, approval.Digest);
        Assert.Single(approval.Slots);
    }

    /// <summary>
    ///     Test that signing an approved review is refused as locked, not as "already signed", even for the
    ///     alternate reviewer of the signed slot.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_ApprovedReview_ThrowsReviewLocked()
    {
        // Arrange: an approved review whose slot was signed by the primary reviewer
        var (fixture, slot, digest) = await CreateApprovedAsync();

        // Act: the alternate reviewer tries to sign the same slot
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
            TestActors.Bob, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken));

        // Assert: refused as locked
        Assert.Equal(DomainRule.ReviewLocked, ex.Rule);
        Assert.Equal(ReviewLock.LockedMessage, ex.Message);
    }

    /// <summary>
    ///     Test that reopening is refused for somebody who does not administer the project, with the real access
    ///     rules, and that nothing is recorded.
    /// </summary>
    [Fact]
    public async Task SigningService_ReopenReviewAsync_NotAdmin_ThrowsActorNotAllowed()
    {
        // Arrange: a project with an administrator, a plain member and an approved review, under the real access rules
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var userStore = new FakeUserStore();
        var clock = new MovableTimeProvider(_now);
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        await users.CreateAsync("root", "Root", true, Password, Actor.CommandLine, cancellationToken);
        var member = await users.CreateAsync("mem", "Mem", false, Password, Actor.CommandLine, cancellationToken);
        var access = new ProjectAccess(userStore, store);
        var rootActor = new Actor("root", "Root");
        var project = await new ProjectService(store, userStore, access, new FakePasswordHasher(), clock)
            .CreateAsync("Apollo", null, rootActor, cancellationToken);
        await store.AddMembershipAsync(member.Id, project.Id, ProjectRole.User, cancellationToken);
        await TestServices.Reviews(store, clock).CreateDraftAsync(
            project.Id, "rev-1", "First", [new("a.txt", TestContent.A)], rootActor, cancellationToken);
        var signing = new SigningService(store, users, access, clock);

        // Act: the member reopens
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => signing.ReopenReviewAsync(
            new Actor("mem", "Mem"), project.Id, "rev-1", "Because.", cancellationToken));

        // Assert: refused, and nothing was recorded
        Assert.Equal(DomainRule.ActorNotAllowed, ex.Rule);
        Assert.DoesNotContain(store.Audits, a => a.Action == "review.reopened");
        Assert.Empty(await store.ListReopensAsync(project.Id, "rev-1", cancellationToken));
    }

    /// <summary>
    ///     Test that a missing, blank or overlong reason is refused with <see cref="DomainRule.ReopenReasonInvalid"/>
    ///     and the review stays locked; the limit itself is accepted.
    /// </summary>
    /// <param name="reason">The reason to try.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SigningService_ReopenReviewAsync_InvalidReason_ThrowsReopenReasonInvalid(string? reason)
    {
        // Arrange: an approved review
        var cancellationToken = TestContext.Current.CancellationToken;
        var (fixture, _, _) = await CreateApprovedAsync();

        // Act: reopen without a usable reason, and with one character too many
        var empty = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.ReopenReviewAsync(
            Cyd, fixture.ProjectId, "rev-1", reason, cancellationToken));
        var tooLong = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.ReopenReviewAsync(
            Cyd, fixture.ProjectId, "rev-1", new string('x', ReviewLimits.MaxReopenReasonLength + 1), cancellationToken));

        // Assert: both refused, and the review is still locked
        Assert.Equal(DomainRule.ReopenReasonInvalid, empty.Rule);
        Assert.Equal(DomainRule.ReopenReasonInvalid, tooLong.Rule);
        Assert.True((await ApprovalAsync(fixture)).IsApproved);
        await fixture.Signing.ReopenReviewAsync(
            Cyd, fixture.ProjectId, "rev-1", new string('x', ReviewLimits.MaxReopenReasonLength), cancellationToken);
        Assert.False((await ApprovalAsync(fixture)).IsApproved);
    }

    /// <summary>
    ///     Test that a review that is not approved cannot be reopened.
    /// </summary>
    [Fact]
    public async Task SigningService_ReopenReviewAsync_NotLocked_ThrowsReviewNotLocked()
    {
        // Arrange: a review with a slot that is not signed
        var (fixture, _) = await CreateReadyAsync();

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.ReopenReviewAsync(
            Cyd, fixture.ProjectId, "rev-1", "Because.", TestContext.Current.CancellationToken));

        // Assert: nothing was recorded
        Assert.Equal(DomainRule.ReviewNotLocked, ex.Rule);
        Assert.DoesNotContain(fixture.Store.Audits, a => a.Action == "review.reopened");
    }

    /// <summary>
    ///     Test that an API token cannot reopen a review, even when its user administers the project.
    /// </summary>
    [Fact]
    public async Task SigningService_ReopenReviewAsync_ApiToken_ThrowsActorNotAllowed()
    {
        // Arrange: an approved review and an administrator's identity arriving through a token
        var (fixture, _, _) = await CreateApprovedAsync();
        var viaApi = new Actor("cyd", "Cyd") { ApiTokenId = "K7M2QX9P" };

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.ReopenReviewAsync(
            viaApi, fixture.ProjectId, "rev-1", "Because.", TestContext.Current.CancellationToken));

        // Assert: refused, and the review is still approved
        Assert.Equal(DomainRule.ActorNotAllowed, ex.Rule);
        Assert.True((await ApprovalAsync(fixture)).IsApproved);
    }

    /// <summary>
    ///     Test that an administrator's reopen returns the review to In review, keeps the old signature as history
    ///     (out of date), changes the digest, and records the history entry and the audit entry with the reason and
    ///     the previous digest.
    /// </summary>
    [Fact]
    public async Task SigningService_ReopenReviewAsync_Admin_ReturnsToInReviewKeepsSignaturesAndAudits()
    {
        // Arrange: an approved review
        var cancellationToken = TestContext.Current.CancellationToken;
        var (fixture, _, digest) = await CreateApprovedAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act: reopen with a reason that has blanks around it
        await fixture.Signing.ReopenReviewAsync(Cyd, fixture.ProjectId, "rev-1", "  Missed a case.  ", cancellationToken);

        // Assert: not approved, the old signature is kept but out of date, and the digest moved on
        var approval = await ApprovalAsync(fixture);
        Assert.False(approval.IsApproved);
        Assert.NotEqual(digest, approval.Digest);
        Assert.Equal(SlotSignatureState.OutOfDate, approval.Slots.Single().State);
        Assert.Single(await fixture.Store.ListSignaturesAsync(fixture.ProjectId, "rev-1", cancellationToken));

        // Assert: the history says who, when, why and which digest was superseded
        var reopen = Assert.Single(approval.Reopens!);
        Assert.Equal("cyd", reopen.ReopenedBy);
        Assert.Equal("Cyd", reopen.ReopenedByDisplayName);
        Assert.Equal("Missed a case.", reopen.Reason);
        Assert.Equal(digest, reopen.PreviousDigest);
        Assert.Equal(_now.AddMinutes(5), reopen.ReopenedAt);

        // Assert: the audit chain says the same
        var audit = Assert.Single(fixture.Store.Audits, a => a.Action == "review.reopened");
        Assert.Equal($"previous digest {digest}; reason: Missed a case.", audit.Detail);
        Assert.Equal("rev-1", audit.ReviewId);
        Assert.Equal("Cyd (cyd)", audit.ActorName);
    }

    /// <summary>
    ///     Test that a reopen of a review that does not exist is refused with <see cref="DomainRule.ReviewNotFound"/>
    ///     and records nothing.
    /// </summary>
    [Fact]
    public async Task SigningService_ReopenReviewAsync_MissingReview_ThrowsReviewNotFound()
    {
        // Arrange: a project without that review
        var fixture = await CreateAsync();

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.ReopenReviewAsync(
            Cyd, fixture.ProjectId, "nope", "Because.", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DomainRule.ReviewNotFound, ex.Rule);
        Assert.DoesNotContain(fixture.Store.Audits, a => a.Action == "review.reopened");
    }

    /// <summary>
    ///     Test that the limit of the reason is counted after trimming: a reason of exactly the limit with blanks
    ///     around it is accepted and stored trimmed, and one character more is refused.
    /// </summary>
    [Fact]
    public async Task SigningService_ReopenReviewAsync_ReasonAtLimitWithBlanks_IsAcceptedTrimmed()
    {
        // Arrange: an approved review
        var cancellationToken = TestContext.Current.CancellationToken;
        var (fixture, _, _) = await CreateApprovedAsync();
        var limit = new string('x', ReviewLimits.MaxReopenReasonLength);

        // Act: one character too many (with blanks) is refused, the limit with blanks is accepted
        var tooLong = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.ReopenReviewAsync(
            Cyd, fixture.ProjectId, "rev-1", $"  {limit}y\t", cancellationToken));
        await fixture.Signing.ReopenReviewAsync(Cyd, fixture.ProjectId, "rev-1", $"  {limit}\t\n", cancellationToken);

        // Assert: the refusal names the reason rule, and the stored reason is trimmed
        Assert.Equal(DomainRule.ReopenReasonInvalid, tooLong.Rule);
        var reopen = Assert.Single(await fixture.Store.ListReopensAsync(fixture.ProjectId, "rev-1", cancellationToken));
        Assert.Equal(limit, reopen.Reason);
        Assert.EndsWith($"reason: {limit}", Assert.Single(fixture.Store.Audits, a => a.Action == "review.reopened").Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test, with the real access rules, that the administrator of the project and a system administrator who is
    ///     not a member can reopen an approved review, that the audit trail names who did it, and that a plain member
    ///     and a user who cannot see the project cannot.
    /// </summary>
    /// <param name="username">The user who reopens.</param>
    /// <param name="allowed">Whether the reopening is allowed.</param>
    /// <param name="rule">The rule of the refusal, when it is not allowed.</param>
    [Theory]
    [InlineData("pro", true, null)]
    [InlineData("root", true, null)]
    [InlineData("mem", false, DomainRule.ActorNotAllowed)]
    [InlineData("out", false, DomainRule.ProjectNotFound)]
    public async Task SigningService_ReopenReviewAsync_RealAccessRules_ReopensOnlyForAdministrators(
        string username,
        bool allowed,
        DomainRule? rule)
    {
        // Arrange: an approved review in a project with a system administrator, a project administrator,
        // a member and an outsider
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await CreateRealAccessWorldAsync();
        var actor = new Actor(username, username);

        // Act
        var attempt = world.Signing.ReopenReviewAsync(actor, world.ProjectId, "rev-1", "Look again.", cancellationToken);

        // Assert
        if (allowed)
        {
            await attempt;
            var audit = Assert.Single(world.Store.Audits, a => a.Action == "review.reopened");
            Assert.Equal($"{username} ({username})", audit.ActorName);
            Assert.False((await world.Signing.GetApprovalAsync(world.ProjectId, "rev-1", cancellationToken))!.IsApproved);
            var reopen = Assert.Single(await world.Store.ListReopensAsync(world.ProjectId, "rev-1", cancellationToken));
            Assert.Equal(username, reopen.ReopenedBy);
        }
        else
        {
            var failure = await Assert.ThrowsAsync<DomainRuleException>(() => attempt);
            Assert.Equal(rule, failure.Rule);
            Assert.DoesNotContain(world.Store.Audits, a => a.Action == "review.reopened");
            Assert.True((await world.Signing.GetApprovalAsync(world.ProjectId, "rev-1", cancellationToken))!.IsApproved);
        }
    }

    /// <summary>
    ///     Test, with the real access rules, that whoever may see the project can add, change and remove a slot, so a
    ///     system administrator who is not a member can too, while a user who cannot see the project is refused as if
    ///     the project did not exist.
    /// </summary>
    [Fact]
    public async Task SigningService_SlotChanges_RealAccessRules_NeedViewAccessOnly()
    {
        // Arrange: a project with a plain member as the only reviewer candidate, an unapproved review
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await CreateRealAccessWorldAsync(approve: false);
        var admin = new Actor("root", "root");
        var outsider = new Actor("out", "out");
        var auditsBefore = world.Store.Audits.Count;

        // Act: the outsider tries every change; the system administrator, who is no member, makes them
        var add = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.AddSlotAsync(
            outsider, world.ProjectId, "rev-1", world.MemberId, null, cancellationToken));
        var update = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.UpdateSlotAsync(
            outsider, world.ProjectId, "rev-1", world.SlotId, world.MemberId, null, cancellationToken));
        var remove = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.RemoveSlotAsync(
            outsider, world.ProjectId, "rev-1", world.SlotId, cancellationToken));
        await world.Signing.UpdateSlotAsync(
            admin, world.ProjectId, "rev-1", world.SlotId, world.MemberId, world.ProjectAdminId, cancellationToken);
        await world.Signing.RemoveSlotAsync(admin, world.ProjectId, "rev-1", world.SlotId, cancellationToken);
        var added = await world.Signing.AddSlotAsync(
            admin, world.ProjectId, "rev-1", world.ProjectAdminId, null, cancellationToken);

        // Assert: the outsider is told the project does not exist, and the administrator's changes are recorded
        Assert.Equal(DomainRule.ProjectNotFound, add.Rule);
        Assert.Equal(DomainRule.ProjectNotFound, update.Rule);
        Assert.Equal(DomainRule.ProjectNotFound, remove.Rule);
        var recorded = world.Store.Audits.Skip(auditsBefore).ToList();
        Assert.Equal(["slot.updated", "slot.removed", "slot.added"], recorded.Select(a => a.Action));
        Assert.All(recorded, a => Assert.Equal("root (root)", a.ActorName));
        Assert.Equal(world.ProjectAdminId, added.PrimaryUserId);
    }

    /// <summary>
    ///     Test that the old signature stays out of date after a reopen even when the content is changed back to what
    ///     it was, that a fresh signature is needed to approve the review again, and that the old signature stays in
    ///     the history.
    /// </summary>
    [Fact]
    public async Task SigningService_ReopenReviewAsync_ThenResign_ApprovesAgainWithBothSignatures()
    {
        // Arrange: an approved review that is reopened
        var cancellationToken = TestContext.Current.CancellationToken;
        var (fixture, slot, oldDigest) = await CreateApprovedAsync();
        await fixture.Signing.ReopenReviewAsync(Cyd, fixture.ProjectId, "rev-1", "Look again.", cancellationToken);

        // Act: change an answer and change it back, so the content is what it was when the old signature was made
        var itemId = DefaultChecklist.Current.Sections[0].Items[0].Id;
        await fixture.Checklists.SetAnswerAsync(
            fixture.ProjectId, "rev-1", itemId, ChecklistOutcome.NotApplicable, "Not relevant.", TestActors.Ann, cancellationToken);
        await fixture.Checklists.SetAnswerAsync(
            fixture.ProjectId, "rev-1", itemId, ChecklistOutcome.Pass, null, TestActors.Ann, cancellationToken);
        var reverted = await ApprovalAsync(fixture);
        var newDigest = reverted.Digest;
        await fixture.Signing.SignAsync(TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, newDigest, Password, cancellationToken);

        // Assert: the reverted content did not revive the old signature, which was out of date until the new one
        Assert.False(reverted.IsApproved);
        Assert.Equal(SlotSignatureState.OutOfDate, reverted.Slots.Single().State);
        Assert.NotEqual(oldDigest, newDigest);

        // Assert: approved again for the new digest, with the old and the new signature both stored
        var approval = await ApprovalAsync(fixture);
        Assert.True(approval.IsApproved);
        Assert.Equal(newDigest, approval.Digest);
        var signatures = await fixture.Store.ListSignaturesAsync(fixture.ProjectId, "rev-1", cancellationToken);
        Assert.Equal(2, signatures.Count);
        Assert.Contains(signatures, s => s.RecordDigest == oldDigest);
        Assert.Contains(signatures, s => s.RecordDigest == newDigest);
    }

    /// <summary>
    ///     Test that a signature is refused for the reasons that come before the password check without asking for
    ///     the password: a wrong password given again and again neither counts toward the lockout nor hides the real
    ///     reason, so the signer can still sign with the right password once the cause is gone.
    /// </summary>
    /// <param name="scenario">
    ///     The reason: <c>stale</c> (the record changed), <c>blocker</c> (an open thread) or <c>notReviewer</c> (the
    ///     signer is a member who is not in the slot).
    /// </param>
    [Theory]
    [InlineData("stale")]
    [InlineData("blocker")]
    [InlineData("notReviewer")]
    public async Task SigningService_SignAsync_RefusedBeforePassword_DoesNotCountWrongPasswords(string scenario)
    {
        // Arrange: a review that can be signed by ann, optionally with an open thread
        var cancellationToken = TestContext.Current.CancellationToken;
        var (fixture, slot) = await CreateReadyAsync();
        ReviewThread? thread = null;
        if (scenario == "blocker")
        {
            thread = await fixture.Threads.StartThreadAsync(
                fixture.ProjectId, "rev-1", new ThreadAnchor("a.txt"), TestActors.Bob, "Open remark", cancellationToken);
        }

        var digest = (await ApprovalAsync(fixture)).Digest;
        var signer = scenario == "notReviewer" ? TestActors.Dee : TestActors.Ann;
        var attempted = scenario == "stale" ? "stale-digest" : digest;
        var expected = scenario switch
        {
            "stale" => DomainRule.RecordChanged,
            "blocker" => DomainRule.SigningNotAllowed,
            _ => DomainRule.ActorNotAllowed
        };

        // Act: more wrong passwords than the lockout allows, each refused for the real reason
        var rules = new List<DomainRule>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var failure = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
                signer, fixture.ProjectId, "rev-1", slot.Id, attempted, "wrong password here", cancellationToken));
            rules.Add(failure.Rule);
        }

        // Act: the cause is removed, and the signer who was refused signs with the right password
        if (thread is not null)
        {
            await fixture.Threads.ResolveAsync(fixture.ProjectId, "rev-1", thread.Id, cancellationToken);
        }

        if (scenario == "notReviewer")
        {
            // dee becomes the primary reviewer of the still unsigned slot; ann's answers leave with her, so dee answers
            await fixture.Signing.UpdateSlotAsync(
                Cyd, fixture.ProjectId, "rev-1", slot.Id, fixture.Ids["dee"], null, cancellationToken);
            await AuditTestExtensions.AnswerAllAsync(fixture.Checklists, fixture.ProjectId, "rev-1", TestActors.Dee, cancellationToken);
        }

        var signature = await fixture.Signing.SignAsync(
            signer,
            fixture.ProjectId,
            "rev-1",
            slot.Id,
            (await ApprovalAsync(fixture)).Digest,
            Password,
            cancellationToken);

        // Assert: the real reason every time, no lockout, and the signature of the refused signer is stored
        Assert.All(rules, rule => Assert.Equal(expected, rule));
        Assert.Equal(fixture.Ids[scenario == "notReviewer" ? "dee" : "ann"], signature.UserId);
    }

    /// <summary>
    ///     Approves <c>rev-1</c> with slot 1 (cyd primary, ann alternate) signed by ann, so that cyd can reopen the
    ///     review and still leave a person who may sign.
    /// </summary>
    /// <returns>The fixture and the slot.</returns>
    private static async Task<(Fixture Fixture, ReviewerSlot Slot)> CreateApprovedWithAlternateAsync()
    {
        var fixture = await CreateAsync();
        await AnswerAllAsync(fixture);
        var slot = await AddSlotAsync(fixture, "rev-1", "cyd", "ann");
        await fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, (await ApprovalAsync(fixture)).Digest, Password, TestContext.Current.CancellationToken);
        return (fixture, slot);
    }

    /// <summary>
    ///     Test that the person who reopened a review cannot sign it again while the rule is on: the refusal comes
    ///     before the password, so repeated wrong passwords neither count nor leave a re-authentication entry, and the
    ///     other person of the slot can sign.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_ReopenerWhileRuleIsOn_ThrowsReopenerCannotSignBeforePassword()
    {
        // Arrange: an approved review that cyd (primary of the slot) reopens
        var cancellationToken = TestContext.Current.CancellationToken;
        var (fixture, slot) = await CreateApprovedWithAlternateAsync();
        await fixture.Signing.ReopenReviewAsync(Cyd, fixture.ProjectId, "rev-1", "Look again.", cancellationToken);
        var digest = (await ApprovalAsync(fixture)).Digest;

        // Act: cyd tries more wrong passwords than the lockout allows
        var rules = new List<DomainRule>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var failure = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
                Cyd, fixture.ProjectId, "rev-1", slot.Id, digest, "wrong password here", cancellationToken));
            rules.Add(failure.Rule);
        }

        // Act: the alternate signs with the right password
        var signature = await fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, Password, cancellationToken);

        // Assert: the rule every time, no re-authentication entries, and ann's signature is stored
        Assert.All(rules, rule => Assert.Equal(DomainRule.ReopenerCannotSign, rule));
        Assert.DoesNotContain(fixture.Store.Audits, a => a.Action == "signature.reauth-failed");
        Assert.Equal(fixture.Ids["ann"], signature.UserId);
        Assert.True((await ApprovalAsync(fixture)).IsApproved);
    }

    /// <summary>
    ///     Test that an earlier reopener stays blocked after a later reopen by somebody else.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_EarlierReopener_StaysBlocked()
    {
        // Arrange: cyd reopens, ann signs again, and then bob (who holds no slot) reopens again
        var cancellationToken = TestContext.Current.CancellationToken;
        var (fixture, slot) = await CreateApprovedWithAlternateAsync();
        await fixture.Signing.ReopenReviewAsync(Cyd, fixture.ProjectId, "rev-1", "Look again.", cancellationToken);
        await fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, (await ApprovalAsync(fixture)).Digest, Password, cancellationToken);
        await fixture.Signing.ReopenReviewAsync(TestActors.Bob, fixture.ProjectId, "rev-1", "And again.", cancellationToken);

        // Act: cyd, who reopened first, tries to sign
        var digest = (await ApprovalAsync(fixture)).Digest;
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
            Cyd, fixture.ProjectId, "rev-1", slot.Id, digest, Password, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ReopenerCannotSign, ex.Rule);
    }

    /// <summary>
    ///     Test that the reopener signs again once the project administrator has switched the rule off, and that a
    ///     signature on a review that was never reopened is not affected by the rule.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_ReopenerWhileRuleIsOff_Signs()
    {
        // Arrange: the rule is off, and cyd reopens an approved review
        var cancellationToken = TestContext.Current.CancellationToken;
        var (fixture, slot) = await CreateApprovedWithAlternateAsync();
        await TestServices.Projects(fixture.Store, fixture.Clock).SetReviewRuleOffAsync(
            fixture.ProjectId, ReviewRule.NonReopenerSigning, cancellationToken);
        await fixture.Signing.ReopenReviewAsync(Cyd, fixture.ProjectId, "rev-1", "Look again.", cancellationToken);

        // Act: cyd signs again
        var signature = await fixture.Signing.SignAsync(
            Cyd, fixture.ProjectId, "rev-1", slot.Id, (await ApprovalAsync(fixture)).Digest, Password, cancellationToken);

        // Assert
        Assert.Equal(fixture.Ids["cyd"], signature.UserId);
        Assert.True((await ApprovalAsync(fixture)).IsApproved);
    }

    /// <summary>
    ///     Test that the person who reopened a review, while barred from signing it, can still answer a check and hand
    ///     in the ballot of the own slot.
    /// </summary>
    [Fact]
    public async Task ChecklistService_Ballot_ReopenerWhileRuleIsOn_EditsAndSubmitsButCannotSign()
    {
        // Arrange: cyd (primary of the only slot) reopens an approved review
        var cancellationToken = TestContext.Current.CancellationToken;
        var (fixture, slot) = await CreateApprovedWithAlternateAsync();
        var firstItem = DefaultChecklist.Current.Sections[0].Items[0].Id;
        await fixture.Signing.ReopenReviewAsync(Cyd, fixture.ProjectId, "rev-1", "Look again.", cancellationToken);

        // Act: cyd changes an answer and puts it back, then hands the ballot in
        await fixture.Checklists.SetAnswerAsync(
            fixture.ProjectId, "rev-1", firstItem, ChecklistOutcome.Fail, "Wrong.", Cyd, cancellationToken);
        await fixture.Checklists.SetAnswerAsync(
            fixture.ProjectId, "rev-1", firstItem, ChecklistOutcome.Pass, null, Cyd, cancellationToken);
        var submitted = await fixture.Checklists.SubmitBallotAsync(fixture.ProjectId, "rev-1", Cyd, cancellationToken);
        var digest = (await ApprovalAsync(fixture)).Digest;
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
            Cyd, fixture.ProjectId, "rev-1", slot.Id, digest, Password, cancellationToken));

        // Assert: the ballot is handed in, and only the signature is refused
        Assert.Equal(BallotStatus.Submitted, submitted.Status);
        Assert.Equal(ChecklistOutcome.Pass, submitted.Answers.Single(a => a.ItemId == firstItem).Outcome);
        Assert.Equal(DomainRule.ReopenerCannotSign, ex.Rule);
    }

    /// <summary>
    ///     Test that a reopen leaves the submitted ballots submitted and, through the reopen count, makes every earlier
    ///     signature out of date for good, even though the ballots still say what they said.
    /// </summary>
    [Fact]
    public async Task SigningService_ReopenReviewAsync_BallotsStaySubmitted()
    {
        // Arrange: an approved review
        var cancellationToken = TestContext.Current.CancellationToken;
        var (fixture, _, digest) = await CreateApprovedAsync();

        // Act
        await fixture.Signing.ReopenReviewAsync(Cyd, fixture.ProjectId, "rev-1", "Look again.", cancellationToken);
        var record = (await fixture.Store.GetSnapshotAsync(fixture.ProjectId, "rev-1", cancellationToken))!.Record;
        var approval = await ApprovalAsync(fixture);

        // Assert: the ballot is still handed in, the reopen count moved the digest, and the same record with the old
        // reopen count still has the digest the signature was given for
        Assert.All(record.Ballots, ballot => Assert.Equal(BallotStatus.Submitted, ballot.Status));
        Assert.NotEmpty(record.Ballots);
        Assert.Equal(1, record.Reopens);
        Assert.Equal(digest, RecordDigest.Compute(record with { Reopens = 0 }));
        Assert.NotEqual(digest, approval.Digest);
        Assert.Equal(SlotSignatureState.OutOfDate, approval.Slots.Single().State);
    }

    /// <summary>
    ///     Test that a reopen that would leave a slot in which nobody may sign is refused with
    ///     <see cref="DomainRule.ReopenWouldStrandSlot"/>, naming the slot, and that nothing is recorded.
    /// </summary>
    [Fact]
    public async Task SigningService_ReopenReviewAsync_WouldStrandSlot_ThrowsAndRecordsNothing()
    {
        // Arrange: two slots, one of ann alone and one of bob alone, both signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await CreateAsync();
        await AnswerAllAsync(fixture);
        var first = await AddSlotAsync(fixture, "rev-1", "ann");
        var second = await AddSlotAsync(fixture, "rev-1", "bob");
        var digest = (await ApprovalAsync(fixture)).Digest;
        await fixture.Signing.SignAsync(TestActors.Ann, fixture.ProjectId, "rev-1", first.Id, digest, Password, cancellationToken);
        await fixture.Signing.SignAsync(TestActors.Bob, fixture.ProjectId, "rev-1", second.Id, digest, Password, cancellationToken);

        // Act: ann reopens
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.ReopenReviewAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", "Look again.", cancellationToken));

        // Assert: refused naming slot 1 only, still approved, nothing recorded
        Assert.Equal(DomainRule.ReopenWouldStrandSlot, ex.Rule);
        Assert.Equal("Slot 1 would have no one who may sign; ask another administrator or switch the rule off.", ex.Message);
        Assert.True((await ApprovalAsync(fixture)).IsApproved);
        Assert.Empty(await fixture.Store.ListReopensAsync(fixture.ProjectId, "rev-1", cancellationToken));
        Assert.DoesNotContain(fixture.Store.Audits, a => a.Action == "review.reopened");
    }

    /// <summary>
    ///     Test that earlier reopens count in the guard: after cyd reopened a slot of cyd and ann, a reopen by ann
    ///     would leave nobody for it and is refused, while a reopen by somebody without a slot is allowed.
    /// </summary>
    [Fact]
    public async Task SigningService_ReopenReviewAsync_EarlierReopenerCounts_ThrowsForTheLastFreePerson()
    {
        // Arrange: slot of cyd and ann, reopened by cyd and signed again by ann
        var cancellationToken = TestContext.Current.CancellationToken;
        var (fixture, slot) = await CreateApprovedWithAlternateAsync();
        await fixture.Signing.ReopenReviewAsync(Cyd, fixture.ProjectId, "rev-1", "Look again.", cancellationToken);
        await fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, (await ApprovalAsync(fixture)).Digest, Password, cancellationToken);

        // Act: ann reopens; then bob, who holds no slot, reopens
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.ReopenReviewAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", "Once more.", cancellationToken));
        await fixture.Signing.ReopenReviewAsync(TestActors.Bob, fixture.ProjectId, "rev-1", "Once more.", cancellationToken);

        // Assert
        Assert.Equal(DomainRule.ReopenWouldStrandSlot, ex.Rule);
        Assert.Equal(2, (await fixture.Store.ListReopensAsync(fixture.ProjectId, "rev-1", cancellationToken)).Count);
    }

    /// <summary>
    ///     Test that the guard is not applied while the rule is off: the only reviewer may reopen the review.
    /// </summary>
    [Fact]
    public async Task SigningService_ReopenReviewAsync_WouldStrandSlotWhileRuleIsOff_Reopens()
    {
        // Arrange: a slot of ann alone, signed, and the rule off
        var cancellationToken = TestContext.Current.CancellationToken;
        var (fixture, slot) = await CreateReadyAsync();
        await TestServices.Projects(fixture.Store, fixture.Clock).SetReviewRuleOffAsync(
            fixture.ProjectId, ReviewRule.NonReopenerSigning, cancellationToken);
        await fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, (await ApprovalAsync(fixture)).Digest, Password, cancellationToken);

        // Act: ann (the primary of the slot) reopens
        await fixture.Signing.ReopenReviewAsync(TestActors.Ann, fixture.ProjectId, "rev-1", "Look again.", cancellationToken);

        // Assert
        Assert.False((await ApprovalAsync(fixture)).IsApproved);
        Assert.Single(await fixture.Store.ListReopensAsync(fixture.ProjectId, "rev-1", cancellationToken));
    }

    /// <summary>
    ///     Test that the constructor refuses null arguments.
    /// </summary>
    [Fact]
    public void SigningService_Constructor_NullArguments_Throw()
    {
        // Arrange
        var store = new FakeReviewStore();
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), TimeProvider.System);
        var access = new AllowAllProjectAccess(store);

        // Act, Assert: each null is refused
        Assert.Throws<ArgumentNullException>(() => new SigningService(null!, users, access, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new SigningService(store, null!, access, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new SigningService(store, users, null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new SigningService(store, users, access, null!));
    }

    /// <summary>
    ///     Creates a project under the real access rules with a system administrator who is not a member (<c>root</c>),
    ///     a project administrator (<c>pro</c>), a plain member (<c>mem</c>) and a user outside the project
    ///     (<c>out</c>), and the review <c>rev-1</c> with one slot whose primary reviewer is <c>mem</c>.
    /// </summary>
    /// <param name="approve">Whether <c>mem</c> signs the slot, so that the review is approved and locked.</param>
    /// <returns>The prepared world.</returns>
    private static async Task<RealAccessWorld> CreateRealAccessWorldAsync(bool approve = true)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var userStore = new FakeUserStore();
        var clock = new MovableTimeProvider(_now);
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        await users.CreateAsync("root", "root", true, Password, Actor.CommandLine, cancellationToken);
        var projectAdmin = await users.CreateAsync("pro", "pro", false, Password, Actor.CommandLine, cancellationToken);
        var member = await users.CreateAsync("mem", "mem", false, Password, Actor.CommandLine, cancellationToken);
        await users.CreateAsync("out", "out", false, Password, Actor.CommandLine, cancellationToken);
        var access = new ProjectAccess(userStore, store);
        var rootActor = new Actor("root", "root");
        var project = await new ProjectService(store, userStore, access, new FakePasswordHasher(), clock)
            .CreateAsync("Apollo", null, rootActor, cancellationToken);
        await store.AddMembershipAsync(projectAdmin.Id, project.Id, ProjectRole.ProjectAdmin, cancellationToken);
        await store.AddMembershipAsync(member.Id, project.Id, ProjectRole.User, cancellationToken);
        await TestServices.Reviews(store, clock).CreateDraftAsync(
            project.Id, "rev-1", "First", [new("a.txt", TestContent.A)], rootActor, cancellationToken);
        var userActor = new Actor("mem", "mem");
        var checklists = TestServices.Checklists(store, clock, userStore);
        var signing = new SigningService(store, users, access, clock);

        // The system administrator is not a member, and still arranges the slot
        var slot = await signing.AddSlotAsync(rootActor, project.Id, "rev-1", member.Id, null, cancellationToken);
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            // root created the review and may not answer it, so the member of the slot does; the one ballot is handed in
            await checklists.SetAnswerAsync(
                project.Id, "rev-1", item.Id, ChecklistOutcome.Pass, null, userActor, cancellationToken);
        }

        if (approve)
        {
            var digest = (await signing.GetApprovalAsync(project.Id, "rev-1", cancellationToken))!.Digest;
            await signing.SignAsync(
                userActor, project.Id, "rev-1", slot.Id, digest, Password, cancellationToken);
        }

        return new RealAccessWorld(signing, store, project.Id, member.Id, projectAdmin.Id, slot.Id);
    }

    /// <summary>
    ///     The services and identifiers of a test under the real access rules.
    /// </summary>
    /// <param name="Signing">The service under test.</param>
    /// <param name="Store">The review store.</param>
    /// <param name="ProjectId">The identity of the project.</param>
    /// <param name="MemberId">The identity of <c>mem</c>.</param>
    /// <param name="ProjectAdminId">The identity of <c>pro</c>.</param>
    /// <param name="SlotId">The identity of the slot of <c>mem</c>.</param>
    private sealed record RealAccessWorld(
        SigningService Signing,
        FakeReviewStore Store,
        int ProjectId,
        int MemberId,
        int ProjectAdminId,
        int SlotId);

    /// <summary>
    ///     The services and identifiers of one test.
    /// </summary>
    /// <param name="Store">The review store.</param>
    /// <param name="Clock">The clock of the signing service.</param>
    /// <param name="Users">The user service.</param>
    /// <param name="Signing">The service under test.</param>
    /// <param name="Threads">The thread service.</param>
    /// <param name="Checklists">The checklist service.</param>
    /// <param name="ProjectId">The identity of the project.</param>
    /// <param name="Ids">The identity of each user, by user name.</param>
    private sealed record Fixture(
        FakeReviewStore Store,
        MovableTimeProvider Clock,
        UserService Users,
        SigningService Signing,
        ThreadService Threads,
        ChecklistService Checklists,
        int ProjectId,
        IReadOnlyDictionary<string, int> Ids)
    {
        /// <summary>
        ///     Gets the reviews that are marked as answered: the primary reviewer of every slot of such a review
        ///     answers every check and hands the ballot in, as soon as the slot exists.
        /// </summary>
        public HashSet<string> Answered { get; } = [];
    }
}
