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
///     Looks up the relations of a person to a review (lead, reviewer of a slot) that the report rules of
///     <see cref="ThreadService"/> depend on.
/// </summary>
/// <remarks>
///     The lead rule is the same as for changing the due date (<see cref="ReviewService.CanSetDueDateAsync"/>): the
///     creator of the review who can still contribute, or an administrator of the project. It is repeated here, not
///     shared, so that rule's service stays untouched; the two must change together. The class is stateless and
///     thread-safe as long as the injected collaborators are.
/// </remarks>
internal static class ReviewActorRoles
{
    /// <summary>
    ///     Tells whether a person leads a review.
    /// </summary>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <param name="actor">The signed-in user; <c>null</c> and an API token are never a lead.</param>
    /// <param name="review">The review. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the actor created the review (by user name, without regard to case) and can still
    /// contribute to the project, or administers the project.</returns>
    public static async Task<bool> IsLeadAsync(
        IProjectAccess access,
        Actor? actor,
        Review review,
        CancellationToken cancellationToken)
    {
        if (actor is null || actor.ViaApiToken)
        {
            return false;
        }

        // The creator is recorded by user name, which is unique without regard to case; the right needs a project in
        // which the actor can still work
        var isCreator = review.CreatedBy is not null
                        && string.Equals(review.CreatedBy, actor.Username, StringComparison.OrdinalIgnoreCase)
                        && await access.CanContributeAsync(actor, review.ProjectId, cancellationToken);
        return isCreator || await access.CanAdministerAsync(actor, review.ProjectId, cancellationToken);
    }

    /// <summary>
    ///     Tells whether a person is the primary or the alternate reviewer of any of the given slots.
    /// </summary>
    /// <param name="users">The user store used to resolve the user name to an identity. Must not be null.</param>
    /// <param name="actor">The signed-in user; <c>null</c> and an API token are never a slot owner.</param>
    /// <param name="slots">The reviewer slots of the review. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the person is known and holds a slot; <c>false</c> for an unknown user, for an API
    /// token and for the command line, which has no user record.</returns>
    public static async Task<bool> IsSlotOwnerAsync(
        IUserStore users,
        Actor? actor,
        IReadOnlyList<ReviewerSlot> slots,
        CancellationToken cancellationToken)
    {
        if (actor is null || actor.ViaApiToken || slots.Count == 0)
        {
            return false;
        }

        var user = await users.GetByUsernameAsync(actor.Username, cancellationToken);
        return user is not null && slots.Any(slot => slot.PrimaryUserId == user.Id || slot.AlternateUserId == user.Id);
    }

    /// <summary>
    ///     Finds the slot a user holds in a review from the identity of the user, without any lookup.
    /// </summary>
    /// <param name="userId">The identity of the user; <c>null</c> for nobody or an unknown user.</param>
    /// <param name="slots">The reviewer slots of the review. Must not be null.</param>
    /// <returns>
    ///     The slot with the lowest identity of which the user is the primary or the alternate reviewer; <c>null</c> when
    ///     the user holds no slot.
    /// </returns>
    public static ReviewerSlot? FindOwnSlot(int? userId, IReadOnlyList<ReviewerSlot> slots)
    {
        return userId is null
            ? null
            : slots.OrderBy(s => s.Id).FirstOrDefault(s => s.PrimaryUserId == userId || s.AlternateUserId == userId);
    }

    /// <summary>
    ///     Finds the slot a person holds in a review, to look up the status of their ballot.
    /// </summary>
    /// <param name="users">The user store used to resolve the user name to an identity. Must not be null.</param>
    /// <param name="actor">The signed-in user; may be null, which holds no slot.</param>
    /// <param name="slots">The reviewer slots of the review. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The slot (the one with the lowest identity when the person holds several) of which the person is the primary
    ///     or the alternate reviewer; <c>null</c> for an unknown user, the command line and a person without a slot.
    /// </returns>
    public static async Task<ReviewerSlot?> FindOwnSlotAsync(
        IUserStore users,
        Actor? actor,
        IReadOnlyList<ReviewerSlot> slots,
        CancellationToken cancellationToken)
    {
        if (actor is null || slots.Count == 0)
        {
            return null;
        }

        var user = await users.GetByUsernameAsync(actor.Username, cancellationToken);
        return user is null
            ? null
            : slots.OrderBy(s => s.Id).FirstOrDefault(s => s.PrimaryUserId == user.Id || s.AlternateUserId == user.Id);
    }
}
