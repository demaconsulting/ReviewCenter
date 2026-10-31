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
///     The methods that change reviewed content (<see cref="AddThreadAsync"/>, <see cref="AddCommentAsync"/>,
///     <see cref="SetThreadStateAsync"/>, <see cref="SetChecklistSectionStateAsync"/>, <see cref="SetChecklistAnswerAsync"/>,
///     <see cref="AddSlotAsync"/>, <see cref="UpdateSlotAsync"/> and <see cref="RemoveSlotAsync"/>) must also refuse with
///     <see cref="DomainRule.ReviewLocked"/> when the review is approved (see <see cref="ReviewLock"/>), checked inside
///     the same transaction as the change, so a signature that completes the approval at the same moment cannot be
///     overtaken by a change; nothing is stored then.
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
    ///     Changes the evidence settings of a project, if they still hold the value the caller based its change on.
    /// </summary>
    /// <remarks>
    ///     The comparison and the update are one atomic step (compare-and-set), so two administrators who save at
    ///     the same time cannot silently overwrite each other: the later one gets <c>false</c> and must look again.
    /// </remarks>
    /// <param name="projectId">The project identity.</param>
    /// <param name="expected">The settings the caller read and based its change on. Must not be null.</param>
    /// <param name="updated">The new settings. Must not be null.</param>
    /// <param name="audit">The audit draft of the change, saved in the same transaction. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the settings were changed; <c>false</c> (and nothing recorded) when the project does not
    ///     exist or its settings are no longer <paramref name="expected"/>.
    /// </returns>
    Task<bool> UpdateEvidenceSettingsAsync(
        int projectId,
        EvidenceSettings expected,
        EvidenceSettings updated,
        AuditDraft audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Changes the "require independent reviewers" setting of a project, if it still holds the value the caller
    ///     based its change on.
    /// </summary>
    /// <remarks>
    ///     The comparison and the update are one atomic step (compare-and-set), like
    ///     <see cref="UpdateEvidenceSettingsAsync"/>, so two administrators who save at the same time cannot silently
    ///     overwrite each other.
    /// </remarks>
    /// <param name="projectId">The project identity.</param>
    /// <param name="expected">The value the caller read and based its change on.</param>
    /// <param name="updated">The new value.</param>
    /// <param name="audit">The audit draft of the change, saved in the same transaction. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the setting was changed; <c>false</c> (and nothing recorded) when the project does not
    ///     exist or the setting is no longer <paramref name="expected"/>.
    /// </returns>
    Task<bool> UpdateRequireIndependentReviewersAsync(
        int projectId,
        bool expected,
        bool updated,
        AuditDraft audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Changes one review rule of a project, if it still holds the value the caller based its change on.
    /// </summary>
    /// <remarks>
    ///     The comparison and the update are one atomic step (compare-and-set) inside the transaction that also
    ///     stores the audit entry, like <see cref="UpdateRequireIndependentReviewersAsync"/>. Changing one rule
    ///     never changes another.
    /// </remarks>
    /// <param name="projectId">The project identity.</param>
    /// <param name="rule">The rule to change.</param>
    /// <param name="expected">The value the caller read and based its change on.</param>
    /// <param name="updated">The new value.</param>
    /// <param name="audit">The audit draft of the change, saved in the same transaction. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the rule was changed; <c>false</c> (and nothing recorded) when the project does not
    ///     exist or the rule is no longer <paramref name="expected"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="rule"/> is not defined.</exception>
    Task<bool> UpdateReviewRuleAsync(
        int projectId,
        ReviewRule rule,
        bool expected,
        bool updated,
        AuditDraft audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Changes the checklist definition of a project, if it still is the one whose hash the caller based its
    ///     change on.
    /// </summary>
    /// <remarks>
    ///     The comparison and the update are one atomic step (compare-and-set) inside the transaction that also
    ///     stores the audit entry, like <see cref="UpdateEvidenceSettingsAsync"/>, so two administrators who save
    ///     at the same time cannot silently overwrite each other and the audit chain never misses a change. The
    ///     store keeps the text as given; whoever calls it has validated the definition (see
    ///     <see cref="ChecklistValidator"/>). The version label is part of the JSON, so it is not passed separately.
    /// </remarks>
    /// <param name="projectId">The project identity.</param>
    /// <param name="expectedHash">
    ///     The hash the caller read for the stored definition (see <see cref="ProjectChecklistService.HashOf"/>; the
    ///     hash of the built-in default when the project has no definition of its own). Must not be null.
    /// </param>
    /// <param name="definitionJson">The new definition as JSON; <c>null</c> removes the project's own definition, so
    ///     the built-in default applies again.</param>
    /// <param name="audit">The audit draft of the change, saved in the same transaction. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the definition was changed; <c>false</c> (and nothing recorded) when the project does not
    ///     exist or its definition no longer has the <paramref name="expectedHash"/>.
    /// </returns>
    Task<bool> UpdateChecklistDefinitionAsync(
        int projectId,
        string expectedHash,
        string? definitionJson,
        AuditDraft audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stores a new project membership.
    /// </summary>
    /// <param name="membership">The membership to store. Must not be null.</param>
    /// <param name="audit">The audit draft of the change, saved in the same transaction. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the membership is stored.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.MembershipExists"/> when the user already is a member of the project.
    /// </exception>
    Task AddMembershipAsync(ProjectMembership membership, AuditDraft audit, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Removes a project membership.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="audit">The audit draft of the change, saved in the same transaction. Must not be null.</param>
    /// <param name="requireAnotherAdmin">
    ///     When <c>true</c> and the membership is that of an enabled project administrator, the removal is refused
    ///     unless another enabled project administrator remains; the check and the removal are one atomic step.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the membership was removed; <c>false</c> (and nothing recorded) when there was none.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.LastProjectAdmin"/> when <paramref name="requireAnotherAdmin"/> is set
    ///     and the removal would leave the project without an enabled project administrator; nothing is stored then.
    /// </exception>
    Task<bool> RemoveMembershipAsync(
        int projectId,
        int userId,
        AuditDraft audit,
        bool requireAnotherAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Changes the role of an existing project membership.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="role">The new role.</param>
    /// <param name="audit">The audit draft of the change, saved in the same transaction. Must not be null.</param>
    /// <param name="requireAnotherAdmin">
    ///     When <c>true</c> and the change demotes an enabled project administrator, the change is refused unless
    ///     another enabled project administrator remains; the check and the change are one atomic step.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the role was changed; <c>false</c> (and nothing recorded) when there is no membership.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.LastProjectAdmin"/> when <paramref name="requireAnotherAdmin"/> is set
    ///     and the change would leave the project without an enabled project administrator; nothing is stored then.
    /// </exception>
    Task<bool> SetMembershipRoleAsync(
        int projectId,
        int userId,
        ProjectRole role,
        AuditDraft audit,
        bool requireAnotherAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads the membership of a user in a project.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The membership, or <c>null</c> when the user is not a member.</returns>
    Task<ProjectMembership?> GetMembershipAsync(int userId, int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the memberships of a project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The memberships of the project, ordered by user identity.</returns>
    Task<IReadOnlyList<ProjectMembership>> ListMembershipsAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the memberships of a user.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The memberships of the user, ordered by project identity.</returns>
    Task<IReadOnlyList<ProjectMembership>> ListMembershipsForUserAsync(
        int userId,
        CancellationToken cancellationToken = default);

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
    ///     Reads the exact baseline bytes of one file of a repository review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="path">The file path within the review.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The frozen baseline bytes, or <c>null</c> when the file does not exist or has no baseline content (an added or uploaded file).</returns>
    Task<byte[]?> GetBaselineContentAsync(
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

    /// <summary>
    ///     Reopens an approved review: stores the reopen and its audit entry in one transaction.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="reopen">The reopen to store. Must not be null; its <see cref="ReviewReopen.Id"/> is ignored.</param>
    /// <param name="audit">The audit draft of the change, saved in the same transaction. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the reopen is stored.</returns>
    /// <remarks>
    ///     The store re-checks inside its transaction that the review is still approved, so two administrators who
    ///     reopen at the same moment cannot both succeed. Signatures are not touched: the reopen raises the reopen
    ///     count of the record, which changes the digest and so retires every earlier signature while keeping it as history.
    /// </remarks>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ReviewNotFound"/> when the review does not exist and with
    ///     <see cref="DomainRule.ReviewNotLocked"/> when it is not approved. Nothing is stored then.
    /// </exception>
    Task ReopenReviewAsync(
        int projectId,
        string reviewId,
        ReviewReopen reopen,
        AuditDraft audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the reopens of a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The reopens ordered by identity (oldest first); empty when there are none or the review does not exist.</returns>
    Task<IReadOnlyList<ReviewReopen>> ListReopensAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default);
}
