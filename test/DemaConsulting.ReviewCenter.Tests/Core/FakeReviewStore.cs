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
    ///     The stored project memberships.
    /// </summary>
    private readonly List<ProjectMembership> _memberships = [];

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
    ///     The stored reopens of approved reviews.
    /// </summary>
    private readonly List<ReviewReopen> _reopens = [];

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

    /// <summary>
    ///     Gets the audit drafts received so far, in the order the changes were made; a change that did not
    ///     take effect adds none.
    /// </summary>
    public List<AuditDraft> Audits { get; } = [];

    /// <inheritdoc />
    public Task<Project> AddProjectAsync(
        Project project,
        Func<Project, AuditDraft> audit,
        CancellationToken cancellationToken = default)
    {
        var stored = new Project
        {
            Id = _projects.Count + 1,
            Name = project.Name,
            Description = project.Description,
            Created = project.Created,
            Evidence = project.Evidence,
            RequireIndependentReviewers = project.RequireIndependentReviewers,
            RequireNonAuthorChecklistAnswers = project.RequireNonAuthorChecklistAnswers,
            RequireNonReopenerSigning = project.RequireNonReopenerSigning,
            ChecklistDefinition = project.ChecklistDefinition
        };
        _projects.Add(stored);
        Audits.Add(audit(stored));
        return Task.FromResult(stored);
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
    public Task<bool> UpdateReviewRuleAsync(
        int projectId,
        ReviewRule rule,
        bool expected,
        bool updated,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        // Like the real store: compare and set in one step; a lost race or an unknown project changes nothing
        if (!Enum.IsDefined(rule))
        {
            throw new ArgumentOutOfRangeException(nameof(rule), rule, "The rule is not a known value.");
        }

        var index = _projects.FindIndex(p => p.Id == projectId);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        var old = _projects[index];
        var current = rule switch
        {
            ReviewRule.IndependentReviewers => old.RequireIndependentReviewers,
            ReviewRule.NonAuthorChecklistAnswers => old.RequireNonAuthorChecklistAnswers,
            _ => old.RequireNonReopenerSigning
        };
        if (current != expected)
        {
            return Task.FromResult(false);
        }

        _projects[index] = new Project
        {
            Id = old.Id,
            Name = old.Name,
            Description = old.Description,
            Created = old.Created,
            Evidence = old.Evidence,
            RequireIndependentReviewers = rule == ReviewRule.IndependentReviewers ? updated : old.RequireIndependentReviewers,
            RequireNonAuthorChecklistAnswers = rule == ReviewRule.NonAuthorChecklistAnswers ? updated : old.RequireNonAuthorChecklistAnswers,
            RequireNonReopenerSigning = rule == ReviewRule.NonReopenerSigning ? updated : old.RequireNonReopenerSigning,
            ChecklistDefinition = old.ChecklistDefinition
        };
        Audits.Add(audit);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task ReopenReviewAsync(
        int projectId,
        string reviewId,
        ReviewReopen reopen,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        // Like the real store: the review must be approved now; the fake adds no lock to the other writes so
        // that tests can arrange a record that the services would refuse to change
        var snapshot = await GetSnapshotAsync(projectId, reviewId, cancellationToken)
                       ?? throw new DomainRuleException(DomainRule.ReviewNotFound, "The review does not exist.");
        if (!ReviewLock.IsApproved(snapshot.Record, snapshot.Slots, snapshot.Signatures))
        {
            throw new DomainRuleException(DomainRule.ReviewNotLocked, "The review is not approved, so it cannot be reopened.");
        }

        _reopens.Add(reopen with { Id = _reopens.Count + 1 });
        Audits.Add(audit);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReviewReopen>> ListReopensAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReviewReopen> list = _reopens
            .Where(r => r.ProjectId == projectId && r.ReviewId == reviewId)
            .OrderBy(r => r.Id)
            .ToList();
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task<bool> UpdateEvidenceSettingsAsync(
        int projectId,
        EvidenceSettings expected,
        EvidenceSettings updated,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        // Like the real store: compare and set in one step; a lost race or an unknown project changes nothing
        var index = _projects.FindIndex(p => p.Id == projectId);
        if (index < 0 || _projects[index].Evidence != expected)
        {
            return Task.FromResult(false);
        }

        var old = _projects[index];
        _projects[index] = new Project
        {
            Id = old.Id,
            Name = old.Name,
            Description = old.Description,
            Created = old.Created,
            Evidence = updated,
            RequireIndependentReviewers = old.RequireIndependentReviewers,
            RequireNonAuthorChecklistAnswers = old.RequireNonAuthorChecklistAnswers,
            RequireNonReopenerSigning = old.RequireNonReopenerSigning,
            ChecklistDefinition = old.ChecklistDefinition
        };
        Audits.Add(audit);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> UpdateChecklistDefinitionAsync(
        int projectId,
        string expectedHash,
        string? definitionJson,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        // Like the real store: compare the hash of the stored text and set in one step; a lost race or an unknown
        // project changes nothing
        var index = _projects.FindIndex(p => p.Id == projectId);
        if (index < 0 || ProjectChecklistService.HashOf(_projects[index].ChecklistDefinition) != expectedHash)
        {
            return Task.FromResult(false);
        }

        var old = _projects[index];
        _projects[index] = new Project
        {
            Id = old.Id,
            Name = old.Name,
            Description = old.Description,
            Created = old.Created,
            Evidence = old.Evidence,
            RequireIndependentReviewers = old.RequireIndependentReviewers,
            RequireNonAuthorChecklistAnswers = old.RequireNonAuthorChecklistAnswers,
            RequireNonReopenerSigning = old.RequireNonReopenerSigning,
            ChecklistDefinition = definitionJson
        };
        Audits.Add(audit);
        return Task.FromResult(true);
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
    public Task AddMembershipAsync(
        ProjectMembership membership,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        // Like the real store's unique key: a user is a member of a project at most once
        if (_memberships.Any(m => m.UserId == membership.UserId && m.ProjectId == membership.ProjectId))
        {
            throw new DomainRuleException(DomainRule.MembershipExists, "The user is already a member of the project.");
        }

        _memberships.Add(membership);
        Audits.Add(audit);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> RemoveMembershipAsync(
        int projectId,
        int userId,
        AuditDraft audit,
        bool requireAnotherAdmin,
        CancellationToken cancellationToken = default)
    {
        var target = _memberships.FirstOrDefault(m => m.UserId == userId && m.ProjectId == projectId);
        if (target is null)
        {
            return Task.FromResult(false);
        }

        // Like the real store: the rule is checked in the same step as the change
        if (requireAnotherAdmin && target.Role == ProjectRole.ProjectAdmin && IsUserEnabled(userId))
        {
            EnsureAnotherAdmin(projectId, userId);
        }

        _memberships.Remove(target);
        Audits.Add(audit);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> SetMembershipRoleAsync(
        int projectId,
        int userId,
        ProjectRole role,
        AuditDraft audit,
        bool requireAnotherAdmin,
        CancellationToken cancellationToken = default)
    {
        var index = _memberships.FindIndex(m => m.UserId == userId && m.ProjectId == projectId);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        if (requireAnotherAdmin && role != ProjectRole.ProjectAdmin
            && _memberships[index].Role == ProjectRole.ProjectAdmin && IsUserEnabled(userId))
        {
            EnsureAnotherAdmin(projectId, userId);
        }

        _memberships[index] = _memberships[index] with { Role = role };
        Audits.Add(audit);
        return Task.FromResult(true);
    }

    /// <summary>
    ///     Gets or sets the test hook that tells whether a user is enabled; the last-administrator rule counts only
    ///     enabled administrators, like the real store. By default every user counts as enabled.
    /// </summary>
    public Func<int, bool> IsUserEnabled { get; set; } = _ => true;

    /// <summary>
    ///     Adds a membership without an audit entry; used by <see cref="FakeUserStore"/> when it stores a user and
    ///     the membership in one step (the one entry is kept by that store).
    /// </summary>
    /// <param name="membership">The membership to add.</param>
    public void AddMembershipWithoutAudit(ProjectMembership membership)
    {
        _memberships.Add(membership);
    }

    /// <summary>
    ///     Throws when no other enabled project administrator remains in a project.
    /// </summary>
    /// <param name="projectId">The project.</param>
    /// <param name="userId">The administrator that is about to be removed or demoted.</param>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.LastProjectAdmin"/>.</exception>
    private void EnsureAnotherAdmin(int projectId, int userId)
    {
        var others = _memberships.Count(
            m => m.ProjectId == projectId
                && m.UserId != userId
                && m.Role == ProjectRole.ProjectAdmin
                && IsUserEnabled(m.UserId));
        if (others == 0)
        {
            throw new DomainRuleException(DomainRule.LastProjectAdmin, "This is the last project administrator.");
        }
    }

    /// <inheritdoc />
    public Task<ProjectMembership?> GetMembershipAsync(
        int userId,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_memberships.FirstOrDefault(m => m.UserId == userId && m.ProjectId == projectId));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProjectMembership>> ListMembershipsAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProjectMembership> list = _memberships
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.UserId)
            .ToList();
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProjectMembership>> ListMembershipsForUserAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProjectMembership> list = _memberships
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.ProjectId)
            .ToList();
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task<bool> ReviewExistsAsync(int projectId, string reviewId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_reviews.Any(review => review.ProjectId == projectId && review.Id == reviewId));
    }

    /// <inheritdoc />
    public Task AddReviewAsync(Review review, AuditDraft audit, CancellationToken cancellationToken = default)
    {
        // Like the real store's unique key: a project cannot have two reviews with the same id
        if (_reviews.Any(r => r.ProjectId == review.ProjectId && r.Id == review.Id))
        {
            throw new DomainRuleException(DomainRule.ReviewIdDuplicate, $"A review with the id '{review.Id}' already exists.");
        }

        _reviews.Add(review);

        // The rows carried over from the followed review are stored with it, like the real store does in one transaction;
        // a thread of a file that is not in the review fails like the real foreign key
        if (review.CarryOver is { } carry)
        {
            foreach (var thread in carry.Threads)
            {
                if (review.Files.All(f => f.Path != thread.FilePath))
                {
                    _reviews.Remove(review);
                    throw new InvalidOperationException($"The carried thread names the file '{thread.FilePath}', which is not in the review.");
                }
            }

            _answers.AddRange(carry.Answers);
            _sectionStates.AddRange(carry.SectionStates);
            foreach (var thread in carry.Threads)
            {
                var id = _threads.Count + 1;
                _threads.Add(new ReviewThread
                {
                    Id = id,
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
                    Comments = thread.Comments.Select(comment => Copy(comment, id)).ToList()
                });
            }
        }

        Audits.Add(audit);
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Gets or sets an action that runs just before a due date change is compared and applied, so a test can
    ///     simulate someone else changing the date at that moment.
    /// </summary>
    public Action? BeforeDueDateUpdate { get; set; }

    /// <inheritdoc />
    public async Task<bool> UpdateDueDateAsync(
        int projectId,
        string reviewId,
        DateOnly? expected,
        DateOnly? dueDate,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        BeforeDueDateUpdate?.Invoke();

        // Like the real store: an approved review refuses the change, and the date must still be the one expected
        var snapshot = await GetSnapshotAsync(projectId, reviewId, cancellationToken);
        if (snapshot is not null && ReviewLock.IsApproved(snapshot.Record, snapshot.Slots, snapshot.Signatures))
        {
            throw new DomainRuleException(DomainRule.ReviewLocked, ReviewLock.LockedMessage);
        }

        var index = _reviews.FindIndex(r => r.ProjectId == projectId && r.Id == reviewId);
        if (index < 0 || _reviews[index].DueDate != expected)
        {
            return false;
        }

        var current = _reviews[index];
        _reviews[index] = new Review
        {
            ProjectId = current.ProjectId,
            Id = current.Id,
            Title = current.Title,
            State = current.State,
            Created = current.Created,
            CreatedBy = current.CreatedBy,
            CreatedViaApi = current.CreatedViaApi,
            DueDate = dueDate,
            ContentFingerprint = current.ContentFingerprint,
            RepositoryId = current.RepositoryId,
            RepositoryName = current.RepositoryName,
            BaselineSha = current.BaselineSha,
            HeadSha = current.HeadSha,
            Selection = current.Selection,
            ReviewSetId = current.ReviewSetId,
            FollowsReviewId = current.FollowsReviewId,
            CarriedAnswerCount = current.CarriedAnswerCount,
            CarriedThreadCount = current.CarriedThreadCount,
            Files = current.Files,
            Checklist = current.Checklist
        };
        Audits.Add(audit);
        return true;
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
    public Task<byte[]?> GetBaselineContentAsync(
        int projectId,
        string reviewId,
        string path,
        CancellationToken cancellationToken = default)
    {
        var file = _reviews
            .Where(review => review.ProjectId == projectId && review.Id == reviewId)
            .SelectMany(review => review.Files)
            .FirstOrDefault(f => f.Path == path);
        return Task.FromResult(file?.BaselineContent);
    }

    /// <inheritdoc />
    public Task<ReviewThread> AddThreadAsync(
        ReviewThread thread,
        Func<ReviewThread, AuditDraft> audit,
        CancellationToken cancellationToken = default)
    {
        var id = _threads.Count + 1;
        var stored = new ReviewThread
        {
            Id = id,
            ProjectId = thread.ProjectId,
            ReviewId = thread.ReviewId,
            FilePath = thread.FilePath,
            Side = thread.Side,
            StartLine = thread.StartLine,
            EndLine = thread.EndLine,
            State = thread.State,
            Created = thread.Created,
            Comments = thread.Comments.Select(comment => Copy(comment, id)).ToList()
        };
        _threads.Add(stored);
        Audits.Add(audit(stored));
        return Task.FromResult(stored);
    }

    /// <inheritdoc />
    public Task<ReviewComment?> AddCommentAsync(
        int projectId,
        string reviewId,
        int threadId,
        ReviewComment comment,
        Func<ReviewComment, AuditDraft> audit,
        CancellationToken cancellationToken = default)
    {
        var thread = Find(projectId, reviewId, threadId);
        if (thread is null)
        {
            return Task.FromResult<ReviewComment?>(null);
        }

        var stored = Copy(comment, threadId);
        thread.Comments.Add(stored);
        Audits.Add(audit(stored));
        return Task.FromResult<ReviewComment?>(stored);
    }

    /// <inheritdoc />
    public Task<bool> SetThreadStateAsync(
        int projectId,
        string reviewId,
        int threadId,
        ReviewThreadState state,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        var index = _threads.FindIndex(t => t.Id == threadId && t.ProjectId == projectId && t.ReviewId == reviewId);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        Audits.Add(audit);
        // The state is init-only, so the stored thread is replaced by a copy with the new state
        var old = _threads[index];
        _threads[index] = new ReviewThread
        {
            Id = old.Id,
            ProjectId = old.ProjectId,
            ReviewId = old.ReviewId,
            FilePath = old.FilePath,
            Side = old.Side,
            StartLine = old.StartLine,
            EndLine = old.EndLine,
            State = state,
            Created = old.Created,
            CarriedFromReviewId = old.CarriedFromReviewId,
            CarriedFromThreadId = old.CarriedFromThreadId,
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
    public Task SetChecklistSectionStateAsync(
        ChecklistSectionState state,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        _sectionStates.RemoveAll(s => s.ProjectId == state.ProjectId && s.ReviewId == state.ReviewId && s.SectionId == state.SectionId);
        _sectionStates.Add(state);
        Audits.Add(audit);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetChecklistAnswerAsync(
        ChecklistAnswer answer,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        _answers.RemoveAll(a => a.ProjectId == answer.ProjectId && a.ReviewId == answer.ReviewId && a.ItemId == answer.ItemId);
        _answers.Add(answer);
        Audits.Add(audit);
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
            _threads.Where(t => t.ProjectId == projectId && t.ReviewId == reviewId).OrderBy(t => t.Id).ToList(),
            review.IsFromRepository
                ? new ReviewSource(
                    review.RepositoryId,
                    review.BaselineSha,
                    review.HeadSha,
                    review.Selection,
                    review.Files
                        .Select(f => new ChangedFile(
                            f.Path,
                            f.ChangeKind,
                            f.BaselineSha256,
                            f.ChangeKind == ChangeKind.Deleted ? null : f.Sha256))
                        .ToList(),
                    review.ReviewSetId)
                : ReviewSource.Upload,
            _reopens.Count(r => r.ProjectId == projectId && r.ReviewId == reviewId),
            review.FollowsReviewId);
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
    public Task<ReviewerSlot> AddSlotAsync(
        ReviewerSlot slot,
        Func<ReviewerSlot, AuditDraft> audit,
        CancellationToken cancellationToken = default)
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
        Audits.Add(audit(stored));
        return Task.FromResult(stored);
    }

    /// <inheritdoc />
    public Task<bool> UpdateSlotAsync(
        int projectId,
        string reviewId,
        int slotId,
        int primaryUserId,
        int? alternateUserId,
        AuditDraft audit,
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
        Audits.Add(audit);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> RemoveSlotAsync(
        int projectId,
        string reviewId,
        int slotId,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        var removed = _slots.RemoveAll(s => s.Id == slotId && s.ProjectId == projectId && s.ReviewId == reviewId) > 0;
        if (removed)
        {
            Audits.Add(audit);
        }

        return Task.FromResult(removed);
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
        AuditDraft audit,
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
        Audits.Add(audit);
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
            CreatedBy = review.CreatedBy,
            CreatedViaApi = review.CreatedViaApi,
            DueDate = review.DueDate,
            ContentFingerprint = review.ContentFingerprint,
            RepositoryId = review.RepositoryId,
            RepositoryName = review.RepositoryName,
            BaselineSha = review.BaselineSha,
            HeadSha = review.HeadSha,
            Selection = review.Selection,
            ReviewSetId = review.ReviewSetId,
            FollowsReviewId = review.FollowsReviewId,
            CarriedAnswerCount = review.CarriedAnswerCount,
            CarriedThreadCount = review.CarriedThreadCount,
            Files = review.Files
                .Select(file => new ReviewFile
                {
                    ProjectId = file.ProjectId,
                    ReviewId = file.ReviewId,
                    Path = file.Path,
                    Size = file.Size,
                    Sha256 = file.Sha256,
                    ChangeKind = file.ChangeKind,
                    BaselineSize = file.BaselineSize,
                    BaselineSha256 = file.BaselineSha256
                })
                .ToList()
        };
    }
}
