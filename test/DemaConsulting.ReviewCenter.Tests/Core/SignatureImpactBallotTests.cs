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
///     Unit tests of <see cref="SignatureImpact"/> for ballots: a change that alters the agreed standing needs the
///     acknowledgement of the current signatures of the other slots, and a change that leaves it alone needs none.
/// </summary>
public class SignatureImpactBallotTests
{
    /// <summary>
    ///     Reads the snapshot of the review of the world.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <returns>The snapshot.</returns>
    private static async Task<ReviewSnapshot> SnapshotAsync(SignedReviewWorld world)
    {
        return (await world.Store.GetSnapshotAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    ///     Builds the record as it would be if the ballot of a slot disagreed on the first check.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="slotId">The slot whose ballot changes.</param>
    /// <returns>The changed record.</returns>
    private static ReviewRecord WithDissent(ReviewSnapshot snapshot, int slotId)
    {
        var ballot = snapshot.Record.Ballots.Single(b => b.SlotId == slotId);
        var answers = ballot.Answers
            .Select(a => a.ItemId == SignedReviewWorld.FirstItemId
                ? new ChecklistAnswer
                {
                    ProjectId = a.ProjectId,
                    ReviewId = a.ReviewId,
                    SlotId = a.SlotId,
                    ItemId = a.ItemId,
                    Outcome = ChecklistOutcome.Fail,
                    Justification = "Wrong.",
                    AnsweredBy = a.AnsweredBy,
                    AnsweredByUsername = a.AnsweredByUsername,
                    AnsweredAt = a.AnsweredAt
                }
                : a)
            .ToList();
        return BallotEdits.WithBallot(snapshot.Record, ballot with { Answers = answers });
    }

    /// <summary>
    ///     Test that a change that alters the agreed standing is refused until the signature of another slot is
    ///     acknowledged.
    /// </summary>
    [Fact]
    public async Task SignatureImpact_RequireAcknowledged_DifferingBallot_ListsTheOtherSlot()
    {
        // Arrange: rita signed
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        var snapshot = await SnapshotAsync(world);
        var changed = WithDissent(snapshot, world.Slots[1].Id);

        // Act
        var ex = Assert.Throws<DomainRuleException>(() => SignatureImpact.RequireAcknowledged(snapshot, changed, null));
        SignatureImpact.RequireAcknowledged(snapshot, changed, [world.Slots[0].Id]);

        // Assert
        Assert.Equal(DomainRule.SignaturesWouldBecomeOutOfDate, ex.Rule);
        Assert.Contains("slot 1 (Rita)", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the slot of the person who changes the own ballot is left out when it is excluded.
    /// </summary>
    [Fact]
    public async Task SignatureImpact_RequireAcknowledged_OwnSlotExcluded_NeedsNoAcknowledgement()
    {
        // Arrange: rita signed, and rita's own ballot changes
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        var snapshot = await SnapshotAsync(world);
        var changed = WithDissent(snapshot, world.Slots[0].Id);

        // Act and assert: no exception when her own slot is excluded
        Assert.Null(Record.Exception(() => SignatureImpact.RequireAcknowledged(snapshot, changed, null, world.Slots[0].Id)));
    }

    /// <summary>
    ///     Test that a change that leaves the agreed standing as it is warns nobody.
    /// </summary>
    [Fact]
    public async Task SignatureImpact_RequireAcknowledged_SameStanding_NoWarning()
    {
        // Arrange: rita signed; the change is the same record
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        var snapshot = await SnapshotAsync(world);

        // Act and assert
        SignatureImpact.RequireAcknowledged(snapshot, snapshot.Record, null);
        Assert.Single(SignatureImpact.CurrentSignatures(snapshot));
    }
}
