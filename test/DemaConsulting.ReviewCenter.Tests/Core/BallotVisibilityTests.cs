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
///     Unit tests of the pure <see cref="BallotVisibility"/> rule that decides whether a viewer may see what the other
///     reviewers answered.
/// </summary>
public class BallotVisibilityTests
{
    /// <summary>
    ///     Test that a review with one slot or none hides nothing, whoever looks and whatever their ballot.
    /// </summary>
    /// <param name="slotCount">The number of slots.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void BallotVisibility_CanSeeOthers_SingleSlot_AlwaysSeesAll(int slotCount)
    {
        // Act
        var result = BallotVisibility.CanSeeOthers(false, slotCount, BallotState.NotStarted, false);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    ///     Test that a plain contributor without a slot cannot see the others of a review with several slots.
    /// </summary>
    [Fact]
    public void BallotVisibility_CanSeeOthers_PlainContributorWithoutSlot_Masked()
    {
        // Act
        var result = BallotVisibility.CanSeeOthers(false, 2, null, false);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    ///     Test that a slot owner with a draft or without a start cannot see the others.
    /// </summary>
    /// <param name="state">The state of the own ballot.</param>
    [Theory]
    [InlineData(BallotState.NotStarted)]
    [InlineData(BallotState.Draft)]
    public void BallotVisibility_CanSeeOthers_SlotOwnerWithoutSubmittedBallot_Masked(BallotState state)
    {
        // Act
        var result = BallotVisibility.CanSeeOthers(false, 3, state, false);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    ///     Test that a slot owner who handed in sees the others.
    /// </summary>
    [Fact]
    public void BallotVisibility_CanSeeOthers_SlotOwnerSubmitted_SeesOthers()
    {
        // Act
        var result = BallotVisibility.CanSeeOthers(false, 3, BallotState.Submitted, false);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    ///     Test that the lead, an administrator and an Auditor (who see everything) are never masked.
    /// </summary>
    [Fact]
    public void BallotVisibility_CanSeeOthers_LeadAdminAuditor_SeeAll()
    {
        // Act
        var result = BallotVisibility.CanSeeOthers(true, 4, null, false);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    ///     Test that everyone sees everything once the review is concluded.
    /// </summary>
    [Fact]
    public void BallotVisibility_CanSeeOthers_Concluded_SeesAll()
    {
        // Act
        var result = BallotVisibility.CanSeeOthers(false, 4, null, true);

        // Assert
        Assert.True(result);
    }
}
