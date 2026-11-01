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
using DemaConsulting.ReviewCenter.Tests.Core;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Helpers for the evidence index tests: adding reviews that carry a review-set id (which only a plan sets), and
///     completing reviews with one or more reviewer slots, over any review store.
/// </summary>
internal static class EvidenceTestSupport
{
    /// <summary>
    ///     The user name of the signer in these helpers.
    /// </summary>
    public static readonly Actor Ann = new("ann", "Ann");

    /// <summary>
    ///     Adds a draft review with a review-set id, as a plan would, directly to a store.
    /// </summary>
    /// <param name="store">The review store.</param>
    /// <param name="clock">The clock that supplies the creation time.</param>
    /// <param name="projectId">The project number.</param>
    /// <param name="id">The review identifier.</param>
    /// <param name="reviewSetId">The review-set id, or <c>null</c> for none.</param>
    /// <param name="contents">The contents of the files of the review.</param>
    /// <returns>A task that completes when the review is stored.</returns>
    public static async Task AddReviewAsync(
        IReviewStore store,
        TimeProvider clock,
        int projectId,
        string id,
        string? reviewSetId,
        params byte[][] contents)
    {
        var files = contents
            .Select((content, index) => new ReviewFile
            {
                ProjectId = projectId,
                ReviewId = id,
                Path = $"src/file{index}.txt",
                Content = content,
                Size = content.Length,
                Sha256 = Fingerprint.ComputeFileHash(content)
            })
            .ToList();
        var review = new Review
        {
            ProjectId = projectId,
            Id = id,
            Title = $"Review {id}",
            State = ReviewState.Draft,
            Created = clock.GetUtcNow(),
            CreatedBy = TestActors.Author.Username,
            ContentFingerprint = Fingerprint.Compute(files.Select(file => file.Sha256)),
            ReviewSetId = reviewSetId,
            Files = files,
            Checklist = new ReviewChecklist
            {
                ProjectId = projectId,
                ReviewId = id,
                Version = DefaultChecklist.Version,
                Definition = DefaultChecklist.Current.ToJson()
            }
        };
        await store.AddReviewAsync(
            review,
            AuditDraft.By(Ann, "review.created", $"review '{review.Title}'", projectId, id),
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Answers every check of a review as passed and hands in the ballot; ann must hold a reviewer slot of the review
    ///     and the checklist service must know her through the same user store as the signing service.
    /// </summary>
    /// <param name="checklists">The checklist service.</param>
    /// <param name="projectId">The project number.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="answerer">The slot owner who answers; ann when <c>null</c>.</param>
    /// <returns>A task that completes when every check is answered.</returns>
    public static async Task AnswerAllPassAsync(
        ChecklistService checklists,
        int projectId,
        string reviewId,
        Actor? answerer = null)
    {
        var actor = answerer ?? Ann;
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await checklists.SetAnswerAsync(
                projectId, reviewId, item.Id, ChecklistOutcome.Pass, null, actor, TestContext.Current.CancellationToken);
        }

        await checklists.SubmitBallotAsync(projectId, reviewId, actor, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Adds a reviewer slot to a review.
    /// </summary>
    /// <param name="signing">The signing service.</param>
    /// <param name="projectId">The project number.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="primaryUserId">The primary reviewer.</param>
    /// <returns>The identity of the slot.</returns>
    public static async Task<int> AddSlotAsync(SigningService signing, int projectId, string reviewId, int primaryUserId)
    {
        var slot = await signing.AddSlotAsync(Ann, projectId, reviewId, primaryUserId, null, TestContext.Current.CancellationToken);
        return slot.Id;
    }

    /// <summary>
    ///     Signs a slot with the current record.
    /// </summary>
    /// <param name="signing">The signing service.</param>
    /// <param name="projectId">The project number.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot.</param>
    /// <param name="signer">The signer, who must be the reviewer of the slot.</param>
    /// <param name="password">The password of the signer.</param>
    /// <returns>A task that completes when the slot is signed.</returns>
    public static async Task SignAsync(
        SigningService signing,
        int projectId,
        string reviewId,
        int slotId,
        Actor signer,
        string password)
    {
        var approval = await signing.GetApprovalAsync(projectId, reviewId, TestContext.Current.CancellationToken);
        await signing.SignAsync(signer, projectId, reviewId, slotId, approval!.Digest, password, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Completes a review: every check passed, one slot for ann, signed.
    /// </summary>
    /// <param name="checklists">The checklist service.</param>
    /// <param name="signing">The signing service.</param>
    /// <param name="projectId">The project number.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="annUserId">The identity of the user ann.</param>
    /// <param name="password">The password of ann.</param>
    /// <returns>A task that completes when the review is complete.</returns>
    public static async Task CompleteAsync(
        ChecklistService checklists,
        SigningService signing,
        int projectId,
        string reviewId,
        int annUserId,
        string password)
    {
        var slotId = await AddSlotAsync(signing, projectId, reviewId, annUserId);
        await AnswerAllPassAsync(checklists, projectId, reviewId);
        await SignAsync(signing, projectId, reviewId, slotId, Ann, password);
    }
}
