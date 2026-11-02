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
///     Decides whether a viewer may see what the other reviewers answered in a review.
/// </summary>
/// <remarks>
///     <para>
///         Reviewers answer independently: in a review with more than one reviewer slot, nobody sees the ballot of
///         another reviewer, the number of checks the ballots disagree on, the provisional outcome, the conclusion text
///         or the audit detail of an answer until their own ballot is handed in. That keeps a reviewer from being
///         swayed by an answer that is not yet final. Who has handed in or signed stays visible, because it is
///         progress and not a verdict: the number of ballots handed in out of the total and the names of the people
///         who have not handed in one show in the inbox ("Waiting for others") and in the project overview to everyone
///         who can see the project, while the disputes, the failed checks and the outcome stay masked.
///     </para>
///     <para>
///         Everyone sees everything when the review has a single slot (nobody else answers), when the review is
///         concluded, and for the lead of the review, a project or system administrator and an Auditor, who steer or
///         check the review. A person without a slot (a plain contributor) is hidden from the others' answers like a
///         reviewer with a draft. The rules are pure (no I/O); the services gather the facts and these rules alone
///         decide. The class is stateless and thread-safe.
///     </para>
/// </remarks>
public static class BallotVisibility
{
    /// <summary>
    ///     Tells whether a viewer may see the answers of the other reviewers.
    /// </summary>
    /// <param name="seesAll">
    ///     <c>true</c> for the lead of the review, an administrator of the project or the system, and an Auditor.
    /// </param>
    /// <param name="slotCount">The number of reviewer slots of the review.</param>
    /// <param name="ownBallot">
    ///     The state of the ballot of the viewer's own slot; <see cref="BallotState.NotStarted"/> or <c>null</c> for a
    ///     viewer who holds no slot or has not started.
    /// </param>
    /// <param name="concluded"><c>true</c> when the review is concluded (approved or rejected).</param>
    /// <returns>
    ///     <c>true</c> when the review has at most one slot, is concluded, the viewer sees everything, or the viewer's own
    ///     ballot is submitted; <c>false</c> otherwise.
    /// </returns>
    public static bool CanSeeOthers(bool seesAll, int slotCount, BallotState? ownBallot, bool concluded)
    {
        // Nobody else answers in a review with a single slot, and a concluded review has nothing left to protect
        if (slotCount <= 1 || concluded)
        {
            return true;
        }

        // The people who steer or check the review see everything
        if (seesAll)
        {
            return true;
        }

        // Everybody else sees the others once their own ballot counts
        return ownBallot == BallotState.Submitted;
    }

    /// <summary>
    ///     Tells whether a viewer may see the answers of the other reviewers, from the facts of a snapshot.
    /// </summary>
    /// <param name="seesAll">
    ///     <c>true</c> for the lead of the review, an administrator of the project or the system, and an Auditor.
    /// </param>
    /// <param name="snapshot">The snapshot of the review. Must not be null.</param>
    /// <param name="viewerUserId">The identity of the viewer; <c>null</c> for nobody or an unknown user.</param>
    /// <returns>The same answer as <see cref="CanSeeOthers(bool, int, BallotState?, bool)"/> for the facts of the snapshot.</returns>
    public static bool CanSeeOthers(bool seesAll, ReviewSnapshot snapshot, int? viewerUserId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Cheap answers first: these need neither the viewer nor the ballots
        var slotCount = snapshot.Slots.Count;
        var concluded = ReviewLock.IsConcluded(snapshot.Record, snapshot.Slots, snapshot.Signatures);
        if (slotCount <= 1 || concluded || seesAll)
        {
            return true;
        }

        // The viewer's own ballot: the slot with the lowest identity that the viewer holds, as the checklist does
        var own = ReviewActorRoles.FindOwnSlot(viewerUserId, snapshot.Slots);
        var ballot = own is null ? null : snapshot.Record.Ballots.FirstOrDefault(b => b.SlotId == own.Id);
        return CanSeeOthers(seesAll, slotCount, ballot?.State, concluded);
    }

    /// <summary>
    ///     Tells whether a viewer sees every review of a project without handing in a ballot: an administrator of the
    ///     project or the system, or an Auditor.
    /// </summary>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <param name="viewer">The viewer; <c>null</c> and an API token see nothing special.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the viewer may audit the project.</returns>
    internal static async Task<bool> SeesAllInProjectAsync(
        IProjectAccess access,
        Actor? viewer,
        int projectId,
        CancellationToken cancellationToken)
    {
        // The audit right covers the system administrators, the administrators of the project and the Auditors
        return viewer is { ViaApiToken: false } && await access.CanAuditAsync(viewer, projectId, cancellationToken);
    }

    /// <summary>
    ///     Tells whether a viewer sees everything in one review without handing in a ballot: the project-wide roles of
    ///     <see cref="SeesAllInProjectAsync"/>, and the lead of the review.
    /// </summary>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <param name="viewer">The viewer; <c>null</c> and an API token see nothing special.</param>
    /// <param name="review">The review. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the viewer is an administrator, an Auditor or the lead of the review.</returns>
    internal static async Task<bool> SeesAllInReviewAsync(
        IProjectAccess access,
        Actor? viewer,
        Review review,
        CancellationToken cancellationToken)
    {
        return await SeesAllInProjectAsync(access, viewer, review.ProjectId, cancellationToken)
               || await ReviewActorRoles.IsLeadAsync(access, viewer, review, cancellationToken);
    }
}
