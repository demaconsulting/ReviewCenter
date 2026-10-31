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
///     How the baseline of each planned review is chosen.
/// </summary>
public enum BaselineMode
{
    /// <summary>No baseline: every selected file at the head is reviewed.</summary>
    None,

    /// <summary>One given revision is the baseline of every review.</summary>
    Explicit,

    /// <summary>The head of the last signed review of the same review-set is the baseline of that set's review.</summary>
    LastReviewed
}

/// <summary>
///     What a plan says about one review-set.
/// </summary>
public enum PlanRowStatus
{
    /// <summary>The set has changed files and no review of exactly this range exists: a review can be created.</summary>
    WillCreate,

    /// <summary>A review of exactly this set, head and baseline already exists.</summary>
    AlreadyFiled,

    /// <summary>No file of the set changed (or none matches its paths), so there is nothing to review.</summary>
    NothingToReview,

    /// <summary>The set cannot be planned, for example because its paths are invalid or it selects too many files.</summary>
    CannotPlan
}

/// <summary>
///     The plan for one review-set.
/// </summary>
/// <remarks>The record is immutable and thread-safe.</remarks>
/// <param name="SetId">The identifier of the review-set.</param>
/// <param name="Title">The title of the review-set.</param>
/// <param name="Selection">The normalized selection text of the set (patterns one per line, exclusions with <c>!</c>).</param>
/// <param name="Status">What the plan says about the set.</param>
/// <param name="BaselineSha">The baseline commit the row uses, or <c>null</c> for a review of all selected files at the head.</param>
/// <param name="BaselineNote">One plain sentence saying where the baseline comes from; <c>null</c> when there is nothing to say.</param>
/// <param name="Added">The number of selected files added since the baseline.</param>
/// <param name="Modified">The number of selected files modified since the baseline.</param>
/// <param name="Deleted">The number of selected files deleted since the baseline.</param>
/// <param name="ExistingReviewId">The identifier of the review that already exists, for <see cref="PlanRowStatus.AlreadyFiled"/>.</param>
/// <param name="Problem">The plain-text reason, for <see cref="PlanRowStatus.CannotPlan"/>.</param>
public sealed record PlanRow(
    string SetId,
    string Title,
    string Selection,
    PlanRowStatus Status,
    string? BaselineSha,
    string? BaselineNote,
    int Added,
    int Modified,
    int Deleted,
    string? ExistingReviewId,
    string? Problem)
{
    /// <summary>
    ///     Gets the number of selected files that changed.
    /// </summary>
    public int ChangedFiles => Added + Modified + Deleted;
}

/// <summary>
///     The plan for a head revision of a repository: one row per review-set of its <c>.reviewmark.yaml</c>.
/// </summary>
/// <remarks>The record is immutable and thread-safe.</remarks>
/// <param name="RepositoryId">The identity of the repository.</param>
/// <param name="RepositoryName">The name of the repository.</param>
/// <param name="HeadRevision">The head revision as the user entered it.</param>
/// <param name="HeadSha">The full head commit the plan was made for.</param>
/// <param name="Mode">How baselines were chosen.</param>
/// <param name="ExplicitBaselineRevision">The baseline revision as entered, for <see cref="BaselineMode.Explicit"/>.</param>
/// <param name="ExplicitBaselineSha">The full baseline commit, for <see cref="BaselineMode.Explicit"/>.</param>
/// <param name="Rows">One row per review-set, in the order of the file.</param>
public sealed record ReviewPlan(
    int RepositoryId,
    string RepositoryName,
    string HeadRevision,
    string HeadSha,
    BaselineMode Mode,
    string? ExplicitBaselineRevision,
    string? ExplicitBaselineSha,
    IReadOnlyList<PlanRow> Rows)
{
    /// <summary>
    ///     Gets the number of rows from which a review can be created.
    /// </summary>
    public int WillCreateCount => Count(PlanRowStatus.WillCreate);

    /// <summary>
    ///     Gets the number of rows that are already filed.
    /// </summary>
    public int AlreadyFiledCount => Count(PlanRowStatus.AlreadyFiled);

    /// <summary>
    ///     Gets the number of rows with nothing to review.
    /// </summary>
    public int NothingCount => Count(PlanRowStatus.NothingToReview);

    /// <summary>
    ///     Gets the number of rows that cannot be planned.
    /// </summary>
    public int CannotPlanCount => Count(PlanRowStatus.CannotPlan);

    /// <summary>
    ///     Counts the rows with a status.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The number of rows with that status.</returns>
    private int Count(PlanRowStatus status) => Rows.Count(row => row.Status == status);
}

/// <summary>
///     One review to create from a plan row.
/// </summary>
/// <remarks>The values come from a <see cref="PlanRow"/>; the planner re-validates all of them.</remarks>
/// <param name="SetId">The identifier of the review-set.</param>
/// <param name="Title">The title of the review-set.</param>
/// <param name="Selection">The normalized selection text of the row.</param>
/// <param name="BaselineSha">The full baseline commit of the row, or <c>null</c> for none.</param>
/// <param name="DueDate">
///     The optional due date of the review that is created from the row (a UTC calendar day), or <c>null</c> for none.
///     The planner checks it with the rule of <see cref="ReviewService"/>; a bad date fails that row only.
/// </param>
public sealed record PlanCreateRequest(
    string SetId,
    string Title,
    string Selection,
    string? BaselineSha,
    DateOnly? DueDate = null);

/// <summary>
///     What happened to one requested review.
/// </summary>
public enum PlanCreateOutcome
{
    /// <summary>The review was created.</summary>
    Created,

    /// <summary>The same review exists already (for example created at the same moment by someone else).</summary>
    AlreadyFiled,

    /// <summary>The review could not be created; nothing was stored for it.</summary>
    Failed
}

/// <summary>
///     The result for one requested review.
/// </summary>
/// <remarks>The record is immutable and thread-safe.</remarks>
/// <param name="SetId">The identifier of the review-set.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="ReviewId">The identifier of the created or existing review; <c>null</c> when it failed.</param>
/// <param name="Message">A plain sentence for the user: the reason when it failed.</param>
public sealed record PlanCreateResult(string SetId, PlanCreateOutcome Outcome, string? ReviewId, string Message);
