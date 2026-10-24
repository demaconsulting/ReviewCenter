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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DemaConsulting.ReviewCenter.Storage;

/// <summary>
///     The <see cref="IReviewStore"/> implementation backed by <see cref="ReviewCenterDbContext"/>.
/// </summary>
/// <remarks>
///     Each operation uses its own short-lived context from the factory, so the store is safe for
///     concurrent use by all browser sessions. Read operations are untracked, and listing a review
///     never loads file bytes. A uniqueness violation on the review key is translated to a
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
