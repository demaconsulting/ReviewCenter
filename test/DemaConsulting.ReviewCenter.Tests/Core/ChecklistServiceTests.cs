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
///     Unit tests for <see cref="ChecklistService"/> and the <see cref="DefaultChecklist"/> it serves, using an
///     in-memory store.
/// </summary>
public class ChecklistServiceTests
{
    /// <summary>
    ///     The instant reported by the service's clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The number of checks in each section of the template, in order.
    /// </summary>
    private static readonly int[] _itemsPerSection = [8, 5, 4, 6, 3, 4, 2, 5, 3, 8, 2, 2, 2, 4];

    /// <summary>
    ///     Creates a store with a project and the review <c>rev-1</c>, and a checklist service over it. The user
    ///     <c>ann</c> holds the one slot of the review, so her answers go into the one ballot, which is handed in with
    ///     the last answer.
    /// </summary>
    /// <param name="seated">The user name of the person who holds the one slot.</param>
    /// <returns>The store, the service and the id of the project.</returns>
    private static async Task<(FakeReviewStore Store, ChecklistService Service, int ProjectId)> CreateAsync(string? seated = "ann")
    {
        var clock = new FixedTimeProvider(_now);
        var store = new FakeReviewStore();
        var userStore = new FakeUserStore();
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        var project = await TestServices.Projects(store, clock)
            .CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await TestServices.Reviews(store, clock).CreateDraftAsync(
            project.Id, "rev-1", "First", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        if (seated is not null)
        {
            var user = await users.CreateAsync(
                seated, char.ToUpperInvariant(seated[0]) + seated[1..], UserRole.Reviewer, "correct horse battery", TestContext.Current.CancellationToken);
            await store.AddSlotAsync(
                new ReviewerSlot { ProjectId = project.Id, ReviewId = "rev-1", PrimaryUserId = user.Id }, TestContext.Current.CancellationToken);
        }

        return (store, TestServices.Checklists(store, clock, userStore), project.Id);
    }

    /// <summary>
    ///     Test that an answer, a section's applicability (either way) and the hand-in of a ballot are refused with
    ///     <see cref="DomainRule.ReviewLocked"/> while the review is approved, and that the record is unchanged.
    /// </summary>
    /// <param name="operation">The mutation to try: answer, section, sectionApplicable or submit.</param>
    [Theory]
    [InlineData("answer")]
    [InlineData("section")]
    [InlineData("sectionApplicable")]
    [InlineData("submit")]
    public async Task ChecklistService_Mutations_ApprovedReview_ThrowReviewLocked(string operation)
    {
        // Arrange: a review approved by a separate reviewer (every check answered Pass)
        var cancellationToken = TestContext.Current.CancellationToken;
        var (store, service, projectId) = await CreateAsync(null);
        await AuditTestExtensions.ApproveReviewAsync(store, new FixedTimeProvider(_now), projectId, "rev-1", "approver", cancellationToken);
        var first = DefaultChecklist.Current.Sections[0];
        var answersBefore = (await service.GetAsync(projectId, "rev-1", cancellationToken))!.Overall.Answered;
        var ballotsBefore = (await store.GetSnapshotAsync(projectId, "rev-1", cancellationToken))!.Record.Ballots;

        // Act: try to change an answer, to mark a section as not applicable or applicable, or to hand a ballot in
        Task Mutate() => operation switch
        {
            "answer" => service.SetAnswerAsync(
                projectId, "rev-1", first.Items[0].Id, ChecklistOutcome.Fail, "Wrong.", TestActors.Ann, cancellationToken),
            "section" => service.SetSectionApplicabilityAsync(
                projectId, "rev-1", first.Id, false, "Not relevant.", TestActors.Ann, cancellationToken),
            "sectionApplicable" => service.SetSectionApplicabilityAsync(
                projectId, "rev-1", first.Id, true, null, TestActors.Ann, cancellationToken),
            _ => service.SubmitBallotAsync(projectId, "rev-1", TestActors.Ann, cancellationToken)
        };
        var ex = await Assert.ThrowsAsync<DomainRuleException>(Mutate);

        // Assert: refused as locked, and the answers and ballots are untouched
        Assert.Equal(DomainRule.ReviewLocked, ex.Rule);
        var state = (await service.GetAsync(projectId, "rev-1", cancellationToken))!;
        Assert.Equal(answersBefore, state.Overall.Answered);
        Assert.Equal(ChecklistOutcome.Pass, state.AnswerFor(first.Items[0].Id)!.Outcome);
        Assert.True(state.IsApplicable(first.Id));
        Assert.Equal(ballotsBefore.Count, (await store.GetSnapshotAsync(projectId, "rev-1", cancellationToken))!.Record.Ballots.Count);
    }

    /// <summary>
    ///     Test that a change is stored without any acknowledgement while no signature is current, and that a change to
    ///     a draft that leaves the consolidated record as it is does not touch a current signature.
    /// </summary>
    [Fact]
    public async Task ChecklistService_Changes_NoSignatureOrNoOp_DoNotThrow()
    {
        // Arrange: nobody has signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        var sectionId = DefaultChecklist.Current.Sections[0].Id;
        var rita = SignedReviewWorld.ActorOf("rita");

        // Act: change an answer and a section while there is no signature, then hand the ballot in again, sign, and
        // change the draft of the own ballot again
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Fail, "Wrong.", rita, cancellationToken);
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Pass, null, rita, cancellationToken);
        await world.Checklists.SetSectionApplicabilityAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, sectionId, true, null, rita, cancellationToken);
        await world.Checklists.SubmitBallotAsync(world.ProjectId, SignedReviewWorld.ReviewId, rita, cancellationToken);
        await world.SignAsync(1);
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Pass, null, rita, cancellationToken);

        // Assert: the other ballots still agree, so the record did not change and the signature is still current
        Assert.Equal(SlotSignatureState.Signed, (await world.ApprovalAsync()).Slots[0].State);
    }

    /// <summary>
    ///     Test that a person who takes back the ballot of another slot is not refused while the record is as it was:
    ///     a draft does not count in the digest, and the answerer is not part of it.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswerAsync_SameValueByOtherSlot_KeepsTheSignature()
    {
        // Arrange: slot 1 signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);

        // Act: sam saves the value that rita gave, which takes his ballot back to a draft
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Pass, null, SignedReviewWorld.ActorOf("sam"), cancellationToken);

        // Assert: nothing was refused, the others still agree, and the signature is current
        var state = (await world.Checklists.GetAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken))!;
        Assert.Equal(ChecklistOutcome.Pass, state.AnswerFor(SignedReviewWorld.FirstItemId)!.Outcome);
        Assert.Equal(SlotSignatureState.Signed, (await world.ApprovalAsync()).Slots[0].State);
    }

    /// <summary>
    ///     Test that a refusal names every current signature with its slot number and signer, whether the primary or the
    ///     alternate signed, and that slots without a signature are not named.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswerAsync_PrimaryAndAlternateSigners_NamesSlotNumbersAndSigners()
    {
        // Arrange: the alternate signed slot 1 and the primary signed slot 3; slot 2 is not signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1, alternate: true);
        await world.SignAsync(3);

        // Act: sam disagrees and hands the ballot in without acknowledging
        var sam = SignedReviewWorld.ActorOf("sam");
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Fail, "Wrong.", sam, cancellationToken);
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Checklists.SubmitBallotAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, sam, cancellationToken));

        // Assert: slot 1 (Alex) and slot 3 (Tess), with the slot identities, in the message and in the payload
        Assert.Equal(
            [(world.Slots[0].Id, 1, "Alex"), (world.Slots[2].Id, 3, "Tess")],
            ex.AffectedSlots.Select(s => (s.SlotId, s.SlotNumber, s.SignerDisplayName)));
        Assert.Contains("slot 1 (Alex), slot 3 (Tess)", ex.Message);
    }

    /// <summary>
    ///     Test that an acknowledgement that does not cover every current signature is refused naming only the
    ///     signatures that remain, that an acknowledgement of an unrelated slot suppresses nothing, and that nothing
    ///     is stored by a refusal.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswerAsync_PartialAcknowledgement_ThrowsWithRemainingSlots()
    {
        // Arrange: slots 1 and 2 signed, and tess disagrees in her draft
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        await world.SignAsync(2);
        var tess = SignedReviewWorld.ActorOf("tess");
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Fail, "Wrong.", tess, cancellationToken);

        // Act: hand in acknowledging slot 1 only, and then only the unsigned slot 3
        var partial = await Assert.ThrowsAsync<DomainRuleException>(() => world.Checklists.SubmitBallotAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, tess, cancellationToken, [world.Slots[0].Id]));
        var wrongSlot = await Assert.ThrowsAsync<DomainRuleException>(() => world.Checklists.SubmitBallotAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, tess, cancellationToken, [world.Slots[2].Id]));

        // Assert: only slot 2 remains in the first refusal, both remain in the second, and nothing was handed in
        Assert.Equal([2], partial.AffectedSlots.Select(s => s.SlotNumber));
        Assert.Equal([1, 2], wrongSlot.AffectedSlots.Select(s => s.SlotNumber));
        var state = (await world.Checklists.GetAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken))!;
        Assert.Equal(ChecklistOutcome.Pass, state.AnswerFor(SignedReviewWorld.FirstItemId)!.Outcome);
        Assert.All((await world.ApprovalAsync()).Slots.Take(2), slot => Assert.Equal(SlotSignatureState.Signed, slot.State));
    }

    /// <summary>
    ///     Test that a hand-in acknowledging every current signature is stored and leaves those signatures out of date.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswerAsync_FullAcknowledgement_StoresAndSignatureOutOfDate()
    {
        // Arrange: slots 1 and 2 signed, and tess disagrees in her draft
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        await world.SignAsync(2);
        var tess = SignedReviewWorld.ActorOf("tess");
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Fail, "Wrong.", tess, cancellationToken);

        // Act: hand the ballot in acknowledging both
        await world.Checklists.SubmitBallotAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, tess, cancellationToken, await world.SignedSlotIdsAsync());

        // Assert: stored, and both signatures are kept but out of date
        var state = (await world.Checklists.GetAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken, "tess"))!;
        Assert.Equal(ChecklistOutcome.Fail, state.AnswerFor(SignedReviewWorld.FirstItemId)!.Outcome);
        var approval = await world.ApprovalAsync();
        Assert.Equal(SlotSignatureState.OutOfDate, approval.Slots[0].State);
        Assert.Equal(SlotSignatureState.OutOfDate, approval.Slots[1].State);
        Assert.Equal(SlotSignatureState.NotSigned, approval.Slots[2].State);
    }

    /// <summary>
    ///     Test that marking a section as not applicable is a draft change that needs no acknowledgement, that handing
    ///     the ballot in is refused while a signature is current, is stored once acknowledged, and that marking a
    ///     section applicable when it has no stored state changes nothing.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetSectionApplicabilityAsync_Signed_NeedsAcknowledgement()
    {
        // Arrange: slot 1 signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        var sectionId = DefaultChecklist.Current.Sections[0].Id;
        var sam = SignedReviewWorld.ActorOf("sam");

        // Act: sam marks the section applicable (already is), then not applicable; he hands in without acknowledging,
        // then acknowledged
        await world.Checklists.SetSectionApplicabilityAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, sectionId, true, null, sam, cancellationToken);
        await world.Checklists.SetSectionApplicabilityAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, sectionId, false, "Not relevant.", sam, cancellationToken);
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Checklists.SubmitBallotAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, sam, cancellationToken));
        var untouched = (await world.Checklists.GetAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken))!.IsApplicable(sectionId);
        await world.Checklists.SubmitBallotAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, sam, cancellationToken, await world.SignedSlotIdsAsync());

        // Assert: refused naming slot 1 and handing nothing in, then stored and the signature is out of date
        Assert.Equal(DomainRule.SignaturesWouldBecomeOutOfDate, ex.Rule);
        Assert.Equal(1, Assert.Single(ex.AffectedSlots).SlotNumber);
        Assert.True(untouched);
        Assert.Equal(SlotSignatureState.OutOfDate, (await world.ApprovalAsync()).Slots[0].State);
    }

    /// <summary>
    ///     Test that the lock of an approved review is reported before the signature impact.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswerAsync_ApprovedReview_ReportsLockedBeforeImpact()
    {
        // Arrange: every slot signed, so the review is approved and locked
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        await world.SignAsync(2);
        await world.SignAsync(3);

        // Act: change an answer without acknowledging
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Fail, "Wrong.", SignedReviewWorld.ActorOf("rita"), cancellationToken));

        // Assert: refused as locked, with no slots listed
        Assert.Equal(DomainRule.ReviewLocked, ex.Rule);
        Assert.Empty(ex.AffectedSlots);
    }

    /// <summary>
    ///     Test that the exception keeps its own copy of the affected slots.
    /// </summary>
    [Fact]
    public void ChecklistService_DomainRuleException_AffectedSlots_AreCopiedFromTheSource()
    {
        // Arrange: a list of one affected signature
        var source = new List<AffectedSignature> { new(10, 1, "Riley Reviewer", 7) };

        // Act: build the exception, then change the source list
        var ex = new DomainRuleException(DomainRule.SignaturesWouldBecomeOutOfDate, "Out of date.", source);
        source.Add(new AffectedSignature(11, 2, "Joan Alternate", 8));
        source.RemoveAt(0);

        // Assert: the exception still lists the one original signature
        Assert.Equal(new AffectedSignature(10, 1, "Riley Reviewer", 7), Assert.Single(ex.AffectedSlots));
        Assert.Empty(new DomainRuleException(DomainRule.ReviewLocked, "Locked.").AffectedSlots);
    }

    /// <summary>
    ///     Test that the default checklist has the 14 sections of the template with the known number of checks.
    /// </summary>
    [Fact]
    public void ChecklistService_DefaultChecklist_HasTemplateSectionsAndItemIdsInOrder()
    {
        // Arrange, Act: the default checklist
        var definition = DefaultChecklist.Current;

        // Assert: version, section order and counts, and the first and last checks verbatim
        Assert.Equal(DefaultChecklist.Version, definition.Version);
        Assert.Equal(
            ["REQ", "DES", "DOC", "CODE", "LOGIC", "ERR", "USE", "TEST", "SEC", "READ", "REQDOC", "REQIMP", "REQTEST", "CODEDOC"],
            definition.Sections.Select(section => section.Id));
        Assert.Equal(_itemsPerSection, definition.Sections.Select(section => section.Items.Count));
        Assert.Equal(58, definition.Sections.Sum(section => section.Items.Count));
        Assert.Equal("Requirements Checks", definition.Sections[0].Title);
        Assert.Equal("REQ-01", definition.Sections[0].Items[0].Id);
        Assert.Equal("CODEDOC-04", definition.Sections[^1].Items[^1].Id);
    }

    /// <summary>
    ///     Test that every check id is unique and starts with the id of its section.
    /// </summary>
    [Fact]
    public void ChecklistService_DefaultChecklist_IdsUniqueAndPrefixedBySection()
    {
        // Arrange, Act: all checks with their section
        var items = DefaultChecklist.Current.Sections
            .SelectMany(section => section.Items.Select(item => (Section: section, Item: item)))
            .ToList();

        // Assert: no duplicate ids; each id is the section id, a dash and a number; every text is present
        Assert.Equal(items.Count, items.Select(i => i.Item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(items, i => Assert.Matches($"^{i.Section.Id}-\\d{{2}}$", i.Item.Id));
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Item.Text)));
    }

    /// <summary>
    ///     Test that the definition survives being written to and read from the stored JSON form.
    /// </summary>
    [Fact]
    public void ChecklistService_DefaultChecklist_RoundTripsThroughJson()
    {
        // Arrange, Act: write the default checklist to JSON and read it back
        var copy = ChecklistDefinition.FromJson(DefaultChecklist.Current.ToJson());

        // Assert: the same version, sections and checks
        Assert.Equal(DefaultChecklist.Version, copy.Version);
        Assert.Equal(
            DefaultChecklist.Current.Sections.Select(s => (s.Id, s.Title, s.ApplicabilityHint)),
            copy.Sections.Select(s => (s.Id, s.Title, s.ApplicabilityHint)));
        Assert.Equal(
            DefaultChecklist.Current.Sections.SelectMany(s => s.Items),
            copy.Sections.SelectMany(s => s.Items));
    }

    /// <summary>
    ///     Test that a definition that is valid JSON but not complete is rejected with a JSON error when it is read,
    ///     both directly and through the stored checklist of a review, so no reader meets a null part.
    /// </summary>
    /// <param name="json">The incomplete definition text.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Version\":\"1\"}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":null}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":[null]}")]
    [InlineData("{\"Sections\":[]}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":[{\"Title\":\"T\",\"Items\":[]}]}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":[{\"Id\":\"A\",\"Items\":[]}]}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":[{\"Id\":\"A\",\"Title\":\"T\"}]}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":[{\"Id\":\"A\",\"Title\":\"T\",\"Items\":[null]}]}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":[{\"Id\":\"A\",\"Title\":\"T\",\"Items\":[{\"Id\":\"A-1\"}]}]}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":[{\"Id\":\"A\",\"Title\":\"T\",\"Items\":[{\"Text\":\"q\"}]}]}")]
    public void ChecklistService_FromJson_IncompleteDefinition_ThrowsJsonException(string json)
    {
        // Arrange: a stored checklist holding the incomplete text
        var checklist = new ReviewChecklist { ProjectId = 1, ReviewId = "rev-1", Version = "x", Definition = json };

        // Act, Assert: both ways of reading it fail with a JSON error
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ChecklistDefinition.FromJson(json));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => checklist.Parse());
    }

    /// <summary>
    ///     Test that the service uses the snapshot stored with the review, not the current default.
    /// </summary>
    [Fact]
    public async Task ChecklistService_Get_AfterDefaultChanges_ReturnsFrozenSnapshot()
    {
        // Arrange: a review whose stored snapshot differs from the default
        var store = new FakeReviewStore();
        var project = await TestServices.Projects(store, new FixedTimeProvider(_now))
            .CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var custom = new ChecklistDefinition(
            "old-1",
            [new ChecklistSection("X", "Old section", "Hint", [new ChecklistItem("X-01", "Old check")])]);
        await store.AddReviewAsync(
            new Review
            {
                ProjectId = project.Id,
                Id = "rev-old",
                Title = "Old",
                Created = _now,
                Checklist = new ReviewChecklist
                {
                    ProjectId = project.Id,
                    ReviewId = "rev-old",
                    Version = custom.Version,
                    Definition = custom.ToJson()
                }
            },
            TestContext.Current.CancellationToken);
        var service = TestServices.Checklists(store, new FixedTimeProvider(_now));

        // Act: read the checklist
        var state = await service.GetAsync(project.Id, "rev-old", TestContext.Current.CancellationToken);

        // Assert: the frozen definition is used
        Assert.NotNull(state);
        Assert.Equal("old-1", state.Definition.Version);
        Assert.Equal(new ChecklistProgress(0, 1), state.Overall);
    }

    /// <summary>
    ///     Test that an unknown review has no checklist.
    /// </summary>
    [Fact]
    public async Task ChecklistService_Get_UnknownReview_ReturnsNull()
    {
        // Arrange: a store with one review
        var (_, service, projectId) = await CreateAsync();

        // Act, Assert: another review has no checklist
        Assert.Null(await service.GetAsync(projectId, "other", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that marking a section as not applicable needs a reason.
    /// </summary>
    /// <param name="reason">The missing reason.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task ChecklistService_SetSectionApplicability_NotApplicableWithoutReason_Throws(string? reason)
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: mark a section as not applicable without a reason
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetSectionApplicabilityAsync(
                projectId, "rev-1", "DES", false, reason, TestActors.Ann, TestContext.Current.CancellationToken));

        // Assert: the rule is named and the section still applies
        Assert.Equal(DomainRule.ChecklistJustificationInvalid, exception.Rule);
        var state = await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken);
        Assert.True(state!.IsApplicable("DES"));
    }

    /// <summary>
    ///     Test that a not-applicable section keeps its trimmed reason and leaves the total.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetSectionApplicability_NotApplicableWithReason_StoresTrimmedReason()
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: mark a section as not applicable
        await service.SetSectionApplicabilityAsync(
            projectId, "rev-1", "DES", false, "  No design files  ", TestActors.Ann, TestContext.Current.CancellationToken);
        var state = await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken);

        // Assert: not applicable with the trimmed reason
        Assert.False(state!.IsApplicable("DES"));
        Assert.Equal("No design files", state.ReasonFor("DES"));
        Assert.Equal(1, state.NotApplicableSectionCount);
    }

    /// <summary>
    ///     Test that applying a section again clears the reason.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetSectionApplicability_Applicable_ClearsReason()
    {
        // Arrange: a section marked as not applicable
        var (_, service, projectId) = await CreateAsync();
        await service.SetSectionApplicabilityAsync(
            projectId, "rev-1", "DES", false, "No design files", TestActors.Ann, TestContext.Current.CancellationToken);

        // Act: mark it as applicable again, with a stray reason
        await service.SetSectionApplicabilityAsync(
            projectId, "rev-1", "DES", true, "ignored", TestActors.Ann, TestContext.Current.CancellationToken);
        var state = await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken);

        // Assert: applicable, with no reason
        Assert.True(state!.IsApplicable("DES"));
        Assert.Null(state.ReasonFor("DES"));
    }

    /// <summary>
    ///     Test that a section that is not in the checklist is refused.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetSectionApplicability_UnknownSection_ThrowsChecklistItemNotFound()
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: use an unknown section id
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetSectionApplicabilityAsync(
                projectId, "rev-1", "NOPE", false, "Reason", TestActors.Ann, TestContext.Current.CancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.ChecklistItemNotFound, exception.Rule);
    }

    /// <summary>
    ///     Test that a pass can be recorded without a justification, and an empty one is stored as none.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswer_PassWithoutJustification_Stores()
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: pass a check with white space as justification
        await service.SetAnswerAsync(
            projectId, "rev-1", "REQ-01", ChecklistOutcome.Pass, "  ", TestActors.Ann, TestContext.Current.CancellationToken);
        var answer = (await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken))!.AnswerFor("REQ-01");

        // Assert: stored as a pass with no justification
        Assert.NotNull(answer);
        Assert.Equal(ChecklistOutcome.Pass, answer.Outcome);
        Assert.Null(answer.Justification);
    }

    /// <summary>
    ///     Test that a failed or not-applicable check needs a justification and stores nothing without it.
    /// </summary>
    /// <param name="outcome">The outcome that needs a justification.</param>
    [Theory]
    [InlineData(ChecklistOutcome.Fail)]
    [InlineData(ChecklistOutcome.NotApplicable)]
    public async Task ChecklistService_SetAnswer_FailOrNotApplicableWithoutJustification_Throws(ChecklistOutcome outcome)
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: answer without a justification
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetAnswerAsync(
                projectId, "rev-1", "REQ-01", outcome, " ", TestActors.Ann, TestContext.Current.CancellationToken));

        // Assert: the rule is named and the check is still unanswered
        Assert.Equal(DomainRule.ChecklistJustificationInvalid, exception.Rule);
        Assert.Null((await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken))!.AnswerFor("REQ-01"));
    }

    /// <summary>
    ///     Test that the justification limit is inclusive.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswer_JustificationTooLong_Throws()
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: answer with a justification of exactly the limit, then one more character
        await service.SetAnswerAsync(
            projectId, "rev-1", "REQ-01", ChecklistOutcome.Fail,
            new string('j', ReviewLimits.MaxJustificationLength), TestActors.Ann, TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetAnswerAsync(
                projectId, "rev-1", "REQ-02", ChecklistOutcome.Fail,
                new string('j', ReviewLimits.MaxJustificationLength + 1), TestActors.Ann, TestContext.Current.CancellationToken));

        // Assert: the limit is accepted, one more is refused
        Assert.Equal(DomainRule.ChecklistJustificationInvalid, exception.Rule);
        var state = await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken);
        Assert.NotNull(state!.AnswerFor("REQ-01"));
        Assert.Null(state.AnswerFor("REQ-02"));
    }

    /// <summary>
    ///     Test that a check that is not in the checklist is refused.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswer_UnknownItem_ThrowsChecklistItemNotFound()
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: answer an unknown check
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetAnswerAsync(
                projectId, "rev-1", "NOPE-01", ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.ChecklistItemNotFound, exception.Rule);
    }

    /// <summary>
    ///     Test that answering a check of a review without a checklist is refused.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswer_UnknownReview_ThrowsChecklistNotFound()
    {
        // Arrange: a store with one review
        var (_, service, projectId) = await CreateAsync();

        // Act: answer a check of another review
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetAnswerAsync(
                projectId, "other", "REQ-01", ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken));

        // Assert: the rule is named, and the message names the review
        Assert.Equal(DomainRule.ChecklistNotFound, exception.Rule);
        Assert.Contains("other", exception.Message);
    }

    /// <summary>
    ///     Test that an outcome that is not a defined value is refused and nothing is stored.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswer_UndefinedOutcome_Throws()
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: answer with an outcome value that is not defined
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.SetAnswerAsync(
                projectId, "rev-1", "REQ-01", (ChecklistOutcome)99, "Because", TestActors.Ann, TestContext.Current.CancellationToken));

        // Assert: nothing is stored
        Assert.Null((await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken))!.AnswerFor("REQ-01"));
    }

    /// <summary>
    ///     Test that answering again replaces the earlier answer and trims the justification.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswer_ReanswerWithPaddedJustification_OverwritesAndTrims()
    {
        // Arrange: a check passed once
        var (_, service, projectId) = await CreateAsync();
        await service.SetAnswerAsync(
            projectId, "rev-1", "REQ-01", ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);

        // Act: answer it again as failed, with padding around the justification
        await service.SetAnswerAsync(
            projectId, "rev-1", "REQ-01", ChecklistOutcome.Fail, "  Not traceable  ", TestActors.Ann, TestContext.Current.CancellationToken);
        var state = await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken);

        // Assert: only the new answer counts
        var answer = state!.AnswerFor("REQ-01")!;
        Assert.Equal(ChecklistOutcome.Fail, answer.Outcome);
        Assert.Equal("Not traceable", answer.Justification);
        Assert.Equal("Ann", answer.AnsweredBy);
        Assert.Equal(1, state.Overall.Answered);
    }

    /// <summary>
    ///     Test that a missing actor is refused for both an answer and a section change, and nothing is stored.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswer_NullActor_Throws()
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: answer and set a section without an actor
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.SetAnswerAsync(
                projectId, "rev-1", "REQ-01", ChecklistOutcome.Pass, null, null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.SetSectionApplicabilityAsync(
                projectId, "rev-1", "DES", false, "Reason", null!, TestContext.Current.CancellationToken));

        // Assert: nothing is stored
        var state = await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken);
        Assert.Null(state!.AnswerFor("REQ-01"));
        Assert.True(state.IsApplicable("DES"));
    }

    /// <summary>
    ///     Test that the answer carries the user name and display name of the actor and the UTC time of the clock.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswer_StoresActorAndUtcTime()
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: answer as an actor
        await service.SetAnswerAsync(
            projectId, "rev-1", "REQ-01", ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        var answer = (await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken))!.AnswerFor("REQ-01")!;

        // Assert: both names and the clock time
        Assert.Equal("Ann", answer.AnsweredBy);
        Assert.Equal("ann", answer.AnsweredByUsername);
        Assert.Equal(_now, answer.AnsweredAt);
        Assert.Equal(TimeSpan.Zero, answer.AnsweredAt.Offset);
    }

    /// <summary>
    ///     Test that the author of a review cannot answer a check or set a section while the rule is on: both are
    ///     refused with <see cref="DomainRule.AuthorCannotAnswer"/>, the message names the switch, the match ignores
    ///     case, and nothing is stored. Another member may answer.
    /// </summary>
    [Fact]
    public async Task ChecklistService_Answer_AuthorWhileRuleIsOn_ThrowsAuthorCannotAnswerAndStoresNothing()
    {
        // Arrange: a review created by the user "author"; the rule is on by default
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, service, projectId) = await CreateAsync();
        var sectionId = DefaultChecklist.Current.Sections[0].Id;

        // Act: the author (also with another case) answers and sets a section; ann, who holds the slot, answers
        var answer = await Assert.ThrowsAsync<DomainRuleException>(() => service.SetAnswerAsync(
            projectId, "rev-1", "REQ-01", ChecklistOutcome.Pass, null, TestActors.Author, cancellationToken));
        var section = await Assert.ThrowsAsync<DomainRuleException>(() => service.SetSectionApplicabilityAsync(
            projectId, "rev-1", sectionId, false, "Reason", new Actor("AUTHOR", "Author"), cancellationToken));
        await service.SetAnswerAsync(projectId, "rev-1", "REQ-01", ChecklistOutcome.Pass, null, TestActors.Ann, cancellationToken);

        // Assert: both refused with the rule and the switch named; only ann's answer is stored
        Assert.Equal(DomainRule.AuthorCannotAnswer, answer.Rule);
        Assert.Equal(DomainRule.AuthorCannotAnswer, section.Rule);
        Assert.Contains("Authors cannot answer the checklist of their own review", answer.Message, StringComparison.Ordinal);
        var state = (await service.GetAsync(projectId, "rev-1", cancellationToken))!;
        Assert.Equal("Ann", state.AnswerFor("REQ-01")!.AnsweredBy);
        Assert.Equal(1, state.Overall.Answered);
        Assert.True(state.IsApplicable(sectionId));
    }

    /// <summary>
    ///     Test that the author may answer once the project administrator has switched the rule off.
    /// </summary>
    [Fact]
    public async Task ChecklistService_Answer_AuthorWhileRuleIsOff_Stores()
    {
        // Arrange: the rule is switched off, and the author holds the one slot
        var cancellationToken = TestContext.Current.CancellationToken;
        var (store, service, projectId) = await CreateAsync("author");
        await TestServices.Projects(store, new FixedTimeProvider(_now)).SetReviewRuleOffAsync(
            projectId, ReviewRule.NonAuthorChecklistAnswers, cancellationToken);

        // Act
        await service.SetAnswerAsync(projectId, "rev-1", "REQ-01", ChecklistOutcome.Pass, null, TestActors.Author, cancellationToken);

        // Assert
        Assert.Equal("Author", (await service.GetAsync(projectId, "rev-1", cancellationToken))!.AnswerFor("REQ-01")!.AnsweredBy);
    }

    /// <summary>
    ///     Test that the author rule is checked before the lock: the author of an approved review is told about the
    ///     rule, not that the review is locked.
    /// </summary>
    [Fact]
    public async Task ChecklistService_Answer_AuthorOfApprovedReview_ReportsAuthorRuleBeforeLock()
    {
        // Arrange: an approved review created by the user "author"
        var cancellationToken = TestContext.Current.CancellationToken;
        var (store, service, projectId) = await CreateAsync(null);
        await AuditTestExtensions.ApproveReviewAsync(store, new FixedTimeProvider(_now), projectId, "rev-1", "approver", cancellationToken);

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => service.SetAnswerAsync(
            projectId, "rev-1", "REQ-01", ChecklistOutcome.Fail, "Wrong.", TestActors.Author, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.AuthorCannotAnswer, ex.Rule);
    }

    /// <summary>
    ///     Test that the state tells whether the viewer is blocked: only for the author, only while the rule is on, and
    ///     never without a viewer.
    /// </summary>
    [Fact]
    public async Task ChecklistService_Get_ViewerUsername_SetsAuthorBlockedOnlyForTheAuthorWhileRuleIsOn()
    {
        // Arrange: a review created by the user "author"
        var cancellationToken = TestContext.Current.CancellationToken;
        var (store, service, projectId) = await CreateAsync();

        // Act, Assert: the author is blocked (in any case), others and a missing viewer are not
        Assert.True((await service.GetAsync(projectId, "rev-1", cancellationToken, "author"))!.AuthorBlocked);
        Assert.True((await service.GetAsync(projectId, "rev-1", cancellationToken, "AUTHOR"))!.AuthorBlocked);
        Assert.False((await service.GetAsync(projectId, "rev-1", cancellationToken, "bob"))!.AuthorBlocked);
        Assert.False((await service.GetAsync(projectId, "rev-1", cancellationToken))!.AuthorBlocked);
        Assert.False((await service.GetAsync(projectId, "rev-1", cancellationToken, "  "))!.AuthorBlocked);

        // Act: switch the rule off
        await TestServices.Projects(store, new FixedTimeProvider(_now)).SetReviewRuleOffAsync(
            projectId, ReviewRule.NonAuthorChecklistAnswers, cancellationToken);

        // Assert: nobody is blocked
        Assert.False((await service.GetAsync(projectId, "rev-1", cancellationToken, "author"))!.AuthorBlocked);
    }

    /// <summary>
    ///     Test that the constructor refuses a missing store, access, user store or clock.
    /// </summary>
    [Fact]
    public void ChecklistService_Constructor_NullArguments_Throws()
    {
        // Arrange, Act, Assert: each missing dependency is refused
        var store = new FakeReviewStore();
        var access = new AllowAllProjectAccess(store);
        var users = new FakeUserStore();
        Assert.Throws<ArgumentNullException>(() => new ChecklistService(null!, access, users, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new ChecklistService(store, null!, users, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new ChecklistService(store, access, null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new ChecklistService(store, access, users, null!));
    }
}

/// <summary>
///     Unit tests for the progress rules of <see cref="ChecklistState"/>, over the default checklist.
/// </summary>
public class ChecklistStateTests
{
    /// <summary>
    ///     The instant used for all answers.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Builds a state over the default checklist.
    /// </summary>
    /// <param name="answered">The identifiers of the checks that are answered.</param>
    /// <param name="notApplicable">The identifiers of the sections marked as not applicable.</param>
    /// <returns>The state.</returns>
    private static ChecklistState Build(IEnumerable<string> answered, params string[] notApplicable)
    {
        var answers = answered.Select(id => new ChecklistAnswer
        {
            ProjectId = 1,
            ReviewId = "rev-1",
            ItemId = id,
            Outcome = ChecklistOutcome.Pass,
            AnsweredBy = "Ann",
            AnsweredAt = _now
        });
        var sections = notApplicable.Select(id => new ChecklistSectionState
        {
            ProjectId = 1,
            ReviewId = "rev-1",
            SectionId = id,
            Applicable = false,
            Reason = "Not relevant",
            SetBy = "Ann",
            SetAt = _now
        });
        return new ChecklistState(DefaultChecklist.Current, sections, answers);
    }

    /// <summary>
    ///     Test that progress counts the answered checks per section and overall.
    /// </summary>
    [Fact]
    public void ChecklistState_Progress_CountsAnsweredPerSectionAndOverall()
    {
        // Arrange: two checks of the first section and one of the second are answered
        var state = Build(["REQ-01", "REQ-02", "DES-01"]);

        // Act, Assert: per-section and overall progress
        Assert.Equal(new ChecklistProgress(2, 8), state.SectionProgress("REQ"));
        Assert.Equal(new ChecklistProgress(1, 5), state.SectionProgress("DES"));
        Assert.Equal(new ChecklistProgress(3, 58), state.Overall);
        Assert.Equal(55, state.Overall.Unanswered);
        Assert.False(state.IsComplete);
    }

    /// <summary>
    ///     Test that a not-applicable section counts as done and leaves the total.
    /// </summary>
    [Fact]
    public void ChecklistState_Progress_NotApplicableSection_CountsAsDoneAndLeavesTotal()
    {
        // Arrange: the 5-check design section does not apply
        var state = Build([], "DES");

        // Act, Assert: the section is done with nothing to answer, and the total is smaller
        Assert.True(state.SectionProgress("DES").IsDone);
        Assert.Equal(new ChecklistProgress(0, 53), state.Overall);
    }

    /// <summary>
    ///     Test that answers kept in a section that does not apply are not counted.
    /// </summary>
    [Fact]
    public void ChecklistState_Progress_AnswersInNotApplicableSection_Ignored()
    {
        // Arrange: a design check was answered, then the design section was marked as not applicable
        var state = Build(["DES-01", "REQ-01"], "DES");

        // Act, Assert: only the requirements answer counts
        Assert.Equal(new ChecklistProgress(1, 53), state.Overall);
    }

    /// <summary>
    ///     Test that the checklist is complete when every check of every applicable section is answered.
    /// </summary>
    [Fact]
    public void ChecklistState_Progress_AllApplicableAnswered_IsComplete()
    {
        // Arrange: every check is answered except those of a section that does not apply
        var designIds = DefaultChecklist.Current.Sections.Single(s => s.Id == "DES").Items.Select(i => i.Id).ToHashSet();
        var ids = DefaultChecklist.Current.Sections.SelectMany(s => s.Items).Select(i => i.Id).Where(id => !designIds.Contains(id));
        var state = Build(ids, "DES");

        // Act, Assert: complete, with nothing left to do
        Assert.True(state.IsComplete);
        Assert.Null(state.FirstIncompleteSectionId());
        Assert.Null(state.NextUnansweredItemId());
    }

    /// <summary>
    ///     Test that a checklist where no section applies is complete.
    /// </summary>
    [Fact]
    public void ChecklistState_Progress_AllSectionsNotApplicable_IsComplete()
    {
        // Arrange: every section is marked as not applicable
        var state = Build([], DefaultChecklist.Current.Sections.Select(s => s.Id).ToArray());

        // Act, Assert: nothing to answer, so complete
        Assert.Equal(new ChecklistProgress(0, 0), state.Overall);
        Assert.True(state.IsComplete);
    }

    /// <summary>
    ///     Test that the next unanswered check is the first one of the applicable sections.
    /// </summary>
    [Fact]
    public void ChecklistState_NextUnansweredItemId_SkipsAnsweredAndNotApplicableSections()
    {
        // Arrange: the first section is fully answered and the second does not apply
        var requirementIds = DefaultChecklist.Current.Sections[0].Items.Select(i => i.Id);
        var state = Build(requirementIds, "DES");

        // Act, Assert: the first check of the third section is next
        Assert.Equal("DOC-01", state.NextUnansweredItemId());
    }

    /// <summary>
    ///     Test that the first incomplete section is the first applicable section with unanswered checks.
    /// </summary>
    [Fact]
    public void ChecklistState_FirstIncompleteSectionId_ReturnsFirstSectionWithUnanswered()
    {
        // Arrange: the first section is partly answered, the second does not apply
        var state = Build(["REQ-01"], "DES");

        // Act, Assert: the partly answered first section is first incomplete
        Assert.Equal("REQ", state.FirstIncompleteSectionId());

        // Arrange, Act, Assert: with the first section fully answered the next applicable one follows
        var all = Build(DefaultChecklist.Current.Sections[0].Items.Select(i => i.Id), "DES");
        Assert.Equal("DOC", all.FirstIncompleteSectionId());
    }
}
