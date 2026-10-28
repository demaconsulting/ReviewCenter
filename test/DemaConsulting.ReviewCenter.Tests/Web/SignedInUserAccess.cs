using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Tests.Core;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     The real <see cref="ProjectAccess"/> for page tests, with one convenience: the user that the test signed in
///     as is created in the user store the first time access is asked for, if the test did not create it.
/// </summary>
/// <remarks>
///     Page tests sign in by claims, as the cookie does, but the access rules read the stored user. Without this,
///     every page test would have to create its signed-in user first. A user that a test created itself (an
///     ordinary user, a disabled user, a project administrator) is never changed, so the rules under test are the
///     real ones; only a user that does not exist at all is filled in, with the administrator flag the test chose.
/// </remarks>
/// <param name="inner">The real access rules.</param>
/// <param name="users">The user store the rules read.</param>
/// <param name="current">Gets the signed-in user of the test, or <c>null</c> when nobody is signed in.</param>
internal sealed class SignedInUserAccess(
    ProjectAccess inner,
    IUserStore users,
    Func<(string Username, string DisplayName, bool IsSystemAdmin)?> current) : IProjectAccess
{
    /// <inheritdoc />
    public async Task<bool> IsSystemAdminAsync(Actor? actor, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(actor, cancellationToken);
        return await inner.IsSystemAdminAsync(actor, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> CanViewAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(actor, cancellationToken);
        return await inner.CanViewAsync(actor, projectId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> CanAdministerAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(actor, cancellationToken);
        return await inner.CanAdministerAsync(actor, projectId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsMemberAsync(int userId, int projectId, CancellationToken cancellationToken = default)
    {
        return inner.IsMemberAsync(userId, projectId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Project>> ListVisibleProjectsAsync(
        Actor? actor,
        CancellationToken cancellationToken = default)
    {
        await EnsureAsync(actor, cancellationToken);
        return await inner.ListVisibleProjectsAsync(actor, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RequireViewAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(actor, cancellationToken);
        await inner.RequireViewAsync(actor, projectId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RequireAdministerAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(actor, cancellationToken);
        await inner.RequireAdministerAsync(actor, projectId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RequireSystemAdminAsync(Actor? actor, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(actor, cancellationToken);
        await inner.RequireSystemAdminAsync(actor, cancellationToken);
    }

    /// <summary>
    ///     Creates the signed-in user when the actor is that user and the store does not know it yet.
    /// </summary>
    /// <param name="actor">The actor of the call.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    private async Task EnsureAsync(Actor? actor, CancellationToken cancellationToken)
    {
        var signedIn = current();
        if (actor is null || signedIn is null || actor.Username != signedIn.Value.Username)
        {
            return;
        }

        if (await users.GetByUsernameAsync(actor.Username, cancellationToken) is not null)
        {
            return;
        }

        await users.AddAsync(
            new User
            {
                Username = signedIn.Value.Username,
                DisplayName = signedIn.Value.DisplayName,
                IsSystemAdmin = signedIn.Value.IsSystemAdmin,
                Enabled = true,
                PasswordHash = "hash:test",
                Created = new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero)
            },
            cancellationToken);
    }
}
