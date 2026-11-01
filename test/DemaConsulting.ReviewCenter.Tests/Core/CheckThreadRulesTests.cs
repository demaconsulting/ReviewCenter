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
///     Unit tests of <see cref="CheckThreadRules"/>: who may start a discussion about a check, decided from facts only.
/// </summary>
public class CheckThreadRulesTests
{
    /// <summary>
    ///     Test that a reviewer who has handed in their ballot may start a check discussion, whatever the number of
    ///     slots, so they learn nothing about the other ballots by doing so.
    /// </summary>
    [Fact]
    public void CheckThreadRules_CanStart_SubmittedSlotOwner_IsAllowed()
    {
        // Arrange, Act, Assert
        Assert.True(CheckThreadRules.CanStart(false, false, true, BallotStatus.Submitted, 1));
        Assert.True(CheckThreadRules.CanStart(false, false, true, BallotStatus.Submitted, 3));
    }

    /// <summary>
    ///     Test that a slot owner with a draft ballot or none is refused when the review has several slots.
    /// </summary>
    [Fact]
    public void CheckThreadRules_CanStart_DraftOrNoBallotSlotOwner_IsRefusedWhenSeveralSlots()
    {
        // Arrange, Act, Assert
        Assert.False(CheckThreadRules.CanStart(false, false, true, BallotStatus.Draft, 2));
        Assert.False(CheckThreadRules.CanStart(false, false, true, null, 2));
        Assert.False(CheckThreadRules.CanStart(false, false, true, BallotStatus.Draft, 3));
    }

    /// <summary>
    ///     Test that the only reviewer of a review may start a check discussion with a draft ballot or none, because
    ///     there is nobody whose view could leak to them.
    /// </summary>
    [Fact]
    public void CheckThreadRules_CanStart_SoleSlotOwnerWithDraft_IsAllowed()
    {
        // Arrange, Act, Assert
        Assert.True(CheckThreadRules.CanStart(false, false, true, BallotStatus.Draft, 1));
        Assert.True(CheckThreadRules.CanStart(false, false, true, null, 1));
    }

    /// <summary>
    ///     Test that the lead (creator or project administrator) and a system administrator may start a check
    ///     discussion whatever the slots and ballots are, and that a person with no role may not.
    /// </summary>
    [Fact]
    public void CheckThreadRules_CanStart_LeadCreatorProjectAdminAndSystemAdmin_AreAllowed()
    {
        // Arrange, Act, Assert
        Assert.True(CheckThreadRules.CanStart(false, true, false, null, 3));
        Assert.True(CheckThreadRules.CanStart(true, false, false, null, 3));
        Assert.True(CheckThreadRules.CanStart(false, true, true, BallotStatus.Draft, 3));
        Assert.False(CheckThreadRules.CanStart(false, false, false, null, 3));
        Assert.False(CheckThreadRules.CanStart(false, false, false, null, 1));
    }
}
