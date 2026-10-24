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
    ///     Gets a snapshot of the reviews added so far, including their file content.
    /// </summary>
    public IReadOnlyList<Review> Reviews => _reviews.ToArray();

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
