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
///     Unit tests for the signature of a user of an external identity provider in <see cref="SigningService"/>: the
///     single-use proof of a fresh sign-in takes the place of the password, and every other rule stays the same.
/// </summary>
public class SigningServiceExternalTests
{
    /// <summary>
    ///     The review the tests sign.
    /// </summary>
    private const string ReviewId = "rev-1";

    /// <summary>
    ///     The actor who sets the reviews up.
    /// </summary>
    private static readonly Actor Boss = new("boss", "Boss");

    /// <summary>
    ///     The user of the identity provider as an actor.
    /// </summary>
    private static readonly Actor Olive = new("olive", "Olive");

    /// <summary>
    ///     Everything a test needs: the services, the proofs, the project and the slot of the external user.
    /// </summary>
    /// <param name="Signing">The signing service.</param>
    /// <param name="Proofs">The store of proofs the service uses.</param>
    /// <param name="Clock">The clock of the service and the proofs.</param>
    /// <param name="Users">The user service.</param>
    /// <param name="ProjectId">The project.</param>
    /// <param name="SlotId">The slot of <c>olive</c>.</param>
    /// <param name="OliveId">The identity of <c>olive</c>.</param>
    /// <param name="Store">The review store, to add members to the project.</param>
    /// <param name="Trail">The audit trail that receives the failed confirmations.</param>
    private sealed record World(
        SigningService Signing,
        ReauthProofStore Proofs,
        MovableTimeProvider Clock,
        UserService Users,
        int ProjectId,
        int SlotId,
        int OliveId,
        FakeReviewStore Store,
        FakeAuditStore Trail);

    /// <summary>
    ///     Creates a review that can be signed, with one slot whose primary reviewer is an external user.
    /// </summary>
    /// <param name="withProofs">Whether the signing service has a store of proofs.</param>
    /// <returns>The prepared world.</returns>
    private static async Task<World> CreateAsync(bool withProofs = true)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var clock = new MovableTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var trail = new FakeAuditStore(clock);
        var userStore = new FakeUserStore();
        var users = new UserService(
            userStore, new FakePasswordHasher(), clock, new AuditService(trail, null, clock));
        var boss = await users.CreateAsync("boss", "Boss", UserRole.Reviewer, "correct horse battery", cancellationToken);
        var external = await users.SignInExternalAsync("https://idp.example/", "sub-1", "olive", "Olive", cancellationToken);
        var olive = external.User!;
        var project = await TestServices.Projects(store, clock).CreateAsync("Apollo", null, cancellationToken);
        await store.AddMembershipAsync(boss.Id, project.Id, cancellationToken);
        await store.AddMembershipAsync(olive.Id, project.Id, cancellationToken);
        await TestServices.Reviews(store, clock).CreateDraftAsync(
            project.Id, ReviewId, "First", [new("a.txt", TestContent.A)], cancellationToken);
        var proofs = new ReauthProofStore(clock);
        var signing = new SigningService(
            store, users, new AllowAllProjectAccess(store), clock, withProofs ? proofs : null);
        var slot = await signing.AddSlotAsync(Boss, project.Id, ReviewId, olive.Id, null, cancellationToken);

        // The primary reviewer answers the checklist; the only ballot is submitted when it is complete
        var checklists = TestServices.Checklists(store, clock, userStore);
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await checklists.SetAnswerAsync(
                project.Id, ReviewId, item.Id, ChecklistOutcome.Pass, null, new Actor("olive", "Olive"), cancellationToken);
        }

        return new World(signing, proofs, clock, users, project.Id, slot.Id, olive.Id, store, trail);
    }

    /// <summary>
    ///     Signs the slot of the external user for the current record.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="actor">The signer.</param>
    /// <param name="password">The password to give; an external user has none.</param>
    /// <returns>The signature.</returns>
    private static async Task<ReviewSignature> SignAsync(World world, Actor actor, string? password = null)
    {
        var approval = (await world.Signing.GetApprovalAsync(
            world.ProjectId, ReviewId, TestContext.Current.CancellationToken))!;
        return await world.Signing.SignAsync(
            actor,
            world.ProjectId,
            ReviewId,
            world.SlotId,
            approval.Digest,
            password,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Test that a live proof for the user, review and slot lets the user sign once without a password, and that
    ///     the proof is spent by it.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_ExternalUserWithProof_SignsOnce()
    {
        // Arrange
        var world = await CreateAsync();
        world.Proofs.Issue(world.OliveId, world.ProjectId, ReviewId, world.SlotId);

        // Act
        var signature = await SignAsync(world, Olive);

        // Assert
        Assert.Equal(world.OliveId, signature.UserId);
        Assert.Equal("Olive", signature.DisplayName);
        Assert.False(world.Proofs.Has(world.OliveId, world.ProjectId, ReviewId, world.SlotId));
    }

    /// <summary>
    ///     Test that a signature without a proof is refused whatever password is given, and that a proof that
    ///     expired is refused too, that the missing confirmation is recorded as a failed confirmation without any
    ///     secret, and that nothing is signed.
    /// </summary>
    /// <param name="password">The password that is given.</param>
    /// <param name="expire">Whether the proof is issued and then left to expire.</param>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("correct horse battery", false)]
    [InlineData(null, true)]
    public async Task SigningService_SignAsync_ExternalUserWithoutLiveProof_IsRefused(string? password, bool expire)
    {
        // Arrange
        var world = await CreateAsync();
        if (expire)
        {
            world.Proofs.Issue(world.OliveId, world.ProjectId, ReviewId, world.SlotId);
            world.Clock.Advance(ReauthProofStore.Lifetime);
        }

        // Act
        var failure = await Assert.ThrowsAsync<DomainRuleException>(() => SignAsync(world, Olive, password));

        // Assert
        Assert.Equal(DomainRule.ReauthenticationFailed, failure.Rule);
        var approval = (await world.Signing.GetApprovalAsync(
            world.ProjectId, ReviewId, TestContext.Current.CancellationToken))!;
        Assert.Equal(SlotSignatureState.NotSigned, Assert.Single(approval.Slots).State);
        var recorded = Assert.Single(world.Trail.Entries, e => e.Action == "signature.reauth-failed");
        Assert.Equal("oidc: no-proof", recorded.Detail);
        Assert.DoesNotContain("correct horse battery", recorded.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a proof for another slot, review or user does not let the user sign, and that the failed attempt
    ///     does not spend the proof of the slot.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_ProofOfOtherBinding_IsRefused()
    {
        // Arrange
        var world = await CreateAsync();
        world.Proofs.Issue(world.OliveId, world.ProjectId, ReviewId, world.SlotId + 1);
        world.Proofs.Issue(world.OliveId, world.ProjectId, "rev-2", world.SlotId);
        world.Proofs.Issue(world.OliveId + 1, world.ProjectId, ReviewId, world.SlotId);

        // Act
        var failure = await Assert.ThrowsAsync<DomainRuleException>(() => SignAsync(world, Olive));

        // Assert
        Assert.Equal(DomainRule.ReauthenticationFailed, failure.Rule);
        Assert.True(world.Proofs.Has(world.OliveId, world.ProjectId, ReviewId, world.SlotId + 1));
    }

    /// <summary>
    ///     Test that without a store of proofs an external user can never sign.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_ExternalUserWithoutProofStore_IsRefused()
    {
        // Arrange
        var world = await CreateAsync(withProofs: false);
        world.Proofs.Issue(world.OliveId, world.ProjectId, ReviewId, world.SlotId);

        // Act
        var failure = await Assert.ThrowsAsync<DomainRuleException>(() => SignAsync(world, Olive));

        // Assert
        Assert.Equal(DomainRule.ReauthenticationFailed, failure.Rule);
    }

    /// <summary>
    ///     Test that a proof does not help against the other checks: a signature of an actor that came through an
    ///     API token is refused, and the proof is not spent by that refusal.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_ApiTokenActorWithProof_IsRefusedAndKeepsProof()
    {
        // Arrange
        var world = await CreateAsync();
        world.Proofs.Issue(world.OliveId, world.ProjectId, ReviewId, world.SlotId);
        var viaApi = new Actor("olive", "Olive") { ApiTokenId = "K7M2QX9P" };

        // Act
        var failure = await Assert.ThrowsAsync<DomainRuleException>(() => SignAsync(world, viaApi));

        // Assert
        Assert.Equal(DomainRule.ActorNotAllowed, failure.Rule);
        Assert.True(world.Proofs.Has(world.OliveId, world.ProjectId, ReviewId, world.SlotId));
    }

    /// <summary>
    ///     Test that a record that changed after the confirmation is refused before the proof is spent.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_RecordChanged_KeepsTheProof()
    {
        // Arrange
        var world = await CreateAsync();
        world.Proofs.Issue(world.OliveId, world.ProjectId, ReviewId, world.SlotId);

        // Act
        var failure = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.SignAsync(
            Olive, world.ProjectId, ReviewId, world.SlotId, "stale", null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DomainRule.RecordChanged, failure.Rule);
        Assert.True(world.Proofs.Has(world.OliveId, world.ProjectId, ReviewId, world.SlotId));
    }

    /// <summary>
    ///     Test that the page can ask whether the identity is confirmed without spending the confirmation, and that
    ///     the answer is <c>false</c> for a local user, an API token and an unknown user.
    /// </summary>
    [Fact]
    public async Task SigningService_HasReauthProofAsync_AnswersWithoutSpending()
    {
        // Arrange
        var world = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        world.Proofs.Issue(world.OliveId, world.ProjectId, ReviewId, world.SlotId);

        // Act
        var confirmed = await world.Signing.HasReauthProofAsync(Olive, world.ProjectId, ReviewId, world.SlotId, cancellationToken);
        var again = await world.Signing.HasReauthProofAsync(Olive, world.ProjectId, ReviewId, world.SlotId, cancellationToken);
        var otherSlot = await world.Signing.HasReauthProofAsync(Olive, world.ProjectId, ReviewId, world.SlotId + 1, cancellationToken);
        var local = await world.Signing.HasReauthProofAsync(Boss, world.ProjectId, ReviewId, world.SlotId, cancellationToken);
        var viaApi = await world.Signing.HasReauthProofAsync(
            new Actor("olive", "Olive") { ApiTokenId = "K7M2QX9P" }, world.ProjectId, ReviewId, world.SlotId, cancellationToken);
        var unknown = await world.Signing.HasReauthProofAsync(
            new Actor("nobody", "Nobody"), world.ProjectId, ReviewId, world.SlotId, cancellationToken);

        // Assert
        Assert.True(confirmed);
        Assert.True(again);
        Assert.False(otherSlot);
        Assert.False(local);
        Assert.False(viaApi);
        Assert.False(unknown);
    }

    /// <summary>
    ///     Test that the check before a confirmation accepts the reviewer of the slot who is a member and refuses,
    ///     with the rule that names the problem, every other case, spending nothing.
    /// </summary>
    [Fact]
    public async Task SigningService_RequireConfirmableAsync_AcceptsOnlyAReviewerOfTheSlot()
    {
        // Arrange
        var world = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var accepted = await world.Signing.RequireConfirmableAsync(Olive, world.ProjectId, ReviewId, world.SlotId, cancellationToken);
        var local = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.RequireConfirmableAsync(
            Boss, world.ProjectId, ReviewId, world.SlotId, cancellationToken));
        var noSlot = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.RequireConfirmableAsync(
            Olive, world.ProjectId, ReviewId, world.SlotId + 1, cancellationToken));
        var viaApi = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.RequireConfirmableAsync(
            new Actor("olive", "Olive") { ApiTokenId = "K7M2QX9P" }, world.ProjectId, ReviewId, world.SlotId, cancellationToken));
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.RequireConfirmableAsync(
            new Actor("nobody", "Nobody"), world.ProjectId, ReviewId, world.SlotId, cancellationToken));

        // Assert
        Assert.Equal(world.OliveId, accepted.Id);
        Assert.Equal(DomainRule.ActorNotAllowed, local.Rule);
        Assert.Equal(DomainRule.SlotNotFound, noSlot.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, viaApi.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, unknown.Rule);
    }

    /// <summary>
    ///     Test that an external user who is a member but not a reviewer of the slot cannot be asked to confirm, and
    ///     that one who is not a member is refused for that reason.
    /// </summary>
    [Fact]
    public async Task SigningService_RequireConfirmableAsync_NotAReviewerOfTheSlot_IsRefused()
    {
        // Arrange: a second external user, first not a member of the project, then a member
        var world = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var second = (await world.Users.SignInExternalAsync("https://idp.example/", "sub-2", "pat", "Pat", cancellationToken)).User!;
        var other = new Actor(second.Username, second.DisplayName);

        // Act
        var notMember = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.RequireConfirmableAsync(
            other, world.ProjectId, ReviewId, world.SlotId, cancellationToken));
        await world.Store.AddMembershipAsync(second.Id, world.ProjectId, cancellationToken);
        var notReviewer = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.RequireConfirmableAsync(
            other, world.ProjectId, ReviewId, world.SlotId, cancellationToken));

        // Assert: each refusal names its own reason
        Assert.Equal(DomainRule.ActorNotAllowed, notMember.Rule);
        Assert.Contains("member of the project", notMember.Message, StringComparison.Ordinal);
        Assert.Equal(DomainRule.ActorNotAllowed, notReviewer.Rule);
        Assert.Equal("You are not a reviewer of this slot.", notReviewer.Message);
    }
}
