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
///     projects, reviews and review files.
/// </summary>
/// <remarks>
///     The interface is owned by Core and implemented by the Storage subsystem, so the domain rules
///     can be tested with a fake store and Core never references a database library. Every method
///     is asynchronous and honors its cancellation token. Implementations must be safe for
///     concurrent use because the services are shared by all browser sessions.
/// </remarks>
public interface IReviewStore
{
    /// <summary>
    ///     Stores a new project and assigns its identity.
    /// </summary>
    /// <param name="project">The project to store. Must not be null; its <see cref="Project.Id"/> is ignored.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored project, including the assigned identity.</returns>
    Task<Project> AddProjectAsync(Project project, CancellationToken cancellationToken = default);

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
    ///     Reports whether a review with the given id exists in a project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the review exists.</returns>
    Task<bool> ReviewExistsAsync(int projectId, string reviewId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stores a new review together with its files.
    /// </summary>
    /// <param name="review">The review to store, with its files and their bytes. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the review is stored.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ReviewIdDuplicate"/> when the project already has a
    ///     review with that id, including when it was created concurrently.
    /// </exception>
    Task AddReviewAsync(Review review, CancellationToken cancellationToken = default);

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
}
