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
///     Unit tests of the ballots of <see cref="ChecklistService"/>: answering into the ballot of the own slot, handing
///     the ballot in, the acknowledgement of signatures and what a viewer is shown.
/// </summary>
public class ChecklistServiceBallotTests
{
    /// <summary>
    ///     The instant reported by the service's clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The first check of the checklist.
    /// </summary>
    private static readonly string _firstItem = DefaultChecklist.Current.Sections[0].Items[0].Id;

    /// <summary>
    ///     Creates a store with a project, the review <c>rev-1</c> and a checklist service, with one slot for each of the
    ///     given users (the users are not answering yet).
    /// </summary>
    /// <param name="seated">The user names that hold a slot each, in order; empty for a review without slots.</param>
    /// <returns>The store, the service and the id of the project.</returns>
    private static async Task<(FakeReviewStore Store, ChecklistService Service, int ProjectId)> CreateAsync(params string[] seated)
    {
        var clock = new FixedTimeProvider(_now);
        var store = new FakeReviewStore();
        var userStore = new FakeUserStore();
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        var project = await TestServices.Projects(store, clock)
            .CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await TestServices.Reviews(store, clock).CreateDraftAsync(
            project.Id, "rev-1", "First", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        foreach (var name in seated.Append("outsider"))
        {
            var user = await users.CreateAsync(
                name, name, UserRole.Reviewer, "correct horse battery", TestContext.Current.CancellationToken);
            if (name != "outsider")
            {
                await store.AddSlotAsync(
                    new ReviewerSlot { ProjectId = project.Id, ReviewId = "rev-1", PrimaryUserId = user.Id }, TestContext.Current.CancellationToken);
            }
        }

        return (store, TestServices.Checklists(store, clock, userStore), project.Id);
    }

    /// <summary>
    ///     Reads the ballot of a slot from the store.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="projectId">The project.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <returns>The ballot, or <c>null</c> when the slot has none.</returns>
    private static async Task<Ballot?> BallotOfAsync(IReviewStore store, int projectId, int slotId)
    {
        var record = (await store.GetSnapshotAsync(projectId, "rev-1", TestContext.Current.CancellationToken))!.Record;
        return record.Ballots.FirstOrDefault(b => b.SlotId == slotId);
    }

    /// <summary>
    ///     Test that somebody who holds no slot of the review cannot answer.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswerAsync_ActorWithoutSlot_ThrowsNoReviewerSlot()
    {
        // Arrange: a review with one slot, held by ann; the outsider has none
        var cancellationToken = TestContext.Current.CancellationToken;
        var (store, service, projectId) = await CreateAsync("ann");

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => service.SetAnswerAsync(
            projectId, "rev-1", _firstItem, ChecklistOutcome.Pass, null, new Actor("outsider", "outsider"), cancellationToken));

        // Assert: refused, and nothing is stored
        Assert.Equal(DomainRule.NoReviewerSlot, ex.Rule);
        Assert.Empty((await store.GetSnapshotAsync(projectId, "rev-1", cancellationToken))!.Record.Ballots);
    }

    /// <summary>
    ///     Test that the last answer of the only slot of a review hands its ballot in, and that an answer before that
    ///     leaves it a draft.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswerAsync_SingleSlotComplete_AutoSubmits()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (store, service, projectId) = await CreateAsync("ann");
        var ann = new Actor("ann", "ann");
        var slotId = (await store.GetSnapshotAsync(projectId, "rev-1", cancellationToken))!.Slots.Single().Id;
        var items = DefaultChecklist.Current.Sections.SelectMany(s => s.Items).ToList();

        // Act: answer all but the last check, look, then answer the last
        foreach (var item in items.Take(items.Count - 1))
        {
            await service.SetAnswerAsync(projectId, "rev-1", item.Id, ChecklistOutcome.Pass, null, ann, cancellationToken);
        }

        var before = await BallotOfAsync(store, projectId, slotId);
        await service.SetAnswerAsync(projectId, "rev-1", items[^1].Id, ChecklistOutcome.Pass, null, ann, cancellationToken);
        var after = await BallotOfAsync(store, projectId, slotId);

        // Assert
        Assert.Equal(BallotStatus.Draft, before!.Status);
        Assert.Equal(BallotStatus.Submitted, after!.Status);
        Assert.Equal("ann", after.SubmittedByUsername);
    }

    /// <summary>
    ///     Test that with two slots a complete ballot stays a draft until it is handed in, and that handing it in again
    ///     changes nothing.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SubmitBallotAsync_TwoSlots_StaysDraftUntilSubmit()
    {
        // Arrange: two slots; ann answers every check
        var cancellationToken = TestContext.Current.CancellationToken;
        var (store, service, projectId) = await CreateAsync("ann", "bob");
        var ann = new Actor("ann", "ann");
        var slotId = (await store.GetSnapshotAsync(projectId, "rev-1", cancellationToken))!.Slots[0].Id;
        await AuditTestExtensions.AnswerAllAsync(service, projectId, "rev-1", ann, cancellationToken);
        var beforeSubmit = await BallotOfAsync(store, projectId, slotId);

        // Act
        var submitted = await service.SubmitBallotAsync(projectId, "rev-1", ann, cancellationToken);
        var again = await service.SubmitBallotAsync(projectId, "rev-1", ann, cancellationToken);

        // Assert
        Assert.Equal(BallotStatus.Draft, beforeSubmit!.Status);
        Assert.Equal(BallotStatus.Submitted, submitted.Status);
        Assert.Equal(BallotStatus.Submitted, again.Status);
        Assert.Equal(BallotStatus.Submitted, (await BallotOfAsync(store, projectId, slotId))!.Status);
    }

    /// <summary>
    ///     Test that a ballot with a check left open cannot be handed in.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SubmitBallotAsync_Incomplete_ThrowsBallotIncomplete()
    {
        // Arrange: two slots; ann answers one check only
        var cancellationToken = TestContext.Current.CancellationToken;
        var (store, service, projectId) = await CreateAsync("ann", "bob");
        var ann = new Actor("ann", "ann");
        await service.SetAnswerAsync(projectId, "rev-1", _firstItem, ChecklistOutcome.Pass, null, ann, cancellationToken);

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => service.SubmitBallotAsync(projectId, "rev-1", ann, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.BallotIncomplete, ex.Rule);
        var slotId = (await store.GetSnapshotAsync(projectId, "rev-1", cancellationToken))!.Slots[0].Id;
        Assert.Equal(BallotStatus.Draft, (await BallotOfAsync(store, projectId, slotId))!.Status);
    }

    /// <summary>
    ///     Test that somebody without a slot cannot hand a ballot in.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SubmitBallotAsync_ActorWithoutSlot_ThrowsNoReviewerSlot()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, service, projectId) = await CreateAsync("ann", "bob");

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SubmitBallotAsync(projectId, "rev-1", new Actor("outsider", "outsider"), cancellationToken));

        // Assert
        Assert.Equal(DomainRule.NoReviewerSlot, ex.Rule);
    }

    /// <summary>
    ///     Test that editing a submitted ballot makes it a draft again without touching a signature of another slot
    ///     when the other ballots still carry the same agreed standing, and that handing a differing ballot in needs the
    ///     acknowledgement of that signature.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SubmitBallotAsync_DigestChanging_RequiresAcknowledgementOfOtherSlots()
    {
        // Arrange: three submitted ballots that agree, and a signature of slot 1
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        var sam = SignedReviewWorld.ActorOf("sam");

        // Act: sam changes an answer (the ballot becomes a draft; the other two ballots still agree)
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Fail, "Wrong.", sam, cancellationToken);
        var signedAfterEdit = await world.SignedSlotIdsAsync();

        // Act: handing the differing ballot in without the acknowledgement is refused
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Checklists.SubmitBallotAsync(world.ProjectId, SignedReviewWorld.ReviewId, sam, cancellationToken));
        var signedAfterRefusal = await world.SignedSlotIdsAsync();

        // Act: with the acknowledgement it goes through and the signature is out of date
        await world.Checklists.SubmitBallotAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, sam, cancellationToken, [world.Slots[0].Id]);
        var signedAfterSubmit = await world.SignedSlotIdsAsync();

        // Assert
        Assert.Equal([world.Slots[0].Id], signedAfterEdit);
        Assert.Equal(DomainRule.SignaturesWouldBecomeOutOfDate, ex.Rule);
        Assert.Equal([world.Slots[0].Id], signedAfterRefusal);
        Assert.Empty(signedAfterSubmit);
    }

    /// <summary>
    ///     Test that a ballot that agrees with the others when it is handed in again warns nobody.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SubmitBallotAsync_AgreeingSubmit_NoWarning()
    {
        // Arrange: a signature of slot 1, and sam edits an answer to the same value (a draft again)
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        var sam = SignedReviewWorld.ActorOf("sam");
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Pass, null, sam, cancellationToken);

        // Act
        await world.Checklists.SubmitBallotAsync(world.ProjectId, SignedReviewWorld.ReviewId, sam, cancellationToken);

        // Assert: the signature of slot 1 is still current
        Assert.Equal([world.Slots[0].Id], await world.SignedSlotIdsAsync());
    }

    /// <summary>
    ///     Test that a reviewer whose own signature is current needs no acknowledgement of that signature when changing
    ///     the own ballot.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SubmitBallotAsync_SignedOwnSlot_NoSelfAcknowledgement()
    {
        // Arrange: rita has signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        var rita = SignedReviewWorld.ActorOf("rita");

        // Act: she changes her own answer and hands the ballot in again
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Fail, "Wrong.", rita, cancellationToken);
        await world.Checklists.SubmitBallotAsync(world.ProjectId, SignedReviewWorld.ReviewId, rita, cancellationToken);

        // Assert: no exception, and her signature is out of date now
        Assert.Empty(await world.SignedSlotIdsAsync());
    }

    /// <summary>
    ///     Test that a viewer who holds a slot sees the own draft, and that everybody else sees only the standing of the
    ///     submitted ballots.
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetAsync_Viewer_SeesOwnDraftOnly()
    {
        // Arrange: sam changes an answer to Fail and has not handed the ballot in
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Fail, "Wrong.",
            SignedReviewWorld.ActorOf("sam"), cancellationToken);

        // Act
        var own = (await world.Checklists.GetAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken, "sam"))!;
        var other = (await world.Checklists.GetAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken, "rita"))!;
        var consolidated = (await world.Checklists.GetAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken))!;

        // Assert: sam sees the draft; rita sees her own Pass; the standing of the submitted ballots is Pass
        Assert.True(own.IsOwnBallot);
        Assert.Equal(BallotState.Draft, own.BallotState);
        Assert.Equal(ChecklistOutcome.Fail, own.AnswerFor(SignedReviewWorld.FirstItemId)!.Outcome);
        Assert.True(other.IsOwnBallot);
        Assert.Equal(ChecklistOutcome.Pass, other.AnswerFor(SignedReviewWorld.FirstItemId)!.Outcome);
        Assert.False(consolidated.IsOwnBallot);
        Assert.Equal(ChecklistOutcome.Pass, consolidated.AnswerFor(SignedReviewWorld.FirstItemId)!.Outcome);
    }

    /// <summary>
    ///     Test that a viewer without a slot gets the consolidated standing, marked as having no slot.
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetAsync_NoSlot_Consolidated()
    {
        // Arrange: one slot, held by ann, with a draft answer
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, service, projectId) = await CreateAsync("ann", "bob");
        await service.SetAnswerAsync(projectId, "rev-1", _firstItem, ChecklistOutcome.Pass, null, new Actor("ann", "ann"), cancellationToken);

        // Act
        var state = (await service.GetAsync(projectId, "rev-1", cancellationToken, "outsider"))!;

        // Assert: the draft of ann is not shown
        Assert.True(state.NoSlot);
        Assert.False(state.IsOwnBallot);
        Assert.Null(state.AnswerFor(_firstItem));
    }

    /// <summary>
    ///     Test that an auditor who still holds a slot cannot answer, change the applicability of a section or hand a
    ///     ballot in: the read-only role is refused before the slot is looked up, and nothing is stored.
    /// </summary>
    [Fact]
    public async Task ChecklistService_BallotWrites_AuditorHoldingSlot_ThrowsActorNotAllowed()
    {
        // Arrange: aud holds the only slot of the review, but has the read-only role of the project
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FixedTimeProvider(_now);
        var store = new FakeReviewStore();
        var userStore = new FakeUserStore();
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        var root = new Actor("root", "root");
        var aud = new Actor("aud", "aud");
        await users.CreateAsync("root", "root", true, "correct horse battery", Actor.CommandLine, cancellationToken);
        var audUser = await users.CreateAsync("aud", "aud", false, "correct horse battery", Actor.CommandLine, cancellationToken);
        var access = new ProjectAccess(userStore, store);
        var project = await new ProjectService(store, userStore, access, new FakePasswordHasher(), clock)
            .CreateAsync("Own", null, root, cancellationToken);
        await store.AddMembershipAsync(audUser.Id, project.Id, ProjectRole.Auditor, cancellationToken);
        await TestServices.Reviews(store, clock).CreateDraftAsync(
            project.Id, "rev-1", "First", [new("a.txt", TestContent.A)], cancellationToken);
        await store.AddSlotAsync(
            new ReviewerSlot { ProjectId = project.Id, ReviewId = "rev-1", PrimaryUserId = audUser.Id }, cancellationToken);
        var service = new ChecklistService(store, access, userStore, clock);

        // Act
        var answer = await Assert.ThrowsAsync<DomainRuleException>(() => service.SetAnswerAsync(
            project.Id, "rev-1", _firstItem, ChecklistOutcome.Pass, null, aud, cancellationToken));
        var section = await Assert.ThrowsAsync<DomainRuleException>(() => service.SetSectionApplicabilityAsync(
            project.Id, "rev-1", DefaultChecklist.Current.Sections[0].Id, false, "Not relevant.", aud, cancellationToken));
        var submit = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SubmitBallotAsync(project.Id, "rev-1", aud, cancellationToken));

        // Assert: the role is the reason every time, and nothing is stored
        Assert.Equal(DomainRule.ActorNotAllowed, answer.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, section.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, submit.Rule);
        Assert.Empty((await store.GetSnapshotAsync(project.Id, "rev-1", cancellationToken))!.Record.Ballots);
    }
}
