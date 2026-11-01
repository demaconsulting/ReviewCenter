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
    ///     The stored ballot status rows; the rows of a slot's ballot are the entries of <c>_answers</c> and
    ///     <c>_sectionStates</c> with the slot's identity.
    /// </summary>
    private readonly List<ChecklistBallot> _ballots = [];

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
        if (!ReviewLock.IsConcluded(snapshot.Record, snapshot.Slots, snapshot.Signatures))
        {
            throw new DomainRuleException(DomainRule.ReviewNotLocked, "The review is not concluded, so it cannot be reopened.");
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
    public Task<IReadOnlyDictionary<string, IReadOnlyList<ReviewReopen>>> ListReopensAsync(
        int projectId,
        IReadOnlyCollection<string> reviewIds,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, IReadOnlyList<ReviewReopen>> grouped = _reopens
            .Where(r => r.ProjectId == projectId && reviewIds.Contains(r.ReviewId))
            .OrderBy(r => r.Id)
            .GroupBy(r => r.ReviewId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ReviewReopen>)g.ToList());
        return Task.FromResult(grouped);
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
    public Task<IReadOnlyList<Project>> ListSoleAdminProjectsAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        // Like the real store: only an enabled administrator counts, and only other enabled administrators count as another
        IReadOnlyList<Project> list = !IsUserEnabled(userId)
            ? []
            : _projects
                .Where(p => _memberships.Any(m => m.ProjectId == p.Id && m.UserId == userId && m.Role == ProjectRole.ProjectAdmin)
                            && !_memberships.Any(m => m.ProjectId == p.Id
                                                      && m.UserId != userId
                                                      && m.Role == ProjectRole.ProjectAdmin
                                                      && IsUserEnabled(m.UserId)))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ThenBy(p => p.Id)
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
                    CheckItemId = thread.CheckItemId,
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

        // Like the real store: a concluded review refuses the change, and the date must still be the one expected
        var snapshot = await GetSnapshotAsync(projectId, reviewId, cancellationToken);
        if (snapshot is not null && ReviewLock.IsConcluded(snapshot.Record, snapshot.Slots, snapshot.Signatures))
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
            ReviewConclusionText = current.ReviewConclusionText,
            CarriedAnswerCount = current.CarriedAnswerCount,
            CarriedThreadCount = current.CarriedThreadCount,
            Files = current.Files,
            Checklist = current.Checklist
        };
        Audits.Add(audit);
        return true;
    }

    /// <summary>
    ///     Gets or sets an action that runs just before a review conclusion text change is compared and applied, so a
    ///     test can simulate someone else changing the text at that moment.
    /// </summary>
    public Func<Task>? BeforeReviewConclusionTextUpdate { get; set; }

    /// <inheritdoc />
    public async Task<bool> UpdateReviewConclusionTextAsync(
        int projectId,
        string reviewId,
        string? expected,
        string? reviewConclusionText,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        if (BeforeReviewConclusionTextUpdate is { } before)
        {
            await before();
        }

        // Like the real store: a concluded review refuses the change, and the text must still be the one expected
        var snapshot = await GetSnapshotAsync(projectId, reviewId, cancellationToken);
        if (snapshot is not null && ReviewLock.IsConcluded(snapshot.Record, snapshot.Slots, snapshot.Signatures))
        {
            throw new DomainRuleException(DomainRule.ReviewLocked, ReviewLock.LockedMessage);
        }

        var index = _reviews.FindIndex(r => r.ProjectId == projectId && r.Id == reviewId);
        if (index < 0 || _reviews[index].ReviewConclusionText != expected)
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
            DueDate = current.DueDate,
            ContentFingerprint = current.ContentFingerprint,
            RepositoryId = current.RepositoryId,
            RepositoryName = current.RepositoryName,
            BaselineSha = current.BaselineSha,
            HeadSha = current.HeadSha,
            Selection = current.Selection,
            ReviewSetId = current.ReviewSetId,
            FollowsReviewId = current.FollowsReviewId,
            ReviewConclusionText = reviewConclusionText,
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

    /// <summary>
    ///     Gets the number of times the reviews of a project were listed, so a test can show that a caller reads the
    ///     list once.
    /// </summary>
    public int ListReviewsCalls { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<Review>> ListReviewsAsync(int projectId, CancellationToken cancellationToken = default)
    {
        ListReviewsCalls++;
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
        var isCheck = thread.CheckItemId is not null;
        var anchorIsValid = isCheck
            ? thread.CheckItemId!.Length > 0
              && thread.FilePath.Length == 0
              && thread.StartLine is null
              && thread.EndLine is null
              && thread.Side == ThreadSide.Head
            : thread.FilePath.Length > 0;
        if (!anchorIsValid)
        {
            throw new ArgumentException("A thread is anchored to exactly one of a file or a check.", nameof(thread));
        }

        // Like the real store, a file thread must name a file of its review (when the review is known to the fake)
        var known = _reviews.FirstOrDefault(review => review.ProjectId == thread.ProjectId && review.Id == thread.ReviewId);
        if (!isCheck && known is not null && known.Files.All(f => f.Path != thread.FilePath))
        {
            throw new InvalidOperationException($"The file '{thread.FilePath}' does not exist in review '{thread.ReviewId}'.");
        }

        var id = _threads.Count + 1;
        var stored = new ReviewThread
        {
            Id = id,
            ProjectId = thread.ProjectId,
            ReviewId = thread.ReviewId,
            FilePath = thread.FilePath,
            CheckItemId = thread.CheckItemId,
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
    public Task<bool> ReopenThreadAsync(
        int projectId,
        string reviewId,
        int threadId,
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
        ReplaceThread(index, ReviewThreadState.Open, _threads[index].Comments);
        return Task.FromResult(true);
    }

    /// <summary>
    ///     Gets or sets an action that runs just before a thread is resolved, so a test can simulate someone else
    ///     promoting or demoting the thread between the service's check and the store's write.
    /// </summary>
    public Func<Task>? BeforeResolveThread { get; set; }

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
        if (BeforeResolveThread is { } before)
        {
            await before();
        }

        await EnsureNotConcludedAsync(projectId, reviewId, cancellationToken);
        var index = _threads.FindIndex(t => t.Id == threadId && t.ProjectId == projectId && t.ReviewId == reviewId);
        if (index < 0 || _threads[index].State == ReviewThreadState.Resolved)
        {
            return false;
        }

        // The kind is read again and compared with the expected one, as the real store does inside its transaction
        var isFinding = _threads[index].Kind == ThreadKind.Finding;
        if (_threads[index].Kind != expectedKind)
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

        var comments = _threads[index].Comments.ToList();
        if (resolution is not null)
        {
            comments.Add(new ReviewComment
            {
                Id = _nextCommentId++,
                ThreadId = threadId,
                Author = resolution.Author,
                AuthorUsername = resolution.AuthorUsername,
                Text = resolution.Text,
                Created = resolution.Created,
                IncludeInReport = isFinding,
                Kind = ReviewCommentKind.Resolution
            });
        }

        ReplaceThread(index, ReviewThreadState.Resolved, comments);
        Audits.Add(audit);
        return true;
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
        await EnsureNotConcludedAsync(projectId, reviewId, cancellationToken);
        var index = _threads.FindIndex(t => t.Id == threadId && t.ProjectId == projectId && t.ReviewId == reviewId);
        if (index < 0 || _threads[index].Kind == kind)
        {
            return false;
        }

        // The starting comment and the resolution notes follow the thread; replies keep their own flags
        var include = kind == ThreadKind.Finding;
        var comments = _threads[index].Comments
            .Select((c, i) => i == 0 || c.Kind == ReviewCommentKind.Resolution ? WithFlag(c, include) : c)
            .ToList();
        ReplaceThread(index, _threads[index].State, comments);
        Audits.Add(audit);
        return true;
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
        await EnsureNotConcludedAsync(projectId, reviewId, cancellationToken);
        var index = _threads.FindIndex(t => t.Id == threadId && t.ProjectId == projectId && t.ReviewId == reviewId);
        var comment = index < 0 ? null : _threads[index].Comments.FirstOrDefault(c => c.Id == commentId);
        if (comment is null)
        {
            return false;
        }

        if (_threads[index].Comments[0].Id == comment.Id || comment.Kind == ReviewCommentKind.Resolution)
        {
            throw new DomainRuleException(DomainRule.CommentNotTogglable, "Only a reply can be included on its own.");
        }

        if (comment.IncludeInReport == include)
        {
            return false;
        }

        var comments = _threads[index].Comments.Select(c => c.Id == commentId ? WithFlag(c, include) : c).ToList();
        ReplaceThread(index, _threads[index].State, comments);
        Audits.Add(audit);
        return true;
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
            .Where(s => s.ProjectId == projectId && s.ReviewId == reviewId && s.SlotId == ChecklistAnswer.BaselineSlotId)
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
            .Where(a => a.ProjectId == projectId && a.ReviewId == reviewId && a.SlotId == ChecklistAnswer.BaselineSlotId)
            .ToList();
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Ballot>> ListBallotsAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Ballot> list = _ballots
            .Where(b => b.ProjectId == projectId && b.ReviewId == reviewId)
            .OrderBy(b => b.SlotId)
            .Select(b => BuildBallot(b))
            .ToList();
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task<Ballot?> GetBallotAsync(
        int projectId,
        string reviewId,
        int slotId,
        CancellationToken cancellationToken = default)
    {
        var row = FindBallot(projectId, reviewId, slotId);
        return Task.FromResult(row is null ? null : BuildBallot(row));
    }

    /// <inheritdoc />
    public async Task<Ballot> SetBallotAnswerAsync(
        ChecklistAnswer answer,
        AuditDraft audit,
        Func<Ballot, bool>? completesBallot,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(answer.SlotId, ChecklistAnswer.BaselineSlotId);
        return await StoreBallotEditAsync(
            answer.ProjectId,
            answer.ReviewId,
            answer.SlotId,
            answer.AnsweredAt,
            answer.AnsweredByUsername,
            audit,
            completesBallot,
            () =>
            {
                _answers.RemoveAll(a => a.ProjectId == answer.ProjectId && a.ReviewId == answer.ReviewId
                    && a.SlotId == answer.SlotId && a.ItemId == answer.ItemId);
                _answers.Add(answer);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Ballot> SetBallotSectionStateAsync(
        ChecklistSectionState state,
        AuditDraft audit,
        Func<Ballot, bool>? completesBallot,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(state.SlotId, ChecklistAnswer.BaselineSlotId);
        return await StoreBallotEditAsync(
            state.ProjectId,
            state.ReviewId,
            state.SlotId,
            state.SetAt,
            state.SetByUsername,
            audit,
            completesBallot,
            () =>
            {
                _sectionStates.RemoveAll(s => s.ProjectId == state.ProjectId && s.ReviewId == state.ReviewId
                    && s.SlotId == state.SlotId && s.SectionId == state.SectionId);
                _sectionStates.Add(state);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Ballot> SubmitBallotAsync(
        int projectId,
        string reviewId,
        int slotId,
        DateTimeOffset submittedAt,
        string submittedByUsername,
        AuditDraft audit,
        Func<Ballot, bool> isComplete,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(slotId, ChecklistAnswer.BaselineSlotId);
        await EnsureBallotWritableAsync(projectId, reviewId, cancellationToken);
        _ = RequireSlotNumber(projectId, reviewId, slotId);
        var row = FindBallot(projectId, reviewId, slotId);
        if (row is { Status: BallotStatus.Submitted })
        {
            return BuildBallot(row);
        }

        var ballot = row is null ? new Ballot(slotId, BallotStatus.Draft, null, null, [], []) : BuildBallot(row);
        if (!isComplete(ballot))
        {
            throw new DomainRuleException(
                DomainRule.BallotIncomplete,
                "The ballot cannot be submitted while a check that applies has no answer.");
        }

        SetBallotStatus(projectId, reviewId, slotId, BallotStatus.Submitted, submittedAt, submittedByUsername);
        var submitted = BuildBallot(FindBallot(projectId, reviewId, slotId)!);
        Audits.Add(audit);
        if (AfterSubmitBallot is { } hook)
        {
            // Runs once: the action itself may hand in another ballot
            AfterSubmitBallot = null;
            await hook();
        }

        return submitted;
    }

    /// <summary>
    ///     Gets or sets an action that runs once right after a ballot was handed in, so a test can simulate somebody
    ///     else changing the record at that moment.
    /// </summary>
    public Func<Task>? AfterSubmitBallot { get; set; }

    /// <summary>
    ///     Gets the 1-based number of a slot among the slots of its review and the number of slots, as the real store does.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <returns>The slot number and the slot count.</returns>
    private (int Number, int Count) RequireSlotNumber(int projectId, string reviewId, int slotId)
    {
        var ids = _slots
            .Where(s => s.ProjectId == projectId && s.ReviewId == reviewId)
            .Select(s => s.Id)
            .Order()
            .ToList();
        var index = ids.IndexOf(slotId);
        if (index < 0)
        {
            throw new DomainRuleException(DomainRule.SlotNotFound, $"The slot {slotId} does not exist in this review.");
        }

        return (index + 1, ids.Count);
    }

    /// <summary>
    ///     Finds the status row of a ballot.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <returns>The status row, or <c>null</c> when the slot has none.</returns>
    private ChecklistBallot? FindBallot(int projectId, string reviewId, int slotId)
    {
        return _ballots.FirstOrDefault(b => b.ProjectId == projectId && b.ReviewId == reviewId && b.SlotId == slotId);
    }

    /// <summary>
    ///     Builds a ballot from its status row and the stored rows of its slot.
    /// </summary>
    /// <param name="row">The status row.</param>
    /// <returns>The ballot.</returns>
    private Ballot BuildBallot(ChecklistBallot row)
    {
        return new Ballot(
            row.SlotId,
            row.Status,
            row.SubmittedAt,
            row.SubmittedByUsername,
            _answers
                .Where(a => a.ProjectId == row.ProjectId && a.ReviewId == row.ReviewId && a.SlotId == row.SlotId)
                .OrderBy(a => a.ItemId, StringComparer.Ordinal)
                .ToList(),
            _sectionStates
                .Where(s => s.ProjectId == row.ProjectId && s.ReviewId == row.ReviewId && s.SlotId == row.SlotId)
                .OrderBy(s => s.SectionId, StringComparer.Ordinal)
                .ToList());
    }

    /// <summary>
    ///     Stores the status of a ballot, replacing the status row when there is one.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <param name="status">The status to store.</param>
    /// <param name="submittedAt">When the ballot was handed in; <c>null</c> for a draft.</param>
    /// <param name="submittedByUsername">Who handed the ballot in; <c>null</c> for a draft.</param>
    private void SetBallotStatus(
        int projectId,
        string reviewId,
        int slotId,
        BallotStatus status,
        DateTimeOffset? submittedAt,
        string? submittedByUsername)
    {
        _ballots.RemoveAll(b => b.ProjectId == projectId && b.ReviewId == reviewId && b.SlotId == slotId);
        _ballots.Add(new ChecklistBallot
        {
            ProjectId = projectId,
            ReviewId = reviewId,
            SlotId = slotId,
            Status = status,
            SubmittedAt = submittedAt,
            SubmittedByUsername = submittedByUsername
        });
    }

    /// <summary>
    ///     Stores one edit of a ballot and settles its status, as the real store does.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <param name="editedAt">When the row was written.</param>
    /// <param name="editedByUsername">Who wrote the row.</param>
    /// <param name="audit">The audit draft of the edit.</param>
    /// <param name="completesBallot">The completeness test for the single-slot automatic submit, or <c>null</c>.</param>
    /// <param name="storeRow">Stores the answer or section state row.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The ballot as stored.</returns>
    private async Task<Ballot> StoreBallotEditAsync(
        int projectId,
        string reviewId,
        int slotId,
        DateTimeOffset editedAt,
        string editedByUsername,
        AuditDraft audit,
        Func<Ballot, bool>? completesBallot,
        Action storeRow,
        CancellationToken cancellationToken)
    {
        await EnsureBallotWritableAsync(projectId, reviewId, cancellationToken);
        var (slotNumber, slotCount) = RequireSlotNumber(projectId, reviewId, slotId);
        var wasSubmitted = FindBallot(projectId, reviewId, slotId) is { Status: BallotStatus.Submitted };

        // An edit makes the ballot a draft; a single-slot review submits a complete ballot at once
        storeRow();
        SetBallotStatus(projectId, reviewId, slotId, BallotStatus.Draft, null, null);
        if (slotCount == 1 && completesBallot is not null && completesBallot(BuildBallot(FindBallot(projectId, reviewId, slotId)!)))
        {
            SetBallotStatus(projectId, reviewId, slotId, BallotStatus.Submitted, editedAt, editedByUsername);
        }

        var ballot = BuildBallot(FindBallot(projectId, reviewId, slotId)!);
        Audits.Add(audit);
        var isSubmitted = ballot.Status == BallotStatus.Submitted;
        if (wasSubmitted != isSubmitted)
        {
            Audits.Add(isSubmitted
                ? audit with
                {
                    Action = "ballot.submitted",
                    Detail = $"slot {slotNumber}: ballot submitted ({ballot.Answers.Count} answers, {ballot.SectionStates.Count} section states)"
                }
                : audit with
                {
                    Action = "ballot.reopened",
                    Detail = $"slot {slotNumber}: ballot reverted to draft by an edit"
                });
        }

        return ballot;
    }

    /// <summary>
    ///     Gets or sets how the store finds the user name of a user identity, which the reassignment of a slot needs to
    ///     tell the answers of a departing reviewer from the others; an unknown user has none.
    /// </summary>
    public Func<int, string?> UsernameOfUser { get; set; } = _ => null;

    /// <summary>
    ///     Gets or sets a value indicating whether ballot writes ignore the lock of a concluded review, so a test can
    ///     model a change that slipped in between two checks of a service, which the real store prevents by taking the
    ///     lock inside its transaction.
    /// </summary>
    public bool IgnoreLockForBallots { get; set; }

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
            _ballots
                .Where(b => b.ProjectId == projectId && b.ReviewId == reviewId)
                .OrderBy(b => b.SlotId)
                .Select(b => BuildBallot(b))
                .ToList(),
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
            review.FollowsReviewId,
            review.ReviewConclusionText);
        return Task.FromResult<ReviewRecord?>(record);
    }

    /// <inheritdoc />
    public async Task<ReviewSnapshot?> GetSnapshotAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        SnapshotReads++;
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

    /// <summary>
    ///     Gets the number of single snapshot reads (<see cref="GetSnapshotAsync"/>) made so far; tests use it to show
    ///     that a batch read does not read a snapshot per review.
    /// </summary>
    public int SnapshotReads { get; private set; }

    /// <summary>
    ///     Gets the number of batch snapshot reads (<see cref="ListSnapshotsAsync"/>) made so far.
    /// </summary>
    public int SnapshotBatchReads { get; private set; }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, ReviewSnapshot>> ListSnapshotsAsync(
        int projectId,
        IReadOnlyCollection<string> reviewIds,
        CancellationToken cancellationToken = default)
    {
        // In memory, so the batch reads each snapshot the single way without counting it as a single read
        SnapshotBatchReads++;
        var snapshots = new Dictionary<string, ReviewSnapshot>();
        var singleReads = SnapshotReads;
        foreach (var id in reviewIds.Distinct())
        {
            var snapshot = await GetSnapshotAsync(projectId, id, cancellationToken);
            if (snapshot is not null)
            {
                snapshots[id] = snapshot;
            }
        }

        SnapshotReads = singleReads;
        return snapshots;
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
    public Task<IReadOnlyList<UserReviewSlot>> ListSlotsOfUserAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<UserReviewSlot> list = _slots
            .Where(s => s.PrimaryUserId == userId || s.AlternateUserId == userId)
            .Select(s => new UserReviewSlot(
                s.ProjectId,
                _projects.First(p => p.Id == s.ProjectId).Name,
                s.ReviewId,
                _reviews.First(r => r.ProjectId == s.ProjectId && r.Id == s.ReviewId).Title,
                s.Id,
                s.PrimaryUserId == userId ? SlotRole.Primary : SlotRole.Alternate))
            .OrderBy(s => s.ProjectId)
            .ThenBy(s => s.ReviewId, StringComparer.Ordinal)
            .ThenBy(s => s.SlotId)
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

        // A follow-up review carries review-level rows; the new slot starts from them as a draft ballot
        var carriedAnswers = _answers
            .Where(a => a.ProjectId == stored.ProjectId && a.ReviewId == stored.ReviewId
                && a.SlotId == ChecklistAnswer.BaselineSlotId && a.CarriedFromReviewId != null)
            .ToList();
        var carriedSections = _sectionStates
            .Where(s => s.ProjectId == stored.ProjectId && s.ReviewId == stored.ReviewId
                && s.SlotId == ChecklistAnswer.BaselineSlotId && s.CarriedFromReviewId != null)
            .ToList();
        if (carriedAnswers.Count > 0 || carriedSections.Count > 0)
        {
            _answers.AddRange(carriedAnswers.Select(a => new ChecklistAnswer
            {
                ProjectId = a.ProjectId,
                ReviewId = a.ReviewId,
                SlotId = stored.Id,
                ItemId = a.ItemId,
                Outcome = a.Outcome,
                Justification = a.Justification,
                AnsweredBy = a.AnsweredBy,
                AnsweredByUsername = a.AnsweredByUsername,
                AnsweredAt = a.AnsweredAt,
                CarriedFromReviewId = a.CarriedFromReviewId
            }));
            _sectionStates.AddRange(carriedSections.Select(s => new ChecklistSectionState
            {
                ProjectId = s.ProjectId,
                ReviewId = s.ReviewId,
                SlotId = stored.Id,
                SectionId = s.SectionId,
                Applicable = s.Applicable,
                Reason = s.Reason,
                SetBy = s.SetBy,
                SetByUsername = s.SetByUsername,
                SetAt = s.SetAt,
                CarriedFromReviewId = s.CarriedFromReviewId
            }));
            SetBallotStatus(stored.ProjectId, stored.ReviewId, stored.Id, BallotStatus.Draft, null, null);
        }

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

        var old = _slots[index];
        _slots[index] = new ReviewerSlot
        {
            Id = slotId,
            ProjectId = projectId,
            ReviewId = reviewId,
            PrimaryUserId = primaryUserId,
            AlternateUserId = alternateUserId
        };
        Audits.Add(audit);

        // Reviewers who leave the slot take their own rows with them; carried rows stay even when they still name a
        // departing reviewer (they keep the user name of the earlier author), and the ballot is a draft again
        var departing = new[] { old.PrimaryUserId, old.AlternateUserId }
            .OfType<int>()
            .Where(id => id != primaryUserId && id != alternateUserId)
            .Distinct()
            .ToList();
        if (departing.Count > 0)
        {
            ReassignBallot(old, departing, audit);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    ///     Removes the contributions of departing reviewers from the ballot of a slot, as the real store does.
    /// </summary>
    /// <param name="slot">The slot before the change.</param>
    /// <param name="departing">The identities of the reviewers who leave the slot.</param>
    /// <param name="audit">The audit draft of the slot change.</param>
    private void ReassignBallot(ReviewerSlot slot, List<int> departing, AuditDraft audit)
    {
        var names = departing.Select(id => UsernameOfUser(id)).OfType<string>().ToList();
        var removedAnswers = _answers.RemoveAll(a => a.ProjectId == slot.ProjectId && a.ReviewId == slot.ReviewId
            && a.SlotId == slot.Id && a.CarriedFromReviewId == null
            && names.Contains(a.AnsweredByUsername, StringComparer.OrdinalIgnoreCase));
        var removedSections = _sectionStates.RemoveAll(s => s.ProjectId == slot.ProjectId && s.ReviewId == slot.ReviewId
            && s.SlotId == slot.Id && s.CarriedFromReviewId == null
            && names.Contains(s.SetByUsername, StringComparer.OrdinalIgnoreCase));
        var reverted = FindBallot(slot.ProjectId, slot.ReviewId, slot.Id) is { Status: BallotStatus.Submitted };
        if (reverted)
        {
            SetBallotStatus(slot.ProjectId, slot.ReviewId, slot.Id, BallotStatus.Draft, null, null);
        }

        if (removedAnswers == 0 && removedSections == 0 && !reverted)
        {
            return;
        }

        var (slotNumber, _) = RequireSlotNumber(slot.ProjectId, slot.ReviewId, slot.Id);
        Audits.Add(audit with
        {
            Action = "ballot.reassigned",
            Detail = $"slot {slotNumber}: ballot reverted to draft after a reassignment "
                + $"({removedAnswers} answers, {removedSections} section states of departed reviewers removed)"
        });
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
            _answers.RemoveAll(a => a.ProjectId == projectId && a.ReviewId == reviewId && a.SlotId == slotId);
            _sectionStates.RemoveAll(s => s.ProjectId == projectId && s.ReviewId == reviewId && s.SlotId == slotId);
            _ballots.RemoveAll(b => b.ProjectId == projectId && b.ReviewId == reviewId && b.SlotId == slotId);
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

    /// <summary>
    ///     Gets or sets a value indicating whether <see cref="AddSignatureAsync"/> refuses a signature whose outcome is not
    ///     the outcome of the record (as the real store does). A test that needs to seed an inconsistent signature, to
    ///     show how a reader treats it, sets this to <c>false</c>.
    /// </summary>
    public bool ValidateSignatureOutcome { get; set; } = true;

    /// <inheritdoc />
    public async Task<ReviewSignature> AddSignatureAsync(
        ReviewSignature signature,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        // The same checks, in the same order, as the real store: the digest is recomputed from what is stored, an open
        // thread is refused (thread state is not in the digest) and the pair (slot, digest) is unique
        var record = await GetRecordAsync(signature.ProjectId, signature.ReviewId, cancellationToken)
                     ?? throw new DomainRuleException(DomainRule.ReviewNotFound, "The review does not exist.");
        if (RecordDigest.Compute(record) != signature.RecordDigest)
        {
            throw new DomainRuleException(DomainRule.RecordChanged, "The record changed while you were signing.");
        }

        if (record.Threads.Any(t => t.State != ReviewThreadState.Resolved))
        {
            throw new DomainRuleException(DomainRule.SigningNotAllowed, "A thread is open; resolve it before signing.");
        }

        if (record.Checklist is not null
            && !record.Ballots.Any(b => b.SlotId == signature.SlotId && b.Status == BallotStatus.Submitted))
        {
            throw new DomainRuleException(
                DomainRule.BallotNotSubmitted,
                "Your ballot has not been handed in; submit it before signing.");
        }

        // The outcome of the signature must be the one the record has now (as in the real store): an approval of a
        // rejected record or the reverse is refused; a record without a checklist is approved, and a record with
        // pending or disputed checks has no outcome yet, so nothing is compared
        var recordOutcome = record.Checklist is null
            ? ReviewOutcome.Approved
            : BallotConsolidation.Consolidate(record.Checklist.Parse(), record.Ballots).Outcome;
        if (ValidateSignatureOutcome && recordOutcome is { } expected && expected != signature.Outcome)
        {
            throw new DomainRuleException(
                DomainRule.RecordChanged,
                "The outcome of the record changed while you were signing. Review it and try again.");
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
            Outcome = signature.Outcome,
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
            Created = comment.Created,
            IncludeInReport = comment.IncludeInReport,
            Kind = comment.Kind
        };
    }

    /// <summary>
    ///     Replaces a stored thread by a copy with another state and other comments (the classes are init-only).
    /// </summary>
    /// <param name="index">The position of the thread in the store.</param>
    /// <param name="state">The state of the copy.</param>
    /// <param name="comments">The comments of the copy.</param>
    private void ReplaceThread(int index, ReviewThreadState state, List<ReviewComment> comments)
    {
        var old = _threads[index];
        _threads[index] = new ReviewThread
        {
            Id = old.Id,
            ProjectId = old.ProjectId,
            ReviewId = old.ReviewId,
            FilePath = old.FilePath,
            CheckItemId = old.CheckItemId,
            Side = old.Side,
            StartLine = old.StartLine,
            EndLine = old.EndLine,
            State = state,
            Created = old.Created,
            CarriedFromReviewId = old.CarriedFromReviewId,
            CarriedFromThreadId = old.CarriedFromThreadId,
            Comments = comments
        };
    }

    /// <summary>
    ///     Copies a comment with another report flag, keeping its identity.
    /// </summary>
    /// <param name="comment">The comment to copy.</param>
    /// <param name="include">The report flag of the copy.</param>
    /// <returns>The copy.</returns>
    private static ReviewComment WithFlag(ReviewComment comment, bool include)
    {
        return new ReviewComment
        {
            Id = comment.Id,
            ThreadId = comment.ThreadId,
            Author = comment.Author,
            AuthorUsername = comment.AuthorUsername,
            Text = comment.Text,
            Created = comment.Created,
            IncludeInReport = include,
            Kind = comment.Kind
        };
    }

    /// <summary>
    ///     Refuses a ballot write when the stored review is concluded, unless the test lets the write through.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the write may go on.</returns>
    private Task EnsureBallotWritableAsync(int projectId, string reviewId, CancellationToken cancellationToken)
    {
        return IgnoreLockForBallots ? Task.CompletedTask : EnsureNotConcludedAsync(projectId, reviewId, cancellationToken);
    }

    /// <summary>
    ///     Refuses the change when the stored review is concluded, as the real store does inside its transaction.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the review is known not to be locked.</returns>
    private async Task EnsureNotConcludedAsync(int projectId, string reviewId, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(projectId, reviewId, cancellationToken);
        if (snapshot is not null && ReviewLock.IsConcluded(snapshot.Record, snapshot.Slots, snapshot.Signatures))
        {
            throw new DomainRuleException(DomainRule.ReviewLocked, ReviewLock.LockedMessage);
        }
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
            ReviewConclusionText = review.ReviewConclusionText,
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
