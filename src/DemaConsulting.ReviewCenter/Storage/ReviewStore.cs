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

using System.Data;
using DemaConsulting.ReviewCenter.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DemaConsulting.ReviewCenter.Storage;

/// <summary>
///     The <see cref="IReviewStore"/> implementation backed by <see cref="ReviewCenterDbContext"/>.
/// </summary>
/// <remarks>
///     Each operation uses its own short-lived context from the factory, so the store is safe for
///     concurrent use by all browser sessions. Read operations are untracked, and listing a review
///     never loads file bytes. The checklist snapshot is stored in the same transaction as its review.
///     Answers and section states are replaced in place (one row per check or section). Slots are plain rows.
///     Signatures are only ever inserted, inside a transaction that rechecks the record digest. A uniqueness
///     violation on the review key is translated to a <see cref="DomainRuleException"/> so callers see the same
///     error as for the service pre-check.
///     Every change is made through <see cref="AuditAppender.ExecuteAsync{TResult}"/>: one write transaction, under the
///     process-wide audit gate, that stores the change and its audit entry together or not at all. The changes of
///     reviewed content and of the reviewer slots re-check inside that transaction that the review is not concluded
///     (locked) and refuse with <see cref="DomainRule.ReviewLocked"/> otherwise; so does the change of the due date,
///     which also compares the due date the caller read (compare-and-set).
/// </remarks>
/// <param name="factory">The factory that creates a context per operation.</param>
/// <param name="appender">The appender that runs each change together with its audit entry.</param>
internal sealed class ReviewStore(IDbContextFactory<ReviewCenterDbContext> factory, AuditAppender appender) : IReviewStore
{
    /// <summary>
    ///     The SQLite extended result code for a primary key constraint violation.
    /// </summary>
    private const int SqlitePrimaryKeyViolation = 1555;

    /// <summary>
    ///     The SQLite extended result code for a unique constraint violation.
    /// </summary>
    private const int SqliteUniqueViolation = 2067;

    /// <summary>
    ///     The number of review identifiers in one statement of a batch read; far below the host parameter limit of SQLite.
    /// </summary>
    private const int IdChunkSize = 500;

    /// <inheritdoc />
    public async Task<Project> AddProjectAsync(
        Project project,
        Func<Project, AuditDraft> audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<Project>(
            factory,
            async context =>
            {
                // Insert a fresh entity so that the database always assigns the identity, whatever the caller supplied
                var entity = new Project
                {
                    Name = project.Name,
                    Description = project.Description,
                    Created = project.Created,
                    Evidence = project.Evidence,
                    RequireIndependentReviewers = project.RequireIndependentReviewers,
                    RequireNonAuthorChecklistAnswers = project.RequireNonAuthorChecklistAnswers,
                    RequireNonReopenerSigning = project.RequireNonReopenerSigning,
                    ChecklistDefinition = project.ChecklistDefinition
                };
                context.Projects.Add(entity);
                await context.SaveChangesAsync(cancellationToken);
                return (entity, audit(entity));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Project?> GetProjectAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Project>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        // SQLite cannot order by a DateTimeOffset, so the listing is ordered by name and then identity
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.Projects.AsNoTracking()
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateEvidenceSettingsAsync(
        int projectId,
        EvidenceSettings expected,
        EvidenceSettings updated,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(updated);
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                // The comparison is part of the update statement, so it is atomic with the change
                var rows = await context.Projects
                    .Where(p => p.Id == projectId
                                && p.Evidence.AnonymousIndex == expected.AnonymousIndex
                                && p.Evidence.AnonymousAttestations == expected.AnonymousAttestations
                                && p.Evidence.IncludeThreads == expected.IncludeThreads
                                && p.Evidence.IncludeReviewerNames == expected.IncludeReviewerNames
                                && p.Evidence.IncludeChecklistWording == expected.IncludeChecklistWording
                                && p.Evidence.IncludeFiles == expected.IncludeFiles
                                && p.Evidence.IncludeGitContext == expected.IncludeGitContext)
                    .ExecuteUpdateAsync(
                        p => p
                            .SetProperty(x => x.Evidence.AnonymousIndex, updated.AnonymousIndex)
                            .SetProperty(x => x.Evidence.AnonymousAttestations, updated.AnonymousAttestations)
                            .SetProperty(x => x.Evidence.IncludeThreads, updated.IncludeThreads)
                            .SetProperty(x => x.Evidence.IncludeReviewerNames, updated.IncludeReviewerNames)
                            .SetProperty(x => x.Evidence.IncludeChecklistWording, updated.IncludeChecklistWording)
                            .SetProperty(x => x.Evidence.IncludeFiles, updated.IncludeFiles)
                            .SetProperty(x => x.Evidence.IncludeGitContext, updated.IncludeGitContext),
                        cancellationToken);
                return rows > 0 ? (true, audit) : (false, null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> UpdateRequireIndependentReviewersAsync(
        int projectId,
        bool expected,
        bool updated,
        AuditDraft audit,
        CancellationToken cancellationToken = default) =>
        UpdateReviewRuleAsync(projectId, ReviewRule.IndependentReviewers, expected, updated, audit, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> UpdateReviewRuleAsync(
        int projectId,
        ReviewRule rule,
        bool expected,
        bool updated,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (!Enum.IsDefined(rule))
        {
            throw new ArgumentOutOfRangeException(nameof(rule), rule, "The rule is not a known value.");
        }

        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                // The comparison is part of the update statement, so it is atomic with the change; EF cannot
                // parameterize the property, so each rule has its own statement
                var rows = rule switch
                {
                    ReviewRule.IndependentReviewers => await context.Projects
                        .Where(p => p.Id == projectId && p.RequireIndependentReviewers == expected)
                        .ExecuteUpdateAsync(p => p.SetProperty(x => x.RequireIndependentReviewers, updated), cancellationToken),
                    ReviewRule.NonAuthorChecklistAnswers => await context.Projects
                        .Where(p => p.Id == projectId && p.RequireNonAuthorChecklistAnswers == expected)
                        .ExecuteUpdateAsync(p => p.SetProperty(x => x.RequireNonAuthorChecklistAnswers, updated), cancellationToken),
                    ReviewRule.NonReopenerSigning => await context.Projects
                        .Where(p => p.Id == projectId && p.RequireNonReopenerSigning == expected)
                        .ExecuteUpdateAsync(p => p.SetProperty(x => x.RequireNonReopenerSigning, updated), cancellationToken),
                    _ => throw new System.Diagnostics.UnreachableException()
                };
                return rows > 0 ? (true, audit) : (false, null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateChecklistDefinitionAsync(
        int projectId,
        string expectedHash,
        string? definitionJson,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedHash);
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                // Read the stored text: a hash cannot be compared inside SQL. The write lock of the transaction is held
                // from its start, so nobody else can change the row between this read and the update
                var stored = await context.Projects
                    .Where(p => p.Id == projectId)
                    .Select(p => new { p.ChecklistDefinition })
                    .FirstOrDefaultAsync(cancellationToken);
                if (stored is null
                    || !string.Equals(ProjectChecklistService.HashOf(stored.ChecklistDefinition), expectedHash, StringComparison.Ordinal))
                {
                    return (false, null);
                }

                // The text that was compared is repeated in the update statement, so the change stays atomic even if
                // the lock above were ever not enough
                var current = stored.ChecklistDefinition;
                var rows = await context.Projects
                    .Where(p => p.Id == projectId && p.ChecklistDefinition == current)
                    .ExecuteUpdateAsync(p => p.SetProperty(x => x.ChecklistDefinition, definitionJson), cancellationToken);
                return rows > 0 ? (true, audit) : (false, null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddMembershipAsync(
        ProjectMembership membership,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(audit);

        await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                context.ProjectMemberships.Add(membership);
                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (IsDuplicateKey(ex))
                {
                    // The key is (user, project): the user is a member already
                    throw new DomainRuleException(
                        DomainRule.MembershipExists,
                        "The user is already a member of this project.");
                }

                return (true, audit);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveMembershipAsync(
        int projectId,
        int userId,
        AuditDraft audit,
        bool requireAnotherAdmin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                // The check runs in the same serialized transaction as the delete, so two concurrent removals of
                // two administrators cannot both pass it
                if (requireAnotherAdmin)
                {
                    await EnsureAnotherAdminAsync(context, projectId, userId, cancellationToken);
                }

                var rows = await context.ProjectMemberships
                    .Where(m => m.ProjectId == projectId && m.UserId == userId)
                    .ExecuteDeleteAsync(cancellationToken);
                return rows > 0 ? (true, audit) : (false, null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SetMembershipRoleAsync(
        int projectId,
        int userId,
        ProjectRole role,
        AuditDraft audit,
        bool requireAnotherAdmin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                // Only a demotion can remove the last administrator; the check shares the transaction of the update
                if (requireAnotherAdmin && role != ProjectRole.ProjectAdmin)
                {
                    await EnsureAnotherAdminAsync(context, projectId, userId, cancellationToken);
                }

                var rows = await context.ProjectMemberships
                    .Where(m => m.ProjectId == projectId && m.UserId == userId)
                    .ExecuteUpdateAsync(m => m.SetProperty(x => x.Role, role), cancellationToken);
                return rows > 0 ? (true, audit) : (false, null);
            },
            cancellationToken);
    }

    /// <summary>
    ///     Refuses a change that would take away the last enabled administrator of a project.
    /// </summary>
    /// <remarks>
    ///     Does nothing when the target is not an enabled project administrator (a plain member, a disabled
    ///     administrator or a non-member does not count as an administrator, so losing it changes nothing). Otherwise
    ///     at least one OTHER enabled project administrator must exist. Must run inside the audit transaction.
    /// </remarks>
    /// <param name="context">The context of the open transaction.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="userId">The user whose membership is about to be removed or demoted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the check passed.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.LastProjectAdmin"/> when no other enabled administrator exists.
    /// </exception>
    private static async Task EnsureAnotherAdminAsync(
        ReviewCenterDbContext context,
        int projectId,
        int userId,
        CancellationToken cancellationToken)
    {
        // Is the target an enabled project administrator right now?
        var targetIsAdmin = await context.ProjectMemberships.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.UserId == userId && m.Role == ProjectRole.ProjectAdmin)
            .Join(context.Users, m => m.UserId, u => u.Id, (m, u) => u.Enabled)
            .AnyAsync(enabled => enabled, cancellationToken);
        if (!targetIsAdmin)
        {
            return;
        }

        // Is there any other enabled project administrator?
        var otherAdmin = await context.ProjectMemberships.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.UserId != userId && m.Role == ProjectRole.ProjectAdmin)
            .Join(context.Users, m => m.UserId, u => u.Id, (m, u) => u.Enabled)
            .AnyAsync(enabled => enabled, cancellationToken);
        if (!otherAdmin)
        {
            throw new DomainRuleException(
                DomainRule.LastProjectAdmin,
                "A project needs at least one project administrator. This is the last one.");
        }
    }

    /// <inheritdoc />
    public async Task<ProjectMembership?> GetMembershipAsync(
        int userId,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.ProjectMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.ProjectId == projectId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectMembership>> ListMembershipsAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.ProjectMemberships.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.UserId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectMembership>> ListMembershipsForUserAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.ProjectMemberships.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.ProjectId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Project>> ListSoleAdminProjectsAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // The same rule as the last-administrator check: only an enabled administrator is one, and only other
        // enabled administrators count as another. It is one query, so the enabled state cannot change between reads.
        return await context.Projects.AsNoTracking()
            .Where(p => context.ProjectMemberships.Any(
                            m => m.ProjectId == p.Id
                                 && m.UserId == userId
                                 && m.Role == ProjectRole.ProjectAdmin
                                 && context.Users.Any(u => u.Id == m.UserId && u.Enabled))
                        && !context.ProjectMemberships.Any(
                            m => m.ProjectId == p.Id
                                 && m.UserId != userId
                                 && m.Role == ProjectRole.ProjectAdmin
                                 && context.Users.Any(u => u.Id == m.UserId && u.Enabled)))
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ReviewExistsAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.Reviews.AnyAsync(r => r.ProjectId == projectId && r.Id == reviewId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddReviewAsync(Review review, AuditDraft audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(audit);

        await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                context.Reviews.Add(review);

                // The rows carried over from the review this one follows are saved in the same call, so the review and
                // everything carried exist together or not at all. Threads are copied so the database assigns their
                // identities without touching the caller's objects
                if (review.CarryOver is { } carry)
                {
                    context.ChecklistAnswers.AddRange(carry.Answers);
                    context.ChecklistSectionStates.AddRange(carry.SectionStates);
                    context.Threads.AddRange(carry.Threads.Select(CopyCarriedThread));
                }

                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (IsDuplicateKey(ex))
                {
                    // Two requests passed the service pre-check at the same time; the key constraint stopped the second
                    throw new DomainRuleException(
                        DomainRule.ReviewIdDuplicate,
                        $"A review with the id '{review.Id}' already exists in this project.");
                }

                return (true, audit);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateDueDateAsync(
        int projectId,
        string reviewId,
        DateOnly? expected,
        DateOnly? dueDate,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                // A concluded review is locked; the check shares the transaction with the change
                await EnsureNotLockedAsync(context, projectId, reviewId, cancellationToken);

                // The comparison is part of the update statement, so it is atomic with the change (null needs IS NULL)
                var rows = await context.Reviews
                    .Where(r => r.ProjectId == projectId && r.Id == reviewId && r.DueDate == expected)
                    .ExecuteUpdateAsync(r => r.SetProperty(x => x.DueDate, dueDate), cancellationToken);
                return rows > 0 ? (true, audit) : (false, null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Review?> GetReviewAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // Read the review row, then the file metadata alone: the bytes of a large file set stay in the database
        var review = await context.Reviews.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == reviewId, cancellationToken);
        if (review is null)
        {
            return null;
        }

        var files = await context.ReviewFiles.AsNoTracking()
            .Where(f => f.ProjectId == projectId && f.ReviewId == reviewId)
            .OrderBy(f => f.Path)
            .Select(f => new ReviewFile
            {
                ProjectId = f.ProjectId,
                ReviewId = f.ReviewId,
                Path = f.Path,
                Size = f.Size,
                Sha256 = f.Sha256,
                ChangeKind = f.ChangeKind,
                BaselineSize = f.BaselineSize,
                BaselineSha256 = f.BaselineSha256
            })
            .ToListAsync(cancellationToken);
        review.Files.AddRange(files);
        return review;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Review>> ListReviewsAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.Reviews.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetFileContentAsync(
        int projectId,
        string reviewId,
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.ReviewFiles.AsNoTracking()
            .Where(f => f.ProjectId == projectId && f.ReviewId == reviewId && f.Path == path)
            .Select(f => f.Content)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetBaselineContentAsync(
        int projectId,
        string reviewId,
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.ReviewFiles.AsNoTracking()
            .Where(f => f.ProjectId == projectId && f.ReviewId == reviewId && f.Path == path)
            .Select(f => f.BaselineContent)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReviewThread> AddThreadAsync(
        ReviewThread thread,
        Func<ReviewThread, AuditDraft> audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<ReviewThread>(
            factory,
            async context =>
            {
                await EnsureNotLockedAsync(context, thread.ProjectId, thread.ReviewId, cancellationToken);

                // Insert fresh entities so that the database always assigns the identities, whatever the caller supplied
                var entity = new ReviewThread
                {
                    ProjectId = thread.ProjectId,
                    ReviewId = thread.ReviewId,
                    FilePath = thread.FilePath,
                    Side = thread.Side,
                    StartLine = thread.StartLine,
                    EndLine = thread.EndLine,
                    State = thread.State,
                    Created = thread.Created,
                    Comments = thread.Comments.Select(comment => NewComment(comment)).ToList()
                };
                context.Threads.Add(entity);
                await context.SaveChangesAsync(cancellationToken);
                return (entity, audit(entity));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReviewComment?> AddCommentAsync(
        int projectId,
        string reviewId,
        int threadId,
        ReviewComment comment,
        Func<ReviewComment, AuditDraft> audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comment);
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<ReviewComment?>(
            factory,
            async context =>
            {
                await EnsureNotLockedAsync(context, projectId, reviewId, cancellationToken);

                // The thread must belong to the stated review, so an identity from another review cannot be commented on
                var exists = await context.Threads.AnyAsync(
                    t => t.Id == threadId && t.ProjectId == projectId && t.ReviewId == reviewId,
                    cancellationToken);
                if (!exists)
                {
                    return (null, null);
                }

                var entity = NewComment(comment, threadId);
                context.Comments.Add(entity);
                await context.SaveChangesAsync(cancellationToken);
                return (entity, audit(entity));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ReopenThreadAsync(
        int projectId,
        string reviewId,
        int threadId,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                await EnsureNotLockedAsync(context, projectId, reviewId, cancellationToken);
                var rows = await context.Threads
                    .Where(t => t.Id == threadId && t.ProjectId == projectId && t.ReviewId == reviewId)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.State, ReviewThreadState.Open), cancellationToken);
                return rows > 0 ? (true, audit) : (false, null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ResolveThreadAsync(
        int projectId,
        string reviewId,
        int threadId,
        ThreadKind expectedKind,
        ReviewComment? resolution,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                await EnsureNotLockedAsync(context, projectId, reviewId, cancellationToken);
                var thread = await context.Threads.AsNoTracking()
                    .Include(t => t.Comments.OrderBy(c => c.Id))
                    .FirstOrDefaultAsync(
                        t => t.Id == threadId && t.ProjectId == projectId && t.ReviewId == reviewId,
                        cancellationToken);

                // A missing or already resolved thread changes nothing and leaves no audit entry
                if (thread is null || thread.State == ReviewThreadState.Resolved)
                {
                    return (false, null);
                }

                // The kind is read again inside the transaction. The caller decided the note, the acknowledgement of
                // the signatures and the report flag for the expected kind, so another kind is refused, not accepted
                var isFinding = thread.Kind == ThreadKind.Finding;
                if (thread.Kind != expectedKind)
                {
                    throw new DomainRuleException(
                        DomainRule.RecordChanged,
                        "The thread was promoted or demoted while you were resolving it. Review it and try again.");
                }

                if (isFinding && resolution is null)
                {
                    throw new DomainRuleException(
                        DomainRule.ResolutionNoteRequired,
                        "A finding can only be resolved with a note that says what was done.");
                }

                await context.Threads
                    .Where(t => t.Id == threadId)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.State, ReviewThreadState.Resolved), cancellationToken);
                if (resolution is not null)
                {
                    context.Comments.Add(new ReviewComment
                    {
                        ThreadId = threadId,
                        Author = resolution.Author,
                        AuthorUsername = resolution.AuthorUsername,
                        Text = resolution.Text,
                        Created = resolution.Created,
                        IncludeInReport = isFinding,
                        Kind = ReviewCommentKind.Resolution
                    });
                    await context.SaveChangesAsync(cancellationToken);
                }

                return (true, audit);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SetThreadKindAsync(
        int projectId,
        string reviewId,
        int threadId,
        ThreadKind kind,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                await EnsureNotLockedAsync(context, projectId, reviewId, cancellationToken);
                var thread = await context.Threads.AsNoTracking()
                    .Include(t => t.Comments.OrderBy(c => c.Id))
                    .FirstOrDefaultAsync(
                        t => t.Id == threadId && t.ProjectId == projectId && t.ReviewId == reviewId,
                        cancellationToken);
                if (thread is null || thread.Kind == kind)
                {
                    return (false, null);
                }

                // The starting comment decides the kind; the notes that resolved the thread follow it, replies do not
                var include = kind == ThreadKind.Finding;
                var followers = thread.Comments
                    .Where((c, i) => i == 0 || c.Kind == ReviewCommentKind.Resolution)
                    .Select(c => c.Id)
                    .ToList();
                await context.Comments
                    .Where(c => followers.Contains(c.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.IncludeInReport, include), cancellationToken);
                return (true, audit);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SetCommentReportAsync(
        int projectId,
        string reviewId,
        int threadId,
        int commentId,
        bool include,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                await EnsureNotLockedAsync(context, projectId, reviewId, cancellationToken);
                var thread = await context.Threads.AsNoTracking()
                    .Include(t => t.Comments.OrderBy(c => c.Id))
                    .FirstOrDefaultAsync(
                        t => t.Id == threadId && t.ProjectId == projectId && t.ReviewId == reviewId,
                        cancellationToken);
                var comment = thread?.Comments.FirstOrDefault(c => c.Id == commentId);
                if (thread is null || comment is null)
                {
                    return (false, null);
                }

                // The starting comment and the resolution notes follow the thread; only a reply can be set on its own
                if (thread.Comments[0].Id == comment.Id || comment.Kind == ReviewCommentKind.Resolution)
                {
                    throw new DomainRuleException(
                        DomainRule.CommentNotTogglable,
                        "Only a reply can be included on its own; promote or demote the thread to change its starting " +
                        "comment and its resolution notes.");
                }

                if (comment.IncludeInReport == include)
                {
                    return (false, null);
                }

                await context.Comments
                    .Where(c => c.Id == commentId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.IncludeInReport, include), cancellationToken);
                return (true, audit);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReviewThread>> ListThreadsAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        // Ordered by identity, which grows with creation order (SQLite cannot order by a DateTimeOffset)
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.Threads.AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.ReviewId == reviewId)
            .Include(t => t.Comments.OrderBy(c => c.Id))
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReviewChecklist?> GetChecklistAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.ReviewChecklists.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProjectId == projectId && c.ReviewId == reviewId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChecklistSectionState>> ListChecklistSectionStatesAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.ChecklistSectionStates.AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.ReviewId == reviewId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChecklistAnswer>> ListChecklistAnswersAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.ChecklistAnswers.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.ReviewId == reviewId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetChecklistSectionStateAsync(
        ChecklistSectionState state,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(audit);

        // Replace the row when there is one; otherwise insert it. The write lock is held for the whole
        // transaction, so no other request can insert the row between the update and the insert
        await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                await EnsureNotLockedAsync(context, state.ProjectId, state.ReviewId, cancellationToken);
                if (await UpdateSectionStateAsync(context, state, cancellationToken) == 0)
                {
                    context.ChecklistSectionStates.Add(state);
                    await context.SaveChangesAsync(cancellationToken);
                }

                return (true, audit);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetChecklistAnswerAsync(
        ChecklistAnswer answer,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(audit);

        // Same replace-or-insert approach as for the section state
        await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                await EnsureNotLockedAsync(context, answer.ProjectId, answer.ReviewId, cancellationToken);
                if (await UpdateAnswerAsync(context, answer, cancellationToken) == 0)
                {
                    context.ChecklistAnswers.Add(answer);
                    await context.SaveChangesAsync(cancellationToken);
                }

                return (true, audit);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReviewRecord?> GetRecordAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // A read transaction, so the parts of the record come from one state of the database
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        return await LoadRecordAsync(context, projectId, reviewId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReviewSnapshot?> GetSnapshotAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // One read transaction for the record, the slots and the signatures: a writer cannot commit a change
        // between the reads (SQLite keeps the reader's view stable until the transaction ends)
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var record = await LoadRecordAsync(context, projectId, reviewId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var slots = await context.ReviewerSlots.AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.ReviewId == reviewId)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);
        var signatures = await context.Signatures.AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.ReviewId == reviewId)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);
        return new ReviewSnapshot(record, slots, signatures);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, ReviewSnapshot>> ListSnapshotsAsync(
        int projectId,
        IReadOnlyCollection<string> reviewIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewIds);
        var snapshots = new Dictionary<string, ReviewSnapshot>();
        if (reviewIds.Count == 0)
        {
            return snapshots;
        }

        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // One read transaction for all records, slots and signatures, like the snapshot of a single review
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);

        // The same assembly path as the single read, so a batch entry cannot differ from GetSnapshotAsync
        var records = await LoadRecordsAsync(context, projectId, reviewIds, cancellationToken);
        foreach (var chunk in records.Keys.Chunk(IdChunkSize))
        {
            var ids = chunk.ToList();
            var slots = (await context.ReviewerSlots.AsNoTracking()
                    .Where(s => s.ProjectId == projectId && ids.Contains(s.ReviewId))
                    .OrderBy(s => s.Id)
                    .ToListAsync(cancellationToken))
                .ToLookup(s => s.ReviewId);
            var signatures = (await context.Signatures.AsNoTracking()
                    .Where(s => s.ProjectId == projectId && ids.Contains(s.ReviewId))
                    .OrderBy(s => s.Id)
                    .ToListAsync(cancellationToken))
                .ToLookup(s => s.ReviewId);
            foreach (var id in ids)
            {
                snapshots[id] = new ReviewSnapshot(records[id], slots[id].ToList(), signatures[id].ToList());
            }
        }

        return snapshots;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReviewerSlot>> ListSlotsAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.ReviewerSlots.AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.ReviewId == reviewId)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserReviewSlot>> ListSlotsOfUserAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // The two slot indexes serve the OR; only titles and names are read, never review content
        var rows = await (
            from slot in context.ReviewerSlots.AsNoTracking()
            where slot.PrimaryUserId == userId || slot.AlternateUserId == userId
            join review in context.Reviews.AsNoTracking()
                on new { slot.ProjectId, slot.ReviewId } equals new { review.ProjectId, ReviewId = review.Id }
            join project in context.Projects.AsNoTracking() on slot.ProjectId equals project.Id
            select new
            {
                slot.ProjectId,
                ProjectName = project.Name,
                slot.ReviewId,
                review.Title,
                SlotId = slot.Id,
                slot.PrimaryUserId
            })
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new UserReviewSlot(
                row.ProjectId,
                row.ProjectName,
                row.ReviewId,
                row.Title,
                row.SlotId,
                row.PrimaryUserId == userId ? SlotRole.Primary : SlotRole.Alternate))
            .OrderBy(row => row.ProjectId)
            .ThenBy(row => row.ReviewId, StringComparer.Ordinal)
            .ThenBy(row => row.SlotId)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ReviewerSlot> AddSlotAsync(
        ReviewerSlot slot,
        Func<ReviewerSlot, AuditDraft> audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<ReviewerSlot>(
            factory,
            async context =>
            {
                await EnsureNotLockedAsync(context, slot.ProjectId, slot.ReviewId, cancellationToken);

                // Insert a fresh entity so that the database always assigns the identity, whatever the caller supplied
                var entity = new ReviewerSlot
                {
                    ProjectId = slot.ProjectId,
                    ReviewId = slot.ReviewId,
                    PrimaryUserId = slot.PrimaryUserId,
                    AlternateUserId = slot.AlternateUserId
                };
                context.ReviewerSlots.Add(entity);
                await context.SaveChangesAsync(cancellationToken);
                return (entity, audit(entity));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateSlotAsync(
        int projectId,
        string reviewId,
        int slotId,
        int primaryUserId,
        int? alternateUserId,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                await EnsureNotLockedAsync(context, projectId, reviewId, cancellationToken);
                var rows = await context.ReviewerSlots
                    .Where(s => s.Id == slotId && s.ProjectId == projectId && s.ReviewId == reviewId)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(x => x.PrimaryUserId, primaryUserId)
                            .SetProperty(x => x.AlternateUserId, alternateUserId),
                        cancellationToken);
                return rows > 0 ? (true, audit) : (false, null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveSlotAsync(
        int projectId,
        string reviewId,
        int slotId,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                await EnsureNotLockedAsync(context, projectId, reviewId, cancellationToken);

                // The foreign key from the signatures makes the database refuse to remove a slot that was signed
                var rows = await context.ReviewerSlots
                    .Where(s => s.Id == slotId && s.ProjectId == projectId && s.ReviewId == reviewId)
                    .ExecuteDeleteAsync(cancellationToken);
                return rows > 0 ? (true, audit) : (false, null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReviewSignature>> ListSignaturesAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.Signatures.AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.ReviewId == reviewId)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReviewSignature> AddSignatureAsync(
        ReviewSignature signature,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(audit);

        // The write transaction (begun by the appender) takes the SQLite write lock at once, so no other writer can
        // change the record between the digest check below and the insert
        return await appender.ExecuteAsync<ReviewSignature>(
            factory,
            async context =>
            {
                // Recompute the digest from what is stored right now, with the same code the service used
                var record = await LoadRecordAsync(context, signature.ProjectId, signature.ReviewId, cancellationToken)
                             ?? throw new DomainRuleException(
                                 DomainRule.ReviewNotFound,
                                 $"Review '{signature.ReviewId}' does not exist.");
                if (RecordDigest.Compute(record) != signature.RecordDigest)
                {
                    throw new DomainRuleException(
                        DomainRule.RecordChanged,
                        "The record changed while you were signing. Review it and try again.");
                }

                var slotExists = await context.ReviewerSlots.AnyAsync(
                    s => s.Id == signature.SlotId && s.ProjectId == signature.ProjectId && s.ReviewId == signature.ReviewId,
                    cancellationToken);
                if (!slotExists)
                {
                    throw new DomainRuleException(
                        DomainRule.SlotNotFound,
                        $"Slot {signature.SlotId} does not exist in this review.");
                }

                // The open-thread rule is checked again inside the write transaction: a thread that opened after the
                // service looked cannot be signed over. Thread state is not part of the digest, so the digest check
                // above cannot see it
                if (record.Threads.Any(t => t.State != ReviewThreadState.Resolved))
                {
                    throw new DomainRuleException(
                        DomainRule.SigningNotAllowed,
                        "A thread is open; resolve it before signing.");
                }

                // Insert a fresh entity so that the database always assigns the identity
                var entity = new ReviewSignature
                {
                    ProjectId = signature.ProjectId,
                    ReviewId = signature.ReviewId,
                    SlotId = signature.SlotId,
                    UserId = signature.UserId,
                    DisplayName = signature.DisplayName,
                    Role = signature.Role,
                    SignedAt = signature.SignedAt,
                    RecordDigest = signature.RecordDigest,
                    Meaning = signature.Meaning
                };
                context.Signatures.Add(entity);
                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (IsDuplicateKey(ex))
                {
                    // The unique index on (slot, digest) stopped a second signature of the same record from the same slot
                    throw new DomainRuleException(
                        DomainRule.SigningNotAllowed,
                        "This slot already has a signature of the current record.");
                }

                return (entity, audit);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReopenReviewAsync(
        int projectId,
        string reviewId,
        ReviewReopen reopen,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reopen);
        ArgumentNullException.ThrowIfNull(audit);

        // The conclusion is checked again inside the write transaction, so two administrators who reopen at the same
        // moment cannot both succeed: the second one finds the review no longer concluded
        await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                if (!await ReviewRowExistsAsync(context, projectId, reviewId, cancellationToken))
                {
                    throw new DomainRuleException(
                        DomainRule.ReviewNotFound,
                        $"Review '{reviewId}' does not exist.");
                }

                if (!await IsConcludedAsync(context, projectId, reviewId, cancellationToken))
                {
                    throw new DomainRuleException(
                        DomainRule.ReviewNotLocked,
                        "The review is not locked, so there is nothing to reopen.");
                }

                // Insert a fresh entity so that the database always assigns the identity
                context.ReviewReopens.Add(new ReviewReopen(
                    0,
                    projectId,
                    reviewId,
                    reopen.ReopenedAt,
                    reopen.ReopenedBy,
                    reopen.ReopenedByDisplayName,
                    reopen.Reason,
                    reopen.PreviousDigest));
                return (true, audit);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReviewReopen>> ListReopensAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        // Ordered by identity, which grows with the order of the reopens (SQLite cannot order by a DateTimeOffset)
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.ReviewReopens.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.ReviewId == reviewId)
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<ReviewReopen>>> ListReopensAsync(
        int projectId,
        IReadOnlyCollection<string> reviewIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewIds);
        var reopens = new Dictionary<string, IReadOnlyList<ReviewReopen>>();
        if (reviewIds.Count == 0)
        {
            return reopens;
        }

        // Ordered by identity, which grows with the order of the reopens (SQLite cannot order by a DateTimeOffset)
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        foreach (var chunk in reviewIds.Distinct().Chunk(IdChunkSize))
        {
            var ids = chunk.ToList();
            var rows = await context.ReviewReopens.AsNoTracking()
                .Where(r => r.ProjectId == projectId && ids.Contains(r.ReviewId))
                .OrderBy(r => r.Id)
                .ToListAsync(cancellationToken);
            foreach (var group in rows.GroupBy(r => r.ReviewId))
            {
                reopens[group.Key] = group.ToList();
            }
        }

        return reopens;
    }

    /// <summary>
    ///     Refuses the change when the review is concluded; must run inside the audit transaction.
    /// </summary>
    /// <param name="context">The context of the open transaction.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the review is known not to be locked.</returns>
    /// <remarks>
    ///     This is the second line of defense behind the Core services: the check shares the write transaction with the
    ///     change, so a final signature that commits between the service check and the change cannot be overtaken.
    ///     A review that does not exist is not locked.
    /// </remarks>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ReviewLocked"/> when the review is concluded.</exception>
    private static async Task EnsureNotLockedAsync(
        ReviewCenterDbContext context,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken)
    {
        if (await IsConcludedAsync(context, projectId, reviewId, cancellationToken))
        {
            throw new DomainRuleException(DomainRule.ReviewLocked, ReviewLock.LockedMessage);
        }
    }

    /// <summary>
    ///     Tells whether a review is concluded right now, using the one definition of <see cref="ReviewLock.IsConcluded"/>.
    /// </summary>
    /// <param name="context">The context of the open transaction.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the review exists, has slots, no thread is open and every slot holds a signature of the
    ///     current digest.
    /// </returns>
    private static async Task<bool> IsConcludedAsync(
        ReviewCenterDbContext context,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken)
    {
        // Without a slot nothing can be concluded; this skips loading the record for the common case
        var slots = await context.ReviewerSlots.AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.ReviewId == reviewId)
            .ToListAsync(cancellationToken);
        if (slots.Count == 0)
        {
            return false;
        }

        var record = await LoadRecordAsync(context, projectId, reviewId, cancellationToken);
        if (record is null)
        {
            return false;
        }

        var signatures = await context.Signatures.AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.ReviewId == reviewId)
            .ToListAsync(cancellationToken);
        return ReviewLock.IsConcluded(record, slots, signatures);
    }

    /// <summary>
    ///     Tells whether the review row exists.
    /// </summary>
    /// <param name="context">The context to read with.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the review exists.</returns>
    private static Task<bool> ReviewRowExistsAsync(
        ReviewCenterDbContext context,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken)
    {
        return context.Reviews.AnyAsync(r => r.ProjectId == projectId && r.Id == reviewId, cancellationToken);
    }

    /// <summary>
    ///     Reads the record a signature covers, in one context so the parts are consistent with each other.
    /// </summary>
    /// <param name="context">The context to read with; inside a transaction opened by the caller.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The record, or <c>null</c> when the review does not exist.</returns>
    private static async Task<ReviewRecord?> LoadRecordAsync(
        ReviewCenterDbContext context,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken)
    {
        // The one assembly path: the single read is the batch read of one review
        var records = await LoadRecordsAsync(context, projectId, [reviewId], cancellationToken);
        return records.GetValueOrDefault(reviewId);
    }

    /// <summary>
    ///     Reads the records of several reviews with a constant number of queries, in one context so the parts are
    ///     consistent with each other.
    /// </summary>
    /// <param name="context">The context to read with; inside a transaction opened by the caller.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewIds">The review identifiers; duplicates are read once.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The record of each review that exists, by review identifier; a review that does not exist is absent.</returns>
    /// <remarks>
    ///     The identifiers are read in chunks of <see cref="IdChunkSize"/>, so no statement exceeds the parameter limit
    ///     of SQLite; the number of queries grows with the number of chunks and not with the number of reviews.
    /// </remarks>
    private static async Task<Dictionary<string, ReviewRecord>> LoadRecordsAsync(
        ReviewCenterDbContext context,
        int projectId,
        IEnumerable<string> reviewIds,
        CancellationToken cancellationToken)
    {
        var records = new Dictionary<string, ReviewRecord>();
        foreach (var chunk in reviewIds.Distinct().Chunk(IdChunkSize))
        {
            var ids = chunk.ToList();
            var headers = await context.Reviews.AsNoTracking()
                .Where(r => r.ProjectId == projectId && ids.Contains(r.Id))
                .Select(r => new { r.Id, r.ContentFingerprint, r.RepositoryId, r.BaselineSha, r.HeadSha, r.Selection, r.ReviewSetId, r.FollowsReviewId })
                .ToListAsync(cancellationToken);
            if (headers.Count == 0)
            {
                continue;
            }

            // The source and every change come from the frozen rows (hashes only; no file bytes are loaded)
            var files = (await context.ReviewFiles.AsNoTracking()
                    .Where(f => f.ProjectId == projectId && ids.Contains(f.ReviewId))
                    .OrderBy(f => f.Path)
                    .Select(f => new { f.ReviewId, f.Path, f.ChangeKind, f.BaselineSha256, f.Sha256 })
                    .ToListAsync(cancellationToken))
                .ToLookup(f => f.ReviewId);
            var checklists = (await context.ReviewChecklists.AsNoTracking()
                    .Where(c => c.ProjectId == projectId && ids.Contains(c.ReviewId))
                    .ToListAsync(cancellationToken))
                .ToLookup(c => c.ReviewId);
            var sections = (await context.ChecklistSectionStates.AsNoTracking()
                    .Where(s => s.ProjectId == projectId && ids.Contains(s.ReviewId))
                    .ToListAsync(cancellationToken))
                .ToLookup(s => s.ReviewId);
            var answers = (await context.ChecklistAnswers.AsNoTracking()
                    .Where(a => a.ProjectId == projectId && ids.Contains(a.ReviewId))
                    .ToListAsync(cancellationToken))
                .ToLookup(a => a.ReviewId);
            var threads = (await context.Threads.AsNoTracking()
                    .Where(t => t.ProjectId == projectId && ids.Contains(t.ReviewId))
                    .Include(t => t.Comments.OrderBy(c => c.Id))
                    .OrderBy(t => t.Id)
                    .ToListAsync(cancellationToken))
                .ToLookup(t => t.ReviewId);
            var reopens = (await context.ReviewReopens.AsNoTracking()
                    .Where(r => r.ProjectId == projectId && ids.Contains(r.ReviewId))
                    .GroupBy(r => r.ReviewId)
                    .Select(g => new { ReviewId = g.Key, Count = g.Count() })
                    .ToListAsync(cancellationToken))
                .ToDictionary(g => g.ReviewId, g => g.Count);

            foreach (var header in headers)
            {
                var source = ReviewSource.Upload;
                if (header.RepositoryId is not null)
                {
                    source = new ReviewSource(
                        header.RepositoryId,
                        header.BaselineSha,
                        header.HeadSha,
                        header.Selection,
                        files[header.Id].Select(c => new ChangedFile(
                            c.Path,
                            c.ChangeKind,
                            c.BaselineSha256,
                            c.ChangeKind == ChangeKind.Deleted ? null : c.Sha256)).ToList(),
                        header.ReviewSetId);
                }

                records[header.Id] = new ReviewRecord(
                    header.ContentFingerprint,
                    checklists[header.Id].FirstOrDefault(),
                    sections[header.Id].ToList(),
                    answers[header.Id].ToList(),
                    threads[header.Id].ToList(),
                    source,
                    reopens.GetValueOrDefault(header.Id),
                    header.FollowsReviewId);
            }
        }

        return records;
    }

    /// <summary>
    ///     Overwrites the stored row of a section state.
    /// </summary>
    /// <param name="context">The context to use.</param>
    /// <param name="state">The state to store.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of rows updated: 1 when the row exists, otherwise 0.</returns>
    private static Task<int> UpdateSectionStateAsync(
        ReviewCenterDbContext context,
        ChecklistSectionState state,
        CancellationToken cancellationToken)
    {
        return context.ChecklistSectionStates
            .Where(s => s.ProjectId == state.ProjectId && s.ReviewId == state.ReviewId && s.SectionId == state.SectionId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.Applicable, state.Applicable)
                    .SetProperty(x => x.Reason, state.Reason)
                    .SetProperty(x => x.SetBy, state.SetBy)
                    .SetProperty(x => x.SetByUsername, state.SetByUsername)
                    .SetProperty(x => x.SetAt, state.SetAt)
                    .SetProperty(x => x.CarriedFromReviewId, state.CarriedFromReviewId),
                cancellationToken);
    }

    /// <summary>
    ///     Overwrites the stored row of an answer.
    /// </summary>
    /// <param name="context">The context to use.</param>
    /// <param name="answer">The answer to store.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of rows updated: 1 when the row exists, otherwise 0.</returns>
    private static Task<int> UpdateAnswerAsync(
        ReviewCenterDbContext context,
        ChecklistAnswer answer,
        CancellationToken cancellationToken)
    {
        return context.ChecklistAnswers
            .Where(a => a.ProjectId == answer.ProjectId && a.ReviewId == answer.ReviewId && a.ItemId == answer.ItemId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.Outcome, answer.Outcome)
                    .SetProperty(x => x.Justification, answer.Justification)
                    .SetProperty(x => x.AnsweredBy, answer.AnsweredBy)
                    .SetProperty(x => x.AnsweredByUsername, answer.AnsweredByUsername)
                    .SetProperty(x => x.AnsweredAt, answer.AnsweredAt)
                    .SetProperty(x => x.CarriedFromReviewId, answer.CarriedFromReviewId),
                cancellationToken);
    }

    /// <summary>
    ///     Copies a carried thread into a fresh entity without identities, so the database assigns them.
    /// </summary>
    /// <param name="thread">The carried thread.</param>
    /// <returns>The fresh thread with fresh copies of its comments.</returns>
    private static ReviewThread CopyCarriedThread(ReviewThread thread)
    {
        return new ReviewThread
        {
            ProjectId = thread.ProjectId,
            ReviewId = thread.ReviewId,
            FilePath = thread.FilePath,
            Side = thread.Side,
            StartLine = thread.StartLine,
            EndLine = thread.EndLine,
            State = thread.State,
            Created = thread.Created,
            CarriedFromReviewId = thread.CarriedFromReviewId,
            CarriedFromThreadId = thread.CarriedFromThreadId,
            Comments = thread.Comments.Select(c => NewComment(c)).ToList()
        };
    }

    /// <summary>
    ///     Copies a comment into a fresh entity without identities, so the database assigns them.
    /// </summary>
    /// <param name="comment">The comment to copy.</param>
    /// <param name="threadId">The owning thread, or 0 when the comment is added together with a new thread.</param>
    /// <returns>The fresh comment.</returns>
    private static ReviewComment NewComment(ReviewComment comment, int threadId = 0)
    {
        return new ReviewComment
        {
            ThreadId = threadId,
            Author = comment.Author,
            AuthorUsername = comment.AuthorUsername,
            Text = comment.Text,
            Created = comment.Created,
            IncludeInReport = comment.IncludeInReport,
            Kind = comment.Kind
        };
    }

    /// <summary>
    ///     Reports whether a save failure was caused by a primary key or unique constraint violation.
    /// </summary>
    /// <param name="exception">The failure raised by the save.</param>
    /// <returns><c>true</c> when the cause is a duplicate key; foreign key and other failures are not.</returns>
    private static bool IsDuplicateKey(DbUpdateException exception)
    {
        return exception.InnerException is SqliteException
        {
            SqliteExtendedErrorCode: SqlitePrimaryKeyViolation or SqliteUniqueViolation
        };
    }
}
