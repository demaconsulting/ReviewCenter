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
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Tests.Core;

#pragma warning disable S4136 // The shortcuts are grouped by what they stand in for, not by name

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
    ///     Gets the identity of the project that the shortcut overloads of the repository calls use; a test that
    ///     uses them adds that project first (the first project of an empty store has this identity).
    /// </summary>
    public const int DefaultProjectId = 1;

    /// <summary>
    ///     Registers a repository in the default project.
    /// </summary>
    public static Task<SourceRepository> CreateAsync(
        this RepositoryService service,
        string name,
        string location,
        string? username,
        string? token,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        return service.CreateAsync(DefaultProjectId, name, location, username, token, actor, cancellationToken);
    }

    /// <summary>
    ///     Lists the repositories of the default project.
    /// </summary>
    public static Task<IReadOnlyList<SourceRepository>> ListAsync(
        this RepositoryService service,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        return service.ListAsync(actor, DefaultProjectId, cancellationToken);
    }

    /// <summary>
    ///     Gets the access to a repository of the default project.
    /// </summary>
    internal static Task<RepositoryAccess> GetAccessAsync(
        this RepositoryService service,
        int id,
        CancellationToken cancellationToken = default)
    {
        return service.GetAccessAsync(DefaultProjectId, id, cancellationToken);
    }

    /// <summary>
    ///     Lists the registrations of the default project in a repository store.
    /// </summary>
    public static Task<IReadOnlyList<SourceRepository>> ListAsync(
        this IRepositoryStore store,
        CancellationToken cancellationToken = default)
    {
        return store.ListAsync(DefaultProjectId, cancellationToken);
    }

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
        bool isSystemAdmin,
        string? password,
        CancellationToken cancellationToken = default)
    {
        return service.CreateAsync(username, displayName, isSystemAdmin, password, DefaultActor, cancellationToken);
    }

    /// <summary>
    ///     Creates a user as the default actor, using the test shorthand <see cref="UserRole"/>.
    /// </summary>
    public static Task<User> CreateAsync(
        this UserService service,
        string? username,
        string? displayName,
        UserRole role,
        string? password,
        CancellationToken cancellationToken = default)
    {
        return service.CreateAsync(username, displayName, role == UserRole.Admin, password, DefaultActor, cancellationToken);
    }

    /// <summary>
    ///     Creates a user as the given actor, using the test shorthand <see cref="UserRole"/>.
    /// </summary>
    public static Task<User> CreateAsync(
        this UserService service,
        string? username,
        string? displayName,
        UserRole role,
        string? password,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        return service.CreateAsync(username, displayName, role == UserRole.Admin, password, actor, cancellationToken);
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
    ///     Switches the independence of reviewers off for a project, as the default actor, for a test that has the
    ///     creator of a review also review it.
    /// </summary>
    public static Task SetIndependentReviewersOffAsync(
        this ProjectService service,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        return service.SetRequireIndependentReviewersAsync(DefaultActor, projectId, true, false, cancellationToken);
    }

    /// <summary>
    ///     Builds the evidence index as the default actor.
    /// </summary>
    public static Task<IReadOnlyList<EvidenceIndexEntry>> BuildAsync(
        this EvidenceIndexService service,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        return service.BuildAsync(DefaultActor, projectId, cancellationToken);
    }

    /// <summary>
    ///     Plans a review as the default actor.
    /// </summary>
    public static Task<ReviewPlan> PlanAsync(
        this ReviewPlanner planner,
        int projectId,
        int repositoryId,
        string? headRevision,
        BaselineMode mode,
        string? baselineRevision,
        CancellationToken cancellationToken = default)
    {
        return planner.PlanAsync(
            DefaultActor, projectId, repositoryId, headRevision, mode, baselineRevision, cancellationToken);
    }

    /// <summary>
    ///     Lists the projects as the default actor.
    /// </summary>
    public static Task<IReadOnlyList<Project>> ListAsync(
        this ProjectService service,
        CancellationToken cancellationToken = default)
    {
        return service.ListAsync(DefaultActor, cancellationToken);
    }

    /// <summary>
    ///     Reads a project as the default actor.
    /// </summary>
    public static Task<Project?> GetAsync(
        this ProjectService service,
        int id,
        CancellationToken cancellationToken = default)
    {
        return service.GetAsync(DefaultActor, id, cancellationToken);
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
    ///     Grants or removes system administrator rights as the default actor, using the test shorthand
    ///     <see cref="UserRole"/>.
    /// </summary>
    public static Task SetRoleAsync(
        this UserService service,
        string username,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        return service.SetSystemAdminAsync(username, role == UserRole.Admin, DefaultActor, cancellationToken);
    }

    /// <summary>
    ///     Grants or removes system administrator rights as the given actor, using the test shorthand
    ///     <see cref="UserRole"/>.
    /// </summary>
    public static Task SetRoleAsync(
        this UserService service,
        string username,
        UserRole role,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        return service.SetSystemAdminAsync(username, role == UserRole.Admin, actor, cancellationToken);
    }

    /// <summary>
    ///     Makes a user a member of a project with a generic audit draft.
    /// </summary>
    public static Task AddMembershipAsync(
        this IReviewStore store,
        int userId,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        return store.AddMembershipAsync(userId, projectId, ProjectRole.User, cancellationToken);
    }

    /// <summary>
    ///     Makes a user a member of a project with a role and a generic audit draft.
    /// </summary>
    public static Task AddMembershipAsync(
        this IReviewStore store,
        int userId,
        int projectId,
        ProjectRole role,
        CancellationToken cancellationToken = default)
    {
        return store.AddMembershipAsync(new ProjectMembership(userId, projectId, role), Draft(), cancellationToken);
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
    ///     Creates a draft review as <see cref="TestActors.Author"/>, who is no project member and so can never be
    ///     a reviewer of it.
    /// </summary>
    /// <remarks>
    ///     A test that goes through the real project access (a signed-in user, a real database) cannot use this,
    ///     because the author must see the project: it creates the review as its user and switches the independence
    ///     of reviewers off with <see cref="SetIndependentReviewersOffAsync"/>.
    /// </remarks>
    public static Task<Review> CreateDraftAsync(
        this ReviewService service,
        int projectId,
        string? id,
        string? title,
        IReadOnlyList<UploadedFile> files,
        CancellationToken cancellationToken = default)
    {
        return service.CreateDraftAsync(projectId, id, title, files, TestActors.Author, cancellationToken);
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

    /// <summary>
    ///     Approves (and so locks) a review: answers every check, seats a new user as the only reviewer and lets
    ///     that user sign.
    /// </summary>
    /// <remarks>
    ///     The reviewer is a new user (not the author of the review) with its own user store, so the helper works
    ///     for any fake review store whose project allows the reviewer. The review must have no open thread.
    /// </remarks>
    /// <param name="store">The review store that holds the review.</param>
    /// <param name="clock">The clock of the services.</param>
    /// <param name="projectId">The project of the review.</param>
    /// <param name="reviewId">The review to approve.</param>
    /// <param name="reviewerName">The user name (and display name) of the new reviewer.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The digest the approval is for.</returns>
    public static async Task<string> ApproveReviewAsync(
        FakeReviewStore store,
        TimeProvider clock,
        int projectId,
        string reviewId,
        string reviewerName = "approver",
        CancellationToken cancellationToken = default)
    {
        const string password = "correct horse battery";
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), clock);
        var reviewer = await users.CreateAsync(reviewerName, reviewerName, UserRole.Reviewer, password, cancellationToken);
        await store.AddMembershipAsync(reviewer.Id, projectId, cancellationToken);
        var checklists = TestServices.Checklists(store, clock);
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await checklists.SetAnswerAsync(
                projectId, reviewId, item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, cancellationToken);
        }

        var signing = TestServices.Signing(store, users, clock);
        var actor = new Actor(reviewerName, reviewerName);
        var slot = await signing.AddSlotAsync(actor, projectId, reviewId, reviewer.Id, null, cancellationToken);
        var digest = (await signing.GetApprovalAsync(projectId, reviewId, cancellationToken))!.Digest;
        await signing.SignAsync(actor, projectId, reviewId, slot.Id, digest, password, cancellationToken);
        return digest;
    }
}
