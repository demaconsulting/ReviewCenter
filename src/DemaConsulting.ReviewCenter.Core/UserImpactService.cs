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
///     Tells what would be left behind when a user leaves: the review work that still waits for the user and the
///     projects that would have no enabled project administrator any more.
/// </summary>
/// <remarks>
///     The answer is information for a warning and never a block, because a leaver may have to be disabled at once.
///     Open work is a slot of an unlocked (not approved) review in which the user is the primary or the alternate
///     reviewer and which holds no signature of the current record: <see cref="SlotSignatureState.NotSigned"/> or
///     <see cref="SlotSignatureState.OutOfDate"/>, decided by the same rules the approval status and the inbox use
///     (<see cref="ReviewLock"/>, <see cref="RecordDigest"/>), not by a copy of them. A sole administrator project is
///     one in which the user is an enabled project administrator and no other enabled project administrator exists
///     (the last-administrator rule of the membership changes); system administrators do not count there.
///     A system administrator may ask about any user. A project administrator may ask about any user too, but the
///     answer is limited to the projects the asker administers, so nothing is learned about other projects. Everyone
///     else is refused. Nothing is stored or audited. The service holds no state of its own and is safe for
///     concurrent use as long as the injected stores are.
/// </remarks>
public sealed class UserImpactService
{
    /// <summary>
    ///     The message of the refusal for an actor who administers no project.
    /// </summary>
    private const string NotAllowedMessage = "Only a system administrator or a project administrator may do this.";

    /// <summary>
    ///     The store of the slots, reviews and project memberships.
    /// </summary>
    private readonly IReviewStore _reviews;

    /// <summary>
    ///     The store of the users, to tell that a user exists.
    /// </summary>
    private readonly IUserStore _users;

    /// <summary>
    ///     Decides who may ask.
    /// </summary>
    private readonly IProjectAccess _access;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UserImpactService"/> class.
    /// </summary>
    /// <param name="reviews">The store of the reviews. Must not be null.</param>
    /// <param name="users">The store of the users. Must not be null.</param>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public UserImpactService(IReviewStore reviews, IUserStore users, IProjectAccess access)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(access);
        _reviews = reviews;
        _users = users;
        _access = access;
    }

    /// <summary>
    ///     Finds what would be left behind when a user leaves.
    /// </summary>
    /// <param name="actor">The signed-in user who asks. Must not be null.</param>
    /// <param name="userId">The identity of the user who may leave.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The open work and the sole administrator projects; for a project administrator only those of the projects
    ///     the asker administers.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/> when the actor is neither a system administrator nor
    ///     an administrator of a project (a disabled actor has no rights at all), and with
    ///     <see cref="DomainRule.UserNotFound"/> when there is no such user.
    /// </exception>
    public async Task<UserImpact> GetImpactAsync(
        Actor actor,
        int userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // Who asks decides the scope: null means every project
        var scope = await FindScopeAsync(actor, cancellationToken);
        if (!(await _users.ListAsync(cancellationToken)).Any(user => user.Id == userId))
        {
            throw new DomainRuleException(DomainRule.UserNotFound, "The user does not exist.");
        }

        var openWork = await FindOpenWorkAsync(_reviews, userId, scope, cancellationToken);
        var soleAdmin = (await _reviews.ListSoleAdminProjectsAsync(userId, cancellationToken))
            .Where(project => scope is null || scope.Contains(project.Id))
            .Select(project => new SoleAdminProject(project.Id, project.Name))
            .ToList();
        return new UserImpact(openWork, soleAdmin);
    }

    /// <summary>
    ///     Finds the projects the answer is limited to.
    /// </summary>
    /// <param name="actor">The actor who asks.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The identities of the administered projects, or <c>null</c> for a system administrator (no limit).</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ActorNotAllowed"/> when there are none.</exception>
    private async Task<HashSet<int>?> FindScopeAsync(Actor actor, CancellationToken cancellationToken)
    {
        if (await _access.IsSystemAdminAsync(actor, cancellationToken))
        {
            return null;
        }

        var administered = new HashSet<int>();
        var visible = (await _access.ListVisibleProjectsAsync(actor, cancellationToken)).Select(project => project.Id);
        foreach (var projectId in visible)
        {
            if (await _access.CanAdministerAsync(actor, projectId, cancellationToken))
            {
                administered.Add(projectId);
            }
        }

        return administered.Count > 0
            ? administered
            : throw new DomainRuleException(DomainRule.ActorNotAllowed, NotAllowedMessage);
    }

    /// <summary>
    ///     Finds the slots of unlocked reviews that still wait for a user. This is the one definition of "open review
    ///     work", shared with the membership change of <see cref="ProjectService"/> (making a member an auditor).
    /// </summary>
    /// <param name="reviews">The store of the reviews.</param>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="scope">The projects to consider, or <c>null</c> for all.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The open work ordered by project name, review identifier and slot number.</returns>
    internal static async Task<IReadOnlyList<UserOpenWork>> FindOpenWorkAsync(
        IReviewStore reviews,
        int userId,
        HashSet<int>? scope,
        CancellationToken cancellationToken)
    {
        var candidates = (await reviews.ListSlotsOfUserAsync(userId, cancellationToken))
            .Where(slot => scope is null || scope.Contains(slot.ProjectId))
            .ToList();

        // One batch read of the snapshots per project, because "signed" depends on the record digest
        var work = new List<UserOpenWork>();
        foreach (var project in candidates.GroupBy(slot => slot.ProjectId))
        {
            var reviewIds = project.Select(slot => slot.ReviewId).Distinct().ToList();
            var snapshots = await reviews.ListSnapshotsAsync(project.Key, reviewIds, cancellationToken);
            foreach (var candidate in project)
            {
                if (snapshots.TryGetValue(candidate.ReviewId, out var snapshot))
                {
                    AddOpenWork(work, candidate, snapshot);
                }
            }
        }

        return work
            .OrderBy(item => item.ProjectName, StringComparer.Ordinal)
            .ThenBy(item => item.ProjectId)
            .ThenBy(item => item.ReviewId, StringComparer.Ordinal)
            .ThenBy(item => item.SlotNumber)
            .ToList();
    }

    /// <summary>
    ///     Adds the open work of one slot, unless its review is approved or the slot holds a current signature.
    /// </summary>
    /// <param name="work">The open work collected so far.</param>
    /// <param name="candidate">The slot of the user.</param>
    /// <param name="snapshot">The snapshot of the review of the slot.</param>
    private static void AddOpenWork(List<UserOpenWork> work, UserReviewSlot candidate, ReviewSnapshot snapshot)
    {
        var (record, slots, signatures) = snapshot;
        if (ReviewLock.IsConcluded(record, slots, signatures))
        {
            return;
        }

        // Slots are numbered in creation order, like the approval status does
        var ordered = slots.OrderBy(slot => slot.Id).ToList();
        var index = ordered.FindIndex(slot => slot.Id == candidate.SlotId);
        if (index < 0)
        {
            return;
        }

        var state = InboxRules.SlotState(ordered[index], signatures, RecordDigest.Compute(record));
        if (state == SlotSignatureState.Signed)
        {
            return;
        }

        work.Add(new UserOpenWork(
            candidate.ProjectId,
            candidate.ProjectName,
            candidate.ReviewId,
            candidate.Title,
            index + 1,
            candidate.Role,
            state));
    }
}
