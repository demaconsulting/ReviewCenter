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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     Decides who may start a discussion about a check of the frozen checklist.
/// </summary>
/// <remarks>
///     A check thread names a check, and the ballots of the other reviewers stay hidden until a reviewer has handed in
///     their own, so an early discussion about a check could reveal how another reviewer voted. The rule therefore lets
///     a check thread be started by a system administrator, by the lead of the review, and by a reviewer whose own
///     ballot is submitted. In a review with a single reviewer slot nothing is hidden from the only reviewer, so that
///     reviewer may start a check thread while the ballot is still a draft. Replying to and resolving a check thread
///     are not governed by this rule. The rule is pure (no I/O), so <see cref="ThreadService"/> gathers the facts and
///     the rule alone decides; the class is stateless and thread-safe.
/// </remarks>
public static class CheckThreadRules
{
    /// <summary>
    ///     Tells whether a person may start a check thread.
    /// </summary>
    /// <param name="isSystemAdmin"><c>true</c> when the person is a system administrator.</param>
    /// <param name="isLead">
    ///     <c>true</c> when the person leads the review (its creator, or an administrator of the project).
    /// </param>
    /// <param name="isSlotOwner"><c>true</c> when the person is the primary or the alternate reviewer of a slot.</param>
    /// <param name="ownBallotStatus">
    ///     The status of the ballot of the person's slot; <c>null</c> when the slot has no ballot yet or the person
    ///     holds no slot. Only read when <paramref name="isSlotOwner"/> is set.
    /// </param>
    /// <param name="slotCount">The number of reviewer slots of the review.</param>
    /// <returns>
    ///     <c>true</c> for a system administrator, for the lead, and for a slot owner whose ballot is
    ///     <see cref="BallotStatus.Submitted"/> or who holds the only slot of the review; <c>false</c> otherwise.
    /// </returns>
    public static bool CanStart(
        bool isSystemAdmin,
        bool isLead,
        bool isSlotOwner,
        BallotStatus? ownBallotStatus,
        int slotCount)
    {
        // An administrator and the lead steer the review and may always start a discussion
        if (isSystemAdmin || isLead)
        {
            return true;
        }

        // A reviewer who has handed in a ballot has nothing left to hide from; the only reviewer of a review has no
        // one else's ballot to learn about, so a draft is enough
        return isSlotOwner && (ownBallotStatus == BallotStatus.Submitted || slotCount == 1);
    }
}
