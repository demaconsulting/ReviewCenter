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

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     Overloads of the audited service and store methods that supply a fixed test actor and a generic audit
///     draft, for tests whose subject is not the audit trail itself.
/// </summary>
/// <remarks>
///     Instance methods that need an actor or an audit draft are not applicable to calls that omit it, so these
///     extension methods are chosen for those calls. Tests of the audit trail itself pass their own actor and
///     inspect the drafts explicitly.
/// </remarks>
internal static class AuditTestExtensions
{
    /// <summary>
    ///     Gets the actor that the shortcut overloads use.
    /// </summary>
    public static Actor DefaultActor => TestActors.Ann;

    /// <summary>
    ///     Builds a generic audit draft.
    /// </summary>
    /// <returns>The draft.</returns>
    public static AuditDraft Draft()
    {
        return AuditDraft.By(DefaultActor, "test.change", "test change");
    }

    /// <summary>
    ///     Creates a user as the default actor.
    /// </summary>
    public static Task<User> CreateAsync(
        this UserService service,
        string? username,
        string? displayName,
        UserRole role,
        string? password,
        CancellationToken cancellationToken = default)
    {
        return service.CreateAsync(username, displayName, role, password, DefaultActor, cancellationToken);
    }

    /// <summary>
    ///     Creates a project as the default actor.
    /// </summary>
    public static Task<Project> CreateAsync(
        this ProjectService service,
        string? name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        return service.CreateAsync(name, description, DefaultActor, cancellationToken);
    }

    /// <summary>
    ///     Enables or disables a user as the default actor.
    /// </summary>
    public static Task SetEnabledAsync(
        this UserService service,
        string username,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        return service.SetEnabledAsync(username, enabled, DefaultActor, cancellationToken);
    }

    /// <summary>
    ///     Changes a user role as the default actor.
    /// </summary>
    public static Task SetRoleAsync(
        this UserService service,
        string username,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        return service.SetRoleAsync(username, role, DefaultActor, cancellationToken);
    }

    /// <summary>
    ///     Sets a password as the default actor.
    /// </summary>
    public static Task SetPasswordAsync(
        this UserService service,
        string username,
        string? newPassword,
        CancellationToken cancellationToken = default)
    {
        return service.SetPasswordAsync(username, newPassword, DefaultActor, cancellationToken);
    }

    /// <summary>
    ///     Creates a draft review as the default actor.
    /// </summary>
    public static Task<Review> CreateDraftAsync(
        this ReviewService service,
        int projectId,
        string? id,
        string? title,
        IReadOnlyList<UploadedFile> files,
        CancellationToken cancellationToken = default)
    {
        return service.CreateDraftAsync(projectId, id, title, files, DefaultActor, cancellationToken);
    }

    /// <summary>
    ///     Resolves a thread as the default actor.
    /// </summary>
    public static Task ResolveAsync(
        this ThreadService service,
        int projectId,
        string reviewId,
        int threadId,
        CancellationToken cancellationToken = default)
    {
        return service.ResolveAsync(projectId, reviewId, threadId, DefaultActor, cancellationToken);
    }

    /// <summary>
    ///     Reopens a thread as the default actor.
    /// </summary>
    public static Task ReopenAsync(
        this ThreadService service,
        int projectId,
        string reviewId,
        int threadId,
        CancellationToken cancellationToken = default)
    {
        return service.ReopenAsync(projectId, reviewId, threadId, DefaultActor, cancellationToken);
    }

    /// <summary>
    ///     Adds a project to a store with a generic audit draft.
    /// </summary>
    public static Task<Project> AddProjectAsync(
        this IReviewStore store,
        Project project,
        CancellationToken cancellationToken = default)
    {
        return store.AddProjectAsync(project, p => AuditDraft.By(DefaultActor, "project.created", "project", p.Id), cancellationToken);
    }

    /// <summary>
    ///     Adds a review to a store with a generic audit draft.
    /// </summary>
    public static Task AddReviewAsync(
        this IReviewStore store,
        Review review,
        CancellationToken cancellationToken = default)
    {
        return store.AddReviewAsync(review, Draft(), cancellationToken);
    }

    /// <summary>
    ///     Adds a thread to a store with a generic audit draft.
    /// </summary>
    public static Task<ReviewThread> AddThreadAsync(
        this IReviewStore store,
        ReviewThread thread,
        CancellationToken cancellationToken = default)
    {
        return store.AddThreadAsync(thread, _ => Draft(), cancellationToken);
    }

    /// <summary>
    ///     Adds a comment to a store with a generic audit draft.
    /// </summary>
    public static Task<ReviewComment?> AddCommentAsync(
        this IReviewStore store,
        int projectId,
        string reviewId,
        int threadId,
        ReviewComment comment,
        CancellationToken cancellationToken = default)
    {
        return store.AddCommentAsync(projectId, reviewId, threadId, comment, _ => Draft(), cancellationToken);
    }

    /// <summary>
    ///     Sets a thread state in a store with a generic audit draft.
    /// </summary>
    public static Task<bool> SetThreadStateAsync(
        this IReviewStore store,
        int projectId,
        string reviewId,
        int threadId,
        ReviewThreadState state,
        CancellationToken cancellationToken = default)
    {
        return store.SetThreadStateAsync(projectId, reviewId, threadId, state, Draft(), cancellationToken);
    }

    /// <summary>
    ///     Sets a checklist section state in a store with a generic audit draft.
    /// </summary>
    public static Task SetChecklistSectionStateAsync(
        this IReviewStore store,
        ChecklistSectionState state,
        CancellationToken cancellationToken = default)
    {
        return store.SetChecklistSectionStateAsync(state, Draft(), cancellationToken);
    }

    /// <summary>
    ///     Sets a checklist answer in a store with a generic audit draft.
    /// </summary>
    public static Task SetChecklistAnswerAsync(
        this IReviewStore store,
        ChecklistAnswer answer,
        CancellationToken cancellationToken = default)
    {
        return store.SetChecklistAnswerAsync(answer, Draft(), cancellationToken);
    }

    /// <summary>
    ///     Adds a slot to a store with a generic audit draft.
    /// </summary>
    public static Task<ReviewerSlot> AddSlotAsync(
        this IReviewStore store,
        ReviewerSlot slot,
        CancellationToken cancellationToken = default)
    {
        return store.AddSlotAsync(slot, _ => Draft(), cancellationToken);
    }

    /// <summary>
    ///     Updates a slot in a store with a generic audit draft.
    /// </summary>
    public static Task<bool> UpdateSlotAsync(
        this IReviewStore store,
        int projectId,
        string reviewId,
        int slotId,
        int primaryUserId,
        int? alternateUserId,
        CancellationToken cancellationToken = default)
    {
        return store.UpdateSlotAsync(projectId, reviewId, slotId, primaryUserId, alternateUserId, Draft(), cancellationToken);
    }

    /// <summary>
    ///     Removes a slot from a store with a generic audit draft.
    /// </summary>
    public static Task<bool> RemoveSlotAsync(
        this IReviewStore store,
        int projectId,
        string reviewId,
        int slotId,
        CancellationToken cancellationToken = default)
    {
        return store.RemoveSlotAsync(projectId, reviewId, slotId, Draft(), cancellationToken);
    }

    /// <summary>
    ///     Adds a signature to a store with a generic audit draft.
    /// </summary>
    public static Task<ReviewSignature> AddSignatureAsync(
        this IReviewStore store,
        ReviewSignature signature,
        CancellationToken cancellationToken = default)
    {
        return store.AddSignatureAsync(signature, Draft(), cancellationToken);
    }

    /// <summary>
    ///     Adds a user to a store with a generic audit draft.
    /// </summary>
    public static Task<User> AddAsync(
        this IUserStore store,
        User user,
        CancellationToken cancellationToken = default)
    {
        return store.AddAsync(user, u => AuditDraft.By(DefaultActor, "user.created", $"user '{u.Username}'"), cancellationToken);
    }

    /// <summary>
    ///     Updates a user in a store with a generic audit draft.
    /// </summary>
    public static Task UpdateAsync(
        this IUserStore store,
        User user,
        CancellationToken cancellationToken = default)
    {
        return store.UpdateAsync(user, Draft(), cancellationToken);
    }
}
