using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     An <see cref="IProjectAccess"/> for tests whose subject is not access control: every actor, even a missing
///     one, is a system administrator, but a project that does not exist is still not found.
/// </summary>
/// <remarks>
///     The tests of the access rules themselves (<c>ProjectAccessTests</c>, the endpoint and page matrices) use the
///     real <see cref="ProjectAccess"/> and never this double. Membership (<see cref="IsMemberAsync"/>) is real: it
///     reads the memberships of the store, so slot and signing rules can still be tested.
/// </remarks>
/// <param name="store">The store that knows the projects and memberships.</param>
internal sealed class AllowAllProjectAccess(IReviewStore store) : IProjectAccess
{
    /// <inheritdoc />
    public Task<bool> IsSystemAdminAsync(Actor? actor, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<bool> CanViewAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
    {
        return await store.GetProjectAsync(projectId, cancellationToken) is not null;
    }

    /// <inheritdoc />
    public Task<bool> CanAdministerAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
    {
        return CanViewAsync(actor, projectId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsMemberAsync(int userId, int projectId, CancellationToken cancellationToken = default)
    {
        return await store.GetMembershipAsync(userId, projectId, cancellationToken) is not null;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Project>> ListVisibleProjectsAsync(
        Actor? actor,
        CancellationToken cancellationToken = default)
    {
        return store.ListProjectsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RequireViewAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync(actor, projectId, cancellationToken))
        {
            throw new DomainRuleException(DomainRule.ProjectNotFound, "The project does not exist.");
        }
    }

    /// <inheritdoc />
    public Task RequireAdministerAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
    {
        return RequireViewAsync(actor, projectId, cancellationToken);
    }

    /// <inheritdoc />
    public Task RequireSystemAdminAsync(Actor? actor, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
