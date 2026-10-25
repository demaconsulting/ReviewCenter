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
///     The persistence boundary of the domain: everything the services need to store and read
///     projects, reviews, review files and review threads.
/// </summary>
/// <remarks>
///     The interface is owned by Core and implemented by the Storage subsystem, so the domain rules
///     can be tested with a fake store and Core never references a database library. Every method
///     is asynchronous and honors its cancellation token. Implementations must be safe for
///     concurrent use because the services are shared by all browser sessions.
///     Every method that changes data takes the <see cref="AuditDraft"/> of that change (or a function that builds
///     it from the stored entity, when the draft needs an identity the database assigns) and must save the
///     audit entry in the same transaction as the change: both are stored or neither. A change that did not
///     take effect (a method that returns <c>null</c> or <c>false</c>) records nothing.
/// </remarks>
public interface IReviewStore
{
    /// <summary>
    ///     Stores a new project and assigns its identity.
    /// </summary>
    /// <param name="project">The project to store. Must not be null; its <see cref="Project.Id"/> is ignored.</param>
    /// <param name="audit">Builds the audit draft from the stored project (so the draft can name the assigned identity). Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored project, including the assigned identity.</returns>
    Task<Project> AddProjectAsync(
        Project project,
        Func<Project, AuditDraft> audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads one project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The project, or <c>null</c> when it does not exist.</returns>
    Task<Project?> GetProjectAsync(int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists all projects.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>All projects, ordered by name and then identity.</returns>
    Task<IReadOnlyList<Project>> ListProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reports whether a review with the given id exists in a project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the review exists.</returns>
    Task<bool> ReviewExistsAsync(int projectId, string reviewId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stores a new review together with its files and, when it has one, its frozen checklist.
    /// </summary>
    /// <param name="review">The review to store, with its files and their bytes. Must not be null.</param>
    /// <param name="audit">The audit draft of the change.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the review is stored.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ReviewIdDuplicate"/> when the project already has a
    ///     review with that id, including when it was created concurrently.
    /// </exception>
    Task AddReviewAsync(Review review, AuditDraft audit, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads one review with its file metadata (without file bytes).
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The review with its files ordered by path, or <c>null</c> when it does not exist.</returns>
    Task<Review?> GetReviewAsync(int projectId, string reviewId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the reviews of a project (without files).
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The reviews ordered by id; empty when the project has none or does not exist.</returns>
    Task<IReadOnlyList<Review>> ListReviewsAsync(int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads the exact bytes of one file of a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="path">The file path within the review.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The bytes, or <c>null</c> when the file does not exist.</returns>
    Task<byte[]?> GetFileContentAsync(
        int projectId,
        string reviewId,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stores a new thread together with its first comment and assigns their identities.
    /// </summary>
    /// <param name="thread">
    ///     The thread to store, with its comments. Must not be null; the identities of the thread and its
    ///     comments are ignored. The file it names must exist in the review.
    /// </param>
    /// <param name="audit">Builds the audit draft from the stored thread (so the draft can name the assigned identities). Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored thread, including the assigned identities.</returns>
    Task<ReviewThread> AddThreadAsync(
        ReviewThread thread,
        Func<ReviewThread, AuditDraft> audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Appends a comment to an existing thread.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The thread identity; the thread must belong to the given review.</param>
    /// <param name="comment">The comment to store. Must not be null; its identities are ignored.</param>
    /// <param name="audit">Builds the audit draft from the stored comment (so the draft can name the assigned identity). Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored comment, or <c>null</c> when the thread does not exist in that review (nothing is recorded then).</returns>
    Task<ReviewComment?> AddCommentAsync(
        int projectId,
        string reviewId,
        int threadId,
        ReviewComment comment,
        Func<ReviewComment, AuditDraft> audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Changes the state of a thread. It is the only change ever made to a stored thread.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The thread identity; the thread must belong to the given review.</param>
    /// <param name="state">The state to store.</param>
    /// <param name="audit">The audit draft of the change; stored only when the thread exists.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the thread exists in that review (also when it already had the state); otherwise <c>false</c>.</returns>
    Task<bool> SetThreadStateAsync(
        int projectId,
        string reviewId,
        int threadId,
        ReviewThreadState state,
        AuditDraft audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the threads of a review, for every file, with their comments.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The threads ordered by identity, each with its comments ordered by identity; empty when there are none.</returns>
    Task<IReadOnlyList<ReviewThread>> ListThreadsAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads the frozen checklist of a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The checklist, or <c>null</c> when the review does not exist or was stored without one.</returns>
    Task<ReviewChecklist?> GetChecklistAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the stored applicability of the checklist sections of a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One entry per section that has been set; empty when none has.</returns>
    Task<IReadOnlyList<ChecklistSectionState>> ListChecklistSectionStatesAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the stored checklist answers of a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One entry per answered check; empty when none has been answered.</returns>
    Task<IReadOnlyList<ChecklistAnswer>> ListChecklistAnswersAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stores the applicability of a checklist section, replacing an earlier one.
    /// </summary>
    /// <param name="state">The state to store. Must not be null; the review must exist.</param>
    /// <param name="audit">The audit draft of the change.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the state is stored.</returns>
    Task SetChecklistSectionStateAsync(
        ChecklistSectionState state,
        AuditDraft audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stores the answer to a checklist check, replacing an earlier answer.
    /// </summary>
    /// <param name="answer">The answer to store. Must not be null; the review must exist.</param>
    /// <param name="audit">The audit draft of the change.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the answer is stored.</returns>
    Task SetChecklistAnswerAsync(
        ChecklistAnswer answer,
        AuditDraft audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads a consistent snapshot of everything a signature covers (see <see cref="ReviewRecord"/>).
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The record, or <c>null</c> when the review does not exist.</returns>
    Task<ReviewRecord?> GetRecordAsync(int projectId, string reviewId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads the record, the reviewer slots and the signatures of a review as one consistent snapshot, so a
    ///     change made by another request cannot leave the three describing different states.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The snapshot, or <c>null</c> when the review does not exist.</returns>
    Task<ReviewSnapshot?> GetSnapshotAsync(int projectId, string reviewId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the reviewer slots of a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The slots ordered by identity (creation order); empty when there are none.</returns>
    Task<IReadOnlyList<ReviewerSlot>> ListSlotsAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stores a new reviewer slot and assigns its identity.
    /// </summary>
    /// <param name="slot">The slot to store. Must not be null; its <see cref="ReviewerSlot.Id"/> is ignored and the review must exist.</param>
    /// <param name="audit">Builds the audit draft from the stored slot (so the draft can name the assigned identity). Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored slot, including the assigned identity.</returns>
    Task<ReviewerSlot> AddSlotAsync(
        ReviewerSlot slot,
        Func<ReviewerSlot, AuditDraft> audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Changes the primary and alternate reviewer of a slot.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot identity; the slot must belong to the given review.</param>
    /// <param name="primaryUserId">The new primary reviewer.</param>
    /// <param name="alternateUserId">The new alternate reviewer, or <c>null</c> for none.</param>
    /// <param name="audit">The audit draft of the change; stored only when the slot exists.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the slot exists in that review; otherwise <c>false</c>.</returns>
    Task<bool> UpdateSlotAsync(
        int projectId,
        string reviewId,
        int slotId,
        int primaryUserId,
        int? alternateUserId,
        AuditDraft audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Removes a reviewer slot.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot identity; the slot must belong to the given review.</param>
    /// <param name="audit">The audit draft of the change; stored only when the slot existed.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the slot existed and was removed; otherwise <c>false</c>.</returns>
    /// <remarks>
    ///     A slot that has a signature cannot be removed; the store refuses with a database error. The service
    ///     checks first and gives the operator a clear message.
    /// </remarks>
    Task<bool> RemoveSlotAsync(
        int projectId,
        string reviewId,
        int slotId,
        AuditDraft audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the signatures of a review, including out-of-date ones.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The signatures ordered by identity (signing order); empty when there are none.</returns>
    Task<IReadOnlyList<ReviewSignature>> ListSignaturesAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Appends a signature, provided the review record still has the digest the signer signed.
    /// </summary>
    /// <param name="signature">The signature to store. Must not be null; its <see cref="ReviewSignature.Id"/> is ignored.</param>
    /// <param name="audit">The audit draft of the change.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored signature, including the assigned identity.</returns>
    /// <remarks>
    ///     This is the only way a signature is stored, and there is deliberately no method to change or delete
    ///     one (signatures are append-only). The store recomputes the digest of the record inside the same
    ///     transaction as the insert, so a comment or answer that arrives while signing cannot slip in
    ///     between the check and the insert.
    /// </remarks>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.RecordChanged"/> when the digest of the record now differs from
    ///     <see cref="ReviewSignature.RecordDigest"/>, with <see cref="DomainRule.ReviewNotFound"/> when the
    ///     review does not exist, with <see cref="DomainRule.SlotNotFound"/> when the slot is not part of the review,
    ///     and with <see cref="DomainRule.SigningNotAllowed"/> when the slot already has a signature of this digest.
    ///     Nothing is stored then.
    /// </exception>
    Task<ReviewSignature> AddSignatureAsync(
        ReviewSignature signature,
        AuditDraft audit,
        CancellationToken cancellationToken = default);
}
