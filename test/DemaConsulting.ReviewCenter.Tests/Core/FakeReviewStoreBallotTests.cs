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
///     Tests that <see cref="FakeReviewStore"/> keeps the ballots of the slots the way the real store does, so that the
///     service tests of later changes can rely on it.
/// </summary>
public class FakeReviewStoreBallotTests
{
    /// <summary>
    ///     The time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _time = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Adds a project with the review <c>REV-1</c> and the given number of slots.
    /// </summary>
    /// <param name="store">The fake store.</param>
    /// <param name="slotCount">The number of slots to add.</param>
    /// <returns>The project identity and the slot identities.</returns>
    private static async Task<(int ProjectId, List<int> SlotIds)> ArrangeAsync(FakeReviewStore store, int slotCount)
    {
        var project = await store.AddProjectAsync(new Project { Name = "P", Created = _time });
        await store.AddReviewAsync(new Review
        {
            ProjectId = project.Id,
            Id = "REV-1",
            Title = "Title",
            State = ReviewState.Draft,
            Created = _time,
            ContentFingerprint = TestContent.FingerprintA
        });
        var slotIds = new List<int>();
        for (var i = 1; i <= slotCount; i++)
        {
            var slot = await store.AddSlotAsync(
                new ReviewerSlot { ProjectId = project.Id, ReviewId = "REV-1", PrimaryUserId = i });
            slotIds.Add(slot.Id);
        }

        return (project.Id, slotIds);
    }

    /// <summary>
    ///     Builds an answer in the ballot of a slot.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="username">The user name of the answerer.</param>
    /// <param name="itemId">The check identifier.</param>
    /// <returns>The answer.</returns>
    private static ChecklistAnswer NewAnswer(
        int projectId,
        int slotId,
        ChecklistOutcome outcome = ChecklistOutcome.Pass,
        string username = "ann",
        string itemId = "REQ-01")
    {
        return new ChecklistAnswer
        {
            ProjectId = projectId,
            ReviewId = "REV-1",
            SlotId = slotId,
            ItemId = itemId,
            Outcome = outcome,
            AnsweredBy = username,
            AnsweredByUsername = username,
            AnsweredAt = _time
        };
    }

    /// <summary>
    ///     Test that the fake keeps one row per check and slot, lists them in the ballot of their slot and keeps them out
    ///     of the review-level rows.
    /// </summary>
    [Fact]
    public async Task FakeReviewStore_SetBallotAnswerAsync_SlotsAreKeptApart()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var (projectId, slots) = await ArrangeAsync(store, 2);

        // Act
        await store.SetBallotAnswerAsync(NewAnswer(projectId, slots[0]), AuditTestExtensions.Draft(), null, cancellationToken);
        await store.SetBallotAnswerAsync(
            NewAnswer(projectId, slots[1], ChecklistOutcome.Fail, "bob"), AuditTestExtensions.Draft(), null, cancellationToken);
        var ballots = await store.ListBallotsAsync(projectId, "REV-1", cancellationToken);

        // Assert
        Assert.Equal(slots, ballots.Select(b => b.SlotId));
        Assert.Equal(ChecklistOutcome.Pass, Assert.Single(ballots[0].Answers).Outcome);
        Assert.Equal(ChecklistOutcome.Fail, Assert.Single(ballots[1].Answers).Outcome);
        Assert.Empty(await store.ListChecklistAnswersAsync(projectId, "REV-1", cancellationToken));
        Assert.Empty((await store.GetRecordAsync(projectId, "REV-1", cancellationToken))!.Answers);
    }

    /// <summary>
    ///     Test that the fake sends a handed-in ballot back to a draft when it is edited, as the real store does, and
    ///     records the reopen entry after the entry of the edit.
    /// </summary>
    [Fact]
    public async Task FakeReviewStore_SetBallotAnswerAsync_SubmittedBallotEdited_RevertsToDraft()
    {
        // Arrange: a handed-in ballot in a review with two slots
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var (projectId, slots) = await ArrangeAsync(store, 2);
        await store.SetBallotAnswerAsync(NewAnswer(projectId, slots[0]), AuditTestExtensions.Draft(), null, cancellationToken);
        await store.SubmitBallotAsync(
            projectId, "REV-1", slots[0], _time, "ann", AuditTestExtensions.Draft(), _ => true, cancellationToken);
        var auditBefore = store.Audits.Count;

        // Act
        var ballot = await store.SetBallotAnswerAsync(
            NewAnswer(projectId, slots[0], ChecklistOutcome.Fail), AuditTestExtensions.Draft(), null, cancellationToken);

        // Assert
        Assert.Equal(BallotStatus.Draft, ballot.Status);
        Assert.Null(ballot.SubmittedAt);
        Assert.Equal(["test.change", "ballot.reopened"], store.Audits.Skip(auditBefore).Select(a => a.Action));
    }

    /// <summary>
    ///     Test that a slot added to a follow-up review starts from a draft ballot with the carried rows.
    /// </summary>
    [Fact]
    public async Task FakeReviewStore_AddSlotAsync_FollowUpBaseline_SeedsDraftBallot()
    {
        // Arrange: a follow-up review that carries one answer
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var (projectId, _) = await ArrangeAsync(store, 0);
        var carry = new ReviewCarryOver(
            [
                new ChecklistAnswer
                {
                    ProjectId = projectId,
                    ReviewId = "REV-2",
                    ItemId = "REQ-01",
                    Outcome = ChecklistOutcome.Pass,
                    AnsweredBy = "Ann",
                    AnsweredByUsername = "ann",
                    AnsweredAt = _time,
                    CarriedFromReviewId = "REV-1"
                }
            ],
            [],
            [],
            0,
            []);
        await store.AddReviewAsync(new Review
        {
            ProjectId = projectId,
            Id = "REV-2",
            Title = "Title",
            State = ReviewState.Draft,
            Created = _time,
            ContentFingerprint = TestContent.FingerprintA,
            FollowsReviewId = "REV-1",
            CarryOver = carry
        },
        cancellationToken);

        // Act
        var slot = await store.AddSlotAsync(
            new ReviewerSlot { ProjectId = projectId, ReviewId = "REV-2", PrimaryUserId = 1 }, cancellationToken);
        var ballot = await store.GetBallotAsync(projectId, "REV-2", slot.Id, cancellationToken);

        // Assert
        Assert.Equal(BallotStatus.Draft, ballot!.Status);
        var answer = Assert.Single(ballot.Answers);
        Assert.Equal((slot.Id, "REV-1"), (answer.SlotId, answer.CarriedFromReviewId));
        Assert.Single(await store.ListChecklistAnswersAsync(projectId, "REV-2", cancellationToken));
    }

    /// <summary>
    ///     Test that a reviewer who leaves a slot takes the rows of the reviewer along, found through the user name the
    ///     test configures, and that the others stay.
    /// </summary>
    [Fact]
    public async Task FakeReviewStore_UpdateSlotAsync_DepartingUser_RemovesOwnRows()
    {
        // Arrange: slot 1 has the reviewers 1 (ann) and 2 (bob), who both answered
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore
        {
            UsernameOfUser = id => id == 1 ? "ann" : "bob"
        };
        var (projectId, slots) = await ArrangeAsync(store, 2);
        await store.UpdateSlotAsync(projectId, "REV-1", slots[0], 1, 2, AuditTestExtensions.Draft(), cancellationToken);
        await store.SetBallotAnswerAsync(NewAnswer(projectId, slots[0]), AuditTestExtensions.Draft(), null, cancellationToken);
        await store.SetBallotAnswerAsync(
            NewAnswer(projectId, slots[0], itemId: "REQ-02", username: "bob"), AuditTestExtensions.Draft(), null, cancellationToken);
        var auditBefore = store.Audits.Count;

        // Act: ann is replaced by user 3
        await store.UpdateSlotAsync(projectId, "REV-1", slots[0], 2, 3, AuditTestExtensions.Draft(), cancellationToken);
        var ballot = await store.GetBallotAsync(projectId, "REV-1", slots[0], cancellationToken);

        // Assert
        Assert.Equal(BallotStatus.Draft, ballot!.Status);
        Assert.Equal(["test.change", "ballot.reassigned"], store.Audits.Skip(auditBefore).Select(a => a.Action));
    }
}
