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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     A <see cref="TimeProvider"/> that always reports the same instant.
/// </summary>
/// <param name="now">The instant to report.</param>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
///     In-memory <see cref="IReviewStore"/> used to test the domain services and pages without a database.
/// </summary>
/// <remarks>
///     It stores what it is given and returns copies shaped like the real store: reads of a review carry
///     file metadata but no content, and content is read separately. It performs no validation of its own,
///     so it never hides a rule the services are supposed to enforce.
/// </remarks>
internal sealed class FakeReviewStore : IReviewStore
{
    /// <summary>
    ///     The stored projects.
    /// </summary>
    private readonly List<Project> _projects = [];

    /// <summary>
    ///     The stored reviews, including their file content.
    /// </summary>
    private readonly List<Review> _reviews = [];

    /// <summary>
    ///     The stored threads, each with its comments.
    /// </summary>
    private readonly List<ReviewThread> _threads = [];

    /// <summary>
    ///     The stored checklist section states.
    /// </summary>
    private readonly List<ChecklistSectionState> _sectionStates = [];

    /// <summary>
    ///     The stored checklist answers.
    /// </summary>
    private readonly List<ChecklistAnswer> _answers = [];

    /// <summary>
    ///     The stored reviewer slots.
    /// </summary>
    private readonly List<ReviewerSlot> _slots = [];

    /// <summary>
    ///     The stored signatures.
    /// </summary>
    private readonly List<ReviewSignature> _signatures = [];

    /// <summary>
    ///     The identity given to the next stored comment.
    /// </summary>
    private int _nextCommentId = 1;

    /// <summary>
    ///     Gets a snapshot of the reviews added so far, including their file content.
    /// </summary>
    public IReadOnlyList<Review> Reviews => _reviews.ToArray();

    /// <summary>
    ///     Gets a snapshot of the threads stored so far.
    /// </summary>
    public IReadOnlyList<ReviewThread> Threads => _threads.ToArray();

    /// <inheritdoc />
    public Task<Project> AddProjectAsync(Project project, CancellationToken cancellationToken = default)
    {
        var stored = new Project
        {
            Id = _projects.Count + 1,
            Name = project.Name,
            Description = project.Description,
            Created = project.Created
        };
        _projects.Add(stored);
        return Task.FromResult(stored);
    }

    /// <inheritdoc />
    public Task<Project?> GetProjectAsync(int projectId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_projects.FirstOrDefault(project => project.Id == projectId));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Project>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<Project>>(_projects.OrderBy(project => project.Name, StringComparer.Ordinal).ToList());
    }

    /// <inheritdoc />
    public Task<bool> ReviewExistsAsync(int projectId, string reviewId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_reviews.Any(review => review.ProjectId == projectId && review.Id == reviewId));
    }

    /// <inheritdoc />
    public Task AddReviewAsync(Review review, CancellationToken cancellationToken = default)
    {
        _reviews.Add(review);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Review?> GetReviewAsync(int projectId, string reviewId, CancellationToken cancellationToken = default)
    {
        var review = _reviews.FirstOrDefault(r => r.ProjectId == projectId && r.Id == reviewId);
        return Task.FromResult(review is null ? null : WithoutContent(review));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Review>> ListReviewsAsync(int projectId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Review> list = _reviews
            .Where(review => review.ProjectId == projectId)
            .Select(WithoutContent)
            .ToList();
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task<byte[]?> GetFileContentAsync(
        int projectId,
        string reviewId,
        string path,
        CancellationToken cancellationToken = default)
    {
        var file = _reviews
            .Where(review => review.ProjectId == projectId && review.Id == reviewId)
            .SelectMany(review => review.Files)
            .FirstOrDefault(f => f.Path == path);
        return Task.FromResult(file?.Content);
    }

    /// <inheritdoc />
    public Task<ReviewThread> AddThreadAsync(ReviewThread thread, CancellationToken cancellationToken = default)
    {
        var id = _threads.Count + 1;
        var stored = new ReviewThread
        {
            Id = id,
            ProjectId = thread.ProjectId,
            ReviewId = thread.ReviewId,
            FilePath = thread.FilePath,
            StartLine = thread.StartLine,
            EndLine = thread.EndLine,
            State = thread.State,
            Created = thread.Created,
            Comments = thread.Comments.Select(comment => Copy(comment, id)).ToList()
        };
        _threads.Add(stored);
        return Task.FromResult(stored);
    }

    /// <inheritdoc />
    public Task<ReviewComment?> AddCommentAsync(
        int projectId,
        string reviewId,
        int threadId,
        ReviewComment comment,
        CancellationToken cancellationToken = default)
    {
        var thread = Find(projectId, reviewId, threadId);
        if (thread is null)
        {
            return Task.FromResult<ReviewComment?>(null);
        }

        var stored = Copy(comment, threadId);
        thread.Comments.Add(stored);
        return Task.FromResult<ReviewComment?>(stored);
    }

    /// <inheritdoc />
    public Task<bool> SetThreadStateAsync(
        int projectId,
        string reviewId,
        int threadId,
        ReviewThreadState state,
        CancellationToken cancellationToken = default)
    {
        var index = _threads.FindIndex(t => t.Id == threadId && t.ProjectId == projectId && t.ReviewId == reviewId);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        // The state is init-only, so the stored thread is replaced by a copy with the new state
        var old = _threads[index];
        _threads[index] = new ReviewThread
        {
            Id = old.Id,
            ProjectId = old.ProjectId,
            ReviewId = old.ReviewId,
            FilePath = old.FilePath,
            StartLine = old.StartLine,
            EndLine = old.EndLine,
            State = state,
            Created = old.Created,
            Comments = old.Comments
        };
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReviewThread>> ListThreadsAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReviewThread> list = _threads
            .Where(t => t.ProjectId == projectId && t.ReviewId == reviewId)
            .OrderBy(t => t.Id)
            .ToList();
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task<ReviewChecklist?> GetChecklistAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        var review = _reviews.FirstOrDefault(r => r.ProjectId == projectId && r.Id == reviewId);
        return Task.FromResult(review?.Checklist);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChecklistSectionState>> ListChecklistSectionStatesAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChecklistSectionState> list = _sectionStates
            .Where(s => s.ProjectId == projectId && s.ReviewId == reviewId)
            .ToList();
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChecklistAnswer>> ListChecklistAnswersAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChecklistAnswer> list = _answers
            .Where(a => a.ProjectId == projectId && a.ReviewId == reviewId)
            .ToList();
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task SetChecklistSectionStateAsync(ChecklistSectionState state, CancellationToken cancellationToken = default)
    {
        _sectionStates.RemoveAll(s => s.ProjectId == state.ProjectId && s.ReviewId == state.ReviewId && s.SectionId == state.SectionId);
        _sectionStates.Add(state);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetChecklistAnswerAsync(ChecklistAnswer answer, CancellationToken cancellationToken = default)
    {
        _answers.RemoveAll(a => a.ProjectId == answer.ProjectId && a.ReviewId == answer.ReviewId && a.ItemId == answer.ItemId);
        _answers.Add(answer);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ReviewRecord?> GetRecordAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        var review = _reviews.FirstOrDefault(r => r.ProjectId == projectId && r.Id == reviewId);
        if (review is null)
        {
            return Task.FromResult<ReviewRecord?>(null);
        }

        var record = new ReviewRecord(
            review.ContentFingerprint,
            review.Checklist,
            _sectionStates.Where(s => s.ProjectId == projectId && s.ReviewId == reviewId).ToList(),
            _answers.Where(a => a.ProjectId == projectId && a.ReviewId == reviewId).ToList(),
            _threads.Where(t => t.ProjectId == projectId && t.ReviewId == reviewId).OrderBy(t => t.Id).ToList());
        return Task.FromResult<ReviewRecord?>(record);
    }

    /// <inheritdoc />
    public async Task<ReviewSnapshot?> GetSnapshotAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        var record = await GetRecordAsync(projectId, reviewId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        return new ReviewSnapshot(
            record,
            await ListSlotsAsync(projectId, reviewId, cancellationToken),
            await ListSignaturesAsync(projectId, reviewId, cancellationToken));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReviewerSlot>> ListSlotsAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReviewerSlot> list = _slots
            .Where(s => s.ProjectId == projectId && s.ReviewId == reviewId)
            .OrderBy(s => s.Id)
            .ToList();
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task<ReviewerSlot> AddSlotAsync(ReviewerSlot slot, CancellationToken cancellationToken = default)
    {
        var stored = new ReviewerSlot
        {
            Id = _slots.Count == 0 ? 1 : _slots.Max(s => s.Id) + 1,
            ProjectId = slot.ProjectId,
            ReviewId = slot.ReviewId,
            PrimaryUserId = slot.PrimaryUserId,
            AlternateUserId = slot.AlternateUserId
        };
        _slots.Add(stored);
        return Task.FromResult(stored);
    }

    /// <inheritdoc />
    public Task<bool> UpdateSlotAsync(
        int projectId,
        string reviewId,
        int slotId,
        int primaryUserId,
        int? alternateUserId,
        CancellationToken cancellationToken = default)
    {
        var index = _slots.FindIndex(s => s.Id == slotId && s.ProjectId == projectId && s.ReviewId == reviewId);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        _slots[index] = new ReviewerSlot
        {
            Id = slotId,
            ProjectId = projectId,
            ReviewId = reviewId,
            PrimaryUserId = primaryUserId,
            AlternateUserId = alternateUserId
        };
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> RemoveSlotAsync(
        int projectId,
        string reviewId,
        int slotId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_slots.RemoveAll(s => s.Id == slotId && s.ProjectId == projectId && s.ReviewId == reviewId) > 0);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReviewSignature>> ListSignaturesAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReviewSignature> list = _signatures
            .Where(s => s.ProjectId == projectId && s.ReviewId == reviewId)
            .OrderBy(s => s.Id)
            .ToList();
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public async Task<ReviewSignature> AddSignatureAsync(
        ReviewSignature signature,
        CancellationToken cancellationToken = default)
    {
        // The same checks as the real store: the digest is recomputed from what is stored and the pair
        // (slot, digest) is unique
        var record = await GetRecordAsync(signature.ProjectId, signature.ReviewId, cancellationToken)
                     ?? throw new DomainRuleException(DomainRule.ReviewNotFound, "The review does not exist.");
        if (RecordDigest.Compute(record) != signature.RecordDigest)
        {
            throw new DomainRuleException(DomainRule.RecordChanged, "The record changed while you were signing.");
        }

        if (_signatures.Any(s => s.SlotId == signature.SlotId && s.RecordDigest == signature.RecordDigest))
        {
            throw new DomainRuleException(DomainRule.SigningNotAllowed, "This slot already has a signature of the current record.");
        }

        var stored = new ReviewSignature
        {
            Id = _signatures.Count + 1,
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
        _signatures.Add(stored);
        return stored;
    }

    /// <summary>
    ///     Finds a stored thread of a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The thread identity.</param>
    /// <returns>The thread, or <c>null</c> when the review has no such thread.</returns>
    private ReviewThread? Find(int projectId, string reviewId, int threadId)
    {
        return _threads.FirstOrDefault(t => t.Id == threadId && t.ProjectId == projectId && t.ReviewId == reviewId);
    }

    /// <summary>
    ///     Copies a comment and gives the copy the next identity.
    /// </summary>
    /// <param name="comment">The comment to copy.</param>
    /// <param name="threadId">The thread the copy belongs to.</param>
    /// <returns>The stored copy.</returns>
    private ReviewComment Copy(ReviewComment comment, int threadId)
    {
        return new ReviewComment
        {
            Id = _nextCommentId++,
            ThreadId = threadId,
            Author = comment.Author,
            AuthorUsername = comment.AuthorUsername,
            Text = comment.Text,
            Created = comment.Created
        };
    }

    /// <summary>
    ///     Copies a review without the bytes of its files, as the real store does for display reads.
    /// </summary>
    /// <param name="review">The stored review.</param>
    /// <returns>The copy.</returns>
    private static Review WithoutContent(Review review)
    {
        return new Review
        {
            ProjectId = review.ProjectId,
            Id = review.Id,
            Title = review.Title,
            State = review.State,
            Created = review.Created,
            ContentFingerprint = review.ContentFingerprint,
            Files = review.Files
                .Select(file => new ReviewFile
                {
                    ProjectId = file.ProjectId,
                    ReviewId = file.ReviewId,
                    Path = file.Path,
                    Size = file.Size,
                    Sha256 = file.Sha256
                })
                .ToList()
        };
    }
}
