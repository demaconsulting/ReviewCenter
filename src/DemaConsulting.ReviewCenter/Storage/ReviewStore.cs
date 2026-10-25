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
///     Answers and section states are replaced in place (one row per check or section). Slots are plain rows;
///     signatures are only ever inserted, inside a transaction that rechecks the record digest. A uniqueness violation on the review key is translated to a
///     <see cref="DomainRuleException"/> so callers see the same error as for the service pre-check.
/// </remarks>
/// <param name="factory">The factory that creates a context per operation.</param>
internal sealed class ReviewStore(IDbContextFactory<ReviewCenterDbContext> factory) : IReviewStore
{
    /// <summary>
    ///     The SQLite extended result code for a primary key constraint violation.
    /// </summary>
    private const int SqlitePrimaryKeyViolation = 1555;

    /// <summary>
    ///     The SQLite extended result code for a unique constraint violation.
    /// </summary>
    private const int SqliteUniqueViolation = 2067;

    /// <inheritdoc />
    public async Task<Project> AddProjectAsync(Project project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // Insert a fresh entity so that the database always assigns the identity, whatever the caller supplied
        var entity = new Project
        {
            Name = project.Name,
            Description = project.Description,
            Created = project.Created
        };
        context.Projects.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity;
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
    public async Task<bool> ReviewExistsAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.Reviews.AnyAsync(r => r.ProjectId == projectId && r.Id == reviewId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddReviewAsync(Review review, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);

        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        context.Reviews.Add(review);
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
                Sha256 = f.Sha256
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
    public async Task<ReviewThread> AddThreadAsync(ReviewThread thread, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);

        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // Insert fresh entities so that the database always assigns the identities, whatever the caller supplied
        var entity = new ReviewThread
        {
            ProjectId = thread.ProjectId,
            ReviewId = thread.ReviewId,
            FilePath = thread.FilePath,
            StartLine = thread.StartLine,
            EndLine = thread.EndLine,
            State = thread.State,
            Created = thread.Created,
            Comments = thread.Comments.Select(comment => NewComment(comment)).ToList()
        };
        context.Threads.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <inheritdoc />
    public async Task<ReviewComment?> AddCommentAsync(
        int projectId,
        string reviewId,
        int threadId,
        ReviewComment comment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comment);

        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // The thread must belong to the stated review, so an identity from another review cannot be commented on
        var exists = await context.Threads.AnyAsync(
            t => t.Id == threadId && t.ProjectId == projectId && t.ReviewId == reviewId,
            cancellationToken);
        if (!exists)
        {
            return null;
        }

        var entity = NewComment(comment, threadId);
        context.Comments.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <inheritdoc />
    public async Task<bool> SetThreadStateAsync(
        int projectId,
        string reviewId,
        int threadId,
        ReviewThreadState state,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await context.Threads
            .Where(t => t.Id == threadId && t.ProjectId == projectId && t.ReviewId == reviewId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.State, state), cancellationToken);
        return rows > 0;
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // Replace the row when there is one; otherwise insert it. Two requests can both find no row, so the
        // loser of the insert race updates the row the winner created
        if (await UpdateSectionStateAsync(context, state, cancellationToken) > 0)
        {
            return;
        }

        context.ChecklistSectionStates.Add(state);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            context.ChangeTracker.Clear();
            await UpdateSectionStateAsync(context, state, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task SetChecklistAnswerAsync(ChecklistAnswer answer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(answer);

        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // Same replace-or-insert approach as for the section state
        if (await UpdateAnswerAsync(context, answer, cancellationToken) > 0)
        {
            return;
        }

        context.ChecklistAnswers.Add(answer);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            context.ChangeTracker.Clear();
            await UpdateAnswerAsync(context, answer, cancellationToken);
        }
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
    public async Task<ReviewerSlot> AddSlotAsync(ReviewerSlot slot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slot);

        await using var context = await factory.CreateDbContextAsync(cancellationToken);

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
        return entity;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateSlotAsync(
        int projectId,
        string reviewId,
        int slotId,
        int primaryUserId,
        int? alternateUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await context.ReviewerSlots
            .Where(s => s.Id == slotId && s.ProjectId == projectId && s.ReviewId == reviewId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.PrimaryUserId, primaryUserId)
                    .SetProperty(x => x.AlternateUserId, alternateUserId),
                cancellationToken);
        return rows > 0;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveSlotAsync(
        int projectId,
        string reviewId,
        int slotId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // The foreign key from the signatures makes the database refuse to remove a slot that was signed
        var rows = await context.ReviewerSlots
            .Where(s => s.Id == slotId && s.ProjectId == projectId && s.ReviewId == reviewId)
            .ExecuteDeleteAsync(cancellationToken);
        return rows > 0;
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signature);

        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // Serializable makes SQLite take the write lock when the transaction begins, so no other writer can change
        // the record between the digest check below and the insert
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

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

        await transaction.CommitAsync(cancellationToken);
        return entity;
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
        var fingerprint = await context.Reviews.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.Id == reviewId)
            .Select(r => r.ContentFingerprint)
            .FirstOrDefaultAsync(cancellationToken);
        if (fingerprint is null)
        {
            return null;
        }

        var checklist = await context.ReviewChecklists.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProjectId == projectId && c.ReviewId == reviewId, cancellationToken);
        var sections = await context.ChecklistSectionStates.AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.ReviewId == reviewId)
            .ToListAsync(cancellationToken);
        var answers = await context.ChecklistAnswers.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.ReviewId == reviewId)
            .ToListAsync(cancellationToken);
        var threads = await context.Threads.AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.ReviewId == reviewId)
            .Include(t => t.Comments.OrderBy(c => c.Id))
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);
        return new ReviewRecord(fingerprint, checklist, sections, answers, threads);
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
                    .SetProperty(x => x.SetAt, state.SetAt),
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
                    .SetProperty(x => x.AnsweredAt, answer.AnsweredAt),
                cancellationToken);
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
            Created = comment.Created
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
