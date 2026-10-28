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
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), clock);
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
            TestServices.Checklists(store, clock),
            project.Id,
            ids);
    }

    /// <summary>
    ///     Answers every check of the checklist as Pass.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <returns>A task that completes when all checks are answered.</returns>
    private static async Task AnswerAllAsync(Fixture fixture)
    {
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await fixture.Checklists.SetAnswerAsync(
                fixture.ProjectId, "rev-1", item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Creates a slot with ann as primary and bob as alternate, on a review that can be signed.
    /// </summary>
    /// <returns>The fixture and the slot.</returns>
    private static async Task<(Fixture Fixture, ReviewerSlot Slot)> CreateReadyAsync()
    {
        var fixture = await CreateAsync();
        await AnswerAllAsync(fixture);
        var slot = await fixture.Signing.AddSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", fixture.Ids["ann"], fixture.Ids["bob"], TestContext.Current.CancellationToken);
        return (fixture, slot);
    }

    /// <summary>
    ///     Reads the approval status of <c>rev-1</c>.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <returns>The status.</returns>
    private static async Task<ReviewApproval> ApprovalAsync(Fixture fixture)
    {
        return (await fixture.Signing.GetApprovalAsync(fixture.ProjectId, "rev-1", TestContext.Current.CancellationToken))!;
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

        // Act: sign with everything right
        var right = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
            viaApi, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken));

        // Act: sign with everything wrong, which must give the same answer because nothing else is checked
        var wrong = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
            viaApi, fixture.ProjectId, "rev-999", 12345, "stale", "wrong password here", TestContext.Current.CancellationToken));

        // Assert: refused as not allowed both times, and the review is still unsigned
        Assert.Equal(DomainRule.ActorNotAllowed, right.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, wrong.Rule);
        Assert.All((await ApprovalAsync(fixture)).Slots, s => Assert.Equal(SlotSignatureState.NotSigned, s.State));
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
        // Arrange: a slot signed by ann, optionally followed by a change of the record
        var (fixture, slot) = await CreateReadyAsync();
        var digest = (await ApprovalAsync(fixture)).Digest;
        await fixture.Signing.SignAsync(TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken);
        if (outOfDate)
        {
            await fixture.Threads.StartThreadAsync(
                fixture.ProjectId, "rev-1", "a.txt", null, null, TestActors.Bob, "A late remark", TestContext.Current.CancellationToken);
        }

        // Act: try to change and to remove the slot
        var update = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.UpdateSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", slot.Id, fixture.Ids["cyd"], null, TestContext.Current.CancellationToken));
        var remove = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.RemoveSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", slot.Id, TestContext.Current.CancellationToken));

        // Assert: both refused and the slot is unchanged
        Assert.Equal(DomainRule.SlotSigned, update.Rule);
        Assert.Equal(DomainRule.SlotSigned, remove.Rule);
        Assert.Equal("Ann", (await ApprovalAsync(fixture)).Slots.Single().Primary.DisplayName);
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
    /// <param name="blocker">The blocker under test: unanswered, fail or thread.</param>
    [Theory]
    [InlineData("unanswered")]
    [InlineData("fail")]
    [InlineData("thread")]
    public async Task SigningService_SignAsync_Blocker_ThrowsSigningNotAllowedAndListsBlocker(string blocker)
    {
        // Arrange: a review with a slot, answered completely except for the "unanswered" case, then one blocker introduced
        var fixture = await CreateAsync();
        if (blocker != "unanswered")
        {
            await AnswerAllAsync(fixture);
        }

        var slot = await fixture.Signing.AddSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", fixture.Ids["ann"], fixture.Ids["bob"], TestContext.Current.CancellationToken);
        var first = DefaultChecklist.Current.Sections[0].Items[0].Id;
        switch (blocker)
        {
            case "fail":
                await fixture.Checklists.SetAnswerAsync(
                    fixture.ProjectId, "rev-1", first, ChecklistOutcome.Fail, "It is wrong", TestActors.Ann, TestContext.Current.CancellationToken);
                break;
            case "thread":
                await fixture.Threads.StartThreadAsync(
                    fixture.ProjectId, "rev-1", "a.txt", null, null, TestActors.Bob, "Look at this", TestContext.Current.CancellationToken);
                break;
        }

        var approval = await ApprovalAsync(fixture);

        // Act: ann signs with the right password
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Signing.SignAsync(
            TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, approval.Digest, Password, TestContext.Current.CancellationToken));

        // Assert: refused with a blocker sentence; not approved, nothing stored
        Assert.Equal(DomainRule.SigningNotAllowed, exception.Rule);
        var expected = blocker switch
        {
            "fail" => "answered Fail",
            "thread" => "open",
            _ => "unanswered"
        };
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
            fixture.ProjectId, "rev-1", "a.txt", null, null, TestActors.Bob, "Remark", TestContext.Current.CancellationToken);
        await fixture.Threads.ResolveAsync(fixture.ProjectId, "rev-1", thread.Id, TestContext.Current.CancellationToken);

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
        // Arrange: a slot signed by ann
        var (fixture, slot) = await CreateReadyAsync();
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
    ///     Test that a change of the record makes the signature out of date, which can be signed again, and that
    ///     the history stays.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_RecordChangedAfterSigning_OutOfDateThenResignable()
    {
        // Arrange: a slot signed by ann
        var (fixture, slot) = await CreateReadyAsync();
        var digest = (await ApprovalAsync(fixture)).Digest;
        await fixture.Signing.SignAsync(TestActors.Ann, fixture.ProjectId, "rev-1", slot.Id, digest, Password, TestContext.Current.CancellationToken);

        // Act: a thread is opened and resolved, so the record differs although nothing is open
        var thread = await fixture.Threads.StartThreadAsync(
            fixture.ProjectId, "rev-1", "a.txt", null, null, TestActors.Bob, "One more thing", TestContext.Current.CancellationToken);
        await fixture.Threads.ResolveAsync(fixture.ProjectId, "rev-1", thread.Id, TestContext.Current.CancellationToken);
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
    ///     Test that the review is approved only when every slot has a current signature.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_TwoSlots_ApprovedOnlyWhenBothSigned()
    {
        // Arrange: two slots (ann + bob, cy alone) on a ready review
        var (fixture, first) = await CreateReadyAsync();
        var second = await fixture.Signing.AddSlotAsync(
            Cyd, fixture.ProjectId, "rev-1", fixture.Ids["cyd"], null, TestContext.Current.CancellationToken);
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
        // Arrange: a review that is otherwise ready
        var fixture = await CreateAsync();
        await AnswerAllAsync(fixture);

        // Act: read the status
        var approval = await ApprovalAsync(fixture);

        // Assert: not approved, with the slot hint
        Assert.False(approval.IsApproved);
        Assert.Empty(approval.Blockers);
        Assert.Equal("Add a reviewer slot.", approval.NextStep);
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
        IReadOnlyDictionary<string, int> Ids);
}
