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
///     Creates and reads projects, enforcing the project rules.
/// </summary>
/// <remarks>
///     The service is the single place where project rules live, so every caller (web pages today,
///     other front ends later) gets the same behavior. It holds no state of its own and is safe for
///     concurrent use as long as the injected <see cref="IReviewStore"/> is.
/// </remarks>
public sealed class ProjectService
{
    /// <summary>
    ///     The store that persists projects.
    /// </summary>
    private readonly IReviewStore _store;

    /// <summary>
    ///     The clock that supplies creation times.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ProjectService"/> class.
    /// </summary>
    /// <param name="store">The store that persists projects. Must not be null.</param>
    /// <param name="timeProvider">The clock used for creation times; injected so tests control time. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public ProjectService(IReviewStore store, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     Creates a project.
    /// </summary>
    /// <param name="name">
    ///     The project name; leading and trailing white space is removed. Must not be empty and must
    ///     not exceed <see cref="ReviewLimits.MaxProjectNameLength"/> characters. Need not be unique.
    /// </param>
    /// <param name="description">
    ///     The description; may be null or empty, leading and trailing white space is removed and the
    ///     result must not exceed <see cref="ReviewLimits.MaxProjectDescriptionLength"/> characters.
    /// </param>
    /// <param name="actor">The signed-in user creating the project; recorded in the audit trail. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The created project, including its assigned identity.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown when the name or description violates a rule; nothing is stored then.
    /// </exception>
    public async Task<Project> CreateAsync(
        string? name,
        string? description,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        // Normalize first so that the rules apply to what would actually be stored
        var trimmedName = name?.Trim() ?? string.Empty;
        var trimmedDescription = description?.Trim() ?? string.Empty;

        // Reject invalid input before any I/O
        if (trimmedName.Length == 0)
        {
            throw new DomainRuleException(DomainRule.ProjectNameInvalid, "The project name is required.");
        }

        if (trimmedName.Length > ReviewLimits.MaxProjectNameLength)
        {
            throw new DomainRuleException(
                DomainRule.ProjectNameInvalid,
                $"The project name must not be longer than {ReviewLimits.MaxProjectNameLength} characters.");
        }

        if (trimmedDescription.Length > ReviewLimits.MaxProjectDescriptionLength)
        {
            throw new DomainRuleException(
                DomainRule.ProjectDescriptionTooLong,
                $"The project description must not be longer than {ReviewLimits.MaxProjectDescriptionLength} characters.");
        }

        // Store the project; the store assigns the identity, and saves the audit entry in the same transaction
        var project = new Project
        {
            Name = trimmedName,
            Description = trimmedDescription,
            Created = _timeProvider.GetUtcNow()
        };
        return await _store.AddProjectAsync(
            project,
            stored => AuditDraft.By(actor, "project.created", $"project '{stored.Name}'", stored.Id),
            cancellationToken);
    }

    /// <summary>
    ///     Lists all projects.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>All projects, ordered by name and then identity.</returns>
    public Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default)
    {
        return _store.ListProjectsAsync(cancellationToken);
    }

    /// <summary>
    ///     Reads one project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The project, or <c>null</c> when it does not exist.</returns>
    public Task<Project?> GetAsync(int projectId, CancellationToken cancellationToken = default)
    {
        return _store.GetProjectAsync(projectId, cancellationToken);
    }
}
