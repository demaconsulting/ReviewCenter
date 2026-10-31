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
///     One reviewer slot a user holds, with the review and project it belongs to, as the store lists it without
///     loading the content of the review.
/// </summary>
/// <remarks>
///     It is the candidate row of <see cref="UserImpactService"/>: whether the slot still needs the user is decided from
///     the snapshot of the review. An immutable value holder that is safe to share between threads.
/// </remarks>
/// <param name="ProjectId">The identity of the owning project.</param>
/// <param name="ProjectName">The name of the owning project.</param>
/// <param name="ReviewId">The review identifier.</param>
/// <param name="Title">The review title.</param>
/// <param name="SlotId">The identity of the slot.</param>
/// <param name="Role">Whether the user is the primary or the alternate reviewer of the slot.</param>
public sealed record UserReviewSlot(
    int ProjectId,
    string ProjectName,
    string ReviewId,
    string Title,
    int SlotId,
    SlotRole Role);

/// <summary>
///     One slot of an unlocked review that still waits for a user, as the offboarding warning lists it.
/// </summary>
/// <remarks>An immutable value holder that is safe to share between threads.</remarks>
/// <param name="ProjectId">The identity of the owning project.</param>
/// <param name="ProjectName">The name of the owning project.</param>
/// <param name="ReviewId">The review identifier.</param>
/// <param name="Title">The review title.</param>
/// <param name="SlotNumber">The position of the slot among the slots of the review, starting at 1 ("Slot 1").</param>
/// <param name="Role">Whether the user is the primary or the alternate reviewer of the slot.</param>
/// <param name="SlotState">
///     Why the slot still waits: <see cref="SlotSignatureState.NotSigned"/> or <see cref="SlotSignatureState.OutOfDate"/>;
///     never <see cref="SlotSignatureState.Signed"/>.
/// </param>
public sealed record UserOpenWork(
    int ProjectId,
    string ProjectName,
    string ReviewId,
    string Title,
    int SlotNumber,
    SlotRole Role,
    SlotSignatureState SlotState)
{
    /// <summary>
    ///     Gets a value indicating whether the user is the primary reviewer of the slot.
    /// </summary>
    public bool IsPrimary => Role == SlotRole.Primary;

    /// <summary>
    ///     Gets a value indicating whether the user is the alternate reviewer of the slot.
    /// </summary>
    public bool IsAlternate => Role == SlotRole.Alternate;
}

/// <summary>
///     A project that would be left without an enabled project administrator if a user were disabled, removed or
///     demoted.
/// </summary>
/// <remarks>An immutable value holder that is safe to share between threads.</remarks>
/// <param name="ProjectId">The identity of the project.</param>
/// <param name="ProjectName">The name of the project.</param>
public sealed record SoleAdminProject(int ProjectId, string ProjectName);

/// <summary>
///     What would be left behind when a user leaves: the review work that still waits for the user and the projects
///     in which the user is the only enabled project administrator.
/// </summary>
/// <remarks>
///     Computed on demand by <see cref="UserImpactService"/> and never stored. It is information for a warning, not a
///     block: a leaver may have to be disabled at once. An immutable value holder that is safe to share between
///     threads.
/// </remarks>
/// <param name="OpenWork">
///     The slots of unlocked reviews that are not currently signed and in which the user is primary or alternate,
///     ordered by project name, review identifier and slot number.
/// </param>
/// <param name="SoleAdminProjects">The projects of which the user is the only enabled project administrator, ordered by name.</param>
public sealed record UserImpact(
    IReadOnlyList<UserOpenWork> OpenWork,
    IReadOnlyList<SoleAdminProject> SoleAdminProjects)
{
    /// <summary>
    ///     Gets a value indicating whether nothing would be left behind.
    /// </summary>
    public bool IsEmpty => OpenWork.Count == 0 && SoleAdminProjects.Count == 0;
}
